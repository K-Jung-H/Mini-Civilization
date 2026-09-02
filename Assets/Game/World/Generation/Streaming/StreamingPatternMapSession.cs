using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Streaming
{
    internal readonly struct StreamingPatternMapSample
    {
        public StreamingPatternMapSample(
            int worldX,
            int worldZ,
            in WorldPatternResult terrain,
            in WorldPatternResult combined,
            WaterType hydrologyType,
            float hydrologyMembership)
        {
            IsAvailable = true;
            WorldX = worldX;
            WorldZ = worldZ;
            Terrain = terrain;
            Combined = combined;
            HydrologyType = hydrologyType;
            HydrologyMembership = Math.Clamp(hydrologyMembership, 0f, 1f);
        }

        public int WorldX { get; }
        public int WorldZ { get; }
        public bool IsAvailable { get; }
        public WorldPatternResult Terrain { get; }
        public WorldPatternResult Combined { get; }
        public WaterType HydrologyType { get; }
        public float HydrologyMembership { get; }
    }

    internal sealed class StreamingPatternMapSession : IDisposable
    {
        private StreamingFeatureWorld featureWorld;
        private StreamingHydrologyCellQuery hydrology;
        private readonly WorldCellRectangle rectangle;
        private readonly int leaseId;

        internal StreamingPatternMapSession(
            StreamingFeatureWorld featureWorld,
            in WorldCellRectangle rectangle,
            int leaseId)
        {
            if (leaseId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseId));
            }

            this.rectangle = rectangle;
            this.featureWorld = featureWorld ?? throw new ArgumentNullException(
                nameof(featureWorld));
            this.leaseId = leaseId;
            hydrology = new StreamingHydrologyCellQuery(featureWorld);
        }

        public StreamingPatternMapSample Sample(int worldX, int worldZ)
        {
            if (featureWorld == null)
            {
                throw new ObjectDisposedException(nameof(StreamingPatternMapSession));
            }

            return featureWorld.SamplePatternMap(
                hydrology,
                rectangle,
                worldX,
                worldZ);
        }

        public void Dispose()
        {
            featureWorld?.ReleasePatternMapSession(leaseId);
            hydrology = null;
            featureWorld = null;
        }

    }

    internal sealed class StreamingHydrologyCellQuery
    {
        private readonly StreamingFeatureWorld featureWorld;
        private readonly Dictionary<PlanningTileKey, RiverQueryTile> riverTiles =
            new();

        public StreamingHydrologyCellQuery(StreamingFeatureWorld featureWorld)
        {
            this.featureWorld = featureWorld ?? throw new ArgumentNullException(
                nameof(featureWorld));
        }

        public StreamingHydrologyCell Sample(int worldX, int worldZ)
        {
            var topology = featureWorld.SampleTopology(worldX, worldZ);
            var result = new StreamingHydrologyCell(
                topology.TargetTerrainSurfaceUnits,
                topology.WaterTopUnits,
                topology.WaterType,
                topology.BasinComponent,
                topology.Membership,
                topology.InteriorProgress);
            if (result.Membership > 0f)
            {
                return result;
            }

            return GetRiverTile(worldX, worldZ).Sample(
                featureWorld,
                worldX,
                worldZ,
                result);
        }

        private RiverQueryTile GetRiverTile(int worldX, int worldZ)
        {
            var key = PlanningTileKey.FromCell(
                worldX,
                worldZ,
                featureWorld.Settings.Hydrology.Map.PlanningRegionSizeCells);
            if (riverTiles.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var created = RiverQueryTile.Create(featureWorld, key);
            riverTiles.Add(key, created);
            return created;
        }

        private sealed class RiverQueryTile
        {
            private readonly List<StreamingRiverRoutePlan> routes;
            private readonly Dictionary<StreamingCellKey,
                StreamingRiverJunctionPlan> junctions;

            private RiverQueryTile(
                List<StreamingRiverRoutePlan> routes,
                Dictionary<StreamingCellKey, StreamingRiverJunctionPlan> junctions)
            {
                this.routes = routes;
                this.junctions = junctions;
            }

            public static RiverQueryTile Create(
                StreamingFeatureWorld featureWorld,
                in PlanningTileKey key)
            {
                var settings = featureWorld.Settings;
                var extent = checked((int)MathF.Ceiling(
                    settings.Hydrology.RiverCorridor.WidthCells.Maximum * 0.5f
                    + settings.Hydrology.RiverCorridor.BankMarginCells));
                var required = key.ToCore(
                    settings.Hydrology.Map.PlanningRegionSizeCells).Expand(extent);
                var size = settings.Hydrology.Map.PlanningRegionSizeCells;
                var minimumX = WorldCoordinateUtility.FloorDivide(
                    required.MinimumX,
                    size);
                var maximumX = WorldCoordinateUtility.FloorDivide(
                    required.MaximumX,
                    size);
                var minimumZ = WorldCoordinateUtility.FloorDivide(
                    required.MinimumZ,
                    size);
                var maximumZ = WorldCoordinateUtility.FloorDivide(
                    required.MaximumZ,
                    size);
                var routesById = new Dictionary<StreamingRiverEdgeId,
                    StreamingRiverRoutePlan>();
                var junctions = new Dictionary<StreamingCellKey,
                    StreamingRiverJunctionPlan>();
                for (var tileZ = minimumZ; tileZ <= maximumZ; tileZ++)
                for (var tileX = minimumX; tileX <= maximumX; tileX++)
                {
                    var spatial = featureWorld.GetRiverSpatialTile(
                        new PlanningTileKey(tileX, tileZ));
                    for (var index = 0; index < spatial.Routes.Count; index++)
                    {
                        var route = spatial.Routes[index];
                        routesById.TryAdd(route.Id, route);
                    }

                    for (var index = 0; index < spatial.Junctions.Count; index++)
                    {
                        var junction = spatial.Junctions[index];
                        junctions.TryAdd(new StreamingCellKey(
                            junction.WorldX,
                            junction.WorldZ), junction);
                    }
                }

                var routes = new List<StreamingRiverRoutePlan>(
                    routesById.Values);
                routes.Sort((left, right) => left.Id.CompareTo(right.Id));
                return new RiverQueryTile(routes, junctions);
            }

            public StreamingHydrologyCell Sample(
                StreamingFeatureWorld featureWorld,
                int worldX,
                int worldZ,
                in StreamingHydrologyCell topology)
            {
                var hasNearest = false;
                var nearest = default(RiverCellSample);
                for (var routeIndex = 0; routeIndex < routes.Count; routeIndex++)
                {
                    var route = routes[routeIndex];
                    for (var pointIndex = 1;
                         pointIndex < route.Route.Count;
                         pointIndex++)
                    {
                        var candidate = RiverCellSample.Sample(
                            route,
                            route.Route[pointIndex - 1],
                            route.Route[pointIndex],
                            worldX,
                            worldZ);
                        if (candidate.WidthCells <= 0f
                            || candidate.DistanceSquared >= candidate.RadiusSquared
                            || hasNearest
                            && !candidate.IsCloserThan(nearest))
                        {
                            continue;
                        }

                        nearest = candidate;
                        hasNearest = true;
                    }
                }

                if (hasNearest)
                {
                    var river = CreateRiverCell(
                        featureWorld,
                        worldX,
                        worldZ,
                        nearest);
                    if (river.Membership > 0f)
                    {
                        return river;
                    }
                }

                if (!junctions.TryGetValue(
                        new StreamingCellKey(worldX, worldZ),
                        out var junction)
                    || junction.WaterTopUnits <= junction.TargetTerrainSurfaceUnits)
                {
                    return topology;
                }

                return new StreamingHydrologyCell(
                    junction.TargetTerrainSurfaceUnits,
                    junction.WaterTopUnits,
                    WaterType.River,
                    default,
                    1f,
                    1f,
                    junction.Edges[0]);
            }

            private static StreamingHydrologyCell CreateRiverCell(
                StreamingFeatureWorld featureWorld,
                int worldX,
                int worldZ,
                in RiverCellSample sample)
            {
                var settings = featureWorld.Settings;
                var radius = sample.WidthCells * 0.5f;
                var coreProgress = radius > 0f
                    ? 1f - (float)(Math.Sqrt(sample.DistanceSquared) / radius)
                    : 0f;
                var influence = Math.Clamp(settings.Hydrology.RiverCorridor.CrossSection
                    .Evaluate(coreProgress), 0f, 1f);
                if (influence <= 0f)
                {
                    return default;
                }

                var waterTop = Math.Clamp((int)MathF.Round(sample.WaterTopUnits,
                    MidpointRounding.AwayFromZero), 0,
                    checked(settings.WorldHeight * WorldGrid.HeightStepsPerCell));
                var terrain = featureWorld.SampleBaseTerrain(worldX, worldZ);
                var centerDepth = Math.Max(0f, sample.WaterTopUnits
                    - sample.TargetTerrainSurfaceUnits);
                var bedDetail = ToSignedValue(WorldNoiseFieldSampler.Sample2D(
                        worldX,
                        worldZ,
                        settings.Hydrology.RiverCorridor.RiverbedField,
                        sample.Route.RiverbedSeed),
                    settings.Hydrology.RiverCorridor.RiverbedField.Mode)
                    * sample.Route.RiverbedAmplitudeUnits * influence;
                var shapedBed = sample.WaterTopUnits - centerDepth * influence
                    + bedDetail;
                var target = Math.Min(terrain.Surface.SurfaceUnits, shapedBed);
                var targetUnits = Math.Clamp((int)MathF.Floor(target), 0,
                    Math.Max(0, waterTop - 1));
                return waterTop <= targetUnits ? default : new StreamingHydrologyCell(
                    targetUnits,
                    waterTop,
                    WaterType.River,
                    default,
                    influence,
                    influence,
                    sample.Route.Id);
            }

            private static float ToSignedValue(float value, WorldNoiseMode mode) =>
                mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge
                    ? value
                    : value * 2f - 1f;
        }

        private readonly struct RiverCellSample
        {
            private RiverCellSample(
                StreamingRiverRoutePlan route,
                double distanceSquared,
                float waterTopUnits,
                float targetTerrainSurfaceUnits,
                float widthCells)
            {
                Route = route;
                DistanceSquared = distanceSquared;
                WaterTopUnits = waterTopUnits;
                TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
                WidthCells = widthCells;
            }

            public StreamingRiverRoutePlan Route { get; }
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

            public bool IsCloserThan(in RiverCellSample other)
            {
                var difference = DistanceSquared - other.DistanceSquared;
                return difference < -1e-9
                    || Math.Abs(difference) <= 1e-9
                    && Route.Id.CompareTo(other.Route.Id) < 0;
            }

            public static RiverCellSample Sample(
                StreamingRiverRoutePlan route,
                in StreamingRiverRoutePoint from,
                in StreamingRiverRoutePoint to,
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
                return new RiverCellSample(
                    route,
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
}
