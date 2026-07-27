using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    public enum WaterCellBehavior : byte
    {
        None = 0,
        Source = 1,
        FlowDependent = 2,
        Reservoir = 3,
        FixedReservoir = 4
    }

    [Serializable]
    public sealed class WaterSourceGroupData
    {
        private readonly int[] cellIndices;

        public int Id { get; }
        public WaterType WaterType { get; }
        public short OutputSurfaceTenths { get; }
        public ushort SourceAmount { get; }
        public IReadOnlyList<int> CellIndices => cellIndices;

        public WaterSourceGroupData(
            int id,
            WaterType waterType,
            short outputSurfaceTenths,
            ushort sourceAmount,
            int[] cellIndices)
        {
            Id = id;
            WaterType = waterType;
            OutputSurfaceTenths = outputSurfaceTenths;
            SourceAmount = Math.Max((ushort)1, sourceAmount);
            this.cellIndices = cellIndices
                ?? throw new ArgumentNullException(nameof(cellIndices));
        }
    }

    public static class WaterAmountConversion
    {
        public const int UnitsPerAmount = 100;
        public const ushort DefaultMaximumAmount = UnitsPerAmount;
        public const float MinimumConfigurableAmount = 0.01f;
        public const float MaximumConfigurableAmount =
            ushort.MaxValue / (float)UnitsPerAmount;

        public static ushort ToUnits(float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount))
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            var clamped = Math.Clamp(
                amount,
                MinimumConfigurableAmount,
                MaximumConfigurableAmount);
            return checked((ushort)Math.Round(
                clamped * UnitsPerAmount,
                MidpointRounding.AwayFromZero));
        }

        public static float ToAmount(ushort units) =>
            units / (float)UnitsPerAmount;

        public static ushort FromRenderFill(
            byte renderFill,
            ushort maximumAmount)
        {
            if (renderFill == 0)
            {
                return 0;
            }

            var clampedFill = Math.Min(
                WorldGrid.HeightStepsPerCell,
                (int)renderFill);
            return checked((ushort)Math.Max(
                1,
                (clampedFill * (long)maximumAmount
                    + WorldGrid.HeightStepsPerCell / 2)
                / WorldGrid.HeightStepsPerCell));
        }

        public static byte ToRenderFill(
            ushort amount,
            ushort maximumAmount,
            int capacitySteps)
        {
            if (amount == 0 || capacitySteps <= 0)
            {
                return 0;
            }

            var normalizedSteps =
                (amount * (long)WorldGrid.HeightStepsPerCell
                    + maximumAmount - 1)
                / maximumAmount;
            return checked((byte)Math.Min(
                capacitySteps,
                Math.Max(1L, normalizedSteps)));
        }
    }

    /// <summary>
    /// Persistent, authoritative water data. Amount uses fixed 0.01 units;
    /// CellData.WaterFill remains the normalized 0.2-step render representation.
    /// </summary>
    public sealed class WaterState
    {
        private static readonly (int x, int z)[] CardinalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        private readonly ushort[] amountsByCell;
        private readonly WaterCellBehavior[] behaviorsByCell;
        private readonly int[] sourceGroupIdsByCell;
        private readonly List<WaterSourceGroupData> sourceGroups = new();
        private readonly Dictionary<int, WaterSourceGroupData> sourceGroupsById = new();

        public int CellCount => amountsByCell.Length;
        public ushort MaximumAmount { get; private set; } =
            WaterAmountConversion.DefaultMaximumAmount;
        public IReadOnlyList<WaterSourceGroupData> SourceGroups => sourceGroups;
        public bool IsInitialized { get; private set; }

        internal WaterState(int cellCount)
        {
            if (cellCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellCount));
            }

            amountsByCell = new ushort[cellCount];
            behaviorsByCell = new WaterCellBehavior[cellCount];
            sourceGroupIdsByCell = new int[cellCount];
        }

        public ushort GetAmount(int cellIndex) => amountsByCell[cellIndex];
        public WaterCellBehavior GetBehavior(int cellIndex) => behaviorsByCell[cellIndex];
        public int GetSourceGroupId(int cellIndex) => sourceGroupIdsByCell[cellIndex];

        internal ushort GetSourceAmount(int cellIndex)
        {
            var groupId = sourceGroupIdsByCell[cellIndex];
            if (sourceGroupsById.TryGetValue(groupId, out var group))
            {
                return group.SourceAmount;
            }

            return amountsByCell[cellIndex];
        }

        internal WaterType GetSourceWaterType(int cellIndex)
        {
            var groupId = sourceGroupIdsByCell[cellIndex];
            if (sourceGroupsById.TryGetValue(groupId, out var group))
            {
                return group.WaterType;
            }

            return WaterType.Fresh;
        }

        internal void SetCell(
            int cellIndex,
            ushort amount,
            WaterCellBehavior behavior,
            int sourceGroupId = 0)
        {
            amountsByCell[cellIndex] = Math.Min(MaximumAmount, amount);
            behaviorsByCell[cellIndex] = behavior;
            sourceGroupIdsByCell[cellIndex] = sourceGroupId;
        }

        internal void SetAmount(int cellIndex, ushort amount) =>
            amountsByCell[cellIndex] = Math.Min(MaximumAmount, amount);

        internal void ConfigureMaximumAmount(ushort maximumAmount)
        {
            maximumAmount = Math.Max((ushort)1, maximumAmount);
            if (maximumAmount == MaximumAmount)
            {
                return;
            }

            var previousMaximum = MaximumAmount;
            for (var index = 0; index < amountsByCell.Length; index++)
            {
                amountsByCell[index] = RescaleAmount(
                    amountsByCell[index],
                    previousMaximum,
                    maximumAmount);
            }

            if (sourceGroups.Count > 0)
            {
                var rescaledGroups = new WaterSourceGroupData[sourceGroups.Count];
                for (var index = 0; index < sourceGroups.Count; index++)
                {
                    var group = sourceGroups[index];
                    var cellIndices = new int[group.CellIndices.Count];
                    for (var cellIndex = 0;
                         cellIndex < group.CellIndices.Count;
                         cellIndex++)
                    {
                        cellIndices[cellIndex] = group.CellIndices[cellIndex];
                    }

                    rescaledGroups[index] = new WaterSourceGroupData(
                        group.Id,
                        group.WaterType,
                        group.OutputSurfaceTenths,
                        RescaleAmount(
                            group.SourceAmount,
                            previousMaximum,
                            maximumAmount),
                        cellIndices);
                }

                sourceGroups.Clear();
                sourceGroupsById.Clear();
                for (var index = 0; index < rescaledGroups.Length; index++)
                {
                    var group = rescaledGroups[index];
                    sourceGroups.Add(group);
                    sourceGroupsById[group.Id] = group;
                }
            }

            MaximumAmount = maximumAmount;
        }

        internal void SetBehavior(
            int cellIndex,
            WaterCellBehavior behavior,
            int sourceGroupId = 0)
        {
            behaviorsByCell[cellIndex] = behavior;
            sourceGroupIdsByCell[cellIndex] = sourceGroupId;
        }

        internal void ReplaceSourceGroups(IEnumerable<WaterSourceGroupData> groups)
        {
            sourceGroups.Clear();
            sourceGroupsById.Clear();
            if (groups != null)
            {
                foreach (var group in groups)
                {
                    if (group == null)
                    {
                        continue;
                    }

                    sourceGroups.Add(group);
                    sourceGroupsById[group.Id] = group;
                }
            }

            IsInitialized = true;
        }

        internal void InitializeFromGeneratedWorld(
            WorldData world,
            ushort maximumAmount = WaterAmountConversion.DefaultMaximumAmount)
        {
            MaximumAmount = Math.Max((ushort)1, maximumAmount);
            Array.Clear(amountsByCell, 0, amountsByCell.Length);
            Array.Clear(behaviorsByCell, 0, behaviorsByCell.Length);
            Array.Clear(sourceGroupIdsByCell, 0, sourceGroupIdsByCell.Length);
            sourceGroups.Clear();
            sourceGroupsById.Clear();

            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater)
                {
                    continue;
                }

                var behavior = cell.Water == WaterType.Sea
                    ? WaterCellBehavior.FixedReservoir
                    : (cell.Flags & CellFlags.River) != 0
                        ? WaterCellBehavior.FlowDependent
                        : WaterCellBehavior.Reservoir;
                SetCell(
                    WorldIndex.EncodeCell(world, x, y, z),
                    WaterAmountConversion.FromRenderFill(
                        cell.WaterFill,
                        MaximumAmount),
                    behavior);
            }

            ClassifyRiverSources(world);
            IsInitialized = true;
        }

        internal void MarkInitialized() => IsInitialized = true;

        private void ClassifyRiverSources(WorldData world)
        {
            var visited = new bool[world.Size * world.Size];
            var queue = new Queue<int>();
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var startIndex = WorldIndex.EncodeColumn(world, x, z);
                var column = world.GetSurfaceColumn(x, z);
                if (visited[startIndex]
                    || !IsRiverColumn(world, x, z, column))
                {
                    continue;
                }

                var plateauHeight = column.WaterTopUnits;
                var plateauColumns = new List<int>();
                var hasHigherRiverNeighbor = false;
                queue.Enqueue(startIndex);
                visited[startIndex] = true;
                while (queue.Count > 0)
                {
                    var plateauIndex = queue.Dequeue();
                    plateauColumns.Add(plateauIndex);
                    WorldIndex.DecodeColumn(
                        world,
                        plateauIndex,
                        out var plateauX,
                        out var plateauZ);
                    for (var directionIndex = 0;
                         directionIndex < CardinalDirections.Length;
                         directionIndex++)
                    {
                        var direction = CardinalDirections[directionIndex];
                        var nextX = plateauX + direction.x;
                        var nextZ = plateauZ + direction.z;
                        if (!world.ContainsColumn(nextX, nextZ))
                        {
                            continue;
                        }

                        var next = world.GetSurfaceColumn(nextX, nextZ);
                        if (!IsRiverColumn(world, nextX, nextZ, next))
                        {
                            continue;
                        }

                        if (next.WaterTopUnits > plateauHeight)
                        {
                            hasHigherRiverNeighbor = true;
                        }

                        var nextIndex = WorldIndex.EncodeColumn(
                            world,
                            nextX,
                            nextZ);
                        if (next.WaterTopUnits == plateauHeight
                            && !visited[nextIndex])
                        {
                            visited[nextIndex] = true;
                            queue.Enqueue(nextIndex);
                        }
                    }
                }

                if (hasHigherRiverNeighbor)
                {
                    continue;
                }

                var sourceCells = new List<int>();
                var groupId = sourceGroups.Count + 1;
                for (var columnIndexPosition = 0;
                     columnIndexPosition < plateauColumns.Count;
                     columnIndexPosition++)
                {
                    WorldIndex.DecodeColumn(
                        world,
                        plateauColumns[columnIndexPosition],
                        out var sourceX,
                        out var sourceZ);
                    for (var y = 0; y < world.Height; y++)
                    {
                        var cellIndex = WorldIndex.EncodeCell(
                            world,
                            sourceX,
                            y,
                            sourceZ);
                        if (amountsByCell[cellIndex] == 0
                            || behaviorsByCell[cellIndex] != WaterCellBehavior.FlowDependent)
                        {
                            continue;
                        }

                        behaviorsByCell[cellIndex] = WaterCellBehavior.Source;
                        sourceGroupIdsByCell[cellIndex] = groupId;
                        sourceCells.Add(cellIndex);
                    }
                }

                if (sourceCells.Count > 0)
                {
                    var sourceGroup = new WaterSourceGroupData(
                        groupId,
                        WaterType.Fresh,
                        checked((short)(plateauHeight * 2)),
                        MaximumAmount,
                        sourceCells.ToArray());
                    sourceGroups.Add(sourceGroup);
                    sourceGroupsById[sourceGroup.Id] = sourceGroup;
                }
            }
        }

        private static ushort RescaleAmount(
            ushort amount,
            ushort previousMaximum,
            ushort nextMaximum)
        {
            if (amount == 0)
            {
                return 0;
            }

            return checked((ushort)Math.Clamp(
                (amount * (long)nextMaximum + previousMaximum / 2)
                    / previousMaximum,
                1,
                nextMaximum));
        }

        private static bool IsRiverColumn(
            WorldData world,
            int x,
            int z,
            SurfaceColumnData column)
        {
            if (!column.HasWater)
            {
                return false;
            }

            var cell = world.GetCell(x, column.WaterCellY, z);
            return (cell.Flags & CellFlags.River) != 0;
        }
    }
}
