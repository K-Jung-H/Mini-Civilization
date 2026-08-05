using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    internal static class WaterTypeResolver
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        public static void RefreshAll(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var columns = new int[checked(world.Size * world.Size)];
            for (var index = 0; index < columns.Length; index++)
            {
                columns[index] = index;
            }

            RefreshChanged(world, columns, null, null, null, null);
        }

        public static void RefreshChanged(
            WorldData world,
            IEnumerable<int> changedColumns,
            ISet<int> changedCells,
            ISet<int> renderCells,
            ISet<int> typeChangedCells,
            ISet<int> classifiedColumns)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (changedColumns == null)
            {
                return;
            }

            var seeds = new HashSet<int>();
            foreach (var columnIndex in changedColumns)
            {
                if ((uint)columnIndex >= (uint)(world.Size * world.Size))
                {
                    continue;
                }

                seeds.Add(columnIndex);
                var x = columnIndex % world.Size;
                var z = columnIndex / world.Size;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var nextX = x + direction.x;
                    var nextZ = z + direction.z;
                    if (world.ContainsColumn(nextX, nextZ))
                    {
                        seeds.Add(nextX + world.Size * nextZ);
                    }
                }
            }

            var visited = new bool[checked(world.Size * world.Size)];
            var queue = new Queue<int>();
            var component = new List<int>();
            foreach (var seed in seeds)
            {
                if (visited[seed])
                {
                    continue;
                }

                if (!HasClassifiableWater(world, seed))
                {
                    visited[seed] = true;
                    continue;
                }

                component.Clear();
                queue.Enqueue(seed);
                visited[seed] = true;
                var touchesEdge = false;
                while (queue.Count > 0)
                {
                    var columnIndex = queue.Dequeue();
                    component.Add(columnIndex);
                    var x = columnIndex % world.Size;
                    var z = columnIndex / world.Size;
                    touchesEdge |= x == 0
                        || z == 0
                        || x == world.Size - 1
                        || z == world.Size - 1;

                    for (var directionIndex = 0;
                         directionIndex < Directions.Length;
                         directionIndex++)
                    {
                        var direction = Directions[directionIndex];
                        var nextX = x + direction.x;
                        var nextZ = z + direction.z;
                        if (!world.ContainsColumn(nextX, nextZ))
                        {
                            continue;
                        }

                        var nextIndex = nextX + world.Size * nextZ;
                        if (visited[nextIndex]
                            || !HasClassifiableWater(world, nextIndex))
                        {
                            continue;
                        }

                        visited[nextIndex] = true;
                        queue.Enqueue(nextIndex);
                    }
                }

                var type = touchesEdge
                    ? WaterType.Sea
                    : component.Count <= world.PondMaximumArea
                        ? WaterType.Pond
                        : WaterType.Lake;
                for (var componentIndex = 0;
                     componentIndex < component.Count;
                     componentIndex++)
                {
                    ApplyType(
                        world,
                        component[componentIndex],
                        type,
                        changedCells,
                        renderCells,
                        typeChangedCells,
                        classifiedColumns);
                }
            }
        }

        private static bool HasClassifiableWater(
            WorldData world,
            int columnIndex)
        {
            var x = columnIndex % world.Size;
            var z = columnIndex / world.Size;
            for (var y = 0; y < world.Height; y++)
            {
                var water = world.GetCell(x, y, z).Water;
                if (water.HasWater && water.Type != WaterType.River)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyType(
            WorldData world,
            int columnIndex,
            WaterType type,
            ISet<int> changedCells,
            ISet<int> renderCells,
            ISet<int> typeChangedCells,
            ISet<int> classifiedColumns)
        {
            var x = columnIndex % world.Size;
            var z = columnIndex / world.Size;
            var columnChanged = false;
            for (var y = 0; y < world.Height; y++)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater
                    || cell.Water.Type == WaterType.River
                    || cell.Water.Type == type)
                {
                    continue;
                }

                cell.Water.Type = type;
                world.SetCellForEdit(x, y, z, cell);
                var cellIndex = WorldIndex.EncodeCell(world, x, y, z);
                changedCells?.Add(cellIndex);
                renderCells?.Add(cellIndex);
                typeChangedCells?.Add(cellIndex);
                columnChanged = true;
            }

            if (columnChanged)
            {
                classifiedColumns?.Add(columnIndex);
            }
        }
    }
}
