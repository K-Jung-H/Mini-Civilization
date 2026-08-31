using System;

namespace MiniCivilization.World.Domain
{
    public readonly struct HydrologyMapSettingsData
    {
        public HydrologyMapSettingsData(
            int planningRegionSizeCells,
            int routeSampleSpacingCells,
            int basinSeedSpacingCells,
            WorldNoiseFieldSettingsData basinPotentialField,
            WorldCurveSettingsData basinPotentialResponse,
            float basinPotentialCost,
            float terrainDeformationCost,
            float slopeCost)
        {
            if (planningRegionSizeCells < 16
                || routeSampleSpacingCells < 1
                || planningRegionSizeCells % routeSampleSpacingCells != 0
                || basinSeedSpacingCells < 4
                || !IsNonNegative(basinPotentialCost)
                || !IsNonNegative(terrainDeformationCost)
                || !IsNonNegative(slopeCost))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(planningRegionSizeCells),
                    "Hydrology Map settings are invalid.");
            }

            PlanningRegionSizeCells = planningRegionSizeCells;
            RouteSampleSpacingCells = routeSampleSpacingCells;
            BasinSeedSpacingCells = basinSeedSpacingCells;
            BasinPotentialField = basinPotentialField;
            BasinPotentialResponse = basinPotentialResponse;
            BasinPotentialCost = basinPotentialCost;
            TerrainDeformationCost = terrainDeformationCost;
            SlopeCost = slopeCost;
        }

        public int PlanningRegionSizeCells { get; }
        public int RouteSampleSpacingCells { get; }
        public int BasinSeedSpacingCells { get; }
        public WorldNoiseFieldSettingsData BasinPotentialField { get; }
        public WorldCurveSettingsData BasinPotentialResponse { get; }
        public float BasinPotentialCost { get; }
        public float TerrainDeformationCost { get; }
        public float SlopeCost { get; }

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;
    }

    public readonly struct RiverNetworkSettingsData
    {
        public RiverNetworkSettingsData(
            float headDensity,
            float endDensity,
            WorldSeededRangeSettingsData lengthCells,
            float junctionChance,
            float lakeEndpointWeight,
            float pondEndpointWeight,
            float seaEndpointWeight,
            float naturalEndpointWeight,
            float terrainChangeCost,
            float uphillCost,
            float crossSlopeCost,
            float valleyPreference,
            WorldNoiseFieldSettingsData routeVariationField,
            float routeVariationCost,
            int naturalHeadTransitionCells,
            WorldCurveSettingsData naturalHeadTransition,
            int naturalEndTransitionCells,
            WorldCurveSettingsData naturalEndTransition)
        {
            if (!IsUnit(headDensity)
                || !IsUnit(endDensity)
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
                throw new ArgumentOutOfRangeException(
                    nameof(headDensity),
                    "River Network settings are invalid.");
            }

            HeadDensity = headDensity;
            EndDensity = endDensity;
            LengthCells = lengthCells;
            JunctionChance = junctionChance;
            LakeEndpointWeight = lakeEndpointWeight;
            PondEndpointWeight = pondEndpointWeight;
            SeaEndpointWeight = seaEndpointWeight;
            NaturalEndpointWeight = naturalEndpointWeight;
            TerrainChangeCost = terrainChangeCost;
            UphillCost = uphillCost;
            CrossSlopeCost = crossSlopeCost;
            ValleyPreference = valleyPreference;
            RouteVariationField = routeVariationField;
            RouteVariationCost = routeVariationCost;
            NaturalHeadTransitionCells = naturalHeadTransitionCells;
            NaturalHeadTransition = naturalHeadTransition;
            NaturalEndTransitionCells = naturalEndTransitionCells;
            NaturalEndTransition = naturalEndTransition;
        }

        public float HeadDensity { get; }
        public float EndDensity { get; }
        public WorldSeededRangeSettingsData LengthCells { get; }
        public float JunctionChance { get; }
        public float LakeEndpointWeight { get; }
        public float PondEndpointWeight { get; }
        public float SeaEndpointWeight { get; }
        public float NaturalEndpointWeight { get; }
        public float TerrainChangeCost { get; }
        public float UphillCost { get; }
        public float CrossSlopeCost { get; }
        public float ValleyPreference { get; }
        public WorldNoiseFieldSettingsData RouteVariationField { get; }
        public float RouteVariationCost { get; }
        public int NaturalHeadTransitionCells { get; }
        public WorldCurveSettingsData NaturalHeadTransition { get; }
        public int NaturalEndTransitionCells { get; }
        public WorldCurveSettingsData NaturalEndTransition { get; }

        private static bool IsUnit(float value) =>
            float.IsFinite(value) && value >= 0f && value <= 1f;

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;
    }

    public readonly struct RiverGraphSettingsData
    {
        public RiverGraphSettingsData(
            WorldSeededRangeSettingsData connectionRadiusCells,
            float elevationChangeCost,
            int naturalTransitionCells,
            WorldCurveSettingsData naturalTransitionRate,
            WorldCurveSettingsData proximityChance,
            WorldCurveSettingsData alignmentChance)
        {
            if (connectionRadiusCells.Minimum < 1f
                || !IsNonNegative(elevationChangeCost)
                || naturalTransitionCells < 1
                || !HasPositiveIntegral(naturalTransitionRate)
                || !IsUnitCurve(proximityChance, descending: true)
                || !IsUnitCurve(alignmentChance, descending: false))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(connectionRadiusCells),
                    "River Graph settings are invalid.");
            }

            ConnectionRadiusCells = connectionRadiusCells;
            ElevationChangeCost = elevationChangeCost;
            NaturalTransitionCells = naturalTransitionCells;
            NaturalTransitionRate = naturalTransitionRate;
            ProximityChance = proximityChance;
            AlignmentChance = alignmentChance;
        }

        public WorldSeededRangeSettingsData ConnectionRadiusCells { get; }
        public float ElevationChangeCost { get; }
        public int NaturalTransitionCells { get; }
        public WorldCurveSettingsData NaturalTransitionRate { get; }
        public WorldCurveSettingsData ProximityChance { get; }
        public WorldCurveSettingsData AlignmentChance { get; }

        private static bool HasPositiveIntegral(
            in WorldCurveSettingsData curve) =>
            IsNonNegative(curve.AtZero)
            && IsNonNegative(curve.AtQuarter)
            && IsNonNegative(curve.AtHalf)
            && IsNonNegative(curve.AtThreeQuarters)
            && IsNonNegative(curve.AtOne)
            && curve.AtZero + curve.AtQuarter + curve.AtHalf
                + curve.AtThreeQuarters + curve.AtOne > 0f;

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

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;
    }

    public readonly struct RiverCorridorSettingsData
    {
        public RiverCorridorSettingsData(
            float corridorExposureCost,
            float bankMarginCells,
            int smoothingIterations,
            WorldNoiseFieldSettingsData widthField,
            WorldSeededRangeSettingsData widthCells,
            WorldCurveSettingsData crossSection,
            WorldSeededRangeSettingsData depthUnits,
            WorldSeededRangeSettingsData waterInsetUnits,
            int dropTransitionCells,
            WorldCurveSettingsData dropTransition,
            WorldNoiseFieldSettingsData riverbedField,
            WorldSeededRangeSettingsData riverbedAmplitudeUnits)
        {
            if (!IsNonNegative(corridorExposureCost)
                || !IsNonNegative(bankMarginCells)
                || smoothingIterations < 0
                || smoothingIterations > 4
                || widthCells.Minimum < 1f
                || widthCells.Maximum > 64f
                || dropTransitionCells < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(widthCells),
                    "River Corridor settings are invalid.");
            }

            CorridorExposureCost = corridorExposureCost;
            BankMarginCells = bankMarginCells;
            SmoothingIterations = smoothingIterations;
            WidthField = widthField;
            WidthCells = widthCells;
            CrossSection = crossSection;
            DepthUnits = depthUnits;
            WaterInsetUnits = waterInsetUnits;
            DropTransitionCells = dropTransitionCells;
            DropTransition = dropTransition;
            RiverbedField = riverbedField;
            RiverbedAmplitudeUnits = riverbedAmplitudeUnits;
        }

        public float CorridorExposureCost { get; }
        public float BankMarginCells { get; }
        public int SmoothingIterations { get; }
        public WorldNoiseFieldSettingsData WidthField { get; }
        public WorldSeededRangeSettingsData WidthCells { get; }
        public WorldCurveSettingsData CrossSection { get; }
        public WorldSeededRangeSettingsData DepthUnits { get; }
        public WorldSeededRangeSettingsData WaterInsetUnits { get; }
        public int DropTransitionCells { get; }
        public WorldCurveSettingsData DropTransition { get; }
        public WorldNoiseFieldSettingsData RiverbedField { get; }
        public WorldSeededRangeSettingsData RiverbedAmplitudeUnits { get; }

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;
    }

    public readonly struct BasinProfileSettingsData
    {
        public BasinProfileSettingsData(
            float occurrence,
            WorldSeededRangeSettingsData areaCells,
            WorldSeededRangeSettingsData maximumDepthUnits,
            float riverConnectionChance,
            float headRoleChance)
        {
            if (!IsUnit(occurrence)
                || areaCells.Minimum < 1f
                || maximumDepthUnits.Minimum <= 0f
                || !IsUnit(riverConnectionChance)
                || !IsUnit(headRoleChance))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(occurrence),
                    "Basin profile settings are invalid.");
            }

            Occurrence = occurrence;
            AreaCells = areaCells;
            MaximumDepthUnits = maximumDepthUnits;
            RiverConnectionChance = riverConnectionChance;
            HeadRoleChance = headRoleChance;
        }

        public float Occurrence { get; }
        public WorldSeededRangeSettingsData AreaCells { get; }
        public WorldSeededRangeSettingsData MaximumDepthUnits { get; }
        public float RiverConnectionChance { get; }
        public float HeadRoleChance { get; }

        private static bool IsUnit(float value) =>
            float.IsFinite(value) && value >= 0f && value <= 1f;
    }

    public readonly struct BasinPatternSettingsData
    {
        public BasinPatternSettingsData(
            int minimumSeparationCells,
            int maximumReachCells,
            float cutCost,
            float fillCost,
            float rimCost,
            int shoreTransitionCells,
            WorldCurveSettingsData shoreTransition,
            WorldCurveSettingsData depthByInterior,
            WorldNoiseFieldSettingsData bedField,
            WorldSeededRangeSettingsData bedAmplitudeUnits,
            BasinProfileSettingsData lake,
            BasinProfileSettingsData pond)
        {
            var maximumDiameter = checked((long)maximumReachCells * 2 + 1);
            var maximumFootprintCells = checked(
                maximumDiameter * maximumDiameter);
            if (minimumSeparationCells < 0
                || maximumReachCells < 1
                || lake.AreaCells.Maximum > maximumFootprintCells
                || pond.AreaCells.Maximum > maximumFootprintCells
                || !IsNonNegative(cutCost)
                || !IsNonNegative(fillCost)
                || !IsNonNegative(rimCost)
                || shoreTransitionCells < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumSeparationCells),
                    "Basin Pattern settings are invalid.");
            }

            MinimumSeparationCells = minimumSeparationCells;
            MaximumReachCells = maximumReachCells;
            CutCost = cutCost;
            FillCost = fillCost;
            RimCost = rimCost;
            ShoreTransitionCells = shoreTransitionCells;
            ShoreTransition = shoreTransition;
            DepthByInterior = depthByInterior;
            BedField = bedField;
            BedAmplitudeUnits = bedAmplitudeUnits;
            Lake = lake;
            Pond = pond;
        }

        public int MinimumSeparationCells { get; }
        public int MaximumReachCells { get; }
        public float CutCost { get; }
        public float FillCost { get; }
        public float RimCost { get; }
        public int ShoreTransitionCells { get; }
        public WorldCurveSettingsData ShoreTransition { get; }
        public WorldCurveSettingsData DepthByInterior { get; }
        public WorldNoiseFieldSettingsData BedField { get; }
        public WorldSeededRangeSettingsData BedAmplitudeUnits { get; }
        public BasinProfileSettingsData Lake { get; }
        public BasinProfileSettingsData Pond { get; }

        private static bool IsNonNegative(float value) =>
            float.IsFinite(value) && value >= 0f;
    }

    public readonly struct HydrologySettingsData
    {
        public HydrologySettingsData(
            HydrologyMapSettingsData map,
            RiverNetworkSettingsData riverNetwork,
            RiverGraphSettingsData riverGraph,
            RiverCorridorSettingsData riverCorridor,
            BasinPatternSettingsData basins)
        {
            Map = map;
            RiverNetwork = riverNetwork;
            RiverGraph = riverGraph;
            RiverCorridor = riverCorridor;
            Basins = basins;
        }

        public HydrologyMapSettingsData Map { get; }
        public RiverNetworkSettingsData RiverNetwork { get; }
        public RiverGraphSettingsData RiverGraph { get; }
        public RiverCorridorSettingsData RiverCorridor { get; }
        public BasinPatternSettingsData Basins { get; }
    }
}
