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

    internal sealed class WaterFlowResolver
    {
        private static readonly (int x, int z)[] HorizontalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        private readonly List<CellCoordinate> activeWave = new();
        private readonly HashSet<CellCoordinate> restartWave = new();
        private readonly HashSet<CellCoordinate> nextWave = new();
        private readonly HashSet<CellCoordinate> applyCells = new();
        private readonly Dictionary<CellCoordinate, WaterVisualState>
            previousVisualStates = new();
        private readonly WaterFlowRecalculationResult result = new();
        private readonly int cellCount;
        private int cursor;

        public bool HasWork => activeWave.Count > 0;
        public bool IsWaveInProgress => cursor > 0;
        public int PendingCellCount => Math.Max(0, activeWave.Count - cursor);

        public WaterFlowResolver(int cellCount)
        {
            if (cellCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellCount));
            }

            this.cellCount = cellCount;
        }

        public void RestoreFrontier(
            WorldData world,
            WaterFlowState state,
            IReadOnlyList<CellCoordinate> frontier)
        {
            ValidateWorldAndState(world, state);
            restartWave.Clear();
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

                    restartWave.Add(cell);
                }
            }

            ReplaceActiveWave(world, state, restartWave);
        }

        public void EnqueueChanges(
            WorldData world,
            WaterFlowState state,
            IReadOnlyCollection<CellCoordinate> changedCells,
            IReadOnlyCollection<CellColumnCoordinate> changedColumns)
        {
            ValidateWorldAndState(world, state);
            restartWave.Clear();
            for (var index = 0; index < activeWave.Count; index++)
            {
                restartWave.Add(activeWave[index]);
            }

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
                    if (!world.ContainsColumn(column.X, column.Z))
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

            ReplaceActiveWave(world, state, restartWave);
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
            if (!HasWork)
            {
                state.IsRecalculating = false;
                return false;
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
            ReplaceActiveWave(world, state, nextWave);

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
                current.Amount = WaterAmount.Full;
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
            if (coordinate.Y + 1 < world.Height)
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
                if (!world.Contains(donorX, coordinate.Y, donorZ))
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
            if (WaterAmount.Full <= parameters.SpreadAmountLoss)
            {
                return FlowDirection.None;
            }

            var candidateAmount = checked((byte)(
                WaterAmount.Full - parameters.SpreadAmountLoss));
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
                if (!world.Contains(targetX, source.Y, targetZ))
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

            var belowCell = new CellCoordinate(
                coordinate.X,
                coordinate.Y - 1,
                coordinate.Z);
            return state.GetWater(belowCell).Role
                == WaterRole.Source;
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

            var below = world.GetCell(
                coordinate.X,
                coordinate.Y - 1,
                coordinate.Z);
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

            var below = world.GetCell(
                coordinate.X,
                coordinate.Y - 1,
                coordinate.Z);
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
            var donorCell = world.GetCell(
                donorCoordinate.X,
                donorCoordinate.Y,
                donorCoordinate.Z);
            var targetCell = world.GetCell(
                targetCoordinate.X,
                targetCoordinate.Y,
                targetCoordinate.Z);
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
                if (!world.Contains(targetX, donor.Y, targetZ))
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
                var cell = world.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
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

        private void ReplaceActiveWave(
            WorldData world,
            WaterFlowState state,
            IReadOnlyCollection<CellCoordinate> cells)
        {
            state.CancelResolutionPass();
            activeWave.Clear();
            if (cells != null && cells.Count > 0)
            {
                foreach (var cell in cells)
                {
                    activeWave.Add(cell);
                }

                activeWave.Sort();
            }

            cursor = 0;
            world.WaterFlowSchedule.ReplaceFrontier(activeWave);
            state.IsRecalculating = activeWave.Count > 0;
        }

        private static void AddCellAndNeighbors(
            WorldData world,
            HashSet<CellCoordinate> cells,
            CellCoordinate coordinate)
        {
            cells.Add(coordinate);
            AddIfContained(coordinate.X + 1, coordinate.Y, coordinate.Z);
            AddIfContained(coordinate.X - 1, coordinate.Y, coordinate.Z);
            AddIfContained(coordinate.X, coordinate.Y + 1, coordinate.Z);
            AddIfContained(coordinate.X, coordinate.Y - 1, coordinate.Z);
            AddIfContained(coordinate.X, coordinate.Y, coordinate.Z + 1);
            AddIfContained(coordinate.X, coordinate.Y, coordinate.Z - 1);

            void AddIfContained(int x, int y, int z)
            {
                if (world.Contains(x, y, z))
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

            if (state.CellCount != cellCount)
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
            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater
                    || cell.Water.Role != WaterRole.Source)
                {
                    continue;
                }

                AddSourceAndNeighbors(
                    new CellCoordinate(x, y, z));
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
                if (world.Contains(x, y, z))
                {
                    frontier.Add(new CellCoordinate(x, y, z));
                }
            }
        }
    }
}
