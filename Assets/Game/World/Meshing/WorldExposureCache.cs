using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Meshing
{
    internal readonly struct ExposedCell
    {
        public readonly CellCoordinate Coordinate;
        public readonly CellExposureFlags Exposure;

        public ExposedCell(
            CellCoordinate coordinate,
            CellExposureFlags exposure)
        {
            Coordinate = coordinate;
            Exposure = exposure;
        }
    }

    internal sealed class WorldExposureCache
    {
        private static readonly (int x, int z)[] NeighborDirections =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        private sealed class ChunkExposure
        {
            public readonly List<ExposedCell> SolidCells = new();
            public readonly List<ExposedCell> WaterCells = new();
        }

        private readonly WorldData world;
        private readonly HashSet<ChunkColumnCoordinate> preparedColumns = new();
        private readonly Dictionary<ChunkCoordinate, ChunkExposure> chunks = new();

        public WorldExposureCache(WorldData world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public int PreparedColumnCount => preparedColumns.Count;
        public int PreparedChunkCount => chunks.Count;

        public void PrepareColumn(ChunkColumnCoordinate coordinate)
        {
            if ((uint)coordinate.X >= world.ChunkCountX
                || (uint)coordinate.Z >= world.ChunkCountZ)
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }

            if (!preparedColumns.Add(coordinate))
            {
                return;
            }

            for (var chunkY = 0; chunkY < world.ChunkCountY; chunkY++)
            {
                RebuildChunk(new ChunkCoordinate(
                    coordinate.X,
                    chunkY,
                    coordinate.Z));
            }

            RebuildPreparedNeighborBoundaries(coordinate);
        }

        public void ReleaseColumn(ChunkColumnCoordinate coordinate)
        {
            if (!preparedColumns.Remove(coordinate))
            {
                return;
            }

            for (var chunkY = 0; chunkY < world.ChunkCountY; chunkY++)
            {
                chunks.Remove(new ChunkCoordinate(
                    coordinate.X,
                    chunkY,
                    coordinate.Z));
            }

        }

        public void ApplyChanges(WorldChangeSet changeSet)
        {
            if (changeSet == null
                || changeSet.World != world
                || (!changeSet.Includes(WorldChangeType.CellStructure)
                    && !changeSet.Includes(WorldChangeType.WaterTopology)
                    && !changeSet.Includes(WorldChangeType.WaterSurface)))
            {
                return;
            }

            for (var index = 0; index < changeSet.AffectedChunks.Count; index++)
            {
                var chunk = changeSet.AffectedChunks[index];
                if (preparedColumns.Contains(
                    new ChunkColumnCoordinate(chunk.X, chunk.Z)))
                {
                    RebuildChunk(chunk);
                }
            }
        }

        internal void CopySolidCellsForPatch(
            int startX,
            int startZ,
            int endX,
            int endZ,
            List<ExposedCell> target)
        {
            CopyCellsForPatch(
                startX,
                startZ,
                endX,
                endZ,
                target,
                water: false);
        }

        internal void CopyWaterCellsForPatch(
            int startX,
            int startZ,
            int endX,
            int endZ,
            List<ExposedCell> target)
        {
            CopyCellsForPatch(
                startX,
                startZ,
                endX,
                endZ,
                target,
                water: true);
        }

        private void CopyCellsForPatch(
            int startX,
            int startZ,
            int endX,
            int endZ,
            List<ExposedCell> target,
            bool water)
        {
            target.Clear();
            startX = Math.Max(0, startX);
            startZ = Math.Max(0, startZ);
            endX = Math.Min(world.Size, endX);
            endZ = Math.Min(world.Size, endZ);
            if (startX >= endX || startZ >= endZ)
            {
                return;
            }

            var minimumChunkX = startX / world.ChunkSizeX;
            var maximumChunkX = (endX - 1) / world.ChunkSizeX;
            var minimumChunkZ = startZ / world.ChunkSizeZ;
            var maximumChunkZ = (endZ - 1) / world.ChunkSizeZ;
            for (var chunkY = 0; chunkY < world.ChunkCountY; chunkY++)
            for (var chunkZ = minimumChunkZ; chunkZ <= maximumChunkZ; chunkZ++)
            for (var chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
            {
                if (!chunks.TryGetValue(
                        new ChunkCoordinate(chunkX, chunkY, chunkZ),
                        out var chunk))
                {
                    continue;
                }

                var source = water ? chunk.WaterCells : chunk.SolidCells;
                for (var index = 0; index < source.Count; index++)
                {
                    var coordinate = source[index].Coordinate;
                    if (coordinate.X >= startX
                        && coordinate.X < endX
                        && coordinate.Z >= startZ
                        && coordinate.Z < endZ)
                    {
                        target.Add(source[index]);
                    }
                }
            }
        }

        private void RebuildPreparedNeighborBoundaries(
            ChunkColumnCoordinate coordinate)
        {
            for (var directionIndex = 0;
                 directionIndex < NeighborDirections.Length;
                 directionIndex++)
            {
                var direction = NeighborDirections[directionIndex];
                var x = coordinate.X + direction.x;
                var z = coordinate.Z + direction.z;
                var neighbor = new ChunkColumnCoordinate(x, z);
                if (!preparedColumns.Contains(neighbor))
                {
                    continue;
                }

                for (var chunkY = 0; chunkY < world.ChunkCountY; chunkY++)
                {
                    RebuildChunkBoundary(
                        new ChunkCoordinate(x, chunkY, z),
                        coordinate);
                }
            }
        }

        private void RebuildChunkBoundary(
            ChunkCoordinate coordinate,
            ChunkColumnCoordinate changedNeighbor)
        {
            if (!chunks.TryGetValue(coordinate, out var target))
            {
                return;
            }

            var startX = coordinate.X * world.ChunkSizeX;
            var startY = coordinate.Y * world.ChunkSizeY;
            var startZ = coordinate.Z * world.ChunkSizeZ;
            var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
            var endY = Math.Min(startY + world.ChunkSizeY, world.Height);
            var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);
            var boundaryX = changedNeighbor.X < coordinate.X
                ? startX
                : changedNeighbor.X > coordinate.X
                    ? endX - 1
                    : -1;
            var boundaryZ = changedNeighbor.Z < coordinate.Z
                ? startZ
                : changedNeighbor.Z > coordinate.Z
                    ? endZ - 1
                    : -1;
            if (boundaryX >= 0)
            {
                target.SolidCells.RemoveAll(
                    cell => cell.Coordinate.X == boundaryX);
                target.WaterCells.RemoveAll(
                    cell => cell.Coordinate.X == boundaryX);
                for (var y = startY; y < endY; y++)
                for (var z = startZ; z < endZ; z++)
                {
                    AddExposure(target, boundaryX, y, z);
                }
            }

            if (boundaryZ >= 0)
            {
                target.SolidCells.RemoveAll(
                    cell => cell.Coordinate.Z == boundaryZ);
                target.WaterCells.RemoveAll(
                    cell => cell.Coordinate.Z == boundaryZ);
                for (var y = startY; y < endY; y++)
                for (var x = startX; x < endX; x++)
                {
                    AddExposure(target, x, y, boundaryZ);
                }
            }
        }

        private void RebuildChunk(ChunkCoordinate coordinate)
        {
            var column = new ChunkColumnCoordinate(
                coordinate.X,
                coordinate.Z);
            if ((uint)coordinate.Y >= world.ChunkCountY
                || !preparedColumns.Contains(column))
            {
                return;
            }

            if (!world.TryGetChunk(coordinate, out _))
            {
                chunks.Remove(coordinate);
                return;
            }

            if (!chunks.TryGetValue(coordinate, out var target))
            {
                target = new ChunkExposure();
                chunks.Add(coordinate, target);
            }

            target.SolidCells.Clear();
            target.WaterCells.Clear();
            var startX = coordinate.X * world.ChunkSizeX;
            var startY = coordinate.Y * world.ChunkSizeY;
            var startZ = coordinate.Z * world.ChunkSizeZ;
            var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
            var endY = Math.Min(startY + world.ChunkSizeY, world.Height);
            var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);
            for (var y = startY; y < endY; y++)
            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                AddExposure(target, x, y, z);
            }
        }

        private void AddExposure(
            ChunkExposure target,
            int x,
            int y,
            int z)
        {
            var exposure = CellOccupancyResolver.ResolveExposure(
                world,
                x,
                y,
                z);
            const CellExposureFlags solidFlags =
                CellExposureFlags.SolidTop
                | CellExposureFlags.SolidBottom
                | CellExposureFlags.SolidNegativeX
                | CellExposureFlags.SolidPositiveX
                | CellExposureFlags.SolidNegativeZ
                | CellExposureFlags.SolidPositiveZ;
            const CellExposureFlags waterFlags =
                CellExposureFlags.WaterTop
                | CellExposureFlags.WaterBottom
                | CellExposureFlags.WaterNegativeX
                | CellExposureFlags.WaterPositiveX
                | CellExposureFlags.WaterNegativeZ
                | CellExposureFlags.WaterPositiveZ;
            if ((exposure & solidFlags) != 0)
            {
                target.SolidCells.Add(new ExposedCell(
                    new CellCoordinate(x, y, z),
                    exposure));
            }

            if ((exposure & waterFlags) != 0)
            {
                target.WaterCells.Add(new ExposedCell(
                    new CellCoordinate(x, y, z),
                    exposure));
            }
        }
    }
}
