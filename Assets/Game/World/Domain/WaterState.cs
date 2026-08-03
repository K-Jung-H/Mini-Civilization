using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    [Serializable]
    public sealed class WaterFlowScheduleData
    {
        private int[] frontierCellIndices = Array.Empty<int>();

        public IReadOnlyList<int> FrontierCellIndices => frontierCellIndices;
        public bool HasPendingFlow => frontierCellIndices.Length > 0;

        internal void ReplaceFrontier(IReadOnlyCollection<int> values)
        {
            if (values == null || values.Count == 0)
            {
                frontierCellIndices = Array.Empty<int>();
                return;
            }

            frontierCellIndices = new int[values.Count];
            var index = 0;
            foreach (var value in values)
            {
                frontierCellIndices[index++] = value;
            }
        }
    }

    [Serializable]
    public sealed class WaterSourceGroupData
    {
        private readonly int[] cellIndices;

        public int Id { get; }
        public IReadOnlyList<int> CellIndices => cellIndices;

        public WaterSourceGroupData(int id, int[] cellIndices)
        {
            Id = id;
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
        private static readonly (int x, int y, int z)[] NeighborDirections =
        {
            (1, 0, 0), (-1, 0, 0),
            (0, 1, 0), (0, -1, 0),
            (0, 0, 1), (0, 0, -1)
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

            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater)
                {
                    continue;
                }

                if (cell.Water.Role == WaterCellRole.None)
                {
                    cell.Water.Role = WaterCellRole.Source;
                    world.SetCellBulk(x, y, z, cell);
                }
            }

            RebuildGroups(world);
        }

        internal void SynchronizeWithWorld(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            RebuildGroups(world);
        }

        private void RebuildGroups(WorldData world)
        {
            groups.Clear();
            var visited = new bool[checked(
                world.Size * world.Size * world.Height)];
            var queue = new Queue<int>();
            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var startIndex = WorldIndex.EncodeCell(world, x, y, z);
                if (visited[startIndex]
                    || !IsSource(world.GetCell(x, y, z)))
                {
                    continue;
                }

                var sourceCells = new List<int>();
                queue.Enqueue(startIndex);
                visited[startIndex] = true;
                while (queue.Count > 0)
                {
                    var cellIndex = queue.Dequeue();
                    sourceCells.Add(cellIndex);
                    var coordinate = WorldIndex.DecodeCell(world, cellIndex);
                    for (var directionIndex = 0;
                         directionIndex < NeighborDirections.Length;
                         directionIndex++)
                    {
                        var direction = NeighborDirections[directionIndex];
                        var nextX = coordinate.X + direction.x;
                        var nextY = coordinate.Y + direction.y;
                        var nextZ = coordinate.Z + direction.z;
                        if (!world.Contains(nextX, nextY, nextZ))
                        {
                            continue;
                        }

                        var nextIndex = WorldIndex.EncodeCell(
                            world,
                            nextX,
                            nextY,
                            nextZ);
                        if (visited[nextIndex]
                            || !IsSource(world.GetCell(nextX, nextY, nextZ)))
                        {
                            continue;
                        }

                        visited[nextIndex] = true;
                        queue.Enqueue(nextIndex);
                    }
                }

                groups.Add(new WaterSourceGroupData(
                    groups.Count + 1,
                    sourceCells.ToArray()));
            }

            IsInitialized = true;
        }

        private static bool IsSource(CellData cell) =>
            cell.HasWater && cell.Water.Role == WaterCellRole.Source;
    }
}
