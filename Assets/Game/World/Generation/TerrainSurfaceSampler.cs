using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal readonly struct TerrainSurfaceSample
    {
        public TerrainSurfaceSample(
            float surfaceUnits,
            bool hasSeaWater,
            int waterTopUnits)
        {
            SurfaceUnits = surfaceUnits;
            HasSeaWater = hasSeaWater;
            WaterTopUnits = waterTopUnits;
        }

        public float SurfaceUnits { get; }
        public bool HasSeaWater { get; }
        public int WaterTopUnits { get; }
    }

    internal static class TerrainSurfaceSampler
    {
        public static TerrainSurfaceSample SampleResolved(
            in WorldDensityField densityField,
            WorldSettingsData settings,
            int worldX,
            int worldZ,
            in WorldFieldSample field,
            in WorldPatternResult terrain) => new(
                FindSurfaceUnits(
                    densityField,
                    settings,
                    worldX,
                    worldZ,
                    field,
                    terrain),
                terrain.WaterType == WaterType.Sea,
                terrain.WaterTopUnits);

        private static float FindSurfaceUnits(
            in WorldDensityField densityField,
            WorldSettingsData settings,
            int worldX,
            int worldZ,
            in WorldFieldSample field,
            in WorldPatternResult profile)
        {
            var maximumHeightUnit = checked(
                settings.WorldHeight * WorldGrid.HeightStepsPerCell);
            var verticalFactor = MathF.Abs(profile.VerticalFactor);
            var detailMagnitude = MathF.Abs(profile.DetailUnits);
            var expectedSurface = settings.TerrainBaseHeightUnits
                + profile.SurfaceOffsetUnits;
            var upperBound = verticalFactor > float.Epsilon
                ? expectedSurface
                    + detailMagnitude * (
                        MathF.Abs(field.Detail) + 1f / verticalFactor)
                : maximumHeightUnit;
            var upperUnit = Math.Clamp(
                (int)MathF.Ceiling(upperBound) + 1,
                1,
                maximumHeightUnit);
            var upperDensity = densityField.Sample(
                worldX,
                upperUnit,
                worldZ,
                field,
                profile);
            while (upperDensity >= 0f && upperUnit < maximumHeightUnit)
            {
                upperUnit++;
                upperDensity = densityField.Sample(
                    worldX,
                    upperUnit,
                    worldZ,
                    field,
                    profile);
            }

            if (upperDensity >= 0f)
            {
                return maximumHeightUnit;
            }

            for (var lowerUnit = upperUnit - 1; lowerUnit >= 0; lowerUnit--)
            {
                var lowerDensity = densityField.Sample(
                    worldX,
                    lowerUnit,
                    worldZ,
                    field,
                    profile);
                if (lowerDensity < 0f)
                {
                    upperDensity = lowerDensity;
                    continue;
                }

                var denominator = lowerDensity - upperDensity;
                var fraction = denominator > 0f
                    ? lowerDensity / denominator
                    : 0f;
                return Math.Clamp(
                    lowerUnit + Math.Clamp(fraction, 0f, 1f),
                    0f,
                    maximumHeightUnit);
            }

            return 0f;
        }
    }
}
