using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    public sealed class ChunkData
    {
        private readonly CellData[] cells;
        private int nonDefaultCellCount;

        public ChunkCoordinate Coordinate { get; }
        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }
        public bool IsEmpty => nonDefaultCellCount == 0;

        public ChunkData(
            ChunkCoordinate coordinate,
            int sizeX,
            int sizeY,
            int sizeZ)
        {
            Coordinate = coordinate;
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            cells = new CellData[sizeX * sizeY * sizeZ];
        }

        public CellData GetCell(LocalCellIndex index) =>
            cells[ValidateIndex(index)];

        internal bool SetCell(LocalCellIndex localIndex, CellData cell)
        {
            cell.Normalize();
            var index = ValidateIndex(localIndex);
            var previous = cells[index];
            if (previous.Equals(cell))
            {
                return false;
            }

            TrackDefaultTransition(previous, cell);
            cells[index] = cell;
            return true;
        }

        public ReadOnlySpan<CellData> AsSpan() => cells;

        internal void SetCellRaw(LocalCellIndex localIndex, CellData cell)
        {
            var index = ValidateIndex(localIndex);
            TrackDefaultTransition(cells[index], cell);
            cells[index] = cell;
        }

        private void TrackDefaultTransition(CellData previous, CellData next)
        {
            var previousIsDefault = previous.Equals(default);
            var nextIsDefault = next.Equals(default);
            if (previousIsDefault == nextIsDefault)
            {
                return;
            }

            nonDefaultCellCount += nextIsDefault ? -1 : 1;
        }

        private int ValidateIndex(LocalCellIndex index)
        {
            if ((uint)index.Value >= cells.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    $"Local Cell index {index} is outside chunk {Coordinate}.");
            }

            return index.Value;
        }
    }

    public sealed class WorldChunkColumn
    {
        private readonly ChunkData[] chunksByY;
        private readonly List<EntityData> entities = new();

        public ChunkColumnCoordinate Coordinate { get; }
        public IReadOnlyList<ChunkData> ChunksByY => chunksByY;
        public IReadOnlyList<EntityData> Entities => entities;

        internal WorldChunkColumn(
            ChunkColumnCoordinate coordinate,
            int chunkCountY)
        {
            Coordinate = coordinate;
            chunksByY = new ChunkData[chunkCountY];
        }

        public bool TryGetChunk(int chunkY, out ChunkData chunk)
        {
            if ((uint)chunkY >= chunksByY.Length)
            {
                chunk = null;
                return false;
            }

            chunk = chunksByY[chunkY];
            return chunk != null;
        }

        internal ChunkData GetOrCreateChunk(
            int chunkY,
            int chunkSizeX,
            int chunkSizeY,
            int chunkSizeZ)
        {
            if ((uint)chunkY >= chunksByY.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkY));
            }

            var chunk = chunksByY[chunkY];
            if (chunk != null)
            {
                return chunk;
            }

            chunk = new ChunkData(
                new ChunkCoordinate(Coordinate.X, chunkY, Coordinate.Z),
                chunkSizeX,
                chunkSizeY,
                chunkSizeZ);
            chunksByY[chunkY] = chunk;
            return chunk;
        }

        internal void ReleaseChunkIfEmpty(int chunkY)
        {
            if ((uint)chunkY >= chunksByY.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkY));
            }

            if (chunksByY[chunkY]?.IsEmpty == true)
            {
                chunksByY[chunkY] = null;
            }
        }

        internal void AddEntity(EntityData entity)
        {
            entities.Add(entity);
        }

        internal bool RemoveEntity(EntityData entity) =>
            entities.Remove(entity);
    }

    public sealed class WorldData
    {
        private readonly Dictionary<ChunkColumnCoordinate, WorldChunkColumn>
            loadedColumns = new();
        private readonly Dictionary<EntityId, EntityData> entitiesById = new();

        public WorldSettingsData Settings { get; }
        public int Size => Settings.WorldSize;
        public int Height => Settings.WorldHeight;
        public float CellSize => Settings.CellSize;
        public float HeightStep => Settings.HeightStep;
        public int ChunkSizeX => Settings.ChunkCellCountXZ;
        public int ChunkSizeY => Settings.ChunkCellCountY;
        public int ChunkSizeZ => Settings.ChunkCellCountXZ;
        public int ChunkCountX => Settings.WorldChunkCountXZ;
        public int ChunkCountY => Settings.WorldChunkCountY;
        public int ChunkCountZ => Settings.WorldChunkCountXZ;
        public int Seed => Settings.Seed;
        public WaterFlowRules WaterFlowRules => Settings.WaterFlowRules;
        public int PondMaximumArea => Settings.PondMaximumArea;
        public WaterFlowScheduleData WaterFlowSchedule { get; }
        public IReadOnlyDictionary<ChunkColumnCoordinate, WorldChunkColumn>
            LoadedColumns => loadedColumns;
        public int EntityCount => entitiesById.Count;

        public WorldData(WorldSettingsData settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            WaterFlowSchedule = new WaterFlowScheduleData();
        }

        public bool IsValidHeight(int y) => (uint)y < Height;

        public bool IsWithinHorizontalBounds(int x, int z) =>
            (uint)x < Size && (uint)z < Size;

        public bool Contains(int x, int y, int z) =>
            IsValidHeight(y) && IsWithinHorizontalBounds(x, z);

        public bool ContainsColumn(int x, int z) =>
            IsWithinHorizontalBounds(x, z);

        public bool IsColumnLoaded(ChunkColumnCoordinate coordinate) =>
            loadedColumns.ContainsKey(coordinate);

        public bool IsColumnLoaded(int cellX, int cellZ) =>
            loadedColumns.ContainsKey(ToChunkColumn(cellX, cellZ));

        public bool TryGetColumn(
            ChunkColumnCoordinate coordinate,
            out WorldChunkColumn column) =>
            loadedColumns.TryGetValue(coordinate, out column);

        public CellData GetCell(int x, int y, int z)
        {
            ValidateCellCoordinate(x, y, z);
            GetChunkAndLocal(
                x,
                y,
                z,
                createColumn: false,
                createChunk: false,
                out var chunk,
                out var localIndex,
                out _);
            return chunk == null
                ? default
                : chunk.GetCell(localIndex);
        }

        public bool TryGetCell(int x, int y, int z, out CellData cell)
        {
            if (!Contains(x, y, z)
                || !IsColumnLoaded(x, z))
            {
                cell = default;
                return false;
            }

            cell = GetCell(x, y, z);
            return true;
        }

        internal bool SetCellForEdit(int x, int y, int z, CellData cell)
        {
            ValidateCellCoordinate(x, y, z);
            cell.Normalize();
            GetChunkAndLocal(
                x,
                y,
                z,
                createColumn: true,
                createChunk: !cell.Equals(default),
                out var chunk,
                out var localIndex,
                out var chunkY);
            if (chunk == null)
            {
                return false;
            }

            var changed = chunk.SetCell(localIndex, cell);
            if (changed && chunk.IsEmpty)
            {
                loadedColumns[ToChunkColumn(x, z)].ReleaseChunkIfEmpty(chunkY);
            }

            return changed;
        }

        public bool TryGetChunk(
            ChunkCoordinate coordinate,
            out ChunkData chunk)
        {
            if ((uint)coordinate.Y >= ChunkCountY
                || !loadedColumns.TryGetValue(
                    new ChunkColumnCoordinate(coordinate.X, coordinate.Z),
                    out var column))
            {
                chunk = null;
                return false;
            }

            return column.TryGetChunk(coordinate.Y, out chunk);
        }

        public ChunkData GetChunk(int chunkX, int chunkY, int chunkZ)
        {
            var coordinate = new ChunkCoordinate(chunkX, chunkY, chunkZ);
            if (!TryGetChunk(coordinate, out var chunk))
            {
                throw new InvalidOperationException(
                    $"Chunk {coordinate} is not loaded or is known to be empty.");
            }

            return chunk;
        }

        internal ChunkData GetOrCreateChunk(int chunkX, int chunkY, int chunkZ)
        {
            if ((uint)chunkX >= ChunkCountX
                || (uint)chunkY >= ChunkCountY
                || (uint)chunkZ >= ChunkCountZ)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkX));
            }

            var column = GetOrCreateColumn(
                new ChunkColumnCoordinate(chunkX, chunkZ));
            return column.GetOrCreateChunk(
                chunkY,
                ChunkSizeX,
                ChunkSizeY,
                ChunkSizeZ);
        }

        public IEnumerable<WorldChunkColumn> EnumerateLoadedColumns()
        {
            foreach (var column in loadedColumns.Values)
            {
                yield return column;
            }
        }

        public IEnumerable<ChunkData> EnumerateChunks()
        {
            foreach (var column in loadedColumns.Values)
            {
                for (var chunkY = 0; chunkY < ChunkCountY; chunkY++)
                {
                    if (column.TryGetChunk(chunkY, out var chunk))
                    {
                        yield return chunk;
                    }
                }
            }
        }

        public IEnumerable<EntityData> EnumerateEntities()
        {
            foreach (var entity in entitiesById.Values)
            {
                yield return entity;
            }
        }

        public bool HasTerrainCell(int x, int z)
        {
            if (!ContainsColumn(x, z) || !IsColumnLoaded(x, z))
            {
                return false;
            }

            for (var y = 0; y < Height; y++)
            {
                if (GetCell(x, y, z).HasTerrain)
                {
                    return true;
                }
            }

            return false;
        }

        internal void AddEntity(EntityData entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            ValidateCellCoordinate(
                entity.AnchorCell.X,
                entity.AnchorCell.Y,
                entity.AnchorCell.Z);
            if (!entitiesById.TryAdd(entity.Id, entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {entity.Id} already exists in the world.");
            }

            GetOrCreateColumn(ToChunkColumn(
                entity.AnchorCell.X,
                entity.AnchorCell.Z)).AddEntity(entity);
        }

        internal EntityData RemoveEntity(EntityId id)
        {
            if (!entitiesById.TryGetValue(id, out var entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {id} does not exist in the world.");
            }

            var coordinate = ToChunkColumn(
                entity.AnchorCell.X,
                entity.AnchorCell.Z);
            if (!loadedColumns.TryGetValue(coordinate, out var column)
                || !column.RemoveEntity(entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {id} is not owned by column {coordinate}.");
            }

            entitiesById.Remove(id);
            return entity;
        }

        internal void MoveEntity(EntityData entity, CellCoordinate destination)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            ValidateCellCoordinate(destination.X, destination.Y, destination.Z);
            if (!entitiesById.TryGetValue(entity.Id, out var registered)
                || !ReferenceEquals(registered, entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {entity.Id} does not exist in the world.");
            }

            var previousColumnCoordinate = ToChunkColumn(
                entity.AnchorCell.X,
                entity.AnchorCell.Z);
            var nextColumnCoordinate = ToChunkColumn(destination.X, destination.Z);
            if (!previousColumnCoordinate.Equals(nextColumnCoordinate))
            {
                var previousColumn = loadedColumns[previousColumnCoordinate];
                if (!previousColumn.RemoveEntity(entity))
                {
                    throw new InvalidOperationException(
                        $"Entity ID {entity.Id} is not owned by column {previousColumnCoordinate}.");
                }

                GetOrCreateColumn(nextColumnCoordinate).AddEntity(entity);
            }

            entity.MoveTo(destination);
        }

        internal WorldChunkColumn EnsureColumnLoaded(
            ChunkColumnCoordinate coordinate)
        {
            if ((uint)coordinate.X >= ChunkCountX
                || (uint)coordinate.Z >= ChunkCountZ)
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }

            return GetOrCreateColumn(coordinate);
        }

        internal void SetCellBulk(int x, int y, int z, CellData cell)
        {
            ValidateCellCoordinate(x, y, z);
            cell.Normalize();
            SetCellWithoutChangeTracking(x, y, z, cell);
        }

        internal void SetCellRaw(int x, int y, int z, CellData cell)
        {
            ValidateCellCoordinate(x, y, z);
            SetCellWithoutChangeTracking(x, y, z, cell);
        }

        private void SetCellWithoutChangeTracking(
            int x,
            int y,
            int z,
            CellData cell)
        {
            GetChunkAndLocal(
                x,
                y,
                z,
                createColumn: true,
                createChunk: !cell.Equals(default),
                out var chunk,
                out var localIndex,
                out var chunkY);
            if (chunk == null)
            {
                return;
            }

            chunk.SetCellRaw(localIndex, cell);
            if (chunk.IsEmpty)
            {
                loadedColumns[ToChunkColumn(x, z)].ReleaseChunkIfEmpty(chunkY);
            }
        }

        private WorldChunkColumn GetOrCreateColumn(
            ChunkColumnCoordinate coordinate)
        {
            if (loadedColumns.TryGetValue(coordinate, out var column))
            {
                return column;
            }

            column = new WorldChunkColumn(coordinate, ChunkCountY);
            loadedColumns.Add(coordinate, column);
            return column;
        }

        private ChunkColumnCoordinate ToChunkColumn(int x, int z) =>
            WorldCoordinateUtility.ToChunkColumn(x, z, ChunkSizeX);

        private void GetChunkAndLocal(
            int x,
            int y,
            int z,
            bool createColumn,
            bool createChunk,
            out ChunkData chunk,
            out LocalCellIndex localIndex,
            out int chunkY)
        {
            var columnCoordinate = ToChunkColumn(x, z);
            chunkY = WorldCoordinateUtility.FloorDivide(y, ChunkSizeY);
            localIndex = WorldCoordinateUtility.ToLocalCellIndex(
                new CellCoordinate(x, y, z),
                ChunkSizeX,
                ChunkSizeY);

            if (!loadedColumns.TryGetValue(columnCoordinate, out var column))
            {
                if (!createColumn)
                {
                    throw new InvalidOperationException(
                        $"World column {columnCoordinate} is not loaded.");
                }

                column = GetOrCreateColumn(columnCoordinate);
            }

            if (column.TryGetChunk(chunkY, out chunk) || !createChunk)
            {
                return;
            }

            chunk = column.GetOrCreateChunk(
                chunkY,
                ChunkSizeX,
                ChunkSizeY,
                ChunkSizeZ);
        }

        private void ValidateCellCoordinate(int x, int y, int z)
        {
            if (!IsValidHeight(y))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(y),
                    $"Cell Y {y} is outside the fixed height range.");
            }

            if (!IsWithinHorizontalBounds(x, z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"World column ({x}, {z}) is outside the current finite bounds.");
            }
        }
    }
}
