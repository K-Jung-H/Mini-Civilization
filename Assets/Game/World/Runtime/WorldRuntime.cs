using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Generation.Streaming;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Runtime
{
    public sealed class WorldRuntime
    {
        private readonly Dictionary<ChunkCoordinate, ChunkRuntime>
            chunkRuntimes = new();
        private readonly List<ChunkCoordinate> removedChunks = new();
        private readonly List<CellCoordinate> generatedWaterSources = new();
        private readonly ChunkStreamingCoordinator streamingCoordinator;
        private StreamingChunkDemand streamingDemand = StreamingChunkDemand.Empty;
        private bool derivedDataRefreshPending;
        private bool simulationStateChangedPending;

        private WorldRuntime(WorldData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            SurfaceCache = new SurfaceCache(data);
            NavigationCache = new NavigationCache(data, SurfaceCache);
            streamingCoordinator = new ChunkStreamingCoordinator(data);
            Context = new WorldContext(this);
        }

        public WorldData Data { get; }
        public SurfaceCache SurfaceCache { get; }
        public NavigationCache NavigationCache { get; }
        public WorldContext Context { get; }
        public WaterFlowState WaterFlowState { get; private set; }
        internal WaterFlowResolver WaterFlowResolver { get; private set; }
        public WorldChangeId CurrentChangeId { get; private set; }
        public RuntimeChangeApplier ChangeApplier { get; internal set; }
        public EntityRuntime Entities { get; private set; }
        internal WorldRoadTopology RoadTopology { get; private set; }
        public WorldWayPointGraph WayPointGraph { get; private set; }
        public IReadOnlyDictionary<ChunkCoordinate, ChunkRuntime>
            ChunkRuntimes => chunkRuntimes;
        public event Action<ChunkRuntime> ChunkStateChanged;
        public event Action SimulationStateChanged;
        public event Action<ChunkRuntime> TerrainRenderStateChanged;
        public event Action<ChunkRuntime> EntityRenderStateChanged;

        public static WorldRuntime CreatePrepared(WorldData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            WorldDataValidator.Validate(data);
            var runtime = new WorldRuntime(data);
            runtime.WaterFlowState = new WaterFlowState(
                data,
                Array.Empty<WaterBody>());
            runtime.WaterFlowResolver = new WaterFlowResolver(
                data.ChunkSizeX,
                runtime.CanSimulateWaterCell);
            runtime.WaterFlowResolver.RestoreFrontier(
                data,
                runtime.WaterFlowState,
                data.WaterFlowSchedule.FrontierCells);
            runtime.ChangeApplier = new RuntimeChangeApplier(runtime);
            runtime.Entities = new EntityRuntime(
                runtime,
                EntityTypeRegistry.Shared);
            runtime.RebuildWayPointGraph();
            return runtime;
        }

        internal void RebuildWayPointGraph()
        {
            RoadTopology = WorldRoadTopology.Build(this, Entities);
            WayPointGraph = WorldWayPointGraph.Build(this, Entities, RoadTopology);
            Entities.RestoreBuildingWayLocations(WayPointGraph);
        }

        internal bool AffectsWayPointGraph(WorldChangeSet changeSet)
        {
            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            if (changeSet.Includes(WorldChangeType.RoadTopology))
            {
                return true;
            }

            if ((changeSet.ChangeTypes & (
                    WorldChangeType.CellStructure
                    | WorldChangeType.Surface)) == 0)
            {
                return false;
            }

            for (var index = 0;
                 index < changeSet.ChangedCells.Count;
                 index++)
            {
                var changed = changeSet.ChangedCells[index];
                for (var z = changed.Z - 1; z <= changed.Z + 1; z++)
                for (var x = changed.X - 1; x <= changed.X + 1; x++)
                {
                    if (!Data.ContainsColumn(x, z))
                    {
                        continue;
                    }

                    if (RoadTopologyResolver.TryGetRoad(this, x, z, out _)
                        || Entities.HasBuildingInColumn(x, z))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal WorldChangeId AdvanceChangeId()
        {
            CurrentChangeId = new WorldChangeId(checked(CurrentChangeId.Value + 1));
            return CurrentChangeId;
        }

        public bool TryGetChunkRuntime(
            ChunkCoordinate coordinate,
            out ChunkRuntime chunkRuntime) =>
            chunkRuntimes.TryGetValue(coordinate, out chunkRuntime);

        public ChunkState GetChunkState(
            ChunkCoordinate coordinate) =>
            chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
                ? chunkRuntime.State
                : ChunkState.Unloaded;

        public bool IsSimulationActive(CellCoordinate cell) =>
            IsSimulationActive(WorldCoordinateUtility.ToChunk(
                cell.X,
                cell.Z,
                Data.ChunkSizeX));

        public bool IsSimulationActive(ChunkCoordinate coordinate) =>
            GetChunkState(coordinate) == ChunkState.Active;

        internal bool CanSimulateWaterCell(CellCoordinate cell)
        {
            if (!Data.Contains(cell.X, cell.Y, cell.Z))
            {
                return false;
            }

            var chunk = WorldCoordinateUtility.ToChunk(
                cell.X,
                cell.Z,
                Data.ChunkSizeX);
            if (!IsSimulationActive(chunk))
            {
                return false;
            }

            var localX = WorldCoordinateUtility.PositiveModulo(
                cell.X,
                Data.ChunkSizeX);
            var localZ = WorldCoordinateUtility.PositiveModulo(
                cell.Z,
                Data.ChunkSizeZ);
            if (localX == 0
                && IsUnavailableSimulationNeighbor(new ChunkCoordinate(
                    chunk.X - 1,
                    chunk.Z)))
            {
                return false;
            }

            if (localX == Data.ChunkSizeX - 1
                && IsUnavailableSimulationNeighbor(new ChunkCoordinate(
                    chunk.X + 1,
                    chunk.Z)))
            {
                return false;
            }

            if (localZ == 0
                && IsUnavailableSimulationNeighbor(new ChunkCoordinate(
                    chunk.X,
                    chunk.Z - 1)))
            {
                return false;
            }

            return localZ != Data.ChunkSizeZ - 1
                || !IsUnavailableSimulationNeighbor(new ChunkCoordinate(
                    chunk.X,
                    chunk.Z + 1));
        }

        private bool IsUnavailableSimulationNeighbor(
            ChunkCoordinate coordinate) =>
            Data.IsChunkWithinBounds(coordinate)
            && !IsSimulationActive(coordinate);

        public bool IsEntityRenderingEnabled(ChunkCoordinate coordinate) =>
            chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
            && chunkRuntime.EntityRenderingEnabled;

        public bool IsChunkPrepared(ChunkCoordinate coordinate) =>
            SurfaceCache.IsPrepared(coordinate)
            && NavigationCache.IsPrepared(coordinate);

        internal bool IsTerrainRenderDemandComplete(
            out int completedChunkCount,
            out int chunkCount)
        {
            completedChunkCount = 0;
            var terrainRenderChunks = streamingDemand.TerrainRenderChunks;
            chunkCount = terrainRenderChunks.Count;
            if (chunkCount == 0)
            {
                return false;
            }

            foreach (var coordinate in terrainRenderChunks)
            {
                if (Data.IsChunkLoaded(coordinate))
                {
                    completedChunkCount++;
                }
            }

            return completedChunkCount == chunkCount;
        }

        internal bool IsChunkPrepared(int cellX, int cellZ) =>
            Data.IsColumnLoaded(cellX, cellZ)
            && IsChunkPrepared(WorldCoordinateUtility.ToChunk(
                cellX,
                cellZ,
                Data.ChunkSizeX));

        internal StreamingPatternMapSession OpenPatternMapSession(
            in WorldCellRectangle rectangle) =>
            streamingCoordinator.OpenPatternMapSession(rectangle);

        internal void UpdateStreamingChunks(
            ChunkCoordinate center,
            int renderRadius,
            int entityRenderRadius,
            int simulationRadius)
        {
            if (renderRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(renderRadius));
            }

            if (entityRenderRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityRenderRadius));
            }

            if (simulationRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(simulationRadius));
            }

            streamingDemand = StreamingChunkDemandBuilder.Build(
                Data,
                new StreamingRequest(
                    center,
                    renderRadius,
                    entityRenderRadius,
                    simulationRadius));

            removedChunks.Clear();
            foreach (var pair in chunkRuntimes)
            {
                if (!streamingDemand.IsPrepared(pair.Key))
                {
                    removedChunks.Add(pair.Key);
                }
            }

            var cacheSetChanged = false;
            for (var index = 0; index < removedChunks.Count; index++)
            {
                cacheSetChanged |= UnloadChunk(
                    removedChunks[index],
                    rebuildWaterDistances: false);
            }

            foreach (var coordinate in streamingDemand.PreparedChunks)
            {
                if (!chunkRuntimes.TryGetValue(
                        coordinate,
                        out var chunkRuntime))
                {
                    if (!Data.IsChunkLoaded(coordinate))
                    {
                        continue;
                    }

                    chunkRuntime = PrepareChunkRuntime(coordinate);
                    cacheSetChanged = true;
                }

                ApplyDesiredChunkState(chunkRuntime);
            }

            if (cacheSetChanged)
            {
                RefreshDerivedData();
            }

            streamingCoordinator.SetDemand(streamingDemand);
            RefreshDerivedDataWhenReady();
            FlushSimulationStateChanged();
        }

        internal void ProcessStreamingWork(int maximumChunkApplications)
        {
            if (maximumChunkApplications <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumChunkApplications));
            }

            var applied = 0;
            while (applied < maximumChunkApplications
                   && streamingCoordinator.TryTakeCompleted(out var build))
            {
                if (streamingCoordinator.IsPrepared(build.Coordinate)
                    && !Data.IsChunkLoaded(build.Coordinate))
                {
                    WorldDataBuilder.ApplyChunk(Data, build);
                    EnqueueGeneratedWaterSources(build.Coordinate);
                    var chunkRuntime = PrepareChunkRuntime(build.Coordinate);
                    ApplyDesiredChunkState(chunkRuntime);
                    derivedDataRefreshPending = true;
                    applied++;
                }
            }

            ApplyAllDesiredChunkStates();
            RefreshDerivedDataWhenReady();
            FlushSimulationStateChanged();
        }

        private ChunkRuntime PrepareChunkRuntime(ChunkCoordinate coordinate)
        {
            if (chunkRuntimes.TryGetValue(coordinate, out var existing))
            {
                return existing;
            }

            var chunkRuntime = new ChunkRuntime(coordinate);
            chunkRuntimes.Add(coordinate, chunkRuntime);
            ChangeChunkState(chunkRuntime, ChunkState.Preparing);
            SurfaceCache.PrepareChunk(coordinate);
            NavigationCache.PrepareChunk(
                coordinate,
                rebuildWaterDistances: false);
            ChangeChunkState(chunkRuntime, ChunkState.Ready);
            return chunkRuntime;
        }

        private void ApplyAllDesiredChunkStates()
        {
            foreach (var coordinate in streamingDemand.PreparedChunks)
            {
                if (chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime))
                {
                    ApplyDesiredChunkState(chunkRuntime);
                }
            }
        }

        private void ApplyDesiredChunkState(ChunkRuntime chunkRuntime)
        {
            var coordinate = chunkRuntime.Coordinate;
            var shouldBeActive = streamingDemand.IsActive(coordinate);
            if (chunkRuntime.State == ChunkState.Ready && shouldBeActive)
            {
                ChangeChunkState(chunkRuntime, ChunkState.Active);
            }
            else if (chunkRuntime.State == ChunkState.Active && !shouldBeActive)
            {
                ChangeChunkState(chunkRuntime, ChunkState.Ready);
            }

            ChangeTerrainRenderState(
                chunkRuntime,
                streamingDemand.IsTerrainRendering(coordinate)
                && HasPreparedTerrainTopology(coordinate));
            ChangeEntityRenderState(
                chunkRuntime,
                streamingDemand.IsEntityRendering(coordinate));
        }

        private bool HasPreparedTerrainTopology(ChunkCoordinate coordinate)
        {
            for (var z = coordinate.Z - 1; z <= coordinate.Z + 1; z++)
            for (var x = coordinate.X - 1; x <= coordinate.X + 1; x++)
            {
                var neighbor = new ChunkCoordinate(x, z);
                if (Data.IsChunkWithinBounds(neighbor)
                    && !IsChunkPrepared(neighbor))
                {
                    return false;
                }
            }

            return true;
        }

        private void RefreshDerivedDataWhenReady()
        {
            if (!derivedDataRefreshPending
                || streamingCoordinator.HasWork)
            {
                return;
            }

            RefreshDerivedData();
        }

        private void RefreshDerivedData()
        {
            NavigationCache.RebuildWaterDistances();
            RebuildWayPointGraph();
            WaterFlowState.ReplaceWaterBodies(
                WaterBodyResolver.ResolvePrepared(this));
            derivedDataRefreshPending = false;
        }

        private void EnqueueGeneratedWaterSources(ChunkCoordinate coordinate)
        {
            generatedWaterSources.Clear();
            var startX = coordinate.X * Data.ChunkSizeX;
            var startZ = coordinate.Z * Data.ChunkSizeZ;
            for (var y = 0; y < Data.Height; y++)
            for (var localZ = -1; localZ <= Data.ChunkSizeZ; localZ++)
            for (var localX = -1; localX <= Data.ChunkSizeX; localX++)
            {
                var cell = new CellCoordinate(
                    startX + localX,
                    y,
                    startZ + localZ);
                if (Data.TryGetCell(cell.X, cell.Y, cell.Z, out var data)
                    && data.HasWater
                    && data.Water.Role == WaterRole.Source
                    && WaterSourceFrontierSelector.IsNeeded(Data, cell))
                {
                    generatedWaterSources.Add(cell);
                }
            }

            if (generatedWaterSources.Count > 0)
            {
                WaterFlowResolver.EnqueueChanges(
                    Data,
                    WaterFlowState,
                    generatedWaterSources,
                    null);
            }
        }

        internal bool HasTerrainRenderingInPatch(
            int patchX,
            int patchZ,
            int chunksPerPatch)
        {
            var startX = patchX * chunksPerPatch;
            var startZ = patchZ * chunksPerPatch;
            var endX = startX + chunksPerPatch;
            var endZ = startZ + chunksPerPatch;
            foreach (var pair in chunkRuntimes)
            {
                var coordinate = pair.Key;
                if (coordinate.X >= startX
                    && coordinate.X < endX
                    && coordinate.Z >= startZ
                    && coordinate.Z < endZ
                    && pair.Value.TerrainRenderingEnabled)
                {
                    return true;
                }
            }

            return false;
        }

        internal void ClearStreamingChunks()
        {
            streamingCoordinator.Clear();
            removedChunks.Clear();
            foreach (var coordinate in chunkRuntimes.Keys)
            {
                removedChunks.Add(coordinate);
            }

            var cacheSetChanged = false;
            for (var index = 0; index < removedChunks.Count; index++)
            {
                cacheSetChanged |= UnloadChunk(
                    removedChunks[index],
                    rebuildWaterDistances: false);
            }

            if (cacheSetChanged)
            {
                NavigationCache.RebuildWaterDistances();
                RebuildWayPointGraph();
                WaterFlowState.ReplaceWaterBodies(
                    WaterBodyResolver.ResolvePrepared(this));
            }

            streamingDemand = StreamingChunkDemand.Empty;
            derivedDataRefreshPending = false;
            FlushSimulationStateChanged();
        }

        private bool UnloadChunk(
            ChunkCoordinate coordinate,
            bool rebuildWaterDistances)
        {
            if (!chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime))
            {
                return false;
            }

            if (chunkRuntime.State == ChunkState.Active)
            {
                ChangeChunkState(
                    chunkRuntime,
                    ChunkState.Ready);
            }

            ChangeTerrainRenderState(chunkRuntime, false);
            ChangeEntityRenderState(chunkRuntime, false);

            NavigationCache.ReleaseChunk(
                coordinate,
                rebuildWaterDistances);
            SurfaceCache.ReleaseChunk(coordinate);
            ChangeChunkState(chunkRuntime, ChunkState.Unloaded);
            chunkRuntimes.Remove(coordinate);
            return true;
        }

        private void ChangeChunkState(
            ChunkRuntime chunkRuntime,
            ChunkState state)
        {
            var wasActive = chunkRuntime.State == ChunkState.Active;
            if (chunkRuntime.SetState(state))
            {
                ChunkStateChanged?.Invoke(chunkRuntime);
                if (wasActive != (state == ChunkState.Active))
                {
                    simulationStateChangedPending = true;
                }
            }
        }

        private void FlushSimulationStateChanged()
        {
            if (!simulationStateChangedPending)
            {
                return;
            }

            simulationStateChangedPending = false;
            SimulationStateChanged?.Invoke();
        }

        private void ChangeEntityRenderState(
            ChunkRuntime chunkRuntime,
            bool enabled)
        {
            if (chunkRuntime.SetEntityRenderingEnabled(enabled))
            {
                EntityRenderStateChanged?.Invoke(chunkRuntime);
            }
        }

        private void ChangeTerrainRenderState(
            ChunkRuntime chunkRuntime,
            bool enabled)
        {
            if (chunkRuntime.SetTerrainRenderingEnabled(enabled))
            {
                TerrainRenderStateChanged?.Invoke(chunkRuntime);
            }
        }

    }

    public sealed class WorldContext
    {
        public WorldRuntime Runtime { get; }
        public WorldData World => Runtime.Data;

        internal WorldContext(WorldRuntime runtime)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public CellView GetCell(CellCoordinate position)
        {
            if (!World.Contains(position.X, position.Y, position.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            return new CellView(Runtime, position);
        }
    }

    public readonly struct CellView
    {
        private readonly WorldRuntime runtime;

        public CellCoordinate Position { get; }
        public CellData Data => Current;
        public TerrainData Terrain => Current.Terrain;
        public WaterData Water => Current.Water;
        public bool HasTerrain => Current.HasTerrain;
        public bool HasWater => Current.HasWater;
        public byte WaterHeight => Current.WaterHeight;
        public CellBiome Biome => Current.Biome;
        public SurfaceHeightData SurfaceHeight =>
            runtime.SurfaceCache.GetSurfaceHeight(Position.X, Position.Z);
        public PathData Path => runtime.NavigationCache.GetPathData(
            Position.X,
            Position.Y,
            Position.Z);

        private CellData Current => runtime.Data.GetCell(
            Position.X,
            Position.Y,
            Position.Z);

        internal CellView(WorldRuntime runtime, CellCoordinate position)
        {
            this.runtime = runtime;
            Position = position;
        }
    }
}
