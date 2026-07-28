using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.WaterFlow
{
    [DisallowMultipleComponent]
    public sealed class WorldWaterFlowController : MonoBehaviour
    {
        private readonly HashSet<int> pendingCellIndices = new();
        private readonly HashSet<int> pendingColumnIndices = new();
        private readonly HashSet<int> affectedWaterBodyIds = new();
        private WorldData boundWorld;
        private WaterFlowResolver resolver;
        private bool recalculationRequested;
        private bool waterBodyTopologyRefreshRequested;
        private bool waterBodyMetricsRefreshRequested;
        private WaterFlowParameters activeParameters;

        public WorldData BoundWorld => boundWorld;
        public WaterFlowState State { get; private set; }
        public WorldChangeId LastAppliedChangeId { get; private set; }
        public bool HasPendingRecalculation => recalculationRequested;
        public int PendingChangeCount =>
            pendingCellIndices.Count + pendingColumnIndices.Count;
        public event Action<WaterFlowState> StateChanged;
        public event Action<WorldChangeSet> ChangeCommitted;

        private void Update()
        {
            if (!recalculationRequested || boundWorld == null || State == null)
            {
                return;
            }

            recalculationRequested = false;
            var result = resolver.Recalculate(
                boundWorld,
                State,
                pendingCellIndices,
                pendingColumnIndices,
                activeParameters);
            foreach (var columnIndex in result.ChangedColumnIndices)
            {
                pendingColumnIndices.Add(columnIndex);
            }

            if (result.HasPersistentChanges)
            {
                CommitResolvedChanges(result);
            }

            if (result.HasTopologyChanges
                || waterBodyTopologyRefreshRequested)
            {
                State.ReplaceWaterBodies(WaterBodyResolver.Resolve(boundWorld));
            }
            else if (result.HasRenderChanges
                || waterBodyMetricsRefreshRequested)
            {
                WaterBodyResolver.RefreshMetrics(
                    boundWorld,
                    State,
                    pendingColumnIndices,
                    affectedWaterBodyIds);
            }

            pendingCellIndices.Clear();
            pendingColumnIndices.Clear();
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            StateChanged?.Invoke(State);
        }

        public void Bind(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            boundWorld = world;
            activeParameters = CreateParameters();
            if (!world.WaterSources.IsInitialized)
            {
                world.WaterSources.InitializeFromGeneratedWorld(world);
            }

            State = new WaterFlowState(
                world,
                WaterBodyResolver.Resolve(world));
            resolver = new WaterFlowResolver(State.CellCount);
            pendingCellIndices.Clear();
            pendingColumnIndices.Clear();
            recalculationRequested = false;
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            LastAppliedChangeId = world.CurrentChangeId;
            StateChanged?.Invoke(State);
        }

        public void Unbind()
        {
            boundWorld = null;
            State = null;
            resolver = null;
            activeParameters = default;
            recalculationRequested = false;
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            pendingCellIndices.Clear();
            pendingColumnIndices.Clear();
            affectedWaterBodyIds.Clear();
            LastAppliedChangeId = WorldChangeId.None;
            StateChanged?.Invoke(null);
        }

        public void ApplyChanges(WorldChangeSet changeSet)
        {
            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            if (changeSet.World != boundWorld)
            {
                throw new InvalidOperationException(
                    "The change set belongs to a different world.");
            }

            if (changeSet.ChangeId <= LastAppliedChangeId)
            {
                return;
            }

            const WorldChangeType relevantChanges =
                WorldChangeType.CellStructure
                | WorldChangeType.Surface
                | WorldChangeType.WaterTopology
                | WorldChangeType.WaterSurface;
            if ((changeSet.ChangeTypes & relevantChanges) != 0)
            {
                ReconcileEditedWater(changeSet.ChangedCellIndices);
                AddPendingChanges(changeSet);
                recalculationRequested = true;
                waterBodyTopologyRefreshRequested |=
                    changeSet.Includes(WorldChangeType.CellStructure)
                    || changeSet.Includes(WorldChangeType.WaterTopology);
                waterBodyMetricsRefreshRequested |=
                    changeSet.Includes(WorldChangeType.WaterSurface);
            }

            LastAppliedChangeId = changeSet.ChangeId;
        }

        private void AddPendingChanges(WorldChangeSet changeSet)
        {
            for (var index = 0;
                 index < changeSet.ChangedCellIndices.Count;
                 index++)
            {
                pendingCellIndices.Add(changeSet.ChangedCellIndices[index]);
            }

            for (var index = 0;
                 index < changeSet.ChangedColumnIndices.Count;
                 index++)
            {
                pendingColumnIndices.Add(changeSet.ChangedColumnIndices[index]);
            }
        }

        private void ReconcileEditedWater(
            IReadOnlyList<int> changedCellIndices)
        {
            for (var index = 0; index < changedCellIndices.Count; index++)
            {
                var cellIndex = changedCellIndices[index];
                var coordinate = WorldIndex.DecodeCell(boundWorld, cellIndex);
                var cell = boundWorld.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if (!cell.HasWater)
                {
                    State.SynchronizeFromPersistent(cellIndex);
                    continue;
                }

                if (cell.Water.Role == WaterCellRole.None)
                {
                    cell.Water.Role = cell.Water.Type == WaterType.Sea
                        ? WaterCellRole.Reservoir
                        : (cell.Water.Flags & WaterCellFlags.River) != 0
                            ? WaterCellRole.Dynamic
                            : WaterCellRole.Reservoir;
                    boundWorld.SetCellForEdit(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z,
                        cell);
                }

                State.SynchronizeFromPersistent(cellIndex);
            }

            boundWorld.WaterSources.SynchronizeWithWorld(boundWorld);
        }

        private void CommitResolvedChanges(
            WaterFlowRecalculationResult result)
        {
            var changedColumns = ToSortedArray(result.ChangedColumnIndices);
            for (var index = 0; index < changedColumns.Length; index++)
            {
                WorldIndex.DecodeColumn(
                    boundWorld,
                    changedColumns[index],
                    out var x,
                    out var z);
                boundWorld.RebuildSurfaceColumn(x, z);
            }

            var logicalCells = ToSortedArray(
                result.LogicalChangedCellIndices);
            var renderCells = ToSortedArray(
                result.RenderChangedCellIndices);
            var affectedChunks = BuildAffectedChunks(
                renderCells,
                changedColumns);
            var changeId = boundWorld.AdvanceChangeId();
            for (var index = 0; index < affectedChunks.Length; index++)
            {
                boundWorld.MarkChunkChanged(affectedChunks[index], changeId);
            }

            var changeTypes = WorldChangeType.None;
            if (result.HasRenderChanges)
            {
                changeTypes |= WorldChangeType.WaterSurface;
            }

            if (result.HasTopologyChanges)
            {
                changeTypes |= WorldChangeType.WaterTopology
                    | WorldChangeType.Navigation
                    | WorldChangeType.Ecology;
            }

            var changeSet = new WorldChangeSet(
                boundWorld,
                changeId,
                changeTypes,
                logicalCells,
                changedColumns,
                affectedChunks,
                BuildBounds(renderCells));
            LastAppliedChangeId = changeId;
            ChangeCommitted?.Invoke(changeSet);
        }

        private ChunkCoordinate[] BuildAffectedChunks(
            int[] cells,
            int[] columns)
        {
            var chunks = new HashSet<ChunkCoordinate>();
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = WorldIndex.DecodeCell(boundWorld, cells[index]);
                var minX = Math.Max(0, cell.X - 1) / boundWorld.ChunkSizeX;
                var maxX = Math.Min(boundWorld.Size - 1, cell.X + 1)
                    / boundWorld.ChunkSizeX;
                var minY = Math.Max(0, cell.Y - 1) / boundWorld.ChunkSizeY;
                var maxY = Math.Min(boundWorld.Height - 1, cell.Y + 1)
                    / boundWorld.ChunkSizeY;
                var minZ = Math.Max(0, cell.Z - 1) / boundWorld.ChunkSizeZ;
                var maxZ = Math.Min(boundWorld.Size - 1, cell.Z + 1)
                    / boundWorld.ChunkSizeZ;
                for (var chunkY = minY; chunkY <= maxY; chunkY++)
                for (var chunkZ = minZ; chunkZ <= maxZ; chunkZ++)
                for (var chunkX = minX; chunkX <= maxX; chunkX++)
                {
                    chunks.Add(new ChunkCoordinate(chunkX, chunkY, chunkZ));
                }
            }

            if (chunks.Count == 0)
            {
                for (var index = 0; index < columns.Length; index++)
                {
                    WorldIndex.DecodeColumn(
                        boundWorld,
                        columns[index],
                        out var x,
                        out var z);
                    chunks.Add(new ChunkCoordinate(
                        x / boundWorld.ChunkSizeX,
                        0,
                        z / boundWorld.ChunkSizeZ));
                }
            }

            var result = new ChunkCoordinate[chunks.Count];
            chunks.CopyTo(result);
            return result;
        }

        private CellBounds BuildBounds(int[] cells)
        {
            if (cells.Length == 0)
            {
                return new CellBounds(default, default);
            }

            var minimum = WorldIndex.DecodeCell(boundWorld, cells[0]);
            var maximum = minimum;
            for (var index = 1; index < cells.Length; index++)
            {
                var cell = WorldIndex.DecodeCell(boundWorld, cells[index]);
                minimum = new CellCoordinate(
                    Math.Min(minimum.X, cell.X),
                    Math.Min(minimum.Y, cell.Y),
                    Math.Min(minimum.Z, cell.Z));
                maximum = new CellCoordinate(
                    Math.Max(maximum.X, cell.X),
                    Math.Max(maximum.Y, cell.Y),
                    Math.Max(maximum.Z, cell.Z));
            }

            return new CellBounds(minimum, maximum);
        }

        private static int[] ToSortedArray(HashSet<int> source)
        {
            var result = new int[source.Count];
            source.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        private WaterFlowParameters CreateParameters() =>
            new(boundWorld.WaterFlowRules);
    }
}
