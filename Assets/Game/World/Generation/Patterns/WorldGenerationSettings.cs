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
            int chunkPreparePerFrame,
            int mapBuildConcurrency,
            int chunkActivatePerFrame = 1,
            int chunkUnloadPerFrame = 1,
            int meshPatchPerFrame = 2, ClimateSettings climate = null)
        {
            climateSettings = (climate ?? new ClimateSettings()).Snapshot();
            World = world ?? throw new ArgumentNullException(nameof(world));
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            Elevation = new ElevationPatternSettingsData(terrain, hydrology.Sea.SurfaceHeight,
                world.WorldHeight * WorldGrid.HeightStepsPerCell - 1 - hydrology.Sea.SurfaceHeight,
                hydrology.Sea.SurfaceHeight);
                foreach (var rule in climateSettings.TerrainRules)
                    if ((double)terrain.Region.SmoothShare * rule.Smooth
                        + (double)terrain.Region.RuggedShare * rule.Rugged
                        + (double)terrain.Region.MountainShare * rule.Mountain
                        + (double)terrain.Region.CanyonShare * rule.Canyon <= 0)
                        throw new ArgumentException("Climate and Terrain weights leave no land pattern available.", nameof(climate));
            Hydrology = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
            PatternTiles = patternTiles ?? throw new ArgumentNullException(nameof(patternTiles));
            if (updateRangeChunks < 0
                || renderRangeChunks < updateRangeChunks
                || prepareRangeChunks < renderRangeChunks)
            {
                throw new ArgumentOutOfRangeException(nameof(prepareRangeChunks));
            }

            if (chunkPreparePerFrame <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkPreparePerFrame));
            }

            if (mapBuildConcurrency <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mapBuildConcurrency));
            }

            if (chunkActivatePerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkActivatePerFrame));
            if (chunkUnloadPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkUnloadPerFrame));
            if (meshPatchPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(meshPatchPerFrame));
            ChunkActivatePerFrame = chunkActivatePerFrame;
            ChunkUnloadPerFrame = chunkUnloadPerFrame;
            MeshPatchPerFrame = meshPatchPerFrame;
            UpdateRangeChunks = updateRangeChunks;
            RenderRangeChunks = renderRangeChunks;
            PrepareRangeChunks = prepareRangeChunks;
            ChunkPreparePerFrame = chunkPreparePerFrame;
            MapBuildConcurrency = mapBuildConcurrency;
        }

        private readonly ClimateSettings climateSettings;
        public ClimateSettings Climate => climateSettings.Snapshot();
        public WorldSettingsData World { get; }
        public TerrainPatternSettingsData Terrain { get; }
        public ElevationPatternSettingsData Elevation { get; }
        public HydrologyFeatureSettingsData Hydrology { get; }
        public PatternTileGridSettingsData PatternTiles { get; }
        public int UpdateRangeChunks { get; }
        public int RenderRangeChunks { get; }
        public int PrepareRangeChunks { get; }
        public int ChunkPreparePerFrame { get; }
        public int MapBuildConcurrency { get; }
        public int ChunkActivatePerFrame { get; }
        public int ChunkUnloadPerFrame { get; }
        public int MeshPatchPerFrame { get; }
    }

    [CreateAssetMenu(
        fileName = "WorldGenerationSettings",
        menuName = "Mini Civilization/World/World Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [Header("World")]
        [Tooltip("동일한 설정에서 동일한 월드를 생성하는 기준 시드.")]
        [SerializeField] private int seed;
        [Tooltip("유한 월드 또는 무한 스트리밍 월드 선택.")]
        [SerializeField] private WorldType worldType;
        [Tooltip("셀 한 변의 월드 공간 크기.")]
        [SerializeField, Min(0.01f)] private float cellSize = 1f;
        [Tooltip("청크 한 변의 가로·세로 셀 수.")]
        [SerializeField, Min(1)] private int chunkCellCountXZ = 8;
        [Tooltip("청크 섹션 하나의 세로 셀 수.")]
        [SerializeField, Min(1)] private int chunkSectionCellCountY = 10;
        [Tooltip("초기 월드 한 변의 청크 수.")]
        [SerializeField, Min(1)] private int initialChunkCountXZ = 15;
        [Tooltip("청크의 세로 섹션 수로, 월드 높이를 결정합니다.")]
        [SerializeField, Min(1)] private int chunkSectionCountY = 10;
        [Tooltip("렌더 패치 한 변에 묶는 청크 수.")]
        [SerializeField, Min(1)] private int renderChunksPerPatch = 1;
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell)]
        [Tooltip("도로가 허용하는 최대 높이 차이(높이 단계).")]
        private int roadMaxHeightSteps = WorldGrid.HeightStepsPerCell;
        [Tooltip("연못 분류의 최대 면적으로, Hydrology의 Pond Maximum Area Cells와 같아야 합니다.")]
        [SerializeField, Min(1)] private int pondMaximumArea = 72;

        [Header("Water Flow")]
        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("물이 수평으로 한 셀 확산할 때 줄어드는 수량 비율.")]
        private float spreadAmountLoss = 0.01f;
        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("물 확산을 허용하는 최소 수량 비율.")]
        private float minimumSpreadAmount = 0.05f;
        [SerializeField, Range(0.01f, 1f)]
        [Tooltip("공급이 부족한 Dynamic 물이 갱신될 때 줄어드는 수량 비율.")]
        private float dissipationAmountLoss = 0.05f;

        [Header("World Generation Ranges")]
        [Tooltip("Streaming Target 주변에서 시뮬레이션을 갱신할 반경(청크).")]
        [SerializeField, Min(0)] private int updateRangeChunks = 5;
        [Tooltip("Streaming Target 주변에서 청크를 표시할 반경(청크).")]
        [SerializeField, Min(0)] private int renderRangeChunks = 7;
        [Tooltip("Streaming Target 주변에서 데이터를 미리 준비할 반경(청크).")]
        [SerializeField, Min(0)] private int prepareRangeChunks = 10;
        [Header("Streaming Processing")]
        [UnityEngine.Serialization.FormerlySerializedAs("chunkMaterializationsPerFrame")]
        [Tooltip("프레임당 준비할 청크 수의 상한.")]
        [SerializeField, Min(1)] private int chunkPreparePerFrame = 1;
        [Tooltip("프레임당 활성화할 청크 수의 상한.")]
        [SerializeField, Min(1)] private int chunkActivatePerFrame = 1;
        [Tooltip("프레임당 해제할 청크 수의 상한.")]
        [SerializeField, Min(1)] private int chunkUnloadPerFrame = 1;
        [Tooltip("프레임당 처리할 메시 패치 작업 수의 기준.")]
        [SerializeField, Min(1)] private int meshPatchPerFrame = 2;
        [UnityEngine.Serialization.FormerlySerializedAs("maximumConcurrentTileBuilds")]
        [Tooltip("동시에 실행할 패턴맵 생성 작업 수의 상한.")]
        [SerializeField, Min(1)] private int mapBuildConcurrency = 2;

        [Header("Pattern Sources")]
        [Tooltip("지형 패턴과 기본 고도에 사용할 설정 에셋.")]
        [SerializeField] private TerrainPatternSettings terrain;
        [Tooltip("해수면·호수·연못·강에 사용할 설정 에셋.")]
        [SerializeField] private HydrologyFeatureSettings hydrology;
        [Tooltip("온도·습도와 바이옴 판정 및 지형·수역 가중치 설정.")]
        [SerializeField] private ClimateSettings climate = new();

        // Streaming limits belong to the current machine, not the saved terrain definition.
        public WorldGenerationConfiguration ApplyStreamingSettings(WorldGenerationConfiguration saved) => new(
            saved.World, saved.Terrain, saved.Hydrology, saved.PatternTiles,
            updateRangeChunks, renderRangeChunks, prepareRangeChunks,
            chunkPreparePerFrame, mapBuildConcurrency,
            chunkActivatePerFrame, chunkUnloadPerFrame, meshPatchPerFrame, saved.Climate);

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
                chunkPreparePerFrame,
                mapBuildConcurrency,
                chunkActivatePerFrame,
                chunkUnloadPerFrame,
                meshPatchPerFrame, climate);
        }
    }
}
