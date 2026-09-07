using System;
using System.Collections.Generic;
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

            var terrainBuilder = new TerrainPatternTileBuilder(
                configuration.PatternTiles,
                configuration.Terrain);
            var terrainMap = new TerrainPatternMapReader(
                configuration.PatternTiles,
                runtime.PatternMaps,
                terrainBuilder);
            var hydrologyDrawer = new HydrologyPatternDrawer(
                configuration.PatternTiles,
                configuration.Hydrology,
                terrainMap,
                runtime.PatternMaps.WaterBrushes);
            mapScheduler = new PatternMapPreparationScheduler(
                runtime.PatternMaps,
                terrainBuilder,
                hydrologyDrawer,
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
            var count = Math.Min(unloadQueue.Count, configuration.ChunkUnloadPerFrame);
            for (var index = 0; index < count; index++)
            {
                var coordinate = unloadQueue[0];
                persistence?.SaveAndDetachChunk(coordinate);
                runtime.ReleaseChunk(coordinate, unloadWorldData: true);
                unloadQueue.RemoveAt(0);
            }
        }

        private void ProcessPreparations()
        {
            var completed = 0;
            for (var index = 0; index < prepareQueue.Count
                 && completed < configuration.ChunkPreparePerFrame; index++)
            {
                var coordinate = prepareQueue[index];
                IReadOnlyList<CellCoordinate> sources = Array.Empty<CellCoordinate>();
                var loaded = runtime.Data.IsChunkLoaded(coordinate)
                    || persistence?.TryLoadChunk(coordinate) == true;
                if (!loaded)
                {
                    if (!runtime.PatternMaps.TryGetPair(
                            configuration.PatternTiles.GetKeyForChunk(coordinate), out var tile))
                        continue;
                    runtime.BeginChunkPreparation(coordinate);
                    sources = materializer.Materialize(runtime.Data, coordinate, tile).SourceCells;
                    persistence?.MarkDirty(coordinate);
                }
                else
                {
                    runtime.BeginChunkPreparation(coordinate);
                }

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

        private void ProcessActivations()
        {
            var count = Math.Min(activateQueue.Count, configuration.ChunkActivatePerFrame);
            for (var index = 0; index < count; index++)
            {
                var coordinate = activateQueue[0];
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
