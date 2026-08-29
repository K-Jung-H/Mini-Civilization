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

    public readonly struct WorldSeededRangeSettingsData
    {
        public WorldSeededRangeSettingsData(float minimum, float maximum)
        {
            if (!float.IsFinite(minimum)
                || !float.IsFinite(maximum)
                || maximum < minimum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            Minimum = minimum;
            Maximum = maximum;
        }

        public float Minimum { get; }
        public float Maximum { get; }
    }

    public readonly struct TerrainDomainWarpSettingsData
    {
        public TerrainDomainWarpSettingsData(
            WorldNoiseFieldSettingsData field,
            float strengthCells)
        {
            if (!float.IsFinite(strengthCells) || strengthCells < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(strengthCells));
            }

            Field = field;
            StrengthCells = strengthCells;
        }

        public WorldNoiseFieldSettingsData Field { get; }
        public float StrengthCells { get; }
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
            float interiorReachRatio,
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
            InteriorReachRatio = interiorReachRatio;
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
        public float InteriorReachRatio { get; }
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
            TerrainDomainWarpSettingsData domainWarp,
            WorldNoiseFieldSettingsData heightField,
            WorldCurveSettingsData heightResponse,
            WorldSeededRangeSettingsData heightAmplitudeUnits,
            WorldNoiseFieldSettingsData detailField,
            WorldSeededRangeSettingsData detailAmplitudeUnits)
        {
            DomainWarp = domainWarp;
            HeightField = heightField;
            HeightResponse = heightResponse;
            HeightAmplitudeUnits = heightAmplitudeUnits;
            DetailField = detailField;
            DetailAmplitudeUnits = detailAmplitudeUnits;
        }

        public TerrainDomainWarpSettingsData DomainWarp { get; }
        public WorldNoiseFieldSettingsData HeightField { get; }
        public WorldCurveSettingsData HeightResponse { get; }
        public WorldSeededRangeSettingsData HeightAmplitudeUnits { get; }
        public WorldNoiseFieldSettingsData DetailField { get; }
        public WorldSeededRangeSettingsData DetailAmplitudeUnits { get; }
    }

    public readonly struct RuggedTerrainSettingsData
    {
        public RuggedTerrainSettingsData(
            TerrainDomainWarpSettingsData domainWarp,
            WorldNoiseFieldSettingsData reliefField,
            WorldCurveSettingsData reliefResponse,
            WorldSeededRangeSettingsData reliefAmplitudeUnits,
            WorldNoiseFieldSettingsData detailField,
            WorldSeededRangeSettingsData detailAmplitudeUnits)
        {
            DomainWarp = domainWarp;
            ReliefField = reliefField;
            ReliefResponse = reliefResponse;
            ReliefAmplitudeUnits = reliefAmplitudeUnits;
            DetailField = detailField;
            DetailAmplitudeUnits = detailAmplitudeUnits;
        }

        public TerrainDomainWarpSettingsData DomainWarp { get; }
        public WorldNoiseFieldSettingsData ReliefField { get; }
        public WorldCurveSettingsData ReliefResponse { get; }
        public WorldSeededRangeSettingsData ReliefAmplitudeUnits { get; }
        public WorldNoiseFieldSettingsData DetailField { get; }
        public WorldSeededRangeSettingsData DetailAmplitudeUnits { get; }
    }

    public readonly struct MountainTerrainSettingsData
    {
        public MountainTerrainSettingsData(
            TerrainDomainWarpSettingsData domainWarp,
            WorldNoiseFieldSettingsData massField,
            WorldCurveSettingsData massResponse,
            WorldSeededRangeSettingsData heightUnits,
            WorldNoiseFieldSettingsData ridgeField,
            WorldCurveSettingsData ridgeResponse,
            WorldSeededRangeSettingsData ridgeStrengthUnits,
            WorldNoiseFieldSettingsData detailField,
            WorldSeededRangeSettingsData detailAmplitudeUnits)
        {
            DomainWarp = domainWarp;
            MassField = massField;
            MassResponse = massResponse;
            HeightUnits = heightUnits;
            RidgeField = ridgeField;
            RidgeResponse = ridgeResponse;
            RidgeStrengthUnits = ridgeStrengthUnits;
            DetailField = detailField;
            DetailAmplitudeUnits = detailAmplitudeUnits;
        }

        public TerrainDomainWarpSettingsData DomainWarp { get; }
        public WorldNoiseFieldSettingsData MassField { get; }
        public WorldCurveSettingsData MassResponse { get; }
        public WorldSeededRangeSettingsData HeightUnits { get; }
        public WorldNoiseFieldSettingsData RidgeField { get; }
        public WorldCurveSettingsData RidgeResponse { get; }
        public WorldSeededRangeSettingsData RidgeStrengthUnits { get; }
        public WorldNoiseFieldSettingsData DetailField { get; }
        public WorldSeededRangeSettingsData DetailAmplitudeUnits { get; }
    }

    public readonly struct CanyonTerrainSettingsData
    {
        public CanyonTerrainSettingsData(
            TerrainDomainWarpSettingsData domainWarp,
            WorldNoiseFieldSettingsData basinField,
            WorldCurveSettingsData basinResponse,
            WorldSeededRangeSettingsData basinDepthRatio,
            WorldNoiseFieldSettingsData valleyField,
            WorldCurveSettingsData valleyResponse,
            WorldSeededRangeSettingsData valleyDepthRatio,
            WorldSeededRangeSettingsData depthUnits,
            WorldNoiseFieldSettingsData detailField,
            WorldSeededRangeSettingsData detailAmplitudeUnits)
        {
            DomainWarp = domainWarp;
            BasinField = basinField;
            BasinResponse = basinResponse;
            BasinDepthRatio = basinDepthRatio;
            ValleyField = valleyField;
            ValleyResponse = valleyResponse;
            ValleyDepthRatio = valleyDepthRatio;
            DepthUnits = depthUnits;
            DetailField = detailField;
            DetailAmplitudeUnits = detailAmplitudeUnits;
        }

        public TerrainDomainWarpSettingsData DomainWarp { get; }
        public WorldNoiseFieldSettingsData BasinField { get; }
        public WorldCurveSettingsData BasinResponse { get; }
        public WorldSeededRangeSettingsData BasinDepthRatio { get; }
        public WorldNoiseFieldSettingsData ValleyField { get; }
        public WorldCurveSettingsData ValleyResponse { get; }
        public WorldSeededRangeSettingsData ValleyDepthRatio { get; }
        public WorldSeededRangeSettingsData DepthUnits { get; }
        public WorldNoiseFieldSettingsData DetailField { get; }
        public WorldSeededRangeSettingsData DetailAmplitudeUnits { get; }
    }

    public readonly struct SeaPatternSettingsData
    {
        public SeaPatternSettingsData(
            TerrainDomainWarpSettingsData domainWarp,
            WorldNoiseFieldSettingsData basinField,
            float basinVariation,
            WorldCurveSettingsData depthByBasin,
            WorldSeededRangeSettingsData maximumDepthUnits,
            WorldNoiseFieldSettingsData seabedField,
            WorldSeededRangeSettingsData seabedAmplitudeUnits,
            int surfaceUnits)
        {
            if (surfaceUnits <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(surfaceUnits));
            }

            if (!float.IsFinite(basinVariation)
                || basinVariation < 0f
                || basinVariation > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(basinVariation));
            }

            DomainWarp = domainWarp;
            BasinField = basinField;
            BasinVariation = basinVariation;
            DepthByBasin = depthByBasin;
            MaximumDepthUnits = maximumDepthUnits;
            SeabedField = seabedField;
            SeabedAmplitudeUnits = seabedAmplitudeUnits;
            SurfaceUnits = surfaceUnits;
        }

        public TerrainDomainWarpSettingsData DomainWarp { get; }
        public WorldNoiseFieldSettingsData BasinField { get; }
        public float BasinVariation { get; }
        public WorldCurveSettingsData DepthByBasin { get; }
        public WorldSeededRangeSettingsData MaximumDepthUnits { get; }
        public WorldNoiseFieldSettingsData SeabedField { get; }
        public WorldSeededRangeSettingsData SeabedAmplitudeUnits { get; }
        public int SurfaceUnits { get; }
    }

    public readonly struct RiverPatternSettingsData
    {
        public RiverPatternSettingsData(
            int planningRegionSizeCells,
            int routeSampleSpacingCells,
            float networkDensity,
            float terrainChangeCost,
            float uphillCost,
            float crossSlopeCost,
            float corridorExposureCost,
            float bankMarginCells,
            float valleyPreference,
            WorldNoiseFieldSettingsData routeVariationField,
            float routeVariationCost,
            int smoothingIterations,
            WorldNoiseFieldSettingsData widthField,
            int maximumWidthCells,
            WorldCurveSettingsData crossSection,
            WorldSeededRangeSettingsData depthUnits,
            WorldSeededRangeSettingsData waterInsetUnits,
            int dropTransitionCells,
            WorldCurveSettingsData dropTransition,
            WorldNoiseFieldSettingsData riverbedField,
            WorldSeededRangeSettingsData riverbedAmplitudeUnits)
        {
            if (planningRegionSizeCells < 16
                || routeSampleSpacingCells < 1
                || planningRegionSizeCells % routeSampleSpacingCells != 0
                || !float.IsFinite(networkDensity)
                || networkDensity < 0f
                || networkDensity > 1f
                || !float.IsFinite(terrainChangeCost)
                || terrainChangeCost < 0f
                || !float.IsFinite(uphillCost)
                || uphillCost < 0f
                || !float.IsFinite(crossSlopeCost)
                || crossSlopeCost < 0f
                || !float.IsFinite(corridorExposureCost)
                || corridorExposureCost < 0f
                || !float.IsFinite(bankMarginCells)
                || bankMarginCells < 0f
                || !float.IsFinite(valleyPreference)
                || valleyPreference < 0f
                || !float.IsFinite(routeVariationCost)
                || routeVariationCost < 0f
                || smoothingIterations < 0
                || smoothingIterations > 4
                || maximumWidthCells < 1
                || maximumWidthCells > 10
                || dropTransitionCells < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(planningRegionSizeCells),
                    "River Pattern settings are invalid.");
            }

            PlanningRegionSizeCells = planningRegionSizeCells;
            RouteSampleSpacingCells = routeSampleSpacingCells;
            NetworkDensity = networkDensity;
            TerrainChangeCost = terrainChangeCost;
            UphillCost = uphillCost;
            CrossSlopeCost = crossSlopeCost;
            CorridorExposureCost = corridorExposureCost;
            BankMarginCells = bankMarginCells;
            ValleyPreference = valleyPreference;
            RouteVariationField = routeVariationField;
            RouteVariationCost = routeVariationCost;
            SmoothingIterations = smoothingIterations;
            WidthField = widthField;
            MaximumWidthCells = maximumWidthCells;
            CrossSection = crossSection;
            DepthUnits = depthUnits;
            WaterInsetUnits = waterInsetUnits;
            DropTransitionCells = dropTransitionCells;
            DropTransition = dropTransition;
            RiverbedField = riverbedField;
            RiverbedAmplitudeUnits = riverbedAmplitudeUnits;
        }

        public int PlanningRegionSizeCells { get; }
        public int RouteSampleSpacingCells { get; }
        public float NetworkDensity { get; }
        public float TerrainChangeCost { get; }
        public float UphillCost { get; }
        public float CrossSlopeCost { get; }
        public float CorridorExposureCost { get; }
        public float BankMarginCells { get; }
        public float ValleyPreference { get; }
        public WorldNoiseFieldSettingsData RouteVariationField { get; }
        public float RouteVariationCost { get; }
        public int SmoothingIterations { get; }
        public WorldNoiseFieldSettingsData WidthField { get; }
        public int MaximumWidthCells { get; }
        public WorldCurveSettingsData CrossSection { get; }
        public WorldSeededRangeSettingsData DepthUnits { get; }
        public WorldSeededRangeSettingsData WaterInsetUnits { get; }
        public int DropTransitionCells { get; }
        public WorldCurveSettingsData DropTransition { get; }
        public WorldNoiseFieldSettingsData RiverbedField { get; }
        public WorldSeededRangeSettingsData RiverbedAmplitudeUnits { get; }
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
            SeaPatternSettingsData sea,
            RiverPatternSettingsData river)
        {
            Region = region;
            BaseDensity = baseDensity;
            Smooth = smooth;
            Rugged = rugged;
            Mountain = mountain;
            Canyon = canyon;
            Sea = sea;
            River = river;
        }

        public WorldPatternRegionSettingsData Region { get; }
        public TerrainBaseDensitySettingsData BaseDensity { get; }
        public SmoothTerrainSettingsData Smooth { get; }
        public RuggedTerrainSettingsData Rugged { get; }
        public MountainTerrainSettingsData Mountain { get; }
        public CanyonTerrainSettingsData Canyon { get; }
        public SeaPatternSettingsData Sea { get; }
        public RiverPatternSettingsData River { get; }
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
