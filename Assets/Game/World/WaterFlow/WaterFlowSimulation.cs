using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    internal readonly struct WaterFlowParameters : IEquatable<WaterFlowParameters>
    {
        public readonly byte SpreadAmountLoss;
        public readonly byte MinimumSpreadAmount;

        public WaterFlowParameters(
            float spreadAmountLoss,
            float minimumSpreadAmount)
        {
            SpreadAmountLoss = WaterAmount.FromNormalized(
                spreadAmountLoss);
            MinimumSpreadAmount = WaterAmount.FromNormalized(
                minimumSpreadAmount);
        }

        public WaterFlowParameters(in WaterFlowRules rules)
        {
            SpreadAmountLoss = rules.SpreadAmountLoss;
            MinimumSpreadAmount = rules.MinimumSpreadAmount;
        }

        public bool Equals(WaterFlowParameters other) =>
            SpreadAmountLoss == other.SpreadAmountLoss
            && MinimumSpreadAmount == other.MinimumSpreadAmount;

        public override bool Equals(object obj) =>
            obj is WaterFlowParameters other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            SpreadAmountLoss,
            MinimumSpreadAmount);
    }

    internal readonly struct WaterVisualState : IEquatable<WaterVisualState>
    {
        private readonly byte waterFill;
        private readonly WaterType waterType;
        private readonly WaterCellRole waterRole;
        private readonly byte connectionMask;
        private readonly WaterFlowDirectionMask direction;

        private WaterVisualState(
            byte waterFill,
            WaterType waterType,
            WaterCellRole waterRole,
            byte connectionMask,
            WaterFlowDirectionMask direction)
        {
            this.waterFill = waterFill;
            this.waterType = waterType;
            this.waterRole = waterRole;
            this.connectionMask = connectionMask;
            this.direction = direction;
        }

        public bool HasWater => waterFill > 0;

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
                cell.Water.Type,
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
            && waterType == other.waterType
            && waterRole == other.waterRole
            && connectionMask == other.connectionMask
            && direction == other.direction;

        public override bool Equals(object obj) =>
            obj is WaterVisualState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            waterFill,
            waterType,
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

        public bool HasPersistentChanges =>
            LogicalChangedCellIndices.Count > 0;
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
    /// Resolves water from neighboring Cells. No source ownership or
    /// parent/child flow graph is retained.
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
        private readonly HashSet<int> applyCells = new();
        private readonly List<int> changedCellsInPass = new();
        private readonly List<int> expandedCellsInPass = new();
        private readonly Dictionary<int, WaterVisualState>
            previousVisualStates = new();
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
            applyCells.Clear();
            previousVisualStates.Clear();
            state.IsRecalculating = true;

            foreach (var cellIndex in changedCellIndices)
            {
                EnqueueCellAndNeighbors(world, cellIndex);
            }

            foreach (var columnIndex in changedColumnIndices)
            {
                WorldIndex.DecodeColumn(
                    world,
                    columnIndex,
                    out var x,
                    out var z);
                for (var y = 0; y < world.Height; y++)
                {
                    EnqueueCellAndNeighbors(
                        world,
                        WorldIndex.EncodeCell(world, x, y, z));
                }
            }

            var processed = 0;
            var maximumProcessed = Math.Max(
                checked(state.CellCount * 32),
                4096);
            try
            {
                while (dirtyCells.Count > 0)
                {
                    var passCount = dirtyCells.Count;
                    changedCellsInPass.Clear();
                    expandedCellsInPass.Clear();
                    for (var passIndex = 0;
                         passIndex < passCount;
                         passIndex++)
                    {
                        if (++processed > maximumProcessed)
                        {
                            throw new InvalidOperationException(
                                "Water flow did not converge.");
                        }

                        var cellIndex = dirtyCells.Dequeue();
                        queuedGenerationByCell[cellIndex] = 0;
                        var firstExpansion =
                            expandedGenerationByCell[cellIndex] != generation;
                        expandedGenerationByCell[cellIndex] = generation;

                        var desired = ResolveDesiredWater(
                            world,
                            state,
                            cellIndex,
                            parameters);
                        if (state.StageResolvedCell(cellIndex, desired))
                        {
                            changedCellsInPass.Add(cellIndex);
                        }
                        else if (firstExpansion
                                 && desired.Amount
                                 >= parameters.MinimumSpreadAmount)
                        {
                            expandedCellsInPass.Add(cellIndex);
                        }
                    }

                    state.ApplyResolutionPass();
                    for (var index = 0;
                         index < changedCellsInPass.Count;
                         index++)
                    {
                        EnqueueCellAndNeighbors(
                            world,
                            changedCellsInPass[index]);
                    }

                    for (var index = 0;
                         index < expandedCellsInPass.Count;
                         index++)
                    {
                        EnqueueCellAndNeighbors(
                            world,
                            expandedCellsInPass[index]);
                    }
                }

                BuildApplySet(world, state);
                ApplyResolvedState(world, state);
                return result;
            }
            finally
            {
                state.CancelResolutionPass();
                state.IsRecalculating = false;
                state.ClearResolvedCells();
            }
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
                current.Direction &= WaterFlowDirectionMask.Horizontal;
                if (CanFlowDown(world, coordinate))
                {
                    current.Direction |= WaterFlowDirectionMask.Down;
                }

                current.Normalize();
                return current;
            }

            if (current.Role == WaterCellRole.Reservoir)
            {
                current.Normalize();
                return current;
            }

            var desired = default(WaterCellData);
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
                        new CellCoordinate(
                            coordinate.X,
                            coordinate.Y + 1,
                            coordinate.Z)))
                {
                    desired = CreateDynamicWater(
                        above.Amount,
                        above.Type,
                        (above.Direction
                            & WaterFlowDirectionMask.Horizontal)
                        | WaterFlowDirectionMask.Down,
                        above.Flags);
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
                    || CanFlowDown(
                        world,
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
                if (donor.Role == WaterCellRole.Dynamic
                    && donorHeading != WaterFlowDirectionMask.None
                    && (donorHeading & outgoingDirection) == 0
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
                        donor.Type,
                        outgoingDirection,
                        donor.Flags | WaterCellFlags.River);
                }
                else if (candidateAmount == desired.Amount
                         && candidateAmount > 0)
                {
                    desired.Direction |= outgoingDirection;
                }
            }

            if (desired.Amount == 0)
            {
                return default;
            }

            if (CanFlowDown(world, coordinate))
            {
                desired.Direction |= WaterFlowDirectionMask.Down;
            }
            else
            {
                desired.Direction &= WaterFlowDirectionMask.Horizontal;
            }

            desired.Normalize();
            return desired;
        }

        private static WaterCellData CreateDynamicWater(
            byte amount,
            WaterType type,
            WaterFlowDirectionMask direction,
            WaterCellFlags flags) => new()
        {
            Amount = amount,
            Type = type == WaterType.None ? WaterType.Fresh : type,
            Role = WaterCellRole.Dynamic,
            Direction = direction,
            Flags = flags
        };

        private static bool CanFlowDown(
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
            return below.SolidFill < WorldGrid.HeightStepsPerCell;
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
            var donorCapacity =
                WorldGrid.HeightStepsPerCell - donorCell.SolidFill;
            var donorTopUnits = donorCoordinate.Y
                * WorldGrid.HeightStepsPerCell
                + donorCell.SolidFill
                + donorWater.Amount
                * donorCapacity
                / (float)WaterAmount.Full;
            var targetFloorUnits = targetCoordinate.Y
                * WorldGrid.HeightStepsPerCell
                + targetCell.SolidFill;
            if (targetFloorUnits >= donorTopUnits)
            {
                return false;
            }

            return candidateAmount > 0
                && targetCell.SolidFill < WorldGrid.HeightStepsPerCell;
        }

        private static bool HasReachablePreferredDirection(
            WorldData world,
            WaterFlowState state,
            int donorIndex,
            WaterFlowDirectionMask preferredDirections,
            byte candidateAmount)
        {
            var donor = WorldIndex.DecodeCell(world, donorIndex);
            for (var index = 0;
                 index < HorizontalDirections.Length;
                 index++)
            {
                var offset = HorizontalDirections[index];
                var direction = ToDirection(offset.x, offset.z);
                if ((preferredDirections & direction) == 0)
                {
                    continue;
                }

                var targetX = donor.X + offset.x;
                var targetZ = donor.Z + offset.z;
                if (!world.Contains(targetX, donor.Y, targetZ))
                {
                    continue;
                }

                var targetIndex = WorldIndex.EncodeCell(
                    world,
                    targetX,
                    donor.Y,
                    targetZ);
                if (CanReachHorizontally(
                        world,
                        state,
                        donorIndex,
                        targetIndex,
                        candidateAmount))
                {
                    return true;
                }
            }

            return false;
        }

        private void BuildApplySet(WorldData world, WaterFlowState state)
        {
            foreach (var pair in state.EnumerateResolvedCells())
            {
                AddApplyCellAndNeighbors(world, pair.Key);
            }

            foreach (var cellIndex in applyCells)
            {
                previousVisualStates[cellIndex] =
                    WaterVisualState.Resolve(world, cellIndex);
            }
        }

        private void ApplyResolvedState(
            WorldData world,
            WaterFlowState state)
        {
            foreach (var pair in state.EnumerateResolvedCells())
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
        }

        private void EnqueueCellAndNeighbors(
            WorldData world,
            int cellIndex)
        {
            Enqueue(cellIndex);
            var coordinate = WorldIndex.DecodeCell(world, cellIndex);
            EnqueueIfContained(coordinate.X + 1, coordinate.Y, coordinate.Z);
            EnqueueIfContained(coordinate.X - 1, coordinate.Y, coordinate.Z);
            EnqueueIfContained(coordinate.X, coordinate.Y + 1, coordinate.Z);
            EnqueueIfContained(coordinate.X, coordinate.Y - 1, coordinate.Z);
            EnqueueIfContained(coordinate.X, coordinate.Y, coordinate.Z + 1);
            EnqueueIfContained(coordinate.X, coordinate.Y, coordinate.Z - 1);

            void EnqueueIfContained(int x, int y, int z)
            {
                if (world.Contains(x, y, z))
                {
                    Enqueue(WorldIndex.EncodeCell(world, x, y, z));
                }
            }
        }

        private void AddApplyCellAndNeighbors(
            WorldData world,
            int cellIndex)
        {
            applyCells.Add(cellIndex);
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
                    applyCells.Add(WorldIndex.EncodeCell(world, x, y, z));
                }
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

        private void BeginGeneration()
        {
            dirtyCells.Clear();
            generation++;
            if (generation != int.MaxValue)
            {
                return;
            }

            Array.Clear(
                queuedGenerationByCell,
                0,
                queuedGenerationByCell.Length);
            Array.Clear(
                expandedGenerationByCell,
                0,
                expandedGenerationByCell.Length);
            generation = 1;
        }

        private static WaterFlowDirectionMask ToDirection(int x, int z)
        {
            if (x > 0) return WaterFlowDirectionMask.East;
            if (x < 0) return WaterFlowDirectionMask.West;
            if (z > 0) return WaterFlowDirectionMask.North;
            if (z < 0) return WaterFlowDirectionMask.South;
            return WaterFlowDirectionMask.None;
        }
    }

    /// <summary>
    /// Shared synchronous entry point used by generation and runtime rebuilds.
    /// Generated river paths provide only persistent sources; every dynamic
    /// WaterCell is resolved by the same neighbor-based rules used after edits.
    /// </summary>
    internal static class WaterFlowSolver
    {
        private static readonly int[] EmptyColumns = Array.Empty<int>();

        public static void ResolveGeneratedWorld(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.WaterSources.IsInitialized)
            {
                throw new InvalidOperationException(
                    "Water sources must be classified before water is resolved.");
            }

            var seedCells = new List<int>();
            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasWater)
                {
                    continue;
                }

                if (cell.Water.Role == WaterCellRole.Dynamic)
                {
                    cell.Water = default;
                    world.SetCellBulk(x, y, z, cell);
                    continue;
                }

                seedCells.Add(WorldIndex.EncodeCell(world, x, y, z));
            }

            world.RebuildAllSurfaceColumns();
            var state = new WaterFlowState(
                world,
                WaterBodyResolver.Resolve(world));
            var resolver = new WaterFlowResolver(state.CellCount);
            resolver.Recalculate(
                world,
                state,
                seedCells,
                EmptyColumns,
                new WaterFlowParameters(world.WaterFlowRules));
            world.RebuildAllSurfaceColumns();
        }
    }
}
