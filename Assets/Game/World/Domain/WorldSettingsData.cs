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
            int pondMaximumArea,
            WaterFlowRules waterFlowRules)
        {
            if (!Enum.IsDefined(typeof(WorldType), worldType))
            {
                throw new ArgumentOutOfRangeException(nameof(worldType));
            }

            if (!float.IsFinite(cellSize) || cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            }

            if (chunkCellCountXZ <= 0
                || chunkSectionCellCountY <= 0
                || initialChunkCountXZ <= 0
                || chunkSectionCountY <= 0
                || (initialChunkCountXZ & 1) == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCellCountXZ));
            }

            if (renderChunksPerPatch <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(renderChunksPerPatch));
            }

            if (roadMaxHeightSteps < 0
                || roadMaxHeightSteps > WorldGrid.HeightStepsPerCell)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roadMaxHeightSteps));
            }

            if (pondMaximumArea < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pondMaximumArea));
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
            PondMaximumArea = pondMaximumArea;
            WaterFlowRules = waterFlowRules;
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
        public int PondMaximumArea { get; }
        public WaterFlowRules WaterFlowRules { get; }
        public int InitialWorldSize => checked(
            ChunkCellCountXZ * InitialChunkCountXZ);
        public int WorldSize => InitialWorldSize;
        public int MinimumChunkCoordinate => -(InitialChunkCountXZ / 2);
        public int MaximumChunkCoordinate => InitialChunkCountXZ / 2;
        public int MinimumCellCoordinate => checked(
            MinimumChunkCoordinate * ChunkCellCountXZ);
        public int MaximumCellCoordinateExclusive => checked(
            (MaximumChunkCoordinate + 1) * ChunkCellCountXZ);
        public int WorldHeight => checked(
            ChunkSectionCellCountY * ChunkSectionCountY);
        public int RenderPatchSizeXZ => checked(
            ChunkCellCountXZ * RenderChunksPerPatch);
    }
}
