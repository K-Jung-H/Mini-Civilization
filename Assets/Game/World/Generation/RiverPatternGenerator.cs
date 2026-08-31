using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal static class RiverHydrologyPlanner
    {
        private const int RouteVariationChannel = 7120;
        private const int WidthChannel = 7130;
        private const int DepthChannel = 7140;
        private const int WaterInsetChannel = 7150;
        private const int RiverbedChannel = 7160;
        private const int RiverbedAmplitudeChannel = 7170;

        private static readonly ConditionalWeakTable<
            HydrologyGenerationContext,
            RiverEdgeStore> EdgeStores = new();

        internal static RiverEdgeLease AcquireForBatch(
            HydrologyGenerationContext context,
            int originX,
            int originZ,
            int width,
            int height) => EdgeStores.GetValue(
                context,
                _ => new RiverEdgeStore(context)).Acquire(
                    originX,
                    originZ,
                    width,
                    height);

        internal static void RasterizeBatch(
            HydrologyGenerationContext context,
            int originX,
            int originZ,
            int width,
            int height,
            HydrologyMapCell[] cells,
            RiverEdgeLease lease)
        {
            if (cells == null || cells.Length != width * height)
            {
                throw new ArgumentException(
                    "Hydrology Batch raster dimensions are invalid.",
                    nameof(cells));
            }

            var nearest = new RiverPathSample[cells.Length];
            var hasNearest = new bool[cells.Length];
            var settings = context.Settings;
            var maximumRadius = settings.Hydrology.RiverCorridor
                .WidthCells.Maximum * 0.5f;
            var edges = lease.GetEdges(
                originX,
                originZ,
                width,
                height);
            for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                var edge = edges[edgeIndex];
                for (var segmentIndex = 0;
                     segmentIndex < edge.Segments.Length;
                     segmentIndex++)
                {
                    RasterizeSegment(
                        edge.Segments[segmentIndex],
                        originX,
                        originZ,
                        width,
                        height,
                        maximumRadius,
                        cells,
                        nearest,
                        hasNearest);
                }
            }

            for (var index = 0; index < cells.Length; index++)
            {
                if (cells[index].HasTerrainTarget || !hasNearest[index])
                {
                    continue;
                }

                var worldX = originX + index % width;
                var worldZ = originZ + index / width;
                if (!TryCreateCell(
                    context,
                    worldX,
                    worldZ,
                    nearest[index],
                    out var river))
                {
                    continue;
                }

                cells[index] = river;
            }
        }

        private static void RasterizeSegment(
            in RiverSegment segment,
            int originX,
            int originZ,
            int width,
            int height,
            float maximumRadius,
            IReadOnlyList<HydrologyMapCell> baseCells,
            RiverPathSample[] nearest,
            bool[] hasNearest)
        {
            var minimumX = Math.Max(
                0,
                (int)Math.Ceiling(
                    segment.MinimumX - maximumRadius - originX));
            var maximumX = Math.Min(
                width - 1,
                (int)Math.Floor(
                    segment.MaximumX + maximumRadius - originX));
            var minimumZ = Math.Max(
                0,
                (int)Math.Ceiling(
                    segment.MinimumZ - maximumRadius - originZ));
            var maximumZ = Math.Min(
                height - 1,
                (int)Math.Floor(
                    segment.MaximumZ + maximumRadius - originZ));
            if (minimumX > maximumX || minimumZ > maximumZ)
            {
                return;
            }

            for (var localZ = minimumZ; localZ <= maximumZ; localZ++)
            for (var localX = minimumX; localX <= maximumX; localX++)
            {
                var index = localX + width * localZ;
                if (baseCells[index].HasTerrainTarget)
                {
                    continue;
                }

                var candidate = segment.Sample(
                    originX + localX,
                    originZ + localZ);
                if (hasNearest[index]
                    && !candidate.IsCloserThan(nearest[index]))
                {
                    continue;
                }

                nearest[index] = candidate;
                hasNearest[index] = true;
            }
        }

        private static bool TryCreateCell(
            HydrologyGenerationContext context,
            int worldX,
            int worldZ,
            in RiverPathSample nearest,
            out HydrologyMapCell cell)
        {
            cell = default;
            var settings = context.Settings;
            var riverSettings = settings.Hydrology.RiverCorridor;

            var radius = nearest.WidthCells * 0.5f;
            if (radius <= 0f || nearest.Distance >= radius)
            {
                return false;
            }

            var coreProgress = Math.Clamp(
                1f - (float)(nearest.Distance / radius),
                0f,
                1f);
            var influence = Math.Clamp(
                riverSettings.CrossSection.Evaluate(coreProgress),
                0f,
                1f);
            if (influence <= 0f)
            {
                return false;
            }

            var centerDepthUnits = nearest.BedDepthUnits;
            var riverbedDetail = SampleSigned(
                    worldX,
                    worldZ,
                    riverSettings.RiverbedField,
                    Seed(settings.Seed, RiverbedChannel))
                * Lerp(
                    riverSettings.RiverbedAmplitudeUnits,
                    Sample01(
                        worldX,
                        worldZ,
                        riverSettings.WidthField,
                        Seed(settings.Seed, RiverbedAmplitudeChannel)));
            var waterTopUnits = checked((int)Math.Floor(
                nearest.WaterTopUnits));
            var terrainSurfaceUnits = SampleTerrain(
                    context,
                    worldX,
                    worldZ)
                .SurfaceUnits;
            var terrainRelativeDepthUnits = Math.Max(
                    0f,
                    centerDepthUnits - riverbedDetail)
                * influence;
            var terrainRelativeBedUnits = terrainSurfaceUnits
                - terrainRelativeDepthUnits;
            var waterDepthUnits = Math.Max(
                    0f,
                    nearest.WaterDepthBaseUnits
                    - riverbedDetail)
                * influence;
            var waterRelativeBedUnits = nearest.WaterTopUnits
                - waterDepthUnits;
            var targetBedUnits = Math.Min(
                terrainRelativeBedUnits,
                waterRelativeBedUnits);
            var depthUnits = Math.Max(
                0f,
                nearest.WaterTopUnits - targetBedUnits);
            cell = new HydrologyMapCell(
                nearest.ComponentId,
                WaterType.River,
                influence,
                influence,
                targetBedUnits,
                waterTopUnits,
                true,
                influence,
                depthUnits);
            return true;
        }

        internal sealed class RiverEdgeLease : IDisposable
        {
            private RiverEdgeStore store;
            private List<RiverGraphEntry> entries;

            internal RiverEdgeLease(
                RiverEdgeStore store,
                List<RiverGraphEntry> entries)
            {
                this.store = store;
                this.entries = entries;
            }

            internal IReadOnlyList<RiverEdge> GetEdges(
                int originX,
                int originZ,
                int width,
                int height)
            {
                if (store == null)
                {
                    throw new ObjectDisposedException(nameof(RiverEdgeLease));
                }

                return store.GetRegisteredEdges(
                    originX,
                    originZ,
                    width,
                    height);
            }

            public void Dispose()
            {
                if (store == null)
                {
                    return;
                }

                store.Release(entries);
                store = null;
                entries = null;
            }
        }

        internal sealed class RiverEdgeStore
        {
            private readonly HydrologyGenerationContext context;
            private readonly int regionSize;
            private readonly float maximumRadius;
            private readonly float ownerReachCells;
            private readonly object gate = new();
            private readonly Dictionary<long, RiverGraphEntry> graphs = new();
            private readonly Dictionary<RiverEdgeId, RiverEdge> edges = new();
            private readonly Dictionary<long, HashSet<RiverEdgeId>> regionEdges = new();

            public RiverEdgeStore(HydrologyGenerationContext context)
            {
                this.context = context;
                var settings = context.Settings;
                regionSize = settings.Hydrology.Map.PlanningRegionSizeCells;
                maximumRadius = settings.Hydrology.RiverCorridor
                    .WidthCells.Maximum * 0.5f;
                ownerReachCells = settings.Hydrology.RiverNetwork
                    .LengthCells.Maximum + maximumRadius;
            }

            public RiverEdgeLease Acquire(
                int originX,
                int originZ,
                int width,
                int height)
            {
                var halo = checked((int)Math.Ceiling(ownerReachCells));
                var minimumRegionX = FloorDivide(originX - halo, regionSize);
                var maximumRegionX = FloorDivide(
                    checked(originX + width - 1 + halo),
                    regionSize);
                var minimumRegionZ = FloorDivide(originZ - halo, regionSize);
                var maximumRegionZ = FloorDivide(
                    checked(originZ + height - 1 + halo),
                    regionSize);
                var entries = new List<RiverGraphEntry>();
                lock (gate)
                {
                    for (var regionZ = minimumRegionZ;
                         regionZ <= maximumRegionZ;
                         regionZ++)
                    for (var regionX = minimumRegionX;
                         regionX <= maximumRegionX;
                         regionX++)
                    {
                        var ownerRegionX = regionX;
                        var ownerRegionZ = regionZ;
                        var key = CoordinateKey(ownerRegionX, ownerRegionZ);
                        if (!graphs.TryGetValue(key, out var entry))
                        {
                            entry = new RiverGraphEntry(
                                key,
                                () => BuildRegionPlan(
                                    context,
                                    ownerRegionX,
                                    ownerRegionZ));
                            graphs.Add(key, entry);
                        }

                        entry.LeaseCount++;
                        entries.Add(entry);
                    }
                }

                try
                {
                    for (var index = 0; index < entries.Count; index++)
                    {
                        Register(entries[index]);
                    }
                    return new RiverEdgeLease(this, entries);
                }
                catch
                {
                    Release(entries);
                    throw;
                }
            }

            public IReadOnlyList<RiverEdge> GetRegisteredEdges(
                int originX,
                int originZ,
                int width,
                int height)
            {
                var minimumRegionX = FloorDivide(originX, regionSize);
                var maximumRegionX = FloorDivide(
                    checked(originX + width - 1),
                    regionSize);
                var minimumRegionZ = FloorDivide(originZ, regionSize);
                var maximumRegionZ = FloorDivide(
                    checked(originZ + height - 1),
                    regionSize);
                lock (gate)
                {
                    var ids = new HashSet<RiverEdgeId>();
                    for (var regionZ = minimumRegionZ;
                         regionZ <= maximumRegionZ;
                         regionZ++)
                    for (var regionX = minimumRegionX;
                         regionX <= maximumRegionX;
                         regionX++)
                    {
                        if (!regionEdges.TryGetValue(
                                CoordinateKey(regionX, regionZ),
                                out var registered))
                        {
                            continue;
                        }

                        ids.UnionWith(registered);
                    }

                    var result = new List<RiverEdge>(ids.Count);
                    foreach (var id in ids)
                    {
                        if (edges.TryGetValue(id, out var edge))
                        {
                            result.Add(edge);
                        }
                    }

                    return result;
                }
            }

            public void Release(IReadOnlyList<RiverGraphEntry> entries)
            {
                lock (gate)
                {
                    for (var index = 0; index < entries.Count; index++)
                    {
                        var entry = entries[index];
                        entry.LeaseCount--;
                        if (entry.LeaseCount != 0)
                        {
                            continue;
                        }

                        Remove(entry);
                    }
                }
            }

            private void Register(RiverGraphEntry entry)
            {
                var plan = entry.Plan.Value;
                lock (gate)
                {
                    if (entry.Registered)
                    {
                        return;
                    }

                    for (var edgeIndex = 0;
                         edgeIndex < plan.Edges.Length;
                         edgeIndex++)
                    {
                        var edge = plan.Edges[edgeIndex];
                        edges.Add(edge.Id, edge);
                        RegisterAffectedRegions(edge);
                    }

                    entry.Registered = true;
                }
            }

            private void Remove(RiverGraphEntry entry)
            {
                graphs.Remove(entry.Key);
                if (!entry.Registered)
                {
                    return;
                }

                var plan = entry.Plan.Value;
                for (var edgeIndex = 0;
                     edgeIndex < plan.Edges.Length;
                     edgeIndex++)
                {
                    var edge = plan.Edges[edgeIndex];
                    edges.Remove(edge.Id);
                    UnregisterAffectedRegions(edge);
                }
            }

            private void RegisterAffectedRegions(in RiverEdge edge)
            {
                GetAffectedRegionBounds(
                    edge,
                    out var minimumRegionX,
                    out var maximumRegionX,
                    out var minimumRegionZ,
                    out var maximumRegionZ);
                for (var regionZ = minimumRegionZ;
                     regionZ <= maximumRegionZ;
                     regionZ++)
                for (var regionX = minimumRegionX;
                     regionX <= maximumRegionX;
                     regionX++)
                {
                    var key = CoordinateKey(regionX, regionZ);
                    if (!regionEdges.TryGetValue(key, out var ids))
                    {
                        ids = new HashSet<RiverEdgeId>();
                        regionEdges.Add(key, ids);
                    }
                    ids.Add(edge.Id);
                }
            }

            private void UnregisterAffectedRegions(in RiverEdge edge)
            {
                GetAffectedRegionBounds(
                    edge,
                    out var minimumRegionX,
                    out var maximumRegionX,
                    out var minimumRegionZ,
                    out var maximumRegionZ);
                for (var regionZ = minimumRegionZ;
                     regionZ <= maximumRegionZ;
                     regionZ++)
                for (var regionX = minimumRegionX;
                     regionX <= maximumRegionX;
                     regionX++)
                {
                    var key = CoordinateKey(regionX, regionZ);
                    if (!regionEdges.TryGetValue(key, out var ids))
                    {
                        continue;
                    }

                    ids.Remove(edge.Id);
                    if (ids.Count == 0)
                    {
                        regionEdges.Remove(key);
                    }
                }
            }

            private void GetAffectedRegionBounds(
                in RiverEdge edge,
                out int minimumRegionX,
                out int maximumRegionX,
                out int minimumRegionZ,
                out int maximumRegionZ)
            {
                minimumRegionX = FloorDivide(
                    (int)Math.Floor(edge.MinimumX - maximumRadius),
                    regionSize);
                maximumRegionX = FloorDivide(
                    (int)Math.Ceiling(edge.MaximumX + maximumRadius),
                    regionSize);
                minimumRegionZ = FloorDivide(
                    (int)Math.Floor(edge.MinimumZ - maximumRadius),
                    regionSize);
                maximumRegionZ = FloorDivide(
                    (int)Math.Ceiling(edge.MaximumZ + maximumRadius),
                    regionSize);
            }
        }

        internal sealed class RiverGraphEntry
        {
            public RiverGraphEntry(long key, Func<RiverRegionPlan> create)
            {
                Key = key;
                Plan = new Lazy<RiverRegionPlan>(create, true);
            }

            public long Key { get; }
            public Lazy<RiverRegionPlan> Plan { get; }
            public int LeaseCount;
            public bool Registered;
        }

        internal static RiverRegionPlan BuildRegionPlan(
            HydrologyGenerationContext context,
            int regionX,
            int regionZ)
        {
            var settings = context.Settings;
            var mapSettings = settings.Hydrology.Map;
            var riverSettings = settings.Hydrology.RiverCorridor;
            var networkSettings = settings.Hydrology.RiverNetwork;
            var spacing = mapSettings.RouteSampleSpacingCells;
            var regionSize = mapSettings.PlanningRegionSizeCells;
            var routeReachCells = checked(
                (int)Math.Ceiling(networkSettings.LengthCells.Maximum / spacing)
                * spacing);
            var routeSizeCells = checked(regionSize + routeReachCells * 2);
            var gridSize = checked(routeSizeCells / spacing + 1);
            var nodeCount = checked(gridSize * gridSize);
            var regionOriginX = checked(regionX * regionSize);
            var regionOriginZ = checked(regionZ * regionSize);
            var originX = checked(regionOriginX - routeReachCells);
            var originZ = checked(regionOriginZ - routeReachCells);
            var surfaceUnits = new float[nodeCount];
            var slope = new float[nodeCount];
            var valleyDepth = new float[nodeCount];
            var sea = new bool[nodeCount];
            for (var z = 0; z < gridSize; z++)
            for (var x = 0; x < gridSize; x++)
            {
                var worldX = checked(originX + x * spacing);
                var worldZ = checked(originZ + z * spacing);
                var terrain = SampleTerrain(
                    context,
                    worldX,
                    worldZ);
                var index = x + gridSize * z;
                surfaceUnits[index] = terrain.SurfaceUnits;
                sea[index] = terrain.HasSeaWater;
            }

            BuildTerrainMetrics(
                surfaceUnits,
                slope,
                valleyDepth,
                gridSize,
                spacing);
            var basinComponent = BuildBasinComponentGrid(
                context,
                originX,
                originZ,
                gridSize,
                spacing);
            var endpoints = BuildEndpointCatalog(
                context,
                regionX,
                regionZ,
                regionOriginX,
                regionOriginZ,
                regionSize,
                routeReachCells,
                originX,
                originZ,
                gridSize,
                spacing,
                surfaceUnits,
                sea);
            var graphEdges = BuildGraphEdges(
                settings,
                endpoints);
            if (graphEdges.Count == 0)
            {
                return RiverRegionPlan.Empty;
            }

            var edges = new List<RiverEdge>();
            for (var index = 0; index < graphEdges.Count; index++)
            {
                var edge = graphEdges[index];
                var goals = new bool[nodeCount];
                goals[edge.End.GridIndex] = true;
                var path = FindRoute(
                    settings,
                    originX,
                    originZ,
                    gridSize,
                    spacing,
                    surfaceUnits,
                    slope,
                    valleyDepth,
                    sea,
                    basinComponent,
                    edge.Head,
                    edge.End,
                    goals);
                if (path.Count < 2)
                {
                    continue;
                }

                var segments = new List<RiverSegment>();
                AddLocalProfileSegments(
                    segments,
                    path,
                    context,
                    originX,
                    originZ,
                    gridSize,
                    spacing,
                    riverSettings.SmoothingIterations,
                    edge);
                if (segments.Count > 0)
                {
                    edges.Add(new RiverEdge(
                        new RiverEdgeId(
                            regionX,
                            regionZ,
                            edge.ComponentId),
                        segments.ToArray()));
                }
            }

            return edges.Count == 0
                ? RiverRegionPlan.Empty
                : new RiverRegionPlan(
                    edges.ToArray());
        }

        private static void BuildTerrainMetrics(
            float[] surfaceUnits,
            float[] slope,
            float[] valleyDepth,
            int gridSize,
            int spacing)
        {
            var unitScale = WorldGrid.HeightStepsPerCell * spacing;
            for (var z = 0; z < gridSize; z++)
            for (var x = 0; x < gridSize; x++)
            {
                var left = surfaceUnits[Math.Max(0, x - 1) + gridSize * z];
                var right = surfaceUnits[Math.Min(gridSize - 1, x + 1) + gridSize * z];
                var down = surfaceUnits[x + gridSize * Math.Max(0, z - 1)];
                var up = surfaceUnits[x + gridSize * Math.Min(gridSize - 1, z + 1)];
                var index = x + gridSize * z;
                var current = surfaceUnits[index];
                var gradientX = (right - left) / (2f * unitScale);
                var gradientZ = (up - down) / (2f * unitScale);
                slope[index] = MathF.Sqrt(
                    gradientX * gradientX + gradientZ * gradientZ);
                valleyDepth[index] = Math.Max(
                    0f,
                    (left + right + down + up) * 0.25f - current)
                    / WorldGrid.HeightStepsPerCell;
            }
        }

        private static int[] BuildBasinComponentGrid(
            HydrologyGenerationContext context,
            int originX,
            int originZ,
            int gridSize,
            int spacing)
        {
            var result = new int[gridSize * gridSize];
            for (var z = 0; z < gridSize; z++)
            for (var x = 0; x < gridSize; x++)
            {
                var worldX = originX + x * spacing;
                var worldZ = originZ + z * spacing;
                var sample = HydrologyRegionPlanner.SampleBase(
                    context,
                    worldX,
                    worldZ);
                result[x + gridSize * z] = sample.WaterType is WaterType.Lake
                    or WaterType.Pond
                    ? sample.ComponentId
                    : 0;
            }
            return result;
        }

        private static List<PlannedEndpoint> BuildEndpointCatalog(
            HydrologyGenerationContext context,
            int regionX,
            int regionZ,
            int regionOriginX,
            int regionOriginZ,
            int regionSize,
            int routeReachCells,
            int originX,
            int originZ,
            int gridSize,
            int spacing,
            float[] surfaceUnits,
            bool[] sea)
        {
            var settings = context.Settings;
            var result = new List<PlannedEndpoint>();
            var endpointReach = checked(routeReachCells * 2);
            var minimumRegionX = FloorDivide(
                regionOriginX - endpointReach,
                regionSize);
            var maximumRegionX = FloorDivide(
                regionOriginX + regionSize - 1 + endpointReach,
                regionSize);
            var minimumRegionZ = FloorDivide(
                regionOriginZ - endpointReach,
                regionSize);
            var maximumRegionZ = FloorDivide(
                regionOriginZ + regionSize - 1 + endpointReach,
                regionSize);
            for (var endpointRegionZ = minimumRegionZ;
                 endpointRegionZ <= maximumRegionZ;
                 endpointRegionZ++)
            for (var endpointRegionX = minimumRegionX;
                 endpointRegionX <= maximumRegionX;
                 endpointRegionX++)
            {
                var plannedEndpoints = HydrologyRegionPlanner.GetEndpoints(
                    context,
                    endpointRegionX,
                    endpointRegionZ);
                for (var index = 0; index < plannedEndpoints.Count; index++)
                {
                    var endpoint = plannedEndpoints[index];
                    var localX = (int)Math.Round(
                        (endpoint.WorldX - originX) / (double)spacing,
                        MidpointRounding.AwayFromZero);
                    var localZ = (int)Math.Round(
                        (endpoint.WorldZ - originZ) / (double)spacing,
                        MidpointRounding.AwayFromZero);
                    var gridIndex = (uint)localX < gridSize
                        && (uint)localZ < gridSize
                            ? localX + gridSize * localZ
                            : -1;
                    result.Add(new PlannedEndpoint(
                        endpoint.ComponentId,
                        endpoint.WorldX,
                        endpoint.WorldZ,
                        gridIndex,
                        endpoint.WaterTopUnits,
                        endpoint.Kind,
                        endpoint.Role,
                        endpoint.WorldX >= regionOriginX
                            && endpoint.WorldX < regionOriginX + regionSize
                            && endpoint.WorldZ >= regionOriginZ
                            && endpoint.WorldZ < regionOriginZ + regionSize
                            && endpoint.Kind is (
                                HydrologyEndpointKind.Lake
                                or HydrologyEndpointKind.Pond)));
                }

                AddNaturalEndpointsForRegion(
                    result,
                    context,
                    settings,
                    endpointRegionX,
                    endpointRegionZ,
                    regionX,
                    regionZ,
                    originX,
                    originZ,
                    gridSize,
                    spacing,
                    surfaceUnits,
                    sea);
            }
            return result;
        }

        private static void AddNaturalEndpointsForRegion(
            List<PlannedEndpoint> endpoints,
            HydrologyGenerationContext context,
            WorldSettingsData settings,
            int endpointRegionX,
            int endpointRegionZ,
            int ownerRegionX,
            int ownerRegionZ,
            int routeOriginX,
            int routeOriginZ,
            int gridSize,
            int spacing,
            float[] surfaceUnits,
            bool[] sea)
        {
            var network = settings.Hydrology.RiverNetwork;
            TryAddNaturalEndpointForRegion(
                endpoints,
                context,
                settings,
                endpointRegionX,
                endpointRegionZ,
                ownerRegionX,
                ownerRegionZ,
                routeOriginX,
                routeOriginZ,
                gridSize,
                spacing,
                surfaceUnits,
                sea,
                network.HeadDensity,
                HydrologyEndpointRole.Head,
                7200);
            TryAddNaturalEndpointForRegion(
                endpoints,
                context,
                settings,
                endpointRegionX,
                endpointRegionZ,
                ownerRegionX,
                ownerRegionZ,
                routeOriginX,
                routeOriginZ,
                gridSize,
                spacing,
                surfaceUnits,
                sea,
                network.EndDensity,
                HydrologyEndpointRole.End,
                7210);
        }

        private static void TryAddNaturalEndpointForRegion(
            List<PlannedEndpoint> endpoints,
            HydrologyGenerationContext context,
            WorldSettingsData settings,
            int regionX,
            int regionZ,
            int ownerRegionX,
            int ownerRegionZ,
            int routeOriginX,
            int routeOriginZ,
            int gridSize,
            int spacing,
            float[] surfaceUnits,
            bool[] sea,
            float density,
            HydrologyEndpointRole role,
            int channel)
        {
            if (DeterministicNoise.Value01(
                    regionX,
                    regionZ,
                    Seed(settings.Seed, channel)) >= density)
            {
                return;
            }

            var selectorX = DeterministicNoise.Value01(
                regionX,
                regionZ,
                Seed(settings.Seed, channel + 1));
            var selectorZ = DeterministicNoise.Value01(
                regionZ,
                regionX,
                Seed(settings.Seed, channel + 2));
            var regionSize = settings.Hydrology.Map.PlanningRegionSizeCells;
            var worldX = checked(regionX * regionSize
                + Math.Min(regionSize - 1, (int)(selectorX * regionSize)));
            var worldZ = checked(regionZ * regionSize
                + Math.Min(regionSize - 1, (int)(selectorZ * regionSize)));
            var x = (int)Math.Round(
                (worldX - routeOriginX) / (double)spacing,
                MidpointRounding.AwayFromZero);
            var z = (int)Math.Round(
                (worldZ - routeOriginZ) / (double)spacing,
                MidpointRounding.AwayFromZero);
            var index = (uint)x < gridSize && (uint)z < gridSize
                ? x + gridSize * z
                : -1;
            if (index >= 0 && sea[index])
            {
                return;
            }

            var outsideSea = false;
            var waterTopUnits = index >= 0
                ? (int)Math.Floor(surfaceUnits[index])
                : ResolveNaturalEndpointSurface(
                    context,
                    worldX,
                    worldZ,
                    out outsideSea);
            if (index < 0 && outsideSea)
            {
                return;
            }

            endpoints.Add(new PlannedEndpoint(
                unchecked((int)DeterministicNoise.Hash(
                    regionX,
                    regionZ,
                    Seed(settings.Seed, channel + 3))),
                worldX,
                worldZ,
                index,
                waterTopUnits,
                HydrologyEndpointKind.Natural,
                role,
                regionX == ownerRegionX && regionZ == ownerRegionZ));
        }

        private static int ResolveNaturalEndpointSurface(
            HydrologyGenerationContext context,
            int worldX,
            int worldZ,
            out bool sea)
        {
            var terrain = context.SampleBaseTerrain(
                worldX,
                worldZ);
            sea = terrain.HasSeaWater;
            return (int)Math.Floor(terrain.SurfaceUnits);
        }

        private static List<GraphEdge> BuildGraphEdges(
            WorldSettingsData settings,
            IReadOnlyList<PlannedEndpoint> endpoints)
        {
            var heads = new List<PlannedEndpoint>();
            var ends = new List<PlannedEndpoint>();
            for (var index = 0; index < endpoints.Count; index++)
            {
                if (endpoints[index].Role == HydrologyEndpointRole.Head)
                {
                    heads.Add(endpoints[index]);
                }
                else
                {
                    ends.Add(endpoints[index]);
                }
            }

            var result = new List<GraphEdge>();
            var network = settings.Hydrology.RiverNetwork;
            for (var headIndex = 0; headIndex < heads.Count; headIndex++)
            {
                var head = heads[headIndex];
                if (!head.OwnedByPlan || head.GridIndex < 0)
                {
                    continue;
                }

                var candidates = new List<EndpointMatch>();
                var targetLength = ResolveRange(
                    network.LengthCells,
                    DeterministicNoise.Value01(
                        head.WorldX,
                        head.WorldZ,
                        Seed(settings.Seed, 7220)));
                for (var endIndex = 0; endIndex < ends.Count; endIndex++)
                {
                    var end = ends[endIndex];
                    if (head.ComponentId == end.ComponentId)
                    {
                        continue;
                    }

                    var distance = EndpointDistance(head, end);
                    if (distance < network.LengthCells.Minimum
                        || distance > network.LengthCells.Maximum)
                    {
                        continue;
                    }

                    if (end.GridIndex < 0)
                    {
                        continue;
                    }

                    var elevationPenalty = Math.Max(
                        0f,
                        end.WaterTopUnits - head.WaterTopUnits);
                    var score = MathF.Abs(distance - targetLength)
                        + elevationPenalty * network.UphillCost
                        - EndpointWeight(network, end.Kind);
                    candidates.Add(new EndpointMatch(end, score));
                }

                candidates.Sort((left, right) =>
                {
                    var order = left.Score.CompareTo(right.Score);
                    return order != 0
                        ? order
                        : left.Endpoint.ComponentId.CompareTo(
                            right.Endpoint.ComponentId);
                });
                if (candidates.Count == 0)
                {
                    continue;
                }

                var selected = candidates[0].Endpoint;
                var selectedHead = FindBestHeadForEnd(
                    settings,
                    selected,
                    heads,
                    network);
                var mutual = selectedHead.ComponentId == head.ComponentId;
                if (!mutual && (selected.Kind is HydrologyEndpointKind.Lake
                        or HydrologyEndpointKind.Pond
                    || DeterministicNoise.Value01(
                        head.ComponentId,
                        selected.ComponentId,
                        Seed(settings.Seed, 7230)) >= network.JunctionChance))
                {
                    continue;
                }

                result.Add(new GraphEdge(
                    unchecked((int)DeterministicNoise.Hash(
                        head.ComponentId,
                        selected.ComponentId,
                        Seed(settings.Seed, 7240))),
                    head,
                    selected));
            }
            return result;
        }

        private static PlannedEndpoint FindBestHeadForEnd(
            WorldSettingsData settings,
            in PlannedEndpoint end,
            IReadOnlyList<PlannedEndpoint> heads,
            in RiverNetworkSettingsData network)
        {
            var found = false;
            var best = default(PlannedEndpoint);
            var bestScore = float.PositiveInfinity;
            for (var index = 0; index < heads.Count; index++)
            {
                var head = heads[index];
                if (head.ComponentId == end.ComponentId)
                {
                    continue;
                }

                var distance = EndpointDistance(head, end);
                if (distance < network.LengthCells.Minimum
                    || distance > network.LengthCells.Maximum)
                {
                    continue;
                }

                var targetLength = ResolveRange(
                    network.LengthCells,
                    DeterministicNoise.Value01(
                        head.WorldX,
                        head.WorldZ,
                        Seed(settings.Seed, 7220)));
                var score = MathF.Abs(distance - targetLength)
                    + Math.Max(0f, end.WaterTopUnits - head.WaterTopUnits)
                        * network.UphillCost
                    - EndpointWeight(network, end.Kind);
                if (!found || score < bestScore
                    || score == bestScore
                    && head.ComponentId < best.ComponentId)
                {
                    best = head;
                    bestScore = score;
                    found = true;
                }
            }
            return best;
        }

        private static float EndpointWeight(
            in RiverNetworkSettingsData settings,
            HydrologyEndpointKind kind) => kind switch
            {
                HydrologyEndpointKind.Lake => settings.LakeEndpointWeight,
                HydrologyEndpointKind.Pond => settings.PondEndpointWeight,
                HydrologyEndpointKind.Sea => settings.SeaEndpointWeight,
                _ => settings.NaturalEndpointWeight
            };

        private static float EndpointDistance(
            in PlannedEndpoint from,
            in PlannedEndpoint to)
        {
            var deltaX = from.WorldX - to.WorldX;
            var deltaZ = from.WorldZ - to.WorldZ;
            return MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private static List<int> FindRoute(
            WorldSettingsData settings,
            int originX,
            int originZ,
            int gridSize,
            int spacing,
            float[] surfaceUnits,
            float[] slope,
            float[] valleyDepth,
            bool[] sea,
            int[] basinComponent,
            in PlannedEndpoint head,
            in PlannedEndpoint end,
            bool[] goals)
        {
            var nodeCount = surfaceUnits.Length;
            var cost = new float[nodeCount];
            var previous = new int[nodeCount];
            var closed = new bool[nodeCount];
            Array.Fill(cost, float.PositiveInfinity);
            Array.Fill(previous, -1);
            var frontier = new MinimumHeap();
            cost[head.GridIndex] = 0f;
            frontier.Push(head.GridIndex, 0f);
            var destination = -1;
            var river = settings.Hydrology.RiverNetwork;
            var variationSeed = Seed(settings.Seed, RouteVariationChannel);
            ReadOnlySpan<int> neighborX = stackalloc int[]
                { -1, 0, 1, -1, 1, -1, 0, 1 };
            ReadOnlySpan<int> neighborZ = stackalloc int[]
                { -1, -1, -1, 0, 0, 1, 1, 1 };

            while (frontier.Count > 0)
            {
                var current = frontier.Pop();
                if (closed[current])
                {
                    continue;
                }

                closed[current] = true;
                if (goals[current])
                {
                    destination = current;
                    break;
                }

                var currentX = current % gridSize;
                var currentZ = current / gridSize;
                for (var direction = 0; direction < neighborX.Length; direction++)
                {
                    var nextX = currentX + neighborX[direction];
                    var nextZ = currentZ + neighborZ[direction];
                    if ((uint)nextX >= gridSize || (uint)nextZ >= gridSize)
                    {
                        continue;
                    }

                    var next = nextX + gridSize * nextZ;
                    if (closed[next])
                    {
                        continue;
                    }

                    if (sea[next] && next != end.GridIndex
                        || basinComponent[next] != 0
                        && basinComponent[next] != head.ComponentId
                        && basinComponent[next] != end.ComponentId)
                    {
                        continue;
                    }

                    var deltaCells = (surfaceUnits[next] - surfaceUnits[current])
                        / WorldGrid.HeightStepsPerCell;
                    var variation = Sample01(
                        originX + nextX * spacing,
                        originZ + nextZ * spacing,
                        river.RouteVariationField,
                        variationSeed);
                    var corridorExposure = SampleCorridorExposure(
                        settings,
                        originX,
                        originZ,
                        gridSize,
                        spacing,
                        surfaceUnits,
                        sea,
                        currentX,
                        currentZ,
                        nextX,
                        nextZ);
                    var terrainCost = 1f
                        + MathF.Abs(deltaCells) * river.TerrainChangeCost
                        + Math.Max(0f, deltaCells) * river.UphillCost
                        + slope[next] * river.CrossSlopeCost
                        + corridorExposure
                            * settings.Hydrology.RiverCorridor.CorridorExposureCost
                        + variation * river.RouteVariationCost;
                    terrainCost /= 1f
                        + valleyDepth[next] * river.ValleyPreference;
                    var movement = neighborX[direction] != 0
                        && neighborZ[direction] != 0
                        ? 1.41421356f
                        : 1f;
                    var nextCost = cost[current] + movement * terrainCost;
                    if (nextCost >= cost[next])
                    {
                        continue;
                    }

                    cost[next] = nextCost;
                    previous[next] = current;
                    frontier.Push(next, nextCost);
                }
            }

            if (destination < 0)
            {
                return new List<int>();
            }

            var path = new List<int>();
            for (var node = destination; node >= 0; node = previous[node])
            {
                path.Add(node);
                if (node == head.GridIndex)
                {
                    break;
                }
            }

            path.Reverse();
            return path;
        }

        private static float SampleCorridorExposure(
            WorldSettingsData settings,
            int originX,
            int originZ,
            int gridSize,
            int spacing,
            float[] surfaceUnits,
            bool[] sea,
            int currentX,
            int currentZ,
            int nextX,
            int nextZ)
        {
            var next = nextX + gridSize * nextZ;
            if (sea[next])
            {
                return 0f;
            }

            var river = settings.Hydrology.RiverCorridor;
            var worldX = originX + nextX * spacing;
            var worldZ = originZ + nextZ * spacing;
            var width = ResolveRange(
                river.WidthCells,
                Sample01(
                    worldX,
                    worldZ,
                    river.WidthField,
                    Seed(settings.Seed, WidthChannel)));
            var bankOffsetCells = width * 0.5f
                + river.BankMarginCells;
            var directionX = nextX - currentX;
            var directionZ = nextZ - currentZ;
            var directionLength = Math.Sqrt(
                directionX * directionX + directionZ * directionZ);
            var normalX = -directionZ / directionLength;
            var normalZ = directionX / directionLength;
            var gridOffset = bankOffsetCells / spacing;
            var leftX = nextX + normalX * gridOffset;
            var leftZ = nextZ + normalZ * gridOffset;
            var rightX = nextX - normalX * gridOffset;
            var rightZ = nextZ - normalZ * gridOffset;
            if (SampleGridFlag(sea, gridSize, leftX, leftZ)
                || SampleGridFlag(sea, gridSize, rightX, rightZ))
            {
                return 0f;
            }

            var insetUnits = Lerp(
                river.WaterInsetUnits,
                Sample01(
                    worldX,
                    worldZ,
                    river.WidthField,
                    Seed(settings.Seed, WaterInsetChannel)));
            var waterTopUnits = surfaceUnits[next] - insetUnits;
            var leftBankUnits = ToFullyFilledHeightUnits(
                SampleGridSurface(surfaceUnits, gridSize, leftX, leftZ),
                settings);
            var rightBankUnits = ToFullyFilledHeightUnits(
                SampleGridSurface(surfaceUnits, gridSize, rightX, rightZ),
                settings);
            return Math.Max(
                    0f,
                    waterTopUnits - Math.Min(leftBankUnits, rightBankUnits))
                / WorldGrid.HeightStepsPerCell;
        }

        private static float SampleGridSurface(
            float[] values,
            int gridSize,
            double x,
            double z)
        {
            x = Math.Clamp(x, 0.0, gridSize - 1.0);
            z = Math.Clamp(z, 0.0, gridSize - 1.0);
            var x0 = (int)Math.Floor(x);
            var z0 = (int)Math.Floor(z);
            var x1 = Math.Min(gridSize - 1, x0 + 1);
            var z1 = Math.Min(gridSize - 1, z0 + 1);
            var amountX = (float)(x - x0);
            var amountZ = (float)(z - z0);
            var lower = values[x0 + gridSize * z0]
                + (values[x1 + gridSize * z0]
                    - values[x0 + gridSize * z0]) * amountX;
            var upper = values[x0 + gridSize * z1]
                + (values[x1 + gridSize * z1]
                    - values[x0 + gridSize * z1]) * amountX;
            return lower + (upper - lower) * amountZ;
        }

        private static bool SampleGridFlag(
            bool[] values,
            int gridSize,
            double x,
            double z)
        {
            var sampleX = Math.Clamp(
                (int)Math.Round(x, MidpointRounding.AwayFromZero),
                0,
                gridSize - 1);
            var sampleZ = Math.Clamp(
                (int)Math.Round(z, MidpointRounding.AwayFromZero),
                0,
                gridSize - 1);
            return values[sampleX + gridSize * sampleZ];
        }

        private static void AddLocalProfileSegments(
            List<RiverSegment> segments,
            List<int> path,
            HydrologyGenerationContext context,
            int originX,
            int originZ,
            int gridSize,
            int spacing,
            int smoothingIterations,
            in GraphEdge edge)
        {
            var settings = context.Settings;
            var points = new List<RiverPoint>(path.Count);
            for (var index = 0; index < path.Count; index++)
            {
                points.Add(new RiverPoint(
                    originX + path[index] % gridSize * spacing,
                    originZ + path[index] / gridSize * spacing));
            }

            for (var iteration = 0;
                 iteration < smoothingIterations;
                 iteration++)
            {
                var smoothed = new List<RiverPoint>(points.Count * 2);
                smoothed.Add(points[0]);
                for (var index = 0; index < points.Count - 1; index++)
                {
                    var left = points[index];
                    var right = points[index + 1];
                    smoothed.Add(RiverPoint.Lerp(left, right, 0.25));
                    smoothed.Add(RiverPoint.Lerp(left, right, 0.75));
                }

                smoothed.Add(points[^1]);
                points = smoothed;
            }

            var river = settings.Hydrology.RiverCorridor;
            var rawWaterTopUnits = new float[points.Count];
            var containedWaterTopUnits = new float[points.Count];
            var widths = new float[points.Count];
            var bedDepths = new float[points.Count];
            var waterDepthBases = new float[points.Count];
            var widthSeed = Seed(settings.Seed, WidthChannel);
            var depthSeed = Seed(settings.Seed, DepthChannel);
            var insetSeed = Seed(settings.Seed, WaterInsetChannel);
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                var previous = points[Math.Max(0, index - 1)];
                var next = points[Math.Min(points.Count - 1, index + 1)];
                var tangentX = next.X - previous.X;
                var tangentZ = next.Z - previous.Z;
                var tangentLength = Math.Sqrt(
                    tangentX * tangentX + tangentZ * tangentZ);
                var normalX = tangentLength > double.Epsilon
                    ? -tangentZ / tangentLength
                    : 0.0;
                var normalZ = tangentLength > double.Epsilon
                    ? tangentX / tangentLength
                    : 1.0;
                var width = ResolveRange(
                    river.WidthCells,
                    Sample01(
                        point.X,
                        point.Z,
                        river.WidthField,
                        widthSeed));
                var depthUnits = Lerp(
                    river.DepthUnits,
                    Sample01(
                        point.X,
                        point.Z,
                        river.WidthField,
                        depthSeed));
                var insetUnits = Lerp(
                    river.WaterInsetUnits,
                    Sample01(
                        point.X,
                        point.Z,
                        river.WidthField,
                        insetSeed));
                var center = SampleTerrain(
                    context,
                    point.X,
                    point.Z);
                var waterTopUnits = center.HasSeaWater
                    ? center.WaterTopUnits
                    : center.SurfaceUnits - insetUnits;
                var bankOffset = width * 0.5f
                    + river.BankMarginCells;
                var leftBank = SampleTerrain(
                    context,
                    point.X + normalX * bankOffset,
                    point.Z + normalZ * bankOffset);
                var rightBank = SampleTerrain(
                    context,
                    point.X - normalX * bankOffset,
                    point.Z - normalZ * bankOffset);
                var containedWaterTop = waterTopUnits;
                if (!center.HasSeaWater
                    && !leftBank.HasSeaWater
                    && !rightBank.HasSeaWater)
                {
                    containedWaterTop = Math.Min(
                        containedWaterTop,
                        Math.Min(
                            ToFullyFilledHeightUnits(
                                leftBank.SurfaceUnits,
                                settings),
                            ToFullyFilledHeightUnits(
                                rightBank.SurfaceUnits,
                                settings)));
                }

                rawWaterTopUnits[index] = waterTopUnits;
                containedWaterTopUnits[index] = containedWaterTop;
                widths[index] = width;
                bedDepths[index] = depthUnits;
                waterDepthBases[index] = depthUnits - insetUnits;
            }

            var hydraulicWaterTopUnits = BuildWaterProfile(
                points,
                rawWaterTopUnits,
                containedWaterTopUnits,
                river.DropTransitionCells,
                river.DropTransition);
            ApplyEndpointProfiles(
                points,
                edge,
                settings.Hydrology.RiverNetwork,
                river,
                hydraulicWaterTopUnits,
                widths,
                bedDepths,
                waterDepthBases);
            var profiles = new RiverProfilePoint[points.Count];
            for (var index = 0; index < profiles.Length; index++)
            {
                profiles[index] = new RiverProfilePoint(
                    edge.ComponentId,
                    points[index].X,
                    points[index].Z,
                    hydraulicWaterTopUnits[index],
                    widths[index],
                    bedDepths[index],
                    waterDepthBases[index]);
            }

            if (!IsCorridorCompatibleWithBasins(
                    context,
                    profiles,
                    edge.Head.ComponentId,
                    edge.End.ComponentId))
            {
                return;
            }

            for (var index = 0; index < points.Count - 1; index++)
            {
                segments.Add(new RiverSegment(
                    profiles[index],
                    profiles[index + 1]));
            }
        }

        private static bool IsCorridorCompatibleWithBasins(
            HydrologyGenerationContext context,
            IReadOnlyList<RiverProfilePoint> profiles,
            int allowedComponentIdA,
            int allowedComponentIdB)
        {
            var settings = context.Settings;
            for (var segmentIndex = 0;
                 segmentIndex < profiles.Count - 1;
                 segmentIndex++)
            {
                var from = profiles[segmentIndex];
                var to = profiles[segmentIndex + 1];
                var deltaX = to.X - from.X;
                var deltaZ = to.Z - from.Z;
                var length = Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
                var sampleCount = Math.Max(1, (int)Math.Ceiling(length));
                var normalX = length > double.Epsilon ? -deltaZ / length : 0.0;
                var normalZ = length > double.Epsilon ? deltaX / length : 1.0;
                for (var sampleIndex = 0;
                     sampleIndex <= sampleCount;
                     sampleIndex++)
                {
                    var amount = sampleIndex / (double)sampleCount;
                    var centerX = from.X + deltaX * amount;
                    var centerZ = from.Z + deltaZ * amount;
                    var radius = (from.WidthCells
                            + (to.WidthCells - from.WidthCells) * amount)
                        * 0.5
                        + settings.Hydrology.RiverCorridor.BankMarginCells;
                    var lateralSamples = Math.Max(1, (int)Math.Ceiling(radius));
                    for (var lateral = -lateralSamples;
                         lateral <= lateralSamples;
                         lateral++)
                    {
                        var offset = Math.Clamp(lateral, -radius, radius);
                        var worldX = (int)Math.Round(
                            centerX + normalX * offset,
                            MidpointRounding.AwayFromZero);
                        var worldZ = (int)Math.Round(
                            centerZ + normalZ * offset,
                            MidpointRounding.AwayFromZero);
                        if (HydrologyRegionPlanner.IsBasinReserved(
                                context,
                                worldX,
                                worldZ,
                                allowedComponentIdA,
                                allowedComponentIdB))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static void ApplyEndpointProfiles(
            IReadOnlyList<RiverPoint> points,
            in GraphEdge edge,
            in RiverNetworkSettingsData network,
            in RiverCorridorSettingsData corridor,
            float[] waterTopUnits,
            float[] widths,
            float[] bedDepths,
            float[] waterDepthBases)
        {
            var distances = new double[points.Count];
            for (var index = 1; index < points.Count; index++)
            {
                var deltaX = points[index].X - points[index - 1].X;
                var deltaZ = points[index].Z - points[index - 1].Z;
                distances[index] = distances[index - 1]
                    + Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            }

            var total = distances[^1];
            for (var index = 0; index < points.Count; index++)
            {
                var factor = 1f;
                if (edge.Head.Kind == HydrologyEndpointKind.Natural)
                {
                    factor = Math.Min(
                        factor,
                        network.NaturalHeadTransition.Evaluate(
                            (float)Math.Clamp(
                                distances[index]
                                    / network.NaturalHeadTransitionCells,
                                0.0,
                                1.0)));
                }

                if (edge.End.Kind == HydrologyEndpointKind.Natural)
                {
                    factor = Math.Min(
                        factor,
                        network.NaturalEndTransition.Evaluate(
                            (float)Math.Clamp(
                                (total - distances[index])
                                    / network.NaturalEndTransitionCells,
                                0.0,
                                1.0)));
                }

                widths[index] *= factor;
                bedDepths[index] *= factor;
                waterDepthBases[index] *= factor;
                ApplyConnectedWaterTop(
                    ref waterTopUnits[index],
                    edge.Head,
                    distances[index],
                    corridor);
                ApplyConnectedWaterTop(
                    ref waterTopUnits[index],
                    edge.End,
                    total - distances[index],
                    corridor);
            }
        }

        private static void ApplyConnectedWaterTop(
            ref float waterTopUnits,
            in PlannedEndpoint endpoint,
            double distance,
            in RiverCorridorSettingsData corridor)
        {
            if (endpoint.Kind == HydrologyEndpointKind.Natural
                || distance > corridor.DropTransitionCells)
            {
                return;
            }

            var progress = 1f - (float)(distance
                / corridor.DropTransitionCells);
            var amount = corridor.DropTransition.Evaluate(progress);
            waterTopUnits += (endpoint.WaterTopUnits - waterTopUnits) * amount;
        }

        private static float[] BuildWaterProfile(
            IReadOnlyList<RiverPoint> points,
            float[] rawWaterTopUnits,
            float[] containedWaterTopUnits,
            int transitionCells,
            in WorldCurveSettingsData transition)
        {
            var distances = new double[points.Count];
            for (var index = 1; index < points.Count; index++)
            {
                var deltaX = points[index].X - points[index - 1].X;
                var deltaZ = points[index].Z - points[index - 1].Z;
                distances[index] = distances[index - 1]
                    + Math.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            }

            var profile = (float[])rawWaterTopUnits.Clone();
            for (var index = 0; index < points.Count; index++)
            {
                var localWaterTopUnits = rawWaterTopUnits[index];
                var minimumDistance = distances[index] - transitionCells;
                for (var source = index;
                     source >= 0 && distances[source] >= minimumDistance;
                     source--)
                {
                    if (containedWaterTopUnits[source] >= localWaterTopUnits)
                    {
                        continue;
                    }

                    var distance = distances[index] - distances[source];
                    var progress = Math.Clamp(
                        1f - (float)(distance / transitionCells),
                        0f,
                        1f);
                    var amount = transition.Evaluate(progress);
                    profile[index] = Math.Min(
                        profile[index],
                        localWaterTopUnits
                        + (containedWaterTopUnits[source]
                            - localWaterTopUnits) * amount);
                }

                var maximumDistance = distances[index] + transitionCells;
                for (var source = index + 1;
                     source < points.Count
                     && distances[source] <= maximumDistance;
                     source++)
                {
                    if (containedWaterTopUnits[source] >= localWaterTopUnits)
                    {
                        continue;
                    }

                    var distance = distances[source] - distances[index];
                    var progress = Math.Clamp(
                        1f - (float)(distance / transitionCells),
                        0f,
                        1f);
                    var amount = transition.Evaluate(progress);
                    profile[index] = Math.Min(
                        profile[index],
                        localWaterTopUnits
                        + (containedWaterTopUnits[source]
                            - localWaterTopUnits) * amount);
                }
            }

            return profile;
        }

        private static TerrainSurfaceSample SampleTerrain(
            HydrologyGenerationContext context,
            double worldX,
            double worldZ)
        {
            var sampleX = checked((int)Math.Round(
                worldX,
                MidpointRounding.AwayFromZero));
            var sampleZ = checked((int)Math.Round(
                worldZ,
                MidpointRounding.AwayFromZero));
            return context.SampleBaseTerrain(sampleX, sampleZ);
        }

        private static int ToFullyFilledHeightUnits(
            float surfaceUnits,
            WorldSettingsData settings)
        {
            var solidHeightUnits = Math.Clamp(
                (int)MathF.Round(
                    surfaceUnits,
                    MidpointRounding.AwayFromZero),
                0,
                checked(settings.WorldHeight
                    * WorldGrid.HeightStepsPerCell));
            return solidHeightUnits / WorldGrid.HeightStepsPerCell
                * WorldGrid.HeightStepsPerCell;
        }

        private static float Sample01(
            double worldX,
            double worldZ,
            in WorldNoiseFieldSettingsData field,
            int seed)
        {
            var sample = WorldNoiseFieldSampler.Sample2D(
                worldX,
                worldZ,
                field,
                seed);
            return field.Mode is WorldNoiseMode.Signed
                or WorldNoiseMode.SignedRidge
                ? Math.Clamp((sample + 1f) * 0.5f, 0f, 1f)
                : Math.Clamp(sample, 0f, 1f);
        }

        private static float SampleSigned(
            double worldX,
            double worldZ,
            in WorldNoiseFieldSettingsData field,
            int seed) => Sample01(worldX, worldZ, field, seed) * 2f - 1f;

        private static float Lerp(
            in WorldSeededRangeSettingsData range,
            float amount) => range.Minimum
                + (range.Maximum - range.Minimum) * amount;

        private static float ResolveRange(
            in WorldSeededRangeSettingsData range,
            float amount) => range.Minimum
                + (range.Maximum - range.Minimum) * amount;

        private static int Seed(int worldSeed, int channel) => unchecked(
            (int)DeterministicNoise.Hash(channel, channel * 31L, worldSeed));

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            var remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static long CoordinateKey(int x, int z) =>
            ((long)x << 32) ^ (uint)z;

        private readonly struct PlannedEndpoint
        {
            public PlannedEndpoint(
                int componentId,
                int worldX,
                int worldZ,
                int gridIndex,
                int waterTopUnits,
                HydrologyEndpointKind kind,
                HydrologyEndpointRole role,
                bool ownedByPlan)
            {
                ComponentId = componentId;
                WorldX = worldX;
                WorldZ = worldZ;
                GridIndex = gridIndex;
                WaterTopUnits = waterTopUnits;
                Kind = kind;
                Role = role;
                OwnedByPlan = ownedByPlan;
            }

            public int ComponentId { get; }
            public int WorldX { get; }
            public int WorldZ { get; }
            public int GridIndex { get; }
            public int WaterTopUnits { get; }
            public HydrologyEndpointKind Kind { get; }
            public HydrologyEndpointRole Role { get; }
            public bool OwnedByPlan { get; }
        }

        private readonly struct GraphEdge
        {
            public GraphEdge(
                int componentId,
                in PlannedEndpoint head,
                in PlannedEndpoint end)
            {
                ComponentId = componentId;
                Head = head;
                End = end;
            }

            public int ComponentId { get; }
            public PlannedEndpoint Head { get; }
            public PlannedEndpoint End { get; }
        }

        private readonly struct EndpointMatch
        {
            public EndpointMatch(
                in PlannedEndpoint endpoint,
                float score)
            {
                Endpoint = endpoint;
                Score = score;
            }

            public PlannedEndpoint Endpoint { get; }
            public float Score { get; }
        }

        internal sealed class RiverRegionPlan
        {
            internal static readonly RiverRegionPlan Empty = new(
                Array.Empty<RiverEdge>());
            internal readonly RiverEdge[] Edges;

            internal RiverRegionPlan(
                RiverEdge[] edges)
            {
                Edges = edges;
            }
        }

        internal readonly struct RiverEdgeId : IEquatable<RiverEdgeId>
        {
            public RiverEdgeId(int ownerRegionX, int ownerRegionZ, int value)
            {
                OwnerRegionX = ownerRegionX;
                OwnerRegionZ = ownerRegionZ;
                Value = value;
            }

            public int OwnerRegionX { get; }
            public int OwnerRegionZ { get; }
            public int Value { get; }

            public bool Equals(RiverEdgeId other) => OwnerRegionX == other.OwnerRegionX
                && OwnerRegionZ == other.OwnerRegionZ
                && Value == other.Value;

            public override bool Equals(object obj) => obj is RiverEdgeId other
                && Equals(other);

            public override int GetHashCode() => unchecked(
                ((OwnerRegionX * 397) ^ OwnerRegionZ) * 397 ^ Value);
        }

        internal readonly struct RiverEdge
        {
            public RiverEdge(RiverEdgeId id, RiverSegment[] segments)
            {
                Id = id;
                Segments = segments ?? throw new ArgumentNullException(
                    nameof(segments));
            }

            public RiverEdgeId Id { get; }
            public RiverSegment[] Segments { get; }
            public double MinimumX => GetMinimum(segment => segment.MinimumX);
            public double MaximumX => GetMaximum(segment => segment.MaximumX);
            public double MinimumZ => GetMinimum(segment => segment.MinimumZ);
            public double MaximumZ => GetMaximum(segment => segment.MaximumZ);

            private double GetMinimum(Func<RiverSegment, double> select)
            {
                var value = double.PositiveInfinity;
                for (var index = 0; index < Segments.Length; index++)
                {
                    value = Math.Min(value, select(Segments[index]));
                }
                return value;
            }

            private double GetMaximum(Func<RiverSegment, double> select)
            {
                var value = double.NegativeInfinity;
                for (var index = 0; index < Segments.Length; index++)
                {
                    value = Math.Max(value, select(Segments[index]));
                }
                return value;
            }
        }

        private readonly struct RiverPoint
        {
            public RiverPoint(double x, double z)
            {
                X = x;
                Z = z;
            }

            public double X { get; }
            public double Z { get; }

            public static RiverPoint Lerp(
                in RiverPoint from,
                in RiverPoint to,
                double amount) => new(
                    from.X + (to.X - from.X) * amount,
                    from.Z + (to.Z - from.Z) * amount);
        }

        internal readonly struct RiverProfilePoint
        {
            internal RiverProfilePoint(
                int componentId,
                double x,
                double z,
                float waterTopUnits,
                float widthCells,
                float bedDepthUnits,
                float waterDepthBaseUnits)
            {
                ComponentId = componentId;
                X = x;
                Z = z;
                WaterTopUnits = waterTopUnits;
                WidthCells = widthCells;
                BedDepthUnits = bedDepthUnits;
                WaterDepthBaseUnits = waterDepthBaseUnits;
            }

            public int ComponentId { get; }
            public double X { get; }
            public double Z { get; }
            public float WaterTopUnits { get; }
            public float WidthCells { get; }
            public float BedDepthUnits { get; }
            public float WaterDepthBaseUnits { get; }
        }

        internal readonly struct RiverPathSample
        {
            internal RiverPathSample(
                int componentId,
                double distanceSquared,
                float waterTopUnits,
                float widthCells,
                float bedDepthUnits,
                float waterDepthBaseUnits)
            {
                ComponentId = componentId;
                DistanceSquared = distanceSquared;
                WaterTopUnits = waterTopUnits;
                WidthCells = widthCells;
                BedDepthUnits = bedDepthUnits;
                WaterDepthBaseUnits = waterDepthBaseUnits;
            }

            public int ComponentId { get; }
            public double DistanceSquared { get; }
            public double Distance => Math.Sqrt(DistanceSquared);
            public float WaterTopUnits { get; }
            public float WidthCells { get; }
            public float BedDepthUnits { get; }
            public float WaterDepthBaseUnits { get; }

            public bool IsCloserThan(in RiverPathSample other)
            {
                var difference = DistanceSquared - other.DistanceSquared;
                return difference < -1e-9
                    || Math.Abs(difference) <= 1e-9
                    && WaterTopUnits < other.WaterTopUnits;
            }
        }

        internal readonly struct RiverSegment
        {
            private readonly RiverProfilePoint from;
            private readonly RiverProfilePoint to;

            internal RiverSegment(
                in RiverProfilePoint from,
                in RiverProfilePoint to)
            {
                this.from = from;
                this.to = to;
            }

            public double MinimumX => Math.Min(from.X, to.X);
            public double MaximumX => Math.Max(from.X, to.X);
            public double MinimumZ => Math.Min(from.Z, to.Z);
            public double MaximumZ => Math.Max(from.Z, to.Z);
            internal RiverPathSample Sample(double x, double z)
            {
                var deltaX = to.X - from.X;
                var deltaZ = to.Z - from.Z;
                var lengthSquared = deltaX * deltaX + deltaZ * deltaZ;
                if (lengthSquared <= double.Epsilon)
                {
                    var pointX = x - from.X;
                    var pointZ = z - from.Z;
                    return new RiverPathSample(
                        from.ComponentId,
                        pointX * pointX + pointZ * pointZ,
                        from.WaterTopUnits,
                        from.WidthCells,
                        from.BedDepthUnits,
                        from.WaterDepthBaseUnits);
                }

                var projection = Math.Clamp(
                    ((x - from.X) * deltaX + (z - from.Z) * deltaZ)
                    / lengthSquared,
                    0.0,
                    1.0);
                var nearestX = from.X + deltaX * projection;
                var nearestZ = from.Z + deltaZ * projection;
                var distanceX = x - nearestX;
                var distanceZ = z - nearestZ;
                return new RiverPathSample(
                    from.ComponentId,
                    distanceX * distanceX + distanceZ * distanceZ,
                    from.WaterTopUnits
                    + (to.WaterTopUnits - from.WaterTopUnits)
                    * (float)projection,
                    from.WidthCells
                    + (to.WidthCells - from.WidthCells)
                    * (float)projection,
                    from.BedDepthUnits
                    + (to.BedDepthUnits - from.BedDepthUnits)
                    * (float)projection,
                    from.WaterDepthBaseUnits
                    + (to.WaterDepthBaseUnits - from.WaterDepthBaseUnits)
                    * (float)projection);
            }
        }

        private sealed class MinimumHeap
        {
            private readonly List<Entry> entries = new();

            public int Count => entries.Count;

            public void Push(int node, float priority)
            {
                entries.Add(new Entry(node, priority));
                var index = entries.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (entries[parent].Priority <= priority)
                    {
                        break;
                    }

                    entries[index] = entries[parent];
                    index = parent;
                }

                entries[index] = new Entry(node, priority);
            }

            public int Pop()
            {
                var root = entries[0].Node;
                var last = entries[^1];
                entries.RemoveAt(entries.Count - 1);
                if (entries.Count == 0)
                {
                    return root;
                }

                var index = 0;
                while (true)
                {
                    var left = index * 2 + 1;
                    if (left >= entries.Count)
                    {
                        break;
                    }

                    var right = left + 1;
                    var child = right < entries.Count
                        && entries[right].Priority < entries[left].Priority
                        ? right
                        : left;
                    if (entries[child].Priority >= last.Priority)
                    {
                        break;
                    }

                    entries[index] = entries[child];
                    index = child;
                }

                entries[index] = last;
                return root;
            }

            private readonly struct Entry
            {
                public Entry(int node, float priority)
                {
                    Node = node;
                    Priority = priority;
                }

                public int Node { get; }
                public float Priority { get; }
            }
        }
    }
}
