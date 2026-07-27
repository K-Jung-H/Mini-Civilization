using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    internal readonly struct WaterFlowParameters : IEquatable<WaterFlowParameters>
    {
        public readonly ushort MaximumAmount;
        public readonly ushort SpreadAmountLoss;
        public readonly ushort MinimumSpreadAmount;

        public WaterFlowParameters(
            float maximumAmount,
            float spreadAmountLoss,
            float minimumSpreadAmount)
        {
            MaximumAmount = WaterAmountConversion.ToUnits(maximumAmount);
            SpreadAmountLoss = Math.Min(
                MaximumAmount,
                WaterAmountConversion.ToUnits(spreadAmountLoss));
            MinimumSpreadAmount = Math.Min(
                MaximumAmount,
                WaterAmountConversion.ToUnits(minimumSpreadAmount));
        }

        public bool Equals(WaterFlowParameters other) =>
            MaximumAmount == other.MaximumAmount
            && SpreadAmountLoss == other.SpreadAmountLoss
            && MinimumSpreadAmount == other.MinimumSpreadAmount;

        public override bool Equals(object obj) =>
            obj is WaterFlowParameters other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            MaximumAmount,
            SpreadAmountLoss,
            MinimumSpreadAmount);
    }

    internal readonly struct WaterVisualState : IEquatable<WaterVisualState>
    {
        private readonly byte waterFill;
        private readonly WaterType waterType;
        private readonly byte connectionMask;
        private readonly bool isFalling;
        private readonly bool connectsFromAbove;

        private WaterVisualState(
            byte waterFill,
            WaterType waterType,
            byte connectionMask,
            bool isFalling,
            bool connectsFromAbove)
        {
            this.waterFill = waterFill;
            this.waterType = waterType;
            this.connectionMask = connectionMask;
            this.isFalling = isFalling;
            this.connectsFromAbove = connectsFromAbove;
        }

        public bool HasWater => waterFill > 0;

        public static WaterVisualState Resolve(WorldData world, int cellIndex)
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

            var connectsFromAbove = world.TryGetCell(
                    coordinate.X,
                    coordinate.Y + 1,
                    coordinate.Z,
                    out var above)
                && above.HasWater
                && (above.Flags & CellFlags.FallingWater) != 0;
            return new WaterVisualState(
                cell.WaterFill,
                cell.Water,
                connections,
                (cell.Flags & CellFlags.FallingWater) != 0,
                connectsFromAbove);

            void AddConnection(int offsetX, int offsetY, int offsetZ, int mask)
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
            && waterType == other.waterType
            && connectionMask == other.connectionMask
            && isFalling == other.isFalling
            && connectsFromAbove == other.connectsFromAbove;

        public override bool Equals(object obj) =>
            obj is WaterVisualState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            waterFill,
            waterType,
            connectionMask,
            isFalling,
            connectsFromAbove);
    }

    internal sealed class WaterFlowRecalculationResult
    {
        public readonly HashSet<int> LogicalChangedCellIndices = new();
        public readonly HashSet<int> RenderChangedCellIndices = new();
        public readonly HashSet<int> TopologyChangedCellIndices = new();
        public readonly HashSet<int> ChangedColumnIndices = new();

        public bool HasPersistentChanges => LogicalChangedCellIndices.Count > 0;
        public bool HasRenderChanges => RenderChangedCellIndices.Count > 0;
        public bool HasTopologyChanges => TopologyChangedCellIndices.Count > 0;

        public void Clear()
        {
            LogicalChangedCellIndices.Clear();
            RenderChangedCellIndices.Clear();
            TopologyChangedCellIndices.Clear();
            ChangedColumnIndices.Clear();
        }
    }

    /// <summary>
    /// Re-evaluates water from neighboring cells until the quantized state is
    /// stable. Sources are fixed boundary cells; flowing water has no owner,
    /// parent, or child relationship.
    /// </summary>
    internal sealed class WaterFlowResolver
    {
        private static readonly (int x, int z)[] HorizontalDirections =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        private readonly Queue<int> dirtyCells;
        private readonly int[] queuedGenerationByCell;
        private readonly int[] expandedGenerationByCell;
        private readonly WaterVisualState[] previousVisualStatesByCell;
        private readonly HashSet<int> stateChangedCells = new();
        private readonly HashSet<int> applyCells = new();
        private readonly WaterFlowRecalculationResult result = new();
        private int generation;

        public WaterFlowResolver(int cellCount)
        {
            if (cellCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellCount));
            }

            dirtyCells = new Queue<int>(Math.Min(cellCount, 4096));
            queuedGenerationByCell = new int[cellCount];
            expandedGenerationByCell = new int[cellCount];
            previousVisualStatesByCell = new WaterVisualState[cellCount];
        }

        public WaterFlowRecalculationResult Recalculate(
            WorldData world,
            WaterFlowState state,
            IReadOnlyCollection<int> changedCellIndices,
            IReadOnlyCollection<int> changedColumnIndices,
            in WaterFlowParameters parameters)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            BeginGeneration();
            result.Clear();
            stateChangedCells.Clear();
            applyCells.Clear();
            state.IsRecalculating = true;

            foreach (var cellIndex in changedCellIndices)
            {
                EnqueueCellAndNeighbors(world, cellIndex);
            }

            foreach (var columnIndex in changedColumnIndices)
            {
                WorldIndex.DecodeColumn(world, columnIndex, out var x, out var z);
                for (var y = 0; y < world.Height; y++)
                {
                    EnqueueCellAndNeighbors(
                        world,
                        WorldIndex.EncodeCell(world, x, y, z));
                }
            }

            var processedCellCount = 0;
            var maximumProcessedCellCount = Math.Max(
                checked(state.CellCount * 32),
                4096);
            try
            {
                while (dirtyCells.Count > 0)
                {
                    if (++processedCellCount > maximumProcessedCellCount)
                    {
                        throw new InvalidOperationException(
                            "Water flow did not converge. " +
                            "The local routing state contains a cycle.");
                    }

                    var cellIndex = dirtyCells.Dequeue();
                    queuedGenerationByCell[cellIndex] = 0;
                    var firstExpansion =
                        expandedGenerationByCell[cellIndex] != generation;
                    expandedGenerationByCell[cellIndex] = generation;

                    ResolveDesiredState(
                        world,
                        state,
                        cellIndex,
                        parameters,
                        out var desiredAmount,
                        out var desiredWaterType,
                        out var desiredIncomingDirections,
                        out var desiredFlowHeading,
                        out var desiredFlowMode);
                    var changed = state.SetResolvedCell(
                        cellIndex,
                        desiredAmount,
                        desiredWaterType,
                        desiredIncomingDirections,
                        desiredFlowHeading,
                        desiredFlowMode);
                    if (changed)
                    {
                        stateChangedCells.Add(cellIndex);
                        EnqueueCellAndNeighbors(world, cellIndex);
                        continue;
                    }

                    if (firstExpansion
                        && state.GetTargetAmount(cellIndex)
                            >= parameters.MinimumSpreadAmount)
                    {
                        EnqueuePotentialRecipients(
                            world,
                            state,
                            cellIndex,
                            parameters);
                    }
                }

                BuildApplySet(world);
                ApplyResolvedState(world, state);
                return result;
            }
            finally
            {
                state.IsRecalculating = false;
            }
        }

        private void ResolveDesiredState(
            WorldData world,
            WaterFlowState state,
            int cellIndex,
            in WaterFlowParameters parameters,
            out ushort desiredAmount,
            out WaterType desiredWaterType,
            out WaterIncomingDirectionMask desiredIncomingDirections,
            out WaterFlowHeadingMask desiredFlowHeading,
            out WaterFlowMode desiredFlowMode)
        {
            var coordinate = WorldIndex.DecodeCell(world, cellIndex);
            var cell = world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            var capacity = GetWaterCapacityAmount(cell, parameters.MaximumAmount);
            var behavior = world.WaterState.GetBehavior(cellIndex);
            if (IsFixedBehavior(behavior))
            {
                var amount = behavior == WaterCellBehavior.Source
                    ? Math.Max(
                        world.WaterState.GetAmount(cellIndex),
                        world.WaterState.GetSourceAmount(cellIndex))
                    : world.WaterState.GetAmount(cellIndex);
                desiredAmount = checked((ushort)Math.Min(capacity, amount));
                desiredWaterType = desiredAmount == 0
                    ? WaterType.None
                    : cell.Water != WaterType.None
                        ? cell.Water
                        : world.WaterState.GetSourceWaterType(cellIndex);
                desiredIncomingDirections = WaterIncomingDirectionMask.None;
                desiredFlowHeading = WaterFlowHeadingMask.None;
                desiredFlowMode = desiredAmount > 0
                    ? WaterFlowMode.Surface
                    : WaterFlowMode.None;
                return;
            }

            if (capacity < parameters.MinimumSpreadAmount)
            {
                desiredAmount = 0;
                desiredWaterType = WaterType.None;
                desiredIncomingDirections = WaterIncomingDirectionMask.None;
                desiredFlowHeading = WaterFlowHeadingMask.None;
                desiredFlowMode = WaterFlowMode.None;
                return;
            }

            var bestAmount = 0;
            var bestDonorIndex = int.MaxValue;
            var bestWaterType = WaterType.None;
            var incomingDirections = WaterIncomingDirectionMask.None;
            var flowHeading = WaterFlowHeadingMask.None;

            if (coordinate.Y + 1 < world.Height)
            {
                var aboveIndex = WorldIndex.EncodeCell(
                    world,
                    coordinate.X,
                    coordinate.Y + 1,
                    coordinate.Z);
                var aboveAmount = state.GetTargetAmount(aboveIndex);
                var aboveCell = world.GetCell(
                    coordinate.X,
                    coordinate.Y + 1,
                    coordinate.Z);
                var aboveCoordinate = new CellCoordinate(
                    coordinate.X,
                    coordinate.Y + 1,
                    coordinate.Z);
                if (aboveAmount >= parameters.MinimumSpreadAmount
                    && CanFlowDown(
                        world,
                        aboveCoordinate,
                        aboveCell,
                        parameters.MinimumSpreadAmount,
                        parameters.MaximumAmount))
                {
                    SelectBestDonor(
                        state,
                        aboveIndex,
                        Math.Min(capacity, aboveAmount),
                        WaterIncomingDirectionMask.FromAbove,
                        state.GetFlowHeading(aboveIndex),
                        parameters.MinimumSpreadAmount,
                        ref bestAmount,
                        ref bestDonorIndex,
                        ref bestWaterType,
                        ref incomingDirections,
                        ref flowHeading);
                }
            }

            for (var directionIndex = 0;
                 directionIndex < HorizontalDirections.Length;
                 directionIndex++)
            {
                var direction = HorizontalDirections[directionIndex];
                var donorX = coordinate.X + direction.x;
                var donorZ = coordinate.Z + direction.z;
                if (!world.Contains(donorX, coordinate.Y, donorZ))
                {
                    continue;
                }

                var donorIndex = WorldIndex.EncodeCell(
                    world,
                    donorX,
                    coordinate.Y,
                    donorZ);
                var donorAmount = state.GetTargetAmount(donorIndex);
                var candidateAmount = donorAmount - parameters.SpreadAmountLoss;
                if (candidateAmount < parameters.MinimumSpreadAmount)
                {
                    continue;
                }

                var donorCoordinate = new CellCoordinate(
                    donorX,
                    coordinate.Y,
                    donorZ);
                if (!CanFlowHorizontallyTo(
                        world,
                        state,
                        donorIndex,
                        donorCoordinate,
                        cellIndex,
                        parameters))
                {
                    continue;
                }

                SelectBestDonor(
                    state,
                    donorIndex,
                    Math.Min(capacity, candidateAmount),
                    GetIncomingDirection(directionIndex),
                    GetHeadingFromDonorOffset(directionIndex),
                    parameters.MinimumSpreadAmount,
                    ref bestAmount,
                    ref bestDonorIndex,
                    ref bestWaterType,
                    ref incomingDirections,
                    ref flowHeading);
            }

            desiredAmount = checked((ushort)bestAmount);
            desiredWaterType = bestAmount > 0
                ? bestWaterType != WaterType.None
                    ? bestWaterType
                    : WaterType.Fresh
                : WaterType.None;
            desiredIncomingDirections = bestAmount > 0
                ? incomingDirections
                : WaterIncomingDirectionMask.None;
            desiredFlowHeading = bestAmount > 0
                ? flowHeading
                : WaterFlowHeadingMask.None;
            desiredFlowMode = ResolveFlowMode(
                world,
                coordinate,
                cell,
                desiredAmount,
                incomingDirections,
                parameters);
        }

        private static void SelectBestDonor(
            WaterFlowState state,
            int donorIndex,
            int candidateAmount,
            WaterIncomingDirectionMask incomingDirection,
            WaterFlowHeadingMask candidateHeading,
            ushort minimumSpreadAmount,
            ref int bestAmount,
            ref int bestDonorIndex,
            ref WaterType bestWaterType,
            ref WaterIncomingDirectionMask incomingDirections,
            ref WaterFlowHeadingMask flowHeading)
        {
            if (candidateAmount < minimumSpreadAmount
                || candidateAmount < bestAmount)
            {
                return;
            }

            if (candidateAmount == bestAmount)
            {
                incomingDirections |= incomingDirection;
                flowHeading |= candidateHeading;
                if (donorIndex >= bestDonorIndex)
                {
                    return;
                }
            }
            else
            {
                incomingDirections = incomingDirection;
                flowHeading = candidateHeading;
            }

            bestAmount = candidateAmount;
            bestDonorIndex = donorIndex;
            bestWaterType = state.GetWaterType(donorIndex);
        }

        private bool CanFlowHorizontallyTo(
            WorldData world,
            WaterFlowState state,
            int donorIndex,
            in CellCoordinate donorCoordinate,
            int targetIndex,
            in WaterFlowParameters parameters)
        {
            var donorCell = world.GetCell(
                donorCoordinate.X,
                donorCoordinate.Y,
                donorCoordinate.Z);
            if (CanFlowDown(
                    world,
                    donorCoordinate,
                    donorCell,
                    parameters.MinimumSpreadAmount,
                    parameters.MaximumAmount))
            {
                return false;
            }

            Span<FlowCandidate> candidates = stackalloc FlowCandidate[4];
            var count = BuildHorizontalCandidates(
                world,
                state,
                donorIndex,
                donorCoordinate,
                donorCell,
                parameters,
                candidates);
            if (count == 0)
            {
                return false;
            }

            var behavior = world.WaterState.GetBehavior(donorIndex);
            var recipientCount = IsFixedBehavior(behavior) ? count : 1;
            for (var index = 0; index < recipientCount; index++)
            {
                if (candidates[index].TargetCellIndex == targetIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private int BuildHorizontalCandidates(
            WorldData world,
            WaterFlowState state,
            int donorIndex,
            in CellCoordinate coordinate,
            in CellData cell,
            in WaterFlowParameters parameters,
            Span<FlowCandidate> candidates)
        {
            var donorAmount = state.GetTargetAmount(donorIndex);
            if (donorAmount - parameters.SpreadAmountLoss
                < parameters.MinimumSpreadAmount)
            {
                return 0;
            }

            var donorSurface = coordinate.Y * parameters.MaximumAmount
                + GetSolidFillAmount(cell, parameters.MaximumAmount)
                + donorAmount;
            var flowHeading = state.GetFlowHeading(donorIndex);
            var count = 0;
            for (var directionIndex = 0;
                 directionIndex < HorizontalDirections.Length;
                 directionIndex++)
            {
                var direction = HorizontalDirections[directionIndex];
                var targetX = coordinate.X + direction.x;
                var targetZ = coordinate.Z + direction.z;
                if (!world.Contains(targetX, coordinate.Y, targetZ))
                {
                    continue;
                }

                var targetIndex = WorldIndex.EncodeCell(
                    world,
                    targetX,
                    coordinate.Y,
                    targetZ);
                if (IsFixedWater(world, targetIndex))
                {
                    continue;
                }

                var target = world.GetCell(targetX, coordinate.Y, targetZ);
                if (GetWaterCapacityAmount(target, parameters.MaximumAmount)
                    < parameters.MinimumSpreadAmount)
                {
                    continue;
                }

                var targetFloor = coordinate.Y * parameters.MaximumAmount
                    + GetSolidFillAmount(target, parameters.MaximumAmount);
                var availableSurface = donorSurface
                    - parameters.SpreadAmountLoss;
                if (availableSurface <= targetFloor)
                {
                    continue;
                }

                var targetCoordinate = new CellCoordinate(
                    targetX,
                    coordinate.Y,
                    targetZ);
                var flowDirection = (HorizontalFlowDirection)directionIndex;
                candidates[count++] = new FlowCandidate(
                    targetIndex,
                    targetFloor,
                    CanFlowDown(
                        world,
                        targetCoordinate,
                        target,
                        parameters.MinimumSpreadAmount,
                        parameters.MaximumAmount),
                    IsStraightContinuation(
                        flowHeading,
                        flowDirection),
                    IsReverseDirection(
                        flowHeading,
                        flowDirection));
            }

            SortCandidates(world.Seed, candidates, count);
            return count;
        }

        private void EnqueuePotentialRecipients(
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
            if (CanFlowDown(
                    world,
                    coordinate,
                    cell,
                    parameters.MinimumSpreadAmount,
                    parameters.MaximumAmount))
            {
                Enqueue(world, coordinate.X, coordinate.Y - 1, coordinate.Z);
                return;
            }

            Span<FlowCandidate> candidates = stackalloc FlowCandidate[4];
            var count = BuildHorizontalCandidates(
                world,
                state,
                cellIndex,
                coordinate,
                cell,
                parameters,
                candidates);
            var behavior = world.WaterState.GetBehavior(cellIndex);
            var recipientCount = IsFixedBehavior(behavior) ? count : Math.Min(1, count);
            for (var index = 0; index < recipientCount; index++)
            {
                Enqueue(candidates[index].TargetCellIndex);
            }
        }

        private void BuildApplySet(WorldData world)
        {
            foreach (var cellIndex in stateChangedCells)
            {
                applyCells.Add(cellIndex);
                var coordinate = WorldIndex.DecodeCell(world, cellIndex);
                AddIfContained(world, coordinate.X + 1, coordinate.Y, coordinate.Z);
                AddIfContained(world, coordinate.X - 1, coordinate.Y, coordinate.Z);
                AddIfContained(world, coordinate.X, coordinate.Y + 1, coordinate.Z);
                AddIfContained(world, coordinate.X, coordinate.Y - 1, coordinate.Z);
                AddIfContained(world, coordinate.X, coordinate.Y, coordinate.Z + 1);
                AddIfContained(world, coordinate.X, coordinate.Y, coordinate.Z - 1);
            }
        }

        private void ApplyResolvedState(
            WorldData world,
            WaterFlowState state)
        {
            foreach (var cellIndex in applyCells)
            {
                previousVisualStatesByCell[cellIndex] =
                    WaterVisualState.Resolve(world, cellIndex);
            }

            foreach (var cellIndex in applyCells)
            {
                var previousBehavior = world.WaterState.GetBehavior(cellIndex);
                var isFixed = IsFixedBehavior(previousBehavior);
                var targetAmount = state.GetTargetAmount(cellIndex);
                var targetBehavior = isFixed
                    ? previousBehavior
                    : targetAmount > 0
                        ? WaterCellBehavior.FlowDependent
                        : WaterCellBehavior.None;
                var previousAmount = world.WaterState.GetAmount(cellIndex);
                if (previousAmount != targetAmount
                    || previousBehavior != targetBehavior)
                {
                    world.WaterState.SetCell(
                        cellIndex,
                        targetAmount,
                        targetBehavior,
                        previousBehavior == WaterCellBehavior.Source
                            ? world.WaterState.GetSourceGroupId(cellIndex)
                            : 0);
                    result.LogicalChangedCellIndices.Add(cellIndex);
                }

                var coordinate = WorldIndex.DecodeCell(world, cellIndex);
                var cell = world.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                var previousCell = cell;
                var capacitySteps = WorldGrid.HeightStepsPerCell
                    - cell.SolidFill;
                var renderFill = targetAmount == 0
                    ? (byte)0
                    : WaterAmountConversion.ToRenderFill(
                        targetAmount,
                        state.MaximumAmount,
                        capacitySteps);
                cell.WaterFill = renderFill;
                cell.Water = renderFill > 0
                    ? state.GetWaterType(cellIndex) != WaterType.None
                        ? state.GetWaterType(cellIndex)
                        : WaterType.Fresh
                    : WaterType.None;

                if (renderFill > 0)
                {
                    if (!isFixed)
                    {
                        cell.Flags |= CellFlags.River | CellFlags.Generated;
                    }

                    if (state.GetFlowMode(cellIndex)
                        == WaterFlowMode.Falling)
                    {
                        cell.Flags |= CellFlags.FallingWater;
                    }
                    else
                    {
                        cell.Flags &= ~CellFlags.FallingWater;
                    }
                }
                else
                {
                    cell.Flags &= ~(CellFlags.River | CellFlags.FallingWater);
                }

                if (cell.Equals(previousCell))
                {
                    continue;
                }

                world.SetCellForEdit(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    cell);
                result.LogicalChangedCellIndices.Add(cellIndex);
            }

            foreach (var cellIndex in applyCells)
            {
                var previousVisual = previousVisualStatesByCell[cellIndex];
                var nextVisual = WaterVisualState.Resolve(world, cellIndex);
                if (previousVisual.Equals(nextVisual))
                {
                    continue;
                }

                result.RenderChangedCellIndices.Add(cellIndex);
                var coordinate = WorldIndex.DecodeCell(world, cellIndex);
                result.ChangedColumnIndices.Add(WorldIndex.EncodeColumn(
                    world,
                    coordinate.X,
                    coordinate.Z));
                if (previousVisual.HasWater != nextVisual.HasWater)
                {
                    result.TopologyChangedCellIndices.Add(cellIndex);
                }
            }
        }

        private void EnqueueCellAndNeighbors(WorldData world, int cellIndex)
        {
            if ((uint)cellIndex >= (uint)queuedGenerationByCell.Length)
            {
                return;
            }

            Enqueue(cellIndex);
            var coordinate = WorldIndex.DecodeCell(world, cellIndex);
            Enqueue(world, coordinate.X + 1, coordinate.Y, coordinate.Z);
            Enqueue(world, coordinate.X - 1, coordinate.Y, coordinate.Z);
            Enqueue(world, coordinate.X, coordinate.Y + 1, coordinate.Z);
            Enqueue(world, coordinate.X, coordinate.Y - 1, coordinate.Z);
            Enqueue(world, coordinate.X, coordinate.Y, coordinate.Z + 1);
            Enqueue(world, coordinate.X, coordinate.Y, coordinate.Z - 1);
        }

        private void Enqueue(WorldData world, int x, int y, int z)
        {
            if (world.Contains(x, y, z))
            {
                Enqueue(WorldIndex.EncodeCell(world, x, y, z));
            }
        }

        private void Enqueue(int cellIndex)
        {
            if (queuedGenerationByCell[cellIndex] == generation)
            {
                return;
            }

            queuedGenerationByCell[cellIndex] = generation;
            dirtyCells.Enqueue(cellIndex);
        }

        private void AddIfContained(WorldData world, int x, int y, int z)
        {
            if (world.Contains(x, y, z))
            {
                applyCells.Add(WorldIndex.EncodeCell(world, x, y, z));
            }
        }

        private void BeginGeneration()
        {
            dirtyCells.Clear();
            if (generation == int.MaxValue)
            {
                Array.Clear(
                    queuedGenerationByCell,
                    0,
                    queuedGenerationByCell.Length);
                Array.Clear(
                    expandedGenerationByCell,
                    0,
                    expandedGenerationByCell.Length);
                generation = 1;
                return;
            }

            generation++;
            if (generation == 0)
            {
                generation = 1;
            }
        }

        private static bool CanFlowDown(
            WorldData world,
            in CellCoordinate coordinate,
            in CellData cell,
            ushort minimumSpreadAmount,
            ushort maximumAmount)
        {
            if (coordinate.Y <= 0 || cell.SolidFill > 0)
            {
                return false;
            }

            var belowIndex = WorldIndex.EncodeCell(
                world,
                coordinate.X,
                coordinate.Y - 1,
                coordinate.Z);
            if (IsFixedWater(world, belowIndex))
            {
                return false;
            }

            var below = world.GetCell(
                coordinate.X,
                coordinate.Y - 1,
                coordinate.Z);
            return GetWaterCapacityAmount(below, maximumAmount)
                >= minimumSpreadAmount;
        }

        private static bool IsFixedWater(WorldData world, int cellIndex) =>
            IsFixedBehavior(world.WaterState.GetBehavior(cellIndex));

        private static bool IsFixedBehavior(WaterCellBehavior behavior) =>
            behavior == WaterCellBehavior.Source
            || behavior == WaterCellBehavior.Reservoir
            || behavior == WaterCellBehavior.FixedReservoir;

        private static WaterIncomingDirectionMask GetIncomingDirection(
            int donorOffsetDirectionIndex) =>
            donorOffsetDirectionIndex switch
            {
                0 => WaterIncomingDirectionMask.FromEast,
                1 => WaterIncomingDirectionMask.FromNorth,
                2 => WaterIncomingDirectionMask.FromWest,
                3 => WaterIncomingDirectionMask.FromSouth,
                _ => WaterIncomingDirectionMask.None
            };

        private static WaterFlowHeadingMask GetHeadingFromDonorOffset(
            int donorOffsetDirectionIndex) =>
            donorOffsetDirectionIndex switch
            {
                0 => WaterFlowHeadingMask.West,
                1 => WaterFlowHeadingMask.South,
                2 => WaterFlowHeadingMask.East,
                3 => WaterFlowHeadingMask.North,
                _ => WaterFlowHeadingMask.None
            };

        private static WaterFlowMode ResolveFlowMode(
            WorldData world,
            in CellCoordinate coordinate,
            in CellData cell,
            int amount,
            WaterIncomingDirectionMask incomingDirections,
            in WaterFlowParameters parameters)
        {
            if (amount < parameters.MinimumSpreadAmount)
            {
                return WaterFlowMode.None;
            }

            if (CanFlowDown(
                    world,
                    coordinate,
                    cell,
                    parameters.MinimumSpreadAmount,
                    parameters.MaximumAmount))
            {
                return WaterFlowMode.Falling;
            }

            return incomingDirections != WaterIncomingDirectionMask.None
                ? WaterFlowMode.Flowing
                : WaterFlowMode.Surface;
        }

        private static bool IsStraightContinuation(
            WaterFlowHeadingMask flowHeading,
            HorizontalFlowDirection outgoingDirection) =>
            outgoingDirection switch
            {
                HorizontalFlowDirection.East =>
                    (flowHeading & WaterFlowHeadingMask.East) != 0,
                HorizontalFlowDirection.North =>
                    (flowHeading & WaterFlowHeadingMask.North) != 0,
                HorizontalFlowDirection.West =>
                    (flowHeading & WaterFlowHeadingMask.West) != 0,
                HorizontalFlowDirection.South =>
                    (flowHeading & WaterFlowHeadingMask.South) != 0,
                _ => false
            };

        private static bool IsReverseDirection(
            WaterFlowHeadingMask flowHeading,
            HorizontalFlowDirection outgoingDirection) =>
            outgoingDirection switch
            {
                HorizontalFlowDirection.East =>
                    (flowHeading & WaterFlowHeadingMask.West) != 0,
                HorizontalFlowDirection.North =>
                    (flowHeading & WaterFlowHeadingMask.South) != 0,
                HorizontalFlowDirection.West =>
                    (flowHeading & WaterFlowHeadingMask.East) != 0,
                HorizontalFlowDirection.South =>
                    (flowHeading & WaterFlowHeadingMask.North) != 0,
                _ => false
            };

        private static int GetWaterCapacityAmount(
            in CellData cell,
            ushort maximumAmount) =>
            (int)Math.Round(
                (WorldGrid.HeightStepsPerCell - cell.SolidFill)
                    * maximumAmount
                    / (double)WorldGrid.HeightStepsPerCell,
                MidpointRounding.AwayFromZero);

        private static int GetSolidFillAmount(
            in CellData cell,
            ushort maximumAmount) =>
            (int)Math.Round(
                cell.SolidFill * maximumAmount
                    / (double)WorldGrid.HeightStepsPerCell,
                MidpointRounding.AwayFromZero);

        private static void SortCandidates(
            int seed,
            Span<FlowCandidate> candidates,
            int count)
        {
            for (var index = 1; index < count; index++)
            {
                var value = candidates[index];
                var position = index - 1;
                while (position >= 0
                       && CompareCandidates(
                           seed,
                           value,
                           candidates[position]) < 0)
                {
                    candidates[position + 1] = candidates[position];
                    position--;
                }

                candidates[position + 1] = value;
            }
        }

        private static int CompareCandidates(
            int seed,
            in FlowCandidate left,
            in FlowCandidate right)
        {
            var comparison = right.IsStraightContinuation.CompareTo(
                left.IsStraightContinuation);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = right.LeadsToFall.CompareTo(left.LeadsToFall);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.IsReverseDirection.CompareTo(
                right.IsReverseDirection);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.TargetFloor.CompareTo(right.TargetFloor);
            if (comparison != 0)
            {
                return comparison;
            }

            return DeterministicTie(seed, left.TargetCellIndex)
                .CompareTo(DeterministicTie(seed, right.TargetCellIndex));
        }

        private static int DeterministicTie(int seed, int cellIndex)
        {
            unchecked
            {
                return (seed * 397 ^ cellIndex) & int.MaxValue;
            }
        }

        private readonly struct FlowCandidate
        {
            public readonly int TargetCellIndex;
            public readonly int TargetFloor;
            public readonly bool LeadsToFall;
            public readonly bool IsStraightContinuation;
            public readonly bool IsReverseDirection;

            public FlowCandidate(
                int targetCellIndex,
                int targetFloor,
                bool leadsToFall,
                bool isStraightContinuation,
                bool isReverseDirection)
            {
                TargetCellIndex = targetCellIndex;
                TargetFloor = targetFloor;
                LeadsToFall = leadsToFall;
                IsStraightContinuation = isStraightContinuation;
                IsReverseDirection = isReverseDirection;
            }
        }

        private enum HorizontalFlowDirection : byte
        {
            East = 0,
            North = 1,
            West = 2,
            South = 3
        }
    }
}
