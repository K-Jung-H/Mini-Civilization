using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Streaming
{
    internal sealed class StreamingFeatureWorld
    {
        private static readonly (int x, int z, float distance)[] routeNeighbors =
        {
            (-1, -1, 1.41421356f), (0, -1, 1f), (1, -1, 1.41421356f),
            (-1, 0, 1f),                         (1, 0, 1f),
            (-1, 1, 1.41421356f),  (0, 1, 1f),  (1, 1, 1.41421356f)
        };

        private readonly object gate = new();
        private readonly object leaseGate = new();
        private readonly WorldSettingsData settings;
        private readonly StreamingBaseTerrainEvaluator baseTerrain;
        private readonly StreamingBasinCandidateEvaluator basinCandidates;
        private readonly Dictionary<PlanningTileKey,
            Dictionary<StreamingCellKey, StreamingBaseTerrainFact>> baseTerrainTiles = new();
        private readonly Dictionary<StreamingBasinComponentId,
            StreamingBasinComponent> basinComponents = new();
        private readonly Dictionary<StreamingBasinComponentId, bool> basinActivity = new();
        private readonly HashSet<StreamingBasinComponentId> resolvingBasinActivity = new();
        private readonly Dictionary<PlanningTileKey, StreamingBasinAllocationTile>
            basinAllocationTiles = new();
        private readonly Dictionary<PlanningTileKey, StreamingTopologyEvaluation>
            topologyTiles = new();
        private readonly Dictionary<PlanningTileKey, StreamingEndpointTile>
            endpointTiles = new();
        private readonly Dictionary<StreamingRiverEdgeId, FeatureRiverEdge>
            riverEdges = new();
        private readonly Dictionary<StreamingRiverEdgeId, FeatureEdgeResolution>
            edgeResolutions = new();
        private readonly Dictionary<PlanningTileKey, StreamingRiverSpatialIndexTile>
            riverSpatialTiles = new();
        private readonly HashSet<PlanningTileKey> leasedTiles = new();
        private readonly HashSet<ChunkCoordinate> leasedChunks = new();
        private readonly Dictionary<int, WorldCellRectangle> patternMapLeases =
            new();
        private readonly Dictionary<ChunkCoordinate, HashSet<PlanningTileKey>>
            chunkDependencies = new();
        private readonly Dictionary<ChunkCoordinate, HashSet<PlanningTileKey>>
            pendingChunkDependencies = new();
        private HashSet<ChunkCoordinate> requestedLeaseChunks;
        private bool hasRequestedLeaseUpdate;
        private bool isBuildingRaster;
        private int nextPatternMapLeaseId;
        private HashSet<PlanningTileKey> activeDependencies;

        public StreamingFeatureWorld(WorldSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            baseTerrain = new StreamingBaseTerrainEvaluator(settings);
            basinCandidates = new StreamingBasinCandidateEvaluator(settings);
        }

        public WorldSettingsData Settings => settings;

        public void SetLeaseChunks(IEnumerable<ChunkCoordinate> chunks)
        {
            if (chunks == null)
            {
                throw new ArgumentNullException(nameof(chunks));
            }

            var requested = new HashSet<ChunkCoordinate>();
            foreach (var chunk in chunks)
            {
                requested.Add(chunk);
            }

            lock (leaseGate)
            {
                requestedLeaseChunks = requested;
                hasRequestedLeaseUpdate = true;
            }
        }

        internal StreamingPatternMapSession OpenPatternMapSession(
            in WorldCellRectangle rectangle)
        {
            lock (gate)
            {
                ApplyRequestedLeaseChunks();
                var leaseId = checked(nextPatternMapLeaseId + 1);
                nextPatternMapLeaseId = leaseId;
                patternMapLeases.Add(leaseId, rectangle);
                RebuildLeasedTiles();
                return new StreamingPatternMapSession(this, rectangle, leaseId);
            }
        }

        internal StreamingPatternMapSample SamplePatternMap(
            StreamingHydrologyCellQuery hydrology,
            in WorldCellRectangle rectangle,
            int worldX,
            int worldZ)
        {
            if (hydrology == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            if (!rectangle.Contains(worldX, worldZ))
            {
                throw new ArgumentOutOfRangeException(nameof(worldX));
            }

            lock (gate)
            {
                ApplyRequestedLeaseChunks();
                var terrainFact = SampleBaseTerrain(worldX, worldZ);
                var hydrologyCell = hydrology.Sample(worldX, worldZ);
                var combined = StreamingHydrologyRaster.Compose(
                    settings,
                    terrainFact.Terrain,
                    hydrologyCell);
                var hydrologyType = combined.HydrologyType != WaterType.None
                    ? combined.HydrologyType
                    : combined.WaterType;
                var membership = combined.HydrologyMembership;
                if (hydrologyType != WaterType.None && membership <= 0f)
                {
                    membership = 1f;
                }

                return new StreamingPatternMapSample(
                    worldX,
                    worldZ,
                    terrainFact.Terrain,
                    combined,
                    hydrologyType,
                    membership);
            }
        }

        internal void ReleasePatternMapSession(int leaseId)
        {
            lock (gate)
            {
                if (patternMapLeases.Remove(leaseId))
                {
                    ApplyRequestedLeaseChunks();
                    RebuildLeasedTiles();
                }
            }
        }

        public StreamingHydrologyRaster BuildRaster(in WorldCellRectangle rectangle)
        {
            lock (gate)
            {
                ApplyRequestedLeaseChunks();
                if (isBuildingRaster)
                {
                    throw new InvalidOperationException(
                        "Feature Raster construction cannot nest.");
                }

                isBuildingRaster = true;
                try
                {
                    return StreamingHydrologyRaster.BuildFromFeatures(this, rectangle);
                }
                finally
                {
                    isBuildingRaster = false;
                }
            }
        }

        public StreamingHydrologyRaster BuildRaster(
            in WorldCellRectangle rectangle,
            in ChunkCoordinate coordinate)
        {
            lock (gate)
            {
                ApplyRequestedLeaseChunks();
                if (isBuildingRaster || activeDependencies != null)
                {
                    throw new InvalidOperationException(
                        "Feature Raster construction cannot nest.");
                }

                isBuildingRaster = true;
                activeDependencies = new HashSet<PlanningTileKey>();
                try
                {
                    var raster = StreamingHydrologyRaster.BuildFromFeatures(this, rectangle);
                    pendingChunkDependencies[coordinate] = activeDependencies;
                    return raster;
                }
                finally
                {
                    isBuildingRaster = false;
                    activeDependencies = null;
                }
            }
        }

        internal void CommitChunkDependencies(in ChunkCoordinate coordinate)
        {
            lock (gate)
            {
                ApplyRequestedLeaseChunks();
                if (!pendingChunkDependencies.TryGetValue(coordinate,
                        out var dependencies))
                {
                    return;
                }

                pendingChunkDependencies.Remove(coordinate);

                if (leasedChunks.Contains(coordinate))
                {
                    chunkDependencies[coordinate] = dependencies;
                }

                RebuildLeasedTiles();
            }
        }

        internal void DiscardChunkDependencies(in ChunkCoordinate coordinate)
        {
            lock (gate)
            {
                ApplyRequestedLeaseChunks();
                if (pendingChunkDependencies.Remove(coordinate))
                {
                    RebuildLeasedTiles();
                }
            }
        }

        internal StreamingBaseTerrainFact SampleBaseTerrain(int worldX, int worldZ)
        {
            var tileKey = PlanningTileKey.FromCell(
                worldX,
                worldZ,
                settings.Hydrology.Map.PlanningRegionSizeCells);
            RecordDependency(tileKey);
            if (!baseTerrainTiles.TryGetValue(tileKey, out var tile))
            {
                tile = new Dictionary<StreamingCellKey, StreamingBaseTerrainFact>();
                baseTerrainTiles.Add(tileKey, tile);
            }

            var key = new StreamingCellKey(worldX, worldZ);
            if (!tile.TryGetValue(key, out var fact))
            {
                fact = baseTerrain.Sample(worldX, worldZ);
                tile.Add(key, fact);
            }

            return fact;
        }

        internal StreamingTopologyCell SampleTopology(int worldX, int worldZ)
        {
            var topology = GetTopologyTile(PlanningTileKey.FromCell(
                worldX,
                worldZ,
                settings.Hydrology.Map.PlanningRegionSizeCells));
            return topology.Sample(SampleBaseTerrain(worldX, worldZ), worldX, worldZ);
        }

        internal StreamingRiverSpatialIndexTile GetRiverSpatialTile(
            PlanningTileKey key)
        {
            RecordDependency(key);
            if (riverSpatialTiles.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var core = key.ToCore(settings.Hydrology.Map.PlanningRegionSizeCells);
            var required = core.Expand(checked((int)MathF.Ceiling(
                settings.Hydrology.RiverGraph.ConnectionRadiusCells.Maximum)
                + CorridorExtent()));
            var anchors = CollectEndpoints(required);
            var routesById = new Dictionary<StreamingRiverEdgeId,
                StreamingRiverRoutePlan>();
            var routeCore = core.Expand(CorridorExtent());
            for (var index = 0; index < anchors.Count; index++)
            {
                var route = GetRouteForAnchor(anchors[index]);
                if (route != null && StreamingRiverInteractionPlanner.IntersectsRoute(
                        route,
                        routeCore))
                {
                    routesById.TryAdd(route.Id, route);
                }
            }

            var routes = new List<StreamingRiverRoutePlan>(routesById.Values);
            routes.Sort((left, right) => left.Id.CompareTo(right.Id));
            var activeRoutes = new List<StreamingRiverRoutePlan>();
            for (var index = 0; index < routes.Count; index++)
            {
                if (GetEdgeResolution(routes[index]).IsActive)
                {
                    activeRoutes.Add(routes[index]);
                }
            }

            var junctions = BuildJunctions(activeRoutes, core);
            var tile = new StreamingRiverSpatialIndexTile(
                key,
                activeRoutes,
                junctions);
            riverSpatialTiles.Add(key, tile);
            return tile;
        }

        private StreamingBasinAllocationTile GetBasinAllocationTile(
            PlanningTileKey key)
        {
            RecordDependency(key);
            if (basinAllocationTiles.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var core = key.ToCore(settings.Hydrology.Map.PlanningRegionSizeCells);
            var ids = new List<StreamingBasinComponentId>();
            var active = new List<StreamingBasinComponent>();
            foreach (var id in EnumerateSeedCandidates(core))
            {
                var component = GetBasinCandidate(id);
                if (!core.Contains(component.SeedWorldX, component.SeedWorldZ))
                {
                    continue;
                }

                ids.Add(id);
                if (IsBasinActive(id))
                {
                    active.Add(component);
                }
            }

            ids.Sort();
            active.Sort((left, right) => left.Id.CompareTo(right.Id));
            var tile = new StreamingBasinAllocationTile(key, ids, active);
            basinAllocationTiles.Add(key, tile);
            return tile;
        }

        private StreamingBasinComponent GetBasinCandidate(
            StreamingBasinComponentId id)
        {
            if (basinComponents.TryGetValue(id, out var existing))
            {
                RecordDependency(PlanningTileKey.FromCell(existing.SeedWorldX,
                    existing.SeedWorldZ,
                    settings.Hydrology.Map.PlanningRegionSizeCells));
                return existing;
            }

            var seed = basinCandidates.Describe(id);
            RecordDependency(PlanningTileKey.FromCell(seed.SeedWorldX,
                seed.SeedWorldZ,
                settings.Hydrology.Map.PlanningRegionSizeCells));
            if (!seed.PassesOccurrence
                || SampleBaseTerrain(seed.SeedWorldX, seed.SeedWorldZ).HasSeaWater)
            {
                var inactive = basinCandidates.CreateInactive(seed);
                basinComponents.Add(id, inactive);
                return inactive;
            }

            var reach = settings.Hydrology.Basins.MaximumReachCells;
            var candidate = basinCandidates.Build(seed, new StreamingBasinFieldInput(
                checked(seed.SeedWorldX - reach),
                checked(seed.SeedWorldZ - reach),
                checked(reach * 2 + 1),
                SampleBaseTerrain,
                SampleBasinPotential));
            basinComponents.Add(id, candidate);
            return candidate;
        }

        private bool IsBasinActive(StreamingBasinComponentId id)
        {
            if (basinActivity.TryGetValue(id, out var existing))
            {
                return existing;
            }

            if (!resolvingBasinActivity.Add(id))
            {
                throw new InvalidOperationException(
                    "Basin priority resolution must follow a strict order.");
            }

            try
            {
                var component = GetBasinCandidate(id);
                var active = component.IsCandidate;
                if (active)
                {
                    foreach (var otherId in EnumerateConflictCandidates(component))
                    {
                        if (otherId.Equals(id))
                        {
                            continue;
                        }

                        var other = GetBasinCandidate(otherId);
                        if (!other.IsCandidate
                            || !IsHigherPriority(other, component)
                            || !CanConflict(component, other)
                            || !IsBasinActive(otherId))
                        {
                            continue;
                        }

                        active = false;
                        break;
                    }
                }

                basinActivity.Add(id, active);
                return active;
            }
            finally
            {
                resolvingBasinActivity.Remove(id);
            }
        }

        private List<StreamingBasinComponent> CollectActiveBasins(
            in WorldCellRectangle rectangle)
        {
            var extent = checked(settings.Hydrology.Basins.MaximumReachCells
                + settings.Hydrology.Basins.ShoreTransitionCells);
            var required = rectangle.Expand(extent);
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var minimumX = WorldCoordinateUtility.FloorDivide(required.MinimumX, size);
            var maximumX = WorldCoordinateUtility.FloorDivide(required.MaximumX, size);
            var minimumZ = WorldCoordinateUtility.FloorDivide(required.MinimumZ, size);
            var maximumZ = WorldCoordinateUtility.FloorDivide(required.MaximumZ, size);
            var result = new List<StreamingBasinComponent>();
            var added = new HashSet<StreamingBasinComponentId>();
            for (var tileZ = minimumZ; tileZ <= maximumZ; tileZ++)
            for (var tileX = minimumX; tileX <= maximumX; tileX++)
            {
                var tile = GetBasinAllocationTile(new PlanningTileKey(tileX, tileZ));
                for (var index = 0; index < tile.ActiveComponents.Count; index++)
                {
                    var component = tile.ActiveComponents[index];
                    if (added.Add(component.Id))
                    {
                        result.Add(component);
                    }
                }
            }

            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }

        private StreamingTopologyEvaluation GetTopologyTile(PlanningTileKey key)
        {
            RecordDependency(key);
            if (topologyTiles.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var core = key.ToCore(settings.Hydrology.Map.PlanningRegionSizeCells);
            var topology = new StreamingTopologyEvaluator(settings).CreateEvaluation(
                core,
                CollectActiveBasins(core));
            topologyTiles.Add(key, topology);
            return topology;
        }

        private StreamingEndpointTile GetEndpointTile(PlanningTileKey key)
        {
            RecordDependency(key);
            if (endpointTiles.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var core = key.ToCore(settings.Hydrology.Map.PlanningRegionSizeCells);
            var topology = GetTopologyTile(key);
            var components = CollectActiveBasins(core);
            var endpoints = new List<StreamingEndpoint>();
            for (var index = 0; index < components.Count; index++)
            {
                AddBasinEndpoint(components[index], core, topology, endpoints);
            }

            AddSeaEndpoints(core, endpoints);
            AddNaturalEndpoint(core, topology, endpoints);
            endpoints.Sort((left, right) => left.Id.CompareTo(right.Id));
            var tile = new StreamingEndpointTile(key, endpoints);
            endpointTiles.Add(key, tile);
            return tile;
        }

        private List<StreamingEndpoint> CollectEndpoints(in WorldCellRectangle rectangle)
        {
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var minimumX = WorldCoordinateUtility.FloorDivide(rectangle.MinimumX, size);
            var maximumX = WorldCoordinateUtility.FloorDivide(rectangle.MaximumX, size);
            var minimumZ = WorldCoordinateUtility.FloorDivide(rectangle.MinimumZ, size);
            var maximumZ = WorldCoordinateUtility.FloorDivide(rectangle.MaximumZ, size);
            var result = new List<StreamingEndpoint>();
            for (var tileZ = minimumZ; tileZ <= maximumZ; tileZ++)
            for (var tileX = minimumX; tileX <= maximumX; tileX++)
            {
                var tile = GetEndpointTile(new PlanningTileKey(tileX, tileZ));
                for (var index = 0; index < tile.Endpoints.Count; index++)
                {
                    var endpoint = tile.Endpoints[index];
                    if (rectangle.Contains(endpoint.WorldX, endpoint.WorldZ))
                    {
                        result.Add(endpoint);
                    }
                }
            }

            result.Sort((left, right) => left.Id.CompareTo(right.Id));
            return result;
        }

        private StreamingRiverRoutePlan GetRouteForAnchor(in StreamingEndpoint anchor)
        {
            var radius = checked((int)MathF.Ceiling(
                settings.Hydrology.RiverGraph.ConnectionRadiusCells.Maximum));
            var candidates = BuildCandidates(anchor, CollectEndpoints(new WorldCellRectangle(
                checked(anchor.WorldX - radius),
                checked(anchor.WorldZ - radius),
                checked(anchor.WorldX + radius + 1),
                checked(anchor.WorldZ + radius + 1))));
            return candidates.Count == 0 ? null : GetRoute(candidates[0]);
        }

        private List<StreamingRiverCandidate> BuildCandidates(
            in StreamingEndpoint anchor,
            IReadOnlyList<StreamingEndpoint> endpoints)
        {
            var byKind = new Dictionary<StreamingEndpointKind,
                List<StreamingRiverCandidate>>();
            for (var index = 0; index < endpoints.Count; index++)
            {
                var target = endpoints[index];
                if (target.Id.Equals(anchor.Id))
                {
                    continue;
                }

                var id = new StreamingRiverEdgeId(anchor.Id, target.Id);
                var distance = StreamingRiverMath.Distance(
                    anchor.WorldX,
                    anchor.WorldZ,
                    target.WorldX,
                    target.WorldZ);
                var radius = ResolveConnectionRadius(id);
                if (distance > radius)
                {
                    continue;
                }

                if (!byKind.TryGetValue(target.Kind, out var candidates))
                {
                    candidates = new List<StreamingRiverCandidate>();
                    byKind.Add(target.Kind, candidates);
                }

                candidates.Add(new StreamingRiverCandidate(
                    id,
                    anchor,
                    target,
                    distance,
                    radius));
            }

            var firstByKind = new List<StreamingRiverCandidate>();
            foreach (var candidates in byKind.Values)
            {
                candidates.Sort(StreamingRiverCandidate.Compare);
                firstByKind.Add(candidates[0]);
            }

            if (firstByKind.Count == 0)
            {
                return firstByKind;
            }

            firstByKind.Sort(StreamingRiverCandidate.Compare);
            var naturalIndex = -1;
            for (var index = 0; index < firstByKind.Count; index++)
            {
                if (firstByKind[index].Target.Kind == StreamingEndpointKind.Natural)
                {
                    naturalIndex = index;
                    break;
                }
            }

            if (naturalIndex >= 0 && DeterministicNoise.Value01(
                    StreamingRiverMath.EndpointHash(anchor.Id),
                    firstByKind.Count,
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.RiverGraph.Natural.Selection"))
                < 1f / firstByKind.Count)
            {
                var natural = firstByKind[naturalIndex];
                firstByKind.RemoveAt(naturalIndex);
                firstByKind.Insert(0, natural);
            }

            return firstByKind;
        }

        private StreamingRiverRoutePlan GetRoute(in StreamingRiverCandidate candidate)
        {
            RecordDependency(PlanningTileKey.FromCell(candidate.Id.First.WorldX,
                candidate.Id.First.WorldZ,
                settings.Hydrology.Map.PlanningRegionSizeCells));
            if (riverEdges.TryGetValue(candidate.Id, out var existing))
            {
                return existing.Route;
            }

            var first = candidate.Id.First.Equals(candidate.Anchor.Id)
                ? candidate.Anchor
                : candidate.Target;
            var second = candidate.Id.Second.Equals(candidate.Anchor.Id)
                ? candidate.Anchor
                : candidate.Target;
            var route = FindRoute(first, second, candidate.CandidateRadiusCells);
            if (route == null)
            {
                riverEdges.Add(candidate.Id, new FeatureRiverEdge(null));
                return null;
            }

            var corridor = settings.Hydrology.RiverCorridor;
            var width = StreamingRiverMath.ResolveRange(
                corridor.WidthCells,
                DeterministicNoise.Value01(
                    StreamingRiverMath.EndpointHash(candidate.Id.First),
                    StreamingRiverMath.EndpointHash(candidate.Id.Second),
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.RiverGraph.Corridor")));
            var depth = StreamingRiverMath.ResolveRange(
                corridor.DepthUnits,
                DeterministicNoise.Value01(
                    StreamingRiverMath.EndpointHash(candidate.Id.Second),
                    StreamingRiverMath.EndpointHash(candidate.Id.First),
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.RiverGraph.Depth")));
            var amplitude = StreamingRiverMath.ResolveRange(
                corridor.RiverbedAmplitudeUnits,
                DeterministicNoise.Value01(
                    StreamingRiverMath.EndpointHash(candidate.Id.First),
                    StreamingRiverMath.EndpointHash(candidate.Id.Second),
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.RiverGraph.BedAmplitude")));
            var plan = new StreamingRiverRoutePlan(
                candidate.Id,
                first,
                second,
                candidate.CandidateRadiusCells,
                width,
                depth,
                DeterministicNoise.DeriveSeed(settings.Seed,
                    "Hydrology.RiverGraph.Bed"),
                amplitude,
                BuildRoutePoints(first, second, route, width, depth));
            riverEdges.Add(candidate.Id, new FeatureRiverEdge(plan));
            return plan;
        }

        private List<(int x, int z)> FindRoute(
            in StreamingEndpoint first,
            in StreamingEndpoint second,
            float radius)
        {
            var spacing = settings.Hydrology.Map.RouteSampleSpacingCells;
            var startX = RoundToSpacing(first.WorldX, spacing);
            var startZ = RoundToSpacing(first.WorldZ, spacing);
            var endX = RoundToSpacing(second.WorldX, spacing);
            var endZ = RoundToSpacing(second.WorldZ, spacing);
            var gridRadius = (int)MathF.Ceiling(radius);
            var cells = new Dictionary<StreamingCellKey, FeatureRouteCell>();
            if (!HasClearConnector(first.WorldX, first.WorldZ, startX, startZ,
                    first, second, cells)
                || !HasClearConnector(endX, endZ, second.WorldX, second.WorldZ,
                    first, second, cells))
            {
                return null;
            }

            var minimumGridX = WorldCoordinateUtility.FloorDivide(
                checked(Math.Min(startX, endX) - gridRadius), spacing);
            var maximumGridX = WorldCoordinateUtility.FloorDivide(
                checked(Math.Max(startX, endX) + gridRadius), spacing);
            var minimumGridZ = WorldCoordinateUtility.FloorDivide(
                checked(Math.Min(startZ, endZ) - gridRadius), spacing);
            var maximumGridZ = WorldCoordinateUtility.FloorDivide(
                checked(Math.Max(startZ, endZ) + gridRadius), spacing);
            var width = checked(maximumGridX - minimumGridX + 1);
            var height = checked(maximumGridZ - minimumGridZ + 1);
            var start = checked(startX / spacing - minimumGridX)
                + width * checked(startZ / spacing - minimumGridZ);
            var end = checked(endX / spacing - minimumGridX)
                + width * checked(endZ / spacing - minimumGridZ);
            var count = checked(width * height);
            var costs = new float[count];
            var previous = new int[count];
            var closed = new bool[count];
            Array.Fill(costs, float.PositiveInfinity);
            Array.Fill(previous, -1);
            costs[start] = 0f;
            var frontier = new StreamingMinHeap();
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
                var worldX = checked((minimumGridX + localX) * spacing);
                var worldZ = checked((minimumGridZ + localZ) * spacing);
                var currentCell = GetRouteCell(worldX, worldZ, cells);
                for (var direction = 0; direction < routeNeighbors.Length; direction++)
                {
                    var neighbor = routeNeighbors[direction];
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

                    var nextWorldX = checked((minimumGridX + nextX) * spacing);
                    var nextWorldZ = checked((minimumGridZ + nextZ) * spacing);
                    if (next != end && next != start
                        && (!IsInsideLens(nextWorldX, nextWorldZ, first, second,
                                radius)
                            || IsBlocked(nextWorldX, nextWorldZ, first, second,
                                cells)))
                    {
                        continue;
                    }

                    var nextCell = GetRouteCell(nextWorldX, nextWorldZ, cells);
                    var nextCost = costs[current] + neighbor.distance * GetRouteStepCost(
                        currentCell,
                        nextCell,
                        nextWorldX,
                        nextWorldZ,
                        neighbor.distance);
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
                gridRoute.Add(((minimumGridX + localX) * spacing,
                    (minimumGridZ + localZ) * spacing));
                if (current == start)
                {
                    break;
                }
            }

            gridRoute.Reverse();
            var result = new List<(int x, int z)>(gridRoute.Count + 2);
            AddDistinct(result, (first.WorldX, first.WorldZ));
            for (var index = 0; index < gridRoute.Count; index++)
            {
                AddDistinct(result, gridRoute[index]);
            }

            AddDistinct(result, (second.WorldX, second.WorldZ));
            return result.Count >= 2 ? result : null;
        }

        private FeatureEdgeResolution GetEdgeResolution(
            StreamingRiverRoutePlan route)
        {
            if (edgeResolutions.TryGetValue(route.Id, out var existing))
            {
                return existing;
            }

            var radius = checked((int)MathF.Ceiling(
                settings.Hydrology.RiverGraph.ConnectionRadiusCells.Maximum)
                + CorridorExtent());
            var required = new WorldCellRectangle(
                checked(route.MinimumX - radius),
                checked(route.MinimumZ - radius),
                checked(route.MaximumX + radius + 1),
                checked(route.MaximumZ + radius + 1));
            var active = true;
            var candidates = CollectEndpoints(required);
            for (var index = 0; index < candidates.Count; index++)
            {
                var other = GetRouteForAnchor(candidates[index]);
                if (other == null || other.Id.Equals(route.Id))
                {
                    continue;
                }

                var interaction = StreamingRiverInteractionPlanner.Build(
                    settings,
                    new[] { route, other });
                if (!interaction.EdgeResolutions[route.Id].IsActive)
                {
                    active = false;
                    break;
                }
            }

            var resolution = new FeatureEdgeResolution(active);
            edgeResolutions.Add(route.Id, resolution);
            return resolution;
        }

        private List<StreamingRiverJunctionPlan> BuildJunctions(
            IReadOnlyList<StreamingRiverRoutePlan> routes,
            in WorldCellRectangle core)
        {
            var junctions = new Dictionary<StreamingCellKey, FeatureJunction>();
            for (var firstIndex = 0; firstIndex < routes.Count; firstIndex++)
            for (var secondIndex = firstIndex + 1;
                 secondIndex < routes.Count;
                 secondIndex++)
            {
                var first = routes[firstIndex];
                var second = routes[secondIndex];
                var interaction = StreamingRiverInteractionPlanner.Build(
                    settings,
                    new[] { first, second });
                if (!interaction.EdgeResolutions[first.Id].IsActive
                    || !interaction.EdgeResolutions[second.Id].IsActive
                    || !GetEdgeResolution(first).IsActive
                    || !GetEdgeResolution(second).IsActive)
                {
                    continue;
                }

                foreach (var pair in interaction.Junctions)
                {
                    var plan = pair.Value;
                    if (!core.Contains(plan.WorldX, plan.WorldZ))
                    {
                        continue;
                    }

                    if (!junctions.TryGetValue(pair.Key, out var junction))
                    {
                        junction = new FeatureJunction(plan.WorldX, plan.WorldZ,
                            plan.WaterTopUnits, plan.TargetTerrainSurfaceUnits);
                        junctions.Add(pair.Key, junction);
                    }

                    for (var edgeIndex = 0; edgeIndex < plan.Edges.Count; edgeIndex++)
                    {
                        junction.Add(plan.Edges[edgeIndex]);
                    }

                    junction.Combine(plan.WaterTopUnits,
                        plan.TargetTerrainSurfaceUnits);
                }
            }

            var result = new List<StreamingRiverJunctionPlan>();
            foreach (var pair in junctions)
            {
                if (pair.Value.EdgeCount >= 2)
                {
                    result.Add(pair.Value.ToPlan());
                }
            }

            result.Sort((left, right) =>
            {
                var x = left.WorldX.CompareTo(right.WorldX);
                return x != 0 ? x : left.WorldZ.CompareTo(right.WorldZ);
            });
            return result;
        }

        private List<StreamingRiverRoutePoint> BuildRoutePoints(
            in StreamingEndpoint first,
            in StreamingEndpoint second,
            IReadOnlyList<(int x, int z)> route,
            float width,
            float depth)
        {
            var distances = new float[route.Count];
            for (var index = 1; index < route.Count; index++)
            {
                distances[index] = distances[index - 1] + StreamingRiverMath.Distance(
                    route[index - 1].x,
                    route[index - 1].z,
                    route[index].x,
                    route[index].z);
            }

            var total = distances[^1];
            var points = new List<StreamingRiverRoutePoint>(route.Count);
            for (var index = 0; index < route.Count; index++)
            {
                var point = route[index];
                var progress = total > 0f ? distances[index] / total : 0f;
                var waterTop = (int)MathF.Round(first.WaterTopUnits
                    + (second.WaterTopUnits - first.WaterTopUnits) * progress,
                    MidpointRounding.AwayFromZero);
                var transition = 1f;
                if (first.Kind == StreamingEndpointKind.Natural)
                {
                    transition = Math.Min(transition, EvaluateNaturalTransition(
                        distances[index]
                        / settings.Hydrology.RiverGraph.NaturalTransitionCells));
                }

                if (second.Kind == StreamingEndpointKind.Natural)
                {
                    transition = Math.Min(transition, EvaluateNaturalTransition(
                        (total - distances[index])
                        / settings.Hydrology.RiverGraph.NaturalTransitionCells));
                }

                points.Add(new StreamingRiverRoutePoint(
                    point.x,
                    point.z,
                    waterTop,
                    Math.Max(0, waterTop - (int)MathF.Round(
                        depth * transition,
                        MidpointRounding.AwayFromZero)),
                    width * transition,
                    transition));
            }

            return points;
        }

        private bool HasClearConnector(
            int fromX,
            int fromZ,
            int toX,
            int toZ,
            in StreamingEndpoint first,
            in StreamingEndpoint second,
            IDictionary<StreamingCellKey, FeatureRouteCell> cells)
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
                if (IsBlocked(currentX, currentZ, first, second, cells))
                {
                    return false;
                }

                if (currentX == toX && currentZ == toZ)
                {
                    return true;
                }

                var twice = error * 2;
                if (twice > -deltaZ)
                {
                    error -= deltaZ;
                    currentX += stepX;
                }

                if (twice < deltaX)
                {
                    error += deltaX;
                    currentZ += stepZ;
                }
            }
        }

        private static bool IsInsideLens(
            int worldX,
            int worldZ,
            in StreamingEndpoint first,
            in StreamingEndpoint second,
            float radius) => StreamingRiverMath.Distance(worldX, worldZ,
                first.WorldX, first.WorldZ) <= radius
                && StreamingRiverMath.Distance(worldX, worldZ,
                    second.WorldX, second.WorldZ) <= radius;

        private bool IsBlocked(
            int worldX,
            int worldZ,
            in StreamingEndpoint first,
            in StreamingEndpoint second,
            IDictionary<StreamingCellKey, FeatureRouteCell> cells)
        {
            if (worldX == first.WorldX && worldZ == first.WorldZ
                || worldX == second.WorldX && worldZ == second.WorldZ)
            {
                return false;
            }

            var cell = GetRouteCell(worldX, worldZ, cells);
            return cell.Topology.HasWater || cell.Topology.IsBasinProtected;
        }

        private FeatureRouteCell GetRouteCell(
            int worldX,
            int worldZ,
            IDictionary<StreamingCellKey, FeatureRouteCell> cells)
        {
            var key = new StreamingCellKey(worldX, worldZ);
            if (cells.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var baseFact = SampleBaseTerrain(worldX, worldZ);
            var cell = new FeatureRouteCell(baseFact, SampleTopology(worldX, worldZ));
            cells.Add(key, cell);
            return cell;
        }

        private float GetRouteStepCost(
            in FeatureRouteCell current,
            in FeatureRouteCell next,
            int worldX,
            int worldZ,
            float movement)
        {
            var network = settings.Hydrology.RiverNetwork;
            var elevation = MathF.Abs(next.BaseTerrain.Surface.SurfaceUnits
                - current.BaseTerrain.Surface.SurfaceUnits)
                / WorldGrid.HeightStepsPerCell;
            var slope = elevation / movement;
            var variation = ToUnit(WorldNoiseFieldSampler.Sample2D(
                worldX,
                worldZ,
                network.RouteVariationField,
                DeterministicNoise.DeriveSeed(settings.Seed,
                    "Hydrology.RiverGraph.RouteVariation")),
                network.RouteVariationField.Mode);
            var valley = Math.Clamp(1f - next.BaseTerrain.Field.PeaksValleys, 0f, 1f);
            return (1f + elevation * settings.Hydrology.RiverGraph.ElevationChangeCost
                + slope * network.CrossSlopeCost
                + variation * network.RouteVariationCost)
                / (1f + valley * network.ValleyPreference);
        }

        private void AddBasinEndpoint(
            StreamingBasinComponent component,
            in WorldCellRectangle core,
            StreamingTopologyEvaluation topology,
            List<StreamingEndpoint> endpoints)
        {
            if (!core.Contains(component.SeedWorldX, component.SeedWorldZ))
            {
                return;
            }

            var selected = -1;
            var size = core.MaximumXExclusive - core.MinimumX;
            for (var index = 0; index < component.Footprint.Count; index++)
            {
                var cell = component.Footprint[index];
                if (!core.Contains(cell.WorldX, cell.WorldZ))
                {
                    continue;
                }

                var local = cell.WorldX - core.MinimumX
                    + size * (cell.WorldZ - core.MinimumZ);
                if (topology.Sample(SampleBaseTerrain(cell.WorldX, cell.WorldZ),
                        cell.WorldX, cell.WorldZ).WaterType != component.Id.Type
                    || selected >= 0 && local >= selected)
                {
                    continue;
                }

                selected = local;
            }

            if (selected < 0)
            {
                return;
            }

            var kind = component.Id.Type == WaterType.Lake
                ? StreamingEndpointKind.Lake
                : StreamingEndpointKind.Pond;
            endpoints.Add(new StreamingEndpoint(new StreamingEndpointId(
                kind,
                checked(core.MinimumX + selected % size),
                checked(core.MinimumZ + selected / size),
                component.Id), component.WaterTopUnits));
        }

        private void AddSeaEndpoints(
            in WorldCellRectangle core,
            List<StreamingEndpoint> endpoints)
        {
            var spacing = settings.Hydrology.Map.RouteSampleSpacingCells;
            for (var worldZ = core.MinimumZ;
                 worldZ < core.MaximumZExclusive;
                 worldZ++)
            for (var worldX = core.MinimumX;
                 worldX < core.MaximumXExclusive;
                 worldX++)
            {
                if (WorldCoordinateUtility.FloorDivide(worldX, spacing) * spacing
                    != worldX
                    || WorldCoordinateUtility.FloorDivide(worldZ, spacing) * spacing
                    != worldZ)
                {
                    continue;
                }

                var sample = SampleBaseTerrain(worldX, worldZ);
                if (!sample.HasSeaWater
                    || SampleBaseTerrain(checked(worldX - spacing), worldZ).HasSeaWater
                    && SampleBaseTerrain(checked(worldX + spacing), worldZ).HasSeaWater
                    && SampleBaseTerrain(worldX, checked(worldZ - spacing)).HasSeaWater
                    && SampleBaseTerrain(worldX, checked(worldZ + spacing)).HasSeaWater)
                {
                    continue;
                }

                endpoints.Add(new StreamingEndpoint(new StreamingEndpointId(
                    StreamingEndpointKind.Sea,
                    worldX,
                    worldZ,
                    default), sample.SeaWaterTopUnits));
            }
        }

        private void AddNaturalEndpoint(
            in WorldCellRectangle core,
            StreamingTopologyEvaluation topology,
            List<StreamingEndpoint> endpoints)
        {
            var seed = DeterministicNoise.DeriveSeed(settings.Seed,
                "Hydrology.RiverGraph.Natural.Endpoint");
            var found = false;
            var selectedX = 0;
            var selectedZ = 0;
            var selectedScore = 0f;
            var selected = default(StreamingTopologyCell);
            for (var worldZ = core.MinimumZ;
                 worldZ < core.MaximumZExclusive;
                 worldZ++)
            for (var worldX = core.MinimumX;
                 worldX < core.MaximumXExclusive;
                 worldX++)
            {
                var cell = topology.Sample(SampleBaseTerrain(worldX, worldZ),
                    worldX, worldZ);
                if (cell.HasWater || cell.IsBasinProtected)
                {
                    continue;
                }

                var score = DeterministicNoise.Value01(worldX, worldZ, seed);
                if (!found || score > selectedScore || score == selectedScore
                    && (worldX < selectedX
                        || worldX == selectedX && worldZ < selectedZ))
                {
                    found = true;
                    selectedX = worldX;
                    selectedZ = worldZ;
                    selectedScore = score;
                    selected = cell;
                }
            }

            if (found)
            {
                endpoints.Add(new StreamingEndpoint(new StreamingEndpointId(
                    StreamingEndpointKind.Natural,
                    selectedX,
                    selectedZ,
                    default), selected.TargetTerrainSurfaceUnits));
            }
        }

        private IEnumerable<StreamingBasinComponentId> EnumerateSeedCandidates(
            WorldCellRectangle core)
        {
            var spacing = settings.Hydrology.Map.BasinSeedSpacingCells;
            var minimumX = WorldCoordinateUtility.FloorDivide(
                checked(core.MinimumX - (spacing - 1)), spacing);
            var maximumX = WorldCoordinateUtility.FloorDivide(core.MaximumX, spacing);
            var minimumZ = WorldCoordinateUtility.FloorDivide(
                checked(core.MinimumZ - (spacing - 1)), spacing);
            var maximumZ = WorldCoordinateUtility.FloorDivide(core.MaximumZ, spacing);
            for (var seedZ = minimumZ; seedZ <= maximumZ; seedZ++)
            for (var seedX = minimumX; seedX <= maximumX; seedX++)
            {
                yield return new StreamingBasinComponentId(WaterType.Lake,
                    seedX, seedZ);
                yield return new StreamingBasinComponentId(WaterType.Pond,
                    seedX, seedZ);
            }
        }

        private IEnumerable<StreamingBasinComponentId> EnumerateConflictCandidates(
            StreamingBasinComponent component)
        {
            var spacing = settings.Hydrology.Map.BasinSeedSpacingCells;
            var distance = checked(settings.Hydrology.Basins.MaximumReachCells * 2
                + settings.Hydrology.Basins.MinimumSeparationCells);
            var radius = checked((distance + spacing - 1) / spacing);
            for (var seedZ = component.Id.SeedGridZ - radius;
                 seedZ <= component.Id.SeedGridZ + radius;
                 seedZ++)
            for (var seedX = component.Id.SeedGridX - radius;
                 seedX <= component.Id.SeedGridX + radius;
                 seedX++)
            {
                yield return new StreamingBasinComponentId(WaterType.Lake,
                    seedX, seedZ);
                yield return new StreamingBasinComponentId(WaterType.Pond,
                    seedX, seedZ);
            }
        }

        private bool CanConflict(
            StreamingBasinComponent first,
            StreamingBasinComponent second)
        {
            var clearance = settings.Hydrology.Basins.MinimumSeparationCells;
            if (first.MaximumX + clearance < second.MinimumX
                || second.MaximumX + clearance < first.MinimumX
                || first.MaximumZ + clearance < second.MinimumZ
                || second.MaximumZ + clearance < first.MinimumZ)
            {
                return false;
            }

            for (var index = 0; index < first.Footprint.Count; index++)
            {
                var cell = first.Footprint[index];
                for (var offsetZ = -clearance; offsetZ <= clearance; offsetZ++)
                for (var offsetX = -clearance; offsetX <= clearance; offsetX++)
                {
                    if (Math.Max(Math.Abs(offsetX), Math.Abs(offsetZ)) > clearance)
                    {
                        continue;
                    }

                    if (second.Contains(checked(cell.WorldX + offsetX),
                            checked(cell.WorldZ + offsetZ)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private float SampleBasinPotential(int worldX, int worldZ)
        {
            var value = WorldNoiseFieldSampler.Sample2D(worldX, worldZ,
                settings.Hydrology.Map.BasinPotentialField,
                DeterministicNoise.DeriveSeed(settings.Seed,
                    "Hydrology.Topology.Basin.Potential"));
            return settings.Hydrology.Map.BasinPotentialResponse.Evaluate(ToUnit(
                value,
                settings.Hydrology.Map.BasinPotentialField.Mode));
        }

        private float ResolveConnectionRadius(in StreamingRiverEdgeId id) =>
            StreamingRiverMath.ResolveRange(
                settings.Hydrology.RiverGraph.ConnectionRadiusCells,
                DeterministicNoise.Value01(
                    StreamingRiverMath.EndpointHash(id.First),
                    StreamingRiverMath.EndpointHash(id.Second),
                    DeterministicNoise.DeriveSeed(settings.Seed,
                        "Hydrology.RiverGraph.ConnectionRadius")));

        private float EvaluateNaturalTransition(float progress)
        {
            progress = Math.Clamp(progress, 0f, 1f);
            var curve = settings.Hydrology.RiverGraph.NaturalTransitionRate;
            var integral = IntegrateRate(curve, progress);
            var total = IntegrateRate(curve, 1f);
            return total > 0f ? Math.Clamp(integral / total, 0f, 1f) : 0f;
        }

        private static float IntegrateRate(in WorldCurveSettingsData curve, float progress)
        {
            progress = Math.Clamp(progress, 0f, 1f);
            var segmentCount = Math.Min(4, (int)MathF.Floor(progress * 4f));
            var integral = 0f;
            for (var segment = 0; segment < segmentCount; segment++)
            {
                integral += 0.125f * (GetRateValue(curve, segment)
                    + GetRateValue(curve, segment + 1));
            }

            if (segmentCount == 4)
            {
                return integral;
            }

            var local = progress * 4f - segmentCount;
            var from = GetRateValue(curve, segmentCount);
            var to = GetRateValue(curve, segmentCount + 1);
            return integral + 0.25f * (from * local + (to - from)
                * (local * local * local - 0.5f * local * local * local * local));
        }

        private void RecordDependency(PlanningTileKey key) =>
            activeDependencies?.Add(key);

        private int DirectLeaseExtent() => Math.Max(
            checked(settings.Hydrology.Basins.MaximumReachCells
                + settings.Hydrology.Basins.ShoreTransitionCells),
            checked((int)MathF.Ceiling(
                settings.Hydrology.RiverGraph.ConnectionRadiusCells.Maximum)
                + CorridorExtent()));

        private void ApplyRequestedLeaseChunks()
        {
            HashSet<ChunkCoordinate> requested;
            lock (leaseGate)
            {
                if (!hasRequestedLeaseUpdate)
                {
                    return;
                }

                requested = requestedLeaseChunks;
                requestedLeaseChunks = null;
                hasRequestedLeaseUpdate = false;
            }

            leasedChunks.Clear();
            leasedChunks.UnionWith(requested);
            RemoveReleasedChunkDependencies();
            RebuildLeasedTiles();
        }

        private void RemoveReleasedChunkDependencies()
        {
            var released = new List<ChunkCoordinate>();
            foreach (var pair in chunkDependencies)
            {
                if (!leasedChunks.Contains(pair.Key))
                {
                    released.Add(pair.Key);
                }
            }

            for (var index = 0; index < released.Count; index++)
            {
                chunkDependencies.Remove(released[index]);
            }
        }

        private void RebuildLeasedTiles()
        {
            var tiles = new HashSet<PlanningTileKey>();
            foreach (var chunk in leasedChunks)
            {
                AddLeasedTiles(WorldCellRectangle.FromChunk(chunk,
                    settings.ChunkCellCountXZ).Expand(DirectLeaseExtent()), tiles);
            }

            foreach (var rectangle in patternMapLeases.Values)
            {
                AddLeasedTiles(rectangle.Expand(DirectLeaseExtent()), tiles);
            }

            foreach (var pair in chunkDependencies)
            {
                tiles.UnionWith(pair.Value);
            }

            foreach (var pair in pendingChunkDependencies)
            {
                tiles.UnionWith(pair.Value);
            }

            ApplyLeasedTiles(tiles);
        }

        private void ApplyLeasedTiles(HashSet<PlanningTileKey> tiles)
        {
            leasedTiles.Clear();
            leasedTiles.UnionWith(tiles);
            TrimUnleasedFeatures();
        }

        private void AddLeasedTiles(
            in WorldCellRectangle rectangle,
            HashSet<PlanningTileKey> tiles)
        {
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var minimumX = WorldCoordinateUtility.FloorDivide(rectangle.MinimumX, size);
            var maximumX = WorldCoordinateUtility.FloorDivide(rectangle.MaximumX, size);
            var minimumZ = WorldCoordinateUtility.FloorDivide(rectangle.MinimumZ, size);
            var maximumZ = WorldCoordinateUtility.FloorDivide(rectangle.MaximumZ, size);
            for (var z = minimumZ; z <= maximumZ; z++)
            for (var x = minimumX; x <= maximumX; x++)
            {
                tiles.Add(new PlanningTileKey(x, z));
            }
        }

        private void TrimUnleasedFeatures()
        {
            RemoveUnleased(baseTerrainTiles);
            RemoveUnleased(basinAllocationTiles);
            RemoveUnleased(topologyTiles);
            RemoveUnleased(endpointTiles);
            RemoveUnleased(riverSpatialTiles);
            var removeCandidates = new List<StreamingBasinComponentId>();
            foreach (var pair in basinComponents)
            {
                var key = PlanningTileKey.FromCell(pair.Value.SeedWorldX,
                    pair.Value.SeedWorldZ,
                    settings.Hydrology.Map.PlanningRegionSizeCells);
                if (!leasedTiles.Contains(key))
                {
                    removeCandidates.Add(pair.Key);
                }
            }

            for (var index = 0; index < removeCandidates.Count; index++)
            {
                basinComponents.Remove(removeCandidates[index]);
                basinActivity.Remove(removeCandidates[index]);
            }

            var removeEdges = new List<StreamingRiverEdgeId>();
            foreach (var pair in riverEdges)
            {
                var key = PlanningTileKey.FromCell(pair.Key.First.WorldX,
                    pair.Key.First.WorldZ,
                    settings.Hydrology.Map.PlanningRegionSizeCells);
                if (!leasedTiles.Contains(key))
                {
                    removeEdges.Add(pair.Key);
                }
            }

            for (var index = 0; index < removeEdges.Count; index++)
            {
                riverEdges.Remove(removeEdges[index]);
                edgeResolutions.Remove(removeEdges[index]);
            }
        }

        private void RemoveUnleased<T>(Dictionary<PlanningTileKey, T> values)
        {
            var keys = new List<PlanningTileKey>();
            foreach (var pair in values)
            {
                if (!leasedTiles.Contains(pair.Key))
                {
                    keys.Add(pair.Key);
                }
            }

            for (var index = 0; index < keys.Count; index++)
            {
                values.Remove(keys[index]);
            }
        }

        private int CorridorExtent() => (int)MathF.Ceiling(
            settings.Hydrology.RiverCorridor.WidthCells.Maximum * 0.5f
            + settings.Hydrology.RiverCorridor.BankMarginCells);

        private static bool IsHigherPriority(
            StreamingBasinComponent candidate,
            StreamingBasinComponent other) => candidate.Priority > other.Priority
                || candidate.Priority == other.Priority
                && candidate.Id.CompareTo(other.Id) < 0;

        private static float ToUnit(float value, WorldNoiseMode mode)
        {
            if (mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge)
            {
                value = (value + 1f) * 0.5f;
            }

            return Math.Clamp(value, 0f, 1f);
        }

        private static int RoundToSpacing(int value, int spacing) => checked((int)Math.Round(
            value / (double)spacing,
            MidpointRounding.AwayFromZero)) * spacing;

        private static float GetRateValue(in WorldCurveSettingsData curve, int index) =>
            index switch
            {
                0 => curve.AtZero,
                1 => curve.AtQuarter,
                2 => curve.AtHalf,
                3 => curve.AtThreeQuarters,
                _ => curve.AtOne
            };

        private static void AddDistinct(ICollection<(int x, int z)> route,
            (int x, int z) point)
        {
            if (route is List<(int x, int z)> list && list.Count > 0
                && list[^1] == point)
            {
                return;
            }

            route.Add(point);
        }

        private sealed class FeatureRiverEdge
        {
            public FeatureRiverEdge(StreamingRiverRoutePlan route)
            {
                Route = route;
            }

            public StreamingRiverRoutePlan Route { get; }
        }

        private readonly struct FeatureRouteCell
        {
            public FeatureRouteCell(StreamingBaseTerrainFact baseTerrain,
                StreamingTopologyCell topology)
            {
                BaseTerrain = baseTerrain;
                Topology = topology;
            }

            public StreamingBaseTerrainFact BaseTerrain { get; }
            public StreamingTopologyCell Topology { get; }
        }

        private readonly struct FeatureEdgeResolution
        {
            public FeatureEdgeResolution(bool isActive)
            {
                IsActive = isActive;
            }

            public bool IsActive { get; }
        }

        private sealed class FeatureJunction
        {
            private readonly List<StreamingRiverEdgeId> edges = new();

            public FeatureJunction(int worldX, int worldZ, int waterTopUnits,
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
            public int EdgeCount => edges.Count;

            public void Add(StreamingRiverEdgeId edge)
            {
                if (!edges.Contains(edge))
                {
                    edges.Add(edge);
                    edges.Sort((left, right) => left.CompareTo(right));
                }
            }

            public void Combine(int waterTopUnits, int targetTerrainSurfaceUnits)
            {
                WaterTopUnits = Math.Min(WaterTopUnits, waterTopUnits);
                TargetTerrainSurfaceUnits = Math.Min(TargetTerrainSurfaceUnits,
                    targetTerrainSurfaceUnits);
                TargetTerrainSurfaceUnits = Math.Min(TargetTerrainSurfaceUnits,
                    WaterTopUnits);
            }

            public StreamingRiverJunctionPlan ToPlan() => new(WorldX, WorldZ,
                WaterTopUnits, TargetTerrainSurfaceUnits, edges);
        }
    }

}
