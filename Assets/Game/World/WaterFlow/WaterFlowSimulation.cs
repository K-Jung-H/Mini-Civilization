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
        private readonly WaterCellRole waterRole;
        private readonly byte connectionMask;
        private readonly WaterFlowDirectionMask direction;

        private WaterVisualState(
            byte waterFill,
            WaterCellRole waterRole,
            byte connectionMask,
            WaterFlowDirectionMask direction)
        {
            this.waterFill = waterFill;
            this.waterRole = waterRole;
            this.connectionMask = connectionMask;
            this.direction = direction;
        }

        public static WaterVisualState Resolve(
            WorldData world,
            int cellIndex)
        {
            var coordinate = WorldIndex.DecodeCell(world, cellIndex);
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
                cell.WaterFill,
                cell.Water.Role,
                connections,
                cell.Water.Direction);

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
            && connectionMask == other.connectionMask
            && direction == other.direction;

        public override bool Equals(object obj) =>
            obj is WaterVisualState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            waterFill,
            waterRole,
            connectionMask,
            direction);
    }

    internal sealed class WaterFlowRecalculationResult
    {
        public readonly HashSet<int> LogicalChangedCellIndices = new();
        public readonly HashSet<int> RenderChangedCellIndices = new();
        public readonly HashSet<int> TopologyChangedCellIndices = new();
        public readonly HashSet<int> ChangedColumnIndices = new();

        public bool HasRenderChanges =>
            RenderChangedCellIndices.Count > 0;
        public bool HasTopologyChanges =>
            TopologyChangedCellIndices.Count > 0;

        public void Clear()
        {
            LogicalChangedCellIndices.Clear();
            RenderChangedCellIndices.Clear();
            TopologyChangedCellIndices.Clear();
            ChangedColumnIndices.Clear();
        }
    }

    /// <summary>
    /// Calculates one atomic water wave over one or more frames. The current
    /// wave is persisted in WorldData; staged Cells are never visible or saved.
    /// </summary>
    internal sealed class WaterFlowResolver
    {
        private static readonly (int x, int z)[] HorizontalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        private readonly List<int> activeWave = new();
        private readonly HashSet<int> restartWave = new();
        private readonly HashSet<int> nextWave = new();
        private readonly HashSet<int> applyCells = new();
        private readonly Dictionary<int, WaterVisualState>
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
            IReadOnlyList<int> frontier)
        {
            ValidateWorldAndState(world, state);
            restartWave.Clear();
            if (frontier != null)
            {
                for (var index = 0; index < frontier.Count; index++)
                {
                    var cellIndex = frontier[index];
                    if ((uint)cellIndex >= cellCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(frontier),
                            "A water frontier Cell is outside the world.");
                    }

                    restartWave.Add(cellIndex);
                }
            }

            ReplaceActiveWave(world, state, restartWave);
        }

        public void EnqueueChanges(
            WorldData world,
            WaterFlowState state,
            IReadOnlyCollection<int> changedCellIndices,
            IReadOnlyCollection<int> changedColumnIndices)
        {
            ValidateWorldAndState(world, state);
            restartWave.Clear();
            for (var index = 0; index < activeWave.Count; index++)
            {
                restartWave.Add(activeWave[index]);
            }

            if (changedCellIndices != null)
            {
                foreach (var cellIndex in changedCellIndices)
                {
                    if ((uint)cellIndex >= cellCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            nameof(changedCellIndices));
                    }

                    AddCellAndNeighbors(world, restartWave, cellIndex);
                }
            }

            if (changedColumnIndices != null)
            {
                foreach (var columnIndex in changedColumnIndices)
                {
                    WorldIndex.DecodeColumn(
                        world,
                        columnIndex,
                        out var x,
                        out var z);
                    for (var y = 0; y < world.Height; y++)
                    {
                        AddCellAndNeighbors(
                            world,
                            restartWave,
                            WorldIndex.EncodeCell(world, x, y, z));
                    }
                }
            }

            ReplaceActiveWave(world, state, restartWave);
        }

        /// <summary>
        /// Returns true only when the current wave was fully calculated and
        /// committed. A false result means the same atomic wave continues next
        /// frame and WorldData remains unchanged.
        /// </summary>
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
                var cellIndex = activeWave[cursor];
                state.StageResolvedCell(
                    cellIndex,
                    ResolveDesiredWater(
                        world,
                        state,
                        cellIndex,
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

        private static WaterCellData ResolveDesiredWater(
            WorldData world,
            WaterFlowState state,
            int cellIndex,
            in WaterFlowParameters parameters)
        {
            var coordinate = WorldIndex.DecodeCell(world, cellIndex);
            var cell = world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            var current = state.GetWater(cellIndex);
            var capacitySteps =
                WorldGrid.HeightStepsPerCell - cell.SolidFill;
            if (capacitySteps <= 0)
            {
                return default;
            }

            if (current.Role == WaterCellRole.Source)
            {
                current.Amount = WaterAmount.Full;
                current.Direction = WaterFlowDirectionMask.None;
                if (CanFlowDown(world, state, coordinate))
                {
                    current.Direction |= WaterFlowDirectionMask.Down;
                }
                else
                {
                    current.Direction |= ResolveSourceOutflowDirections(
                        world,
                        state,
                        cellIndex,
                        parameters);
                }

                current.Normalize();
                return current;
            }

            var desired = default(WaterCellData);
            var hasHorizontalInflow = false;
            var connectsToSourceBelow = IsSourceImmediatelyBelow(
                world,
                state,
                coordinate);
            if (coordinate.Y + 1 < world.Height)
            {
                var aboveIndex = WorldIndex.EncodeCell(
                    world,
                    coordinate.X,
                    coordinate.Y + 1,
                    coordinate.Z);
                var above = state.GetWater(aboveIndex);
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
                        (above.Direction
                            & WaterFlowDirectionMask.Horizontal)
                        | WaterFlowDirectionMask.Down);
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

                var donorIndex = WorldIndex.EncodeCell(
                    world,
                    donorX,
                    coordinate.Y,
                    donorZ);
                var donor = state.GetWater(donorIndex);
                if (donor.Amount <= parameters.SpreadAmountLoss
                    || donor.Amount < parameters.MinimumSpreadAmount
                    || (donor.Role == WaterCellRole.Dynamic
                        && (donor.IsFalling
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
                        donorIndex,
                        cellIndex,
                        candidateAmount))
                {
                    continue;
                }

                var outgoingDirection = ToDirection(offset.x, offset.z);
                var donorHeading = donor.Direction
                    & WaterFlowDirectionMask.Horizontal;
                var targetDescends = HasVerticalDropBelow(
                    world,
                    coordinate);
                if (donor.Role == WaterCellRole.Dynamic
                    && IsSingleDirection(donorHeading)
                    && donorHeading != outgoingDirection
                    && !targetDescends
                    && HasReachablePreferredDirection(
                        world,
                        state,
                        donorIndex,
                        donorHeading,
                        candidateAmount))
                {
                    continue;
                }

                if (candidateAmount > desired.Amount)
                {
                    desired = CreateDynamicWater(
                        candidateAmount,
                        outgoingDirection);
                    hasHorizontalInflow = true;
                }
                else if (candidateAmount == desired.Amount
                         && candidateAmount > 0)
                {
                    desired.Direction |= outgoingDirection;
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
                    desired.Direction |= WaterFlowDirectionMask.Down;
                }
                else
                {
                    desired.Direction &= WaterFlowDirectionMask.Horizontal;
                }

                desired.Normalize();
            }

            if (connectsToSourceBelow
                && current.Role == WaterCellRole.Dynamic)
            {
                current.Direction =
                    (current.Direction & WaterFlowDirectionMask.Horizontal)
                    | WaterFlowDirectionMask.Down;
                current.Normalize();
            }

            return ApplyDissipation(current, desired, parameters);
        }

        private static WaterFlowDirectionMask ResolveSourceOutflowDirections(
            WorldData world,
            WaterFlowState state,
            int sourceIndex,
            in WaterFlowParameters parameters)
        {
            if (WaterAmount.Full <= parameters.SpreadAmountLoss)
            {
                return WaterFlowDirectionMask.None;
            }

            var candidateAmount = checked((byte)(
                WaterAmount.Full - parameters.SpreadAmountLoss));
            if (candidateAmount < parameters.MinimumSpreadAmount)
            {
                return WaterFlowDirectionMask.None;
            }

            var source = WorldIndex.DecodeCell(world, sourceIndex);
            var result = WaterFlowDirectionMask.None;
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

                var targetIndex = WorldIndex.EncodeCell(
                    world,
                    targetX,
                    source.Y,
                    targetZ);
                var targetWater = state.GetWater(targetIndex);
                if (targetWater.Role == WaterCellRole.Source
                    || targetWater.Amount > candidateAmount
                    || !CanReachHorizontally(
                        world,
                        state,
                        sourceIndex,
                        targetIndex,
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

            var belowIndex = WorldIndex.EncodeCell(
                world,
                coordinate.X,
                coordinate.Y - 1,
                coordinate.Z);
            return state.GetWater(belowIndex).Role
                == WaterCellRole.Source;
        }

        private static WaterCellData ApplyDissipation(
            WaterCellData current,
            WaterCellData desired,
            in WaterFlowParameters parameters)
        {
            if (current.Role != WaterCellRole.Dynamic
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

        private static WaterCellData CreateDynamicWater(
            byte amount,
            WaterFlowDirectionMask direction) => new()
        {
            Amount = amount,
            Role = WaterCellRole.Dynamic,
            Direction = direction
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
            if (below.SolidFill >= WorldGrid.HeightStepsPerCell)
            {
                return false;
            }

            var belowIndex = WorldIndex.EncodeCell(
                world,
                coordinate.X,
                coordinate.Y - 1,
                coordinate.Z);
            var belowWater = state.GetWater(belowIndex);
            return WaterFlowReachability.CanFlowDown(
                coordinate.Y,
                below,
                belowWater);
        }

        /// <summary>
        /// A horizontal flow entering this Cell crosses a ledge even when the
        /// Cell below is already filled by the lower water surface. This is a
        /// render and branching condition, distinct from whether the lower
        /// Cell can accept more water.
        /// </summary>
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
            int donorIndex,
            int targetIndex,
            byte candidateAmount)
        {
            var donorCoordinate = WorldIndex.DecodeCell(world, donorIndex);
            var targetCoordinate = WorldIndex.DecodeCell(world, targetIndex);
            var donorCell = world.GetCell(
                donorCoordinate.X,
                donorCoordinate.Y,
                donorCoordinate.Z);
            var targetCell = world.GetCell(
                targetCoordinate.X,
                targetCoordinate.Y,
                targetCoordinate.Z);
            var donorWater = state.GetWater(donorIndex);
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
            int donorIndex,
            WaterFlowDirectionMask preferredDirection,
            byte candidateAmount)
        {
            var donor = WorldIndex.DecodeCell(world, donorIndex);
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
                    donorIndex,
                    WorldIndex.EncodeCell(
                        world,
                        targetX,
                        donor.Y,
                        targetZ),
                    candidateAmount);
            }

            return false;
        }

        private static bool IsSingleDirection(
            WaterFlowDirectionMask direction)
        {
            var value = (byte)(direction & WaterFlowDirectionMask.Horizontal);
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

            foreach (var cellIndex in applyCells)
            {
                previousVisualStates[cellIndex] =
                    WaterVisualState.Resolve(world, cellIndex);
            }
        }

        private void ApplyStagedState(
            WorldData world,
            WaterFlowState state)
        {
            foreach (var pair in state.EnumerateStagedCells())
            {
                var coordinate = WorldIndex.DecodeCell(world, pair.Key);
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
                result.LogicalChangedCellIndices.Add(pair.Key);
                result.ChangedColumnIndices.Add(WorldIndex.EncodeColumn(
                    world,
                    coordinate.X,
                    coordinate.Z));
                if (previousWater.HasWater != cell.Water.HasWater)
                {
                    result.TopologyChangedCellIndices.Add(pair.Key);
                }
            }

            foreach (var cellIndex in applyCells)
            {
                var next = WaterVisualState.Resolve(world, cellIndex);
                if (!previousVisualStates.TryGetValue(
                        cellIndex,
                        out var previous)
                    || !previous.Equals(next))
                {
                    result.RenderChangedCellIndices.Add(cellIndex);
                }
            }

            state.CancelResolutionPass();
        }

        private void BuildNextWave(WorldData world)
        {
            nextWave.Clear();
            foreach (var cellIndex in result.LogicalChangedCellIndices)
            {
                AddCellAndNeighbors(world, nextWave, cellIndex);
            }
        }

        private void ReplaceActiveWave(
            WorldData world,
            WaterFlowState state,
            IReadOnlyCollection<int> cells)
        {
            state.CancelResolutionPass();
            activeWave.Clear();
            if (cells != null && cells.Count > 0)
            {
                foreach (var cellIndex in cells)
                {
                    activeWave.Add(cellIndex);
                }

                activeWave.Sort();
            }

            cursor = 0;
            world.WaterFlowSchedule.ReplaceFrontier(activeWave);
            state.IsRecalculating = activeWave.Count > 0;
        }

        private static void AddCellAndNeighbors(
            WorldData world,
            HashSet<int> cells,
            int cellIndex)
        {
            cells.Add(cellIndex);
            var coordinate = WorldIndex.DecodeCell(world, cellIndex);
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
                    cells.Add(WorldIndex.EncodeCell(world, x, y, z));
                }
            }
        }

        private static WaterFlowDirectionMask ToDirection(int x, int z)
        {
            if (x > 0) return WaterFlowDirectionMask.East;
            if (x < 0) return WaterFlowDirectionMask.West;
            if (z > 0) return WaterFlowDirectionMask.North;
            if (z < 0) return WaterFlowDirectionMask.South;
            return WaterFlowDirectionMask.None;
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

            if (!world.WaterSources.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Water sources must be classified before flow is scheduled.");
            }

            var frontier = new HashSet<int>();
            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater
                    || cell.Water.Role != WaterCellRole.Source)
                {
                    continue;
                }

                AddSourceAndNeighbors(
                    WorldIndex.EncodeCell(world, x, y, z));
            }

            var sortedFrontier = new int[frontier.Count];
            frontier.CopyTo(sortedFrontier);
            Array.Sort(sortedFrontier);
            world.WaterFlowSchedule.ReplaceFrontier(sortedFrontier);

            void AddSourceAndNeighbors(int cellIndex)
            {
                frontier.Add(cellIndex);
                var coordinate = WorldIndex.DecodeCell(world, cellIndex);
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
                    frontier.Add(WorldIndex.EncodeCell(world, x, y, z));
                }
            }
        }
    }
}
