using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldEditController : MonoBehaviour
    {
        [Header("History")]
        [SerializeField, Min(1)] private int historyLimit = 30;

        private readonly Stack<WorldEditRecord> undoRecords = new();
        private readonly Stack<WorldEditRecord> redoRecords = new();

        private WorldRuntime boundRuntime;
        private WorldData boundWorld;
        private WorldEditTransaction activeTransaction;

        public WorldData BoundWorld => boundWorld;
        public bool HasActiveTransaction => activeTransaction != null;
        public bool CanUndo => activeTransaction == null && undoRecords.Count > 0;
        public bool CanRedo => activeTransaction == null && redoRecords.Count > 0;
        public int HistoryLimit => historyLimit;

        public event Action<WorldChangeSet> ChangeCommitted;
        public event Action HistoryChanged;

        private void OnValidate()
        {
            historyLimit = Math.Max(1, historyLimit);
            TrimHistory(undoRecords);
            TrimHistory(redoRecords);
        }

        public void Bind(WorldRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            Unbind();
            boundRuntime = runtime;
            boundWorld = runtime.Data;
        }

        public void Unbind()
        {
            activeTransaction?.Cancel();
            activeTransaction = null;
            boundWorld = null;
            boundRuntime = null;
            ClearHistory();
        }

        public void ClearHistory()
        {
            undoRecords.Clear();
            redoRecords.Clear();
            HistoryChanged?.Invoke();
        }

        public WorldEditTransaction BeginTransaction()
        {
            if (boundWorld == null)
            {
                throw new InvalidOperationException(
                    "WorldEditController is not bound to a world.");
            }

            if (activeTransaction != null)
            {
                throw new InvalidOperationException(
                    "Another world edit transaction is already active.");
            }

            activeTransaction = new WorldEditTransaction(this, boundRuntime);
            return activeTransaction;
        }

        public WorldChangeSet Commit(WorldEditTransaction transaction)
        {
            return Commit(transaction, true);
        }

        internal WorldChangeSet CommitWithoutHistory(
            WorldEditTransaction transaction)
        {
            return Commit(transaction, false);
        }

        public void Rollback(WorldEditTransaction transaction)
        {
            EnsureActiveTransaction(transaction);
            transaction.Cancel();
            activeTransaction = null;
        }

        public WorldChangeSet SetCell(
            int x,
            int y,
            int z,
            CellData cell)
        {
            var transaction = BeginTransaction();
            transaction.SetCell(x, y, z, cell);
            return Commit(transaction);
        }

        public WorldChangeSet SetSolidHeight(
            int x,
            int z,
            int heightUnits,
            SurfaceType surface = SurfaceType.Ground)
        {
            var transaction = BeginTransaction();
            transaction.SetSolidHeight(x, z, heightUnits, surface);
            return Commit(transaction);
        }

        public WorldChangeSet SetWaterLevel(
            int x,
            int z,
            int waterSurfaceUnits)
        {
            var transaction = BeginTransaction();
            transaction.SetWaterLevel(
                x,
                z,
                waterSurfaceUnits);
            return Commit(transaction);
        }

        public WorldChangeSet SetSurfaceType(
            int x,
            int z,
            SurfaceType surface)
        {
            var transaction = BeginTransaction();
            transaction.SetSurfaceType(x, z, surface);
            return Commit(transaction);
        }

        public WorldChangeSet SetRoad(
            int x,
            int z,
            RoadData road)
        {
            var transaction = BeginTransaction();
            transaction.SetRoad(x, z, road);
            return Commit(transaction);
        }

        public bool Undo()
        {
            if (!CanUndo)
            {
                return false;
            }

            var record = undoRecords.Pop();
            try
            {
                ApplyRecord(record, usePreviousValues: true);
            }
            catch
            {
                undoRecords.Push(record);
                HistoryChanged?.Invoke();
                throw;
            }

            redoRecords.Push(record);
            TrimHistory(redoRecords);
            HistoryChanged?.Invoke();
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo)
            {
                return false;
            }

            var record = redoRecords.Pop();
            try
            {
                ApplyRecord(record, usePreviousValues: false);
            }
            catch
            {
                redoRecords.Push(record);
                HistoryChanged?.Invoke();
                throw;
            }

            undoRecords.Push(record);
            TrimHistory(undoRecords);
            HistoryChanged?.Invoke();
            return true;
        }

        private WorldChangeSet Commit(
            WorldEditTransaction transaction,
            bool recordUndo)
        {
            EnsureActiveTransaction(transaction);
            if (!transaction.HasChanges)
            {
                transaction.Complete();
                activeTransaction = null;
                return null;
            }

            var cellChanges = transaction.CopyCellChanges();
            var changedColumns = new HashSet<CellColumnCoordinate>();

            try
            {
                for (var index = 0; index < cellChanges.Length; index++)
                {
                    var change = cellChanges[index];
                    boundWorld.SetCellForEdit(
                        change.Coordinate.X,
                        change.Coordinate.Y,
                        change.Coordinate.Z,
                        change.Current);
                    changedColumns.Add(new CellColumnCoordinate(
                        change.Coordinate.X,
                        change.Coordinate.Z));
                }

                foreach (var column in changedColumns)
                {
                    if (!boundWorld.HasTerrainCell(column.X, column.Z))
                    {
                        throw new InvalidOperationException(
                            $"World edit would remove every solid cell from column {column}.");
                    }

                }
            }
            catch
            {
                RestorePreviousValues(cellChanges);
                RebuildColumns(changedColumns);
                transaction.Cancel();
                activeTransaction = null;
                throw;
            }

            var affectedSections = BuildAffectedSections(cellChanges);
            var changeSet = BuildChangeSet(
                cellChanges,
                changedColumns,
                affectedSections);
            transaction.Complete();
            activeTransaction = null;

            if (recordUndo)
            {
                undoRecords.Push(new WorldEditRecord(cellChanges));
                TrimHistory(undoRecords);
                redoRecords.Clear();
                HistoryChanged?.Invoke();
            }

            ChangeCommitted?.Invoke(changeSet);
            return changeSet;
        }

        private void TrimHistory(Stack<WorldEditRecord> records)
        {
            if (records.Count <= historyLimit)
            {
                return;
            }

            var ordered = records.ToArray();
            records.Clear();
            for (var index = historyLimit - 1; index >= 0; index--)
            {
                records.Push(ordered[index]);
            }
        }

        private void ApplyRecord(
            WorldEditRecord record,
            bool usePreviousValues)
        {
            var transaction = BeginTransaction();
            for (var index = 0; index < record.CellChanges.Length; index++)
            {
                var change = record.CellChanges[index];
                transaction.SetCell(
                    change.Coordinate.X,
                    change.Coordinate.Y,
                    change.Coordinate.Z,
                    usePreviousValues ? change.Previous : change.Current);
            }

            Commit(transaction, false);
        }

        private void RestorePreviousValues(CellEdit[] cellChanges)
        {
            for (var index = 0; index < cellChanges.Length; index++)
            {
                var change = cellChanges[index];
                boundWorld.SetCellForEdit(
                    change.Coordinate.X,
                    change.Coordinate.Y,
                    change.Coordinate.Z,
                    change.Previous);
            }

        }

        private void RebuildColumns(IEnumerable<CellColumnCoordinate> columns)
        {
            var changedColumns = columns as IReadOnlyCollection<CellColumnCoordinate>
                ?? new HashSet<CellColumnCoordinate>(columns);
            boundRuntime.ChangeApplier.RebuildDerived(
                WorldChangeType.CellStructure | WorldChangeType.Surface,
                changedColumns as IReadOnlyList<CellColumnCoordinate>
                    ?? new List<CellColumnCoordinate>(changedColumns),
                rebuildNavigationColumns: true,
                rebuildWaterDistances: true);
        }

        private ChunkSectionCoordinate[] BuildAffectedSections(CellEdit[] cellChanges)
        {
            var sections = new HashSet<ChunkSectionCoordinate>();
            for (var index = 0; index < cellChanges.Length; index++)
            {
                var cell = cellChanges[index].Coordinate;
                AddCellAndBoundarySections(sections, cell.X, cell.Y, cell.Z);
            }

            var result = new ChunkSectionCoordinate[sections.Count];
            sections.CopyTo(result);
            Array.Sort(result, CompareSections);
            return result;
        }

        private void AddCellAndBoundarySections(
            HashSet<ChunkSectionCoordinate> sections,
            int x,
            int y,
            int z)
        {
            var chunkX = WorldCoordinateUtility.FloorDivide(
                x,
                boundWorld.ChunkSizeX);
            var chunkY = WorldCoordinateUtility.FloorDivide(
                y,
                boundWorld.ChunkSectionSizeY);
            var chunkZ = WorldCoordinateUtility.FloorDivide(
                z,
                boundWorld.ChunkSizeZ);
            var localX = WorldCoordinateUtility.PositiveModulo(
                x,
                boundWorld.ChunkSizeX);
            var localY = WorldCoordinateUtility.PositiveModulo(
                y,
                boundWorld.ChunkSectionSizeY);
            var localZ = WorldCoordinateUtility.PositiveModulo(
                z,
                boundWorld.ChunkSizeZ);

            var minimumX = localX == 0 ? chunkX - 1 : chunkX;
            var maximumX = localX == boundWorld.ChunkSizeX - 1
                ? chunkX + 1
                : chunkX;
            var minimumY = localY == 0 ? chunkY - 1 : chunkY;
            var maximumY = localY == boundWorld.ChunkSectionSizeY - 1
                ? chunkY + 1
                : chunkY;
            var minimumZ = localZ == 0 ? chunkZ - 1 : chunkZ;
            var maximumZ = localZ == boundWorld.ChunkSizeZ - 1
                ? chunkZ + 1
                : chunkZ;

            for (var affectedY = minimumY; affectedY <= maximumY; affectedY++)
            for (var affectedZ = minimumZ; affectedZ <= maximumZ; affectedZ++)
            for (var affectedX = minimumX; affectedX <= maximumX; affectedX++)
            {
                if ((uint)affectedX < boundWorld.ChunkCountX
                    && (uint)affectedY < boundWorld.ChunkSectionCountY
                    && (uint)affectedZ < boundWorld.ChunkCountZ)
                {
                    sections.Add(new ChunkSectionCoordinate(
                        affectedX,
                        affectedY,
                        affectedZ));
                }
            }
        }

        private WorldChangeSet BuildChangeSet(
            CellEdit[] cellChanges,
            HashSet<CellColumnCoordinate> changedColumns,
            ChunkSectionCoordinate[] affectedSections)
        {
            var changedCells = new CellCoordinate[cellChanges.Length];
            var changeTypes = WorldChangeType.None;
            var hasBounds = false;
            var minimum = default(CellCoordinate);
            var maximum = default(CellCoordinate);

            for (var index = 0; index < cellChanges.Length; index++)
            {
                var change = cellChanges[index];
                changedCells[index] = change.Coordinate;
                changeTypes |= ClassifyChange(change.Previous, change.Current);
                ExpandBounds(
                    change.Coordinate,
                    ref hasBounds,
                    ref minimum,
                    ref maximum);
            }

            Array.Sort(changedCells);
            var changedColumnArray = new CellColumnCoordinate[changedColumns.Count];
            changedColumns.CopyTo(changedColumnArray);
            Array.Sort(changedColumnArray);

            return boundRuntime.ChangeApplier.Apply(
                changeTypes,
                changedCells,
                changedColumnArray,
                affectedSections,
                new CellBounds(minimum, maximum),
                rebuildNavigationColumns: true,
                rebuildWaterDistances: true);
        }

        private static WorldChangeType ClassifyChange(
            CellData previous,
            CellData current)
        {
            var types = WorldChangeType.None;
            if (previous.Terrain.SolidHeight != current.Terrain.SolidHeight
                || previous.WaterHeight != current.WaterHeight)
            {
                types |= WorldChangeType.CellStructure
                    | WorldChangeType.Surface
                    | WorldChangeType.Navigation;
            }

            if (previous.Terrain.Material != current.Terrain.Material
                || previous.Terrain.Surface != current.Terrain.Surface)
            {
                types |= WorldChangeType.Material
                    | WorldChangeType.Surface
                    | WorldChangeType.Navigation;
            }

            if (!previous.Water.Equals(current.Water))
            {
                types |= WorldChangeType.WaterTopology
                    | WorldChangeType.Surface
                    | WorldChangeType.Navigation
                    | WorldChangeType.Ecology;
            }

            if (!previous.Road.Equals(current.Road))
            {
                types |= WorldChangeType.RoadTopology
                    | WorldChangeType.Navigation;
            }

            return types;
        }

        private static void ExpandBounds(
            CellCoordinate coordinate,
            ref bool hasBounds,
            ref CellCoordinate minimum,
            ref CellCoordinate maximum)
        {
            if (!hasBounds)
            {
                minimum = coordinate;
                maximum = coordinate;
                hasBounds = true;
                return;
            }

            minimum = new CellCoordinate(
                Math.Min(minimum.X, coordinate.X),
                Math.Min(minimum.Y, coordinate.Y),
                Math.Min(minimum.Z, coordinate.Z));
            maximum = new CellCoordinate(
                Math.Max(maximum.X, coordinate.X),
                Math.Max(maximum.Y, coordinate.Y),
                Math.Max(maximum.Z, coordinate.Z));
        }

        private static int CompareSections(
            ChunkSectionCoordinate left,
            ChunkSectionCoordinate right)
        {
            var y = left.Y.CompareTo(right.Y);
            if (y != 0) return y;
            var z = left.Z.CompareTo(right.Z);
            return z != 0 ? z : left.X.CompareTo(right.X);
        }

        private void EnsureActiveTransaction(WorldEditTransaction transaction)
        {
            if (transaction == null)
            {
                throw new ArgumentNullException(nameof(transaction));
            }

            if (transaction != activeTransaction)
            {
                throw new InvalidOperationException(
                    "The transaction is not active on this WorldEditController.");
            }
        }

        private sealed class WorldEditRecord
        {
            public readonly CellEdit[] CellChanges;

            public WorldEditRecord(CellEdit[] cellChanges)
            {
                CellChanges = cellChanges;
            }
        }
    }

    public sealed class WorldEditTransaction
    {
        private readonly WorldEditController owner;
        private readonly WorldRuntime runtime;
        private readonly WorldData world;
        private readonly Dictionary<CellCoordinate, CellEdit> cellChanges = new();

        private bool completed;

        public int ChangedCellCount => cellChanges.Count;
        public bool HasChanges => cellChanges.Count > 0;
        public bool IsCompleted => completed;

        internal WorldEditTransaction(
            WorldEditController owner,
            WorldRuntime runtime)
        {
            this.owner = owner;
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            world = runtime.Data;
        }

        public void SetCell(int x, int y, int z, CellData cell)
        {
            EnsureOpen();
            if (!world.Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Cell ({x}, {y}, {z}) is outside the world.");
            }

            if (IsTerrainProtected(x, y, z))
            {
                return;
            }

            var coordinate = new CellCoordinate(x, y, z);
            if (cellChanges.TryGetValue(coordinate, out var existing))
            {
                cell.Biome = existing.Current.Biome;
                cell.Normalize();
                if (existing.Previous.Equals(cell))
                {
                    cellChanges.Remove(coordinate);
                }
                else
                {
                    cellChanges[coordinate] = new CellEdit(
                        existing.Coordinate,
                        existing.Previous,
                        cell);
                }

                return;
            }

            var previous = world.GetCell(x, y, z);
            cell.Biome = previous.Biome;
            cell.Normalize();
            if (previous.Equals(cell))
            {
                return;
            }

            cellChanges.Add(
                coordinate,
                new CellEdit(
                    coordinate,
                    previous,
                    cell));
        }

        public bool SetRoad(int x, int z, RoadData road)
        {
            EnsureColumn(x, z);
            road.Normalize();
            if (road.HasRoad && runtime.Entities.HasBuildingInColumn(x, z))
            {
                return false;
            }

            var surface = runtime.SurfaceCache.GetSurfaceHeight(x, z);
            if (!surface.HasGround)
            {
                return false;
            }

            var cell = GetPendingCell(x, surface.GroundCellY, z);
            if (!cell.HasTerrain)
            {
                return false;
            }

            cell.Road = road;
            SetCell(x, surface.GroundCellY, z, cell);
            return true;
        }

        public bool RaiseColumn(int x, int z)
        {
            EnsureColumn(x, z);
            if (HasTerrainProtectedInColumn(x, z))
            {
                return false;
            }

            if (!TryGetLowestPendingSolidY(x, z, out var lowestSolidY)
                || !TryGetHighestPendingSolidY(x, z, out var highestSolidY)
                || highestSolidY >= world.Height - 1)
            {
                return false;
            }

            var lowestTerrain = TerrainCellState.FromCell(
                GetPendingCell(x, lowestSolidY, z));
            for (var y = highestSolidY; y >= lowestSolidY; y--)
            {
                var terrain = TerrainCellState.FromCell(
                    GetPendingCell(x, y, z));
                SetTerrainState(x, y + 1, z, terrain);
            }

            SetTerrainState(
                x,
                lowestSolidY,
                z,
                lowestTerrain.CreateFoundation());
            return true;
        }

        public bool LowerColumn(int x, int z)
        {
            EnsureColumn(x, z);
            if (HasTerrainProtectedInColumn(x, z))
            {
                return false;
            }

            if (!TryGetLowestPendingSolidY(x, z, out var lowestSolidY))
            {
                return false;
            }

            var removalY = lowestSolidY + 1;
            if (removalY >= world.Height
                || !GetPendingCell(x, removalY, z).HasTerrain
                || !TryGetHighestPendingSolidY(
                    x,
                    z,
                    out var highestSolidY))
            {
                return false;
            }

            for (var y = lowestSolidY; y < highestSolidY; y++)
            {
                var terrain = TerrainCellState.FromCell(
                    GetPendingCell(x, y + 1, z));
                SetTerrainState(x, y, z, terrain);
            }

            SetTerrainState(
                x,
                highestSolidY,
                z,
                default);
            return true;
        }

        public bool TryClearCell(int x, int y, int z)
        {
            EnsureOpen();
            if (!world.Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Cell ({x}, {y}, {z}) is outside the world.");
            }

            if (IsTerrainProtected(x, y, z))
            {
                return false;
            }

            var current = GetPendingCell(x, y, z);
            if (current.Equals(default(CellData)))
            {
                return false;
            }

            if (current.HasTerrain
                && TryGetLowestPendingSolidY(x, z, out var lowestSolidY)
                && y == lowestSolidY)
            {
                return false;
            }

            SetCell(x, y, z, default);
            return true;
        }

        public void SetSolidHeight(
            int x,
            int z,
            int heightUnits,
            SurfaceType surface = SurfaceType.Ground)
        {
            EnsureColumn(x, z);
            if (HasTerrainProtectedInColumn(x, z))
            {
                return;
            }

            heightUnits = Math.Clamp(
                heightUnits,
                0,
                world.Height * WorldGrid.HeightStepsPerCell);

            for (var y = 0; y < world.Height; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var fill = (byte)Math.Clamp(
                    heightUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var cell = GetPendingCell(x, y, z);
                cell.Terrain.SolidHeight = fill;
                if (fill > 0)
                {
                    cell.Terrain.Material =
                        y < Math.Max(
                            0,
                            heightUnits / WorldGrid.HeightStepsPerCell - 2)
                            ? MaterialType.Rock
                            : MaterialType.Soil;
                    cell.Terrain.Geology = MaterialType.Rock;
                    cell.Terrain.Surface =
                        fill < WorldGrid.HeightStepsPerCell
                        || baseUnits + fill == heightUnits
                            ? surface
                            : SurfaceType.None;
                }
                else
                {
                    cell.Terrain.Material = MaterialType.None;
                    cell.Terrain.Surface = SurfaceType.None;
                    cell.Terrain.Geology = MaterialType.None;
                }

                SetCell(x, y, z, cell);
            }
        }

        public void SetWaterLevel(
            int x,
            int z,
            int waterSurfaceUnits)
        {
            EnsureColumn(x, z);
            if (HasTerrainProtectedInColumn(x, z))
            {
                return;
            }

            waterSurfaceUnits = Math.Clamp(
                waterSurfaceUnits,
                0,
                world.Height * WorldGrid.HeightStepsPerCell);

            for (var y = 0; y < world.Height; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var cell = GetPendingCell(x, y, z);
                var available =
                    WorldGrid.HeightStepsPerCell - cell.Terrain.SolidHeight;
                var desiredTop = Math.Clamp(
                    waterSurfaceUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var waterFill = (byte)Math.Clamp(
                    desiredTop - cell.Terrain.SolidHeight,
                    0,
                    available);
                cell.Water = waterFill > 0
                    ? new WaterData
                    {
                        Amount = WaterAmount.FromRenderFill(
                            waterFill,
                            available),
                        Role = WaterRole.Source,
                        Type = WaterType.Pond
                    }
                    : default;
                SetCell(x, y, z, cell);
            }
        }

        public void SetSurfaceType(
            int x,
            int z,
            SurfaceType surface)
        {
            EnsureColumn(x, z);
            for (var y = world.Height - 1; y >= 0; y--)
            {
                var cell = GetPendingCell(x, y, z);
                if (!cell.HasTerrain)
                {
                    continue;
                }

                cell.Terrain.Surface = surface;
                SetCell(x, y, z, cell);
                return;
            }
        }

        public WorldChangeSet Commit()
        {
            EnsureOpen();
            return owner.Commit(this);
        }

        public void Rollback()
        {
            EnsureOpen();
            owner.Rollback(this);
        }

        internal CellEdit[] CopyCellChanges()
        {
            var result = new CellEdit[cellChanges.Count];
            cellChanges.Values.CopyTo(result, 0);
            Array.Sort(result, (left, right) =>
                left.Coordinate.CompareTo(right.Coordinate));
            return result;
        }

        internal void Complete()
        {
            completed = true;
        }

        internal void Cancel()
        {
            completed = true;
            cellChanges.Clear();
        }

        private CellData GetPendingCell(int x, int y, int z)
        {
            var coordinate = new CellCoordinate(x, y, z);
            return cellChanges.TryGetValue(coordinate, out var change)
                ? change.Current
                : world.GetCell(x, y, z);
        }

        private bool IsTerrainProtected(int x, int y, int z) =>
            runtime.Entities.IsTerrainProtected(
                new CellCoordinate(x, y, z));

        private bool HasTerrainProtectedInColumn(int x, int z) =>
            runtime.Entities.HasTerrainProtectedInColumn(x, z);

        public bool TryGetLowestPendingSolidY(
            int x,
            int z,
            out int lowestY)
        {
            EnsureColumn(x, z);
            for (var y = 0; y < world.Height; y++)
            {
                if (GetPendingCell(x, y, z).HasTerrain)
                {
                    lowestY = y;
                    return true;
                }
            }

            lowestY = -1;
            return false;
        }

        private bool TryGetHighestPendingSolidY(
            int x,
            int z,
            out int highestY)
        {
            EnsureColumn(x, z);
            for (var y = world.Height - 1; y >= 0; y--)
            {
                if (GetPendingCell(x, y, z).HasTerrain)
                {
                    highestY = y;
                    return true;
                }
            }

            highestY = -1;
            return false;
        }

        private void SetTerrainState(
            int x,
            int y,
            int z,
            in TerrainCellState terrain)
        {
            var cell = GetPendingCell(x, y, z);
            terrain.ApplyTo(ref cell);
            SetCell(x, y, z, cell);
        }

        private static void ClearWater(ref CellData cell)
        {
            cell.Water = default;
        }

        private readonly struct TerrainCellState
        {
            private readonly MiniCivilization.World.Domain.TerrainData terrain;

            private TerrainCellState(
                MiniCivilization.World.Domain.TerrainData terrain) =>
                this.terrain = terrain;

            public static TerrainCellState FromCell(in CellData cell) =>
                new(cell.Terrain);

            public TerrainCellState CreateFoundation()
            {
                var foundation = terrain;
                foundation.Surface = SurfaceType.None;
                foundation.SolidHeight = WorldGrid.HeightStepsPerCell;
                return new TerrainCellState(foundation);
            }

            public void ApplyTo(ref CellData cell)
            {
                cell.Terrain = terrain;

                if (terrain.HasTerrain)
                {
                    ClearWater(ref cell);
                }
            }
        }

        private void EnsureColumn(int x, int z)
        {
            EnsureOpen();
            if (!world.ContainsColumn(x, z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Column ({x}, {z}) is outside the world.");
            }
        }

        private void EnsureOpen()
        {
            if (completed)
            {
                throw new InvalidOperationException(
                    "The world edit transaction is already completed.");
            }
        }

    }

    internal readonly struct CellEdit
    {
        public readonly CellCoordinate Coordinate;
        public readonly CellData Previous;
        public readonly CellData Current;

        public CellEdit(
            CellCoordinate coordinate,
            CellData previous,
            CellData current)
        {
            Coordinate = coordinate;
            Previous = previous;
            Current = current;
        }
    }

}
