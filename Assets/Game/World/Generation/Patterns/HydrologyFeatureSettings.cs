using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation.Patterns
{
    [Serializable]
    public struct BasinFeatureSettings
    {
        [SerializeField] private int candidateLatticeSpacingCells;
        [SerializeField] private float occurrence;
        [SerializeField] private PatternRange areaCells;
        [SerializeField] private int pondMaximumAreaCells;
        [SerializeField] private PatternRange maximumDepthCells;
        [SerializeField] private PatternNoiseField potentialField;
        [SerializeField] private PatternCurve potentialResponse;
        [SerializeField] private int shoreTransitionCells;
        [SerializeField] private PatternCurve shoreTransition;
        [SerializeField] private PatternCurve depthByInterior;
        [SerializeField] private PatternNoiseField bedField;
        [SerializeField] private PatternRange bedAmplitudeCells;
        [SerializeField] private int maximumReachCells;
        [SerializeField] private float potentialCost;
        [SerializeField] private float terrainDeformationCost;
        [SerializeField] private float slopeCost;
        [SerializeField] private float cutCost;
        [SerializeField] private float fillCost;
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
        [SerializeField] private PatternDomainWarp domainWarp;
        [SerializeField] private PatternNoiseField basinField;
        [SerializeField] private float basinVariation;
        [SerializeField] private PatternCurve depthByInterior;
        [SerializeField] private PatternRange maximumDepthCells;
        [SerializeField] private PatternNoiseField seabedField;
        [SerializeField] private PatternRange seabedAmplitudeCells;
        [SerializeField] private int surfaceCell;
        [SerializeField] private int surfaceStep;

        internal SeaFeatureSettingsData CreateData() => new(
            domainWarp.CreateData(),
            basinField.CreateData(),
            basinVariation,
            depthByInterior.CreateData(),
            maximumDepthCells.CreateData(WorldGrid.HeightStepsPerCell),
            seabedField.CreateData(),
            seabedAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell),
            checked(surfaceCell * WorldGrid.HeightStepsPerCell + surfaceStep));
    }

    [Serializable]
    public struct RiverFeatureSettings
    {
        [SerializeField] private int candidateLatticeSpacingCells;
        [SerializeField] private int anchorJitterCells;
        [SerializeField] private float occurrence;
        [SerializeField] private int minimumNodeCount;
        [SerializeField] private int averageNodeCount;
        [SerializeField] private int maximumNodeCount;
        [SerializeField] private PatternRange nodeTurnDegrees;
        [SerializeField] private PatternNoiseField curvatureField;
        [SerializeField] private PatternNoiseField widthField;
        [SerializeField] private PatternRange widthCells;
        [SerializeField] private PatternCurve crossSection;
        [SerializeField] private PatternRange depthCells;
        [SerializeField] private PatternRange waterInsetCells;
        [SerializeField] private float bankMarginCells;
        [SerializeField] private int dropTransitionCells;
        [SerializeField] private PatternCurve dropTransition;
        [SerializeField] private PatternNoiseField riverbedField;
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
            widthField.CreateData(),
            widthCells.CreateData(),
            crossSection.CreateData(),
            depthCells.CreateData(WorldGrid.HeightStepsPerCell),
            waterInsetCells.CreateData(WorldGrid.HeightStepsPerCell),
            bankMarginCells,
            dropTransitionCells,
            dropTransition.CreateData(),
            riverbedField.CreateData(),
            riverbedAmplitudeCells.CreateData(WorldGrid.HeightStepsPerCell));
    }

    [CreateAssetMenu(
        fileName = "HydrologyFeatureSettings",
        menuName = "Mini Civilization/World/Hydrology Feature Settings")]
    public sealed class HydrologyFeatureSettings : ScriptableObject
    {
        [SerializeField] private SeaFeatureSettings sea;
        [SerializeField] private BasinFeatureSettings basins;
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
                || maximumDepth.Minimum <= 0f
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
    }

    public readonly struct SeaFeatureSettingsData
    {
        public SeaFeatureSettingsData(
            TerrainDomainWarpData domainWarp,
            TerrainNoiseFieldData basinField,
            float basinVariation,
            TerrainCurveData depthByInterior,
            TerrainRangeData maximumDepth,
            TerrainNoiseFieldData seabedField,
            TerrainRangeData seabedAmplitude,
            int surfaceHeight)
        {
            if (!float.IsFinite(basinVariation)
                || basinVariation < 0f
                || surfaceHeight < 0
                || maximumDepth.Minimum <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(surfaceHeight));
            }

            DomainWarp = domainWarp;
            BasinField = basinField;
            BasinVariation = basinVariation;
            DepthByInterior = depthByInterior;
            MaximumDepth = maximumDepth;
            SeabedField = seabedField;
            SeabedAmplitude = seabedAmplitude;
            SurfaceHeight = surfaceHeight;
        }

        public TerrainDomainWarpData DomainWarp { get; }
        public TerrainNoiseFieldData BasinField { get; }
        public float BasinVariation { get; }
        public TerrainCurveData DepthByInterior { get; }
        public TerrainRangeData MaximumDepth { get; }
        public TerrainNoiseFieldData SeabedField { get; }
        public TerrainRangeData SeabedAmplitude { get; }
        public int SurfaceHeight { get; }
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
            TerrainNoiseFieldData widthField,
            TerrainRangeData width,
            TerrainCurveData crossSection,
            TerrainRangeData depth,
            TerrainRangeData waterInset,
            float bankMarginCells,
            int dropTransitionCells,
            TerrainCurveData dropTransition,
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
                || width.Minimum <= 0f
                || depth.Minimum <= 0f
                || waterInset.Minimum < 0f
                || !float.IsFinite(bankMarginCells) || bankMarginCells < 0f
                || dropTransitionCells <= 0
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
            WidthField = widthField;
            Width = width;
            CrossSection = crossSection;
            Depth = depth;
            WaterInset = waterInset;
            BankMarginCells = bankMarginCells;
            DropTransitionCells = dropTransitionCells;
            DropTransition = dropTransition;
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
        public TerrainNoiseFieldData WidthField { get; }
        public TerrainRangeData Width { get; }
        public TerrainCurveData CrossSection { get; }
        public TerrainRangeData Depth { get; }
        public TerrainRangeData WaterInset { get; }
        public float BankMarginCells { get; }
        public int DropTransitionCells { get; }
        public TerrainCurveData DropTransition { get; }
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
