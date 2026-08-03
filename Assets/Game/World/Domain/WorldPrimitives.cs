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
        Generated = 1 << 0
    }

    public enum CellMaterialType : ushort
    {
        None = 0,
        Soil = 1,
        Rock = 2
    }

    public enum BiomeType : ushort
    {
        None = 0,
        Grassland = 1,
        Forest = 2,
        Desert = 3,
        Snow = 4,
        Wetland = 5,
        Mountain = 6
    }

    public enum SurfaceType : ushort
    {
        None = 0,
        Ground = 1,
        Cliff = 2,
        Road = 3,
        Riverbed = 4,
        Lakebed = 5,
        Seabed = 6,
        Shore = 7
    }

    public enum WaterCellRole : byte
    {
        None = 0,
        Dynamic = 1,
        Source = 2
    }

    [Flags]
    public enum WaterFlowDirectionMask : byte
    {
        None = 0,
        East = 1 << 0,
        North = 1 << 1,
        West = 1 << 2,
        South = 1 << 3,
        Down = 1 << 4,
        Horizontal = East | North | West | South
    }

    public static class WaterAmount
    {
        public const byte Full = 100;
        public const float Unit = 0.01f;

        public static byte FromNormalized(float value) =>
            checked((byte)Math.Clamp(
                (int)MathF.Round(value * Full),
                0,
                Full));

        public static byte FromRenderFill(byte renderFill, int capacitySteps)
        {
            if (renderFill == 0 || capacitySteps <= 0)
            {
                return 0;
            }

            return checked((byte)Math.Clamp(
                (renderFill * Full + capacitySteps - 1) / capacitySteps,
                1,
                Full));
        }

        public static byte ToRenderFill(byte amount, int capacitySteps)
        {
            if (amount == 0 || capacitySteps <= 0)
            {
                return 0;
            }

            return checked((byte)Math.Clamp(
                (amount * capacitySteps + Full - 1) / Full,
                1,
                capacitySteps));
        }
    }

    [Serializable]
    public struct WaterFlowRules : IEquatable<WaterFlowRules>
    {
        public byte SpreadAmountLoss;
        public byte MinimumSpreadAmount;
        public byte DissipationAmountLoss;

        public static WaterFlowRules Default => new(0.05f, 0.1f, 0.05f);

        public WaterFlowRules(
            float spreadAmountLoss,
            float minimumSpreadAmount) : this(
                spreadAmountLoss,
                minimumSpreadAmount,
                spreadAmountLoss)
        {
        }

        public WaterFlowRules(
            float spreadAmountLoss,
            float minimumSpreadAmount,
            float dissipationAmountLoss)
        {
            SpreadAmountLoss = WaterAmount.FromNormalized(
                Math.Clamp(spreadAmountLoss, WaterAmount.Unit, 1f));
            MinimumSpreadAmount = WaterAmount.FromNormalized(
                Math.Clamp(minimumSpreadAmount, WaterAmount.Unit, 1f));
            DissipationAmountLoss = WaterAmount.FromNormalized(
                Math.Clamp(dissipationAmountLoss, WaterAmount.Unit, 1f));
        }

        public WaterFlowRules(
            byte spreadAmountLoss,
            byte minimumSpreadAmount) : this(
                spreadAmountLoss,
                minimumSpreadAmount,
                spreadAmountLoss)
        {
        }

        public WaterFlowRules(
            byte spreadAmountLoss,
            byte minimumSpreadAmount,
            byte dissipationAmountLoss)
        {
            SpreadAmountLoss = Math.Clamp(
                spreadAmountLoss,
                (byte)1,
                WaterAmount.Full);
            MinimumSpreadAmount = Math.Clamp(
                minimumSpreadAmount,
                (byte)1,
                WaterAmount.Full);
            DissipationAmountLoss = Math.Clamp(
                dissipationAmountLoss,
                (byte)1,
                WaterAmount.Full);
        }

        public readonly float SpreadAmountLossNormalized =>
            SpreadAmountLoss * WaterAmount.Unit;

        public readonly float MinimumSpreadAmountNormalized =>
            MinimumSpreadAmount * WaterAmount.Unit;

        public readonly float DissipationAmountLossNormalized =>
            DissipationAmountLoss * WaterAmount.Unit;

        public readonly bool Equals(WaterFlowRules other) =>
            SpreadAmountLoss == other.SpreadAmountLoss
            && MinimumSpreadAmount == other.MinimumSpreadAmount
            && DissipationAmountLoss == other.DissipationAmountLoss;

        public override readonly bool Equals(object obj) =>
            obj is WaterFlowRules other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(
            SpreadAmountLoss,
            MinimumSpreadAmount,
            DissipationAmountLoss);
    }

    [Serializable]
    public struct WaterCellData : IEquatable<WaterCellData>
    {
        public byte Amount;
        public WaterCellRole Role;
        public WaterFlowDirectionMask Direction;

        public readonly bool HasWater => Amount > 0;
        public readonly bool IsStatic =>
            HasWater && Direction == WaterFlowDirectionMask.None;
        public readonly bool IsFalling =>
            (Direction & WaterFlowDirectionMask.Down) != 0;
        public readonly bool IsFlowing =>
            HasWater && Direction != WaterFlowDirectionMask.None;

        public void Normalize()
        {
            Amount = Math.Min(Amount, WaterAmount.Full);
            Direction &= WaterFlowDirectionMask.Horizontal
                | WaterFlowDirectionMask.Down;
            if (Amount == 0)
            {
                Role = WaterCellRole.None;
                Direction = WaterFlowDirectionMask.None;
            }
            else if (Role == WaterCellRole.None)
            {
                Role = WaterCellRole.Source;
            }
        }

        public readonly bool Equals(WaterCellData other) =>
            Amount == other.Amount
            && Role == other.Role
            && Direction == other.Direction;

        public override readonly bool Equals(object obj) =>
            obj is WaterCellData other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(
            Amount,
            Role,
            Direction);
    }

    [Serializable]
    public struct CellData : IEquatable<CellData>
    {
        public CellMaterialType Material;
        public SurfaceType Surface;
        public CellMaterialType Geology;
        public ushort DepositIndex;
        public byte SolidFill;
        public CellFlags Flags;
        public WaterCellData Water;

        public readonly bool HasSolid => SolidFill > 0;
        public readonly bool HasWater => Water.HasWater;
        public readonly byte WaterFill => Water.IsFalling
            ? (byte)(WorldGrid.HeightStepsPerCell - SolidFill)
            : WaterAmount.ToRenderFill(
                Water.Amount,
                WorldGrid.HeightStepsPerCell - SolidFill);

        public void Normalize()
        {
            SolidFill = (byte)Math.Min(WorldGrid.HeightStepsPerCell, (int)SolidFill);
            Water.Normalize();

            if (SolidFill == 0)
            {
                Material = CellMaterialType.None;
                Surface = SurfaceType.None;
            }

            if (!Water.HasWater || WorldGrid.HeightStepsPerCell - SolidFill <= 0)
            {
                Water = default;
            }
        }

        public readonly bool Equals(CellData other)
        {
            return Material == other.Material
                && Surface == other.Surface
                && Geology == other.Geology
                && DepositIndex == other.DepositIndex
                && SolidFill == other.SolidFill
                && Flags == other.Flags
                && Water.Equals(other.Water);
        }

        public override readonly bool Equals(object obj) => obj is CellData other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(
            Material,
            Surface,
            Geology,
            DepositIndex,
            SolidFill,
            Flags,
            Water);
    }

    [Serializable]
    public struct SurfaceColumnData
    {
        public short SurfaceCellY;
        public byte SurfaceLevel;
        public short WaterCellY;
        public byte WaterLevel;
        public SurfaceType Surface;

        public readonly bool HasSurface => SurfaceCellY >= 0 && SurfaceLevel > 0;
        public readonly bool HasWater => WaterCellY >= 0 && WaterLevel > 0;
        public readonly int SolidTopUnits => HasSurface ? SurfaceCellY * WorldGrid.HeightStepsPerCell + SurfaceLevel : 0;
        public readonly int WaterTopUnits => HasWater ? WaterCellY * WorldGrid.HeightStepsPerCell + WaterLevel : 0;
    }

    [Serializable]
    public struct ColumnEnvironmentData
    {
        public BiomeType Biome;
        public byte Temperature;
        public byte Moisture;
        public byte Fertility;
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
