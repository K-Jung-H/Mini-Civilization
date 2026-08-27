using System;

namespace MiniCivilization.World.Domain
{
    public enum WorldType : byte
    {
        Finite,
        Infinite
    }

    public enum TerrainNoiseMode : byte
    {
        Value,
        Signed,
        Ridge,
        SignedRidge
    }

    public readonly struct TerrainNoiseFieldSettingsData
    {
        public TerrainNoiseFieldSettingsData(
            TerrainNoiseMode mode,
            float scale,
            int layers,
            float frequencySpacing,
            float persistence)
        {
            if (!Enum.IsDefined(typeof(TerrainNoiseMode), mode)
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

        public TerrainNoiseMode Mode { get; }
        public float Scale { get; }
        public int Layers { get; }
        public float FrequencySpacing { get; }
        public float Persistence { get; }
    }

    public readonly struct TerrainCurveSettingsData
    {
        public TerrainCurveSettingsData(
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

    public readonly struct TerrainNoiseRouterSettingsData
    {
        public TerrainNoiseRouterSettingsData(
            TerrainNoiseFieldSettingsData patternRegion,
            TerrainNoiseFieldSettingsData continentalness,
            TerrainNoiseFieldSettingsData erosion,
            TerrainNoiseFieldSettingsData weirdness,
            TerrainNoiseFieldSettingsData peaksValleys,
            TerrainNoiseFieldSettingsData roughness,
            TerrainNoiseFieldSettingsData detail)
        {
            PatternRegion = patternRegion;
            Continentalness = continentalness;
            Erosion = erosion;
            Weirdness = weirdness;
            PeaksValleys = peaksValleys;
            Roughness = roughness;
            Detail = detail;
        }

        public TerrainNoiseFieldSettingsData PatternRegion { get; }
        public TerrainNoiseFieldSettingsData Continentalness { get; }
        public TerrainNoiseFieldSettingsData Erosion { get; }
        public TerrainNoiseFieldSettingsData Weirdness { get; }
        public TerrainNoiseFieldSettingsData PeaksValleys { get; }
        public TerrainNoiseFieldSettingsData Roughness { get; }
        public TerrainNoiseFieldSettingsData Detail { get; }
    }

    public readonly struct TerrainBaseDensitySettingsData
    {
        public TerrainBaseDensitySettingsData(
            TerrainCurveSettingsData surfaceByContinentalness,
            TerrainCurveSettingsData surfaceByErosion,
            TerrainCurveSettingsData verticalFactorByErosion,
            TerrainCurveSettingsData detailByRoughness)
        {
            SurfaceByContinentalness = surfaceByContinentalness;
            SurfaceByErosion = surfaceByErosion;
            VerticalFactorByErosion = verticalFactorByErosion;
            DetailByRoughness = detailByRoughness;
        }

        public TerrainCurveSettingsData SurfaceByContinentalness { get; }
        public TerrainCurveSettingsData SurfaceByErosion { get; }
        public TerrainCurveSettingsData VerticalFactorByErosion { get; }
        public TerrainCurveSettingsData DetailByRoughness { get; }
    }

    public readonly struct SmoothTerrainSettingsData
    {
        public SmoothTerrainSettingsData(
            TerrainCurveSettingsData influenceByRegion,
            TerrainCurveSettingsData undulationByWeirdness,
            TerrainCurveSettingsData detailByRoughness)
        {
            InfluenceByRegion = influenceByRegion;
            UndulationByWeirdness = undulationByWeirdness;
            DetailByRoughness = detailByRoughness;
        }

        public TerrainCurveSettingsData InfluenceByRegion { get; }
        public TerrainCurveSettingsData UndulationByWeirdness { get; }
        public TerrainCurveSettingsData DetailByRoughness { get; }
    }

    public readonly struct RuggedTerrainSettingsData
    {
        public RuggedTerrainSettingsData(
            TerrainCurveSettingsData influenceByRegion,
            TerrainCurveSettingsData reliefByPeaksValleys,
            TerrainCurveSettingsData reliefScaleByRoughness,
            TerrainCurveSettingsData detailByRoughness)
        {
            InfluenceByRegion = influenceByRegion;
            ReliefByPeaksValleys = reliefByPeaksValleys;
            ReliefScaleByRoughness = reliefScaleByRoughness;
            DetailByRoughness = detailByRoughness;
        }

        public TerrainCurveSettingsData InfluenceByRegion { get; }
        public TerrainCurveSettingsData ReliefByPeaksValleys { get; }
        public TerrainCurveSettingsData ReliefScaleByRoughness { get; }
        public TerrainCurveSettingsData DetailByRoughness { get; }
    }

    public readonly struct MountainTerrainSettingsData
    {
        public MountainTerrainSettingsData(
            TerrainCurveSettingsData influenceByRegion,
            TerrainCurveSettingsData centerProximityByRegion,
            TerrainCurveSettingsData heightByCenterProximity,
            TerrainCurveSettingsData progressExponentByErosion)
        {
            InfluenceByRegion = influenceByRegion;
            CenterProximityByRegion = centerProximityByRegion;
            HeightByCenterProximity = heightByCenterProximity;
            ProgressExponentByErosion = progressExponentByErosion;
        }

        public TerrainCurveSettingsData InfluenceByRegion { get; }
        public TerrainCurveSettingsData CenterProximityByRegion { get; }
        public TerrainCurveSettingsData HeightByCenterProximity { get; }
        public TerrainCurveSettingsData ProgressExponentByErosion { get; }
    }

    public readonly struct CanyonTerrainSettingsData
    {
        public CanyonTerrainSettingsData(
            TerrainCurveSettingsData influenceByRegion,
            TerrainCurveSettingsData widthByVariation,
            TerrainCurveSettingsData maximumDepthByVariation)
        {
            InfluenceByRegion = influenceByRegion;
            WidthByVariation = widthByVariation;
            MaximumDepthByVariation = maximumDepthByVariation;
        }

        public TerrainCurveSettingsData InfluenceByRegion { get; }
        public TerrainCurveSettingsData WidthByVariation { get; }
        public TerrainCurveSettingsData MaximumDepthByVariation { get; }
    }

    public readonly struct TerrainPatternSettingsData
    {
        public TerrainPatternSettingsData(
            TerrainBaseDensitySettingsData baseDensity,
            SmoothTerrainSettingsData smooth,
            RuggedTerrainSettingsData rugged,
            MountainTerrainSettingsData mountain,
            CanyonTerrainSettingsData canyon)
        {
            BaseDensity = baseDensity;
            Smooth = smooth;
            Rugged = rugged;
            Mountain = mountain;
            Canyon = canyon;
        }

        public TerrainBaseDensitySettingsData BaseDensity { get; }
        public SmoothTerrainSettingsData Smooth { get; }
        public RuggedTerrainSettingsData Rugged { get; }
        public MountainTerrainSettingsData Mountain { get; }
        public CanyonTerrainSettingsData Canyon { get; }
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
            TerrainNoiseRouterSettingsData terrainNoiseRouter,
            TerrainPatternSettingsData terrainPatterns,
            float temperatureScale,
            int terrainBaseHeightUnits,
            int defaultSeaSurfaceUnits,
            int maximumSeaDepthUnits,
            float landThreshold,
            float deepSeaThreshold,
            float seaDepthSteepness,
            float seaDepthNoiseScale,
            float seaDepthNoiseStrength,
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
            TerrainNoiseRouter = terrainNoiseRouter;
            TerrainPatterns = terrainPatterns;
            TemperatureScale = temperatureScale;
            TerrainBaseHeightUnits = terrainBaseHeightUnits;
            DefaultSeaSurfaceUnits = defaultSeaSurfaceUnits;
            MaximumSeaDepthUnits = maximumSeaDepthUnits;
            LandThreshold = landThreshold;
            DeepSeaThreshold = deepSeaThreshold;
            SeaDepthSteepness = seaDepthSteepness;
            SeaDepthNoiseScale = seaDepthNoiseScale;
            SeaDepthNoiseStrength = seaDepthNoiseStrength;
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

            if (DefaultSeaSurfaceUnits <= 0
                || DefaultSeaSurfaceUnits
                >= WorldHeight * WorldGrid.HeightStepsPerCell)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(defaultSeaSurfaceUnits),
                    "The default Sea surface must be inside the vertical world range.");
            }

            if (MaximumSeaDepthUnits <= 0
                || MaximumSeaDepthUnits >= DefaultSeaSurfaceUnits)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSeaDepthUnits),
                    "Maximum Sea depth must be positive and fit below the default Sea surface.");
            }

            if (!float.IsFinite(DeepSeaThreshold)
                || DeepSeaThreshold < 0f
                || DeepSeaThreshold >= LandThreshold)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deepSeaThreshold),
                    "Deep sea threshold must be non-negative and lower than the land threshold.");
            }

            if (!float.IsFinite(SeaDepthSteepness)
                || SeaDepthSteepness < 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seaDepthSteepness),
                    "Sea depth steepness must be finite and at least one.");
            }

            if (!float.IsFinite(SeaDepthNoiseScale)
                || SeaDepthNoiseScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seaDepthNoiseScale),
                    "Sea depth Noise scale must be finite and positive.");
            }

            if (!float.IsFinite(SeaDepthNoiseStrength)
                || SeaDepthNoiseStrength < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seaDepthNoiseStrength),
                    "Sea depth Noise strength must be finite and non-negative.");
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

        public TerrainNoiseRouterSettingsData TerrainNoiseRouter { get; }
        public TerrainPatternSettingsData TerrainPatterns { get; }
        public float TemperatureScale { get; }
        public int TerrainBaseHeightUnits { get; }
        public int DefaultSeaSurfaceUnits { get; }
        public int MaximumSeaDepthUnits { get; }
        public float LandThreshold { get; }
        public float DeepSeaThreshold { get; }
        public float SeaDepthSteepness { get; }
        public float SeaDepthNoiseScale { get; }
        public float SeaDepthNoiseStrength { get; }
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
