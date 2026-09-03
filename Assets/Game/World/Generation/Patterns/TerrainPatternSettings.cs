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
        [SerializeField] private PatternNoiseMode mode;
        [SerializeField] private float scale;
        [SerializeField] private int layers;
        [SerializeField] private float frequencySpacing;
        [SerializeField] private float persistence;
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
        [SerializeField] private float atZero;
        [SerializeField] private float atQuarter;
        [SerializeField] private float atHalf;
        [SerializeField] private float atThreeQuarters;
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
        [SerializeField] private float minimum;
        [SerializeField] private float maximum;

        internal TerrainRangeData CreateData(float scale = 1f) => new(
            minimum * scale,
            maximum * scale);
    }

    [Serializable]
    public struct PatternDomainWarp
    {
        [SerializeField] private PatternNoiseField field;
        [SerializeField] private float strengthCells;

        internal TerrainDomainWarpData CreateData() => new(
            field.CreateData(),
            strengthCells);
    }

    [Serializable]
    public struct TerrainNoiseRouterSettings
    {
        [SerializeField] private PatternNoiseField continentalness;
        [SerializeField] private PatternNoiseField erosion;

        internal TerrainNoiseRouterData CreateData() => new(
            continentalness.CreateData(),
            erosion.CreateData());
    }

    [Serializable]
    public struct TerrainRegionSettings
    {
        [SerializeField] private int sizeCells;
        [SerializeField] private float centerJitter;
        [SerializeField] private PatternNoiseField warpField;
        [SerializeField] private float warpStrengthCells;
        [SerializeField] private float boundaryBlendCells;
        [SerializeField] private float interiorReachRatio;
        [SerializeField] private float smoothShare;
        [SerializeField] private float ruggedShare;
        [SerializeField] private float mountainShare;
        [SerializeField] private float canyonShare;
        [SerializeField] private float seaShare;

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
            canyonShare,
            seaShare);
    }

    [Serializable]
    public struct TerrainBaseSurfaceSettings
    {
        [SerializeField] private PatternCurve surfaceByContinentalness;
        [SerializeField] private PatternCurve surfaceByErosion;

        internal TerrainBaseSurfaceData CreateData() => new(
            surfaceByContinentalness.CreateData(
                WorldGrid.HeightStepsPerCell),
            surfaceByErosion.CreateData(
                WorldGrid.HeightStepsPerCell));
    }

    [Serializable]
    public struct TerrainSurfaceFormSettings
    {
        [SerializeField] private PatternDomainWarp domainWarp;
        [SerializeField] private PatternNoiseField shapeField;
        [SerializeField] private PatternCurve shapeResponse;
        [SerializeField] private PatternRange shapeAmplitudeCells;
        [SerializeField] private PatternNoiseField detailField;
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
        [SerializeField] private PatternDomainWarp domainWarp;
        [SerializeField] private PatternNoiseField massField;
        [SerializeField] private PatternCurve massResponse;
        [SerializeField] private PatternRange heightCells;
        [SerializeField] private PatternNoiseField ridgeField;
        [SerializeField] private PatternCurve ridgeResponse;
        [SerializeField] private PatternRange ridgeStrengthCells;
        [SerializeField] private PatternNoiseField detailField;
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
        [SerializeField] private PatternDomainWarp domainWarp;
        [SerializeField] private PatternNoiseField basinField;
        [SerializeField] private PatternCurve basinResponse;
        [SerializeField] private PatternRange basinDepthRatio;
        [SerializeField] private PatternNoiseField valleyField;
        [SerializeField] private PatternCurve valleyResponse;
        [SerializeField] private PatternRange valleyDepthRatio;
        [SerializeField] private PatternRange depthCells;
        [SerializeField] private PatternNoiseField detailField;
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
        [SerializeField] private int patternTileChunkSpan;
        [SerializeField] private int terrainBaseHeightCells;
        [SerializeField] private TerrainNoiseRouterSettings noiseRouter;
        [SerializeField] private TerrainRegionSettings region;
        [SerializeField] private TerrainBaseSurfaceSettings baseSurface;
        [SerializeField] private TerrainSurfaceFormSettings smooth;
        [SerializeField] private TerrainSurfaceFormSettings rugged;
        [SerializeField] private MountainFormSettings mountain;
        [SerializeField] private CanyonFormSettings canyon;

        public TerrainPatternSettingsData CreateData(int worldSeed) => new(
            worldSeed,
            patternTileChunkSpan,
            checked(terrainBaseHeightCells * WorldGrid.HeightStepsPerCell),
            noiseRouter.CreateData(),
            region.CreateData(),
            baseSurface.CreateData(),
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
            TerrainNoiseFieldData continentalness,
            TerrainNoiseFieldData erosion)
        {
            Continentalness = continentalness;
            Erosion = erosion;
        }

        public TerrainNoiseFieldData Continentalness { get; }
        public TerrainNoiseFieldData Erosion { get; }
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
            float canyonShare,
            float seaShare)
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
                || !float.IsFinite(seaShare)
                || smoothShare < 0f
                || ruggedShare < 0f
                || mountainShare < 0f
                || canyonShare < 0f
                || seaShare < 0f
                || smoothShare + ruggedShare + mountainShare
                    + canyonShare + seaShare <= 0f)
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
            SeaShare = seaShare;
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
        public float SeaShare { get; }
        public float TotalShare => SmoothShare + RuggedShare
            + MountainShare + CanyonShare + SeaShare;
    }

    public readonly struct TerrainBaseSurfaceData
    {
        public TerrainBaseSurfaceData(
            TerrainCurveData surfaceByContinentalness,
            TerrainCurveData surfaceByErosion)
        {
            SurfaceByContinentalness = surfaceByContinentalness;
            SurfaceByErosion = surfaceByErosion;
        }

        public TerrainCurveData SurfaceByContinentalness { get; }
        public TerrainCurveData SurfaceByErosion { get; }
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
            int terrainBaseHeight,
            TerrainNoiseRouterData noiseRouter,
            TerrainRegionData region,
            TerrainBaseSurfaceData baseSurface,
            TerrainSurfaceFormData smooth,
            TerrainSurfaceFormData rugged,
            TerrainMountainFormData mountain,
            TerrainCanyonFormData canyon)
        {
            if (patternTileChunkSpan <= 0 || terrainBaseHeight < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(patternTileChunkSpan));
            }

            WorldSeed = worldSeed;
            PatternTileChunkSpan = patternTileChunkSpan;
            TerrainBaseHeight = terrainBaseHeight;
            NoiseRouter = noiseRouter;
            Region = region;
            BaseSurface = baseSurface;
            Smooth = smooth;
            Rugged = rugged;
            Mountain = mountain;
            Canyon = canyon;
        }

        public int WorldSeed { get; }
        public int PatternTileChunkSpan { get; }
        public int TerrainBaseHeight { get; }
        public TerrainNoiseRouterData NoiseRouter { get; }
        public TerrainRegionData Region { get; }
        public TerrainBaseSurfaceData BaseSurface { get; }
        public TerrainSurfaceFormData Smooth { get; }
        public TerrainSurfaceFormData Rugged { get; }
        public TerrainMountainFormData Mountain { get; }
        public TerrainCanyonFormData Canyon { get; }
    }
}
