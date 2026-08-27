using System;
using MiniCivilization.World.Domain;
using UnityEngine;
using UnityEngine.Serialization;

namespace MiniCivilization.World.Generation
{
    [Serializable]
    public struct TerrainNoiseFieldSettings
    {
        [InspectorName("출력 방식")]
        [SerializeField] private TerrainNoiseMode mode;
        [InspectorName("크기")]
        [SerializeField, Min(0.0001f)] private float scale;
        [InspectorName("단계")]
        [SerializeField, Range(1, 8)] private int layers;
        [InspectorName("주파수 간격")]
        [SerializeField, Range(1f, 4f)] private float frequencySpacing;
        [InspectorName("세부 유지율")]
        [SerializeField, Range(0.1f, 0.9f)] private float persistence;

        public static TerrainNoiseFieldSettings Create(
            TerrainNoiseMode mode,
            float scale,
            int layers = 3,
            float frequencySpacing = 2f,
            float persistence = 0.4f) => new()
        {
            mode = mode,
            scale = scale,
            layers = layers,
            frequencySpacing = frequencySpacing,
            persistence = persistence
        };

        public TerrainNoiseFieldSettingsData CreateData() => new(
            mode,
            scale,
            layers,
            frequencySpacing,
            persistence);

        public bool TryValidate(out string error)
        {
            if (!Enum.IsDefined(typeof(TerrainNoiseMode), mode)
                || !float.IsFinite(scale)
                || scale <= 0f
                || layers <= 0
                || !float.IsFinite(frequencySpacing)
                || frequencySpacing < 1f
                || !float.IsFinite(persistence)
                || persistence <= 0f
                || persistence >= 1f)
            {
                error = "Terrain Noise Field settings are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            if (!Enum.IsDefined(typeof(TerrainNoiseMode), mode))
            {
                mode = TerrainNoiseMode.Value;
            }

            scale = Mathf.Max(0.0001f, scale);
            layers = Math.Clamp(layers, 1, 8);
            frequencySpacing = Math.Clamp(frequencySpacing, 1f, 4f);
            persistence = Math.Clamp(persistence, 0.1f, 0.9f);
        }
    }

    [Serializable]
    public struct TerrainCurveSettings
    {
        [SerializeField] private float atZero;
        [SerializeField] private float atQuarter;
        [SerializeField] private float atHalf;
        [SerializeField] private float atThreeQuarters;
        [SerializeField] private float atOne;

        public static TerrainCurveSettings Create(
            float atZero,
            float atQuarter,
            float atHalf,
            float atThreeQuarters,
            float atOne) => new()
        {
            atZero = atZero,
            atQuarter = atQuarter,
            atHalf = atHalf,
            atThreeQuarters = atThreeQuarters,
            atOne = atOne
        };

        public static TerrainCurveSettings Constant(float value) =>
            Create(value, value, value, value, value);

        public TerrainCurveSettingsData CreateData(float scale = 1f) => new(
            atZero * scale,
            atQuarter * scale,
            atHalf * scale,
            atThreeQuarters * scale,
            atOne * scale);

        public bool TryValidate(out string error)
        {
            if (!float.IsFinite(atZero)
                || !float.IsFinite(atQuarter)
                || !float.IsFinite(atHalf)
                || !float.IsFinite(atThreeQuarters)
                || !float.IsFinite(atOne))
            {
                error = "Terrain curve contains a non-finite value.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool IsInside(float minimum, float maximum) =>
            atZero >= minimum && atZero <= maximum
            && atQuarter >= minimum && atQuarter <= maximum
            && atHalf >= minimum && atHalf <= maximum
            && atThreeQuarters >= minimum
            && atThreeQuarters <= maximum
            && atOne >= minimum && atOne <= maximum;

        public bool IsNonDecreasing() =>
            atZero <= atQuarter
            && atQuarter <= atHalf
            && atHalf <= atThreeQuarters
            && atThreeQuarters <= atOne;

        public void Clamp(float minimum, float maximum)
        {
            atZero = NormalizeValue(atZero, minimum, maximum);
            atQuarter = NormalizeValue(atQuarter, minimum, maximum);
            atHalf = NormalizeValue(atHalf, minimum, maximum);
            atThreeQuarters = NormalizeValue(
                atThreeQuarters,
                minimum,
                maximum);
            atOne = NormalizeValue(atOne, minimum, maximum);
        }

        private static float NormalizeValue(
            float value,
            float minimum,
            float maximum) => float.IsFinite(value)
            ? Math.Clamp(value, minimum, maximum)
            : Math.Clamp(0f, minimum, maximum);
    }

    [Serializable]
    public struct TerrainNoiseRouterSettings
    {
        [InspectorName("패턴 영역")]
        [SerializeField] private TerrainNoiseFieldSettings patternRegion;
        [InspectorName("대륙")]
        [SerializeField] private TerrainNoiseFieldSettings continentalness;
        [InspectorName("침식")]
        [SerializeField] private TerrainNoiseFieldSettings erosion;
        [InspectorName("변형")]
        [SerializeField] private TerrainNoiseFieldSettings weirdness;
        [InspectorName("봉우리·계곡")]
        [SerializeField] private TerrainNoiseFieldSettings peaksValleys;
        [InspectorName("거칠기")]
        [SerializeField] private TerrainNoiseFieldSettings roughness;
        [InspectorName("세부")]
        [SerializeField] private TerrainNoiseFieldSettings detail;

        public static TerrainNoiseRouterSettings Default => new()
        {
            patternRegion = TerrainNoiseFieldSettings.Create(
                TerrainNoiseMode.Value,
                0.0035f,
                1),
            continentalness = TerrainNoiseFieldSettings.Create(
                TerrainNoiseMode.Value,
                0.004f,
                4),
            erosion = TerrainNoiseFieldSettings.Create(
                TerrainNoiseMode.Value,
                0.0055f,
                4),
            weirdness = TerrainNoiseFieldSettings.Create(
                TerrainNoiseMode.Value,
                0.008f,
                4),
            peaksValleys = TerrainNoiseFieldSettings.Create(
                TerrainNoiseMode.Ridge,
                0.014f,
                4),
            roughness = TerrainNoiseFieldSettings.Create(
                TerrainNoiseMode.Value,
                0.018f,
                4),
            detail = TerrainNoiseFieldSettings.Create(
                TerrainNoiseMode.Signed,
                0.09f,
                3)
        };

        public TerrainNoiseRouterSettingsData CreateData() => new(
            patternRegion.CreateData(),
            continentalness.CreateData(),
            erosion.CreateData(),
            weirdness.CreateData(),
            peaksValleys.CreateData(),
            roughness.CreateData(),
            detail.CreateData());

        public bool TryValidate(out string error)
        {
            if (!patternRegion.TryValidate(out error)
                || !continentalness.TryValidate(out error)
                || !erosion.TryValidate(out error)
                || !weirdness.TryValidate(out error)
                || !peaksValleys.TryValidate(out error)
                || !roughness.TryValidate(out error)
                || !detail.TryValidate(out error))
            {
                return false;
            }

            var data = CreateData();
            if (data.PatternRegion.Mode != TerrainNoiseMode.Value
                || data.Continentalness.Mode != TerrainNoiseMode.Value
                || data.Erosion.Mode != TerrainNoiseMode.Value
                || data.Weirdness.Mode != TerrainNoiseMode.Value
                || data.PeaksValleys.Mode != TerrainNoiseMode.Ridge
                || data.Roughness.Mode != TerrainNoiseMode.Value
                || data.Detail.Mode != TerrainNoiseMode.Signed)
            {
                error = "Terrain Noise Router output modes are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            patternRegion.Normalize();
            continentalness.Normalize();
            erosion.Normalize();
            weirdness.Normalize();
            peaksValleys.Normalize();
            roughness.Normalize();
            detail.Normalize();
        }
    }

    [Serializable]
    public struct TerrainBaseDensitySettings
    {
        [InspectorName("표면: 대륙")]
        [SerializeField] private TerrainCurveSettings surfaceByContinentalness;
        [InspectorName("표면: 침식")]
        [SerializeField] private TerrainCurveSettings surfaceByErosion;
        [InspectorName("수직 밀도: 침식")]
        [SerializeField] private TerrainCurveSettings verticalFactorByErosion;
        [InspectorName("세부 굴곡: 거칠기")]
        [SerializeField] private TerrainCurveSettings detailByRoughness;

        public static TerrainBaseDensitySettings Create(
            TerrainCurveSettings surfaceByContinentalness,
            TerrainCurveSettings surfaceByErosion,
            TerrainCurveSettings verticalFactorByErosion,
            TerrainCurveSettings detailByRoughness) => new()
        {
            surfaceByContinentalness = surfaceByContinentalness,
            surfaceByErosion = surfaceByErosion,
            verticalFactorByErosion = verticalFactorByErosion,
            detailByRoughness = detailByRoughness
        };

        public TerrainBaseDensitySettingsData CreateData()
        {
            const float heightScale = WorldGrid.HeightStepsPerCell;
            return new TerrainBaseDensitySettingsData(
                surfaceByContinentalness.CreateData(heightScale),
                surfaceByErosion.CreateData(heightScale),
                verticalFactorByErosion.CreateData(),
                detailByRoughness.CreateData(heightScale));
        }

        public bool TryValidate(out string error)
        {
            if (!surfaceByContinentalness.TryValidate(out error)
                || !surfaceByErosion.TryValidate(out error)
                || !verticalFactorByErosion.TryValidate(out error)
                || !detailByRoughness.TryValidate(out error))
            {
                return false;
            }

            if (!verticalFactorByErosion.IsInside(0.05f, 8f)
                || !detailByRoughness.IsInside(0f, 64f))
            {
                error = "Base Terrain density curves are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            surfaceByContinentalness.Clamp(-64f, 64f);
            surfaceByErosion.Clamp(-64f, 64f);
            verticalFactorByErosion.Clamp(0.05f, 8f);
            detailByRoughness.Clamp(0f, 64f);
        }
    }

    [Serializable]
    public struct SmoothTerrainSettings
    {
        [InspectorName("패턴 영역 영향")]
        [SerializeField] private TerrainCurveSettings influenceByRegion;
        [InspectorName("완만한 높이 변화")]
        [SerializeField] private TerrainCurveSettings undulationByWeirdness;
        [InspectorName("세부 굴곡")]
        [SerializeField] private TerrainCurveSettings detailByRoughness;

        public static SmoothTerrainSettings Create(
            TerrainCurveSettings influenceByRegion,
            TerrainCurveSettings undulationByWeirdness,
            TerrainCurveSettings detailByRoughness) => new()
        {
            influenceByRegion = influenceByRegion,
            undulationByWeirdness = undulationByWeirdness,
            detailByRoughness = detailByRoughness
        };

        public SmoothTerrainSettingsData CreateData()
        {
            const float heightScale = WorldGrid.HeightStepsPerCell;
            return new SmoothTerrainSettingsData(
                influenceByRegion.CreateData(),
                undulationByWeirdness.CreateData(heightScale),
                detailByRoughness.CreateData(heightScale));
        }

        public bool TryValidate(out string error)
        {
            if (!influenceByRegion.TryValidate(out error)
                || !undulationByWeirdness.TryValidate(out error)
                || !detailByRoughness.TryValidate(out error))
            {
                return false;
            }

            if (!influenceByRegion.IsInside(0f, 1f)
                || !detailByRoughness.IsInside(0f, 64f))
            {
                error = "Smooth Terrain curves are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            influenceByRegion.Clamp(0f, 1f);
            undulationByWeirdness.Clamp(-64f, 64f);
            detailByRoughness.Clamp(0f, 64f);
        }
    }

    [Serializable]
    public struct RuggedTerrainSettings
    {
        [InspectorName("패턴 영역 영향")]
        [SerializeField] private TerrainCurveSettings influenceByRegion;
        [InspectorName("봉우리·계곡 굴곡")]
        [SerializeField] private TerrainCurveSettings reliefByPeaksValleys;
        [InspectorName("거칠기 배율")]
        [SerializeField] private TerrainCurveSettings reliefScaleByRoughness;
        [InspectorName("세부 굴곡")]
        [SerializeField] private TerrainCurveSettings detailByRoughness;

        public static RuggedTerrainSettings Create(
            TerrainCurveSettings influenceByRegion,
            TerrainCurveSettings reliefByPeaksValleys,
            TerrainCurveSettings reliefScaleByRoughness,
            TerrainCurveSettings detailByRoughness) => new()
        {
            influenceByRegion = influenceByRegion,
            reliefByPeaksValleys = reliefByPeaksValleys,
            reliefScaleByRoughness = reliefScaleByRoughness,
            detailByRoughness = detailByRoughness
        };

        public RuggedTerrainSettingsData CreateData()
        {
            const float heightScale = WorldGrid.HeightStepsPerCell;
            return new RuggedTerrainSettingsData(
                influenceByRegion.CreateData(),
                reliefByPeaksValleys.CreateData(heightScale),
                reliefScaleByRoughness.CreateData(),
                detailByRoughness.CreateData(heightScale));
        }

        public bool TryValidate(out string error)
        {
            if (!influenceByRegion.TryValidate(out error)
                || !reliefByPeaksValleys.TryValidate(out error)
                || !reliefScaleByRoughness.TryValidate(out error)
                || !detailByRoughness.TryValidate(out error))
            {
                return false;
            }

            if (!influenceByRegion.IsInside(0f, 1f)
                || !reliefScaleByRoughness.IsInside(0f, 8f)
                || !detailByRoughness.IsInside(0f, 64f))
            {
                error = "Rugged Terrain curves are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            influenceByRegion.Clamp(0f, 1f);
            reliefByPeaksValleys.Clamp(-64f, 64f);
            reliefScaleByRoughness.Clamp(0f, 8f);
            detailByRoughness.Clamp(0f, 64f);
        }
    }

    [Serializable]
    public struct MountainTerrainSettings
    {
        [InspectorName("패턴 영역 영향")]
        [SerializeField] private TerrainCurveSettings influenceByRegion;
        [InspectorName("영역 중심 접근도")]
        [SerializeField] private TerrainCurveSettings centerProximityByRegion;
        [InspectorName("중심 접근도별 높이")]
        [SerializeField] private TerrainCurveSettings heightByCenterProximity;
        [InspectorName("침식별 진행 속도")]
        [SerializeField] private TerrainCurveSettings progressExponentByErosion;

        public static MountainTerrainSettings Create(
            TerrainCurveSettings influenceByRegion,
            TerrainCurveSettings centerProximityByRegion,
            TerrainCurveSettings heightByCenterProximity,
            TerrainCurveSettings progressExponentByErosion) => new()
        {
            influenceByRegion = influenceByRegion,
            centerProximityByRegion = centerProximityByRegion,
            heightByCenterProximity = heightByCenterProximity,
            progressExponentByErosion = progressExponentByErosion
        };

        public MountainTerrainSettingsData CreateData()
        {
            const float heightScale = WorldGrid.HeightStepsPerCell;
            return new MountainTerrainSettingsData(
                influenceByRegion.CreateData(),
                centerProximityByRegion.CreateData(),
                heightByCenterProximity.CreateData(heightScale),
                progressExponentByErosion.CreateData());
        }

        public bool TryValidate(out string error)
        {
            if (!influenceByRegion.TryValidate(out error)
                || !centerProximityByRegion.TryValidate(out error)
                || !heightByCenterProximity.TryValidate(out error)
                || !progressExponentByErosion.TryValidate(out error))
            {
                return false;
            }

            if (!influenceByRegion.IsInside(0f, 1f)
                || !centerProximityByRegion.IsInside(0f, 1f)
                || !heightByCenterProximity.IsInside(0f, 64f)
                || !heightByCenterProximity.IsNonDecreasing()
                || !progressExponentByErosion.IsInside(0.25f, 4f))
            {
                error = "Mountain Terrain curves are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            influenceByRegion.Clamp(0f, 1f);
            centerProximityByRegion.Clamp(0f, 1f);
            heightByCenterProximity.Clamp(0f, 64f);
            progressExponentByErosion.Clamp(0.25f, 4f);
        }
    }

    [Serializable]
    public struct CanyonTerrainSettings
    {
        [InspectorName("패턴 영역 영향")]
        [SerializeField] private TerrainCurveSettings influenceByRegion;
        [InspectorName("협곡 폭")]
        [SerializeField] private TerrainCurveSettings widthByVariation;
        [InspectorName("최대 깊이")]
        [SerializeField] private TerrainCurveSettings maximumDepthByVariation;

        public static CanyonTerrainSettings Create(
            TerrainCurveSettings influenceByRegion,
            TerrainCurveSettings widthByVariation,
            TerrainCurveSettings maximumDepthByVariation) => new()
        {
            influenceByRegion = influenceByRegion,
            widthByVariation = widthByVariation,
            maximumDepthByVariation = maximumDepthByVariation
        };

        public CanyonTerrainSettingsData CreateData()
        {
            const float heightScale = WorldGrid.HeightStepsPerCell;
            return new CanyonTerrainSettingsData(
                influenceByRegion.CreateData(),
                widthByVariation.CreateData(),
                maximumDepthByVariation.CreateData(heightScale));
        }

        public bool TryValidate(out string error)
        {
            if (!influenceByRegion.TryValidate(out error)
                || !widthByVariation.TryValidate(out error)
                || !maximumDepthByVariation.TryValidate(out error))
            {
                return false;
            }

            if (!influenceByRegion.IsInside(0f, 1f)
                || !widthByVariation.IsInside(0.1f, 64f)
                || !maximumDepthByVariation.IsInside(0f, 64f))
            {
                error = "Canyon Terrain curves are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            influenceByRegion.Clamp(0f, 1f);
            widthByVariation.Clamp(0.1f, 64f);
            maximumDepthByVariation.Clamp(0f, 64f);
        }
    }

    [Serializable]
    public struct TerrainPatternSettings
    {
        [InspectorName("공통 기준 Density")]
        [SerializeField] private TerrainBaseDensitySettings baseDensity;
        [InspectorName("완만")]
        [SerializeField] private SmoothTerrainSettings smooth;
        [InspectorName("거친")]
        [SerializeField] private RuggedTerrainSettings rugged;
        [InspectorName("산맥")]
        [SerializeField] private MountainTerrainSettings mountain;
        [InspectorName("협곡")]
        [SerializeField] private CanyonTerrainSettings canyon;

        public static TerrainPatternSettings Default => new()
        {
            baseDensity = TerrainBaseDensitySettings.Create(
                TerrainCurveSettings.Create(-5f, -2f, 0f, 3f, 6f),
                TerrainCurveSettings.Create(2f, 1f, 0f, -1f, -2f),
                TerrainCurveSettings.Constant(1f),
                TerrainCurveSettings.Create(0.25f, 0.4f, 0.6f, 0.8f, 1f)),
            smooth = SmoothTerrainSettings.Create(
                TerrainCurveSettings.Create(1f, 1f, 0.2f, 0f, 0f),
                TerrainCurveSettings.Create(-2f, -1f, 0f, 1f, 2f),
                TerrainCurveSettings.Create(0.15f, 0.2f, 0.25f, 0.3f, 0.35f)),
            rugged = RuggedTerrainSettings.Create(
                TerrainCurveSettings.Create(0f, 0.5f, 1f, 0.35f, 0.15f),
                TerrainCurveSettings.Create(-4f, -2f, 0f, 4f, 8f),
                TerrainCurveSettings.Create(0.5f, 0.8f, 1f, 1.3f, 1.7f),
                TerrainCurveSettings.Create(0.5f, 0.8f, 1.2f, 1.8f, 2.5f)),
            mountain = MountainTerrainSettings.Create(
                TerrainCurveSettings.Create(0f, 0f, 0.15f, 1f, 0.1f),
                TerrainCurveSettings.Create(0f, 0f, 0f, 1f, 0f),
                TerrainCurveSettings.Create(0f, 4f, 11f, 21f, 30f),
                TerrainCurveSettings.Create(0.65f, 0.8f, 1f, 1.25f, 1.55f)),
            canyon = CanyonTerrainSettings.Create(
                TerrainCurveSettings.Create(0f, 0f, 0f, 0.1f, 1f),
                TerrainCurveSettings.Create(2f, 3f, 4f, 6f, 8f),
                TerrainCurveSettings.Create(12f, 15f, 18f, 22f, 26f))
        };

        public TerrainPatternSettingsData CreateData() => new(
            baseDensity.CreateData(),
            smooth.CreateData(),
            rugged.CreateData(),
            mountain.CreateData(),
            canyon.CreateData());

        public bool TryValidate(out string error) =>
            baseDensity.TryValidate(out error)
            && smooth.TryValidate(out error)
            && rugged.TryValidate(out error)
            && mountain.TryValidate(out error)
            && canyon.TryValidate(out error);

        public void Normalize()
        {
            baseDensity.Normalize();
            smooth.Normalize();
            rugged.Normalize();
            mountain.Normalize();
            canyon.Normalize();
        }
    }

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
        [Header("지형 Noise Router")]
        [SerializeField]
        private TerrainNoiseRouterSettings terrainNoiseRouter =
            TerrainNoiseRouterSettings.Default;
        [Header("지형 패턴")]
        [SerializeField]
        private TerrainPatternSettings terrainPatterns =
            TerrainPatternSettings.Default;
        [Header("지형 기준")]
        [Tooltip("최종 바이옴 단계에서 사용할 온도 Field 크기입니다.")]
        [InspectorName("온도 Field 크기")]
        [SerializeField, Min(0.0001f)] private float temperatureScale = 0.003f;
        [Tooltip("물 수면과 독립적으로 Preliminary Terrain Density의 기준이 되는 절대 Y Cell 높이입니다.")]
        [InspectorName("지형 기준 높이")]
        [SerializeField, Min(0)] private int terrainBaseHeightCells = 10;

        [Header("바다")]
        [Tooltip("Continental Field가 이 값 이상인 영역을 육지로 판정합니다.")]
        [InspectorName("육지 기준")]
        [SerializeField, Range(0.05f, 0.95f)] private float landThreshold = 0.5f;
        [Tooltip("Sea Water Distribution이 기본으로 사용할 절대 Y 수면 높이입니다. 다른 WaterBody는 각 생성 규칙에서 별도 수면 높이를 사용할 수 있습니다.")]
        [InspectorName("기본 바다 수면 높이")]
        [SerializeField, Min(0)] private int defaultSeaSurfaceCell = 12;
        [Tooltip("기본 Sea 수면 Cell 내부의 추가 양자화 높이 단계입니다. 한 단계는 Cell 높이의 1/5(월드 높이 0.2)입니다.")]
        [InspectorName("기본 바다 수면 세부 높이")]
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell - 1)]
        private int defaultSeaSurfaceStep;
        [Tooltip("Continental Field의 가장 깊은 Ocean 지형이 해수면 아래로 내려갈 최대 깊이입니다. 단위는 수직 Cell입니다.")]
        [InspectorName("바다 최대 깊이")]
        [SerializeField, Min(1)] private int maximumSeaDepthCells = 10;
        [Tooltip("Continental Field가 이 값 이하가 되면 바다가 최대 깊이에 도달합니다. 육지 기준보다 작아야 합니다.")]
        [InspectorName("심해 도달 기준")]
        [SerializeField, Range(0f, 0.95f)] private float deepSeaThreshold = 0.3f;
        [Tooltip("해안의 완만한 경사, 중간의 가파른 경사, 심해의 완만한 바닥을 만드는 깊이 곡선의 강도입니다.")]
        [InspectorName("바다 깊이 경사")]
        [SerializeField, Range(1f, 8f)] private float seaDepthSteepness = 3f;
        [Tooltip("바다 깊이 변화에 사용하는 Noise의 좌표 배율입니다.")]
        [InspectorName("바다 깊이 Noise 크기")]
        [SerializeField, Min(0.0001f)] private float seaDepthNoiseScale = 0.01f;
        [Tooltip("해안선과 최대 깊이는 유지하면서 중간 수심 경계를 불규칙하게 만드는 강도입니다.")]
        [InspectorName("바다 깊이 Noise 강도")]
        [SerializeField, Range(0f, 0.25f)] private float seaDepthNoiseStrength = 0.08f;

        [Header("강 생성")]
        [Tooltip("연속된 River Channel Field의 좌표 배율입니다.")]
        [InspectorName("강 분포 크기")]
        [SerializeField, Min(0.0001f)] private float riverScale = 0.0125f;
        [Tooltip("육지에서 River Channel이 나타나는 밀도입니다.")]
        [InspectorName("강 밀도")]
        [SerializeField, Range(0f, 1f)] private float riverDensity = 0.2f;
        [Tooltip("River Field 중심에서 확보할 기본 수심입니다. 단위는 수직 Cell이며 최종 Terrain Density에 반영됩니다.")]
        [InspectorName("기본 강 깊이")]
        [SerializeField, Range(1, 3)] private int riverDepthCells = 2;
        [Tooltip("River Field가 연속적으로 확장할 수 있는 최대 폭입니다. 단위는 X/Z Cell입니다.")]
        [InspectorName("최대 강폭")]
        [SerializeField, Range(1, 10)] private int maximumRiverWidthCells = 7;
        [Tooltip("River Field 중심부의 최대 수심입니다. 단위는 수직 Cell이며 기본 수심보다 작게 설정할 수 없습니다.")]
        [InspectorName("최대 강 깊이")]
        [SerializeField, Range(1, WorldGrid.HeightStepsPerCell)]
        private int maximumRiverDepthCells = 4;

        [Header("호수 생성")]
        [Tooltip("육지 분지 후보가 Lake 또는 Pond로 생성되는 밀도입니다.")]
        [InspectorName("호수 밀도")]
        [SerializeField, Range(0f, 1f)] private float lakeDensity = 0.15f;
        [Tooltip("불규칙 Lake/Pond Basin 후보를 배치하는 절대 좌표 격자의 Cell 간격입니다.")]
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
        public TerrainNoiseRouterSettingsData TerrainNoiseRouter =>
            terrainNoiseRouter.CreateData();
        public TerrainPatternSettingsData TerrainPatterns =>
            terrainPatterns.CreateData();
        public float TemperatureScale => temperatureScale;
        public int TerrainBaseHeightUnits => checked(
            terrainBaseHeightCells * WorldGrid.HeightStepsPerCell);
        public int DefaultSeaSurfaceUnits => checked(
            defaultSeaSurfaceCell * WorldGrid.HeightStepsPerCell
            + defaultSeaSurfaceStep);
        public int MaximumSeaDepthUnits => checked(
            maximumSeaDepthCells * WorldGrid.HeightStepsPerCell);
        public float LandThreshold => landThreshold;
        public float DeepSeaThreshold => deepSeaThreshold;
        public float SeaDepthSteepness => seaDepthSteepness;
        public float SeaDepthNoiseScale => seaDepthNoiseScale;
        public float SeaDepthNoiseStrength => seaDepthNoiseStrength;
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
            TerrainNoiseRouter,
            TerrainPatterns,
            TemperatureScale,
            TerrainBaseHeightUnits,
            DefaultSeaSurfaceUnits,
            MaximumSeaDepthUnits,
            LandThreshold,
            DeepSeaThreshold,
            SeaDepthSteepness,
            SeaDepthNoiseScale,
            SeaDepthNoiseStrength,
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

            if (!terrainNoiseRouter.TryValidate(out error)
                || !terrainPatterns.TryValidate(out error))
            {
                return false;
            }

            if (!float.IsFinite(temperatureScale)
                || temperatureScale <= 0f)
            {
                error = "Temperature Field scale is invalid.";
                return false;
            }

            if (DefaultSeaSurfaceUnits <= 1
                || DefaultSeaSurfaceUnits
                >= WorldHeight * WorldGrid.HeightStepsPerCell)
            {
                error = "The default Sea surface must be inside the vertical world range.";
                return false;
            }

            if (maximumSeaDepthCells <= 0
                || (long)maximumSeaDepthCells * WorldGrid.HeightStepsPerCell
                >= DefaultSeaSurfaceUnits)
            {
                error = "Maximum Sea depth must be positive and fit below the default Sea surface.";
                return false;
            }

            if (!float.IsFinite(deepSeaThreshold)
                || deepSeaThreshold < 0f
                || deepSeaThreshold >= landThreshold)
            {
                error = "Deep sea threshold must be non-negative and lower than the land threshold.";
                return false;
            }

            if (!float.IsFinite(seaDepthSteepness)
                || seaDepthSteepness < 1f
                || !float.IsFinite(seaDepthNoiseScale)
                || seaDepthNoiseScale <= 0f
                || !float.IsFinite(seaDepthNoiseStrength)
                || seaDepthNoiseStrength < 0f)
            {
                error = "Sea depth curve and Noise settings are invalid.";
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
            terrainBaseHeightCells = Math.Max(
                0,
                terrainBaseHeightCells);
            terrainNoiseRouter.Normalize();
            terrainPatterns.Normalize();
            temperatureScale = Mathf.Max(0.0001f, temperatureScale);
            defaultSeaSurfaceCell = Math.Clamp(
                defaultSeaSurfaceCell,
                0,
                WorldHeight - 1);
            maximumSeaDepthCells = Math.Max(1, maximumSeaDepthCells);
            landThreshold = Math.Clamp(landThreshold, 0.05f, 0.95f);
            deepSeaThreshold = Math.Clamp(deepSeaThreshold, 0f, 0.95f);
            seaDepthSteepness = Math.Clamp(seaDepthSteepness, 1f, 8f);
            seaDepthNoiseScale = Mathf.Max(0.0001f, seaDepthNoiseScale);
            seaDepthNoiseStrength = Math.Clamp(
                seaDepthNoiseStrength,
                0f,
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
                10);

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
