using System;
using System.Diagnostics;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    public sealed class WorldBuildInput
    {
        private WorldBuildInput(WorldGenerationSettings settings, int seed)
        {
            Settings = settings.CreateData(seed);
            Hydrology = new WorldHydrology(Settings);
        }

        public WorldSettingsData Settings { get; }
        internal WorldHydrology Hydrology { get; }
        internal WorldGenerationTimingSummary GenerationTiming { get; } = new();
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
            ChunkCoordinate coordinate,
            HydrologyPlanScope planScope) =>
            WorldChunkBuildInput.Create(
                Settings,
                coordinate,
                Hydrology,
                planScope);
    }

    internal sealed class WorldChunkBuildInput
    {
        private WorldChunkBuildInput(
            WorldSettingsData settings,
            ChunkCoordinate coordinate,
            WorldHydrology hydrology,
            HydrologyPlanScope planScope)
        {
            Settings = settings;
            Hydrology = hydrology;
            PlanScope = planScope;
            Coordinate = coordinate;
            OriginX = checked(coordinate.X * settings.ChunkCellCountXZ);
            OriginZ = checked(coordinate.Z * settings.ChunkCellCountXZ);
        }

        public WorldSettingsData Settings { get; }
        public WorldHydrology Hydrology { get; }
        public HydrologyPlanScope PlanScope { get; }
        public ChunkCoordinate Coordinate { get; }
        public int ChunkSizeXZ => Settings.ChunkCellCountXZ;
        public int WorldHeight => Settings.WorldHeight;
        public int HeightUnitCount => checked(
            WorldHeight * WorldGrid.HeightStepsPerCell);
        public int OriginX { get; }
        public int OriginZ { get; }

        public static WorldChunkBuildInput Create(
            WorldSettingsData settings,
            ChunkCoordinate coordinate,
            WorldHydrology hydrology,
            HydrologyPlanScope planScope)
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

            if (hydrology == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            if (!ReferenceEquals(hydrology.Settings, settings))
            {
                throw new ArgumentException(
                    "The Hydrology service must use the Chunk Build settings.",
                    nameof(hydrology));
            }

            if (planScope == null)
            {
                throw new ArgumentNullException(nameof(planScope));
            }

            return new WorldChunkBuildInput(
                settings,
                coordinate,
                hydrology,
                planScope);
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
            float interiorProgress)
        {
            RegionKey = regionKey;
            PatternType = patternType;
            Influence = influence;
            InteriorProgress = interiorProgress;
        }

        public int RegionKey { get; }
        public WorldPatternType PatternType { get; }
        public float Influence { get; }
        public float InteriorProgress { get; }
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
            WaterType waterType,
            float riverInfluence = 0f,
            float riverDepthUnits = 0f,
            int hydrologyComponentId = 0,
            float hydrologyMembership = 0f,
            float hydrologyInteriorProgress = 0f,
            WaterType hydrologyType = WaterType.None)
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
            RiverInfluence = riverInfluence;
            RiverDepthUnits = riverDepthUnits;
            HydrologyComponentId = hydrologyComponentId;
            HydrologyMembership = hydrologyMembership;
            HydrologyInteriorProgress = hydrologyInteriorProgress;
            HydrologyType = hydrologyType;
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
        public float RiverInfluence { get; }
        public float RiverDepthUnits { get; }
        public int HydrologyComponentId { get; }
        public float HydrologyMembership { get; }
        public float HydrologyInteriorProgress { get; }
        public WaterType HydrologyType { get; }
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

    internal readonly struct TerrainPatternFieldContext
    {
        public TerrainPatternFieldContext(
            int worldSeed,
            int worldX,
            int worldZ,
            in WorldPatternRegionCandidate region)
        {
            WorldSeed = worldSeed;
            WorldX = worldX;
            WorldZ = worldZ;
            Region = region;
        }

        public int WorldSeed { get; }
        public int WorldX { get; }
        public int WorldZ { get; }
        public WorldPatternRegionCandidate Region { get; }
    }

    internal readonly struct TerrainPatternFieldCoordinates
    {
        public TerrainPatternFieldCoordinates(double x, double z)
        {
            X = x;
            Z = z;
        }

        public double X { get; }
        public double Z { get; }
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
        private WorldPatternResult[] baseTerrainPatternResults;
        private WorldPatternResult[] worldPatternResults;
        private float[] finalSurfaceUnits;
        private WorldColumnBuildData[] finalColumns;
        private bool[] finalColumnWritten;
        private HydrologyBatch hydrologyBatch;

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
            baseTerrainPatternResults = new WorldPatternResult[
                worldFieldSamples.Length];
            finalSurfaceUnits = new float[worldFieldSamples.Length];
            finalColumns = new WorldColumnBuildData[
                checked(input.ChunkSizeXZ * input.ChunkSizeXZ)];
            finalColumnWritten = new bool[finalColumns.Length];
            var metricsBefore = input.Hydrology.CaptureMetrics();
            var hydrologyTimer = Stopwatch.StartNew();
            hydrologyBatch = HydrologyBatchBuilder.Build(
                input.Hydrology,
                input.PlanScope,
                SampleOriginX,
                SampleOriginZ,
                SampleSizeXZ,
                SampleSizeXZ);
            HydrologyBatchMilliseconds = hydrologyTimer.ElapsedMilliseconds;
            HydrologyMetrics = input.Hydrology.CaptureMetrics()
                .Delta(metricsBefore);
        }

        public WorldChunkBuildInput Input { get; }
        public int HaloCellCount { get; }
        public int SampleOriginX { get; }
        public int SampleOriginZ { get; }
        public int SampleSizeXZ { get; }
        public bool HasWorldField { get; private set; }
        public bool HasBaseTerrainPattern { get; private set; }
        public bool HasWorldPatternResult { get; private set; }
        public bool HasFinalSurface { get; private set; }
        public bool IsCompleted => finalColumns == null;
        public long HydrologyBatchMilliseconds { get; }
        public HydrologyMetricsSnapshot HydrologyMetrics { get; }
        public HydrologyBatch HydrologyBatch => hydrologyBatch
            ?? throw new InvalidOperationException(
                "Hydrology Batch has already been released.");

        public void ReleaseHydrologyBatch()
        {
            hydrologyBatch?.Dispose();
            hydrologyBatch = null;
        }

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

        public void SetBaseTerrainPattern(
            int sampleLocalX,
            int sampleLocalZ,
            in WorldPatternResult value)
        {
            EnsureNotCompleted();
            if (!HasWorldField || HasBaseTerrainPattern)
            {
                throw new InvalidOperationException(
                    "Base Terrain Pattern stage order is invalid.");
            }
            baseTerrainPatternResults[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)] = value;
        }

        public void CompleteBaseTerrainPattern()
        {
            EnsureNotCompleted();
            if (!HasWorldField || HasBaseTerrainPattern)
            {
                throw new InvalidOperationException(
                    "Base Terrain Pattern stage order is invalid.");
            }
            HasBaseTerrainPattern = true;
        }

        public WorldPatternResult GetBaseTerrainPattern(
            int sampleLocalX,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasBaseTerrainPattern)
            {
                throw new InvalidOperationException(
                    "Base Terrain Pattern is not ready.");
            }
            return baseTerrainPatternResults[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)];
        }

        public void SetWorldPatternResult(
            int sampleLocalX,
            int sampleLocalZ,
            in WorldPatternResult value)
        {
            EnsureNotCompleted();
            if (!HasBaseTerrainPattern)
            {
                throw new InvalidOperationException(
                    "Base Terrain Pattern must be ready before Hydrology composition.");
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
            if (!HasBaseTerrainPattern)
            {
                throw new InvalidOperationException(
                    "Base Terrain Pattern must be ready before Hydrology composition is finalized.");
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

        public void SetFinalSurfaceUnits(
            int sampleLocalX,
            int sampleLocalZ,
            float surfaceUnits)
        {
            EnsureNotCompleted();
            if (!HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result must be ready before Final Surface is extracted.");
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
            if (!HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result must be ready before Final Surface is finalized.");
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

        public WorldChunkBuildData Complete(in WorldChunkGenerationTiming timing)
        {
            EnsureNotCompleted();
            if (!HasWorldField
                || !HasBaseTerrainPattern
                || !HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "Every terrain and Hydrology generation stage must be ready before final output is transferred.");
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
            baseTerrainPatternResults = null;
            worldPatternResults = null;
            finalSurfaceUnits = null;
            finalColumns = null;
            finalColumnWritten = null;
            ReleaseHydrologyBatch();
            return new WorldChunkBuildData(Input, columns, timing);
        }

        private void EnsureNotCompleted()
        {
            if (IsCompleted)
            {
                throw new InvalidOperationException(
                    "Generation working data has already transferred its final output.");
            }
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
            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                var worldX = checked(working.SampleOriginX + sampleLocalX);
                var worldZ = checked(working.SampleOriginZ + sampleLocalZ);
                var terrain = working.HydrologyBatch.SampleBaseTerrainState(
                    worldX,
                    worldZ);
                working.SetWorldField(
                    sampleLocalX,
                    sampleLocalZ,
                    terrain.Field);
            }

            working.CompleteWorldField();
            return working;
        }
    }

    internal static class WorldNoiseFieldSampler
    {
        public static float Sample2D(
            double worldX,
            double worldZ,
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

    internal static class TerrainPatternFieldSampler
    {
        public static TerrainPatternFieldCoordinates Warp(
            in TerrainPatternFieldContext context,
            in TerrainDomainWarpSettingsData settings,
            int channel)
        {
            var origin = new TerrainPatternFieldCoordinates(
                context.WorldX,
                context.WorldZ);
            var warpX = SampleSigned(
                context,
                origin,
                settings.Field,
                channel);
            var warpZ = SampleSigned(
                context,
                origin,
                settings.Field,
                channel + 1);
            return new TerrainPatternFieldCoordinates(
                context.WorldX + warpX * settings.StrengthCells,
                context.WorldZ + warpZ * settings.StrengthCells);
        }

        public static float Sample01(
            in TerrainPatternFieldContext context,
            in TerrainPatternFieldCoordinates coordinates,
            in WorldNoiseFieldSettingsData field,
            int channel)
        {
            var sample = WorldNoiseFieldSampler.Sample2D(
                coordinates.X,
                coordinates.Z,
                field,
                Seed(context, channel));
            return field.Mode is WorldNoiseMode.Signed
                or WorldNoiseMode.SignedRidge
                ? Math.Clamp((sample + 1f) * 0.5f, 0f, 1f)
                : Math.Clamp(sample, 0f, 1f);
        }

        public static float SampleSigned(
            in TerrainPatternFieldContext context,
            in TerrainPatternFieldCoordinates coordinates,
            in WorldNoiseFieldSettingsData field,
            int channel) => Sample01(
                context,
                coordinates,
                field,
                channel) * 2f - 1f;

        public static float Resolve(
            in TerrainPatternFieldContext context,
            in WorldSeededRangeSettingsData range,
            int channel)
        {
            var selector = (DeterministicNoise.Hash(
                    context.Region.RegionKey,
                    channel,
                    context.WorldSeed) & 0x00FFFFFFu)
                / 16777215f;
            return range.Minimum
                + (range.Maximum - range.Minimum) * selector;
        }

        private static int Seed(
            in TerrainPatternFieldContext context,
            int channel) => unchecked((int)DeterministicNoise.Hash(
            context.Region.RegionKey,
            channel,
            context.WorldSeed));
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
                boundaryDistance
                    / (settings.SizeCells * settings.InteriorReachRatio),
                0.0,
                1.0));
            return new WorldPatternRegionSample(
                CreateCandidate(
                    nearest,
                    primaryInfluence,
                    interiorProgress,
                    regionSeed,
                    settings),
                CreateCandidate(
                    second,
                    1f - primaryInfluence,
                    0f,
                    regionSeed,
                    settings));
        }

        private static WorldPatternRegionCandidate CreateCandidate(
            in RawRegion region,
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
                interiorProgress);
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

    internal static class BaseTerrainPatternStage
    {
        public static void Build(GenerationWorkingData working)
        {
            if (working == null || !working.HasWorldField)
            {
                throw new InvalidOperationException("World Field is not ready.");
            }

            for (var z = 0; z < working.SampleSizeXZ; z++)
            for (var x = 0; x < working.SampleSizeXZ; x++)
            {
                var worldX = checked(working.SampleOriginX + x);
                var worldZ = checked(working.SampleOriginZ + z);
                var terrain = working.HydrologyBatch
                    .SampleBaseTerrainState(worldX, worldZ);
                working.SetBaseTerrainPattern(
                    x,
                    z,
                    terrain.Terrain);
            }
            working.CompleteBaseTerrainPattern();
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

            if (!working.HasBaseTerrainPattern)
            {
                throw new InvalidOperationException(
                    "Base Terrain Pattern is not ready.");
            }

            var settings = working.Input.Settings;
            try
            {
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
                        HydrologyPatternResolver.Resolve(
                            worldX,
                            worldZ,
                            settings,
                            working.HydrologyBatch,
                            working.GetBaseTerrainPattern(
                                sampleLocalX,
                                sampleLocalZ)));
                }

                working.CompleteWorldPatternResult();
            }
            finally
            {
                working.ReleaseHydrologyBatch();
            }
        }
    }

    internal static class WorldPatternResolver
    {
        public static WorldPatternWeights SampleWeights(
            in WorldNoiseRouter router,
            int worldX,
            int worldZ) => CreateWeights(
                router.SampleRegion(worldX, worldZ));

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
            var primary = GenerateCandidate(
                region.Primary,
                worldSettings.Seed,
                worldX,
                worldZ,
                settings,
                baseSurfaceUnits);
            var secondary = GenerateCandidate(
                region.Secondary,
                worldSettings.Seed,
                worldX,
                worldZ,
                settings,
                baseSurfaceUnits);
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
            int worldSeed,
            int worldX,
            int worldZ,
            in WorldPatternSettingsData settings,
            float baseSurfaceUnits)
        {
            var context = new TerrainPatternFieldContext(
                worldSeed,
                worldX,
                worldZ,
                region);
            switch (region.PatternType)
            {
                case WorldPatternType.Smooth:
                {
                    var result = SmoothTerrainGenerator.Generate(
                        context,
                        settings.Smooth);
                    return new WorldPatternCandidateResult(
                        baseSurfaceUnits + result.SurfaceOffsetUnits,
                        result.DetailUnits,
                        0f,
                        0f,
                        0f,
                        0,
                        WaterType.None);
                }
                case WorldPatternType.Rugged:
                {
                    var result = RuggedTerrainGenerator.Generate(
                        context,
                        settings.Rugged);
                    return new WorldPatternCandidateResult(
                        baseSurfaceUnits + result.SurfaceOffsetUnits,
                        result.DetailUnits,
                        0f,
                        0f,
                        0f,
                        0,
                        WaterType.None);
                }
                case WorldPatternType.Mountain:
                {
                    var result = MountainTerrainGenerator.Generate(
                        context,
                        settings.Mountain);
                    return new WorldPatternCandidateResult(
                        baseSurfaceUnits + result.SurfaceOffsetUnits,
                        result.DetailUnits,
                        0f,
                        0f,
                        0f,
                        0,
                        WaterType.None);
                }
                case WorldPatternType.Canyon:
                {
                    var result = CanyonTerrainGenerator.Generate(
                        context,
                        settings.Canyon);
                    return new WorldPatternCandidateResult(
                        baseSurfaceUnits + result.SurfaceOffsetUnits,
                        0f,
                        -result.SurfaceOffsetUnits,
                        result.DepthProgress,
                        0f,
                        0,
                        WaterType.None);
                }
                case WorldPatternType.Sea:
                    return SeaPatternGenerator.Generate(
                        context,
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
            in TerrainPatternFieldContext context,
            in SeaPatternSettingsData settings)
        {
            const int channel = 5000;
            var coordinates = TerrainPatternFieldSampler.Warp(
                context,
                settings.DomainWarp,
                channel);
            var basinVariation = TerrainPatternFieldSampler.SampleSigned(
                    context,
                    coordinates,
                    settings.BasinField,
                    channel + 10)
                * settings.BasinVariation;
            var basinProgress = Math.Clamp(
                context.Region.InteriorProgress + basinVariation,
                0f,
                1f);
            var depthProgress = Math.Clamp(
                settings.DepthByBasin.EvaluateMonotonic(basinProgress),
                0f,
                1f);
            var maximumDepthUnits = TerrainPatternFieldSampler.Resolve(
                context,
                settings.MaximumDepthUnits,
                channel + 20);
            var depthUnits = depthProgress * maximumDepthUnits;
            var seabedDetailUnits = TerrainPatternFieldSampler.SampleSigned(
                    context,
                    coordinates,
                    settings.SeabedField,
                    channel + 30)
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.SeabedAmplitudeUnits,
                    channel + 40)
                * depthProgress;
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
            in TerrainPatternFieldContext context,
            in SmoothTerrainSettingsData settings)
        {
            const int channel = 1000;
            var coordinates = TerrainPatternFieldSampler.Warp(
                context,
                settings.DomainWarp,
                channel);
            var height = settings.HeightResponse.Evaluate(
                    TerrainPatternFieldSampler.Sample01(
                        context,
                        coordinates,
                        settings.HeightField,
                        channel + 10))
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.HeightAmplitudeUnits,
                    channel + 20);
            var detail = TerrainPatternFieldSampler.SampleSigned(
                    context,
                    coordinates,
                    settings.DetailField,
                    channel + 30)
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.DetailAmplitudeUnits,
                    channel + 40);
            return new TerrainPatternContribution(height + detail, 0f);
        }
    }

    internal static class RuggedTerrainGenerator
    {
        public static TerrainPatternContribution Generate(
            in TerrainPatternFieldContext context,
            in RuggedTerrainSettingsData settings)
        {
            const int channel = 2000;
            var coordinates = TerrainPatternFieldSampler.Warp(
                context,
                settings.DomainWarp,
                channel);
            var relief = settings.ReliefResponse.Evaluate(
                    TerrainPatternFieldSampler.Sample01(
                        context,
                        coordinates,
                        settings.ReliefField,
                        channel + 10))
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.ReliefAmplitudeUnits,
                    channel + 20);
            var detail = TerrainPatternFieldSampler.SampleSigned(
                    context,
                    coordinates,
                    settings.DetailField,
                    channel + 30)
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.DetailAmplitudeUnits,
                    channel + 40);
            return new TerrainPatternContribution(relief + detail, 0f);
        }
    }

    internal static class MountainTerrainGenerator
    {
        public static TerrainPatternContribution Generate(
            in TerrainPatternFieldContext context,
            in MountainTerrainSettingsData settings)
        {
            const int channel = 3000;
            var coordinates = TerrainPatternFieldSampler.Warp(
                context,
                settings.DomainWarp,
                channel);
            var mass = settings.MassResponse.Evaluate(
                    TerrainPatternFieldSampler.Sample01(
                        context,
                        coordinates,
                        settings.MassField,
                        channel + 10))
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.HeightUnits,
                    channel + 20);
            var ridge = settings.RidgeResponse.Evaluate(
                    TerrainPatternFieldSampler.Sample01(
                        context,
                        coordinates,
                        settings.RidgeField,
                        channel + 30))
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.RidgeStrengthUnits,
                    channel + 40);
            var detail = TerrainPatternFieldSampler.SampleSigned(
                    context,
                    coordinates,
                    settings.DetailField,
                    channel + 50)
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.DetailAmplitudeUnits,
                    channel + 60);
            return new TerrainPatternContribution(
                mass + ridge + detail,
                0f);
        }
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
            in TerrainPatternFieldContext context,
            in CanyonTerrainSettingsData settings)
        {
            const int channel = 4000;
            var coordinates = TerrainPatternFieldSampler.Warp(
                context,
                settings.DomainWarp,
                channel);
            var basin = settings.BasinResponse.Evaluate(
                    TerrainPatternFieldSampler.Sample01(
                        context,
                        coordinates,
                        settings.BasinField,
                        channel + 10))
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.BasinDepthRatio,
                    channel + 20);
            var valley = settings.ValleyResponse.Evaluate(
                    TerrainPatternFieldSampler.Sample01(
                        context,
                        coordinates,
                        settings.ValleyField,
                        channel + 30))
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.ValleyDepthRatio,
                    channel + 40);
            var depthProgress = Math.Clamp(
                1f - (1f - basin) * (1f - valley),
                0f,
                1f);
            var depthUnits = depthProgress
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.DepthUnits,
                    channel + 50);
            var detailUnits = TerrainPatternFieldSampler.SampleSigned(
                    context,
                    coordinates,
                    settings.DetailField,
                    channel + 60)
                * TerrainPatternFieldSampler.Resolve(
                    context,
                    settings.DetailAmplitudeUnits,
                    channel + 70);
            return new CanyonContribution(
                -depthUnits + detailUnits,
                depthProgress);
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
            if (!working.HasWorldPatternResult)
            {
                throw new InvalidOperationException(
                    "World Pattern Result is not ready.");
            }

            var settings = working.Input.Settings;
            var density = new WorldDensityField(settings);
            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                var worldX = checked(
                    working.SampleOriginX + sampleLocalX);
                var worldZ = checked(
                    working.SampleOriginZ + sampleLocalZ);
                var field = working.GetWorldField(
                    sampleLocalX,
                    sampleLocalZ);
                var profile = working.GetWorldPatternResult(
                    sampleLocalX,
                    sampleLocalZ);
                var surfaceUnits = TerrainSurfaceSampler.SampleResolved(
                    density,
                    settings,
                    worldX,
                    worldZ,
                    field,
                    profile).SurfaceUnits;
                working.SetFinalSurfaceUnits(
                    sampleLocalX,
                    sampleLocalZ,
                    surfaceUnits);
            }

            working.CompleteFinalSurface();
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
                var waterBedSurface = hasWater
                    ? ResolveWaterBedSurface(pattern.WaterType)
                    : SurfaceType.None;
                working.SetFinalColumn(
                    localX,
                    localZ,
                    new WorldColumnBuildData(
                        solidHeightUnits,
                        waterSurfaceUnits,
                        hasWater ? WaterRole.Source : WaterRole.None,
                        hasWater ? pattern.WaterType : WaterType.None,
                        waterBedSurface,
                        solidHeightUnits > 0
                            ? hasWater
                                ? waterBedSurface
                                : SurfaceType.Ground
                            : SurfaceType.None,
                        emptyBiome));
            }
        }

        private static SurfaceType ResolveWaterBedSurface(
            WaterType waterType) => waterType switch
            {
                WaterType.River => SurfaceType.Riverbed,
                WaterType.Lake or WaterType.Pond => SurfaceType.Lakebed,
                WaterType.Sea => SurfaceType.Seabed,
                _ => SurfaceType.None
            };
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

    internal readonly struct WorldChunkGenerationTiming
    {
        public WorldChunkGenerationTiming(
            long hydrologyBatchMilliseconds,
            long worldFieldMilliseconds,
            long baseTerrainPatternMilliseconds,
            long hydrologyCompositionMilliseconds,
            long densitySurfaceMilliseconds,
            long densityFillMilliseconds,
            long totalMilliseconds,
            in HydrologyMetricsSnapshot hydrologyMetrics)
        {
            HydrologyBatchMilliseconds = hydrologyBatchMilliseconds;
            WorldFieldMilliseconds = worldFieldMilliseconds;
            BaseTerrainPatternMilliseconds = baseTerrainPatternMilliseconds;
            HydrologyCompositionMilliseconds = hydrologyCompositionMilliseconds;
            DensitySurfaceMilliseconds = densitySurfaceMilliseconds;
            DensityFillMilliseconds = densityFillMilliseconds;
            TotalMilliseconds = totalMilliseconds;
            HydrologyMetrics = hydrologyMetrics;
        }

        public long HydrologyBatchMilliseconds { get; }
        public long WorldFieldMilliseconds { get; }
        public long BaseTerrainPatternMilliseconds { get; }
        public long HydrologyCompositionMilliseconds { get; }
        public long DensitySurfaceMilliseconds { get; }
        public long DensityFillMilliseconds { get; }
        public long TotalMilliseconds { get; }
        public HydrologyMetricsSnapshot HydrologyMetrics { get; }
    }

    internal sealed class WorldGenerationTimingSummary
    {
        private int chunkCount;
        private long hydrologyBatchMilliseconds;
        private long worldFieldMilliseconds;
        private long baseTerrainPatternMilliseconds;
        private long hydrologyCompositionMilliseconds;
        private long densitySurfaceMilliseconds;
        private long densityFillMilliseconds;
        private long chunkTotalMilliseconds;
        private long worldApplyMilliseconds;
        private long pipelineTotalMilliseconds;
        private HydrologyMetricsSnapshot hydrologyMetrics;

        public void Add(in WorldChunkGenerationTiming timing)
        {
            chunkCount++;
            hydrologyBatchMilliseconds += timing.HydrologyBatchMilliseconds;
            worldFieldMilliseconds += timing.WorldFieldMilliseconds;
            baseTerrainPatternMilliseconds += timing.BaseTerrainPatternMilliseconds;
            hydrologyCompositionMilliseconds += timing.HydrologyCompositionMilliseconds;
            densitySurfaceMilliseconds += timing.DensitySurfaceMilliseconds;
            densityFillMilliseconds += timing.DensityFillMilliseconds;
            chunkTotalMilliseconds += timing.TotalMilliseconds;
            hydrologyMetrics = hydrologyMetrics.Add(timing.HydrologyMetrics);
        }

        public void SetPipelineTotal(long milliseconds) =>
            pipelineTotalMilliseconds = milliseconds;

        public void AddWorldApply(long milliseconds) =>
            worldApplyMilliseconds += milliseconds;

        public string ToInitialLog()
        {
            var average = chunkCount > 0
                ? chunkTotalMilliseconds / (double)chunkCount
                : 0d;
            return $"[WorldGenerationTiming] Initial terrain: chunks={chunkCount}, "
                + $"pipeline={pipelineTotalMilliseconds}ms, chunkSum={chunkTotalMilliseconds}ms, "
                + $"chunkAvg={average:F1}ms, hydrologyBatch={hydrologyBatchMilliseconds}ms, "
                + $"worldField={worldFieldMilliseconds}ms, "
                + $"baseTerrain={baseTerrainPatternMilliseconds}ms, "
                + $"hydrologyCompose={hydrologyCompositionMilliseconds}ms, "
                + $"densitySurface={densitySurfaceMilliseconds}ms, "
                + $"densityFill={densityFillMilliseconds}ms, "
                + $"worldApply={worldApplyMilliseconds}ms, "
                + $"hydrologyPlans=[{hydrologyMetrics.ToLogFragment()}]";
        }
    }

    internal static class WorldGenerationDiagnostics
    {
        public static void LogInitial(WorldGenerationTimingSummary timing)
        {
            if (timing == null)
            {
                throw new ArgumentNullException(nameof(timing));
            }

            UnityEngine.Debug.Log(timing.ToInitialLog());
        }

        public static void LogStreaming(
            ChunkCoordinate coordinate,
            in WorldChunkGenerationTiming timing,
            long worldApplyMilliseconds,
            bool wasApplied) =>
            UnityEngine.Debug.Log(
                $"[WorldGenerationTiming] Streaming chunk={coordinate.X},{coordinate.Z}, "
                + $"applied={wasApplied}, worldApply={worldApplyMilliseconds}ms, "
                + $"total={timing.TotalMilliseconds}ms, "
                + $"hydrologyBatch={timing.HydrologyBatchMilliseconds}ms, "
                + $"worldField={timing.WorldFieldMilliseconds}ms, "
                + $"baseTerrain={timing.BaseTerrainPatternMilliseconds}ms, "
                + $"hydrologyCompose={timing.HydrologyCompositionMilliseconds}ms, "
                + $"densitySurface={timing.DensitySurfaceMilliseconds}ms, "
                + $"densityFill={timing.DensityFillMilliseconds}ms, "
                + $"hydrologyPlans=[{timing.HydrologyMetrics.ToLogFragment()}]");
    }

    internal sealed class WorldChunkBuildData
    {
        private readonly WorldColumnBuildData[] columns;

        internal WorldChunkBuildData(
            WorldChunkBuildInput input,
            WorldColumnBuildData[] columns,
            in WorldChunkGenerationTiming timing)
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
            Timing = timing;
        }

        public ChunkCoordinate Coordinate { get; }
        public int ChunkSizeXZ { get; }
        public int ColumnCount => columns.Length;
        public WorldChunkGenerationTiming Timing { get; }

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

            var totalTimer = Stopwatch.StartNew();
            var stageTimer = Stopwatch.StartNew();
            var working = WorldFieldStage.Build(input);
            var worldFieldMilliseconds = stageTimer.ElapsedMilliseconds;
            stageTimer.Restart();
            BaseTerrainPatternStage.Build(working);
            var baseTerrainPatternMilliseconds = stageTimer.ElapsedMilliseconds;
            stageTimer.Restart();
            WorldPatternStage.Build(working);
            var hydrologyCompositionMilliseconds = stageTimer.ElapsedMilliseconds;
            stageTimer.Restart();
            DensitySurfaceStage.BuildFinalSurface(working);
            var densitySurfaceMilliseconds = stageTimer.ElapsedMilliseconds;
            stageTimer.Restart();
            DensityToFilledStage.Build(working);
            var densityFillMilliseconds = stageTimer.ElapsedMilliseconds;
            return working.Complete(new WorldChunkGenerationTiming(
                working.HydrologyBatchMilliseconds,
                worldFieldMilliseconds,
                baseTerrainPatternMilliseconds,
                hydrologyCompositionMilliseconds,
                densitySurfaceMilliseconds,
                densityFillMilliseconds,
                totalTimer.ElapsedMilliseconds,
                working.HydrologyMetrics));
        }
    }
}
