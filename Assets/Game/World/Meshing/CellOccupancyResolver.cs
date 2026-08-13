using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Meshing
{
    [Flags]
    internal enum CellExposureFlags : ushort
    {
        None = 0,
        SolidTop = 1 << 0,
        SolidBottom = 1 << 1,
        SolidNegativeX = 1 << 2,
        SolidPositiveX = 1 << 3,
        SolidNegativeZ = 1 << 4,
        SolidPositiveZ = 1 << 5,
        WaterTop = 1 << 6,
        WaterNegativeX = 1 << 7,
        WaterPositiveX = 1 << 8,
        WaterNegativeZ = 1 << 9,
        WaterPositiveZ = 1 << 10,
        WaterBottom = 1 << 11
    }

    internal readonly struct HeightInterval
    {
        public readonly int BottomUnits;
        public readonly int TopUnits;

        public HeightInterval(int bottomUnits, int topUnits)
        {
            BottomUnits = bottomUnits;
            TopUnits = topUnits;
        }

        public bool IsValid => TopUnits > BottomUnits;
    }

    internal readonly struct SolidCornerOccupancy
    {
        public readonly int CornerX;
        public readonly int HeightUnits;
        public readonly int CornerZ;
        public readonly byte BelowMask;
        public readonly byte AboveMask;

        public SolidCornerOccupancy(
            int cornerX,
            int heightUnits,
            int cornerZ,
            byte belowMask,
            byte aboveMask)
        {
            CornerX = cornerX;
            HeightUnits = heightUnits;
            CornerZ = cornerZ;
            BelowMask = belowMask;
            AboveMask = aboveMask;
        }

        public byte TopMask => (byte)(BelowMask & ~AboveMask & 0x0f);
        public byte BottomMask => (byte)(AboveMask & ~BelowMask & 0x0f);
        public byte ThroughMask => (byte)(BelowMask & AboveMask & 0x0f);

        public static int GetQuadrantIndex(
            int cornerX,
            int cornerZ,
            int cellX,
            int cellZ)
        {
            var xIndex = cellX == cornerX ? 1 : 0;
            var zIndex = cellZ == cornerZ ? 2 : 0;
            return xIndex | zIndex;
        }

        public void GetCellCoordinate(
            int quadrantIndex,
            out int cellX,
            out int cellZ)
        {
            cellX = CornerX - 1 + (quadrantIndex & 1);
            cellZ = CornerZ - 1 + ((quadrantIndex >> 1) & 1);
        }
    }

    internal static class CellOccupancyResolver
    {
        public static SolidCornerOccupancy ResolveSolidCornerOccupancy(
            WorldData world,
            int cornerX,
            int heightUnits,
            int cornerZ)
        {
            byte belowMask = 0;
            byte aboveMask = 0;
            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var cellX = cornerX - 1 + (quadrant & 1);
                var cellZ = cornerZ - 1 + ((quadrant >> 1) & 1);
                var bit = (byte)(1 << quadrant);
                if (IsSolidAtHeightUnit(
                        world,
                        cellX,
                        heightUnits - 1,
                        cellZ))
                {
                    belowMask |= bit;
                }

                if (IsSolidAtHeightUnit(
                        world,
                        cellX,
                        heightUnits,
                        cellZ))
                {
                    aboveMask |= bit;
                }
            }

            return new SolidCornerOccupancy(
                cornerX,
                heightUnits,
                cornerZ,
                belowMask,
                aboveMask);
        }

        private static bool IsSolidAtHeightUnit(
            WorldData world,
            int x,
            int heightUnit,
            int z)
        {
            if (heightUnit < 0
                || !world.ContainsColumn(x, z))
            {
                return false;
            }

            var y = heightUnit / WorldGrid.HeightStepsPerCell;
            if (!world.TryGetCell(x, y, z, out var cell))
            {
                return false;
            }

            var localHeightUnit = heightUnit
                - y * WorldGrid.HeightStepsPerCell;
            return localHeightUnit < cell.Terrain.SolidHeight;
        }

        public static HeightInterval GetSolidInterval(int y, in CellData cell)
        {
            var bottom = y * WorldGrid.HeightStepsPerCell;
            return new HeightInterval(bottom, bottom + cell.Terrain.SolidHeight);
        }

        public static HeightInterval GetWaterInterval(int y, in CellData cell)
        {
            var bottom = y * WorldGrid.HeightStepsPerCell + cell.Terrain.SolidHeight;
            return new HeightInterval(bottom, bottom + cell.WaterHeight);
        }

        public static CellExposureFlags ResolveExposure(
            WorldData world,
            int x,
            int y,
            int z)
        {
            if (!world.TryGetCell(x, y, z, out var cell))
            {
                return CellExposureFlags.None;
            }

            var flags = CellExposureFlags.None;
            if (cell.HasTerrain)
            {
                if (IsSolidTopExposed(world, x, y, z, cell))
                {
                    flags |= CellExposureFlags.SolidTop;
                }

                if (IsSolidBottomExposed(world, x, y, z))
                {
                    flags |= CellExposureFlags.SolidBottom;
                }

                if (TryGetSolidSideExposure(world, x, y, z, cell, -1, 0, out _))
                    flags |= CellExposureFlags.SolidNegativeX;
                if (TryGetSolidSideExposure(world, x, y, z, cell, 1, 0, out _))
                    flags |= CellExposureFlags.SolidPositiveX;
                if (TryGetSolidSideExposure(world, x, y, z, cell, 0, -1, out _))
                    flags |= CellExposureFlags.SolidNegativeZ;
                if (TryGetSolidSideExposure(world, x, y, z, cell, 0, 1, out _))
                    flags |= CellExposureFlags.SolidPositiveZ;
            }

            if (cell.HasWater)
            {
                if (IsWaterTopExposed(world, x, y, z, cell))
                {
                    flags |= CellExposureFlags.WaterTop;
                }

                if (IsWaterBottomExposed(world, x, y, z, cell))
                {
                    flags |= CellExposureFlags.WaterBottom;
                }

                if (TryGetWaterSideExposure(world, x, y, z, cell, -1, 0, out _))
                    flags |= CellExposureFlags.WaterNegativeX;
                if (TryGetWaterSideExposure(world, x, y, z, cell, 1, 0, out _))
                    flags |= CellExposureFlags.WaterPositiveX;
                if (TryGetWaterSideExposure(world, x, y, z, cell, 0, -1, out _))
                    flags |= CellExposureFlags.WaterNegativeZ;
                if (TryGetWaterSideExposure(world, x, y, z, cell, 0, 1, out _))
                    flags |= CellExposureFlags.WaterPositiveZ;
            }

            return flags;
        }

        public static bool IsSolidTopExposed(
            WorldData world,
            int x,
            int y,
            int z,
            in CellData cell)
        {
            if (!cell.HasTerrain)
            {
                return false;
            }

            if (cell.Terrain.SolidHeight < WorldGrid.HeightStepsPerCell)
            {
                return true;
            }

            return !world.TryGetCell(x, y + 1, z, out var above)
                || !above.HasTerrain;
        }

        public static bool IsSolidBottomExposed(
            WorldData world,
            int x,
            int y,
            int z)
        {
            if (y <= 0)
            {
                return false;
            }

            return !world.TryGetCell(x, y - 1, z, out var below)
                || below.Terrain.SolidHeight < WorldGrid.HeightStepsPerCell;
        }

        public static bool TryGetSolidSideExposure(
            WorldData world,
            int x,
            int y,
            int z,
            in CellData cell,
            int directionX,
            int directionZ,
            out HeightInterval exposed)
        {
            var current = GetSolidInterval(y, cell);
            var neighborTop = current.BottomUnits;
            if (world.TryGetCell(
                    x + directionX,
                    y,
                    z + directionZ,
                    out var neighbor))
            {
                neighborTop += neighbor.Terrain.SolidHeight;
            }

            exposed = new HeightInterval(
                Math.Max(current.BottomUnits, neighborTop),
                current.TopUnits);
            return exposed.IsValid;
        }

        public static bool IsWaterTopExposed(
            WorldData world,
            int x,
            int y,
            int z,
            in CellData cell)
        {
            if (!cell.HasWater)
            {
                return false;
            }

            var interval = GetWaterInterval(y, cell);
            var ceiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            if (interval.TopUnits < ceiling)
            {
                return true;
            }

            if (!world.TryGetCell(x, y + 1, z, out var above))
            {
                return true;
            }

            if (above.HasTerrain)
            {
                return false;
            }

            return !above.HasWater || above.Terrain.SolidHeight > 0;
        }

        public static bool TryGetWaterSideExposure(
            WorldData world,
            int x,
            int y,
            int z,
            in CellData cell,
            int directionX,
            int directionZ,
            out HeightInterval exposed)
        {
            var current = GetWaterInterval(y, cell);
            var coveredTop = y * WorldGrid.HeightStepsPerCell;
            if (world.TryGetCell(
                    x + directionX,
                    y,
                    z + directionZ,
                    out var neighbor))
            {
                coveredTop += neighbor.Terrain.SolidHeight + neighbor.WaterHeight;
            }

            exposed = new HeightInterval(
                Math.Max(current.BottomUnits, coveredTop),
                current.TopUnits);
            return exposed.IsValid;
        }

        public static bool IsWaterBottomExposed(
            WorldData world,
            int x,
            int y,
            int z,
            in CellData cell)
        {
            if (!cell.HasWater || cell.HasTerrain)
            {
                return false;
            }

            if (y <= 0)
            {
                return false;
            }

            return !world.TryGetCell(x, y - 1, z, out var below)
                || below.Terrain.SolidHeight + below.WaterHeight
                    < WorldGrid.HeightStepsPerCell;
        }
    }
}
