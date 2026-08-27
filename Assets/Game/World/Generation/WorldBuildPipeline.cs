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

    internal readonly struct TerrainFieldSample
    {
        public TerrainFieldSample(
            float patternRegion,
            float continentalness,
            float erosion,
            float weirdness,
            float peaksValleys,
            float roughness,
            float detail)
        {
            PatternRegion = patternRegion;
            Continentalness = continentalness;
            Erosion = erosion;
            Weirdness = weirdness;
            PeaksValleys = peaksValleys;
            Roughness = roughness;
            Detail = detail;
        }

        public float PatternRegion { get; }
        public float Continentalness { get; }
        public float Erosion { get; }
        public float Weirdness { get; }
        public float PeaksValleys { get; }
        public float Roughness { get; }
        public float Detail { get; }
    }

    internal readonly struct TerrainDensityProfile
    {
        public TerrainDensityProfile(
            float surfaceOffsetUnits,
            float verticalFactor,
            float detailUnits)
        {
            SurfaceOffsetUnits = surfaceOffsetUnits;
            VerticalFactor = verticalFactor;
            DetailUnits = detailUnits;
        }

        public float SurfaceOffsetUnits { get; }
        public float VerticalFactor { get; }
        public float DetailUnits { get; }
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

    internal readonly struct TerrainPatternWeights
    {
        public TerrainPatternWeights(
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

    internal readonly struct TerrainDensityContributions
    {
        public TerrainDensityContributions(
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
        private TerrainFieldSample[] terrainFieldSamples;
        private TerrainDensityProfile[] terrainDensityProfiles;
        private float[] preliminaryDensity;
        private float[] preliminarySurfaceUnits;
        private float[] finalDensity;
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
            terrainFieldSamples = new TerrainFieldSample[checked(
                SampleSizeXZ * SampleSizeXZ)];
            terrainDensityProfiles = new TerrainDensityProfile[
                terrainFieldSamples.Length];
            preliminaryDensity = new float[checked(
                SampleSizeXZ
                * SampleSizeXZ
                * DensityHeightUnitCount)];
            preliminarySurfaceUnits = new float[terrainFieldSamples.Length];
            finalDensity = new float[preliminaryDensity.Length];
            finalSurfaceUnits = new float[preliminarySurfaceUnits.Length];
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
        public bool HasTerrainField { get; private set; }
        public bool HasTerrainDensityProfile { get; private set; }
        public bool HasPreliminaryDensity { get; private set; }
        public bool HasPreliminarySurface { get; private set; }
        public bool HasFinalDensity { get; private set; }
        public bool HasFinalSurface { get; private set; }
        public bool IsCompleted => finalColumns == null;

        public void SetTerrainField(
            int sampleLocalX,
            int sampleLocalZ,
            in TerrainFieldSample value)
        {
            EnsureNotCompleted();
            if (HasTerrainField)
            {
                throw new InvalidOperationException(
                    "Terrain Field has already been finalized.");
            }

            terrainFieldSamples[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)] =
                value;
        }

        public void CompleteTerrainField()
        {
            EnsureNotCompleted();
            if (HasTerrainField)
            {
                throw new InvalidOperationException(
                    "Terrain Field has already been finalized.");
            }

            HasTerrainField = true;
        }

        public TerrainFieldSample GetTerrainField(
            int sampleLocalX,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasTerrainField)
            {
                throw new InvalidOperationException(
                    "Terrain Field is not ready.");
            }

            return terrainFieldSamples[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)];
        }

        public void SetTerrainDensityProfile(
            int sampleLocalX,
            int sampleLocalZ,
            in TerrainDensityProfile value)
        {
            EnsureNotCompleted();
            if (!HasTerrainField)
            {
                throw new InvalidOperationException(
                    "Terrain Field must be ready before its Density Profile is built.");
            }

            if (HasTerrainDensityProfile)
            {
                throw new InvalidOperationException(
                    "Terrain Density Profile has already been finalized.");
            }

            terrainDensityProfiles[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)] = value;
        }

        public void CompleteTerrainDensityProfile()
        {
            EnsureNotCompleted();
            if (!HasTerrainField)
            {
                throw new InvalidOperationException(
                    "Terrain Field must be ready before its Density Profile is finalized.");
            }

            if (HasTerrainDensityProfile)
            {
                throw new InvalidOperationException(
                    "Terrain Density Profile has already been finalized.");
            }

            HasTerrainDensityProfile = true;
        }

        public TerrainDensityProfile GetTerrainDensityProfile(
            int sampleLocalX,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasTerrainDensityProfile)
            {
                throw new InvalidOperationException(
                    "Terrain Density Profile is not ready.");
            }

            return terrainDensityProfiles[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)];
        }

        public void SetPreliminaryDensity(
            int sampleLocalX,
            int heightUnit,
            int sampleLocalZ,
            float density)
        {
            EnsureNotCompleted();
            if (!HasTerrainDensityProfile)
            {
                throw new InvalidOperationException(
                    "Terrain Density Profile must be ready before Preliminary Density is built.");
            }

            if (HasPreliminaryDensity)
            {
                throw new InvalidOperationException(
                    "Preliminary Density has already been finalized.");
            }

            if (!float.IsFinite(density))
            {
                throw new ArgumentOutOfRangeException(nameof(density));
            }

            preliminaryDensity[ToDensityIndex(
                sampleLocalX,
                heightUnit,
                sampleLocalZ)] = density;
        }

        public void CompletePreliminaryDensity()
        {
            EnsureNotCompleted();
            if (!HasTerrainDensityProfile)
            {
                throw new InvalidOperationException(
                    "Terrain Density Profile must be ready before Preliminary Density is finalized.");
            }

            if (HasPreliminaryDensity)
            {
                throw new InvalidOperationException(
                    "Preliminary Density has already been finalized.");
            }

            HasPreliminaryDensity = true;
        }

        public float GetPreliminaryDensity(
            int sampleLocalX,
            int heightUnit,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasPreliminaryDensity)
            {
                throw new InvalidOperationException(
                    "Preliminary Density is not ready.");
            }

            return preliminaryDensity[ToDensityIndex(
                sampleLocalX,
                heightUnit,
                sampleLocalZ)];
        }

        public void SetPreliminarySurfaceUnits(
            int sampleLocalX,
            int sampleLocalZ,
            float surfaceUnits)
        {
            EnsureNotCompleted();
            if (!HasPreliminaryDensity)
            {
                throw new InvalidOperationException(
                    "Preliminary Density must be ready before its surface is extracted.");
            }

            if (HasPreliminarySurface)
            {
                throw new InvalidOperationException(
                    "Preliminary Surface has already been finalized.");
            }

            if (!float.IsFinite(surfaceUnits)
                || surfaceUnits < 0f
                || surfaceUnits > Input.HeightUnitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(surfaceUnits));
            }

            preliminarySurfaceUnits[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)] = surfaceUnits;
        }

        public void CompletePreliminarySurface()
        {
            EnsureNotCompleted();
            if (!HasPreliminaryDensity)
            {
                throw new InvalidOperationException(
                    "Preliminary Density must be ready before its surface is finalized.");
            }

            if (HasPreliminarySurface)
            {
                throw new InvalidOperationException(
                    "Preliminary Surface has already been finalized.");
            }

            HasPreliminarySurface = true;
        }

        public float GetPreliminarySurfaceUnits(
            int sampleLocalX,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasPreliminarySurface)
            {
                throw new InvalidOperationException(
                    "Preliminary Surface is not ready.");
            }

            return preliminarySurfaceUnits[ToSurfaceIndex(
                sampleLocalX,
                sampleLocalZ)];
        }

        public void SetFinalDensity(
            int sampleLocalX,
            int heightUnit,
            int sampleLocalZ,
            float density)
        {
            EnsureNotCompleted();
            if (!HasPreliminarySurface)
            {
                throw new InvalidOperationException(
                    "Preliminary Surface must be ready before Final Density is built.");
            }

            if (HasFinalDensity)
            {
                throw new InvalidOperationException(
                    "Final Density has already been finalized.");
            }

            if (!float.IsFinite(density))
            {
                throw new ArgumentOutOfRangeException(nameof(density));
            }

            finalDensity[ToDensityIndex(
                sampleLocalX,
                heightUnit,
                sampleLocalZ)] = density;
        }

        public void CompleteFinalDensity()
        {
            EnsureNotCompleted();
            if (!HasPreliminarySurface)
            {
                throw new InvalidOperationException(
                    "Preliminary Surface must be ready before Final Density is finalized.");
            }

            if (HasFinalDensity)
            {
                throw new InvalidOperationException(
                    "Final Density has already been finalized.");
            }

            HasFinalDensity = true;
        }

        public float GetFinalDensity(
            int sampleLocalX,
            int heightUnit,
            int sampleLocalZ)
        {
            EnsureNotCompleted();
            if (!HasFinalDensity)
            {
                throw new InvalidOperationException(
                    "Final Density is not ready.");
            }

            return finalDensity[ToDensityIndex(
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
            if (!HasFinalDensity)
            {
                throw new InvalidOperationException(
                    "Final Density must be ready before Final Surface is extracted.");
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
            if (!HasFinalDensity)
            {
                throw new InvalidOperationException(
                    "Final Density must be ready before Final Surface is finalized.");
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
            if (!HasTerrainField
                || !HasTerrainDensityProfile
                || !HasPreliminaryDensity)
            {
                throw new InvalidOperationException(
                    "Terrain Field, Terrain Density Profile, and Preliminary Density must be ready before final output is transferred.");
            }

            if (!HasPreliminarySurface)
            {
                throw new InvalidOperationException(
                    "Preliminary Surface must be ready before final output is transferred.");
            }

            if (!HasFinalDensity || !HasFinalSurface)
            {
                throw new InvalidOperationException(
                    "Final Density and Final Surface must be ready before output is transferred.");
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
            terrainFieldSamples = null;
            terrainDensityProfiles = null;
            preliminaryDensity = null;
            preliminarySurfaceUnits = null;
            finalDensity = null;
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

    internal static class TerrainFieldStage
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
            var router = new TerrainNoiseRouter(input.Settings);
            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                var worldX = checked(working.SampleOriginX + sampleLocalX);
                var worldZ = checked(working.SampleOriginZ + sampleLocalZ);
                working.SetTerrainField(
                    sampleLocalX,
                    sampleLocalZ,
                    router.Sample(worldX, worldZ));
            }

            working.CompleteTerrainField();
            return working;
        }
    }

    internal static class TerrainNoiseFieldSampler
    {
        public static float Sample2D(
            long worldX,
            long worldZ,
            in TerrainNoiseFieldSettingsData field,
            int seed)
        {
            var sample = field.Mode is TerrainNoiseMode.Ridge
                or TerrainNoiseMode.SignedRidge
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
            return field.Mode is TerrainNoiseMode.Signed
                or TerrainNoiseMode.SignedRidge
                ? sample * 2f - 1f
                : sample;
        }

        public static float Sample3D(
            double worldX,
            double worldY,
            double worldZ,
            in TerrainNoiseFieldSettingsData field,
            int seed)
        {
            var sample = field.Mode is TerrainNoiseMode.Ridge
                or TerrainNoiseMode.SignedRidge
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
            return field.Mode is TerrainNoiseMode.Signed
                or TerrainNoiseMode.SignedRidge
                ? sample * 2f - 1f
                : sample;
        }

    }

    internal readonly struct TerrainNoiseRouter
    {
        private readonly int patternRegionSeed;
        private readonly int continentalSeed;
        private readonly int erosionSeed;
        private readonly int weirdnessSeed;
        private readonly int peaksValleysSeed;
        private readonly int roughnessSeed;
        private readonly int detailSeed;
        private readonly TerrainNoiseRouterSettingsData settings;

        public TerrainNoiseRouter(WorldSettingsData worldSettings)
        {
            patternRegionSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "terrain-router-pattern-region");
            continentalSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "terrain-router-continentalness");
            erosionSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "terrain-router-erosion");
            weirdnessSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "terrain-router-weirdness");
            peaksValleysSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "terrain-router-peaks-valleys");
            roughnessSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "terrain-router-roughness");
            detailSeed = DeterministicNoise.DeriveSeed(
                worldSettings.Seed,
                "terrain-router-detail");
            settings = worldSettings.TerrainNoiseRouter;
        }

        public TerrainFieldSample Sample(int worldX, int worldZ) => new(
                SamplePatternRegion(worldX, worldZ),
                TerrainNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                    settings.Continentalness,
                    continentalSeed),
                TerrainNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                    settings.Erosion,
                    erosionSeed),
                TerrainNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                    settings.Weirdness,
                    weirdnessSeed),
                TerrainNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                    settings.PeaksValleys,
                    peaksValleysSeed),
                TerrainNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                    settings.Roughness,
                    roughnessSeed),
                TerrainNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                settings.Detail,
                detailSeed));

        public float SamplePatternRegion(int worldX, int worldZ) =>
            TerrainNoiseFieldSampler.Sample2D(
                worldX,
                worldZ,
                settings.PatternRegion,
                patternRegionSeed);

        public float SampleContinentalness(int worldX, int worldZ) =>
            TerrainNoiseFieldSampler.Sample2D(
                worldX,
                worldZ,
                settings.Continentalness,
                continentalSeed);

        public float SampleErosion(int worldX, int worldZ) =>
            TerrainNoiseFieldSampler.Sample2D(
                worldX,
                worldZ,
                settings.Erosion,
                erosionSeed);

        public float SampleDetail3D(
            double worldX,
            double worldY,
            double worldZ) => TerrainNoiseFieldSampler.Sample3D(
                worldX,
                worldY,
                worldZ,
                settings.Detail,
                detailSeed);
    }

    internal static class TerrainPatternStage
    {
        public static void Build(GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }

            if (!working.HasTerrainField)
            {
                throw new InvalidOperationException(
                    "Terrain Field is not ready.");
            }

            var settings = working.Input.Settings;
            var router = new TerrainNoiseRouter(settings);
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
                var field = working.GetTerrainField(
                    sampleLocalX,
                    sampleLocalZ);
                working.SetTerrainDensityProfile(
                    sampleLocalX,
                    sampleLocalZ,
                    TerrainPatternResolver.Resolve(
                        router,
                        worldX,
                        worldZ,
                        field,
                        settings.TerrainPatterns,
                        out _));
            }

            working.CompleteTerrainDensityProfile();
        }
    }

    internal static class TerrainPatternResolver
    {
        public static TerrainDensityProfile Resolve(
            in TerrainNoiseRouter router,
            int worldX,
            int worldZ,
            in TerrainFieldSample field,
            in TerrainPatternSettingsData settings,
            out TerrainPatternWeights weights)
        {
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
            weights = new TerrainPatternWeights(
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
            return new TerrainDensityProfile(
                surfaceOffset,
                baseDensity.VerticalFactorByErosion.Evaluate(field.Erosion),
                detailUnits);
        }
    }

    internal static class SmoothTerrainGenerator
    {
        public static float EvaluateInfluence(
            in TerrainFieldSample field,
            in SmoothTerrainSettingsData settings) =>
            settings.InfluenceByRegion.Evaluate(field.PatternRegion);

        public static TerrainPatternContribution Generate(
            in TerrainFieldSample field,
            in SmoothTerrainSettingsData settings) => new(
                settings.UndulationByWeirdness.Evaluate(field.Weirdness),
                settings.DetailByRoughness.Evaluate(field.Roughness));
    }

    internal static class RuggedTerrainGenerator
    {
        public static float EvaluateInfluence(
            in TerrainFieldSample field,
            in RuggedTerrainSettingsData settings) =>
            settings.InfluenceByRegion.Evaluate(field.PatternRegion);

        public static TerrainPatternContribution Generate(
            in TerrainFieldSample field,
            in RuggedTerrainSettingsData settings) => new(
                settings.ReliefByPeaksValleys.Evaluate(field.PeaksValleys)
                * settings.ReliefScaleByRoughness.Evaluate(field.Roughness),
                settings.DetailByRoughness.Evaluate(field.Roughness));
    }

    internal static class MountainTerrainGenerator
    {
        public static float EvaluateInfluence(
            in TerrainFieldSample field,
            in MountainTerrainSettingsData settings) =>
            settings.InfluenceByRegion.Evaluate(field.PatternRegion);

        public static TerrainPatternContribution Generate(
            in TerrainFieldSample field,
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
            return new TerrainPatternContribution(
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
            in TerrainFieldSample field,
            in CanyonTerrainSettingsData settings) =>
            settings.InfluenceByRegion.Evaluate(field.PatternRegion);

        public static ShapeSample SampleShape(
            in TerrainNoiseRouter router,
            int worldX,
            int worldZ,
            in TerrainFieldSample field,
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

        public static TerrainPatternContribution Generate(
            in TerrainFieldSample field,
            in ShapeSample shape,
            in CanyonTerrainSettingsData settings)
        {
            var maximumDepth = settings.MaximumDepthByVariation.Evaluate(
                field.PatternRegion);
            var depthShape = SmootherStep(shape.AxisProximity);
            return new TerrainPatternContribution(
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

    internal static class PreliminaryTerrainDensityStage
    {
        public static void Build(GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }

            if (!working.HasTerrainDensityProfile)
            {
                throw new InvalidOperationException(
                    "Terrain Density Profile is not ready.");
            }

            var field = new PreliminaryTerrainDensityField(
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
                var fieldSample = working.GetTerrainField(
                    sampleLocalX,
                    sampleLocalZ);
                var profile = working.GetTerrainDensityProfile(
                    sampleLocalX,
                    sampleLocalZ);
                for (var heightUnit = 0;
                     heightUnit < working.DensityHeightUnitCount;
                     heightUnit++)
                {
                    working.SetPreliminaryDensity(
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

            working.CompletePreliminaryDensity();
        }
    }

    internal readonly struct PreliminaryTerrainDensityField
    {
        private readonly TerrainNoiseRouter noiseRouter;
        private readonly int terrainBaseHeightUnits;
        private readonly int maximumHeightUnit;

        public PreliminaryTerrainDensityField(WorldSettingsData settings)
        {
            noiseRouter = new TerrainNoiseRouter(settings);
            terrainBaseHeightUnits = settings.TerrainBaseHeightUnits;
            maximumHeightUnit = checked(
                settings.WorldHeight * WorldGrid.HeightStepsPerCell);
        }

        public float Sample(
            int worldX,
            int heightUnit,
            int worldZ,
            in TerrainFieldSample field,
            in TerrainDensityProfile profile) =>
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
            in TerrainFieldSample field,
            in TerrainDensityProfile profile,
            out TerrainDensityContributions contributions)
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
            contributions = new TerrainDensityContributions(
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
        public static void BuildPreliminarySurface(
            GenerationWorkingData working)
        {
            ValidateWorkingData(working);
            if (!working.HasPreliminaryDensity)
            {
                throw new InvalidOperationException(
                    "Preliminary Density is not ready.");
            }

            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                working.SetPreliminarySurfaceUnits(
                    sampleLocalX,
                    sampleLocalZ,
                    FindSurfaceUnits(
                        working,
                        sampleLocalX,
                        sampleLocalZ,
                        useFinalDensity: false));
            }

            working.CompletePreliminarySurface();
        }

        public static void BuildFinalSurface(
            GenerationWorkingData working)
        {
            ValidateWorkingData(working);
            if (!working.HasFinalDensity)
            {
                throw new InvalidOperationException(
                    "Final Density is not ready.");
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
                        sampleLocalZ,
                        useFinalDensity: true));
            }

            working.CompleteFinalSurface();
        }

        private static float FindSurfaceUnits(
            GenerationWorkingData working,
            int sampleLocalX,
            int sampleLocalZ,
            bool useFinalDensity)
        {
            var topUnit = working.Input.HeightUnitCount;
            if (GetDensity(
                    working,
                    sampleLocalX,
                    topUnit,
                    sampleLocalZ,
                    useFinalDensity) >= 0f)
            {
                return topUnit;
            }

            if (GetDensity(
                    working,
                    sampleLocalX,
                    0,
                    sampleLocalZ,
                    useFinalDensity) < 0f)
            {
                return 0;
            }

            for (var lowerUnit = working.Input.HeightUnitCount - 1;
                 lowerUnit >= 0;
                 lowerUnit--)
            {
                var lowerDensity = GetDensity(
                    working,
                    sampleLocalX,
                    lowerUnit,
                    sampleLocalZ,
                    useFinalDensity);
                var upperDensity = GetDensity(
                    working,
                    sampleLocalX,
                    lowerUnit + 1,
                    sampleLocalZ,
                    useFinalDensity);
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

        private static float GetDensity(
            GenerationWorkingData working,
            int sampleLocalX,
            int heightUnit,
            int sampleLocalZ,
            bool useFinalDensity) =>
            useFinalDensity
                ? working.GetFinalDensity(
                    sampleLocalX,
                    heightUnit,
                    sampleLocalZ)
                : working.GetPreliminaryDensity(
                    sampleLocalX,
                    heightUnit,
                    sampleLocalZ);

        private static void ValidateWorkingData(
            GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }
        }
    }

    internal static class TerrainDensityFinalizationStage
    {
        public static void Build(GenerationWorkingData working)
        {
            if (working == null)
            {
                throw new ArgumentNullException(nameof(working));
            }

            if (!working.HasPreliminarySurface)
            {
                throw new InvalidOperationException(
                    "Preliminary Surface is not ready.");
            }

            for (var sampleLocalZ = 0;
                 sampleLocalZ < working.SampleSizeXZ;
                 sampleLocalZ++)
            for (var sampleLocalX = 0;
                 sampleLocalX < working.SampleSizeXZ;
                 sampleLocalX++)
            {
                for (var heightUnit = 0;
                     heightUnit < working.DensityHeightUnitCount;
                     heightUnit++)
                {
                    working.SetFinalDensity(
                        sampleLocalX,
                        heightUnit,
                        sampleLocalZ,
                        working.GetPreliminaryDensity(
                            sampleLocalX,
                            heightUnit,
                            sampleLocalZ));
                }
            }

            working.CompleteFinalDensity();
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
                working.SetFinalColumn(
                    localX,
                    localZ,
                    new WorldColumnBuildData(
                        solidHeightUnits,
                        0,
                        WaterRole.None,
                        WaterType.None,
                        SurfaceType.None,
                        solidHeightUnits > 0
                            ? SurfaceType.Ground
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

            var working = TerrainFieldStage.Build(input);
            TerrainPatternStage.Build(working);
            PreliminaryTerrainDensityStage.Build(working);
            DensitySurfaceStage.BuildPreliminarySurface(working);
            TerrainDensityFinalizationStage.Build(working);
            DensitySurfaceStage.BuildFinalSurface(working);
            DensityToFilledStage.Build(working);
            return working.Complete();
        }
    }
}
