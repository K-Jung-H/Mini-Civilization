using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Streaming
{
    internal readonly struct StreamingRiverEdgeId :
        IEquatable<StreamingRiverEdgeId>, IComparable<StreamingRiverEdgeId>
    {
        public StreamingRiverEdgeId(
            in StreamingEndpointId first,
            in StreamingEndpointId second)
        {
            if (first.Equals(second))
            {
                throw new ArgumentException(
                    "A River Edge requires two different Endpoints.");
            }

            if (first.CompareTo(second) <= 0)
            {
                First = first;
                Second = second;
            }
            else
            {
                First = second;
                Second = first;
            }
        }

        public StreamingEndpointId First { get; }
        public StreamingEndpointId Second { get; }

        public int CompareTo(StreamingRiverEdgeId other)
        {
            var first = First.CompareTo(other.First);
            return first != 0 ? first : Second.CompareTo(other.Second);
        }

        public bool Equals(StreamingRiverEdgeId other) => First.Equals(other.First)
            && Second.Equals(other.Second);

        public override bool Equals(object obj) =>
            obj is StreamingRiverEdgeId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(First, Second);
    }

    internal readonly struct StreamingRiverRoutePoint
    {
        public StreamingRiverRoutePoint(
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

    internal sealed class StreamingRiverRoutePlan
    {
        private readonly ReadOnlyCollection<StreamingRiverRoutePoint> route;

        public StreamingRiverRoutePlan(
            StreamingRiverEdgeId id,
            StreamingEndpoint first,
            StreamingEndpoint second,
            float candidateRadiusCells,
            float widthCells,
            float depthUnits,
            int riverbedSeed,
            float riverbedAmplitudeUnits,
            IList<StreamingRiverRoutePoint> route)
        {
            if (!id.First.Equals(first.Id) || !id.Second.Equals(second.Id)
                || candidateRadiusCells <= 0f || widthCells <= 0f
                || depthUnits <= 0f || route == null || route.Count < 2)
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
            this.route = new ReadOnlyCollection<StreamingRiverRoutePoint>(route);
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
        }

        public StreamingRiverEdgeId Id { get; }
        public StreamingEndpoint First { get; }
        public StreamingEndpoint Second { get; }
        public float CandidateRadiusCells { get; }
        public float WidthCells { get; }
        public float DepthUnits { get; }
        public int RiverbedSeed { get; }
        public float RiverbedAmplitudeUnits { get; }
        public IReadOnlyList<StreamingRiverRoutePoint> Route => route;
        public int MinimumX { get; }
        public int MinimumZ { get; }
        public int MaximumX { get; }
        public int MaximumZ { get; }
    }

    internal readonly struct StreamingRiverCandidate
    {
        public StreamingRiverCandidate(
            StreamingRiverEdgeId id,
            StreamingEndpoint anchor,
            StreamingEndpoint target,
            float distanceCells,
            float candidateRadiusCells)
        {
            Id = id;
            Anchor = anchor;
            Target = target;
            DistanceCells = distanceCells;
            CandidateRadiusCells = candidateRadiusCells;
        }

        public StreamingRiverEdgeId Id { get; }
        public StreamingEndpoint Anchor { get; }
        public StreamingEndpoint Target { get; }
        public float DistanceCells { get; }
        public float CandidateRadiusCells { get; }

        public static int Compare(
            StreamingRiverCandidate left,
            StreamingRiverCandidate right)
        {
            var distance = left.DistanceCells.CompareTo(right.DistanceCells);
            return distance != 0 ? distance : left.Id.CompareTo(right.Id);
        }
    }

    internal static class StreamingRiverMath
    {
        public static int EndpointHash(in StreamingEndpointId id)
        {
            var basin = id.BasinComponent;
            var seed = unchecked((int)id.Kind * 486187739
                ^ basin.SeedGridX * 16777619
                ^ basin.SeedGridZ * 374761393);
            return unchecked((int)DeterministicNoise.Hash(
                id.WorldX,
                id.WorldZ,
                seed));
        }

        public static float Distance(
            int firstX,
            int firstZ,
            int secondX,
            int secondZ)
        {
            var x = firstX - secondX;
            var z = firstZ - secondZ;
            return MathF.Sqrt(x * x + z * z);
        }

        public static float ResolveRange(
            in WorldSeededRangeSettingsData range,
            float amount) => range.Minimum
                + (range.Maximum - range.Minimum) * amount;
    }

    internal sealed class StreamingMinHeap
    {
        private readonly List<Entry> entries = new();

        public int Count => entries.Count;

        public void Push(int value, float priority)
        {
            var index = entries.Count;
            entries.Add(new Entry(value, priority));
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (Compare(entries[parent], entries[index]) <= 0)
                {
                    break;
                }

                (entries[parent], entries[index]) =
                    (entries[index], entries[parent]);
                index = parent;
            }
        }

        public int Pop()
        {
            if (entries.Count == 0)
            {
                throw new InvalidOperationException("The heap is empty.");
            }

            var result = entries[0].Value;
            var last = entries.Count - 1;
            entries[0] = entries[last];
            entries.RemoveAt(last);
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
                    && Compare(entries[right], entries[left]) < 0
                    ? right
                    : left;
                if (Compare(entries[index], entries[child]) <= 0)
                {
                    break;
                }

                (entries[index], entries[child]) =
                    (entries[child], entries[index]);
                index = child;
            }

            return result;
        }

        private static int Compare(in Entry left, in Entry right)
        {
            var priority = left.Priority.CompareTo(right.Priority);
            return priority != 0 ? priority : left.Value.CompareTo(right.Value);
        }

        private readonly struct Entry
        {
            public Entry(int value, float priority)
            {
                Value = value;
                Priority = priority;
            }

            public int Value { get; }
            public float Priority { get; }
        }
    }

    internal sealed class StreamingInteractionResolution
    {
        public StreamingInteractionResolution(
            StreamingRiverEdgeId first,
            StreamingRiverEdgeId second,
            int worldX,
            int worldZ,
            float proximity,
            float alignment,
            int waterTopUnits,
            int targetTerrainSurfaceUnits,
            bool isAccepted)
        {
            if (first.CompareTo(second) >= 0 || waterTopUnits < targetTerrainSurfaceUnits
                || targetTerrainSurfaceUnits < 0 || !float.IsFinite(proximity)
                || !float.IsFinite(alignment))
            {
                throw new ArgumentOutOfRangeException(nameof(first));
            }

            First = first;
            Second = second;
            WorldX = worldX;
            WorldZ = worldZ;
            Proximity = Math.Clamp(proximity, 0f, 1f);
            Alignment = Math.Clamp(alignment, 0f, 1f);
            WaterTopUnits = waterTopUnits;
            TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            IsAccepted = isAccepted;
        }

        public StreamingRiverEdgeId First { get; }
        public StreamingRiverEdgeId Second { get; }
        public int WorldX { get; }
        public int WorldZ { get; }
        public float Proximity { get; }
        public float Alignment { get; }
        public int WaterTopUnits { get; }
        public int TargetTerrainSurfaceUnits { get; }
        public bool IsAccepted { get; }

        public bool Contains(in StreamingRiverEdgeId id) => First.Equals(id)
            || Second.Equals(id);

        public StreamingRiverEdgeId Other(in StreamingRiverEdgeId id)
        {
            if (First.Equals(id))
            {
                return Second;
            }

            if (Second.Equals(id))
            {
                return First;
            }

            throw new ArgumentOutOfRangeException(nameof(id));
        }
    }

    internal sealed class StreamingEdgeResolution
    {
        private readonly ReadOnlyCollection<PlanningTileKey> interactionTiles;

        public StreamingEdgeResolution(
            StreamingRiverEdgeId id,
            bool isActive,
            IList<PlanningTileKey> interactionTiles)
        {
            Id = id;
            IsActive = isActive;
            this.interactionTiles = new ReadOnlyCollection<PlanningTileKey>(
                interactionTiles ?? throw new ArgumentNullException(
                    nameof(interactionTiles)));
        }

        public StreamingRiverEdgeId Id { get; }
        public bool IsActive { get; }
        public IReadOnlyList<PlanningTileKey> InteractionTiles => interactionTiles;
    }

    internal sealed class StreamingRiverJunctionPlan
    {
        private readonly ReadOnlyCollection<StreamingRiverEdgeId> edges;

        public StreamingRiverJunctionPlan(
            int worldX,
            int worldZ,
            int waterTopUnits,
            int targetTerrainSurfaceUnits,
            IList<StreamingRiverEdgeId> edges)
        {
            if (waterTopUnits < targetTerrainSurfaceUnits
                || targetTerrainSurfaceUnits < 0 || edges == null
                || edges.Count < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(edges));
            }

            WorldX = worldX;
            WorldZ = worldZ;
            WaterTopUnits = waterTopUnits;
            TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            this.edges = new ReadOnlyCollection<StreamingRiverEdgeId>(edges);
        }

        public int WorldX { get; }
        public int WorldZ { get; }
        public int WaterTopUnits { get; }
        public int TargetTerrainSurfaceUnits { get; }
        public IReadOnlyList<StreamingRiverEdgeId> Edges => edges;
    }

    internal sealed class StreamingRiverSpatialIndexTile
    {
        private readonly ReadOnlyCollection<StreamingRiverRoutePlan> routes;
        private readonly ReadOnlyCollection<StreamingRiverJunctionPlan> junctions;

        public StreamingRiverSpatialIndexTile(
            PlanningTileKey key,
            IList<StreamingRiverRoutePlan> routes,
            IList<StreamingRiverJunctionPlan> junctions)
        {
            Key = key;
            this.routes = new ReadOnlyCollection<StreamingRiverRoutePlan>(
                routes ?? throw new ArgumentNullException(nameof(routes)));
            this.junctions = new ReadOnlyCollection<StreamingRiverJunctionPlan>(
                junctions ?? throw new ArgumentNullException(nameof(junctions)));
        }

        public PlanningTileKey Key { get; }
        public IReadOnlyList<StreamingRiverRoutePlan> Routes => routes;
        public IReadOnlyList<StreamingRiverJunctionPlan> Junctions => junctions;
    }

    internal sealed class StreamingRiverInteractionResult
    {
        public StreamingRiverInteractionResult(
            IReadOnlyDictionary<StreamingRiverEdgeId, StreamingEdgeResolution>
                edgeResolutions,
            IReadOnlyDictionary<StreamingCellKey, StreamingRiverJunctionPlan>
                junctions)
        {
            EdgeResolutions = edgeResolutions ?? throw new ArgumentNullException(
                nameof(edgeResolutions));
            Junctions = junctions ?? throw new ArgumentNullException(nameof(junctions));
        }

        public IReadOnlyDictionary<StreamingRiverEdgeId, StreamingEdgeResolution>
            EdgeResolutions { get; }
        public IReadOnlyDictionary<StreamingCellKey, StreamingRiverJunctionPlan>
            Junctions { get; }
    }

    internal static class StreamingRiverInteractionPlanner
    {
        public static StreamingRiverInteractionResult Build(
            WorldSettingsData settings,
            IReadOnlyList<StreamingRiverRoutePlan> routes)
        {
            if (settings == null || routes == null)
            {
                throw new ArgumentNullException(
                    settings == null ? nameof(settings) : nameof(routes));
            }

            var grouped = new SortedDictionary<PlanningTileKey,
                List<StreamingInteractionResolution>>();
            var byEdge = new Dictionary<StreamingRiverEdgeId,
                List<StreamingInteractionResolution>>();
            for (var firstIndex = 0; firstIndex < routes.Count; firstIndex++)
            for (var secondIndex = firstIndex + 1;
                 secondIndex < routes.Count;
                 secondIndex++)
            {
                var firstRoute = routes[firstIndex];
                var secondRoute = routes[secondIndex];
                if (!CanPossiblyInteract(settings, firstRoute, secondRoute)
                    || !TryFindInteraction(
                        settings,
                        firstRoute,
                        secondRoute,
                        out var interaction,
                        out _))
                {
                    continue;
                }

                var first = firstRoute.Id.CompareTo(secondRoute.Id) < 0
                    ? firstRoute
                    : secondRoute;
                var second = firstRoute.Id.CompareTo(secondRoute.Id) < 0
                    ? secondRoute
                    : firstRoute;
                var resolution = new StreamingInteractionResolution(
                    first.Id,
                    second.Id,
                    interaction.WorldX,
                    interaction.WorldZ,
                    interaction.Proximity,
                    interaction.Alignment,
                    interaction.WaterTopUnits,
                    interaction.TargetTerrainSurfaceUnits,
                    PassesJunctionChance(settings, first, second, interaction));
                var key = PlanningTileKey.FromCell(
                    resolution.WorldX,
                    resolution.WorldZ,
                    settings.Hydrology.Map.PlanningRegionSizeCells);
                if (!grouped.TryGetValue(key, out var entries))
                {
                    entries = new List<StreamingInteractionResolution>();
                    grouped.Add(key, entries);
                }

                entries.Add(resolution);
                AddByEdge(byEdge, resolution.First, resolution);
                AddByEdge(byEdge, resolution.Second, resolution);
            }

            var interactionTileKeys = new Dictionary<StreamingRiverEdgeId,
                List<PlanningTileKey>>();
            foreach (var pair in grouped)
            {
                pair.Value.Sort(CompareInteraction);
                for (var index = 0; index < pair.Value.Count; index++)
                {
                    var resolution = pair.Value[index];
                    AddInteractionTile(interactionTileKeys, resolution.First, pair.Key);
                    AddInteractionTile(interactionTileKeys, resolution.Second, pair.Key);
                }
            }

            var edgeResolutions = new Dictionary<StreamingRiverEdgeId,
                StreamingEdgeResolution>();
            for (var index = 0; index < routes.Count; index++)
            {
                var route = routes[index];
                var isActive = true;
                if (byEdge.TryGetValue(route.Id, out var interactions))
                {
                    for (var interactionIndex = 0;
                         interactionIndex < interactions.Count;
                         interactionIndex++)
                    {
                        var interaction = interactions[interactionIndex];
                        if (!interaction.IsAccepted
                            && interaction.Other(route.Id).CompareTo(route.Id) < 0)
                        {
                            isActive = false;
                            break;
                        }
                    }
                }

                interactionTileKeys.TryGetValue(route.Id, out var keys);
                keys ??= new List<PlanningTileKey>();
                keys.Sort();
                edgeResolutions.Add(route.Id, new StreamingEdgeResolution(
                    route.Id,
                    isActive,
                    keys));
            }

            var junctions = BuildJunctions(grouped, edgeResolutions);
            return new StreamingRiverInteractionResult(
                new ReadOnlyDictionary<StreamingRiverEdgeId,
                    StreamingEdgeResolution>(edgeResolutions),
                new ReadOnlyDictionary<StreamingCellKey, StreamingRiverJunctionPlan>(
                    junctions));
        }

        public static bool IntersectsRoute(
            StreamingRiverRoutePlan route,
            in WorldCellRectangle rectangle) => Intersects(route, rectangle);

        private static Dictionary<StreamingCellKey, StreamingRiverJunctionPlan>
            BuildJunctions(
                IReadOnlyDictionary<PlanningTileKey,
                    List<StreamingInteractionResolution>> grouped,
                IReadOnlyDictionary<StreamingRiverEdgeId, StreamingEdgeResolution>
                    edgeResolutions)
        {
            var mutable = new Dictionary<StreamingCellKey, MutableJunction>();
            foreach (var tile in grouped.Values)
            {
                for (var index = 0; index < tile.Count; index++)
                {
                    var interaction = tile[index];
                    if (!interaction.IsAccepted
                        || !edgeResolutions[interaction.First].IsActive
                        || !edgeResolutions[interaction.Second].IsActive)
                    {
                        continue;
                    }

                    var key = new StreamingCellKey(
                        interaction.WorldX,
                        interaction.WorldZ);
                    if (!mutable.TryGetValue(key, out var junction))
                    {
                        junction = new MutableJunction(
                            interaction.WorldX,
                            interaction.WorldZ,
                            interaction.WaterTopUnits,
                            interaction.TargetTerrainSurfaceUnits);
                        mutable.Add(key, junction);
                    }

                    junction.Add(interaction.First);
                    junction.Add(interaction.Second);
                    junction.Combine(
                        interaction.WaterTopUnits,
                        interaction.TargetTerrainSurfaceUnits);
                }
            }

            var result = new Dictionary<StreamingCellKey, StreamingRiverJunctionPlan>();
            foreach (var pair in mutable)
            {
                if (pair.Value.EdgeCount >= 2)
                {
                    result.Add(pair.Key, pair.Value.ToPlan());
                }
            }

            return result;
        }

        private static void AddByEdge(
            IDictionary<StreamingRiverEdgeId,
                List<StreamingInteractionResolution>> byEdge,
            StreamingRiverEdgeId edge,
            StreamingInteractionResolution interaction)
        {
            if (!byEdge.TryGetValue(edge, out var entries))
            {
                entries = new List<StreamingInteractionResolution>();
                byEdge.Add(edge, entries);
            }

            entries.Add(interaction);
        }

        private static void AddInteractionTile(
            IDictionary<StreamingRiverEdgeId, List<PlanningTileKey>> keys,
            StreamingRiverEdgeId edge,
            PlanningTileKey tile)
        {
            if (!keys.TryGetValue(edge, out var entries))
            {
                entries = new List<PlanningTileKey>();
                keys.Add(edge, entries);
            }

            for (var index = 0; index < entries.Count; index++)
            {
                if (entries[index].Equals(tile))
                {
                    return;
                }
            }

            entries.Add(tile);
        }

        private static int CompareInteraction(
            StreamingInteractionResolution left,
            StreamingInteractionResolution right)
        {
            var first = left.First.CompareTo(right.First);
            if (first != 0)
            {
                return first;
            }

            var second = left.Second.CompareTo(right.Second);
            if (second != 0)
            {
                return second;
            }

            var x = left.WorldX.CompareTo(right.WorldX);
            return x != 0 ? x : left.WorldZ.CompareTo(right.WorldZ);
        }

        private static bool TryFindInteraction(
            WorldSettingsData settings,
            StreamingRiverRoutePlan first,
            StreamingRiverRoutePlan second,
            out StreamingInteraction interaction,
            out long pointPairCount)
        {
            pointPairCount = 0;
            var bestDistance = float.PositiveInfinity;
            var firstIndex = -1;
            var secondIndex = -1;
            for (var firstRoute = 1;
                 firstRoute < first.Route.Count - 1;
                 firstRoute++)
            for (var secondRoute = 1;
                 secondRoute < second.Route.Count - 1;
                 secondRoute++)
            {
                pointPairCount++;
                var from = first.Route[firstRoute];
                var to = second.Route[secondRoute];
                var distance = StreamingRiverMath.Distance(
                    from.WorldX,
                    from.WorldZ,
                    to.WorldX,
                    to.WorldZ);
                var range = from.WidthCells * 0.5f + to.WidthCells * 0.5f
                    + settings.Hydrology.RiverCorridor.BankMarginCells;
                if (distance > range || distance > bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                firstIndex = firstRoute;
                secondIndex = secondRoute;
            }

            if (firstIndex < 0)
            {
                interaction = default;
                return false;
            }

            var firstPoint = first.Route[firstIndex];
            var secondPoint = second.Route[secondIndex];
            var distanceRange = firstPoint.WidthCells * 0.5f
                + secondPoint.WidthCells * 0.5f
                + settings.Hydrology.RiverCorridor.BankMarginCells;
            var waterTop = Math.Min(firstPoint.WaterTopUnits,
                secondPoint.WaterTopUnits);
            interaction = new StreamingInteraction(
                checked((int)Math.Round(
                    (firstPoint.WorldX + secondPoint.WorldX) * 0.5,
                    MidpointRounding.AwayFromZero)),
                checked((int)Math.Round(
                    (firstPoint.WorldZ + secondPoint.WorldZ) * 0.5,
                    MidpointRounding.AwayFromZero)),
                Math.Clamp(bestDistance / distanceRange, 0f, 1f),
                TangentAlignment(
                    first.Route,
                    firstIndex,
                    second.Route,
                    secondIndex),
                waterTop,
                Math.Min(waterTop, Math.Min(
                    firstPoint.TargetTerrainSurfaceUnits,
                    secondPoint.TargetTerrainSurfaceUnits)));
            return true;
        }

        private static bool CanPossiblyInteract(
            WorldSettingsData settings,
            StreamingRiverRoutePlan first,
            StreamingRiverRoutePlan second)
        {
            var range = (first.WidthCells + second.WidthCells) * 0.5f
                + settings.Hydrology.RiverCorridor.BankMarginCells;
            return AxisDistance(
                    first.MinimumX,
                    first.MaximumX,
                    second.MinimumX,
                    second.MaximumX) <= range
                && AxisDistance(
                    first.MinimumZ,
                    first.MaximumZ,
                    second.MinimumZ,
                    second.MaximumZ) <= range;
        }

        private static bool PassesJunctionChance(
            WorldSettingsData settings,
            StreamingRiverRoutePlan first,
            StreamingRiverRoutePlan second,
            in StreamingInteraction interaction)
        {
            var graph = settings.Hydrology.RiverGraph;
            var probability = graph.ProximityChance.Evaluate(interaction.Proximity)
                * graph.AlignmentChance.Evaluate(interaction.Alignment);
            var coordinateHash = DeterministicNoise.Hash(
                interaction.WorldX,
                interaction.WorldZ,
                DeterministicNoise.DeriveSeed(
                    settings.Seed,
                    "Hydrology.RiverGraph.Junction.Coordinate"));
            var noise = DeterministicNoise.Value01(
                StreamingRiverMath.EndpointHash(first.Id.First)
                    ^ StreamingRiverMath.EndpointHash(first.Id.Second),
                StreamingRiverMath.EndpointHash(second.Id.First)
                    ^ StreamingRiverMath.EndpointHash(second.Id.Second)
                    ^ coordinateHash,
                DeterministicNoise.DeriveSeed(
                    settings.Seed,
                    "Hydrology.RiverGraph.Junction.Accept"));
            return noise < probability;
        }

        private static float TangentAlignment(
            IReadOnlyList<StreamingRiverRoutePoint> first,
            int firstIndex,
            IReadOnlyList<StreamingRiverRoutePoint> second,
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

            return Math.Clamp(MathF.Abs((firstX * secondX + firstZ * secondZ)
                / (firstLength * secondLength)), 0f, 1f);
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

        private static bool Intersects(
            StreamingRiverRoutePlan route,
            in WorldCellRectangle rectangle)
        {
            for (var index = 1; index < route.Route.Count; index++)
            {
                if (IntersectsSegment(route.Route[index - 1], route.Route[index], rectangle))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IntersectsSegment(
            in StreamingRiverRoutePoint from,
            in StreamingRiverRoutePoint to,
            in WorldCellRectangle rectangle)
        {
            var minimum = 0d;
            var maximum = 1d;
            var deltaX = to.WorldX - from.WorldX;
            var deltaZ = to.WorldZ - from.WorldZ;
            return ClipSegmentAxis(
                    from.WorldX,
                    deltaX,
                    rectangle.MinimumX,
                    rectangle.MaximumX,
                    ref minimum,
                    ref maximum)
                && ClipSegmentAxis(
                    from.WorldZ,
                    deltaZ,
                    rectangle.MinimumZ,
                    rectangle.MaximumZ,
                    ref minimum,
                    ref maximum);
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

        private readonly struct StreamingInteraction
        {
            public StreamingInteraction(
                int worldX,
                int worldZ,
                float proximity,
                float alignment,
                int waterTopUnits,
                int targetTerrainSurfaceUnits)
            {
                WorldX = worldX;
                WorldZ = worldZ;
                Proximity = proximity;
                Alignment = alignment;
                WaterTopUnits = waterTopUnits;
                TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            }

            public int WorldX { get; }
            public int WorldZ { get; }
            public float Proximity { get; }
            public float Alignment { get; }
            public int WaterTopUnits { get; }
            public int TargetTerrainSurfaceUnits { get; }
        }

        private sealed class MutableJunction
        {
            private readonly List<StreamingRiverEdgeId> edges = new();

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
            public int EdgeCount => edges.Count;

            public void Add(StreamingRiverEdgeId edge)
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

            public StreamingRiverJunctionPlan ToPlan() => new(
                WorldX,
                WorldZ,
                WaterTopUnits,
                TargetTerrainSurfaceUnits,
                edges);
        }
    }

}
