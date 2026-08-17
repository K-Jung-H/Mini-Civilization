using System;
using MiniCivilization.World.Domain;
using UnityEngine;
using UnityEngine.Serialization;

namespace MiniCivilization.World.Generation
{
    [CreateAssetMenu(fileName = "WorldGenerationSettings", menuName = "Mini Civilization/World Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [Header("월드 구조")]
        [Tooltip("Finite는 초기 Chunk 범위를 월드 경계로 사용하고, Infinite는 범위 밖 Chunk를 Seed로 추가 생성합니다.")]
        [InspectorName("월드 타입")]
        [SerializeField] private WorldType worldType = WorldType.Finite;
        [Tooltip("Cell 한 변의 월드 단위 크기입니다. Cell은 항상 정육면체입니다.")]
        [InspectorName("Cell 크기")]
        [SerializeField, Min(0.01f)] private float cellSize = 10f;
        [Tooltip("논리 Chunk 하나의 X/Z 방향 Cell 수입니다.")]
        [InspectorName("Chunk XZ Cell 수")]
        [SerializeField, Min(1)] private int chunkCellCountXZ = 16;
        [Tooltip("ChunkSection 하나의 Y 방향 Cell 수입니다.")]
        [InspectorName("ChunkSection Y Cell 수")]
        [SerializeField, Min(1)] private int chunkSectionCellCountY = 8;
        [Tooltip("원점 Chunk를 중심으로 처음 생성할 X/Z 방향 Chunk 수입니다. 0 또는 짝수는 다음 홀수로 보정됩니다.")]
        [InspectorName("초기 XZ Chunk 수")]
        [FormerlySerializedAs("worldChunkCountXZ")]
        [SerializeField, Min(1)] private int initialChunkCountXZ = 5;
        [Tooltip("월드 Y 방향의 ChunkSection 수입니다.")]
        [InspectorName("월드 Y ChunkSection 수")]
        [SerializeField, Min(1)] private int chunkSectionCountY = 2;
        [Tooltip("렌더 Patch 한 변에 포함되는 논리 Chunk 수입니다.")]
        [InspectorName("Patch당 Chunk 수")]
        [SerializeField, Min(1)] private int renderChunksPerPatch = 2;
        [Header("Road")]
        [Tooltip("인접 Road가 연결될 수 있는 최대 표면 높이 차이입니다. 한 단계는 Cell 높이의 1/5입니다.")]
        [InspectorName("Road 최대 높이 단계")]
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell)]
        private int roadMaxHeightSteps = 1;
        [Header("기본 지형")]
        [Tooltip("지형 노이즈의 좌표 배율입니다. 값이 클수록 지형 변화가 더 짧은 간격으로 나타납니다.")]
        [InspectorName("지형 크기")]
        [SerializeField, Min(0.001f)] private float terrainScale = 0.035f;
        [Tooltip("서로 다른 크기의 지형 노이즈를 합성하는 횟수입니다. 값이 클수록 세부 굴곡이 많아집니다.")]
        [InspectorName("지형 세부 단계")]
        [SerializeField, Range(1, 8)] private int terrainLayers = 4;
        [Tooltip("다음 세부 단계로 갈수록 지형 무늬의 간격이 변하는 비율입니다. 값이 클수록 작은 굴곡의 간격이 빠르게 좁아집니다.")]
        [InspectorName("세부 간격")]
        [SerializeField, Range(1f, 4f)] private float terrainSpacing = 2f;
        [Tooltip("세부 단계가 늘어날 때 작은 굴곡이 유지되는 강도입니다. 값이 클수록 미세한 높이 변화가 뚜렷해집니다.")]
        [InspectorName("세부 강도")]
        [SerializeField, Range(0.1f, 0.9f)] private float terrainDetail = 0.5f;
        [Tooltip("노이즈와 가장자리 감쇠를 적용하기 전 지형의 기준 고도입니다. 단위는 수직 Cell입니다.")]
        [InspectorName("기본 높이")]
        [SerializeField, Min(1)] private int baseHeightCells = 6;
        [Tooltip("지형 노이즈가 기준 고도에서 위아래로 변화시킬 수 있는 높이 규모입니다. 단위는 수직 Cell입니다.")]
        [InspectorName("높이 변화")]
        [SerializeField, Min(1)] private int heightVariationCells = 5;
        [Header("산악 지형")]
        [Tooltip("산맥 능선을 만드는 Ridged Noise의 좌표 배율입니다. 값이 클수록 산맥 간격이 좁아집니다.")]
        [InspectorName("산맥 크기")]
        [SerializeField, Min(0.001f)] private float mountainScale = 0.025f;
        [Tooltip("산맥이 기본 지형 위로 추가되는 최대 높이입니다. 단위는 수직 Cell입니다.")]
        [InspectorName("산맥 높이")]
        [SerializeField, Min(0)] private int mountainHeightCells = 5;
        [Tooltip("산맥이 생성되기 시작하는 저주파 마스크 기준입니다. 값이 클수록 산악 지역이 드물어집니다.")]
        [InspectorName("산맥 분포")]
        [SerializeField, Range(0f, 0.95f)] private float mountainCoverage = 0.42f;
        [Tooltip("산맥 능선의 날카로움입니다. 값이 클수록 좁고 뚜렷한 능선이 생성됩니다.")]
        [InspectorName("산맥 경사")]
        [SerializeField, Range(1f, 6f)] private float mountainSteepness = 2.2f;

        [Header("바다")]
        [Tooltip("대륙과 바다 지역을 나누는 절대 좌표 Noise 크기입니다.")]
        [InspectorName("대륙 크기")]
        [SerializeField, Min(0.0001f)] private float continentalScale = 0.004f;
        [Tooltip("Continental Field가 이 값 이상인 영역을 육지로 판정합니다.")]
        [InspectorName("육지 기준")]
        [SerializeField, Range(0.05f, 0.95f)] private float landThreshold = 0.5f;
        [Tooltip("해안선에서 바다 높이와 육지 지형이 전이되는 Continental Field 범위입니다. 이 범위 밖의 육지는 지형 높이 변화를 그대로 유지합니다.")]
        [InspectorName("해안 전이 범위")]
        [SerializeField, Range(0.001f, 0.25f)]
        private float coastTransitionWidth = 0.08f;
        [Tooltip("해수면의 기본 수직 Cell 위치입니다. Sea Level Step과 합쳐 최종 해수면 높이를 결정합니다. 지형 전체가 최종 해수면보다 높으면 바다가 생성되지 않을 수 있습니다.")]
        [InspectorName("바다 높이")]
        [SerializeField, Min(0)] private int seaLevelCell = 5;
        [Tooltip("해수면 Cell 내부의 추가 양자화 높이 단계입니다. 한 단계는 Cell 높이의 1/5(월드 높이 0.2)입니다.")]
        [InspectorName("바다 세부 높이")]
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell - 1)] private int seaLevelStep;
        [Tooltip("Continental Field의 가장 깊은 Ocean 지형이 해수면 아래로 내려갈 최대 깊이입니다. 단위는 수직 Cell입니다.")]
        [InspectorName("바다 최대 깊이")]
        [SerializeField, Min(1)] private int maximumSeaDepthCells = 3;

        [Header("강 생성")]
        [Tooltip("연속된 River Channel Field의 좌표 배율입니다.")]
        [InspectorName("강 분포 크기")]
        [SerializeField, Min(0.0001f)] private float riverScale = 0.0125f;
        [Tooltip("육지에서 River Channel이 나타나는 밀도입니다.")]
        [InspectorName("강 밀도")]
        [SerializeField, Range(0f, 1f)] private float riverDensity = 0.2f;
        [Tooltip("폭 1인 River 구간에서 확보할 기본 수심입니다. 단위는 수직 Cell이며, WaterCell이 차지할 공간을 먼저 절삭합니다.")]
        [InspectorName("기본 강 깊이")]
        [SerializeField, Range(1, 3)] private int riverDepthCells = 2;
        [Tooltip("강 구간이 확장할 수 있는 최대 폭입니다. 긴 강일수록 평균 폭이 넓고 시작·끝보다 중심부가 넓어집니다. 현재 구조에서는 1·3·5 중 하나만 적용되며 지형 조건에 따라 더 좁아질 수 있습니다.")]
        [InspectorName("최대 강폭")]
        [SerializeField, Range(1, 5)] private int maximumRiverWidthCells = 3;
        [Tooltip("River 중심부가 확보할 수 있는 최대 수심입니다. 단위는 수직 Cell이며 기본 수심보다 작게 설정할 수 없습니다.")]
        [InspectorName("최대 강 깊이")]
        [SerializeField, Range(1, WorldGrid.HeightStepsPerCell)]
        private int maximumRiverDepthCells = 4;

        [Header("호수 생성")]
        [Tooltip("육지 분지 후보가 Lake 또는 Pond로 생성되는 밀도입니다.")]
        [InspectorName("호수 밀도")]
        [SerializeField, Range(0f, 1f)] private float lakeDensity = 0.15f;
        [Tooltip("Lake Basin 후보를 배치하는 절대 좌표 격자의 Cell 간격입니다.")]
        [InspectorName("호수 분포 간격")]
        [SerializeField, Min(4)] private int lakeRegionSizeCells = 32;
        [Tooltip("Lake Basin이 가질 수 있는 최대 반지름입니다. 단위는 Cell입니다.")]
        [InspectorName("호수 최대 반지름")]
        [SerializeField, Min(1)] private int maximumLakeRadiusCells = 8;
        [Tooltip("가장 큰 Lake Basin 중심부의 최대 깊이입니다. 한 단계는 Cell 높이의 1/5입니다.")]
        [InspectorName("호수 최대 깊이 단계")]
        [SerializeField, Range(1, WorldGrid.HeightStepsPerCell * 4)]
        private int maximumLakeDepthSteps = WorldGrid.HeightStepsPerCell * 2;
        [Tooltip("내륙 분지가 호수로 채택되기 위한 최소 수면 Column 수입니다.")]
        [InspectorName("최소 호수 크기")]
        [SerializeField, Range(1, 64)] private int minimumInlandLakeArea = 3;
        [Tooltip("내륙 분지가 호수로 채택되기 위한 최소 깊이 단계입니다.")]
        [InspectorName("최소 호수 깊이")]
        [SerializeField, Range(1, WorldGrid.HeightStepsPerCell)]
        private int minimumInlandLakeDepthSteps = 1;
        [Tooltip("연결된 수면 Column 수가 이 값 이하이면 Pond, 더 크면 Lake로 분류합니다.")]
        [InspectorName("연못 최대 크기")]
        [SerializeField, Range(1, 64)] private int pondMaximumArea = 8;

        [Header("유체 확산 및 소멸")]
        [Tooltip("물이 수평 인접 Cell로 한 번 확산될 때 감소하는 WaterAmount입니다. 아래 방향 확산에는 적용되지 않습니다.")]
        [InspectorName("확산 감소량")]
        [SerializeField, Range(WaterAmount.Unit, 1f)]
        private float spreadAmountLoss = 0.05f;
        [Tooltip("수평 확산 후 남은 WaterAmount가 이 값보다 작으면 해당 WaterCell을 생성하지 않습니다.")]
        [InspectorName("최소 확산량")]
        [SerializeField, Range(WaterAmount.Unit, 1f)]
        private float minimumSpreadAmount = 0.1f;
        [Tooltip("상류 공급량이 현재 WaterAmount보다 적을 때 시뮬레이션 단계마다 감소시키는 양입니다.")]
        [InspectorName("소멸량")]
        [SerializeField, Range(WaterAmount.Unit, 1f)]
        private float dissipationAmountLoss = 0.05f;

        [Header("바이옴 판정")]
        [Tooltip("계산된 기후 값이 이 값 이하이면 Cold, 1에서 이 값을 뺀 값 이상이면 Warm으로 판정합니다.")]
        [InspectorName("한랭 기후 기준")]
        [SerializeField, Range(0f, 0.5f)]
        private float coldClimateThreshold = 0.24f;

        public WorldType WorldType => worldType;
        public float CellSize => cellSize;
        public float HeightStep => cellSize / WorldGrid.HeightStepsPerCell;
        public int ChunkCellCountXZ => chunkCellCountXZ;
        public int ChunkSectionCellCountY => chunkSectionCellCountY;
        public int InitialChunkCountXZ => initialChunkCountXZ;
        public int WorldChunkCountXZ => initialChunkCountXZ;
        public int ChunkSectionCountY => chunkSectionCountY;
        public int RenderChunksPerPatch => renderChunksPerPatch;
        public int RoadMaxHeightSteps => roadMaxHeightSteps;
        public int WorldSize => checked(chunkCellCountXZ * initialChunkCountXZ);
        public int WorldHeight => checked(chunkSectionCellCountY * chunkSectionCountY);
        public int ChunkSizeXZ => chunkCellCountXZ;
        public int ChunkHeight => chunkSectionCellCountY;
        public int RenderPatchSizeXZ => checked(chunkCellCountXZ * renderChunksPerPatch);
        public float TerrainScale => terrainScale;
        public int TerrainLayers => terrainLayers;
        public float TerrainSpacing => terrainSpacing;
        public float TerrainDetail => terrainDetail;
        public int BaseHeightUnits => baseHeightCells * WorldGrid.HeightStepsPerCell;
        public int HeightVariationUnits => heightVariationCells * WorldGrid.HeightStepsPerCell;
        public float MountainScale => mountainScale;
        public int MountainHeightUnits => mountainHeightCells * WorldGrid.HeightStepsPerCell;
        public float MountainCoverage => mountainCoverage;
        public float MountainSteepness => mountainSteepness;
        public int SeaLevelUnits => seaLevelCell * WorldGrid.HeightStepsPerCell + seaLevelStep;
        public int MaximumSeaDepthUnits => Math.Min(
            Math.Max(1, SeaLevelUnits - 1),
            maximumSeaDepthCells * WorldGrid.HeightStepsPerCell);
        public float ContinentalScale => continentalScale;
        public float LandThreshold => landThreshold;
        public float CoastTransitionWidth => coastTransitionWidth;
        public float RiverScale => riverScale;
        public float RiverDensity => riverDensity;
        public int RiverDepthCells => riverDepthCells;
        public int MaximumRiverWidthCells => maximumRiverWidthCells;
        public int MaximumRiverDepthCells => Math.Max(
            riverDepthCells,
            maximumRiverDepthCells);
        public float LakeDensity => lakeDensity;
        public int LakeRegionSizeCells => lakeRegionSizeCells;
        public int MaximumLakeRadiusCells => maximumLakeRadiusCells;
        public int MaximumLakeDepthSteps => maximumLakeDepthSteps;
        public int MinimumInlandLakeArea => minimumInlandLakeArea;
        public int MinimumInlandLakeDepthSteps =>
            minimumInlandLakeDepthSteps;
        public int PondMaximumArea => pondMaximumArea;
        public float SpreadAmountLoss => spreadAmountLoss;
        public float MinimumSpreadAmount => minimumSpreadAmount;
        public float DissipationAmountLoss => dissipationAmountLoss;
        public WaterFlowRules WaterFlowRules => new(
            spreadAmountLoss,
            minimumSpreadAmount,
            dissipationAmountLoss);
        public float ColdClimateThreshold => coldClimateThreshold;

        public WorldSettingsData CreateData(int seed) => new(
            seed,
            WorldType,
            CellSize,
            ChunkCellCountXZ,
            ChunkSectionCellCountY,
            InitialChunkCountXZ,
            ChunkSectionCountY,
            RenderChunksPerPatch,
            RoadMaxHeightSteps,
            TerrainScale,
            TerrainLayers,
            TerrainSpacing,
            TerrainDetail,
            BaseHeightUnits,
            HeightVariationUnits,
            MountainScale,
            MountainHeightUnits,
            MountainCoverage,
            MountainSteepness,
            SeaLevelUnits,
            MaximumSeaDepthUnits,
            ContinentalScale,
            LandThreshold,
            CoastTransitionWidth,
            RiverScale,
            RiverDensity,
            RiverDepthCells,
            MaximumRiverWidthCells,
            MaximumRiverDepthCells,
            LakeDensity,
            LakeRegionSizeCells,
            MaximumLakeRadiusCells,
            MaximumLakeDepthSteps,
            MinimumInlandLakeArea,
            MinimumInlandLakeDepthSteps,
            PondMaximumArea,
            WaterFlowRules,
            ColdClimateThreshold);

        public void ConfigureDimensions(
            int size,
            int height,
            int horizontalChunkSize,
            int verticalChunkSize)
        {
            if (size <= 0 || height <= 0
                || horizontalChunkSize <= 0 || verticalChunkSize <= 0
                || size % horizontalChunkSize != 0
                || height % verticalChunkSize != 0)
            {
                throw new ArgumentException(
                    "World dimensions must be positive and divisible by their Chunk Cell counts.");
            }

            chunkCellCountXZ = horizontalChunkSize;
            chunkSectionCellCountY = verticalChunkSize;
            initialChunkCountXZ = NormalizeInitialChunkCount(
                Math.Max(1, size / horizontalChunkSize));
            chunkSectionCountY = Math.Max(1, height / verticalChunkSize);
            renderChunksPerPatch = 1;
            OnValidate();
        }

        public bool TryValidate(out string error)
        {
            if (!float.IsFinite(cellSize) || cellSize <= 0f)
            {
                error = "Cell size must be finite and positive.";
                return false;
            }

            if (chunkCellCountXZ <= 0 || chunkSectionCellCountY <= 0
                || initialChunkCountXZ <= 0 || chunkSectionCountY <= 0
                || (initialChunkCountXZ & 1) == 0)
            {
                error = "Chunk Cell counts must be positive and the initial horizontal Chunk count must be odd.";
                return false;
            }

            if (renderChunksPerPatch <= 0)
            {
                error = "Render chunks per patch must be positive.";
                return false;
            }

            if (SeaLevelUnits <= 1 || SeaLevelUnits >= WorldHeight * WorldGrid.HeightStepsPerCell)
            {
                error = "Sea level must be inside the vertical world range.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            cellSize = Mathf.Max(0.01f, cellSize);
            chunkCellCountXZ = Math.Max(1, chunkCellCountXZ);
            chunkSectionCellCountY = Math.Max(1, chunkSectionCellCountY);
            initialChunkCountXZ = NormalizeInitialChunkCount(
                initialChunkCountXZ);
            chunkSectionCountY = Math.Max(1, chunkSectionCountY);
            renderChunksPerPatch = Math.Clamp(
                renderChunksPerPatch,
                1,
                initialChunkCountXZ);
            roadMaxHeightSteps = Math.Clamp(
                roadMaxHeightSteps,
                0,
                WorldGrid.HeightStepsPerCell);
            baseHeightCells = Math.Clamp(baseHeightCells, 1, WorldHeight - 1);
            heightVariationCells = Math.Clamp(heightVariationCells, 1, WorldHeight - 1);
            mountainHeightCells = Math.Clamp(mountainHeightCells, 0, WorldHeight - 1);
            seaLevelCell = Math.Clamp(seaLevelCell, 0, WorldHeight - 1);
            maximumSeaDepthCells = Math.Clamp(
                maximumSeaDepthCells,
                1,
                Math.Max(1, seaLevelCell));
            continentalScale = Mathf.Max(0.0001f, continentalScale);
            landThreshold = Math.Clamp(landThreshold, 0.05f, 0.95f);
            coastTransitionWidth = Math.Clamp(
                coastTransitionWidth,
                0.001f,
                0.25f);
            riverScale = Mathf.Max(0.0001f, riverScale);
            riverDensity = Math.Clamp(riverDensity, 0f, 1f);
            lakeDensity = Math.Clamp(lakeDensity, 0f, 1f);
            lakeRegionSizeCells = Math.Max(4, lakeRegionSizeCells);
            maximumLakeRadiusCells = Math.Max(1, maximumLakeRadiusCells);
            maximumLakeDepthSteps = Math.Clamp(
                maximumLakeDepthSteps,
                1,
                WorldGrid.HeightStepsPerCell * 4);
            minimumInlandLakeArea = Math.Max(1, minimumInlandLakeArea);
            minimumInlandLakeDepthSteps = Math.Clamp(
                minimumInlandLakeDepthSteps,
                1,
                WorldGrid.HeightStepsPerCell);
            pondMaximumArea = Math.Max(1, pondMaximumArea);
            maximumRiverWidthCells = Math.Clamp(
                maximumRiverWidthCells,
                1,
                5);
            if ((maximumRiverWidthCells & 1) == 0)
            {
                maximumRiverWidthCells--;
            }

            maximumRiverDepthCells = Math.Clamp(
                maximumRiverDepthCells,
                riverDepthCells,
                WorldGrid.HeightStepsPerCell);
            spreadAmountLoss = Math.Clamp(
                spreadAmountLoss,
                WaterAmount.Unit,
                1f);
            minimumSpreadAmount = Math.Clamp(
                minimumSpreadAmount,
                WaterAmount.Unit,
                1f);
            dissipationAmountLoss = Math.Clamp(
                dissipationAmountLoss,
                WaterAmount.Unit,
                1f);
            coldClimateThreshold = Math.Clamp(
                coldClimateThreshold,
                0f,
                0.5f);
        }

        private static int NormalizeInitialChunkCount(int value)
        {
            value = Math.Max(1, value);
            return (value & 1) == 0 ? value + 1 : value;
        }
    }
}
