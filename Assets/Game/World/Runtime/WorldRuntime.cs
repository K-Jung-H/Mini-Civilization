using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Runtime
{
    public sealed class WorldRuntime
    {
        private readonly Dictionary<ChunkCoordinate, ChunkRuntime>
            chunkRuntimes = new();
        private readonly HashSet<ChunkCoordinate> desiredPreparedChunks = new();
        private readonly HashSet<ChunkCoordinate> desiredTerrainRenderChunks = new();
        private readonly HashSet<ChunkCoordinate> desiredEntityRenderChunks = new();
        private readonly HashSet<ChunkCoordinate> desiredActiveChunks = new();
        private readonly List<ChunkCoordinate> removedChunks = new();
        private bool simulationStateChangedPending;

        private WorldRuntime(WorldData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            SurfaceCache = new SurfaceCache(data);
            NavigationCache = new NavigationCache(data, SurfaceCache);
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
                && cell.X > 0
                && !IsSimulationActive(new ChunkCoordinate(
                    chunk.X - 1,
                    chunk.Z)))
            {
                return false;
            }

            if (localX == Data.ChunkSizeX - 1
                && cell.X + 1 < Data.Size
                && !IsSimulationActive(new ChunkCoordinate(
                    chunk.X + 1,
                    chunk.Z)))
            {
                return false;
            }

            if (localZ == 0
                && cell.Z > 0
                && !IsSimulationActive(new ChunkCoordinate(
                    chunk.X,
                    chunk.Z - 1)))
            {
                return false;
            }

            return localZ != Data.ChunkSizeZ - 1
                || cell.Z + 1 >= Data.Size
                || IsSimulationActive(new ChunkCoordinate(
                    chunk.X,
                    chunk.Z + 1));
        }

        public bool IsEntityRenderingEnabled(ChunkCoordinate coordinate) =>
            chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
            && chunkRuntime.EntityRenderingEnabled;

        public bool IsChunkPrepared(ChunkCoordinate coordinate) =>
            SurfaceCache.IsPrepared(coordinate)
            && NavigationCache.IsPrepared(coordinate);

        internal bool IsChunkPrepared(int cellX, int cellZ) =>
            Data.ContainsColumn(cellX, cellZ)
            && IsChunkPrepared(WorldCoordinateUtility.ToChunk(
                cellX,
                cellZ,
                Data.ChunkSizeX));

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

            desiredPreparedChunks.Clear();
            desiredTerrainRenderChunks.Clear();
            desiredEntityRenderChunks.Clear();
            desiredActiveChunks.Clear();
            var chunksPerPatch = Data.Settings.RenderChunksPerPatch;
            var preparationRadius = Math.Max(
                renderRadius,
                Math.Max(entityRenderRadius, simulationRadius));
            for (var z = center.Z - preparationRadius;
                 z <= center.Z + preparationRadius;
                 z++)
            for (var x = center.X - preparationRadius;
                 x <= center.X + preparationRadius;
                 x++)
            {
                if ((uint)x >= Data.ChunkCountX
                    || (uint)z >= Data.ChunkCountZ)
                {
                    continue;
                }

                if (Math.Abs(x - center.X) <= renderRadius
                    && Math.Abs(z - center.Z) <= renderRadius)
                {
                    var patchX = x / chunksPerPatch;
                    var patchZ = z / chunksPerPatch;
                    var patchStartX = patchX * chunksPerPatch;
                    var patchStartZ = patchZ * chunksPerPatch;
                    var patchEndX = Math.Min(
                        patchStartX + chunksPerPatch,
                        Data.ChunkCountX);
                    var patchEndZ = Math.Min(
                        patchStartZ + chunksPerPatch,
                        Data.ChunkCountZ);
                    for (var patchChunkZ = patchStartZ;
                         patchChunkZ < patchEndZ;
                         patchChunkZ++)
                    for (var patchChunkX = patchStartX;
                         patchChunkX < patchEndX;
                         patchChunkX++)
                    {
                        desiredTerrainRenderChunks.Add(new ChunkCoordinate(
                            patchChunkX,
                            patchChunkZ));
                    }
                }

                if (Math.Abs(x - center.X) <= entityRenderRadius
                    && Math.Abs(z - center.Z) <= entityRenderRadius)
                {
                    desiredEntityRenderChunks.Add(
                        new ChunkCoordinate(x, z));
                }

                if (Math.Abs(x - center.X) <= simulationRadius
                    && Math.Abs(z - center.Z) <= simulationRadius)
                {
                    desiredActiveChunks.Add(
                        new ChunkCoordinate(x, z));
                }
            }

            desiredPreparedChunks.UnionWith(desiredTerrainRenderChunks);
            desiredPreparedChunks.UnionWith(desiredEntityRenderChunks);
            desiredPreparedChunks.UnionWith(desiredActiveChunks);

            removedChunks.Clear();
            foreach (var pair in chunkRuntimes)
            {
                if (!desiredPreparedChunks.Contains(pair.Key))
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

            foreach (var coordinate in desiredPreparedChunks)
            {
                if (!chunkRuntimes.TryGetValue(
                        coordinate,
                        out var chunkRuntime))
                {
                    Data.EnsureChunkLoaded(coordinate);
                    chunkRuntime = new ChunkRuntime(coordinate);
                    chunkRuntimes.Add(coordinate, chunkRuntime);
                    ChangeChunkState(
                        chunkRuntime,
                        ChunkState.Preparing);
                    SurfaceCache.PrepareChunk(coordinate);
                    NavigationCache.PrepareChunk(
                        coordinate,
                        rebuildWaterDistances: false);
                    cacheSetChanged = true;
                    ChangeChunkState(
                        chunkRuntime,
                        ChunkState.Ready);
                }

                var shouldBeActive = desiredActiveChunks.Contains(coordinate);
                if (chunkRuntime.State == ChunkState.Ready
                    && shouldBeActive)
                {
                    ChangeChunkState(
                        chunkRuntime,
                        ChunkState.Active);
                }
                else if (chunkRuntime.State == ChunkState.Active
                    && !shouldBeActive)
                {
                    ChangeChunkState(
                        chunkRuntime,
                        ChunkState.Ready);
                }

                ChangeTerrainRenderState(
                    chunkRuntime,
                    desiredTerrainRenderChunks.Contains(coordinate));
                ChangeEntityRenderState(
                    chunkRuntime,
                    desiredEntityRenderChunks.Contains(coordinate));
            }

            if (cacheSetChanged)
            {
                NavigationCache.RebuildWaterDistances();
                RebuildWayPointGraph();
                WaterFlowState.ReplaceWaterBodies(
                    WaterBodyResolver.ResolvePrepared(this));
            }

            FlushSimulationStateChanged();
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

            desiredPreparedChunks.Clear();
            desiredTerrainRenderChunks.Clear();
            desiredEntityRenderChunks.Clear();
            desiredActiveChunks.Clear();
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
