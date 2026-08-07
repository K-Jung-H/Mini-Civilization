using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    public sealed class ChunkData
    {
        private readonly CellData[] cells;

        public ChunkCoordinate Coordinate { get; }
        public int SizeX { get; }
        public int SizeY { get; }
        public int SizeZ { get; }

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

        public CellData GetCell(int x, int y, int z) => cells[ToIndex(x, y, z)];

        internal bool SetCell(int x, int y, int z, CellData cell)
        {
            cell.Normalize();
            var index = ToIndex(x, y, z);
            if (cells[index].Equals(cell))
            {
                return false;
            }

            cells[index] = cell;
            return true;
        }

        public ReadOnlySpan<CellData> AsSpan() => cells;
        internal void SetCellBulk(int x, int y, int z, CellData cell)
        {
            cell.Normalize();
            cells[ToIndex(x, y, z)] = cell;
        }

        internal void SetCellRaw(int x, int y, int z, CellData cell) =>
            cells[ToIndex(x, y, z)] = cell;

        private int ToIndex(int x, int y, int z)
        {
            if ((uint)x >= SizeX || (uint)y >= SizeY || (uint)z >= SizeZ)
            {
                throw new ArgumentOutOfRangeException($"Local cell ({x}, {y}, {z}) is outside chunk {Coordinate}.");
            }

            return x + SizeX * (z + SizeZ * y);
        }
    }

    public sealed class WorldData
    {
        private readonly ChunkData[,,] chunks;
        private readonly EnvironmentData[] environmentMap;
        private readonly List<EntityData> entities = new();

        public int Size { get; }
        public int Height { get; }
        public int ChunkSizeX { get; }
        public int ChunkSizeY { get; }
        public int ChunkSizeZ { get; }
        public int ChunkCountX { get; }
        public int ChunkCountY { get; }
        public int ChunkCountZ { get; }
        public int Seed { get; }
        public WaterFlowRules WaterFlowRules { get; private set; }
        public int PondMaximumArea { get; private set; }
        public WaterFlowScheduleData WaterFlowSchedule { get; }
        public IReadOnlyList<EntityData> Entities => entities;

        public WorldData(int size, int height, int chunkSizeX, int chunkSizeY, int chunkSizeZ, int seed)
        {
            if (size <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "World dimensions must be positive.");
            }

            if (chunkSizeX <= 0 || chunkSizeY <= 0 || chunkSizeZ <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeX), "Chunk dimensions must be positive.");
            }

            if (size % chunkSizeX != 0 || size % chunkSizeZ != 0 || height % chunkSizeY != 0)
            {
                throw new ArgumentException("World dimensions must be divisible by chunk dimensions.");
            }

            Size = size;
            Height = height;
            ChunkSizeX = chunkSizeX;
            ChunkSizeY = chunkSizeY;
            ChunkSizeZ = chunkSizeZ;
            Seed = seed;
            WaterFlowRules =
                global::MiniCivilization.World.Domain.WaterFlowRules.Default;
            PondMaximumArea = 8;
            ChunkCountX = size / chunkSizeX;
            ChunkCountY = height / chunkSizeY;
            ChunkCountZ = size / chunkSizeZ;
            chunks = new ChunkData[ChunkCountX, ChunkCountY, ChunkCountZ];
            environmentMap = new EnvironmentData[size * size];
            WaterFlowSchedule = new WaterFlowScheduleData();

            for (var chunkY = 0; chunkY < ChunkCountY; chunkY++)
            for (var chunkZ = 0; chunkZ < ChunkCountZ; chunkZ++)
            for (var chunkX = 0; chunkX < ChunkCountX; chunkX++)
            {
                chunks[chunkX, chunkY, chunkZ] = new ChunkData(
                    new ChunkCoordinate(chunkX, chunkY, chunkZ),
                    chunkSizeX,
                    chunkSizeY,
                    chunkSizeZ);
            }

        }

        public void ConfigureWaterFlow(WaterFlowRules rules)
        {
            WaterFlowRules = new WaterFlowRules(
                rules.SpreadAmountLoss,
                rules.MinimumSpreadAmount,
                rules.DissipationAmountLoss);
        }

        public void ConfigureWaterTypes(int pondMaximumArea) =>
            PondMaximumArea = Math.Max(1, pondMaximumArea);

        public bool Contains(int x, int y, int z) => (uint)x < Size && (uint)y < Height && (uint)z < Size;
        public bool ContainsColumn(int x, int z) => (uint)x < Size && (uint)z < Size;

        public CellData GetCell(int x, int y, int z)
        {
            if (!Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException($"World cell ({x}, {y}, {z}) is outside the world.");
            }

            GetChunkAndLocal(x, y, z, out var chunk, out var localX, out var localY, out var localZ);
            return chunk.GetCell(localX, localY, localZ);
        }

        public bool TryGetCell(int x, int y, int z, out CellData cell)
        {
            if (!Contains(x, y, z))
            {
                cell = default;
                return false;
            }

            cell = GetCell(x, y, z);
            return true;
        }

        internal bool SetCellForEdit(int x, int y, int z, CellData cell)
        {
            if (!Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException($"World cell ({x}, {y}, {z}) is outside the world.");
            }

            GetChunkAndLocal(x, y, z, out var chunk, out var localX, out var localY, out var localZ);
            return chunk.SetCell(localX, localY, localZ, cell);
        }

        public ChunkData GetChunk(int chunkX, int chunkY, int chunkZ) => chunks[chunkX, chunkY, chunkZ];

        public IEnumerable<ChunkData> EnumerateChunks()
        {
            for (var y = 0; y < ChunkCountY; y++)
            for (var z = 0; z < ChunkCountZ; z++)
            for (var x = 0; x < ChunkCountX; x++)
            {
                yield return chunks[x, y, z];
            }
        }

        public bool HasTerrainCell(int x, int z)
        {
            if (!ContainsColumn(x, z))
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

        public EnvironmentData GetEnvironment(int x, int z)
        {
            if (!ContainsColumn(x, z))
            {
                return default;
            }

            return environmentMap[x + Size * z];
        }

        public void SetEnvironment(int x, int z, EnvironmentData environment)
        {
            if (!ContainsColumn(x, z))
            {
                throw new ArgumentOutOfRangeException($"World column ({x}, {z}) is outside the world.");
            }

            environmentMap[x + Size * z] = environment;
        }

        internal void AddEntity(EntityData entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!Contains(
                    entity.AnchorCell.X,
                    entity.AnchorCell.Y,
                    entity.AnchorCell.Z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(entity),
                    $"Entity {entity.Id} anchor is outside the world.");
            }

            for (var index = 0; index < entities.Count; index++)
            {
                if (entities[index].Id == entity.Id)
                {
                    throw new InvalidOperationException(
                        $"Entity ID {entity.Id} already exists in the world.");
                }
            }

            entities.Add(entity);
        }

        internal EntityData RemoveEntity(EntityId id)
        {
            for (var index = 0; index < entities.Count; index++)
            {
                if (entities[index].Id != id)
                {
                    continue;
                }

                var entity = entities[index];
                entities.RemoveAt(index);
                return entity;
            }

            throw new InvalidOperationException(
                $"Entity ID {id} does not exist in the world.");
        }

        private void GetChunkAndLocal(int x, int y, int z, out ChunkData chunk, out int localX, out int localY, out int localZ)
        {
            var chunkX = x / ChunkSizeX;
            var chunkY = y / ChunkSizeY;
            var chunkZ = z / ChunkSizeZ;
            localX = x - chunkX * ChunkSizeX;
            localY = y - chunkY * ChunkSizeY;
            localZ = z - chunkZ * ChunkSizeZ;
            chunk = chunks[chunkX, chunkY, chunkZ];
        }

        internal void SetCellBulk(int x, int y, int z, CellData cell)
        {
            if (!Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException($"World cell ({x}, {y}, {z}) is outside the world.");
            }

            GetChunkAndLocal(x, y, z, out var chunk, out var localX, out var localY, out var localZ);
            chunk.SetCellBulk(localX, localY, localZ, cell);
        }

        internal void SetCellRaw(int x, int y, int z, CellData cell)
        {
            if (!Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException($"World cell ({x}, {y}, {z}) is outside the world.");
            }

            GetChunkAndLocal(x, y, z, out var chunk, out var localX, out var localY, out var localZ);
            chunk.SetCellRaw(localX, localY, localZ, cell);
        }

    }
}
