using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    internal readonly struct WaterMapPoint
    {
        public WaterMapPoint(float x, float z)
        {
            X = x;
            Z = z;
        }

        public float X { get; }
        public float Z { get; }
    }

    internal readonly struct RiverBoneNode
    {
        public RiverBoneNode(
            WaterMapPoint point, float distanceFromStart, WaterMapPoint direction)
        {
            Point = point;
            DistanceFromStart = distanceFromStart;
            Direction = direction;
        }

        public WaterMapPoint Point { get; }
        public float DistanceFromStart { get; }
        public WaterMapPoint Direction { get; }
    }

    internal sealed class RiverBoneTree
    {
        private readonly RiverBoneStroke[] strokes;

        public RiverBoneTree(RiverBoneStroke[] strokes)
        {
            if (strokes == null || strokes.Length == 0)
            {
                throw new ArgumentException(
                    "River bone tree requires at least one stroke.",
                    nameof(strokes));
            }

            this.strokes = strokes;
        }

        public IReadOnlyList<RiverBoneStroke> Strokes => strokes;
    }

    internal readonly struct RiverBoneStroke
    {
        public RiverBoneStroke(
            RiverBoneNode[] nodes,
            int featureSeed,
            int parentStrokeIndex,
            int parentNodeIndex,
            float widthScale,
            float depthScale)
        {
            if (nodes == null || nodes.Length < 2)
            {
                throw new ArgumentException(
                    "River bone stroke requires at least two nodes.",
                    nameof(nodes));
            }

            if (parentStrokeIndex < -1 || parentNodeIndex < -1
                || (parentStrokeIndex < 0) != (parentNodeIndex < 0)
                || !float.IsFinite(widthScale) || widthScale <= 0f
                || !float.IsFinite(depthScale) || depthScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(parentStrokeIndex));
            }

            Nodes = nodes;
            FeatureSeed = featureSeed;
            ParentStrokeIndex = parentStrokeIndex;
            ParentNodeIndex = parentNodeIndex;
            WidthScale = widthScale;
            DepthScale = depthScale;
        }

        public RiverBoneNode[] Nodes { get; }
        public int FeatureSeed { get; }
        public int ParentStrokeIndex { get; }
        public int ParentNodeIndex { get; }
        public float WidthScale { get; }
        public float DepthScale { get; }
        public bool HasParent => ParentStrokeIndex >= 0;
        public float TotalDistance => Nodes[^1].DistanceFromStart;
    }

    internal readonly struct RiverWaterProfile
    {
        public RiverWaterProfile(
            WaterMapPoint point,
            float distanceFromStart,
            float waterSurface,
            float width,
            float bedDepth,
            float waterDepthBase)
        {
            Point = point;
            DistanceFromStart = distanceFromStart;
            WaterSurface = waterSurface;
            Width = width;
            BedDepth = bedDepth;
            WaterDepthBase = waterDepthBase;
        }

        public WaterMapPoint Point { get; }
        public float DistanceFromStart { get; }
        public float WaterSurface { get; }
        public float Width { get; }
        public float BedDepth { get; }
        public float WaterDepthBase { get; }

        public static RiverWaterProfile Lerp(
            RiverWaterProfile from,
            RiverWaterProfile to,
            float amount) => new(
            new WaterMapPoint(
                WaterMapDrawingMath.Lerp(from.Point.X, to.Point.X, amount),
                WaterMapDrawingMath.Lerp(from.Point.Z, to.Point.Z, amount)),
            WaterMapDrawingMath.Lerp(
                from.DistanceFromStart,
                to.DistanceFromStart,
                amount),
            WaterMapDrawingMath.Lerp(from.WaterSurface, to.WaterSurface, amount),
            WaterMapDrawingMath.Lerp(from.Width, to.Width, amount),
            WaterMapDrawingMath.Lerp(from.BedDepth, to.BedDepth, amount),
            WaterMapDrawingMath.Lerp(
                from.WaterDepthBase,
                to.WaterDepthBase,
                amount));
    }

    internal sealed class RiverSegmentSpatialIndex
    {
        private static readonly IReadOnlyList<RiverSegmentReference> Empty =
            Array.Empty<RiverSegmentReference>();

        private readonly int bucketSize;
        private readonly Dictionary<long, List<RiverSegmentReference>> buckets = new();

        public RiverSegmentSpatialIndex(
            RiverBoneTree boneTree,
            float maximumHalfWidth)
        {
            if (!float.IsFinite(maximumHalfWidth) || maximumHalfWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumHalfWidth));
            }

            bucketSize = Math.Max(1, checked((int)MathF.Ceiling(
                maximumHalfWidth * 2f)));
            for (var strokeIndex = 0;
                 strokeIndex < boneTree.Strokes.Count;
                 strokeIndex++)
            {
                var nodes = boneTree.Strokes[strokeIndex].Nodes;
                for (var nodeIndex = 0; nodeIndex < nodes.Length - 1; nodeIndex++)
                {
                    Add(new RiverSegmentReference(strokeIndex, nodeIndex),
                        nodes[nodeIndex].Point,
                        nodes[nodeIndex + 1].Point,
                        maximumHalfWidth);
                }
            }
        }

        public IReadOnlyList<RiverSegmentReference> GetCandidates(int x, int z) =>
            buckets.TryGetValue(CreateBucketKey(
                ToBucket(x),
                ToBucket(z)), out var candidates)
                ? candidates
                : Empty;

        private void Add(
            RiverSegmentReference segment,
            WaterMapPoint from,
            WaterMapPoint to,
            float extent)
        {
            var minimumX = ToBucket(MathF.Min(from.X, to.X) - extent);
            var maximumX = ToBucket(MathF.Max(from.X, to.X) + extent);
            var minimumZ = ToBucket(MathF.Min(from.Z, to.Z) - extent);
            var maximumZ = ToBucket(MathF.Max(from.Z, to.Z) + extent);
            for (var bucketZ = minimumZ; bucketZ <= maximumZ; bucketZ++)
            for (var bucketX = minimumX; bucketX <= maximumX; bucketX++)
            {
                var key = CreateBucketKey(bucketX, bucketZ);
                if (!buckets.TryGetValue(key, out var segments))
                {
                    segments = new List<RiverSegmentReference>();
                    buckets.Add(key, segments);
                }

                segments.Add(segment);
            }
        }

        private int ToBucket(float coordinate) => checked((int)MathF.Floor(
            coordinate / bucketSize));

        private static long CreateBucketKey(int x, int z) =>
            ((long)x << 32) ^ (uint)z;
    }

    internal readonly struct RiverSegmentReference
    {
        public RiverSegmentReference(int strokeIndex, int nodeIndex)
        {
            StrokeIndex = strokeIndex;
            NodeIndex = nodeIndex;
        }

        public int StrokeIndex { get; }
        public int NodeIndex { get; }
    }

    internal readonly struct RiverNearestSegment
    {
        private RiverNearestSegment(
            int strokeIndex,
            int nodeIndex,
            float distance,
            float progress)
        {
            StrokeIndex = strokeIndex;
            NodeIndex = nodeIndex;
            Distance = distance;
            Progress = progress;
        }

        public static RiverNearestSegment None => new(
            -1,
            -1,
            float.PositiveInfinity,
            0f);

        public int StrokeIndex { get; }
        public int NodeIndex { get; }
        public float Distance { get; }
        public float Progress { get; }

        public static RiverNearestSegment Create(
            RiverSegmentReference segment,
            WaterMapPoint point,
            WaterMapPoint from,
            WaterMapPoint to)
        {
            var deltaX = to.X - from.X;
            var deltaZ = to.Z - from.Z;
            var lengthSquared = deltaX * deltaX + deltaZ * deltaZ;
            var progress = lengthSquared <= 0f
                ? 0f
                : Math.Clamp(
                    ((point.X - from.X) * deltaX
                        + (point.Z - from.Z) * deltaZ) / lengthSquared,
                    0f,
                    1f);
            var closestX = from.X + deltaX * progress;
            var closestZ = from.Z + deltaZ * progress;
            var distanceX = point.X - closestX;
            var distanceZ = point.Z - closestZ;
            return new RiverNearestSegment(
                segment.StrokeIndex,
                segment.NodeIndex,
                MathF.Sqrt(distanceX * distanceX + distanceZ * distanceZ),
                progress);
        }
    }

    internal static class WaterMapDrawingMath
    {
        public static HydrologyFeatureKey CreateKey(
            HydrologyFeatureKind kind,
            int ownerX,
            int ownerZ,
            int seed) => HydrologyFeatureKey.FromIdentity(
            new WaterFeatureIdentity(
                kind,
                ownerX,
                ownerZ,
                seed,
                unchecked((uint)seed)));

        public static float Distance(WaterMapPoint left, WaterMapPoint right)
        {
            var x = right.X - left.X;
            var z = right.Z - left.Z;
            return MathF.Sqrt(x * x + z * z);
        }

        public static float Lerp(float from, float to, float progress) =>
            from + (to - from) * progress;

        public static int DeriveSeed(int seed, string path) =>
            PatternNoise.DeriveSeed(seed, path);

        public static float Value01(long x, long z, int seed) =>
            PatternNoise.Value01(x, z, seed);

        public static float SignedValue01(long x, long z, int seed) =>
            PatternNoise.SignedValue01(x, z, seed);

        public static float SampleNormalized(
            double x,
            double z,
            TerrainNoiseFieldData field,
            int seed) => PatternNoise.SampleNormalized(x, z, field, seed);

        public static float SampleSigned(
            double x,
            double z,
            TerrainNoiseFieldData field,
            int seed) => PatternNoise.SampleSigned(x, z, field, seed);

    }
}
