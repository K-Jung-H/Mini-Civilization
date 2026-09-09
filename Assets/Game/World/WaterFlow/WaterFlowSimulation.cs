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
        public readonly Dictionary<CellCoordinate, WaterData> PreviousWater = new();
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
            PreviousWater.Clear();
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
        private IEnumerator<int> waveWork;
        private long waveRevision;
        private readonly Dictionary<ChunkCoordinate, (Chunk Chunk, long Revision)> waveDependencies = new();
        private bool wavePrepared;
        private WorldData preparedWorld;
        private readonly HashSet<ChunkSectionCoordinate> publishedChunks = new();
        private readonly Dictionary<ChunkCoordinate, HashSet<int>> snapshotSections = new();
        private readonly SortedSet<CellCoordinate> orderedWave = new();
        private readonly Dictionary<ChunkCoordinate, CellCoordinate[]> savedFrontierChunks = new();
        private readonly Dictionary<ChunkCoordinate, List<CellCoordinate>> activeByChunk = new();
        private readonly HashSet<ChunkCoordinate> dirtyFrontierChunks = new();

        internal Dictionary<ChunkCoordinate, CellCoordinate[]> CaptureFrontierChunks()
        {
            foreach (var chunk in dirtyFrontierChunks)
            {
                var cells = new HashSet<CellCoordinate>();
                if (chunkStates.TryGetValue(chunk, out var state)) cells.UnionWith(state.Frontier);
                if (activeByChunk.TryGetValue(chunk, out var active)) cells.UnionWith(active);
                if (cells.Count == 0) savedFrontierChunks.Remove(chunk);
                else { var values = new CellCoordinate[cells.Count]; cells.CopyTo(values); savedFrontierChunks[chunk] = values; }
            }
            dirtyFrontierChunks.Clear();
            return new Dictionary<ChunkCoordinate, CellCoordinate[]>(savedFrontierChunks);
        }

        private void ClearActiveWave()
        {
            foreach (var chunk in activeByChunk.Keys) dirtyFrontierChunks.Add(chunk);
            activeByChunk.Clear();
            activeWave.Clear();
        }

        private readonly Func<CellCoordinate[]> captureFrontier;
        private readonly Func<bool> hasPendingSnapshot;

        public bool HasWork => waveWork != null || activeWave.Count > 0 || chunkStates.Count > 0;
        public bool HasRunnableWork => waveWork != null || activeWave.Count > 0
            || hasRunnableFrontier;
        public bool IsWaveInProgress => waveWork != null;
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
            captureFrontier = CaptureFrontier;
            hasPendingSnapshot = () => HasWork;
        }

        public void RestoreFrontier(
            WorldData world,
            WaterFlowState state,
            IReadOnlyList<CellCoordinate> frontier)
        {
            ValidateWorldAndState(world, state);
            CancelActiveWave(state, requeue: false);
            foreach (var chunk in chunkStates.Keys) dirtyFrontierChunks.Add(chunk);
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

        public void RestoreChunkFrontier(
            WorldData world,
            WaterFlowState state,
            IReadOnlyList<CellCoordinate> frontier)
        {
            ValidateWorldAndState(world, state);
            if (frontier == null || frontier.Count == 0)
            {
                return;
            }

            if (waveWork != null && (!wavePrepared || DependenciesChanged(world)))
                CancelActiveWave(state, requeue: true);
            for (var index = 0; index < frontier.Count; index++)
            {
                var cell = frontier[index];
                if (!world.TryGetCell(cell.X, cell.Y, cell.Z, out _))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(frontier),
                        "A saved water frontier Cell is not loaded.");
                }

                AddFrontier(cell);
            }

            RefreshRunnableFrontier();
            PersistFrontier(world, state);
        }

        public void DetachChunkFrontier(
            WorldData world,
            WaterFlowState state,
            ChunkCoordinate coordinate,
            List<CellCoordinate> target)
        {
            ValidateWorldAndState(world, state);
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            if (!wavePrepared || waveDependencies.ContainsKey(coordinate) || activeByChunk.ContainsKey(coordinate))
                CancelActiveWave(state, requeue: true);
            if (chunkStates.TryGetValue(coordinate, out var chunkState))
            {
                chunkStates.Remove(coordinate);
                dirtyFrontierChunks.Add(coordinate);
                target.AddRange(chunkState.Frontier);
                target.Sort();
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

            if (!wavePrepared || DependenciesChanged(world))
                CancelActiveWave(state, requeue: true);
            AddFrontier(restartWave);
            RefreshRunnableFrontier();
            PersistFrontier(world, state);
        }

        public void OnSimulationSetChanged(
            WorldData world,
            WaterFlowState state)
        {
            ValidateWorldAndState(world, state);
            if (!wavePrepared) CancelActiveWave(state, requeue: true);
            else
                foreach (var cell in activeWave)
                    if (!CanProcess(cell)) { CancelActiveWave(state, requeue: true); break; }
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
            if (waveWork != null && DependenciesChanged(world))
                CancelActiveWave(state, requeue: true);
            if (waveWork == null)
            {
                waveRevision = world.CellRevision;
                waveDependencies.Clear();
                wavePrepared = false;
                waveWork = RunWave(world, state, parameters).GetEnumerator();
            }
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var work = 0; work < maximumCells; work++)
            {
                if (!waveWork.MoveNext())
                {
                    waveWork.Dispose(); waveWork = null;
                    state.IsRecalculating = HasRunnableWork;
                    completedResult = result;
                    return true;
                }
                if ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d /
                    System.Diagnostics.Stopwatch.Frequency >= 2d) break;
            }
            return false;
        }

        private IEnumerable<int> RunWave(WorldData world, WaterFlowState state, WaterFlowParameters parameters)
        {
            result.Clear(); state.CancelResolutionPass();
            foreach (var work in PrepareWave()) yield return work;
            wavePrepared = true;
            if (activeWave.Count == 0) { state.IsRecalculating = false; yield break; }
            for (cursor = 0; cursor < activeWave.Count; cursor++)
            {
                var cell = activeWave[cursor];
                TrackDependencies(world, cell);
                state.StageResolvedCell(cell, ResolveDesiredWater(world, state, cell, parameters));
                yield return 0;
            }
            if (!state.HasStagedCells)
            {
                ClearActiveWave(); cursor = 0;
                RefreshRunnableFrontier(); PersistFrontier(world, state);
                yield break;
            }
            // Snapshot construction and writes are private until the entire wave is ready.
            foreach (var work in BuildApplySet(world, state)) yield return work;
            snapshotSections.Clear();
            foreach (var cell in applyCells)
            {
                for (var z = cell.Z - 1; z <= cell.Z + 1; z++)
                for (var x = cell.X - 1; x <= cell.X + 1; x++)
                {
                    if (!world.IsColumnLoaded(x, z)) continue;
                    var coordinate = ToChunk(new CellCoordinate(x, cell.Y, z));
                    if (!snapshotSections.TryGetValue(coordinate, out var sections))
                        snapshotSections.Add(coordinate, sections = new HashSet<int>());
                    for (var y = Math.Max(0, cell.Y - 1); y <= Math.Min(world.Height - 1, cell.Y + 1); y++)
                        sections.Add(y / world.ChunkSectionSizeY);
                }
                yield return 0;
            }
            preparedWorld = new WorldData(world.Settings);
            foreach (var pair in snapshotSections)
            {
                if (world.TryGetChunk(pair.Key, out var chunk))
                    preparedWorld.AttachGeneratedChunk(chunk.CopySections(pair.Value));
                yield return 0;
            }
            foreach (var work in ApplyStagedState(preparedWorld, state)) yield return work;
            foreach (var work in BuildNextWave(preparedWorld)) yield return work;
            publishedChunks.Clear();
            foreach (var cell in result.LogicalChangedCells)
            {
                var chunk = ToChunk(cell);
                publishedChunks.Add(new ChunkSectionCoordinate(chunk.X, cell.Y / world.ChunkSectionSizeY, chunk.Z));
                yield return 0;
            }
            world.PublishWaterCells(preparedWorld, publishedChunks);
            preparedWorld = null;
            snapshotSections.Clear();
            ClearActiveWave(); cursor = 0;
            RefreshRunnableFrontier(); PersistFrontier(world, state);
        }

        private bool DependenciesChanged(WorldData world)
        {
            if (waveRevision == world.CellRevision) return false;
            waveRevision = world.CellRevision;
            foreach (var pair in waveDependencies)
            {
                world.TryGetChunk(pair.Key, out var chunk);
                if (!ReferenceEquals(chunk, pair.Value.Chunk)
                    || (chunk != null && chunk.CellRevision != pair.Value.Revision)) return true;
            }
            return false;
        }

        private void TrackDependencies(WorldData world, CellCoordinate cell)
        {
            // Two horizontal cells cover donor look-ahead and visual neighbour queries.
            var minimum = ToChunk(new CellCoordinate(cell.X - 2, cell.Y, cell.Z - 2));
            var maximum = ToChunk(new CellCoordinate(cell.X + 2, cell.Y, cell.Z + 2));
            for (var z = minimum.Z; z <= maximum.Z; z++)
            for (var x = minimum.X; x <= maximum.X; x++)
            {
                var coordinate = new ChunkCoordinate(x, z);
                if (waveDependencies.ContainsKey(coordinate)) continue;
                world.TryGetChunk(coordinate, out var chunk);
                waveDependencies.Add(coordinate, (chunk, chunk?.CellRevision ?? 0));
            }
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
            var selectedDonor = default(CellCoordinate);
            var hasSelectedDonor = false;
            var currentTypeSupported = false;
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
                    ConsiderInflow(
                        aboveCell,
                        above.Amount,
                        (above.Flow & FlowDirection.Horizontal)
                        | FlowDirection.Down,
                        above.Type,
                        horizontal: false);
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

                ConsiderInflow(
                    donorCell,
                    candidateAmount,
                    outgoingDirection,
                    donor.Type,
                    horizontal: true);
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

            void ConsiderInflow(
                CellCoordinate donorCoordinate,
                byte amount,
                FlowDirection direction,
                WaterType type,
                bool horizontal)
            {
                if (amount == 0 || type == WaterType.None)
                {
                    return;
                }

                if (amount > desired.Amount)
                {
                    desired = CreateDynamicWater(amount, direction, type);
                    selectedDonor = donorCoordinate;
                    hasSelectedDonor = true;
                    currentTypeSupported = type == current.Type;
                    hasHorizontalInflow = horizontal;
                    return;
                }

                if (amount != desired.Amount)
                {
                    return;
                }

                desired.Flow |= direction;
                hasHorizontalInflow |= horizontal;
                if (type == current.Type)
                {
                    desired.Type = current.Type;
                    currentTypeSupported = true;
                    return;
                }

                if (!currentTypeSupported
                    && (!hasSelectedDonor
                        || donorCoordinate.CompareTo(selectedDonor) < 0))
                {
                    desired.Type = type;
                    selectedDonor = donorCoordinate;
                    hasSelectedDonor = true;
                }
            }
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

        private IEnumerable<int> BuildApplySet(
            WorldData world,
            WaterFlowState state)
        {
            applyCells.Clear();
            previousVisualStates.Clear();
            foreach (var pair in state.EnumerateStagedCells())
            {
                AddCellAndNeighbors(world, applyCells, pair.Key);
                yield return 0;
            }

            foreach (var cell in applyCells)
            {
                previousVisualStates[cell] =
                    WaterVisualState.Resolve(world, cell);
                yield return 0;
            }
        }

        private IEnumerable<int> ApplyStagedState(
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

                if (!previousWater.Amount.Equals(pair.Value.Amount))
                    result.PreviousWater[coordinate] = previousWater;
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
                if (previousWater.Type != cell.Water.Type)
                {
                    result.WaterTypeChangedCells.Add(pair.Key);
                }
                yield return 0;
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
                yield return 0;
            }

            state.CancelResolutionPass();
        }

        private IEnumerable<int> BuildNextWave(WorldData world)
        {
            nextWave.Clear();
            foreach (var cell in result.LogicalChangedCells)
            {
                AddCellAndNeighbors(world, nextWave, cell);
                yield return 0;
            }
            foreach (var cell in nextWave) { AddFrontier(cell); yield return 0; }
        }

        private IEnumerable<int> PrepareWave()
        {
            ClearActiveWave(); orderedWave.Clear();
            foreach (var pair in chunkStates)
                foreach (var cell in pair.Value.Frontier)
                { if (CanProcess(cell)) orderedWave.Add(cell); yield return 0; }
            foreach (var cell in orderedWave)
            {
                var chunk = ToChunk(cell);
                chunkStates[chunk].Frontier.Remove(cell);
                if (chunkStates[chunk].Frontier.Count == 0) chunkStates.Remove(chunk);
                activeWave.Add(cell); dirtyFrontierChunks.Add(chunk);
                if (!activeByChunk.TryGetValue(chunk, out var active)) activeByChunk.Add(chunk, active = new List<CellCoordinate>());
                active.Add(cell); yield return 0;
            }
            orderedWave.Clear(); cursor = 0;
            RefreshRunnableFrontier();
        }

        private void CancelActiveWave(
            WaterFlowState state,
            bool requeue)
        {

            waveWork?.Dispose(); waveWork = null;
            waveDependencies.Clear(); wavePrepared = false;
            preparedWorld = null; publishedChunks.Clear(); orderedWave.Clear();
            snapshotSections.Clear();
            state.CancelResolutionPass();
            if (requeue)
            {
                AddFrontier(activeWave);
            }

            ClearActiveWave();
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

            if (state.Frontier.Add(cell)) dirtyFrontierChunks.Add(chunk);
        }

        private bool streamingChanges;
        private bool frontierChangedDuringStreaming;

        internal void BeginStreamingChanges()
        {
            if (streamingChanges) throw new InvalidOperationException("Streaming changes already open.");
            streamingChanges = true;
        }

        internal void EndStreamingChanges(WorldData world, WaterFlowState state)
        {
            streamingChanges = false;
            if (!frontierChangedDuringStreaming) return;
            frontierChangedDuringStreaming = false;
            RefreshRunnableFrontier();
            PersistFrontier(world, state);
        }
        private void RefreshRunnableFrontier()
        {

            if (streamingChanges) return;
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
            if (streamingChanges)
            {
                frontierChangedDuringStreaming = true;
                return;
            }
            world.WaterFlowSchedule.InvalidateSnapshot(captureFrontier, hasPendingSnapshot);
            state.IsRecalculating = HasRunnableWork;
        }

        private CellCoordinate[] CaptureFrontier()
        {
            var parts = CaptureFrontierChunks();
            var count = 0;
            foreach (var part in parts.Values) count += part.Length;
            var frontier = new CellCoordinate[count];
            var offset = 0;
            foreach (var part in parts.Values) { part.CopyTo(frontier, offset); offset += part.Length; }
            Array.Sort(frontier);
            return frontier;
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
                && below.Water.Role != WaterRole.Source
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
