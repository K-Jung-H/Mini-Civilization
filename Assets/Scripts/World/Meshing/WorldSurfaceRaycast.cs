using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using UnityEngine;

namespace MiniCivilization.World.Meshing
{
    internal readonly struct WorldSurfaceRaycastHit
    {
        public readonly SurfaceInteractionType SurfaceType;
        public readonly CellSurfaceFace Face;
        public readonly float Distance;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;

        public WorldSurfaceRaycastHit(
            SurfaceInteractionType surfaceType,
            CellSurfaceFace face,
            float distance,
            Vector3 point,
            Vector3 normal)
        {
            SurfaceType = surfaceType;
            Face = face;
            Distance = distance;
            Point = point;
            Normal = normal;
        }
    }

    internal sealed partial class WorldSurfaceQuery
    {
        private const float RaycastShoulder = 0.2f;
        private const float RaycastCoreMax = 1f - RaycastShoulder;
        private const float RaycastEpsilon = 0.00001f;

        private struct RaycastAccumulator
        {
            public Ray Ray;
            public float MinimumDistance;
            public float BestDistance;
            public SurfaceInteractionType BestType;
            public CellSurfaceFace CurrentFace;
            public CellSurfaceFace BestFace;
            public Vector3 BestNormal;
            public bool HasHit;

            public void TestQuad(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 d,
                Vector3 expectedNormal,
                SurfaceInteractionType type)
            {
                TestTriangle(a, b, c, expectedNormal, type);
                TestTriangle(a, c, d, expectedNormal, type);
            }

            public void TestTriangle(
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 expectedNormal,
                SurfaceInteractionType type)
            {
                var edge1 = b - a;
                var edge2 = c - a;
                var normal = Vector3.Cross(edge1, edge2);
                if (normal.sqrMagnitude <= RaycastEpsilon * RaycastEpsilon)
                {
                    return;
                }

                if (Vector3.Dot(normal, expectedNormal) < 0f)
                {
                    (b, c) = (c, b);
                    edge1 = b - a;
                    edge2 = c - a;
                    normal = -normal;
                }

                var p = Vector3.Cross(Ray.direction, edge2);
                var determinant = Vector3.Dot(edge1, p);
                if (determinant <= RaycastEpsilon)
                {
                    return;
                }

                var inverse = 1f / determinant;
                var offset = Ray.origin - a;
                var u = Vector3.Dot(offset, p) * inverse;
                if (u < -RaycastEpsilon || u > 1f + RaycastEpsilon)
                {
                    return;
                }

                var q = Vector3.Cross(offset, edge1);
                var v = Vector3.Dot(Ray.direction, q) * inverse;
                if (v < -RaycastEpsilon || u + v > 1f + RaycastEpsilon)
                {
                    return;
                }

                var distance = Vector3.Dot(edge2, q) * inverse;
                if (distance < MinimumDistance - RaycastEpsilon
                    || distance > BestDistance + RaycastEpsilon)
                {
                    return;
                }

                HasHit = true;
                BestDistance = distance;
                BestType = type;
                BestFace = CurrentFace;
                BestNormal = normal.normalized;
            }
        }

        public bool TryRaycastCell(
            Ray localRay,
            CellCoordinate coordinate,
            float minimumDistance,
            float maximumDistance,
            out WorldSurfaceRaycastHit hit)
        {
            hit = default;
            if (!world.TryGetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    out var cell))
            {
                return false;
            }

            if (!cell.HasTerrain && !cell.HasWater)
            {
                return false;
            }

            var accumulator = new RaycastAccumulator
            {
                Ray = localRay,
                MinimumDistance = Math.Max(0f, minimumDistance),
                BestDistance = maximumDistance
            };
            var exposure = CellOccupancyResolver.ResolveExposure(
                world,
                coordinate.X,
                coordinate.Y,
                coordinate.Z);

            if (cell.HasTerrain)
            {
                RaycastSolid(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    cell,
                    exposure,
                    ref accumulator);
            }

            if (cell.HasWater
                && TryResolveWater(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    out var waterProfile))
            {
                RaycastWater(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    cell,
                    waterProfile,
                    ref accumulator);
            }

            if (!accumulator.HasHit)
            {
                return false;
            }

            hit = new WorldSurfaceRaycastHit(
                accumulator.BestType,
                accumulator.BestFace,
                accumulator.BestDistance,
                localRay.GetPoint(accumulator.BestDistance),
                accumulator.BestNormal);
            return true;
        }

        private void RaycastSolid(
            int x,
            int y,
            int z,
            in CellData cell,
            CellExposureFlags exposure,
            ref RaycastAccumulator accumulator)
        {
            SolidSurfaceProfile topProfile = default;
            if ((exposure & CellExposureFlags.SolidTop) != 0)
            {
                topProfile = ResolveSolid(x, y, z);
                accumulator.CurrentFace = CellSurfaceFace.Top;
                RaycastSolidTop(x, z, topProfile, ref accumulator);
            }

            accumulator.CurrentFace = CellSurfaceFace.NegativeX;
            RaycastSolidSide(x, y, z, cell, exposure, topProfile,
                -1, 0, CellExposureFlags.SolidNegativeX, ref accumulator);
            accumulator.CurrentFace = CellSurfaceFace.PositiveX;
            RaycastSolidSide(x, y, z, cell, exposure, topProfile,
                1, 0, CellExposureFlags.SolidPositiveX, ref accumulator);
            accumulator.CurrentFace = CellSurfaceFace.NegativeZ;
            RaycastSolidSide(x, y, z, cell, exposure, topProfile,
                0, -1, CellExposureFlags.SolidNegativeZ, ref accumulator);
            accumulator.CurrentFace = CellSurfaceFace.PositiveZ;
            RaycastSolidSide(x, y, z, cell, exposure, topProfile,
                0, 1, CellExposureFlags.SolidPositiveZ, ref accumulator);

            if ((exposure & CellExposureFlags.SolidTop) != 0)
            {
                accumulator.CurrentFace = CellSurfaceFace.None;
                RaycastSolidCornerClosure(
                    x, y, z, cell, topProfile, -1, -1, ref accumulator);
                RaycastSolidCornerClosure(
                    x, y, z, cell, topProfile, 1, -1, ref accumulator);
                RaycastSolidCornerClosure(
                    x, y, z, cell, topProfile, -1, 1, ref accumulator);
                RaycastSolidCornerClosure(
                    x, y, z, cell, topProfile, 1, 1, ref accumulator);
            }

            if ((exposure & CellExposureFlags.SolidBottom) != 0)
            {
                accumulator.CurrentFace = CellSurfaceFace.Bottom;
                var height = y * WorldGrid.HeightStepsPerCell;
                accumulator.TestQuad(
                    Point(x, z, 0f, 0f, height),
                    Point(x, z, 1f, 0f, height),
                    Point(x, z, 1f, 1f, height),
                    Point(x, z, 0f, 1f, height),
                    Vector3.down,
                    SurfaceInteractionType.Terrain);
            }
        }

        private void RaycastSolidTop(
            int x,
            int z,
            in SolidSurfaceProfile profile,
            ref RaycastAccumulator accumulator)
        {
            var centerHeight = profile.CenterHeightUnits;
            accumulator.TestQuad(
                Point(x, z, RaycastShoulder, RaycastShoulder, centerHeight),
                Point(x, z, RaycastShoulder, RaycastCoreMax, centerHeight),
                Point(x, z, RaycastCoreMax, RaycastCoreMax, centerHeight),
                Point(x, z, RaycastCoreMax, RaycastShoulder, centerHeight),
                Vector3.up,
                SurfaceInteractionType.Terrain);

            RaycastSolidShoulder(x, z, profile, -1, 0, ref accumulator);
            RaycastSolidShoulder(x, z, profile, 1, 0, ref accumulator);
            RaycastSolidShoulder(x, z, profile, 0, -1, ref accumulator);
            RaycastSolidShoulder(x, z, profile, 0, 1, ref accumulator);
            RaycastSolidTopCorner(x, z, profile, 0f, 0f, ref accumulator);
            RaycastSolidTopCorner(x, z, profile, 1f, 0f, ref accumulator);
            RaycastSolidTopCorner(x, z, profile, 0f, 1f, ref accumulator);
            RaycastSolidTopCorner(x, z, profile, 1f, 1f, ref accumulator);
        }

        private void RaycastSolidShoulder(
            int x,
            int z,
            in SolidSurfaceProfile profile,
            int directionX,
            int directionZ,
            ref RaycastAccumulator accumulator)
        {
            var center = profile.CenterHeightUnits;
            var outerStart = profile.GetBoundaryHeight(
                directionX, directionZ, RaycastShoulder);
            var outerEnd = profile.GetBoundaryHeight(
                directionX, directionZ, RaycastCoreMax);
            if (directionX < 0)
            {
                accumulator.TestQuad(
                    Point(x, z, 0f, RaycastShoulder, outerStart),
                    Point(x, z, 0f, RaycastCoreMax, outerEnd),
                    Point(x, z, RaycastShoulder, RaycastCoreMax, center),
                    Point(x, z, RaycastShoulder, RaycastShoulder, center),
                    Vector3.up,
                    SurfaceInteractionType.Terrain);
            }
            else if (directionX > 0)
            {
                accumulator.TestQuad(
                    Point(x, z, RaycastCoreMax, RaycastShoulder, center),
                    Point(x, z, RaycastCoreMax, RaycastCoreMax, center),
                    Point(x, z, 1f, RaycastCoreMax, outerEnd),
                    Point(x, z, 1f, RaycastShoulder, outerStart),
                    Vector3.up,
                    SurfaceInteractionType.Terrain);
            }
            else if (directionZ < 0)
            {
                accumulator.TestQuad(
                    Point(x, z, RaycastShoulder, 0f, outerStart),
                    Point(x, z, RaycastShoulder, RaycastShoulder, center),
                    Point(x, z, RaycastCoreMax, RaycastShoulder, center),
                    Point(x, z, RaycastCoreMax, 0f, outerEnd),
                    Vector3.up,
                    SurfaceInteractionType.Terrain);
            }
            else
            {
                accumulator.TestQuad(
                    Point(x, z, RaycastShoulder, RaycastCoreMax, center),
                    Point(x, z, RaycastShoulder, 1f, outerStart),
                    Point(x, z, RaycastCoreMax, 1f, outerEnd),
                    Point(x, z, RaycastCoreMax, RaycastCoreMax, center),
                    Vector3.up,
                    SurfaceInteractionType.Terrain);
            }
        }

        private void RaycastSolidTopCorner(
            int x,
            int z,
            in SolidSurfaceProfile profile,
            float cornerX,
            float cornerZ,
            ref RaycastAccumulator accumulator)
        {
            var inwardX = cornerX <= 0f ? 1f : -1f;
            var inwardZ = cornerZ <= 0f ? 1f : -1f;
            var shoulderX = cornerX + inwardX * RaycastShoulder;
            var shoulderZ = cornerZ + inwardZ * RaycastShoulder;
            var directionX = -Mathf.RoundToInt(inwardX);
            var directionZ = -Mathf.RoundToInt(inwardZ);
            var topAlongX = Point(
                x, z, shoulderX, cornerZ,
                profile.GetBoundaryHeight(0, directionZ, shoulderX));
            var topAlongZ = Point(
                x, z, cornerX, shoulderZ,
                profile.GetBoundaryHeight(directionX, 0, shoulderZ));
            var inner = Point(
                x, z, shoulderX, shoulderZ, profile.CenterHeightUnits);
            var corner = Point(
                x, z, cornerX, cornerZ,
                profile.GetCornerHeight(cornerX, cornerZ));

            if (profile.GetCornerHeight(cornerX, cornerZ)
                > profile.CenterHeightUnits)
            {
                accumulator.TestTriangle(
                    corner, topAlongX, topAlongZ,
                    Vector3.up, SurfaceInteractionType.Terrain);
                accumulator.TestTriangle(
                    topAlongX, inner, topAlongZ,
                    Vector3.up, SurfaceInteractionType.Terrain);
            }
            else
            {
                accumulator.TestTriangle(
                    inner, topAlongX, corner,
                    Vector3.up, SurfaceInteractionType.Terrain);
                accumulator.TestTriangle(
                    inner, corner, topAlongZ,
                    Vector3.up, SurfaceInteractionType.Terrain);
            }
        }

        private void RaycastSolidSide(
            int x,
            int y,
            int z,
            in CellData cell,
            CellExposureFlags exposure,
            in SolidSurfaceProfile topProfile,
            int directionX,
            int directionZ,
            CellExposureFlags requiredFlag,
            ref RaycastAccumulator accumulator)
        {
            if ((exposure & requiredFlag) == 0
                || !CellOccupancyResolver.TryGetSolidSideExposure(
                    world, x, y, z, cell, directionX, directionZ,
                    out var interval))
            {
                return;
            }

            var hasTop = (exposure & CellExposureFlags.SolidTop) != 0;
            ResolveSolidRaycastBottomProfile(
                x, z, directionX, directionZ, interval.BottomUnits,
                out var bottom0,
                out var bottom1,
                out var bottom2,
                out var bottom3);
            var top0 = hasTop
                ? topProfile.GetBoundaryHeight(directionX, directionZ, 0f)
                : interval.TopUnits;
            var top1 = hasTop
                ? topProfile.GetBoundaryHeight(
                    directionX, directionZ, RaycastShoulder)
                : interval.TopUnits;
            var top2 = hasTop
                ? topProfile.GetBoundaryHeight(
                    directionX, directionZ, RaycastCoreMax)
                : interval.TopUnits;
            var top3 = hasTop
                ? topProfile.GetBoundaryHeight(directionX, directionZ, 1f)
                : interval.TopUnits;
            var minimum = y * WorldGrid.HeightStepsPerCell;
            var maximum = (y + 1) * WorldGrid.HeightStepsPerCell;
            bottom0 = Mathf.Clamp(bottom0, minimum, maximum);
            bottom1 = Mathf.Clamp(bottom1, minimum, maximum);
            bottom2 = Mathf.Clamp(bottom2, minimum, maximum);
            bottom3 = Mathf.Clamp(bottom3, minimum, maximum);

            RaycastVerticalSegment(x, z, directionX, directionZ,
                0f, RaycastShoulder, top0, top1, bottom0, bottom1,
                SurfaceInteractionType.Terrain, ref accumulator);
            RaycastVerticalSegment(x, z, directionX, directionZ,
                RaycastShoulder, RaycastCoreMax,
                top1, top2, bottom1, bottom2,
                SurfaceInteractionType.Terrain, ref accumulator);
            RaycastVerticalSegment(x, z, directionX, directionZ,
                RaycastCoreMax, 1f, top2, top3, bottom2, bottom3,
                SurfaceInteractionType.Terrain, ref accumulator);
        }

        private void ResolveSolidRaycastBottomProfile(
            int x,
            int z,
            int directionX,
            int directionZ,
            int fallbackHeight,
            out float start,
            out float shoulderStart,
            out float shoulderEnd,
            out float end)
        {
            start = fallbackHeight;
            shoulderStart = fallbackHeight;
            shoulderEnd = fallbackHeight;
            end = fallbackHeight;
            if (!TryResolveSolidAtHeight(
                    x + directionX,
                    z + directionZ,
                    fallbackHeight,
                    out var neighbor))
            {
                return;
            }

            start = neighbor.GetBoundaryHeight(
                -directionX, -directionZ, 0f);
            shoulderStart = neighbor.GetBoundaryHeight(
                -directionX, -directionZ, RaycastShoulder);
            shoulderEnd = neighbor.GetBoundaryHeight(
                -directionX, -directionZ, RaycastCoreMax);
            end = neighbor.GetBoundaryHeight(
                -directionX, -directionZ, 1f);
        }

        private void RaycastSolidCornerClosure(
            int x,
            int y,
            int z,
            in CellData cell,
            in SolidSurfaceProfile topProfile,
            int directionX,
            int directionZ,
            ref RaycastAccumulator accumulator)
        {
            if (!CellOccupancyResolver.TryGetSolidSideExposure(
                    world, x, y, z, cell, directionX, 0,
                    out var xInterval)
                || !CellOccupancyResolver.TryGetSolidSideExposure(
                    world, x, y, z, cell, 0, directionZ,
                    out var zInterval))
            {
                return;
            }

            ResolveSolidRaycastBottomProfile(
                x, z, directionX, 0, xInterval.BottomUnits,
                out var xs, out var xss, out var xse, out var xe);
            ResolveSolidRaycastBottomProfile(
                x, z, 0, directionZ, zInterval.BottomUnits,
                out var zs, out var zss, out var zse, out var ze);
            var xBoundary = new SurfaceBoundaryProfile(xs, xss, xse, xe);
            var zBoundary = new SurfaceBoundaryProfile(zs, zss, zse, ze);
            if (!SurfaceBoundaryClosure.TryResolve(
                    xBoundary,
                    zBoundary,
                    topProfile.GetBoundary(directionX, 0),
                    topProfile.GetBoundary(0, directionZ),
                    directionX,
                    directionZ,
                    RaycastShoulder,
                    y * WorldGrid.HeightStepsPerCell,
                    (y + 1) * WorldGrid.HeightStepsPerCell,
                    out var closure))
            {
                return;
            }

            RaycastCornerClosure(
                x, z, directionX, directionZ, closure,
                SurfaceInteractionType.Terrain, ref accumulator);
        }

        private void RaycastWater(
            int x,
            int y,
            int z,
            in CellData cell,
            in WaterSurfaceProfile profile,
            ref RaycastAccumulator accumulator)
        {
            if (profile.TopExposed)
            {
                accumulator.CurrentFace = CellSurfaceFace.Top;
                RaycastWaterTop(x, z, profile, ref accumulator);
            }

            accumulator.CurrentFace = CellSurfaceFace.NegativeX;
            RaycastWaterSide(x, y, z, profile, -1, 0, ref accumulator);
            accumulator.CurrentFace = CellSurfaceFace.PositiveX;
            RaycastWaterSide(x, y, z, profile, 1, 0, ref accumulator);
            accumulator.CurrentFace = CellSurfaceFace.NegativeZ;
            RaycastWaterSide(x, y, z, profile, 0, -1, ref accumulator);
            accumulator.CurrentFace = CellSurfaceFace.PositiveZ;
            RaycastWaterSide(x, y, z, profile, 0, 1, ref accumulator);
            accumulator.CurrentFace = CellSurfaceFace.None;
            RaycastWaterCornerClosure(
                x, y, z, profile, -1, -1, ref accumulator);
            RaycastWaterCornerClosure(
                x, y, z, profile, 1, -1, ref accumulator);
            RaycastWaterCornerClosure(
                x, y, z, profile, -1, 1, ref accumulator);
            RaycastWaterCornerClosure(
                x, y, z, profile, 1, 1, ref accumulator);

            if (IsWaterBottomExposed(x, y, z, cell, profile))
            {
                accumulator.CurrentFace = CellSurfaceFace.Bottom;
                var height = profile.Interval.BottomUnits;
                accumulator.TestQuad(
                    Point(x, z, 0f, 0f, height),
                    Point(x, z, 1f, 0f, height),
                    Point(x, z, 1f, 1f, height),
                    Point(x, z, 0f, 1f, height),
                    Vector3.down,
                    SurfaceInteractionType.Water);
            }
        }

        private void RaycastWaterTop(
            int x,
            int z,
            in WaterSurfaceProfile profile,
            ref RaycastAccumulator accumulator)
        {
            if (IsWaterProfileFlat(profile))
            {
                RaycastWaterTopPatch(
                    x, z, profile, 0f, 0f, 1f, 1f, ref accumulator);
                return;
            }

            for (var patchZ = 0; patchZ < 3; patchZ++)
            for (var patchX = 0; patchX < 3; patchX++)
            {
                RaycastWaterTopPatch(
                    x,
                    z,
                    profile,
                    PatchCoordinate(patchX),
                    PatchCoordinate(patchZ),
                    PatchCoordinate(patchX + 1),
                    PatchCoordinate(patchZ + 1),
                    ref accumulator);
            }
        }

        private void RaycastWaterTopPatch(
            int x,
            int z,
            in WaterSurfaceProfile profile,
            float minimumX,
            float minimumZ,
            float maximumX,
            float maximumZ,
            ref RaycastAccumulator accumulator)
        {
            accumulator.TestQuad(
                Point(x, z, minimumX, minimumZ,
                    ResolveWaterTopHeight(profile, minimumX, minimumZ)),
                Point(x, z, minimumX, maximumZ,
                    ResolveWaterTopHeight(profile, minimumX, maximumZ)),
                Point(x, z, maximumX, maximumZ,
                    ResolveWaterTopHeight(profile, maximumX, maximumZ)),
                Point(x, z, maximumX, minimumZ,
                    ResolveWaterTopHeight(profile, maximumX, minimumZ)),
                Vector3.up,
                SurfaceInteractionType.Water);
        }

        private void RaycastWaterSide(
            int x,
            int y,
            int z,
            in WaterSurfaceProfile profile,
            int directionX,
            int directionZ,
            ref RaycastAccumulator accumulator)
        {
            RaycastWaterSideSegment(
                x, y, z, profile, directionX, directionZ,
                0f, RaycastShoulder, ref accumulator);
            RaycastWaterSideSegment(
                x, y, z, profile, directionX, directionZ,
                RaycastShoulder, RaycastCoreMax, ref accumulator);
            RaycastWaterSideSegment(
                x, y, z, profile, directionX, directionZ,
                RaycastCoreMax, 1f, ref accumulator);
        }

        private void RaycastWaterSideSegment(
            int x,
            int y,
            int z,
            in WaterSurfaceProfile profile,
            int directionX,
            int directionZ,
            float start,
            float end,
            ref RaycastAccumulator accumulator)
        {
            var top0 = profile.GetVerticalBoundaryHeight(
                directionX, directionZ, start);
            var top1 = profile.GetVerticalBoundaryHeight(
                directionX, directionZ, end);
            var bottom0 = Math.Max(
                profile.Interval.BottomUnits,
                ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, start));
            var bottom1 = Math.Max(
                profile.Interval.BottomUnits,
                ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, end));
            RaycastVerticalSegment(
                x, z, directionX, directionZ,
                start, end, top0, top1, bottom0, bottom1,
                SurfaceInteractionType.Water, ref accumulator);
        }

        private void RaycastWaterCornerClosure(
            int x,
            int y,
            int z,
            in WaterSurfaceProfile profile,
            int directionX,
            int directionZ,
            ref RaycastAccumulator accumulator)
        {
            var xBottom = ResolveWaterCoverageBoundary(
                x, y, z, directionX, 0);
            var zBottom = ResolveWaterCoverageBoundary(
                x, y, z, 0, directionZ);
            if (!SurfaceBoundaryClosure.TryResolve(
                    xBottom,
                    zBottom,
                    profile.GetVerticalBoundary(directionX, 0),
                    profile.GetVerticalBoundary(0, directionZ),
                    directionX,
                    directionZ,
                    RaycastShoulder,
                    profile.Interval.BottomUnits,
                    (y + 1) * WorldGrid.HeightStepsPerCell,
                    out var closure))
            {
                return;
            }

            RaycastCornerClosure(
                x, z, directionX, directionZ, closure,
                SurfaceInteractionType.Water, ref accumulator);
        }

        private SurfaceBoundaryProfile ResolveWaterCoverageBoundary(
            int x,
            int y,
            int z,
            int directionX,
            int directionZ) =>
            new(
                ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, 0f),
                ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, RaycastShoulder),
                ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, RaycastCoreMax),
                ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, 1f));

        private void RaycastVerticalSegment(
            int x,
            int z,
            int directionX,
            int directionZ,
            float t0,
            float t1,
            float top0,
            float top1,
            float bottom0,
            float bottom1,
            SurfaceInteractionType type,
            ref RaycastAccumulator accumulator)
        {
            bottom0 = Math.Min(bottom0, top0);
            bottom1 = Math.Min(bottom1, top1);
            if (top0 <= bottom0 && top1 <= bottom1)
            {
                return;
            }

            GetRaycastBoundaryCoordinates(
                directionX, directionZ, t0, out var u0, out var v0);
            GetRaycastBoundaryCoordinates(
                directionX, directionZ, t1, out var u1, out var v1);
            var outward = new Vector3(directionX, 0f, directionZ);
            accumulator.TestQuad(
                Point(x, z, u0, v0, bottom0),
                Point(x, z, u0, v0, top0),
                Point(x, z, u1, v1, top1),
                Point(x, z, u1, v1, bottom1),
                outward,
                type);
        }

        private void RaycastCornerClosure(
            int x,
            int z,
            int directionX,
            int directionZ,
            in SurfaceCornerClosureProfile closure,
            SurfaceInteractionType type,
            ref RaycastAccumulator accumulator)
        {
            var cornerX = directionX < 0 ? 0f : 1f;
            var cornerZ = directionZ < 0 ? 0f : 1f;
            var shoulderX = directionX < 0
                ? RaycastShoulder
                : RaycastCoreMax;
            var shoulderZ = directionZ < 0
                ? RaycastShoulder
                : RaycastCoreMax;
            var xCorner = Point(
                x, z, cornerX, cornerZ, closure.XCornerHeightUnits);
            var alongX = Point(
                x, z, cornerX, shoulderZ,
                closure.XShoulderHeightUnits);
            var alongZ = Point(
                x, z, shoulderX, cornerZ,
                closure.ZShoulderHeightUnits);
            var zCorner = Point(
                x, z, cornerX, cornerZ, closure.ZCornerHeightUnits);
            var outward = new Vector3(directionX, 0f, directionZ).normalized;
            accumulator.TestTriangle(
                xCorner, alongX, alongZ, outward, type);
            accumulator.TestTriangle(
                xCorner, alongZ, zCorner, outward, type);
        }

        private static bool IsWaterProfileFlat(
            in WaterSurfaceProfile profile)
        {
            var height = profile.NegativeXBoundary.StartHeightUnits;
            return IsFlat(profile.NegativeXBoundary)
                && IsFlat(profile.PositiveXBoundary)
                && IsFlat(profile.NegativeZBoundary)
                && IsFlat(profile.PositiveZBoundary);

            bool IsFlat(in SurfaceBoundaryProfile boundary) =>
                boundary.StartHeightUnits == height
                && boundary.ShoulderStartHeightUnits == height
                && boundary.ShoulderEndHeightUnits == height
                && boundary.EndHeightUnits == height;
        }

        private static float ResolveWaterTopHeight(
            in WaterSurfaceProfile profile,
            float localX,
            float localZ)
        {
            if (localX <= 0f)
                return profile.NegativeXBoundary.GetHeight(localZ);
            if (localX >= 1f)
                return profile.PositiveXBoundary.GetHeight(localZ);
            if (localZ <= 0f)
                return profile.NegativeZBoundary.GetHeight(localX);
            if (localZ >= 1f)
                return profile.PositiveZBoundary.GetHeight(localX);
            return Mathf.Lerp(
                profile.NegativeXBoundary.GetHeight(localZ),
                profile.PositiveXBoundary.GetHeight(localZ),
                localX);
        }

        private static float PatchCoordinate(int index) => index switch
        {
            0 => 0f,
            1 => RaycastShoulder,
            2 => RaycastCoreMax,
            _ => 1f
        };

        private static void GetRaycastBoundaryCoordinates(
            int directionX,
            int directionZ,
            float position,
            out float localX,
            out float localZ)
        {
            if (directionX < 0)
            {
                localX = 0f;
                localZ = position;
            }
            else if (directionX > 0)
            {
                localX = 1f;
                localZ = position;
            }
            else
            {
                localX = position;
                localZ = directionZ < 0 ? 0f : 1f;
            }
        }

        private Vector3 Point(
            int x,
            int z,
            float localX,
            float localZ,
            float heightUnits) =>
            new(
                (x + localX) * world.CellSize,
                heightUnits * world.HeightStep,
                (z + localZ) * world.CellSize);
    }
}
