using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Hydrology
{
    [DisallowMultipleComponent]
    public sealed class WorldHydrologyController : MonoBehaviour
    {
        [Header("Simulation")]
        [SerializeField, Min(0.01f), Tooltip("활성 물 셀 큐를 처리하는 시뮬레이션 틱 간격입니다.")]
        private float tickInterval = 0.1f;
        [SerializeField, Min(1), Tooltip("한 틱에서 처리할 최대 XZ 열 수입니다.")]
        private int columnsPerTick = 256;
        [SerializeField, Range(1, 16), Tooltip("변화가 없는 물 열이 휴면하기 전까지 다시 검사할 틱 수입니다.")]
        private int stableTickThreshold = 2;

        private WorldData boundWorld;
        private float tickAccumulator;
        private bool waterBodiesDirty;

        public WorldData BoundWorld => boundWorld;
        public HydrologyState State { get; private set; }
        public WorldChangeId LastAppliedChangeId { get; private set; }

        public event Action<HydrologyState> StateChanged;
        public event Action<WorldChangeSet> ChangeCommitted;

        private void Update()
        {
            if (boundWorld == null || State == null || !State.HasActiveColumns)
            {
                return;
            }

            tickAccumulator += Time.deltaTime;
            while (tickAccumulator >= tickInterval && State.HasActiveColumns)
            {
                tickAccumulator -= tickInterval;
                SimulateTick();
            }
        }

        public void Bind(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            boundWorld = world;
            if (!world.WaterState.IsInitialized)
            {
                world.WaterState.InitializeFromGeneratedWorld(world);
            }

            State = new HydrologyState(world, WaterBodyResolver.Resolve(world));
            QueueDynamicWaterColumns();
            tickAccumulator = 0f;
            waterBodiesDirty = false;
            LastAppliedChangeId = world.CurrentChangeId;
            StateChanged?.Invoke(State);
        }

        public void Unbind()
        {
            boundWorld = null;
            State = null;
            tickAccumulator = 0f;
            waterBodiesDirty = false;
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
                | WorldChangeType.WaterTopology;
            if ((changeSet.ChangeTypes & relevantChanges) != 0)
            {
                ReconcileEditedWater(changeSet.ChangedCellIndices);
                for (var index = 0;
                     index < changeSet.ChangedColumnIndices.Count;
                     index++)
                {
                    State.EnqueueColumnAndNeighbors(
                        changeSet.ChangedColumnIndices[index]);
                }

                waterBodiesDirty = true;
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

            State = new HydrologyState(
                boundWorld,
                WaterBodyResolver.Resolve(boundWorld));
            QueueDynamicWaterColumns();
            waterBodiesDirty = false;
            StateChanged?.Invoke(State);
        }

        private void SimulateTick()
        {
            var result = HydrologySimulation.Step(
                boundWorld,
                State,
                columnsPerTick,
                stableTickThreshold);
            if (result.HasPersistentChanges)
            {
                CommitSimulationChanges(result);
                waterBodiesDirty = true;
            }

            if (!State.HasActiveColumns && waterBodiesDirty)
            {
                State.ReplaceWaterBodies(WaterBodyResolver.Resolve(boundWorld));
                waterBodiesDirty = false;
                StateChanged?.Invoke(State);
            }
        }

        private void ReconcileEditedWater(IReadOnlyList<int> changedCellIndices)
        {
            for (var index = 0; index < changedCellIndices.Count; index++)
            {
                var cellIndex = changedCellIndices[index];
                var coordinate = WorldIndex.DecodeCell(boundWorld, cellIndex);
                var cell = boundWorld.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                boundWorld.WaterState.SetAmount(
                    cellIndex,
                    (byte)(cell.WaterFill * 2));

                var behavior = boundWorld.WaterState.GetBehavior(cellIndex);
                if (behavior == WaterCellBehavior.None && cell.HasWater)
                {
                    behavior = cell.Water == WaterType.Sea
                        ? WaterCellBehavior.FixedReservoir
                        : (cell.Flags & CellFlags.River) != 0
                            ? WaterCellBehavior.FlowDependent
                            : WaterCellBehavior.Reservoir;
                    boundWorld.WaterState.SetBehavior(cellIndex, behavior);
                }
            }
        }

        private void QueueDynamicWaterColumns()
        {
            State.ClearActiveColumns();
            for (var z = 0; z < boundWorld.Size; z++)
            for (var x = 0; x < boundWorld.Size; x++)
            {
                for (var y = boundWorld.Height - 1; y >= 0; y--)
                {
                    var behavior = boundWorld.WaterState.GetBehavior(
                        WorldIndex.EncodeCell(boundWorld, x, y, z));
                    if (behavior == WaterCellBehavior.Source)
                    {
                        State.Enqueue(WorldIndex.EncodeColumn(boundWorld, x, z));
                        break;
                    }
                }
            }
        }

        private void CommitSimulationChanges(HydrologyStepResult result)
        {
            var changedColumns = new int[result.ChangedColumnIndices.Count];
            result.ChangedColumnIndices.CopyTo(changedColumns);
            Array.Sort(changedColumns);
            for (var index = 0; index < changedColumns.Length; index++)
            {
                WorldIndex.DecodeColumn(
                    boundWorld,
                    changedColumns[index],
                    out var x,
                    out var z);
                boundWorld.RebuildSurfaceColumn(x, z);
            }

            var changedCells = new int[result.ChangedCellIndices.Count];
            result.ChangedCellIndices.CopyTo(changedCells);
            Array.Sort(changedCells);
            var affectedChunks = BuildAffectedChunks(changedColumns);
            var changeId = boundWorld.AdvanceChangeId();
            for (var index = 0; index < affectedChunks.Length; index++)
            {
                boundWorld.MarkChunkChanged(affectedChunks[index], changeId);
            }

            var bounds = BuildBounds(changedCells, changedColumns);
            var changeSet = new WorldChangeSet(
                boundWorld,
                changeId,
                result.HasRenderChanges
                    ? WorldChangeType.WaterTopology
                        | WorldChangeType.Navigation
                        | WorldChangeType.Ecology
                    : WorldChangeType.None,
                changedCells,
                changedColumns,
                affectedChunks,
                bounds);
            LastAppliedChangeId = changeId;
            ChangeCommitted?.Invoke(changeSet);
        }

        private ChunkCoordinate[] BuildAffectedChunks(int[] columns)
        {
            var chunks = new HashSet<ChunkCoordinate>();
            for (var index = 0; index < columns.Length; index++)
            {
                WorldIndex.DecodeColumn(boundWorld, columns[index], out var x, out var z);
                var minX = Math.Max(0, x - 1) / boundWorld.ChunkSizeX;
                var maxX = Math.Min(boundWorld.Size - 1, x + 1) / boundWorld.ChunkSizeX;
                var minZ = Math.Max(0, z - 1) / boundWorld.ChunkSizeZ;
                var maxZ = Math.Min(boundWorld.Size - 1, z + 1) / boundWorld.ChunkSizeZ;
                for (var chunkY = 0; chunkY < boundWorld.ChunkCountY; chunkY++)
                for (var chunkZ = minZ; chunkZ <= maxZ; chunkZ++)
                for (var chunkX = minX; chunkX <= maxX; chunkX++)
                {
                    chunks.Add(new ChunkCoordinate(chunkX, chunkY, chunkZ));
                }
            }

            var result = new ChunkCoordinate[chunks.Count];
            chunks.CopyTo(result);
            return result;
        }

        private CellBounds BuildBounds(int[] cells, int[] columns)
        {
            if (cells.Length > 0)
            {
                var first = WorldIndex.DecodeCell(boundWorld, cells[0]);
                var min = first;
                var max = first;
                for (var index = 1; index < cells.Length; index++)
                {
                    var cell = WorldIndex.DecodeCell(boundWorld, cells[index]);
                    min = new CellCoordinate(
                        Math.Min(min.X, cell.X),
                        Math.Min(min.Y, cell.Y),
                        Math.Min(min.Z, cell.Z));
                    max = new CellCoordinate(
                        Math.Max(max.X, cell.X),
                        Math.Max(max.Y, cell.Y),
                        Math.Max(max.Z, cell.Z));
                }

                return new CellBounds(min, max);
            }

            WorldIndex.DecodeColumn(boundWorld, columns[0], out var x, out var z);
            return new CellBounds(
                new CellCoordinate(x, 0, z),
                new CellCoordinate(x, boundWorld.Height - 1, z));
        }
    }
}
