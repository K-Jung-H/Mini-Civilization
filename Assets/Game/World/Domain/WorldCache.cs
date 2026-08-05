using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    public sealed class WorldCache
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        private readonly WorldData world;
        private readonly SurfaceHeightData[] surfaceHeights;
        private ushort[] openHeights;
        private ushort[] waterDistances;
        private bool[] wetColumns;
        private Queue<int> waterQueue;

        public bool HasPathData => openHeights != null;

        internal WorldCache(WorldData world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            surfaceHeights = new SurfaceHeightData[world.Size * world.Size];
        }

        public SurfaceHeightData GetSurfaceHeight(int x, int z) =>
            world.ContainsColumn(x, z)
                ? surfaceHeights[x + world.Size * z]
                : default;

        public PathData GetPathData(int x, int y, int z)
        {
            if (!world.Contains(x, y, z) || openHeights == null)
            {
                return default;
            }

            var columnIndex = x + world.Size * z;
            return new PathData
            {
                OpenHeight = openHeights[
                    WorldIndex.EncodeCell(world, x, y, z)],
                WaterDistance = y
                    == surfaceHeights[columnIndex].GroundCellY
                        ? waterDistances[columnIndex]
                        : (ushort)0
            };
        }

        public void RebuildAll()
        {
            RebuildAllSurfaceHeights();
            RebuildAllPathData();
        }

        public void RebuildAllSurfaceHeights()
        {
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                RebuildSurfaceHeight(x, z);
            }
        }

        public void RebuildSurfaceHeight(int x, int z)
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

        public void RebuildAllPathData()
        {
            EnsurePathData();
            RebuildAllOpenHeights();
            RebuildWaterDistances();
        }

        public void RebuildPathColumns(IEnumerable<int> columnIndices)
        {
            if (columnIndices == null)
            {
                return;
            }

            EnsurePathData();
            foreach (var columnIndex in columnIndices)
            {
                if ((uint)columnIndex
                    >= (uint)(world.Size * world.Size))
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
            EnsurePathData();
            Array.Fill(waterDistances, ushort.MaxValue);
            Array.Clear(wetColumns, 0, wetColumns.Length);
            waterQueue.Clear();

            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                var columnIndex = x + world.Size * z;
                var surface = surfaceHeights[columnIndex];
                if (!surface.HasGround)
                {
                    waterDistances[columnIndex] = 0;
                    continue;
                }

                wetColumns[columnIndex] = surface.WaterHeight
                    > surface.GroundHeight;
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
                var nextDistance = waterDistances[index]
                        == ushort.MaxValue
                    ? ushort.MaxValue
                    : (ushort)Math.Min(
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
                    if (surfaceHeights[nextIndex].HasGround
                        && !wetColumns[nextIndex])
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private void EnsurePathData()
        {
            if (openHeights != null)
            {
                return;
            }

            openHeights = new ushort[checked(
                world.Size * world.Size * world.Height)];
            waterDistances = new ushort[checked(
                world.Size * world.Size)];
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

                var floor = y * WorldGrid.HeightStepsPerCell
                    + cell.Terrain.SolidHeight;
                openHeights[WorldIndex.EncodeCell(world, x, y, z)] =
                    checked((ushort)Math.Clamp(
                        ceiling - floor,
                        0,
                        ushort.MaxValue));
                ceiling = y * WorldGrid.HeightStepsPerCell;
            }
        }
    }
}
