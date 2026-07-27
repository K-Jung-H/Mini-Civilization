using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    [Flags]
    public enum WaterIncomingDirectionMask : byte
    {
        None = 0,
        FromAbove = 1 << 0,
        FromEast = 1 << 1,
        FromNorth = 1 << 2,
        FromWest = 1 << 3,
        FromSouth = 1 << 4
    }

    [Flags]
    public enum WaterFlowHeadingMask : byte
    {
        None = 0,
        East = 1 << 0,
        North = 1 << 1,
        West = 1 << 2,
        South = 1 << 3
    }

    public enum WaterFlowMode : byte
    {
        None = 0,
        Surface = 1,
        Falling = 2,
        Flowing = 3
    }

    /// <summary>
    /// Runtime-only water amount cache. Flow cells do not belong to a source
    /// and do not retain parent/child routing relationships.
    /// </summary>
    public sealed class WaterFlowState
    {
        private readonly int worldSize;
        private readonly int worldHeight;
        private readonly int cellCount;
        private readonly ushort maximumAmount;
        private readonly int[] waterBodyIdsByColumn;
        private readonly Dictionary<int, WaterBody> waterBodiesById = new();
        private readonly ushort[] targetAmountsByCell;
        private readonly WaterType[] waterTypesByCell;
        private readonly WaterIncomingDirectionMask[] incomingDirectionsByCell;
        private readonly WaterFlowHeadingMask[] flowHeadingsByCell;
        private readonly WaterFlowMode[] flowModesByCell;
        private IReadOnlyList<WaterBody> waterBodies = Array.Empty<WaterBody>();
        private int resolvedCellCount;

        public IReadOnlyList<WaterBody> WaterBodies => waterBodies;
        public int ResolvedCellCount => resolvedCellCount;
        public bool IsRecalculating { get; internal set; }
        public bool IsStable => !IsRecalculating;

        internal int CellCount => cellCount;
        internal ushort MaximumAmount => maximumAmount;

        internal WaterFlowState(WorldData world, IReadOnlyList<WaterBody> bodies)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            worldSize = world.Size;
            worldHeight = world.Height;
            cellCount = checked(world.Size * world.Size * world.Height);
            maximumAmount = world.WaterState.MaximumAmount;
            waterBodyIdsByColumn = new int[checked(world.Size * world.Size)];
            targetAmountsByCell = new ushort[cellCount];
            waterTypesByCell = new WaterType[cellCount];
            incomingDirectionsByCell = new WaterIncomingDirectionMask[cellCount];
            flowHeadingsByCell = new WaterFlowHeadingMask[cellCount];
            flowModesByCell = new WaterFlowMode[cellCount];
            InitializeFromPersistent(world);
            ReplaceWaterBodies(bodies);
        }

        public int GetWaterBodyId(int x, int z)
        {
            if (!ContainsColumn(x, z))
            {
                return 0;
            }

            return waterBodyIdsByColumn[x + worldSize * z];
        }

        public bool TryGetWaterBody(int x, int z, out WaterBody waterBody)
        {
            var id = GetWaterBodyId(x, z);
            if (id == 0)
            {
                waterBody = null;
                return false;
            }

            return waterBodiesById.TryGetValue(id, out waterBody);
        }

        internal bool TryGetWaterBody(int id, out WaterBody waterBody) =>
            waterBodiesById.TryGetValue(id, out waterBody);

        public ushort GetTargetAmount(int x, int y, int z) =>
            ContainsCell(x, y, z)
                ? targetAmountsByCell[EncodeCell(x, y, z)]
                : (ushort)0;

        public WaterIncomingDirectionMask GetIncomingDirections(
            int x,
            int y,
            int z) =>
            ContainsCell(x, y, z)
                ? incomingDirectionsByCell[EncodeCell(x, y, z)]
                : WaterIncomingDirectionMask.None;

        public WaterFlowHeadingMask GetFlowHeading(int x, int y, int z) =>
            ContainsCell(x, y, z)
                ? flowHeadingsByCell[EncodeCell(x, y, z)]
                : WaterFlowHeadingMask.None;

        public WaterFlowMode GetFlowMode(int x, int y, int z) =>
            ContainsCell(x, y, z)
                ? flowModesByCell[EncodeCell(x, y, z)]
                : WaterFlowMode.None;

        internal ushort GetTargetAmount(int cellIndex) =>
            targetAmountsByCell[cellIndex];

        internal WaterType GetWaterType(int cellIndex) =>
            waterTypesByCell[cellIndex];

        internal WaterIncomingDirectionMask GetIncomingDirections(int cellIndex) =>
            incomingDirectionsByCell[cellIndex];

        internal WaterFlowHeadingMask GetFlowHeading(int cellIndex) =>
            flowHeadingsByCell[cellIndex];

        internal WaterFlowMode GetFlowMode(int cellIndex) =>
            flowModesByCell[cellIndex];

        internal bool SetResolvedCell(
            int cellIndex,
            ushort targetAmount,
            WaterType waterType,
            WaterIncomingDirectionMask incomingDirections,
            WaterFlowHeadingMask flowHeading,
            WaterFlowMode flowMode)
        {
            targetAmount = Math.Min(
                maximumAmount,
                targetAmount);
            if (targetAmount == 0)
            {
                waterType = WaterType.None;
                incomingDirections = WaterIncomingDirectionMask.None;
                flowHeading = WaterFlowHeadingMask.None;
                flowMode = WaterFlowMode.None;
            }

            if (targetAmountsByCell[cellIndex] == targetAmount
                && waterTypesByCell[cellIndex] == waterType
                && incomingDirectionsByCell[cellIndex] == incomingDirections
                && flowHeadingsByCell[cellIndex] == flowHeading
                && flowModesByCell[cellIndex] == flowMode)
            {
                return false;
            }

            if (targetAmountsByCell[cellIndex] == 0 && targetAmount > 0)
            {
                resolvedCellCount++;
            }
            else if (targetAmountsByCell[cellIndex] > 0 && targetAmount == 0)
            {
                resolvedCellCount--;
            }

            targetAmountsByCell[cellIndex] = targetAmount;
            waterTypesByCell[cellIndex] = targetAmount > 0
                ? waterType
                : WaterType.None;
            incomingDirectionsByCell[cellIndex] = incomingDirections;
            flowHeadingsByCell[cellIndex] = flowHeading;
            flowModesByCell[cellIndex] = flowMode;
            return true;
        }

        internal void SynchronizeFromPersistent(WorldData world, int cellIndex)
        {
            var amount = world.WaterState.GetAmount(cellIndex);
            var coordinate = WorldIndex.DecodeCell(world, cellIndex);
            var cell = world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            var waterType = amount > 0
                ? cell.Water != WaterType.None
                    ? cell.Water
                    : world.WaterState.GetSourceWaterType(cellIndex)
                : WaterType.None;
            SetResolvedCell(
                cellIndex,
                amount,
                waterType,
                WaterIncomingDirectionMask.None,
                WaterFlowHeadingMask.None,
                amount > 0 ? WaterFlowMode.Surface : WaterFlowMode.None);
        }

        internal void ReplaceWaterBodies(IReadOnlyList<WaterBody> bodies)
        {
            waterBodies = bodies ?? Array.Empty<WaterBody>();
            Array.Clear(waterBodyIdsByColumn, 0, waterBodyIdsByColumn.Length);
            waterBodiesById.Clear();
            for (var bodyIndex = 0; bodyIndex < waterBodies.Count; bodyIndex++)
            {
                var body = waterBodies[bodyIndex];
                waterBodiesById[body.Id] = body;
                for (var cellIndex = 0; cellIndex < body.Cells.Count; cellIndex++)
                {
                    var cell = body.Cells[cellIndex];
                    waterBodyIdsByColumn[cell.X + worldSize * cell.Z] = body.Id;
                }
            }
        }

        private void InitializeFromPersistent(WorldData world)
        {
            for (var cellIndex = 0; cellIndex < cellCount; cellIndex++)
            {
                var amount = world.WaterState.GetAmount(cellIndex);
                if (amount == 0)
                {
                    continue;
                }

                var coordinate = WorldIndex.DecodeCell(world, cellIndex);
                var cell = world.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                targetAmountsByCell[cellIndex] = amount;
                waterTypesByCell[cellIndex] = cell.Water != WaterType.None
                    ? cell.Water
                    : world.WaterState.GetSourceWaterType(cellIndex);
                flowModesByCell[cellIndex] =
                    (cell.Flags & CellFlags.FallingWater) != 0
                        ? WaterFlowMode.Falling
                        : WaterFlowMode.Surface;
                resolvedCellCount++;
            }
        }

        private int EncodeCell(int x, int y, int z) =>
            x + worldSize * (z + worldSize * y);

        private bool ContainsColumn(int x, int z) =>
            (uint)x < worldSize && (uint)z < worldSize;

        private bool ContainsCell(int x, int y, int z) =>
            (uint)x < worldSize
            && (uint)y < worldHeight
            && (uint)z < worldSize;
    }
}
