using System;
using System.Threading;

namespace MiniCivilization.World.Generation.Patterns
{
    internal readonly struct TerrainPatternSample
    {
        public TerrainPatternSample(
            TerrainPatternType type,
            float baseSurfaceHeight,
            float detailSurfaceHeight,
            bool hasSeaPattern,
            int seaRegionKey,
            float seaInteriorProgress,
            bool hasSecondarySeaPattern,
            int secondarySeaRegionKey,
            float secondarySeaInteriorProgress,
            float primaryInfluence,
            float primaryTerrainSurfaceHeight,
            float secondaryTerrainSurfaceHeight)
        {
            Type = type;
            BaseSurfaceHeight = baseSurfaceHeight;
            DetailSurfaceHeight = detailSurfaceHeight;
            HasSeaPattern = hasSeaPattern;
            SeaRegionKey = seaRegionKey;
            SeaInteriorProgress = seaInteriorProgress;
            HasSecondarySeaPattern = hasSecondarySeaPattern;
            SecondarySeaRegionKey = secondarySeaRegionKey;
            SecondarySeaInteriorProgress = secondarySeaInteriorProgress;
            PrimaryInfluence = primaryInfluence;
            PrimaryTerrainSurfaceHeight = primaryTerrainSurfaceHeight;
            SecondaryTerrainSurfaceHeight = secondaryTerrainSurfaceHeight;
        }

        public TerrainPatternType Type { get; }
        public float BaseSurfaceHeight { get; }
        public float DetailSurfaceHeight { get; }
        public bool HasSeaPattern { get; }
        public int SeaRegionKey { get; }
        public float SeaInteriorProgress { get; }
        public bool HasSecondarySeaPattern { get; }
        public int SecondarySeaRegionKey { get; }
        public float SecondarySeaInteriorProgress { get; }
        public float PrimaryInfluence { get; }
        public float PrimaryTerrainSurfaceHeight { get; }
        public float SecondaryTerrainSurfaceHeight { get; }
        public float SurfaceHeight => BaseSurfaceHeight + DetailSurfaceHeight;
    }

    public sealed class TerrainPatternEvaluator
    {
        private enum RegionPattern : byte
        {
            Smooth,
            Rugged,
            Mountain,
            Canyon,
            Sea
        }

        private readonly struct RegionCandidate
        {
            public RegionCandidate(
                long gridX,
                long gridZ,
                int key,
                RegionPattern pattern,
                float influence,
                float interiorProgress)
            {
                GridX = gridX;
                GridZ = gridZ;
                Key = key;
                Pattern = pattern;
                Influence = influence;
                InteriorProgress = interiorProgress;
            }

            public long GridX { get; }
            public long GridZ { get; }
            public int Key { get; }
            public RegionPattern Pattern { get; }
            public float Influence { get; }
            public float InteriorProgress { get; }
        }

        private readonly struct RegionSample
        {
            public RegionSample(RegionCandidate primary, RegionCandidate secondary)
            {
                Primary = primary;
                Secondary = secondary;
            }

            public RegionCandidate Primary { get; }
            public RegionCandidate Secondary { get; }
        }

        private readonly struct TerrainContribution
        {
            public TerrainContribution(float baseHeight, float detailHeight)
            {
                BaseHeight = baseHeight;
                DetailHeight = detailHeight;
            }

            public float BaseHeight { get; }
            public float DetailHeight { get; }
        }

        private readonly TerrainPatternSettingsData settings;

        public TerrainPatternEvaluator(TerrainPatternSettingsData settings)
        {
            this.settings = settings
                ?? throw new ArgumentNullException(nameof(settings));
        }

        internal TerrainPatternSample EvaluateSample(int worldX, int worldZ)
        {
            var continentalness = SampleNoise(
                worldX,
                worldZ,
                settings.NoiseRouter.Continentalness,
                DeriveSeed(settings.WorldSeed, "world-router-continentalness"));
            var erosion = SampleNoise(
                worldX,
                worldZ,
                settings.NoiseRouter.Erosion,
                DeriveSeed(settings.WorldSeed, "world-router-erosion"));
            var region = SampleRegion(worldX, worldZ);
            var primary = SampleContribution(region.Primary, worldX, worldZ);
            var secondary = SampleContribution(region.Secondary, worldX, worldZ);
            var baseSurface = settings.TerrainBaseHeight
                + settings.BaseSurface.SurfaceByContinentalness.Evaluate(
                    NormalizeNoise(continentalness,
                        settings.NoiseRouter.Continentalness.Mode))
                + settings.BaseSurface.SurfaceByErosion.Evaluate(
                    NormalizeNoise(erosion, settings.NoiseRouter.Erosion.Mode));
            var primaryInfluence = region.Primary.Influence;
            var terrainType = ResolveTerrainType(region);
            return new TerrainPatternSample(
                terrainType,
                baseSurface + Lerp(
                    secondary.BaseHeight,
                    primary.BaseHeight,
                    primaryInfluence),
                Lerp(
                    secondary.DetailHeight,
                    primary.DetailHeight,
                    primaryInfluence),
                region.Primary.Pattern == RegionPattern.Sea,
                region.Primary.Key,
                region.Primary.InteriorProgress,
                region.Secondary.Pattern == RegionPattern.Sea,
                region.Secondary.Key,
                region.Secondary.InteriorProgress,
                primaryInfluence,
                baseSurface + primary.BaseHeight + primary.DetailHeight,
                baseSurface + secondary.BaseHeight + secondary.DetailHeight);
        }

        internal TerrainPatternCell ToCell(
            TerrainPatternSample sample,
            float slope) => new(
            sample.Type,
            sample.BaseSurfaceHeight,
            sample.DetailSurfaceHeight,
            slope,
            sample.HasSeaPattern,
            sample.SeaRegionKey,
            sample.SeaInteriorProgress,
            sample.HasSecondarySeaPattern,
            sample.SecondarySeaRegionKey,
            sample.SecondarySeaInteriorProgress,
            sample.PrimaryInfluence,
            sample.PrimaryTerrainSurfaceHeight,
            sample.SecondaryTerrainSurfaceHeight);

        internal static float CalculateSlope(
            float left,
            float right,
            float down,
            float up)
        {
            var horizontal = (right - left) * 0.5f;
            var vertical = (up - down) * 0.5f;
            return MathF.Sqrt(horizontal * horizontal + vertical * vertical);
        }

        private RegionSample SampleRegion(int worldX, int worldZ)
        {
            var regionSettings = settings.Region;
            var warpX = SampleSignedNoise(
                    worldX,
                    worldZ,
                    regionSettings.WarpField,
                    DeriveSeed(settings.WorldSeed, "world-router-pattern-warp-x"))
                * regionSettings.WarpStrengthCells;
            var warpZ = SampleSignedNoise(
                    worldX,
                    worldZ,
                    regionSettings.WarpField,
                    DeriveSeed(settings.WorldSeed, "world-router-pattern-warp-z"))
                * regionSettings.WarpStrengthCells;
            var sampleX = worldX + warpX;
            var sampleZ = worldZ + warpZ;
            var gridX = (long)Math.Floor(sampleX / regionSettings.SizeCells);
            var gridZ = (long)Math.Floor(sampleZ / regionSettings.SizeCells);
            var regionSeed = DeriveSeed(
                settings.WorldSeed,
                "world-router-pattern-region");
            var nearestDistance = double.PositiveInfinity;
            var secondDistance = double.PositiveInfinity;
            var nearestGridX = 0L;
            var nearestGridZ = 0L;
            var secondGridX = 0L;
            var secondGridZ = 0L;

            const int candidateRingCount = 1;
            for (var offsetZ = -candidateRingCount;
                 offsetZ <= candidateRingCount;
                 offsetZ++)
            {
                for (var offsetX = -candidateRingCount;
                     offsetX <= candidateRingCount;
                     offsetX++)
                {
                    var candidateGridX = checked(gridX + offsetX);
                    var candidateGridZ = checked(gridZ + offsetZ);
                    var centerX = (candidateGridX + 0.5)
                        * regionSettings.SizeCells
                        + SignedValue01(
                            candidateGridX,
                            candidateGridZ,
                            unchecked(regionSeed + 101))
                            * regionSettings.CenterJitter
                            * regionSettings.SizeCells;
                    var centerZ = (candidateGridZ + 0.5)
                        * regionSettings.SizeCells
                        + SignedValue01(
                            candidateGridX,
                            candidateGridZ,
                            unchecked(regionSeed + 211))
                            * regionSettings.CenterJitter
                            * regionSettings.SizeCells;
                    var deltaX = sampleX - centerX;
                    var deltaZ = sampleZ - centerZ;
                    var distance = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
                    if (distance < nearestDistance)
                    {
                        secondDistance = nearestDistance;
                        secondGridX = nearestGridX;
                        secondGridZ = nearestGridZ;
                        nearestDistance = distance;
                        nearestGridX = candidateGridX;
                        nearestGridZ = candidateGridZ;
                    }
                    else if (distance < secondDistance)
                    {
                        secondDistance = distance;
                        secondGridX = candidateGridX;
                        secondGridZ = candidateGridZ;
                    }
                }
            }

            var boundaryDistance = Math.Max(
                0d,
                (secondDistance - nearestDistance) * 0.5d);
            var boundaryProgress = SmootherStep((float)Math.Clamp(
                boundaryDistance / regionSettings.BoundaryBlendCells,
                0d,
                1d));
            var primaryInfluence = 0.5f + boundaryProgress * 0.5f;
            var interiorProgress = SmootherStep((float)Math.Clamp(
                boundaryDistance / (
                    regionSettings.SizeCells
                    * regionSettings.InteriorReachRatio),
                0d,
                1d));
            return new RegionSample(
                CreateCandidate(
                    nearestGridX,
                    nearestGridZ,
                    primaryInfluence,
                    interiorProgress),
                CreateCandidate(
                    secondGridX,
                    secondGridZ,
                    1f - primaryInfluence,
                    0f));
        }

        private TerrainContribution SampleContribution(
            RegionCandidate candidate,
            int worldX,
            int worldZ)
        {
            return candidate.Pattern switch
            {
                RegionPattern.Smooth => SampleSurfaceForm(
                    candidate,
                    worldX,
                    worldZ,
                    settings.Smooth,
                    "smooth"),
                RegionPattern.Rugged => SampleSurfaceForm(
                    candidate,
                    worldX,
                    worldZ,
                    settings.Rugged,
                    "rugged"),
                RegionPattern.Mountain => SampleMountain(
                    candidate,
                    worldX,
                    worldZ),
                RegionPattern.Canyon => SampleCanyon(
                    candidate,
                    worldX,
                    worldZ),
                RegionPattern.Sea => default,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private TerrainContribution SampleSurfaceForm(
            RegionCandidate candidate,
            int worldX,
            int worldZ,
            TerrainSurfaceFormData form,
            string patternName)
        {
            Warp(
                candidate,
                worldX,
                worldZ,
                form.DomainWarp,
                patternName + "-warp",
                out var sampleX,
                out var sampleZ);
            var shape = form.ShapeResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.ShapeField,
                    DeriveCandidateSeed(candidate, patternName + "-shape")))
                * ResolveRange(
                    candidate,
                    form.ShapeAmplitude,
                    patternName + "-shape-amplitude");
            var detail = SampleSignedNoise(
                    sampleX,
                    sampleZ,
                    form.DetailField,
                    DeriveCandidateSeed(candidate, patternName + "-detail"))
                * ResolveRange(
                    candidate,
                    form.DetailAmplitude,
                    patternName + "-detail-amplitude");
            return new TerrainContribution(shape, detail);
        }

        private TerrainContribution SampleMountain(
            RegionCandidate candidate,
            int worldX,
            int worldZ)
        {
            var form = settings.Mountain;
            Warp(
                candidate,
                worldX,
                worldZ,
                form.DomainWarp,
                "mountain-warp",
                out var sampleX,
                out var sampleZ);
            var mass = form.MassResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.MassField,
                    DeriveCandidateSeed(candidate, "mountain-mass")))
                * ResolveRange(candidate, form.Height, "mountain-height");
            var ridge = form.RidgeResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.RidgeField,
                    DeriveCandidateSeed(candidate, "mountain-ridge")))
                * ResolveRange(
                    candidate,
                    form.RidgeStrength,
                    "mountain-ridge-strength");
            var detail = SampleSignedNoise(
                    sampleX,
                    sampleZ,
                    form.DetailField,
                    DeriveCandidateSeed(candidate, "mountain-detail"))
                * ResolveRange(
                    candidate,
                    form.DetailAmplitude,
                    "mountain-detail-amplitude");
            return new TerrainContribution(mass + ridge, detail);
        }

        private TerrainContribution SampleCanyon(
            RegionCandidate candidate,
            int worldX,
            int worldZ)
        {
            var form = settings.Canyon;
            Warp(
                candidate,
                worldX,
                worldZ,
                form.DomainWarp,
                "canyon-warp",
                out var sampleX,
                out var sampleZ);
            var basin = form.BasinResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.BasinField,
                    DeriveCandidateSeed(candidate, "canyon-basin")))
                * ResolveRange(
                    candidate,
                    form.BasinDepthRatio,
                    "canyon-basin-ratio");
            var valley = form.ValleyResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.ValleyField,
                    DeriveCandidateSeed(candidate, "canyon-valley")))
                * ResolveRange(
                    candidate,
                    form.ValleyDepthRatio,
                    "canyon-valley-ratio");
            var depthProgress = Math.Clamp(
                1f - (1f - basin) * (1f - valley),
                0f,
                1f);
            var depth = depthProgress * ResolveRange(
                candidate,
                form.Depth,
                "canyon-depth");
            var detail = SampleSignedNoise(
                    sampleX,
                    sampleZ,
                    form.DetailField,
                    DeriveCandidateSeed(candidate, "canyon-detail"))
                * ResolveRange(
                    candidate,
                    form.DetailAmplitude,
                    "canyon-detail-amplitude");
            return new TerrainContribution(-depth, detail);
        }

        private void Warp(
            RegionCandidate candidate,
            int worldX,
            int worldZ,
            TerrainDomainWarpData warp,
            string path,
            out double sampleX,
            out double sampleZ)
        {
            sampleX = worldX + SampleSignedNoise(
                worldX,
                worldZ,
                warp.Field,
                DeriveCandidateSeed(candidate, path + "-x"))
                * warp.StrengthCells;
            sampleZ = worldZ + SampleSignedNoise(
                worldX,
                worldZ,
                warp.Field,
                DeriveCandidateSeed(candidate, path + "-z"))
                * warp.StrengthCells;
        }

        private RegionCandidate CreateCandidate(
            long gridX,
            long gridZ,
            float influence,
            float interiorProgress)
        {
            var regionSeed = DeriveSeed(
                settings.WorldSeed,
                "world-router-pattern-region");
            var hash = Hash(gridX, gridZ, regionSeed);
            var selector = (hash & 0x00FFFFFFu) / 16777215f
                * settings.Region.TotalShare;
            var pattern = SelectRegionPattern(selector);
            return new RegionCandidate(
                gridX,
                gridZ,
                unchecked((int)hash),
                pattern,
                influence,
                interiorProgress);
        }

        private RegionPattern SelectRegionPattern(float selector)
        {
            if ((selector -= settings.Region.SmoothShare) <= 0f)
            {
                return RegionPattern.Smooth;
            }

            if ((selector -= settings.Region.RuggedShare) <= 0f)
            {
                return RegionPattern.Rugged;
            }

            if ((selector -= settings.Region.MountainShare) <= 0f)
            {
                return RegionPattern.Mountain;
            }

            return selector - settings.Region.CanyonShare <= 0f
                ? RegionPattern.Canyon
                : RegionPattern.Sea;
        }

        private TerrainPatternType ResolveTerrainType(RegionSample region)
        {
            if (region.Primary.Pattern != RegionPattern.Sea)
            {
                return ToTerrainPatternType(region.Primary.Pattern);
            }

            if (region.Secondary.Pattern != RegionPattern.Sea)
            {
                return ToTerrainPatternType(region.Secondary.Pattern);
            }

            return TerrainPatternType.Smooth;
        }

        private static TerrainPatternType ToTerrainPatternType(RegionPattern pattern) =>
            pattern switch
            {
                RegionPattern.Smooth => TerrainPatternType.Smooth,
                RegionPattern.Rugged => TerrainPatternType.Rugged,
                RegionPattern.Mountain => TerrainPatternType.Mountain,
                RegionPattern.Canyon => TerrainPatternType.Canyon,
                _ => throw new ArgumentOutOfRangeException(nameof(pattern))
            };

        private int DeriveCandidateSeed(RegionCandidate candidate, string path) =>
            unchecked((int)PatternNoise.Hash(
                candidate.Key,
                ResolveLegacyChannel(path),
                settings.WorldSeed));

        private float ResolveRange(
            RegionCandidate candidate,
            TerrainRangeData range,
            string path)
        {
            var selector = (PatternNoise.Hash(
                    candidate.Key,
                    ResolveLegacyChannel(path),
                    settings.WorldSeed) & 0x00FFFFFFu)
                / 16777215f;
            return range.Minimum + (range.Maximum - range.Minimum) * selector;
        }

        private static int ResolveLegacyChannel(string path) => path switch
        {
            "smooth-warp-x" => 1000,
            "smooth-warp-z" => 1001,
            "smooth-shape" => 1010,
            "smooth-shape-amplitude" => 1020,
            "smooth-detail" => 1030,
            "smooth-detail-amplitude" => 1040,
            "rugged-warp-x" => 2000,
            "rugged-warp-z" => 2001,
            "rugged-shape" => 2010,
            "rugged-shape-amplitude" => 2020,
            "rugged-detail" => 2030,
            "rugged-detail-amplitude" => 2040,
            "mountain-warp-x" => 3000,
            "mountain-warp-z" => 3001,
            "mountain-mass" => 3010,
            "mountain-height" => 3020,
            "mountain-ridge" => 3030,
            "mountain-ridge-strength" => 3040,
            "mountain-detail" => 3050,
            "mountain-detail-amplitude" => 3060,
            "canyon-warp-x" => 4000,
            "canyon-warp-z" => 4001,
            "canyon-basin" => 4010,
            "canyon-basin-ratio" => 4020,
            "canyon-valley" => 4030,
            "canyon-valley-ratio" => 4040,
            "canyon-depth" => 4050,
            "canyon-detail" => 4060,
            "canyon-detail-amplitude" => 4070,
            _ => throw new ArgumentOutOfRangeException(nameof(path))
        };

        private static float NormalizeNoise(float value, PatternNoiseMode mode) =>
            PatternNoise.Normalize(value, mode);

        private static float SampleNormalizedNoise(
            double x,
            double z,
            TerrainNoiseFieldData field,
            int seed) => PatternNoise.SampleNormalized(
            x,
            z,
            field,
            seed);

        private static float SampleSignedNoise(
            double x,
            double z,
            TerrainNoiseFieldData field,
            int seed) => PatternNoise.SampleSigned(x, z, field, seed);

        private static float SampleNoise(
            double x,
            double z,
            TerrainNoiseFieldData field,
            int seed) => PatternNoise.Sample(x, z, field, seed);

        private static bool IsBefore(
            double candidateDistance,
            long candidateX,
            long candidateZ,
            double currentDistance,
            long currentX,
            long currentZ)
        {
            if (candidateDistance != currentDistance)
            {
                return candidateDistance < currentDistance;
            }

            var z = candidateZ.CompareTo(currentZ);
            return z != 0 ? z < 0 : candidateX < currentX;
        }

        private static float SmootherStep(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return value * value * value
                * (value * (value * 6f - 15f) + 10f);
        }

        private static float Lerp(float from, float to, float amount) =>
            from + (to - from) * amount;

        private static int DeriveSeed(int worldSeed, string path) =>
            PatternNoise.DeriveSeed(worldSeed, path);

        private static uint Hash(long x, long z, int seed) =>
            PatternNoise.Hash(x, z, seed);

        private static float Value01(long x, long z, int seed) =>
            PatternNoise.Value01(x, z, seed);

        private static float SignedValue01(long x, long z, int seed) =>
            PatternNoise.SignedValue01(x, z, seed);
    }

    public sealed class TerrainPatternTileBuilder
    {
        private readonly PatternTileGridSettingsData grid;
        private readonly TerrainPatternEvaluator evaluator;

        public TerrainPatternTileBuilder(
            PatternTileGridSettingsData grid,
            TerrainPatternSettingsData settings)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (grid.PatternTileChunkSpan != settings.PatternTileChunkSpan)
            {
                throw new ArgumentException(
                    "Terrain Pattern Tile span and Terrain Pattern settings disagree.",
                    nameof(settings));
            }

            evaluator = new TerrainPatternEvaluator(settings);
        }

        public TerrainPatternTile Build(
            PatternTileKey key,
            CancellationToken cancellationToken = default)
        {
            var bounds = grid.GetCoreBounds(key);
            var sampleWidth = checked(bounds.Width + 2);
            var samples = new TerrainPatternSample[checked(
                sampleWidth * checked(bounds.Height + 2))];
            for (var localZ = -1; localZ <= bounds.Height; localZ++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var localX = -1; localX <= bounds.Width; localX++)
                {
                    samples[ToSampleIndex(localX, localZ, sampleWidth)] =
                        evaluator.EvaluateSample(
                            checked(bounds.MinimumX + localX),
                            checked(bounds.MinimumZ + localZ));
                }
            }

            var cells = new TerrainPatternCell[checked(
                bounds.Width * bounds.Height)];
            for (var localZ = 0; localZ < bounds.Height; localZ++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var localX = 0; localX < bounds.Width; localX++)
                {
                    var center = samples[ToSampleIndex(
                        localX,
                        localZ,
                        sampleWidth)];
                    var slope = TerrainPatternEvaluator.CalculateSlope(
                        samples[ToSampleIndex(localX - 1, localZ, sampleWidth)]
                            .SurfaceHeight,
                        samples[ToSampleIndex(localX + 1, localZ, sampleWidth)]
                            .SurfaceHeight,
                        samples[ToSampleIndex(localX, localZ - 1, sampleWidth)]
                            .SurfaceHeight,
                        samples[ToSampleIndex(localX, localZ + 1, sampleWidth)]
                            .SurfaceHeight);
                    cells[localX + bounds.Width * localZ] = evaluator.ToCell(
                        center,
                        slope);
                }
            }

            return new TerrainPatternTile(key, bounds, cells);
        }

        private static int ToSampleIndex(int localX, int localZ, int sampleWidth) =>
            checked(localX + 1 + sampleWidth * (localZ + 1));
    }
}
