using System;
using System.Collections.Generic;
using System.Threading;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Semantic
{
    internal readonly struct HydrologyDrawingSample
    {
        public HydrologyDrawingSample(
            HydrologyFeatureKey key,
            WaterType waterType,
            float groundHeight,
            float waterSurfaceHeight,
            float interiorInfluence,
            float boundaryInfluence,
            bool hasWater)
        {
            Key = key;
            WaterType = waterType;
            GroundHeight = groundHeight;
            WaterSurfaceHeight = waterSurfaceHeight;
            InteriorInfluence = interiorInfluence;
            BoundaryInfluence = boundaryInfluence;
            HasWater = hasWater;
        }

        public HydrologyFeatureKey Key { get; }
        public WaterType WaterType { get; }
        public float GroundHeight { get; }
        public float WaterSurfaceHeight { get; }
        public float InteriorInfluence { get; }
        public float BoundaryInfluence { get; }
        public bool HasWater { get; }
    }

    internal sealed class WaterMapPainter
    {
        private readonly HydrologyFeatureSettingsData settings;
        private readonly ITerrainPatternMapReader terrain;
        private readonly int seaSeed;
        private readonly HydrologyFeatureKey seaKey;

        public WaterMapPainter(
            HydrologyFeatureSettingsData settings,
            ITerrainPatternMapReader terrain)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            seaSeed = WaterMapDrawingMath.DeriveSeed(
                settings.World.Seed,
                "water-map-sea");
            seaKey = WaterMapDrawingMath.CreateKey(
                HydrologyFeatureKind.Sea,
                0,
                0,
                seaSeed);
        }

        public HydrologyPatternTile Paint(
            PatternTileKey key,
            PatternTileBounds bounds,
            IReadOnlyList<BasinWaterBrush> basins,
            IReadOnlyList<RiverWaterBrush> rivers,
            CancellationToken cancellationToken)
        {
            var featureIndices = new Dictionary<HydrologyFeatureKey, int>();
            var features = new List<HydrologyFeatureKey>();
            var cells = new HydrologyPatternCell[checked(bounds.Width * bounds.Height)];
            for (var localZ = 0; localZ < bounds.Height; localZ++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var absoluteZ = checked(bounds.MinimumZ + localZ);
                for (var localX = 0; localX < bounds.Width; localX++)
                {
                    var absoluteX = checked(bounds.MinimumX + localX);
                    var terrainCell = terrain.GetCell(absoluteX, absoluteZ);
                    var index = checked(localX + bounds.Width * localZ);
                    if (!TryPaint(
                            absoluteX,
                            absoluteZ,
                            terrainCell,
                            basins,
                            rivers,
                            out var sample))
                    {
                        cells[index] = HydrologyPatternCell.None;
                        continue;
                    }

                    if (!featureIndices.TryGetValue(sample.Key, out var featureIndex))
                    {
                        featureIndex = features.Count;
                        featureIndices.Add(sample.Key, featureIndex);
                        features.Add(sample.Key);
                    }

                    cells[index] = sample.HasWater
                        ? HydrologyPatternCell.CreateWater(
                            sample.WaterType,
                            featureIndex,
                            sample.GroundHeight,
                            sample.WaterSurfaceHeight,
                            sample.InteriorInfluence,
                            sample.BoundaryInfluence)
                        : HydrologyPatternCell.CreateGroundOverride(
                            featureIndex,
                            sample.GroundHeight,
                            sample.BoundaryInfluence);
                }
            }

            return new HydrologyPatternTile(key, bounds, features.ToArray(), cells);
        }

        private bool TryPaint(
            int x,
            int z,
            TerrainPatternCell terrainCell,
            IReadOnlyList<BasinWaterBrush> basins,
            IReadOnlyList<RiverWaterBrush> rivers,
            out HydrologyDrawingSample sample)
        {
            if (TryPaintSea(x, z, terrainCell, out sample))
            {
                return true;
            }

            var hasGroundOverride = false;
            var groundOverride = default(HydrologyDrawingSample);
            for (var index = 0; index < basins.Count; index++)
            {
                if (basins[index].TrySample(x, z, terrainCell, out sample))
                {
                    if (sample.HasWater)
                    {
                        return true;
                    }

                    if (!hasGroundOverride)
                    {
                        groundOverride = sample;
                        hasGroundOverride = true;
                    }
                }
            }

            for (var index = 0; index < rivers.Count; index++)
            {
                if (rivers[index].TrySample(
                        x,
                        z,
                        terrain,
                        out sample))
                {
                    return true;
                }
            }

            sample = groundOverride;
            return hasGroundOverride;
        }

        private bool TryPaintSea(
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
            unchecked((int)SemanticPatternNoise.Hash(
                regionKey,
                channel,
                settings.World.Seed));

        private float Value01(int regionKey, int channel) =>
            (SemanticPatternNoise.Hash(
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
