using System;

namespace MiniCivilization.World.Domain
{
    public static class WorldGrid
    {
        public const int HeightStepsPerCell = 5;
        public const float HeightStep = 0.2f;

        public static float ToWorldHeight(int heightUnits) => heightUnits * HeightStep;
    }

    [Flags]
    public enum CellFlags : ushort
    {
        None = 0,
        River = 1 << 0,
        Waterfall = 1 << 1,
        Generated = 1 << 2
    }

    [Flags]
    public enum ChunkDirtyFlags : byte
    {
        None = 0,
        Cells = 1 << 0,
        Surface = 1 << 1,
        TerrainMesh = 1 << 2,
        Hydrology = 1 << 3,
        WaterMesh = 1 << 4,
        Materials = 1 << 5,
        All = Cells | Surface | TerrainMesh | Hydrology | WaterMesh | Materials
    }

    public static class WorldMaterialIds
    {
        public const ushort None = 0;
        public const ushort Soil = 1;
        public const ushort Rock = 2;
        public const ushort Grass = 3;
        public const ushort Sand = 4;
        public const ushort Snow = 5;
        public const ushort Mud = 6;

        public const ushort FreshWater = 100;
        public const ushort SeaWater = 101;
        public const ushort MarshWater = 102;
    }

    public static class WorldBiomeIds
    {
        public const ushort None = 0;
        public const ushort Grassland = 1;
        public const ushort Desert = 2;
        public const ushort Tundra = 3;
        public const ushort Wetland = 4;
        public const ushort Coast = 5;
        public const ushort Mountain = 6;
        public const ushort Seabed = 7;
        public const ushort Lakebed = 8;
        public const ushort Riverbed = 9;
    }

    [Serializable]
    public struct CellData : IEquatable<CellData>
    {
        public ushort MaterialId;
        public ushort SurfaceMaterialId;
        public ushort WaterMaterialId;
        public ushort GeologyId;
        public ushort DepositIndex;
        public byte SolidFill;
        public byte WaterFill;
        public CellFlags Flags;

        public readonly bool HasSolid => SolidFill > 0;
        public readonly bool HasWater => WaterFill > 0;

        public void Normalize()
        {
            SolidFill = (byte)Math.Min(WorldGrid.HeightStepsPerCell, (int)SolidFill);
            WaterFill = (byte)Math.Min(WorldGrid.HeightStepsPerCell - SolidFill, (int)WaterFill);

            if (SolidFill == 0)
            {
                MaterialId = WorldMaterialIds.None;
                SurfaceMaterialId = WorldMaterialIds.None;
            }

            if (WaterFill == 0)
            {
                WaterMaterialId = WorldMaterialIds.None;
                Flags &= ~(CellFlags.River | CellFlags.Waterfall);
            }
        }

        public readonly bool Equals(CellData other)
        {
            return MaterialId == other.MaterialId
                && SurfaceMaterialId == other.SurfaceMaterialId
                && WaterMaterialId == other.WaterMaterialId
                && GeologyId == other.GeologyId
                && DepositIndex == other.DepositIndex
                && SolidFill == other.SolidFill
                && WaterFill == other.WaterFill
                && Flags == other.Flags;
        }

        public override readonly bool Equals(object obj) => obj is CellData other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(MaterialId, SurfaceMaterialId, WaterMaterialId, GeologyId, DepositIndex, SolidFill, WaterFill, Flags);
    }

    [Serializable]
    public struct SurfaceColumnData
    {
        public short SurfaceCellY;
        public byte SurfaceLevel;
        public short WaterCellY;
        public byte WaterLevel;
        public ushort SurfaceMaterialId;
        public ushort WaterMaterialId;
        public ushort BiomeId;
        public byte Temperature;
        public byte Moisture;
        public byte Fertility;

        public readonly bool HasSurface => SurfaceCellY >= 0 && SurfaceLevel > 0;
        public readonly bool HasWater => WaterCellY >= 0 && WaterLevel > 0;
        public readonly int SolidTopUnits => HasSurface ? SurfaceCellY * WorldGrid.HeightStepsPerCell + SurfaceLevel : 0;
        public readonly int WaterTopUnits => HasWater ? WaterCellY * WorldGrid.HeightStepsPerCell + WaterLevel : 0;
    }

    public readonly struct CellCoordinate : IEquatable<CellCoordinate>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public CellCoordinate(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(CellCoordinate other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is CellCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    public readonly struct ChunkCoordinate : IEquatable<ChunkCoordinate>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public ChunkCoordinate(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(ChunkCoordinate other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is ChunkCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
