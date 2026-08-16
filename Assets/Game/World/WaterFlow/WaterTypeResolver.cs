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

            var columns = new List<CellColumnCoordinate>(
                checked(world.Size * world.Size));
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                columns.Add(new CellColumnCoordinate(x, z));
            }

            RefreshChanged(world, columns, null, null, null, null);
        }

        public static void RefreshChanged(
            WorldData world,
            IEnumerable<CellColumnCoordinate> changedColumns,
            ISet<CellCoordinate> changedCells,
            ISet<CellCoordinate> renderCells,
            ISet<CellCoordinate> typeChangedCells,
            ISet<CellColumnCoordinate> classifiedColumns)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (changedColumns == null)
            {
                return;
            }

            var seeds = new HashSet<CellColumnCoordinate>();
            foreach (var column in changedColumns)
            {
                if (!world.ContainsColumn(column.X, column.Z))
                {
                    continue;
                }

                seeds.Add(column);
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var nextX = column.X + direction.x;
                    var nextZ = column.Z + direction.z;
                    if (world.ContainsColumn(nextX, nextZ))
                    {
                        seeds.Add(new CellColumnCoordinate(nextX, nextZ));
                    }
                }
            }

            var visited = new HashSet<CellColumnCoordinate>();
            var queue = new Queue<CellColumnCoordinate>();
            var component = new List<CellColumnCoordinate>();
            foreach (var seed in seeds)
            {
                if (visited.Contains(seed))
                {
                    continue;
                }

                if (!HasClassifiableWater(world, seed))
                {
                    visited.Add(seed);
                    continue;
                }

                component.Clear();
                queue.Enqueue(seed);
                visited.Add(seed);
                var touchesEdge = false;
                while (queue.Count > 0)
                {
                    var column = queue.Dequeue();
                    component.Add(column);
                    touchesEdge |= column.X == 0
                        || column.Z == 0
                        || column.X == world.Size - 1
                        || column.Z == world.Size - 1;

                    for (var directionIndex = 0;
                         directionIndex < Directions.Length;
                         directionIndex++)
                    {
                        var direction = Directions[directionIndex];
                        var nextX = column.X + direction.x;
                        var nextZ = column.Z + direction.z;
                        if (!world.ContainsColumn(nextX, nextZ))
                        {
                            continue;
                        }

                        var nextColumn = new CellColumnCoordinate(nextX, nextZ);
                        if (visited.Contains(nextColumn)
                            || !HasClassifiableWater(world, nextColumn))
                        {
                            continue;
                        }

                        visited.Add(nextColumn);
                        queue.Enqueue(nextColumn);
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
            CellColumnCoordinate column)
        {
            for (var y = 0; y < world.Height; y++)
            {
                var water = world.GetCell(column.X, y, column.Z).Water;
                if (water.HasWater && water.Type != WaterType.River)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyType(
            WorldData world,
            CellColumnCoordinate column,
            WaterType type,
            ISet<CellCoordinate> changedCells,
            ISet<CellCoordinate> renderCells,
            ISet<CellCoordinate> typeChangedCells,
            ISet<CellColumnCoordinate> classifiedColumns)
        {
            var columnChanged = false;
            for (var y = 0; y < world.Height; y++)
            {
                var cell = world.GetCell(column.X, y, column.Z);
                if (!cell.HasWater
                    || cell.Water.Type == WaterType.River
                    || cell.Water.Type == type)
                {
                    continue;
                }

                cell.Water.Type = type;
                world.SetCellForEdit(column.X, y, column.Z, cell);
                var coordinate = new CellCoordinate(column.X, y, column.Z);
                changedCells?.Add(coordinate);
                renderCells?.Add(coordinate);
                typeChangedCells?.Add(coordinate);
                columnChanged = true;
            }

            if (columnChanged)
            {
                classifiedColumns?.Add(column);
            }
        }
    }
}
