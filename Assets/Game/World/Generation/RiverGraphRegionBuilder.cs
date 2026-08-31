using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal sealed class EndpointCatalogRegion
    {
        private readonly ReadOnlyCollection<HydrologyPlanEndpoint> endpoints;

        public EndpointCatalogRegion(
            TopologyRegionKey key,
            IList<HydrologyPlanEndpoint> endpoints)
        {
            Key = key;
            this.endpoints = new ReadOnlyCollection<HydrologyPlanEndpoint>(
                endpoints ?? throw new ArgumentNullException(nameof(endpoints)));
        }

        public TopologyRegionKey Key { get; }
        public IReadOnlyList<HydrologyPlanEndpoint> Endpoints => endpoints;
    }

    internal static class EndpointCatalogRegionBuilder
    {
        public static EndpointCatalogRegion Build(
            WorldHydrology hydrology,
            TopologyRegionKey key)
        {
            using var scope = hydrology.BeginTopologyPlanScope();
            var topology = scope.GetTopologyRegion(key);
            var endpoints = new List<HydrologyPlanEndpoint>(topology.Endpoints);
            if (RiverGraphRegionBuilder.TryCreateNaturalEndpoint(
                    hydrology,
                    topology,
                    out var natural))
            {
                endpoints.Add(natural);
            }

            endpoints.Sort((left, right) => left.Id.CompareTo(right.Id));
            return new EndpointCatalogRegion(key, endpoints);
        }
    }

    internal sealed class RiverGraphSpatialIndexRegion
    {
        private readonly ReadOnlyCollection<RiverEdgePlan> edges;
        private readonly ReadOnlyCollection<RiverJunctionPlan> junctions;

        public RiverGraphSpatialIndexRegion(
            TopologyRegionKey key,
            int size,
            IList<RiverEdgePlan> edges,
            IList<RiverJunctionPlan> junctions)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            Key = key;
            Size = size;
            OriginX = checked(key.X * size);
            OriginZ = checked(key.Z * size);
            this.edges = new ReadOnlyCollection<RiverEdgePlan>(
                edges ?? throw new ArgumentNullException(nameof(edges)));
            this.junctions = new ReadOnlyCollection<RiverJunctionPlan>(
                junctions ?? throw new ArgumentNullException(nameof(junctions)));
        }

        public TopologyRegionKey Key { get; }
        public int Size { get; }
        public int OriginX { get; }
        public int OriginZ { get; }
        public IReadOnlyList<RiverEdgePlan> Edges => edges;
        public IReadOnlyList<RiverJunctionPlan> Junctions => junctions;
    }

    internal readonly struct RiverEdgePlanRequest
    {
        public RiverEdgePlanRequest(
            HydrologyGraphEdgeId id,
            in HydrologyPlanEndpoint first,
            in HydrologyPlanEndpoint second,
            float candidateRadiusCells)
        {
            if (!id.First.Equals(first.Id)
                || !id.Second.Equals(second.Id)
                || candidateRadiusCells <= 0f
                || !float.IsFinite(candidateRadiusCells))
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            Id = id;
            First = first;
            Second = second;
            CandidateRadiusCells = candidateRadiusCells;
        }

        public HydrologyGraphEdgeId Id { get; }
        public HydrologyPlanEndpoint First { get; }
        public HydrologyPlanEndpoint Second { get; }
        public float CandidateRadiusCells { get; }
    }

    internal sealed class RiverEdgePlan
    {
        private readonly ReadOnlyCollection<RiverRoutePoint> route;

        public RiverEdgePlan(
            HydrologyGraphEdgeId id,
            HydrologyPlanEndpoint first,
            HydrologyPlanEndpoint second,
            float candidateRadiusCells,
            float widthCells,
            float depthUnits,
            int riverbedSeed,
            float riverbedAmplitudeUnits,
            IList<RiverRoutePoint> route,
            bool isActive = true)
        {
            if (candidateRadiusCells <= 0f
                || widthCells <= 0f
                || depthUnits <= 0f
                || route == null
                || route.Count < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(route));
            }

            Id = id;
            First = first;
            Second = second;
            CandidateRadiusCells = candidateRadiusCells;
            WidthCells = widthCells;
            DepthUnits = depthUnits;
            RiverbedSeed = riverbedSeed;
            RiverbedAmplitudeUnits = riverbedAmplitudeUnits;
            this.route = new ReadOnlyCollection<RiverRoutePoint>(route);
            MinimumX = route[0].WorldX;
            MinimumZ = route[0].WorldZ;
            MaximumX = route[0].WorldX;
            MaximumZ = route[0].WorldZ;
            for (var index = 1; index < route.Count; index++)
            {
                var point = route[index];
                MinimumX = Math.Min(MinimumX, point.WorldX);
                MinimumZ = Math.Min(MinimumZ, point.WorldZ);
                MaximumX = Math.Max(MaximumX, point.WorldX);
                MaximumZ = Math.Max(MaximumZ, point.WorldZ);
            }

            IsActive = isActive;
        }

        public HydrologyGraphEdgeId Id { get; }
        public HydrologyPlanEndpoint First { get; }
        public HydrologyPlanEndpoint Second { get; }
        public float CandidateRadiusCells { get; }
        public float WidthCells { get; }
        public float DepthUnits { get; }
        public int RiverbedSeed { get; }
        public float RiverbedAmplitudeUnits { get; }
        public IReadOnlyList<RiverRoutePoint> Route => route;
        public int MinimumX { get; }
        public int MinimumZ { get; }
        public int MaximumX { get; }
        public int MaximumZ { get; }
        public bool IsActive { get; }

        public RiverEdgePlan WithActivity(bool isActive)
        {
            if (IsActive == isActive)
            {
                return this;
            }

            return new RiverEdgePlan(
                Id,
                First,
                Second,
                CandidateRadiusCells,
                WidthCells,
                DepthUnits,
                RiverbedSeed,
                RiverbedAmplitudeUnits,
                route,
                isActive);
        }
    }

    internal readonly struct RiverRoutePoint
    {
        public RiverRoutePoint(
            int worldX,
            int worldZ,
            int waterTopUnits,
            int targetTerrainSurfaceUnits,
            float widthCells,
            float transitionMultiplier)
        {
            if (waterTopUnits < targetTerrainSurfaceUnits
                || targetTerrainSurfaceUnits < 0
                || !float.IsFinite(widthCells)
                || !float.IsFinite(transitionMultiplier))
            {
                throw new ArgumentOutOfRangeException(nameof(waterTopUnits));
            }

            WorldX = worldX;
            WorldZ = worldZ;
            WaterTopUnits = waterTopUnits;
            TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            WidthCells = Math.Max(0f, widthCells);
            TransitionMultiplier = Math.Clamp(transitionMultiplier, 0f, 1f);
        }

        public int WorldX { get; }
        public int WorldZ { get; }
        public int WaterTopUnits { get; }
        public int TargetTerrainSurfaceUnits { get; }
        public float WidthCells { get; }
        public float TransitionMultiplier { get; }
    }

    internal sealed class RiverJunctionPlan
    {
        private readonly ReadOnlyCollection<HydrologyGraphEdgeId> edges;

        public RiverJunctionPlan(
            int worldX,
            int worldZ,
            int waterTopUnits,
            int targetTerrainSurfaceUnits,
            IList<HydrologyGraphEdgeId> edges)
        {
            if (waterTopUnits < targetTerrainSurfaceUnits
                || targetTerrainSurfaceUnits < 0
                || edges == null
                || edges.Count < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(edges));
            }

            WorldX = worldX;
            WorldZ = worldZ;
            WaterTopUnits = waterTopUnits;
            TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            this.edges = new ReadOnlyCollection<HydrologyGraphEdgeId>(edges);
        }

        public int WorldX { get; }
        public int WorldZ { get; }
        public int WaterTopUnits { get; }
        public int TargetTerrainSurfaceUnits { get; }
        public IReadOnlyList<HydrologyGraphEdgeId> Edges => edges;
    }

    internal static class RiverGraphRegionBuilder
    {
        private static readonly (int x, int z, float distance)[] Neighbors =
        {
            (-1, -1, 1.41421356f), (0, -1, 1f), (1, -1, 1.41421356f),
            (-1, 0, 1f),                         (1, 0, 1f),
            (-1, 1, 1.41421356f),  (0, 1, 1f),  (1, 1, 1.41421356f)
        };

        /// <summary>
        /// Creates the routes owned by one topology Region.  Only endpoints in the
        /// Region core can become anchors; the surrounding bounds supply targets.
        /// This is the sole candidate-selection and route-search entry point for
        /// the active graph.
        /// </summary>
        internal static RiverProposalRegion BuildProposalRegion(
            WorldHydrology hydrology,
            TopologyRegionKey key,
            RiverProposalRegionStore.Entry proposalEntry)
        {
            if (hydrology == null || proposalEntry == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            var settings = hydrology.Settings;
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var originX = checked(key.X * size);
            var originZ = checked(key.Z * size);
            var core = new Bounds(
                originX,
                originZ,
                checked(originX + size - 1),
                checked(originZ + size - 1));
            var maximumRadius = (int)MathF.Ceiling(
                settings.Hydrology.RiverGraph.ConnectionRadiusCells.Maximum);
            var endpointsBounds = core.Expand(maximumRadius);

            AcquireTopologyRegions(
                proposalEntry.TopologyScope,
                size,
                endpointsBounds);
            var endpoints = CollectEndpoints(
                proposalEntry.EndpointScope,
                size,
                endpointsBounds);
            var anchors = new List<HydrologyPlanEndpoint>();
            for (var index = 0; index < endpoints.Count; index++)
            {
                var endpoint = endpoints[index];
                if (core.Contains(endpoint.WorldX, endpoint.WorldZ))
                {
                    anchors.Add(endpoint);
                }
            }

            anchors.Sort((left, right) => left.Id.CompareTo(right.Id));
            var planner = new RoutePlanner(
                hydrology,
                proposalEntry.TopologyScope,
                settings.Hydrology.RiverGraph);
            var proposals = BuildProposals(settings, planner, anchors, endpoints);
            var routes = new List<RiverEdgePlan>(proposals.Count);
            foreach (var proposal in proposals.Values)
            {
                routes.Add(proposalEntry.GetRoute(
                    new RiverEdgePlanRequest(
                        proposal.Id,
                        proposal.First,
                        proposal.Second,
                        proposal.CandidateRadiusCells),
                    proposal));
            }

            routes.Sort((left, right) => left.Id.CompareTo(right.Id));
            return new RiverProposalRegion(key, size, routes);
        }

        /// <summary>
        /// Resolves only the final junction rejection for one prebuilt route.
        /// The Proposal scope owns any neighbouring proposal Regions it reads, so
        /// no candidate list or A* route is recreated during this operation.
        /// </summary>
        internal static RiverEdgeActivity ResolveActivity(
            WorldHydrology hydrology,
            RiverEdgePlan route,
            RiverProposalRegionStore.ProposalScope proposalScope)
        {
            if (hydrology == null || route == null || proposalScope == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            var settings = hydrology.Settings;
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var maximumRadius = (int)MathF.Ceiling(
                settings.Hydrology.RiverGraph.ConnectionRadiusCells.Maximum);
            var interactionPadding = checked(maximumRadius
                + (int)MathF.Ceiling(
                    settings.Hydrology.RiverCorridor.WidthCells.Maximum * 0.5f
                    + settings.Hydrology.RiverCorridor.BankMarginCells));
            var bounds = GetRouteBounds(route).Expand(interactionPadding);
            var routes = CollectProposalRoutes(proposalScope, size, bounds);
            for (var index = 0; index < routes.Count; index++)
            {
                var other = routes[index];
                if (other.Id.CompareTo(route.Id) >= 0
                    || !CanPossiblyInteract(settings, route, other)
                    || !TryFindInteraction(settings, route, other, out var interaction)
                    || PassesJunctionChance(settings, route, other, interaction))
                {
                    continue;
                }

                return new RiverEdgeActivity(false);
            }

            return new RiverEdgeActivity(true);
        }

        /// <summary>
        /// Materializes the render/raster index for one core from already-owned
        /// proposal geometry and activity facts.
        /// </summary>
        internal static RiverGraphSpatialIndexRegion BuildSpatialIndex(
            WorldHydrology hydrology,
            TopologyRegionKey key,
            RiverProposalRegionStore proposals,
            RiverGraphStoreV2.Entry graphEntry)
        {
            if (hydrology == null || proposals == null || graphEntry == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            var settings = hydrology.Settings;
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var originX = checked(key.X * size);
            var originZ = checked(key.Z * size);
            var core = new Bounds(
                originX,
                originZ,
                checked(originX + size - 1),
                checked(originZ + size - 1));
            var maximumRadius = (int)MathF.Ceiling(
                settings.Hydrology.RiverGraph.ConnectionRadiusCells.Maximum);
            var candidateBounds = core.Expand(maximumRadius);
            using var proposalScope = proposals.BeginScope();
            var proposalRoutes = CollectProposalRoutes(
                proposalScope,
                size,
                candidateBounds);
            var active = new List<RiverEdgePlan>();
            for (var index = 0; index < proposalRoutes.Count; index++)
            {
                var proposal = proposalRoutes[index];
                var route = graphEntry.GetRoute(
                    new RiverEdgePlanRequest(
                        proposal.Id,
                        proposal.First,
                        proposal.Second,
                        proposal.CandidateRadiusCells),
                    proposal);
                if (graphEntry.IsActive(route) && Intersects(route, core))
                {
                    active.Add(route);
                }
            }

            active.Sort((left, right) => left.Id.CompareTo(right.Id));
            var allActive = new List<RiverEdgePlan>();
            for (var index = 0; index < proposalRoutes.Count; index++)
            {
                var route = proposalRoutes[index];
                if (graphEntry.IsActive(route))
                {
                    allActive.Add(route);
                }
            }

            var junctions = new Dictionary<(int x, int z), MutableJunction>();
            BuildJunctions(settings, allActive, junctions);
            var coreJunctions = new List<RiverJunctionPlan>();
            foreach (var junction in junctions.Values)
            {
                if (core.Contains(junction.WorldX, junction.WorldZ))
                {
                    coreJunctions.Add(junction.ToPlan());
                }
            }

            coreJunctions.Sort((left, right) =>
            {
                var x = left.WorldX.CompareTo(right.WorldX);
                return x != 0 ? x : left.WorldZ.CompareTo(right.WorldZ);
            });
            return new RiverGraphSpatialIndexRegion(
                key,
                size,
                active,
                coreJunctions);
        }

        public static RiverGraphSpatialIndexRegion Build(
            WorldHydrology hydrology,
            TopologyRegionKey key,
            RiverGraphStore.Entry graphEntry)
        {
            if (hydrology == null || graphEntry == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            var settings = hydrology.Settings;
            var graph = settings.Hydrology.RiverGraph;
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var originX = checked(key.X * size);
            var originZ = checked(key.Z * size);
            var maximumRadius = (int)MathF.Ceiling(
                graph.ConnectionRadiusCells.Maximum);
            var anchorBounds = new Bounds(
                checked(originX - maximumRadius),
                checked(originZ - maximumRadius),
                checked(originX + size - 1 + maximumRadius),
                checked(originZ + size - 1 + maximumRadius));
            var endpointBounds = anchorBounds.Expand(maximumRadius);

            using var endpointScope = hydrology.BeginEndpointCatalogPlanScope();
            using var topologyScope = hydrology.BeginTopologyPlanScope();
            AcquireTopologyRegions(
                topologyScope,
                settings.Hydrology.Map.PlanningRegionSizeCells,
                endpointBounds);
            var endpoints = CollectEndpoints(
                endpointScope,
                settings.Hydrology.Map.PlanningRegionSizeCells,
                endpointBounds);
            var anchors = new List<HydrologyPlanEndpoint>();
            for (var index = 0; index < endpoints.Count; index++)
            {
                var endpoint = endpoints[index];
                if (anchorBounds.Contains(endpoint.WorldX, endpoint.WorldZ))
                {
                    anchors.Add(endpoint);
                }
            }

            anchors.Sort((left, right) => left.Id.CompareTo(right.Id));
            var routePlanner = new RoutePlanner(
                hydrology,
                topologyScope,
                graph);
            var proposals = BuildProposals(
                settings,
                routePlanner,
                anchors,
                endpoints);
            var orderedProposals = new List<RiverEdgePlan>(proposals.Values);
            orderedProposals.Sort((left, right) => left.Id.CompareTo(right.Id));
            var active = new List<RiverEdgePlan>();
            for (var proposalIndex = 0;
                 proposalIndex < orderedProposals.Count;
                 proposalIndex++)
            {
                var proposal = orderedProposals[proposalIndex];
                var edge = graphEntry.GetEdgePlan(new RiverEdgePlanRequest(
                    proposal.Id,
                    proposal.First,
                    proposal.Second,
                    proposal.CandidateRadiusCells),
                    proposal);
                if (edge.IsActive)
                {
                    active.Add(edge);
                }
            }

            var junctions = new Dictionary<(int x, int z), MutableJunction>();
            BuildJunctions(settings, active, junctions);

            var core = new Bounds(
                originX,
                originZ,
                checked(originX + size - 1),
                checked(originZ + size - 1));
            var coreEdges = new List<RiverEdgePlan>();
            for (var edgeIndex = 0; edgeIndex < active.Count; edgeIndex++)
            {
                var edge = active[edgeIndex];
                if (Intersects(edge, core))
                {
                    coreEdges.Add(edge);
                }
            }

            coreEdges.Sort((left, right) => left.Id.CompareTo(right.Id));
            var coreJunctions = new List<RiverJunctionPlan>();
            foreach (var junction in junctions.Values)
            {
                if (core.Contains(junction.WorldX, junction.WorldZ))
                {
                    coreJunctions.Add(junction.ToPlan());
                }
            }

            coreJunctions.Sort((left, right) =>
            {
                var x = left.WorldX.CompareTo(right.WorldX);
                return x != 0 ? x : left.WorldZ.CompareTo(right.WorldZ);
            });
            return new RiverGraphSpatialIndexRegion(
                key,
                size,
                coreEdges,
                coreJunctions);
        }

        private static Dictionary<HydrologyGraphEdgeId, RiverEdgePlan> BuildProposals(
            WorldSettingsData settings,
            RoutePlanner routePlanner,
            IReadOnlyList<HydrologyPlanEndpoint> anchors,
            IReadOnlyList<HydrologyPlanEndpoint> endpoints)
        {
            var proposals = new Dictionary<HydrologyGraphEdgeId, RiverEdgePlan>();
            for (var anchorIndex = 0;
                 anchorIndex < anchors.Count;
                 anchorIndex++)
            {
                var anchor = anchors[anchorIndex];
                var candidates = BuildCandidateOrder(
                    settings,
                    routePlanner,
                    anchor,
                    endpoints);
                for (var candidateIndex = 0;
                     candidateIndex < candidates.Count;
                     candidateIndex++)
                {
                    var edge = routePlanner.GetRoute(candidates[candidateIndex]);
                    if (edge == null)
                    {
                        continue;
                    }

                    proposals.TryAdd(edge.Id, edge);
                    break;
                }
            }

            return proposals;
        }

        private static void BuildJunctions(
            WorldSettingsData settings,
            IReadOnlyList<RiverEdgePlan> active,
            IDictionary<(int x, int z), MutableJunction> junctions)
        {
            for (var candidateIndex = 0;
                 candidateIndex < active.Count;
                 candidateIndex++)
            for (var existingIndex = candidateIndex + 1;
                 existingIndex < active.Count;
                 existingIndex++)
            {
                var candidate = active[candidateIndex];
                var existing = active[existingIndex];
                if (!CanPossiblyInteract(settings, candidate, existing)
                    || !TryFindInteraction(
                        settings,
                        candidate,
                        existing,
                        out var interaction)
                    || !PassesJunctionChance(
                        settings,
                        candidate,
                        existing,
                        interaction))
                {
                    continue;
                }

                if (!junctions.TryGetValue(
                        (interaction.WorldX, interaction.WorldZ),
                        out var junction))
                {
                    junction = new MutableJunction(
                        interaction.WorldX,
                        interaction.WorldZ,
                        interaction.WaterTopUnits,
                        interaction.TargetTerrainSurfaceUnits);
                    junctions.Add((interaction.WorldX, interaction.WorldZ), junction);
                }

                junction.Add(candidate.Id);
                junction.Add(existing.Id);
                junction.Combine(
                    interaction.WaterTopUnits,
                    interaction.TargetTerrainSurfaceUnits);
            }
        }

        internal static RiverEdgePlan BuildEdgePlan(
            WorldHydrology hydrology,
            in RiverEdgePlanRequest request,
            RiverEdgePlan geometry)
        {
            if (hydrology == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            if (geometry == null || !geometry.Id.Equals(request.Id))
            {
                throw new ArgumentException(
                    "River Edge geometry must match its request.",
                    nameof(geometry));
            }

            var settings = hydrology.Settings;
            var graph = settings.Hydrology.RiverGraph;
            var regionSize = settings.Hydrology.Map.PlanningRegionSizeCells;
            var maximumRadius = (int)MathF.Ceiling(
                graph.ConnectionRadiusCells.Maximum);
            using var endpointScope = hydrology.BeginEndpointCatalogPlanScope();
            using var topologyScope = hydrology.BeginTopologyPlanScope();
            var routeBounds = GetRouteBounds(geometry);
            AcquireTopologyRegions(
                topologyScope,
                regionSize,
                routeBounds.Expand(maximumRadius));
            var routePlanner = new RoutePlanner(hydrology, topologyScope, graph);

            var interactionPadding = checked(maximumRadius + (int)MathF.Ceiling(
                settings.Hydrology.RiverCorridor.WidthCells.Maximum * 0.5f
                + settings.Hydrology.RiverCorridor.BankMarginCells));
            var anchorBounds = routeBounds.Expand(interactionPadding);
            var endpointBounds = anchorBounds.Expand(maximumRadius);
            AcquireTopologyRegions(topologyScope, regionSize, endpointBounds);
            var endpoints = CollectEndpoints(endpointScope, regionSize, endpointBounds);
            var anchors = new List<HydrologyPlanEndpoint>();
            for (var index = 0; index < endpoints.Count; index++)
            {
                var endpoint = endpoints[index];
                if (anchorBounds.Contains(endpoint.WorldX, endpoint.WorldZ))
                {
                    anchors.Add(endpoint);
                }
            }

            anchors.Sort((left, right) => left.Id.CompareTo(right.Id));
            var proposals = BuildProposals(
                settings,
                routePlanner,
                anchors,
                endpoints);
            var orderedProposals = new List<RiverEdgePlan>(proposals.Values);
            orderedProposals.Sort((left, right) => left.Id.CompareTo(right.Id));
            for (var index = 0; index < orderedProposals.Count; index++)
            {
                var other = orderedProposals[index];
                if (other.Id.CompareTo(geometry.Id) >= 0
                    || !TryFindInteraction(settings, geometry, other, out var interaction)
                    || PassesJunctionChance(settings, geometry, other, interaction))
                {
                    continue;
                }

                return geometry.WithActivity(false);
            }

            return geometry;
        }

        private static Bounds GetRouteBounds(RiverEdgePlan edge)
        {
            return new Bounds(
                edge.MinimumX,
                edge.MinimumZ,
                edge.MaximumX,
                edge.MaximumZ);
        }

        private static void AcquireTopologyRegions(
            HydrologyPlanScope scope,
            int size,
            in Bounds bounds)
        {
            var minimumX = FloorDivide(bounds.MinimumX, size);
            var maximumX = FloorDivide(bounds.MaximumX, size);
            var minimumZ = FloorDivide(bounds.MinimumZ, size);
            var maximumZ = FloorDivide(bounds.MaximumZ, size);
            for (var regionZ = minimumZ; regionZ <= maximumZ; regionZ++)
            for (var regionX = minimumX; regionX <= maximumX; regionX++)
            {
                _ = scope.GetTopologyRegion(new TopologyRegionKey(regionX, regionZ));
            }
        }

        private static List<HydrologyPlanEndpoint> CollectEndpoints(
            HydrologyPlanScope scope,
            int size,
            in Bounds bounds)
        {
            var minimumX = FloorDivide(bounds.MinimumX, size);
            var maximumX = FloorDivide(bounds.MaximumX, size);
            var minimumZ = FloorDivide(bounds.MinimumZ, size);
            var maximumZ = FloorDivide(bounds.MaximumZ, size);
            var unique = new Dictionary<HydrologyPlanEndpointId, HydrologyPlanEndpoint>();
            for (var regionZ = minimumZ; regionZ <= maximumZ; regionZ++)
            for (var regionX = minimumX; regionX <= maximumX; regionX++)
            {
                var region = scope.GetEndpointCatalogRegion(new TopologyRegionKey(
                    regionX,
                    regionZ));
                for (var index = 0; index < region.Endpoints.Count; index++)
                {
                    var endpoint = region.Endpoints[index];
                    if (bounds.Contains(endpoint.WorldX, endpoint.WorldZ))
                    {
                        unique[endpoint.Id] = endpoint;
                    }
                }

            }

            var result = new List<HydrologyPlanEndpoint>(unique.Values);
            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }

        private static List<RiverEdgePlan> CollectProposalRoutes(
            RiverProposalRegionStore.ProposalScope scope,
            int size,
            in Bounds bounds)
        {
            var minimumX = FloorDivide(bounds.MinimumX, size);
            var maximumX = FloorDivide(bounds.MaximumX, size);
            var minimumZ = FloorDivide(bounds.MinimumZ, size);
            var maximumZ = FloorDivide(bounds.MaximumZ, size);
            var unique = new Dictionary<HydrologyGraphEdgeId, RiverEdgePlan>();
            for (var regionZ = minimumZ; regionZ <= maximumZ; regionZ++)
            for (var regionX = minimumX; regionX <= maximumX; regionX++)
            {
                var region = scope.Get(new TopologyRegionKey(regionX, regionZ));
                for (var index = 0; index < region.Routes.Count; index++)
                {
                    var route = region.Routes[index];
                    unique.TryAdd(route.Id, route);
                }
            }

            var result = new List<RiverEdgePlan>(unique.Values);
            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }

        internal static bool TryCreateNaturalEndpoint(
            WorldHydrology hydrology,
            TopologyRegion region,
            out HydrologyPlanEndpoint endpoint)
        {
            var seed = DeterministicNoise.DeriveSeed(
                hydrology.Settings.Seed,
                "Hydrology.RiverGraph.Natural.Endpoint");
            var found = false;
            var selectedX = 0;
            var selectedZ = 0;
            var selectedScore = 0f;
            var selectedPlan = default(HydrologyCellPlan);
            for (var localZ = 0; localZ < region.Size; localZ++)
            for (var localX = 0; localX < region.Size; localX++)
            {
                var worldX = checked(region.OriginX + localX);
                var worldZ = checked(region.OriginZ + localZ);
                var plan = region.Sample(worldX, worldZ);
                if (plan.HasWater || plan.IsBasinProtected)
                {
                    continue;
                }

                var score = DeterministicNoise.Value01(worldX, worldZ, seed);
                if (!found || score > selectedScore
                    || score == selectedScore
                    && (worldX < selectedX
                        || worldX == selectedX && worldZ < selectedZ))
                {
                    found = true;
                    selectedX = worldX;
                    selectedZ = worldZ;
                    selectedScore = score;
                    selectedPlan = plan;
                }
            }

            if (!found)
            {
                endpoint = default;
                return false;
            }

            endpoint = new HydrologyPlanEndpoint(
                new HydrologyPlanEndpointId(
                    HydrologyPlanEndpointKind.Natural,
                    selectedX,
                    selectedZ,
                    default),
                selectedPlan.TargetTerrainSurfaceUnits);
            return true;
        }

        private static List<EdgeCandidate> BuildCandidateOrder(
            WorldSettingsData settings,
            RoutePlanner routePlanner,
            in HydrologyPlanEndpoint anchor,
            IReadOnlyList<HydrologyPlanEndpoint> endpoints)
        {
            var byKind = new Dictionary<
                HydrologyPlanEndpointKind,
                List<EdgeCandidate>>();
            for (var index = 0; index < endpoints.Count; index++)
            {
                var target = endpoints[index];
                if (target.Id.Equals(anchor.Id))
                {
                    continue;
                }

                var id = new HydrologyGraphEdgeId(anchor.Id, target.Id);
                var distance = Distance(anchor.WorldX, anchor.WorldZ,
                    target.WorldX, target.WorldZ);
                var radius = ResolveConnectionRadius(
                    settings,
                    id);
                if (distance > radius)
                {
                    continue;
                }

                if (!byKind.TryGetValue(target.Kind, out var candidates))
                {
                    candidates = new List<EdgeCandidate>();
                    byKind.Add(target.Kind, candidates);
                }

                candidates.Add(new EdgeCandidate(
                    id,
                    anchor,
                    target,
                    distance,
                    radius));
            }

            foreach (var candidates in byKind.Values)
            {
                candidates.Sort(EdgeCandidate.Compare);
            }

            var firstByKind = new List<EdgeCandidate>();
            foreach (var pair in byKind)
            {
                for (var index = 0; index < pair.Value.Count; index++)
                {
                    var candidate = pair.Value[index];
                    if (routePlanner.GetRoute(candidate) == null)
                    {
                        continue;
                    }

                    firstByKind.Add(candidate);
                    break;
                }
            }

            if (firstByKind.Count == 0)
            {
                return new List<EdgeCandidate>();
            }

            var natural = default(EdgeCandidate);
            var hasNatural = false;
            for (var index = 0; index < firstByKind.Count; index++)
            {
                if (firstByKind[index].Target.Kind
                    != HydrologyPlanEndpointKind.Natural)
                {
                    continue;
                }

                natural = firstByKind[index];
                hasNatural = true;
                break;
            }

            var naturalFirst = hasNatural
                && DeterministicNoise.Value01(
                    EndpointHash(anchor.Id),
                    firstByKind.Count,
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.RiverGraph.Natural.Selection"))
                    < 1f / firstByKind.Count;
            firstByKind.Sort(EdgeCandidate.Compare);
            var ordered = new List<EdgeCandidate>();
            if (naturalFirst)
            {
                ordered.Add(natural);
            }

            for (var index = 0; index < firstByKind.Count; index++)
            {
                var candidate = firstByKind[index];
                if (candidate.Target.Kind == HydrologyPlanEndpointKind.Natural)
                {
                    continue;
                }

                ordered.Add(candidate);
            }

            if (!naturalFirst && hasNatural)
            {
                ordered.Add(natural);
            }

            var remaining = new List<EdgeCandidate>();
            foreach (var candidates in byKind.Values)
            {
                for (var index = 0; index < candidates.Count; index++)
                {
                    var candidate = candidates[index];
                    var alreadyOrdered = false;
                    for (var orderedIndex = 0;
                         orderedIndex < ordered.Count;
                         orderedIndex++)
                    {
                        if (ordered[orderedIndex].Id.Equals(candidate.Id))
                        {
                            alreadyOrdered = true;
                            break;
                        }
                    }

                    if (!alreadyOrdered)
                    {
                        remaining.Add(candidate);
                    }
                }
            }

            remaining.Sort(EdgeCandidate.Compare);
            ordered.AddRange(remaining);
            return ordered;
        }

        private static float ResolveConnectionRadius(
            WorldSettingsData settings,
            in HydrologyGraphEdgeId id)
        {
            var graph = settings.Hydrology.RiverGraph;
            var amount = DeterministicNoise.Value01(
                EndpointHash(id.First),
                EndpointHash(id.Second),
                DeterministicNoise.DeriveSeed(
                    settings.Seed,
                    "Hydrology.RiverGraph.ConnectionRadius"));
            return graph.ConnectionRadiusCells.Minimum
                + (graph.ConnectionRadiusCells.Maximum
                    - graph.ConnectionRadiusCells.Minimum) * amount;
        }

        private static bool TryFindInteraction(
            WorldSettingsData settings,
            RiverEdgePlan candidate,
            RiverEdgePlan existing,
            out Interaction interaction)
        {
            var bestDistance = float.PositiveInfinity;
            var candidateIndex = -1;
            var existingIndex = -1;
            for (var candidateRoute = 1;
                 candidateRoute < candidate.Route.Count - 1;
                 candidateRoute++)
            for (var existingRoute = 1;
                 existingRoute < existing.Route.Count - 1;
                 existingRoute++)
            {
                var from = candidate.Route[candidateRoute];
                var to = existing.Route[existingRoute];
                var distance = Distance(from.WorldX, from.WorldZ, to.WorldX, to.WorldZ);
                var range = from.WidthCells * 0.5f
                    + to.WidthCells * 0.5f
                    + settings.Hydrology.RiverCorridor.BankMarginCells;
                if (distance > range
                    || distance > bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                candidateIndex = candidateRoute;
                existingIndex = existingRoute;
            }

            if (candidateIndex < 0)
            {
                interaction = default;
                return false;
            }

            var candidatePoint = candidate.Route[candidateIndex];
            var existingPoint = existing.Route[existingIndex];
            var distanceRange = candidatePoint.WidthCells * 0.5f
                + existingPoint.WidthCells * 0.5f
                + settings.Hydrology.RiverCorridor.BankMarginCells;
            var alignment = TangentAlignment(
                candidate.Route,
                candidateIndex,
                existing.Route,
                existingIndex);
            var waterTop = Math.Min(candidatePoint.WaterTopUnits,
                existingPoint.WaterTopUnits);
            interaction = new Interaction(
                checked((int)Math.Round(
                    (candidatePoint.WorldX + existingPoint.WorldX) * 0.5,
                    MidpointRounding.AwayFromZero)),
                checked((int)Math.Round(
                    (candidatePoint.WorldZ + existingPoint.WorldZ) * 0.5,
                    MidpointRounding.AwayFromZero)),
                Math.Clamp(bestDistance / distanceRange, 0f, 1f),
                alignment,
                waterTop,
                Math.Min(waterTop,
                    Math.Min(candidatePoint.TargetTerrainSurfaceUnits,
                        existingPoint.TargetTerrainSurfaceUnits)),
                existing.Id);
            return true;
        }

        private static bool CanPossiblyInteract(
            WorldSettingsData settings,
            RiverEdgePlan first,
            RiverEdgePlan second)
        {
            var range = (first.WidthCells + second.WidthCells) * 0.5f
                + settings.Hydrology.RiverCorridor.BankMarginCells;
            return AxisDistance(
                    first.MinimumX,
                    first.MaximumX,
                    second.MinimumX,
                    second.MaximumX)
                    <= range
                && AxisDistance(
                    first.MinimumZ,
                    first.MaximumZ,
                    second.MinimumZ,
                    second.MaximumZ)
                    <= range;
        }

        private static int AxisDistance(
            int firstMinimum,
            int firstMaximum,
            int secondMinimum,
            int secondMaximum)
        {
            if (firstMaximum < secondMinimum)
            {
                return secondMinimum - firstMaximum;
            }

            return secondMaximum < firstMinimum
                ? firstMinimum - secondMaximum
                : 0;
        }

        private static bool PassesJunctionChance(
            WorldSettingsData settings,
            RiverEdgePlan candidate,
            RiverEdgePlan existing,
            in Interaction interaction)
        {
            var graph = settings.Hydrology.RiverGraph;
            var probability = graph.ProximityChance.Evaluate(
                    interaction.Proximity)
                * graph.AlignmentChance.Evaluate(interaction.Alignment);
            var first = candidate.Id.CompareTo(existing.Id) <= 0
                ? candidate.Id
                : existing.Id;
            var second = candidate.Id.CompareTo(existing.Id) <= 0
                ? existing.Id
                : candidate.Id;
            var coordinateHash = DeterministicNoise.Hash(
                interaction.WorldX,
                interaction.WorldZ,
                DeterministicNoise.DeriveSeed(
                    settings.Seed,
                    "Hydrology.RiverGraph.Junction.Coordinate"));
            var noise = DeterministicNoise.Value01(
                EndpointHash(first.First) ^ EndpointHash(first.Second),
                EndpointHash(second.First) ^ EndpointHash(second.Second)
                    ^ coordinateHash,
                DeterministicNoise.DeriveSeed(
                    settings.Seed,
                    "Hydrology.RiverGraph.Junction.Accept"));
            return noise < probability;
        }

        private static float TangentAlignment(
            IReadOnlyList<RiverRoutePoint> first,
            int firstIndex,
            IReadOnlyList<RiverRoutePoint> second,
            int secondIndex)
        {
            var firstFrom = first[firstIndex - 1];
            var firstTo = first[firstIndex + 1];
            var secondFrom = second[secondIndex - 1];
            var secondTo = second[secondIndex + 1];
            var firstX = firstTo.WorldX - firstFrom.WorldX;
            var firstZ = firstTo.WorldZ - firstFrom.WorldZ;
            var secondX = secondTo.WorldX - secondFrom.WorldX;
            var secondZ = secondTo.WorldZ - secondFrom.WorldZ;
            var firstLength = MathF.Sqrt(firstX * firstX + firstZ * firstZ);
            var secondLength = MathF.Sqrt(secondX * secondX + secondZ * secondZ);
            if (firstLength <= 0f || secondLength <= 0f)
            {
                return 0f;
            }

            return Math.Clamp(MathF.Abs(
                (firstX * secondX + firstZ * secondZ)
                / (firstLength * secondLength)), 0f, 1f);
        }

        private static bool Intersects(
            RiverEdgePlan edge,
            in Bounds bounds)
        {
            for (var index = 1; index < edge.Route.Count; index++)
            {
                if (IntersectsSegment(
                        edge.Route[index - 1],
                        edge.Route[index],
                        bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IntersectsSegment(
            in RiverRoutePoint from,
            in RiverRoutePoint to,
            in Bounds bounds)
        {
            var minimumProgress = 0d;
            var maximumProgress = 1d;
            var deltaX = to.WorldX - from.WorldX;
            var deltaZ = to.WorldZ - from.WorldZ;
            return ClipSegmentAxis(
                    from.WorldX,
                    deltaX,
                    bounds.MinimumX,
                    bounds.MaximumX,
                    ref minimumProgress,
                    ref maximumProgress)
                && ClipSegmentAxis(
                    from.WorldZ,
                    deltaZ,
                    bounds.MinimumZ,
                    bounds.MaximumZ,
                    ref minimumProgress,
                    ref maximumProgress);
        }

        private static bool ClipSegmentAxis(
            int origin,
            int delta,
            int minimum,
            int maximum,
            ref double minimumProgress,
            ref double maximumProgress)
        {
            if (delta == 0)
            {
                return origin >= minimum && origin <= maximum;
            }

            var first = (minimum - origin) / (double)delta;
            var second = (maximum - origin) / (double)delta;
            if (first > second)
            {
                (first, second) = (second, first);
            }

            minimumProgress = Math.Max(minimumProgress, first);
            maximumProgress = Math.Min(maximumProgress, second);
            return minimumProgress <= maximumProgress;
        }

        private static int EndpointHash(in HydrologyPlanEndpointId id)
        {
            var basin = id.BasinComponent;
            var seed = unchecked(
                (int)id.Kind * 486187739
                ^ basin.SeedGridX * 16777619
                ^ basin.SeedGridZ * 374761393);
            return unchecked((int)DeterministicNoise.Hash(
                id.WorldX,
                id.WorldZ,
                seed));
        }

        private static float Distance(
            int firstX,
            int firstZ,
            int secondX,
            int secondZ)
        {
            var x = firstX - secondX;
            var z = firstZ - secondZ;
            return MathF.Sqrt(x * x + z * z);
        }

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }

        private readonly struct EdgeCandidate
        {
            public EdgeCandidate(
                HydrologyGraphEdgeId id,
                HydrologyPlanEndpoint anchor,
                HydrologyPlanEndpoint target,
                float distanceCells,
                float candidateRadiusCells)
            {
                Id = id;
                Anchor = anchor;
                Target = target;
                DistanceCells = distanceCells;
                CandidateRadiusCells = candidateRadiusCells;
            }

            public HydrologyGraphEdgeId Id { get; }
            public HydrologyPlanEndpoint Anchor { get; }
            public HydrologyPlanEndpoint Target { get; }
            public float DistanceCells { get; }
            public float CandidateRadiusCells { get; }

            public static int Compare(EdgeCandidate left, EdgeCandidate right)
            {
                var distance = left.DistanceCells.CompareTo(right.DistanceCells);
                return distance != 0 ? distance : left.Id.CompareTo(right.Id);
            }
        }

        private readonly struct Interaction
        {
            public Interaction(
                int worldX,
                int worldZ,
                float proximity,
                float alignment,
                int waterTopUnits,
                int targetTerrainSurfaceUnits,
                HydrologyGraphEdgeId otherEdgeId)
            {
                WorldX = worldX;
                WorldZ = worldZ;
                Proximity = proximity;
                Alignment = alignment;
                WaterTopUnits = waterTopUnits;
                TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
                OtherEdgeId = otherEdgeId;
            }

            public int WorldX { get; }
            public int WorldZ { get; }
            public float Proximity { get; }
            public float Alignment { get; }
            public int WaterTopUnits { get; }
            public int TargetTerrainSurfaceUnits { get; }
            public HydrologyGraphEdgeId OtherEdgeId { get; }
        }

        private readonly struct Bounds
        {
            public Bounds(int minimumX, int minimumZ, int maximumX, int maximumZ)
            {
                if (maximumX < minimumX || maximumZ < minimumZ)
                {
                    throw new ArgumentOutOfRangeException(nameof(maximumX));
                }

                MinimumX = minimumX;
                MinimumZ = minimumZ;
                MaximumX = maximumX;
                MaximumZ = maximumZ;
            }

            public int MinimumX { get; }
            public int MinimumZ { get; }
            public int MaximumX { get; }
            public int MaximumZ { get; }

            public bool Contains(int worldX, int worldZ) =>
                worldX >= MinimumX && worldX <= MaximumX
                && worldZ >= MinimumZ && worldZ <= MaximumZ;

            public Bounds Expand(int amount) => new(
                checked(MinimumX - amount),
                checked(MinimumZ - amount),
                checked(MaximumX + amount),
                checked(MaximumZ + amount));
        }

        private sealed class MutableJunction
        {
            private readonly List<HydrologyGraphEdgeId> edges = new();

            public MutableJunction(
                int worldX,
                int worldZ,
                int waterTopUnits,
                int targetTerrainSurfaceUnits)
            {
                WorldX = worldX;
                WorldZ = worldZ;
                WaterTopUnits = waterTopUnits;
                TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            }

            public int WorldX { get; }
            public int WorldZ { get; }
            public int WaterTopUnits { get; private set; }
            public int TargetTerrainSurfaceUnits { get; private set; }

            public void Add(in HydrologyGraphEdgeId edge)
            {
                for (var index = 0; index < edges.Count; index++)
                {
                    if (edges[index].Equals(edge))
                    {
                        return;
                    }
                }

                edges.Add(edge);
                edges.Sort((left, right) => left.CompareTo(right));
            }

            public void Combine(int waterTopUnits, int targetTerrainSurfaceUnits)
            {
                WaterTopUnits = Math.Min(WaterTopUnits, waterTopUnits);
                TargetTerrainSurfaceUnits = Math.Min(
                    TargetTerrainSurfaceUnits,
                    targetTerrainSurfaceUnits);
                TargetTerrainSurfaceUnits = Math.Min(
                    TargetTerrainSurfaceUnits,
                    WaterTopUnits);
            }

            public RiverJunctionPlan ToPlan() => new(
                WorldX,
                WorldZ,
                WaterTopUnits,
                TargetTerrainSurfaceUnits,
                edges);
        }

        private sealed class RoutePlanner
        {
            private readonly WorldHydrology hydrology;
            private readonly HydrologyPlanScope topologyScope;
            private readonly RiverGraphSettingsData graph;
            private readonly Dictionary<HydrologyGraphEdgeId, RiverEdgePlan> routes =
                new();
            private readonly HashSet<HydrologyGraphEdgeId> failed = new();
            private readonly Dictionary<(int x, int z), RouteCell> cells = new();

            public RoutePlanner(
                WorldHydrology hydrology,
                HydrologyPlanScope topologyScope,
                RiverGraphSettingsData graph)
            {
                this.hydrology = hydrology ?? throw new ArgumentNullException(
                    nameof(hydrology));
                this.topologyScope = topologyScope ?? throw new ArgumentNullException(
                    nameof(topologyScope));
                this.graph = graph;
            }

            public RiverEdgePlan GetRoute(in EdgeCandidate candidate)
            {
                if (routes.TryGetValue(candidate.Id, out var existing))
                {
                    return existing;
                }

                if (failed.Contains(candidate.Id))
                {
                    return null;
                }

                var first = candidate.Id.First.Equals(candidate.Anchor.Id)
                    ? candidate.Anchor
                    : candidate.Target;
                var second = candidate.Id.Second.Equals(candidate.Anchor.Id)
                    ? candidate.Anchor
                    : candidate.Target;
                var routeStarted = Stopwatch.GetTimestamp();
                List<(int x, int z)> route;
                try
                {
                    route = FindRoute(
                        first,
                        second,
                        candidate.CandidateRadiusCells);
                }
                finally
                {
                    hydrology.Metrics.RecordRiverRouteSearch(
                        Stopwatch.GetTimestamp() - routeStarted);
                }
                if (route == null)
                {
                    failed.Add(candidate.Id);
                    return null;
                }

                var settings = hydrology.Settings;
                var corridor = settings.Hydrology.RiverCorridor;
                var variation = DeterministicNoise.Value01(
                    EndpointHash(candidate.Id.First),
                    EndpointHash(candidate.Id.Second),
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.RiverGraph.Corridor"));
                var width = ResolveRange(corridor.WidthCells, variation);
                var depth = ResolveRange(corridor.DepthUnits,
                    DeterministicNoise.Value01(
                        EndpointHash(candidate.Id.Second),
                        EndpointHash(candidate.Id.First),
                        DeterministicNoise.DeriveSeed(
                            settings.Seed,
                            "Hydrology.RiverGraph.Depth")));
                var amplitude = ResolveRange(corridor.RiverbedAmplitudeUnits,
                    DeterministicNoise.Value01(
                        EndpointHash(candidate.Id.First),
                        EndpointHash(candidate.Id.Second),
                        DeterministicNoise.DeriveSeed(
                            settings.Seed,
                            "Hydrology.RiverGraph.BedAmplitude")));
                var routePoints = BuildRoutePoints(
                    first,
                    second,
                    route,
                    width,
                    depth);
                var plan = new RiverEdgePlan(
                    candidate.Id,
                    first,
                    second,
                    candidate.CandidateRadiusCells,
                    width,
                    depth,
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.RiverGraph.Bed"),
                    amplitude,
                    routePoints);
                routes.Add(candidate.Id, plan);
                return plan;
            }

            private List<RiverRoutePoint> BuildRoutePoints(
                in HydrologyPlanEndpoint first,
                in HydrologyPlanEndpoint second,
                IReadOnlyList<(int x, int z)> route,
                float width,
                float depth)
            {
                var distances = new float[route.Count];
                for (var index = 1; index < route.Count; index++)
                {
                    distances[index] = distances[index - 1] + Distance(
                        route[index - 1].x,
                        route[index - 1].z,
                        route[index].x,
                        route[index].z);
                }

                var total = distances[^1];
                var result = new List<RiverRoutePoint>(route.Count);
                for (var index = 0; index < route.Count; index++)
                {
                    var point = route[index];
                    var progress = total > 0f ? distances[index] / total : 0f;
                    var waterTop = (int)MathF.Round(
                        first.WaterTopUnits
                        + (second.WaterTopUnits - first.WaterTopUnits) * progress,
                        MidpointRounding.AwayFromZero);
                    var transition = 1f;
                    if (first.Kind == HydrologyPlanEndpointKind.Natural)
                    {
                        transition = Math.Min(
                            transition,
                            EvaluateNaturalTransition(
                                distances[index]
                                / graph.NaturalTransitionCells));
                    }

                    if (second.Kind == HydrologyPlanEndpointKind.Natural)
                    {
                        transition = Math.Min(
                            transition,
                            EvaluateNaturalTransition(
                                (total - distances[index])
                                / graph.NaturalTransitionCells));
                    }

                    var target = Math.Max(
                        0,
                        waterTop - (int)MathF.Round(
                            depth * transition,
                            MidpointRounding.AwayFromZero));
                    result.Add(new RiverRoutePoint(
                        point.x,
                        point.z,
                        waterTop,
                        target,
                        width * transition,
                        transition));
                }

                return result;
            }

            private float EvaluateNaturalTransition(float progress)
            {
                progress = Math.Clamp(progress, 0f, 1f);
                var integral = IntegrateRate(graph.NaturalTransitionRate, progress);
                var total = IntegrateRate(graph.NaturalTransitionRate, 1f);
                return total > 0f ? Math.Clamp(integral / total, 0f, 1f) : 0f;
            }

            private static float IntegrateRate(
                in WorldCurveSettingsData curve,
                float progress)
            {
                progress = Math.Clamp(progress, 0f, 1f);
                var segmentCount = Math.Min(4, (int)MathF.Floor(progress * 4f));
                var integral = 0f;
                for (var segment = 0; segment < segmentCount; segment++)
                {
                    integral += 0.125f * (
                        GetRateValue(curve, segment)
                        + GetRateValue(curve, segment + 1));
                }

                if (segmentCount == 4)
                {
                    return integral;
                }

                var local = progress * 4f - segmentCount;
                var from = GetRateValue(curve, segmentCount);
                var to = GetRateValue(curve, segmentCount + 1);
                integral += 0.25f * (
                    from * local
                    + (to - from) * (local * local * local
                        - 0.5f * local * local * local * local));
                return integral;
            }

            private static float GetRateValue(
                in WorldCurveSettingsData curve,
                int index) => index switch
            {
                0 => curve.AtZero,
                1 => curve.AtQuarter,
                2 => curve.AtHalf,
                3 => curve.AtThreeQuarters,
                _ => curve.AtOne
            };

            private List<(int x, int z)> FindRoute(
                in HydrologyPlanEndpoint first,
                in HydrologyPlanEndpoint second,
                float radius)
            {
                var spacing = hydrology.Settings.Hydrology.Map.RouteSampleSpacingCells;
                var startX = RoundToSpacing(first.WorldX, spacing);
                var startZ = RoundToSpacing(first.WorldZ, spacing);
                var endX = RoundToSpacing(second.WorldX, spacing);
                var endZ = RoundToSpacing(second.WorldZ, spacing);
                if (!HasClearConnector(
                        first.WorldX,
                        first.WorldZ,
                        startX,
                        startZ,
                        first,
                        second)
                    || !HasClearConnector(
                        endX,
                        endZ,
                        second.WorldX,
                        second.WorldZ,
                        first,
                        second))
                {
                    return null;
                }

                var gridRadius = (int)MathF.Ceiling(radius);
                var minimumX = FloorDivide(
                    Math.Min(startX, endX) - gridRadius,
                    spacing);
                var maximumX = FloorDivide(
                    Math.Max(startX, endX) + gridRadius,
                    spacing);
                var minimumZ = FloorDivide(
                    Math.Min(startZ, endZ) - gridRadius,
                    spacing);
                var maximumZ = FloorDivide(
                    Math.Max(startZ, endZ) + gridRadius,
                    spacing);
                var width = checked(maximumX - minimumX + 1);
                var height = checked(maximumZ - minimumZ + 1);
                var startLocalX = startX / spacing - minimumX;
                var startLocalZ = startZ / spacing - minimumZ;
                var endLocalX = endX / spacing - minimumX;
                var endLocalZ = endZ / spacing - minimumZ;
                var start = startLocalX + width * startLocalZ;
                var end = endLocalX + width * endLocalZ;
                var count = checked(width * height);
                var costs = new float[count];
                var previous = new int[count];
                var closed = new bool[count];
                Array.Fill(costs, float.PositiveInfinity);
                Array.Fill(previous, -1);
                costs[start] = 0f;
                var frontier = new MinHeap();
                frontier.Push(start, 0f);
                while (frontier.Count > 0)
                {
                    var current = frontier.Pop();
                    if (closed[current])
                    {
                        continue;
                    }

                    closed[current] = true;
                    if (current == end)
                    {
                        break;
                    }

                    var localX = current % width;
                    var localZ = current / width;
                    var worldX = checked((minimumX + localX) * spacing);
                    var worldZ = checked((minimumZ + localZ) * spacing);
                    var currentCell = GetCell(worldX, worldZ);
                    for (var direction = 0; direction < Neighbors.Length; direction++)
                    {
                        var neighbor = Neighbors[direction];
                        var nextX = localX + neighbor.x;
                        var nextZ = localZ + neighbor.z;
                        if ((uint)nextX >= width || (uint)nextZ >= height)
                        {
                            continue;
                        }

                        var next = nextX + width * nextZ;
                        if (closed[next])
                        {
                            continue;
                        }

                        var nextWorldX = checked((minimumX + nextX) * spacing);
                        var nextWorldZ = checked((minimumZ + nextZ) * spacing);
                        if (next != end && next != start
                            && (!IsInsideLens(
                                    nextWorldX,
                                    nextWorldZ,
                                    first,
                                    second,
                                    radius)
                                || IsBlocked(
                                    nextWorldX,
                                    nextWorldZ,
                                    first,
                                    second)))
                        {
                            continue;
                        }

                        var nextCell = GetCell(nextWorldX, nextWorldZ);
                        var cost = GetStepCost(
                            currentCell,
                            nextCell,
                            nextWorldX,
                            nextWorldZ,
                            neighbor.distance);
                        var nextCost = costs[current] + neighbor.distance * cost;
                        if (nextCost >= costs[next])
                        {
                            continue;
                        }

                        costs[next] = nextCost;
                        previous[next] = current;
                        frontier.Push(next, nextCost);
                    }
                }

                if (!closed[end])
                {
                    return null;
                }

                var gridRoute = new List<(int x, int z)>();
                for (var current = end; current >= 0; current = previous[current])
                {
                    var localX = current % width;
                    var localZ = current / width;
                    gridRoute.Add((
                        checked((minimumX + localX) * spacing),
                        checked((minimumZ + localZ) * spacing)));
                    if (current == start)
                    {
                        break;
                    }
                }

                gridRoute.Reverse();
                var route = new List<(int x, int z)>(gridRoute.Count + 2);
                AddDistinct(route, (first.WorldX, first.WorldZ));
                for (var index = 0; index < gridRoute.Count; index++)
                {
                    AddDistinct(route, gridRoute[index]);
                }

                AddDistinct(route, (second.WorldX, second.WorldZ));
                return route.Count >= 2 ? route : null;
            }

            private bool HasClearConnector(
                int fromX,
                int fromZ,
                int toX,
                int toZ,
                in HydrologyPlanEndpoint first,
                in HydrologyPlanEndpoint second)
            {
                var deltaX = Math.Abs(toX - fromX);
                var deltaZ = Math.Abs(toZ - fromZ);
                var stepX = fromX < toX ? 1 : -1;
                var stepZ = fromZ < toZ ? 1 : -1;
                var error = deltaX - deltaZ;
                var currentX = fromX;
                var currentZ = fromZ;
                while (true)
                {
                    if (IsBlocked(currentX, currentZ, first, second))
                    {
                        return false;
                    }

                    if (currentX == toX && currentZ == toZ)
                    {
                        return true;
                    }

                    var twiceError = error * 2;
                    if (twiceError > -deltaZ)
                    {
                        error -= deltaZ;
                        currentX += stepX;
                    }

                    if (twiceError < deltaX)
                    {
                        error += deltaX;
                        currentZ += stepZ;
                    }
                }
            }

            private bool IsInsideLens(
                int worldX,
                int worldZ,
                in HydrologyPlanEndpoint first,
                in HydrologyPlanEndpoint second,
                float radius) =>
                Distance(worldX, worldZ, first.WorldX, first.WorldZ) <= radius
                && Distance(worldX, worldZ, second.WorldX, second.WorldZ) <= radius;

            private bool IsBlocked(
                int worldX,
                int worldZ,
                in HydrologyPlanEndpoint first,
                in HydrologyPlanEndpoint second)
            {
                if (worldX == first.WorldX && worldZ == first.WorldZ
                    || worldX == second.WorldX && worldZ == second.WorldZ)
                {
                    return false;
                }

                var cell = GetCell(worldX, worldZ);
                return cell.Plan.HasWater || cell.Plan.IsBasinProtected;
            }

            private RouteCell GetCell(int worldX, int worldZ)
            {
                if (cells.TryGetValue((worldX, worldZ), out var existing))
                {
                    return existing;
                }

                var region = topologyScope.GetTopologyRegion(
                    hydrology.GetTopologyRegionKey(worldX, worldZ));
                var cell = new RouteCell(
                    hydrology.SampleBaseTerrain(worldX, worldZ),
                    region.Sample(worldX, worldZ));
                cells.Add((worldX, worldZ), cell);
                return cell;
            }

            private float GetStepCost(
                in RouteCell current,
                in RouteCell next,
                int worldX,
                int worldZ,
                float movement)
            {
                var network = hydrology.Settings.Hydrology.RiverNetwork;
                var elevation = MathF.Abs(
                    next.Base.Surface.SurfaceUnits
                    - current.Base.Surface.SurfaceUnits)
                    / WorldGrid.HeightStepsPerCell;
                var slope = elevation / movement;
                var variation = ToUnit(WorldNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                    network.RouteVariationField,
                    DeterministicNoise.DeriveSeed(
                        hydrology.Settings.Seed,
                        "Hydrology.RiverGraph.RouteVariation")),
                    network.RouteVariationField.Mode);
                var valley = Math.Clamp(
                    1f - next.Base.Field.PeaksValleys,
                    0f,
                    1f);
                return (1f
                    + elevation * graph.ElevationChangeCost
                    + slope * network.CrossSlopeCost
                    + variation * network.RouteVariationCost)
                    / (1f + valley * network.ValleyPreference);
            }

            private static float ResolveRange(
                in WorldSeededRangeSettingsData range,
                float amount) => range.Minimum
                    + (range.Maximum - range.Minimum) * amount;

            private static float ToUnit(float value, WorldNoiseMode mode)
            {
                if (mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge)
                {
                    value = (value + 1f) * 0.5f;
                }

                return Math.Clamp(value, 0f, 1f);
            }

            private static int RoundToSpacing(int value, int spacing) =>
                checked((int)Math.Round(
                    value / (double)spacing,
                    MidpointRounding.AwayFromZero)) * spacing;

            private static void AddDistinct(
                ICollection<(int x, int z)> route,
                (int x, int z) point)
            {
                if (route is List<(int x, int z)> list
                    && list.Count > 0
                    && list[^1] == point)
                {
                    return;
                }

                route.Add(point);
            }

            private readonly struct RouteCell
            {
                public RouteCell(BaseTerrainSample @base, HydrologyCellPlan plan)
                {
                    Base = @base;
                    Plan = plan;
                }

                public BaseTerrainSample Base { get; }
                public HydrologyCellPlan Plan { get; }
            }
        }

        private sealed class MinHeap
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
