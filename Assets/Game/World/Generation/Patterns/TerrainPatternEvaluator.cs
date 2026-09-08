using System;
using System.Collections.Generic;
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
                float interiorProgress,
                RegionParameters parameters)
            {
                Parameters = parameters;
                GridX = gridX;
                GridZ = gridZ;
                Key = key;
                Pattern = pattern;
                Influence = influence;
                InteriorProgress = interiorProgress;
            }

            public RegionParameters Parameters { get; }
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
        private readonly IElevationPatternMapReader elevation;
        private readonly IClimatePatternMapReader climate;
        private readonly int warpXSeed;
        private readonly int warpZSeed;
        private readonly int regionSeed;
        // Evaluator lifetime is one tile build; workers never share mutable caches.
        private readonly Dictionary<(long X, long Z), RegionParameters> regionParameters = new();
        private readonly RegionCenter[] regionCenters;
        private readonly double[] regionDistances;
        private readonly int regionSearchRadius;
        private double nearestRegionDistance;
        private bool hasRegionCenters;
        private long centerGridX;
        private long centerGridZ;

        public TerrainPatternEvaluator(TerrainPatternSettingsData settings, IElevationPatternMapReader elevation = null, IClimatePatternMapReader climate = null)
        {
            this.settings = settings
                ?? throw new ArgumentNullException(nameof(settings));
            this.elevation = elevation ?? new ElevationPatternEvaluator(new ElevationPatternSettingsData(settings));
            this.climate = climate;
            warpXSeed = DeriveSeed(settings.WorldSeed, "world-router-pattern-warp-x");
            warpZSeed = DeriveSeed(settings.WorldSeed, "world-router-pattern-warp-z");
            regionSeed = DeriveSeed(settings.WorldSeed, "world-router-pattern-region");
            // A center in the sample's own lattice cell is at most sqrt(2)*(0.5+jitter)
            // cells away. Include every center within that bound + the blend support.
            // Also cover the second nearest center, used by the existing Sea metadata.
            var jitter = (double)settings.Region.CenterJitter;
            var nearestBound = Math.Sqrt(2d) * (0.5d + jitter);
            var secondBound = Math.Sqrt(
                (1.5d + jitter) * (1.5d + jitter)
                + (0.5d + jitter) * (0.5d + jitter));
            var reach = Math.Max(secondBound, nearestBound
                + 2d * settings.Region.BoundaryBlendCells / settings.Region.SizeCells);
            regionSearchRadius = checked((int)Math.Ceiling(reach + jitter + 0.5d));
            var diameter = checked(regionSearchRadius * 2 + 1);
            regionCenters = new RegionCenter[checked(diameter * diameter)];
            regionDistances = new double[regionCenters.Length];
        }

        internal TerrainPatternSample EvaluateSample(double worldX, double worldZ)
        {
            var baseSurface = elevation.GetHeight(worldX, worldZ);
            if (baseSurface < elevation.SeaLevel)
                return new TerrainPatternSample(TerrainPatternType.Smooth, baseSurface, 0, true, 0, 1, false, 0, 0, 1, baseSurface, baseSurface);
            var region = SampleRegion(worldX, worldZ);
            var blended = SampleBlendedContribution(worldX, worldZ);
            float distance = baseSurface - elevation.SeaLevel;
            float fade = Math.Clamp(distance / 25f, 0f, 1f);
            fade = fade * fade * (3f - 2f * fade);
            float delta = Math.Max(-distance, (blended.BaseHeight + blended.DetailHeight) * fade);
            return new TerrainPatternSample(ResolveTerrainType(region), baseSurface + delta, 0,
                false, region.Primary.Key, region.Primary.InteriorProgress, false, region.Secondary.Key, 0, region.Primary.Influence, baseSurface + delta, baseSurface + delta);
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

        private RegionSample SampleRegion(double worldX, double worldZ)
        {
            var regionSettings = settings.Region;
            var warpX = regionSettings.WarpStrengthCells == 0f ? 0f : SampleSignedNoise(
                    worldX,
                    worldZ,
                    regionSettings.WarpField,
                    warpXSeed)
                * regionSettings.WarpStrengthCells;
            var warpZ = regionSettings.WarpStrengthCells == 0f ? 0f : SampleSignedNoise(
                    worldX,
                    worldZ,
                    regionSettings.WarpField,
                    warpZSeed)
                * regionSettings.WarpStrengthCells;
            var sampleX = worldX + warpX;
            var sampleZ = worldZ + warpZ;
            var gridX = (long)Math.Floor(sampleX / regionSettings.SizeCells);
            var gridZ = (long)Math.Floor(sampleZ / regionSettings.SizeCells);
            var nearestDistance = double.PositiveInfinity;
            var secondDistance = double.PositiveInfinity;
            var nearestGridX = 0L;
            var nearestGridZ = 0L;
            var secondGridX = 0L;
            var secondGridZ = 0L;

            PrepareRegionCenters(gridX, gridZ);
            for (var index = 0; index < regionCenters.Length; index++)
            {
                var center = regionCenters[index];
                var deltaX = sampleX - center.X;
                var deltaZ = sampleZ - center.Z;
                var distance = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
                regionDistances[index] = distance;
                if (distance < nearestDistance)
                {
                    secondDistance = nearestDistance;
                    secondGridX = nearestGridX;
                    secondGridZ = nearestGridZ;
                    nearestDistance = distance;
                    nearestGridX = center.GridX;
                    nearestGridZ = center.GridZ;
                }
                else if (distance < secondDistance)
                {
                    secondDistance = distance;
                    secondGridX = center.GridX;
                    secondGridZ = center.GridZ;
                }
            }

            nearestRegionDistance = nearestDistance;
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

        private void PrepareRegionCenters(long gridX, long gridZ)
        {
            if (hasRegionCenters && centerGridX == gridX && centerGridZ == gridZ)
                return;
            var region = settings.Region;
            var index = 0;
            for (var offsetZ = -regionSearchRadius; offsetZ <= regionSearchRadius; offsetZ++)
            for (var offsetX = -regionSearchRadius; offsetX <= regionSearchRadius; offsetX++)
            {
                var x = checked(gridX + offsetX);
                var z = checked(gridZ + offsetZ);
                regionCenters[index++] = new RegionCenter(
                    x, z,
                    (x + 0.5) * region.SizeCells
                        + SignedValue01(x, z, unchecked(regionSeed + 101))
                        * region.CenterJitter * region.SizeCells,
                    (z + 0.5) * region.SizeCells
                        + SignedValue01(x, z, unchecked(regionSeed + 211))
                        * region.CenterJitter * region.SizeCells);
            }
            centerGridX = gridX;
            centerGridZ = gridZ;
            hasRegionCenters = true;
        }

        private TerrainContribution SampleBlendedContribution(double worldX, double worldZ)
        {
            double totalWeight = 0d, baseHeight = 0d, detailHeight = 0d;
            // Fixed absolute Z/X order makes sums independent of tile/cache traversal.
            for (var index = 0; index < regionCenters.Length; index++)
            {
                var weight = RegionBlendWeight(
                    regionDistances[index], nearestRegionDistance,
                    settings.Region.BoundaryBlendCells);
                if (weight == 0d) continue;
                var center = regionCenters[index];
                var contribution = SampleContribution(
                    CreateCandidate(center.GridX, center.GridZ, 0f, 0f), worldX, worldZ);
                totalWeight += weight;
                baseHeight += contribution.BaseHeight * weight;
                detailHeight += contribution.DetailHeight * weight;
            }

            // The nearest center always has weight 1, so the denominator is nonzero.
            return new TerrainContribution(
                (float)(baseHeight / totalWeight), (float)(detailHeight / totalWeight));
        }

        internal static double RegionBlendWeight(
            double distance, double nearestDistance, double blendWidth)
        {
            var t = Math.Clamp(1d - (distance - nearestDistance) / (2d * blendWidth), 0d, 1d);
            return t * t * t * (t * (t * 6d - 15d) + 10d);
        }

        private readonly struct RegionCenter
        {
            public RegionCenter(long gridX, long gridZ, double x, double z)
            {
                GridX = gridX;
                GridZ = gridZ;
                X = x;
                Z = z;
            }

            public long GridX { get; }
            public long GridZ { get; }
            public double X { get; }
            public double Z { get; }
        }

        private TerrainContribution SampleContribution(
            RegionCandidate candidate,
            double worldX,
            double worldZ)
        {
            return candidate.Pattern switch
            {
                RegionPattern.Smooth => SampleSurfaceForm(
                    candidate,
                    worldX,
                    worldZ,
                    settings.Smooth,
                    1000),
                RegionPattern.Rugged => SampleSurfaceForm(
                    candidate,
                    worldX,
                    worldZ,
                    settings.Rugged,
                    2000),
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
            double worldX,
            double worldZ,
            TerrainSurfaceFormData form,
            int channelBase)
        {
            Warp(
                candidate,
                worldX,
                worldZ,
                form.DomainWarp,
                channelBase,
                out var sampleX,
                out var sampleZ);
            var shape = form.ShapeResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.ShapeField,
                    DeriveCandidateSeed(candidate, channelBase + 10)))
                * ResolveRange(candidate, channelBase + 20);
            var detail = SampleSignedNoise(
                    sampleX,
                    sampleZ,
                    form.DetailField,
                    DeriveCandidateSeed(candidate, channelBase + 30))
                * ResolveRange(candidate, channelBase + 40);
            return new TerrainContribution(shape, detail);
        }

        private TerrainContribution SampleMountain(
            RegionCandidate candidate,
            double worldX,
            double worldZ)
        {
            var form = settings.Mountain;
            Warp(
                candidate,
                worldX,
                worldZ,
                form.DomainWarp,
                3000,
                out var sampleX,
                out var sampleZ);
            var ridge = form.RidgeResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.RidgeField,
                    DeriveCandidateSeed(candidate, 3030)))
                * ResolveRange(candidate, 3040);
            var detail = SampleSignedNoise(
                    sampleX,
                    sampleZ,
                    form.DetailField,
                    DeriveCandidateSeed(candidate, 3050))
                * ResolveRange(candidate, 3060);
            return new TerrainContribution(ridge, detail);
        }

        private TerrainContribution SampleCanyon(
            RegionCandidate candidate,
            double worldX,
            double worldZ)
        {
            var form = settings.Canyon;
            Warp(
                candidate,
                worldX,
                worldZ,
                form.DomainWarp,
                4000,
                out var sampleX,
                out var sampleZ);
            var basin = form.BasinResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.BasinField,
                    DeriveCandidateSeed(candidate, 4010)))
                * ResolveRange(candidate, 4020);
            var valley = form.ValleyResponse.Evaluate(SampleNormalizedNoise(
                    sampleX,
                    sampleZ,
                    form.ValleyField,
                    DeriveCandidateSeed(candidate, 4030)))
                * ResolveRange(candidate, 4040);
            var depthProgress = Math.Clamp(
                1f - (1f - basin) * (1f - valley),
                0f,
                1f);
            var depth = depthProgress * ResolveRange(candidate, 4050);
            var detail = SampleSignedNoise(
                    sampleX,
                    sampleZ,
                    form.DetailField,
                    DeriveCandidateSeed(candidate, 4060))
                * ResolveRange(candidate, 4070);
            return new TerrainContribution(-depth, detail);
        }

        private void Warp(
            RegionCandidate candidate,
            double worldX,
            double worldZ,
            TerrainDomainWarpData warp,
            int channelBase,
            out double sampleX,
            out double sampleZ)
        {
            if (warp.StrengthCells == 0f)
            {
                sampleX = worldX;
                sampleZ = worldZ;
                return;
            }
            sampleX = worldX + SampleSignedNoise(
                worldX,
                worldZ,
                warp.Field,
                DeriveCandidateSeed(candidate, channelBase))
                * warp.StrengthCells;
            sampleZ = worldZ + SampleSignedNoise(
                worldX,
                worldZ,
                warp.Field,
                DeriveCandidateSeed(candidate, channelBase + 1))
                * warp.StrengthCells;
        }

        private RegionCandidate CreateCandidate(
            long gridX,
            long gridZ,
            float influence,
            float interiorProgress)
        {
            var hash = Hash(gridX, gridZ, regionSeed);
            var key = unchecked((int)hash);
            if (!regionParameters.TryGetValue((gridX, gridZ), out var parameters))
            {
                var pattern = SelectRegionPattern(gridX, gridZ);
                parameters = new RegionParameters(key, pattern, settings);
                regionParameters.Add((gridX, gridZ), parameters);
            }
            return new RegionCandidate(
                gridX,
                gridZ,
                unchecked((int)hash),
                parameters.Pattern,
                influence,
                interiorProgress,
                parameters);
        }

        private RegionPattern SelectRegionPattern(long gridX, long gridZ)
        {
            // One immutable climate choice at the region's absolute lattice center.
            int anchorX = (int)Math.Clamp(Math.Floor((gridX + 0.5) * settings.Region.SizeCells), int.MinValue, int.MaxValue);
            int anchorZ = (int)Math.Clamp(Math.Floor((gridZ + 0.5) * settings.Region.SizeCells), int.MinValue, int.MaxValue);
            var rule = climate?.GetTerrainRule(anchorX, anchorZ) ?? BiomeTerrainRule.Default;
            double smooth = (double)settings.Region.SmoothShare * rule.Smooth;
            double rugged = (double)settings.Region.RuggedShare * rule.Rugged;
            double mountain = (double)settings.Region.MountainShare * rule.Mountain;
            double canyon = (double)settings.Region.CanyonShare * rule.Canyon;
            double total = smooth + rugged + mountain + canyon;
            if (total <= 0) throw new InvalidOperationException("Climate and Terrain weights leave no land pattern available.");
            // A separate channel avoids correlation with Elevation's sea ownership decision.
            double selector = PatternNoise.Value01(gridX, gridZ, DeriveSeed(settings.WorldSeed, "climate-terrain-choice")) * total;
            if (selector < smooth) return RegionPattern.Smooth;
            if ((selector -= smooth) < rugged) return RegionPattern.Rugged;
            if ((selector -= rugged) < mountain) return RegionPattern.Mountain;
            // Value01 can equal one: select the last nonzero weight, never a disabled pattern.
            if (canyon > 0) return RegionPattern.Canyon;
            if (mountain > 0) return RegionPattern.Mountain;
            if (rugged > 0) return RegionPattern.Rugged;
            return RegionPattern.Smooth;
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

        private static int DeriveCandidateSeed(RegionCandidate candidate, int channel) =>
            candidate.Parameters.Seeds[RegionParameters.Index(channel)];

        private static float ResolveRange(
            RegionCandidate candidate, int channel) =>
            candidate.Parameters.Ranges[RegionParameters.Index(channel)];

        private sealed class RegionParameters
        {
            public readonly RegionPattern Pattern;
            public readonly int[] Seeds = new int[9];
            public readonly float[] Ranges = new float[9];

            public RegionParameters(int key, RegionPattern pattern, TerrainPatternSettingsData settings)
            {
                Pattern = pattern;
                if (pattern == RegionPattern.Sea) return;
                var channelBase = ((int)pattern + 1) * 1000;
                for (var index = 0; index < Seeds.Length; index++)
                {
                    var channel = channelBase + (index < 2 ? index : (index - 1) * 10);
                    Seeds[index] = unchecked((int)PatternNoise.Hash(key, channel, settings.WorldSeed));
                }
                switch (pattern)
                {
                    case RegionPattern.Smooth:
                        SetRange(1020, settings.Smooth.ShapeAmplitude);
                        SetRange(1040, settings.Smooth.DetailAmplitude);
                        break;
                    case RegionPattern.Rugged:
                        SetRange(2020, settings.Rugged.ShapeAmplitude);
                        SetRange(2040, settings.Rugged.DetailAmplitude);
                        break;
                    case RegionPattern.Mountain:
                        SetRange(3040, settings.Mountain.RidgeStrength);
                        SetRange(3060, settings.Mountain.DetailAmplitude);
                        break;
                    case RegionPattern.Canyon:
                        SetRange(4020, settings.Canyon.BasinDepthRatio);
                        SetRange(4040, settings.Canyon.ValleyDepthRatio);
                        SetRange(4050, settings.Canyon.Depth);
                        SetRange(4070, settings.Canyon.DetailAmplitude);
                        break;
                }
            }

            public static int Index(int channel)
            {
                var offset = channel % 1000;
                return offset < 2 ? offset : offset / 10 + 1;
            }

            private void SetRange(int channel, TerrainRangeData range)
            {
                var index = Index(channel);
                var selector = ((uint)Seeds[index] & 0x00FFFFFFu) / 16777215f;
                Ranges[index] = range.Minimum + (range.Maximum - range.Minimum) * selector;
            }
        }

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
        private readonly TerrainPatternSettingsData settings;
        private readonly IElevationPatternMapReader elevation;
        private readonly ClimatePatternMapReader climate;

        public TerrainPatternTileBuilder(
            PatternTileGridSettingsData grid,
            TerrainPatternSettingsData settings, IElevationPatternMapReader elevation = null, ClimatePatternMapReader climate = null)
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

            this.settings = settings;
            this.elevation = elevation;
            this.climate = climate;
        }

        public TerrainPatternTile Build(
            PatternTileKey key,
            CancellationToken cancellationToken = default)
        {
            climate?.Build(key, cancellationToken);
            var scopedElevation = elevation is ElevationPatternMapReader map ? map.CreateReadScope() : elevation;
            var evaluator = new TerrainPatternEvaluator(settings, scopedElevation, climate);
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
