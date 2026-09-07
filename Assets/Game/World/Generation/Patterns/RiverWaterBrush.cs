using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    internal sealed class RiverWaterBrush : IWaterMapBrush
    {
        [ThreadStatic] private static List<RiverNearestSegment> overlapScratch;
        // One exterior Cell protects the cavity; another half-width joins the bank
        // to the original terrain. This is geometric support, not a water-flow limit.
        internal static float InfluenceRadius(float width) => width + 1f;
        private readonly RiverBoneTree boneTree;
        private readonly RiverFeatureSettingsData riverSettings;
        private readonly int seaSurfaceHeight;
        private readonly RiverWaterProfile[][] profiles;
        private readonly RiverSegmentSpatialIndex segmentIndex;

        public RiverWaterBrush(
            HydrologyFeatureKey key,
            RiverBoneTree boneTree,
            RiverFeatureSettingsData riverSettings,
            int seaSurfaceHeight,
            ITerrainPatternMapReader terrain)
        {
            Key = key;
            this.boneTree = boneTree ?? throw new ArgumentNullException(nameof(boneTree));
            this.riverSettings = riverSettings;
            this.seaSurfaceHeight = seaSurfaceHeight;
            profiles = BuildProfiles(terrain ?? throw new ArgumentNullException(nameof(terrain)));
            var minimumX = float.PositiveInfinity;
            var minimumZ = float.PositiveInfinity;
            var maximumX = float.NegativeInfinity;
            var maximumZ = float.NegativeInfinity;
            var halfWidth = InfluenceRadius(riverSettings.Width.Maximum);
            foreach (var stroke in boneTree.Strokes)
            foreach (var node in stroke.Nodes)
            {
                minimumX = Math.Min(minimumX, node.Point.X - halfWidth);
                minimumZ = Math.Min(minimumZ, node.Point.Z - halfWidth);
                maximumX = Math.Max(maximumX, node.Point.X + halfWidth);
                maximumZ = Math.Max(maximumZ, node.Point.Z + halfWidth);
            }
            Bounds = new PatternTileBounds(
                checked((int)MathF.Floor(minimumX)), checked((int)MathF.Floor(minimumZ)),
                checked((int)MathF.Ceiling(maximumX) + 1), checked((int)MathF.Ceiling(maximumZ) + 1));
            segmentIndex = new RiverSegmentSpatialIndex(
                boneTree,
                InfluenceRadius(riverSettings.Width.Maximum));
        }

        public HydrologyFeatureKey Key { get; }
        public PatternTileBounds? Bounds { get; }

        public bool TrySample(
            int x,
            int z,
            TerrainPatternCell terrainCell,
            out HydrologyDrawingSample sample)
        {
            var reaches = overlapScratch ??= new List<RiverNearestSegment>();
            reaches.Clear();
            var nearest = FindNearestSegment(x,z,reaches);
            if (!TrySampleSegment(x,z,terrainCell,nearest,out sample)) return false;
            if (!sample.HasWater || reaches.Count <= 1) return true;
            // The initial candidate traversal also collects wet local reaches.
            reaches.Sort((a,b) => a.StrokeIndex != b.StrokeIndex
                ? a.StrokeIndex.CompareTo(b.StrokeIndex) : a.NodeIndex.CompareTo(b.NodeIndex));
            var ground = sample.GroundHeight;
            for (var i = 0; i < reaches.Count;)
            {
                var best = reaches[i];
                var last = reaches[i++];
                while (i < reaches.Count && reaches[i].StrokeIndex == last.StrokeIndex
                    && reaches[i].NodeIndex <= last.NodeIndex + 1)
                {
                    last = reaches[i++];
                    if (last.Distance < best.Distance) best = last;
                }
                if (best.StrokeIndex == nearest.StrokeIndex && best.NodeIndex == nearest.NodeIndex) continue;
                if (TrySampleSegment(x,z,terrainCell,best,out var other) && other.HasWater)
                    ground = Math.Min(ground,other.GroundHeight);
            }
            sample = new HydrologyDrawingSample(sample.Key,sample.WaterType,ground,
                sample.WaterSurfaceHeight,sample.InteriorInfluence,sample.BoundaryInfluence,true);
            return true;
        }

        private bool TrySampleSegment(int x,int z,TerrainPatternCell terrainCell,
            RiverNearestSegment nearest,out HydrologyDrawingSample sample)
        {
            if (nearest.StrokeIndex < 0)
            {
                sample = default;
                return false;
            }
            var profile = RiverWaterProfile.Lerp(
                profiles[nearest.StrokeIndex][nearest.NodeIndex],
                profiles[nearest.StrokeIndex][nearest.NodeIndex + 1],
                nearest.Progress);
            var width = profile.Width;
            if (width <= 0f || nearest.Distance > InfluenceRadius(width))
            {
                sample = default;
                return false;
            }
            if (width > 0f && nearest.Distance >= width * 0.5f)
            {
                var support = InfluenceRadius(width);
                var progress = Math.Clamp((support - nearest.Distance) / (width * 0.5f), 0f, 1f);
                var membership = progress * progress * (3f - 2f * progress);
                sample = new HydrologyDrawingSample(Key, WaterType.None,
                    terrainCell.SurfaceHeight + (profile.WaterSurface + 1f - terrainCell.SurfaceHeight) * membership,
                    0, 0, membership, false);
                return true;
            }
            var radial = 1f - Math.Clamp(
                nearest.Distance / Math.Max(width * 0.5f, 0.0001f),
                0f,
                1f);
            var cross = riverSettings.CrossSection.Evaluate(radial);
            if (width <= 0f || cross <= 0f)
            {
                sample = default;
                return false;
            }

            var bedNoise = WaterMapDrawingMath.SampleSigned(
                x,
                z,
                riverSettings.RiverbedField,
                WaterMapDrawingMath.DeriveSeed(
                    boneTree.Strokes[nearest.StrokeIndex].FeatureSeed,
                    "riverbed"))
                * WaterMapDrawingMath.Lerp(
                    riverSettings.RiverbedAmplitude.Minimum,
                    riverSettings.RiverbedAmplitude.Maximum,
                    WaterMapDrawingMath.SampleNormalized(
                        x,
                        z,
                        riverSettings.WidthField,
                        WaterMapDrawingMath.DeriveSeed(
                            boneTree.Strokes[nearest.StrokeIndex].FeatureSeed,
                            "riverbed-amplitude")));
            var ground = HydrologyHeightSolver.RiverGround(
                profile.WaterSurface,
                Math.Max(0f, profile.WaterDepthBase - bedNoise), cross);
            sample = new HydrologyDrawingSample(
                Key,
                WaterType.River,
                ground,
                profile.WaterSurface,
                cross,
                1f - cross,
                profile.WaterSurface > ground);
            return true;
        }

        private RiverNearestSegment FindNearestSegment(int x, int z, List<RiverNearestSegment> reaches)
        {
            var point = new WaterMapPoint(x,z);
            var best = RiverNearestSegment.None;
            var bestIsWet = false;
            foreach (var segment in segmentIndex.GetCandidates(x,z))
            {
                var nodes = boneTree.Strokes[segment.StrokeIndex].Nodes;
                var candidate = RiverNearestSegment.Create(segment,point,
                    nodes[segment.NodeIndex].Point,nodes[segment.NodeIndex+1].Point);
                var profile = RiverWaterProfile.Lerp(profiles[segment.StrokeIndex][segment.NodeIndex],
                    profiles[segment.StrokeIndex][segment.NodeIndex+1],candidate.Progress);
                var isWet = candidate.Distance < profile.Width * .5f;
                if (isWet) reaches.Add(candidate);
                if (candidate.Distance <= InfluenceRadius(profile.Width)
                    && (best.StrokeIndex < 0 || (isWet && !bestIsWet)
                        || (isWet == bestIsWet && candidate.Distance < best.Distance)))
                {
                    best = candidate;
                    bestIsWet = isWet;
                }
            }
            return best;
        }
        private RiverWaterProfile[][] BuildProfiles(ITerrainPatternMapReader terrain)
        {
            var result = new RiverWaterProfile[boneTree.Strokes.Count][];
            for (var strokeIndex = 0;
                 strokeIndex < boneTree.Strokes.Count;
                 strokeIndex++)
            {
                result[strokeIndex] = BuildStrokeProfiles(
                    boneTree.Strokes[strokeIndex],
                    result,
                    terrain);
            }

            return result;
        }

        private RiverWaterProfile[] BuildStrokeProfiles(
            RiverBoneStroke stroke,
            IReadOnlyList<RiverWaterProfile[]> completedProfiles,
            ITerrainPatternMapReader terrain)
        {
            var nodes = stroke.Nodes;
            var rawSurfaces = new float[nodes.Length];
            var widths = new float[nodes.Length];
            var bedDepths = new float[nodes.Length];
            var waterDepthBases = new float[nodes.Length];
            var parentProfile = stroke.HasParent
                ? completedProfiles[stroke.ParentStrokeIndex][stroke.ParentNodeIndex]
                : default;
            for (var index = 0; index < nodes.Length; index++)
            {
                var point = nodes[index];
                var progress = stroke.TotalDistance <= 0f
                    ? 0f
                    : point.DistanceFromStart / stroke.TotalDistance;
                var targetWidth = ResolveWidth(
                    point.Point,
                    progress,
                    stroke.FeatureSeed) * stroke.WidthScale;
                var targetDepth = ResolveDepth(
                    point.Point,
                    progress,
                    stroke.FeatureSeed) * stroke.DepthScale;
                var inset = ResolveInset(point.Point, stroke.FeatureSeed);
                var width = stroke.HasParent
                    ? WaterMapDrawingMath.Lerp(
                        parentProfile.Width,
                        targetWidth,
                        progress)
                    : targetWidth;
                var depth = stroke.HasParent
                    ? WaterMapDrawingMath.Lerp(
                        parentProfile.BedDepth,
                        targetDepth,
                        progress)
                    : targetDepth;
                var center = terrain.GetCell(
                    WaterBrushFactory.RoundToMapCell(point.Point.X),
                    WaterBrushFactory.RoundToMapCell(point.Point.Z));
                var rawSurface = center.HasSeaPattern
                    ? seaSurfaceHeight
                    : center.SurfaceHeight - inset;
                if (stroke.HasParent && index == 0)
                {
                    rawSurface = parentProfile.WaterSurface;
                }

                rawSurfaces[index] = rawSurface;
                widths[index] = width;
                bedDepths[index] = depth;
                waterDepthBases[index] = stroke.HasParent
                    ? WaterMapDrawingMath.Lerp(
                        parentProfile.WaterDepthBase,
                        Math.Max(0f, depth - inset),
                        progress)
                    : Math.Max(0f, depth - inset);
            }

            var surfaces = rawSurfaces;
            if (stroke.HasParent)
            {
                surfaces[0] = parentProfile.WaterSurface;
            }

            var result = new RiverWaterProfile[nodes.Length];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = new RiverWaterProfile(
                    nodes[index].Point,
                    nodes[index].DistanceFromStart,
                    surfaces[index],
                    widths[index],
                    bedDepths[index],
                    waterDepthBases[index]);
            }

            return result;
        }

        private float ResolveWidth(
            WaterMapPoint point,
            float progress,
            int strokeSeed) =>
            ResolveProfileRange(
                point,
                progress,
                riverSettings.Width,
                "width",
                strokeSeed);

        private float ResolveDepth(
            WaterMapPoint point,
            float progress,
            int strokeSeed) =>
            ResolveProfileRange(
                point,
                progress,
                riverSettings.Depth,
                "depth",
                strokeSeed);

        private float ResolveProfileRange(
            WaterMapPoint point,
            float progress,
            TerrainRangeData range,
            string channel,
            int strokeSeed)
        {
            var body = WaterMapDrawingMath.Lerp(
                range.Minimum,
                range.Maximum,
                CenterBias(WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(
                        strokeSeed,
                        $"{channel}-body"))));
            var terminus = WaterMapDrawingMath.Lerp(
                range.Minimum,
                range.Maximum,
                LowerBias(WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(
                        strokeSeed,
                        $"{channel}-terminus"))));
            return WaterMapDrawingMath.Lerp(
                body,
                terminus,
                GetTerminusInfluence(progress));
        }

        private float ResolveInset(WaterMapPoint point, int strokeSeed) =>
            WaterMapDrawingMath.Lerp(
                riverSettings.WaterInset.Minimum,
                riverSettings.WaterInset.Maximum,
                WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(strokeSeed, "inset")));

        private static float CenterBias(float value)
        {
            var signed = value * 2f - 1f;
            return 0.5f + 0.5f * signed * signed * signed;
        }

        private static float LowerBias(float value) => value * value;

        private static float GetTerminusInfluence(float progress) =>
            1f - MathF.Sin(Math.Clamp(progress, 0f, 1f) * MathF.PI);

    }

}
