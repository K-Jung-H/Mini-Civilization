using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation.Patterns
{
    public sealed class WorldGenerationConfiguration
    {
        public WorldGenerationConfiguration(
            WorldSettingsData world,
            TerrainPatternSettingsData terrain,
            HydrologyFeatureSettingsData hydrology,
            PatternTileGridSettingsData patternTiles,
            int updateRangeChunks,
            int renderRangeChunks,
            int prepareRangeChunks,
            int chunkMaterializationsPerFrame,
            int maximumConcurrentTileBuilds)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            Hydrology = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
            PatternTiles = patternTiles ?? throw new ArgumentNullException(nameof(patternTiles));
            if (updateRangeChunks < 0
                || renderRangeChunks < updateRangeChunks
                || prepareRangeChunks < renderRangeChunks)
            {
                throw new ArgumentOutOfRangeException(nameof(prepareRangeChunks));
            }

            if (chunkMaterializationsPerFrame <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkMaterializationsPerFrame));
            }

            if (maximumConcurrentTileBuilds <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentTileBuilds));
            }

            UpdateRangeChunks = updateRangeChunks;
            RenderRangeChunks = renderRangeChunks;
            PrepareRangeChunks = prepareRangeChunks;
            ChunkMaterializationsPerFrame = chunkMaterializationsPerFrame;
            MaximumConcurrentTileBuilds = maximumConcurrentTileBuilds;
        }

        public WorldSettingsData World { get; }
        public TerrainPatternSettingsData Terrain { get; }
        public HydrologyFeatureSettingsData Hydrology { get; }
        public PatternTileGridSettingsData PatternTiles { get; }
        public int UpdateRangeChunks { get; }
        public int RenderRangeChunks { get; }
        public int PrepareRangeChunks { get; }
        public int ChunkMaterializationsPerFrame { get; }
        public int MaximumConcurrentTileBuilds { get; }
    }

    [CreateAssetMenu(
        fileName = "WorldGenerationSettings",
        menuName = "Mini Civilization/World/World Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [Header("World")]
        [SerializeField] private int seed;
        [SerializeField] private WorldType worldType;
        [SerializeField, Min(0.01f)] private float cellSize = 1f;
        [SerializeField, Min(1)] private int chunkCellCountXZ = 8;
        [SerializeField, Min(1)] private int chunkSectionCellCountY = 10;
        [SerializeField, Min(1)] private int initialChunkCountXZ = 15;
        [SerializeField, Min(1)] private int chunkSectionCountY = 10;
        [SerializeField, Min(1)] private int renderChunksPerPatch = 1;
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell)]
        private int roadMaxHeightSteps = WorldGrid.HeightStepsPerCell;
        [SerializeField, Min(1)] private int pondMaximumArea = 72;

        [Header("Water Flow")]
        [SerializeField, Range(0.01f, 1f)]
        private float spreadAmountLoss = 0.01f;
        [SerializeField, Range(0.01f, 1f)]
        private float minimumSpreadAmount = 0.05f;
        [SerializeField, Range(0.01f, 1f)]
        private float dissipationAmountLoss = 0.05f;

        [Header("World Generation Ranges")]
        [SerializeField, Min(0)] private int updateRangeChunks = 5;
        [SerializeField, Min(0)] private int renderRangeChunks = 7;
        [SerializeField, Min(0)] private int prepareRangeChunks = 10;
        [SerializeField, Min(1)] private int chunkMaterializationsPerFrame = 1;
        [SerializeField, Min(1)] private int maximumConcurrentTileBuilds = 2;

        [Header("Pattern Sources")]
        [SerializeField] private TerrainPatternSettings terrain;
        [SerializeField] private HydrologyFeatureSettings hydrology;

        public WorldGenerationConfiguration CreateConfiguration()
        {
            if (terrain == null || hydrology == null)
            {
                throw new InvalidOperationException(
                    "World Generation Settings requires Terrain and Hydrology Pattern Settings.");
            }

            var world = new WorldSettingsData(
                seed,
                worldType,
                cellSize,
                chunkCellCountXZ,
                chunkSectionCellCountY,
                initialChunkCountXZ,
                chunkSectionCountY,
                renderChunksPerPatch,
                roadMaxHeightSteps,
                pondMaximumArea,
                new WaterFlowRules(
                    spreadAmountLoss,
                    minimumSpreadAmount,
                    dissipationAmountLoss));
            var terrainData = terrain.CreateData(seed);
            var tiles = new PatternTileGridSettingsData(
                world,
                terrainData.PatternTileChunkSpan);
            var hydrologyData = hydrology.CreateData(world);
            if (world.PondMaximumArea
                != hydrologyData.Basins.PondMaximumAreaCells)
            {
                throw new InvalidOperationException(
                    "World and Hydrology Pond maximum areas must agree.");
            }

            return new WorldGenerationConfiguration(
                world,
                terrainData,
                hydrologyData,
                tiles,
                updateRangeChunks,
                renderRangeChunks,
                prepareRangeChunks,
                chunkMaterializationsPerFrame,
                maximumConcurrentTileBuilds);
        }
    }
}
