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
        public byte EmissionPerTick { get; }
        public IReadOnlyList<int> CellIndices => cellIndices;

        public WaterSourceGroupData(
            int id,
            WaterType waterType,
            short outputSurfaceTenths,
            byte emissionPerTick,
            int[] cellIndices)
        {
            Id = id;
            WaterType = waterType;
            OutputSurfaceTenths = outputSurfaceTenths;
            EmissionPerTick = emissionPerTick;
            this.cellIndices = cellIndices
                ?? throw new ArgumentNullException(nameof(cellIndices));
        }
    }

    /// <summary>
    /// Persistent, authoritative water data. Amount uses tenths of one Cell;
    /// CellData.WaterFill remains the 0.2-step render representation.
    /// </summary>
    public sealed class WaterState
    {
        public const byte MaximumAmount = 10;
        public const byte MinimumVisibleAmount = 2;

        private static readonly (int x, int z)[] CardinalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        private readonly byte[] amountsByCell;
        private readonly WaterCellBehavior[] behaviorsByCell;
        private readonly int[] sourceGroupIdsByCell;
        private readonly List<WaterSourceGroupData> sourceGroups = new();

        public int CellCount => amountsByCell.Length;
        public IReadOnlyList<WaterSourceGroupData> SourceGroups => sourceGroups;
        public bool IsInitialized { get; private set; }

        internal WaterState(int cellCount)
        {
            if (cellCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellCount));
            }

            amountsByCell = new byte[cellCount];
            behaviorsByCell = new WaterCellBehavior[cellCount];
            sourceGroupIdsByCell = new int[cellCount];
        }

        public byte GetAmount(int cellIndex) => amountsByCell[cellIndex];
        public WaterCellBehavior GetBehavior(int cellIndex) => behaviorsByCell[cellIndex];
        public int GetSourceGroupId(int cellIndex) => sourceGroupIdsByCell[cellIndex];

        internal void SetCell(
            int cellIndex,
            byte amount,
            WaterCellBehavior behavior,
            int sourceGroupId = 0)
        {
            amountsByCell[cellIndex] = (byte)Math.Min(MaximumAmount, amount);
            behaviorsByCell[cellIndex] = behavior;
            sourceGroupIdsByCell[cellIndex] = sourceGroupId;
        }

        internal void SetAmount(int cellIndex, byte amount) =>
            amountsByCell[cellIndex] = (byte)Math.Min(MaximumAmount, amount);

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
            if (groups != null)
            {
                sourceGroups.AddRange(groups);
            }

            IsInitialized = true;
        }

        internal void InitializeFromGeneratedWorld(WorldData world)
        {
            Array.Clear(amountsByCell, 0, amountsByCell.Length);
            Array.Clear(behaviorsByCell, 0, behaviorsByCell.Length);
            Array.Clear(sourceGroupIdsByCell, 0, sourceGroupIdsByCell.Length);
            sourceGroups.Clear();

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
                    (byte)(cell.WaterFill * 2),
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
                    sourceGroups.Add(new WaterSourceGroupData(
                        groupId,
                        WaterType.Fresh,
                        checked((short)(plateauHeight * 2)),
                        1,
                        sourceCells.ToArray()));
                }
            }
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
