using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation
{
    [CreateAssetMenu(fileName = "WorldGenerationSettings", menuName = "Mini Civilization/World Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [Header("월드 크기")]
        [Tooltip("월드의 X/Z 방향 길이입니다. 전체 수평 타일 수는 World Size × World Size입니다.")]
        [InspectorName("월드 크기")]
        [SerializeField, Min(8)] private int worldSize = 64;
        [Tooltip("월드의 Y 방향 Cell 층 수입니다. 각 Cell 높이는 양자화된 세부 단계로 나뉩니다.")]
        [InspectorName("월드 높이")]
        [SerializeField, Min(4)] private int worldHeight = 16;

        [Header("청크 및 렌더링")]
        [Tooltip("청크 하나가 담당하는 X/Z 방향 Cell 수입니다. World Size를 나누어떨어지게 설정해야 합니다.")]
        [InspectorName("청크 크기")]
        [SerializeField, Min(4)] private int chunkSizeXZ = 16;
        [Tooltip("청크 하나가 담당하는 Y 방향 Cell 층 수입니다. World Height를 나누어떨어지게 설정해야 합니다.")]
        [InspectorName("청크 높이")]
        [SerializeField, Min(1)] private int chunkHeight = 8;
        [Tooltip("하나의 지형 메시로 묶는 X/Z Cell 크기입니다. 논리 청크의 배수이면서 World Size를 나누어야 합니다.")]
        [InspectorName("메시 영역 크기")]
        [SerializeField, Min(4)] private int renderPatchSizeXZ = 32;
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
        [Tooltip("월드 가장자리의 지형을 낮추는 강도입니다. 값이 클수록 가장자리가 더 많이 잠겨 섬 형태가 강해집니다.")]
        [InspectorName("가장자리 낮춤")]
        [SerializeField, Range(0f, 1.5f)] private float edgeLowering = 0.85f;

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
        [Tooltip("해수면의 기본 수직 Cell 위치입니다. Sea Level Step과 합쳐 최종 해수면 높이를 결정합니다. 지형 전체가 최종 해수면보다 높으면 바다가 생성되지 않을 수 있습니다.")]
        [InspectorName("바다 높이")]
        [SerializeField, Min(0)] private int seaLevelCell = 5;
        [Tooltip("해수면 Cell 내부의 추가 양자화 높이 단계입니다. 한 단계는 Cell 높이의 1/5(월드 높이 0.2)입니다.")]
        [InspectorName("바다 세부 높이")]
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell - 1)] private int seaLevelStep;

        [Header("강 생성")]
        [Tooltip("생성을 시도할 강의 수입니다. 적합한 발원지나 경로가 부족하면 실제 생성 수는 더 적을 수 있습니다.")]
        [InspectorName("강 수")]
        [SerializeField, Range(0, 12)] private int riverCount = 4;
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
        [Tooltip("생성을 시도할 내륙 호수의 수입니다. 적합한 위치가 부족하면 실제 생성 수는 더 적을 수 있습니다.")]
        [InspectorName("호수 수")]
        [SerializeField, Range(0, 8)] private int lakeCount = 2;
        [Tooltip("생성할 내륙 호수와 바다 사이에 필요한 최소 D4 Cell 거리입니다.")]
        [InspectorName("바다와 최소 거리")]
        [SerializeField, Range(1, 32)] private int minimumInlandLakeDistance = 6;
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
        [Tooltip("수분 값이 이 값 이하인 육지를 사막으로 판정합니다.")]
        [InspectorName("사막 건조 기준")]
        [SerializeField, Range(0f, 1f)] private float desertMoistureThreshold = 0.28f;
        [Tooltip("수분 값이 이 값 이상이고 주변에 물의 영향이 있는 육지를 습지로 판정합니다.")]
        [InspectorName("습지 수분 기준")]
        [SerializeField, Range(0f, 1f)] private float wetlandMoistureThreshold = 0.72f;
        [Tooltip("온도 값이 이 값 이하인 육지를 툰드라로 판정하고 눈 재질을 적용합니다.")]
        [InspectorName("설원 온도 기준")]
        [SerializeField, Range(0f, 1f)] private float snowTemperatureThreshold = 0.24f;
        [Tooltip("바다, 강과 호수가 주변 타일의 수분에 영향을 주는 탐색 반지름입니다. 단위는 타일입니다.")]
        [InspectorName("물 영향 거리")]
        [SerializeField, Range(1, 12)] private int waterMoistureRadius = 6;

        public int WorldSize => worldSize;
        public int WorldHeight => worldHeight;
        public int ChunkSizeXZ => chunkSizeXZ;
        public int ChunkHeight => chunkHeight;
        public int RenderPatchSizeXZ => renderPatchSizeXZ;
        public float TerrainScale => terrainScale;
        public int TerrainLayers => terrainLayers;
        public float TerrainSpacing => terrainSpacing;
        public float TerrainDetail => terrainDetail;
        public int BaseHeightUnits => baseHeightCells * WorldGrid.HeightStepsPerCell;
        public int HeightVariationUnits => heightVariationCells * WorldGrid.HeightStepsPerCell;
        public float EdgeLowering => edgeLowering;
        public float MountainScale => mountainScale;
        public int MountainHeightUnits => mountainHeightCells * WorldGrid.HeightStepsPerCell;
        public float MountainCoverage => mountainCoverage;
        public float MountainSteepness => mountainSteepness;
        public int SeaLevelUnits => seaLevelCell * WorldGrid.HeightStepsPerCell + seaLevelStep;
        public int RiverCount => riverCount;
        public int RiverDepthCells => riverDepthCells;
        public int MaximumRiverWidthCells => maximumRiverWidthCells;
        public int MaximumRiverDepthCells => Math.Max(
            riverDepthCells,
            maximumRiverDepthCells);
        public int LakeCount => lakeCount;
        public int MinimumInlandLakeDistance => minimumInlandLakeDistance;
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
        public float DesertMoistureThreshold => desertMoistureThreshold;
        public float WetlandMoistureThreshold => wetlandMoistureThreshold;
        public float SnowTemperatureThreshold => snowTemperatureThreshold;
        public int WaterMoistureRadius => waterMoistureRadius;

        public void ConfigureDimensions(
            int size,
            int height,
            int horizontalChunkSize,
            int verticalChunkSize)
        {
            worldSize = size;
            worldHeight = height;
            chunkSizeXZ = horizontalChunkSize;
            chunkHeight = verticalChunkSize;
            renderPatchSizeXZ = Math.Min(size, horizontalChunkSize * 2);
            if (size % renderPatchSizeXZ != 0)
            {
                renderPatchSizeXZ = horizontalChunkSize;
            }
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

            if (renderPatchSizeXZ < chunkSizeXZ
                || renderPatchSizeXZ % chunkSizeXZ != 0
                || worldSize % renderPatchSizeXZ != 0)
            {
                error = "Render patch size must be a logical chunk multiple and divide the world size.";
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
            renderPatchSizeXZ = Math.Clamp(renderPatchSizeXZ, chunkSizeXZ, worldSize);
            baseHeightCells = Math.Clamp(baseHeightCells, 1, worldHeight - 1);
            heightVariationCells = Math.Clamp(heightVariationCells, 1, worldHeight - 1);
            mountainHeightCells = Math.Clamp(mountainHeightCells, 0, worldHeight - 1);
            seaLevelCell = Math.Clamp(seaLevelCell, 0, worldHeight - 1);
            minimumInlandLakeDistance = Math.Max(
                1,
                minimumInlandLakeDistance);
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
        }
    }
}
