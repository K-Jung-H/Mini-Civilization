using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Authoring
{
    [CreateAssetMenu(fileName = "WorldGenerationSettings", menuName = "Mini Civilization/World Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [Header("World")]
        [SerializeField, Min(8)] private int worldSize = 64;
        [SerializeField, Min(4)] private int worldHeight = 16;
        [SerializeField, Min(4)] private int chunkSizeXZ = 16;
        [SerializeField, Min(1)] private int chunkHeight = 8;
        [SerializeField] private int seed = 12345;

        [Header("Terrain")]
        [SerializeField, Min(0.001f)] private float terrainNoiseScale = 0.035f;
        [SerializeField, Range(1, 8)] private int terrainOctaves = 4;
        [SerializeField, Range(1f, 4f)] private float terrainLacunarity = 2f;
        [SerializeField, Range(0.1f, 0.9f)] private float terrainPersistence = 0.5f;
        [SerializeField, Min(1)] private int baseTerrainHeightCells = 6;
        [SerializeField, Min(1)] private int terrainAmplitudeCells = 5;
        [SerializeField, Range(0f, 1.5f)] private float islandFalloff = 0.85f;

        [Header("Water")]
        [SerializeField, Min(0)] private int seaLevelCell = 5;
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell - 1)] private int seaLevelStep;
        [SerializeField, Range(0, 12)] private int riverCount = 4;
        [SerializeField, Range(1, 3)] private int riverDepthSteps = 2;
        [SerializeField, Range(0, 8)] private int lakeCount = 2;
        [SerializeField, Range(1, 5)] private int lakeRadius = 2;

        [Header("Biome")]
        [SerializeField, Range(0f, 1f)] private float desertMoistureThreshold = 0.28f;
        [SerializeField, Range(0f, 1f)] private float wetlandMoistureThreshold = 0.72f;
        [SerializeField, Range(0f, 1f)] private float snowTemperatureThreshold = 0.24f;
        [SerializeField, Range(1, 12)] private int waterMoistureRadius = 6;

        public int WorldSize => worldSize;
        public int WorldHeight => worldHeight;
        public int ChunkSizeXZ => chunkSizeXZ;
        public int ChunkHeight => chunkHeight;
        public int Seed => seed;
        public float TerrainNoiseScale => terrainNoiseScale;
        public int TerrainOctaves => terrainOctaves;
        public float TerrainLacunarity => terrainLacunarity;
        public float TerrainPersistence => terrainPersistence;
        public int BaseTerrainHeightUnits => baseTerrainHeightCells * WorldGrid.HeightStepsPerCell;
        public int TerrainAmplitudeUnits => terrainAmplitudeCells * WorldGrid.HeightStepsPerCell;
        public float IslandFalloff => islandFalloff;
        public int SeaLevelUnits => seaLevelCell * WorldGrid.HeightStepsPerCell + seaLevelStep;
        public int RiverCount => riverCount;
        public int RiverDepthSteps => riverDepthSteps;
        public int LakeCount => lakeCount;
        public int LakeRadius => lakeRadius;
        public float DesertMoistureThreshold => desertMoistureThreshold;
        public float WetlandMoistureThreshold => wetlandMoistureThreshold;
        public float SnowTemperatureThreshold => snowTemperatureThreshold;
        public int WaterMoistureRadius => waterMoistureRadius;

        public void SetSeed(int value) => seed = value;

        public void ConfigureDimensionsAndSeed(int size, int height, int horizontalChunkSize, int verticalChunkSize, int worldSeed)
        {
            worldSize = size;
            worldHeight = height;
            chunkSizeXZ = horizontalChunkSize;
            chunkHeight = verticalChunkSize;
            seed = worldSeed;
            OnValidate();
        }

        public bool TryValidate(out string error)
        {
            if (worldSize <= 0 || worldHeight <= 0)
            {
                error = "World dimensions must be positive.";
                return false;
            }

            if (chunkSizeXZ <= 0 || chunkHeight <= 0)
            {
                error = "Chunk dimensions must be positive.";
                return false;
            }

            if (worldSize % chunkSizeXZ != 0 || worldHeight % chunkHeight != 0)
            {
                error = "World dimensions must be divisible by chunk dimensions.";
                return false;
            }

            if (SeaLevelUnits <= 0 || SeaLevelUnits >= worldHeight * WorldGrid.HeightStepsPerCell)
            {
                error = "Sea level must be inside the vertical world range.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            worldSize = Math.Max(8, worldSize);
            worldHeight = Math.Max(4, worldHeight);
            chunkSizeXZ = Math.Clamp(chunkSizeXZ, 1, worldSize);
            chunkHeight = Math.Clamp(chunkHeight, 1, worldHeight);
            baseTerrainHeightCells = Math.Clamp(baseTerrainHeightCells, 1, worldHeight - 1);
            terrainAmplitudeCells = Math.Clamp(terrainAmplitudeCells, 1, worldHeight - 1);
            seaLevelCell = Math.Clamp(seaLevelCell, 0, worldHeight - 1);
        }
    }
}
