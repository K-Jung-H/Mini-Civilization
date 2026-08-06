using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.WaterFlow
{
    [DisallowMultipleComponent]
    public sealed class WorldWaterFlowController : MonoBehaviour
    {
        [Header("Simulation Budget")]
        [SerializeField, Min(0.01f)]
        private float simulationStepInterval = 0.1f;
        [SerializeField, Min(1)]
        private int maxCellsPerFrame = 2048;

        private readonly HashSet<int> pendingBodyColumnIndices = new();
        private readonly HashSet<int> affectedWaterBodyIds = new();
        private WorldRuntime boundRuntime;
        private WorldData boundWorld;
        private bool waterBodyTopologyRefreshRequested;
        private bool waterBodyMetricsRefreshRequested;
        private WaterFlowParameters activeParameters;
        private float simulationAccumulator;

        public WorldRuntime Runtime => boundRuntime;
        public WaterFlowState State => boundRuntime?.WaterFlowState;
        public WorldChangeId LastAppliedChangeId { get; private set; }
        public bool HasPendingRecalculation =>
            boundRuntime?.WaterFlowResolver.HasWork == true;
        public int PendingChangeCount =>
            boundRuntime?.WaterFlowResolver.PendingCellCount ?? 0;
        public event Action<WaterFlowState> StateChanged;
        public event Action<WorldChangeSet> ChangeCommitted;

        private void Update()
        {
            if (boundWorld == null
                || State == null
                || boundRuntime?.WaterFlowResolver == null
                || !boundRuntime.WaterFlowResolver.HasWork)
            {
                simulationAccumulator = 0f;
                return;
            }

            if (!boundRuntime.WaterFlowResolver.IsWaveInProgress)
            {
                simulationAccumulator += Time.deltaTime;
                if (simulationAccumulator < simulationStepInterval)
                {
                    return;
                }

                simulationAccumulator -= simulationStepInterval;
            }

            if (!boundRuntime.WaterFlowResolver.Step(
                boundWorld,
                State,
                activeParameters,
                maxCellsPerFrame,
                out var result))
            {
                return;
            }

            foreach (var columnIndex in result.ChangedColumnIndices)
            {
                pendingBodyColumnIndices.Add(columnIndex);
            }

            if (result.HasTopologyChanges
                || waterBodyTopologyRefreshRequested)
            {
                WaterTypeResolver.RefreshChanged(
                    boundWorld,
                    pendingBodyColumnIndices,
                    result.LogicalChangedCellIndices,
                    result.RenderChangedCellIndices,
                    result.WaterTypeChangedCellIndices,
                    result.ChangedColumnIndices);
            }

            // Completing a wave changes the persisted frontier even when no
            // Cell changes, so it must still advance the world change state.
            CommitResolvedChanges(result);

            if (result.HasTopologyChanges
                || waterBodyTopologyRefreshRequested)
            {
                State.ReplaceWaterBodies(WaterBodyResolver.Resolve(
                    boundWorld,
                    boundRuntime.SurfaceCache));
            }
            else if (result.HasRenderChanges
                || waterBodyMetricsRefreshRequested)
            {
                WaterBodyResolver.RefreshMetrics(
                    boundWorld,
                    boundRuntime.SurfaceCache,
                    State,
                    pendingBodyColumnIndices,
                    affectedWaterBodyIds);
            }

            pendingBodyColumnIndices.Clear();
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            StateChanged?.Invoke(State);
        }

        private void OnValidate()
        {
            simulationStepInterval = Math.Max(0.01f, simulationStepInterval);
            maxCellsPerFrame = Math.Max(1, maxCellsPerFrame);
        }

        public void Bind(WorldRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            boundRuntime = runtime;
            boundWorld = runtime.Data;
            activeParameters = CreateParameters();
            if (runtime.WaterFlowState == null
                || runtime.WaterFlowResolver == null)
            {
                throw new InvalidOperationException(
                    "World runtime water state has not been prepared.");
            }

            pendingBodyColumnIndices.Clear();
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            simulationAccumulator = 0f;
            LastAppliedChangeId = runtime.CurrentChangeId;
            StateChanged?.Invoke(State);
        }

        public void Unbind()
        {
            boundWorld = null;
            boundRuntime = null;
            activeParameters = default;
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            pendingBodyColumnIndices.Clear();
            affectedWaterBodyIds.Clear();
            simulationAccumulator = 0f;
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
                boundRuntime.WaterFlowResolver.EnqueueChanges(
                    boundWorld,
                    State,
                    changeSet.ChangedCellIndices,
                    changeSet.ChangedColumnIndices);
                for (var index = 0;
                     index < changeSet.ChangedColumnIndices.Count;
                     index++)
                {
                    pendingBodyColumnIndices.Add(
                        changeSet.ChangedColumnIndices[index]);
                }

                waterBodyTopologyRefreshRequested |=
                    changeSet.Includes(WorldChangeType.CellStructure)
                    || changeSet.Includes(WorldChangeType.WaterTopology);
                waterBodyMetricsRefreshRequested |=
                    changeSet.Includes(WorldChangeType.WaterSurface);
            }

            LastAppliedChangeId = changeSet.ChangeId;
        }

        private void ReconcileEditedWater(
            IReadOnlyList<int> changedCellIndices)
        {
            for (var index = 0; index < changedCellIndices.Count; index++)
            {
                State.SynchronizeFromPersistent(changedCellIndices[index]);
            }
        }

        private void CommitResolvedChanges(
            WaterFlowRecalculationResult result)
        {
            var changedColumns = ToSortedArray(result.ChangedColumnIndices);
            var logicalCells = ToSortedArray(
                result.LogicalChangedCellIndices);
            var renderCells = ToSortedArray(
                result.RenderChangedCellIndices);
            var affectedChunks = BuildAffectedChunks(
                renderCells,
                changedColumns);
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

            if (result.WaterTypeChangedCellIndices.Count > 0)
            {
                changeTypes |= WorldChangeType.Ecology;
            }

            var changeSet = boundRuntime.ChangeApplier.Apply(
                changeTypes,
                logicalCells,
                changedColumns,
                affectedChunks,
                BuildBounds(renderCells),
                rebuildNavigationColumns: false,
                rebuildWaterDistances: result.HasTopologyChanges
                    || waterBodyTopologyRefreshRequested);
            LastAppliedChangeId = changeSet.ChangeId;
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
