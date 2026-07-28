using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using UnityEngine;

namespace MiniCivilization.World.Meshing
{
    internal static class WaterChunkMeshBuilder
    {
        private const float Shoulder = 0.2f;

        public static MeshBuffers Build(
            WorldData world,
            int patchX,
            int patchZ,
            int patchSize,
            WorldSurfaceCatalog catalog,
            WorldSurfaceTopology topology,
            WorldExposureCache exposureCache,
            MeshBuffers buffers,
            List<ExposedCell> cells)
        {
            buffers.Clear();
            var startX = patchX * patchSize;
            var startZ = patchZ * patchSize;
            var endX = Math.Min(startX + patchSize, world.Size);
            var endZ = Math.Min(startZ + patchSize, world.Size);
            exposureCache.CopyWaterCellsForPatch(
                startX,
                startZ,
                endX,
                endZ,
                cells);

            for (var index = 0; index < cells.Count; index++)
            {
                var coordinate = cells[index].Coordinate;
                var x = coordinate.X;
                var y = coordinate.Y;
                var z = coordinate.Z;
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater
                    || !topology.TryResolveWater(x, y, z, out var profile))
                {
                    continue;
                }

                var ownerCellIndex = WorldCellIndex.Encode(world, x, y, z);
                buffers.CurrentTriangleMetadata = new SurfaceTriangleMetadata(
                    ownerCellIndex,
                    true);
                if (profile.TopExposed)
                {
                    AddTop(
                        world,
                        catalog,
                        buffers,
                        x,
                        y,
                        z,
                        startX,
                        startZ,
                        profile);
                }

                AddSide(world, catalog, topology, buffers, x, y, z, startX, startZ, profile, -1, 0);
                AddSide(world, catalog, topology, buffers, x, y, z, startX, startZ, profile, 1, 0);
                AddSide(world, catalog, topology, buffers, x, y, z, startX, startZ, profile, 0, -1);
                AddSide(world, catalog, topology, buffers, x, y, z, startX, startZ, profile, 0, 1);
                AddBoundaryCornerClosure(world, catalog, topology, buffers, x, y, z, startX, startZ, profile, -1, -1);
                AddBoundaryCornerClosure(world, catalog, topology, buffers, x, y, z, startX, startZ, profile, 1, -1);
                AddBoundaryCornerClosure(world, catalog, topology, buffers, x, y, z, startX, startZ, profile, -1, 1);
                AddBoundaryCornerClosure(world, catalog, topology, buffers, x, y, z, startX, startZ, profile, 1, 1);

                if (topology.IsWaterBottomExposed(
                        x,
                        y,
                        z,
                        cell,
                        profile))
                {
                    AddBottom(
                        world,
                        catalog,
                        buffers,
                        x,
                        y,
                        z,
                        startX,
                        startZ,
                        profile.Interval.BottomUnits);
                }
            }

            return buffers;
        }

        private static void AddTop(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in WaterSurfaceProfile profile)
        {
            if (IsFlat(profile))
            {
                AddTopPatch(
                    world, catalog, buffers,
                    x, y, z, startX, startZ, profile,
                    0f, 0f, 1f, 1f);
                return;
            }

            for (var patchZ = 0; patchZ < 3; patchZ++)
            for (var patchX = 0; patchX < 3; patchX++)
            {
                AddTopPatch(
                    world,
                    catalog,
                    buffers,
                    x,
                    y,
                    z,
                    startX,
                    startZ,
                    profile,
                    GetPatchCoordinate(patchX),
                    GetPatchCoordinate(patchZ),
                    GetPatchCoordinate(patchX + 1),
                    GetPatchCoordinate(patchZ + 1));
            }
        }

        private static bool IsFlat(in WaterSurfaceProfile profile)
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

        private static void AddTopPatch(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in WaterSurfaceProfile profile,
            float minX,
            float minZ,
            float maxX,
            float maxZ)
        {
            var a = CreateTopVertex(world, catalog, x, y, z, startX, startZ, minX, minZ, ResolveTopHeight(profile, minX, minZ));
            var b = CreateTopVertex(world, catalog, x, y, z, startX, startZ, minX, maxZ, ResolveTopHeight(profile, minX, maxZ));
            var c = CreateTopVertex(world, catalog, x, y, z, startX, startZ, maxX, maxZ, ResolveTopHeight(profile, maxX, maxZ));
            var d = CreateTopVertex(world, catalog, x, y, z, startX, startZ, maxX, minZ, ResolveTopHeight(profile, maxX, minZ));
            buffers.AddTriangleFacing(a, b, c, Vector3.up);
            buffers.AddTriangleFacing(a, c, d, Vector3.up);
        }

        private static float ResolveTopHeight(
            in WaterSurfaceProfile profile,
            float localX,
            float localZ)
        {
            if (localX <= 0f)
            {
                return profile.NegativeXBoundary.GetHeight(localZ);
            }

            if (localX >= 1f)
            {
                return profile.PositiveXBoundary.GetHeight(localZ);
            }

            if (localZ <= 0f)
            {
                return profile.NegativeZBoundary.GetHeight(localX);
            }

            if (localZ >= 1f)
            {
                return profile.PositiveZBoundary.GetHeight(localX);
            }

            return Mathf.Lerp(
                profile.NegativeXBoundary.GetHeight(localZ),
                profile.PositiveXBoundary.GetHeight(localZ),
                localX);
        }

        private static float GetPatchCoordinate(int index) => index switch
        {
            0 => 0f,
            1 => Shoulder,
            2 => 1f - Shoulder,
            _ => 1f
        };

        private static void AddSide(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WorldSurfaceTopology topology,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in WaterSurfaceProfile profile,
            int directionX,
            int directionZ)
        {
            var resolvedProfile = profile;
            AddSegment(0f, Shoulder);
            AddSegment(Shoulder, 1f - Shoulder);
            AddSegment(1f - Shoulder, 1f);

            void AddSegment(float t0, float t1)
            {
                var top0 = resolvedProfile.GetVerticalBoundaryHeight(
                    directionX,
                    directionZ,
                    t0);
                var top1 = resolvedProfile.GetVerticalBoundaryHeight(
                    directionX,
                    directionZ,
                    t1);
                var bottom0 = Math.Max(
                    resolvedProfile.Interval.BottomUnits,
                    topology.ResolveWaterNeighborCoverage(
                        x, y, z, directionX, directionZ, t0));
                var bottom1 = Math.Max(
                    resolvedProfile.Interval.BottomUnits,
                    topology.ResolveWaterNeighborCoverage(
                        x, y, z, directionX, directionZ, t1));
                bottom0 = Math.Min(bottom0, top0);
                bottom1 = Math.Min(bottom1, top1);
                if (top0 <= bottom0 && top1 <= bottom1)
                {
                    return;
                }

                GetBoundaryCoordinates(
                    directionX,
                    directionZ,
                    t0,
                    out var u0,
                    out var v0);
                GetBoundaryCoordinates(
                    directionX,
                    directionZ,
                    t1,
                    out var u1,
                    out var v1);
                var topVertex0 = CreateSideVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u0, v0, top0);
                var topVertex1 = CreateSideVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u1, v1, top1);
                var bottomVertex0 = CreateSideVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u0, v0, bottom0);
                var bottomVertex1 = CreateSideVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u1, v1, bottom1);
                var outward = new Vector3(directionX, 0f, directionZ);
                buffers.AddTriangleFacing(bottomVertex0, topVertex0, topVertex1, outward);
                buffers.AddTriangleFacing(bottomVertex0, topVertex1, bottomVertex1, outward);
            }
        }

        private static void AddBoundaryCornerClosure(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WorldSurfaceTopology topology,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in WaterSurfaceProfile profile,
            int directionX,
            int directionZ)
        {
            var xBottomBoundary = ResolveCoverageBoundary(
                topology, x, y, z, directionX, 0);
            var zBottomBoundary = ResolveCoverageBoundary(
                topology, x, y, z, 0, directionZ);
            var cellBottom = profile.Interval.BottomUnits;
            var cellCeiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            if (!SurfaceBoundaryClosure.TryResolve(
                    xBottomBoundary,
                    zBottomBoundary,
                    profile.GetVerticalBoundary(directionX, 0),
                    profile.GetVerticalBoundary(0, directionZ),
                    directionX,
                    directionZ,
                    Shoulder,
                    cellBottom,
                    cellCeiling,
                    out var closure))
            {
                return;
            }

            var cornerX = directionX < 0 ? 0f : 1f;
            var cornerZ = directionZ < 0 ? 0f : 1f;
            var edgeX = cornerX <= 0f ? Shoulder : 1f - Shoulder;
            var edgeZ = cornerZ <= 0f ? Shoulder : 1f - Shoulder;
            var xCorner = CreateTopVertex(world, catalog, x, y, z, startX, startZ, cornerX, cornerZ, closure.XCornerHeightUnits);
            var alongXSide = CreateTopVertex(world, catalog, x, y, z, startX, startZ, cornerX, edgeZ, closure.XShoulderHeightUnits);
            var alongZSide = CreateTopVertex(world, catalog, x, y, z, startX, startZ, edgeX, cornerZ, closure.ZShoulderHeightUnits);
            var zCorner = CreateTopVertex(world, catalog, x, y, z, startX, startZ, cornerX, cornerZ, closure.ZCornerHeightUnits);
            var outward = new Vector3(directionX, 0f, directionZ).normalized;
            buffers.AddTriangleFacing(
                xCorner,
                alongXSide,
                alongZSide,
                outward);
            buffers.AddTriangleFacing(
                xCorner,
                alongZSide,
                zCorner,
                outward);
        }

        private static SurfaceBoundaryProfile ResolveCoverageBoundary(
            WorldSurfaceTopology topology,
            int x,
            int y,
            int z,
            int directionX,
            int directionZ) =>
            new SurfaceBoundaryProfile(
                topology.ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, 0f),
                topology.ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, Shoulder),
                topology.ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, 1f - Shoulder),
                topology.ResolveWaterNeighborCoverage(
                    x, y, z, directionX, directionZ, 1f));

        private static void AddBottom(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            int heightUnits)
        {
            var a = CreateTopVertex(world, catalog, x, y, z, startX, startZ, 0f, 0f, heightUnits);
            var b = CreateTopVertex(world, catalog, x, y, z, startX, startZ, 1f, 0f, heightUnits);
            var c = CreateTopVertex(world, catalog, x, y, z, startX, startZ, 1f, 1f, heightUnits);
            var d = CreateTopVertex(world, catalog, x, y, z, startX, startZ, 0f, 1f, heightUnits);
            buffers.AddTriangleFacing(a, b, c, Vector3.down);
            buffers.AddTriangleFacing(a, c, d, Vector3.down);
        }

        private static SurfaceVertex CreateTopVertex(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            float localX,
            float localZ,
            float heightUnits) =>
            new(
                new Vector3(
                    x - startX + localX,
                    heightUnits * WorldGrid.HeightStep,
                    z - startZ + localZ),
                new Vector2(x + localX, z + localZ),
                MaterialBlendResolver.ResolveWaterCell(
                    world,
                    catalog,
                    x,
                    y,
                    z,
                    localX,
                    localZ));

        private static SurfaceVertex CreateSideVertex(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            int directionX,
            int directionZ,
            float localX,
            float localZ,
            float heightUnits)
        {
            var worldX = x + localX;
            var worldZ = z + localZ;
            var horizontalUv = directionX < 0
                ? worldZ
                : directionX > 0
                    ? -worldZ
                    : directionZ < 0
                        ? -worldX
                        : worldX;
            return new SurfaceVertex(
                new Vector3(
                    x - startX + localX,
                    heightUnits * WorldGrid.HeightStep,
                    z - startZ + localZ),
                new Vector2(
                    horizontalUv,
                    heightUnits * WorldGrid.HeightStep),
                MaterialBlendResolver.ResolveWaterCell(
                    world,
                    catalog,
                    x,
                    y,
                    z,
                    localX,
                    localZ));
        }

        private static void GetBoundaryCoordinates(
            int directionX,
            int directionZ,
            float edgePosition,
            out float localX,
            out float localZ)
        {
            if (directionX < 0)
            {
                localX = 0f;
                localZ = edgePosition;
                return;
            }

            if (directionX > 0)
            {
                localX = 1f;
                localZ = edgePosition;
                return;
            }

            localX = edgePosition;
            localZ = directionZ < 0 ? 0f : 1f;
        }
    }
}
