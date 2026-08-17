using System;

namespace MiniCivilization.World.Domain
{
    public enum WorldType : byte
    {
        Finite,
        Infinite
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
            float terrainScale,
            int terrainLayers,
            float terrainSpacing,
            float terrainDetail,
            int baseHeightUnits,
            int heightVariationUnits,
            float mountainScale,
            int mountainHeightUnits,
            float mountainCoverage,
            float mountainSteepness,
            int seaLevelUnits,
            int maximumSeaDepthUnits,
            float continentalScale,
            float landThreshold,
            float coastTransitionWidth,
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
            TerrainScale = terrainScale;
            TerrainLayers = terrainLayers;
            TerrainSpacing = terrainSpacing;
            TerrainDetail = terrainDetail;
            BaseHeightUnits = baseHeightUnits;
            HeightVariationUnits = heightVariationUnits;
            MountainScale = mountainScale;
            MountainHeightUnits = mountainHeightUnits;
            MountainCoverage = mountainCoverage;
            MountainSteepness = mountainSteepness;
            SeaLevelUnits = seaLevelUnits;
            MaximumSeaDepthUnits = maximumSeaDepthUnits;
            ContinentalScale = continentalScale;
            LandThreshold = landThreshold;
            CoastTransitionWidth = coastTransitionWidth;
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

            if (SeaLevelUnits <= 0
                || SeaLevelUnits >= WorldHeight * WorldGrid.HeightStepsPerCell)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seaLevelUnits),
                    "Sea level must be inside the vertical world range.");
            }

            if (MaximumSeaDepthUnits <= 0
                || MaximumSeaDepthUnits >= SeaLevelUnits)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumSeaDepthUnits),
                    "Maximum sea depth must be positive and lower than sea level.");
            }

            if (!float.IsFinite(CoastTransitionWidth)
                || CoastTransitionWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(coastTransitionWidth),
                    "Coast transition width must be finite and positive.");
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

        public float TerrainScale { get; }
        public int TerrainLayers { get; }
        public float TerrainSpacing { get; }
        public float TerrainDetail { get; }
        public int BaseHeightUnits { get; }
        public int HeightVariationUnits { get; }
        public float MountainScale { get; }
        public int MountainHeightUnits { get; }
        public float MountainCoverage { get; }
        public float MountainSteepness { get; }
        public int SeaLevelUnits { get; }
        public int MaximumSeaDepthUnits { get; }
        public float ContinentalScale { get; }
        public float LandThreshold { get; }
        public float CoastTransitionWidth { get; }
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
