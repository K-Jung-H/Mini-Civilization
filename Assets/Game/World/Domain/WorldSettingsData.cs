using System;

namespace MiniCivilization.World.Domain
{
    public enum WorldType : byte
    {
        Finite,
        Infinite
    }

    public enum WorldNoiseMode : byte
    {
        Value,
        Signed,
        Ridge,
        SignedRidge
    }

    public readonly struct WorldNoiseFieldSettingsData
    {
        public WorldNoiseFieldSettingsData(
            WorldNoiseMode mode,
            float scale,
            int layers,
            float frequencySpacing,
            float persistence)
        {
            if (!Enum.IsDefined(typeof(WorldNoiseMode), mode)
                || !float.IsFinite(scale)
                || scale <= 0f
                || layers <= 0
                || !float.IsFinite(frequencySpacing)
                || frequencySpacing < 1f
                || !float.IsFinite(persistence)
                || persistence <= 0f
                || persistence >= 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scale),
                    "Terrain Noise Field settings are invalid.");
            }

            Mode = mode;
            Scale = scale;
            Layers = layers;
            FrequencySpacing = frequencySpacing;
            Persistence = persistence;
        }

        public WorldNoiseMode Mode { get; }
        public float Scale { get; }
        public int Layers { get; }
        public float FrequencySpacing { get; }
        public float Persistence { get; }
    }

    public readonly struct WorldCurveSettingsData
    {
        public WorldCurveSettingsData(
            float atZero,
            float atQuarter,
            float atHalf,
            float atThreeQuarters,
            float atOne)
        {
            if (!float.IsFinite(atZero)
                || !float.IsFinite(atQuarter)
                || !float.IsFinite(atHalf)
                || !float.IsFinite(atThreeQuarters)
                || !float.IsFinite(atOne))
            {
                throw new ArgumentOutOfRangeException(nameof(atZero));
            }

            AtZero = atZero;
            AtQuarter = atQuarter;
            AtHalf = atHalf;
            AtThreeQuarters = atThreeQuarters;
            AtOne = atOne;
        }

        public float AtZero { get; }
        public float AtQuarter { get; }
        public float AtHalf { get; }
        public float AtThreeQuarters { get; }
        public float AtOne { get; }

        public float Evaluate(float input)
        {
            input = Math.Clamp(input, 0f, 1f);
            var scaled = input * 4f;
            var segment = Math.Min(3, (int)scaled);
            var amount = scaled - segment;
            amount = amount * amount * (3f - 2f * amount);
            return segment switch
            {
                0 => Lerp(AtZero, AtQuarter, amount),
                1 => Lerp(AtQuarter, AtHalf, amount),
                2 => Lerp(AtHalf, AtThreeQuarters, amount),
                _ => Lerp(AtThreeQuarters, AtOne, amount)
            };
        }

        public float EvaluateMonotonic(float input)
        {
            input = Math.Clamp(input, 0f, 1f);
            var scaled = input * 4f;
            var segment = Math.Min(3, (int)scaled);
            var amount = scaled - segment;
            var from = GetValue(segment);
            var to = GetValue(segment + 1);
            var delta = to - from;
            var fromTangent = segment == 0
                ? delta
                : MonotoneTangent(
                    from - GetValue(segment - 1),
                    delta);
            var toTangent = segment == 3
                ? delta
                : MonotoneTangent(
                    delta,
                    GetValue(segment + 2) - to);
            var amount2 = amount * amount;
            var amount3 = amount2 * amount;
            return (2f * amount3 - 3f * amount2 + 1f) * from
                + (amount3 - 2f * amount2 + amount) * fromTangent
                + (-2f * amount3 + 3f * amount2) * to
                + (amount3 - amount2) * toTangent;
        }

        private float GetValue(int index) => index switch
        {
            0 => AtZero,
            1 => AtQuarter,
            2 => AtHalf,
            3 => AtThreeQuarters,
            4 => AtOne,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        private static float MonotoneTangent(float before, float after) =>
            before <= 0f || after <= 0f
                ? 0f
                : 2f * before * after / (before + after);

        private static float Lerp(float from, float to, float amount) =>
            from + (to - from) * amount;
    }

    public readonly struct WorldNoiseRouterSettingsData
    {
        public WorldNoiseRouterSettingsData(
            WorldNoiseFieldSettingsData continentalness,
            WorldNoiseFieldSettingsData erosion,
            WorldNoiseFieldSettingsData weirdness,
            WorldNoiseFieldSettingsData peaksValleys,
            WorldNoiseFieldSettingsData roughness,
            WorldNoiseFieldSettingsData detail,
            WorldNoiseFieldSettingsData seaDetail)
        {
            Continentalness = continentalness;
            Erosion = erosion;
            Weirdness = weirdness;
            PeaksValleys = peaksValleys;
            Roughness = roughness;
            Detail = detail;
            SeaDetail = seaDetail;
        }

        public WorldNoiseFieldSettingsData Continentalness { get; }
        public WorldNoiseFieldSettingsData Erosion { get; }
        public WorldNoiseFieldSettingsData Weirdness { get; }
        public WorldNoiseFieldSettingsData PeaksValleys { get; }
        public WorldNoiseFieldSettingsData Roughness { get; }
        public WorldNoiseFieldSettingsData Detail { get; }
        public WorldNoiseFieldSettingsData SeaDetail { get; }
    }

    public readonly struct WorldPatternRegionSettingsData
    {
        public WorldPatternRegionSettingsData(
            int sizeCells,
            float centerJitter,
            float warpScale,
            float warpStrengthCells,
            float boundaryBlendCells,
            float smoothShare,
            float ruggedShare,
            float mountainShare,
            float canyonShare,
            float seaShare)
        {
            SizeCells = sizeCells;
            CenterJitter = centerJitter;
            WarpScale = warpScale;
            WarpStrengthCells = warpStrengthCells;
            BoundaryBlendCells = boundaryBlendCells;
            SmoothShare = smoothShare;
            RuggedShare = ruggedShare;
            MountainShare = mountainShare;
            CanyonShare = canyonShare;
            SeaShare = seaShare;
        }

        public int SizeCells { get; }
        public float CenterJitter { get; }
        public float WarpScale { get; }
        public float WarpStrengthCells { get; }
        public float BoundaryBlendCells { get; }
        public float SmoothShare { get; }
        public float RuggedShare { get; }
        public float MountainShare { get; }
        public float CanyonShare { get; }
        public float SeaShare { get; }
        public float TotalShare => SmoothShare + RuggedShare + MountainShare
            + CanyonShare + SeaShare;
    }

    public readonly struct TerrainBaseDensitySettingsData
    {
        public TerrainBaseDensitySettingsData(
            WorldCurveSettingsData surfaceByContinentalness,
            WorldCurveSettingsData surfaceByErosion,
            WorldCurveSettingsData verticalFactorByErosion,
            WorldCurveSettingsData detailByRoughness)
        {
            SurfaceByContinentalness = surfaceByContinentalness;
            SurfaceByErosion = surfaceByErosion;
            VerticalFactorByErosion = verticalFactorByErosion;
            DetailByRoughness = detailByRoughness;
        }

        public WorldCurveSettingsData SurfaceByContinentalness { get; }
        public WorldCurveSettingsData SurfaceByErosion { get; }
        public WorldCurveSettingsData VerticalFactorByErosion { get; }
        public WorldCurveSettingsData DetailByRoughness { get; }
    }

    public readonly struct SmoothTerrainSettingsData
    {
        public SmoothTerrainSettingsData(
            WorldCurveSettingsData undulationByWeirdness,
            WorldCurveSettingsData detailByRoughness)
        {
            UndulationByWeirdness = undulationByWeirdness;
            DetailByRoughness = detailByRoughness;
        }

        public WorldCurveSettingsData UndulationByWeirdness { get; }
        public WorldCurveSettingsData DetailByRoughness { get; }
    }

    public readonly struct RuggedTerrainSettingsData
    {
        public RuggedTerrainSettingsData(
            WorldCurveSettingsData reliefByPeaksValleys,
            WorldCurveSettingsData reliefScaleByRoughness,
            WorldCurveSettingsData detailByRoughness)
        {
            ReliefByPeaksValleys = reliefByPeaksValleys;
            ReliefScaleByRoughness = reliefScaleByRoughness;
            DetailByRoughness = detailByRoughness;
        }

        public WorldCurveSettingsData ReliefByPeaksValleys { get; }
        public WorldCurveSettingsData ReliefScaleByRoughness { get; }
        public WorldCurveSettingsData DetailByRoughness { get; }
    }

    public readonly struct MountainTerrainSettingsData
    {
        public MountainTerrainSettingsData(
            float minimumHeightUnits,
            float maximumHeightUnits,
            float heightBias,
            float slopeVariation,
            float ridgeStrengthUnits)
        {
            MinimumHeightUnits = minimumHeightUnits;
            MaximumHeightUnits = maximumHeightUnits;
            HeightBias = heightBias;
            SlopeVariation = slopeVariation;
            RidgeStrengthUnits = ridgeStrengthUnits;
        }

        public float MinimumHeightUnits { get; }
        public float MaximumHeightUnits { get; }
        public float HeightBias { get; }
        public float SlopeVariation { get; }
        public float RidgeStrengthUnits { get; }
    }

    public readonly struct CanyonTerrainSettingsData
    {
        public CanyonTerrainSettingsData(
            float minimumWidthCells,
            float maximumWidthCells,
            float minimumDepthUnits,
            float maximumDepthUnits,
            float minimumRegionDepthRatio,
            float maximumRegionDepthRatio,
            int minimumValleyCount,
            int maximumValleyCount,
            float maximumValleyOffsetRatio,
            float axisWarpCells,
            float detailStrength)
        {
            MinimumWidthCells = minimumWidthCells;
            MaximumWidthCells = maximumWidthCells;
            MinimumDepthUnits = minimumDepthUnits;
            MaximumDepthUnits = maximumDepthUnits;
            MinimumRegionDepthRatio = minimumRegionDepthRatio;
            MaximumRegionDepthRatio = maximumRegionDepthRatio;
            MinimumValleyCount = minimumValleyCount;
            MaximumValleyCount = maximumValleyCount;
            MaximumValleyOffsetRatio = maximumValleyOffsetRatio;
            AxisWarpCells = axisWarpCells;
            DetailStrength = detailStrength;
        }

        public float MinimumWidthCells { get; }
        public float MaximumWidthCells { get; }
        public float MinimumDepthUnits { get; }
        public float MaximumDepthUnits { get; }
        public float MinimumRegionDepthRatio { get; }
        public float MaximumRegionDepthRatio { get; }
        public int MinimumValleyCount { get; }
        public int MaximumValleyCount { get; }
        public float MaximumValleyOffsetRatio { get; }
        public float AxisWarpCells { get; }
        public float DetailStrength { get; }
    }

    public readonly struct SeaPatternSettingsData
    {
        public SeaPatternSettingsData(
            WorldCurveSettingsData depthByInterior,
            int maximumDepthCells,
            int surfaceUnits,
            float shapeDetailStrength)
        {
            if (maximumDepthCells <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDepthCells));
            }

            if (surfaceUnits <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(surfaceUnits));
            }

            if (!float.IsFinite(shapeDetailStrength)
                || shapeDetailStrength < 0f
                || shapeDetailStrength > 0.5f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shapeDetailStrength));
            }

            DepthByInterior = depthByInterior;
            MaximumDepthCells = maximumDepthCells;
            SurfaceUnits = surfaceUnits;
            ShapeDetailStrength = shapeDetailStrength;
        }

        public WorldCurveSettingsData DepthByInterior { get; }
        public int MaximumDepthCells { get; }
        public int SurfaceUnits { get; }
        public float ShapeDetailStrength { get; }
    }

    public readonly struct WorldPatternSettingsData
    {
        public WorldPatternSettingsData(
            WorldPatternRegionSettingsData region,
            TerrainBaseDensitySettingsData baseDensity,
            SmoothTerrainSettingsData smooth,
            RuggedTerrainSettingsData rugged,
            MountainTerrainSettingsData mountain,
            CanyonTerrainSettingsData canyon,
            SeaPatternSettingsData sea)
        {
            Region = region;
            BaseDensity = baseDensity;
            Smooth = smooth;
            Rugged = rugged;
            Mountain = mountain;
            Canyon = canyon;
            Sea = sea;
        }

        public WorldPatternRegionSettingsData Region { get; }
        public TerrainBaseDensitySettingsData BaseDensity { get; }
        public SmoothTerrainSettingsData Smooth { get; }
        public RuggedTerrainSettingsData Rugged { get; }
        public MountainTerrainSettingsData Mountain { get; }
        public CanyonTerrainSettingsData Canyon { get; }
        public SeaPatternSettingsData Sea { get; }
    }

    public sealed class WorldSettingsData
    {
        public WorldSettingsData(
            int seed,
            WorldType worldType,
            float cellSize,
            int chunkCellCountXZ,
            int chunkSectionCellCountY,
            int initialChunkCountXZ,
            int chunkSectionCountY,
            int renderChunksPerPatch,
            int roadMaxHeightSteps,
            WorldNoiseRouterSettingsData worldNoiseRouter,
            WorldPatternSettingsData worldPatterns,
            float temperatureScale,
            int terrainBaseHeightUnits,
            float riverScale,
            float riverDensity,
            int riverDepthCells,
            int maximumRiverWidthCells,
            int maximumRiverDepthCells,
            float lakeDensity,
            int lakeRegionSizeCells,
            int maximumLakeRadiusCells,
            int maximumLakeDepthSteps,
            int minimumInlandLakeArea,
            int minimumInlandLakeDepthSteps,
            int pondMaximumArea,
            WaterFlowRules waterFlowRules,
            float coldClimateThreshold)
        {
            if (!float.IsFinite(cellSize) || cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    "Cell size must be finite and positive.");
            }

            if (chunkCellCountXZ <= 0 || chunkSectionCellCountY <= 0
                || initialChunkCountXZ <= 0 || chunkSectionCountY <= 0
                || (initialChunkCountXZ & 1) == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkCellCountXZ),
                    "Chunk Cell counts must be positive and the initial horizontal Chunk count must be odd.");
            }

            if (renderChunksPerPatch <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(renderChunksPerPatch),
                    "Render chunks per patch must be positive.");
            }

            if (roadMaxHeightSteps < 0
                || roadMaxHeightSteps > WorldGrid.HeightStepsPerCell)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roadMaxHeightSteps),
                    "Road maximum height steps must be inside one Cell height.");
            }

            Seed = seed;
            WorldType = worldType;
            CellSize = cellSize;
            ChunkCellCountXZ = chunkCellCountXZ;
            ChunkSectionCellCountY = chunkSectionCellCountY;
            InitialChunkCountXZ = initialChunkCountXZ;
            ChunkSectionCountY = chunkSectionCountY;
            RenderChunksPerPatch = renderChunksPerPatch;
            RoadMaxHeightSteps = roadMaxHeightSteps;
            WorldNoiseRouter = worldNoiseRouter;
            WorldPatterns = worldPatterns;
            TemperatureScale = temperatureScale;
            TerrainBaseHeightUnits = terrainBaseHeightUnits;
            RiverScale = riverScale;
            RiverDensity = riverDensity;
            RiverDepthCells = riverDepthCells;
            MaximumRiverWidthCells = maximumRiverWidthCells;
            MaximumRiverDepthCells = maximumRiverDepthCells;
            LakeDensity = lakeDensity;
            LakeRegionSizeCells = lakeRegionSizeCells;
            MaximumLakeRadiusCells = maximumLakeRadiusCells;
            MaximumLakeDepthSteps = maximumLakeDepthSteps;
            MinimumInlandLakeArea = minimumInlandLakeArea;
            MinimumInlandLakeDepthSteps = minimumInlandLakeDepthSteps;
            PondMaximumArea = pondMaximumArea;
            WaterFlowRules = waterFlowRules;
            ColdClimateThreshold = Math.Clamp(
                coldClimateThreshold,
                0f,
                0.5f);

            if (TerrainBaseHeightUnits < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(terrainBaseHeightUnits),
                    "Terrain base height must be non-negative.");
            }

            if (!float.IsFinite(TemperatureScale)
                || TemperatureScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(temperatureScale),
                    "Temperature Field scale must be finite and positive.");
            }

            if (WorldPatterns.Sea.SurfaceUnits
                >= WorldHeight * WorldGrid.HeightStepsPerCell)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldPatterns),
                    "The Sea surface must be inside the vertical world range.");
            }
        }

        public int Seed { get; }
        public WorldType WorldType { get; }
        public float CellSize { get; }
        public float HeightStep => CellSize / WorldGrid.HeightStepsPerCell;
        public int ChunkCellCountXZ { get; }
        public int ChunkSectionCellCountY { get; }
        public int InitialChunkCountXZ { get; }
        public int WorldChunkCountXZ => InitialChunkCountXZ;
        public int ChunkSectionCountY { get; }
        public int RenderChunksPerPatch { get; }
        public int RoadMaxHeightSteps { get; }
        public int InitialWorldSize => checked(ChunkCellCountXZ * InitialChunkCountXZ);
        public int WorldSize => InitialWorldSize;
        public int MinimumChunkCoordinate => -(InitialChunkCountXZ / 2);
        public int MaximumChunkCoordinate => InitialChunkCountXZ / 2;
        public int MinimumCellCoordinate => checked(MinimumChunkCoordinate * ChunkCellCountXZ);
        public int MaximumCellCoordinateExclusive => checked((MaximumChunkCoordinate + 1) * ChunkCellCountXZ);
        public int WorldHeight => checked(ChunkSectionCellCountY * ChunkSectionCountY);
        public int RenderPatchSizeXZ => checked(ChunkCellCountXZ * RenderChunksPerPatch);

        public WorldNoiseRouterSettingsData WorldNoiseRouter { get; }
        public WorldPatternSettingsData WorldPatterns { get; }
        public float TemperatureScale { get; }
        public int TerrainBaseHeightUnits { get; }
        public float RiverScale { get; }
        public float RiverDensity { get; }
        public int RiverDepthCells { get; }
        public int MaximumRiverWidthCells { get; }
        public int MaximumRiverDepthCells { get; }
        public float LakeDensity { get; }
        public int LakeRegionSizeCells { get; }
        public int MaximumLakeRadiusCells { get; }
        public int MaximumLakeDepthSteps { get; }
        public int MinimumInlandLakeArea { get; }
        public int MinimumInlandLakeDepthSteps { get; }
        public int PondMaximumArea { get; }
        public WaterFlowRules WaterFlowRules { get; }
        public float ColdClimateThreshold { get; }
    }
}
