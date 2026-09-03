using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    internal interface IWaterMapBrush
    {
        HydrologyFeatureKey Key { get; }
    }

    internal sealed class WaterBrushCatalog
    {
        private sealed class PendingBrush
        {
            public TaskCompletionSource<IWaterMapBrush> Completion { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly object gate = new();
        private readonly Dictionary<HydrologyFeatureKey, IWaterMapBrush> brushes = new();
        private readonly Dictionary<HydrologyFeatureKey, PendingBrush> pending = new();

        public BasinWaterBrush GetOrCreateBasin(
            HydrologyFeatureKey key,
            Func<BasinWaterBrush> create,
            CancellationToken cancellationToken)
        {
            return GetOrCreate(key, create, cancellationToken);
        }

        public RiverWaterBrush GetOrCreateRiver(
            HydrologyFeatureKey key,
            Func<RiverWaterBrush> create,
            CancellationToken cancellationToken)
        {
            return GetOrCreate(key, create, cancellationToken);
        }

        private T GetOrCreate<T>(
            HydrologyFeatureKey key,
            Func<T> create,
            CancellationToken cancellationToken)
            where T : class, IWaterMapBrush
        {
            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            cancellationToken.ThrowIfCancellationRequested();
            PendingBrush build = null;
            var buildHere = false;
            lock (gate)
            {
                if (brushes.TryGetValue(key, out var existing))
                {
                    return existing as T ?? throw new InvalidOperationException(
                        "Water Brush key has an incompatible brush type.");
                }

                if (!pending.TryGetValue(key, out build))
                {
                    build = new PendingBrush();
                    pending.Add(key, build);
                    buildHere = true;
                }
            }

            if (buildHere)
            {
                try
                {
                    var brush = create();
                    lock (gate)
                    {
                        brushes.Add(key, brush);
                        pending.Remove(key);
                    }

                    build.Completion.TrySetResult(brush);
                    return brush;
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        pending.Remove(key);
                    }

                    build.Completion.TrySetException(exception);
                    throw;
                }
            }

            build.Completion.Task.Wait(cancellationToken);
            return build.Completion.Task.GetAwaiter().GetResult() as T
                ?? throw new InvalidOperationException(
                    "Water Brush key has an incompatible brush type.");
        }
    }

    internal sealed class WaterBrushFactory
    {
        private readonly HydrologyFeatureSettingsData settings;
        private readonly int basinSeed;
        private readonly int riverSeed;

        public WaterBrushFactory(HydrologyFeatureSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            basinSeed = WaterMapDrawingMath.DeriveSeed(
                settings.World.Seed,
                "water-map-basin");
            riverSeed = WaterMapDrawingMath.DeriveSeed(
                settings.World.Seed,
                "water-map-river");
        }

        public int BasinPaddingCells => checked(
            settings.Basins.MaximumReachCells
            + settings.Basins.ShoreTransitionCells);

        public int BasinCandidateSpacingCells =>
            settings.Basins.CandidateLatticeSpacingCells;

        public int RiverPaddingCells => checked((int)MathF.Ceiling(
            settings.River.AnchorJitterCells
            + settings.River.Length.Maximum
            + settings.River.TerrainCorrectionRadiusCells
            + settings.River.Width.Maximum * 0.5f
            + settings.River.BankMarginCells
            + settings.NaturalEndpoint.EndpointTransitionCells));

        public int RiverCandidateSpacingCells =>
            settings.River.CandidateLatticeSpacingCells;

        public bool IsBasinCandidate(int gridX, int gridZ) =>
            WaterMapDrawingMath.Value01(gridX, gridZ, basinSeed)
            < settings.Basins.Occurrence;

        public bool IsRiverCandidate(int gridX, int gridZ) =>
            WaterMapDrawingMath.Value01(gridX, gridZ, riverSeed)
            < settings.River.Occurrence;

        public HydrologyFeatureKey GetBasinKey(int gridX, int gridZ)
        {
            var area = ResolveBasinArea(gridX, gridZ);
            var kind = area <= settings.Basins.PondMaximumAreaCells
                ? HydrologyFeatureKind.Pond
                : HydrologyFeatureKind.Lake;
            return WaterMapDrawingMath.CreateKey(
                kind,
                checked(gridX * settings.Basins.CandidateLatticeSpacingCells),
                checked(gridZ * settings.Basins.CandidateLatticeSpacingCells),
                basinSeed);
        }

        public BasinWaterBrush CreateBasin(
            int gridX,
            int gridZ,
            ITerrainPatternMapReader terrain)
        {
            var basin = settings.Basins;
            var area = ResolveBasinArea(gridX, gridZ);
            var waterType = area <= basin.PondMaximumAreaCells
                ? WaterType.Pond
                : WaterType.Lake;
            var ownerX = checked(gridX * basin.CandidateLatticeSpacingCells);
            var ownerZ = checked(gridZ * basin.CandidateLatticeSpacingCells);
            var maximumDepth = WaterMapDrawingMath.Lerp(
                basin.MaximumDepth.Minimum,
                basin.MaximumDepth.Maximum,
                WaterMapDrawingMath.Value01(
                    gridX,
                    gridZ,
                    WaterMapDrawingMath.DeriveSeed(basinSeed, "maximum-depth")));
            var bedAmplitude = WaterMapDrawingMath.Lerp(
                basin.BedAmplitude.Minimum,
                basin.BedAmplitude.Maximum,
                WaterMapDrawingMath.Value01(
                    gridX,
                    gridZ,
                    WaterMapDrawingMath.DeriveSeed(basinSeed, "bed-amplitude")));
            var key = GetBasinKey(gridX, gridZ);
            var geometry = BuildBasinGeometry(
                ownerX,
                ownerZ,
                area,
                terrain);
            return new BasinWaterBrush(
                key,
                waterType,
                geometry,
                maximumDepth,
                bedAmplitude,
                basin);
        }

        public HydrologyFeatureKey GetRiverKey(int gridX, int gridZ) =>
            WaterMapDrawingMath.CreateKey(
                HydrologyFeatureKind.River,
                checked(gridX * settings.River.CandidateLatticeSpacingCells),
                checked(gridZ * settings.River.CandidateLatticeSpacingCells),
                riverSeed);

        public RiverWaterBrush CreateRiver(
            int gridX,
            int gridZ,
            ITerrainPatternMapReader terrain)
        {
            var river = settings.River;
            var ownerX = checked(gridX * river.CandidateLatticeSpacingCells);
            var ownerZ = checked(gridZ * river.CandidateLatticeSpacingCells);
            var key = GetRiverKey(gridX, gridZ);
            var featureSeed = WaterMapDrawingMath.DeriveSeed(
                unchecked((int)key.Identity.SeedSalt),
                "stroke");
            var anchorX = ownerX + WaterMapDrawingMath.SignedValue01(
                gridX,
                gridZ,
                WaterMapDrawingMath.DeriveSeed(featureSeed, "anchor-x"))
                * river.AnchorJitterCells;
            var anchorZ = ownerZ + WaterMapDrawingMath.SignedValue01(
                gridX,
                gridZ,
                WaterMapDrawingMath.DeriveSeed(featureSeed, "anchor-z"))
                * river.AnchorJitterCells;
            var direction = WaterMapDrawingMath.Value01(
                gridX,
                gridZ,
                WaterMapDrawingMath.DeriveSeed(featureSeed, "direction"))
                * MathF.PI * 2f;
            var length = WaterMapDrawingMath.Lerp(
                river.Length.Minimum,
                river.Length.Maximum,
                WaterMapDrawingMath.Value01(
                    gridX,
                    gridZ,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "length")));
            var points = BuildSeedNodePoints(
                new WaterMapPoint(anchorX, anchorZ),
                direction,
                length,
                featureSeed);

            var terrainAwarePoints = BuildTerrainAwareRiverPoints(
                points,
                featureSeed,
                terrain);
            return new RiverWaterBrush(
                key,
                featureSeed,
                terrainAwarePoints,
                terrainAwarePoints[^1].DistanceFromStart,
                river,
                settings.NaturalEndpoint,
                settings.Sea.SurfaceHeight,
                terrain);
        }

        private WaterMapRiverPoint[] BuildSeedNodePoints(
            WaterMapPoint anchor,
            float initialDirection,
            float length,
            int featureSeed)
        {
            var river = settings.River;
            var nodeCount = Math.Max(
                2,
                checked((int)MathF.Ceiling(length
                    / river.StrokeSampleSpacingCells) + 1));
            var result = new WaterMapRiverPoint[nodeCount];
            var point = anchor;
            var direction = initialDirection;
            var distance = 0f;
            result[0] = new WaterMapRiverPoint(point, distance);
            for (var index = 1; index < nodeCount; index++)
            {
                var remaining = length - distance;
                var step = Math.Min(
                    river.StrokeSampleSpacingCells,
                    remaining);
                if (index > 1)
                {
                    var turnDegrees = WaterMapDrawingMath.Lerp(
                        river.NodeTurnDegrees.Minimum,
                        river.NodeTurnDegrees.Maximum,
                        WaterMapDrawingMath.Value01(
                            index,
                            featureSeed,
                            WaterMapDrawingMath.DeriveSeed(
                                featureSeed,
                                "node-turn-magnitude")));
                    var turnSign = WaterMapDrawingMath.SignedValue01(
                        index,
                        featureSeed,
                        WaterMapDrawingMath.DeriveSeed(
                            featureSeed,
                            "node-turn-sign"));
                    direction += (turnSign < 0f ? -1f : 1f)
                        * turnDegrees * MathF.PI / 180f;
                }

                point = new WaterMapPoint(
                    point.X + MathF.Cos(direction) * step,
                    point.Z + MathF.Sin(direction) * step);
                distance += step;
                result[index] = new WaterMapRiverPoint(point, distance);
            }

            return result;
        }

        private WaterMapRiverPoint[] BuildTerrainAwareRiverPoints(
            IReadOnlyList<WaterMapRiverPoint> basicPoints,
            int featureSeed,
            ITerrainPatternMapReader terrain)
        {
            var candidates = new RiverNodeCandidate[basicPoints.Count][];
            var state = new RiverNodeCandidate[basicPoints.Count];
            for (var index = 0; index < basicPoints.Count; index++)
            {
                candidates[index] = CreateRiverNodeCandidates(
                    basicPoints,
                    index,
                    featureSeed,
                    terrain);
                state[index] = FindBaseCandidate(candidates[index]);
            }

            for (var pass = 0;
                 pass <= settings.River.TerrainCorrectionSmoothingPasses;
                 pass++)
            {
                var next = new RiverNodeCandidate[state.Length];
                for (var index = 0; index < next.Length; index++)
                {
                    var previous = state[Math.Max(0, index - 1)];
                    var following = state[Math.Min(state.Length - 1, index + 1)];
                    next[index] = SelectRiverNodeCandidate(
                        candidates[index],
                        previous,
                        following);
                }

                state = next;
            }

            var result = new WaterMapRiverPoint[state.Length];
            var distance = 0f;
            for (var index = 0; index < result.Length; index++)
            {
                if (index > 0)
                {
                    distance += WaterMapDrawingMath.Distance(
                        result[index - 1].Point,
                        state[index].Point);
                }

                result[index] = new WaterMapRiverPoint(state[index].Point, distance);
            }

            return result;
        }

        private RiverNodeCandidate[] CreateRiverNodeCandidates(
            IReadOnlyList<WaterMapRiverPoint> basicPoints,
            int index,
            int featureSeed,
            ITerrainPatternMapReader terrain)
        {
            var river = settings.River;
            var normal = GetBaseNodeNormal(basicPoints, index);
            var basePoint = basicPoints[index].Point;
            var result = new List<RiverNodeCandidate>();
            var used = new HashSet<long>();
            for (var offset = -river.TerrainCorrectionRadiusCells;
                 offset <= river.TerrainCorrectionRadiusCells;
                 offset++)
            {
                var x = RoundCell(basePoint.X + normal.X * offset);
                var z = RoundCell(basePoint.Z + normal.Z * offset);
                if (!used.Add(CoordinateKey(x, z)))
                {
                    continue;
                }

                var point = new WaterMapPoint(x, z);
                var center = terrain.GetCell(x, z);
                result.Add(new RiverNodeCandidate(
                    point,
                    offset,
                    center.SurfaceHeight,
                    center.Slope,
                    EstimateCorridorDeformation(
                        point,
                        normal,
                        featureSeed,
                        terrain)));
            }

            result.Sort((left, right) => left.CompareTo(right));
            return result.ToArray();
        }

        private RiverNodeCandidate SelectRiverNodeCandidate(
            IReadOnlyList<RiverNodeCandidate> candidates,
            RiverNodeCandidate previous,
            RiverNodeCandidate following)
        {
            var best = candidates[0];
            var bestCost = EvaluateRiverNodeCost(best, previous, following);
            for (var index = 1; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var cost = EvaluateRiverNodeCost(candidate, previous, following);
                if (cost < bestCost
                    || cost == bestCost
                    && candidate.CompareTo(best) < 0)
                {
                    best = candidate;
                    bestCost = cost;
                }
            }

            return best;
        }

        private float EvaluateRiverNodeCost(
            RiverNodeCandidate candidate,
            RiverNodeCandidate previous,
            RiverNodeCandidate following)
        {
            var river = settings.River;
            var neighborHeight = (previous.SurfaceHeight
                + following.SurfaceHeight) * 0.5f;
            var neighborOffset = (previous.Offset + following.Offset) * 0.5f;
            return candidate.Slope / WorldGrid.HeightStepsPerCell
                * river.TerrainSlopeCost
                + MathF.Abs(candidate.Offset) * river.BaseStrokeDeviationCost
                + MathF.Abs(candidate.SurfaceHeight - neighborHeight)
                    / WorldGrid.HeightStepsPerCell
                    * river.ElevationChangeCost
                + candidate.CorridorDeformation
                    / WorldGrid.HeightStepsPerCell
                    * river.CorridorDeformationCost
                + MathF.Abs(candidate.Offset - neighborOffset)
                    * river.CurvatureCost;
        }

        private RiverNodeCandidate FindBaseCandidate(
            IReadOnlyList<RiverNodeCandidate> candidates)
        {
            var best = candidates[0];
            for (var index = 1; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                if (MathF.Abs(candidate.Offset) < MathF.Abs(best.Offset)
                    || MathF.Abs(candidate.Offset) == MathF.Abs(best.Offset)
                    && candidate.CompareTo(best) < 0)
                {
                    best = candidate;
                }
            }

            return best;
        }

        private float EstimateCorridorDeformation(
            WaterMapPoint point,
            WaterMapPoint normal,
            int featureSeed,
            ITerrainPatternMapReader terrain)
        {
            var river = settings.River;
            var center = terrain.GetCell(RoundCell(point.X), RoundCell(point.Z));
            var width = ResolveRiverRange(
                point,
                river.WidthField,
                river.Width,
                featureSeed,
                "width");
            var inset = ResolveRiverRange(
                point,
                river.WidthField,
                river.WaterInset,
                featureSeed,
                "inset");
            var depth = ResolveRiverRange(
                point,
                river.WidthField,
                river.Depth,
                featureSeed,
                "depth");
            var bankOffset = width * 0.5f + river.BankMarginCells;
            var left = terrain.GetCell(
                RoundCell(point.X + normal.X * bankOffset),
                RoundCell(point.Z + normal.Z * bankOffset));
            var right = terrain.GetCell(
                RoundCell(point.X - normal.X * bankOffset),
                RoundCell(point.Z - normal.Z * bankOffset));
            var rawSurface = center.HasSeaPattern
                ? settings.Sea.SurfaceHeight
                : center.SurfaceHeight - inset;
            var containedSurface = left.HasSeaPattern || right.HasSeaPattern
                ? rawSurface
                : Math.Min(
                    rawSurface,
                    Math.Min(
                        ToFullyFilledHeight(left.SurfaceHeight),
                        ToFullyFilledHeight(right.SurfaceHeight)));
            return MathF.Abs(center.SurfaceHeight - (containedSurface - depth));
        }

        private static WaterMapPoint GetBaseNodeNormal(
            IReadOnlyList<WaterMapRiverPoint> points,
            int index)
        {
            var previous = points[Math.Max(0, index - 1)].Point;
            var following = points[Math.Min(points.Count - 1, index + 1)].Point;
            var deltaX = following.X - previous.X;
            var deltaZ = following.Z - previous.Z;
            var length = MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            return length > 0f
                ? new WaterMapPoint(-deltaZ / length, deltaX / length)
                : new WaterMapPoint(0f, 1f);
        }

        private static int RoundCell(float value) => checked((int)MathF.Round(
            value,
            MidpointRounding.AwayFromZero));

        private static float ToFullyFilledHeight(float surfaceHeight)
        {
            var units = Math.Max(0, RoundCell(surfaceHeight));
            return units / WorldGrid.HeightStepsPerCell
                * WorldGrid.HeightStepsPerCell;
        }

        private static float ResolveRiverRange(
            WaterMapPoint point,
            TerrainNoiseFieldData field,
            TerrainRangeData range,
            int featureSeed,
            string channel) => WaterMapDrawingMath.Lerp(
            range.Minimum,
            range.Maximum,
            WaterMapDrawingMath.SampleNormalized(
                point.X,
                point.Z,
                field,
                WaterMapDrawingMath.DeriveSeed(featureSeed, channel)));

        private int ResolveBasinArea(int gridX, int gridZ) => Math.Clamp(
            (int)MathF.Round(WaterMapDrawingMath.Lerp(
                settings.Basins.Area.Minimum,
                settings.Basins.Area.Maximum,
                WaterMapDrawingMath.Value01(
                    gridX,
                    gridZ,
                    WaterMapDrawingMath.DeriveSeed(basinSeed, "area")))),
            1,
            checked((int)MathF.Ceiling(settings.Basins.Area.Maximum)));

        private BasinDrawingGeometry BuildBasinGeometry(
            int ownerX,
            int ownerZ,
            int targetArea,
            ITerrainPatternMapReader terrain)
        {
            var start = CoordinateKey(ownerX, ownerZ);
            var sampled = new Dictionary<long, TerrainPatternCell>();
            var first = SampleTerrain(ownerX, ownerZ);
            if (HasSeaInfluence(first))
            {
                return BasinDrawingGeometry.Empty;
            }

            var costs = new Dictionary<long, float>
            {
                { start, 0f }
            };
            var footprint = new Dictionary<long, TerrainPatternCell>();
            var frontier = new BasinCostHeap();
            frontier.Push(start, 0f);
            while (frontier.Count > 0 && footprint.Count < targetArea)
            {
                var current = frontier.Pop();
                if (!costs.TryGetValue(current.Key, out var currentCost)
                    || current.Cost != currentCost)
                {
                    continue;
                }

                if (!sampled.TryGetValue(current.Key, out var currentCell))
                {
                    var currentX = (int)(current.Key >> 32);
                    var currentZ = (int)current.Key;
                    currentCell = SampleTerrain(currentX, currentZ);
                }

                if (HasSeaInfluence(currentCell))
                {
                    continue;
                }

                footprint.Add(current.Key, currentCell);
                var x = (int)(current.Key >> 32);
                var z = (int)current.Key;
                AddNeighbor(x - 1, z, 1f);
                AddNeighbor(x + 1, z, 1f);
                AddNeighbor(x, z - 1, 1f);
                AddNeighbor(x, z + 1, 1f);
                AddNeighbor(x - 1, z - 1, MathF.Sqrt(2f));
                AddNeighbor(x + 1, z - 1, MathF.Sqrt(2f));
                AddNeighbor(x - 1, z + 1, MathF.Sqrt(2f));
                AddNeighbor(x + 1, z + 1, MathF.Sqrt(2f));

                void AddNeighbor(int nextX, int nextZ, float distance)
                {
                    if (Math.Max(
                            Math.Abs(nextX - ownerX),
                            Math.Abs(nextZ - ownerZ))
                        > settings.Basins.MaximumReachCells)
                    {
                        return;
                    }

                    var next = CoordinateKey(nextX, nextZ);
                    if (footprint.ContainsKey(next))
                    {
                        return;
                    }

                    var nextCell = SampleTerrain(nextX, nextZ);
                    if (HasSeaInfluence(nextCell))
                    {
                        return;
                    }

                    var terrainDelta = MathF.Abs(
                        nextCell.SurfaceHeight - currentCell.SurfaceHeight)
                        / WorldGrid.HeightStepsPerCell;
                    var potential = settings.Basins.PotentialResponse.Evaluate(
                        WaterMapDrawingMath.SampleNormalized(
                            nextX,
                            nextZ,
                            settings.Basins.PotentialField,
                            WaterMapDrawingMath.DeriveSeed(
                                basinSeed,
                                "potential")));
                    var cost = current.Cost + distance + distance * (
                        potential * settings.Basins.PotentialCost
                        + terrainDelta * settings.Basins.TerrainDeformationCost
                        + terrainDelta / distance * settings.Basins.SlopeCost);
                    if (costs.TryGetValue(next, out var previous)
                        && previous <= cost)
                    {
                        return;
                    }

                    costs[next] = cost;
                    frontier.Push(next, cost);
                }
            }

            if (footprint.Count != targetArea)
            {
                return BasinDrawingGeometry.Empty;
            }

            var surfaceHeight = SelectBasinSurface(footprint);
            var boundary = FindBasinBoundary(footprint);
            return new BasinDrawingGeometry(
                surfaceHeight,
                BuildInteriorProgress(footprint, boundary),
                BuildShoreMembership(footprint, boundary));

            TerrainPatternCell SampleTerrain(int x, int z)
            {
                var coordinate = CoordinateKey(x, z);
                if (!sampled.TryGetValue(coordinate, out var cell))
                {
                    cell = terrain.GetCell(x, z);
                    sampled.Add(coordinate, cell);
                }

                return cell;
            }
        }

        private float SelectBasinSurface(
            IReadOnlyDictionary<long, TerrainPatternCell> cells)
        {
            var minimum = int.MaxValue;
            var maximum = int.MinValue;
            foreach (var pair in cells)
            {
                var surface = checked((int)MathF.Round(
                    pair.Value.SurfaceHeight,
                    MidpointRounding.AwayFromZero));
                minimum = Math.Min(minimum, surface);
                maximum = Math.Max(maximum, surface);
            }

            var best = minimum;
            var bestCost = float.PositiveInfinity;
            for (var candidate = minimum; candidate <= maximum; candidate++)
            {
                var cost = 0f;
                foreach (var pair in cells)
                {
                    var x = (int)(pair.Key >> 32);
                    var z = (int)pair.Key;
                    var delta = pair.Value.SurfaceHeight - candidate;
                    cost += delta >= 0f
                        ? delta * settings.Basins.CutCost
                        : -delta * settings.Basins.FillCost;
                    if (IsBasinBoundary(cells, x, z))
                    {
                        cost += MathF.Abs(delta) * settings.Basins.RimCost;
                    }
                }

                if (cost < bestCost)
                {
                    best = candidate;
                    bestCost = cost;
                }
            }

            return best;
        }

        private static List<long> FindBasinBoundary(
            IReadOnlyDictionary<long, TerrainPatternCell> cells)
        {
            var result = new List<long>();
            foreach (var pair in cells)
            {
                var x = (int)(pair.Key >> 32);
                var z = (int)pair.Key;
                if (IsBasinBoundary(cells, x, z))
                {
                    result.Add(pair.Key);
                }
            }

            return result;
        }

        private static Dictionary<long, float> BuildInteriorProgress(
            IReadOnlyDictionary<long, TerrainPatternCell> cells,
            IReadOnlyList<long> boundary)
        {
            var distances = new Dictionary<long, int>(cells.Count);
            var queue = new Queue<long>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distances.Add(boundary[index], 0);
                queue.Enqueue(boundary[index]);
            }

            var maximumDistance = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var distance = distances[current];
                var x = (int)(current >> 32);
                var z = (int)current;
                AddInteriorNeighbor(x - 1, z, distance);
                AddInteriorNeighbor(x + 1, z, distance);
                AddInteriorNeighbor(x, z - 1, distance);
                AddInteriorNeighbor(x, z + 1, distance);
            }

            var progress = new Dictionary<long, float>(distances.Count);
            foreach (var pair in distances)
            {
                progress.Add(
                    pair.Key,
                    maximumDistance > 0
                        ? pair.Value / (float)maximumDistance
                        : 1f);
            }

            return progress;

            void AddInteriorNeighbor(int x, int z, int distance)
            {
                var next = CoordinateKey(x, z);
                if (!cells.ContainsKey(next) || distances.ContainsKey(next))
                {
                    return;
                }

                var nextDistance = distance + 1;
                distances.Add(next, nextDistance);
                maximumDistance = Math.Max(maximumDistance, nextDistance);
                queue.Enqueue(next);
            }
        }

        private Dictionary<long, float> BuildShoreMembership(
            IReadOnlyDictionary<long, TerrainPatternCell> cells,
            IReadOnlyList<long> boundary)
        {
            var distances = new Dictionary<long, int>();
            var queue = new Queue<long>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distances.Add(boundary[index], 0);
                queue.Enqueue(boundary[index]);
            }

            var membership = new Dictionary<long, float>();
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var distance = distances[current];
                if (distance >= settings.Basins.ShoreTransitionCells)
                {
                    continue;
                }

                var x = (int)(current >> 32);
                var z = (int)current;
                AddShoreNeighbor(x - 1, z, distance);
                AddShoreNeighbor(x + 1, z, distance);
                AddShoreNeighbor(x, z - 1, distance);
                AddShoreNeighbor(x, z + 1, distance);
            }

            return membership;

            void AddShoreNeighbor(int x, int z, int distance)
            {
                var next = CoordinateKey(x, z);
                if (cells.ContainsKey(next) || distances.ContainsKey(next))
                {
                    return;
                }

                var nextDistance = distance + 1;
                distances.Add(next, nextDistance);
                queue.Enqueue(next);
                membership.Add(
                    next,
                    settings.Basins.ShoreTransition.Evaluate(
                        1f - nextDistance /
                        (float)settings.Basins.ShoreTransitionCells));
            }
        }

        private static bool IsBasinBoundary(
            IReadOnlyDictionary<long, TerrainPatternCell> cells,
            int x,
            int z) => !cells.ContainsKey(CoordinateKey(x - 1, z))
            || !cells.ContainsKey(CoordinateKey(x + 1, z))
            || !cells.ContainsKey(CoordinateKey(x, z - 1))
            || !cells.ContainsKey(CoordinateKey(x, z + 1));

        private static long CoordinateKey(int x, int z) =>
            ((long)x << 32) ^ (uint)z;

        private static bool HasSeaInfluence(TerrainPatternCell cell) =>
            cell.HasSeaPattern || cell.HasSecondarySeaPattern;

        private readonly struct BasinCostEntry
        {
            public BasinCostEntry(long key, float cost)
            {
                Key = key;
                Cost = cost;
            }

            public long Key { get; }
            public float Cost { get; }
        }

        private sealed class BasinCostHeap
        {
            private readonly List<BasinCostEntry> entries = new();

            public int Count => entries.Count;

            public void Push(long key, float cost)
            {
                entries.Add(new BasinCostEntry(key, cost));
                var index = entries.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (Compare(entries[parent], entries[index]) <= 0)
                    {
                        break;
                    }

                    (entries[parent], entries[index]) = (entries[index], entries[parent]);
                    index = parent;
                }
            }

            public BasinCostEntry Pop()
            {
                var result = entries[0];
                var lastIndex = entries.Count - 1;
                var last = entries[lastIndex];
                entries.RemoveAt(lastIndex);
                if (entries.Count == 0)
                {
                    return result;
                }

                entries[0] = last;
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

                    (entries[index], entries[child]) = (entries[child], entries[index]);
                    index = child;
                }

                return result;
            }

            private static int Compare(BasinCostEntry left, BasinCostEntry right)
            {
                var cost = left.Cost.CompareTo(right.Cost);
                return cost != 0 ? cost : left.Key.CompareTo(right.Key);
            }
        }

        private readonly struct RiverNodeCandidate
        {
            public RiverNodeCandidate(
                WaterMapPoint point,
                int offset,
                float surfaceHeight,
                float slope,
                float corridorDeformation)
            {
                Point = point;
                Offset = offset;
                SurfaceHeight = surfaceHeight;
                Slope = slope;
                CorridorDeformation = corridorDeformation;
            }

            public WaterMapPoint Point { get; }
            public int Offset { get; }
            public float SurfaceHeight { get; }
            public float Slope { get; }
            public float CorridorDeformation { get; }

            public int CompareTo(RiverNodeCandidate other)
            {
                var x = Point.X.CompareTo(other.Point.X);
                return x != 0 ? x : Point.Z.CompareTo(other.Point.Z);
            }
        }
    }

    internal sealed class BasinWaterBrush : IWaterMapBrush
    {
        private readonly BasinDrawingGeometry geometry;
        private readonly float maximumDepth;
        private readonly float bedAmplitude;
        private readonly BasinFeatureSettingsData settings;

        public BasinWaterBrush(
            HydrologyFeatureKey key,
            WaterType waterType,
            BasinDrawingGeometry geometry,
            float maximumDepth,
            float bedAmplitude,
            BasinFeatureSettingsData settings)
        {
            Key = key;
            WaterType = waterType;
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            this.maximumDepth = maximumDepth;
            this.bedAmplitude = bedAmplitude;
            this.settings = settings;
        }

        public HydrologyFeatureKey Key { get; }
        public WaterType WaterType { get; }

        public bool TrySample(
            int x,
            int z,
            TerrainPatternCell terrainCell,
            out HydrologyDrawingSample sample)
        {
            if (terrainCell.HasSeaPattern)
            {
                sample = default;
                return false;
            }

            var coordinate = CoordinateKey(x, z);
            if (!geometry.InteriorProgress.TryGetValue(
                    coordinate,
                    out var interior))
            {
                if (!geometry.ShoreMembership.TryGetValue(
                        coordinate,
                        out var membership))
                {
                    sample = default;
                    return false;
                }

                var ground = terrainCell.SurfaceHeight
                    + (geometry.SurfaceHeight - terrainCell.SurfaceHeight)
                    * membership;
                sample = new HydrologyDrawingSample(
                    Key,
                    WaterType.None,
                    ground,
                    0f,
                    0f,
                    membership,
                    false);
                return true;
            }

            var bedNoise = WaterMapDrawingMath.SampleSigned(
                x,
                z,
                settings.BedField,
                WaterMapDrawingMath.DeriveSeed(
                    unchecked((int)Key.Identity.SeedSalt),
                    "bed")) * bedAmplitude;
            var depth = settings.DepthByInterior.Evaluate(interior)
                * (maximumDepth + bedNoise);
            sample = new HydrologyDrawingSample(
                Key,
                WaterType,
                geometry.SurfaceHeight - depth,
                geometry.SurfaceHeight,
                interior,
                1f - settings.ShoreTransition.Evaluate(interior),
                depth > 0f);
            return true;
        }

        private static long CoordinateKey(int x, int z) =>
            ((long)x << 32) ^ (uint)z;
    }

    internal sealed class BasinDrawingGeometry
    {
        public static BasinDrawingGeometry Empty { get; } = new(
            0f,
            new Dictionary<long, float>(),
            new Dictionary<long, float>());

        public BasinDrawingGeometry(
            float surfaceHeight,
            IReadOnlyDictionary<long, float> interiorProgress,
            IReadOnlyDictionary<long, float> shoreMembership)
        {
            SurfaceHeight = surfaceHeight;
            InteriorProgress = interiorProgress
                ?? throw new ArgumentNullException(nameof(interiorProgress));
            ShoreMembership = shoreMembership
                ?? throw new ArgumentNullException(nameof(shoreMembership));
        }

        public float SurfaceHeight { get; }
        public IReadOnlyDictionary<long, float> InteriorProgress { get; }
        public IReadOnlyDictionary<long, float> ShoreMembership { get; }
    }

    internal sealed class RiverWaterBrush : IWaterMapBrush
    {
        private readonly int featureSeed;
        private readonly WaterMapRiverPoint[] points;
        private readonly float totalDistance;
        private readonly RiverFeatureSettingsData riverSettings;
        private readonly NaturalEndpointSettingsData endpointSettings;
        private readonly int seaSurfaceHeight;
        private readonly RiverWaterProfile[] profiles;

        public RiverWaterBrush(
            HydrologyFeatureKey key,
            int featureSeed,
            WaterMapRiverPoint[] points,
            float totalDistance,
            RiverFeatureSettingsData riverSettings,
            NaturalEndpointSettingsData endpointSettings,
            int seaSurfaceHeight,
            ITerrainPatternMapReader terrain)
        {
            Key = key;
            this.featureSeed = featureSeed;
            this.points = points ?? throw new ArgumentNullException(nameof(points));
            this.totalDistance = totalDistance;
            this.riverSettings = riverSettings;
            this.endpointSettings = endpointSettings;
            this.seaSurfaceHeight = seaSurfaceHeight;
            profiles = BuildProfiles(terrain ?? throw new ArgumentNullException(nameof(terrain)));
        }

        public HydrologyFeatureKey Key { get; }

        public bool TrySample(
            int x,
            int z,
            ITerrainPatternMapReader terrain,
            out HydrologyDrawingSample sample)
        {
            var nearest = FindNearestSegment(x, z);
            if (nearest.Index < 0)
            {
                sample = default;
                return false;
            }

            var profile = RiverWaterProfile.Lerp(
                profiles[nearest.Index],
                profiles[nearest.Index + 1],
                nearest.Progress);
            var width = profile.Width;
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
                WaterMapDrawingMath.DeriveSeed(featureSeed, "riverbed"))
                * WaterMapDrawingMath.Lerp(
                    riverSettings.RiverbedAmplitude.Minimum,
                    riverSettings.RiverbedAmplitude.Maximum,
                    WaterMapDrawingMath.SampleNormalized(
                        x,
                        z,
                        riverSettings.WidthField,
                        WaterMapDrawingMath.DeriveSeed(
                            featureSeed,
                            "riverbed-amplitude")));
            var effectiveDepth = Math.Max(0f, profile.BedDepth - bedNoise)
                * cross;
            var terrainBed = terrain.GetCell(x, z).SurfaceHeight
                - effectiveDepth;
            var waterBed = profile.WaterSurface
                - Math.Max(0f, profile.WaterDepthBase - bedNoise) * cross;
            var ground = Math.Min(terrainBed, waterBed);
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

        private RiverWaterProfile[] BuildProfiles(ITerrainPatternMapReader terrain)
        {
            var rawSurfaces = new float[points.Length];
            var containedSurfaces = new float[points.Length];
            var widths = new float[points.Length];
            var bedDepths = new float[points.Length];
            var waterDepthBases = new float[points.Length];
            for (var index = 0; index < points.Length; index++)
            {
                var point = points[index];
                var natural = GetNaturalProgress(point.DistanceFromStart);
                var width = ResolveWidth(point.Point) * natural;
                var depth = ResolveDepth(point.Point);
                var inset = ResolveInset(point.Point);
                var center = terrain.GetCell(
                    checked((int)MathF.Round(point.Point.X)),
                    checked((int)MathF.Round(point.Point.Z)));
                var rawSurface = center.HasSeaPattern
                    ? seaSurfaceHeight
                    : center.SurfaceHeight - inset * natural;
                rawSurfaces[index] = rawSurface;
                containedSurfaces[index] = ResolveContainedWaterSurface(
                    terrain,
                    index,
                    width,
                    rawSurface);
                widths[index] = width;
                bedDepths[index] = depth * natural;
                waterDepthBases[index] = (depth - inset) * natural;
            }

            var surfaces = BuildWaterProfile(rawSurfaces, containedSurfaces);
            var result = new RiverWaterProfile[points.Length];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = new RiverWaterProfile(
                    points[index].Point,
                    points[index].DistanceFromStart,
                    surfaces[index],
                    widths[index],
                    bedDepths[index],
                    waterDepthBases[index]);
            }

            return result;
        }

        private float[] BuildWaterProfile(
            IReadOnlyList<float> rawSurfaces,
            IReadOnlyList<float> containedSurfaces)
        {
            var profile = new float[rawSurfaces.Count];
            for (var index = 0; index < profile.Length; index++)
            {
                var rawSurface = rawSurfaces[index];
                var surface = rawSurface;
                for (var source = index;
                     source >= 0
                     && points[index].DistanceFromStart - points[source].DistanceFromStart
                        <= riverSettings.DropTransitionCells;
                     source--)
                {
                    surface = ResolveProfileSurface(
                        surface,
                        rawSurface,
                        containedSurfaces[source],
                        points[index].DistanceFromStart
                            - points[source].DistanceFromStart);
                }

                for (var source = index + 1;
                     source < profile.Length
                     && points[source].DistanceFromStart - points[index].DistanceFromStart
                        <= riverSettings.DropTransitionCells;
                     source++)
                {
                    surface = ResolveProfileSurface(
                        surface,
                        rawSurface,
                        containedSurfaces[source],
                        points[source].DistanceFromStart
                            - points[index].DistanceFromStart);
                }

                profile[index] = surface;
            }

            return profile;
        }

        private float ResolveProfileSurface(
            float surface,
            float rawSurface,
            float containedSurface,
            float distance)
        {
            if (containedSurface >= rawSurface)
            {
                return surface;
            }

            var amount = riverSettings.DropTransition.Evaluate(
                1f - distance / riverSettings.DropTransitionCells);
            return Math.Min(
                surface,
                rawSurface + (containedSurface - rawSurface) * amount);
        }

        private float ResolveContainedWaterSurface(
            ITerrainPatternMapReader terrain,
            int index,
            float width,
            float rawSurface)
        {
            if (width <= 0f)
            {
                return rawSurface;
            }

            var previous = points[Math.Max(0, index - 1)].Point;
            var next = points[Math.Min(points.Length - 1, index + 1)].Point;
            var deltaX = next.X - previous.X;
            var deltaZ = next.Z - previous.Z;
            var length = MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            var normalX = length > 0f ? -deltaZ / length : 0f;
            var normalZ = length > 0f ? deltaX / length : 1f;
            var center = points[index].Point;
            var bankOffset = width * 0.5f + riverSettings.BankMarginCells;
            var left = terrain.GetCell(
                checked((int)MathF.Round(center.X + normalX * bankOffset)),
                checked((int)MathF.Round(center.Z + normalZ * bankOffset)));
            var right = terrain.GetCell(
                checked((int)MathF.Round(center.X - normalX * bankOffset)),
                checked((int)MathF.Round(center.Z - normalZ * bankOffset)));
            if (left.HasSeaPattern || right.HasSeaPattern)
            {
                return rawSurface;
            }

            return Math.Min(
                rawSurface,
                Math.Min(
                    ToFullyFilledHeight(left.SurfaceHeight),
                    ToFullyFilledHeight(right.SurfaceHeight)));
        }

        private static float ToFullyFilledHeight(float surfaceHeight)
        {
            var units = Math.Max(0, checked((int)MathF.Round(
                surfaceHeight,
                MidpointRounding.AwayFromZero)));
            return units / WorldGrid.HeightStepsPerCell
                * WorldGrid.HeightStepsPerCell;
        }

        private float ResolveWidth(WaterMapPoint point) =>
            WaterMapDrawingMath.Lerp(
                riverSettings.Width.Minimum,
                riverSettings.Width.Maximum,
                WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "width")));

        private float ResolveDepth(WaterMapPoint point) =>
            WaterMapDrawingMath.Lerp(
                riverSettings.Depth.Minimum,
                riverSettings.Depth.Maximum,
                WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "depth")));

        private float ResolveInset(WaterMapPoint point) =>
            WaterMapDrawingMath.Lerp(
                riverSettings.WaterInset.Minimum,
                riverSettings.WaterInset.Maximum,
                WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "inset")));

        private float GetNaturalProgress(float distanceFromStart) => Math.Min(
            WaterMapDrawingMath.EvaluateEndpointProgress(
                distanceFromStart,
                endpointSettings),
            WaterMapDrawingMath.EvaluateEndpointProgress(
                totalDistance - distanceFromStart,
                endpointSettings));

        private WaterMapNearestSegment FindNearestSegment(int x, int z)
        {
            var point = new WaterMapPoint(x, z);
            var best = WaterMapNearestSegment.None;
            for (var index = 0; index < points.Length - 1; index++)
            {
                var candidate = WaterMapNearestSegment.Create(
                    index,
                    point,
                    points[index].Point,
                    points[index + 1].Point);
                if (candidate.Distance < best.Distance)
                {
                    best = candidate;
                }
            }

            return best;
        }
    }

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

    internal readonly struct WaterMapRiverPoint
    {
        public WaterMapRiverPoint(WaterMapPoint point, float distanceFromStart)
        {
            Point = point;
            DistanceFromStart = distanceFromStart;
        }

        public WaterMapPoint Point { get; }
        public float DistanceFromStart { get; }
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

    internal readonly struct WaterMapNearestSegment
    {
        private WaterMapNearestSegment(int index, float distance, float progress)
        {
            Index = index;
            Distance = distance;
            Progress = progress;
        }

        public static WaterMapNearestSegment None => new(
            -1,
            float.PositiveInfinity,
            0f);

        public int Index { get; }
        public float Distance { get; }
        public float Progress { get; }

        public static WaterMapNearestSegment Create(
            int index,
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
            return new WaterMapNearestSegment(
                index,
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

        public static float EvaluateEndpointProgress(
            float distance,
            NaturalEndpointSettingsData endpoint) => EvaluateIntegratedRate(
            Math.Clamp(distance / endpoint.EndpointTransitionCells, 0f, 1f),
            endpoint.EndpointTransitionRate);

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

        private static float EvaluateIntegratedRate(
            float progress,
            TerrainCurveData curve)
        {
            var total = IntegrateCurve(1f, curve);
            return total <= 0f
                ? progress
                : IntegrateCurve(progress, curve) / total;
        }

        private static float IntegrateCurve(float progress, TerrainCurveData curve)
        {
            progress = Math.Clamp(progress, 0f, 1f);
            var segment = Math.Min(3, (int)(progress * 4f));
            var result = 0f;
            for (var index = 0; index < segment; index++)
            {
                result += IntegrateCurveSegment(
                    1f,
                    GetCurveValue(curve, index),
                    GetCurveValue(curve, index + 1)) * 0.25f;
            }

            var local = progress * 4f - segment;
            return result + IntegrateCurveSegment(
                local,
                GetCurveValue(curve, segment),
                GetCurveValue(curve, Math.Min(segment + 1, 4))) * 0.25f;
        }

        private static float IntegrateCurveSegment(float progress, float from, float to) =>
            from * progress + (to - from)
            * (progress * progress * progress
                - 0.5f * progress * progress * progress * progress);

        private static float GetCurveValue(TerrainCurveData curve, int index) => index switch
        {
            0 => curve.AtZero,
            1 => curve.AtQuarter,
            2 => curve.AtHalf,
            3 => curve.AtThreeQuarters,
            _ => curve.AtOne
        };
    }
}
