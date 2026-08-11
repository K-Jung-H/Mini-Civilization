using System;

namespace MiniCivilization.World.Domain
{
    public static class WorldGrid
    {
        public const int HeightStepsPerCell = 5;

    }

    public enum MaterialType : ushort
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

    public enum RoadType : ushort
    {
        None = 0,
        Basic = 1
    }

    public enum WaterRole : byte
    {
        None = 0,
        Dynamic = 1,
        Source = 2
    }

    public enum WaterType : byte
    {
        None = 0,
        Pond = 1,
        Lake = 2,
        Sea = 3,
        River = 4
    }

    [Flags]
    public enum FlowDirection : byte
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
    public struct WaterData : IEquatable<WaterData>
    {
        public byte Amount;
        public WaterRole Role;
        public WaterType Type;
        public FlowDirection Flow;

        public readonly bool HasWater => Amount > 0;
        public readonly bool Falls =>
            (Flow & FlowDirection.Down) != 0;
        public readonly bool Flows =>
            HasWater && Flow != FlowDirection.None;

        public void Normalize()
        {
            Amount = Math.Min(Amount, WaterAmount.Full);
            Flow &= FlowDirection.Horizontal | FlowDirection.Down;
            if (Amount == 0)
            {
                Role = WaterRole.None;
                Type = WaterType.None;
                Flow = FlowDirection.None;
                return;
            }
        }

        public readonly bool Equals(WaterData other) =>
            Amount == other.Amount
            && Role == other.Role
            && Type == other.Type
            && Flow == other.Flow;

        public override readonly bool Equals(object obj) =>
            obj is WaterData other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(
            Amount,
            Role,
            Type,
            Flow);
    }

    [Serializable]
    public struct TerrainData : IEquatable<TerrainData>
    {
        public MaterialType Material;
        public SurfaceType Surface;
        public MaterialType Geology;
        public ushort ResourceId;
        public byte SolidHeight;

        public readonly bool HasTerrain => SolidHeight > 0;

        public void Normalize()
        {
            SolidHeight = (byte)Math.Min(
                WorldGrid.HeightStepsPerCell,
                (int)SolidHeight);
            if (SolidHeight > 0)
            {
                return;
            }

            Material = MaterialType.None;
            Surface = SurfaceType.None;
            Geology = MaterialType.None;
            ResourceId = 0;
        }

        public readonly bool Equals(TerrainData other) =>
            Material == other.Material
            && Surface == other.Surface
            && Geology == other.Geology
            && ResourceId == other.ResourceId
            && SolidHeight == other.SolidHeight;

        public override readonly bool Equals(object obj) =>
            obj is TerrainData other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(
            Material,
            Surface,
            Geology,
            ResourceId,
            SolidHeight);
    }

    [Serializable]
    public struct RoadData : IEquatable<RoadData>
    {
        public RoadType Type;
        public bool CrossesCenter;

        public readonly bool HasRoad => Type != RoadType.None;

        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(RoadType), Type)
                || Type == RoadType.None)
            {
                Type = RoadType.None;
                CrossesCenter = false;
            }
        }

        public readonly bool Equals(RoadData other) =>
            Type == other.Type
            && CrossesCenter == other.CrossesCenter;

        public override readonly bool Equals(object obj) =>
            obj is RoadData other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(
            Type,
            CrossesCenter);
    }

    [Serializable]
    public struct CellData : IEquatable<CellData>
    {
        public TerrainData Terrain;
        public WaterData Water;
        public RoadData Road;

        public readonly bool HasTerrain => Terrain.HasTerrain;
        public readonly bool HasWater => Water.HasWater;
        public readonly bool HasRoad => Road.HasRoad;
        public readonly byte WaterHeight => Water.Falls
            ? (byte)(WorldGrid.HeightStepsPerCell - Terrain.SolidHeight)
            : WaterAmount.ToRenderFill(
                Water.Amount,
                WorldGrid.HeightStepsPerCell - Terrain.SolidHeight);

        public void Normalize()
        {
            Terrain.Normalize();
            Water.Normalize();
            Road.Normalize();

            if (!Terrain.HasTerrain)
            {
                Road = default;
            }

            if (!Water.HasWater
                || WorldGrid.HeightStepsPerCell
                    - Terrain.SolidHeight <= 0)
            {
                Water = default;
            }
        }

        public readonly bool Equals(CellData other)
        {
            return Terrain.Equals(other.Terrain)
                && Water.Equals(other.Water)
                && Road.Equals(other.Road);
        }

        public override readonly bool Equals(object obj) => obj is CellData other && Equals(other);
        public override readonly int GetHashCode() => HashCode.Combine(
            Terrain,
            Water,
            Road);
    }

    [Serializable]
    public struct SurfaceHeightData
    {
        public int GroundHeight;
        public int WaterHeight;

        public readonly bool HasGround => GroundHeight > 0;
        public readonly bool HasWater => WaterHeight > 0;
        public readonly int GroundCellY => HasGround
            ? (GroundHeight - 1) / WorldGrid.HeightStepsPerCell
            : -1;
        public readonly int WaterCellY => HasWater
            ? (WaterHeight - 1) / WorldGrid.HeightStepsPerCell
            : -1;
    }

    [Serializable]
    public struct EnvironmentData
    {
        public BiomeType Biome;
        public byte Temperature;
        public byte Moisture;
        public byte Fertility;
    }

    public struct PathData
    {
        public ushort OpenHeight;
        public ushort WaterDistance;
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
