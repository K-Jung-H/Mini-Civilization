using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    public sealed class WorldBuildInput
    {
        private WorldBuildInput(WorldGenerationSettings settings, int seed)
        {
            Settings = settings.CreateData(seed);
        }

        public WorldSettingsData Settings { get; }
        public int Seed => Settings.Seed;

        public static WorldBuildInput Create(
            WorldGenerationSettings settings,
            int seed)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (!settings.TryValidate(out var error))
            {
                throw new InvalidOperationException(error);
            }

            return new WorldBuildInput(settings, seed);
        }

        internal WorldChunkBuildInput CreateChunkInput(
            ChunkCoordinate coordinate) =>
            WorldChunkBuildInput.Create(Settings, coordinate);
    }

    internal sealed class WorldChunkBuildInput
    {
        private WorldChunkBuildInput(
            WorldSettingsData settings,
            ChunkCoordinate coordinate)
        {
            Settings = settings;
            Coordinate = coordinate;
            OriginX = checked(coordinate.X * settings.ChunkCellCountXZ);
            OriginZ = checked(coordinate.Z * settings.ChunkCellCountXZ);
        }

        public WorldSettingsData Settings { get; }
        public ChunkCoordinate Coordinate { get; }
        public int ChunkSizeXZ => Settings.ChunkCellCountXZ;
        public int WorldHeight => Settings.WorldHeight;
        public int HeightUnitCount => checked(
            WorldHeight * WorldGrid.HeightStepsPerCell);
        public int OriginX { get; }
        public int OriginZ { get; }

        public static WorldChunkBuildInput Create(
            WorldSettingsData settings,
            ChunkCoordinate coordinate)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.WorldType == WorldType.Finite
                && (coordinate.X < settings.MinimumChunkCoordinate
                    || coordinate.X > settings.MaximumChunkCoordinate
                    || coordinate.Z < settings.MinimumChunkCoordinate
                    || coordinate.Z > settings.MaximumChunkCoordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }

            return new WorldChunkBuildInput(settings, coordinate);
        }

        public int ToWorldX(int localX)
        {
            ValidateLocalCoordinate(localX, nameof(localX));
            return checked(OriginX + localX);
        }

        public int ToWorldZ(int localZ)
        {
            ValidateLocalCoordinate(localZ, nameof(localZ));
            return checked(OriginZ + localZ);
        }

        private void ValidateLocalCoordinate(int value, string parameterName)
        {
            if ((uint)value >= ChunkSizeXZ)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }

    internal readonly struct WorldFieldSample
    {
        public WorldFieldSample(
            float continentalness,
            float erosion,
            float weirdness,
            float peaksValleys,
            float roughness,
            float detail,
            float seaDetail)
        {
            Continentalness = continentalness;
            Erosion = erosion;
            Weirdness = weirdness;
            PeaksValleys = peaksValleys;
            Roughness = roughness;
            Detail = detail;
            SeaDetail = seaDetail;
        }

        public float Continentalness { get; }
        public float Erosion { get; }
        public float Weirdness { get; }
        public float PeaksValleys { get; }
        public float Roughness { get; }
        public float Detail { get; }
        public float SeaDetail { get; }
    }

    internal enum WorldPatternType : byte
    {
        Smooth,
        Rugged,
        Mountain,
        Canyon,
        Sea
    }

    internal readonly struct WorldPatternRegionCandidate
    {
        public WorldPatternRegionCandidate(
            int regionKey,
            WorldPatternType patternType,
            float influence,
            float interiorProgress,
            double localX,
            double localZ,
            int sizeCells)
        {
            RegionKey = regionKey;
            PatternType = patternType;
            Influence = influence;
            InteriorProgress = interiorProgress;
            LocalX = localX;
            LocalZ = localZ;
            SizeCells = sizeCells;
        }

        public int RegionKey { get; }
        public WorldPatternType PatternType { get; }
        public float Influence { get; }
        public float InteriorProgress { get; }
        public double LocalX { get; }
        public double LocalZ { get; }
        public int SizeCells { get; }
    }

    internal readonly struct WorldPatternRegionSample
    {
        public WorldPatternRegionSample(
            in WorldPatternRegionCandidate primary,
            in WorldPatternRegionCandidate secondary)
        {
            Primary = primary;
            Secondary = secondary;
        }

        public WorldPatternRegionCandidate Primary { get; }
        public WorldPatternRegionCandidate Secondary { get; }
    }

    internal readonly struct WorldPatternResult
    {
        public WorldPatternResult(
            float surfaceOffsetUnits,
            float verticalFactor,
            float detailUnits,
            WorldPatternType dominantPattern,
            int regionKey,
            float interiorProgress,
            float patternDepthUnits,
            float patternDepthProgress,
            float patternDetailUnits,
            int waterTopUnits,
            WaterType waterType)
        {
            SurfaceOffsetUnits = surfaceOffsetUnits;
            VerticalFactor = verticalFactor;
            DetailUnits = detailUnits;
            DominantPattern = dominantPattern;
            RegionKey = regionKey;
            InteriorProgress = interiorProgress;
            PatternDepthUnits = patternDepthUnits;
            PatternDepthProgress = patternDepthProgress;
            PatternDetailUnits = patternDetailUnits;
            WaterTopUnits = waterTopUnits;
            WaterType = waterType;
        }

        public float SurfaceOffsetUnits { get; }
        public float VerticalFactor { get; }
        public float DetailUnits { get; }
        public WorldPatternType DominantPattern { get; }
        public int RegionKey { get; }
        public float InteriorProgress { get; }
        public float PatternDepthUnits { get; }
        public float PatternDepthProgress { get; }
        public float PatternDetailUnits { get; }
        public int WaterTopUnits { get; }
        public WaterType WaterType { get; }
        public bool HasWaterPattern => WaterType != WaterType.None;
    }

    internal readonly struct WorldPatternCandidateResult
    {
        public WorldPatternCandidateResult(
            float surfaceUnits,
            float detailUnits,
            float depthUnits,
            float depthProgress,
            float patternDetailUnits,
            int waterTopUnits,
            WaterType waterType)
        {
            SurfaceUnits = surfaceUnits;
            DetailUnits = detailUnits;
            DepthUnits = depthUnits;
            DepthProgress = depthProgress;
            PatternDetailUnits = patternDetailUnits;
            WaterTopUnits = waterTopUnits;
            WaterType = waterType;
        }

        public float SurfaceUnits { get; }
        public float DetailUnits { get; }
        public float DepthUnits { get; }
        public float DepthProgress { get; }
        public float PatternDetailUnits { get; }
        public int WaterTopUnits { get; }
        public WaterType WaterType { get; }
    }

    internal readonly struct TerrainPatternContribution
    {
        public TerrainPatternContribution(
            float surfaceOffsetUnits,
            float detailUnits)
        {
            SurfaceOffsetUnits = surfaceOffsetUnits;
            DetailUnits = detailUnits;
        }

        public float SurfaceOffsetUnits { get; }
        public float DetailUnits { get; }
    }

    internal readonly struct WorldPatternWeights
    {
        public WorldPatternWeights(
            float smooth,
            float rugged,
            float mountain,
            float canyon,
            float sea)
        {
            Smooth = smooth;
            Rugged = rugged;
            Mountain = mountain;
            Canyon = canyon;
            Sea = sea;
        }

        public float Smooth { get; }
        public float Rugged { get; }
        public float Mountain { get; }
        public float Canyon { get; }
        public float Sea { get; }
    }

    internal readonly struct WorldDensityContributions
    {
        public WorldDensityContributions(
            float verticalGradient,
            float surfaceOffset,
            float surfaceDetail,
            float densityDetail)
        {
            VerticalGradient = verticalGradient;
            SurfaceOffset = surfaceOffset;
            SurfaceDetail = surfaceDetail;
            DensityDetail = densityDetail;
        }

        public float VerticalGradient { get; }
        public float SurfaceOffset { get; }
        public float SurfaceDetail { get; }
        public float DensityDetail { get; }
        public float Total => VerticalGradient
            + SurfaceOffset
            + SurfaceDetail
            + DensityDetail;
    }

    internal sealed class GenerationWorkingData
    {
        private WorldFieldSample[] worldFieldSamples;
        private WorldPatternResult[] worldPatternResults;
        private float[] worldDensity;
        private float[] finalSurfaceUnits;
        private WorldColumnBuildData[] finalColumns;
        private bool[] finalColumnWritten;

        public GenerationWorkingData(
            WorldChunkBuildInput input,
            int haloCellCount)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            if (haloCellCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(haloCellCount));
            }

            HaloCellCount = haloCellCount;
            SampleOriginX = checked(input.OriginX - haloCellCount);
            SampleOriginZ = checked(input.OriginZ - haloCellCount);
            SampleSizeXZ = checked(input.ChunkSizeXZ + haloCellCount * 2);
            worldFieldSamples = new WorldFieldSample[checked(
                SampleSizeXZ * SampleSizeXZ)];
            worldPatternResults = new WorldPatternResult[
                worldFieldSamples.Length];
            worldDensity = new float[checked(
                SampleSizeXZ
                * SampleSizeXZ
                * DensityHeightUnitCount)];
            finalSurfaceUnits = new float[worldFieldSamples.Length];
            finalColumns = new WorldColumnBuildData[
                checked(input.ChunkSizeXZ * input.ChunkSizeXZ)];
            finalColumnWritten = new bool[finalColumns.Length];
        }

        public WorldChunkBuildInput Input { get; }
        public int HaloCellCount { get; }
        public int SampleOriginX { get; }
        public int SampleOriginZ { get; }
        public int SampleSizeXZ { get; }
        public int DensityHeightUnitCount => checked(Input.HeightUnitCount + 1);
        public bool HasWorldField { get; private set; }
        public bool HasWorldPatternResult { get; private set; }
        public bool HasWorldDensity { get; private set; }
        public bool HasFinalSurface { get; private set; }
        public bool IsCompleted => finalColumns == null;

        public void SetWorldField(
            int sampleLocalX,
            int sampleLocalZ,
            in WorldFieldSample value)
        {
            EnsureNotCompleted();
            if (HasWorldField)
            {
                throw new InvalidOperationException(
                    "World Field has already been finalized.");
            }

            worldFieldSamples[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)] =
                value;
        }

        public void CompleteWorldField()
        {
            EnsureNotCompleted();
            if (HasWorldField)
            {
                throw new InvalidOperationException(
                    "World Field has already been finalized.");
            }

            HasWorldField = true;
        }

        public WorldFieldSample GetWorldField(
            int sampleLocalX,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasWorldField)
            {
                throw new InvalidOperationException(
                    "World Field is not ready.");
            }

            return worldFieldSamples[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)];
        }

        public void SetWorldPatternResult(
            int sampleLocalX,
            int sampleLocalZ,
            in WorldPatternResult value)
        {
            EnsureNotCompleted();
            if (!HasWorldField)
            {
                throw new InvalidOperationException(
                    "World Field must be ready before its Pattern Result is built.");
            }

            if (HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result has already been finalized.");
            }

            worldPatternResults[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)] = value;
        }

        public void CompleteWorldPatternResult()
        {
            EnsureNotCompleted();
            if (!HasWorldField)
            {
                throw new InvalidOperationException(
                    "World Field must be ready before its Pattern Result is finalized.");
            }

            if (HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result has already been finalized.");
            }

            HasWorldPatternResult = true;
        }

        public WorldPatternResult GetWorldPatternResult(
            int sampleLocalX,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result is not ready.");
            }

            return worldPatternResults[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)];
        }

        public void SetWorldDensity(
            int sampleLocalX,
            int heightUnit,
            int sampleLocalZ,
            float density)
        {
            EnsureNotCompleted();
            if (!HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result must be ready before World Density is built.");
            }

            if (HasWorldDensity)
            {
                throw new InvalidOperationException(
                    "World Density has already been finalized.");
            }

            if (!float.IsFinite(density))
            {
                throw new ArgumentOutOfRangeException(nameof(density));
            }

            worldDensity[ToDensityIndex(
                sampleLocalX,
                heightUnit,
                sampleLocalZ)] = density;
        }

        public void CompleteWorldDensity()
        {
            EnsureNotCompleted();
            if (!HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result must be ready before World Density is finalized.");
            }

            if (HasWorldDensity)
            {
                throw new InvalidOperationException(
                    "World Density has already been finalized.");
            }

            HasWorldDensity = true;
        }

        public float GetWorldDensity(
            int sampleLocalX,
            int heightUnit,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasWorldDensity)
            {
                throw new InvalidOperationException(
                    "World Density is not ready.");
            }

            return worldDensity[ToDensityIndex(
                sampleLocalX,
                heightUnit,
                sampleLocalZ)];
        }

        public void SetFinalSurfaceUnits(
            int sampleLocalX,
            int sampleLocalZ,
            float surfaceUnits)
        {
            EnsureNotCompleted();
            if (!HasWorldDensity)
            {
                throw new InvalidOperationException(
                    "World Density must be ready before Final Surface is extracted.");
            }

            if (HasFinalSurface)
            {
                throw new InvalidOperationException(
                    "Final Surface has already been finalized.");
            }

            if (!float.IsFinite(surfaceUnits)
                || surfaceUnits < 0f
                || surfaceUnits > Input.HeightUnitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(surfaceUnits));
            }

            finalSurfaceUnits[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)] = surfaceUnits;
        }

        public void CompleteFinalSurface()
        {
            EnsureNotCompleted();
            if (!HasWorldDensity)
            {
                throw new InvalidOperationException(
                    "World Density must be ready before Final Surface is finalized.");
            }

            if (HasFinalSurface)
            {
                throw new InvalidOperationException(
                    "Final Surface has already been finalized.");
            }

            HasFinalSurface = true;
        }

        public float GetFinalSurfaceUnits(
            int sampleLocalX,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasFinalSurface)
            {
                throw new InvalidOperationException(
                    "Final Surface is not ready.");
            }

            return finalSurfaceUnits[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)];
        }

        public void SetFinalColumn(
            int localX,
            int localZ,
            in WorldColumnBuildData column)
        {
            EnsureNotCompleted();
            if ((uint)localX >= Input.ChunkSizeXZ)
            {
                throw new ArgumentOutOfRangeException(nameof(localX));
            }

            if ((uint)localZ >= Input.ChunkSizeXZ)
            {
                throw new ArgumentOutOfRangeException(nameof(localZ));
            }

            var index = localX + Input.ChunkSizeXZ * localZ;
            finalColumns[index] = column;
            finalColumnWritten[index] = true;
        }

        public WorldChunkBuildData Complete()
        {
            EnsureNotCompleted();
            if (!HasWorldField
                || !HasWorldPatternResult
                || !HasWorldDensity)
            {
                throw new InvalidOperationException(
                    "World Field, World Pattern Result, and World Density must be ready before final output is transferred.");
            }

            if (!HasFinalSurface)
            {
                throw new InvalidOperationException(
                    "Final Surface must be ready before output is transferred.");
            }

            for (var index = 0; index < finalColumnWritten.Length; index++)
            {
                if (!finalColumnWritten[index])
                {
                    throw new InvalidOperationException(
                        "Every Chunk Column must be explicitly finalized before output is transferred.");
                }
            }

            var columns = finalColumns;
            worldFieldSamples = null;
            worldPatternResults = null;
            worldDensity = null;
            finalSurfaceUnits = null;
            finalColumns = null;
            finalColumnWritten = null;
            return new WorldChunkBuildData(Input, columns);
        }

        private void EnsureNotCompleted()
        {
            if (IsCompleted)
            {
                throw new InvalidOperationException(
                    "Generation working data has already transferred its final output.");
            }
        }

        private int ToDensityIndex(
            int sampleLocalX,
            int heightUnit,
            int sampleLocalZ)
        {
            if ((uint)sampleLocalX >= SampleSizeXZ)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleLocalX));
            }

            if ((uint)sampleLocalZ >= SampleSizeXZ)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleLocalZ));
            }

            if ((uint)heightUnit >= DensityHeightUnitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(heightUnit));
            }

            return checked(
                heightUnit
                + DensityHeightUnitCount
                * (sampleLocalX + SampleSizeXZ * sampleLocalZ));
        }

        private int ToSurfaceIndex(int sampleLocalX, int sampleLocalZ)
        {
            if ((uint)sampleLocalX >= SampleSizeXZ)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleLocalX));
            }

            if ((uint)sampleLocalZ >= SampleSizeXZ)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleLocalZ));
            }

            return checked(sampleLocalX + SampleSizeXZ * sampleLocalZ);
        }
    }

    internal static class WorldFieldStage
    {
        public const int RequiredHaloCellCount = 1;

        public static GenerationWorkingData Build(WorldChunkBuildInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var working = new GenerationWorkingData(
                input,
                RequiredHaloCellCount);
            var router = new WorldNoiseRouter(input.Settings);
            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                var worldX = checked(working.SampleOriginX + sampleLocalX);
                var worldZ = checked(working.SampleOriginZ + sampleLocalZ);
                working.SetWorldField(
                    sampleLocalX,
                    sampleLocalZ,
                    router.Sample(worldX, worldZ));
            }

            working.CompleteWorldField();
            return working;
        }
    }

    internal static class WorldNoiseFieldSampler
    {
        public static float Sample2D(
            long worldX,
            long worldZ,
            in WorldNoiseFieldSettingsData field,
            int seed)
        {
            var sample = field.Mode is WorldNoiseMode.Ridge
                or WorldNoiseMode.SignedRidge
                ? DeterministicNoise.RidgedFractalNoise(
                    worldX * field.Scale,
                    worldZ * field.Scale,
                    seed,
                    field.Layers,
                    field.FrequencySpacing,
                    field.Persistence)
                : DeterministicNoise.FractalNoise(
                    worldX * field.Scale,
                    worldZ * field.Scale,
                    seed,
                    field.Layers,
                    field.FrequencySpacing,
                    field.Persistence);
            return field.Mode is WorldNoiseMode.Signed
                or WorldNoiseMode.SignedRidge
                ? sample * 2f - 1f
                : sample;
        }

        public static float Sample3D(
            double worldX,
            double worldY,
            double worldZ,
            in WorldNoiseFieldSettingsData field,
            int seed)
        {
            var sample = field.Mode is WorldNoiseMode.Ridge
                or WorldNoiseMode.SignedRidge
                ? DeterministicNoise.RidgedFractalNoise(
                    worldX * field.Scale,
                    worldY * field.Scale,
                    worldZ * field.Scale,
                    seed,
                    field.Layers,
                    field.FrequencySpacing,
                    field.Persistence)
                : DeterministicNoise.FractalNoise(
                    worldX * field.Scale,
                    worldY * field.Scale,
                    worldZ * field.Scale,
                    seed,
                    field.Layers,
                    field.FrequencySpacing,
                    field.Persistence);
            return field.Mode is WorldNoiseMode.Signed
                or WorldNoiseMode.SignedRidge
                ? sample * 2f - 1f
                : sample;
        }

    }

    internal readonly struct WorldNoiseRouter
    {
        private readonly int continentalSeed;
        private readonly int erosionSeed;
        private readonly int weirdnessSeed;
        private readonly int peaksValleysSeed;
        private readonly int roughnessSeed;
        private readonly int detailSeed;
        private readonly int seaDetailSeed;
        private readonly int regionSeed;
        private readonly int regionWarpXSeed;
        private readonly int regionWarpZSeed;
        private readonly WorldNoiseRouterSettingsData settings;
        private readonly WorldPatternRegionSettingsData regionSettings;

        public WorldNoiseRouter(WorldSettingsData worldSettings)
        {
            continentalSeed = Derive(worldSettings.Seed, "continentalness");
            erosionSeed = Derive(worldSettings.Seed, "erosion");
            weirdnessSeed = Derive(worldSettings.Seed, "weirdness");
            peaksValleysSeed = Derive(worldSettings.Seed, "peaks-valleys");
            roughnessSeed = Derive(worldSettings.Seed, "roughness");
            detailSeed = Derive(worldSettings.Seed, "detail");
            seaDetailSeed = Derive(worldSettings.Seed, "sea-detail");
            regionSeed = Derive(worldSettings.Seed, "pattern-region");
            regionWarpXSeed = Derive(worldSettings.Seed, "pattern-warp-x");
            regionWarpZSeed = Derive(worldSettings.Seed, "pattern-warp-z");
            settings = worldSettings.WorldNoiseRouter;
            regionSettings = worldSettings.WorldPatterns.Region;
        }

        public WorldFieldSample Sample(int worldX, int worldZ) => new(
            Sample2D(worldX, worldZ, settings.Continentalness, continentalSeed),
            Sample2D(worldX, worldZ, settings.Erosion, erosionSeed),
            Sample2D(worldX, worldZ, settings.Weirdness, weirdnessSeed),
            Sample2D(worldX, worldZ, settings.PeaksValleys, peaksValleysSeed),
            Sample2D(worldX, worldZ, settings.Roughness, roughnessSeed),
            Sample2D(worldX, worldZ, settings.Detail, detailSeed),
            Sample2D(worldX, worldZ, settings.SeaDetail, seaDetailSeed));

        public WorldPatternRegionSample SampleRegion(int worldX, int worldZ) =>
            WorldPatternRegionSampler.Sample(
                worldX,
                worldZ,
                regionSeed,
                regionWarpXSeed,
                regionWarpZSeed,
                regionSettings);

        public float SampleDetail3D(
            double worldX,
            double worldY,
            double worldZ) => WorldNoiseFieldSampler.Sample3D(
                worldX,
                worldY,
                worldZ,
                settings.Detail,
                detailSeed);

        private static int Derive(int worldSeed, string channel) =>
            DeterministicNoise.DeriveSeed(worldSeed, "world-router-" + channel);

        private static float Sample2D(
            int worldX,
            int worldZ,
            in WorldNoiseFieldSettingsData field,
            int seed) => WorldNoiseFieldSampler.Sample2D(
                worldX,
                worldZ,
                field,
                seed);
    }

    internal static class WorldPatternRegionSampler
    {
        private readonly struct RawRegion
        {
            public RawRegion(
                long gridX,
                long gridZ,
                double centerX,
                double centerZ,
                double distance)
            {
                GridX = gridX;
                GridZ = gridZ;
                CenterX = centerX;
                CenterZ = centerZ;
                Distance = distance;
            }

            public long GridX { get; }
            public long GridZ { get; }
            public double CenterX { get; }
            public double CenterZ { get; }
            public double Distance { get; }
        }

        public static WorldPatternRegionSample Sample(
            int worldX,
            int worldZ,
            int regionSeed,
            int warpXSeed,
            int warpZSeed,
            in WorldPatternRegionSettingsData settings)
        {
            var warpX = SignedFractal(
                    worldX * settings.WarpScale,
                    worldZ * settings.WarpScale,
                    warpXSeed)
                * settings.WarpStrengthCells;
            var warpZ = SignedFractal(
                    worldX * settings.WarpScale,
                    worldZ * settings.WarpScale,
                    warpZSeed)
                * settings.WarpStrengthCells;
            var sampleX = worldX + warpX;
            var sampleZ = worldZ + warpZ;
            var gridX = (long)Math.Floor(sampleX / settings.SizeCells);
            var gridZ = (long)Math.Floor(sampleZ / settings.SizeCells);
            var nearest = default(RawRegion);
            var second = default(RawRegion);
            var nearestDistance = double.PositiveInfinity;
            var secondDistance = double.PositiveInfinity;

            for (var offsetZ = -1; offsetZ <= 1; offsetZ++)
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                var candidateGridX = gridX + offsetX;
                var candidateGridZ = gridZ + offsetZ;
                var jitterX = (DeterministicNoise.Value01(
                        candidateGridX,
                        candidateGridZ,
                        regionSeed + 101) * 2f - 1f)
                    * settings.CenterJitter
                    * settings.SizeCells;
                var jitterZ = (DeterministicNoise.Value01(
                        candidateGridX,
                        candidateGridZ,
                        regionSeed + 211) * 2f - 1f)
                    * settings.CenterJitter
                    * settings.SizeCells;
                var centerX = (candidateGridX + 0.5) * settings.SizeCells
                    + jitterX;
                var centerZ = (candidateGridZ + 0.5) * settings.SizeCells
                    + jitterZ;
                var deltaX = sampleX - centerX;
                var deltaZ = sampleZ - centerZ;
                var distance = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
                var candidate = new RawRegion(
                    candidateGridX,
                    candidateGridZ,
                    centerX,
                    centerZ,
                    distance);
                if (distance < nearestDistance)
                {
                    second = nearest;
                    secondDistance = nearestDistance;
                    nearest = candidate;
                    nearestDistance = distance;
                }
                else if (distance < secondDistance)
                {
                    second = candidate;
                    secondDistance = distance;
                }
            }

            var boundaryDistance = Math.Max(
                0.0,
                (secondDistance - nearestDistance) * 0.5);
            var boundaryProgress = SmootherStep((float)Math.Clamp(
                boundaryDistance / settings.BoundaryBlendCells,
                0.0,
                1.0));
            var primaryInfluence = 0.5f + boundaryProgress * 0.5f;
            var interiorProgress = SmootherStep((float)Math.Clamp(
                boundaryDistance / (settings.SizeCells * 0.35),
                0.0,
                1.0));
            return new WorldPatternRegionSample(
                CreateCandidate(
                    nearest,
                    sampleX,
                    sampleZ,
                    primaryInfluence,
                    interiorProgress,
                    regionSeed,
                    settings),
                CreateCandidate(
                    second,
                    sampleX,
                    sampleZ,
                    1f - primaryInfluence,
                    0f,
                    regionSeed,
                    settings));
        }

        private static WorldPatternRegionCandidate CreateCandidate(
            in RawRegion region,
            double sampleX,
            double sampleZ,
            float influence,
            float interiorProgress,
            int seed,
            in WorldPatternRegionSettingsData settings)
        {
            var hash = DeterministicNoise.Hash(region.GridX, region.GridZ, seed);
            var key = unchecked((int)hash);
            var selector = (hash & 0x00FFFFFFu) / 16777215f
                * settings.TotalShare;
            var type = SelectType(selector, settings);
            return new WorldPatternRegionCandidate(
                key,
                type,
                influence,
                interiorProgress,
                sampleX - region.CenterX,
                sampleZ - region.CenterZ,
                settings.SizeCells);
        }

        private static WorldPatternType SelectType(
            float value,
            in WorldPatternRegionSettingsData settings)
        {
            if ((value -= settings.SmoothShare) <= 0f)
            {
                return WorldPatternType.Smooth;
            }

            if ((value -= settings.RuggedShare) <= 0f)
            {
                return WorldPatternType.Rugged;
            }

            if ((value -= settings.MountainShare) <= 0f)
            {
                return WorldPatternType.Mountain;
            }

            return value - settings.CanyonShare <= 0f
                ? WorldPatternType.Canyon
                : WorldPatternType.Sea;
        }

        private static float SignedFractal(double x, double z, int seed) =>
            DeterministicNoise.FractalNoise(x, z, seed, 3, 2f, 0.5f) * 2f - 1f;

        internal static float SmootherStep(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return value * value * value
                * (value * (value * 6f - 15f) + 10f);
        }
    }

    internal static class WorldPatternStage
    {
        public static void Build(GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }

            if (!working.HasWorldField)
            {
                throw new InvalidOperationException("World Field is not ready.");
            }

            var settings = working.Input.Settings;
            var router = new WorldNoiseRouter(settings);
            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                var worldX = checked(working.SampleOriginX + sampleLocalX);
                var worldZ = checked(working.SampleOriginZ + sampleLocalZ);
                working.SetWorldPatternResult(
                    sampleLocalX,
                    sampleLocalZ,
                    WorldPatternResolver.Resolve(
                        router,
                        worldX,
                        worldZ,
                        working.GetWorldField(sampleLocalX, sampleLocalZ),
                        settings,
                        out _));
            }

            working.CompleteWorldPatternResult();
        }
    }

    internal static class WorldPatternResolver
    {
        public static WorldPatternResult Resolve(
            in WorldNoiseRouter router,
            int worldX,
            int worldZ,
            in WorldFieldSample field,
            WorldSettingsData worldSettings,
            out WorldPatternWeights weights)
        {
            if (worldSettings == null)
            {
                throw new ArgumentNullException(nameof(worldSettings));
            }

            var settings = worldSettings.WorldPatterns;
            var region = router.SampleRegion(worldX, worldZ);
            weights = CreateWeights(region);
            var baseSurfaceUnits = worldSettings.TerrainBaseHeightUnits
                + settings.BaseDensity.SurfaceByContinentalness.Evaluate(
                    field.Continentalness)
                + settings.BaseDensity.SurfaceByErosion.Evaluate(field.Erosion);
            var baseDetailUnits = settings.BaseDensity.DetailByRoughness.Evaluate(
                field.Roughness);
            var primary = GenerateCandidate(
                region.Primary,
                field,
                settings,
                baseSurfaceUnits,
                baseDetailUnits);
            var secondary = GenerateCandidate(
                region.Secondary,
                field,
                settings,
                baseSurfaceUnits,
                baseDetailUnits);
            var surfaceUnits = Lerp(
                secondary.SurfaceUnits,
                primary.SurfaceUnits,
                region.Primary.Influence);
            var detailUnits = Lerp(
                secondary.DetailUnits,
                primary.DetailUnits,
                region.Primary.Influence);
            var primaryIsSea = region.Primary.PatternType == WorldPatternType.Sea;
            return new WorldPatternResult(
                surfaceUnits - worldSettings.TerrainBaseHeightUnits,
                settings.BaseDensity.VerticalFactorByErosion.Evaluate(
                    field.Erosion),
                detailUnits,
                region.Primary.PatternType,
                region.Primary.RegionKey,
                region.Primary.InteriorProgress,
                primary.DepthUnits,
                primary.DepthProgress,
                primary.PatternDetailUnits,
                primaryIsSea ? primary.WaterTopUnits : 0,
                primaryIsSea ? primary.WaterType : WaterType.None);
        }

        private static WorldPatternCandidateResult GenerateCandidate(
            in WorldPatternRegionCandidate region,
            in WorldFieldSample field,
            in WorldPatternSettingsData settings,
            float baseSurfaceUnits,
            float baseDetailUnits)
        {
            switch (region.PatternType)
            {
                case WorldPatternType.Smooth:
                {
                    var result = SmoothTerrainGenerator.Generate(
                        field,
                        settings.Smooth);
                    return new WorldPatternCandidateResult(
                        baseSurfaceUnits + result.SurfaceOffsetUnits,
                        baseDetailUnits + result.DetailUnits,
                        0f,
                        0f,
                        0f,
                        0,
                        WaterType.None);
                }
                case WorldPatternType.Rugged:
                {
                    var result = RuggedTerrainGenerator.Generate(
                        field,
                        settings.Rugged);
                    return new WorldPatternCandidateResult(
                        baseSurfaceUnits + result.SurfaceOffsetUnits,
                        baseDetailUnits + result.DetailUnits,
                        0f,
                        0f,
                        0f,
                        0,
                        WaterType.None);
                }
                case WorldPatternType.Mountain:
                {
                    var result = MountainTerrainGenerator.Generate(
                        region,
                        field,
                        settings.Mountain);
                    return new WorldPatternCandidateResult(
                        baseSurfaceUnits + result.SurfaceOffsetUnits,
                        baseDetailUnits,
                        0f,
                        0f,
                        0f,
                        0,
                        WaterType.None);
                }
                case WorldPatternType.Canyon:
                {
                    var result = CanyonTerrainGenerator.Generate(
                        region,
                        settings.Canyon);
                    return new WorldPatternCandidateResult(
                        baseSurfaceUnits + result.SurfaceOffsetUnits,
                        baseDetailUnits * settings.Canyon.DetailStrength,
                        -result.SurfaceOffsetUnits,
                        result.DepthProgress,
                        0f,
                        0,
                        WaterType.None);
                }
                case WorldPatternType.Sea:
                    return SeaPatternGenerator.Generate(
                        region,
                        field,
                        settings.Sea);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static WorldPatternWeights CreateWeights(
            in WorldPatternRegionSample region)
        {
            var smooth = 0f;
            var rugged = 0f;
            var mountain = 0f;
            var canyon = 0f;
            var sea = 0f;
            AddWeight(
                region.Primary.PatternType,
                region.Primary.Influence,
                ref smooth,
                ref rugged,
                ref mountain,
                ref canyon,
                ref sea);
            AddWeight(
                region.Secondary.PatternType,
                region.Secondary.Influence,
                ref smooth,
                ref rugged,
                ref mountain,
                ref canyon,
                ref sea);
            return new WorldPatternWeights(
                smooth,
                rugged,
                mountain,
                canyon,
                sea);
        }

        private static void AddWeight(
            WorldPatternType type,
            float value,
            ref float smooth,
            ref float rugged,
            ref float mountain,
            ref float canyon,
            ref float sea)
        {
            switch (type)
            {
                case WorldPatternType.Smooth:
                    smooth += value;
                    break;
                case WorldPatternType.Rugged:
                    rugged += value;
                    break;
                case WorldPatternType.Mountain:
                    mountain += value;
                    break;
                case WorldPatternType.Canyon:
                    canyon += value;
                    break;
                case WorldPatternType.Sea:
                    sea += value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private static float Lerp(float from, float to, float amount) =>
            from + (to - from) * amount;

        internal static WorldPatternCandidateResult CreateSeaCandidate(
            float surfaceUnits,
            float depthUnits,
            float depthProgress,
            float detailUnits,
            int waterTopUnits) => new(
                surfaceUnits,
                0f,
                depthUnits,
                depthProgress,
                detailUnits,
                waterTopUnits,
                WaterType.Sea);
    }

    internal static class SeaPatternGenerator
    {
        public static WorldPatternCandidateResult Generate(
            in WorldPatternRegionCandidate region,
            in WorldFieldSample field,
            in SeaPatternSettingsData settings)
        {
            var depthProgress = Math.Clamp(
                settings.DepthByInterior.EvaluateMonotonic(
                    region.InteriorProgress),
                0f,
                1f);
            var maximumDepthUnits = settings.MaximumDepthCells
                * WorldGrid.HeightStepsPerCell;
            var depthUnits = depthProgress * maximumDepthUnits;
            var seabedDetailUnits = field.SeaDetail
                * settings.ShapeDetailStrength
                * depthUnits;
            return WorldPatternResolver.CreateSeaCandidate(
                settings.SurfaceUnits - depthUnits + seabedDetailUnits,
                depthUnits,
                depthProgress,
                seabedDetailUnits,
                settings.SurfaceUnits);
        }
    }

    internal static class SmoothTerrainGenerator
    {
        public static TerrainPatternContribution Generate(
            in WorldFieldSample field,
            in SmoothTerrainSettingsData settings) => new(
                settings.UndulationByWeirdness.Evaluate(field.Weirdness),
                settings.DetailByRoughness.Evaluate(field.Roughness));
    }

    internal static class RuggedTerrainGenerator
    {
        public static TerrainPatternContribution Generate(
            in WorldFieldSample field,
            in RuggedTerrainSettingsData settings) => new(
                settings.ReliefByPeaksValleys.Evaluate(field.PeaksValleys)
                * settings.ReliefScaleByRoughness.Evaluate(field.Roughness),
                settings.DetailByRoughness.Evaluate(field.Roughness));
    }

    internal static class MountainTerrainGenerator
    {
        private const int SlopeSegmentCount = 6;

        public static TerrainPatternContribution Generate(
            in WorldPatternRegionCandidate region,
            in WorldFieldSample field,
            in MountainTerrainSettingsData settings)
        {
            var heightSelector = Hash01(region.RegionKey, 401);
            var selectedHeight = Lerp(
                settings.MinimumHeightUnits,
                settings.MaximumHeightUnits,
                MathF.Pow(heightSelector, settings.HeightBias));
            var climb = IntegratedPositiveSlope(
                region.InteriorProgress,
                region.RegionKey,
                settings.SlopeVariation);
            var ridge = field.PeaksValleys
                * settings.RidgeStrengthUnits
                * WorldPatternRegionSampler.SmootherStep(
                    region.InteriorProgress);
            return new TerrainPatternContribution(
                selectedHeight * climb + ridge,
                0f);
        }

        private static float IntegratedPositiveSlope(
            float progress,
            int regionKey,
            float variation)
        {
            progress = Math.Clamp(progress, 0f, 1f);
            var totalArea = 0f;
            var partialArea = 0f;
            var scaled = progress * SlopeSegmentCount;
            for (var segment = 0; segment < SlopeSegmentCount; segment++)
            {
                var from = Slope(regionKey, segment, variation);
                var to = Slope(regionKey, segment + 1, variation);
                var area = (from + to) * 0.5f;
                totalArea += area;
                if (scaled <= segment)
                {
                    continue;
                }

                var amount = Math.Clamp(scaled - segment, 0f, 1f);
                partialArea += from * amount
                    + (to - from) * amount * amount * 0.5f;
            }

            return totalArea > 0f
                ? Math.Clamp(partialArea / totalArea, 0f, 1f)
                : progress;
        }

        private static float Slope(int regionKey, int index, float variation)
        {
            var value = Hash01(regionKey, 503 + index * 37) * 2f - 1f;
            return MathF.Exp(value * variation);
        }

        private static float Hash01(int regionKey, int channel) =>
            (DeterministicNoise.Hash(regionKey, channel, 7193)
                & 0x00FFFFFFu) / 16777215f;

        private static float Lerp(float from, float to, float amount) =>
            from + (to - from) * amount;
    }

    internal static class CanyonTerrainGenerator
    {
        internal readonly struct CanyonContribution
        {
            public CanyonContribution(float surfaceOffsetUnits, float depthProgress)
            {
                SurfaceOffsetUnits = surfaceOffsetUnits;
                DepthProgress = depthProgress;
            }

            public float SurfaceOffsetUnits { get; }
            public float DepthProgress { get; }
        }

        public static CanyonContribution Generate(
            in WorldPatternRegionCandidate region,
            in CanyonTerrainSettingsData settings)
        {
            var selectedRegionDepth = Lerp(
                settings.MinimumDepthUnits,
                settings.MaximumDepthUnits,
                Hash01(region.RegionKey, 149));
            var regionDepthRatio = Lerp(
                settings.MinimumRegionDepthRatio,
                settings.MaximumRegionDepthRatio,
                Hash01(region.RegionKey, 163));
            var basinDepth = selectedRegionDepth * regionDepthRatio;
            var additionalDepthCapacity = Math.Max(
                0f,
                settings.MaximumDepthUnits - basinDepth);
            var valleyCount = SelectValleyCount(region.RegionKey, settings);
            var remainingOutsideValleys = 1f;
            var baseAngle = Hash01(region.RegionKey, 101) * MathF.PI;
            var angleSpacing = MathF.PI / valleyCount;
            for (var valleyIndex = 0; valleyIndex < valleyCount; valleyIndex++)
            {
                var angle = baseAngle
                    + (valleyIndex + Hash01(
                        region.RegionKey,
                        113 + valleyIndex * 97))
                    * angleSpacing;
                var valley = EvaluateValley(
                    region,
                    settings,
                    valleyIndex,
                    angle,
                    selectedRegionDepth,
                    basinDepth,
                    additionalDepthCapacity);
                remainingOutsideValleys *= 1f - valley;
            }

            var valleyNetwork = 1f - remainingOutsideValleys;
            var regionEnvelope = WorldPatternRegionSampler.SmootherStep(
                region.InteriorProgress);
            var finalDepth = regionEnvelope
                * (basinDepth + additionalDepthCapacity * valleyNetwork);
            var depthProgress = settings.MaximumDepthUnits > 0f
                ? Math.Clamp(finalDepth / settings.MaximumDepthUnits, 0f, 1f)
                : 0f;
            return new CanyonContribution(-finalDepth, depthProgress);
        }

        private static float EvaluateValley(
            in WorldPatternRegionCandidate region,
            in CanyonTerrainSettingsData settings,
            int valleyIndex,
            float angle,
            float selectedRegionDepth,
            float basinDepth,
            float additionalDepthCapacity)
        {
            var directionX = MathF.Cos(angle);
            var directionZ = MathF.Sin(angle);
            var perpendicularX = -directionZ;
            var perpendicularZ = directionX;
            var along = region.LocalX * directionX + region.LocalZ * directionZ;
            var across = region.LocalX * perpendicularX
                + region.LocalZ * perpendicularZ;
            var offset = (Hash01(
                    region.RegionKey,
                    179 + valleyIndex * 131) * 2f - 1f)
                * region.SizeCells
                * settings.MaximumValleyOffsetRatio;
            var axisWarp = AxisWarp(
                along,
                region,
                settings,
                valleyIndex);
            var width = Lerp(
                settings.MinimumWidthCells,
                settings.MaximumWidthCells,
                AlongNoise(along, region, 311 + valleyIndex * 149));
            var proximity = 1f - Math.Clamp(
                (float)Math.Abs(across - offset - axisWarp)
                    / Math.Max(0.5f, width),
                0f,
                1f);
            var crossSection = WorldPatternRegionSampler.SmootherStep(proximity);
            if (additionalDepthCapacity <= 0f)
            {
                return 0f;
            }

            var valleyDepth = Lerp(
                selectedRegionDepth,
                settings.MaximumDepthUnits,
                Hash01(region.RegionKey, 197 + valleyIndex * 167));
            var relativeDepth = Math.Clamp(
                (valleyDepth - basinDepth) / additionalDepthCapacity,
                0f,
                1f);
            return crossSection * relativeDepth;
        }

        private static int SelectValleyCount(
            int regionKey,
            in CanyonTerrainSettingsData settings)
        {
            var range = settings.MaximumValleyCount
                - settings.MinimumValleyCount
                + 1;
            var selected = (int)(Hash01(regionKey, 89) * range);
            return settings.MinimumValleyCount + Math.Min(range - 1, selected);
        }

        private static float AxisWarp(
            double along,
            in WorldPatternRegionCandidate region,
            in CanyonTerrainSettingsData settings,
            int valleyIndex)
        {
            var channel = 233 + valleyIndex * 181;
            var current = AlongNoise(along, region, channel);
            var center = AlongNoise(0.0, region, channel);
            return (current - center) * 2f * settings.AxisWarpCells;
        }

        private static float AlongNoise(
            double along,
            in WorldPatternRegionCandidate region,
            int channel)
        {
            var seed = unchecked(region.RegionKey ^ channel * 7919);
            return DeterministicNoise.FractalNoise(
                along / Math.Max(16.0, region.SizeCells * 0.65),
                channel * 0.173,
                seed,
                3,
                2f,
                0.5f);
        }

        private static float Hash01(int regionKey, int channel) =>
            (DeterministicNoise.Hash(regionKey, channel, 1237)
                & 0x00FFFFFFu) / 16777215f;

        private static float Lerp(float from, float to, float amount) =>
            from + (to - from) * amount;
    }

    internal static class WorldDensityStage
    {
        public static void Build(GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }

            if (!working.HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result is not ready.");
            }

            var field = new WorldDensityField(
                working.Input.Settings);
            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                var worldX = checked(working.SampleOriginX + sampleLocalX);
                var worldZ = checked(working.SampleOriginZ + sampleLocalZ);
                var fieldSample = working.GetWorldField(
                    sampleLocalX,
                    sampleLocalZ);
                var profile = working.GetWorldPatternResult(
                    sampleLocalX,
                    sampleLocalZ);
                for (var heightUnit = 0;
                     heightUnit < working.DensityHeightUnitCount;
                     heightUnit++)
                {
                    working.SetWorldDensity(
                        sampleLocalX,
                        heightUnit,
                        sampleLocalZ,
                        field.Sample(
                            worldX,
                            heightUnit,
                            worldZ,
                            fieldSample,
                            profile));
                }
            }

            working.CompleteWorldDensity();
        }
    }

    internal readonly struct WorldDensityField
    {
        private readonly WorldNoiseRouter noiseRouter;
        private readonly int terrainBaseHeightUnits;
        private readonly int maximumHeightUnit;

        public WorldDensityField(WorldSettingsData settings)
        {
            noiseRouter = new WorldNoiseRouter(settings);
            terrainBaseHeightUnits = settings.TerrainBaseHeightUnits;
            maximumHeightUnit = checked(
                settings.WorldHeight * WorldGrid.HeightStepsPerCell);
        }

        public float Sample(
            int worldX,
            int heightUnit,
            int worldZ,
            in WorldFieldSample field,
            in WorldPatternResult profile) =>
            Sample(
                worldX,
                heightUnit,
                worldZ,
                field,
                profile,
                out _);

        public float Sample(
            int worldX,
            int heightUnit,
            int worldZ,
            in WorldFieldSample field,
            in WorldPatternResult profile,
            out WorldDensityContributions contributions)
        {
            if ((uint)heightUnit > maximumHeightUnit)
            {
                throw new ArgumentOutOfRangeException(nameof(heightUnit));
            }

            var worldY = heightUnit
                / (double)WorldGrid.HeightStepsPerCell;
            var detail3D = noiseRouter.SampleDetail3D(
                worldX,
                worldY,
                worldZ);
            var verticalContribution = (
                terrainBaseHeightUnits - heightUnit)
                * profile.VerticalFactor;
            var surfaceOffsetContribution =
                profile.SurfaceOffsetUnits * profile.VerticalFactor;
            var surfaceDetailContribution = field.Detail
                * profile.DetailUnits
                * profile.VerticalFactor;
            var densityDetailContribution = detail3D
                * profile.DetailUnits;
            contributions = new WorldDensityContributions(
                verticalContribution,
                surfaceOffsetContribution,
                surfaceDetailContribution,
                densityDetailContribution);
            var density = contributions.Total;

            return heightUnit == 0 ? Math.Max(1f, density) : density;
        }
    }

    internal static class DensitySurfaceStage
    {
        public static void BuildFinalSurface(
            GenerationWorkingData working)
        {
            ValidateWorkingData(working);
            if (!working.HasWorldDensity)
            {
                throw new InvalidOperationException(
                    "World Density is not ready.");
            }

            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                working.SetFinalSurfaceUnits(
                    sampleLocalX,
                    sampleLocalZ,
                    FindSurfaceUnits(
                        working,
                        sampleLocalX,
                        sampleLocalZ));
            }

            working.CompleteFinalSurface();
        }

        private static float FindSurfaceUnits(
            GenerationWorkingData working,
            int sampleLocalX,
            int sampleLocalZ)
        {
            var topUnit = working.Input.HeightUnitCount;
            if (working.GetWorldDensity(
                    sampleLocalX,
                    topUnit,
                    sampleLocalZ) >= 0f)
            {
                return topUnit;
            }

            if (working.GetWorldDensity(
                    sampleLocalX,
                    0,
                    sampleLocalZ) < 0f)
            {
                return 0;
            }

            for (var lowerUnit = working.Input.HeightUnitCount - 1;
                 lowerUnit >= 0;
                 lowerUnit--)
            {
                var lowerDensity = working.GetWorldDensity(
                    sampleLocalX,
                    lowerUnit,
                    sampleLocalZ);
                var upperDensity = working.GetWorldDensity(
                    sampleLocalX,
                    lowerUnit + 1,
                    sampleLocalZ);
                if (lowerDensity < 0f || upperDensity >= 0f)
                {
                    continue;
                }

                var denominator = lowerDensity - upperDensity;
                var fraction = denominator > 0f
                    ? lowerDensity / denominator
                    : 0f;
                var continuousSurface = lowerUnit
                    + Math.Clamp(fraction, 0f, 1f);
                return Math.Clamp(
                    continuousSurface,
                    0f,
                    working.Input.HeightUnitCount);
            }

            throw new InvalidOperationException(
                "Terrain Density does not contain a solid-to-air surface.");
        }

        private static void ValidateWorkingData(
            GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }
        }
    }

    internal static class DensityToFilledStage
    {
        public static void Build(GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }

            if (!working.HasFinalSurface)
            {
                throw new InvalidOperationException(
                    "Final Surface is not ready.");
            }

            var halo = working.HaloCellCount;
            var emptyBiome = default(CellBiome);
            for (var localZ = 0;
                 localZ < working.Input.ChunkSizeXZ;
                 localZ++)
            for (var localX = 0;
                 localX < working.Input.ChunkSizeXZ;
                 localX++)
            {
                var sampleLocalX = localX + halo;
                var sampleLocalZ = localZ + halo;
                var solidHeightUnits = Math.Clamp(
                    Math.Max(
                        1,
                        (int)MathF.Round(
                            working.GetFinalSurfaceUnits(
                                sampleLocalX,
                                sampleLocalZ),
                            MidpointRounding.AwayFromZero)),
                    1,
                    working.Input.HeightUnitCount);
                var pattern = working.GetWorldPatternResult(
                    sampleLocalX,
                    sampleLocalZ);
                var waterSurfaceUnits = pattern.HasWaterPattern
                    && pattern.WaterTopUnits > solidHeightUnits
                        ? Math.Clamp(
                            pattern.WaterTopUnits,
                            0,
                            working.Input.HeightUnitCount)
                        : 0;
                var hasWater = waterSurfaceUnits > solidHeightUnits;
                working.SetFinalColumn(
                    localX,
                    localZ,
                    new WorldColumnBuildData(
                        solidHeightUnits,
                        waterSurfaceUnits,
                        hasWater ? WaterRole.Source : WaterRole.None,
                        hasWater ? pattern.WaterType : WaterType.None,
                        hasWater ? SurfaceType.Seabed : SurfaceType.None,
                        solidHeightUnits > 0
                            ? hasWater
                                ? SurfaceType.Seabed
                                : SurfaceType.Ground
                            : SurfaceType.None,
                        emptyBiome));
            }
        }
    }

    internal readonly struct WorldColumnBuildData
    {
        public WorldColumnBuildData(
            int solidHeightUnits,
            int waterSurfaceUnits,
            WaterRole waterRole,
            WaterType waterType,
            SurfaceType waterBedSurface,
            SurfaceType topSurface,
            CellBiome biome)
        {
            SolidHeightUnits = solidHeightUnits;
            WaterSurfaceUnits = waterSurfaceUnits;
            WaterRole = waterRole;
            WaterType = waterType;
            WaterBedSurface = waterBedSurface;
            TopSurface = topSurface;
            Biome = biome;
        }

        public int SolidHeightUnits { get; }
        public int WaterSurfaceUnits { get; }
        public WaterRole WaterRole { get; }
        public WaterType WaterType { get; }
        public SurfaceType WaterBedSurface { get; }
        public SurfaceType TopSurface { get; }
        public CellBiome Biome { get; }
    }

    internal sealed class WorldChunkBuildData
    {
        private readonly WorldColumnBuildData[] columns;

        internal WorldChunkBuildData(
            WorldChunkBuildInput input,
            WorldColumnBuildData[] columns)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            this.columns = columns ?? throw new ArgumentNullException(nameof(columns));
            var expectedColumnCount = checked(
                input.ChunkSizeXZ * input.ChunkSizeXZ);
            if (columns.Length != expectedColumnCount)
            {
                throw new ArgumentException(
                    "Chunk output does not match the requested Chunk size.",
                    nameof(columns));
            }

            Coordinate = input.Coordinate;
            ChunkSizeXZ = input.ChunkSizeXZ;
        }

        public ChunkCoordinate Coordinate { get; }
        public int ChunkSizeXZ { get; }
        public int ColumnCount => columns.Length;

        public WorldColumnBuildData GetColumn(int localX, int localZ)
        {
            if ((uint)localX >= ChunkSizeXZ)
            {
                throw new ArgumentOutOfRangeException(nameof(localX));
            }

            if ((uint)localZ >= ChunkSizeXZ)
            {
                throw new ArgumentOutOfRangeException(nameof(localZ));
            }

            return columns[localX + ChunkSizeXZ * localZ];
        }
    }

    public static class WorldDataBuilder
    {
        public static WorldData CreateWorld(WorldBuildInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            return new WorldData(input.Settings);
        }

        internal static void ApplyChunk(
            WorldData world,
            WorldChunkBuildData build)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            if (!world.IsChunkWithinBounds(build.Coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(build));
            }

            if (world.IsChunkLoaded(build.Coordinate))
            {
                return;
            }

            if (build.ChunkSizeXZ != world.ChunkSizeX
                || build.ColumnCount != world.ChunkSizeX * world.ChunkSizeZ)
            {
                throw new InvalidOperationException(
                    "Chunk output does not match the target world settings.");
            }

            ValidateColumns(world, build);
            world.EnsureChunkLoaded(build.Coordinate);
            var startX = checked(build.Coordinate.X * world.ChunkSizeX);
            var startZ = checked(build.Coordinate.Z * world.ChunkSizeZ);
            for (var localZ = 0; localZ < world.ChunkSizeZ; localZ++)
            for (var localX = 0; localX < world.ChunkSizeX; localX++)
            {
                WriteColumn(
                    world,
                    startX + localX,
                    startZ + localZ,
                    build.GetColumn(localX, localZ));
            }
        }

        private static void ValidateColumns(
            WorldData world,
            WorldChunkBuildData build)
        {
            var maximumUnits = checked(
                world.Height * WorldGrid.HeightStepsPerCell);
            for (var localZ = 0; localZ < build.ChunkSizeXZ; localZ++)
            for (var localX = 0; localX < build.ChunkSizeXZ; localX++)
            {
                var column = build.GetColumn(localX, localZ);
                if ((uint)column.SolidHeightUnits > maximumUnits
                    || (uint)column.WaterSurfaceUnits > maximumUnits)
                {
                    throw new InvalidOperationException(
                        "Chunk output contains a height outside the world range.");
                }

                var hasWater = column.WaterSurfaceUnits
                    > column.SolidHeightUnits;
                if (hasWater
                    && (column.WaterRole == WaterRole.None
                        || column.WaterType == WaterType.None))
                {
                    throw new InvalidOperationException(
                        "Chunk output Water height, role, and type do not describe the same final fact.");
                }

                if (!hasWater
                    && (column.WaterSurfaceUnits != 0
                        || column.WaterRole != WaterRole.None
                        || column.WaterType != WaterType.None
                        || column.WaterBedSurface != SurfaceType.None))
                {
                    throw new InvalidOperationException(
                        "A dry Chunk Column cannot contain Water output values.");
                }

                if (hasWater
                    && (column.WaterBedSurface == SurfaceType.None
                        || column.TopSurface != column.WaterBedSurface))
                {
                    throw new InvalidOperationException(
                        "Water output must use the same final bed and top surface.");
                }

                if (column.SolidHeightUnits > 0)
                {
                    if (column.TopSurface == SurfaceType.None)
                    {
                        throw new InvalidOperationException(
                            "Solid output must identify its top surface.");
                    }
                }
                else if (column.TopSurface != SurfaceType.None)
                {
                    throw new InvalidOperationException(
                        "An empty Chunk Column cannot contain a top surface.");
                }
            }
        }

        private static void WriteColumn(
            WorldData world,
            int x,
            int z,
            in WorldColumnBuildData column)
        {
            var usedHeightUnits = Math.Max(
                column.SolidHeightUnits,
                column.WaterSurfaceUnits);
            var usedCellCount = Math.Min(
                world.Height,
                Math.Max(
                    0,
                    (usedHeightUnits + WorldGrid.HeightStepsPerCell - 1)
                    / WorldGrid.HeightStepsPerCell));
            for (var y = 0; y < usedCellCount; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var solidFill = (byte)Math.Clamp(
                    column.SolidHeightUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var cell = new CellData
                {
                    Terrain = new TerrainData { SolidHeight = solidFill }
                };
                if (solidFill > 0)
                {
                    cell.Terrain.Material = y < Math.Max(
                        0,
                        column.SolidHeightUnits
                        / WorldGrid.HeightStepsPerCell - 2)
                            ? MaterialType.Rock
                            : MaterialType.Soil;
                    cell.Terrain.Geology = MaterialType.Rock;
                    cell.Terrain.Surface = solidFill < WorldGrid.HeightStepsPerCell
                        || baseUnits + solidFill == column.SolidHeightUnits
                            ? column.TopSurface
                            : SurfaceType.None;
                }

                var available = WorldGrid.HeightStepsPerCell - solidFill;
                var desiredTop = Math.Clamp(
                    column.WaterSurfaceUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var waterFill = (byte)Math.Clamp(
                    desiredTop - solidFill,
                    0,
                    available);
                if (waterFill > 0)
                {
                    cell.Water = new WaterData
                    {
                        Amount = WaterAmount.FromRenderFill(
                            waterFill,
                            available),
                        Role = column.WaterRole,
                        Type = column.WaterType,
                        Flow = FlowDirection.None
                    };
                }

                if (cell.HasTerrain || cell.HasWater)
                {
                    cell.Biome = column.Biome;
                    world.SetCellBulk(x, y, z, cell);
                }
            }
        }
    }

    internal static class WorldChunkGenerator
    {
        public static WorldChunkBuildData Build(WorldChunkBuildInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var working = WorldFieldStage.Build(input);
            WorldPatternStage.Build(working);
            WorldDensityStage.Build(working);
            DensitySurfaceStage.BuildFinalSurface(working);
            DensityToFilledStage.Build(working);
            return working.Complete();
        }
    }
}
