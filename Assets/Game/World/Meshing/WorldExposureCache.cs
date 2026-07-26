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

    public sealed class WorldExposureCache
    {
        private sealed class ChunkExposure
        {
            public readonly List<ExposedCell> SolidCells = new();
            public readonly List<ExposedCell> WaterCells = new();
        }

        private readonly WorldData world;
        private readonly ChunkExposure[,,] chunks;

        public WorldExposureCache(WorldData world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            chunks = new ChunkExposure[
                world.ChunkCountX,
                world.ChunkCountY,
                world.ChunkCountZ];
            for (var chunkY = 0; chunkY < world.ChunkCountY; chunkY++)
            for (var chunkZ = 0; chunkZ < world.ChunkCountZ; chunkZ++)
            for (var chunkX = 0; chunkX < world.ChunkCountX; chunkX++)
            {
                chunks[chunkX, chunkY, chunkZ] = new ChunkExposure();
                RebuildChunk(new ChunkCoordinate(chunkX, chunkY, chunkZ));
            }
        }

        public void ApplyChanges(WorldChangeSet changeSet)
        {
            if (changeSet == null
                || changeSet.World != world
                || (!changeSet.Includes(WorldChangeType.CellStructure)
                    && !changeSet.Includes(WorldChangeType.WaterTopology)))
            {
                return;
            }

            for (var index = 0; index < changeSet.AffectedChunks.Count; index++)
            {
                RebuildChunk(changeSet.AffectedChunks[index]);
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
                var source = water
                    ? chunks[chunkX, chunkY, chunkZ].WaterCells
                    : chunks[chunkX, chunkY, chunkZ].SolidCells;
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

        private void RebuildChunk(ChunkCoordinate coordinate)
        {
            if ((uint)coordinate.X >= world.ChunkCountX
                || (uint)coordinate.Y >= world.ChunkCountY
                || (uint)coordinate.Z >= world.ChunkCountZ)
            {
                return;
            }

            var target = chunks[coordinate.X, coordinate.Y, coordinate.Z];
            target.SolidCells.Clear();
            target.WaterCells.Clear();
            var startX = coordinate.X * world.ChunkSizeX;
            var startY = coordinate.Y * world.ChunkSizeY;
            var startZ = coordinate.Z * world.ChunkSizeZ;
            var endX = startX + world.ChunkSizeX;
            var endY = startY + world.ChunkSizeY;
            var endZ = startZ + world.ChunkSizeZ;
            for (var y = startY; y < endY; y++)
            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
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
}
