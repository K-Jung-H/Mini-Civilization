using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.WaterFlow;
using UnityEngine;

namespace MiniCivilization.World.Meshing
{
    internal readonly struct WaterCellMeshProfile
    {
        public readonly WaterFlowMode Mode;
        public readonly HeightInterval Interval;
        public readonly int NegativeXNegativeZ;
        public readonly int NegativeXPositiveZ;
        public readonly int PositiveXPositiveZ;
        public readonly int PositiveXNegativeZ;
        public readonly bool TopExposed;
        public readonly bool ConnectsFromAbove;

        public WaterCellMeshProfile(
            WaterFlowMode mode,
            HeightInterval interval,
            int negativeXNegativeZ,
            int negativeXPositiveZ,
            int positiveXPositiveZ,
            int positiveXNegativeZ,
            bool topExposed,
            bool connectsFromAbove)
        {
            Mode = mode;
            Interval = interval;
            NegativeXNegativeZ = negativeXNegativeZ;
            NegativeXPositiveZ = negativeXPositiveZ;
            PositiveXPositiveZ = positiveXPositiveZ;
            PositiveXNegativeZ = positiveXNegativeZ;
            TopExposed = topExposed;
            ConnectsFromAbove = connectsFromAbove;
        }

        public int GetCorner(float localX, float localZ)
        {
            if (localX <= 0f)
            {
                return localZ <= 0f
                    ? NegativeXNegativeZ
                    : NegativeXPositiveZ;
            }

            return localZ <= 0f
                ? PositiveXNegativeZ
                : PositiveXPositiveZ;
        }

        public float GetTopHeight(float localX, float localZ)
        {
            var negativeZ = Mathf.Lerp(
                NegativeXNegativeZ,
                PositiveXNegativeZ,
                localX);
            var positiveZ = Mathf.Lerp(
                NegativeXPositiveZ,
                PositiveXPositiveZ,
                localX);
            return Mathf.Lerp(negativeZ, positiveZ, localZ);
        }
    }

    /// <summary>
    /// Resolves one canonical profile for every logical Water Cell. Adjacent
    /// cells sample the same world-grid corner, so their shared vertices are
    /// identical and do not require aprons or connector polygons.
    /// </summary>
    internal static class WaterCellMeshProfileResolver
    {
        public static bool TryResolveHorizontalBoundary(
            WorldData world,
            int x,
            int y,
            int z,
            int directionXFromCurrent,
            int directionZFromCurrent,
            out CellSurfaceBoundary boundary)
        {
            boundary = default;
            if (!TryResolve(world, null, x, y, z, out var profile)
                || !profile.TopExposed
                || profile.Mode == WaterFlowMode.Falling)
            {
                return false;
            }

            boundary = new CellSurfaceBoundary(
                GetHeight(0f),
                GetHeight(0.2f),
                GetHeight(0.8f),
                GetHeight(1f));
            return true;

            float GetHeight(float edgePosition)
            {
                GetNeighborBoundaryCoordinates(
                    directionXFromCurrent,
                    directionZFromCurrent,
                    edgePosition,
                    out var localX,
                    out var localZ);
                return profile.GetTopHeight(localX, localZ);
            }
        }

        public static bool TryResolve(
            WorldData world,
            WaterFlowState flowState,
            int x,
            int y,
            int z,
            out WaterCellMeshProfile profile)
        {
            if (!world.TryGetCell(x, y, z, out var cell) || !cell.HasWater)
            {
                profile = default;
                return false;
            }

            var mode = ResolveMode(world, flowState, x, y, z, cell);
            var interval = ResolveInterval(
                world,
                flowState,
                x,
                y,
                z,
                cell,
                mode);
            var negativeXNegativeZ = ResolveCornerHeight(
                world, flowState, x, y, z);
            var negativeXPositiveZ = ResolveCornerHeight(
                world, flowState, x, y, z + 1);
            var positiveXPositiveZ = ResolveCornerHeight(
                world, flowState, x + 1, y, z + 1);
            var positiveXNegativeZ = ResolveCornerHeight(
                world, flowState, x + 1, y, z);
            var topExposed = IsTopExposed(
                world,
                flowState,
                x,
                y,
                z,
                interval);
            var connectsFromAbove = IsFallingWater(
                world,
                flowState,
                x,
                y + 1,
                z);

            profile = new WaterCellMeshProfile(
                mode,
                interval,
                negativeXNegativeZ,
                negativeXPositiveZ,
                positiveXPositiveZ,
                positiveXNegativeZ,
                topExposed,
                connectsFromAbove);
            return true;
        }

        public static WaterFlowMode ResolveMode(
            WorldData world,
            WaterFlowState flowState,
            int x,
            int y,
            int z,
            in CellData cell)
        {
            if (!cell.HasWater)
            {
                return WaterFlowMode.None;
            }

            if (flowState != null)
            {
                var resolved = flowState.GetFlowMode(x, y, z);
                if (resolved != WaterFlowMode.None)
                {
                    return resolved;
                }
            }

            if ((cell.Flags & CellFlags.FallingWater) != 0)
            {
                return WaterFlowMode.Falling;
            }

            if (world.TryGetCell(x, y + 1, z, out var above)
                && above.HasWater
                && (above.Flags & CellFlags.FallingWater) != 0)
            {
                return WaterFlowMode.Flowing;
            }

            return WaterFlowMode.Surface;
        }

        public static HeightInterval ResolveInterval(
            WorldData world,
            WaterFlowState flowState,
            int x,
            int y,
            int z,
            in CellData cell,
            WaterFlowMode mode)
        {
            var logical = CellOccupancyResolver.GetWaterInterval(y, cell);
            var cellBottom = y * WorldGrid.HeightStepsPerCell;
            var ceiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            var fallingAbove = IsFallingWater(
                world,
                flowState,
                x,
                y + 1,
                z);

            if (mode == WaterFlowMode.Falling)
            {
                return new HeightInterval(
                    cellBottom,
                    fallingAbove ? ceiling : logical.TopUnits);
            }

            if (fallingAbove)
            {
                // The landing Cell owns the last part of the falling column.
                // It therefore reaches the Cell ceiling without a second mesh.
                return new HeightInterval(logical.BottomUnits, ceiling);
            }

            return logical;
        }

        public static bool IsBottomExposed(
            WorldData world,
            WaterFlowState flowState,
            int x,
            int y,
            int z,
            in CellData cell,
            in WaterCellMeshProfile profile)
        {
            if (cell.HasSolid || y <= 0)
            {
                return false;
            }

            if (!world.TryGetCell(x, y - 1, z, out var below))
            {
                return true;
            }

            var coveredTop = (y - 1) * WorldGrid.HeightStepsPerCell
                + below.SolidFill;
            if (below.HasWater
                && TryResolve(
                    world,
                    flowState,
                    x,
                    y - 1,
                    z,
                    out var belowProfile))
            {
                coveredTop = Math.Max(
                    coveredTop,
                    belowProfile.Interval.TopUnits);
            }

            return coveredTop < profile.Interval.BottomUnits;
        }

        public static float ResolveNeighborCoverage(
            WorldData world,
            WaterFlowState flowState,
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            float edgePosition)
        {
            var cellBottom = y * WorldGrid.HeightStepsPerCell;
            var neighborX = x + directionX;
            var neighborZ = z + directionZ;
            if (!world.TryGetCell(
                    neighborX,
                    y,
                    neighborZ,
                    out var neighbor))
            {
                return cellBottom;
            }

            var coveredTop = (float)cellBottom;
            if (neighbor.HasSolid)
            {
                coveredTop = cellBottom + neighbor.SolidFill;
                if (CellOccupancyResolver.IsSolidTopExposed(
                        world,
                        neighborX,
                        y,
                        neighborZ,
                        neighbor))
                {
                    var solidProfile = CellSurfaceShapeResolver.Resolve(
                        world,
                        neighborX,
                        y,
                        neighborZ);
                    coveredTop = ResolveSolidBoundaryHeight(
                        solidProfile,
                        -directionX,
                        -directionZ,
                        edgePosition);
                }
            }

            if (!neighbor.HasWater
                || !TryResolve(
                    world,
                    flowState,
                    neighborX,
                    y,
                    neighborZ,
                    out var neighborProfile))
            {
                return coveredTop;
            }

            GetNeighborBoundaryCoordinates(
                directionX,
                directionZ,
                edgePosition,
                out var localX,
                out var localZ);
            return Math.Max(
                coveredTop,
                ResolveWaterBoundaryHeight(
                    neighborProfile,
                    localX,
                    localZ,
                    directionX,
                    directionZ,
                    edgePosition));
        }

        private static float ResolveSolidBoundaryHeight(
            in CellSurfaceProfile profile,
            int directionX,
            int directionZ,
            float edgePosition) =>
            profile.GetBoundaryHeight(
                directionX,
                directionZ,
                edgePosition);

        private static float ResolveWaterBoundaryHeight(
            in WaterCellMeshProfile profile,
            float localX,
            float localZ,
            int directionX,
            int directionZ,
            float edgePosition)
        {
            if (directionX != 0)
            {
                return Mathf.Lerp(
                    profile.GetCorner(localX, 0f),
                    profile.GetCorner(localX, 1f),
                    edgePosition);
            }

            return Mathf.Lerp(
                profile.GetCorner(0f, localZ),
                profile.GetCorner(1f, localZ),
                edgePosition);
        }

        private static bool IsTopExposed(
            WorldData world,
            WaterFlowState flowState,
            int x,
            int y,
            int z,
            in HeightInterval interval)
        {
            var ceiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            if (interval.TopUnits < ceiling)
            {
                return true;
            }

            if (!world.TryGetCell(x, y + 1, z, out var above))
            {
                return true;
            }

            if (above.HasSolid)
            {
                return false;
            }

            if (!above.HasWater
                || !TryResolve(
                    world,
                    flowState,
                    x,
                    y + 1,
                    z,
                    out var aboveProfile))
            {
                return true;
            }

            return aboveProfile.Interval.BottomUnits > interval.TopUnits;
        }

        private static int ResolveCornerHeight(
            WorldData world,
            WaterFlowState flowState,
            int cornerX,
            int y,
            int cornerZ)
        {
            var sum = 0;
            var count = 0;
            var maximumBottom = y * WorldGrid.HeightStepsPerCell;
            var ceiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            var requiresCeiling = false;

            Sample(cornerX - 1, cornerZ - 1);
            Sample(cornerX, cornerZ - 1);
            Sample(cornerX - 1, cornerZ);
            Sample(cornerX, cornerZ);

            if (count == 0)
            {
                return maximumBottom;
            }

            if (requiresCeiling)
            {
                return ceiling;
            }

            return Math.Clamp(
                Mathf.RoundToInt((float)sum / count),
                maximumBottom,
                ceiling);

            void Sample(int sampleX, int sampleZ)
            {
                if (!world.TryGetCell(sampleX, y, sampleZ, out var sample)
                    || !sample.HasWater)
                {
                    return;
                }

                var mode = ResolveMode(
                    world,
                    flowState,
                    sampleX,
                    y,
                    sampleZ,
                    sample);
                var interval = ResolveInterval(
                    world,
                    flowState,
                    sampleX,
                    y,
                    sampleZ,
                    sample,
                    mode);
                maximumBottom = Math.Max(maximumBottom, interval.BottomUnits);
                sum += interval.TopUnits;
                count++;
                requiresCeiling |= interval.TopUnits >= ceiling
                    && (mode == WaterFlowMode.Falling
                        || IsFallingWater(
                            world,
                            flowState,
                            sampleX,
                            y + 1,
                            sampleZ));
            }
        }

        private static bool IsFallingWater(
            WorldData world,
            WaterFlowState flowState,
            int x,
            int y,
            int z)
        {
            return world.TryGetCell(x, y, z, out var cell)
                && cell.HasWater
                && ResolveMode(world, flowState, x, y, z, cell)
                    == WaterFlowMode.Falling;
        }

        private static void GetNeighborBoundaryCoordinates(
            int directionX,
            int directionZ,
            float edgePosition,
            out float localX,
            out float localZ)
        {
            if (directionX < 0)
            {
                localX = 1f;
                localZ = edgePosition;
                return;
            }

            if (directionX > 0)
            {
                localX = 0f;
                localZ = edgePosition;
                return;
            }

            localX = edgePosition;
            localZ = directionZ < 0 ? 1f : 0f;
        }
    }

    internal static class WaterChunkMeshBuilder
    {
        private const float Shoulder = 0.2f;

        public static MeshBuffers Build(
            WorldData world,
            int patchX,
            int patchZ,
            int patchSize,
            WorldSurfaceCatalog catalog,
            WaterFlowState flowState,
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
                    || !WaterCellMeshProfileResolver.TryResolve(
                        world,
                        flowState,
                        x,
                        y,
                        z,
                        out var profile))
                {
                    continue;
                }

                var ownerCellIndex = WorldCellIndex.Encode(world, x, y, z);
                if (profile.TopExposed)
                {
                    buffers.CurrentTriangleMetadata =
                        new SurfaceTriangleMetadata(
                            ownerCellIndex,
                            profile.Mode == WaterFlowMode.Falling
                                ? SurfaceTriangleRole.FallingWater
                                : SurfaceTriangleRole.Core,
                            true);
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

                AddSide(world, catalog, flowState, buffers, x, y, z, startX, startZ, ownerCellIndex, profile, -1, 0);
                AddSide(world, catalog, flowState, buffers, x, y, z, startX, startZ, ownerCellIndex, profile, 1, 0);
                AddSide(world, catalog, flowState, buffers, x, y, z, startX, startZ, ownerCellIndex, profile, 0, -1);
                AddSide(world, catalog, flowState, buffers, x, y, z, startX, startZ, ownerCellIndex, profile, 0, 1);
                AddCornerSeal(world, catalog, flowState, buffers, x, y, z, startX, startZ, ownerCellIndex, profile, 0f, 0f);
                AddCornerSeal(world, catalog, flowState, buffers, x, y, z, startX, startZ, ownerCellIndex, profile, 1f, 0f);
                AddCornerSeal(world, catalog, flowState, buffers, x, y, z, startX, startZ, ownerCellIndex, profile, 0f, 1f);
                AddCornerSeal(world, catalog, flowState, buffers, x, y, z, startX, startZ, ownerCellIndex, profile, 1f, 1f);

                if (WaterCellMeshProfileResolver.IsBottomExposed(
                        world,
                        flowState,
                        x,
                        y,
                        z,
                        cell,
                        profile))
                {
                    buffers.CurrentTriangleMetadata =
                        new SurfaceTriangleMetadata(
                            ownerCellIndex,
                            SurfaceTriangleRole.Bottom,
                            true);
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
            in WaterCellMeshProfile profile)
        {
            var isFlat = profile.NegativeXNegativeZ == profile.NegativeXPositiveZ
                && profile.NegativeXNegativeZ == profile.PositiveXPositiveZ
                && profile.NegativeXNegativeZ == profile.PositiveXNegativeZ;
            if (isFlat)
            {
                AddTopPatch(
                    world, catalog, buffers,
                    x, y, z, startX, startZ, profile,
                    0f, 0f, 1f, 1f);
                return;
            }

            // A sloped top owns the same 0 / shoulder / core / shoulder / 1
            // boundary samples as its sides. This prevents the top edge and
            // side edge from becoming two independently evaluated polylines.
            for (var patchZ = 0; patchZ < 3; patchZ++)
            for (var patchX = 0; patchX < 3; patchX++)
            {
                var minX = GetPatchCoordinate(patchX);
                var minZ = GetPatchCoordinate(patchZ);
                var maxX = GetPatchCoordinate(patchX + 1);
                var maxZ = GetPatchCoordinate(patchZ + 1);
                AddTopPatch(
                    world, catalog, buffers,
                    x, y, z, startX, startZ, profile,
                    minX, minZ, maxX, maxZ);
            }
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
            in WaterCellMeshProfile profile,
            float minX,
            float minZ,
            float maxX,
            float maxZ)
        {
            var a = CreateTopVertex(
                world, catalog, x, y, z, startX, startZ,
                minX, minZ, ResolveTopHeight(profile, minX, minZ));
            var b = CreateTopVertex(
                world, catalog, x, y, z, startX, startZ,
                minX, maxZ, ResolveTopHeight(profile, minX, maxZ));
            var c = CreateTopVertex(
                world, catalog, x, y, z, startX, startZ,
                maxX, maxZ, ResolveTopHeight(profile, maxX, maxZ));
            var d = CreateTopVertex(
                world, catalog, x, y, z, startX, startZ,
                maxX, minZ, ResolveTopHeight(profile, maxX, minZ));
            buffers.AddTriangleFacing(a, b, c, Vector3.up);
            buffers.AddTriangleFacing(a, c, d, Vector3.up);
        }

        private static float GetPatchCoordinate(int index) => index switch
        {
            0 => 0f,
            1 => Shoulder,
            2 => 1f - Shoulder,
            _ => 1f
        };

        private static float ResolveTopHeight(
            in WaterCellMeshProfile profile,
            float localX,
            float localZ) =>
            profile.GetTopHeight(localX, localZ);

        private static void AddSide(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterFlowState flowState,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            int ownerCellIndex,
            in WaterCellMeshProfile profile,
            int directionX,
            int directionZ)
        {
            var resolvedProfile = profile;
            buffers.CurrentTriangleMetadata = new SurfaceTriangleMetadata(
                ownerCellIndex,
                resolvedProfile.Mode == WaterFlowMode.Falling
                    || resolvedProfile.ConnectsFromAbove
                        ? SurfaceTriangleRole.FallingWater
                        : SurfaceTriangleRole.GapFill,
                true);
            AddSideSegment(0f, Shoulder);
            AddSideSegment(Shoulder, 1f - Shoulder);
            AddSideSegment(1f - Shoulder, 1f);

            void AddSideSegment(float t0, float t1)
            {
                GetBoundaryCoordinates(directionX, directionZ, t0, out var u0, out var v0);
                GetBoundaryCoordinates(directionX, directionZ, t1, out var u1, out var v1);
                var top0 = ResolveWaterBoundaryHeight(resolvedProfile, directionX, directionZ, t0);
                var top1 = ResolveWaterBoundaryHeight(resolvedProfile, directionX, directionZ, t1);
                var bottom0 = Math.Max(
                    resolvedProfile.Interval.BottomUnits,
                    WaterCellMeshProfileResolver.ResolveNeighborCoverage(
                        world,
                        flowState,
                        x,
                        y,
                        z,
                        directionX,
                        directionZ,
                        t0));
                var bottom1 = Math.Max(
                    resolvedProfile.Interval.BottomUnits,
                    WaterCellMeshProfileResolver.ResolveNeighborCoverage(
                        world,
                        flowState,
                        x,
                        y,
                        z,
                        directionX,
                        directionZ,
                        t1));
                bottom0 = Math.Min(bottom0, top0);
                bottom1 = Math.Min(bottom1, top1);
                if (top0 <= bottom0 && top1 <= bottom1)
                {
                    return;
                }

                var topVertex0 = CreateSideVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u0, v0, top0);
                var topVertex1 = CreateSideVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u1, v1, top1);
                var bottomVertex0 = CreateSideVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u0, v0, bottom0);
                var bottomVertex1 = CreateSideVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u1, v1, bottom1);
                var outward = new Vector3(directionX, 0f, directionZ);
                buffers.AddTriangleFacing(bottomVertex0, topVertex0, topVertex1, outward);
                buffers.AddTriangleFacing(bottomVertex0, topVertex1, bottomVertex1, outward);
            }
        }

        private static void AddCornerSeal(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterFlowState flowState,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            int ownerCellIndex,
            in WaterCellMeshProfile profile,
            float cornerX,
            float cornerZ)
        {
            var directionX = cornerX <= 0f ? -1 : 1;
            var directionZ = cornerZ <= 0f ? -1 : 1;
            if (!IsDrySolidNeighbor(world, x + directionX, y, z)
                || !IsDrySolidNeighbor(world, x, y, z + directionZ))
            {
                return;
            }

            var edgePositionX = cornerX <= 0f ? Shoulder : 1f - Shoulder;
            var edgePositionZ = cornerZ <= 0f ? Shoulder : 1f - Shoulder;
            var cornerPositionX = cornerX <= 0f ? 0f : 1f;
            var cornerPositionZ = cornerZ <= 0f ? 0f : 1f;
            var xSideCornerCoverage = WaterCellMeshProfileResolver.ResolveNeighborCoverage(
                world, flowState, x, y, z, directionX, 0, cornerPositionZ);
            var zSideCornerCoverage = WaterCellMeshProfileResolver.ResolveNeighborCoverage(
                world, flowState, x, y, z, 0, directionZ, cornerPositionX);
            var xSideShoulderCoverage = WaterCellMeshProfileResolver.ResolveNeighborCoverage(
                world, flowState, x, y, z, directionX, 0, edgePositionZ);
            var zSideShoulderCoverage = WaterCellMeshProfileResolver.ResolveNeighborCoverage(
                world, flowState, x, y, z, 0, directionZ, edgePositionX);
            var top = profile.GetCorner(cornerX, cornerZ);
            var xBottom = Math.Clamp(
                xSideShoulderCoverage,
                profile.Interval.BottomUnits,
                top);
            var zBottom = Math.Clamp(
                zSideShoulderCoverage,
                profile.Interval.BottomUnits,
                top);
            var hasRecessedCorner = xSideCornerCoverage < xSideShoulderCoverage
                || zSideCornerCoverage < zSideShoulderCoverage;
            if (!hasRecessedCorner || (xBottom >= top && zBottom >= top))
            {
                return;
            }

            buffers.CurrentTriangleMetadata = new SurfaceTriangleMetadata(
                ownerCellIndex,
                SurfaceTriangleRole.GapFill,
                true);
            var corner = CreateTopVertex(
                world, catalog, x, y, z, startX, startZ,
                cornerX, cornerZ, top);
            var alongX = CreateTopVertex(
                world, catalog, x, y, z, startX, startZ,
                edgePositionX, cornerZ, zBottom);
            var alongZ = CreateTopVertex(
                world, catalog, x, y, z, startX, startZ,
                cornerX, edgePositionZ, xBottom);
            buffers.AddTriangleFacing(
                corner,
                alongX,
                alongZ,
                new Vector3(directionX, 0f, directionZ).normalized);
        }

        private static bool IsDrySolidNeighbor(
            WorldData world,
            int x,
            int y,
            int z) =>
            world.TryGetCell(x, y, z, out var cell)
            && cell.HasSolid
            && !cell.HasWater;

        private static float ResolveWaterBoundaryHeight(
            in WaterCellMeshProfile profile,
            int directionX,
            int directionZ,
            float edgePosition)
        {
            GetBoundaryCoordinates(directionX, directionZ, 0f, out var startX, out var startZ);
            GetBoundaryCoordinates(directionX, directionZ, 1f, out var endX, out var endZ);
            return ResolveTopHeight(profile, startX, startZ) * (1f - edgePosition)
                + ResolveTopHeight(profile, endX, endZ) * edgePosition;
        }

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
            float heightUnits)
        {
            return new SurfaceVertex(
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
        }

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
