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
            float patternRegion,
            float continentalness,
            float erosion,
            float weirdness,
            float peaksValleys,
            float roughness,
            float detail,
            float seaRegion,
            float seaDetail)
        {
            PatternRegion = patternRegion;
            Continentalness = continentalness;
            Erosion = erosion;
            Weirdness = weirdness;
            PeaksValleys = peaksValleys;
            Roughness = roughness;
            Detail = detail;
            SeaRegion = seaRegion;
            SeaDetail = seaDetail;
        }

        public float PatternRegion { get; }
        public float Continentalness { get; }
        public float Erosion { get; }
        public float Weirdness { get; }
        public float PeaksValleys { get; }
        public float Roughness { get; }
        public float Detail { get; }
        public float SeaRegion { get; }
        public float SeaDetail { get; }
    }

    internal readonly struct WaterPatternContribution
    {
        public WaterPatternContribution(
            float targetBedSurfaceUnits,
            float depthUnits,
            float depthProgress,
            float seabedDetailUnits,
            int waterTopUnits,
            WaterType waterType,
            int waterRegionKey,
            float interiorProximity)
        {
            TargetBedSurfaceUnits = targetBedSurfaceUnits;
            DepthUnits = depthUnits;
            DepthProgress = depthProgress;
            SeabedDetailUnits = seabedDetailUnits;
            WaterTopUnits = waterTopUnits;
            WaterType = waterType;
            WaterRegionKey = waterRegionKey;
            InteriorProximity = interiorProximity;
        }

        public float TargetBedSurfaceUnits { get; }
        public float DepthUnits { get; }
        public float DepthProgress { get; }
        public float SeabedDetailUnits { get; }
        public int WaterTopUnits { get; }
        public WaterType WaterType { get; }
        public int WaterRegionKey { get; }
        public float InteriorProximity { get; }
        public bool HasWaterPattern => WaterType != WaterType.None;

        public static WaterPatternContribution SelectLowerBed(
            in WaterPatternContribution left,
            in WaterPatternContribution right)
        {
            if (!left.HasWaterPattern)
            {
                return right;
            }

            if (!right.HasWaterPattern)
            {
                return left;
            }

            return right.TargetBedSurfaceUnits
                    < left.TargetBedSurfaceUnits
                ? right
                : left;
        }
    }

    internal readonly struct WorldShapeProfile
    {
        public WorldShapeProfile(
            float surfaceOffsetUnits,
            float verticalFactor,
            float detailUnits,
            in WaterPatternContribution water)
        {
            SurfaceOffsetUnits = surfaceOffsetUnits;
            VerticalFactor = verticalFactor;
            DetailUnits = detailUnits;
            Water = water;
        }

        public float SurfaceOffsetUnits { get; }
        public float VerticalFactor { get; }
        public float DetailUnits { get; }
        public WaterPatternContribution Water { get; }
    }

    internal readonly struct LandformPatternContribution
    {
        public LandformPatternContribution(
            float surfaceOffsetUnits,
            float detailUnits)
        {
            SurfaceOffsetUnits = surfaceOffsetUnits;
            DetailUnits = detailUnits;
        }

        public float SurfaceOffsetUnits { get; }
        public float DetailUnits { get; }
    }

    internal readonly struct LandformPatternWeights
    {
        public LandformPatternWeights(
            float smooth,
            float rugged,
            float mountain,
            float canyon)
        {
            Smooth = smooth;
            Rugged = rugged;
            Mountain = mountain;
            Canyon = canyon;
        }

        public float Smooth { get; }
        public float Rugged { get; }
        public float Mountain { get; }
        public float Canyon { get; }
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
        private WorldShapeProfile[] worldShapeProfiles;
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
            worldShapeProfiles = new WorldShapeProfile[
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
        public bool HasWorldShapeProfile { get; private set; }
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

        public void SetWorldShapeProfile(
            int sampleLocalX,
            int sampleLocalZ,
            in WorldShapeProfile value)
        {
            EnsureNotCompleted();
            if (!HasWorldField)
            {
                throw new InvalidOperationException(
                    "World Field must be ready before its Shape Profile is built.");
            }

            if (HasWorldShapeProfile)
            {
                throw new InvalidOperationException(
                    "World Shape Profile has already been finalized.");
            }

            worldShapeProfiles[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)] = value;
        }

        public void CompleteWorldShapeProfile()
        {
            EnsureNotCompleted();
            if (!HasWorldField)
            {
                throw new InvalidOperationException(
                    "World Field must be ready before its Shape Profile is finalized.");
            }

            if (HasWorldShapeProfile)
            {
                throw new InvalidOperationException(
                    "World Shape Profile has already been finalized.");
            }

            HasWorldShapeProfile = true;
        }

        public WorldShapeProfile GetWorldShapeProfile(
            int sampleLocalX,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasWorldShapeProfile)
            {
                throw new InvalidOperationException(
                    "World Shape Profile is not ready.");
            }

            return worldShapeProfiles[ToSurfaceIndex(
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
            if (!HasWorldShapeProfile)
            {
                throw new InvalidOperationException(
                    "World Shape Profile must be ready before World Density is built.");
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
            if (!HasWorldShapeProfile)
            {
                throw new InvalidOperationException(
                    "World Shape Profile must be ready before World Density is finalized.");
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
                || !HasWorldShapeProfile
                || !HasWorldDensity)
            {
                throw new InvalidOperationException(
                    "World Field, World Shape Profile, and World Density must be ready before final output is transferred.");
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
            worldShapeProfiles = null;
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
        private readonly int patternRegionSeed;
        private readonly int continentalSeed;
        private readonly int erosionSeed;
        private readonly int weirdnessSeed;
        private readonly int peaksValleysSeed;
        private readonly int roughnessSeed;
        private readonly int detailSeed;
        private readonly int seaRegionSeed;
        private readonly int seaDetailSeed;
        private readonly int seaRegionKey;
        private readonly WorldNoiseRouterSettingsData settings;

        public WorldNoiseRouter(WorldSettingsData worldSettings)
        {
            patternRegionSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-pattern-region");
            continentalSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-continentalness");
            erosionSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-erosion");
            weirdnessSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-weirdness");
            peaksValleysSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-peaks-valleys");
            roughnessSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-roughness");
            detailSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-detail");
            seaRegionSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-sea-region");
            seaDetailSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-router-sea-detail");
            seaRegionKey = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "world-pattern-sea-primary-region");
            settings = worldSettings.WorldNoiseRouter;
        }

        public int SeaRegionKey => seaRegionKey;

        public WorldFieldSample Sample(int worldX, int worldZ) => new(
            SamplePatternRegion(worldX, worldZ),
            Sample2D(worldX, worldZ, settings.Continentalness, continentalSeed),
            Sample2D(worldX, worldZ, settings.Erosion, erosionSeed),
            Sample2D(worldX, worldZ, settings.Weirdness, weirdnessSeed),
            Sample2D(worldX, worldZ, settings.PeaksValleys, peaksValleysSeed),
            Sample2D(worldX, worldZ, settings.Roughness, roughnessSeed),
            Sample2D(worldX, worldZ, settings.Detail, detailSeed),
            Sample2D(worldX, worldZ, settings.SeaRegion, seaRegionSeed),
            Sample2D(worldX, worldZ, settings.SeaDetail, seaDetailSeed));

        public float SamplePatternRegion(int worldX, int worldZ) =>
            Sample2D(worldX, worldZ, settings.PatternRegion, patternRegionSeed);

        public float SampleContinentalness(int worldX, int worldZ) =>
            Sample2D(worldX, worldZ, settings.Continentalness, continentalSeed);

        public float SampleErosion(int worldX, int worldZ) =>
            Sample2D(worldX, worldZ, settings.Erosion, erosionSeed);

        public float SampleDetail3D(
            double worldX,
            double worldY,
            double worldZ) => WorldNoiseFieldSampler.Sample3D(
                worldX,
                worldY,
                worldZ,
                settings.Detail,
                detailSeed);

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
                throw new InvalidOperationException(
                    "World Field is not ready.");
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
                var worldX = checked(
                    working.SampleOriginX + sampleLocalX);
                var worldZ = checked(
                    working.SampleOriginZ + sampleLocalZ);
                var field = working.GetWorldField(
                    sampleLocalX,
                    sampleLocalZ);
                working.SetWorldShapeProfile(
                    sampleLocalX,
                    sampleLocalZ,
                    WorldPatternResolver.Resolve(
                        router,
                        worldX,
                        worldZ,
                        field,
                        settings,
                        out _));
            }

            working.CompleteWorldShapeProfile();
        }
    }

    internal static class WorldPatternResolver
    {
        public static WorldShapeProfile Resolve(
            in WorldNoiseRouter router,
            int worldX,
            int worldZ,
            in WorldFieldSample field,
            WorldSettingsData worldSettings,
            out LandformPatternWeights weights)
        {
            if (worldSettings == null)
            {
                throw new ArgumentNullException(nameof(worldSettings));
            }

            var settings = worldSettings.WorldPatterns;
            var smoothWeight = SmoothTerrainGenerator.EvaluateInfluence(
                field,
                settings.Smooth);
            var ruggedWeight = RuggedTerrainGenerator.EvaluateInfluence(
                field,
                settings.Rugged);
            var mountainWeight = MountainTerrainGenerator.EvaluateInfluence(
                field,
                settings.Mountain);
            var canyonWeight = CanyonTerrainGenerator.EvaluateInfluence(
                field,
                settings.Canyon);
            var canyonShape = CanyonTerrainGenerator.SampleShape(
                router,
                worldX,
                worldZ,
                field,
                settings.Canyon);
            var total = smoothWeight
                + ruggedWeight
                + mountainWeight
                + canyonWeight;
            if (!float.IsFinite(total) || total <= 0f)
            {
                throw new InvalidOperationException(
                    "Terrain pattern weights must produce a positive total.");
            }

            var inverseTotal = 1f / total;
            weights = new LandformPatternWeights(
                smoothWeight * inverseTotal,
                ruggedWeight * inverseTotal,
                mountainWeight * inverseTotal,
                canyonWeight * inverseTotal);

            var smooth = SmoothTerrainGenerator.Generate(
                field,
                settings.Smooth);
            var rugged = RuggedTerrainGenerator.Generate(
                field,
                settings.Rugged);
            var mountain = MountainTerrainGenerator.Generate(
                field,
                settings.Mountain);
            var canyon = CanyonTerrainGenerator.Generate(
                field,
                canyonShape,
                settings.Canyon);
            var baseDensity = settings.BaseDensity;
            var surfaceOffset =
                baseDensity.SurfaceByContinentalness.Evaluate(
                    field.Continentalness)
                + baseDensity.SurfaceByErosion.Evaluate(field.Erosion)
                + smooth.SurfaceOffsetUnits * weights.Smooth
                + rugged.SurfaceOffsetUnits * weights.Rugged
                + mountain.SurfaceOffsetUnits * weights.Mountain
                + canyon.SurfaceOffsetUnits * weights.Canyon;
            var detailUnits = baseDensity.DetailByRoughness.Evaluate(
                    field.Roughness)
                + smooth.DetailUnits * weights.Smooth
                + rugged.DetailUnits * weights.Rugged
                + mountain.DetailUnits * weights.Mountain
                + canyon.DetailUnits * weights.Canyon;
            var water = WaterPatternContribution.SelectLowerBed(
                default,
                SeaPatternGenerator.Generate(
                    router,
                    field,
                    settings.Sea));
            return WorldShapeComposer.Compose(
                surfaceOffset,
                worldSettings.TerrainBaseHeightUnits,
                baseDensity.VerticalFactorByErosion.Evaluate(field.Erosion),
                detailUnits,
                water);
        }
    }

    internal static class WorldShapeComposer
    {
        public static WorldShapeProfile Compose(
            float landformSurfaceOffsetUnits,
            int terrainBaseHeightUnits,
            float verticalFactor,
            float detailUnits,
            in WaterPatternContribution water)
        {
            if (!water.HasWaterPattern)
            {
                return new WorldShapeProfile(
                    landformSurfaceOffsetUnits,
                    verticalFactor,
                    detailUnits,
                    water);
            }

            var blend = Math.Clamp(water.InteriorProximity, 0f, 1f);
            var landformSurfaceUnits = terrainBaseHeightUnits
                + landformSurfaceOffsetUnits;
            var finalSurfaceUnits = Lerp(
                landformSurfaceUnits,
                water.TargetBedSurfaceUnits,
                blend);
            return new WorldShapeProfile(
                finalSurfaceUnits - terrainBaseHeightUnits,
                verticalFactor,
                Lerp(detailUnits, 0f, blend),
                water);
        }

        private static float Lerp(float from, float to, float amount) =>
            from + (to - from) * amount;
    }

    internal static class SeaPatternGenerator
    {
        public static WaterPatternContribution Generate(
            in WorldNoiseRouter router,
            in WorldFieldSample field,
            in SeaPatternSettingsData settings)
        {
            var interior = Math.Clamp(
                settings.InteriorByRegion.Evaluate(field.SeaRegion),
                0f,
                1f);
            if (interior <= 0f)
            {
                return default;
            }

            var depthProgress = Math.Clamp(
                settings.DepthByInterior.EvaluateMonotonic(interior),
                0f,
                1f);
            var maximumDepthUnits = settings.MaximumDepthCells
                * WorldGrid.HeightStepsPerCell;
            var depthUnits = depthProgress * maximumDepthUnits;
            var seabedDetailUnits = (field.SeaDetail * 0.5f + 0.5f)
                * settings.ShapeDetailStrength
                * depthUnits;
            var targetBedSurfaceUnits = settings.SurfaceUnits
                - depthUnits
                + seabedDetailUnits;
            return new WaterPatternContribution(
                targetBedSurfaceUnits,
                depthUnits,
                depthProgress,
                seabedDetailUnits,
                settings.SurfaceUnits,
                WaterType.Sea,
                router.SeaRegionKey,
                interior);
        }
    }

    internal static class SmoothTerrainGenerator
    {
        public static float EvaluateInfluence(
            in WorldFieldSample field,
            in SmoothTerrainSettingsData settings) =>
            settings.InfluenceByRegion.Evaluate(field.PatternRegion);

        public static LandformPatternContribution Generate(
            in WorldFieldSample field,
            in SmoothTerrainSettingsData settings) => new(
                settings.UndulationByWeirdness.Evaluate(field.Weirdness),
                settings.DetailByRoughness.Evaluate(field.Roughness));
    }

    internal static class RuggedTerrainGenerator
    {
        public static float EvaluateInfluence(
            in WorldFieldSample field,
            in RuggedTerrainSettingsData settings) =>
            settings.InfluenceByRegion.Evaluate(field.PatternRegion);

        public static LandformPatternContribution Generate(
            in WorldFieldSample field,
            in RuggedTerrainSettingsData settings) => new(
                settings.ReliefByPeaksValleys.Evaluate(field.PeaksValleys)
                * settings.ReliefScaleByRoughness.Evaluate(field.Roughness),
                settings.DetailByRoughness.Evaluate(field.Roughness));
    }

    internal static class MountainTerrainGenerator
    {
        public static float EvaluateInfluence(
            in WorldFieldSample field,
            in MountainTerrainSettingsData settings) =>
            settings.InfluenceByRegion.Evaluate(field.PatternRegion);

        public static LandformPatternContribution Generate(
            in WorldFieldSample field,
            in MountainTerrainSettingsData settings)
        {
            var centerProximity = Math.Clamp(
                settings.CenterProximityByRegion.Evaluate(
                    field.PatternRegion),
                0f,
                1f);
            var progressExponent = settings.ProgressExponentByErosion
                .Evaluate(field.Erosion);
            var warpedProgress = MathF.Pow(
                centerProximity,
                progressExponent);
            var height = settings.HeightByCenterProximity.EvaluateMonotonic(
                warpedProgress);
            return new LandformPatternContribution(
                height,
                0f);
        }
    }

    internal static class CanyonTerrainGenerator
    {
        internal readonly struct ShapeSample
        {
            public ShapeSample(float axisProximity)
            {
                AxisProximity = axisProximity;
            }

            public float AxisProximity { get; }
        }

        public static float EvaluateInfluence(
            in WorldFieldSample field,
            in CanyonTerrainSettingsData settings) =>
            settings.InfluenceByRegion.Evaluate(field.PatternRegion);

        public static ShapeSample SampleShape(
            in WorldNoiseRouter router,
            int worldX,
            int worldZ,
            in WorldFieldSample field,
            in CanyonTerrainSettingsData settings)
        {
            var axisValue = field.Continentalness - field.Erosion;
            var left = router.SampleContinentalness(worldX - 1, worldZ)
                - router.SampleErosion(worldX - 1, worldZ);
            var right = router.SampleContinentalness(worldX + 1, worldZ)
                - router.SampleErosion(worldX + 1, worldZ);
            var back = router.SampleContinentalness(worldX, worldZ - 1)
                - router.SampleErosion(worldX, worldZ - 1);
            var forward = router.SampleContinentalness(worldX, worldZ + 1)
                - router.SampleErosion(worldX, worldZ + 1);
            var gradientX = (right - left) * 0.5f;
            var gradientZ = (forward - back) * 0.5f;
            var gradientLength = MathF.Sqrt(
                gradientX * gradientX + gradientZ * gradientZ);
            var width = settings.WidthByVariation.Evaluate(field.Erosion);
            var axisProximity = gradientLength > float.Epsilon
                ? 1f - Math.Clamp(
                    MathF.Abs(axisValue) / gradientLength / width,
                    0f,
                    1f)
                : 0f;
            return new ShapeSample(axisProximity);
        }

        public static LandformPatternContribution Generate(
            in WorldFieldSample field,
            in ShapeSample shape,
            in CanyonTerrainSettingsData settings)
        {
            var maximumDepth = settings.MaximumDepthByVariation.Evaluate(
                field.PatternRegion);
            var depthShape = SmootherStep(shape.AxisProximity);
            return new LandformPatternContribution(
                -maximumDepth * depthShape,
                0f);
        }

        private static float SmootherStep(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return value * value * value
                * (value * (value * 6f - 15f) + 10f);
        }
    }

    internal static class WorldDensityStage
    {
        public static void Build(GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }

            if (!working.HasWorldShapeProfile)
            {
                throw new InvalidOperationException(
                    "World Shape Profile is not ready.");
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
                var profile = working.GetWorldShapeProfile(
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
            in WorldShapeProfile profile) =>
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
            in WorldShapeProfile profile,
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
                    (int)MathF.Round(
                        working.GetFinalSurfaceUnits(
                            sampleLocalX,
                            sampleLocalZ),
                        MidpointRounding.AwayFromZero),
                    0,
                    working.Input.HeightUnitCount);
                var water = working.GetWorldShapeProfile(
                    sampleLocalX,
                    sampleLocalZ).Water;
                var waterSurfaceUnits = water.HasWaterPattern
                    && water.WaterTopUnits > solidHeightUnits
                        ? Math.Clamp(
                            water.WaterTopUnits,
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
                        hasWater ? water.WaterType : WaterType.None,
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
