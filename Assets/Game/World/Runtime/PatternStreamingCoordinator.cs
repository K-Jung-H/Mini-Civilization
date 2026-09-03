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

        private static int ComparePriority(
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
        private readonly List<ChunkCoordinate> orderedRenderChunks = new();
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
            var hydrologyBuilder = new HydrologyPatternTileBuilder(
                configuration.PatternTiles,
                configuration.Hydrology,
                terrainMap,
                runtime.PatternMaps.WaterBrushes);
            mapScheduler = new PatternMapPreparationScheduler(
                runtime.PatternMaps,
                terrainBuilder,
                hydrologyBuilder,
                configuration.MaximumConcurrentTileBuilds);
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
            if (!hasTarget || !target.Equals(nextTarget))
            {
                target = nextTarget;
                hasTarget = true;
                UpdateDemand();
            }

            mapScheduler.Update();
            ProcessCompletedChunks();
            UpdateSimulationRange();
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
            orderedRenderChunks.Clear();
            PublishProgress();
        }

        private void UpdateDemand()
        {
            var orderedRender = ChunkDemand.Build(
                runtime.Data,
                target,
                configuration.RenderRangeChunks);
            var nextRender = new HashSet<ChunkCoordinate>(orderedRender);
            foreach (var coordinate in renderChunks)
            {
                if (!nextRender.Contains(coordinate))
                {
                    persistence?.SaveAndDetachChunk(coordinate);
                    runtime.ReleaseChunk(
                        coordinate,
                        unloadWorldData: persistence != null);
                }
            }

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
            orderedRenderChunks.Clear();
            orderedRenderChunks.AddRange(orderedRender);
        }

        private void ProcessCompletedChunks()
        {
            var completed = 0;
            for (var index = 0;
                 index < orderedRenderChunks.Count
                 && completed < configuration.ChunkMaterializationsPerFrame;
                 index++)
            {
                var coordinate = orderedRenderChunks[index];
                if (!renderChunks.Contains(coordinate)
                    || runtime.GetChunkState(coordinate) == ChunkState.Active)
                {
                    continue;
                }

                if (runtime.GetChunkState(coordinate) == ChunkState.Unloaded)
                {
                    ChunkMaterializationResult result;
                    if (runtime.Data.IsChunkLoaded(coordinate))
                    {
                        result = new ChunkMaterializationResult(
                            coordinate,
                            Array.Empty<CellCoordinate>());
                    }
                    else if (persistence?.TryLoadChunk(coordinate) == true)
                    {
                        result = new ChunkMaterializationResult(
                            coordinate,
                            Array.Empty<CellCoordinate>());
                    }
                    else if (!runtime.PatternMaps.TryGetPair(
                                 configuration.PatternTiles.GetKeyForChunk(
                                     coordinate),
                                 out var tile))
                    {
                        continue;
                    }
                    else
                    {
                        runtime.BeginChunkPreparation(coordinate);
                        result = materializer.Materialize(
                            runtime.Data,
                            coordinate,
                            tile);
                        persistence?.MarkDirty(coordinate);
                        runtime.CompleteChunkPreparation(
                            coordinate,
                            result.SourceCells);
                        runtime.ActivateChunk(coordinate);
                        completed++;
                        continue;
                    }

                    runtime.BeginChunkPreparation(coordinate);
                    runtime.CompleteChunkPreparation(
                        coordinate,
                        result.SourceCells);
                    persistence?.RestoreWaterFrontier(coordinate);
                    persistence?.RestoreAvailableEntities();
                }

                if (runtime.GetChunkState(coordinate) == ChunkState.Ready)
                {
                    runtime.ActivateChunk(coordinate);
                    completed++;
                    continue;
                }

                throw new InvalidOperationException(
                    $"Chunk {coordinate} did not reach a ready streaming state.");
            }
        }

        private void UpdateSimulationRange()
        {
            for (var index = 0; index < orderedRenderChunks.Count; index++)
            {
                var coordinate = orderedRenderChunks[index];
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
            var pending = 0;
            for (var index = 0; index < orderedRenderChunks.Count; index++)
            {
                if (runtime.GetChunkState(orderedRenderChunks[index])
                    != ChunkState.Active)
                {
                    pending++;
                }
            }

            var next = new WorldStreamingProgress(
                orderedRenderChunks.Count - pending,
                orderedRenderChunks.Count);
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
