using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation
{
    [CreateAssetMenu(fileName = "WorldGenerationSettings", menuName = "Mini Civilization/World Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [Header("World")]
        [Tooltip("월드의 X/Z 방향 길이입니다. 전체 수평 타일 수는 World Size × World Size입니다.")]
        [SerializeField, Min(8)] private int worldSize = 64;
        [Tooltip("월드의 Y 방향 Cell 층 수입니다. 각 Cell 높이는 양자화된 세부 단계로 나뉩니다.")]
        [SerializeField, Min(4)] private int worldHeight = 16;
        [Tooltip("청크 하나가 담당하는 X/Z 방향 Cell 수입니다. World Size를 나누어떨어지게 설정해야 합니다.")]
        [SerializeField, Min(4)] private int chunkSizeXZ = 16;
        [Tooltip("청크 하나가 담당하는 Y 방향 Cell 층 수입니다. World Height를 나누어떨어지게 설정해야 합니다.")]
        [SerializeField, Min(1)] private int chunkHeight = 8;
        [Tooltip("하나의 지형 메시로 묶는 X/Z Cell 크기입니다. 논리 청크의 배수이면서 World Size를 나누어야 합니다.")]
        [SerializeField, Min(4)] private int renderPatchSizeXZ = 32;
        [Header("Terrain")]
        [Tooltip("지형 노이즈의 좌표 배율입니다. 값이 클수록 지형 변화가 더 짧은 간격으로 나타납니다.")]
        [SerializeField, Min(0.001f)] private float terrainNoiseScale = 0.035f;
        [Tooltip("서로 다른 크기의 지형 노이즈를 합성하는 횟수입니다. 값이 클수록 세부 굴곡이 많아집니다.")]
        [SerializeField, Range(1, 8)] private int terrainOctaves = 4;
        [Tooltip("Octave가 증가할 때 노이즈 주파수가 커지는 배율입니다. 값이 클수록 미세 지형의 간격이 빠르게 작아집니다.")]
        [SerializeField, Range(1f, 4f)] private float terrainLacunarity = 2f;
        [Tooltip("Octave가 증가할 때 노이즈 진폭이 유지되는 비율입니다. 값이 클수록 미세 지형의 높이 영향이 강해집니다.")]
        [SerializeField, Range(0.1f, 0.9f)] private float terrainPersistence = 0.5f;
        [Tooltip("노이즈와 가장자리 감쇠를 적용하기 전 지형의 기준 고도입니다. 단위는 수직 Cell입니다.")]
        [SerializeField, Min(1)] private int baseTerrainHeightCells = 6;
        [Tooltip("지형 노이즈가 기준 고도에서 위아래로 변화시킬 수 있는 높이 규모입니다. 단위는 수직 Cell입니다.")]
        [SerializeField, Min(1)] private int terrainAmplitudeCells = 5;
        [Tooltip("월드 가장자리의 지형을 낮추는 강도입니다. 값이 클수록 가장자리가 더 많이 잠겨 섬 형태가 강해집니다.")]
        [SerializeField, Range(0f, 1.5f)] private float islandFalloff = 0.85f;
        [Tooltip("산맥 능선을 만드는 Ridged Noise의 좌표 배율입니다. 값이 클수록 산맥 간격이 좁아집니다.")]
        [SerializeField, Min(0.001f)] private float mountainNoiseScale = 0.025f;
        [Tooltip("산맥이 기본 지형 위로 추가되는 최대 높이입니다. 단위는 수직 Cell입니다.")]
        [SerializeField, Min(0)] private int mountainStrengthCells = 5;
        [Tooltip("산맥이 생성되기 시작하는 저주파 마스크 기준입니다. 값이 클수록 산악 지역이 드물어집니다.")]
        [SerializeField, Range(0f, 0.95f)] private float mountainThreshold = 0.42f;
        [Tooltip("산맥 능선의 날카로움입니다. 값이 클수록 좁고 뚜렷한 능선이 생성됩니다.")]
        [SerializeField, Range(1f, 6f)] private float mountainSharpness = 2.2f;

        [Header("Water")]
        [Tooltip("해수면의 기본 수직 Cell 위치입니다. Sea Level Step과 합쳐 최종 해수면 높이를 결정합니다.")]
        [SerializeField, Min(0)] private int seaLevelCell = 5;
        [Tooltip("해수면 Cell 내부의 추가 양자화 높이 단계입니다. 한 단계는 Cell 높이의 1/5(월드 높이 0.2)입니다.")]
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell - 1)] private int seaLevelStep;
        [Tooltip("생성을 시도할 강의 수입니다. 적합한 발원지나 경로가 부족하면 실제 생성 수는 더 적을 수 있습니다.")]
        [SerializeField, Range(0, 12)] private int riverCount = 4;
        [Tooltip("강바닥을 수면보다 낮게 깎는 양자화 높이 단계 수입니다. 한 단계는 월드 높이 0.2입니다.")]
        [SerializeField, Range(1, 3)] private int riverDepthSteps = 2;
        [Tooltip("생성을 시도할 내륙 호수의 수입니다. 적합한 위치가 부족하면 실제 생성 수는 더 적을 수 있습니다.")]
        [SerializeField, Range(0, 8)] private int lakeCount = 2;
        [Tooltip("호수를 깎고 물을 채우는 수평 반지름입니다. 단위는 타일입니다.")]
        [SerializeField, Range(1, 5)] private int lakeRadius = 2;
        [Tooltip("WaterAmount lost whenever water spreads to a horizontal Cell. Downward spread does not lose Amount.")]
        [SerializeField, Range(WaterAmount.Unit, 1f)]
        private float spreadAmountLoss = 0.05f;
        [Tooltip("A horizontal spread candidate below this WaterAmount is not created.")]
        [SerializeField, Range(WaterAmount.Unit, 1f)]
        private float minimumSpreadAmount = 0.1f;

        [Header("Biome")]
        [Tooltip("수분 값이 이 값 이하인 육지를 사막으로 판정합니다.")]
        [SerializeField, Range(0f, 1f)] private float desertMoistureThreshold = 0.28f;
        [Tooltip("수분 값이 이 값 이상이고 주변에 물의 영향이 있는 육지를 습지로 판정합니다.")]
        [SerializeField, Range(0f, 1f)] private float wetlandMoistureThreshold = 0.72f;
        [Tooltip("온도 값이 이 값 이하인 육지를 툰드라로 판정하고 눈 재질을 적용합니다.")]
        [SerializeField, Range(0f, 1f)] private float snowTemperatureThreshold = 0.24f;
        [Tooltip("바다, 강과 호수가 주변 타일의 수분에 영향을 주는 탐색 반지름입니다. 단위는 타일입니다.")]
        [SerializeField, Range(1, 12)] private int waterMoistureRadius = 6;

        public int WorldSize => worldSize;
        public int WorldHeight => worldHeight;
        public int ChunkSizeXZ => chunkSizeXZ;
        public int ChunkHeight => chunkHeight;
        public int RenderPatchSizeXZ => renderPatchSizeXZ;
        public float TerrainNoiseScale => terrainNoiseScale;
        public int TerrainOctaves => terrainOctaves;
        public float TerrainLacunarity => terrainLacunarity;
        public float TerrainPersistence => terrainPersistence;
        public int BaseTerrainHeightUnits => baseTerrainHeightCells * WorldGrid.HeightStepsPerCell;
        public int TerrainAmplitudeUnits => terrainAmplitudeCells * WorldGrid.HeightStepsPerCell;
        public float IslandFalloff => islandFalloff;
        public float MountainNoiseScale => mountainNoiseScale;
        public int MountainStrengthUnits => mountainStrengthCells * WorldGrid.HeightStepsPerCell;
        public float MountainThreshold => mountainThreshold;
        public float MountainSharpness => mountainSharpness;
        public int SeaLevelUnits => seaLevelCell * WorldGrid.HeightStepsPerCell + seaLevelStep;
        public int RiverCount => riverCount;
        public int RiverDepthSteps => riverDepthSteps;
        public int LakeCount => lakeCount;
        public int LakeRadius => lakeRadius;
        public float SpreadAmountLoss => spreadAmountLoss;
        public float MinimumSpreadAmount => minimumSpreadAmount;
        public WaterFlowRules WaterFlowRules => new(
            spreadAmountLoss,
            minimumSpreadAmount);
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
            baseTerrainHeightCells = Math.Clamp(baseTerrainHeightCells, 1, worldHeight - 1);
            terrainAmplitudeCells = Math.Clamp(terrainAmplitudeCells, 1, worldHeight - 1);
            mountainStrengthCells = Math.Clamp(mountainStrengthCells, 0, worldHeight - 1);
            seaLevelCell = Math.Clamp(seaLevelCell, 0, worldHeight - 1);
            spreadAmountLoss = Math.Clamp(
                spreadAmountLoss,
                WaterAmount.Unit,
                1f);
            minimumSpreadAmount = Math.Clamp(
                minimumSpreadAmount,
                WaterAmount.Unit,
                1f);
        }
    }
}
