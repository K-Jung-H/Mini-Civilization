using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Runtime
{
    public sealed class WorldRuntime
    {
        private readonly Dictionary<ChunkColumnCoordinate, WorldChunkColumnRuntime>
            columnRuntimes = new();
        private readonly HashSet<ChunkColumnCoordinate> desiredRenderColumns = new();
        private readonly HashSet<ChunkColumnCoordinate> desiredActiveColumns = new();
        private readonly List<ChunkColumnCoordinate> removedColumns = new();

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
        public IReadOnlyDictionary<ChunkColumnCoordinate, WorldChunkColumnRuntime>
            ColumnRuntimes => columnRuntimes;
        public event Action<WorldChunkColumnRuntime> ColumnStateChanged;

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
                WaterBodyResolver.Resolve(data, runtime.SurfaceCache));
            runtime.WaterFlowResolver = new WaterFlowResolver(
                runtime.WaterFlowState.CellCount);
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

        public bool TryGetColumnRuntime(
            ChunkColumnCoordinate coordinate,
            out WorldChunkColumnRuntime columnRuntime) =>
            columnRuntimes.TryGetValue(coordinate, out columnRuntime);

        public WorldChunkColumnState GetColumnState(
            ChunkColumnCoordinate coordinate) =>
            columnRuntimes.TryGetValue(coordinate, out var columnRuntime)
                ? columnRuntime.State
                : WorldChunkColumnState.Unloaded;

        public bool IsSimulationActive(CellCoordinate cell) =>
            IsSimulationActive(WorldCoordinateUtility.ToChunkColumn(
                cell.X,
                cell.Z,
                Data.ChunkSizeX));

        public bool IsSimulationActive(ChunkColumnCoordinate coordinate) =>
            GetColumnState(coordinate) == WorldChunkColumnState.Active;

        public bool IsColumnPrepared(ChunkColumnCoordinate coordinate) =>
            SurfaceCache.IsPrepared(coordinate)
            && NavigationCache.IsPrepared(coordinate);

        internal bool IsColumnPrepared(int cellX, int cellZ) =>
            Data.ContainsColumn(cellX, cellZ)
            && IsColumnPrepared(WorldCoordinateUtility.ToChunkColumn(
                cellX,
                cellZ,
                Data.ChunkSizeX));

        internal void UpdateStreamingColumns(
            ChunkColumnCoordinate center,
            int renderRadius,
            int simulationRadius)
        {
            if (renderRadius < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(renderRadius));
            }

            if (simulationRadius < 0 || simulationRadius > renderRadius)
            {
                throw new ArgumentOutOfRangeException(nameof(simulationRadius));
            }

            desiredRenderColumns.Clear();
            desiredActiveColumns.Clear();
            var chunksPerPatch = Data.Settings.RenderChunksPerPatch;
            for (var z = center.Z - renderRadius;
                 z <= center.Z + renderRadius;
                 z++)
            for (var x = center.X - renderRadius;
                 x <= center.X + renderRadius;
                 x++)
            {
                if ((uint)x >= Data.ChunkCountX
                    || (uint)z >= Data.ChunkCountZ)
                {
                    continue;
                }

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
                for (var patchColumnZ = patchStartZ;
                     patchColumnZ < patchEndZ;
                     patchColumnZ++)
                for (var patchColumnX = patchStartX;
                     patchColumnX < patchEndX;
                     patchColumnX++)
                {
                    desiredRenderColumns.Add(new ChunkColumnCoordinate(
                        patchColumnX,
                        patchColumnZ));
                }

                if (Math.Abs(x - center.X) <= simulationRadius
                    && Math.Abs(z - center.Z) <= simulationRadius)
                {
                    desiredActiveColumns.Add(
                        new ChunkColumnCoordinate(x, z));
                }
            }

            removedColumns.Clear();
            foreach (var pair in columnRuntimes)
            {
                if (!desiredRenderColumns.Contains(pair.Key))
                {
                    removedColumns.Add(pair.Key);
                }
            }

            var cacheSetChanged = false;
            for (var index = 0; index < removedColumns.Count; index++)
            {
                cacheSetChanged |= UnloadColumn(
                    removedColumns[index],
                    rebuildWaterDistances: false);
            }

            foreach (var coordinate in desiredRenderColumns)
            {
                if (!columnRuntimes.TryGetValue(
                        coordinate,
                        out var columnRuntime))
                {
                    Data.EnsureColumnLoaded(coordinate);
                    columnRuntime = new WorldChunkColumnRuntime(coordinate);
                    columnRuntimes.Add(coordinate, columnRuntime);
                    SurfaceCache.PrepareColumn(coordinate);
                    NavigationCache.PrepareColumn(
                        coordinate,
                        rebuildWaterDistances: false);
                    cacheSetChanged = true;
                    ChangeColumnState(
                        columnRuntime,
                        WorldChunkColumnState.Preparing);
                    continue;
                }

                var shouldBeActive = desiredActiveColumns.Contains(coordinate);
                if (columnRuntime.State == WorldChunkColumnState.Rendered
                    && shouldBeActive)
                {
                    ChangeColumnState(
                        columnRuntime,
                        WorldChunkColumnState.Active);
                }
                else if (columnRuntime.State == WorldChunkColumnState.Active
                    && !shouldBeActive)
                {
                    ChangeColumnState(
                        columnRuntime,
                        WorldChunkColumnState.Rendered);
                }
            }

            if (cacheSetChanged)
            {
                NavigationCache.RebuildWaterDistances();
            }
        }

        internal void MarkColumnRendered(ChunkColumnCoordinate coordinate)
        {
            if (!desiredRenderColumns.Contains(coordinate)
                || !columnRuntimes.TryGetValue(
                    coordinate,
                    out var columnRuntime)
                || columnRuntime.State != WorldChunkColumnState.Preparing)
            {
                return;
            }

            ChangeColumnState(columnRuntime, WorldChunkColumnState.Rendered);
            if (desiredActiveColumns.Contains(coordinate))
            {
                ChangeColumnState(columnRuntime, WorldChunkColumnState.Active);
            }
        }

        internal bool HasPresentedColumnInPatch(
            int patchX,
            int patchZ,
            int chunksPerPatch)
        {
            var startX = patchX * chunksPerPatch;
            var startZ = patchZ * chunksPerPatch;
            var endX = startX + chunksPerPatch;
            var endZ = startZ + chunksPerPatch;
            foreach (var pair in columnRuntimes)
            {
                var coordinate = pair.Key;
                if (coordinate.X >= startX
                    && coordinate.X < endX
                    && coordinate.Z >= startZ
                    && coordinate.Z < endZ
                    && pair.Value.State != WorldChunkColumnState.Unloaded)
                {
                    return true;
                }
            }

            return false;
        }

        internal void MarkPatchRendered(
            int patchX,
            int patchZ,
            int chunksPerPatch)
        {
            var startX = patchX * chunksPerPatch;
            var startZ = patchZ * chunksPerPatch;
            var endX = Math.Min(startX + chunksPerPatch, Data.ChunkCountX);
            var endZ = Math.Min(startZ + chunksPerPatch, Data.ChunkCountZ);
            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                MarkColumnRendered(new ChunkColumnCoordinate(x, z));
            }
        }

        internal void ClearStreamingColumns()
        {
            removedColumns.Clear();
            foreach (var coordinate in columnRuntimes.Keys)
            {
                removedColumns.Add(coordinate);
            }

            var cacheSetChanged = false;
            for (var index = 0; index < removedColumns.Count; index++)
            {
                cacheSetChanged |= UnloadColumn(
                    removedColumns[index],
                    rebuildWaterDistances: false);
            }

            if (cacheSetChanged)
            {
                NavigationCache.RebuildWaterDistances();
            }

            desiredRenderColumns.Clear();
            desiredActiveColumns.Clear();
        }

        private bool UnloadColumn(
            ChunkColumnCoordinate coordinate,
            bool rebuildWaterDistances)
        {
            if (!columnRuntimes.TryGetValue(coordinate, out var columnRuntime))
            {
                return false;
            }

            if (columnRuntime.State == WorldChunkColumnState.Active)
            {
                ChangeColumnState(
                    columnRuntime,
                    WorldChunkColumnState.Rendered);
            }

            NavigationCache.ReleaseColumn(
                coordinate,
                rebuildWaterDistances);
            SurfaceCache.ReleaseColumn(coordinate);
            ChangeColumnState(columnRuntime, WorldChunkColumnState.Unloaded);
            columnRuntimes.Remove(coordinate);
            return true;
        }

        private void ChangeColumnState(
            WorldChunkColumnRuntime columnRuntime,
            WorldChunkColumnState state)
        {
            if (columnRuntime.SetState(state))
            {
                ColumnStateChanged?.Invoke(columnRuntime);
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
