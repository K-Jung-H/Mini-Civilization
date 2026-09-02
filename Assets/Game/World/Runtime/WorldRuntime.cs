using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Generation.Semantic;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Runtime
{
    public sealed class WorldRuntime
    {
        private readonly Dictionary<ChunkCoordinate, ChunkRuntime>
            chunkRuntimes = new();

        private WorldRuntime(WorldData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            SurfaceCache = new SurfaceCache(data);
            NavigationCache = new NavigationCache(data, SurfaceCache);
            PatternMaps = new PatternMapStore();
            Context = new WorldContext(this);
        }

        public WorldData Data { get; }
        public SurfaceCache SurfaceCache { get; }
        public NavigationCache NavigationCache { get; }
        public PatternMapStore PatternMaps { get; }
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
            return CreateStreaming(data);
        }

        public static WorldRuntime CreateStreaming(WorldData data)
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

        internal void BeginChunkPreparation(ChunkCoordinate coordinate)
        {
            if (!Data.IsChunkWithinBounds(coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }

            if (!chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime))
            {
                chunkRuntime = new ChunkRuntime(coordinate);
                chunkRuntimes.Add(coordinate, chunkRuntime);
            }

            if (chunkRuntime.State != ChunkState.Unloaded)
            {
                throw new InvalidOperationException(
                    $"Chunk {coordinate} is not available for preparation.");
            }

            if (chunkRuntime.SetState(ChunkState.Preparing))
            {
                ChunkStateChanged?.Invoke(chunkRuntime);
            }
        }

        internal void CompleteChunkPreparation(
            ChunkCoordinate coordinate,
            IReadOnlyList<CellCoordinate> sourceCells)
        {
            if (sourceCells == null)
            {
                throw new ArgumentNullException(nameof(sourceCells));
            }

            if (!Data.IsChunkLoaded(coordinate))
            {
                throw new InvalidOperationException(
                    $"Chunk {coordinate} has no materialized WorldData.");
            }

            if (!chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
                || chunkRuntime.State != ChunkState.Preparing)
            {
                throw new InvalidOperationException(
                    $"Chunk {coordinate} is not preparing.");
            }

            SurfaceCache.PrepareChunk(coordinate);
            NavigationCache.PrepareChunk(
                coordinate,
                rebuildWaterDistances: false);
            if (chunkRuntime.SetState(ChunkState.Ready))
            {
                ChunkStateChanged?.Invoke(chunkRuntime);
            }

            var waterFrontier = BuildWaterFrontier(
                coordinate,
                sourceCells);
            WaterFlowResolver.EnqueueChanges(
                Data,
                WaterFlowState,
                waterFrontier,
                null);
        }

        internal void ActivateChunk(ChunkCoordinate coordinate)
        {
            if (!chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
                || chunkRuntime.State != ChunkState.Ready)
            {
                throw new InvalidOperationException(
                    $"Chunk {coordinate} is not ready for activation.");
            }

            if (chunkRuntime.SetState(ChunkState.Active))
            {
                ChunkStateChanged?.Invoke(chunkRuntime);
            }

            if (chunkRuntime.SetTerrainRenderingEnabled(true))
            {
                TerrainRenderStateChanged?.Invoke(chunkRuntime);
            }

            SimulationStateChanged?.Invoke();
        }

        internal void SetChunkSimulationEnabled(
            ChunkCoordinate coordinate,
            bool enabled)
        {
            if (!chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
                || chunkRuntime.State != ChunkState.Active)
            {
                if (enabled)
                {
                    throw new InvalidOperationException(
                        $"Chunk {coordinate} is not active for simulation.");
                }

                return;
            }

            if (chunkRuntime.SetSimulationEnabled(enabled))
            {
                SimulationStateChanged?.Invoke();
            }
        }

        internal void ReleaseChunk(ChunkCoordinate coordinate)
        {
            if (!chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
                || chunkRuntime.State == ChunkState.Unloaded)
            {
                return;
            }

            var simulationChanged = chunkRuntime.SimulationEnabled;
            if (chunkRuntime.State == ChunkState.Active)
            {
                chunkRuntime.SetSimulationEnabled(false);
                if (chunkRuntime.SetTerrainRenderingEnabled(false))
                {
                    TerrainRenderStateChanged?.Invoke(chunkRuntime);
                }

                if (chunkRuntime.SetState(ChunkState.Ready))
                {
                    ChunkStateChanged?.Invoke(chunkRuntime);
                }
            }

            if (chunkRuntime.State == ChunkState.Preparing)
            {
                if (chunkRuntime.SetState(ChunkState.Unloaded))
                {
                    ChunkStateChanged?.Invoke(chunkRuntime);
                }

                return;
            }

            if (chunkRuntime.State != ChunkState.Ready)
            {
                throw new InvalidOperationException(
                    $"Chunk {coordinate} cannot be released from {chunkRuntime.State}.");
            }

            NavigationCache.ReleaseChunk(
                coordinate,
                rebuildWaterDistances: false);
            SurfaceCache.ReleaseChunk(coordinate);
            if (chunkRuntime.SetState(ChunkState.Unloaded))
            {
                ChunkStateChanged?.Invoke(chunkRuntime);
            }

            if (simulationChanged)
            {
                SimulationStateChanged?.Invoke();
            }
        }

        internal void RebuildWayPointGraph()
        {
            RoadTopology = WorldRoadTopology.Build(this, Entities);
            WayPointGraph = WorldWayPointGraph.Build(
                this,
                Entities,
                RoadTopology);
            Entities.RestoreBuildingWayLocations(WayPointGraph);
        }

        private IReadOnlyList<CellCoordinate> BuildWaterFrontier(
            ChunkCoordinate coordinate,
            IReadOnlyList<CellCoordinate> sourceCells)
        {
            var candidates = new HashSet<CellCoordinate>();
            for (var index = 0; index < sourceCells.Count; index++)
            {
                candidates.Add(sourceCells[index]);
            }

            AddBoundaryWaterCells(coordinate, candidates);
            var frontier = new List<CellCoordinate>(candidates.Count);
            foreach (var candidate in candidates)
            {
                if (Data.TryGetCell(
                        candidate.X,
                        candidate.Y,
                        candidate.Z,
                        out var cell)
                    && cell.HasWater
                    && (cell.Water.Role != WaterRole.Source
                        || WaterSourceFrontierSelector.IsNeeded(
                            Data,
                            candidate)))
                {
                    frontier.Add(candidate);
                }
            }

            frontier.Sort();
            return frontier;
        }

        private void AddBoundaryWaterCells(
            ChunkCoordinate coordinate,
            ISet<CellCoordinate> candidates)
        {
            AddBoundaryWaterCells(
                coordinate,
                coordinate.X * Data.ChunkSizeX,
                coordinate.Z * Data.ChunkSizeZ,
                -1,
                0,
                candidates);
            AddBoundaryWaterCells(
                coordinate,
                coordinate.X * Data.ChunkSizeX,
                coordinate.Z * Data.ChunkSizeZ,
                1,
                0,
                candidates);
            AddBoundaryWaterCells(
                coordinate,
                coordinate.X * Data.ChunkSizeX,
                coordinate.Z * Data.ChunkSizeZ,
                0,
                -1,
                candidates);
            AddBoundaryWaterCells(
                coordinate,
                coordinate.X * Data.ChunkSizeX,
                coordinate.Z * Data.ChunkSizeZ,
                0,
                1,
                candidates);
        }

        private void AddBoundaryWaterCells(
            ChunkCoordinate coordinate,
            int startX,
            int startZ,
            int directionX,
            int directionZ,
            ISet<CellCoordinate> candidates)
        {
            var neighbor = new ChunkCoordinate(
                coordinate.X + directionX,
                coordinate.Z + directionZ);
            if (!Data.IsChunkLoaded(neighbor))
            {
                return;
            }

            var currentX = directionX < 0 ? startX :
                directionX > 0 ? startX + Data.ChunkSizeX - 1 : 0;
            var currentZ = directionZ < 0 ? startZ :
                directionZ > 0 ? startZ + Data.ChunkSizeZ - 1 : 0;
            var neighborX = currentX + directionX;
            var neighborZ = currentZ + directionZ;
            var span = directionX == 0 ? Data.ChunkSizeX : Data.ChunkSizeZ;
            for (var offset = 0; offset < span; offset++)
            {
                var currentColumnX = directionX == 0
                    ? startX + offset
                    : currentX;
                var currentColumnZ = directionZ == 0
                    ? startZ + offset
                    : currentZ;
                var neighborColumnX = directionX == 0
                    ? startX + offset
                    : neighborX;
                var neighborColumnZ = directionZ == 0
                    ? startZ + offset
                    : neighborZ;
                for (var y = 0; y < Data.Height; y++)
                {
                    AddWaterCell(
                        currentColumnX,
                        y,
                        currentColumnZ,
                        candidates);
                    AddWaterCell(
                        neighborColumnX,
                        y,
                        neighborColumnZ,
                        candidates);
                }
            }
        }

        private void AddWaterCell(
            int x,
            int y,
            int z,
            ISet<CellCoordinate> candidates)
        {
            if (Data.TryGetCell(x, y, z, out var cell) && cell.HasWater)
            {
                candidates.Add(new CellCoordinate(x, y, z));
            }
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
            CurrentChangeId = new WorldChangeId(
                checked(CurrentChangeId.Value + 1));
            return CurrentChangeId;
        }

        public bool TryGetChunkRuntime(
            ChunkCoordinate coordinate,
            out ChunkRuntime chunkRuntime) =>
            chunkRuntimes.TryGetValue(coordinate, out chunkRuntime);

        public ChunkState GetChunkState(ChunkCoordinate coordinate) =>
            chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
                ? chunkRuntime.State
                : ChunkState.Unloaded;

        public bool IsSimulationActive(CellCoordinate cell) =>
            IsSimulationActive(WorldCoordinateUtility.ToChunk(
                cell.X,
                cell.Z,
                Data.ChunkSizeX));

        public bool IsSimulationActive(ChunkCoordinate coordinate) =>
            chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
            && chunkRuntime.State == ChunkState.Active
            && chunkRuntime.SimulationEnabled;

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
            return IsSimulationActive(chunk);
        }

        public bool IsEntityRenderingEnabled(ChunkCoordinate coordinate) =>
            chunkRuntimes.TryGetValue(coordinate, out var chunkRuntime)
            && chunkRuntime.EntityRenderingEnabled;

        public bool IsChunkPrepared(ChunkCoordinate coordinate) =>
            SurfaceCache.IsPrepared(coordinate)
            && NavigationCache.IsPrepared(coordinate);

        internal bool IsChunkPrepared(int cellX, int cellZ) =>
            Data.IsColumnLoaded(cellX, cellZ)
            && IsChunkPrepared(WorldCoordinateUtility.ToChunk(
                cellX,
                cellZ,
                Data.ChunkSizeX));

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
    }

    public sealed class WorldContext
    {
        public WorldRuntime Runtime { get; }
        public WorldData World => Runtime.Data;

        internal WorldContext(WorldRuntime runtime)
        {
            Runtime = runtime ?? throw new ArgumentNullException(
                nameof(runtime));
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
