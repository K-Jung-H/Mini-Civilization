using System;
using MiniCivilization.World.Domain;
namespace MiniCivilization.World.Generation.Patterns
{
    internal sealed class SeaPatternSampler
    {
        private readonly HydrologyFeatureSettingsData settings;

        private readonly int seaSeed;
        private readonly HydrologyFeatureKey seaKey;

        public SeaPatternSampler(
            HydrologyFeatureSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            seaSeed = WaterMapDrawingMath.DeriveSeed(
                settings.World.Seed,
                "water-map-sea");
            seaKey = WaterMapDrawingMath.CreateKey(
                HydrologyFeatureKind.Sea,
                0,
                0,
                seaSeed);
        }

        public bool TrySample(
            int x,
            int z,
            TerrainPatternCell terrainCell,
            out HydrologyDrawingSample sample)
        {
            if (!terrainCell.HasSeaPattern
                && !terrainCell.HasSecondarySeaPattern)
            {
                sample = default;
                return false;
            }

            var sea = settings.Sea;
            var primaryInfluence = Math.Clamp(
                terrainCell.PrimaryInfluence,
                0f,
                1f);
            var primarySea = terrainCell.HasSeaPattern
                ? CreateSeaGeometry(
                    x,
                    z,
                    terrainCell.SeaRegionKey,
                    terrainCell.SeaInteriorProgress)
                : default;
            var secondarySea = terrainCell.HasSecondarySeaPattern
                ? CreateSeaGeometry(
                    x,
                    z,
                    terrainCell.SecondarySeaRegionKey,
                    terrainCell.SecondarySeaInteriorProgress)
                : default;
            float ground;
            float interior;
            float boundary;
            if (terrainCell.HasSeaPattern)
            {
                if (terrainCell.HasSecondarySeaPattern)
                {
                    ground = WaterMapDrawingMath.Lerp(
                        secondarySea.GroundHeight,
                        primarySea.GroundHeight,
                        primaryInfluence);
                    interior = WaterMapDrawingMath.Lerp(
                        secondarySea.Interior,
                        primarySea.Interior,
                        primaryInfluence);
                }
                else
                {
                    ground = WaterMapDrawingMath.Lerp(
                        terrainCell.SecondaryTerrainSurfaceHeight,
                        primarySea.GroundHeight,
                        primaryInfluence);
                    interior = primarySea.Interior * primaryInfluence;
                }

                boundary = 1f - primaryInfluence;
            }
            else
            {
                var secondaryInfluence = 1f - primaryInfluence;
                ground = WaterMapDrawingMath.Lerp(
                    terrainCell.PrimaryTerrainSurfaceHeight,
                    secondarySea.GroundHeight,
                    secondaryInfluence);
                interior = secondarySea.Interior * secondaryInfluence;
                boundary = primaryInfluence;
            }

            sample = new HydrologyDrawingSample(
                seaKey,
                WaterType.Sea,
                ground,
                sea.SurfaceHeight,
                interior,
                boundary,
                sea.SurfaceHeight > ground);
            return true;
        }

        private SeaDrawingGeometry CreateSeaGeometry(
            int x,
            int z,
            int regionKey,
            float regionInterior)
        {
            var sea = settings.Sea;
            var warpX = WaterMapDrawingMath.SampleSigned(
                    x,
                    z,
                    sea.DomainWarp.Field,
                    DeriveRegionSeed(regionKey, 5000))
                * sea.DomainWarp.StrengthCells;
            var warpZ = WaterMapDrawingMath.SampleSigned(
                    x,
                    z,
                    sea.DomainWarp.Field,
                    DeriveRegionSeed(regionKey, 5001))
                * sea.DomainWarp.StrengthCells;
            var variation = WaterMapDrawingMath.SampleNormalized(
                x + warpX,
                z + warpZ,
                sea.BasinField,
                DeriveRegionSeed(regionKey, 5010));
            var interior = Math.Clamp(
                regionInterior + (variation * 2f - 1f) * sea.BasinVariation,
                0f,
                1f);
            var maximumDepth = WaterMapDrawingMath.Lerp(
                sea.MaximumDepth.Minimum,
                sea.MaximumDepth.Maximum,
                Value01(regionKey, 5020));
            var bedAmplitude = WaterMapDrawingMath.Lerp(
                sea.SeabedAmplitude.Minimum,
                sea.SeabedAmplitude.Maximum,
                Value01(regionKey, 5040));
            var depthProgress = sea.DepthByInterior.Evaluate(interior);
            var bedNoise = WaterMapDrawingMath.SampleSigned(
                    x,
                    z,
                    sea.SeabedField,
                    DeriveRegionSeed(regionKey, 5030))
                * bedAmplitude;
            return new SeaDrawingGeometry(
                sea.SurfaceHeight - depthProgress * maximumDepth
                    + bedNoise * depthProgress,
                interior);
        }

        private int DeriveRegionSeed(int regionKey, int channel) =>
            unchecked((int)PatternNoise.Hash(
                regionKey,
                channel,
                settings.World.Seed));

        private float Value01(int regionKey, int channel) =>
            (PatternNoise.Hash(
                regionKey,
                channel,
                settings.World.Seed) & 0x00FFFFFFu) / 16777215f;

        private readonly struct SeaDrawingGeometry
        {
            public SeaDrawingGeometry(float groundHeight, float interior)
            {
                GroundHeight = groundHeight;
                Interior = interior;
            }

            public float GroundHeight { get; }
            public float Interior { get; }
        }
    }
}
