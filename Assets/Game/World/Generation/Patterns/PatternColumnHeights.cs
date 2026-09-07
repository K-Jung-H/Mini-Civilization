using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    // Absolute integer HeightUnits (one unit = 1/5 Cell), not WaterAmount ratios.
    public readonly struct PatternColumnHeights
    {
        private PatternColumnHeights(int ground, int water)
        {
            Ground = ground;
            WaterSurface = water;
        }

        public int Ground { get; }
        public int WaterSurface { get; }
        public bool HasWater => WaterSurface > Ground;
        public int UsedCellCount => checked((int)(((long)Math.Max(Ground, WaterSurface)
            + WorldGrid.HeightStepsPerCell - 1) / WorldGrid.HeightStepsPerCell));

        public static PatternColumnHeights Dry(int ground)
        {
            if (ground < 0) throw new ArgumentOutOfRangeException(nameof(ground));
            return new PatternColumnHeights(ground, 0);
        }

        public static PatternColumnHeights Wet(int ground, int waterSurface)
        {
            if (ground < 0 || waterSurface <= ground)
                throw new ArgumentOutOfRangeException(nameof(waterSurface),
                    "A wet column requires at least one HeightUnit of water.");
            return new PatternColumnHeights(ground, waterSurface);
        }

        public void ValidateWorldHeight(int heightCells)
        {
            var maximum = checked(heightCells * WorldGrid.HeightStepsPerCell);
            if (heightCells <= 0 || Ground > maximum || WaterSurface > maximum)
                throw new InvalidOperationException("Pattern height is outside the configured world height.");
        }

        public byte SolidAt(int y)
        {
            if (y < 0) throw new ArgumentOutOfRangeException(nameof(y));
            return (byte)Math.Clamp((long)Ground - (long)y * WorldGrid.HeightStepsPerCell,
                0L, WorldGrid.HeightStepsPerCell);
        }

        public byte WaterAt(int y)
        {
            var solid = SolidAt(y);
            return HasWater ? (byte)Math.Clamp(
                (long)WaterSurface - (long)y * WorldGrid.HeightStepsPerCell - solid,
                0L, WorldGrid.HeightStepsPerCell - solid) : (byte)0;
        }
    }

    internal static class PatternHeightQuantization
    {
        public static int Round(float height)
        {
            if (!float.IsFinite(height))
                throw new InvalidOperationException("Pattern height is not finite.");
            return checked((int)MathF.Round(height, MidpointRounding.AwayFromZero));
        }

        // Sea and dry transition samples still carry float heights. Quantize here;
        // equal rounded heights have no representable water volume.
        public static PatternColumnHeights FromCurrentPattern(in CombinedPatternCell pattern)
        {
            var ground = Round(pattern.GroundHeight);
            if (!pattern.Hydrology.HasWater) return PatternColumnHeights.Dry(ground);
            var water = Round(pattern.Hydrology.WaterSurfaceHeight);
            if (water < ground)
                throw new InvalidOperationException("Hydrology water surface is below its final ground height.");
            return water == ground ? PatternColumnHeights.Dry(ground)
                : PatternColumnHeights.Wet(ground, water);
        }
    }
}
