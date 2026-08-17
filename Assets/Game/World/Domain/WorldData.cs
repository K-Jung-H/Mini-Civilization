using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    public sealed class ChunkSection
    {
        private readonly CellData[] cells;
        private int nonDefaultCellCount;

        public ChunkSectionCoordinate Coordinate { get; }
        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }
        public bool IsEmpty => nonDefaultCellCount == 0;

        public ChunkSection(
            ChunkSectionCoordinate coordinate,
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

    public sealed class Chunk
    {
        private readonly ChunkSection[] sectionsByY;
        private readonly List<EntityData> entities = new();

        public ChunkCoordinate Coordinate { get; }
        public IReadOnlyList<ChunkSection> SectionsByY => sectionsByY;
        public IReadOnlyList<EntityData> Entities => entities;

        internal Chunk(
            ChunkCoordinate coordinate,
            int sectionCountY)
        {
            Coordinate = coordinate;
            sectionsByY = new ChunkSection[sectionCountY];
        }

        public bool TryGetSection(int sectionY, out ChunkSection section)
        {
            if ((uint)sectionY >= sectionsByY.Length)
            {
                section = null;
                return false;
            }

            section = sectionsByY[sectionY];
            return section != null;
        }

        internal ChunkSection GetOrCreateSection(
            int sectionY,
            int chunkSizeX,
            int sectionSizeY,
            int chunkSizeZ)
        {
            if ((uint)sectionY >= sectionsByY.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(sectionY));
            }

            var section = sectionsByY[sectionY];
            if (section != null)
            {
                return section;
            }

            section = new ChunkSection(
                new ChunkSectionCoordinate(Coordinate.X, sectionY, Coordinate.Z),
                chunkSizeX,
                sectionSizeY,
                chunkSizeZ);
            sectionsByY[sectionY] = section;
            return section;
        }

        internal void ReleaseSectionIfEmpty(int sectionY)
        {
            if ((uint)sectionY >= sectionsByY.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(sectionY));
            }

            if (sectionsByY[sectionY]?.IsEmpty == true)
            {
                sectionsByY[sectionY] = null;
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
        private readonly Dictionary<ChunkCoordinate, Chunk>
            loadedChunks = new();
        private readonly Dictionary<EntityId, EntityData> entitiesById = new();

        public WorldSettingsData Settings { get; }
        public int Size => Settings.WorldSize;
        public WorldType WorldType => Settings.WorldType;
        public bool IsInfinite => WorldType == WorldType.Infinite;
        public int Height => Settings.WorldHeight;
        public float CellSize => Settings.CellSize;
        public float HeightStep => Settings.HeightStep;
        public int ChunkSizeX => Settings.ChunkCellCountXZ;
        public int ChunkSectionSizeY => Settings.ChunkSectionCellCountY;
        public int ChunkSizeZ => Settings.ChunkCellCountXZ;
        public int ChunkCountX => Settings.WorldChunkCountXZ;
        public int ChunkSectionCountY => Settings.ChunkSectionCountY;
        public int ChunkCountZ => Settings.WorldChunkCountXZ;
        public int MinimumChunkX => Settings.MinimumChunkCoordinate;
        public int MaximumChunkX => Settings.MaximumChunkCoordinate;
        public int MinimumChunkZ => Settings.MinimumChunkCoordinate;
        public int MaximumChunkZ => Settings.MaximumChunkCoordinate;
        public int MinimumCellX => Settings.MinimumCellCoordinate;
        public int MaximumCellXExclusive => Settings.MaximumCellCoordinateExclusive;
        public int MinimumCellZ => Settings.MinimumCellCoordinate;
        public int MaximumCellZExclusive => Settings.MaximumCellCoordinateExclusive;
        public int Seed => Settings.Seed;
        public WaterFlowRules WaterFlowRules => Settings.WaterFlowRules;
        public int PondMaximumArea => Settings.PondMaximumArea;
        public WaterFlowScheduleData WaterFlowSchedule { get; }
        public IReadOnlyDictionary<ChunkCoordinate, Chunk>
            LoadedChunks => loadedChunks;
        public int EntityCount => entitiesById.Count;

        public WorldData(WorldSettingsData settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            WaterFlowSchedule = new WaterFlowScheduleData();
        }

        public bool IsValidHeight(int y) => (uint)y < Height;

        public bool IsWithinHorizontalBounds(int x, int z) =>
            IsInfinite
            || x >= MinimumCellX && x < MaximumCellXExclusive
            && z >= MinimumCellZ && z < MaximumCellZExclusive;

        public bool IsChunkWithinBounds(ChunkCoordinate coordinate) =>
            IsInfinite
            || coordinate.X >= MinimumChunkX && coordinate.X <= MaximumChunkX
            && coordinate.Z >= MinimumChunkZ && coordinate.Z <= MaximumChunkZ;

        public bool Contains(int x, int y, int z) =>
            IsValidHeight(y) && IsWithinHorizontalBounds(x, z);

        public bool ContainsColumn(int x, int z) =>
            IsWithinHorizontalBounds(x, z);

        public bool IsColumnLoaded(int x, int z) =>
            ContainsColumn(x, z) && IsChunkLoaded(x, z);

        public bool IsChunkLoaded(ChunkCoordinate coordinate) =>
            loadedChunks.ContainsKey(coordinate);

        public bool IsChunkLoaded(int cellX, int cellZ) =>
            loadedChunks.ContainsKey(ToChunk(cellX, cellZ));

        public bool TryGetChunk(
            ChunkCoordinate coordinate,
            out Chunk chunk) =>
            loadedChunks.TryGetValue(coordinate, out chunk);

        public CellData GetCell(int x, int y, int z)
        {
            ValidateCellCoordinate(x, y, z);
            GetSectionAndLocal(
                x,
                y,
                z,
                createChunk: false,
                createSection: false,
                out var section,
                out var localIndex,
                out _);
            return section == null
                ? default
                : section.GetCell(localIndex);
        }

        public bool TryGetCell(int x, int y, int z, out CellData cell)
        {
            if (!Contains(x, y, z)
                || !IsChunkLoaded(x, z))
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
            GetSectionAndLocal(
                x,
                y,
                z,
                createChunk: true,
                createSection: !cell.Equals(default),
                out var section,
                out var localIndex,
                out var sectionY);
            if (section == null)
            {
                return false;
            }

            var changed = section.SetCell(localIndex, cell);
            if (changed && section.IsEmpty)
            {
                loadedChunks[ToChunk(x, z)].ReleaseSectionIfEmpty(sectionY);
            }

            return changed;
        }

        public bool TryGetSection(
            ChunkSectionCoordinate coordinate,
            out ChunkSection section)
        {
            if ((uint)coordinate.Y >= ChunkSectionCountY
                || !loadedChunks.TryGetValue(
                    new ChunkCoordinate(coordinate.X, coordinate.Z),
                    out var chunk))
            {
                section = null;
                return false;
            }

            return chunk.TryGetSection(coordinate.Y, out section);
        }

        public ChunkSection GetSection(int chunkX, int sectionY, int chunkZ)
        {
            var coordinate = new ChunkSectionCoordinate(chunkX, sectionY, chunkZ);
            if (!TryGetSection(coordinate, out var section))
            {
                throw new InvalidOperationException(
                    $"Chunk Section {coordinate} is not loaded or is known to be empty.");
            }

            return section;
        }

        internal ChunkSection GetOrCreateSection(
            int chunkX,
            int sectionY,
            int chunkZ)
        {
            if ((uint)sectionY >= ChunkSectionCountY
                || !IsChunkWithinBounds(new ChunkCoordinate(chunkX, chunkZ)))
            {
                throw new ArgumentOutOfRangeException(nameof(chunkX));
            }

            var chunk = GetOrCreateChunk(
                new ChunkCoordinate(chunkX, chunkZ));
            return chunk.GetOrCreateSection(
                sectionY,
                ChunkSizeX,
                ChunkSectionSizeY,
                ChunkSizeZ);
        }

        public IEnumerable<Chunk> EnumerateLoadedChunks()
        {
            foreach (var chunk in loadedChunks.Values)
            {
                yield return chunk;
            }
        }

        public IEnumerable<ChunkSection> EnumerateSections()
        {
            foreach (var chunk in loadedChunks.Values)
            {
                for (var sectionY = 0; sectionY < ChunkSectionCountY; sectionY++)
                {
                    if (chunk.TryGetSection(sectionY, out var section))
                    {
                        yield return section;
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
            if (!ContainsColumn(x, z) || !IsChunkLoaded(x, z))
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

            GetOrCreateChunk(ToChunk(
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

            var coordinate = ToChunk(
                entity.AnchorCell.X,
                entity.AnchorCell.Z);
            if (!loadedChunks.TryGetValue(coordinate, out var chunk)
                || !chunk.RemoveEntity(entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {id} is not owned by Chunk {coordinate}.");
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

            var previousChunkCoordinate = ToChunk(
                entity.AnchorCell.X,
                entity.AnchorCell.Z);
            var nextChunkCoordinate = ToChunk(destination.X, destination.Z);
            if (!previousChunkCoordinate.Equals(nextChunkCoordinate))
            {
                var previousChunk = loadedChunks[previousChunkCoordinate];
                if (!previousChunk.RemoveEntity(entity))
                {
                    throw new InvalidOperationException(
                        $"Entity ID {entity.Id} is not owned by Chunk {previousChunkCoordinate}.");
                }

                GetOrCreateChunk(nextChunkCoordinate).AddEntity(entity);
            }

            entity.MoveTo(destination);
        }

        internal Chunk EnsureChunkLoaded(
            ChunkCoordinate coordinate)
        {
            if (!IsChunkWithinBounds(coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }

            return GetOrCreateChunk(coordinate);
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
            GetSectionAndLocal(
                x,
                y,
                z,
                createChunk: true,
                createSection: !cell.Equals(default),
                out var section,
                out var localIndex,
                out var sectionY);
            if (section == null)
            {
                return;
            }

            section.SetCellRaw(localIndex, cell);
            if (section.IsEmpty)
            {
                loadedChunks[ToChunk(x, z)].ReleaseSectionIfEmpty(sectionY);
            }
        }

        private Chunk GetOrCreateChunk(
            ChunkCoordinate coordinate)
        {
            if (loadedChunks.TryGetValue(coordinate, out var chunk))
            {
                return chunk;
            }

            chunk = new Chunk(coordinate, ChunkSectionCountY);
            loadedChunks.Add(coordinate, chunk);
            return chunk;
        }

        private ChunkCoordinate ToChunk(int x, int z) =>
            WorldCoordinateUtility.ToChunk(x, z, ChunkSizeX);

        private void GetSectionAndLocal(
            int x,
            int y,
            int z,
            bool createChunk,
            bool createSection,
            out ChunkSection section,
            out LocalCellIndex localIndex,
            out int sectionY)
        {
            var chunkCoordinate = ToChunk(x, z);
            sectionY = WorldCoordinateUtility.FloorDivide(y, ChunkSectionSizeY);
            localIndex = WorldCoordinateUtility.ToLocalCellIndex(
                new CellCoordinate(x, y, z),
                ChunkSizeX,
                ChunkSectionSizeY);

            if (!loadedChunks.TryGetValue(chunkCoordinate, out var chunk))
            {
                if (!createChunk)
                {
                    throw new InvalidOperationException(
                        $"Chunk {chunkCoordinate} is not loaded.");
                }

                chunk = GetOrCreateChunk(chunkCoordinate);
            }

            if (chunk.TryGetSection(sectionY, out section) || !createSection)
            {
                return;
            }

            section = chunk.GetOrCreateSection(
                sectionY,
                ChunkSizeX,
                ChunkSectionSizeY,
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
