using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.WaterFlow
{
    [DisallowMultipleComponent]
    public sealed class WorldWaterFlowController : MonoBehaviour
    {
        [Header("Water Flow Resolution")]
        [SerializeField, Min(WaterAmountConversion.MinimumConfigurableAmount),
         Tooltip("WaterSource와 WaterCell이 가질 수 있는 최대 WaterAmount입니다. " +
                 "수면 높이는 이 값에 대한 비율을 0.2 단위로 양자화합니다.")]
        private float maximumAmount = 1f;

        [SerializeField, Min(WaterAmountConversion.MinimumConfigurableAmount),
         Tooltip("물의 수평 확산 한 칸마다 감소하는 WaterAmount입니다. " +
                 "아래 방향 확산에는 적용되지 않습니다.")]
        private float spreadAmountLoss = 0.05f;

        [SerializeField, Min(WaterAmountConversion.MinimumConfigurableAmount),
         Tooltip("확산 결과가 이 값보다 작으면 새로운 WaterCell을 생성하지 않습니다.")]
        private float minimumSpreadAmount = 0.1f;

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
        public float MaximumAmount => maximumAmount;
        public float SpreadAmountLoss => spreadAmountLoss;
        public float MinimumSpreadAmount => minimumSpreadAmount;

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
            if (!world.WaterState.IsInitialized)
            {
                world.WaterState.InitializeFromGeneratedWorld(
                    world,
                    activeParameters.MaximumAmount);
            }
            else
            {
                world.WaterState.ConfigureMaximumAmount(
                    activeParameters.MaximumAmount);
            }

            State = new WaterFlowState(
                world,
                WaterBodyResolver.Resolve(world));
            resolver = new WaterFlowResolver(State.CellCount);
            pendingCellIndices.Clear();
            pendingColumnIndices.Clear();
            QueueInitialFlowCells();
            recalculationRequested = pendingCellIndices.Count > 0;
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

        public void RebuildAll()
        {
            if (boundWorld == null)
            {
                State = null;
                return;
            }

            activeParameters = CreateParameters();
            boundWorld.WaterState.ConfigureMaximumAmount(
                activeParameters.MaximumAmount);

            State = new WaterFlowState(
                boundWorld,
                WaterBodyResolver.Resolve(boundWorld));
            resolver = new WaterFlowResolver(State.CellCount);
            pendingCellIndices.Clear();
            pendingColumnIndices.Clear();
            QueueAllPersistentWaterCells();

            recalculationRequested = true;
            waterBodyTopologyRefreshRequested = false;
            waterBodyMetricsRefreshRequested = false;
            StateChanged?.Invoke(State);
        }

        private void QueueAllPersistentWaterCells()
        {
            if (boundWorld == null)
            {
                return;
            }

            for (var cellIndex = 0;
                 cellIndex < boundWorld.WaterState.CellCount;
                 cellIndex++)
            {
                if (boundWorld.WaterState.GetBehavior(cellIndex)
                    != WaterCellBehavior.None)
                {
                    pendingCellIndices.Add(cellIndex);
                }
            }
        }

        private void QueueInitialFlowCells()
        {
            if (boundWorld == null)
            {
                return;
            }

            for (var cellIndex = 0;
                 cellIndex < boundWorld.WaterState.CellCount;
                 cellIndex++)
            {
                var behavior = boundWorld.WaterState.GetBehavior(cellIndex);
                if (behavior == WaterCellBehavior.Source)
                {
                    pendingCellIndices.Add(cellIndex);
                    continue;
                }

                var coordinate = WorldIndex.DecodeCell(boundWorld, cellIndex);
                var cell = boundWorld.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if ((cell.Flags & CellFlags.FallingWater) != 0)
                {
                    pendingCellIndices.Add(cellIndex);
                }
            }
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
                var behavior = boundWorld.WaterState.GetBehavior(cellIndex);
                boundWorld.WaterState.SetAmount(
                    cellIndex,
                    WaterAmountConversion.FromRenderFill(
                        cell.WaterFill,
                        activeParameters.MaximumAmount));

                if (!cell.HasWater)
                {
                    if (behavior != WaterCellBehavior.Source)
                    {
                        boundWorld.WaterState.SetCell(
                            cellIndex,
                            0,
                            WaterCellBehavior.None);
                    }

                    State.SynchronizeFromPersistent(boundWorld, cellIndex);
                    continue;
                }

                if (behavior == WaterCellBehavior.None)
                {
                    behavior = cell.Water == WaterType.Sea
                        ? WaterCellBehavior.FixedReservoir
                        : (cell.Flags & CellFlags.River) != 0
                            ? WaterCellBehavior.FlowDependent
                            : WaterCellBehavior.Reservoir;
                    boundWorld.WaterState.SetBehavior(cellIndex, behavior);
                }

                State.SynchronizeFromPersistent(boundWorld, cellIndex);
            }
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

        private WaterFlowParameters CreateParameters() => new(
            maximumAmount,
            spreadAmountLoss,
            minimumSpreadAmount);

        private void OnValidate()
        {
            maximumAmount = Mathf.Clamp(
                maximumAmount,
                WaterAmountConversion.MinimumConfigurableAmount,
                WaterAmountConversion.MaximumConfigurableAmount);
            spreadAmountLoss = Mathf.Clamp(
                spreadAmountLoss,
                WaterAmountConversion.MinimumConfigurableAmount,
                maximumAmount);
            minimumSpreadAmount = Mathf.Clamp(
                minimumSpreadAmount,
                WaterAmountConversion.MinimumConfigurableAmount,
                maximumAmount);
        }
    }
}
