using System;
using MiniCivilization.World.Domain;
using UnityEngine;
using UnityEngine.Serialization;

namespace MiniCivilization.World.Generation
{
    [Serializable]
    public struct WorldNoiseFieldSettings
    {
        [InspectorName("출력 방식")]
        [SerializeField] private WorldNoiseMode mode;
        [InspectorName("크기")]
        [SerializeField, Min(0.0001f)] private float scale;
        [InspectorName("단계")]
        [SerializeField, Range(1, 8)] private int layers;
        [InspectorName("주파수 간격")]
        [SerializeField, Range(1f, 4f)] private float frequencySpacing;
        [InspectorName("세부 유지율")]
        [SerializeField, Range(0.1f, 0.9f)] private float persistence;

        public static WorldNoiseFieldSettings Create(
            WorldNoiseMode mode,
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

        public WorldNoiseFieldSettingsData CreateData() => new(
            mode,
            scale,
            layers,
            frequencySpacing,
            persistence);

        public bool TryValidate(out string error)
        {
            if (!Enum.IsDefined(typeof(WorldNoiseMode), mode)
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
            if (!Enum.IsDefined(typeof(WorldNoiseMode), mode))
            {
                mode = WorldNoiseMode.Value;
            }

            scale = Mathf.Max(0.0001f, scale);
            layers = Math.Clamp(layers, 1, 8);
            frequencySpacing = Math.Clamp(frequencySpacing, 1f, 4f);
            persistence = Math.Clamp(persistence, 0.1f, 0.9f);
        }
    }

    [Serializable]
    public struct WorldCurveSettings
    {
        [SerializeField] private float atZero;
        [SerializeField] private float atQuarter;
        [SerializeField] private float atHalf;
        [SerializeField] private float atThreeQuarters;
        [SerializeField] private float atOne;

        public static WorldCurveSettings Create(
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

        public static WorldCurveSettings Constant(float value) =>
            Create(value, value, value, value, value);

        public WorldCurveSettingsData CreateData(float scale = 1f) => new(
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
    public struct WorldNoiseRouterSettings
    {
        [InspectorName("대륙")]
        [SerializeField] private WorldNoiseFieldSettings continentalness;
        [InspectorName("침식")]
        [SerializeField] private WorldNoiseFieldSettings erosion;
        [InspectorName("변형")]
        [SerializeField] private WorldNoiseFieldSettings weirdness;
        [InspectorName("봉우리·계곡")]
        [SerializeField] private WorldNoiseFieldSettings peaksValleys;
        [InspectorName("거칠기")]
        [SerializeField] private WorldNoiseFieldSettings roughness;
        [InspectorName("세부")]
        [SerializeField] private WorldNoiseFieldSettings detail;
        [InspectorName("바다 형상 세부")]
        [SerializeField] private WorldNoiseFieldSettings seaDetail;

        public static WorldNoiseRouterSettings Default => new()
        {
            continentalness = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value,
                0.004f,
                4),
            erosion = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value,
                0.0055f,
                4),
            weirdness = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value,
                0.008f,
                4),
            peaksValleys = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Ridge,
                0.014f,
                4),
            roughness = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value,
                0.018f,
                4),
            detail = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Signed,
                0.09f,
                3),
            seaDetail = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Signed,
                0.012f,
                3)
        };

        public WorldNoiseRouterSettingsData CreateData() => new(
            continentalness.CreateData(),
            erosion.CreateData(),
            weirdness.CreateData(),
            peaksValleys.CreateData(),
            roughness.CreateData(),
            detail.CreateData(),
            seaDetail.CreateData());

        public bool TryValidate(out string error)
        {
            if (!continentalness.TryValidate(out error)
                || !erosion.TryValidate(out error)
                || !weirdness.TryValidate(out error)
                || !peaksValleys.TryValidate(out error)
                || !roughness.TryValidate(out error)
                || !detail.TryValidate(out error)
                || !seaDetail.TryValidate(out error))
            {
                return false;
            }

            var data = CreateData();
            if (data.Continentalness.Mode != WorldNoiseMode.Value
                || data.Erosion.Mode != WorldNoiseMode.Value
                || data.Weirdness.Mode != WorldNoiseMode.Value
                || data.PeaksValleys.Mode != WorldNoiseMode.Ridge
                || data.Roughness.Mode != WorldNoiseMode.Value
                || data.Detail.Mode != WorldNoiseMode.Signed
                || data.SeaDetail.Mode != WorldNoiseMode.Signed)
            {
                error = "World Noise Router output modes are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            continentalness.Normalize();
            erosion.Normalize();
            weirdness.Normalize();
            peaksValleys.Normalize();
            roughness.Normalize();
            detail.Normalize();
            seaDetail.Normalize();
        }
    }

    [Serializable]
    public struct WorldPatternRegionSettings
    {
        [InspectorName("Region 크기 Cell")]
        [SerializeField, Min(16)] private int sizeCells;
        [InspectorName("Region 중심 변형")]
        [SerializeField, Range(0f, 0.45f)] private float centerJitter;
        [InspectorName("Region 경계 Noise 크기")]
        [SerializeField, Min(0.0001f)] private float warpScale;
        [InspectorName("Region 경계 변형 Cell")]
        [SerializeField, Min(0f)] private float warpStrengthCells;
        [InspectorName("Region 경계 혼합 Cell")]
        [SerializeField, Min(0.1f)] private float boundaryBlendCells;
        [InspectorName("완만 분포")]
        [SerializeField, Min(0f)] private float smoothShare;
        [InspectorName("거친 분포")]
        [SerializeField, Min(0f)] private float ruggedShare;
        [InspectorName("산맥 분포")]
        [SerializeField, Min(0f)] private float mountainShare;
        [InspectorName("협곡 분포")]
        [SerializeField, Min(0f)] private float canyonShare;
        [InspectorName("바다 분포")]
        [SerializeField, Min(0f)] private float seaShare;

        public static WorldPatternRegionSettings Default => new()
        {
            sizeCells = 128,
            centerJitter = 0.35f,
            warpScale = 0.0025f,
            warpStrengthCells = 24f,
            boundaryBlendCells = 10f,
            smoothShare = 0.24f,
            ruggedShare = 0.24f,
            mountainShare = 0.20f,
            canyonShare = 0.16f,
            seaShare = 0.16f
        };

        public WorldPatternRegionSettingsData CreateData() => new(
            sizeCells,
            centerJitter,
            warpScale,
            warpStrengthCells,
            boundaryBlendCells,
            smoothShare,
            ruggedShare,
            mountainShare,
            canyonShare,
            seaShare);

        public bool TryValidate(out string error)
        {
            var data = CreateData();
            if (sizeCells < 16
                || !float.IsFinite(centerJitter)
                || centerJitter < 0f
                || centerJitter > 0.45f
                || !float.IsFinite(warpScale)
                || warpScale <= 0f
                || !float.IsFinite(warpStrengthCells)
                || warpStrengthCells < 0f
                || !float.IsFinite(boundaryBlendCells)
                || boundaryBlendCells <= 0f
                || !float.IsFinite(data.TotalShare)
                || data.TotalShare <= 0f)
            {
                error = "World Pattern Region settings are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            sizeCells = Math.Max(16, sizeCells);
            centerJitter = float.IsFinite(centerJitter)
                ? Math.Clamp(centerJitter, 0f, 0.45f)
                : 0f;
            warpScale = float.IsFinite(warpScale)
                ? Math.Max(0.0001f, warpScale)
                : 0.0025f;
            warpStrengthCells = float.IsFinite(warpStrengthCells)
                ? Math.Max(0f, warpStrengthCells)
                : 0f;
            boundaryBlendCells = float.IsFinite(boundaryBlendCells)
                ? Math.Max(0.1f, boundaryBlendCells)
                : 1f;
            smoothShare = NormalizeShare(smoothShare);
            ruggedShare = NormalizeShare(ruggedShare);
            mountainShare = NormalizeShare(mountainShare);
            canyonShare = NormalizeShare(canyonShare);
            seaShare = NormalizeShare(seaShare);
            if (smoothShare + ruggedShare + mountainShare + canyonShare
                + seaShare <= 0f)
            {
                this = Default;
            }
        }

        private static float NormalizeShare(float value) =>
            float.IsFinite(value) ? Math.Max(0f, value) : 0f;
    }

    [Serializable]
    public struct TerrainBaseDensitySettings
    {
        [InspectorName("표면: 대륙")]
        [SerializeField] private WorldCurveSettings surfaceByContinentalness;
        [InspectorName("표면: 침식")]
        [SerializeField] private WorldCurveSettings surfaceByErosion;
        [InspectorName("수직 밀도: 침식")]
        [SerializeField] private WorldCurveSettings verticalFactorByErosion;
        [InspectorName("세부 굴곡: 거칠기")]
        [SerializeField] private WorldCurveSettings detailByRoughness;

        public static TerrainBaseDensitySettings Create(
            WorldCurveSettings surfaceByContinentalness,
            WorldCurveSettings surfaceByErosion,
            WorldCurveSettings verticalFactorByErosion,
            WorldCurveSettings detailByRoughness) => new()
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
        [InspectorName("완만한 높이 변화")]
        [SerializeField] private WorldCurveSettings undulationByWeirdness;
        [InspectorName("세부 굴곡")]
        [SerializeField] private WorldCurveSettings detailByRoughness;

        public static SmoothTerrainSettings Create(
            WorldCurveSettings undulationByWeirdness,
            WorldCurveSettings detailByRoughness) => new()
        {
            undulationByWeirdness = undulationByWeirdness,
            detailByRoughness = detailByRoughness
        };

        public SmoothTerrainSettingsData CreateData()
        {
            const float heightScale = WorldGrid.HeightStepsPerCell;
            return new SmoothTerrainSettingsData(
                undulationByWeirdness.CreateData(heightScale),
                detailByRoughness.CreateData(heightScale));
        }

        public bool TryValidate(out string error)
        {
            if (!undulationByWeirdness.TryValidate(out error)
                || !detailByRoughness.TryValidate(out error))
            {
                return false;
            }

            if (!detailByRoughness.IsInside(0f, 64f))
            {
                error = "Smooth Terrain curves are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            undulationByWeirdness.Clamp(-64f, 64f);
            detailByRoughness.Clamp(0f, 64f);
        }
    }

    [Serializable]
    public struct RuggedTerrainSettings
    {
        [InspectorName("봉우리·계곡 굴곡")]
        [SerializeField] private WorldCurveSettings reliefByPeaksValleys;
        [InspectorName("거칠기 배율")]
        [SerializeField] private WorldCurveSettings reliefScaleByRoughness;
        [InspectorName("세부 굴곡")]
        [SerializeField] private WorldCurveSettings detailByRoughness;

        public static RuggedTerrainSettings Create(
            WorldCurveSettings reliefByPeaksValleys,
            WorldCurveSettings reliefScaleByRoughness,
            WorldCurveSettings detailByRoughness) => new()
        {
            reliefByPeaksValleys = reliefByPeaksValleys,
            reliefScaleByRoughness = reliefScaleByRoughness,
            detailByRoughness = detailByRoughness
        };

        public RuggedTerrainSettingsData CreateData()
        {
            const float heightScale = WorldGrid.HeightStepsPerCell;
            return new RuggedTerrainSettingsData(
                reliefByPeaksValleys.CreateData(heightScale),
                reliefScaleByRoughness.CreateData(),
                detailByRoughness.CreateData(heightScale));
        }

        public bool TryValidate(out string error)
        {
            if (!reliefByPeaksValleys.TryValidate(out error)
                || !reliefScaleByRoughness.TryValidate(out error)
                || !detailByRoughness.TryValidate(out error))
            {
                return false;
            }

            if (!reliefScaleByRoughness.IsInside(0f, 8f)
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
            reliefByPeaksValleys.Clamp(-64f, 64f);
            reliefScaleByRoughness.Clamp(0f, 8f);
            detailByRoughness.Clamp(0f, 64f);
        }
    }

    [Serializable]
    public struct MountainTerrainSettings
    {
        [InspectorName("최소 높이 Cell")]
        [SerializeField, Min(0f)] private float minimumHeightCells;
        [InspectorName("최대 높이 Cell")]
        [SerializeField, Min(0f)] private float maximumHeightCells;
        [InspectorName("높이 분포")]
        [SerializeField, Min(0.1f)] private float heightBias;
        [InspectorName("기울기 변화")]
        [SerializeField, Range(0f, 3f)] private float slopeVariation;
        [InspectorName("능선 높이 Cell")]
        [SerializeField, Min(0f)] private float ridgeStrengthCells;

        public static MountainTerrainSettings Create(
            float minimumHeightCells,
            float maximumHeightCells,
            float heightBias,
            float slopeVariation,
            float ridgeStrengthCells) => new()
        {
            minimumHeightCells = minimumHeightCells,
            maximumHeightCells = maximumHeightCells,
            heightBias = heightBias,
            slopeVariation = slopeVariation,
            ridgeStrengthCells = ridgeStrengthCells
        };

        public MountainTerrainSettingsData CreateData() => new(
            minimumHeightCells * WorldGrid.HeightStepsPerCell,
            maximumHeightCells * WorldGrid.HeightStepsPerCell,
            heightBias,
            slopeVariation,
            ridgeStrengthCells * WorldGrid.HeightStepsPerCell);

        public bool TryValidate(out string error)
        {
            if (!float.IsFinite(minimumHeightCells)
                || minimumHeightCells < 0f
                || !float.IsFinite(maximumHeightCells)
                || maximumHeightCells < minimumHeightCells
                || !float.IsFinite(heightBias)
                || heightBias < 0.1f
                || !float.IsFinite(slopeVariation)
                || slopeVariation < 0f
                || slopeVariation > 3f
                || !float.IsFinite(ridgeStrengthCells)
                || ridgeStrengthCells < 0f)
            {
                error = "Mountain Terrain curves are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            minimumHeightCells = NormalizeNonNegative(minimumHeightCells);
            maximumHeightCells = Math.Max(
                minimumHeightCells,
                NormalizeNonNegative(maximumHeightCells));
            heightBias = float.IsFinite(heightBias)
                ? Math.Max(0.1f, heightBias)
                : 1f;
            slopeVariation = float.IsFinite(slopeVariation)
                ? Math.Clamp(slopeVariation, 0f, 3f)
                : 0f;
            ridgeStrengthCells = NormalizeNonNegative(ridgeStrengthCells);
        }

        private static float NormalizeNonNegative(float value) =>
            float.IsFinite(value) ? Math.Max(0f, value) : 0f;
    }

    [Serializable]
    public struct CanyonTerrainSettings
    {
        [InspectorName("최소 폭 Cell")]
        [SerializeField, Min(0.5f)] private float minimumWidthCells;
        [InspectorName("최대 폭 Cell")]
        [SerializeField, Min(0.5f)] private float maximumWidthCells;
        [InspectorName("최소 깊이 Cell")]
        [SerializeField, Min(0f)] private float minimumDepthCells;
        [InspectorName("최대 깊이 Cell")]
        [SerializeField, Min(0f)] private float maximumDepthCells;
        [InspectorName("Region 최소 분지 깊이 비율")]
        [SerializeField, Range(0f, 1f)] private float minimumRegionDepthRatio;
        [InspectorName("Region 최대 분지 깊이 비율")]
        [SerializeField, Range(0f, 1f)] private float maximumRegionDepthRatio;
        [InspectorName("최소 골짜기 수")]
        [SerializeField, Min(1)] private int minimumValleyCount;
        [InspectorName("최대 골짜기 수")]
        [SerializeField, Min(1)] private int maximumValleyCount;
        [InspectorName("최대 골짜기 중심 Offset 비율")]
        [SerializeField, Range(0f, 0.5f)] private float maximumValleyOffsetRatio;
        [InspectorName("축 굴곡 Cell")]
        [SerializeField, Min(0f)] private float axisWarpCells;
        [InspectorName("벽면 세부 강도")]
        [SerializeField, Range(0f, 1f)] private float detailStrength;

        public static CanyonTerrainSettings Create(
            float minimumWidthCells,
            float maximumWidthCells,
            float minimumDepthCells,
            float maximumDepthCells,
            float minimumRegionDepthRatio,
            float maximumRegionDepthRatio,
            int minimumValleyCount,
            int maximumValleyCount,
            float maximumValleyOffsetRatio,
            float axisWarpCells,
            float detailStrength) => new()
        {
            minimumWidthCells = minimumWidthCells,
            maximumWidthCells = maximumWidthCells,
            minimumDepthCells = minimumDepthCells,
            maximumDepthCells = maximumDepthCells,
            minimumRegionDepthRatio = minimumRegionDepthRatio,
            maximumRegionDepthRatio = maximumRegionDepthRatio,
            minimumValleyCount = minimumValleyCount,
            maximumValleyCount = maximumValleyCount,
            maximumValleyOffsetRatio = maximumValleyOffsetRatio,
            axisWarpCells = axisWarpCells,
            detailStrength = detailStrength
        };

        public CanyonTerrainSettingsData CreateData() => new(
            minimumWidthCells,
            maximumWidthCells,
            minimumDepthCells * WorldGrid.HeightStepsPerCell,
            maximumDepthCells * WorldGrid.HeightStepsPerCell,
            minimumRegionDepthRatio,
            maximumRegionDepthRatio,
            minimumValleyCount,
            maximumValleyCount,
            maximumValleyOffsetRatio,
            axisWarpCells,
            detailStrength);

        public bool TryValidate(out string error)
        {
            if (!float.IsFinite(minimumWidthCells)
                || minimumWidthCells < 0.5f
                || !float.IsFinite(maximumWidthCells)
                || maximumWidthCells < minimumWidthCells
                || !float.IsFinite(minimumDepthCells)
                || minimumDepthCells < 0f
                || !float.IsFinite(maximumDepthCells)
                || maximumDepthCells < minimumDepthCells
                || !float.IsFinite(minimumRegionDepthRatio)
                || minimumRegionDepthRatio < 0f
                || minimumRegionDepthRatio > 1f
                || !float.IsFinite(maximumRegionDepthRatio)
                || maximumRegionDepthRatio < minimumRegionDepthRatio
                || maximumRegionDepthRatio > 1f
                || minimumValleyCount < 1
                || maximumValleyCount < minimumValleyCount
                || !float.IsFinite(maximumValleyOffsetRatio)
                || maximumValleyOffsetRatio < 0f
                || maximumValleyOffsetRatio > 0.5f
                || !float.IsFinite(axisWarpCells)
                || axisWarpCells < 0f
                || !float.IsFinite(detailStrength)
                || detailStrength < 0f
                || detailStrength > 1f)
            {
                error = "Canyon Terrain curves are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            minimumWidthCells = NormalizeAtLeast(minimumWidthCells, 0.5f);
            maximumWidthCells = Math.Max(
                minimumWidthCells,
                NormalizeAtLeast(maximumWidthCells, 0.5f));
            minimumDepthCells = NormalizeAtLeast(minimumDepthCells, 0f);
            maximumDepthCells = Math.Max(
                minimumDepthCells,
                NormalizeAtLeast(maximumDepthCells, 0f));
            minimumRegionDepthRatio = NormalizeUnit(minimumRegionDepthRatio);
            maximumRegionDepthRatio = Math.Max(
                minimumRegionDepthRatio,
                NormalizeUnit(maximumRegionDepthRatio));
            minimumValleyCount = Math.Max(1, minimumValleyCount);
            maximumValleyCount = Math.Max(minimumValleyCount, maximumValleyCount);
            maximumValleyOffsetRatio = float.IsFinite(maximumValleyOffsetRatio)
                ? Math.Clamp(maximumValleyOffsetRatio, 0f, 0.5f)
                : 0f;
            axisWarpCells = NormalizeAtLeast(axisWarpCells, 0f);
            detailStrength = NormalizeUnit(detailStrength);
        }

        private static float NormalizeAtLeast(float value, float minimum) =>
            float.IsFinite(value) ? Math.Max(minimum, value) : minimum;

        private static float NormalizeUnit(float value) =>
            float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    [Serializable]
    public struct SeaPatternSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("중심 접근도별 수심")]
        [SerializeField] private WorldCurveSettings depthByInterior;
        [InspectorName("최대 깊이 Cell")]
        [SerializeField, Min(1)] private int maximumDepthCells;
        [InspectorName("수면 Cell")]
        [SerializeField, Min(0)] private int surfaceCell;
        [InspectorName("수면 세부 높이")]
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell - 1)]
        private int surfaceStep;
        [InspectorName("해저 굴곡 강도")]
        [SerializeField, Range(0f, 0.5f)] private float shapeDetailStrength;

        public static SeaPatternSettings Default => new()
        {
            initialized = true,
            depthByInterior = WorldCurveSettings.Create(
                0f,
                0.05f,
                0.55f,
                1f,
                1f),
            maximumDepthCells = 10,
            surfaceCell = 12,
            surfaceStep = 0,
            shapeDetailStrength = 0.08f
        };

        public SeaPatternSettingsData CreateData() => new(
            depthByInterior.CreateData(),
            maximumDepthCells,
            checked(surfaceCell * WorldGrid.HeightStepsPerCell + surfaceStep),
            shapeDetailStrength);

        public bool TryValidate(out string error)
        {
            if (!depthByInterior.TryValidate(out error))
            {
                return false;
            }

            if (!depthByInterior.IsInside(0f, 1f)
                || !depthByInterior.IsNonDecreasing()
                || maximumDepthCells <= 0
                || surfaceCell < 0
                || surfaceStep < 0
                || surfaceStep >= WorldGrid.HeightStepsPerCell
                || !float.IsFinite(shapeDetailStrength)
                || shapeDetailStrength < 0f
                || shapeDetailStrength > 0.5f)
            {
                error = "Sea Pattern settings are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            if (!initialized)
            {
                this = Default;
                return;
            }

            depthByInterior.Clamp(0f, 1f);
            maximumDepthCells = Math.Max(1, maximumDepthCells);
            surfaceCell = Math.Max(0, surfaceCell);
            surfaceStep = Math.Clamp(
                surfaceStep,
                0,
                WorldGrid.HeightStepsPerCell - 1);
            shapeDetailStrength = float.IsFinite(shapeDetailStrength)
                ? Math.Clamp(shapeDetailStrength, 0f, 0.5f)
                : 0f;
        }
    }

    [Serializable]
    public struct WorldPatternSettings
    {
        [SerializeField, HideInInspector] private bool regionInitialized;
        [InspectorName("Region")]
        [SerializeField] private WorldPatternRegionSettings region;
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
        [InspectorName("바다")]
        [SerializeField] private SeaPatternSettings sea;

        public static WorldPatternSettings Default => new()
        {
            regionInitialized = true,
            region = WorldPatternRegionSettings.Default,
            baseDensity = TerrainBaseDensitySettings.Create(
                WorldCurveSettings.Create(-5f, -2f, 0f, 3f, 6f),
                WorldCurveSettings.Create(2f, 1f, 0f, -1f, -2f),
                WorldCurveSettings.Constant(1f),
                WorldCurveSettings.Create(0.25f, 0.4f, 0.6f, 0.8f, 1f)),
            smooth = SmoothTerrainSettings.Create(
                WorldCurveSettings.Create(-2f, -1f, 0f, 1f, 2f),
                WorldCurveSettings.Create(0.15f, 0.2f, 0.25f, 0.3f, 0.35f)),
            rugged = RuggedTerrainSettings.Create(
                WorldCurveSettings.Create(-4f, -2f, 0f, 4f, 8f),
                WorldCurveSettings.Create(0.5f, 0.8f, 1f, 1.3f, 1.7f),
                WorldCurveSettings.Create(0.5f, 0.8f, 1.2f, 1.8f, 2.5f)),
            mountain = MountainTerrainSettings.Create(
                14f,
                38f,
                0.8f,
                1.65f,
                5f),
            canyon = CanyonTerrainSettings.Create(
                3f,
                12f,
                12f,
                28f,
                0.45f,
                0.75f,
                3,
                7,
                0.28f,
                10f,
                0.15f),
            sea = SeaPatternSettings.Default
        };

        public WorldPatternSettingsData CreateData() => new(
            region.CreateData(),
            baseDensity.CreateData(),
            smooth.CreateData(),
            rugged.CreateData(),
            mountain.CreateData(),
            canyon.CreateData(),
            sea.CreateData());

        public bool TryValidate(out string error) =>
            region.TryValidate(out error)
            && baseDensity.TryValidate(out error)
            && smooth.TryValidate(out error)
            && rugged.TryValidate(out error)
            && mountain.TryValidate(out error)
            && canyon.TryValidate(out error)
            && sea.TryValidate(out error);

        public void Normalize()
        {
            if (!regionInitialized)
            {
                region = WorldPatternRegionSettings.Default;
                regionInitialized = true;
            }

            region.Normalize();
            baseDensity.Normalize();
            smooth.Normalize();
            rugged.Normalize();
            mountain.Normalize();
            canyon.Normalize();
            sea.Normalize();
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
        [Header("월드 Noise Router")]
        [FormerlySerializedAs("terrainNoiseRouter")]
        [SerializeField]
        private WorldNoiseRouterSettings worldNoiseRouter =
            WorldNoiseRouterSettings.Default;
        [Header("월드 패턴")]
        [FormerlySerializedAs("terrainPatterns")]
        [SerializeField]
        private WorldPatternSettings worldPatterns =
            WorldPatternSettings.Default;
        [Header("지형 기준")]
        [Tooltip("최종 바이옴 단계에서 사용할 온도 Field 크기입니다.")]
        [InspectorName("온도 Field 크기")]
        [SerializeField, Min(0.0001f)] private float temperatureScale = 0.003f;
        [Tooltip("물 수면과 독립적으로 Preliminary Terrain Density의 기준이 되는 절대 Y Cell 높이입니다.")]
        [InspectorName("지형 기준 높이")]
        [SerializeField, Min(0)] private int terrainBaseHeightCells = 10;

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
        public WorldNoiseRouterSettingsData WorldNoiseRouter =>
            worldNoiseRouter.CreateData();
        public WorldPatternSettingsData WorldPatterns =>
            worldPatterns.CreateData();
        public float TemperatureScale => temperatureScale;
        public int TerrainBaseHeightUnits => checked(
            terrainBaseHeightCells * WorldGrid.HeightStepsPerCell);
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
            WorldNoiseRouter,
            WorldPatterns,
            TemperatureScale,
            TerrainBaseHeightUnits,
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

            if (!worldNoiseRouter.TryValidate(out error)
                || !worldPatterns.TryValidate(out error))
            {
                return false;
            }

            if (!float.IsFinite(temperatureScale)
                || temperatureScale <= 0f)
            {
                error = "Temperature Field scale is invalid.";
                return false;
            }

            if (WorldPatterns.Sea.SurfaceUnits <= 1
                || WorldPatterns.Sea.SurfaceUnits
                    >= WorldHeight * WorldGrid.HeightStepsPerCell)
            {
                error = "The Sea Pattern surface must be inside the vertical world range.";
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
            worldNoiseRouter.Normalize();
            worldPatterns.Normalize();
            temperatureScale = Mathf.Max(0.0001f, temperatureScale);
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
