using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal interface IWorldHydrologyRaster : IDisposable
    {
        Streaming.StreamingBaseTerrainFact SampleBaseTerrain(
            int worldX,
            int worldZ);

        WorldPatternResult Compose(
            WorldSettingsData settings,
            int worldX,
            int worldZ,
            in WorldPatternResult terrain);
    }
}

namespace MiniCivilization.World.Generation.Streaming
{
    internal readonly struct StreamingHydrologyCell
    {
        public StreamingHydrologyCell(
            int targetTerrainSurfaceUnits,
            int waterTopUnits,
            WaterType waterType,
            StreamingBasinComponentId basinComponent,
            float membership,
            float interiorProgress,
            StreamingRiverEdgeId? riverEdgeId = null)
        {
            if (targetTerrainSurfaceUnits < 0 || waterTopUnits < 0
                || !float.IsFinite(membership)
                || !float.IsFinite(interiorProgress))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetTerrainSurfaceUnits));
            }

            var hasWater = waterTopUnits > targetTerrainSurfaceUnits;
            if (hasWater != (waterType != WaterType.None))
            {
                throw new ArgumentException(
                    "Water height and WaterType must describe the same cell fact.");
            }

            if (waterType is WaterType.Lake or WaterType.Pond)
            {
                if (!basinComponent.IsValid || basinComponent.Type != waterType)
                {
                    throw new ArgumentException(
                        "Basin water must identify the matching Basin component.",
                        nameof(basinComponent));
                }
            }
            else if (waterType == WaterType.Sea && basinComponent.IsValid)
            {
                throw new ArgumentException(
                    "Sea cells cannot identify a Basin component.",
                    nameof(basinComponent));
            }

            TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            WaterTopUnits = waterTopUnits;
            WaterType = waterType;
            BasinComponent = basinComponent;
            Membership = Math.Clamp(membership, 0f, 1f);
            InteriorProgress = Math.Clamp(interiorProgress, 0f, 1f);
            RiverEdgeId = riverEdgeId;
        }

        public int TargetTerrainSurfaceUnits { get; }
        public int WaterTopUnits { get; }
        public WaterType WaterType { get; }
        public StreamingBasinComponentId BasinComponent { get; }
        public float Membership { get; }
        public float InteriorProgress { get; }
        public StreamingRiverEdgeId? RiverEdgeId { get; }
        public bool HasWater => WaterTopUnits > TargetTerrainSurfaceUnits;
    }

    internal static class StreamingHydrologyPlanIdentity
    {
        public static int ToDebugComponentId(in StreamingHydrologyCell cell)
        {
            if (cell.BasinComponent.IsValid)
            {
                return unchecked((int)DeterministicNoise.Hash(
                    cell.BasinComponent.SeedGridX,
                    cell.BasinComponent.SeedGridZ,
                    (int)cell.BasinComponent.Type));
            }

            return cell.RiverEdgeId.HasValue
                ? unchecked((int)DeterministicNoise.Hash(
                    EndpointHash(cell.RiverEdgeId.Value.First),
                    EndpointHash(cell.RiverEdgeId.Value.Second),
                    (int)WaterType.River))
                : 0;
        }

        private static int EndpointHash(in StreamingEndpointId endpoint)
        {
            var hash = unchecked((int)DeterministicNoise.Hash(
                endpoint.WorldX,
                endpoint.WorldZ,
                (int)endpoint.Kind));
            return endpoint.BasinComponent.IsValid
                ? unchecked((int)DeterministicNoise.Hash(
                    hash,
                    endpoint.BasinComponent.SeedGridX,
                    endpoint.BasinComponent.SeedGridZ))
                : hash;
        }
    }

    internal sealed class StreamingHydrologyRaster : IWorldHydrologyRaster
    {
        private StreamingHydrologyCell[] cells;
        private readonly StreamingFeatureWorld featureWorld;

        private StreamingHydrologyRaster(
            StreamingFeatureWorld featureWorld,
            WorldCellRectangle rectangle,
            StreamingHydrologyCell[] cells)
        {
            this.featureWorld = featureWorld ?? throw new ArgumentNullException(
                nameof(featureWorld));
            Rectangle = rectangle;
            this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public StreamingFeatureWorld FeatureWorld => featureWorld;
        public WorldCellRectangle Rectangle { get; }
        public int Width => Rectangle.MaximumXExclusive - Rectangle.MinimumX;
        public int Height => Rectangle.MaximumZExclusive - Rectangle.MinimumZ;

        internal static StreamingHydrologyRaster BuildFromFeatures(
            StreamingFeatureWorld featureWorld,
            WorldCellRectangle rectangle)
        {
            if (featureWorld == null)
            {
                throw new ArgumentNullException(nameof(featureWorld));
            }

            var cells = new StreamingHydrologyCell[checked(
                (rectangle.MaximumXExclusive - rectangle.MinimumX)
                * (rectangle.MaximumZExclusive - rectangle.MinimumZ))];
            var width = rectangle.MaximumXExclusive - rectangle.MinimumX;
            var height = rectangle.MaximumZExclusive - rectangle.MinimumZ;
            for (var localZ = 0; localZ < height; localZ++)
            for (var localX = 0; localX < width; localX++)
            {
                var worldX = checked(rectangle.MinimumX + localX);
                var worldZ = checked(rectangle.MinimumZ + localZ);
                var topology = featureWorld.SampleTopology(worldX, worldZ);
                cells[localX + width * localZ] = new StreamingHydrologyCell(
                    topology.TargetTerrainSurfaceUnits,
                    topology.WaterTopUnits,
                    topology.WaterType,
                    topology.BasinComponent,
                    topology.Membership,
                    topology.InteriorProgress);
            }

            RasterizeRivers(featureWorld, rectangle, cells);
            return new StreamingHydrologyRaster(featureWorld, rectangle, cells);
        }

        public StreamingBaseTerrainFact SampleBaseTerrain(int worldX, int worldZ)
        {
            EnsureNotDisposed();
            EnsureContains(worldX, worldZ);
            return featureWorld.SampleBaseTerrain(worldX, worldZ);
        }

        public StreamingHydrologyCell Sample(int worldX, int worldZ)
        {
            EnsureNotDisposed();
            var localX = worldX - Rectangle.MinimumX;
            var localZ = worldZ - Rectangle.MinimumZ;
            if ((uint)localX >= Width || (uint)localZ >= Height)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldX),
                    "Hydrology cell is outside this Raster.");
            }

            return cells[localX + Width * localZ];
        }

        public WorldPatternResult Compose(
            WorldSettingsData settings,
            int worldX,
            int worldZ,
            in WorldPatternResult terrain)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (!ReferenceEquals(settings, featureWorld.Settings))
            {
                throw new ArgumentException(
                    "The Raster and Chunk materializer must use the same World settings.",
                    nameof(settings));
            }

            return Compose(settings, terrain, Sample(worldX, worldZ));
        }

        public void Dispose() => cells = null;

        private static void RasterizeRivers(
            StreamingFeatureWorld featureWorld,
            in WorldCellRectangle rectangle,
            StreamingHydrologyCell[] cells)
        {
            var settings = featureWorld.Settings;
            var routes = new Dictionary<StreamingRiverEdgeId,
                StreamingRiverRoutePlan>();
            var junctions = new Dictionary<StreamingCellKey,
                StreamingRiverJunctionPlan>();
            var required = rectangle.Expand((int)MathF.Ceiling(
                settings.Hydrology.RiverCorridor.WidthCells.Maximum * 0.5f
                + settings.Hydrology.RiverCorridor.BankMarginCells));
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var minimumX = WorldCoordinateUtility.FloorDivide(required.MinimumX, size);
            var maximumX = WorldCoordinateUtility.FloorDivide(required.MaximumX, size);
            var minimumZ = WorldCoordinateUtility.FloorDivide(required.MinimumZ, size);
            var maximumZ = WorldCoordinateUtility.FloorDivide(required.MaximumZ, size);
            for (var tileZ = minimumZ; tileZ <= maximumZ; tileZ++)
            for (var tileX = minimumX; tileX <= maximumX; tileX++)
            {
                var tile = featureWorld.GetRiverSpatialTile(
                    new PlanningTileKey(tileX, tileZ));
                for (var index = 0; index < tile.Routes.Count; index++)
                {
                    var route = tile.Routes[index];
                    routes.TryAdd(route.Id, route);
                }

                for (var index = 0; index < tile.Junctions.Count; index++)
                {
                    var junction = tile.Junctions[index];
                    junctions.TryAdd(new StreamingCellKey(junction.WorldX,
                        junction.WorldZ), junction);
                }
            }

            if (routes.Count == 0 && junctions.Count == 0)
            {
                return;
            }

            var nearest = new StreamingRiverRasterSample[cells.Length];
            var hasNearest = new bool[cells.Length];
            foreach (var route in routes.Values)
            {
                RasterizeRoute(route, rectangle, cells, nearest, hasNearest);
            }

            var width = rectangle.MaximumXExclusive - rectangle.MinimumX;
            for (var index = 0; index < cells.Length; index++)
            {
                if (cells[index].Membership > 0f || !hasNearest[index])
                {
                    continue;
                }

                var localX = index % width;
                var localZ = index / width;
                cells[index] = CreateRiverCell(featureWorld,
                    checked(rectangle.MinimumX + localX),
                    checked(rectangle.MinimumZ + localZ),
                    nearest[index]);
            }

            foreach (var junction in junctions.Values)
            {
                var localX = junction.WorldX - rectangle.MinimumX;
                var localZ = junction.WorldZ - rectangle.MinimumZ;
                if ((uint)localX >= width
                    || (uint)localZ >= rectangle.MaximumZExclusive - rectangle.MinimumZ)
                {
                    continue;
                }

                var index = localX + width * localZ;
                if (cells[index].Membership > 0f
                    || junction.WaterTopUnits <= junction.TargetTerrainSurfaceUnits)
                {
                    continue;
                }

                cells[index] = new StreamingHydrologyCell(
                    junction.TargetTerrainSurfaceUnits,
                    junction.WaterTopUnits,
                    WaterType.River,
                    default,
                    1f,
                    1f,
                    junction.Edges[0]);
            }
        }

        private static void RasterizeRoute(
            StreamingRiverRoutePlan route,
            in WorldCellRectangle rectangle,
            IReadOnlyList<StreamingHydrologyCell> topology,
            StreamingRiverRasterSample[] nearest,
            bool[] hasNearest)
        {
            var width = rectangle.MaximumXExclusive - rectangle.MinimumX;
            var height = rectangle.MaximumZExclusive - rectangle.MinimumZ;
            for (var routeIndex = 1; routeIndex < route.Route.Count; routeIndex++)
            {
                var from = route.Route[routeIndex - 1];
                var to = route.Route[routeIndex];
                var maximumRadius = Math.Max(from.WidthCells, to.WidthCells) * 0.5f;
                var minimumX = Math.Max(0, (int)Math.Ceiling(
                    Math.Min(from.WorldX, to.WorldX) - maximumRadius
                    - rectangle.MinimumX));
                var maximumX = Math.Min(width - 1, (int)Math.Floor(
                    Math.Max(from.WorldX, to.WorldX) + maximumRadius
                    - rectangle.MinimumX));
                var minimumZ = Math.Max(0, (int)Math.Ceiling(
                    Math.Min(from.WorldZ, to.WorldZ) - maximumRadius
                    - rectangle.MinimumZ));
                var maximumZ = Math.Min(height - 1, (int)Math.Floor(
                    Math.Max(from.WorldZ, to.WorldZ) + maximumRadius
                    - rectangle.MinimumZ));
                for (var localZ = minimumZ; localZ <= maximumZ; localZ++)
                for (var localX = minimumX; localX <= maximumX; localX++)
                {
                    var index = localX + width * localZ;
                    if (topology[index].Membership > 0f)
                    {
                        continue;
                    }

                    var sample = StreamingRiverRasterSample.Sample(
                        route,
                        from,
                        to,
                        checked(rectangle.MinimumX + localX),
                        checked(rectangle.MinimumZ + localZ));
                    if (sample.WidthCells <= 0f
                        || sample.DistanceSquared >= sample.RadiusSquared
                        || hasNearest[index]
                        && !sample.IsCloserThan(nearest[index]))
                    {
                        continue;
                    }

                    nearest[index] = sample;
                    hasNearest[index] = true;
                }
            }
        }

        private static StreamingHydrologyCell CreateRiverCell(
            StreamingFeatureWorld featureWorld,
            int worldX,
            int worldZ,
            in StreamingRiverRasterSample sample)
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
            var shapedBed = sample.WaterTopUnits - centerDepth * influence + bedDetail;
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

        private static WorldPatternResult Compose(
            WorldSettingsData settings,
            in WorldPatternResult terrain,
            in StreamingHydrologyCell hydrology)
        {
            if (hydrology.Membership <= 0f)
            {
                return terrain;
            }

            var hydrologyType = hydrology.WaterType != WaterType.None
                ? hydrology.WaterType
                : hydrology.BasinComponent.IsValid
                    ? hydrology.BasinComponent.Type
                    : WaterType.None;
            var riverDepth = hydrology.WaterType == WaterType.River
                ? Math.Max(0f,
                    hydrology.WaterTopUnits
                    - hydrology.TargetTerrainSurfaceUnits)
                : 0f;
            return new WorldPatternResult(
                hydrology.TargetTerrainSurfaceUnits
                    - settings.TerrainBaseHeightUnits,
                terrain.VerticalFactor,
                0f,
                terrain.DominantPattern,
                terrain.RegionKey,
                terrain.InteriorProgress,
                terrain.PatternDepthUnits,
                terrain.PatternDepthProgress,
                terrain.PatternDetailUnits,
                hydrology.HasWater ? hydrology.WaterTopUnits : 0,
                hydrology.HasWater ? hydrology.WaterType : WaterType.None,
                hydrology.WaterType == WaterType.River
                    ? hydrology.Membership
                    : 0f,
                riverDepth,
                StreamingHydrologyPlanIdentity.ToDebugComponentId(hydrology),
                hydrology.Membership,
                hydrology.InteriorProgress,
                hydrologyType);
        }

        private static float ToSignedValue(float value, WorldNoiseMode mode) =>
            mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge
                ? value
                : value * 2f - 1f;

        private void EnsureContains(int worldX, int worldZ)
        {
            if (!Rectangle.Contains(worldX, worldZ))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldX),
                    "Base terrain is outside this Raster.");
            }
        }

        private void EnsureNotDisposed()
        {
            if (cells == null)
            {
                throw new ObjectDisposedException(nameof(StreamingHydrologyRaster));
            }
        }

        private readonly struct StreamingRiverRasterSample
        {
            private StreamingRiverRasterSample(
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

            public bool IsCloserThan(in StreamingRiverRasterSample other)
            {
                var difference = DistanceSquared - other.DistanceSquared;
                return difference < -1e-9
                    || Math.Abs(difference) <= 1e-9
                    && Route.Id.CompareTo(other.Route.Id) < 0;
            }

            public static StreamingRiverRasterSample Sample(
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
                return new StreamingRiverRasterSample(
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

    internal static class StreamingWorldChunkMaterializer
    {
        public static WorldChunkBuildData Build(
            StreamingFeatureWorld featureWorld,
            ChunkCoordinate coordinate)
        {
            try
            {
                var build = WorldChunkGenerator.Build(
                    WorldChunkBuildInput.Create(featureWorld, coordinate));
                featureWorld.CommitChunkDependencies(coordinate);
                return build;
            }
            catch
            {
                featureWorld.DiscardChunkDependencies(coordinate);
                throw;
            }
        }

    }
}
