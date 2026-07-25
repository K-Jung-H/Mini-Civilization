using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldEditController : MonoBehaviour
    {
        private readonly Stack<WorldEditRecord> undoRecords = new();
        private readonly Stack<WorldEditRecord> redoRecords = new();

        private WorldData boundWorld;
        private WorldEditTransaction activeTransaction;

        public WorldData BoundWorld => boundWorld;
        public bool HasActiveTransaction => activeTransaction != null;
        public bool CanUndo => activeTransaction == null && undoRecords.Count > 0;
        public bool CanRedo => activeTransaction == null && redoRecords.Count > 0;

        public event Action<WorldChangeSet> ChangeCommitted;

        public void Bind(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            Unbind();
            boundWorld = world;
        }

        public void Unbind()
        {
            activeTransaction?.Cancel();
            activeTransaction = null;
            boundWorld = null;
            undoRecords.Clear();
            redoRecords.Clear();
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

            activeTransaction = new WorldEditTransaction(this, boundWorld);
            return activeTransaction;
        }

        public WorldChangeSet Commit(WorldEditTransaction transaction)
        {
            return Commit(transaction, true);
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
            int waterSurfaceUnits,
            WaterType water,
            CellFlags flags = CellFlags.None)
        {
            var transaction = BeginTransaction();
            transaction.SetWaterLevel(
                x,
                z,
                waterSurfaceUnits,
                water,
                flags);
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

        public WorldChangeSet SetBiome(int x, int z, BiomeType biome)
        {
            var transaction = BeginTransaction();
            transaction.SetBiome(x, z, biome);
            return Commit(transaction);
        }

        public bool Undo()
        {
            if (!CanUndo)
            {
                return false;
            }

            var record = undoRecords.Pop();
            ApplyRecord(record, usePreviousValues: true);
            redoRecords.Push(record);
            return true;
        }

        public bool Redo()
        {
            if (!CanRedo)
            {
                return false;
            }

            var record = redoRecords.Pop();
            ApplyRecord(record, usePreviousValues: false);
            undoRecords.Push(record);
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
            var environmentChanges = transaction.CopyEnvironmentChanges();
            var changedColumns = new HashSet<int>();

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
                    changedColumns.Add(WorldIndex.EncodeColumn(
                        boundWorld,
                        change.Coordinate.X,
                        change.Coordinate.Z));
                }

                for (var index = 0; index < environmentChanges.Length; index++)
                {
                    var change = environmentChanges[index];
                    WorldIndex.DecodeColumn(
                        boundWorld,
                        change.ColumnIndex,
                        out var x,
                        out var z);
                    boundWorld.SetColumnEnvironment(x, z, change.Current);
                    changedColumns.Add(change.ColumnIndex);
                }

                foreach (var columnIndex in changedColumns)
                {
                    WorldIndex.DecodeColumn(
                        boundWorld,
                        columnIndex,
                        out var x,
                        out var z);
                    if (!boundWorld.IsWaterSupported(x, z))
                    {
                        throw new InvalidOperationException(
                            $"World edit would leave unsupported water in column ({x}, {z}).");
                    }
                }
            }
            catch
            {
                RestorePreviousValues(cellChanges, environmentChanges);
                RebuildColumns(changedColumns);
                transaction.Cancel();
                activeTransaction = null;
                throw;
            }

            RebuildColumns(changedColumns);
            var changeId = boundWorld.AdvanceChangeId();
            var affectedChunks = BuildAffectedChunks(cellChanges, environmentChanges);
            foreach (var coordinate in affectedChunks)
            {
                boundWorld.MarkChunkChanged(coordinate, changeId);
            }

            var changeSet = BuildChangeSet(
                changeId,
                cellChanges,
                environmentChanges,
                changedColumns,
                affectedChunks);
            transaction.Complete();
            activeTransaction = null;

            if (recordUndo)
            {
                undoRecords.Push(new WorldEditRecord(
                    cellChanges,
                    environmentChanges));
                redoRecords.Clear();
            }

            ChangeCommitted?.Invoke(changeSet);
            return changeSet;
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

            for (var index = 0; index < record.EnvironmentChanges.Length; index++)
            {
                var change = record.EnvironmentChanges[index];
                WorldIndex.DecodeColumn(
                    boundWorld,
                    change.ColumnIndex,
                    out var x,
                    out var z);
                transaction.SetEnvironment(
                    x,
                    z,
                    usePreviousValues ? change.Previous : change.Current);
            }

            Commit(transaction, false);
        }

        private void RestorePreviousValues(
            CellEdit[] cellChanges,
            EnvironmentEdit[] environmentChanges)
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

            for (var index = 0; index < environmentChanges.Length; index++)
            {
                var change = environmentChanges[index];
                WorldIndex.DecodeColumn(
                    boundWorld,
                    change.ColumnIndex,
                    out var x,
                    out var z);
                boundWorld.SetColumnEnvironment(x, z, change.Previous);
            }
        }

        private void RebuildColumns(IEnumerable<int> columnIndices)
        {
            foreach (var columnIndex in columnIndices)
            {
                WorldIndex.DecodeColumn(
                    boundWorld,
                    columnIndex,
                    out var x,
                    out var z);
                boundWorld.RebuildSurfaceColumn(x, z);
            }
        }

        private ChunkCoordinate[] BuildAffectedChunks(
            CellEdit[] cellChanges,
            EnvironmentEdit[] environmentChanges)
        {
            var chunks = new HashSet<ChunkCoordinate>();
            for (var index = 0; index < cellChanges.Length; index++)
            {
                var cell = cellChanges[index].Coordinate;
                AddCellAndBoundaryChunks(chunks, cell.X, cell.Y, cell.Z);
            }

            for (var index = 0; index < environmentChanges.Length; index++)
            {
                WorldIndex.DecodeColumn(
                    boundWorld,
                    environmentChanges[index].ColumnIndex,
                    out var x,
                    out var z);
                var chunkX = x / boundWorld.ChunkSizeX;
                var chunkZ = z / boundWorld.ChunkSizeZ;
                for (var chunkY = 0;
                     chunkY < boundWorld.ChunkCountY;
                     chunkY++)
                {
                    chunks.Add(new ChunkCoordinate(chunkX, chunkY, chunkZ));
                }
            }

            var result = new ChunkCoordinate[chunks.Count];
            chunks.CopyTo(result);
            Array.Sort(result, CompareChunks);
            return result;
        }

        private void AddCellAndBoundaryChunks(
            HashSet<ChunkCoordinate> chunks,
            int x,
            int y,
            int z)
        {
            var chunkX = x / boundWorld.ChunkSizeX;
            var chunkY = y / boundWorld.ChunkSizeY;
            var chunkZ = z / boundWorld.ChunkSizeZ;
            var localX = x % boundWorld.ChunkSizeX;
            var localY = y % boundWorld.ChunkSizeY;
            var localZ = z % boundWorld.ChunkSizeZ;

            var minimumX = localX == 0 ? chunkX - 1 : chunkX;
            var maximumX = localX == boundWorld.ChunkSizeX - 1
                ? chunkX + 1
                : chunkX;
            var minimumY = localY == 0 ? chunkY - 1 : chunkY;
            var maximumY = localY == boundWorld.ChunkSizeY - 1
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
                    && (uint)affectedY < boundWorld.ChunkCountY
                    && (uint)affectedZ < boundWorld.ChunkCountZ)
                {
                    chunks.Add(new ChunkCoordinate(
                        affectedX,
                        affectedY,
                        affectedZ));
                }
            }
        }

        private WorldChangeSet BuildChangeSet(
            WorldChangeId changeId,
            CellEdit[] cellChanges,
            EnvironmentEdit[] environmentChanges,
            HashSet<int> changedColumns,
            ChunkCoordinate[] affectedChunks)
        {
            var cellIndices = new int[cellChanges.Length];
            var changeTypes = WorldChangeType.None;
            var hasBounds = false;
            var minimum = default(CellCoordinate);
            var maximum = default(CellCoordinate);

            for (var index = 0; index < cellChanges.Length; index++)
            {
                var change = cellChanges[index];
                cellIndices[index] = change.CellIndex;
                changeTypes |= ClassifyChange(change.Previous, change.Current);
                ExpandBounds(
                    change.Coordinate,
                    ref hasBounds,
                    ref minimum,
                    ref maximum);
            }

            for (var index = 0; index < environmentChanges.Length; index++)
            {
                var change = environmentChanges[index];
                changeTypes |= WorldChangeType.Environment
                    | WorldChangeType.Material
                    | WorldChangeType.Ecology;
                WorldIndex.DecodeColumn(
                    boundWorld,
                    change.ColumnIndex,
                    out var x,
                    out var z);
                ExpandBounds(
                    new CellCoordinate(x, 0, z),
                    ref hasBounds,
                    ref minimum,
                    ref maximum);
                ExpandBounds(
                    new CellCoordinate(x, boundWorld.Height - 1, z),
                    ref hasBounds,
                    ref minimum,
                    ref maximum);
            }

            Array.Sort(cellIndices);
            var columnIndices = new int[changedColumns.Count];
            changedColumns.CopyTo(columnIndices);
            Array.Sort(columnIndices);

            return new WorldChangeSet(
                boundWorld,
                changeId,
                changeTypes,
                cellIndices,
                columnIndices,
                affectedChunks,
                new CellBounds(minimum, maximum));
        }

        private static WorldChangeType ClassifyChange(
            CellData previous,
            CellData current)
        {
            var types = WorldChangeType.None;
            if (previous.SolidFill != current.SolidFill
                || previous.WaterFill != current.WaterFill)
            {
                types |= WorldChangeType.CellStructure
                    | WorldChangeType.Surface
                    | WorldChangeType.Navigation;
            }

            if (previous.Material != current.Material
                || previous.Surface != current.Surface)
            {
                types |= WorldChangeType.Material
                    | WorldChangeType.Surface
                    | WorldChangeType.Navigation;
            }

            const CellFlags waterFlags =
                CellFlags.River | CellFlags.Waterfall;
            if (previous.Water != current.Water
                || previous.WaterFill != current.WaterFill
                || (previous.Flags & waterFlags) != (current.Flags & waterFlags))
            {
                types |= WorldChangeType.WaterTopology
                    | WorldChangeType.Surface
                    | WorldChangeType.Navigation
                    | WorldChangeType.Ecology;
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

        private static int CompareChunks(
            ChunkCoordinate left,
            ChunkCoordinate right)
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
            public readonly EnvironmentEdit[] EnvironmentChanges;

            public WorldEditRecord(
                CellEdit[] cellChanges,
                EnvironmentEdit[] environmentChanges)
            {
                CellChanges = cellChanges;
                EnvironmentChanges = environmentChanges;
            }
        }
    }

    public sealed class WorldEditTransaction
    {
        private readonly WorldEditController owner;
        private readonly WorldData world;
        private readonly Dictionary<int, CellEdit> cellChanges = new();
        private readonly Dictionary<int, EnvironmentEdit> environmentChanges =
            new();

        private bool completed;

        public int ChangedCellCount => cellChanges.Count;
        public int ChangedColumnCount => environmentChanges.Count;
        public bool HasChanges =>
            cellChanges.Count > 0 || environmentChanges.Count > 0;
        public bool IsCompleted => completed;

        internal WorldEditTransaction(
            WorldEditController owner,
            WorldData world)
        {
            this.owner = owner;
            this.world = world;
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

            cell.Normalize();
            var cellIndex = WorldIndex.EncodeCell(world, x, y, z);
            if (cellChanges.TryGetValue(cellIndex, out var existing))
            {
                if (existing.Previous.Equals(cell))
                {
                    cellChanges.Remove(cellIndex);
                }
                else
                {
                    cellChanges[cellIndex] = new CellEdit(
                        cellIndex,
                        existing.Coordinate,
                        existing.Previous,
                        cell);
                }

                return;
            }

            var previous = world.GetCell(x, y, z);
            if (previous.Equals(cell))
            {
                return;
            }

            cellChanges.Add(
                cellIndex,
                new CellEdit(
                    cellIndex,
                    new CellCoordinate(x, y, z),
                    previous,
                    cell));
        }

        public void SetSolidHeight(
            int x,
            int z,
            int heightUnits,
            SurfaceType surface = SurfaceType.Ground)
        {
            EnsureColumn(x, z);
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
                cell.SolidFill = fill;
                cell.WaterFill = (byte)Math.Min(
                    cell.WaterFill,
                    WorldGrid.HeightStepsPerCell - fill);

                if (fill > 0)
                {
                    cell.Material =
                        y < Math.Max(
                            0,
                            heightUnits / WorldGrid.HeightStepsPerCell - 2)
                            ? CellMaterialType.Rock
                            : CellMaterialType.Soil;
                    cell.Geology = CellMaterialType.Rock;
                    cell.Surface =
                        fill < WorldGrid.HeightStepsPerCell
                        || baseUnits + fill == heightUnits
                            ? surface
                            : SurfaceType.None;
                    cell.Flags |= CellFlags.Generated;
                }
                else
                {
                    cell.Material = CellMaterialType.None;
                    cell.Surface = SurfaceType.None;
                    cell.Geology = CellMaterialType.None;
                }

                SetCell(x, y, z, cell);
            }
        }

        public void SetWaterLevel(
            int x,
            int z,
            int waterSurfaceUnits,
            WaterType water,
            CellFlags flags = CellFlags.None)
        {
            EnsureColumn(x, z);
            waterSurfaceUnits = Math.Clamp(
                waterSurfaceUnits,
                0,
                world.Height * WorldGrid.HeightStepsPerCell);

            for (var y = 0; y < world.Height; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var cell = GetPendingCell(x, y, z);
                var available =
                    WorldGrid.HeightStepsPerCell - cell.SolidFill;
                var desiredTop = Math.Clamp(
                    waterSurfaceUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                cell.WaterFill = (byte)Math.Clamp(
                    desiredTop - cell.SolidFill,
                    0,
                    available);
                cell.Water = cell.WaterFill > 0 ? water : WaterType.None;
                cell.Flags = cell.WaterFill > 0
                    ? cell.Flags | flags | CellFlags.Generated
                    : cell.Flags & ~(CellFlags.River | CellFlags.Waterfall);
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
                if (!cell.HasSolid)
                {
                    continue;
                }

                cell.Surface = surface;
                SetCell(x, y, z, cell);
                return;
            }
        }

        public void SetBiome(int x, int z, BiomeType biome)
        {
            EnsureColumn(x, z);
            var environment = GetPendingEnvironment(x, z);
            environment.Biome = biome;
            SetEnvironment(x, z, environment);
        }

        public void SetEnvironment(
            int x,
            int z,
            ColumnEnvironmentData environment)
        {
            EnsureOpen();
            EnsureColumn(x, z);
            var columnIndex = WorldIndex.EncodeColumn(world, x, z);
            if (environmentChanges.TryGetValue(
                    columnIndex,
                    out var existing))
            {
                if (EnvironmentEquals(existing.Previous, environment))
                {
                    environmentChanges.Remove(columnIndex);
                }
                else
                {
                    environmentChanges[columnIndex] = new EnvironmentEdit(
                        columnIndex,
                        existing.Previous,
                        environment);
                }

                return;
            }

            var previous = world.GetColumnEnvironment(x, z);
            if (EnvironmentEquals(previous, environment))
            {
                return;
            }

            environmentChanges.Add(
                columnIndex,
                new EnvironmentEdit(columnIndex, previous, environment));
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
                left.CellIndex.CompareTo(right.CellIndex));
            return result;
        }

        internal EnvironmentEdit[] CopyEnvironmentChanges()
        {
            var result = new EnvironmentEdit[environmentChanges.Count];
            environmentChanges.Values.CopyTo(result, 0);
            Array.Sort(result, (left, right) =>
                left.ColumnIndex.CompareTo(right.ColumnIndex));
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
            environmentChanges.Clear();
        }

        private CellData GetPendingCell(int x, int y, int z)
        {
            var cellIndex = WorldIndex.EncodeCell(world, x, y, z);
            return cellChanges.TryGetValue(cellIndex, out var change)
                ? change.Current
                : world.GetCell(x, y, z);
        }

        private ColumnEnvironmentData GetPendingEnvironment(int x, int z)
        {
            var columnIndex = WorldIndex.EncodeColumn(world, x, z);
            return environmentChanges.TryGetValue(columnIndex, out var change)
                ? change.Current
                : world.GetColumnEnvironment(x, z);
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

        private static bool EnvironmentEquals(
            ColumnEnvironmentData left,
            ColumnEnvironmentData right)
        {
            return left.Biome == right.Biome
                && left.Temperature == right.Temperature
                && left.Moisture == right.Moisture
                && left.Fertility == right.Fertility;
        }
    }

    internal readonly struct CellEdit
    {
        public readonly int CellIndex;
        public readonly CellCoordinate Coordinate;
        public readonly CellData Previous;
        public readonly CellData Current;

        public CellEdit(
            int cellIndex,
            CellCoordinate coordinate,
            CellData previous,
            CellData current)
        {
            CellIndex = cellIndex;
            Coordinate = coordinate;
            Previous = previous;
            Current = current;
        }
    }

    internal readonly struct EnvironmentEdit
    {
        public readonly int ColumnIndex;
        public readonly ColumnEnvironmentData Previous;
        public readonly ColumnEnvironmentData Current;

        public EnvironmentEdit(
            int columnIndex,
            ColumnEnvironmentData previous,
            ColumnEnvironmentData current)
        {
            ColumnIndex = columnIndex;
            Previous = previous;
            Current = current;
        }
    }
}
