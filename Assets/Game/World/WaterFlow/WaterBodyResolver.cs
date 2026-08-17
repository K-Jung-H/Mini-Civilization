using System;
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
            SurfaceCache surfaceCache) =>
            Resolve(world, surfaceCache, null);

        internal static IReadOnlyList<WaterBody> ResolvePrepared(
            WorldRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            var preparedChunks = new List<ChunkCoordinate>();
            foreach (var pair in runtime.ChunkRuntimes)
            {
                if (runtime.IsChunkPrepared(pair.Key))
                {
                    preparedChunks.Add(pair.Key);
                }
            }

            preparedChunks.Sort();
            return Resolve(
                runtime.Data,
                runtime.SurfaceCache,
                preparedChunks);
        }

        private static IReadOnlyList<WaterBody> Resolve(
            WorldData world,
            SurfaceCache surfaceCache,
            IReadOnlyList<ChunkCoordinate> preparedChunks)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (surfaceCache == null)
            {
                throw new ArgumentNullException(nameof(surfaceCache));
            }

            var visited = new HashSet<CellColumnCoordinate>();
            var dryColumns = new HashSet<CellColumnCoordinate>();
            var wetSurfaceHeights =
                new Dictionary<CellColumnCoordinate, SurfaceHeightData>();
            var includedChunks = preparedChunks == null
                ? null
                : new HashSet<ChunkCoordinate>(preparedChunks);
            var result = new List<WaterBody>();
            var queue = new Queue<(int x, int z)>();

            if (preparedChunks == null)
            {
                foreach (var chunk in world.EnumerateLoadedChunks())
                {
                    var startX = chunk.Coordinate.X * world.ChunkSizeX;
                    var startZ = chunk.Coordinate.Z * world.ChunkSizeZ;
                    for (var localZ = 0; localZ < world.ChunkSizeZ; localZ++)
                    for (var localX = 0; localX < world.ChunkSizeX; localX++)
                    {
                        ResolveFromColumn(
                            startX + localX,
                            startZ + localZ);
                    }
                }
            }
            else
            {
                for (var chunkIndex = 0;
                     chunkIndex < preparedChunks.Count;
                     chunkIndex++)
                {
                    var chunk = preparedChunks[chunkIndex];
                    var startX = chunk.X * world.ChunkSizeX;
                    var startZ = chunk.Z * world.ChunkSizeZ;
                    var endX = startX + world.ChunkSizeX;
                    var endZ = startZ + world.ChunkSizeZ;
                    for (var z = startZ; z < endZ; z++)
                    for (var x = startX; x < endX; x++)
                    {
                        ResolveFromColumn(x, z);
                    }
                }
            }

            return result;

            void ResolveFromColumn(int x, int z)
            {
                var column = new CellColumnCoordinate(x, z);
                if (visited.Contains(column)
                    || !TryGetWaterSurface(column, out _))
                {
                    return;
                }

                var body = new WaterBody(result.Count + 1);
                queue.Enqueue((x, z));
                visited.Add(column);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var currentColumn = new CellColumnCoordinate(
                        current.x,
                        current.z);
                    AddExposedColumn(
                        world,
                        wetSurfaceHeights[currentColumn],
                        current.x,
                        current.z,
                        body);
                    body.SurfaceCellCount++;
                    body.TouchesWorldEdge |= !world.IsInfinite
                        && (current.x == world.MinimumCellX
                            || current.z == world.MinimumCellZ
                            || current.x == world.MaximumCellXExclusive - 1
                            || current.z == world.MaximumCellZExclusive - 1);

                    for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                    {
                        var nextX = current.x + Directions[directionIndex].x;
                        var nextZ = current.z + Directions[directionIndex].z;
                        if (!world.IsColumnLoaded(nextX, nextZ))
                        {
                            continue;
                        }

                        if (includedChunks != null
                            && !includedChunks.Contains(
                                WorldCoordinateUtility.ToChunk(
                                    nextX,
                                    nextZ,
                                    world.ChunkSizeX)))
                        {
                            continue;
                        }

                        var nextColumn = new CellColumnCoordinate(nextX, nextZ);
                        if (visited.Contains(nextColumn)
                            || !TryGetWaterSurface(nextColumn, out _))
                        {
                            continue;
                        }

                        visited.Add(nextColumn);
                        queue.Enqueue((nextX, nextZ));
                    }
                }

                result.Add(body);
            }

            bool TryGetWaterSurface(
                CellColumnCoordinate coordinate,
                out SurfaceHeightData height)
            {
                if (wetSurfaceHeights.TryGetValue(coordinate, out height))
                {
                    return true;
                }

                if (dryColumns.Contains(coordinate))
                {
                    height = default;
                    return false;
                }

                height = surfaceCache.GetSurfaceHeight(
                    coordinate.X,
                    coordinate.Z);
                if (height.HasWater)
                {
                    wetSurfaceHeights.Add(coordinate, height);
                    return true;
                }

                dryColumns.Add(coordinate);
                return false;
            }
        }

        internal static void RefreshMetrics(
            WorldData world,
            SurfaceCache surfaceCache,
            WaterFlowState state,
            IReadOnlyCollection<CellColumnCoordinate> changedColumns,
            HashSet<int> affectedBodyIds)
        {
            affectedBodyIds.Clear();
            if (world == null
                || state == null
                || changedColumns == null
                || changedColumns.Count == 0)
            {
                return;
            }

            foreach (var column in changedColumns)
            {
                var bodyId = state.GetWaterBodyId(column.X, column.Z);
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
            SurfaceHeightData column,
            int x,
            int z,
            WaterBody body)
        {
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

    }
}
