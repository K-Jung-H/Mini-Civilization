using System;
using System.Collections.Generic;
using System.Diagnostics;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    /// <summary>
    /// Materializes a requested rectangle from an explicitly owned Hydrology plan scope.
    /// The batch owns only its raster cells; the caller owns the scope that preserves
    /// topology and graph plans while the batch is being built.
    /// </summary>
    internal static class HydrologyBatchBuilder
    {
        public static HydrologyBatch Build(
            WorldHydrology hydrology,
            HydrologyPlanScope scope,
            int originX,
            int originZ,
            int width,
            int height)
        {
            ValidateRequest(hydrology, scope, width, height);
            var cells = new HydrologyCellPlan[checked(width * height)];
            var topologyStarted = Stopwatch.GetTimestamp();
            try
            {
                RasterizeTopology(
                    hydrology,
                    scope,
                    originX,
                    originZ,
                    width,
                    height,
                    cells);
            }
            finally
            {
                hydrology.Metrics.RecordTopologyRaster(
                    Stopwatch.GetTimestamp() - topologyStarted);
            }

            var riverStarted = Stopwatch.GetTimestamp();
            try
            {
                RasterizeRivers(
                    hydrology,
                    scope,
                    originX,
                    originZ,
                    width,
                    height,
                    cells);
            }
            finally
            {
                hydrology.Metrics.RecordRiverRaster(
                    Stopwatch.GetTimestamp() - riverStarted);
            }
            return new HydrologyBatch(
                hydrology,
                originX,
                originZ,
                width,
                height,
                cells);
        }

        public static HydrologyCellPlan Sample(
            WorldHydrology hydrology,
            HydrologyPlanScope scope,
            int worldX,
            int worldZ)
        {
            using var batch = Build(hydrology, scope, worldX, worldZ, 1, 1);
            return batch.Sample(worldX, worldZ);
        }

        private static void RasterizeTopology(
            WorldHydrology hydrology,
            HydrologyPlanScope scope,
            int originX,
            int originZ,
            int width,
            int height,
            HydrologyCellPlan[] cells)
        {
            for (var localZ = 0; localZ < height; localZ++)
            for (var localX = 0; localX < width; localX++)
            {
                var worldX = checked(originX + localX);
                var worldZ = checked(originZ + localZ);
                var key = hydrology.GetTopologyRegionKey(worldX, worldZ);
                cells[localX + width * localZ] = scope
                    .GetTopologyRegion(key)
                    .Sample(worldX, worldZ);
            }
        }

        private static void RasterizeRivers(
            WorldHydrology hydrology,
            HydrologyPlanScope scope,
            int originX,
            int originZ,
            int width,
            int height,
            HydrologyCellPlan[] cells)
        {
            var edges = new Dictionary<HydrologyGraphEdgeId, RiverEdgePlan>();
            var junctions = new Dictionary<(int x, int z), RiverJunctionPlan>();
            CollectSpatialPlans(
                hydrology,
                scope,
                originX,
                originZ,
                width,
                height,
                edges,
                junctions);
            if (edges.Count == 0 && junctions.Count == 0)
            {
                return;
            }

            var nearest = new RiverRasterSample[cells.Length];
            var hasNearest = new bool[cells.Length];
            foreach (var pair in edges)
            {
                RasterizeEdge(
                    pair.Value,
                    originX,
                    originZ,
                    width,
                    height,
                    cells,
                    nearest,
                    hasNearest);
            }

            for (var index = 0; index < cells.Length; index++)
            {
                if (cells[index].Membership > 0f || !hasNearest[index])
                {
                    continue;
                }

                var localX = index % width;
                var localZ = index / width;
                cells[index] = CreateRiverCell(
                    hydrology,
                    checked(originX + localX),
                    checked(originZ + localZ),
                    nearest[index]);
            }

            foreach (var pair in junctions)
            {
                var localX = pair.Key.x - originX;
                var localZ = pair.Key.z - originZ;
                if ((uint)localX >= width || (uint)localZ >= height)
                {
                    continue;
                }

                var index = localX + width * localZ;
                if (cells[index].Membership > 0f)
                {
                    continue;
                }

                var junction = pair.Value;
                if (junction.WaterTopUnits <= junction.TargetTerrainSurfaceUnits)
                {
                    continue;
                }

                cells[index] = new HydrologyCellPlan(
                    junction.TargetTerrainSurfaceUnits,
                    junction.WaterTopUnits,
                    WaterType.River,
                    default,
                    1f,
                    1f,
                    junction.Edges[0]);
            }
        }

        private static void CollectSpatialPlans(
            WorldHydrology hydrology,
            HydrologyPlanScope scope,
            int originX,
            int originZ,
            int width,
            int height,
            IDictionary<HydrologyGraphEdgeId, RiverEdgePlan> edges,
            IDictionary<(int x, int z), RiverJunctionPlan> junctions)
        {
            var corridor = hydrology.Settings.Hydrology.RiverCorridor;
            var padding = checked((int)MathF.Ceiling(
                corridor.WidthCells.Maximum * 0.5f
                + corridor.BankMarginCells));
            var minimum = hydrology.GetTopologyRegionKey(
                checked(originX - padding),
                checked(originZ - padding));
            var maximum = hydrology.GetTopologyRegionKey(
                checked(originX + width - 1 + padding),
                checked(originZ + height - 1 + padding));
            for (var regionZ = minimum.Z; regionZ <= maximum.Z; regionZ++)
            for (var regionX = minimum.X; regionX <= maximum.X; regionX++)
            {
                var region = scope.GetRiverGraphSpatialIndexRegion(
                    new TopologyRegionKey(regionX, regionZ));
                for (var index = 0; index < region.Edges.Count; index++)
                {
                    var edge = region.Edges[index];
                    edges.TryAdd(edge.Id, edge);
                }

                for (var index = 0; index < region.Junctions.Count; index++)
                {
                    var junction = region.Junctions[index];
                    junctions.TryAdd((junction.WorldX, junction.WorldZ), junction);
                }
            }
        }

        private static void RasterizeEdge(
            RiverEdgePlan edge,
            int originX,
            int originZ,
            int width,
            int height,
            IReadOnlyList<HydrologyCellPlan> topology,
            RiverRasterSample[] nearest,
            bool[] hasNearest)
        {
            if (!edge.IsActive)
            {
                return;
            }

            for (var routeIndex = 1; routeIndex < edge.Route.Count; routeIndex++)
            {
                var from = edge.Route[routeIndex - 1];
                var to = edge.Route[routeIndex];
                var maximumRadius = Math.Max(from.WidthCells, to.WidthCells) * 0.5f;
                var minimumX = Math.Max(0, (int)Math.Ceiling(
                    Math.Min(from.WorldX, to.WorldX) - maximumRadius - originX));
                var maximumX = Math.Min(width - 1, (int)Math.Floor(
                    Math.Max(from.WorldX, to.WorldX) + maximumRadius - originX));
                var minimumZ = Math.Max(0, (int)Math.Ceiling(
                    Math.Min(from.WorldZ, to.WorldZ) - maximumRadius - originZ));
                var maximumZ = Math.Min(height - 1, (int)Math.Floor(
                    Math.Max(from.WorldZ, to.WorldZ) + maximumRadius - originZ));
                for (var localZ = minimumZ; localZ <= maximumZ; localZ++)
                for (var localX = minimumX; localX <= maximumX; localX++)
                {
                    var cellIndex = localX + width * localZ;
                    if (topology[cellIndex].Membership > 0f)
                    {
                        continue;
                    }

                    var sample = RiverRasterSample.Sample(
                        edge,
                        from,
                        to,
                        checked(originX + localX),
                        checked(originZ + localZ));
                    if (sample.WidthCells <= 0f
                        || sample.DistanceSquared >= sample.RadiusSquared
                        || hasNearest[cellIndex]
                        && !sample.IsCloserThan(nearest[cellIndex]))
                    {
                        continue;
                    }

                    nearest[cellIndex] = sample;
                    hasNearest[cellIndex] = true;
                }
            }
        }

        private static HydrologyCellPlan CreateRiverCell(
            WorldHydrology hydrology,
            int worldX,
            int worldZ,
            in RiverRasterSample sample)
        {
            var radius = sample.WidthCells * 0.5f;
            var coreProgress = radius > 0f
                ? 1f - (float)(Math.Sqrt(sample.DistanceSquared) / radius)
                : 0f;
            var influence = Math.Clamp(
                hydrology.Settings.Hydrology.RiverCorridor.CrossSection
                    .Evaluate(coreProgress),
                0f,
                1f);
            if (influence <= 0f)
            {
                return default;
            }

            var waterTop = Math.Clamp(
                (int)MathF.Round(sample.WaterTopUnits,
                    MidpointRounding.AwayFromZero),
                0,
                checked(hydrology.Settings.WorldHeight
                    * WorldGrid.HeightStepsPerCell));
            var terrain = hydrology.SampleBaseTerrain(worldX, worldZ);
            var centerDepth = Math.Max(
                0f,
                sample.WaterTopUnits - sample.TargetTerrainSurfaceUnits);
            var bedDetail = ToSignedValue(
                    WorldNoiseFieldSampler.Sample2D(
                        worldX,
                        worldZ,
                        hydrology.Settings.Hydrology.RiverCorridor.RiverbedField,
                        sample.Edge.RiverbedSeed),
                    hydrology.Settings.Hydrology.RiverCorridor.RiverbedField.Mode)
                * sample.Edge.RiverbedAmplitudeUnits
                * influence;
            var shapedBed = sample.WaterTopUnits - centerDepth * influence
                + bedDetail;
            var target = Math.Min(terrain.Surface.SurfaceUnits, shapedBed);
            var targetUnits = Math.Clamp(
                (int)MathF.Floor(target),
                0,
                Math.Max(0, waterTop - 1));
            if (waterTop <= targetUnits)
            {
                return default;
            }

            return new HydrologyCellPlan(
                targetUnits,
                waterTop,
                WaterType.River,
                default,
                influence,
                influence,
                sample.Edge.Id);
        }

        private static float ToSignedValue(float value, WorldNoiseMode mode) =>
            mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge
                ? value
                : value * 2f - 1f;

        private static void ValidateRequest(
            WorldHydrology hydrology,
            HydrologyPlanScope scope,
            int width,
            int height)
        {
            if (hydrology == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
        }

        private readonly struct RiverRasterSample
        {
            private RiverRasterSample(
                RiverEdgePlan edge,
                double distanceSquared,
                float waterTopUnits,
                float targetTerrainSurfaceUnits,
                float widthCells)
            {
                Edge = edge;
                DistanceSquared = distanceSquared;
                WaterTopUnits = waterTopUnits;
                TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
                WidthCells = widthCells;
            }

            public RiverEdgePlan Edge { get; }
            public double DistanceSquared { get; }
            public float WaterTopUnits { get; }
            public float TargetTerrainSurfaceUnits { get; }
            public float WidthCells { get; }
            public double RadiusSquared
            {
                get
                {
                    var radius = WidthCells * 0.5f;
                    return radius * radius;
                }
            }

            public bool IsCloserThan(in RiverRasterSample other)
            {
                var difference = DistanceSquared - other.DistanceSquared;
                return difference < -1e-9
                    || Math.Abs(difference) <= 1e-9
                    && Edge.Id.CompareTo(other.Edge.Id) < 0;
            }

            public static RiverRasterSample Sample(
                RiverEdgePlan edge,
                in RiverRoutePoint from,
                in RiverRoutePoint to,
                int worldX,
                int worldZ)
            {
                var deltaX = to.WorldX - from.WorldX;
                var deltaZ = to.WorldZ - from.WorldZ;
                var lengthSquared = deltaX * deltaX + deltaZ * deltaZ;
                var progress = lengthSquared > 0d
                    ? Math.Clamp(
                        ((worldX - from.WorldX) * deltaX
                        + (worldZ - from.WorldZ) * deltaZ) / lengthSquared,
                        0d,
                        1d)
                    : 0d;
                var nearestX = from.WorldX + deltaX * progress;
                var nearestZ = from.WorldZ + deltaZ * progress;
                var distanceX = worldX - nearestX;
                var distanceZ = worldZ - nearestZ;
                return new RiverRasterSample(
                    edge,
                    distanceX * distanceX + distanceZ * distanceZ,
                    Lerp(from.WaterTopUnits, to.WaterTopUnits, (float)progress),
                    Lerp(
                        from.TargetTerrainSurfaceUnits,
                        to.TargetTerrainSurfaceUnits,
                        (float)progress),
                    Lerp(from.WidthCells, to.WidthCells, (float)progress));
            }

            private static float Lerp(float from, float to, float amount) =>
                from + (to - from) * amount;
        }
    }

    internal sealed class HydrologyBatch : IDisposable
    {
        private HydrologyCellPlan[] cells;

        internal HydrologyBatch(
            WorldHydrology hydrology,
            int originX,
            int originZ,
            int width,
            int height,
            HydrologyCellPlan[] cells)
        {
            Hydrology = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
            OriginX = originX;
            OriginZ = originZ;
            Width = width;
            Height = height;
            this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public WorldHydrology Hydrology { get; }
        public int OriginX { get; }
        public int OriginZ { get; }
        public int Width { get; }
        public int Height { get; }

        public BaseTerrainSample SampleBaseTerrainState(int worldX, int worldZ)
        {
            EnsureNotDisposed();
            return Hydrology.SampleBaseTerrain(worldX, worldZ);
        }

        public TerrainSurfaceSample SampleBaseTerrain(int worldX, int worldZ) =>
            SampleBaseTerrainState(worldX, worldZ).Surface;

        public HydrologyCellPlan Sample(int worldX, int worldZ)
        {
            EnsureNotDisposed();
            var localX = worldX - OriginX;
            var localZ = worldZ - OriginZ;
            if ((uint)localX >= Width || (uint)localZ >= Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldX),
                    "Hydrology cell is outside this Batch.");
            }

            return cells[localX + Width * localZ];
        }

        public void Dispose() => cells = null;

        private void EnsureNotDisposed()
        {
            if (cells == null)
            {
                throw new ObjectDisposedException(nameof(HydrologyBatch));
            }
        }
    }
}
