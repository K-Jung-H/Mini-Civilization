using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.WaterFlow
{
    internal static class WaterBodyResolver
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        internal static IReadOnlyList<WaterBody> Resolve(
            WorldData world,
            SurfaceCache surfaceCache)
        {
            var visited = new bool[world.Size * world.Size];
            var result = new List<WaterBody>();
            var queue = new Queue<(int x, int z)>();

            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var index = ToIndex(world, x, z);
                if (visited[index] || !surfaceCache.GetSurfaceHeight(x, z).HasWater)
                {
                    continue;
                }

                var body = new WaterBody(result.Count + 1);
                queue.Enqueue((x, z));
                visited[index] = true;

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    AddExposedColumn(world, surfaceCache, current.x, current.z, body);
                    body.SurfaceCellCount++;
                    body.TouchesWorldEdge |= current.x == 0
                        || current.z == 0
                        || current.x == world.Size - 1
                        || current.z == world.Size - 1;

                    for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                    {
                        var nextX = current.x + Directions[directionIndex].x;
                        var nextZ = current.z + Directions[directionIndex].z;
                        if (!world.ContainsColumn(nextX, nextZ))
                        {
                            continue;
                        }

                        var nextIndex = ToIndex(world, nextX, nextZ);
                        if (visited[nextIndex] || !surfaceCache.GetSurfaceHeight(nextX, nextZ).HasWater)
                        {
                            continue;
                        }

                        visited[nextIndex] = true;
                        queue.Enqueue((nextX, nextZ));
                    }
                }

                result.Add(body);
            }

            return result;
        }

        internal static void RefreshMetrics(
            WorldData world,
            SurfaceCache surfaceCache,
            WaterFlowState state,
            IReadOnlyCollection<int> changedColumnIndices,
            HashSet<int> affectedBodyIds)
        {
            affectedBodyIds.Clear();
            if (world == null
                || state == null
                || changedColumnIndices == null
                || changedColumnIndices.Count == 0)
            {
                return;
            }

            foreach (var columnIndex in changedColumnIndices)
            {
                WorldIndex.DecodeColumn(world, columnIndex, out var x, out var z);
                var bodyId = state.GetWaterBodyId(x, z);
                if (bodyId != 0)
                {
                    affectedBodyIds.Add(bodyId);
                }
            }

            foreach (var bodyId in affectedBodyIds)
            {
                if (!state.TryGetWaterBody(bodyId, out var body))
                {
                    continue;
                }

                var volumeUnits = 0;
                for (var cellIndex = 0; cellIndex < body.Cells.Count; cellIndex++)
                {
                    volumeUnits += CalculateExposedUnits(
                        world,
                        surfaceCache,
                        body.Cells[cellIndex]);
                }

                body.VolumeUnits = volumeUnits;
            }
        }

        private static void AddExposedColumn(
            WorldData world,
            SurfaceCache surfaceCache,
            int x,
            int z,
            WaterBody body)
        {
            var column = surfaceCache.GetSurfaceHeight(x, z);
            var solidTopUnits = column.GroundHeight;

            for (var y = 0; y <= column.WaterCellY; y++)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater)
                {
                    continue;
                }

                var exposedUnits = CalculateExposedUnits(
                    cell,
                    y,
                    solidTopUnits);
                if (exposedUnits <= 0)
                {
                    continue;
                }

                body.Add(new CellCoordinate(x, y, z));
                body.VolumeUnits += exposedUnits;
            }
        }

        private static int CalculateExposedUnits(
            WorldData world,
            SurfaceCache surfaceCache,
            CellCoordinate coordinate)
        {
            var cell = world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            return CalculateExposedUnits(
                cell,
                coordinate.Y,
                surfaceCache.GetSurfaceHeight(
                    coordinate.X,
                    coordinate.Z).GroundHeight);
        }

        private static int CalculateExposedUnits(
            CellData cell,
            int y,
            int solidTopUnits)
        {
            if (!cell.HasWater)
            {
                return 0;
            }

            var waterBottomUnits = y * WorldGrid.HeightStepsPerCell
                + cell.Terrain.SolidHeight;
            var waterTopUnits = waterBottomUnits + cell.WaterHeight;
            return System.Math.Max(
                0,
                waterTopUnits - System.Math.Max(
                    waterBottomUnits,
                    solidTopUnits));
        }

        private static int ToIndex(WorldData world, int x, int z) => x + world.Size * z;
    }
}
