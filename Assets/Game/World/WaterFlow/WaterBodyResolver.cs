using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    internal static class WaterBodyResolver
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        internal static IReadOnlyList<WaterBody> Resolve(WorldData world, int pondMaximumVolumeUnits = 80)
        {
            var visited = new bool[world.Size * world.Size];
            var result = new List<WaterBody>();
            var queue = new Queue<(int x, int z)>();

            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var index = ToIndex(world, x, z);
                if (visited[index] || !world.GetSurfaceColumn(x, z).HasWater)
                {
                    continue;
                }

                var body = new WaterBody(result.Count + 1);
                queue.Enqueue((x, z));
                visited[index] = true;

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    AddExposedColumn(world, current.x, current.z, body);
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
                        if (visited[nextIndex] || !world.GetSurfaceColumn(nextX, nextZ).HasWater)
                        {
                            continue;
                        }

                        // Adjacent surface-water columns belong to one body even if their
                        // quantized heights create a visual waterfall between cell layers.
                        visited[nextIndex] = true;
                        queue.Enqueue((nextX, nextZ));
                    }
                }

                body.Type = ResolveType(body, pondMaximumVolumeUnits);
                result.Add(body);
            }

            return result;
        }

        internal static void RefreshMetrics(
            WorldData world,
            WaterFlowState state,
            IReadOnlyCollection<int> changedColumnIndices,
            HashSet<int> affectedBodyIds,
            int pondMaximumVolumeUnits = 80)
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
                        body.Cells[cellIndex]);
                }

                body.VolumeUnits = volumeUnits;
                body.Type = ResolveType(body, pondMaximumVolumeUnits);
            }
        }

        private static void AddExposedColumn(WorldData world, int x, int z, WaterBody body)
        {
            var column = world.GetSurfaceColumn(x, z);
            var solidTopUnits = column.SolidTopUnits;

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
            CellCoordinate coordinate)
        {
            var cell = world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            return CalculateExposedUnits(
                cell,
                coordinate.Y,
                world.GetSurfaceColumn(
                    coordinate.X,
                    coordinate.Z).SolidTopUnits);
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
                + cell.SolidFill;
            var waterTopUnits = waterBottomUnits + cell.WaterFill;
            return System.Math.Max(
                0,
                waterTopUnits - System.Math.Max(
                    waterBottomUnits,
                    solidTopUnits));
        }

        private static WaterBodyType ResolveType(
            WaterBody body,
            int pondMaximumVolumeUnits) =>
            body.TouchesWorldEdge
                ? WaterBodyType.Sea
                : body.VolumeUnits <= pondMaximumVolumeUnits
                    ? WaterBodyType.Pond
                    : WaterBodyType.Lake;

        private static int ToIndex(WorldData world, int x, int z) => x + world.Size * z;
    }
}
