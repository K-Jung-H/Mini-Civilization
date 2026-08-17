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

            var columns = new List<CellColumnCoordinate>();
            foreach (var chunk in world.EnumerateLoadedChunks())
            {
                var startX = chunk.Coordinate.X * world.ChunkSizeX;
                var startZ = chunk.Coordinate.Z * world.ChunkSizeZ;
                for (var localZ = 0; localZ < world.ChunkSizeZ; localZ++)
                for (var localX = 0; localX < world.ChunkSizeX; localX++)
                {
                    columns.Add(new CellColumnCoordinate(
                        startX + localX,
                        startZ + localZ));
                }
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
                if (!IsLoadedColumn(world, column.X, column.Z))
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
                    if (IsLoadedColumn(world, nextX, nextZ))
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
                var touchesUnloadedBoundary = false;
                var containsSea = false;
                while (queue.Count > 0)
                {
                    var column = queue.Dequeue();
                    component.Add(column);
                    touchesEdge |= !world.IsInfinite
                        && (column.X == world.MinimumCellX
                            || column.Z == world.MinimumCellZ
                            || column.X == world.MaximumCellXExclusive - 1
                            || column.Z == world.MaximumCellZExclusive - 1);
                    containsSea |= HasWaterType(
                        world,
                        column,
                        WaterType.Sea);

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

                        if (!world.IsChunkLoaded(nextX, nextZ))
                        {
                            touchesUnloadedBoundary = true;
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

                if (touchesUnloadedBoundary)
                {
                    continue;
                }

                var type = containsSea || touchesEdge
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

        private static bool HasWaterType(
            WorldData world,
            CellColumnCoordinate column,
            WaterType type)
        {
            for (var y = 0; y < world.Height; y++)
            {
                var water = world.GetCell(column.X, y, column.Z).Water;
                if (water.HasWater && water.Type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLoadedColumn(WorldData world, int x, int z) =>
            world.IsColumnLoaded(x, z);

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
