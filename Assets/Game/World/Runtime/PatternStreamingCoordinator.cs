using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.Persistence;

namespace MiniCivilization.World.Runtime
{
    internal static class ChunkDemand
    {

        public static List<ChunkCoordinate> Build(
            WorldData world,
            ChunkCoordinate target,
            int radiusChunks)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (radiusChunks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusChunks));
            }

            var minimumX = checked(target.X - radiusChunks);
            var maximumX = checked(target.X + radiusChunks);
            var minimumZ = checked(target.Z - radiusChunks);
            var maximumZ = checked(target.Z + radiusChunks);
            if (!world.IsInfinite)
            {
                minimumX = Math.Max(minimumX, world.MinimumChunkX);
                maximumX = Math.Min(maximumX, world.MaximumChunkX);
                minimumZ = Math.Max(minimumZ, world.MinimumChunkZ);
                maximumZ = Math.Min(maximumZ, world.MaximumChunkZ);
            }

            if (minimumX > maximumX || minimumZ > maximumZ)
            {
                return new List<ChunkCoordinate>();
            }

            var result = new List<ChunkCoordinate>(checked(
                (maximumX - minimumX + 1)
                * (maximumZ - minimumZ + 1)));
            for (var z = minimumZ; z <= maximumZ; z++)
            for (var x = minimumX; x <= maximumX; x++)
            {
                result.Add(new ChunkCoordinate(x, z));
            }

            result.Sort((left, right) => ComparePriority(left, right, target));
            return result;
        }

        internal static int ComparePriority(
            ChunkCoordinate left,
            ChunkCoordinate right,
            ChunkCoordinate target)
        {
            var leftDistance = SquareDistance(left, target);
            var rightDistance = SquareDistance(right, target);
            var distance = leftDistance.CompareTo(rightDistance);
            return distance != 0 ? distance : left.CompareTo(right);
        }

        private static long SquareDistance(
            ChunkCoordinate coordinate,
            ChunkCoordinate target)
        {
            var x = (long)coordinate.X - target.X;
            var z = (long)coordinate.Z - target.Z;
            return checked(x * x + z * z);
        }
    }

    internal sealed class PatternStreamingCoordinator : IDisposable
    {
#if ENABLE_PROFILER
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage0 = new("World.Streaming.Update");
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage1 = new("World.Streaming.Unload");
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage2 = new("World.Streaming.Cells");
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage3 = new("World.Streaming.Activate");
#endif

        private readonly WorldRuntime runtime;
        private readonly WorldGenerationConfiguration configuration;
        private readonly PatternMapPreparationScheduler mapScheduler;
        private readonly PatternChunkMaterializer materializer;
        private readonly WorldPersistenceService persistence;
        private readonly HashSet<ChunkCoordinate> renderChunks = new();
        private readonly HashSet<ChunkCoordinate> updateChunks = new();
        private readonly List<ChunkCoordinate> prepareQueue = new();
        private readonly List<ChunkCoordinate> activateQueue = new();
        private readonly List<ChunkCoordinate> unloadQueue = new();
        private bool hasTarget;
        private ChunkCoordinate target;
        private bool disposed;
        private readonly Dictionary<ChunkCoordinate, Task<GeneratedChunk>> cellBuilds = new();
        private readonly HashSet<ChunkCoordinate> missingSavedChunks = new();
        private readonly Dictionary<ChunkCoordinate, Task<Chunk>> loadBuilds = new();
        private sealed class GeneratedChunk
        {
            internal Chunk Chunk;
            internal IReadOnlyList<CellCoordinate> Sources;
        }

        private void StartCellBuild(ChunkCoordinate coordinate, PatternTilePair tile)
        {
            var settings = configuration.World;
            cellBuilds.Add(coordinate, Task.Run(() =>
            {
                // The worker owns this WorldData; the live world's dictionaries are never shared.
                var isolated = new WorldData(settings);
                var result = materializer.Materialize(isolated, coordinate, tile);
                isolated.TryGetChunk(coordinate, out var chunk);
                return new GeneratedChunk { Chunk = chunk, Sources = result.SourceCells };
            }));
        }

        public PatternStreamingCoordinator(
            WorldRuntime runtime,
            WorldGenerationConfiguration configuration,
            WorldPersistenceService persistence = null)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.configuration = configuration
                ?? throw new ArgumentNullException(nameof(configuration));
            if (!ReferenceEquals(runtime.Data.Settings, configuration.World))
            {
                throw new ArgumentException(
                    "Streaming Runtime and World Generation Settings disagree.",
                    nameof(configuration));
            }

            var elevationMap = new ElevationPatternMapReader(
                configuration.PatternTiles, runtime.PatternMaps, configuration.Elevation);
            var climateMap = new ClimatePatternMapReader(
                configuration.PatternTiles, runtime.PatternMaps, elevationMap, configuration.Climate);
            var terrainBuilder = new TerrainPatternTileBuilder(
                configuration.PatternTiles, configuration.Terrain, elevationMap, climateMap);
            var terrainMap = new TerrainPatternMapReader(
                configuration.PatternTiles, runtime.PatternMaps, terrainBuilder);
            var hydrologyDrawer = new HydrologyPatternDrawer(
                configuration.PatternTiles, configuration.Hydrology,
                terrainMap, runtime.PatternMaps.WaterBrushes, climateMap, elevationMap);
            mapScheduler = new PatternMapPreparationScheduler(
                runtime.PatternMaps, terrainBuilder, hydrologyDrawer,
                configuration.MapBuildConcurrency);
            materializer = new PatternChunkMaterializer(
                configuration.PatternTiles);
            this.persistence = persistence;
        }

        public WorldStreamingProgress Progress { get; private set; }

        public event Action<WorldStreamingProgress> ProgressChanged;

        public void SetDebuggerPrepareDemand(PatternTileBounds bounds)
        {
            ThrowIfDisposed();
            var tiles = new List<PatternTileKey>();
            foreach (var key in configuration.PatternTiles
                         .EnumerateOutputIntersecting(bounds))
            {
                tiles.Add(key);
            }

            var anchor = configuration.PatternTiles.GetKeyForCell(
                checked(bounds.MinimumX + bounds.Width / 2),
                checked(bounds.MinimumZ + bounds.Height / 2));
            mapScheduler.SetDebuggerDemand(tiles, anchor);
        }

        public void ClearDebuggerPrepareDemand()
        {
            ThrowIfDisposed();
            mapScheduler.SetDebuggerDemand(
                Array.Empty<PatternTileKey>(),
                default);
        }

        public void Update(ChunkCoordinate nextTarget)
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage0.Auto();
#endif
            ThrowIfDisposed();
            runtime.BeginStreamingChanges();
            try
            {
                if (!hasTarget || !target.Equals(nextTarget))
                {
                    target = nextTarget;
                    hasTarget = true;
                    UpdateDemand();
                }

                mapScheduler.Update();
                CollectAbandonedCellBuilds();
                ProcessUnloads();
                ProcessPreparations();
                ProcessActivations();
            }
            finally
            {
                runtime.EndStreamingChanges();
            }
            persistence?.FlushDetachedChunks();
            PublishProgress();
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            mapScheduler.Dispose();
            foreach (var task in cellBuilds.Values)
                _ = task.ContinueWith(failed => { _ = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            cellBuilds.Clear();
            foreach (var task in loadBuilds.Values)
                _ = task.ContinueWith(failed => { _ = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            loadBuilds.Clear();
            missingSavedChunks.Clear();
            renderChunks.Clear();
            updateChunks.Clear();
            prepareQueue.Clear();
            activateQueue.Clear();
            unloadQueue.Clear();
            PublishProgress();
        }

        private void UpdateDemand()
        {
            var orderedRender = ChunkDemand.Build(
                runtime.Data,
                target,
                configuration.RenderRangeChunks);
            var nextRender = new HashSet<ChunkCoordinate>(orderedRender);
            unloadQueue.Clear();
            foreach (var pair in runtime.ChunkRuntimes)
            {
                if (!nextRender.Contains(pair.Key))
                {
                    runtime.SetChunkSimulationEnabled(pair.Key, false);
                    unloadQueue.Add(pair.Key);
                }
            }
            unloadQueue.Sort((left, right) => ChunkDemand.ComparePriority(right, left, target));
            var orderedUpdate = ChunkDemand.Build(
                runtime.Data,
                target,
                configuration.UpdateRangeChunks);
            var orderedPrepare = ChunkDemand.Build(
                runtime.Data,
                target,
                configuration.PrepareRangeChunks);
            var prepareTiles = new List<PatternTileKey>();
            var seenTiles = new HashSet<PatternTileKey>();
            for (var index = 0; index < orderedPrepare.Count; index++)
            {
                var key = configuration.PatternTiles.GetKeyForChunk(
                    orderedPrepare[index]);
                if (seenTiles.Add(key))
                {
                    prepareTiles.Add(key);
                }
            }

            mapScheduler.SetStreamingDemand(
                prepareTiles,
                configuration.PatternTiles.GetKeyForChunk(target));
            renderChunks.Clear();
            renderChunks.UnionWith(nextRender);
            updateChunks.Clear();
            updateChunks.UnionWith(orderedUpdate);
            prepareQueue.Clear();
            activateQueue.Clear();
            foreach (var coordinate in orderedRender)
            {
                var state = runtime.GetChunkState(coordinate);
                if (state == ChunkState.Ready) activateQueue.Add(coordinate);
                else if (state != ChunkState.Active) prepareQueue.Add(coordinate);
            }

            UpdateSimulationRange();
        }

        private void ProcessUnloads()
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage1.Auto();
#endif
            var count = Math.Min(unloadQueue.Count, configuration.ChunkUnloadPerFrame);
            for (var index = 0; index < count; index++)
            {
                if (persistence != null && !persistence.CanAcceptWrites) break;
                var coordinate = unloadQueue[0];
                persistence?.SaveAndDetachChunk(coordinate);
                runtime.ReleaseChunk(coordinate, unloadWorldData: true);
                unloadQueue.RemoveAt(0);
            }
        }

        private void ProcessPreparations()
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage2.Auto();
#endif
            var completed = 0;
            var probes = 0;
            for (var index = 0; index < prepareQueue.Count
                 && index < configuration.MapBuildConcurrency
                 && completed < configuration.ChunkPreparePerFrame; index++)
            {
                var coordinate = prepareQueue[index];
                IReadOnlyList<CellCoordinate> sources = Array.Empty<CellCoordinate>();
                cellBuilds.TryGetValue(coordinate, out var build);
                var loaded = runtime.Data.IsChunkLoaded(coordinate);
                if (!loaded && build == null && !missingSavedChunks.Contains(coordinate))
                {
                    if (!loadBuilds.TryGetValue(coordinate, out var load))
                    {
                        if (cellBuilds.Count + loadBuilds.Count >= configuration.MapBuildConcurrency) continue;
                        if (probes++ >= configuration.ChunkPreparePerFrame) break;
                        load = persistence?.LoadChunkAsync(coordinate) ?? Task.FromResult<Chunk>(null);
                        loadBuilds.Add(coordinate, load);
                    }
                    if (!load.IsCompleted || index != 0) continue;
                    var saved = load.GetAwaiter().GetResult();
                    loadBuilds.Remove(coordinate);
                    if (saved == null) missingSavedChunks.Add(coordinate);
                    else
                    {
                        runtime.Data.AttachGeneratedChunk(saved);
                        loaded = true;
                    }
                }
                if (!loaded)
                {
                    if (build != null)
                    {
                        if (!build.IsCompleted || index != 0) continue;
                        var result = build.GetAwaiter().GetResult();
                        cellBuilds.Remove(coordinate);
                        runtime.BeginChunkPreparation(coordinate);
                        runtime.Data.AttachGeneratedChunk(result.Chunk);
                        sources = result.Sources;
                        persistence?.MarkDirty(coordinate);
                        missingSavedChunks.Remove(coordinate);
                    }
                    else
                    {
                        if (cellBuilds.Count + loadBuilds.Count >= configuration.MapBuildConcurrency) continue;
                        if (!runtime.PatternMaps.TryGetPair(
                                configuration.PatternTiles.GetKeyForChunk(coordinate), out var tile))
                            continue;
                        StartCellBuild(coordinate, tile);
                        continue;
                    }
                }
                else
                {
                    if (index != 0) continue;
                    runtime.BeginChunkPreparation(coordinate);
                }

                if (index != 0) continue;
                runtime.CompleteChunkPreparation(coordinate, sources);
                if (loaded) persistence?.RestoreWaterFrontier(coordinate);
                prepareQueue.RemoveAt(index--);
                activateQueue.Add(coordinate);
                completed++;
            }
            if (completed > 0)
            {
                persistence?.RestoreAvailableEntities();
                activateQueue.Sort((left, right) => ChunkDemand.ComparePriority(left, right, target));
            }
        }

        private void CollectAbandonedCellBuilds()
        {
            abandonedCellBuilds.Clear();
            foreach (var pair in cellBuilds)
                if ((prepareQueue.IndexOf(pair.Key) < 0 || prepareQueue.IndexOf(pair.Key) >= configuration.MapBuildConcurrency) && pair.Value.IsCompleted)
                {
                    _ = pair.Value.Exception;
                    abandonedCellBuilds.Add(pair.Key);
                }
            foreach (var coordinate in abandonedCellBuilds) cellBuilds.Remove(coordinate);
            abandonedCellBuilds.Clear();
            foreach (var pair in loadBuilds)
                if ((prepareQueue.IndexOf(pair.Key) < 0 || prepareQueue.IndexOf(pair.Key) >= configuration.MapBuildConcurrency) && pair.Value.IsCompleted)
                { _ = pair.Value.Exception; abandonedCellBuilds.Add(pair.Key); }
            foreach (var coordinate in abandonedCellBuilds) loadBuilds.Remove(coordinate);
            missingSavedChunks.IntersectWith(renderChunks);
        }

        private readonly List<ChunkCoordinate> abandonedCellBuilds = new();

        private void ProcessActivations()
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage3.Auto();
#endif
            var count = Math.Min(activateQueue.Count, configuration.ChunkActivatePerFrame);
            for (var index = 0; index < count; index++)
            {
                var coordinate = activateQueue[0];
                if (prepareQueue.Count > 0 && ChunkDemand.ComparePriority(prepareQueue[0], coordinate, target) < 0) break;
                runtime.ActivateChunk(coordinate);
                runtime.SetChunkSimulationEnabled(coordinate, updateChunks.Contains(coordinate));
                activateQueue.RemoveAt(0);
            }
        }
        private void UpdateSimulationRange()
        {
            foreach (var coordinate in renderChunks)
            {
                if (runtime.GetChunkState(coordinate) == ChunkState.Active)
                {
                    runtime.SetChunkSimulationEnabled(
                        coordinate,
                        updateChunks.Contains(coordinate));
                }
            }
        }

        private void PublishProgress()
        {
            var next = new WorldStreamingProgress(
                renderChunks.Count - prepareQueue.Count - activateQueue.Count,
                renderChunks.Count);
            if (next.Equals(Progress))
            {
                return;
            }

            Progress = next;
            ProgressChanged?.Invoke(next);
        }
        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(PatternStreamingCoordinator));
            }
        }
    }
}
