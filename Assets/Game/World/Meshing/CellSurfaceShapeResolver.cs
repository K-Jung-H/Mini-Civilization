using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Meshing
{
    internal enum CellSurfaceKind : byte
    {
        Solid,
        Water
    }

    internal readonly struct CellSurfaceProfile
    {
        public readonly int CurrentHeightUnits;
        public readonly int NegativeXHeightUnits;
        public readonly int PositiveXHeightUnits;
        public readonly int NegativeZHeightUnits;
        public readonly int PositiveZHeightUnits;
        public readonly int NegativeXNegativeZCornerUnits;
        public readonly int PositiveXNegativeZCornerUnits;
        public readonly int NegativeXPositiveZCornerUnits;
        public readonly int PositiveXPositiveZCornerUnits;

        public CellSurfaceProfile(
            int currentHeightUnits,
            int negativeXHeightUnits,
            int positiveXHeightUnits,
            int negativeZHeightUnits,
            int positiveZHeightUnits,
            int negativeXNegativeZCornerUnits,
            int positiveXNegativeZCornerUnits,
            int negativeXPositiveZCornerUnits,
            int positiveXPositiveZCornerUnits)
        {
            CurrentHeightUnits = currentHeightUnits;
            NegativeXHeightUnits = negativeXHeightUnits;
            PositiveXHeightUnits = positiveXHeightUnits;
            NegativeZHeightUnits = negativeZHeightUnits;
            PositiveZHeightUnits = positiveZHeightUnits;
            NegativeXNegativeZCornerUnits = negativeXNegativeZCornerUnits;
            PositiveXNegativeZCornerUnits = positiveXNegativeZCornerUnits;
            NegativeXPositiveZCornerUnits = negativeXPositiveZCornerUnits;
            PositiveXPositiveZCornerUnits = positiveXPositiveZCornerUnits;
        }

        public int GetEdgeHeight(int directionX, int directionZ)
        {
            if (directionX < 0) return NegativeXHeightUnits;
            if (directionX > 0) return PositiveXHeightUnits;
            if (directionZ < 0) return NegativeZHeightUnits;
            return PositiveZHeightUnits;
        }

        public int GetCornerHeight(float cornerX, float cornerZ)
        {
            if (cornerX <= 0f)
            {
                return cornerZ <= 0f
                    ? NegativeXNegativeZCornerUnits
                    : NegativeXPositiveZCornerUnits;
            }

            return cornerZ <= 0f
                ? PositiveXNegativeZCornerUnits
                : PositiveXPositiveZCornerUnits;
        }
    }

    internal static class CellSurfaceShapeResolver
    {
        public static CellSurfaceProfile Resolve(
            WorldData world,
            int x,
            int y,
            int z,
            CellSurfaceKind kind)
        {
            var currentHeight = GetCurrentHeight(world, x, y, z, kind);
            return new CellSurfaceProfile(
                currentHeight,
                ResolveEdgeHeight(world, x, y, z, -1, 0, kind, currentHeight),
                ResolveEdgeHeight(world, x, y, z, 1, 0, kind, currentHeight),
                ResolveEdgeHeight(world, x, y, z, 0, -1, kind, currentHeight),
                ResolveEdgeHeight(world, x, y, z, 0, 1, kind, currentHeight),
                ResolveCornerHeight(world, x, y, z, -1, -1, kind, currentHeight),
                ResolveCornerHeight(world, x, y, z, 1, -1, kind, currentHeight),
                ResolveCornerHeight(world, x, y, z, -1, 1, kind, currentHeight),
                ResolveCornerHeight(world, x, y, z, 1, 1, kind, currentHeight));
        }

        public static bool TryResolveSurfaceAtHeight(
            WorldData world,
            int x,
            int z,
            CellSurfaceKind kind,
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
                || !HasContent(cell, kind)
                || GetTop(y, cell, kind) != heightUnits
                || !IsTopExposed(world, x, y, z, cell, kind))
            {
                return false;
            }

            profile = Resolve(world, x, y, z, kind);
            return true;
        }

        public static bool TryResolveHighestSurfaceBelow(
            WorldData world,
            int x,
            int z,
            CellSurfaceKind kind,
            int exclusiveMaximumHeightUnits,
            out int heightUnits,
            out CellSurfaceProfile profile)
        {
            heightUnits = 0;
            profile = default;
            if (!world.ContainsColumn(x, z)
                || exclusiveMaximumHeightUnits <= 0)
            {
                return false;
            }

            var maximumY = Math.Min(
                world.Height - 1,
                (exclusiveMaximumHeightUnits - 1)
                    / WorldGrid.HeightStepsPerCell);
            for (var y = maximumY; y >= 0; y--)
            {
                var cell = world.GetCell(x, y, z);
                if (!HasContent(cell, kind)
                    || !IsTopExposed(world, x, y, z, cell, kind))
                {
                    continue;
                }

                var top = GetTop(y, cell, kind);
                if (top >= exclusiveMaximumHeightUnits)
                {
                    continue;
                }

                heightUnits = top;
                profile = Resolve(world, x, y, z, kind);
                return true;
            }

            return false;
        }

        private static int ResolveEdgeHeight(
            WorldData world,
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            CellSurfaceKind kind,
            int currentHeight)
        {
            var exists = TryGetConnectedSurfaceHeight(
                world,
                x + directionX,
                y,
                z + directionZ,
                kind,
                out var neighborHeight);
            return QuantizedSurfaceResolver.Resolve(
                currentHeight,
                neighborHeight,
                exists).OuterHeightUnits;
        }

        private static int ResolveCornerHeight(
            WorldData world,
            int x,
            int y,
            int z,
            int directionX,
            int directionZ,
            CellSurfaceKind kind,
            int currentHeight)
        {
            var hasX = TryGetConnectedSurfaceHeight(
                world, x + directionX, y, z, kind, out var xHeight);
            var hasZ = TryGetConnectedSurfaceHeight(
                world, x, y, z + directionZ, kind, out var zHeight);
            var hasDiagonal = TryGetConnectedSurfaceHeight(
                world,
                x + directionX,
                y,
                z + directionZ,
                kind,
                out var diagonalHeight);

            if (hasX
                && hasZ
                && hasDiagonal
                && xHeight > currentHeight
                && zHeight > currentHeight
                && diagonalHeight > currentHeight)
            {
                return Math.Min(
                    currentHeight + 1,
                    Math.Min(xHeight, zHeight));
            }

            var lowerX = hasX && xHeight < currentHeight;
            var lowerZ = hasZ && zHeight < currentHeight;
            var lowerDiagonal = hasDiagonal && diagonalHeight < currentHeight;
            return lowerX && lowerZ
                || lowerX && lowerDiagonal
                || lowerZ && lowerDiagonal
                    ? currentHeight - 1
                    : currentHeight;
        }

        private static int GetCurrentHeight(
            WorldData world,
            int x,
            int y,
            int z,
            CellSurfaceKind kind)
        {
            var cell = world.GetCell(x, y, z);
            return kind == CellSurfaceKind.Solid
                ? y * WorldGrid.HeightStepsPerCell + cell.SolidFill
                : y * WorldGrid.HeightStepsPerCell
                    + cell.SolidFill
                    + cell.WaterFill;
        }

        private static bool TryGetConnectedSurfaceHeight(
            WorldData world,
            int x,
            int y,
            int z,
            CellSurfaceKind kind,
            out int height)
        {
            if (!world.ContainsColumn(x, z))
            {
                height = 0;
                return false;
            }

            if (world.TryGetCell(x, y, z, out var same)
                && HasContent(same, kind))
            {
                height = GetTop(y, same, kind);
                var scanY = y;
                while (height == (scanY + 1) * WorldGrid.HeightStepsPerCell
                    && world.TryGetCell(x, scanY + 1, z, out var above)
                    && HasContent(above, kind)
                    && GetBottom(scanY + 1, above, kind) == height)
                {
                    scanY++;
                    height = GetTop(scanY, above, kind);
                }

                return true;
            }

            for (var scanY = Math.Min(y - 1, world.Height - 1);
                 scanY >= 0;
                 scanY--)
            {
                var candidate = world.GetCell(x, scanY, z);
                if (!HasContent(candidate, kind))
                {
                    continue;
                }

                height = GetTop(scanY, candidate, kind);
                return true;
            }

            height = 0;
            return false;
        }

        private static bool HasContent(in CellData cell, CellSurfaceKind kind) =>
            kind == CellSurfaceKind.Solid ? cell.HasSolid : cell.HasWater;

        private static bool IsTopExposed(
            WorldData world,
            int x,
            int y,
            int z,
            in CellData cell,
            CellSurfaceKind kind) =>
            kind == CellSurfaceKind.Solid
                ? CellOccupancyResolver.IsSolidTopExposed(
                    world, x, y, z, cell)
                : CellOccupancyResolver.IsWaterTopExposed(
                    world, x, y, z, cell);

        private static int GetBottom(
            int y,
            in CellData cell,
            CellSurfaceKind kind) =>
            kind == CellSurfaceKind.Solid
                ? y * WorldGrid.HeightStepsPerCell
                : y * WorldGrid.HeightStepsPerCell + cell.SolidFill;

        private static int GetTop(
            int y,
            in CellData cell,
            CellSurfaceKind kind) =>
            kind == CellSurfaceKind.Solid
                ? y * WorldGrid.HeightStepsPerCell + cell.SolidFill
                : y * WorldGrid.HeightStepsPerCell
                    + cell.SolidFill
                    + cell.WaterFill;
    }
}
