using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation
{
    [Serializable]
    public struct HydrologyMapSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("계획 Region 크기 Cell")]
        [SerializeField, Min(16)] private int planningRegionSizeCells;
        [InspectorName("경로 Sampling 간격 Cell")]
        [SerializeField, Range(1, 16)] private int routeSampleSpacingCells;
        [InspectorName("Basin Seed 간격 Cell")]
        [SerializeField, Min(4)] private int basinSeedSpacingCells;
        [InspectorName("Basin Potential Field")]
        [SerializeField] private WorldNoiseFieldSettings basinPotentialField;
        [InspectorName("Basin Potential 응답")]
        [SerializeField] private WorldCurveSettings basinPotentialResponse;
        [InspectorName("Basin Potential 이동 비용")]
        [SerializeField, Min(0f)] private float basinPotentialCost;
        [InspectorName("지형 변형 비용")]
        [SerializeField, Min(0f)] private float terrainDeformationCost;
        [InspectorName("경사 비용")]
        [SerializeField, Min(0f)] private float slopeCost;

        public static HydrologyMapSettings Default => new()
        {
            initialized = true,
            planningRegionSizeCells = 128,
            routeSampleSpacingCells = 4,
            basinSeedSpacingCells = 24,
            basinPotentialField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value, 0.018f, 4, 2f, 0.48f),
            basinPotentialResponse = WorldCurveSettings.Create(
                1f, 0.55f, 0.2f, 0.05f, 0f),
            basinPotentialCost = 3f,
            terrainDeformationCost = 0.7f,
            slopeCost = 1.2f
        };

        public HydrologyMapSettingsData CreateData() => new(
            planningRegionSizeCells,
            routeSampleSpacingCells,
            basinSeedSpacingCells,
            basinPotentialField.CreateData(),
            basinPotentialResponse.CreateData(),
            basinPotentialCost,
            terrainDeformationCost,
            slopeCost);

        public bool TryValidate(out string error)
        {
            if (!basinPotentialField.TryValidate(out error)
                || !basinPotentialResponse.TryValidate(out error)
                || planningRegionSizeCells < 16
                || routeSampleSpacingCells < 1
                || planningRegionSizeCells % routeSampleSpacingCells != 0
                || basinSeedSpacingCells < 4
                || !IsNonNegative(basinPotentialCost)
                || !IsNonNegative(terrainDeformationCost)
                || !IsNonNegative(slopeCost))
            {
                error = "Hydrology Map settings are invalid.";
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

            planningRegionSizeCells = Math.Max(16, planningRegionSizeCells);
            routeSampleSpacingCells = Math.Clamp(routeSampleSpacingCells, 1, 16);
            planningRegionSizeCells = checked(
                (planningRegionSizeCells + routeSampleSpacingCells - 1)
                / routeSampleSpacingCells * routeSampleSpacingCells);
            basinSeedSpacingCells = Math.Max(4, basinSeedSpacingCells);
            basinPotentialField.Normalize();
            basinPotentialResponse.Clamp(0f, 1f);
            basinPotentialCost = NormalizeNonNegative(basinPotentialCost);
            terrainDeformationCost = NormalizeNonNegative(terrainDeformationCost);
            slopeCost = NormalizeNonNegative(slopeCost);
        }

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;

        private static float NormalizeNonNegative(float value) =>
            float.IsFinite(value) ? Math.Max(0f, value) : 0f;
    }

    [Serializable]
    public struct RiverNetworkSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("자연 Head 밀도")]
        [SerializeField, Range(0f, 1f)] private float headDensity;
        [InspectorName("자연 End 밀도")]
        [SerializeField, Range(0f, 1f)] private float endDensity;
        [InspectorName("River 길이 Cell")]
        [SerializeField] private WorldSeededRangeSettings lengthCells;
        [InspectorName("Junction 확률")]
        [SerializeField, Range(0f, 1f)] private float junctionChance;
        [InspectorName("Lake Endpoint 가중치")]
        [SerializeField, Min(0f)] private float lakeEndpointWeight;
        [InspectorName("Pond Endpoint 가중치")]
        [SerializeField, Min(0f)] private float pondEndpointWeight;
        [InspectorName("Sea Endpoint 가중치")]
        [SerializeField, Min(0f)] private float seaEndpointWeight;
        [InspectorName("자연 Endpoint 가중치")]
        [SerializeField, Min(0f)] private float naturalEndpointWeight;
        [InspectorName("지형 변경 비용")]
        [SerializeField, Min(0f)] private float terrainChangeCost;
        [InspectorName("오르막 비용")]
        [SerializeField, Min(0f)] private float uphillCost;
        [InspectorName("횡단 경사 비용")]
        [SerializeField, Min(0f)] private float crossSlopeCost;
        [InspectorName("골짜기 선호")]
        [SerializeField, Min(0f)] private float valleyPreference;
        [InspectorName("경로 변형 Field")]
        [SerializeField] private WorldNoiseFieldSettings routeVariationField;
        [InspectorName("경로 변형 비용")]
        [SerializeField, Min(0f)] private float routeVariationCost;
        [InspectorName("자연 Head 전이 길이 Cell")]
        [SerializeField, Min(1)] private int naturalHeadTransitionCells;
        [InspectorName("자연 Head 전이")]
        [SerializeField] private WorldCurveSettings naturalHeadTransition;
        [InspectorName("자연 End 전이 길이 Cell")]
        [SerializeField, Min(1)] private int naturalEndTransitionCells;
        [InspectorName("자연 End 전이")]
        [SerializeField] private WorldCurveSettings naturalEndTransition;

        public static RiverNetworkSettings Default => new()
        {
            initialized = true,
            headDensity = 0.45f,
            endDensity = 0.4f,
            lengthCells = WorldSeededRangeSettings.Create(28f, 112f),
            junctionChance = 0.2f,
            lakeEndpointWeight = 1f,
            pondEndpointWeight = 0.65f,
            seaEndpointWeight = 1.4f,
            naturalEndpointWeight = 1f,
            terrainChangeCost = 0.8f,
            uphillCost = 1.8f,
            crossSlopeCost = 0.55f,
            valleyPreference = 0.7f,
            routeVariationField = WorldNoiseFieldSettings.Create(
                WorldNoiseMode.Value, 0.018f, 3, 2f, 0.45f),
            routeVariationCost = 0.45f,
            naturalHeadTransitionCells = 12,
            naturalHeadTransition = WorldCurveSettings.Create(0f, 0.05f, 0.5f, 0.95f, 1f),
            naturalEndTransitionCells = 16,
            naturalEndTransition = WorldCurveSettings.Create(0f, 0.05f, 0.5f, 0.95f, 1f)
        };

        public RiverNetworkSettingsData CreateData() => new(
            headDensity,
            endDensity,
            lengthCells.CreateData(),
            junctionChance,
            lakeEndpointWeight,
            pondEndpointWeight,
            seaEndpointWeight,
            naturalEndpointWeight,
            terrainChangeCost,
            uphillCost,
            crossSlopeCost,
            valleyPreference,
            routeVariationField.CreateData(),
            routeVariationCost,
            naturalHeadTransitionCells,
            naturalHeadTransition.CreateData(),
            naturalEndTransitionCells,
            naturalEndTransition.CreateData());

        public bool TryValidate(out string error)
        {
            if (!lengthCells.TryValidate(2f, 4096f, out error)
                || !routeVariationField.TryValidate(out error)
                || !naturalHeadTransition.TryValidate(out error)
                || !naturalEndTransition.TryValidate(out error)
                || !IsUnit(headDensity) || !IsUnit(endDensity)
                || !IsUnit(junctionChance)
                || !IsNonNegative(lakeEndpointWeight)
                || !IsNonNegative(pondEndpointWeight)
                || !IsNonNegative(seaEndpointWeight)
                || !IsNonNegative(naturalEndpointWeight)
                || lakeEndpointWeight + pondEndpointWeight
                    + seaEndpointWeight + naturalEndpointWeight <= 0f
                || !IsNonNegative(terrainChangeCost)
                || !IsNonNegative(uphillCost)
                || !IsNonNegative(crossSlopeCost)
                || !IsNonNegative(valleyPreference)
                || !IsNonNegative(routeVariationCost)
                || naturalHeadTransitionCells < 1
                || naturalEndTransitionCells < 1)
            {
                error = "River Network settings are invalid.";
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

            headDensity = Math.Clamp(headDensity, 0f, 1f);
            endDensity = Math.Clamp(endDensity, 0f, 1f);
            lengthCells.Normalize(2f, 4096f);
            junctionChance = Math.Clamp(junctionChance, 0f, 1f);
            lakeEndpointWeight = NormalizeNonNegative(lakeEndpointWeight);
            pondEndpointWeight = NormalizeNonNegative(pondEndpointWeight);
            seaEndpointWeight = NormalizeNonNegative(seaEndpointWeight);
            naturalEndpointWeight = NormalizeNonNegative(naturalEndpointWeight);
            if (lakeEndpointWeight + pondEndpointWeight
                + seaEndpointWeight + naturalEndpointWeight <= 0f)
            {
                naturalEndpointWeight = 1f;
            }
            terrainChangeCost = NormalizeNonNegative(terrainChangeCost);
            uphillCost = NormalizeNonNegative(uphillCost);
            crossSlopeCost = NormalizeNonNegative(crossSlopeCost);
            valleyPreference = NormalizeNonNegative(valleyPreference);
            routeVariationField.Normalize();
            routeVariationCost = NormalizeNonNegative(routeVariationCost);
            naturalHeadTransitionCells = Math.Max(1, naturalHeadTransitionCells);
            naturalHeadTransition.Clamp(0f, 1f);
            naturalEndTransitionCells = Math.Max(1, naturalEndTransitionCells);
            naturalEndTransition.Clamp(0f, 1f);
        }

        private static bool IsUnit(float value) =>
            float.IsFinite(value) && value >= 0f && value <= 1f;
        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;
        private static float NormalizeNonNegative(float value) =>
            float.IsFinite(value) ? Math.Max(0f, value) : 0f;
    }

    [Serializable]
    public struct RiverGraphSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("연결 탐색 반경 Chunk")]
        [SerializeField] private WorldSeededRangeSettings connectionRadiusChunks;
        [InspectorName("대칭 고도 변화 비용")]
        [SerializeField, Min(0f)] private float elevationChangeCost;
        [InspectorName("Natural 전이 길이 Cell")]
        [SerializeField, Min(1)] private int naturalTransitionCells;
        [InspectorName("Natural 전이 속도")]
        [SerializeField] private WorldCurveSettings naturalTransitionRate;
        [InspectorName("Junction 거리 확률")]
        [SerializeField] private WorldCurveSettings proximityChance;
        [InspectorName("Junction 방향 유사도 확률")]
        [SerializeField] private WorldCurveSettings alignmentChance;

        public static RiverGraphSettings Default => new()
        {
            initialized = true,
            connectionRadiusChunks = WorldSeededRangeSettings.Create(5f, 10f),
            elevationChangeCost = 0.8f,
            naturalTransitionCells = 16,
            naturalTransitionRate = WorldCurveSettings.Create(
                0f, 1f, 2f, 2f, 1f),
            proximityChance = WorldCurveSettings.Create(
                1f, 0.9f, 0.45f, 0.1f, 0f),
            alignmentChance = WorldCurveSettings.Create(
                0f, 0.05f, 0.25f, 0.75f, 1f)
        };

        public RiverGraphSettingsData CreateData(int chunkCellCountXZ) => new(
            connectionRadiusChunks.CreateData(chunkCellCountXZ),
            elevationChangeCost,
            naturalTransitionCells,
            naturalTransitionRate.CreateData(),
            proximityChance.CreateData(),
            alignmentChance.CreateData());

        public bool TryValidate(out string error)
        {
            var rate = naturalTransitionRate.CreateData();
            if (!connectionRadiusChunks.TryValidate(1f, 256f, out error)
                || !IsNonNegative(elevationChangeCost)
                || naturalTransitionCells < 1
                || !naturalTransitionRate.TryValidate(out error)
                || !proximityChance.TryValidate(out error)
                || !alignmentChance.TryValidate(out error)
                || !IsNonNegative(rate.AtZero)
                || !IsNonNegative(rate.AtQuarter)
                || !IsNonNegative(rate.AtHalf)
                || !IsNonNegative(rate.AtThreeQuarters)
                || !IsNonNegative(rate.AtOne)
                || rate.AtZero + rate.AtQuarter + rate.AtHalf
                    + rate.AtThreeQuarters + rate.AtOne <= 0f
                || !IsUnitCurve(proximityChance.CreateData(), descending: true)
                || !IsUnitCurve(alignmentChance.CreateData(), descending: false))
            {
                error = "River Graph settings are invalid.";
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

            connectionRadiusChunks.Normalize(1f, 256f);
            elevationChangeCost = NormalizeNonNegative(elevationChangeCost);
            naturalTransitionCells = Math.Max(1, naturalTransitionCells);
            NormalizeNonNegativeCurve(ref naturalTransitionRate);
            NormalizeUnitCurve(ref proximityChance, descending: true);
            NormalizeUnitCurve(ref alignmentChance, descending: false);
        }

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;

        private static float NormalizeNonNegative(float value) =>
            float.IsFinite(value) ? Math.Max(0f, value) : 0f;

        private static bool IsUnitCurve(
            in WorldCurveSettingsData curve,
            bool descending)
        {
            var zero = curve.AtZero;
            var quarter = curve.AtQuarter;
            var half = curve.AtHalf;
            var threeQuarters = curve.AtThreeQuarters;
            var one = curve.AtOne;
            return IsUnit(zero) && IsUnit(quarter) && IsUnit(half)
                && IsUnit(threeQuarters) && IsUnit(one)
                && (descending
                    ? zero >= quarter && quarter >= half
                        && half >= threeQuarters && threeQuarters >= one
                    : zero <= quarter && quarter <= half
                        && half <= threeQuarters && threeQuarters <= one);
        }

        private static bool IsUnit(float value) =>
            float.IsFinite(value) && value >= 0f && value <= 1f;

        private static void NormalizeNonNegativeCurve(
            ref WorldCurveSettings curve)
        {
            var data = curve.CreateData();
            curve = WorldCurveSettings.Create(
                NormalizeNonNegative(data.AtZero),
                NormalizeNonNegative(data.AtQuarter),
                NormalizeNonNegative(data.AtHalf),
                NormalizeNonNegative(data.AtThreeQuarters),
                NormalizeNonNegative(data.AtOne));
        }

        private static void NormalizeUnitCurve(
            ref WorldCurveSettings curve,
            bool descending)
        {
            var data = curve.CreateData();
            var zero = Math.Clamp(data.AtZero, 0f, 1f);
            var quarter = Math.Clamp(data.AtQuarter, 0f, 1f);
            var half = Math.Clamp(data.AtHalf, 0f, 1f);
            var threeQuarters = Math.Clamp(data.AtThreeQuarters, 0f, 1f);
            var one = Math.Clamp(data.AtOne, 0f, 1f);
            if (descending)
            {
                quarter = Math.Min(quarter, zero);
                half = Math.Min(half, quarter);
                threeQuarters = Math.Min(threeQuarters, half);
                one = Math.Min(one, threeQuarters);
            }
            else
            {
                quarter = Math.Max(quarter, zero);
                half = Math.Max(half, quarter);
                threeQuarters = Math.Max(threeQuarters, half);
                one = Math.Max(one, threeQuarters);
            }

            curve = WorldCurveSettings.Create(
                zero, quarter, half, threeQuarters, one);
        }
    }

    [Serializable]
    public struct RiverCorridorSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("Corridor 노출 비용")]
        [SerializeField, Min(0f)] private float corridorExposureCost;
        [InspectorName("강둑 검사 여유폭 Cell")]
        [SerializeField, Min(0f)] private float bankMarginCells;
        [InspectorName("경로 곡선화 반복")]
        [SerializeField, Range(0, 4)] private int smoothingIterations;
        [InspectorName("강폭 Field")]
        [SerializeField] private WorldNoiseFieldSettings widthField;
        [InspectorName("강폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings widthCells;
        [InspectorName("단면 진행도")]
        [SerializeField] private WorldCurveSettings crossSection;
        [InspectorName("깊이 Cell")]
        [SerializeField] private WorldSeededRangeSettings depthCells;
        [InspectorName("수면 하강 Cell")]
        [SerializeField] private WorldSeededRangeSettings waterInsetCells;
        [InspectorName("낙차 전이 길이 Cell")]
        [SerializeField, Min(1)] private int dropTransitionCells;
        [InspectorName("낙차 전이 진행도")]
        [SerializeField] private WorldCurveSettings dropTransition;
        [InspectorName("강바닥 Field")]
        [SerializeField] private WorldNoiseFieldSettings riverbedField;
        [InspectorName("강바닥 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings riverbedAmplitudeCells;

        public static RiverCorridorSettings Default => new()
        {
            initialized = true,
            corridorExposureCost = 4f,
            bankMarginCells = 1f,
            smoothingIterations = 2,
            widthField = WorldNoiseFieldSettings.Create(WorldNoiseMode.Value, 0.025f, 3, 2f, 0.45f),
            widthCells = WorldSeededRangeSettings.Create(1f, 7f),
            crossSection = WorldCurveSettings.Create(0f, 0.05f, 0.5f, 0.95f, 1f),
            depthCells = WorldSeededRangeSettings.Create(1.5f, 4f),
            waterInsetCells = WorldSeededRangeSettings.Create(0.15f, 0.65f),
            dropTransitionCells = 12,
            dropTransition = WorldCurveSettings.Create(0f, 0.05f, 0.5f, 0.95f, 1f),
            riverbedField = WorldNoiseFieldSettings.Create(WorldNoiseMode.Signed, 0.08f, 3, 2f, 0.4f),
            riverbedAmplitudeCells = WorldSeededRangeSettings.Create(0.05f, 0.3f)
        };

        public RiverCorridorSettingsData CreateData() => new(
            corridorExposureCost, bankMarginCells, smoothingIterations,
            widthField.CreateData(), widthCells.CreateData(),
            crossSection.CreateData(),
            depthCells.CreateData(WorldGrid.HeightStepsPerCell),
            waterInsetCells.CreateData(WorldGrid.HeightStepsPerCell),
            dropTransitionCells, dropTransition.CreateData(),
            riverbedField.CreateData(),
            riverbedAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));

        public bool TryValidate(out string error)
        {
            if (!widthField.TryValidate(out error)
                || !crossSection.TryValidate(out error)
                || !widthCells.TryValidate(1f, 64f, out error)
                || !depthCells.TryValidate(0.2f, 32f, out error)
                || !waterInsetCells.TryValidate(0f, 8f, out error)
                || !dropTransition.TryValidate(out error)
                || !riverbedField.TryValidate(out error)
                || !riverbedAmplitudeCells.TryValidate(0f, 4f, out error)
                || !IsNonNegative(corridorExposureCost)
                || !IsNonNegative(bankMarginCells)
                || smoothingIterations < 0 || smoothingIterations > 4
                || dropTransitionCells < 1
                || !crossSection.IsInside(0f, 1f)
                || !crossSection.IsNonDecreasing()
                || !dropTransition.IsInside(0f, 1f)
                || !dropTransition.IsNonDecreasing())
            {
                error = "River Corridor settings are invalid.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            if (!initialized) { this = Default; return; }
            corridorExposureCost = NormalizeNonNegative(corridorExposureCost);
            bankMarginCells = NormalizeNonNegative(bankMarginCells);
            smoothingIterations = Math.Clamp(smoothingIterations, 0, 4);
            widthField.Normalize();
            widthCells.Normalize(1f, 64f);
            crossSection.Clamp(0f, 1f);
            depthCells.Normalize(0.2f, 32f);
            waterInsetCells.Normalize(0f, 8f);
            dropTransitionCells = Math.Clamp(dropTransitionCells, 1, 128);
            dropTransition.Clamp(0f, 1f);
            riverbedField.Normalize();
            riverbedAmplitudeCells.Normalize(0f, 4f);
        }

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;
        private static float NormalizeNonNegative(float value) =>
            float.IsFinite(value) ? Math.Max(0f, value) : 0f;
    }

    [Serializable]
    public struct BasinProfileSettings
    {
        [InspectorName("발생률")]
        [SerializeField, Range(0f, 1f)] private float occurrence;
        [InspectorName("면적 Cell")]
        [SerializeField] private WorldSeededRangeSettings areaCells;
        [InspectorName("최대 깊이 Cell")]
        [SerializeField] private WorldSeededRangeSettings maximumDepthCells;
        [InspectorName("River 연결 확률")]
        [SerializeField, Range(0f, 1f)] private float riverConnectionChance;
        [InspectorName("연결 시 Head 확률")]
        [SerializeField, Range(0f, 1f)] private float headRoleChance;

        public static BasinProfileSettings Create(
            float occurrence,
            float minimumArea,
            float maximumArea,
            float minimumDepthCells,
            float maximumDepthCells,
            float riverConnectionChance,
            float headRoleChance) => new()
        {
            occurrence = occurrence,
            areaCells = WorldSeededRangeSettings.Create(minimumArea, maximumArea),
            maximumDepthCells = WorldSeededRangeSettings.Create(minimumDepthCells, maximumDepthCells),
            riverConnectionChance = riverConnectionChance,
            headRoleChance = headRoleChance
        };

        public BasinProfileSettingsData CreateData() => new(
            occurrence,
            areaCells.CreateData(),
            maximumDepthCells.CreateData(WorldGrid.HeightStepsPerCell),
            riverConnectionChance,
            headRoleChance);

        public bool TryValidate(out string error) =>
            areaCells.TryValidate(1f, 65536f, out error)
            && maximumDepthCells.TryValidate(0.2f, 128f, out error)
            && IsUnit(occurrence)
            && IsUnit(riverConnectionChance)
            && IsUnit(headRoleChance);

        public void Normalize()
        {
            occurrence = Math.Clamp(occurrence, 0f, 1f);
            areaCells.Normalize(1f, 65536f);
            maximumDepthCells.Normalize(0.2f, 128f);
            riverConnectionChance = Math.Clamp(riverConnectionChance, 0f, 1f);
            headRoleChance = Math.Clamp(headRoleChance, 0f, 1f);
        }

        public float MaximumAreaCells => areaCells.CreateData().Maximum;

        private static bool IsUnit(float value) =>
            float.IsFinite(value) && value >= 0f && value <= 1f;
    }

    [Serializable]
    public struct BasinPatternSettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("Basin 최소 간격 Cell")]
        [SerializeField, Min(0)] private int minimumSeparationCells;
        [InspectorName("Basin 최대 영향 반경 Cell")]
        [SerializeField, Min(1)] private int maximumReachCells;
        [InspectorName("절삭 비용")]
        [SerializeField, Min(0f)] private float cutCost;
        [InspectorName("성토 비용")]
        [SerializeField, Min(0f)] private float fillCost;
        [InspectorName("가장자리 비용")]
        [SerializeField, Min(0f)] private float rimCost;
        [InspectorName("해안 전이 Cell")]
        [SerializeField, Min(1)] private int shoreTransitionCells;
        [InspectorName("해안 전이")]
        [SerializeField] private WorldCurveSettings shoreTransition;
        [InspectorName("내부 깊이 진행")]
        [SerializeField] private WorldCurveSettings depthByInterior;
        [InspectorName("바닥 세부 Field")]
        [SerializeField] private WorldNoiseFieldSettings bedField;
        [InspectorName("바닥 세부 진폭 Cell")]
        [SerializeField] private WorldSeededRangeSettings bedAmplitudeCells;
        [InspectorName("Lake")]
        [SerializeField] private BasinProfileSettings lake;
        [InspectorName("Pond")]
        [SerializeField] private BasinProfileSettings pond;

        public static BasinPatternSettings Default => new()
        {
            initialized = true,
            minimumSeparationCells = 6,
            maximumReachCells = 48,
            cutCost = 1f,
            fillCost = 1.35f,
            rimCost = 0.8f,
            shoreTransitionCells = 5,
            shoreTransition = WorldCurveSettings.Create(0f, 0.08f, 0.5f, 0.92f, 1f),
            depthByInterior = WorldCurveSettings.Create(0f, 0.12f, 0.55f, 0.9f, 1f),
            bedField = WorldNoiseFieldSettings.Create(WorldNoiseMode.Signed, 0.055f, 3, 2f, 0.42f),
            bedAmplitudeCells = WorldSeededRangeSettings.Create(0.05f, 0.35f),
            lake = BasinProfileSettings.Create(0.34f, 48f, 420f, 2f, 10f, 0.7f, 0.3f),
            pond = BasinProfileSettings.Create(0.38f, 8f, 72f, 0.6f, 3f, 0.35f, 0.55f)
        };

        public BasinPatternSettingsData CreateData() => new(
            minimumSeparationCells, maximumReachCells,
            cutCost, fillCost, rimCost,
            shoreTransitionCells, shoreTransition.CreateData(),
            depthByInterior.CreateData(), bedField.CreateData(),
            bedAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell),
            lake.CreateData(), pond.CreateData());

        public bool TryValidate(out string error)
        {
            if (!shoreTransition.TryValidate(out error)
                || !depthByInterior.TryValidate(out error)
                || !bedField.TryValidate(out error)
                || !bedAmplitudeCells.TryValidate(0f, 8f, out error)
                || !lake.TryValidate(out error)
                || !pond.TryValidate(out error)
                || minimumSeparationCells < 0
                || maximumReachCells < 1
                || lake.MaximumAreaCells > MaximumFootprintCells(maximumReachCells)
                || pond.MaximumAreaCells > MaximumFootprintCells(maximumReachCells)
                || !IsNonNegative(cutCost)
                || !IsNonNegative(fillCost)
                || !IsNonNegative(rimCost)
                || shoreTransitionCells < 1
                || !shoreTransition.IsInside(0f, 1f)
                || !shoreTransition.IsNonDecreasing()
                || !depthByInterior.IsInside(0f, 1f)
                || !depthByInterior.IsNonDecreasing())
            {
                error = "Basin Pattern settings are invalid.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public void Normalize()
        {
            if (!initialized) { this = Default; return; }
            minimumSeparationCells = Math.Max(0, minimumSeparationCells);
            maximumReachCells = Math.Max(1, maximumReachCells);
            cutCost = NormalizeNonNegative(cutCost);
            fillCost = NormalizeNonNegative(fillCost);
            rimCost = NormalizeNonNegative(rimCost);
            shoreTransitionCells = Math.Max(1, shoreTransitionCells);
            shoreTransition.Clamp(0f, 1f);
            depthByInterior.Clamp(0f, 1f);
            bedField.Normalize();
            bedAmplitudeCells.Normalize(0f, 8f);
            lake.Normalize();
            pond.Normalize();
        }

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;
        private static long MaximumFootprintCells(int reach)
        {
            var diameter = checked((long)reach * 2 + 1);
            return checked(diameter * diameter);
        }
        private static float NormalizeNonNegative(float value) =>
            float.IsFinite(value) ? Math.Max(0f, value) : 0f;
    }

    [Serializable]
    public struct HydrologySettings
    {
        [SerializeField, HideInInspector] private bool initialized;
        [InspectorName("공통 Map")]
        [SerializeField] private HydrologyMapSettings map;
        [InspectorName("River Graph")]
        [SerializeField] private RiverNetworkSettings riverNetwork;
        [InspectorName("River Graph (재설계)")]
        [SerializeField] private RiverGraphSettings riverGraph;
        [InspectorName("River Corridor")]
        [SerializeField] private RiverCorridorSettings riverCorridor;
        [InspectorName("Lake / Pond Basin")]
        [SerializeField] private BasinPatternSettings basins;

        public static HydrologySettings Default => new()
        {
            initialized = true,
            map = HydrologyMapSettings.Default,
            riverNetwork = RiverNetworkSettings.Default,
            riverGraph = RiverGraphSettings.Default,
            riverCorridor = RiverCorridorSettings.Default,
            basins = BasinPatternSettings.Default
        };

        public HydrologySettingsData CreateData(int chunkCellCountXZ) => new(
            map.CreateData(), riverNetwork.CreateData(),
            riverGraph.CreateData(chunkCellCountXZ),
            riverCorridor.CreateData(), basins.CreateData());

        public bool TryValidate(out string error) =>
            map.TryValidate(out error)
            && riverNetwork.TryValidate(out error)
            && riverGraph.TryValidate(out error)
            && riverCorridor.TryValidate(out error)
            && basins.TryValidate(out error);

        public void Normalize()
        {
            if (!initialized) { this = Default; return; }
            map.Normalize();
            riverNetwork.Normalize();
            riverGraph.Normalize();
            riverCorridor.Normalize();
            basins.Normalize();
        }
    }
}
