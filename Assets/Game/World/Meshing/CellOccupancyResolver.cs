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

    internal static class CellOccupancyResolver
    {
        public static HeightInterval GetSolidInterval(int y, in CellData cell)
        {
            var bottom = y * WorldGrid.HeightStepsPerCell;
            return new HeightInterval(bottom, bottom + cell.SolidFill);
        }

        public static HeightInterval GetWaterInterval(int y, in CellData cell)
        {
            var bottom = y * WorldGrid.HeightStepsPerCell + cell.SolidFill;
            return new HeightInterval(bottom, bottom + cell.WaterFill);
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
            if (cell.HasSolid)
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
            if (!cell.HasSolid)
            {
                return false;
            }

            if (cell.SolidFill < WorldGrid.HeightStepsPerCell)
            {
                return true;
            }

            return !world.TryGetCell(x, y + 1, z, out var above)
                || !above.HasSolid;
        }

        public static bool IsSolidBottomExposed(
            WorldData world,
            int x,
            int y,
            int z)
        {
            if (y <= 0)
            {
                // The world floor is a closed boundary and is never visible.
                return false;
            }

            return !world.TryGetCell(x, y - 1, z, out var below)
                || below.SolidFill < WorldGrid.HeightStepsPerCell;
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
                neighborTop += neighbor.SolidFill;
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

            if (above.HasSolid)
            {
                return false;
            }

            return !above.HasWater || above.SolidFill > 0;
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
                coveredTop += neighbor.SolidFill + neighbor.WaterFill;
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
            if (!cell.HasWater || cell.HasSolid)
            {
                return false;
            }

            if (y <= 0)
            {
                return false;
            }

            return !world.TryGetCell(x, y - 1, z, out var below)
                || below.SolidFill + below.WaterFill
                    < WorldGrid.HeightStepsPerCell;
        }
    }
}
