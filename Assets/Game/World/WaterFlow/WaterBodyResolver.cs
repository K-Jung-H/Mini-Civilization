using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.WaterFlow
{
    internal static class WaterBodyResolver
    {
        internal static void RefreshStreaming(WorldRuntime runtime,
            IReadOnlyCollection<ChunkCoordinate> changed)
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

        internal static void Advance(WorldRuntime runtime)
        {
            if (runtime.WaterFlowState?.TopologyGraph is ChunkGraph graph) graph.Advance(runtime);
        }

        internal static void RefreshChunkMetrics(WorldRuntime runtime, HashSet<ChunkCoordinate> changed, HashSet<ChunkCoordinate> rebuilt)
        {

            if (runtime.WaterFlowState.TopologyGraph is ChunkGraph graph)
                graph.RefreshChunkMetrics(runtime, changed, rebuilt);
        }

        internal static void RefreshAmountMetrics(WorldRuntime runtime,
            IReadOnlyDictionary<CellCoordinate, WaterData> previousWater, HashSet<ChunkCoordinate> rebuilt)
        {

            if (runtime.WaterFlowState.TopologyGraph is ChunkGraph graph)
                graph.RefreshAmountMetrics(runtime, previousWater, rebuilt);
        }

        // Working connectivity is private; lookups keep the preceding epoch until commit.
        internal sealed class ChunkGraph
        {
            private sealed class Node
            {
                internal WaterBody Part;
                internal readonly HashSet<Node> Edges = new();
                internal WaterBody Body;
                internal WaterBody PreviousBody;
                internal long Epoch;
            }
            private sealed class ColumnEntry
            {
                internal Node Current;
                internal Node Previous;
                internal long Epoch;
            }

            private readonly Dictionary<ChunkCoordinate, List<Node>> chunks = new();
            private readonly Dictionary<CellColumnCoordinate, ColumnEntry> columns = new();
            private readonly Dictionary<int, HashSet<Node>> members = new();
            private readonly HashSet<ChunkCoordinate> pending = new();
            private readonly HashSet<ChunkCoordinate> metricPending = new();
            private readonly Func<int, int, int> lookup;
            private IEnumerator<int> work;
            private long committedEpoch;
            private int nextId = 1;

            internal ChunkGraph() { lookup = Lookup; }

            private int Lookup(int x, int z)
            {
                if (!columns.TryGetValue(new CellColumnCoordinate(x, z), out var entry)) return 0;
                var node = entry.Epoch > committedEpoch ? entry.Previous : entry.Current;
                if (node == null) return 0;
                var body = node.Epoch > committedEpoch ? node.PreviousBody : node.Body;
                return body?.Id ?? 0;
            }

            internal void Update(WorldRuntime runtime, IReadOnlyCollection<ChunkCoordinate> changed)
            {
                foreach (var coordinate in changed) pending.Add(coordinate);
            }

            internal void Advance(WorldRuntime runtime)
            {
                if (work == null)
                {
                    if (pending.Count > 0)
                    {
                        var batch = new List<ChunkCoordinate>(pending);
                        batch.Sort();
                        pending.Clear();
                        work = Repair(runtime, batch).GetEnumerator();
                    }
                    else if (metricPending.Count > 0)
                    {
                        var batch = new List<ChunkCoordinate>(metricPending);
                        metricPending.Clear();
                        work = RefreshMetrics(runtime, batch).GetEnumerator();
                    }
                    else return;
                }

                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (var count = 0; count < 256; count++)
                {
                    if (!work.MoveNext())
                    {
                        work.Dispose();
                        work = null;
                        break;
                    }
                    if ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d /
                        System.Diagnostics.Stopwatch.Frequency >= 1d) break;
                }
            }

            private IEnumerable<int> Repair(WorldRuntime runtime, List<ChunkCoordinate> changed)
            {
                var world = runtime.Data;
                var epoch = committedEpoch + 1;
                var affected = new HashSet<int>();
                var newNodes = new List<Node>();
                var touchedColumns = new HashSet<CellColumnCoordinate>();
                var affinity = new Dictionary<Node, HashSet<int>>();
                var maySplit = false;

                foreach (var coordinate in changed)
                {
                    var replacements = new List<Node>();
                    var localColumns = new Dictionary<CellColumnCoordinate, Node>();
                    if (runtime.IsChunkPrepared(coordinate))
                    {
                        // Local cell resolution is bounded to one chunk.
                        foreach (var part in Resolve(world, runtime.SurfaceCache, new[] { coordinate }))
                        {
                            var node = new Node { Part = part };
                            replacements.Add(node);
                            newNodes.Add(node);
                            foreach (var cell in part.Cells)
                            {
                                localColumns[new CellColumnCoordinate(cell.X, cell.Z)] = node;
                                yield return 0;
                            }
                        }
                    }

                    if (chunks.Remove(coordinate, out var previous))
                    {
                        foreach (var node in previous)
                        {
                            var bodyId = node.Body.Id;
                            affected.Add(bodyId);
                            members[bodyId].Remove(node);
                            Node destination = null;
                            var preserved = true;
                            foreach (var cell in node.Part.Cells)
                            {
                                var column = new CellColumnCoordinate(cell.X, cell.Z);
                                if (!localColumns.TryGetValue(column, out var next)) preserved = false;
                                else if (destination == null) destination = next;
                                else if (destination != next) preserved = false;
                                SetColumn(column, null, epoch);
                                touchedColumns.Add(column);
                                yield return 0;
                            }
                            if (!preserved || destination == null) maySplit = true;
                            else
                            {
                                if (!affinity.TryGetValue(destination, out var ids))
                                    affinity.Add(destination, ids = new HashSet<int>());
                                ids.Add(bodyId);
                            }
                            foreach (var edge in node.Edges)
                            {
                                edge.Edges.Remove(node);
                                yield return 0;
                            }
                        }
                    }
                    chunks[coordinate] = replacements;
                    foreach (var pair in localColumns)
                    {
                        SetColumn(pair.Key, pair.Value, epoch);
                        touchedColumns.Add(pair.Key);
                        yield return 0;
                    }
                }

                foreach (var coordinate in changed)
                {
                    var sx = coordinate.X * world.ChunkSizeX;
                    var sz = coordinate.Z * world.ChunkSizeZ;
                    for (var z = sz; z < sz + world.ChunkSizeZ; z++)
                    {
                        Connect(sx, z, sx - 1, z, affected);
                        Connect(sx + world.ChunkSizeX - 1, z, sx + world.ChunkSizeX, z, affected);
                        yield return 0;
                    }
                    for (var x = sx; x < sx + world.ChunkSizeX; x++)
                    {
                        Connect(x, sz, x, sz - 1, affected);
                        Connect(x, sz + world.ChunkSizeZ - 1, x, sz + world.ChunkSizeZ, affected);
                        yield return 0;
                    }
                }

                var candidates = new HashSet<Node>();
                foreach (var node in newNodes) { candidates.Add(node); yield return 0; }
                foreach (var id in affected)
                    if (members.Remove(id, out var previous))
                        foreach (var node in previous) { candidates.Add(node); yield return 0; }

                var groups = new Dictionary<Node, List<Node>>();
                if (!maySplit)
                {
                    // Addition-only changes preserve old components: union their representatives.
                    // No BFS over the unchanged ocean's edges is required.
                    var parents = new Dictionary<Node, Node>();
                    var sizes = new Dictionary<Node, int>();
                    var representatives = new Dictionary<int, Node>();
                    foreach (var node in candidates)
                    {
                        parents.Add(node, node);
                        sizes.Add(node, 1);
                        yield return 0;
                    }
                    foreach (var node in candidates)
                    {
                        if (node.Body != null) JoinBody(node, node.Body.Id);
                        if (affinity.TryGetValue(node, out var ids))
                            foreach (var id in ids) JoinBody(node, id);
                        yield return 0;
                    }
                    foreach (var node in newNodes)
                        foreach (var edge in node.Edges) { Union(node, edge); yield return 0; }
                    foreach (var node in candidates)
                    {
                        var root = Find(node);
                        if (!groups.TryGetValue(root, out var group)) groups.Add(root, group = new List<Node>());
                        group.Add(node);
                        yield return 0;
                    }

                    Node Find(Node node)
                    {
                        var root = node;
                        while (parents[root] != root) root = parents[root];
                        while (parents[node] != node)
                        {
                            var next = parents[node]; parents[node] = root; node = next;
                        }
                        return root;
                    }
                    void Union(Node a, Node b)
                    {
                        a = Find(a); b = Find(b);
                        if (a == b) return;
                        if (sizes[a] < sizes[b]) { var swap = a; a = b; b = swap; }
                        parents[b] = a; sizes[a] += sizes[b];
                    }
                    void JoinBody(Node node, int id)
                    {
                        if (representatives.TryGetValue(id, out var representative)) Union(node, representative);
                        else representatives.Add(id, node);
                    }
                }
                else
                {
                    var seen = new HashSet<Node>();
                    var queue = new Queue<Node>();
                    foreach (var root in candidates)
                    {
                        if (!seen.Add(root)) { yield return 0; continue; }
                        var group = new List<Node>();
                        groups.Add(root, group);
                        queue.Enqueue(root);
                        while (queue.Count > 0)
                        {
                            var node = queue.Dequeue();
                            group.Add(node);
                            foreach (var edge in node.Edges)
                            {
                                if (seen.Add(edge)) queue.Enqueue(edge);
                                yield return 0;
                            }
                            yield return 0;
                        }
                    }
                }

                var additions = new List<WaterBody>();
                foreach (var group in groups.Values)
                {
                    var body = new WaterBody(nextId++);
                    var membership = new HashSet<Node>();
                    foreach (var node in group)
                    {
                        node.PreviousBody = node.Body;
                        node.Body = body;
                        node.Epoch = epoch;
                        membership.Add(node);
                        body.AddPart(node.Part);
                        yield return 0;
                    }
                    members.Add(body.Id, membership);
                    additions.Add(body);
                }

                // Both the body index and lookup epoch become visible in this non-yielding commit.
                runtime.WaterFlowState.UpdateGraphBodies(affected, additions, lookup);
                committedEpoch = epoch;
                foreach (var column in touchedColumns)
                {
                    var entry = columns[column];
                    entry.Previous = null;
                    if (entry.Current == null) columns.Remove(column);
                    yield return 0;
                }
                foreach (var node in candidates) { node.PreviousBody = null; yield return 0; }
                foreach (var coordinate in changed) { metricPending.Add(coordinate); yield return 0; }
            }

            private void SetColumn(CellColumnCoordinate column, Node node, long epoch)
            {
                if (!columns.TryGetValue(column, out var entry))
                    columns.Add(column, entry = new ColumnEntry());
                if (entry.Epoch != epoch) entry.Previous = entry.Current;
                entry.Current = node;
                entry.Epoch = epoch;
            }

            private void Connect(int x, int z, int nx, int nz, HashSet<int> affected)
            {
                if (!columns.TryGetValue(new CellColumnCoordinate(x, z), out var aEntry)
                    || !columns.TryGetValue(new CellColumnCoordinate(nx, nz), out var bEntry)) return;
                var a = aEntry.Current; var b = bEntry.Current;
                if (a == null || b == null) return;
                if (a.Body != null) affected.Add(a.Body.Id);
                if (b.Body != null) affected.Add(b.Body.Id);
                a.Edges.Add(b); b.Edges.Add(a);
            }

            internal void RefreshChunkMetrics(WorldRuntime runtime, HashSet<ChunkCoordinate> changed, HashSet<ChunkCoordinate> rebuilt)
            {
                foreach (var coordinate in changed) metricPending.Add(coordinate);
            }

            internal void RefreshAmountMetrics(WorldRuntime runtime,
                IReadOnlyDictionary<CellCoordinate, WaterData> previousWater, HashSet<ChunkCoordinate> rebuilt)
            {
                foreach (var pair in previousWater)
                {
                    var coordinate = pair.Key;
                    var chunk = WorldCoordinateUtility.ToChunk(coordinate.X, coordinate.Z, runtime.Data.ChunkSizeX);
                    if (work != null || pending.Count > 0)
                    {
                        metricPending.Add(chunk);
                        continue;
                    }
                    if (rebuilt.Contains(chunk) ||
                        !columns.TryGetValue(new CellColumnCoordinate(coordinate.X, coordinate.Z), out var entry)
                        || entry.Current == null) continue;
                    var node = entry.Current;
                    var current = runtime.Data.GetCell(coordinate.X, coordinate.Y, coordinate.Z);
                    var previous = current; previous.Water = pair.Value;
                    var ground = runtime.SurfaceCache.GetSurfaceHeight(coordinate.X, coordinate.Z).GroundHeight;
                    var delta = CalculateExposedUnits(current, coordinate.Y, ground) - CalculateExposedUnits(previous, coordinate.Y, ground);
                    node.Part.VolumeUnits += delta;
                    node.Body.VolumeUnits += delta;
                }
            }

            private IEnumerable<int> RefreshMetrics(WorldRuntime runtime, List<ChunkCoordinate> changed)
            {
                foreach (var coordinate in changed)
                {
                    if (!chunks.TryGetValue(coordinate, out var nodes)) continue;
                    foreach (var node in nodes)
                    {
                        if (!runtime.Data.TryGetChunk(coordinate, out var source)) continue;
                        var revision = source.CellRevision;
                        var volume = 0;
                        foreach (var cell in node.Part.Cells)
                        {
                            volume += CalculateExposedUnits(runtime.Data, runtime.SurfaceCache, cell);
                            yield return 0;
                        }
                        if (!runtime.Data.TryGetChunk(coordinate, out var current)
                            || !ReferenceEquals(source, current) || current.CellRevision != revision)
                        {
                            metricPending.Add(coordinate);
                            continue;
                        }
                        node.Body.VolumeUnits += volume - node.Part.VolumeUnits;
                        node.Part.VolumeUnits = volume;
                        yield return 0;
                    }
                }
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
