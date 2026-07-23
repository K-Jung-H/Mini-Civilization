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
        public ChunkDirtyFlags DirtyFlags { get; private set; } = ChunkDirtyFlags.All;
        public uint Revision { get; private set; }

        public ChunkData(ChunkCoordinate coordinate, int sizeX, int sizeY, int sizeZ)
        {
            Coordinate = coordinate;
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            cells = new CellData[sizeX * sizeY * sizeZ];
        }

        public CellData GetCell(int x, int y, int z) => cells[ToIndex(x, y, z)];

        public void SetCell(int x, int y, int z, CellData cell)
        {
            cell.Normalize();
            cells[ToIndex(x, y, z)] = cell;
            DirtyFlags |= ChunkDirtyFlags.Cells | ChunkDirtyFlags.Surface | ChunkDirtyFlags.TerrainMesh | ChunkDirtyFlags.Hydrology | ChunkDirtyFlags.WaterMesh | ChunkDirtyFlags.Materials;
            Revision++;
        }

        public ReadOnlySpan<CellData> AsSpan() => cells;
        public void MarkDirty(ChunkDirtyFlags flags) => DirtyFlags |= flags;
        public void ClearDirty(ChunkDirtyFlags flags) => DirtyFlags &= ~flags;

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
        private readonly SurfaceColumnData[] surfaceColumns;

        public int Size { get; }
        public int Height { get; }
        public int ChunkSizeX { get; }
        public int ChunkSizeY { get; }
        public int ChunkSizeZ { get; }
        public int ChunkCountX { get; }
        public int ChunkCountY { get; }
        public int ChunkCountZ { get; }
        public int Seed { get; }

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
            ChunkCountX = size / chunkSizeX;
            ChunkCountY = height / chunkSizeY;
            ChunkCountZ = size / chunkSizeZ;
            chunks = new ChunkData[ChunkCountX, ChunkCountY, ChunkCountZ];
            surfaceColumns = new SurfaceColumnData[size * size];

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

            for (var i = 0; i < surfaceColumns.Length; i++)
            {
                surfaceColumns[i].SurfaceCellY = -1;
                surfaceColumns[i].WaterCellY = -1;
            }
        }

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

        public void SetCell(int x, int y, int z, CellData cell)
        {
            var previous = GetCell(x, y, z);
            SetCellWithoutSurfaceRebuild(x, y, z, cell);

            if (!IsWaterSupported(x, z))
            {
                SetCellWithoutSurfaceRebuild(x, y, z, previous);
                RebuildSurfaceColumn(x, z);
                throw new InvalidOperationException(
                    $"Cell edit at ({x}, {y}, {z}) would leave unsupported water. " +
                    "Water edits must preserve vertical support or be handled by a redistribution command.");
            }

            RebuildSurfaceColumn(x, z);
        }

        private void SetCellWithoutSurfaceRebuild(int x, int y, int z, CellData cell)
        {
            if (!Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException($"World cell ({x}, {y}, {z}) is outside the world.");
            }

            GetChunkAndLocal(x, y, z, out var chunk, out var localX, out var localY, out var localZ);
            chunk.SetCell(localX, localY, localZ, cell);
            MarkBoundaryNeighborsDirty(x, y, z);
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

        public SurfaceColumnData GetSurfaceColumn(int x, int z)
        {
            if (!ContainsColumn(x, z))
            {
                return new SurfaceColumnData { SurfaceCellY = -1, WaterCellY = -1 };
            }

            return surfaceColumns[x + Size * z];
        }

        public void SetSurfaceColumn(int x, int z, SurfaceColumnData column)
        {
            surfaceColumns[x + Size * z] = column;
        }

        public void RebuildAllSurfaceColumns()
        {
            for (var z = 0; z < Size; z++)
            for (var x = 0; x < Size; x++)
            {
                RebuildSurfaceColumn(x, z);
            }
        }

        public void RebuildSurfaceColumn(int x, int z)
        {
            var previous = GetSurfaceColumn(x, z);
            var column = new SurfaceColumnData
            {
                SurfaceCellY = -1,
                WaterCellY = -1,
                Biome = previous.Biome,
                Temperature = previous.Temperature,
                Moisture = previous.Moisture,
                Fertility = previous.Fertility
            };

            var solidTopUnits = 0;
            var waterTopUnits = 0;

            for (var y = Height - 1; y >= 0; y--)
            {
                var cell = GetCell(x, y, z);
                if (column.WaterCellY < 0 && cell.WaterFill > 0)
                {
                    column.WaterCellY = (short)y;
                    column.WaterLevel = (byte)(cell.SolidFill + cell.WaterFill);
                    column.Water = cell.Water;
                    waterTopUnits = y * WorldGrid.HeightStepsPerCell + column.WaterLevel;
                }

                if (column.SurfaceCellY < 0 && cell.SolidFill > 0)
                {
                    column.SurfaceCellY = (short)y;
                    column.SurfaceLevel = cell.SolidFill;
                    column.Surface = cell.Surface != SurfaceType.None
                        ? cell.Surface
                        : SurfaceType.Ground;
                    solidTopUnits = y * WorldGrid.HeightStepsPerCell + cell.SolidFill;
                }

                if (column.SurfaceCellY >= 0 && column.WaterCellY >= 0)
                {
                    break;
                }
            }

            // Water at or below the highest solid surface is groundwater/buried data,
            // not a renderable surface water layer.
            if (column.WaterCellY >= 0 && waterTopUnits <= solidTopUnits)
            {
                column.WaterCellY = -1;
                column.WaterLevel = 0;
                column.Water = WaterType.None;
            }

            SetSurfaceColumn(x, z, column);
        }

        internal void SetColumnSolidHeightUnits(
            int x,
            int z,
            int heightUnits,
            SurfaceType surface = SurfaceType.Ground)
        {
            heightUnits = Math.Clamp(heightUnits, 0, Height * WorldGrid.HeightStepsPerCell);
            for (var y = 0; y < Height; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var fill = (byte)Math.Clamp(heightUnits - baseUnits, 0, WorldGrid.HeightStepsPerCell);
                var cell = GetCell(x, y, z);
                cell.SolidFill = fill;
                cell.WaterFill = (byte)Math.Min(cell.WaterFill, WorldGrid.HeightStepsPerCell - fill);

                if (fill > 0)
                {
                    cell.Material = y < Math.Max(0, heightUnits / WorldGrid.HeightStepsPerCell - 2)
                        ? CellMaterialType.Rock
                        : CellMaterialType.Soil;
                    cell.Geology = CellMaterialType.Rock;
                    cell.Surface = fill < WorldGrid.HeightStepsPerCell || baseUnits + fill == heightUnits
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

                SetCellWithoutSurfaceRebuild(x, y, z, cell);
            }

            RebuildSurfaceColumn(x, z);
        }

        internal void SetColumnWaterSurfaceUnits(
            int x,
            int z,
            int waterSurfaceUnits,
            WaterType water,
            CellFlags flags = CellFlags.None)
        {
            waterSurfaceUnits = Math.Clamp(waterSurfaceUnits, 0, Height * WorldGrid.HeightStepsPerCell);
            for (var y = 0; y < Height; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var cell = GetCell(x, y, z);
                var available = WorldGrid.HeightStepsPerCell - cell.SolidFill;
                var desiredTop = Math.Clamp(waterSurfaceUnits - baseUnits, 0, WorldGrid.HeightStepsPerCell);
                cell.WaterFill = (byte)Math.Clamp(desiredTop - cell.SolidFill, 0, available);
                cell.Water = cell.WaterFill > 0 ? water : WaterType.None;
                cell.Flags = cell.WaterFill > 0
                    ? cell.Flags | flags | CellFlags.Generated
                    : cell.Flags & ~(CellFlags.River | CellFlags.Waterfall);
                SetCellWithoutSurfaceRebuild(x, y, z, cell);
            }

            RebuildSurfaceColumn(x, z);
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

        private void MarkBoundaryNeighborsDirty(int x, int y, int z)
        {
            var chunkX = x / ChunkSizeX;
            var chunkY = y / ChunkSizeY;
            var chunkZ = z / ChunkSizeZ;
            var localX = x % ChunkSizeX;
            var localY = y % ChunkSizeY;
            var localZ = z % ChunkSizeZ;
            const ChunkDirtyFlags flags = ChunkDirtyFlags.TerrainMesh | ChunkDirtyFlags.WaterMesh | ChunkDirtyFlags.Materials;

            if (localX == 0 && chunkX > 0) chunks[chunkX - 1, chunkY, chunkZ].MarkDirty(flags);
            if (localX == ChunkSizeX - 1 && chunkX + 1 < ChunkCountX) chunks[chunkX + 1, chunkY, chunkZ].MarkDirty(flags);
            if (localY == 0 && chunkY > 0) chunks[chunkX, chunkY - 1, chunkZ].MarkDirty(flags);
            if (localY == ChunkSizeY - 1 && chunkY + 1 < ChunkCountY) chunks[chunkX, chunkY + 1, chunkZ].MarkDirty(flags);
            if (localZ == 0 && chunkZ > 0) chunks[chunkX, chunkY, chunkZ - 1].MarkDirty(flags);
            if (localZ == ChunkSizeZ - 1 && chunkZ + 1 < ChunkCountZ) chunks[chunkX, chunkY, chunkZ + 1].MarkDirty(flags);

            if (localX == 0 && localZ == 0 && chunkX > 0 && chunkZ > 0)
                chunks[chunkX - 1, chunkY, chunkZ - 1].MarkDirty(flags);
            if (localX == 0 && localZ == ChunkSizeZ - 1 && chunkX > 0 && chunkZ + 1 < ChunkCountZ)
                chunks[chunkX - 1, chunkY, chunkZ + 1].MarkDirty(flags);
            if (localX == ChunkSizeX - 1 && localZ == 0 && chunkX + 1 < ChunkCountX && chunkZ > 0)
                chunks[chunkX + 1, chunkY, chunkZ - 1].MarkDirty(flags);
            if (localX == ChunkSizeX - 1 && localZ == ChunkSizeZ - 1 && chunkX + 1 < ChunkCountX && chunkZ + 1 < ChunkCountZ)
                chunks[chunkX + 1, chunkY, chunkZ + 1].MarkDirty(flags);
        }

        private bool IsWaterSupported(int x, int z)
        {
            for (var y = 0; y < Height; y++)
            {
                var cell = GetCell(x, y, z);
                if (!cell.HasWater || cell.SolidFill > 0 || y == 0)
                {
                    continue;
                }

                var below = GetCell(x, y - 1, z);
                if (below.SolidFill + below.WaterFill < WorldGrid.HeightStepsPerCell)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
