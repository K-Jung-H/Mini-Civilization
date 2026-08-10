using System;

namespace MiniCivilization.World.Domain
{
    public sealed class WorldSettingsData
    {
        public WorldSettingsData(
            int seed,
            float cellSize,
            int chunkCellCountXZ,
            int chunkCellCountY,
            int worldChunkCountXZ,
            int worldChunkCountY,
            int renderChunksPerPatch,
            float terrainScale,
            int terrainLayers,
            float terrainSpacing,
            float terrainDetail,
            int baseHeightUnits,
            int heightVariationUnits,
            float edgeLowering,
            float mountainScale,
            int mountainHeightUnits,
            float mountainCoverage,
            float mountainSteepness,
            int seaLevelUnits,
            int riverCount,
            int riverDepthCells,
            int maximumRiverWidthCells,
            int maximumRiverDepthCells,
            int lakeCount,
            int minimumInlandLakeDistance,
            int minimumInlandLakeArea,
            int minimumInlandLakeDepthSteps,
            int pondMaximumArea,
            WaterFlowRules waterFlowRules,
            float desertMoistureThreshold,
            float wetlandMoistureThreshold,
            float snowTemperatureThreshold,
            int waterMoistureRadius)
        {
            if (!float.IsFinite(cellSize) || cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    "Cell size must be finite and positive.");
            }

            if (chunkCellCountXZ <= 0 || chunkCellCountY <= 0
                || worldChunkCountXZ <= 0 || worldChunkCountY <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkCellCountXZ),
                    "Chunk Cell counts and world chunk counts must be positive.");
            }

            if (renderChunksPerPatch <= 0
                || worldChunkCountXZ % renderChunksPerPatch != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(renderChunksPerPatch),
                    "Render chunks per patch must be positive and divide the horizontal world chunk count.");
            }

            Seed = seed;
            CellSize = cellSize;
            ChunkCellCountXZ = chunkCellCountXZ;
            ChunkCellCountY = chunkCellCountY;
            WorldChunkCountXZ = worldChunkCountXZ;
            WorldChunkCountY = worldChunkCountY;
            RenderChunksPerPatch = renderChunksPerPatch;
            TerrainScale = terrainScale;
            TerrainLayers = terrainLayers;
            TerrainSpacing = terrainSpacing;
            TerrainDetail = terrainDetail;
            BaseHeightUnits = baseHeightUnits;
            HeightVariationUnits = heightVariationUnits;
            EdgeLowering = edgeLowering;
            MountainScale = mountainScale;
            MountainHeightUnits = mountainHeightUnits;
            MountainCoverage = mountainCoverage;
            MountainSteepness = mountainSteepness;
            SeaLevelUnits = seaLevelUnits;
            RiverCount = riverCount;
            RiverDepthCells = riverDepthCells;
            MaximumRiverWidthCells = maximumRiverWidthCells;
            MaximumRiverDepthCells = maximumRiverDepthCells;
            LakeCount = lakeCount;
            MinimumInlandLakeDistance = minimumInlandLakeDistance;
            MinimumInlandLakeArea = minimumInlandLakeArea;
            MinimumInlandLakeDepthSteps = minimumInlandLakeDepthSteps;
            PondMaximumArea = pondMaximumArea;
            WaterFlowRules = waterFlowRules;
            DesertMoistureThreshold = desertMoistureThreshold;
            WetlandMoistureThreshold = wetlandMoistureThreshold;
            SnowTemperatureThreshold = snowTemperatureThreshold;
            WaterMoistureRadius = waterMoistureRadius;

            if (SeaLevelUnits <= 0
                || SeaLevelUnits >= WorldHeight * WorldGrid.HeightStepsPerCell)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seaLevelUnits),
                    "Sea level must be inside the vertical world range.");
            }
        }

        public int Seed { get; }
        public float CellSize { get; }
        public float HeightStep => CellSize / WorldGrid.HeightStepsPerCell;
        public int ChunkCellCountXZ { get; }
        public int ChunkCellCountY { get; }
        public int WorldChunkCountXZ { get; }
        public int WorldChunkCountY { get; }
        public int RenderChunksPerPatch { get; }
        public int WorldSize => checked(ChunkCellCountXZ * WorldChunkCountXZ);
        public int WorldHeight => checked(ChunkCellCountY * WorldChunkCountY);
        public int RenderPatchSizeXZ => checked(ChunkCellCountXZ * RenderChunksPerPatch);

        public float TerrainScale { get; }
        public int TerrainLayers { get; }
        public float TerrainSpacing { get; }
        public float TerrainDetail { get; }
        public int BaseHeightUnits { get; }
        public int HeightVariationUnits { get; }
        public float EdgeLowering { get; }
        public float MountainScale { get; }
        public int MountainHeightUnits { get; }
        public float MountainCoverage { get; }
        public float MountainSteepness { get; }
        public int SeaLevelUnits { get; }
        public int RiverCount { get; }
        public int RiverDepthCells { get; }
        public int MaximumRiverWidthCells { get; }
        public int MaximumRiverDepthCells { get; }
        public int LakeCount { get; }
        public int MinimumInlandLakeDistance { get; }
        public int MinimumInlandLakeArea { get; }
        public int MinimumInlandLakeDepthSteps { get; }
        public int PondMaximumArea { get; }
        public WaterFlowRules WaterFlowRules { get; }
        public float DesertMoistureThreshold { get; }
        public float WetlandMoistureThreshold { get; }
        public float SnowTemperatureThreshold { get; }
        public int WaterMoistureRadius { get; }
    }
}
