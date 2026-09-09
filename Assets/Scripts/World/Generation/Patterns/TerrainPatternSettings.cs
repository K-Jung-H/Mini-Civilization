using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation.Patterns
{
    public enum PatternNoiseMode : byte
    {
        Value,
        Signed,
        Ridge,
        SignedRidge
    }

    [Serializable]
    public struct PatternNoiseField
    {
        [Tooltip("노이즈 출력 형태: 기본값, 부호값, 능선 또는 부호가 있는 능선.")]
        [SerializeField] private PatternNoiseMode mode;
        [Tooltip("노이즈 좌표 배율로, 작을수록 넓고 완만한 패턴이 됩니다.")]
        [SerializeField] private float scale;
        [Tooltip("겹쳐서 합산할 노이즈 층 수.")]
        [SerializeField] private int layers;
        [Tooltip("다음 노이즈 층의 주파수 배율.")]
        [SerializeField] private float frequencySpacing;
        [Tooltip("다음 노이즈 층에 적용할 진폭 비율.")]
        [SerializeField] private float persistence;
        [Tooltip("노이즈 층마다 시드를 바꾸는 간격.")]
        [SerializeField] private int octaveSeedStride;

        internal TerrainNoiseFieldData CreateData() => new(
            mode,
            scale,
            layers,
            frequencySpacing,
            persistence,
            octaveSeedStride);
    }

    [Serializable]
    public struct PatternCurve
    {
        [Tooltip("입력 0일 때의 출력값.")]
        [SerializeField] private float atZero;
        [Tooltip("입력 0.25일 때의 출력값.")]
        [SerializeField] private float atQuarter;
        [Tooltip("입력 0.5일 때의 출력값.")]
        [SerializeField] private float atHalf;
        [Tooltip("입력 0.75일 때의 출력값.")]
        [SerializeField] private float atThreeQuarters;
        [Tooltip("입력 1일 때의 출력값.")]
        [SerializeField] private float atOne;

        internal TerrainCurveData CreateData(float scale = 1f) => new(
            atZero * scale,
            atQuarter * scale,
            atHalf * scale,
            atThreeQuarters * scale,
            atOne * scale);
    }

    [Serializable]
    public struct PatternRange
    {
        [Tooltip("선택 가능한 최솟값.")]
        [SerializeField] private float minimum;
        [Tooltip("선택 가능한 최댓값.")]
        [SerializeField] private float maximum;

        internal TerrainRangeData CreateData(float scale = 1f) => new(
            minimum * scale,
            maximum * scale);
    }

    [Serializable]
    public struct PatternDomainWarp
    {
        [Tooltip("좌표를 왜곡하는 데 사용할 노이즈.")]
        [SerializeField] private PatternNoiseField field;
        [Tooltip("좌표 왜곡의 최대 이동 크기(셀).")]
        [SerializeField] private float strengthCells;

        internal TerrainDomainWarpData CreateData() => new(
            field.CreateData(),
            strengthCells);
    }

    [Serializable]
    public struct TerrainNoiseRouterSettings
    {
        [Tooltip("대륙·해양 분포와 기본 고도를 결정하는 노이즈.")]
        [SerializeField] private PatternNoiseField continentalness;

        internal TerrainNoiseRouterData CreateData() => new(
            continentalness.CreateData());
    }

    [Serializable]
    public struct TerrainRegionSettings
    {
        [Tooltip("지형 패턴 영역의 기준 크기(셀).")]
        [SerializeField] private int sizeCells;
        [Tooltip("영역 크기에 비례한 중심점의 무작위 이동 비율.")]
        [SerializeField] private float centerJitter;
        [Tooltip("지형 영역의 경계를 왜곡하는 노이즈.")]
        [SerializeField] private PatternNoiseField warpField;
        [Tooltip("지형 영역 경계의 좌표 왜곡 크기(셀).")]
        [SerializeField] private float warpStrengthCells;
        [Tooltip("서로 다른 지형 패턴을 부드럽게 섞는 경계 폭(셀).")]
        [SerializeField] private float boundaryBlendCells;
        [Tooltip("영역 내부 진행도를 계산하는 기준 거리의 비율.")]
        [SerializeField] private float interiorReachRatio;
        [Tooltip("완만한 지형 패턴의 기본 선택 가중치.")]
        [SerializeField] private float smoothShare;
        [Tooltip("거친 지형 패턴의 기본 선택 가중치.")]
        [SerializeField] private float ruggedShare;
        [Tooltip("산악 지형 패턴의 기본 선택 가중치.")]
        [SerializeField] private float mountainShare;
        [Tooltip("협곡 지형 패턴의 기본 선택 가중치.")]
        [SerializeField] private float canyonShare;

        internal TerrainRegionData CreateData() => new(
            sizeCells,
            centerJitter,
            warpField.CreateData(),
            warpStrengthCells,
            boundaryBlendCells,
            interiorReachRatio,
            smoothShare,
            ruggedShare,
            mountainShare,
            canyonShare);
    }

    [Serializable]
    public struct TerrainSurfaceFormSettings
    {
        [Tooltip("패턴 모양의 규칙성을 줄이는 좌표 왜곡 설정.")]
        [SerializeField] private PatternDomainWarp domainWarp;
        [Tooltip("지형의 큰 굴곡을 만드는 노이즈.")]
        [SerializeField] private PatternNoiseField shapeField;
        [Tooltip("큰 굴곡 노이즈를 높이 변화로 변환하는 곡선.")]
        [SerializeField] private PatternCurve shapeResponse;
        [Tooltip("큰 굴곡의 높이 변화 범위(셀).")]
        [SerializeField] private PatternRange shapeAmplitudeCells;
        [Tooltip("지형 표면의 세부 요철 노이즈.")]
        [SerializeField] private PatternNoiseField detailField;
        [Tooltip("세부 요철의 높이 변화 범위(셀).")]
        [SerializeField] private PatternRange detailAmplitudeCells;

        internal TerrainSurfaceFormData CreateData() => new(
            domainWarp.CreateData(),
            shapeField.CreateData(),
            shapeResponse.CreateData(),
            shapeAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell),
            detailField.CreateData(),
            detailAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));
    }

    [Serializable]
    public struct MountainFormSettings
    {
        [Tooltip("패턴 모양의 규칙성을 줄이는 좌표 왜곡 설정.")]
        [SerializeField] private PatternDomainWarp domainWarp;
        [Tooltip("산악 지형의 큰 산체 형태를 만드는 노이즈.")]
        [SerializeField] private PatternNoiseField massField;
        [Tooltip("산체 노이즈를 높이 기여도로 변환하는 곡선.")]
        [SerializeField] private PatternCurve massResponse;
        [Tooltip("산체 높이의 범위(셀).")]
        [SerializeField] private PatternRange heightCells;
        [Tooltip("산 능선의 형태를 만드는 노이즈.")]
        [SerializeField] private PatternNoiseField ridgeField;
        [Tooltip("능선 노이즈를 높이 기여도로 변환하는 곡선.")]
        [SerializeField] private PatternCurve ridgeResponse;
        [Tooltip("능선의 높이 변화 범위(셀).")]
        [SerializeField] private PatternRange ridgeStrengthCells;
        [Tooltip("지형 표면의 세부 요철 노이즈.")]
        [SerializeField] private PatternNoiseField detailField;
        [Tooltip("세부 요철의 높이 변화 범위(셀).")]
        [SerializeField] private PatternRange detailAmplitudeCells;

        internal TerrainMountainFormData CreateData() => new(
            domainWarp.CreateData(),
            massField.CreateData(),
            massResponse.CreateData(),
            heightCells.CreateData(WorldGrid.HeightStepsPerCell),
            ridgeField.CreateData(),
            ridgeResponse.CreateData(),
            ridgeStrengthCells.CreateData(WorldGrid.HeightStepsPerCell),
            detailField.CreateData(),
            detailAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));
    }

    [Serializable]
    public struct CanyonFormSettings
    {
        [Tooltip("패턴 모양의 규칙성을 줄이는 좌표 왜곡 설정.")]
        [SerializeField] private PatternDomainWarp domainWarp;
        [Tooltip("협곡의 넓은 함몰 영역을 만드는 노이즈.")]
        [SerializeField] private PatternNoiseField basinField;
        [Tooltip("함몰 노이즈를 깊이 기여도로 변환하는 곡선.")]
        [SerializeField] private PatternCurve basinResponse;
        [Tooltip("전체 협곡 깊이에 대한 넓은 함몰의 깊이 비율.")]
        [SerializeField] private PatternRange basinDepthRatio;
        [Tooltip("협곡 내부 골짜기의 형태를 만드는 노이즈.")]
        [SerializeField] private PatternNoiseField valleyField;
        [Tooltip("골짜기 노이즈를 깊이 기여도로 변환하는 곡선.")]
        [SerializeField] private PatternCurve valleyResponse;
        [Tooltip("전체 협곡 깊이에 대한 골짜기의 깊이 비율.")]
        [SerializeField] private PatternRange valleyDepthRatio;
        [Tooltip("깎아내리는 깊이 범위(셀).")]
        [SerializeField] private PatternRange depthCells;
        [Tooltip("지형 표면의 세부 요철 노이즈.")]
        [SerializeField] private PatternNoiseField detailField;
        [Tooltip("세부 요철의 높이 변화 범위(셀).")]
        [SerializeField] private PatternRange detailAmplitudeCells;

        internal TerrainCanyonFormData CreateData() => new(
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
    }

    [CreateAssetMenu(
        fileName = "TerrainPatternSettings",
        menuName = "Mini Civilization/World/Terrain Pattern Settings")]
    public sealed class TerrainPatternSettings : ScriptableObject
    {
        [Tooltip("패턴맵 타일 한 변에 포함되는 청크 수.")]
        [SerializeField] private int patternTileChunkSpan;
        [Tooltip("기본 고도 분포를 만드는 노이즈 설정.")]
        [SerializeField] private TerrainNoiseRouterSettings noiseRouter;
        [Tooltip("지형 패턴 영역의 크기, 경계와 선택 가중치.")]
        [SerializeField] private TerrainRegionSettings region;
        [Tooltip("완만한 지형의 굴곡과 세부 요철 설정.")]
        [SerializeField] private TerrainSurfaceFormSettings smooth;
        [Tooltip("거친 지형의 굴곡과 세부 요철 설정.")]
        [SerializeField] private TerrainSurfaceFormSettings rugged;
        [Tooltip("산체·능선의 형태와 높이 설정.")]
        [SerializeField] private MountainFormSettings mountain;
        [Tooltip("협곡·골짜기의 형태와 깊이 설정.")]
        [SerializeField] private CanyonFormSettings canyon;

        public TerrainPatternSettingsData CreateData(int worldSeed) => new(
            worldSeed,
            patternTileChunkSpan,
            noiseRouter.CreateData(),
            region.CreateData(),
            smooth.CreateData(),
            rugged.CreateData(),
            mountain.CreateData(),
            canyon.CreateData());
    }

    public readonly struct TerrainNoiseFieldData
    {
        public TerrainNoiseFieldData(
            PatternNoiseMode mode,
            float scale,
            int layers,
            float frequencySpacing,
            float persistence,
            int octaveSeedStride)
        {
            if (!Enum.IsDefined(typeof(PatternNoiseMode), mode)
                || !float.IsFinite(scale)
                || scale <= 0f
                || layers <= 0
                || !float.IsFinite(frequencySpacing)
                || frequencySpacing < 1f
                || !float.IsFinite(persistence)
                || persistence <= 0f
                || persistence >= 1f
                || octaveSeedStride == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale));
            }

            Mode = mode;
            Scale = scale;
            Layers = layers;
            FrequencySpacing = frequencySpacing;
            Persistence = persistence;
            OctaveSeedStride = octaveSeedStride;
        }

        public PatternNoiseMode Mode { get; }
        public float Scale { get; }
        public int Layers { get; }
        public float FrequencySpacing { get; }
        public float Persistence { get; }
        public int OctaveSeedStride { get; }
    }

    public readonly struct TerrainCurveData
    {
        public TerrainCurveData(
            float atZero,
            float atQuarter,
            float atHalf,
            float atThreeQuarters,
            float atOne)
        {
            if (!float.IsFinite(atZero)
                || !float.IsFinite(atQuarter)
                || !float.IsFinite(atHalf)
                || !float.IsFinite(atThreeQuarters)
                || !float.IsFinite(atOne))
            {
                throw new ArgumentOutOfRangeException(nameof(atZero));
            }

            AtZero = atZero;
            AtQuarter = atQuarter;
            AtHalf = atHalf;
            AtThreeQuarters = atThreeQuarters;
            AtOne = atOne;
        }

        public float AtZero { get; }
        public float AtQuarter { get; }
        public float AtHalf { get; }
        public float AtThreeQuarters { get; }
        public float AtOne { get; }

        public float Evaluate(float input)
        {
            input = Math.Clamp(input, 0f, 1f);
            var scaled = input * 4f;
            var segment = Math.Min(3, (int)scaled);
            var amount = scaled - segment;
            amount = amount * amount * (3f - 2f * amount);
            return segment switch
            {
                0 => Lerp(AtZero, AtQuarter, amount),
                1 => Lerp(AtQuarter, AtHalf, amount),
                2 => Lerp(AtHalf, AtThreeQuarters, amount),
                _ => Lerp(AtThreeQuarters, AtOne, amount)
            };
        }

        private static float Lerp(float from, float to, float amount) =>
            from + (to - from) * amount;
    }

    public readonly struct TerrainRangeData
    {
        public TerrainRangeData(float minimum, float maximum)
        {
            if (!float.IsFinite(minimum)
                || !float.IsFinite(maximum)
                || maximum < minimum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            Minimum = minimum;
            Maximum = maximum;
        }

        public float Minimum { get; }
        public float Maximum { get; }
    }

    public readonly struct TerrainDomainWarpData
    {
        public TerrainDomainWarpData(
            TerrainNoiseFieldData field,
            float strengthCells)
        {
            if (!float.IsFinite(strengthCells) || strengthCells < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(strengthCells));
            }

            Field = field;
            StrengthCells = strengthCells;
        }

        public TerrainNoiseFieldData Field { get; }
        public float StrengthCells { get; }
    }

    public readonly struct TerrainNoiseRouterData
    {
        public TerrainNoiseRouterData(
            TerrainNoiseFieldData continentalness)
        {
            Continentalness = continentalness;
        }

        public TerrainNoiseFieldData Continentalness { get; }
    }

    public readonly struct TerrainRegionData
    {
        public TerrainRegionData(
            int sizeCells,
            float centerJitter,
            TerrainNoiseFieldData warpField,
            float warpStrengthCells,
            float boundaryBlendCells,
            float interiorReachRatio,
            float smoothShare,
            float ruggedShare,
            float mountainShare,
            float canyonShare)
        {
            if (sizeCells <= 0
                || !float.IsFinite(centerJitter)
                || centerJitter < 0f
                || !float.IsFinite(warpStrengthCells)
                || warpStrengthCells < 0f
                || !float.IsFinite(boundaryBlendCells)
                || boundaryBlendCells <= 0f
                || !float.IsFinite(interiorReachRatio)
                || interiorReachRatio <= 0f
                || !float.IsFinite(smoothShare)
                || !float.IsFinite(ruggedShare)
                || !float.IsFinite(mountainShare)
                || !float.IsFinite(canyonShare)
                || smoothShare < 0f
                || ruggedShare < 0f
                || mountainShare < 0f
                || canyonShare < 0f
                || smoothShare + ruggedShare + mountainShare
                    + canyonShare <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeCells));
            }

            SizeCells = sizeCells;
            CenterJitter = centerJitter;
            WarpField = warpField;
            WarpStrengthCells = warpStrengthCells;
            BoundaryBlendCells = boundaryBlendCells;
            InteriorReachRatio = interiorReachRatio;
            SmoothShare = smoothShare;
            RuggedShare = ruggedShare;
            MountainShare = mountainShare;
            CanyonShare = canyonShare;
        }

        public int SizeCells { get; }
        public float CenterJitter { get; }
        public TerrainNoiseFieldData WarpField { get; }
        public float WarpStrengthCells { get; }
        public float BoundaryBlendCells { get; }
        public float InteriorReachRatio { get; }
        public float SmoothShare { get; }
        public float RuggedShare { get; }
        public float MountainShare { get; }
        public float CanyonShare { get; }
        public float TotalShare => SmoothShare + RuggedShare
            + MountainShare + CanyonShare;
    }

    public readonly struct TerrainSurfaceFormData
    {
        public TerrainSurfaceFormData(
            TerrainDomainWarpData domainWarp,
            TerrainNoiseFieldData shapeField,
            TerrainCurveData shapeResponse,
            TerrainRangeData shapeAmplitude,
            TerrainNoiseFieldData detailField,
            TerrainRangeData detailAmplitude)
        {
            DomainWarp = domainWarp;
            ShapeField = shapeField;
            ShapeResponse = shapeResponse;
            ShapeAmplitude = shapeAmplitude;
            DetailField = detailField;
            DetailAmplitude = detailAmplitude;
        }

        public TerrainDomainWarpData DomainWarp { get; }
        public TerrainNoiseFieldData ShapeField { get; }
        public TerrainCurveData ShapeResponse { get; }
        public TerrainRangeData ShapeAmplitude { get; }
        public TerrainNoiseFieldData DetailField { get; }
        public TerrainRangeData DetailAmplitude { get; }
    }

    public readonly struct TerrainMountainFormData
    {
        public TerrainMountainFormData(
            TerrainDomainWarpData domainWarp,
            TerrainNoiseFieldData massField,
            TerrainCurveData massResponse,
            TerrainRangeData height,
            TerrainNoiseFieldData ridgeField,
            TerrainCurveData ridgeResponse,
            TerrainRangeData ridgeStrength,
            TerrainNoiseFieldData detailField,
            TerrainRangeData detailAmplitude)
        {
            DomainWarp = domainWarp;
            MassField = massField;
            MassResponse = massResponse;
            Height = height;
            RidgeField = ridgeField;
            RidgeResponse = ridgeResponse;
            RidgeStrength = ridgeStrength;
            DetailField = detailField;
            DetailAmplitude = detailAmplitude;
        }

        public TerrainDomainWarpData DomainWarp { get; }
        public TerrainNoiseFieldData MassField { get; }
        public TerrainCurveData MassResponse { get; }
        public TerrainRangeData Height { get; }
        public TerrainNoiseFieldData RidgeField { get; }
        public TerrainCurveData RidgeResponse { get; }
        public TerrainRangeData RidgeStrength { get; }
        public TerrainNoiseFieldData DetailField { get; }
        public TerrainRangeData DetailAmplitude { get; }
    }

    public readonly struct TerrainCanyonFormData
    {
        public TerrainCanyonFormData(
            TerrainDomainWarpData domainWarp,
            TerrainNoiseFieldData basinField,
            TerrainCurveData basinResponse,
            TerrainRangeData basinDepthRatio,
            TerrainNoiseFieldData valleyField,
            TerrainCurveData valleyResponse,
            TerrainRangeData valleyDepthRatio,
            TerrainRangeData depth,
            TerrainNoiseFieldData detailField,
            TerrainRangeData detailAmplitude)
        {
            DomainWarp = domainWarp;
            BasinField = basinField;
            BasinResponse = basinResponse;
            BasinDepthRatio = basinDepthRatio;
            ValleyField = valleyField;
            ValleyResponse = valleyResponse;
            ValleyDepthRatio = valleyDepthRatio;
            Depth = depth;
            DetailField = detailField;
            DetailAmplitude = detailAmplitude;
        }

        public TerrainDomainWarpData DomainWarp { get; }
        public TerrainNoiseFieldData BasinField { get; }
        public TerrainCurveData BasinResponse { get; }
        public TerrainRangeData BasinDepthRatio { get; }
        public TerrainNoiseFieldData ValleyField { get; }
        public TerrainCurveData ValleyResponse { get; }
        public TerrainRangeData ValleyDepthRatio { get; }
        public TerrainRangeData Depth { get; }
        public TerrainNoiseFieldData DetailField { get; }
        public TerrainRangeData DetailAmplitude { get; }
    }

    public sealed class TerrainPatternSettingsData
    {
        public TerrainPatternSettingsData(
            int worldSeed,
            int patternTileChunkSpan,
            TerrainNoiseRouterData noiseRouter,
            TerrainRegionData region,
            TerrainSurfaceFormData smooth,
            TerrainSurfaceFormData rugged,
            TerrainMountainFormData mountain,
            TerrainCanyonFormData canyon)
        {
            if (patternTileChunkSpan <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(patternTileChunkSpan));
            }

            WorldSeed = worldSeed;
            PatternTileChunkSpan = patternTileChunkSpan;
            NoiseRouter = noiseRouter;
            Region = region;
            Smooth = smooth;
            Rugged = rugged;
            Mountain = mountain;
            Canyon = canyon;
        }

        public int WorldSeed { get; }
        public int PatternTileChunkSpan { get; }
        public TerrainNoiseRouterData NoiseRouter { get; }
        public TerrainRegionData Region { get; }
        public TerrainSurfaceFormData Smooth { get; }
        public TerrainSurfaceFormData Rugged { get; }
        public TerrainMountainFormData Mountain { get; }
        public TerrainCanyonFormData Canyon { get; }
    }
}
