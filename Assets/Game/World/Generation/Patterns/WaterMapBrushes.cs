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
            + settings.River.MaximumNodeCount - 1
            + settings.River.Width.Maximum * 0.5f
            + settings.River.BankMarginCells));

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
            var nodeCount = ResolveRiverNodeCount(
                gridX,
                gridZ,
                featureSeed);
            var bone = BuildMainStream(
                new WaterMapPoint(anchorX, anchorZ),
                nodeCount,
                featureSeed);
            return new RiverWaterBrush(
                key,
                featureSeed,
                bone,
                bone[^1].DistanceFromStart,
                river,
                settings.Sea.SurfaceHeight,
                terrain);
        }

        private int ResolveRiverNodeCount(
            int gridX,
            int gridZ,
            int featureSeed)
        {
            var river = settings.River;
            if (river.MinimumNodeCount == river.MaximumNodeCount
                || river.AverageNodeCount == river.MinimumNodeCount)
            {
                return river.MinimumNodeCount;
            }

            if (river.AverageNodeCount == river.MaximumNodeCount)
            {
                return river.MaximumNodeCount;
            }

            var average = (river.AverageNodeCount - river.MinimumNodeCount)
                / (float)(river.MaximumNodeCount - river.MinimumNodeCount);
            var distributionExponent = (1f - average) / average;
            var random = WaterMapDrawingMath.Value01(
                gridX,
                gridZ,
                WaterMapDrawingMath.DeriveSeed(featureSeed, "node-count"));
            return Math.Clamp(
                (int)MathF.Round(WaterMapDrawingMath.Lerp(
                    river.MinimumNodeCount,
                    river.MaximumNodeCount,
                    MathF.Pow(random, distributionExponent))),
                river.MinimumNodeCount,
                river.MaximumNodeCount);
        }

        private RiverBoneNode[] BuildMainStream(
            WaterMapPoint anchor,
            int nodeCount,
            int featureSeed)
        {
            var river = settings.River;
            var result = new RiverBoneNode[nodeCount];
            var point = new WaterMapPoint(
                RoundToMapCell(anchor.X),
                RoundToMapCell(anchor.Z));
            var direction = CreateInitialMainDirection(featureSeed);
            var turnRadians = WaterMapDrawingMath.Lerp(
                    river.NodeTurnDegrees.Minimum,
                    river.NodeTurnDegrees.Maximum,
                    WaterMapDrawingMath.Value01(
                        featureSeed,
                        0,
                        WaterMapDrawingMath.DeriveSeed(
                            featureSeed,
                            "turn-amplitude")))
                * MathF.PI / 180f;
            var distance = 0f;
            result[0] = new RiverBoneNode(point, distance);
            for (var index = 1; index < nodeCount; index++)
            {
                var curvature = WaterMapDrawingMath.SampleSigned(
                    index,
                    0,
                    river.CurvatureField,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "curvature"));
                direction = TurnForward(direction, curvature * turnRadians);
                var previous = point;
                point = new WaterMapPoint(
                    RoundToMapCell(point.X + direction.X),
                    RoundToMapCell(point.Z + direction.Z));
                distance += WaterMapDrawingMath.Distance(previous, point);
                result[index] = new RiverBoneNode(point, distance);
            }

            return result;
        }

        private static WaterMapPoint CreateInitialMainDirection(int featureSeed)
        {
            var angle = WaterMapDrawingMath.Value01(
                    featureSeed,
                    0,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "direction"))
                * MathF.PI * 2f;
            return EmphasizeDominantAxis(new WaterMapPoint(
                MathF.Cos(angle),
                MathF.Sin(angle)));
        }

        private static WaterMapPoint TurnForward(
            WaterMapPoint direction,
            float turnRadians)
        {
            var amount = MathF.Tan(turnRadians);
            return Normalize(new WaterMapPoint(
                direction.X - direction.Z * amount,
                direction.Z + direction.X * amount));
        }

        private static WaterMapPoint EmphasizeDominantAxis(
            WaterMapPoint direction) => Normalize(new WaterMapPoint(
            direction.X * MathF.Abs(direction.X),
            direction.Z * MathF.Abs(direction.Z)));

        private static WaterMapPoint Normalize(WaterMapPoint value)
        {
            var length = MathF.Sqrt(value.X * value.X + value.Z * value.Z);
            return new WaterMapPoint(value.X / length, value.Z / length);
        }

        private static int RoundToMapCell(float value) => checked((int)MathF.Round(
            value,
            MidpointRounding.AwayFromZero));

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
        private readonly RiverBoneNode[] bone;
        private readonly float totalDistance;
        private readonly RiverFeatureSettingsData riverSettings;
        private readonly int seaSurfaceHeight;
        private readonly RiverWaterProfile[] profiles;

        public RiverWaterBrush(
            HydrologyFeatureKey key,
            int featureSeed,
            RiverBoneNode[] bone,
            float totalDistance,
            RiverFeatureSettingsData riverSettings,
            int seaSurfaceHeight,
            ITerrainPatternMapReader terrain)
        {
            Key = key;
            this.featureSeed = featureSeed;
            this.bone = bone ?? throw new ArgumentNullException(nameof(bone));
            this.totalDistance = totalDistance;
            this.riverSettings = riverSettings;
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
            var rawSurfaces = new float[bone.Length];
            var containedSurfaces = new float[bone.Length];
            var widths = new float[bone.Length];
            var bedDepths = new float[bone.Length];
            var waterDepthBases = new float[bone.Length];
            for (var index = 0; index < bone.Length; index++)
            {
                var point = bone[index];
                var progress = point.DistanceFromStart / totalDistance;
                var width = ResolveWidth(point.Point, progress);
                var depth = ResolveDepth(point.Point, progress);
                var inset = ResolveInset(point.Point);
                var center = terrain.GetCell(
                    checked((int)MathF.Round(point.Point.X)),
                    checked((int)MathF.Round(point.Point.Z)));
                var rawSurface = center.HasSeaPattern
                    ? seaSurfaceHeight
                    : center.SurfaceHeight - inset;
                rawSurfaces[index] = rawSurface;
                containedSurfaces[index] = ResolveContainedWaterSurface(
                    terrain,
                    index,
                    width,
                    rawSurface);
                widths[index] = width;
                bedDepths[index] = depth;
                waterDepthBases[index] = Math.Max(0f, depth - inset);
            }

            var surfaces = BuildWaterProfile(rawSurfaces, containedSurfaces);
            var result = new RiverWaterProfile[bone.Length];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = new RiverWaterProfile(
                    bone[index].Point,
                    bone[index].DistanceFromStart,
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
                     && bone[index].DistanceFromStart - bone[source].DistanceFromStart
                        <= riverSettings.DropTransitionCells;
                     source--)
                {
                    surface = ResolveProfileSurface(
                        surface,
                        rawSurface,
                        containedSurfaces[source],
                        bone[index].DistanceFromStart
                            - bone[source].DistanceFromStart);
                }

                for (var source = index + 1;
                     source < profile.Length
                     && bone[source].DistanceFromStart - bone[index].DistanceFromStart
                        <= riverSettings.DropTransitionCells;
                     source++)
                {
                    surface = ResolveProfileSurface(
                        surface,
                        rawSurface,
                        containedSurfaces[source],
                        bone[source].DistanceFromStart
                            - bone[index].DistanceFromStart);
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

            var previous = bone[Math.Max(0, index - 1)].Point;
            var next = bone[Math.Min(bone.Length - 1, index + 1)].Point;
            var deltaX = next.X - previous.X;
            var deltaZ = next.Z - previous.Z;
            var length = MathF.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            var normalX = length > 0f ? -deltaZ / length : 0f;
            var normalZ = length > 0f ? deltaX / length : 1f;
            var center = bone[index].Point;
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

        private float ResolveWidth(WaterMapPoint point, float progress) =>
            ResolveProfileRange(
                point,
                progress,
                riverSettings.Width,
                "width");

        private float ResolveDepth(WaterMapPoint point, float progress) =>
            ResolveProfileRange(
                point,
                progress,
                riverSettings.Depth,
                "depth");

        private float ResolveProfileRange(
            WaterMapPoint point,
            float progress,
            TerrainRangeData range,
            string channel)
        {
            var body = WaterMapDrawingMath.Lerp(
                range.Minimum,
                range.Maximum,
                CenterBias(WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(
                        featureSeed,
                        $"{channel}-body"))));
            var terminus = WaterMapDrawingMath.Lerp(
                range.Minimum,
                range.Maximum,
                LowerBias(WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(
                        featureSeed,
                        $"{channel}-terminus"))));
            return WaterMapDrawingMath.Lerp(
                body,
                terminus,
                GetTerminusInfluence(progress));
        }

        private float ResolveInset(WaterMapPoint point) =>
            WaterMapDrawingMath.Lerp(
                riverSettings.WaterInset.Minimum,
                riverSettings.WaterInset.Maximum,
                WaterMapDrawingMath.SampleNormalized(
                    point.X,
                    point.Z,
                    riverSettings.WidthField,
                    WaterMapDrawingMath.DeriveSeed(featureSeed, "inset")));

        private static float CenterBias(float value)
        {
            var signed = value * 2f - 1f;
            return 0.5f + 0.5f * signed * signed * signed;
        }

        private static float LowerBias(float value) => value * value;

        private static float GetTerminusInfluence(float progress) =>
            1f - MathF.Sin(Math.Clamp(progress, 0f, 1f) * MathF.PI);

        private WaterMapNearestSegment FindNearestSegment(int x, int z)
        {
            var point = new WaterMapPoint(x, z);
            var best = WaterMapNearestSegment.None;
            for (var index = 0; index < bone.Length - 1; index++)
            {
                var candidate = WaterMapNearestSegment.Create(
                    index,
                    point,
                    bone[index].Point,
                    bone[index + 1].Point);
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

    internal readonly struct RiverBoneNode
    {
        public RiverBoneNode(WaterMapPoint point, float distanceFromStart)
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
