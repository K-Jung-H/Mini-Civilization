using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.WaterFlow
{
    internal static class WaterBodyResolver
    {
        internal sealed class StreamingScratch
        {
            internal readonly HashSet<int> Affected = new();
            internal readonly HashSet<CellColumnCoordinate> Seeds = new();
            internal readonly HashSet<CellColumnCoordinate> Visited = new();
            internal readonly Queue<CellColumnCoordinate> Queue = new();
            internal readonly List<WaterBody> Bodies = new();
        }

        internal static void RefreshStreaming(WorldRuntime runtime,
            IReadOnlyCollection<ChunkCoordinate> changed, StreamingScratch scratch)
        {
            if (runtime.WaterFlowState.TopologyGraph is not ChunkGraph graph)
            {
                graph = new ChunkGraph();
                runtime.WaterFlowState.TopologyGraph = graph;
                var all = new List<ChunkCoordinate>();
                foreach (var entry in runtime.ChunkRuntimes)
                    if (runtime.IsChunkPrepared(entry.Key)) all.Add(entry.Key);
                graph.Update(runtime, all);
            }
            else graph.Update(runtime, changed);
        }

        // Nodes are connected components within one chunk. Revisit only affected bodies,
        // never the cells of unchanged chunks, including when a large ocean splits.
        internal sealed class ChunkGraph
        {
            private sealed class Node
            {
                internal WaterBody Part;
                internal readonly HashSet<Node> Edges = new();
                internal WaterBody Body;
            }
            private readonly Dictionary<ChunkCoordinate, List<Node>> chunks = new();
            private readonly Dictionary<CellColumnCoordinate, Node> columns = new();
            private readonly HashSet<Node> seen = new();
            private readonly Dictionary<int, HashSet<Node>> members = new();
            private readonly HashSet<int> affected = new();
            private readonly HashSet<Node> seeds = new();
            private readonly List<Node> group = new();
            private readonly List<WaterBody> partsBuffer = new();
            private readonly List<WaterBody> additions = new();
            private readonly Queue<Node> queue = new();
            private int nextId = 1;
            private readonly Func<int, int, int> lookup;
            internal ChunkGraph() { lookup = Lookup; }
            private int Lookup(int x, int z) => columns.TryGetValue(new CellColumnCoordinate(x, z), out var node) ? node.Body.Id : 0;

            internal void Update(WorldRuntime runtime, IReadOnlyCollection<ChunkCoordinate> changed)
            {
                if (changed.Count == 0) return;
                var world = runtime.Data;
                affected.Clear(); seeds.Clear(); additions.Clear();
                foreach (var chunk in changed)
                {
                    if (chunks.Remove(chunk, out var old))
                        foreach (var node in old)
                        {
                            if (node.Body != null)
                            {
                                affected.Add(node.Body.Id);
                                members[node.Body.Id].Remove(node);
                            }
                            foreach (var other in node.Edges) other.Edges.Remove(node);
                            foreach (var cell in node.Part.Cells) columns.Remove(new CellColumnCoordinate(cell.X, cell.Z));
                        }
                    if (!runtime.IsChunkPrepared(chunk)) continue;
                    var parts = Resolve(world, runtime.SurfaceCache, new[] { chunk });
                    var nodes = new List<Node>(parts.Count);
                    foreach (var part in parts)
                    {
                        var node = new Node { Part = part };
                        nodes.Add(node);
                        seeds.Add(node);
                        foreach (var cell in part.Cells) columns[new CellColumnCoordinate(cell.X, cell.Z)] = node;
                    }
                    chunks.Add(chunk, nodes);
                }
                foreach (var chunk in changed)
                {
                    if (!chunks.TryGetValue(chunk, out var nodes)) continue;
                    var sx = chunk.X * world.ChunkSizeX;
                    var sz = chunk.Z * world.ChunkSizeZ;
                    for (var z = sz; z < sz + world.ChunkSizeZ; z++)
                    { Connect(sx, z, sx - 1, z); Connect(sx + world.ChunkSizeX - 1, z, sx + world.ChunkSizeX, z); }
                    for (var x = sx; x < sx + world.ChunkSizeX; x++)
                    { Connect(x, sz, x, sz - 1); Connect(x, sz + world.ChunkSizeZ - 1, x, sz + world.ChunkSizeZ); }
                }
                // Only components touched by removal or a new edge can split or merge.
                foreach (var id in affected)
                    if (members.Remove(id, out var previous)) seeds.UnionWith(previous);
                seen.Clear();
                foreach (var root in seeds)
                {
                    if (!seen.Add(root)) continue;
                    queue.Enqueue(root);
                    group.Clear(); partsBuffer.Clear();
                    while (queue.Count > 0)
                    {
                        var node = queue.Dequeue(); group.Add(node); partsBuffer.Add(node.Part);
                        foreach (var edge in node.Edges) if (seen.Add(edge)) queue.Enqueue(edge);
                    }
                    var body = new WaterBody(nextId++, partsBuffer.ToArray());
                    var membership = new HashSet<Node>(group);
                    foreach (var node in group) node.Body = body;
                    members.Add(body.Id, membership);
                    additions.Add(body);
                }
                runtime.WaterFlowState.UpdateGraphBodies(affected, additions, lookup);
                seeds.Clear(); seen.Clear(); group.Clear(); partsBuffer.Clear(); additions.Clear();
            }
            private void Connect(int x, int z, int nx, int nz)
            {
                if (!columns.TryGetValue(new CellColumnCoordinate(x, z), out var a)
                    || !columns.TryGetValue(new CellColumnCoordinate(nx, nz), out var b)) return;
                if (a.Body != null) affected.Add(a.Body.Id);
                if (b.Body != null) affected.Add(b.Body.Id);
                a.Edges.Add(b); b.Edges.Add(a);
            }
        }

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
