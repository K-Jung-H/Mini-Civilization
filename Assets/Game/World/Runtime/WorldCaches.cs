using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Runtime
{
    public sealed class SurfaceCache
    {
        private readonly WorldData world;
        private readonly SurfaceHeightData[] surfaceHeights;

        internal SurfaceCache(WorldData world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            surfaceHeights = new SurfaceHeightData[world.Size * world.Size];
        }

        public SurfaceHeightData GetSurfaceHeight(int x, int z) =>
            world.ContainsColumn(x, z)
                ? surfaceHeights[x + world.Size * z]
                : default;

        public void RebuildAll()
        {
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                Rebuild(x, z);
            }
        }

        public void Rebuild(int x, int z)
        {
            if (!world.ContainsColumn(x, z))
            {
                throw new ArgumentOutOfRangeException(
                    $"World column ({x}, {z}) is outside the world.");
            }

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

            surfaceHeights[x + world.Size * z] = new SurfaceHeightData
            {
                GroundHeight = groundHeight,
                WaterHeight = waterHeight
            };
        }
    }

    public sealed class NavigationCache
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        private readonly WorldData world;
        private readonly SurfaceCache surface;
        private ushort[] openHeights;
        private ushort[] waterDistances;
        private bool[] wetColumns;
        private Queue<int> waterQueue;
        private readonly HashSet<int> affectedWaterColumns = new();

        internal NavigationCache(WorldData world, SurfaceCache surface)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        public bool HasData => openHeights != null;

        public PathData GetPathData(int x, int y, int z)
        {
            if (!world.Contains(x, y, z) || openHeights == null)
            {
                return default;
            }

            var columnIndex = x + world.Size * z;
            return new PathData
            {
                OpenHeight = openHeights[WorldIndex.EncodeCell(world, x, y, z)],
                WaterDistance = y == surface.GetSurfaceHeight(x, z).GroundCellY
                    ? waterDistances[columnIndex]
                    : (ushort)0
            };
        }

        public void RebuildAll()
        {
            EnsureData();
            RebuildAllOpenHeights();
            RebuildWaterDistances();
        }

        public void RebuildColumns(IEnumerable<int> columnIndices)
        {
            if (columnIndices == null)
            {
                return;
            }

            EnsureData();
            foreach (var columnIndex in columnIndices)
            {
                if ((uint)columnIndex >= (uint)(world.Size * world.Size))
                {
                    continue;
                }

                RebuildOpenHeightColumn(
                    columnIndex % world.Size,
                    columnIndex / world.Size);
            }
        }

        public void RebuildWaterDistances()
        {
            EnsureData();
            Array.Fill(waterDistances, ushort.MaxValue);
            Array.Clear(wetColumns, 0, wetColumns.Length);
            waterQueue.Clear();

            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var columnIndex = x + world.Size * z;
                var column = surface.GetSurfaceHeight(x, z);
                if (!column.HasGround)
                {
                    waterDistances[columnIndex] = 0;
                    continue;
                }

                wetColumns[columnIndex] = column.WaterHeight > column.GroundHeight;
                if (!wetColumns[columnIndex])
                {
                    waterDistances[columnIndex] = 0;
                }
            }

            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var index = x + world.Size * z;
                if (!wetColumns[index] || !HasDryNeighbor(x, z))
                {
                    continue;
                }

                waterDistances[index] = 1;
                waterQueue.Enqueue(index);
            }

            while (waterQueue.Count > 0)
            {
                var index = waterQueue.Dequeue();
                var x = index % world.Size;
                var z = index / world.Size;
                var nextDistance = waterDistances[index] == ushort.MaxValue
                    ? ushort.MaxValue
                    : (ushort)Math.Min(ushort.MaxValue, waterDistances[index] + 1);
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var nextX = x + direction.x;
                    var nextZ = z + direction.z;
                    if (!world.ContainsColumn(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = nextX + world.Size * nextZ;
                    if (!wetColumns[nextIndex]
                        || waterDistances[nextIndex] <= nextDistance)
                    {
                        continue;
                    }

                    waterDistances[nextIndex] = nextDistance;
                    waterQueue.Enqueue(nextIndex);
                }
            }

            bool HasDryNeighbor(int x, int z)
            {
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var nextX = x + direction.x;
                    var nextZ = z + direction.z;
                    if (!world.ContainsColumn(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = nextX + world.Size * nextZ;
                    if (surface.GetSurfaceHeight(nextX, nextZ).HasGround
                        && !wetColumns[nextIndex])
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void RebuildWaterDistances(
            IReadOnlyList<int> changedColumns)
        {
            EnsureData();
            if (changedColumns == null || changedColumns.Count == 0)
            {
                return;
            }

            var maximumPartialColumns = Math.Max(
                1,
                waterDistances.Length / 4);
            if (changedColumns.Count > maximumPartialColumns)
            {
                RebuildWaterDistances();
                return;
            }

            affectedWaterColumns.Clear();
            for (var index = 0; index < changedColumns.Count; index++)
            {
                var columnIndex = changedColumns[index];
                if ((uint)columnIndex >= (uint)waterDistances.Length)
                {
                    continue;
                }

                RefreshWaterColumnState(columnIndex);
                if (!CollectAffectedWaterComponent(
                        columnIndex,
                        maximumPartialColumns))
                {
                    RebuildWaterDistances();
                    return;
                }

                var x = columnIndex % world.Size;
                var z = columnIndex / world.Size;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var nextX = x + direction.x;
                    var nextZ = z + direction.z;
                    if (!world.ContainsColumn(nextX, nextZ)
                        || !CollectAffectedWaterComponent(
                            nextX + world.Size * nextZ,
                            maximumPartialColumns))
                    {
                        if (!world.ContainsColumn(nextX, nextZ))
                        {
                            continue;
                        }

                        RebuildWaterDistances();
                        return;
                    }
                }
            }

            RebuildAffectedWaterDistances();
        }

        private bool CollectAffectedWaterComponent(
            int startIndex,
            int maximumPartialColumns)
        {
            RefreshWaterColumnState(startIndex);
            if (!wetColumns[startIndex]
                || affectedWaterColumns.Contains(startIndex))
            {
                return true;
            }

            waterQueue.Clear();
            waterQueue.Enqueue(startIndex);
            affectedWaterColumns.Add(startIndex);
            while (waterQueue.Count > 0)
            {
                var index = waterQueue.Dequeue();
                if (affectedWaterColumns.Count > maximumPartialColumns)
                {
                    waterQueue.Clear();
                    return false;
                }

                var x = index % world.Size;
                var z = index / world.Size;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var nextX = x + direction.x;
                    var nextZ = z + direction.z;
                    if (!world.ContainsColumn(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = nextX + world.Size * nextZ;
                    RefreshWaterColumnState(nextIndex);
                    if (!wetColumns[nextIndex]
                        || !affectedWaterColumns.Add(nextIndex))
                    {
                        continue;
                    }

                    waterQueue.Enqueue(nextIndex);
                }
            }

            return true;
        }

        private void RebuildAffectedWaterDistances()
        {
            if (affectedWaterColumns.Count == 0)
            {
                return;
            }

            waterQueue.Clear();
            foreach (var index in affectedWaterColumns)
            {
                waterDistances[index] = ushort.MaxValue;
            }

            foreach (var index in affectedWaterColumns)
            {
                var x = index % world.Size;
                var z = index / world.Size;
                if (!HasDryNeighbor(x, z))
                {
                    continue;
                }

                waterDistances[index] = 1;
                waterQueue.Enqueue(index);
            }

            while (waterQueue.Count > 0)
            {
                var index = waterQueue.Dequeue();
                var x = index % world.Size;
                var z = index / world.Size;
                var nextDistance = (ushort)Math.Min(
                    ushort.MaxValue,
                    waterDistances[index] + 1);
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var direction = Directions[directionIndex];
                    var nextX = x + direction.x;
                    var nextZ = z + direction.z;
                    if (!world.ContainsColumn(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = nextX + world.Size * nextZ;
                    if (!affectedWaterColumns.Contains(nextIndex)
                        || waterDistances[nextIndex] <= nextDistance)
                    {
                        continue;
                    }

                    waterDistances[nextIndex] = nextDistance;
                    waterQueue.Enqueue(nextIndex);
                }
            }
        }

        private void RefreshWaterColumnState(int columnIndex)
        {
            var x = columnIndex % world.Size;
            var z = columnIndex / world.Size;
            var column = surface.GetSurfaceHeight(x, z);
            wetColumns[columnIndex] = column.HasGround
                && column.WaterHeight > column.GroundHeight;
            if (!wetColumns[columnIndex])
            {
                waterDistances[columnIndex] = 0;
            }
        }

        private bool HasDryNeighbor(int x, int z)
        {
            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                var direction = Directions[directionIndex];
                var nextX = x + direction.x;
                var nextZ = z + direction.z;
                if (!world.ContainsColumn(nextX, nextZ))
                {
                    continue;
                }

                var nextIndex = nextX + world.Size * nextZ;
                var next = surface.GetSurfaceHeight(nextX, nextZ);
                if (next.HasGround && !wetColumns[nextIndex])
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureData()
        {
            if (openHeights != null)
            {
                return;
            }

            openHeights = new ushort[checked(world.Size * world.Size * world.Height)];
            waterDistances = new ushort[checked(world.Size * world.Size)];
            wetColumns = new bool[waterDistances.Length];
            waterQueue = new Queue<int>(waterDistances.Length);
        }

        private void RebuildAllOpenHeights()
        {
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                RebuildOpenHeightColumn(x, z);
            }
        }

        private void RebuildOpenHeightColumn(int x, int z)
        {
            var firstIndex = WorldIndex.EncodeCell(world, x, 0, z);
            for (var y = 0; y < world.Height; y++)
            {
                openHeights[firstIndex + world.Size * world.Size * y] = 0;
            }

            var ceiling = world.Height * WorldGrid.HeightStepsPerCell;
            for (var y = world.Height - 1; y >= 0; y--)
            {
                var cell = world.GetCell(x, y, z);
                if (!cell.HasTerrain)
                {
                    continue;
                }

                var floor = y * WorldGrid.HeightStepsPerCell + cell.Terrain.SolidHeight;
                openHeights[WorldIndex.EncodeCell(world, x, y, z)] =
                    checked((ushort)Math.Clamp(ceiling - floor, 0, ushort.MaxValue));
                ceiling = y * WorldGrid.HeightStepsPerCell;
            }
        }
    }
}
