using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Runtime
{
    public sealed class SurfaceCache
    {
        private sealed class ChunkCacheData
        {
            public ChunkCacheData(int cellCount)
            {
                Heights = new SurfaceHeightData[cellCount];
            }

            public SurfaceHeightData[] Heights { get; }
        }

        private readonly WorldData world;
        private readonly Dictionary<ChunkCoordinate, ChunkCacheData> chunks =
            new();

        internal SurfaceCache(WorldData world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public int PreparedChunkCount => chunks.Count;

        public bool IsPrepared(ChunkCoordinate coordinate) =>
            chunks.ContainsKey(coordinate);

        public bool IsPrepared(int x, int z) =>
            world.ContainsColumn(x, z)
            && chunks.ContainsKey(ToChunk(x, z));

        public SurfaceHeightData GetSurfaceHeight(int x, int z)
        {
            if (!world.ContainsColumn(x, z))
            {
                return default;
            }

            var coordinate = ToChunk(x, z);
            if (chunks.TryGetValue(coordinate, out var column))
            {
                return column.Heights[ToLocalColumnIndex(coordinate, x, z)];
            }

            return ResolveSurfaceHeight(x, z);
        }

        public void RebuildAll()
        {
            chunks.Clear();
            for (var chunkZ = 0; chunkZ < world.ChunkCountZ; chunkZ++)
            for (var chunkX = 0; chunkX < world.ChunkCountX; chunkX++)
            {
                PrepareChunk(new ChunkCoordinate(chunkX, chunkZ));
            }
        }

        public void Rebuild(int x, int z)
        {
            if (!world.ContainsColumn(x, z))
            {
                throw new ArgumentOutOfRangeException(
                    $"World column ({x}, {z}) is outside the world.");
            }

            var coordinate = ToChunk(x, z);
            if (chunks.TryGetValue(coordinate, out var column))
            {
                column.Heights[ToLocalColumnIndex(coordinate, x, z)] =
                    ResolveSurfaceHeight(x, z);
            }
        }

        internal bool PrepareChunk(ChunkCoordinate coordinate)
        {
            ValidateChunk(coordinate);
            if (chunks.ContainsKey(coordinate))
            {
                return false;
            }

            var column = new ChunkCacheData(
                checked(world.ChunkSizeX * world.ChunkSizeZ));
            chunks.Add(coordinate, column);
            var startX = coordinate.X * world.ChunkSizeX;
            var startZ = coordinate.Z * world.ChunkSizeZ;
            var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
            var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);
            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                column.Heights[ToLocalColumnIndex(coordinate, x, z)] =
                    ResolveSurfaceHeight(x, z);
            }

            return true;
        }

        internal bool ReleaseChunk(ChunkCoordinate coordinate) =>
            chunks.Remove(coordinate);

        private SurfaceHeightData ResolveSurfaceHeight(int x, int z)
        {
            var groundHeight = 0;
            var waterHeight = 0;
            for (var y = world.Height - 1; y >= 0; y--)
            {
                var cell = world.GetCell(x, y, z);
                if (waterHeight == 0 && cell.WaterHeight > 0)
                {
                    waterHeight = y * WorldGrid.HeightStepsPerCell
                        + cell.Terrain.SolidHeight
                        + cell.WaterHeight;
                }

                if (groundHeight == 0 && cell.Terrain.SolidHeight > 0)
                {
                    groundHeight = y * WorldGrid.HeightStepsPerCell
                        + cell.Terrain.SolidHeight;
                }

                if (groundHeight > 0 && waterHeight > 0)
                {
                    break;
                }
            }

            if (waterHeight <= groundHeight)
            {
                waterHeight = 0;
            }

            return new SurfaceHeightData
            {
                GroundHeight = groundHeight,
                WaterHeight = waterHeight
            };
        }

        private ChunkCoordinate ToChunk(int x, int z) =>
            WorldCoordinateUtility.ToChunk(x, z, world.ChunkSizeX);

        private int ToLocalColumnIndex(
            ChunkCoordinate coordinate,
            int x,
            int z) =>
            x - coordinate.X * world.ChunkSizeX
            + world.ChunkSizeX * (z - coordinate.Z * world.ChunkSizeZ);

        private void ValidateChunk(ChunkCoordinate coordinate)
        {
            if ((uint)coordinate.X >= world.ChunkCountX
                || (uint)coordinate.Z >= world.ChunkCountZ)
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }
        }
    }

    public sealed class NavigationCache
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        private sealed class ChunkCacheData
        {
            public ChunkCacheData(int horizontalCellCount, int worldHeight)
            {
                OpenHeights = new ushort[checked(
                    horizontalCellCount * worldHeight)];
                WaterDistances = new ushort[horizontalCellCount];
                WetColumns = new bool[horizontalCellCount];
            }

            public ushort[] OpenHeights { get; }
            public ushort[] WaterDistances { get; }
            public bool[] WetColumns { get; }
        }

        private readonly WorldData world;
        private readonly SurfaceCache surface;
        private readonly Dictionary<ChunkCoordinate, ChunkCacheData> chunks =
            new();
        private readonly Queue<CellColumnCoordinate> waterQueue = new();

        internal NavigationCache(WorldData world, SurfaceCache surface)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        public bool HasData => chunks.Count > 0;
        public int PreparedChunkCount => chunks.Count;

        public bool IsPrepared(ChunkCoordinate coordinate) =>
            chunks.ContainsKey(coordinate);

        public PathData GetPathData(int x, int y, int z)
        {
            if (!world.Contains(x, y, z)
                || !TryGetChunkCacheData(x, z, out var coordinate, out var column))
            {
                return default;
            }

            var localColumnIndex = ToLocalColumnIndex(coordinate, x, z);
            return new PathData
            {
                OpenHeight = column.OpenHeights[
                    localColumnIndex + HorizontalCellCount * y],
                WaterDistance = y == surface.GetSurfaceHeight(x, z).GroundCellY
                    ? column.WaterDistances[localColumnIndex]
                    : (ushort)0
            };
        }

        public void RebuildColumns(IEnumerable<CellColumnCoordinate> changedColumns)
        {
            if (changedColumns == null)
            {
                return;
            }

            foreach (var changed in changedColumns)
            {
                if (!world.ContainsColumn(changed.X, changed.Z)
                    || !TryGetChunkCacheData(
                        changed.X,
                        changed.Z,
                        out var coordinate,
                        out var column))
                {
                    continue;
                }

                RebuildOpenHeightColumn(
                    coordinate,
                    column,
                    changed.X,
                    changed.Z);
            }
        }

        public void RebuildWaterDistances()
        {
            waterQueue.Clear();
            foreach (var pair in chunks)
            {
                var coordinate = pair.Key;
                var column = pair.Value;
                var startX = coordinate.X * world.ChunkSizeX;
                var startZ = coordinate.Z * world.ChunkSizeZ;
                var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
                var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);
                for (var z = startZ; z < endZ; z++)
                for (var x = startX; x < endX; x++)
                {
                    var localIndex = ToLocalColumnIndex(coordinate, x, z);
                    var height = surface.GetSurfaceHeight(x, z);
                    var wet = height.HasGround
                        && height.WaterHeight > height.GroundHeight;
                    column.WetColumns[localIndex] = wet;
                    column.WaterDistances[localIndex] = wet
                        ? ushort.MaxValue
                        : (ushort)0;
                }
            }

            foreach (var pair in chunks)
            {
                var coordinate = pair.Key;
                var column = pair.Value;
                var startX = coordinate.X * world.ChunkSizeX;
                var startZ = coordinate.Z * world.ChunkSizeZ;
                var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
                var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);
                for (var z = startZ; z < endZ; z++)
                for (var x = startX; x < endX; x++)
                {
                    var localIndex = ToLocalColumnIndex(coordinate, x, z);
                    if (!column.WetColumns[localIndex]
                        || !HasPreparedDryNeighbor(x, z))
                    {
                        continue;
                    }

                    column.WaterDistances[localIndex] = 1;
                    waterQueue.Enqueue(new CellColumnCoordinate(x, z));
                }
            }

            while (waterQueue.Count > 0)
            {
                var current = waterQueue.Dequeue();
                if (!TryGetChunkCacheData(
                        current.X,
                        current.Z,
                        out var currentCoordinate,
                        out var currentColumn))
                {
                    continue;
                }

                var currentIndex = ToLocalColumnIndex(
                    currentCoordinate,
                    current.X,
                    current.Z);
                var nextDistance = currentColumn.WaterDistances[currentIndex]
                    == ushort.MaxValue
                        ? ushort.MaxValue
                        : (ushort)Math.Min(
                            ushort.MaxValue,
                            currentColumn.WaterDistances[currentIndex] + 1);
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var nextX = current.X + direction.x;
                    var nextZ = current.Z + direction.z;
                    if (!TryGetChunkCacheData(
                            nextX,
                            nextZ,
                            out var nextCoordinate,
                            out var nextColumn))
                    {
                        continue;
                    }

                    var nextIndex = ToLocalColumnIndex(
                        nextCoordinate,
                        nextX,
                        nextZ);
                    if (!nextColumn.WetColumns[nextIndex]
                        || nextColumn.WaterDistances[nextIndex] <= nextDistance)
                    {
                        continue;
                    }

                    nextColumn.WaterDistances[nextIndex] = nextDistance;
                    waterQueue.Enqueue(new CellColumnCoordinate(nextX, nextZ));
                }
            }
        }

        public void RebuildWaterDistances(
            IReadOnlyList<CellColumnCoordinate> changedColumns)
        {
            if (changedColumns == null || changedColumns.Count == 0)
            {
                return;
            }

            for (var index = 0; index < changedColumns.Count; index++)
            {
                var changed = changedColumns[index];
                if (IsPreparedCellColumn(changed.X, changed.Z)
                    || HasPreparedNeighbor(changed.X, changed.Z))
                {
                    RebuildWaterDistances();
                    return;
                }
            }
        }

        internal bool PrepareChunk(
            ChunkCoordinate coordinate,
            bool rebuildWaterDistances)
        {
            ValidateChunk(coordinate);
            if (chunks.ContainsKey(coordinate))
            {
                return false;
            }

            var column = new ChunkCacheData(HorizontalCellCount, world.Height);
            chunks.Add(coordinate, column);
            var startX = coordinate.X * world.ChunkSizeX;
            var startZ = coordinate.Z * world.ChunkSizeZ;
            var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
            var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);
            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                RebuildOpenHeightColumn(coordinate, column, x, z);
            }

            if (rebuildWaterDistances)
            {
                RebuildWaterDistances();
            }

            return true;
        }

        internal bool ReleaseChunk(
            ChunkCoordinate coordinate,
            bool rebuildWaterDistances)
        {
            if (!chunks.Remove(coordinate))
            {
                return false;
            }

            if (rebuildWaterDistances)
            {
                RebuildWaterDistances();
            }

            return true;
        }

        private void RebuildOpenHeightColumn(
            ChunkCoordinate coordinate,
            ChunkCacheData column,
            int x,
            int z)
        {
            var localColumnIndex = ToLocalColumnIndex(coordinate, x, z);
            for (var y = 0; y < world.Height; y++)
            {
                column.OpenHeights[
                    localColumnIndex + HorizontalCellCount * y] = 0;
            }

            var ceiling = world.Height * WorldGrid.HeightStepsPerCell;
            for (var y = world.Height - 1; y >= 0; y--)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasTerrain)
                {
                    continue;
                }

                var floor = y * WorldGrid.HeightStepsPerCell
                    + cell.Terrain.SolidHeight;
                column.OpenHeights[
                    localColumnIndex + HorizontalCellCount * y] =
                    checked((ushort)Math.Clamp(
                        ceiling - floor,
                        0,
                        ushort.MaxValue));
                ceiling = y * WorldGrid.HeightStepsPerCell;
            }
        }

        private bool HasPreparedDryNeighbor(int x, int z)
        {
            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                var direction = Directions[directionIndex];
                var nextX = x + direction.x;
                var nextZ = z + direction.z;
                if (!TryGetChunkCacheData(
                        nextX,
                        nextZ,
                        out var coordinate,
                        out var column))
                {
                    continue;
                }

                var index = ToLocalColumnIndex(
                    coordinate,
                    nextX,
                    nextZ);
                if (!column.WetColumns[index]
                    && surface.GetSurfaceHeight(nextX, nextZ).HasGround)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasPreparedNeighbor(int x, int z)
        {
            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                var direction = Directions[directionIndex];
                if (IsPreparedCellColumn(
                    x + direction.x,
                    z + direction.z))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsPreparedCellColumn(int x, int z) =>
            world.ContainsColumn(x, z)
            && chunks.ContainsKey(WorldCoordinateUtility.ToChunk(
                x,
                z,
                world.ChunkSizeX));

        private bool TryGetChunkCacheData(
            int x,
            int z,
            out ChunkCoordinate coordinate,
            out ChunkCacheData column)
        {
            if (!world.ContainsColumn(x, z))
            {
                coordinate = default;
                column = null;
                return false;
            }

            coordinate = WorldCoordinateUtility.ToChunk(
                x,
                z,
                world.ChunkSizeX);
            return chunks.TryGetValue(coordinate, out column);
        }

        private int HorizontalCellCount =>
            checked(world.ChunkSizeX * world.ChunkSizeZ);

        private int ToLocalColumnIndex(
            ChunkCoordinate coordinate,
            int x,
            int z) =>
            x - coordinate.X * world.ChunkSizeX
            + world.ChunkSizeX * (z - coordinate.Z * world.ChunkSizeZ);

        private void ValidateChunk(ChunkCoordinate coordinate)
        {
            if ((uint)coordinate.X >= world.ChunkCountX
                || (uint)coordinate.Z >= world.ChunkCountZ)
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }
        }
    }
}
