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
        public WorldChangeId LastChangeId { get; private set; }

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
        internal void MarkChanged(WorldChangeId changeId) =>
            LastChangeId = changeId;

        internal void SetCellBulk(int x, int y, int z, CellData cell)
        {
            cell.Normalize();
            cells[ToIndex(x, y, z)] = cell;
        }

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
        private readonly SurfaceColumnData[] surfaceColumnMap;
        private readonly ColumnEnvironmentData[] columnEnvironmentMap;

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
        public WorldChangeId CurrentChangeId { get; private set; }
        public WaterSourceCollection WaterSources { get; }
        public WaterFlowScheduleData WaterFlowSchedule { get; }

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
            ChunkCountX = size / chunkSizeX;
            ChunkCountY = height / chunkSizeY;
            ChunkCountZ = size / chunkSizeZ;
            chunks = new ChunkData[ChunkCountX, ChunkCountY, ChunkCountZ];
            surfaceColumnMap = new SurfaceColumnData[size * size];
            columnEnvironmentMap = new ColumnEnvironmentData[size * size];
            WaterSources = new WaterSourceCollection();
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

            for (var i = 0; i < surfaceColumnMap.Length; i++)
            {
                surfaceColumnMap[i].SurfaceCellY = -1;
                surfaceColumnMap[i].WaterCellY = -1;
            }
        }

        public void ConfigureWaterFlow(WaterFlowRules rules)
        {
            WaterFlowRules = new WaterFlowRules(
                rules.SpreadAmountLoss,
                rules.MinimumSpreadAmount,
                rules.DissipationAmountLoss);
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

        public SurfaceColumnData GetSurfaceColumn(int x, int z)
        {
            if (!ContainsColumn(x, z))
            {
                return new SurfaceColumnData { SurfaceCellY = -1, WaterCellY = -1 };
            }

            return surfaceColumnMap[x + Size * z];
        }

        public bool HasSolidCell(int x, int z)
        {
            if (!ContainsColumn(x, z))
            {
                return false;
            }

            for (var y = 0; y < Height; y++)
            {
                if (GetCell(x, y, z).HasSolid)
                {
                    return true;
                }
            }

            return false;
        }

        public void SetSurfaceColumn(int x, int z, SurfaceColumnData column)
        {
            surfaceColumnMap[x + Size * z] = column;
        }

        public ColumnEnvironmentData GetColumnEnvironment(int x, int z)
        {
            if (!ContainsColumn(x, z))
            {
                return default;
            }

            return columnEnvironmentMap[x + Size * z];
        }

        public void SetColumnEnvironment(int x, int z, ColumnEnvironmentData environment)
        {
            if (!ContainsColumn(x, z))
            {
                throw new ArgumentOutOfRangeException($"World column ({x}, {z}) is outside the world.");
            }

            columnEnvironmentMap[x + Size * z] = environment;
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
            var column = new SurfaceColumnData
            {
                SurfaceCellY = -1,
                WaterCellY = -1
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
            }

            SetSurfaceColumn(x, z, column);
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

        internal WorldChangeId AdvanceChangeId()
        {
            CurrentChangeId = new WorldChangeId(checked(CurrentChangeId.Value + 1));
            return CurrentChangeId;
        }

        internal void MarkChunkChanged(
            ChunkCoordinate coordinate,
            WorldChangeId changeId)
        {
            chunks[coordinate.X, coordinate.Y, coordinate.Z]
                .MarkChanged(changeId);
        }

    }
}
