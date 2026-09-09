using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation.Patterns
{
    [Serializable]
    public struct BasinFeatureSettings
    {
        [Tooltip("생성 후보 지점 사이의 간격으로, 클수록 후보가 줄어듭니다(셀).")]
        [SerializeField] private int candidateLatticeSpacingCells;
        [Tooltip("각 후보 지점에서 생성을 시도할 기본 확률(0~1).")]
        [SerializeField] private float occurrence;
        [Tooltip("호수·연못의 목표 면적 범위(셀 수).")]
        [SerializeField] private PatternRange areaCells;
        [Tooltip("연못으로 분류할 최대 면적으로, World의 Pond Maximum Area와 같아야 합니다.")]
        [SerializeField] private int pondMaximumAreaCells;
        [Tooltip("최대 수심 범위(셀).")]
        [SerializeField] private PatternRange maximumDepthCells;
        [Tooltip("호수·연못이 확장될 위치별 비용을 만드는 노이즈.")]
        [SerializeField] private PatternNoiseField potentialField;
        [Tooltip("노이즈 값을 호수·연못의 확장 비용으로 변환하는 곡선.")]
        [SerializeField] private PatternCurve potentialResponse;
        [Tooltip("물가와 주변 지형을 연결하는 전이 폭(셀).")]
        [SerializeField] private int shoreTransitionCells;
        [Tooltip("물가 전이 구간의 지형 혼합 곡선.")]
        [SerializeField] private PatternCurve shoreTransition;
        [Tooltip("물가에서 내부로 들어갈수록 깊어지는 정도를 정하는 곡선.")]
        [SerializeField] private PatternCurve depthByInterior;
        [Tooltip("호수·연못 바닥의 요철 노이즈.")]
        [SerializeField] private PatternNoiseField bedField;
        [Tooltip("호수·연못 바닥 요철의 높이 변화 범위(셀).")]
        [SerializeField] private PatternRange bedAmplitudeCells;
        [Tooltip("생성 기준점에서 호수·연못이 확장할 수 있는 최대 거리(셀).")]
        [SerializeField] private int maximumReachCells;
        [Tooltip("노이즈 기반 확장 비용의 가중치.")]
        [SerializeField] private float potentialCost;
        [Tooltip("인접 지형의 높이 차이에 부과하는 확장 비용 가중치.")]
        [SerializeField] private float terrainDeformationCost;
        [Tooltip("경사가 큰 방향으로 확장하기 어렵게 하는 비용 가중치.")]
        [SerializeField] private float slopeCost;
        [Tooltip("수면 높이 결정 시 지형을 깎는 비용의 가중치.")]
        [SerializeField] private float cutCost;
        [Tooltip("수면 높이 결정 시 지형을 메우는 비용의 가중치.")]
        [SerializeField] private float fillCost;
        [Tooltip("수면 높이 결정 시 가장자리 지형과의 높이 차이에 부과하는 비용 가중치.")]
        [SerializeField] private float rimCost;

        internal BasinFeatureSettingsData CreateData() => new(
            candidateLatticeSpacingCells,
            occurrence,
            areaCells.CreateData(),
            pondMaximumAreaCells,
            maximumDepthCells.CreateData(WorldGrid.HeightStepsPerCell),
            potentialField.CreateData(),
            potentialResponse.CreateData(),
            shoreTransitionCells,
            shoreTransition.CreateData(),
            depthByInterior.CreateData(),
            bedField.CreateData(),
            bedAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell),
            maximumReachCells,
            potentialCost,
            terrainDeformationCost,
            slopeCost,
            cutCost,
            fillCost,
            rimCost);
    }

    [Serializable]
    public struct SeaFeatureSettings
    {
        [Tooltip("해수면의 기준 높이(셀).")]
        [SerializeField] private int surfaceCell;
        [Tooltip("해수면 기준 셀에 더하는 세부 높이(높이 단계).")]
        [SerializeField] private int surfaceStep;

        internal SeaFeatureSettingsData CreateData() => new(
            checked(surfaceCell * WorldGrid.HeightStepsPerCell + surfaceStep));
    }

    [Serializable]
    public struct RiverDistribution
    {
        [Tooltip("선택 가능한 최솟값.")]
        [SerializeField] private float minimum;
        [Tooltip("분포의 중심이 되는 기준값.")]
        [SerializeField] private float average;
        [Tooltip("선택 가능한 최댓값.")]
        [SerializeField] private float maximum;

        internal RiverDistributionData CreateData() => new(
            minimum,
            average,
            maximum);
    }

    [Serializable]
    public struct RiverFeatureSettings
    {
        [Tooltip("생성 후보 지점 사이의 간격으로, 클수록 후보가 줄어듭니다(셀).")]
        [SerializeField] private int candidateLatticeSpacingCells;
        [Tooltip("강 생성 기준점의 무작위 위치 이동 범위(셀).")]
        [SerializeField] private int anchorJitterCells;
        [Tooltip("각 후보 지점에서 생성을 시도할 기본 확률(0~1).")]
        [SerializeField] private float occurrence;
        [Tooltip("강줄기를 구성하는 최소 노드 수.")]
        [SerializeField] private int minimumNodeCount;
        [Tooltip("강줄기 노드 수 분포의 중심값.")]
        [SerializeField] private int averageNodeCount;
        [Tooltip("강줄기를 구성하는 최대 노드 수.")]
        [SerializeField] private int maximumNodeCount;
        [Tooltip("강줄기의 노드별 회전 각도 범위(도).")]
        [SerializeField] private PatternRange nodeTurnDegrees;
        [Tooltip("강줄기의 굽이치는 형태를 정하는 노이즈.")]
        [SerializeField] private PatternNoiseField curvatureField;
        [Tooltip("강 경로의 높이 차이를 회피 비용으로 환산하는 기준 높이(셀).")]
        [SerializeField] private float terrainHeightChangeReferenceCells;
        [Tooltip("강 경로가 큰 지형 높이 차이를 피하는 강도.")]
        [SerializeField] private float terrainAvoidanceStrength;
        [Tooltip("본류에서 파생되는 전체 지류의 최대 개수.")]
        [SerializeField] private int maximumDescendantBranchCount;
        [Tooltip("각 노드에서 지류가 발생할 확률(0~1).")]
        [SerializeField] private float branchOccurrencePerNode;
        [Tooltip("부모 강줄기 노드 수에 대한 지류 노드 수의 비율.")]
        [SerializeField] private RiverDistribution branchNodeCountRatio;
        [Tooltip("지류를 생성하기 위해 필요한 최소 노드 수.")]
        [SerializeField] private int minimumBranchNodeCount;
        [Tooltip("지류가 부모 강줄기에서 갈라지는 각도(도).")]
        [SerializeField] private RiverDistribution branchOpeningAngleDegrees;
        [Tooltip("부모 강줄기 너비에 대한 지류 너비 비율.")]
        [SerializeField] private RiverDistribution branchWidthRatio;
        [Tooltip("부모 강줄기 깊이에 대한 지류 깊이 비율.")]
        [SerializeField] private RiverDistribution branchDepthRatio;
        [Tooltip("강 너비와 수면 위치의 변화를 만드는 노이즈.")]
        [SerializeField] private PatternNoiseField widthField;
        [Tooltip("강 너비 범위(셀).")]
        [SerializeField] private PatternRange widthCells;
        [Tooltip("강 중심과 가장자리 사이의 단면 형태를 정하는 곡선.")]
        [SerializeField] private PatternCurve crossSection;
        [Tooltip("깎아내리는 깊이 범위(셀).")]
        [SerializeField] private PatternRange depthCells;
        [Tooltip("기존 지표면에서 강 수면을 낮추는 깊이 범위(셀).")]
        [SerializeField] private PatternRange waterInsetCells;
        [Tooltip("강바닥 요철 노이즈.")]
        [SerializeField] private PatternNoiseField riverbedField;
        [Tooltip("강바닥 요철의 높이 변화 범위(셀).")]
        [SerializeField] private PatternRange riverbedAmplitudeCells;

        internal RiverFeatureSettingsData CreateData() => new(
            candidateLatticeSpacingCells,
            anchorJitterCells,
            occurrence,
            minimumNodeCount,
            averageNodeCount,
            maximumNodeCount,
            nodeTurnDegrees.CreateData(),
            curvatureField.CreateData(),
            terrainHeightChangeReferenceCells,
            terrainAvoidanceStrength,
            maximumDescendantBranchCount,
            branchOccurrencePerNode,
            branchNodeCountRatio.CreateData(),
            minimumBranchNodeCount,
            branchOpeningAngleDegrees.CreateData(),
            branchWidthRatio.CreateData(),
            branchDepthRatio.CreateData(),
            widthField.CreateData(),
            widthCells.CreateData(),
            crossSection.CreateData(),
            depthCells.CreateData(WorldGrid.HeightStepsPerCell),
            waterInsetCells.CreateData(WorldGrid.HeightStepsPerCell),
            riverbedField.CreateData(),
            riverbedAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));
    }

    [CreateAssetMenu(
        fileName = "HydrologyFeatureSettings",
        menuName = "Mini Civilization/World/Hydrology Feature Settings")]
    public sealed class HydrologyFeatureSettings : ScriptableObject
    {
        [Tooltip("해수면 설정으로, 현재 해양 범위와 해저 높이는 Elevation에서 결정합니다.")]
        [SerializeField] private SeaFeatureSettings sea;
        [Tooltip("호수·연못의 생성 빈도, 면적, 깊이와 물가 설정.")]
        [SerializeField] private BasinFeatureSettings basins;
        [Tooltip("강의 생성 빈도, 경로, 지류와 단면 설정.")]
        [SerializeField] private RiverFeatureSettings river;

        public HydrologyFeatureSettingsData CreateData(
            WorldSettingsData world) => new(
            world ?? throw new ArgumentNullException(nameof(world)),
            sea.CreateData(),
            basins.CreateData(),
            river.CreateData());
    }

    public readonly struct BasinFeatureSettingsData
    {
        public BasinFeatureSettingsData(
            int candidateLatticeSpacingCells,
            float occurrence,
            TerrainRangeData area,
            int pondMaximumAreaCells,
            TerrainRangeData maximumDepth,
            TerrainNoiseFieldData potentialField,
            TerrainCurveData potentialResponse,
            int shoreTransitionCells,
            TerrainCurveData shoreTransition,
            TerrainCurveData depthByInterior,
            TerrainNoiseFieldData bedField,
            TerrainRangeData bedAmplitude,
            int maximumReachCells,
            float potentialCost,
            float terrainDeformationCost,
            float slopeCost,
            float cutCost,
            float fillCost,
            float rimCost)
        {
            if (candidateLatticeSpacingCells <= 0
                || !float.IsFinite(occurrence)
                || occurrence < 0f
                || occurrence > 1f
                || area.Minimum <= 0f
                || pondMaximumAreaCells <= 0
                || maximumDepth.Minimum - bedAmplitude.Maximum < 1f
                || !IsUnitTransition(depthByInterior)
                || !IsUnitTransition(shoreTransition)
                || shoreTransitionCells <= 0
                || bedAmplitude.Maximum >= maximumDepth.Minimum
                || maximumReachCells < 1
                || !float.IsFinite(potentialCost) || potentialCost < 0f
                || !float.IsFinite(terrainDeformationCost) || terrainDeformationCost < 0f
                || !float.IsFinite(slopeCost) || slopeCost < 0f
                || !float.IsFinite(cutCost) || cutCost < 0f
                || !float.IsFinite(fillCost) || fillCost < 0f
                || !float.IsFinite(rimCost) || rimCost < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(candidateLatticeSpacingCells));
            }

            CandidateLatticeSpacingCells = candidateLatticeSpacingCells;
            Occurrence = occurrence;
            Area = area;
            PondMaximumAreaCells = pondMaximumAreaCells;
            MaximumDepth = maximumDepth;
            PotentialField = potentialField;
            PotentialResponse = potentialResponse;
            ShoreTransitionCells = shoreTransitionCells;
            ShoreTransition = shoreTransition;
            DepthByInterior = depthByInterior;
            BedField = bedField;
            BedAmplitude = bedAmplitude;
            MaximumReachCells = maximumReachCells;
            PotentialCost = potentialCost;
            TerrainDeformationCost = terrainDeformationCost;
            SlopeCost = slopeCost;
            CutCost = cutCost;
            FillCost = fillCost;
            RimCost = rimCost;
        }

        public int CandidateLatticeSpacingCells { get; }
        public float Occurrence { get; }
        public TerrainRangeData Area { get; }
        public int PondMaximumAreaCells { get; }
        public TerrainRangeData MaximumDepth { get; }
        public TerrainNoiseFieldData PotentialField { get; }
        public TerrainCurveData PotentialResponse { get; }
        public int ShoreTransitionCells { get; }
        public TerrainCurveData ShoreTransition { get; }
        public TerrainCurveData DepthByInterior { get; }
        public TerrainNoiseFieldData BedField { get; }
        public TerrainRangeData BedAmplitude { get; }
        public int MaximumReachCells { get; }
        public float PotentialCost { get; }
        public float TerrainDeformationCost { get; }
        public float SlopeCost { get; }
        public float CutCost { get; }
        public float FillCost { get; }
        public float RimCost { get; }

        internal static bool IsUnitTransition(TerrainCurveData curve) =>
            curve.AtZero == 0f && curve.AtOne == 1f
            && curve.AtQuarter >= 0f && curve.AtQuarter <= 1f
            && curve.AtHalf >= 0f && curve.AtHalf <= 1f
            && curve.AtThreeQuarters >= 0f && curve.AtThreeQuarters <= 1f;
    }

    public readonly struct SeaFeatureSettingsData
    {
        public SeaFeatureSettingsData(int surfaceHeight)
        {
            if (surfaceHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(surfaceHeight));
            SurfaceHeight = surfaceHeight;
        }

        public int SurfaceHeight { get; }
    }

    public readonly struct RiverDistributionData
    {
        public RiverDistributionData(float minimum, float average, float maximum)
        {
            if (!float.IsFinite(minimum)
                || !float.IsFinite(average)
                || !float.IsFinite(maximum)
                || minimum > average
                || average > maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(minimum));
            }

            Minimum = minimum;
            Average = average;
            Maximum = maximum;
        }

        public float Minimum { get; }
        public float Average { get; }
        public float Maximum { get; }
    }

    public readonly struct RiverFeatureSettingsData
    {
        public RiverFeatureSettingsData(
            int candidateLatticeSpacingCells,
            int anchorJitterCells,
            float occurrence,
            int minimumNodeCount,
            int averageNodeCount,
            int maximumNodeCount,
            TerrainRangeData nodeTurnDegrees,
            TerrainNoiseFieldData curvatureField,
            float terrainHeightChangeReferenceCells,
            float terrainAvoidanceStrength,
            int maximumDescendantBranchCount,
            float branchOccurrencePerNode,
            RiverDistributionData branchNodeCountRatio,
            int minimumBranchNodeCount,
            RiverDistributionData branchOpeningAngleDegrees,
            RiverDistributionData branchWidthRatio,
            RiverDistributionData branchDepthRatio,
            TerrainNoiseFieldData widthField,
            TerrainRangeData width,
            TerrainCurveData crossSection,
            TerrainRangeData depth,
            TerrainRangeData waterInset,
            TerrainNoiseFieldData riverbedField,
            TerrainRangeData riverbedAmplitude)
        {
            if (candidateLatticeSpacingCells <= 0
                || anchorJitterCells < 0
                || !float.IsFinite(occurrence)
                || occurrence < 0f
                || occurrence > 1f
                || minimumNodeCount < 2
                || averageNodeCount < minimumNodeCount
                || averageNodeCount > maximumNodeCount
                || nodeTurnDegrees.Minimum < 0f
                || nodeTurnDegrees.Maximum >= 90f
                || !float.IsFinite(terrainHeightChangeReferenceCells)
                || terrainHeightChangeReferenceCells <= 0f
                || !float.IsFinite(terrainAvoidanceStrength)
                || terrainAvoidanceStrength < 0f
                || maximumDescendantBranchCount < 0
                || !float.IsFinite(branchOccurrencePerNode)
                || branchOccurrencePerNode < 0f
                || branchOccurrencePerNode > 1f
                || branchNodeCountRatio.Minimum <= 0f
                || branchNodeCountRatio.Maximum >= 1f
                || minimumBranchNodeCount < 2
                || branchOpeningAngleDegrees.Minimum <= 0f
                || branchOpeningAngleDegrees.Maximum >= 90f
                || branchWidthRatio.Minimum <= 0f
                || branchWidthRatio.Maximum > 1f
                || branchDepthRatio.Minimum <= 0f
                || branchDepthRatio.Maximum > 1f
                || width.Minimum <= 0f
                || !BasinFeatureSettingsData.IsUnitTransition(crossSection)
                || depth.Minimum <= 0f
                || waterInset.Minimum < 0f
                || riverbedAmplitude.Maximum >= depth.Minimum)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(candidateLatticeSpacingCells));
            }

            CandidateLatticeSpacingCells = candidateLatticeSpacingCells;
            AnchorJitterCells = anchorJitterCells;
            Occurrence = occurrence;
            MinimumNodeCount = minimumNodeCount;
            AverageNodeCount = averageNodeCount;
            MaximumNodeCount = maximumNodeCount;
            NodeTurnDegrees = nodeTurnDegrees;
            CurvatureField = curvatureField;
            TerrainHeightChangeReferenceCells = terrainHeightChangeReferenceCells;
            TerrainAvoidanceStrength = terrainAvoidanceStrength;
            MaximumDescendantBranchCount = maximumDescendantBranchCount;
            BranchOccurrencePerNode = branchOccurrencePerNode;
            BranchNodeCountRatio = branchNodeCountRatio;
            MinimumBranchNodeCount = minimumBranchNodeCount;
            BranchOpeningAngleDegrees = branchOpeningAngleDegrees;
            BranchWidthRatio = branchWidthRatio;
            BranchDepthRatio = branchDepthRatio;
            WidthField = widthField;
            Width = width;
            CrossSection = crossSection;
            Depth = depth;
            WaterInset = waterInset;
            RiverbedField = riverbedField;
            RiverbedAmplitude = riverbedAmplitude;
        }

        public int CandidateLatticeSpacingCells { get; }
        public int AnchorJitterCells { get; }
        public float Occurrence { get; }
        public int MinimumNodeCount { get; }
        public int AverageNodeCount { get; }
        public int MaximumNodeCount { get; }
        public TerrainRangeData NodeTurnDegrees { get; }
        public TerrainNoiseFieldData CurvatureField { get; }
        public float TerrainHeightChangeReferenceCells { get; }
        public float TerrainAvoidanceStrength { get; }
        public int MaximumDescendantBranchCount { get; }
        public float BranchOccurrencePerNode { get; }
        public RiverDistributionData BranchNodeCountRatio { get; }
        public int MinimumBranchNodeCount { get; }
        public RiverDistributionData BranchOpeningAngleDegrees { get; }
        public RiverDistributionData BranchWidthRatio { get; }
        public RiverDistributionData BranchDepthRatio { get; }
        public TerrainNoiseFieldData WidthField { get; }
        public TerrainRangeData Width { get; }
        public TerrainCurveData CrossSection { get; }
        public TerrainRangeData Depth { get; }
        public TerrainRangeData WaterInset { get; }
        public TerrainNoiseFieldData RiverbedField { get; }
        public TerrainRangeData RiverbedAmplitude { get; }
    }

    public sealed class HydrologyFeatureSettingsData
    {
        public HydrologyFeatureSettingsData(
            WorldSettingsData world,
            SeaFeatureSettingsData sea,
            BasinFeatureSettingsData basins,
            RiverFeatureSettingsData river)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Sea = sea;
            Basins = basins;
            River = river;
        }

        public WorldSettingsData World { get; }
        public SeaFeatureSettingsData Sea { get; }
        public BasinFeatureSettingsData Basins { get; }
        public RiverFeatureSettingsData River { get; }
    }
}
