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
            world.IsColumnLoaded(x, z)
            && chunks.ContainsKey(ToChunk(x, z));

        public SurfaceHeightData GetSurfaceHeight(int x, int z)
        {
            if (!world.IsColumnLoaded(x, z))
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
            foreach (var chunk in world.EnumerateLoadedChunks())
            {
                PrepareChunk(chunk.Coordinate);
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
            var endX = startX + world.ChunkSizeX;
            var endZ = startZ + world.ChunkSizeZ;
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
            if (!world.IsChunkWithinBounds(coordinate))
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
                InputGround = new bool[horizontalCellCount];
                InputWet = new bool[horizontalCellCount];
                WaterParents = new byte[horizontalCellCount];
            }

            public ushort[] OpenHeights { get; }
            public ushort[] WaterDistances { get; }
            public bool[] WetColumns { get; }
            public bool[] InputGround { get; }
            public bool[] InputWet { get; }
            public byte[] WaterParents { get; }
        }

        private readonly WorldData world;
        private readonly SurfaceCache surface;
        private readonly Dictionary<ChunkCoordinate, ChunkCacheData> chunks =
            new();
        private readonly Queue<CellColumnCoordinate> dirtyColumns = new();
        private readonly Queue<CellColumnCoordinate> raiseQueue = new();
        private readonly Queue<CellColumnCoordinate> lowerQueue = new();
        private readonly HashSet<CellColumnCoordinate> dirtySet = new();
        private readonly HashSet<CellColumnCoordinate> raiseSet = new();
        private readonly HashSet<CellColumnCoordinate> lowerSet = new();

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
                if (RefreshDistanceInput(coordinate, column, changed.X, changed.Z))
                    EnqueueDirtyWithNeighbors(changed.X, changed.Z);
            }
        }

        public void RebuildWaterDistances()
        {
            foreach (var pair in chunks)
            {
                var startX = pair.Key.X * world.ChunkSizeX;
                var startZ = pair.Key.Z * world.ChunkSizeZ;
                for (var z = startZ; z < startZ + world.ChunkSizeZ; z++)
                for (var x = startX; x < startX + world.ChunkSizeX; x++)
                    EnqueueDirty(x, z);
            }
        }

        internal void AdvanceWaterDistances(int budget = 2048)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var i = 0; i < budget; i++)
            {
                if (!AdvanceWaterDistance()) return;
                if ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d /
                    System.Diagnostics.Stopwatch.Frequency >= 2d) return;
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
                if (TryGetChunkCacheData(changed.X, changed.Z, out var coordinate, out var column)
                    && RefreshDistanceInput(coordinate, column, changed.X, changed.Z))
                {
                    EnqueueDirtyWithNeighbors(changed.X, changed.Z);
                }
            }
        }

        internal bool PrepareChunk(
            ChunkCoordinate coordinate)
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
            var endX = startX + world.ChunkSizeX;
            var endZ = startZ + world.ChunkSizeZ;
            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                RebuildOpenHeightColumn(coordinate, column, x, z);
                RefreshDistanceInput(coordinate, column, x, z);
            }

            EnqueueChunk(coordinate);

            return true;
        }

        internal bool ReleaseChunk(
            ChunkCoordinate coordinate)
        {
            if (!chunks.TryGetValue(coordinate, out _))
            {
                return false;
            }
            EnqueueChunkBorder(coordinate);
            chunks.Remove(coordinate);

            return true;
        }

        private bool AdvanceWaterDistance()
        {
            if (dirtyColumns.Count > 0)
            {
                var coordinate = dirtyColumns.Dequeue();
                dirtySet.Remove(coordinate);
                QueueRaise(coordinate.X, coordinate.Z);
                foreach (var direction in Directions)
                    QueueRaise(coordinate.X + direction.x, coordinate.Z + direction.z);
                return true;
            }

            if (raiseQueue.Count > 0)
            {
                var coordinate = raiseQueue.Dequeue();
                raiseSet.Remove(coordinate);
                if (!TryGetChunkCacheData(coordinate.X, coordinate.Z, out var chunk, out var column)) return true;
                var index = ToLocalColumnIndex(chunk, coordinate.X, coordinate.Z);
                if (!column.InputWet[index])
                {
                    column.WetColumns[index] = false;
                    column.WaterDistances[index] = 0;
                    column.WaterParents[index] = 0;
                }
                else if (!HasValidParent(coordinate.X, coordinate.Z, column.WaterDistances[index], column.WaterParents[index]))
                {
                    column.WetColumns[index] = true;
                    column.WaterDistances[index] = ushort.MaxValue;
                    column.WaterParents[index] = 0;
                    foreach (var direction in Directions)
                    {
                        var nextX = coordinate.X + direction.x;
                        var nextZ = coordinate.Z + direction.z;
                        if (PointsTo(nextX, nextZ, coordinate.X, coordinate.Z)) QueueRaise(nextX, nextZ);
                    }
                }
                QueueLower(coordinate.X, coordinate.Z);
                foreach (var direction in Directions) QueueLower(coordinate.X + direction.x, coordinate.Z + direction.z);
                return true;
            }

            if (lowerQueue.Count == 0) return false;
            var cell = lowerQueue.Dequeue();
            lowerSet.Remove(cell);
            if (!TryGetChunkCacheData(cell.X, cell.Z, out var owner, out var data)) return true;
            var local = ToLocalColumnIndex(owner, cell.X, cell.Z);
            if (!data.InputWet[local]) return true;
            var best = ushort.MaxValue;
            byte parent = 0;
            foreach (var direction in Directions)
            {
                var x = cell.X + direction.x;
                var z = cell.Z + direction.z;
                if (!TryGetChunkCacheData(x, z, out var neighborChunk, out var neighbor)) continue;
                var neighborIndex = ToLocalColumnIndex(neighborChunk, x, z);
                if (!neighbor.InputWet[neighborIndex] && neighbor.InputGround[neighborIndex])
                {
                    best = 1;
                    parent = 5;
                    break;
                }
                if (!neighbor.InputWet[neighborIndex] || neighbor.WaterDistances[neighborIndex] == ushort.MaxValue) continue;
                var candidate = (ushort)Math.Min(ushort.MaxValue, neighbor.WaterDistances[neighborIndex] + 1);
                if (candidate < best)
                {
                    best = candidate;
                    parent = DirectionToParent(direction.x, direction.z);
                }
            }
            if (best >= data.WaterDistances[local]) return true;
            data.WetColumns[local] = true;
            data.WaterDistances[local] = best;
            data.WaterParents[local] = parent;
            foreach (var direction in Directions) QueueLower(cell.X + direction.x, cell.Z + direction.z);
            return true;
        }

        private void EnqueueDirtyWithNeighbors(int x, int z)
        {
            EnqueueDirty(x, z);
            foreach (var direction in Directions) EnqueueDirty(x + direction.x, z + direction.z);
        }

        private void EnqueueDirty(int x, int z)
        {
            if (!TryGetChunkCacheData(x, z, out _, out _)) return;
            var cell = new CellColumnCoordinate(x, z);
            if (dirtySet.Add(cell)) dirtyColumns.Enqueue(cell);
        }

        private void QueueRaise(int x, int z)
        {
            if (!TryGetChunkCacheData(x, z, out _, out _)) return;
            var cell = new CellColumnCoordinate(x, z);
            if (raiseSet.Add(cell)) raiseQueue.Enqueue(cell);
        }

        private void QueueLower(int x, int z)
        {
            if (!TryGetChunkCacheData(x, z, out _, out _)) return;
            var cell = new CellColumnCoordinate(x, z);
            if (lowerSet.Add(cell)) lowerQueue.Enqueue(cell);
        }

        private void EnqueueChunk(ChunkCoordinate chunk)
        {
            var startX = chunk.X * world.ChunkSizeX;
            var startZ = chunk.Z * world.ChunkSizeZ;
            for (var z = startZ; z < startZ + world.ChunkSizeZ; z++)
            for (var x = startX; x < startX + world.ChunkSizeX; x++) EnqueueDirty(x, z);
        }

        private void EnqueueChunkBorder(ChunkCoordinate chunk)
        {
            var startX = chunk.X * world.ChunkSizeX;
            var startZ = chunk.Z * world.ChunkSizeZ;
            for (var z = startZ - 1; z <= startZ + world.ChunkSizeZ; z++)
            {
                EnqueueDirty(startX - 1, z);
                EnqueueDirty(startX + world.ChunkSizeX, z);
            }
            for (var x = startX; x < startX + world.ChunkSizeX; x++)
            {
                EnqueueDirty(x, startZ - 1);
                EnqueueDirty(x, startZ + world.ChunkSizeZ);
            }
        }

        private bool HasValidParent(int x, int z, ushort distance, byte parent)
        {
            if (distance == 0 || distance == ushort.MaxValue) return false;
            if (parent == 5)
            {
                foreach (var direction in Directions)
                    if (TryGetChunkCacheData(x + direction.x, z + direction.z, out var chunk, out var column))
                    {
                        var index = ToLocalColumnIndex(chunk, x + direction.x, z + direction.z);
                        if (!column.InputWet[index] && column.InputGround[index]) return true;
                    }
                return false;
            }
            ParentOffset(parent, out var offsetX, out var offsetZ);
            if (!TryGetChunkCacheData(x + offsetX, z + offsetZ, out var parentChunk, out var parentColumn)) return false;
            var parentIndex = ToLocalColumnIndex(parentChunk, x + offsetX, z + offsetZ);
            return parentColumn.InputWet[parentIndex]
                && parentColumn.WaterDistances[parentIndex] != ushort.MaxValue
                && parentColumn.WaterDistances[parentIndex] + 1 == distance;
        }

        private bool PointsTo(int x, int z, int targetX, int targetZ)
        {
            if (!TryGetChunkCacheData(x, z, out var chunk, out var column)) return false;
            var parent = column.WaterParents[ToLocalColumnIndex(chunk, x, z)];
            ParentOffset(parent, out var offsetX, out var offsetZ);
            return x + offsetX == targetX && z + offsetZ == targetZ;
        }

        private static byte DirectionToParent(int x, int z) => (x, z) switch
        {
            (1, 0) => 1, (-1, 0) => 2, (0, 1) => 3, (0, -1) => 4, _ => 0
        };

        private static void ParentOffset(byte parent, out int x, out int z)
        {
            (x, z) = parent switch { 1 => (1, 0), 2 => (-1, 0), 3 => (0, 1), 4 => (0, -1), _ => (0, 0) };
        }

        private bool RefreshDistanceInput(ChunkCoordinate coordinate, ChunkCacheData column, int x, int z)
        {
            var index = ToLocalColumnIndex(coordinate, x, z);
            var height = surface.GetSurfaceHeight(x, z);
            var wet = height.HasGround && height.WaterHeight > height.GroundHeight;
            if (column.InputGround[index] == height.HasGround && column.InputWet[index] == wet) return false;
            column.InputGround[index] = height.HasGround;
            column.InputWet[index] = wet;
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

        private bool TryGetChunkCacheData(
            int x,
            int z,
            out ChunkCoordinate coordinate,
            out ChunkCacheData column)
        {
            if (!world.IsColumnLoaded(x, z))
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
            if (!world.IsChunkWithinBounds(coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }
        }
    }
}
