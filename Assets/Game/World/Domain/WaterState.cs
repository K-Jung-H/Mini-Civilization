using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    [Serializable]
    public sealed class WaterSourceGroupData
    {
        private readonly int[] cellIndices;

        public int Id { get; }
        public WaterType WaterType { get; }
        public IReadOnlyList<int> CellIndices => cellIndices;

        public WaterSourceGroupData(
            int id,
            WaterType waterType,
            int[] cellIndices)
        {
            Id = id;
            WaterType = waterType;
            this.cellIndices = cellIndices
                ?? throw new ArgumentNullException(nameof(cellIndices));
        }
    }

    /// <summary>
    /// Sparse persistent metadata for grouped water sources. Per-cell water
    /// state is stored exclusively in CellData.Water.
    /// </summary>
    public sealed class WaterSourceCollection
    {
        private static readonly (int x, int z)[] CardinalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        private readonly List<WaterSourceGroupData> groups = new();

        public IReadOnlyList<WaterSourceGroupData> Groups => groups;
        public bool IsInitialized { get; private set; }

        internal void ReplaceGroups(IEnumerable<WaterSourceGroupData> values)
        {
            groups.Clear();
            if (values != null)
            {
                foreach (var group in values)
                {
                    if (group != null)
                    {
                        groups.Add(group);
                    }
                }
            }

            IsInitialized = true;
        }

        internal void InitializeFromGeneratedWorld(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            groups.Clear();
            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater)
                {
                    continue;
                }

                cell.Water.Role = cell.Water.Type == WaterType.Sea
                    ? WaterCellRole.Reservoir
                    : (cell.Water.Flags & WaterCellFlags.River) != 0
                        ? WaterCellRole.Dynamic
                        : WaterCellRole.Reservoir;
                world.SetCellBulk(x, y, z, cell);
            }

            ClassifyRiverSources(world);
            IsInitialized = true;
        }

        internal void SynchronizeWithWorld(WorldData world)
        {
            for (var groupIndex = groups.Count - 1;
                 groupIndex >= 0;
                 groupIndex--)
            {
                var group = groups[groupIndex];
                var validCells = new List<int>(group.CellIndices.Count);
                for (var index = 0;
                     index < group.CellIndices.Count;
                     index++)
                {
                    var cellIndex = group.CellIndices[index];
                    var coordinate = WorldIndex.DecodeCell(world, cellIndex);
                    var cell = world.GetCell(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z);
                    if (cell.HasWater
                        && cell.Water.Role == WaterCellRole.Source)
                    {
                        validCells.Add(cellIndex);
                    }
                }

                if (validCells.Count == 0)
                {
                    groups.RemoveAt(groupIndex);
                    continue;
                }

                if (validCells.Count != group.CellIndices.Count)
                {
                    groups[groupIndex] = new WaterSourceGroupData(
                        group.Id,
                        group.WaterType,
                        validCells.ToArray());
                }
            }
        }

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
                var groupId = groups.Count + 1;
                for (var position = 0;
                     position < plateauColumns.Count;
                     position++)
                {
                    WorldIndex.DecodeColumn(
                        world,
                        plateauColumns[position],
                        out var sourceX,
                        out var sourceZ);
                    for (var y = 0; y < world.Height; y++)
                    {
                        var cell = world.GetCell(sourceX, y, sourceZ);
                        if (!cell.HasWater
                            || cell.Water.Role != WaterCellRole.Dynamic)
                        {
                            continue;
                        }

                        cell.Water.Role = WaterCellRole.Source;
                        cell.Water.Amount = WaterAmount.Full;
                        world.SetCellBulk(sourceX, y, sourceZ, cell);
                        sourceCells.Add(WorldIndex.EncodeCell(
                            world,
                            sourceX,
                            y,
                            sourceZ));
                    }
                }

                if (sourceCells.Count > 0)
                {
                    groups.Add(new WaterSourceGroupData(
                        groupId,
                        WaterType.Fresh,
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
            return (cell.Water.Flags & WaterCellFlags.River) != 0;
        }
    }
}
