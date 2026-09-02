using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    internal readonly struct WaterFlowParameters : IEquatable<WaterFlowParameters>
    {
        public readonly byte SpreadAmountLoss;
        public readonly byte MinimumSpreadAmount;
        public readonly byte DissipationAmountLoss;

        public WaterFlowParameters(
            float spreadAmountLoss,
            float minimumSpreadAmount,
            float dissipationAmountLoss)
        {
            SpreadAmountLoss = WaterAmount.FromNormalized(spreadAmountLoss);
            MinimumSpreadAmount = WaterAmount.FromNormalized(
                minimumSpreadAmount);
            DissipationAmountLoss = WaterAmount.FromNormalized(
                dissipationAmountLoss);
        }

        public WaterFlowParameters(in WaterFlowRules rules)
        {
            SpreadAmountLoss = rules.SpreadAmountLoss;
            MinimumSpreadAmount = rules.MinimumSpreadAmount;
            DissipationAmountLoss = rules.DissipationAmountLoss;
        }

        public bool Equals(WaterFlowParameters other) =>
            SpreadAmountLoss == other.SpreadAmountLoss
            && MinimumSpreadAmount == other.MinimumSpreadAmount
            && DissipationAmountLoss == other.DissipationAmountLoss;

        public override bool Equals(object obj) =>
            obj is WaterFlowParameters other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            SpreadAmountLoss,
            MinimumSpreadAmount,
            DissipationAmountLoss);
    }

    internal readonly struct WaterVisualState : IEquatable<WaterVisualState>
    {
        private readonly byte waterFill;
        private readonly WaterRole waterRole;
        private readonly WaterType waterType;
        private readonly byte connectionMask;
        private readonly FlowDirection direction;

        private WaterVisualState(
            byte waterFill,
            WaterRole waterRole,
            WaterType waterType,
            byte connectionMask,
            FlowDirection direction)
        {
            this.waterFill = waterFill;
            this.waterRole = waterRole;
            this.waterType = waterType;
            this.connectionMask = connectionMask;
            this.direction = direction;
        }

        public static WaterVisualState Resolve(
            WorldData world,
            CellCoordinate coordinate)
        {
            var cell = world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            if (!cell.HasWater)
            {
                return default;
            }

            byte connections = 0;
            AddConnection(1, 0, 0, 1 << 0);
            AddConnection(-1, 0, 0, 1 << 1);
            AddConnection(0, 1, 0, 1 << 2);
            AddConnection(0, -1, 0, 1 << 3);
            AddConnection(0, 0, 1, 1 << 4);
            AddConnection(0, 0, -1, 1 << 5);
            return new WaterVisualState(
                cell.WaterHeight,
                cell.Water.Role,
                cell.Water.Type,
                connections,
                cell.Water.Flow);

            void AddConnection(
                int offsetX,
                int offsetY,
                int offsetZ,
                int mask)
            {
                if (world.TryGetCell(
                        coordinate.X + offsetX,
                        coordinate.Y + offsetY,
                        coordinate.Z + offsetZ,
                        out var neighbor)
                    && neighbor.HasWater)
                {
                    connections |= (byte)mask;
                }
            }
        }

        public bool Equals(WaterVisualState other) =>
            waterFill == other.waterFill
            && waterRole == other.waterRole
            && waterType == other.waterType
            && connectionMask == other.connectionMask
            && direction == other.direction;

        public override bool Equals(object obj) =>
            obj is WaterVisualState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            waterFill,
            waterRole,
            waterType,
            connectionMask,
            direction);
    }

    internal sealed class WaterFlowRecalculationResult
    {
        public readonly HashSet<CellCoordinate> LogicalChangedCells = new();
        public readonly HashSet<CellCoordinate> RenderChangedCells = new();
        public readonly HashSet<CellCoordinate> TopologyChangedCells = new();
        public readonly HashSet<CellCoordinate> WaterTypeChangedCells = new();
        public readonly HashSet<CellColumnCoordinate> ChangedColumns = new();

        public bool HasRenderChanges =>
            RenderChangedCells.Count > 0;
        public bool HasTopologyChanges =>
            TopologyChangedCells.Count > 0;

        public void Clear()
        {
            LogicalChangedCells.Clear();
            RenderChangedCells.Clear();
            TopologyChangedCells.Clear();
            WaterTypeChangedCells.Clear();
            ChangedColumns.Clear();
        }
    }

    internal sealed class ChunkWaterFlowState
    {
        public ChunkCoordinate Coordinate { get; }
        public HashSet<CellCoordinate> Frontier { get; } = new();

        public ChunkWaterFlowState(ChunkCoordinate coordinate)
        {
            Coordinate = coordinate;
        }
    }

    internal sealed class WaterFlowResolver
    {
        private static readonly (int x, int z)[] HorizontalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        private readonly List<CellCoordinate> activeWave = new();
        private readonly List<CellCoordinate> selectedCells = new();
        private readonly List<ChunkCoordinate> emptyChunks = new();
        private readonly Dictionary<ChunkCoordinate, ChunkWaterFlowState>
            chunkStates = new();
        private readonly HashSet<CellCoordinate> restartWave = new();
        private readonly HashSet<CellCoordinate> nextWave = new();
        private readonly HashSet<CellCoordinate> applyCells = new();
        private readonly Dictionary<CellCoordinate, WaterVisualState>
            previousVisualStates = new();
        private readonly WaterFlowRecalculationResult result = new();
        private readonly int chunkSizeXZ;
        private readonly Func<CellCoordinate, bool> canProcessCell;
        private bool hasRunnableFrontier;
        private int cursor;

        public bool HasWork => activeWave.Count > 0 || chunkStates.Count > 0;
        public bool HasRunnableWork => activeWave.Count > 0
            || hasRunnableFrontier;
        public bool IsWaveInProgress => cursor > 0;
        public int PendingCellCount
        {
            get
            {
                var count = Math.Max(0, activeWave.Count - cursor);
                foreach (var state in chunkStates.Values)
                {
                    count += state.Frontier.Count;
                }

                return count;
            }
        }

        public WaterFlowResolver(
            int chunkSizeXZ,
            Func<CellCoordinate, bool> canProcessCell = null)
        {
            if (chunkSizeXZ <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeXZ));
            }

            this.chunkSizeXZ = chunkSizeXZ;
            this.canProcessCell = canProcessCell;
        }

        public void RestoreFrontier(
            WorldData world,
            WaterFlowState state,
            IReadOnlyList<CellCoordinate> frontier)
        {
            ValidateWorldAndState(world, state);
            CancelActiveWave(state, requeue: false);
            chunkStates.Clear();
            if (frontier != null)
            {
                for (var index = 0; index < frontier.Count; index++)
                {
                    var cell = frontier[index];
                    if (!world.Contains(cell.X, cell.Y, cell.Z))
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(frontier),
                            "A water frontier Cell is outside the world.");
                    }

                    AddFrontier(cell);
                }
            }

            RefreshRunnableFrontier();
            PersistFrontier(world, state);
        }

        public void EnqueueChanges(
            WorldData world,
            WaterFlowState state,
            IReadOnlyCollection<CellCoordinate> changedCells,
            IReadOnlyCollection<CellColumnCoordinate> changedColumns)
        {
            ValidateWorldAndState(world, state);
            CancelActiveWave(state, requeue: true);
            restartWave.Clear();

            if (changedCells != null)
            {
                foreach (var cell in changedCells)
                {
                    if (!world.Contains(cell.X, cell.Y, cell.Z))
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(changedCells));
                    }

                    AddCellAndNeighbors(world, restartWave, cell);
                }
            }

            if (changedColumns != null)
            {
                foreach (var column in changedColumns)
                {
                    if (!world.IsColumnLoaded(column.X, column.Z))
                    {
                        continue;
                    }

                    for (var y = 0; y < world.Height; y++)
                    {
                        AddCellAndNeighbors(
                            world,
                            restartWave,
                            new CellCoordinate(column.X, y, column.Z));
                    }
                }
            }

            AddFrontier(restartWave);
            RefreshRunnableFrontier();
            PersistFrontier(world, state);
        }

        public void OnSimulationSetChanged(
            WorldData world,
            WaterFlowState state)
        {
            ValidateWorldAndState(world, state);
            CancelActiveWave(state, requeue: true);
            RefreshRunnableFrontier();
            PersistFrontier(world, state);
        }

        public bool Step(
            WorldData world,
            WaterFlowState state,
            in WaterFlowParameters parameters,
            int maximumCells,
            out WaterFlowRecalculationResult completedResult)
        {
            ValidateWorldAndState(world, state);
            if (maximumCells <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCells));
            }

            completedResult = null;
            if (activeWave.Count == 0)
            {
                BuildActiveWave();
                if (activeWave.Count == 0)
                {
                    state.IsRecalculating = false;
                    return false;
                }
            }

            if (cursor == 0)
            {
                result.Clear();
                state.CancelResolutionPass();
            }

            var stop = Math.Min(activeWave.Count, cursor + maximumCells);
            for (; cursor < stop; cursor++)
            {
                var cell = activeWave[cursor];
                state.StageResolvedCell(
                    cell,
                    ResolveDesiredWater(
                        world,
                        state,
                        cell,
                        parameters));
            }

            if (cursor < activeWave.Count)
            {
                return false;
            }

            BuildApplySet(world, state);
            ApplyStagedState(world, state);
            BuildNextWave(world);
            activeWave.Clear();
            cursor = 0;
            AddFrontier(nextWave);
            RefreshRunnableFrontier();
            PersistFrontier(world, state);

            completedResult = result;
            return true;
        }

        private static WaterData ResolveDesiredWater(
            WorldData world,
            WaterFlowState state,
            CellCoordinate coordinate,
            in WaterFlowParameters parameters)
        {
            var cell = world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            var current = state.GetWater(coordinate);
            var capacitySteps =
                WorldGrid.HeightStepsPerCell - cell.Terrain.SolidHeight;
            if (capacitySteps <= 0)
            {
                return default;
            }

            if (current.HasWater && current.Role == WaterRole.Source)
            {
                current.Flow = FlowDirection.None;

                if (CanFlowDown(world, state, coordinate))
                {
                    current.Flow |= FlowDirection.Down;
                }
                else
                {
                    current.Flow |= ResolveSourceOutflowDirections(
                        world,
                        state,
                        coordinate,
                        parameters);
                }

                current.Normalize();
                return current;
            }

            var desired = default(WaterData);
            var hasHorizontalInflow = false;
            var connectsToSourceBelow = IsSourceImmediatelyBelow(
                world,
                state,
                coordinate);
            if (coordinate.Y + 1 < world.Height
                && world.IsColumnLoaded(coordinate.X, coordinate.Z))
            {
                var aboveCell = new CellCoordinate(
                    coordinate.X,
                    coordinate.Y + 1,
                    coordinate.Z);
                var above = state.GetWater(aboveCell);
                if (above.Amount >= parameters.MinimumSpreadAmount
                    && CanFlowDown(
                        world,
                        state,
                        new CellCoordinate(
                            coordinate.X,
                            coordinate.Y + 1,
                            coordinate.Z)))
                {
                    desired = CreateDynamicWater(
                        above.Amount,
                        (above.Flow
                            & FlowDirection.Horizontal)
                        | FlowDirection.Down,
                        above.Type);
                }
            }

            for (var directionIndex = 0;
                 directionIndex < HorizontalDirections.Length;
                 directionIndex++)
            {
                var offset = HorizontalDirections[directionIndex];
                var donorX = coordinate.X - offset.x;
                var donorZ = coordinate.Z - offset.z;
                if (!world.IsColumnLoaded(donorX, donorZ))
                {
                    continue;
                }

                var donorCell = new CellCoordinate(
                    donorX,
                    coordinate.Y,
                    donorZ);
                var donor = state.GetWater(donorCell);
                if (donor.Amount <= parameters.SpreadAmountLoss
                    || donor.Amount < parameters.MinimumSpreadAmount
                    || (donor.Role == WaterRole.Dynamic
                        && (donor.Falls
                            || IsSourceImmediatelyBelow(
                                world,
                                state,
                                new CellCoordinate(
                                    donorX,
                                    coordinate.Y,
                                    donorZ))))
                    || CanFlowDown(
                        world,
                        state,
                        new CellCoordinate(
                            donorX,
                            coordinate.Y,
                            donorZ)))
                {
                    continue;
                }

                var candidateAmount = checked((byte)(
                    donor.Amount - parameters.SpreadAmountLoss));
                if (candidateAmount < parameters.MinimumSpreadAmount
                    || !CanReachHorizontally(
                        world,
                        state,
                        donorCell,
                        coordinate,
                        candidateAmount))
                {
                    continue;
                }

                var outgoingDirection = ToDirection(offset.x, offset.z);
                var donorHeading = donor.Flow
                    & FlowDirection.Horizontal;
                var targetDescends = HasVerticalDropBelow(
                    world,
                    coordinate);
                if (donor.Role == WaterRole.Dynamic
                    && IsSingleDirection(donorHeading)
                    && donorHeading != outgoingDirection
                    && !targetDescends
                    && HasReachablePreferredDirection(
                        world,
                        state,
                        donorCell,
                        donorHeading,
                        candidateAmount))
                {
                    continue;
                }

                if (candidateAmount > desired.Amount)
                {
                    desired = CreateDynamicWater(
                        candidateAmount,
                        outgoingDirection,
                        donor.Type);
                    hasHorizontalInflow = true;
                }
                else if (candidateAmount == desired.Amount
                         && candidateAmount > 0)
                {
                    desired.Flow |= outgoingDirection;
                    desired.Type = MergeWaterType(
                        desired.Type,
                        donor.Type);
                    hasHorizontalInflow = true;
                }
            }

            if (desired.Amount > 0)
            {
                if (connectsToSourceBelow
                    || CanFlowDown(world, state, coordinate)
                    || (hasHorizontalInflow
                        && HasVerticalDropBelow(world, coordinate)))
                {
                    desired.Flow |= FlowDirection.Down;
                }
                else
                {
                    desired.Flow &= FlowDirection.Horizontal;
                }

                desired.Normalize();
            }

            if (connectsToSourceBelow
                && current.Role == WaterRole.Dynamic)
            {
                current.Flow =
                    (current.Flow & FlowDirection.Horizontal)
                    | FlowDirection.Down;
                current.Normalize();
            }

            return ApplyDissipation(current, desired, parameters);
        }

        private static FlowDirection ResolveSourceOutflowDirections(
            WorldData world,
            WaterFlowState state,
            CellCoordinate source,
            in WaterFlowParameters parameters)
        {
            var sourceWater = state.GetWater(source);
            if (sourceWater.Amount <= parameters.SpreadAmountLoss)
            {
                return FlowDirection.None;
            }

            var candidateAmount = checked((byte)(
                sourceWater.Amount - parameters.SpreadAmountLoss));
            if (candidateAmount < parameters.MinimumSpreadAmount)
            {
                return FlowDirection.None;
            }

            var result = FlowDirection.None;
            for (var directionIndex = 0;
                 directionIndex < HorizontalDirections.Length;
                 directionIndex++)
            {
                var offset = HorizontalDirections[directionIndex];
                var targetX = source.X + offset.x;
                var targetZ = source.Z + offset.z;
                if (!world.IsColumnLoaded(targetX, targetZ))
                {
                    continue;
                }

                var targetCell = new CellCoordinate(
                    targetX,
                    source.Y,
                    targetZ);
                var targetWater = state.GetWater(targetCell);
                if (targetWater.Role == WaterRole.Source
                    || targetWater.Amount > candidateAmount
                    || !CanReachHorizontally(
                        world,
                        state,
                        source,
                        targetCell,
                        candidateAmount))
                {
                    continue;
                }

                result |= ToDirection(offset.x, offset.z);
            }

            return result;
        }

        private static bool IsSourceImmediatelyBelow(
            WorldData world,
            WaterFlowState state,
            CellCoordinate coordinate)
        {
            if (coordinate.Y <= 0)
            {
                return false;
            }

            return world.TryGetCell(
                    coordinate.X,
                    coordinate.Y - 1,
                    coordinate.Z,
                    out var below)
                && below.Water.Role == WaterRole.Source;
        }

        private static WaterData ApplyDissipation(
            WaterData current,
            WaterData desired,
            in WaterFlowParameters parameters)
        {
            if (current.Role != WaterRole.Dynamic
                || desired.Amount >= current.Amount)
            {
                return desired;
            }

            var reducedAmount = Math.Max(
                desired.Amount,
                current.Amount - parameters.DissipationAmountLoss);
            if (reducedAmount < parameters.MinimumSpreadAmount)
            {
                return default;
            }

            if (desired.Amount == 0)
            {
                current.Amount = checked((byte)reducedAmount);
                current.Normalize();
                return current;
            }

            desired.Amount = checked((byte)reducedAmount);
            desired.Normalize();
            return desired;
        }

        private static WaterData CreateDynamicWater(
            byte amount,
            FlowDirection direction,
            WaterType type) => new()
        {
            Amount = amount,
            Role = WaterRole.Dynamic,
            Type = type,
            Flow = direction
        };

        private static WaterType MergeWaterType(
            WaterType current,
            WaterType candidate)
        {
            if (current == candidate || candidate == WaterType.None)
            {
                return current;
            }

            if (current == WaterType.None)
            {
                return candidate;
            }

            return TypePriority(candidate) > TypePriority(current)
                ? candidate
                : current;
        }

        private static int TypePriority(WaterType type) => type switch
        {
            WaterType.River => 4,
            WaterType.Sea => 3,
            WaterType.Lake => 2,
            WaterType.Pond => 1,
            _ => 0
        };

        private static bool CanFlowDown(
            WorldData world,
            WaterFlowState state,
            CellCoordinate coordinate)
        {
            if (coordinate.Y <= 0)
            {
                return false;
            }

            if (!world.TryGetCell(
                    coordinate.X,
                    coordinate.Y - 1,
                    coordinate.Z,
                    out var below))
            {
                return false;
            }
            if (below.Terrain.SolidHeight >= WorldGrid.HeightStepsPerCell)
            {
                return false;
            }

            var belowCell = new CellCoordinate(
                coordinate.X,
                coordinate.Y - 1,
                coordinate.Z);
            var belowWater = state.GetWater(belowCell);
            return WaterFlowReachability.CanFlowDown(
                coordinate.Y,
                below,
                belowWater);
        }

        private static bool HasVerticalDropBelow(
            WorldData world,
            CellCoordinate coordinate)
        {
            if (coordinate.Y <= 0)
            {
                return false;
            }

            if (!world.TryGetCell(
                    coordinate.X,
                    coordinate.Y - 1,
                    coordinate.Z,
                    out var below))
            {
                return false;
            }
            return WaterFlowReachability.HasVerticalDropBelow(
                coordinate.Y,
                below);
        }

        private static bool CanReachHorizontally(
            WorldData world,
            WaterFlowState state,
            CellCoordinate donorCoordinate,
            CellCoordinate targetCoordinate,
            byte candidateAmount)
        {
            if (!world.TryGetCell(
                    donorCoordinate.X,
                    donorCoordinate.Y,
                    donorCoordinate.Z,
                    out var donorCell)
                || !world.TryGetCell(
                    targetCoordinate.X,
                    targetCoordinate.Y,
                    targetCoordinate.Z,
                    out var targetCell))
            {
                return false;
            }
            var donorWater = state.GetWater(donorCoordinate);
            return WaterFlowReachability.CanReachHorizontally(
                donorCoordinate,
                donorCell,
                donorWater,
                targetCoordinate,
                targetCell,
                candidateAmount);
        }

        private static bool HasReachablePreferredDirection(
            WorldData world,
            WaterFlowState state,
            CellCoordinate donor,
            FlowDirection preferredDirection,
            byte candidateAmount)
        {
            for (var index = 0;
                 index < HorizontalDirections.Length;
                 index++)
            {
                var offset = HorizontalDirections[index];
                if (ToDirection(offset.x, offset.z) != preferredDirection)
                {
                    continue;
                }

                var targetX = donor.X + offset.x;
                var targetZ = donor.Z + offset.z;
                if (!world.IsColumnLoaded(targetX, targetZ))
                {
                    return false;
                }

                return CanReachHorizontally(
                    world,
                    state,
                    donor,
                    new CellCoordinate(
                        targetX,
                        donor.Y,
                        targetZ),
                    candidateAmount);
            }

            return false;
        }

        private static bool IsSingleDirection(
            FlowDirection direction)
        {
            var value = (byte)(direction & FlowDirection.Horizontal);
            return value != 0 && (value & (value - 1)) == 0;
        }

        private void BuildApplySet(
            WorldData world,
            WaterFlowState state)
        {
            applyCells.Clear();
            previousVisualStates.Clear();
            foreach (var pair in state.EnumerateStagedCells())
            {
                AddCellAndNeighbors(world, applyCells, pair.Key);
            }

            foreach (var cell in applyCells)
            {
                previousVisualStates[cell] =
                    WaterVisualState.Resolve(world, cell);
            }
        }

        private void ApplyStagedState(
            WorldData world,
            WaterFlowState state)
        {
            foreach (var pair in state.EnumerateStagedCells())
            {
                var coordinate = pair.Key;
                if (!world.TryGetCell(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z,
                        out var cell))
                {
                    continue;
                }
                var previousWater = cell.Water;
                if (previousWater.Equals(pair.Value))
                {
                    continue;
                }

                cell.Water = pair.Value;
                cell.Normalize();
                world.SetCellForEdit(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    cell);
                result.LogicalChangedCells.Add(pair.Key);
                result.ChangedColumns.Add(new CellColumnCoordinate(
                    coordinate.X,
                    coordinate.Z));
                if (previousWater.HasWater != cell.Water.HasWater)
                {
                    result.TopologyChangedCells.Add(pair.Key);
                }
            }

            foreach (var cell in applyCells)
            {
                var next = WaterVisualState.Resolve(world, cell);
                if (!previousVisualStates.TryGetValue(
                        cell,
                        out var previous)
                    || !previous.Equals(next))
                {
                    result.RenderChangedCells.Add(cell);
                }
            }

            state.CancelResolutionPass();
        }

        private void BuildNextWave(WorldData world)
        {
            nextWave.Clear();
            foreach (var cell in result.LogicalChangedCells)
            {
                AddCellAndNeighbors(world, nextWave, cell);
            }
        }

        private void BuildActiveWave()
        {
            activeWave.Clear();
            emptyChunks.Clear();
            foreach (var pair in chunkStates)
            {
                var chunkState = pair.Value;
                selectedCells.Clear();
                foreach (var cell in chunkState.Frontier)
                {
                    if (CanProcess(cell))
                    {
                        selectedCells.Add(cell);
                    }
                }

                for (var index = 0; index < selectedCells.Count; index++)
                {
                    var cell = selectedCells[index];
                    chunkState.Frontier.Remove(cell);
                    activeWave.Add(cell);
                }

                if (chunkState.Frontier.Count == 0)
                {
                    emptyChunks.Add(pair.Key);
                }
            }

            for (var index = 0; index < emptyChunks.Count; index++)
            {
                chunkStates.Remove(emptyChunks[index]);
            }

            activeWave.Sort();
            cursor = 0;
            RefreshRunnableFrontier();
        }

        private void CancelActiveWave(
            WaterFlowState state,
            bool requeue)
        {
            state.CancelResolutionPass();
            if (requeue)
            {
                AddFrontier(activeWave);
            }

            activeWave.Clear();
            cursor = 0;
            result.Clear();
        }

        private void AddFrontier(
            IReadOnlyCollection<CellCoordinate> cells)
        {
            if (cells == null)
            {
                return;
            }

            foreach (var cell in cells)
            {
                AddFrontier(cell);
            }
        }

        private void AddFrontier(CellCoordinate cell)
        {
            var chunk = ToChunk(cell);
            if (!chunkStates.TryGetValue(chunk, out var state))
            {
                state = new ChunkWaterFlowState(chunk);
                chunkStates.Add(chunk, state);
            }

            state.Frontier.Add(cell);
        }

        private void RefreshRunnableFrontier()
        {
            hasRunnableFrontier = false;
            foreach (var state in chunkStates.Values)
            {
                foreach (var cell in state.Frontier)
                {
                    if (!CanProcess(cell))
                    {
                        continue;
                    }

                    hasRunnableFrontier = true;
                    return;
                }
            }
        }

        private void PersistFrontier(
            WorldData world,
            WaterFlowState state)
        {
            restartWave.Clear();
            for (var index = 0; index < activeWave.Count; index++)
            {
                restartWave.Add(activeWave[index]);
            }

            foreach (var chunkState in chunkStates.Values)
            {
                restartWave.UnionWith(chunkState.Frontier);
            }

            var frontier = new CellCoordinate[restartWave.Count];
            restartWave.CopyTo(frontier);
            Array.Sort(frontier);
            world.WaterFlowSchedule.ReplaceFrontier(frontier);
            state.IsRecalculating = HasRunnableWork;
        }

        private bool CanProcess(CellCoordinate cell) =>
            canProcessCell == null || canProcessCell(cell);

        private ChunkCoordinate ToChunk(CellCoordinate cell) =>
            WorldCoordinateUtility.ToChunk(
                cell.X,
                cell.Z,
                chunkSizeXZ);

        private static void AddCellAndNeighbors(
            WorldData world,
            HashSet<CellCoordinate> cells,
            CellCoordinate coordinate)
        {
            AddIfLoaded(coordinate.X, coordinate.Y, coordinate.Z);
            AddIfContained(coordinate.X + 1, coordinate.Y, coordinate.Z);
            AddIfContained(coordinate.X - 1, coordinate.Y, coordinate.Z);
            AddIfContained(coordinate.X, coordinate.Y + 1, coordinate.Z);
            AddIfContained(coordinate.X, coordinate.Y - 1, coordinate.Z);
            AddIfContained(coordinate.X, coordinate.Y, coordinate.Z + 1);
            AddIfContained(coordinate.X, coordinate.Y, coordinate.Z - 1);

            void AddIfContained(int x, int y, int z)
            {
                AddIfLoaded(x, y, z);
            }

            void AddIfLoaded(int x, int y, int z)
            {
                if (world.TryGetCell(x, y, z, out _))
                {
                    cells.Add(new CellCoordinate(x, y, z));
                }
            }
        }

        private static FlowDirection ToDirection(int x, int z)
        {
            if (x > 0) return FlowDirection.East;
            if (x < 0) return FlowDirection.West;
            if (z > 0) return FlowDirection.North;
            if (z < 0) return FlowDirection.South;
            return FlowDirection.None;
        }

        private void ValidateWorldAndState(
            WorldData world,
            WaterFlowState state)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (!state.BelongsTo(world))
            {
                throw new InvalidOperationException(
                    "The water resolver belongs to a different world.");
            }
        }
    }

    internal static class WaterFlowSolver
    {
        public static void PrepareGeneratedWorld(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var frontier = new HashSet<CellCoordinate>();
            foreach (var chunk in world.EnumerateLoadedChunks())
            {
                var startX = chunk.Coordinate.X * world.ChunkSizeX;
                var startZ = chunk.Coordinate.Z * world.ChunkSizeZ;
                for (var y = 0; y < world.Height; y++)
                for (var localZ = 0; localZ < world.ChunkSizeZ; localZ++)
                for (var localX = 0; localX < world.ChunkSizeX; localX++)
                {
                    var x = startX + localX;
                    var z = startZ + localZ;
                    var cell = world.GetCell(x, y, z);
                    if (!cell.HasWater
                        || cell.Water.Role != WaterRole.Source)
                    {
                        continue;
                    }

                    var coordinate = new CellCoordinate(x, y, z);
                    if (WaterSourceFrontierSelector.IsNeeded(
                            world,
                            coordinate))
                    {
                        AddSourceAndNeighbors(coordinate);
                    }
                }
            }

            var sortedFrontier = new CellCoordinate[frontier.Count];
            frontier.CopyTo(sortedFrontier);
            Array.Sort(sortedFrontier);
            world.WaterFlowSchedule.ReplaceFrontier(sortedFrontier);

            void AddSourceAndNeighbors(CellCoordinate coordinate)
            {
                frontier.Add(coordinate);
                AddIfContained(coordinate.X + 1, coordinate.Y, coordinate.Z);
                AddIfContained(coordinate.X - 1, coordinate.Y, coordinate.Z);
                AddIfContained(coordinate.X, coordinate.Y + 1, coordinate.Z);
                AddIfContained(coordinate.X, coordinate.Y - 1, coordinate.Z);
                AddIfContained(coordinate.X, coordinate.Y, coordinate.Z + 1);
                AddIfContained(coordinate.X, coordinate.Y, coordinate.Z - 1);
            }

            void AddIfContained(int x, int y, int z)
            {
                if (world.TryGetCell(x, y, z, out _))
                {
                    frontier.Add(new CellCoordinate(x, y, z));
                }
            }
        }
    }

    internal static class WaterSourceFrontierSelector
    {
        private static readonly (int x, int z)[] HorizontalDirections =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        public static bool IsNeeded(
            WorldData world,
            CellCoordinate coordinate)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.TryGetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    out var sourceCell)
                || !sourceCell.HasWater
                || sourceCell.Water.Role != WaterRole.Source)
            {
                return false;
            }

            if (coordinate.Y > 0
                && world.TryGetCell(
                    coordinate.X,
                    coordinate.Y - 1,
                    coordinate.Z,
                    out var below)
                && WaterFlowReachability.CanFlowDown(
                    coordinate.Y,
                    below,
                    below.Water))
            {
                return true;
            }

            var spreadAmount = (byte)Math.Max(
                0,
                sourceCell.Water.Amount
                - world.Settings.WaterFlowRules.SpreadAmountLoss);
            for (var index = 0; index < HorizontalDirections.Length; index++)
            {
                var direction = HorizontalDirections[index];
                if (!world.TryGetCell(
                        coordinate.X + direction.x,
                        coordinate.Y,
                        coordinate.Z + direction.z,
                        out var neighbor))
                {
                    continue;
                }

                if (neighbor.HasWater
                    && neighbor.Water.Role == WaterRole.Source
                    && neighbor.Water.Type == sourceCell.Water.Type)
                {
                    continue;
                }

                if (WaterFlowReachability.CanReachHorizontally(
                        coordinate,
                        sourceCell,
                        sourceCell.Water,
                        new CellCoordinate(
                            coordinate.X + direction.x,
                            coordinate.Y,
                            coordinate.Z + direction.z),
                        neighbor,
                        spreadAmount))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
