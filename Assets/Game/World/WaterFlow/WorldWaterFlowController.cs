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

        private readonly HashSet<CellColumnCoordinate> pendingBodyColumns = new();
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
                || !boundRuntime.WaterFlowResolver.HasRunnableWork)
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

            foreach (var column in result.ChangedColumns)
            {
                pendingBodyColumns.Add(column);
            }

            if (result.HasTopologyChanges
                || waterBodyTopologyRefreshRequested)
            {
                WaterTypeResolver.RefreshChanged(
                    boundWorld,
                    pendingBodyColumns,
                    result.LogicalChangedCells,
                    result.RenderChangedCells,
                    result.WaterTypeChangedCells,
                    result.ChangedColumns);
            }

            CommitResolvedChanges(result);

            if (result.HasTopologyChanges
                || waterBodyTopologyRefreshRequested)
            {
                State.ReplaceWaterBodies(
                    WaterBodyResolver.ResolvePrepared(boundRuntime));
            }
            else if (result.HasRenderChanges
                || waterBodyMetricsRefreshRequested)
            {
                WaterBodyResolver.RefreshMetrics(
                    boundWorld,
                    boundRuntime.SurfaceCache,
                    State,
                    pendingBodyColumns,
                    affectedWaterBodyIds);
            }

            pendingBodyColumns.Clear();
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
            boundRuntime.SimulationStateChanged += OnSimulationStateChanged;
            activeParameters = CreateParameters();
            if (runtime.WaterFlowState == null
                || runtime.WaterFlowResolver == null)
            {
                throw new InvalidOperationException(
                    "World runtime water state has not been prepared.");
            }

            pendingBodyColumns.Clear();
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            simulationAccumulator = 0f;
            LastAppliedChangeId = runtime.CurrentChangeId;
            StateChanged?.Invoke(State);
        }

        public void Unbind()
        {
            if (boundRuntime != null)
            {
                boundRuntime.SimulationStateChanged -= OnSimulationStateChanged;
            }

            boundWorld = null;
            boundRuntime = null;
            activeParameters = default;
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            pendingBodyColumns.Clear();
            affectedWaterBodyIds.Clear();
            simulationAccumulator = 0f;
            LastAppliedChangeId = WorldChangeId.None;
            StateChanged?.Invoke(null);
        }

        private void OnSimulationStateChanged()
        {
            if (boundRuntime == null
                || boundWorld == null
                || boundRuntime.WaterFlowResolver == null)
            {
                return;
            }

            boundRuntime.WaterFlowResolver.OnSimulationSetChanged(
                boundWorld,
                State);
            simulationAccumulator = 0f;
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
                ReconcileEditedWater(changeSet.ChangedCells);
                boundRuntime.WaterFlowResolver.EnqueueChanges(
                    boundWorld,
                    State,
                    changeSet.ChangedCells,
                    changeSet.ChangedColumns);
                for (var index = 0;
                     index < changeSet.ChangedColumns.Count;
                     index++)
                {
                    pendingBodyColumns.Add(
                        changeSet.ChangedColumns[index]);
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
            IReadOnlyList<CellCoordinate> changedCells)
        {
            for (var index = 0; index < changedCells.Count; index++)
            {
                State.SynchronizeFromPersistent(changedCells[index]);
            }
        }

        private void CommitResolvedChanges(
            WaterFlowRecalculationResult result)
        {
            var changedColumns = ToSortedArray(result.ChangedColumns);
            var logicalCells = ToSortedArray(
                result.LogicalChangedCells);
            var renderCells = ToSortedArray(
                result.RenderChangedCells);
            var affectedSections = BuildAffectedSections(
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

            if (result.WaterTypeChangedCells.Count > 0)
            {
                changeTypes |= WorldChangeType.Ecology;
            }

            var changeSet = boundRuntime.ChangeApplier.Apply(
                changeTypes,
                logicalCells,
                changedColumns,
                affectedSections,
                BuildBounds(renderCells),
                rebuildNavigationColumns: false,
                rebuildWaterDistances: result.HasTopologyChanges
                    || waterBodyTopologyRefreshRequested);
            LastAppliedChangeId = changeSet.ChangeId;
            ChangeCommitted?.Invoke(changeSet);
        }

        private ChunkSectionCoordinate[] BuildAffectedSections(
            CellCoordinate[] cells,
            CellColumnCoordinate[] columns)
        {
            var sections = new HashSet<ChunkSectionCoordinate>();
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = cells[index];
                var minX = Math.Max(0, cell.X - 1) / boundWorld.ChunkSizeX;
                var maxX = Math.Min(boundWorld.Size - 1, cell.X + 1)
                    / boundWorld.ChunkSizeX;
                var minY = Math.Max(0, cell.Y - 1) / boundWorld.ChunkSectionSizeY;
                var maxY = Math.Min(boundWorld.Height - 1, cell.Y + 1)
                    / boundWorld.ChunkSectionSizeY;
                var minZ = Math.Max(0, cell.Z - 1) / boundWorld.ChunkSizeZ;
                var maxZ = Math.Min(boundWorld.Size - 1, cell.Z + 1)
                    / boundWorld.ChunkSizeZ;
                for (var chunkY = minY; chunkY <= maxY; chunkY++)
                for (var chunkZ = minZ; chunkZ <= maxZ; chunkZ++)
                for (var chunkX = minX; chunkX <= maxX; chunkX++)
                {
                    sections.Add(new ChunkSectionCoordinate(chunkX, chunkY, chunkZ));
                }
            }

            if (sections.Count == 0)
            {
                for (var index = 0; index < columns.Length; index++)
                {
                    var column = columns[index];
                    sections.Add(new ChunkSectionCoordinate(
                        WorldCoordinateUtility.FloorDivide(
                            column.X,
                            boundWorld.ChunkSizeX),
                        0,
                        WorldCoordinateUtility.FloorDivide(
                            column.Z,
                            boundWorld.ChunkSizeZ)));
                }
            }

            var result = new ChunkSectionCoordinate[sections.Count];
            sections.CopyTo(result);
            return result;
        }

        private static CellBounds BuildBounds(CellCoordinate[] cells)
        {
            if (cells.Length == 0)
            {
                return new CellBounds(default, default);
            }

            var minimum = cells[0];
            var maximum = minimum;
            for (var index = 1; index < cells.Length; index++)
            {
                var cell = cells[index];
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

        private static T[] ToSortedArray<T>(HashSet<T> source)
            where T : IComparable<T>
        {
            var result = new T[source.Count];
            source.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        private WaterFlowParameters CreateParameters() =>
            new(boundWorld.WaterFlowRules);
    }
}
