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
    public struct WorldSeededRangeSettings
    {
        [InspectorName("최소")]
        [SerializeField] private float minimum;
        [InspectorName("최대")]
        [SerializeField] private float maximum;

        public static WorldSeededRangeSettings Create(
            float minimum,
            float maximum) => new()
        {
            minimum = minimum,
            maximum = maximum
        };

        public WorldSeededRangeSettingsData CreateData(float scale = 1f) =>
            new(minimum * scale, maximum * scale);

        public bool TryValidate(
            float minimumAllowed,
            float maximumAllowed,
            out string error)
        {
            if (!float.IsFinite(minimum)
                || !float.IsFinite(maximum)
                || minimum < minimumAllowed
                || maximum < minimum
                || maximum > maximumAllowed)
            {
                error = "Seeded range settings are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize(float minimumAllowed, float maximumAllowed)
        {
            minimum = float.IsFinite(minimum)
                ? Math.Clamp(minimum, minimumAllowed, maximumAllowed)
                : minimumAllowed;
            maximum = float.IsFinite(maximum)
                ? Math.Clamp(maximum, minimum, maximumAllowed)
                : minimum;
        }
    }

    [Serializable]
    public struct TerrainDomainWarpSettings
    {
        [InspectorName("Warp Field")]
        [SerializeField] private WorldNoiseFieldSettings field;
        [InspectorName("Warp 강도 Cell")]
        [SerializeField, Min(0f)] private float strengthCells;

        public static TerrainDomainWarpSettings Create(
            WorldNoiseFieldSettings field,
            float strengthCells) => new()
        {
            field = field,
            strengthCells = strengthCells
        };

        public TerrainDomainWarpSettingsData CreateData() => new(
            field.CreateData(),
            strengthCells);

        public bool TryValidate(out string error)
        {
            if (!field.TryValidate(out error))
            {
                return false;
            }

            if (!float.IsFinite(strengthCells) || strengthCells < 0f)
            {
                error = "Terrain Domain Warp settings are invalid.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            field.Normalize();
            strengthCells = float.IsFinite(strengthCells)
                ? Math.Max(0f, strengthCells)
                : 0f;
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
        [InspectorName("Region 내부 도달 비율")]
        [SerializeField, Range(0.1f, 1f)] private float interiorReachRatio;
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
            interiorReachRatio = 0.35f,
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
            interiorReachRatio,
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
                || !float.IsFinite(interiorReachRatio)
                || interiorReachRatio < 0.1f
                || interiorReachRatio > 1f
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
            interiorReachRatio = float.IsFinite(interiorReachRatio)
                ? Math.Clamp(interiorReachRatio, 0.1f, 1f)
                : 0.35f;
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
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("좌표 굴곡")]
        [SerializeField] private TerrainDomainWarpSettings domainWarp;
        [InspectorName("높이 Field")]
        [SerializeField] private WorldNoiseFieldSettings heightField;
        [InspectorName("높이 Field 응답")]
        [SerializeField] private WorldCurveSettings heightResponse;
        [InspectorName("높이 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings heightAmplitudeCells;
        [InspectorName("세부 Field")]
        [SerializeField] private WorldNoiseFieldSettings detailField;
        [InspectorName("세부 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings detailAmplitudeCells;

        public static SmoothTerrainSettings Default => new()
        {
            initialized = true,
            domainWarp = TerrainDomainWarpSettings.Create(
                WorldNoiseFieldSettings.Create(
                    WorldNoiseMode.Signed,
                    0.006f,
                    3,
                    2f,
                    0.45f),
                10f),
            heightField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value,
                0.012f,
                4,
                2f,
                0.45f),
            heightResponse = WorldCurveSettings.Create(-1f, -0.45f, 0f, 0.45f, 1f),
            heightAmplitudeCells = WorldSeededRangeSettings.Create(1.5f, 4f),
            detailField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Signed,
                0.055f,
                3,
                2f,
                0.4f),
            detailAmplitudeCells = WorldSeededRangeSettings.Create(0.1f, 0.5f)
        };

        public SmoothTerrainSettingsData CreateData() => new(
            domainWarp.CreateData(),
            heightField.CreateData(),
            heightResponse.CreateData(),
            heightAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell),
            detailField.CreateData(),
            detailAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));

        public bool TryValidate(out string error)
        {
            if (!domainWarp.TryValidate(out error)
                || !heightField.TryValidate(out error)
                || !heightResponse.TryValidate(out error)
                || !heightAmplitudeCells.TryValidate(0f, 64f, out error)
                || !detailField.TryValidate(out error)
                || !detailAmplitudeCells.TryValidate(0f, 16f, out error))
            {
                return false;
            }

            if (!heightResponse.IsInside(-1f, 1f))
            {
                error = "Smooth Terrain response is invalid.";
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

            domainWarp.Normalize();
            heightField.Normalize();
            heightResponse.Clamp(-1f, 1f);
            heightAmplitudeCells.Normalize(0f, 64f);
            detailField.Normalize();
            detailAmplitudeCells.Normalize(0f, 16f);
        }
    }

    [Serializable]
    public struct RuggedTerrainSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("좌표 굴곡")]
        [SerializeField] private TerrainDomainWarpSettings domainWarp;
        [InspectorName("기복 Field")]
        [SerializeField] private WorldNoiseFieldSettings reliefField;
        [InspectorName("기복 Field 응답")]
        [SerializeField] private WorldCurveSettings reliefResponse;
        [InspectorName("기복 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings reliefAmplitudeCells;
        [InspectorName("세부 Field")]
        [SerializeField] private WorldNoiseFieldSettings detailField;
        [InspectorName("세부 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings detailAmplitudeCells;

        public static RuggedTerrainSettings Default => new()
        {
            initialized = true,
            domainWarp = TerrainDomainWarpSettings.Create(
                WorldNoiseFieldSettings.Create(
                    WorldNoiseMode.Signed,
                    0.007f,
                    3,
                    2f,
                    0.45f),
                14f),
            reliefField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Ridge,
                0.018f,
                5,
                2f,
                0.48f),
            reliefResponse = WorldCurveSettings.Create(-1f, -0.5f, 0f, 0.5f, 1f),
            reliefAmplitudeCells = WorldSeededRangeSettings.Create(4f, 10f),
            detailField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.SignedRidge,
                0.075f,
                4,
                2f,
                0.42f),
            detailAmplitudeCells = WorldSeededRangeSettings.Create(0.4f, 1.5f)
        };

        public RuggedTerrainSettingsData CreateData() => new(
            domainWarp.CreateData(),
            reliefField.CreateData(),
            reliefResponse.CreateData(),
            reliefAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell),
            detailField.CreateData(),
            detailAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));

        public bool TryValidate(out string error)
        {
            if (!domainWarp.TryValidate(out error)
                || !reliefField.TryValidate(out error)
                || !reliefResponse.TryValidate(out error)
                || !reliefAmplitudeCells.TryValidate(0f, 64f, out error)
                || !detailField.TryValidate(out error)
                || !detailAmplitudeCells.TryValidate(0f, 16f, out error))
            {
                return false;
            }

            if (!reliefResponse.IsInside(-1f, 1f))
            {
                error = "Rugged Terrain response is invalid.";
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

            domainWarp.Normalize();
            reliefField.Normalize();
            reliefResponse.Clamp(-1f, 1f);
            reliefAmplitudeCells.Normalize(0f, 64f);
            detailField.Normalize();
            detailAmplitudeCells.Normalize(0f, 16f);
        }
    }

    [Serializable]
    public struct MountainTerrainSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("좌표 굴곡")]
        [SerializeField] private TerrainDomainWarpSettings domainWarp;
        [InspectorName("산체 Field")]
        [SerializeField] private WorldNoiseFieldSettings massField;
        [InspectorName("산체 Field 응답")]
        [SerializeField] private WorldCurveSettings massResponse;
        [InspectorName("산체 높이 Cell")]
        [SerializeField] private WorldSeededRangeSettings heightCells;
        [InspectorName("능선망 Field")]
        [SerializeField] private WorldNoiseFieldSettings ridgeField;
        [InspectorName("능선망 Field 응답")]
        [SerializeField] private WorldCurveSettings ridgeResponse;
        [InspectorName("능선 높이 Cell")]
        [SerializeField] private WorldSeededRangeSettings ridgeStrengthCells;
        [InspectorName("표면 세부 Field")]
        [SerializeField] private WorldNoiseFieldSettings detailField;
        [InspectorName("표면 세부 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings detailAmplitudeCells;

        public static MountainTerrainSettings Default => new()
        {
            initialized = true,
            domainWarp = TerrainDomainWarpSettings.Create(
                WorldNoiseFieldSettings.Create(
                    WorldNoiseMode.Signed,
                    0.0045f,
                    4,
                    2f,
                    0.48f),
                24f),
            massField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value,
                0.006f,
                4,
                2f,
                0.5f),
            massResponse = WorldCurveSettings.Create(0.12f, 0.28f, 0.55f, 0.82f, 1f),
            heightCells = WorldSeededRangeSettings.Create(22f, 46f),
            ridgeField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Ridge,
                0.018f,
                5,
                2f,
                0.48f),
            ridgeResponse = WorldCurveSettings.Create(0f, 0.12f, 0.42f, 0.78f, 1f),
            ridgeStrengthCells = WorldSeededRangeSettings.Create(6f, 18f),
            detailField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Signed,
                0.06f,
                3,
                2f,
                0.42f),
            detailAmplitudeCells = WorldSeededRangeSettings.Create(0.5f, 2.5f)
        };

        public MountainTerrainSettingsData CreateData() => new(
            domainWarp.CreateData(),
            massField.CreateData(),
            massResponse.CreateData(),
            heightCells.CreateData(WorldGrid.HeightStepsPerCell),
            ridgeField.CreateData(),
            ridgeResponse.CreateData(),
            ridgeStrengthCells.CreateData(WorldGrid.HeightStepsPerCell),
            detailField.CreateData(),
            detailAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));

        public bool TryValidate(out string error)
        {
            if (!domainWarp.TryValidate(out error)
                || !massField.TryValidate(out error)
                || !massResponse.TryValidate(out error)
                || !heightCells.TryValidate(0f, 128f, out error)
                || !ridgeField.TryValidate(out error)
                || !ridgeResponse.TryValidate(out error)
                || !ridgeStrengthCells.TryValidate(0f, 64f, out error)
                || !detailField.TryValidate(out error)
                || !detailAmplitudeCells.TryValidate(0f, 16f, out error)
                || !massResponse.IsInside(0f, 1f)
                || !ridgeResponse.IsInside(0f, 1f))
            {
                error = "Mountain Terrain fields are invalid.";
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

            domainWarp.Normalize();
            massField.Normalize();
            massResponse.Clamp(0f, 1f);
            heightCells.Normalize(0f, 128f);
            ridgeField.Normalize();
            ridgeResponse.Clamp(0f, 1f);
            ridgeStrengthCells.Normalize(0f, 64f);
            detailField.Normalize();
            detailAmplitudeCells.Normalize(0f, 16f);
        }
    }

    [Serializable]
    public struct CanyonTerrainSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("좌표 굴곡")]
        [SerializeField] private TerrainDomainWarpSettings domainWarp;
        [InspectorName("분지 Field")]
        [SerializeField] private WorldNoiseFieldSettings basinField;
        [InspectorName("분지 Field 응답")]
        [SerializeField] private WorldCurveSettings basinResponse;
        [InspectorName("분지 깊이 비율")]
        [SerializeField] private WorldSeededRangeSettings basinDepthRatio;
        [InspectorName("골짜기망 Field")]
        [SerializeField] private WorldNoiseFieldSettings valleyField;
        [InspectorName("골짜기망 Field 응답")]
        [SerializeField] private WorldCurveSettings valleyResponse;
        [InspectorName("골짜기 깊이 비율")]
        [SerializeField] private WorldSeededRangeSettings valleyDepthRatio;
        [InspectorName("전체 깊이 Cell")]
        [SerializeField] private WorldSeededRangeSettings depthCells;
        [InspectorName("벽면 세부 Field")]
        [SerializeField] private WorldNoiseFieldSettings detailField;
        [InspectorName("벽면 세부 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings detailAmplitudeCells;

        public static CanyonTerrainSettings Default => new()
        {
            initialized = true,
            domainWarp = TerrainDomainWarpSettings.Create(
                WorldNoiseFieldSettings.Create(
                    WorldNoiseMode.Signed,
                    0.006f,
                    4,
                    2f,
                    0.48f),
                28f),
            basinField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value,
                0.006f,
                4,
                2f,
                0.5f),
            basinResponse = WorldCurveSettings.Create(0.25f, 0.38f, 0.55f, 0.72f, 0.88f),
            basinDepthRatio = WorldSeededRangeSettings.Create(0.35f, 0.65f),
            valleyField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Ridge,
                0.025f,
                5,
                2f,
                0.5f),
            valleyResponse = WorldCurveSettings.Create(0f, 0.05f, 0.32f, 0.72f, 1f),
            valleyDepthRatio = WorldSeededRangeSettings.Create(0.75f, 1f),
            depthCells = WorldSeededRangeSettings.Create(16f, 32f),
            detailField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Signed,
                0.065f,
                3,
                2f,
                0.4f),
            detailAmplitudeCells = WorldSeededRangeSettings.Create(0.1f, 0.6f)
        };

        public CanyonTerrainSettingsData CreateData() => new(
            domainWarp.CreateData(),
            basinField.CreateData(),
            basinResponse.CreateData(),
            basinDepthRatio.CreateData(),
            valleyField.CreateData(),
            valleyResponse.CreateData(),
            valleyDepthRatio.CreateData(),
            depthCells.CreateData(WorldGrid.HeightStepsPerCell),
            detailField.CreateData(),
            detailAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));

        public bool TryValidate(out string error)
        {
            if (!domainWarp.TryValidate(out error)
                || !basinField.TryValidate(out error)
                || !basinResponse.TryValidate(out error)
                || !basinDepthRatio.TryValidate(0f, 1f, out error)
                || !valleyField.TryValidate(out error)
                || !valleyResponse.TryValidate(out error)
                || !valleyDepthRatio.TryValidate(0f, 1f, out error)
                || !depthCells.TryValidate(0f, 128f, out error)
                || !detailField.TryValidate(out error)
                || !detailAmplitudeCells.TryValidate(0f, 16f, out error)
                || !basinResponse.IsInside(0f, 1f)
                || !valleyResponse.IsInside(0f, 1f))
            {
                error = "Canyon Terrain fields are invalid.";
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

            domainWarp.Normalize();
            basinField.Normalize();
            basinResponse.Clamp(0f, 1f);
            basinDepthRatio.Normalize(0f, 1f);
            valleyField.Normalize();
            valleyResponse.Clamp(0f, 1f);
            valleyDepthRatio.Normalize(0f, 1f);
            depthCells.Normalize(0f, 128f);
            detailField.Normalize();
            detailAmplitudeCells.Normalize(0f, 16f);
        }
    }

    [Serializable]
    public struct SeaPatternSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("좌표 굴곡")]
        [SerializeField] private TerrainDomainWarpSettings domainWarp;
        [InspectorName("분지 Field")]
        [SerializeField] private WorldNoiseFieldSettings basinField;
        [InspectorName("분지 Field 영향")]
        [SerializeField, Range(0f, 1f)] private float basinVariation;
        [InspectorName("분지 진행도별 수심")]
        [SerializeField] private WorldCurveSettings depthByBasin;
        [InspectorName("최대 깊이 Cell")]
        [SerializeField] private WorldSeededRangeSettings maximumDepthCells;
        [InspectorName("해저 Field")]
        [SerializeField] private WorldNoiseFieldSettings seabedField;
        [InspectorName("해저 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings seabedAmplitudeCells;
        [InspectorName("수면 Cell")]
        [SerializeField, Min(0)] private int surfaceCell;
        [InspectorName("수면 세부 높이")]
        [SerializeField, Range(0, WorldGrid.HeightStepsPerCell - 1)]
        private int surfaceStep;

        public static SeaPatternSettings Default => new()
        {
            initialized = true,
            domainWarp = TerrainDomainWarpSettings.Create(
                WorldNoiseFieldSettings.Create(
                    WorldNoiseMode.Signed,
                    0.0045f,
                    3,
                    2f,
                    0.45f),
                18f),
            basinField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value,
                0.005f,
                4,
                2f,
                0.48f),
            basinVariation = 0.15f,
            depthByBasin = WorldCurveSettings.Create(
                0f,
                0.05f,
                0.55f,
                1f,
                1f),
            maximumDepthCells = WorldSeededRangeSettings.Create(10f, 10f),
            seabedField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Signed,
                0.035f,
                4,
                2f,
                0.42f),
            seabedAmplitudeCells = WorldSeededRangeSettings.Create(0.25f, 0.8f),
            surfaceCell = 12,
            surfaceStep = 0
        };

        public SeaPatternSettingsData CreateData() => new(
            domainWarp.CreateData(),
            basinField.CreateData(),
            basinVariation,
            depthByBasin.CreateData(),
            maximumDepthCells.CreateData(WorldGrid.HeightStepsPerCell),
            seabedField.CreateData(),
            seabedAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell),
            checked(surfaceCell * WorldGrid.HeightStepsPerCell + surfaceStep));

        public bool TryValidate(out string error)
        {
            if (!domainWarp.TryValidate(out error)
                || !basinField.TryValidate(out error)
                || !depthByBasin.TryValidate(out error)
                || !maximumDepthCells.TryValidate(1f, 128f, out error)
                || !seabedField.TryValidate(out error)
                || !seabedAmplitudeCells.TryValidate(0f, 16f, out error))
            {
                return false;
            }

            if (!float.IsFinite(basinVariation)
                || basinVariation < 0f
                || basinVariation > 1f
                || !depthByBasin.IsInside(0f, 1f)
                || !depthByBasin.IsNonDecreasing()
                || surfaceCell < 0
                || surfaceStep < 0
                || surfaceStep >= WorldGrid.HeightStepsPerCell)
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

            domainWarp.Normalize();
            basinField.Normalize();
            basinVariation = float.IsFinite(basinVariation)
                ? Math.Clamp(basinVariation, 0f, 1f)
                : 0f;
            depthByBasin.Clamp(0f, 1f);
            maximumDepthCells.Normalize(1f, 128f);
            seabedField.Normalize();
            seabedAmplitudeCells.Normalize(0f, 16f);
            surfaceCell = Math.Max(0, surfaceCell);
            surfaceStep = Math.Clamp(
                surfaceStep,
                0,
                WorldGrid.HeightStepsPerCell - 1);
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
            smooth = SmoothTerrainSettings.Default,
            rugged = RuggedTerrainSettings.Default,
            mountain = MountainTerrainSettings.Default,
            canyon = CanyonTerrainSettings.Default,
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
        [Header("Hydrology")]
        [SerializeField] private HydrologySettings hydrology =
            HydrologySettings.Default;
        [Header("지형 기준")]
        [Tooltip("최종 바이옴 단계에서 사용할 온도 Field 크기입니다.")]
        [InspectorName("온도 Field 크기")]
        [SerializeField, Min(0.0001f)] private float temperatureScale = 0.003f;
        [Tooltip("물 수면과 독립적으로 Preliminary Terrain Density의 기준이 되는 절대 Y Cell 높이입니다.")]
        [InspectorName("지형 기준 높이")]
        [SerializeField, Min(0)] private int terrainBaseHeightCells = 10;

        [Header("동적 Water 분류")]
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
        public HydrologySettingsData Hydrology =>
            hydrology.CreateData(chunkCellCountXZ);
        public float TemperatureScale => temperatureScale;
        public int TerrainBaseHeightUnits => checked(
            terrainBaseHeightCells * WorldGrid.HeightStepsPerCell);
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
            Hydrology,
            TemperatureScale,
            TerrainBaseHeightUnits,
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
                || !worldPatterns.TryValidate(out error)
                || !hydrology.TryValidate(out error))
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
            hydrology.Normalize();
            temperatureScale = Mathf.Max(0.0001f, temperatureScale);
            pondMaximumArea = Math.Max(1, pondMaximumArea);
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
