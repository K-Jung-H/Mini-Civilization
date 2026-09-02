using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation.Semantic
{
    [Serializable]
    public struct SemanticBasinFeatureSettings
    {
        [SerializeField] private int candidateLatticeSpacingCells;
        [SerializeField] private float occurrence;
        [SerializeField] private SemanticRange areaCells;
        [SerializeField] private int pondMaximumAreaCells;
        [SerializeField] private SemanticRange maximumDepthCells;
        [SerializeField] private SemanticNoiseField potentialField;
        [SerializeField] private SemanticCurve potentialResponse;
        [SerializeField] private int shoreTransitionCells;
        [SerializeField] private SemanticCurve shoreTransition;
        [SerializeField] private SemanticCurve depthByInterior;
        [SerializeField] private SemanticNoiseField bedField;
        [SerializeField] private SemanticRange bedAmplitudeCells;
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
    public struct SemanticSeaFeatureSettings
    {
        [SerializeField] private SemanticDomainWarp domainWarp;
        [SerializeField] private SemanticNoiseField basinField;
        [SerializeField] private float basinVariation;
        [SerializeField] private SemanticCurve depthByInterior;
        [SerializeField] private SemanticRange maximumDepthCells;
        [SerializeField] private SemanticNoiseField seabedField;
        [SerializeField] private SemanticRange seabedAmplitudeCells;
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
    public struct SemanticRiverFeatureSettings
    {
        [SerializeField] private int candidateLatticeSpacingCells;
        [SerializeField] private int anchorJitterCells;
        [SerializeField] private float occurrence;
        [SerializeField] private SemanticRange lengthCells;
        [SerializeField] private int strokeSampleSpacingCells;
        [SerializeField] private SemanticRange nodeTurnDegrees;
        [SerializeField] private int terrainCorrectionRadiusCells;
        [SerializeField] private int terrainCorrectionSmoothingPasses;
        [SerializeField] private float terrainSlopeCost;
        [SerializeField] private float baseStrokeDeviationCost;
        [SerializeField] private float elevationChangeCost;
        [SerializeField] private float corridorDeformationCost;
        [SerializeField] private float curvatureCost;
        [SerializeField] private SemanticNoiseField widthField;
        [SerializeField] private SemanticRange widthCells;
        [SerializeField] private SemanticCurve crossSection;
        [SerializeField] private SemanticRange depthCells;
        [SerializeField] private SemanticRange waterInsetCells;
        [SerializeField] private float bankMarginCells;
        [SerializeField] private int dropTransitionCells;
        [SerializeField] private SemanticCurve dropTransition;
        [SerializeField] private SemanticNoiseField riverbedField;
        [SerializeField] private SemanticRange riverbedAmplitudeCells;

        internal RiverFeatureSettingsData CreateData() => new(
            candidateLatticeSpacingCells,
            anchorJitterCells,
            occurrence,
            lengthCells.CreateData(),
            strokeSampleSpacingCells,
            nodeTurnDegrees.CreateData(),
            terrainCorrectionRadiusCells,
            terrainCorrectionSmoothingPasses,
            terrainSlopeCost,
            baseStrokeDeviationCost,
            elevationChangeCost,
            corridorDeformationCost,
            curvatureCost,
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

    [Serializable]
    public struct SemanticNaturalEndpointSettings
    {
        [SerializeField] private int endpointTransitionCells;
        [SerializeField] private SemanticCurve endpointTransitionRate;

        internal NaturalEndpointSettingsData CreateData() => new(
            endpointTransitionCells,
            endpointTransitionRate.CreateData());
    }

    [CreateAssetMenu(
        fileName = "SemanticHydrologyFeatureSettings",
        menuName = "Mini Civilization/World/Semantic Hydrology Feature Settings")]
    public sealed class SemanticHydrologyFeatureSettings : ScriptableObject
    {
        [SerializeField] private SemanticSeaFeatureSettings sea;
        [SerializeField] private SemanticBasinFeatureSettings basins;
        [SerializeField] private SemanticRiverFeatureSettings river;
        [SerializeField] private SemanticNaturalEndpointSettings naturalEndpoint;

        public HydrologyFeatureSettingsData CreateData(
            WorldSettingsData world) => new(
            world ?? throw new ArgumentNullException(nameof(world)),
            sea.CreateData(),
            basins.CreateData(),
            river.CreateData(),
            naturalEndpoint.CreateData());
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
            TerrainRangeData length,
            int strokeSampleSpacingCells,
            TerrainRangeData nodeTurnDegrees,
            int terrainCorrectionRadiusCells,
            int terrainCorrectionSmoothingPasses,
            float terrainSlopeCost,
            float baseStrokeDeviationCost,
            float elevationChangeCost,
            float corridorDeformationCost,
            float curvatureCost,
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
                || length.Minimum <= 0f
                || strokeSampleSpacingCells <= 0
                || nodeTurnDegrees.Minimum < 0f
                || nodeTurnDegrees.Maximum >= 90f
                || terrainCorrectionRadiusCells < 0
                || terrainCorrectionSmoothingPasses < 0
                || !float.IsFinite(terrainSlopeCost) || terrainSlopeCost < 0f
                || !float.IsFinite(baseStrokeDeviationCost) || baseStrokeDeviationCost < 0f
                || !float.IsFinite(elevationChangeCost) || elevationChangeCost < 0f
                || !float.IsFinite(corridorDeformationCost) || corridorDeformationCost < 0f
                || !float.IsFinite(curvatureCost) || curvatureCost < 0f
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
            Length = length;
            StrokeSampleSpacingCells = strokeSampleSpacingCells;
            NodeTurnDegrees = nodeTurnDegrees;
            TerrainCorrectionRadiusCells = terrainCorrectionRadiusCells;
            TerrainCorrectionSmoothingPasses = terrainCorrectionSmoothingPasses;
            TerrainSlopeCost = terrainSlopeCost;
            BaseStrokeDeviationCost = baseStrokeDeviationCost;
            ElevationChangeCost = elevationChangeCost;
            CorridorDeformationCost = corridorDeformationCost;
            CurvatureCost = curvatureCost;
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
        public TerrainRangeData Length { get; }
        public int StrokeSampleSpacingCells { get; }
        public TerrainRangeData NodeTurnDegrees { get; }
        public int TerrainCorrectionRadiusCells { get; }
        public int TerrainCorrectionSmoothingPasses { get; }
        public float TerrainSlopeCost { get; }
        public float BaseStrokeDeviationCost { get; }
        public float ElevationChangeCost { get; }
        public float CorridorDeformationCost { get; }
        public float CurvatureCost { get; }
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

    public readonly struct NaturalEndpointSettingsData
    {
        public NaturalEndpointSettingsData(
            int endpointTransitionCells,
            TerrainCurveData endpointTransitionRate)
        {
            if (endpointTransitionCells <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(endpointTransitionCells));
            }

            EndpointTransitionCells = endpointTransitionCells;
            EndpointTransitionRate = endpointTransitionRate;
        }

        public int EndpointTransitionCells { get; }
        public TerrainCurveData EndpointTransitionRate { get; }
    }

    public sealed class HydrologyFeatureSettingsData
    {
        public HydrologyFeatureSettingsData(
            WorldSettingsData world,
            SeaFeatureSettingsData sea,
            BasinFeatureSettingsData basins,
            RiverFeatureSettingsData river,
            NaturalEndpointSettingsData naturalEndpoint)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Sea = sea;
            Basins = basins;
            River = river;
            NaturalEndpoint = naturalEndpoint;
        }

        public WorldSettingsData World { get; }
        public SeaFeatureSettingsData Sea { get; }
        public BasinFeatureSettingsData Basins { get; }
        public RiverFeatureSettingsData River { get; }
        public NaturalEndpointSettingsData NaturalEndpoint { get; }
    }
}
