using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Hydrology
{
    internal sealed class HydrologyStepResult
    {
        public readonly HashSet<int> ChangedCellIndices = new();
        public readonly HashSet<int> ChangedColumnIndices = new();
        public bool HasPersistentChanges;
        public bool HasRenderChanges;
    }

    internal static class HydrologySimulation
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        public static HydrologyStepResult Step(
            WorldData world,
            HydrologyState state,
            int maximumColumns,
            int stableTickThreshold)
        {
            var result = new HydrologyStepResult();
            for (var processed = 0;
                 processed < maximumColumns && state.TryDequeue(out var columnIndex);
                 processed++)
            {
                ProcessColumn(world, state, columnIndex, result);
                if (result.ChangedColumnIndices.Contains(columnIndex))
                {
                    state.ResetStableTicks(columnIndex);
                    state.EnqueueColumnAndNeighbors(columnIndex);
                }
                else if (state.IncrementStableTicks(columnIndex) < stableTickThreshold)
                {
                    state.Enqueue(columnIndex);
                }
            }

            return result;
        }

        private static void ProcessColumn(
            WorldData world,
            HydrologyState state,
            int columnIndex,
            HydrologyStepResult result)
        {
            WorldIndex.DecodeColumn(world, columnIndex, out var x, out var z);
            var behavior = GetColumnBehavior(world, x, z);
            if (behavior == WaterCellBehavior.None)
            {
                DetachPreviousDownstream(world, state, columnIndex);
                state.SetWaterfallEdges(columnIndex, WaterfallEdgeFlags.None);
                return;
            }

            var currentTop = GetLogicalWaterTopTenths(world, x, z);
            var groundTop = world.GetSurfaceColumn(x, z).SolidTopUnits * 2;
            var source = default(WaterSourceGroupData);
            if (behavior == WaterCellBehavior.Source)
            {
                source = FindSourceGroup(world, x, z);
                if (source != null && IsPrimarySourceColumn(world, source, x, z))
                {
                    currentTop = Math.Max(currentTop, source.OutputSurfaceTenths);
                    SetColumnWaterTop(
                        world,
                        x,
                        z,
                        currentTop,
                        WaterType.Fresh,
                        WaterCellBehavior.Source,
                        result);
                }
                else
                {
                    state.SetFlowDirection(columnIndex, WaterFlowDirection.None);
                    return;
                }
            }
            else if (behavior == WaterCellBehavior.FlowDependent
                     && !HasValidUpstream(world, state, columnIndex, currentTop))
            {
                var loweredTop = currentTop > 0 ? currentTop - 1 : 0;
                if (loweredTop - groundTop < WaterState.MinimumVisibleAmount)
                {
                    loweredTop = groundTop;
                }

                SetColumnWaterTop(
                    world,
                    x,
                    z,
                    loweredTop,
                    GetColumnWaterType(world, x, z),
                    WaterCellBehavior.FlowDependent,
                    result);
                currentTop = loweredTop;
            }

            if (behavior == WaterCellBehavior.Reservoir
                || behavior == WaterCellBehavior.FixedReservoir
                || GetFlowStrength(behavior, currentTop, groundTop, source) < 3)
            {
                DetachPreviousDownstream(world, state, columnIndex);
                state.SetWaterfallEdges(columnIndex, WaterfallEdgeFlags.None);
                SetWaterfallFlag(world, x, z, false, result);
                return;
            }

            var flowStrength = GetFlowStrength(
                behavior,
                currentTop,
                groundTop,
                source);
            var maximumTargetSurface = currentTop - 1;
            var previousDirection = state.GetFlowDirection(columnIndex);
            if (!TryChooseTarget(
                    world,
                    state,
                    columnIndex,
                    x,
                    z,
                    maximumTargetSurface,
                    out var targetIndex,
                    out var direction,
                    out var targetX,
                    out var targetZ))
            {
                DetachPreviousDownstream(world, state, columnIndex);
                state.SetWaterfallEdges(columnIndex, WaterfallEdgeFlags.None);
                SetWaterfallFlag(world, x, z, false, result);
                return;
            }

            if (previousDirection != WaterFlowDirection.None
                && previousDirection != direction)
            {
                DetachDownstream(
                    world,
                    state,
                    columnIndex,
                    x,
                    z,
                    previousDirection);
            }

            state.SetFlowDirection(columnIndex, direction);
            var targetGround =
                world.GetSurfaceColumn(targetX, targetZ).SolidTopUnits * 2;
            var targetStrength = Math.Min(
                flowStrength - 1,
                maximumTargetSurface - targetGround);
            if (targetStrength < WaterState.MinimumVisibleAmount)
            {
                DetachPreviousDownstream(world, state, columnIndex);
                state.SetWaterfallEdges(columnIndex, WaterfallEdgeFlags.None);
                SetWaterfallFlag(world, x, z, false, result);
                return;
            }

            // A drop transfers flow strength, not the upstream absolute water
            // surface. The vertical gap is represented by the waterfall edge.
            var targetTop = targetGround + targetStrength;
            var targetBehavior = GetColumnBehavior(world, targetX, targetZ);
            if (targetBehavior != WaterCellBehavior.FixedReservoir
                && targetBehavior != WaterCellBehavior.Reservoir
                && targetBehavior != WaterCellBehavior.Source)
            {
                // A FlowDependent column has one authoritative upstream. Match
                // its requested strength in both directions so legacy tall
                // water columns are drained instead of being preserved.
                SetColumnWaterTop(
                    world,
                    targetX,
                    targetZ,
                    targetTop,
                    GetColumnWaterType(world, x, z),
                    WaterCellBehavior.FlowDependent,
                    result);

                state.SetUpstream(targetIndex, columnIndex);
            }

            var targetSurface = Math.Max(
                targetGround,
                GetLogicalWaterTopTenths(world, targetX, targetZ));
            var isWaterfall = currentTop - targetSurface >= 4;
            state.SetWaterfallEdges(
                columnIndex,
                isWaterfall
                    ? (WaterfallEdgeFlags)(1 << (int)direction)
                    : WaterfallEdgeFlags.None);
            SetWaterfallFlag(world, x, z, isWaterfall, result);
            state.Enqueue(targetIndex);
        }

        private static bool TryChooseTarget(
            WorldData world,
            HydrologyState state,
            int currentIndex,
            int x,
            int z,
            int targetTop,
            out int targetIndex,
            out WaterFlowDirection direction,
            out int targetX,
            out int targetZ)
        {
            targetIndex = -1;
            direction = WaterFlowDirection.None;
            targetX = -1;
            targetZ = -1;
            var bestSurface = int.MaxValue;
            var previousDirection = state.GetFlowDirection(currentIndex);
            var bestTie = int.MaxValue;

            if (previousDirection != WaterFlowDirection.None
                && TryEvaluateDirection(
                    world,
                    state,
                    currentIndex,
                    x,
                    z,
                    targetTop,
                    (int)previousDirection,
                    out targetIndex,
                    out targetX,
                    out targetZ,
                    out _))
            {
                direction = previousDirection;
                return true;
            }

            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                if (!TryEvaluateDirection(
                        world,
                        state,
                        currentIndex,
                        x,
                        z,
                        targetTop,
                        directionIndex,
                        out var nextIndex,
                        out var nextX,
                        out var nextZ,
                        out var obstruction))
                {
                    continue;
                }

                var candidateDirection = (WaterFlowDirection)directionIndex;
                var tie = candidateDirection == previousDirection
                    ? -1
                    : DeterministicTie(world.Seed, nextX, nextZ, directionIndex);
                if (obstruction > bestSurface
                    || (obstruction == bestSurface && tie >= bestTie))
                {
                    continue;
                }

                bestSurface = obstruction;
                bestTie = tie;
                targetX = nextX;
                targetZ = nextZ;
                targetIndex = nextIndex;
                direction = candidateDirection;
            }

            return targetIndex >= 0;
        }

        private static bool TryEvaluateDirection(
            WorldData world,
            HydrologyState state,
            int currentIndex,
            int x,
            int z,
            int maximumTargetSurface,
            int directionIndex,
            out int targetIndex,
            out int targetX,
            out int targetZ,
            out int obstruction)
        {
            targetX = x + Directions[directionIndex].x;
            targetZ = z + Directions[directionIndex].z;
            targetIndex = -1;
            obstruction = int.MaxValue;
            if (!world.ContainsColumn(targetX, targetZ))
            {
                return false;
            }

            targetIndex = WorldIndex.EncodeColumn(world, targetX, targetZ);
            if (state.GetUpstream(currentIndex) == targetIndex)
            {
                return false;
            }

            var targetUpstream = state.GetUpstream(targetIndex);
            if (targetUpstream >= 0 && targetUpstream != currentIndex)
            {
                return false;
            }

            var column = world.GetSurfaceColumn(targetX, targetZ);
            var ground = column.SolidTopUnits * 2;
            if (maximumTargetSurface - ground
                < WaterState.MinimumVisibleAmount)
            {
                return false;
            }

            var waterTop = GetLogicalWaterTopTenths(world, targetX, targetZ);
            obstruction = Math.Max(ground, waterTop);
            return true;
        }

        private static int GetFlowStrength(
            WaterCellBehavior behavior,
            int waterTop,
            int groundTop,
            WaterSourceGroupData source)
        {
            if (behavior == WaterCellBehavior.Source && source != null)
            {
                return WaterState.MaximumAmount;
            }

            return Math.Clamp(
                waterTop - groundTop,
                0,
                WaterState.MaximumAmount);
        }

        private static void DetachPreviousDownstream(
            WorldData world,
            HydrologyState state,
            int columnIndex)
        {
            var direction = state.GetFlowDirection(columnIndex);
            if (direction == WaterFlowDirection.None)
            {
                return;
            }

            WorldIndex.DecodeColumn(world, columnIndex, out var x, out var z);
            DetachDownstream(
                world,
                state,
                columnIndex,
                x,
                z,
                direction);
            state.SetFlowDirection(columnIndex, WaterFlowDirection.None);
        }

        private static void DetachDownstream(
            WorldData world,
            HydrologyState state,
            int columnIndex,
            int x,
            int z,
            WaterFlowDirection direction)
        {
            var targetX = x + Directions[(int)direction].x;
            var targetZ = z + Directions[(int)direction].z;
            if (!world.ContainsColumn(targetX, targetZ))
            {
                return;
            }

            var targetIndex = WorldIndex.EncodeColumn(world, targetX, targetZ);
            if (state.ClearUpstreamIfMatches(targetIndex, columnIndex))
            {
                state.EnqueueColumnAndNeighbors(targetIndex);
            }
        }

        private static bool HasValidUpstream(
            WorldData world,
            HydrologyState state,
            int columnIndex,
            int currentTop)
        {
            var upstream = state.GetUpstream(columnIndex);
            if (upstream < 0)
            {
                return false;
            }

            WorldIndex.DecodeColumn(world, upstream, out var upstreamX, out var upstreamZ);
            var upstreamTop = GetLogicalWaterTopTenths(world, upstreamX, upstreamZ);
            if (upstreamTop <= currentTop)
            {
                return false;
            }

            var direction = state.GetFlowDirection(upstream);
            if (direction == WaterFlowDirection.None)
            {
                return false;
            }

            WorldIndex.DecodeColumn(world, columnIndex, out var x, out var z);
            return upstreamX + Directions[(int)direction].x == x
                && upstreamZ + Directions[(int)direction].z == z;
        }

        private static void SetColumnWaterTop(
            WorldData world,
            int x,
            int z,
            int targetTopTenths,
            WaterType waterType,
            WaterCellBehavior behavior,
            HydrologyStepResult result)
        {
            var groundTopTenths = world.GetSurfaceColumn(x, z).SolidTopUnits * 2;
            targetTopTenths = Math.Clamp(
                targetTopTenths,
                groundTopTenths,
                world.Height * 10);

            for (var y = 0; y < world.Height; y++)
            {
                var cellIndex = WorldIndex.EncodeCell(world, x, y, z);
                var cell = world.GetCell(x, y, z);
                var persistentBehavior = world.WaterState.GetBehavior(cellIndex);
                if (persistentBehavior == WaterCellBehavior.Source
                    || persistentBehavior == WaterCellBehavior.Reservoir
                    || persistentBehavior == WaterCellBehavior.FixedReservoir)
                {
                    continue;
                }

                var baseTenths = y * 10;
                var solidTenths = cell.SolidFill * 2;
                var waterBottom = Math.Max(baseTenths + solidTenths, groundTopTenths);
                var available = (WorldGrid.HeightStepsPerCell - cell.SolidFill) * 2;
                var amount = (byte)Math.Clamp(
                    targetTopTenths - waterBottom,
                    0,
                    available);
                var previousAmount = world.WaterState.GetAmount(cellIndex);
                var nextBehavior = amount > 0
                    ? behavior
                    : WaterCellBehavior.None;
                if (previousAmount != amount
                    || persistentBehavior != nextBehavior)
                {
                    world.WaterState.SetCell(cellIndex, amount, nextBehavior);
                    result.HasPersistentChanges = true;
                    result.ChangedCellIndices.Add(cellIndex);
                    result.ChangedColumnIndices.Add(
                        WorldIndex.EncodeColumn(world, x, z));
                }

                var renderFill = amount == 0
                    ? (byte)0
                    : (byte)Math.Min(
                        WorldGrid.HeightStepsPerCell - cell.SolidFill,
                        (amount + 1) / 2);
                var previousCell = cell;
                cell.WaterFill = renderFill;
                cell.Water = renderFill > 0 ? waterType : WaterType.None;
                if (renderFill > 0)
                {
                    cell.Flags |= CellFlags.River | CellFlags.Generated;
                }
                else
                {
                    cell.Flags &= ~(CellFlags.River | CellFlags.Waterfall);
                }

                if (!cell.Equals(previousCell))
                {
                    world.SetCellForEdit(x, y, z, cell);
                    result.HasRenderChanges = true;
                    result.ChangedCellIndices.Add(cellIndex);
                    result.ChangedColumnIndices.Add(
                        WorldIndex.EncodeColumn(world, x, z));
                }
            }
        }

        private static void SetWaterfallFlag(
            WorldData world,
            int x,
            int z,
            bool enabled,
            HydrologyStepResult result)
        {
            var column = world.GetSurfaceColumn(x, z);
            if (!column.HasWater)
            {
                return;
            }

            var y = column.WaterCellY;
            var cell = world.GetCell(x, y, z);
            var previous = cell;
            if (enabled) cell.Flags |= CellFlags.Waterfall;
            else cell.Flags &= ~CellFlags.Waterfall;
            if (cell.Equals(previous))
            {
                return;
            }

            world.SetCellForEdit(x, y, z, cell);
            result.HasRenderChanges = true;
            result.HasPersistentChanges = true;
            result.ChangedCellIndices.Add(WorldIndex.EncodeCell(world, x, y, z));
            result.ChangedColumnIndices.Add(WorldIndex.EncodeColumn(world, x, z));
        }

        private static int GetLogicalWaterTopTenths(WorldData world, int x, int z)
        {
            for (var y = world.Height - 1; y >= 0; y--)
            {
                var index = WorldIndex.EncodeCell(world, x, y, z);
                var amount = world.WaterState.GetAmount(index);
                if (amount == 0)
                {
                    continue;
                }

                var cell = world.GetCell(x, y, z);
                return y * 10 + cell.SolidFill * 2 + amount;
            }

            return 0;
        }

        private static WaterCellBehavior GetColumnBehavior(
            WorldData world,
            int x,
            int z)
        {
            var fallback = WaterCellBehavior.None;
            for (var y = world.Height - 1; y >= 0; y--)
            {
                var index = WorldIndex.EncodeCell(world, x, y, z);
                var behavior = world.WaterState.GetBehavior(index);
                if (behavior == WaterCellBehavior.None)
                {
                    continue;
                }

                if (world.WaterState.GetAmount(index) > 0)
                {
                    return behavior;
                }

                fallback = behavior;
            }

            return fallback;
        }

        private static WaterType GetColumnWaterType(WorldData world, int x, int z)
        {
            var column = world.GetSurfaceColumn(x, z);
            return column.HasWater && column.Water != WaterType.None
                ? column.Water
                : WaterType.Fresh;
        }

        private static WaterSourceGroupData FindSourceGroup(
            WorldData world,
            int x,
            int z)
        {
            for (var y = 0; y < world.Height; y++)
            {
                var index = WorldIndex.EncodeCell(world, x, y, z);
                var groupId = world.WaterState.GetSourceGroupId(index);
                if (groupId == 0)
                {
                    continue;
                }

                for (var groupIndex = 0;
                     groupIndex < world.WaterState.SourceGroups.Count;
                     groupIndex++)
                {
                    var group = world.WaterState.SourceGroups[groupIndex];
                    if (group.Id == groupId)
                    {
                        return group;
                    }
                }
            }

            return null;
        }

        private static bool IsPrimarySourceColumn(
            WorldData world,
            WaterSourceGroupData source,
            int x,
            int z)
        {
            if (source.CellIndices.Count == 0)
            {
                return false;
            }

            var primary = WorldIndex.DecodeCell(world, source.CellIndices[0]);
            return primary.X == x && primary.Z == z;
        }

        private static int DeterministicTie(int seed, int x, int z, int direction)
        {
            unchecked
            {
                var hash = seed;
                hash = hash * 397 ^ x;
                hash = hash * 397 ^ z;
                return (hash * 397 ^ direction) & int.MaxValue;
            }
        }
    }
}
