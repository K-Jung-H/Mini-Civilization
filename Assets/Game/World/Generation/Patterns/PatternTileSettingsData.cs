using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{

    public sealed class PatternTileGridSettingsData
    {
        public PatternTileGridSettingsData(
            WorldSettingsData world,
            int patternTileChunkSpan)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            if (patternTileChunkSpan <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(patternTileChunkSpan));
            }

            if (world.WorldType == WorldType.Finite
                && world.InitialChunkCountXZ % patternTileChunkSpan != 0)
            {
                throw new ArgumentException(
                    "Finite worlds require an exact Pattern Tile to world boundary alignment.",
                    nameof(patternTileChunkSpan));
            }

            PatternTileChunkSpan = patternTileChunkSpan;
            PatternTileCellSpan = checked(
                world.ChunkCellCountXZ * patternTileChunkSpan);
        }

        public WorldSettingsData World { get; }
        public int PatternTileChunkSpan { get; }
        public int PatternTileCellSpan { get; }

        public PatternTileKey GetKeyForCell(int cellX, int cellZ) => new(
            WorldCoordinateUtility.FloorDivide(cellX, PatternTileCellSpan),
            WorldCoordinateUtility.FloorDivide(cellZ, PatternTileCellSpan));

        public PatternTileKey GetKeyForChunk(ChunkCoordinate chunk) => new(
            WorldCoordinateUtility.FloorDivide(
                chunk.X,
                PatternTileChunkSpan),
            WorldCoordinateUtility.FloorDivide(
                chunk.Z,
                PatternTileChunkSpan));

        public PatternTileBounds GetCoreBounds(PatternTileKey key)
        {
            var minimumX = checked(key.X * PatternTileCellSpan);
            var minimumZ = checked(key.Z * PatternTileCellSpan);
            return new PatternTileBounds(
                minimumX,
                minimumZ,
                checked(minimumX + PatternTileCellSpan),
                checked(minimumZ + PatternTileCellSpan));
        }

        public bool IsOutputAllowed(PatternTileKey key)
        {
            if (World.WorldType == WorldType.Infinite)
            {
                return true;
            }

            var core = GetCoreBounds(key);
            return core.MinimumX >= World.MinimumCellCoordinate
                && core.MinimumZ >= World.MinimumCellCoordinate
                && core.MaximumXExclusive <= World.MaximumCellCoordinateExclusive
                && core.MaximumZExclusive <= World.MaximumCellCoordinateExclusive;
        }

        public IEnumerable<PatternTileKey> EnumerateIntersecting(
            PatternTileBounds bounds)
        {
            var minimum = GetKeyForCell(bounds.MinimumX, bounds.MinimumZ);
            var maximum = GetKeyForCell(
                checked(bounds.MaximumXExclusive - 1),
                checked(bounds.MaximumZExclusive - 1));
            for (var z = minimum.Z; z <= maximum.Z; z++)
            {
                for (var x = minimum.X; x <= maximum.X; x++)
                {
                    yield return new PatternTileKey(x, z);
                }
            }
        }

        public IEnumerable<PatternTileKey> EnumerateOutputIntersecting(
            PatternTileBounds bounds)
        {
            foreach (var key in EnumerateIntersecting(bounds))
            {
                if (IsOutputAllowed(key))
                {
                    yield return key;
                }
            }
        }
    }

}
