using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Meshing
{
    internal readonly struct CellSurfaceBoundary
    {
        public readonly float StartHeightUnits;
        public readonly float ShoulderStartHeightUnits;
        public readonly float ShoulderEndHeightUnits;
        public readonly float EndHeightUnits;

        public CellSurfaceBoundary(
            float startHeightUnits,
            float shoulderStartHeightUnits,
            float shoulderEndHeightUnits,
            float endHeightUnits)
        {
            StartHeightUnits = startHeightUnits;
            ShoulderStartHeightUnits = shoulderStartHeightUnits;
            ShoulderEndHeightUnits = shoulderEndHeightUnits;
            EndHeightUnits = endHeightUnits;
        }

        public float GetHeight(float position)
        {
            position = Math.Clamp(position, 0f, 1f);
            if (position <= 0.2f)
            {
                return Lerp(
                    StartHeightUnits,
                    ShoulderStartHeightUnits,
                    position / 0.2f);
            }

            if (position >= 0.8f)
            {
                return Lerp(
                    ShoulderEndHeightUnits,
                    EndHeightUnits,
                    (position - 0.8f) / 0.2f);
            }

            return Lerp(
                ShoulderStartHeightUnits,
                ShoulderEndHeightUnits,
                (position - 0.2f) / 0.6f);
        }

        private static float Lerp(float a, float b, float t) =>
            a + (b - a) * t;
    }

    internal readonly struct CellSurfaceProfile
    {
        public readonly int CurrentHeightUnits;
        public readonly CellSurfaceBoundary NegativeXBoundary;
        public readonly CellSurfaceBoundary PositiveXBoundary;
        public readonly CellSurfaceBoundary NegativeZBoundary;
        public readonly CellSurfaceBoundary PositiveZBoundary;

        public CellSurfaceProfile(
            int currentHeightUnits,
            in CellSurfaceBoundary negativeXBoundary,
            in CellSurfaceBoundary positiveXBoundary,
            in CellSurfaceBoundary negativeZBoundary,
            in CellSurfaceBoundary positiveZBoundary)
        {
            CurrentHeightUnits = currentHeightUnits;
            NegativeXBoundary = negativeXBoundary;
            PositiveXBoundary = positiveXBoundary;
            NegativeZBoundary = negativeZBoundary;
            PositiveZBoundary = positiveZBoundary;
        }

        public CellSurfaceBoundary GetBoundary(int directionX, int directionZ)
        {
            if (directionX < 0) return NegativeXBoundary;
            if (directionX > 0) return PositiveXBoundary;
            if (directionZ < 0) return NegativeZBoundary;
            return PositiveZBoundary;
        }

        public float GetBoundaryHeight(
            int directionX,
            int directionZ,
            float position) =>
            GetBoundary(directionX, directionZ).GetHeight(position);

        public float GetCornerHeight(float cornerX, float cornerZ)
        {
            if (cornerX <= 0f)
            {
                return cornerZ <= 0f
                    ? NegativeXBoundary.StartHeightUnits
                    : NegativeXBoundary.EndHeightUnits;
            }

            return cornerZ <= 0f
                ? PositiveXBoundary.StartHeightUnits
                : PositiveXBoundary.EndHeightUnits;
        }
    }

    internal static class CellSurfaceShapeResolver
    {
        public static CellSurfaceProfile Resolve(
            WorldData world,
            int x,
            int y,
            int z)
        {
            var currentHeight = GetCurrentHeight(world, x, y, z);
            if (ShouldPinSolidTopToWaterBoundary(
                    world,
                    x,
                    y,
                    z))
            {
                return CreateFlatProfile(currentHeight);
            }

            var cellBottom = y * WorldGrid.HeightStepsPerCell;
            var cellCeiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            var negativeXNegativeZ = ClampToCell(
                ResolveCornerHeight(world, x, y, z, -1, -1, currentHeight),
                cellBottom,
                cellCeiling);
            var positiveXNegativeZ = ClampToCell(
                ResolveCornerHeight(world, x, y, z, 1, -1, currentHeight),
                cellBottom,
                cellCeiling);
            var negativeXPositiveZ = ClampToCell(
                ResolveCornerHeight(world, x, y, z, -1, 1, currentHeight),
                cellBottom,
                cellCeiling);
            var positiveXPositiveZ = ClampToCell(
                ResolveCornerHeight(world, x, y, z, 1, 1, currentHeight),
                cellBottom,
                cellCeiling);

            var negativeX = ResolveBoundary(
                world, x, y, z, -1, 0, currentHeight,
                negativeXNegativeZ, negativeXPositiveZ,
                cellBottom, cellCeiling);
            var positiveX = ResolveBoundary(
                world, x, y, z, 1, 0, currentHeight,
                positiveXNegativeZ, positiveXPositiveZ,
                cellBottom, cellCeiling);
            var negativeZ = ResolveBoundary(
                world, x, y, z, 0, -1, currentHeight,
                negativeXNegativeZ, positiveXNegativeZ,
                cellBottom, cellCeiling);
            var positiveZ = ResolveBoundary(
                world, x, y, z, 0, 1, currentHeight,
                negativeXPositiveZ, positiveXPositiveZ,
                cellBottom, cellCeiling);

            return new CellSurfaceProfile(
                currentHeight,
                negativeX,
                positiveX,
                negativeZ,
                positiveZ);
        }

        private static CellSurfaceProfile CreateFlatProfile(int height)
        {
            var boundary = new CellSurfaceBoundary(
                height,
                height,
                height,
                height);
            return new CellSurfaceProfile(
                height,
                boundary,
                boundary,
                boundary,
                boundary);
        }

        private static int ClampToCell(
            int height,
            int cellBottom,
            int cellCeiling) =>
            Math.Clamp(height, cellBottom, cellCeiling);

        private static float ClampToCell(
            float height,
            int cellBottom,
            int cellCeiling) =>
            Math.Clamp(height, cellBottom, cellCeiling);

        private static CellSurfaceBoundary ResolveBoundary(
            WorldData world,
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            int currentHeight,
            float startCornerHeight,
            float endCornerHeight,
            int cellBottom,
            int cellCeiling)
        {
            return new CellSurfaceBoundary(
                startCornerHeight,
                ClampToCell(
                    ResolveSolidBoundaryHeight(
                        world,
                        x,
                        y,
                        z,
                        directionX,
                        directionZ,
                        currentHeight,
                        0.2f),
                    cellBottom,
                    cellCeiling),
                ClampToCell(
                    ResolveSolidBoundaryHeight(
                        world,
                        x,
                        y,
                        z,
                        directionX,
                        directionZ,
                        currentHeight,
                        0.8f),
                    cellBottom,
                    cellCeiling),
                endCornerHeight);
        }

        /// <summary>
        /// Resolves every Solid boundary vertex from the highest visible
        /// coverage in the neighboring Cell. Solid and horizontal Water are
        /// therefore evaluated by one rule, and a shoulder can descend by at
        /// most one quantized height unit. Any remaining drop belongs to the
        /// vertical side mesh.
        /// </summary>
        private static float ResolveSolidBoundaryHeight(
            WorldData world,
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            int currentHeight,
            float edgePosition)
        {
            var neighborExists = TryGetConnectedSurfaceHeight(
                world,
                x,
                z,
                x + directionX,
                y,
                z + directionZ,
                out var solidHeight);
            var neighborHeight = neighborExists
                ? (float)solidHeight
                : float.MinValue;

            if (WaterCellMeshProfileResolver.TryResolveHorizontalBoundary(
                    world,
                    x + directionX,
                    y,
                    z + directionZ,
                    directionX,
                    directionZ,
                    out var waterBoundary))
            {
                neighborExists = true;
                neighborHeight = Math.Max(
                    neighborHeight,
                    waterBoundary.GetHeight(edgePosition));
            }

            if (!neighborExists || neighborHeight >= currentHeight)
            {
                return currentHeight;
            }

            return Math.Max(currentHeight - 1f, neighborHeight);
        }

        private static bool ShouldPinSolidTopToWaterBoundary(
            WorldData world,
            int x,
            int y,
            int z)
        {
            if (!world.TryGetCell(x, y, z, out var cell))
            {
                return false;
            }

            // Water stored above this SolidFill owns the remaining volume of
            // the same Cell. Their contact must stay on the logical fill plane.
            if (cell.HasWater)
            {
                return true;
            }

            // A full Solid Cell and the Water Cell immediately above share the
            // same Cell boundary. Pinning the covered top prevents shoulders
            // from protruding into the Water Cell or receding below it.
            return cell.SolidFill == WorldGrid.HeightStepsPerCell
                && world.TryGetCell(x, y + 1, z, out var above)
                && above.HasWater
                && above.SolidFill == 0;
        }

        public static bool TryResolveSurfaceAtHeight(
            WorldData world,
            int x,
            int z,
            int heightUnits,
            out CellSurfaceProfile profile)
        {
            profile = default;
            if (!world.ContainsColumn(x, z) || heightUnits <= 0)
            {
                return false;
            }

            var y = (heightUnits - 1) / WorldGrid.HeightStepsPerCell;
            if (!world.TryGetCell(x, y, z, out var cell)
                || !cell.HasSolid
                || GetTop(y, cell) != heightUnits
                || !CellOccupancyResolver.IsSolidTopExposed(
                    world, x, y, z, cell))
            {
                return false;
            }

            profile = Resolve(world, x, y, z);
            return true;
        }

        private static int ResolveCornerHeight(
            WorldData world,
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            int currentHeight)
        {
            return SolidSurfaceJunctionResolver.ResolveTopCorner(
                world,
                x,
                y,
                z,
                directionX,
                directionZ,
                currentHeight);
        }

        /// <summary>
        /// Resolves a Solid top corner from one canonical 2x2x2 occupancy
        /// junction. Face-connected top participants share one result, while
        /// only their exterior boundary arms propose the corner height.
        /// </summary>
        private static class SolidSurfaceJunctionResolver
        {
            public static int ResolveTopCorner(
                WorldData world,
                int sourceX,
                int sourceY,
                int sourceZ,
                int directionX,
                int directionZ,
                int currentHeight)
            {
                var cornerX = sourceX + (directionX > 0 ? 1 : 0);
                var cornerZ = sourceZ + (directionZ > 0 ? 1 : 0);
                var occupancy =
                    CellOccupancyResolver.ResolveSolidCornerOccupancy(
                        world,
                        cornerX,
                        currentHeight,
                        cornerZ);
                var sourceQuadrant =
                    SolidCornerOccupancy.GetQuadrantIndex(
                        cornerX,
                        cornerZ,
                        sourceX,
                        sourceZ);
                var sourceBit = (byte)(1 << sourceQuadrant);
                if ((occupancy.TopMask & sourceBit) == 0)
                {
                    return ResolveLocalFallback(
                        world,
                        sourceX,
                        sourceY,
                        sourceZ,
                        directionX,
                        directionZ,
                        currentHeight);
                }

                var connectedTopMask = ExpandConnected(
                    sourceBit,
                    occupancy.TopMask);

                // A single low top surrounded by three occupied upper
                // quadrants forms the concave step-pyramid junction. Raising
                // by one quantized step is safe because every upper quadrant
                // is occupied immediately above this plane.
                if (CountBits((byte)(occupancy.AboveMask
                        & ~connectedTopMask)) >= 3)
                {
                    return currentHeight + 1;
                }

                var resolvedHeight = currentHeight - 1;
                var exteriorBoundaryCount = 0;
                for (var quadrant = 0; quadrant < 4; quadrant++)
                {
                    var bit = (byte)(1 << quadrant);
                    if ((connectedTopMask & bit) == 0)
                    {
                        continue;
                    }

                    occupancy.GetCellCoordinate(
                        quadrant,
                        out var cellX,
                        out var cellZ);
                    var cellY = (currentHeight - 1)
                        / WorldGrid.HeightStepsPerCell;
                    var cornerDirectionX = cellX < cornerX ? 1 : -1;
                    var cornerDirectionZ = cellZ < cornerZ ? 1 : -1;

                    // An edge between two participants of this top component
                    // is internal. It must not veto a Down requested by the
                    // visible outer shoulders around the junction.
                    var acrossXBit = (byte)(1 << (quadrant ^ 1));
                    if ((connectedTopMask & acrossXBit) == 0)
                    {
                        exteriorBoundaryCount++;
                        resolvedHeight = Math.Max(
                            resolvedHeight,
                            RoundHeightUnit(ResolveSolidBoundaryHeight(
                                world,
                                cellX,
                                cellY,
                                cellZ,
                                cornerDirectionX,
                                0,
                                currentHeight,
                                cornerDirectionZ > 0 ? 1f : 0f)));
                    }

                    var acrossZBit = (byte)(1 << (quadrant ^ 2));
                    if ((connectedTopMask & acrossZBit) == 0)
                    {
                        exteriorBoundaryCount++;
                        resolvedHeight = Math.Max(
                            resolvedHeight,
                            RoundHeightUnit(ResolveSolidBoundaryHeight(
                                world,
                                cellX,
                                cellY,
                                cellZ,
                                0,
                                cornerDirectionZ,
                                currentHeight,
                                cornerDirectionX > 0 ? 1f : 0f)));
                    }
                }

                // Four connected top quadrants make this an interior corner;
                // there is no exterior shoulder that could request a slope.
                return exteriorBoundaryCount == 0
                    ? currentHeight
                    : resolvedHeight;
            }

            private static int ResolveLocalFallback(
                WorldData world,
                int x,
                int y,
                int z,
                int directionX,
                int directionZ,
                int currentHeight)
            {
                var xEdgeHeight = RoundHeightUnit(
                    ResolveSolidBoundaryHeight(
                        world,
                        x,
                        y,
                        z,
                        directionX,
                        0,
                        currentHeight,
                        directionZ > 0 ? 1f : 0f));
                var zEdgeHeight = RoundHeightUnit(
                    ResolveSolidBoundaryHeight(
                        world,
                        x,
                        y,
                        z,
                        0,
                        directionZ,
                        currentHeight,
                        directionX > 0 ? 1f : 0f));
                return Math.Max(xEdgeHeight, zEdgeHeight);
            }

            private static int RoundHeightUnit(float height) =>
                (int)Math.Round(height, MidpointRounding.AwayFromZero);

            private static byte ExpandConnected(
                byte seedMask,
                byte availableMask)
            {
                var connected = (byte)(seedMask & availableMask);
                for (var pass = 0; pass < 4; pass++)
                {
                    var expanded = connected;
                    for (var quadrant = 0; quadrant < 4; quadrant++)
                    {
                        if ((connected & (1 << quadrant)) == 0)
                        {
                            continue;
                        }

                        expanded |= GetAdjacentMask(quadrant);
                    }

                    expanded &= availableMask;
                    if (expanded == connected)
                    {
                        break;
                    }

                    connected = expanded;
                }

                return connected;
            }

            private static byte GetAdjacentMask(int quadrant)
            {
                return quadrant switch
                {
                    0 => 0b0110,
                    1 => 0b1001,
                    2 => 0b1001,
                    _ => 0b0110
                };
            }

            private static int CountBits(byte mask)
            {
                var count = 0;
                while (mask != 0)
                {
                    count += mask & 1;
                    mask >>= 1;
                }

                return count;
            }
        }

        private static int ResolveContinuousTop(
            WorldData world,
            int x,
            int y,
            int z,
            in CellData cell)
        {
            var height = GetTop(y, cell);
            var scanY = y;
            while (height == (scanY + 1) * WorldGrid.HeightStepsPerCell
                && world.TryGetCell(x, scanY + 1, z, out var above)
                && above.HasSolid
                && GetBottom(scanY + 1) == height)
            {
                scanY++;
                height = GetTop(scanY, above);
            }

            return height;
        }

        private static int GetCurrentHeight(
            WorldData world,
            int x,
            int y,
            int z)
        {
            var cell = world.GetCell(x, y, z);
            return y * WorldGrid.HeightStepsPerCell + cell.SolidFill;
        }

        private static bool TryGetConnectedSurfaceHeight(
            WorldData world,
            int sourceX,
            int sourceZ,
            int targetX,
            int y,
            int targetZ,
            out int height)
        {
            if (!world.ContainsColumn(sourceX, sourceZ)
                || !world.ContainsColumn(targetX, targetZ))
            {
                height = 0;
                return false;
            }

            if (world.TryGetCell(targetX, y, targetZ, out var same)
                && same.HasSolid)
            {
                height = ResolveContinuousTop(
                    world,
                    targetX,
                    y,
                    targetZ,
                    same);
                return true;
            }

            // Descend both columns together. The target surface is connected
            // only while the source column remains a continuous volume. This
            // preserves stepped shoulders without linking a floating Cell to
            // unrelated terrain far below it.
            for (var scanY = y - 1; scanY >= 0; scanY--)
            {
                var source = world.GetCell(sourceX, scanY, sourceZ);
                var sourceCeiling = (scanY + 1)
                    * WorldGrid.HeightStepsPerCell;
                if (!source.HasSolid
                    || GetTop(scanY, source) != sourceCeiling)
                {
                    break;
                }

                var candidate = world.GetCell(targetX, scanY, targetZ);
                if (candidate.HasSolid)
                {
                    height = GetTop(scanY, candidate);
                    return true;
                }
            }

            height = 0;
            return false;
        }

        private static int GetBottom(int y) =>
            y * WorldGrid.HeightStepsPerCell;

        private static int GetTop(
            int y,
            in CellData cell) =>
            y * WorldGrid.HeightStepsPerCell + cell.SolidFill;
    }
}
