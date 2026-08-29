using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal readonly struct RiverPatternSample
    {
        public RiverPatternSample(
            float influence,
            float surfaceDownUnits,
            float depthUnits,
            int waterTopUnits)
        {
            Influence = influence;
            SurfaceDownUnits = surfaceDownUnits;
            DepthUnits = depthUnits;
            WaterTopUnits = waterTopUnits;
        }

        public float Influence { get; }
        public float SurfaceDownUnits { get; }
        public float DepthUnits { get; }
        public int WaterTopUnits { get; }
        public bool HasRiver => Influence > 0f && DepthUnits > 0f;
    }

    internal static class RiverPatternResolver
    {
        public static WorldPatternResult Resolve(
            in WorldNoiseRouter router,
            int worldX,
            int worldZ,
            in WorldFieldSample field,
            WorldSettingsData settings,
            out WorldPatternWeights weights)
        {
            var terrain = WorldPatternResolver.Resolve(
                router,
                worldX,
                worldZ,
                field,
                settings,
                out weights);
            var river = RiverHydrologyPlanner.Sample(
                router,
                settings,
                worldX,
                worldZ);
            if (!river.HasRiver)
            {
                return terrain;
            }

            var keepSeaWater = terrain.WaterType == WaterType.Sea;
            return new WorldPatternResult(
                terrain.SurfaceOffsetUnits - river.SurfaceDownUnits,
                terrain.VerticalFactor,
                terrain.DetailUnits,
                terrain.DominantPattern,
                terrain.RegionKey,
                terrain.InteriorProgress,
                terrain.PatternDepthUnits,
                terrain.PatternDepthProgress,
                terrain.PatternDetailUnits,
                keepSeaWater ? terrain.WaterTopUnits : river.WaterTopUnits,
                keepSeaWater ? WaterType.Sea : WaterType.River,
                river.Influence,
                river.DepthUnits);
        }
    }

    internal static class RiverHydrologyPlanner
    {
        private const int VerticalEdgeChannel = 7101;
        private const int HorizontalEdgeChannel = 7102;
        private const int PortalOffsetChannel = 7110;
        private const int RouteVariationChannel = 7120;
        private const int WidthChannel = 7130;
        private const int DepthChannel = 7140;
        private const int WaterInsetChannel = 7150;
        private const int RiverbedChannel = 7160;
        private const int RiverbedAmplitudeChannel = 7170;

        private static readonly ConditionalWeakTable<
            WorldSettingsData,
            RiverPlanCache> Caches = new();

        public static RiverPatternSample Sample(
            in WorldNoiseRouter router,
            WorldSettingsData settings,
            int worldX,
            int worldZ)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var riverSettings = settings.WorldPatterns.River;
            var regionSize = riverSettings.PlanningRegionSizeCells;
            var regionX = FloorDivide(worldX, regionSize);
            var regionZ = FloorDivide(worldZ, regionSize);
            var maximumRadius = Math.Max(
                0.75f,
                riverSettings.MaximumWidthCells * 0.5f);
            var neighborReach = checked(
                (int)Math.Ceiling(maximumRadius / regionSize));
            var nearest = default(RiverPathSample);
            var hasNearest = false;

            for (var offsetZ = -neighborReach;
                 offsetZ <= neighborReach;
                 offsetZ++)
            for (var offsetX = -neighborReach;
                 offsetX <= neighborReach;
                 offsetX++)
            {
                var plan = GetPlan(
                    settings,
                    regionX + offsetX,
                    regionZ + offsetZ);
                if (!plan.TrySample(worldX, worldZ, out var candidate)
                    || hasNearest
                    && !candidate.IsCloserThan(nearest))
                {
                    continue;
                }

                nearest = candidate;
                hasNearest = true;
            }

            if (!hasNearest)
            {
                return default;
            }

            var radius = Math.Max(0.75f, nearest.WidthCells * 0.5f);
            if (nearest.Distance >= radius)
            {
                return default;
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
                return default;
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
            var densityField = new WorldDensityField(settings);
            var terrainSurfaceUnits = SampleTerrain(
                    router,
                    densityField,
                    settings,
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
            var surfaceDownUnits = Math.Max(
                0f,
                terrainSurfaceUnits - targetBedUnits);
            var resultingSurfaceUnits = terrainSurfaceUnits
                - surfaceDownUnits;
            var depthUnits = Math.Max(
                0f,
                nearest.WaterTopUnits - resultingSurfaceUnits);
            return new RiverPatternSample(
                influence,
                surfaceDownUnits,
                depthUnits,
                waterTopUnits);
        }

        private static RiverPlan GetPlan(
            WorldSettingsData settings,
            int regionX,
            int regionZ)
        {
            var cache = Caches.GetValue(settings, _ => new RiverPlanCache());
            var key = ((long)regionX << 32) ^ (uint)regionZ;
            return cache.Plans.GetOrAdd(
                    key,
                    _ => new Lazy<RiverPlan>(
                        () => BuildPlan(settings, regionX, regionZ),
                        true))
                .Value;
        }

        private static RiverPlan BuildPlan(
            WorldSettingsData settings,
            int regionX,
            int regionZ)
        {
            var riverSettings = settings.WorldPatterns.River;
            var spacing = riverSettings.RouteSampleSpacingCells;
            var regionSize = riverSettings.PlanningRegionSizeCells;
            var gridSize = checked(regionSize / spacing + 1);
            var nodeCount = checked(gridSize * gridSize);
            var originX = checked(regionX * regionSize);
            var originZ = checked(regionZ * regionSize);
            var surfaceUnits = new float[nodeCount];
            var slope = new float[nodeCount];
            var valleyDepth = new float[nodeCount];
            var sea = new bool[nodeCount];
            var router = new WorldNoiseRouter(settings);
            var densityField = new WorldDensityField(settings);

            for (var z = 0; z < gridSize; z++)
            for (var x = 0; x < gridSize; x++)
            {
                var worldX = checked(originX + x * spacing);
                var worldZ = checked(originZ + z * spacing);
                var terrain = SampleTerrain(
                    router,
                    densityField,
                    settings,
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

            var portals = BuildPortals(
                settings,
                regionX,
                regionZ,
                gridSize);
            var seaTarget = FindCoastalSeaTarget(
                surfaceUnits,
                sea,
                gridSize);
            if (seaTarget < 0 && portals.Count < 2
                || seaTarget >= 0 && portals.Count < 1)
            {
                return RiverPlan.Empty;
            }

            var trunk = new bool[nodeCount];
            var outlet = seaTarget >= 0
                ? seaTarget
                : SelectLowestPortal(portals, surfaceUnits);
            trunk[outlet] = true;
            portals.Remove(outlet);
            portals.Sort((left, right) =>
                surfaceUnits[right].CompareTo(surfaceUnits[left]));
            var segments = new List<RiverSegment>();

            for (var index = 0; index < portals.Count; index++)
            {
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
                    portals[index],
                    trunk);
                if (path.Count < 2)
                {
                    continue;
                }

                for (var pathIndex = 0;
                     pathIndex < path.Count;
                     pathIndex++)
                {
                    trunk[path[pathIndex]] = true;
                }

                AddLocalProfileSegments(
                    segments,
                    path,
                    router,
                    densityField,
                    settings,
                    originX,
                    originZ,
                    gridSize,
                    spacing,
                    riverSettings.SmoothingIterations);
            }

            return segments.Count == 0
                ? RiverPlan.Empty
                : new RiverPlan(segments.ToArray());
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

        private static List<int> BuildPortals(
            WorldSettingsData settings,
            int regionX,
            int regionZ,
            int gridSize)
        {
            var density = settings.WorldPatterns.River.NetworkDensity;
            var portals = new List<int>(4);
            TryAddPortal(
                portals,
                regionX,
                regionZ,
                VerticalEdgeChannel,
                PortalOffsetChannel,
                density,
                gridSize,
                westOrSouth: true,
                vertical: true,
                settings.Seed);
            TryAddPortal(
                portals,
                regionX + 1,
                regionZ,
                VerticalEdgeChannel,
                PortalOffsetChannel,
                density,
                gridSize,
                westOrSouth: false,
                vertical: true,
                settings.Seed);
            TryAddPortal(
                portals,
                regionX,
                regionZ,
                HorizontalEdgeChannel,
                PortalOffsetChannel + 1,
                density,
                gridSize,
                westOrSouth: true,
                vertical: false,
                settings.Seed);
            TryAddPortal(
                portals,
                regionX,
                regionZ + 1,
                HorizontalEdgeChannel,
                PortalOffsetChannel + 1,
                density,
                gridSize,
                westOrSouth: false,
                vertical: false,
                settings.Seed);
            return portals;
        }

        private static void TryAddPortal(
            List<int> portals,
            int edgeX,
            int edgeZ,
            int activationChannel,
            int offsetChannel,
            float density,
            int gridSize,
            bool westOrSouth,
            bool vertical,
            int worldSeed)
        {
            if (DeterministicNoise.Value01(
                    edgeX,
                    edgeZ,
                    Seed(worldSeed, activationChannel)) >= density)
            {
                return;
            }

            var selector = DeterministicNoise.Value01(
                edgeX,
                edgeZ,
                Seed(worldSeed, offsetChannel));
            var interiorIndex = 1 + Math.Min(
                gridSize - 3,
                (int)(selector * (gridSize - 2)));
            var x = vertical
                ? (westOrSouth ? 0 : gridSize - 1)
                : interiorIndex;
            var z = vertical
                ? interiorIndex
                : (westOrSouth ? 0 : gridSize - 1);
            portals.Add(x + gridSize * z);
        }

        private static int FindCoastalSeaTarget(
            float[] surfaceUnits,
            bool[] sea,
            int gridSize)
        {
            var target = -1;
            var highestSurface = float.NegativeInfinity;
            for (var z = 1; z < gridSize - 1; z++)
            for (var x = 1; x < gridSize - 1; x++)
            {
                var index = x + gridSize * z;
                if (!sea[index])
                {
                    continue;
                }

                var coastal = !sea[index - 1]
                    || !sea[index + 1]
                    || !sea[index - gridSize]
                    || !sea[index + gridSize];
                if (coastal && surfaceUnits[index] > highestSurface)
                {
                    highestSurface = surfaceUnits[index];
                    target = index;
                }
            }

            return target;
        }

        private static int SelectLowestPortal(
            List<int> portals,
            float[] surfaceUnits)
        {
            var lowest = portals[0];
            for (var index = 1; index < portals.Count; index++)
            {
                if (surfaceUnits[portals[index]] < surfaceUnits[lowest])
                {
                    lowest = portals[index];
                }
            }

            return lowest;
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
            int start,
            bool[] goals)
        {
            var nodeCount = surfaceUnits.Length;
            var cost = new float[nodeCount];
            var previous = new int[nodeCount];
            var closed = new bool[nodeCount];
            Array.Fill(cost, float.PositiveInfinity);
            Array.Fill(previous, -1);
            var frontier = new MinimumHeap();
            cost[start] = 0f;
            frontier.Push(start, 0f);
            var destination = -1;
            var river = settings.WorldPatterns.River;
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
                        + corridorExposure * river.CorridorExposureCost
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
                if (node == start)
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

            var river = settings.WorldPatterns.River;
            var worldX = originX + nextX * spacing;
            var worldZ = originZ + nextZ * spacing;
            var width = 1f + Sample01(
                    worldX,
                    worldZ,
                    river.WidthField,
                    Seed(settings.Seed, WidthChannel))
                * (river.MaximumWidthCells - 1f);
            var bankOffsetCells = Math.Max(0.75f, width * 0.5f)
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
            var leftBankUnits = ToSolidHeightUnits(
                SampleGridSurface(surfaceUnits, gridSize, leftX, leftZ),
                settings);
            var rightBankUnits = ToSolidHeightUnits(
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
            in WorldNoiseRouter router,
            in WorldDensityField densityField,
            WorldSettingsData settings,
            int originX,
            int originZ,
            int gridSize,
            int spacing,
            int smoothingIterations)
        {
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

            var river = settings.WorldPatterns.River;
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
                var width = 1f + Sample01(
                        point.X,
                        point.Z,
                        river.WidthField,
                        widthSeed)
                    * (river.MaximumWidthCells - 1f);
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
                    router,
                    densityField,
                    settings,
                    point.X,
                    point.Z);
                var waterTopUnits = center.HasSeaWater
                    ? center.WaterTopUnits
                    : center.SurfaceUnits - insetUnits;
                var bankOffset = Math.Max(0.75f, width * 0.5f)
                    + river.BankMarginCells;
                var leftBank = SampleTerrain(
                    router,
                    densityField,
                    settings,
                    point.X + normalX * bankOffset,
                    point.Z + normalZ * bankOffset);
                var rightBank = SampleTerrain(
                    router,
                    densityField,
                    settings,
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
                            ToSolidHeightUnits(leftBank.SurfaceUnits, settings),
                            ToSolidHeightUnits(rightBank.SurfaceUnits, settings)));
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
            var profiles = new RiverProfilePoint[points.Count];
            for (var index = 0; index < profiles.Length; index++)
            {
                profiles[index] = new RiverProfilePoint(
                    points[index].X,
                    points[index].Z,
                    hydraulicWaterTopUnits[index],
                    widths[index],
                    bedDepths[index],
                    waterDepthBases[index]);
            }

            for (var index = 0; index < points.Count - 1; index++)
            {
                segments.Add(new RiverSegment(
                    profiles[index],
                    profiles[index + 1]));
            }
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

        private static RiverTerrainSample SampleTerrain(
            in WorldNoiseRouter router,
            in WorldDensityField densityField,
            WorldSettingsData settings,
            double worldX,
            double worldZ)
        {
            var sampleX = checked((int)Math.Round(
                worldX,
                MidpointRounding.AwayFromZero));
            var sampleZ = checked((int)Math.Round(
                worldZ,
                MidpointRounding.AwayFromZero));
            var field = router.Sample(sampleX, sampleZ);
            var terrain = WorldPatternResolver.Resolve(
                router,
                sampleX,
                sampleZ,
                field,
                settings,
                out _);
            return new RiverTerrainSample(
                FindSurfaceUnits(
                    densityField,
                    settings,
                    sampleX,
                    sampleZ,
                    field,
                    terrain),
                terrain.WaterType == WaterType.Sea,
                terrain.WaterTopUnits);
        }

        private static float FindSurfaceUnits(
            in WorldDensityField densityField,
            WorldSettingsData settings,
            int worldX,
            int worldZ,
            in WorldFieldSample field,
            in WorldPatternResult profile)
        {
            var maximumHeightUnit = checked(
                settings.WorldHeight * WorldGrid.HeightStepsPerCell);
            var verticalFactor = MathF.Abs(profile.VerticalFactor);
            var detailMagnitude = MathF.Abs(profile.DetailUnits);
            var expectedSurface = settings.TerrainBaseHeightUnits
                + profile.SurfaceOffsetUnits;
            var upperBound = verticalFactor > float.Epsilon
                ? expectedSurface
                    + detailMagnitude * (
                        MathF.Abs(field.Detail) + 1f / verticalFactor)
                : maximumHeightUnit;
            var upperUnit = Math.Clamp(
                (int)MathF.Ceiling(upperBound) + 1,
                1,
                maximumHeightUnit);
            var upperDensity = densityField.Sample(
                worldX,
                upperUnit,
                worldZ,
                field,
                profile);
            while (upperDensity >= 0f && upperUnit < maximumHeightUnit)
            {
                upperUnit++;
                upperDensity = densityField.Sample(
                    worldX,
                    upperUnit,
                    worldZ,
                    field,
                    profile);
            }

            if (upperDensity >= 0f)
            {
                return maximumHeightUnit;
            }

            for (var lowerUnit = upperUnit - 1; lowerUnit >= 0; lowerUnit--)
            {
                var lowerDensity = densityField.Sample(
                    worldX,
                    lowerUnit,
                    worldZ,
                    field,
                    profile);
                if (lowerDensity < 0f)
                {
                    upperDensity = lowerDensity;
                    continue;
                }

                var denominator = lowerDensity - upperDensity;
                var fraction = denominator > 0f
                    ? lowerDensity / denominator
                    : 0f;
                return Math.Clamp(
                    lowerUnit + Math.Clamp(fraction, 0f, 1f),
                    0f,
                    maximumHeightUnit);
            }

            return 0f;
        }

        private static int ToSolidHeightUnits(
            float surfaceUnits,
            WorldSettingsData settings) => Math.Clamp(
            Math.Max(
                1,
                (int)MathF.Round(
                    surfaceUnits,
                    MidpointRounding.AwayFromZero)),
            1,
            checked(settings.WorldHeight * WorldGrid.HeightStepsPerCell));

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

        private static int Seed(int worldSeed, int channel) => unchecked(
            (int)DeterministicNoise.Hash(channel, channel * 31L, worldSeed));

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            var remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private sealed class RiverPlanCache
        {
            public ConcurrentDictionary<long, Lazy<RiverPlan>> Plans { get; } =
                new();
        }

        private sealed class RiverPlan
        {
            public static readonly RiverPlan Empty = new(Array.Empty<RiverSegment>());
            private readonly RiverSegment[] segments;

            public RiverPlan(RiverSegment[] segments)
            {
                this.segments = segments;
            }

            public bool TrySample(
                double worldX,
                double worldZ,
                out RiverPathSample nearest)
            {
                nearest = default;
                var found = false;
                for (var index = 0; index < segments.Length; index++)
                {
                    var sample = segments[index].Sample(worldX, worldZ);
                    if (found && !sample.IsCloserThan(nearest))
                    {
                        continue;
                    }

                    nearest = sample;
                    found = true;
                }

                return found;
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

        private readonly struct RiverProfilePoint
        {
            public RiverProfilePoint(
                double x,
                double z,
                float waterTopUnits,
                float widthCells,
                float bedDepthUnits,
                float waterDepthBaseUnits)
            {
                X = x;
                Z = z;
                WaterTopUnits = waterTopUnits;
                WidthCells = widthCells;
                BedDepthUnits = bedDepthUnits;
                WaterDepthBaseUnits = waterDepthBaseUnits;
            }

            public double X { get; }
            public double Z { get; }
            public float WaterTopUnits { get; }
            public float WidthCells { get; }
            public float BedDepthUnits { get; }
            public float WaterDepthBaseUnits { get; }
        }

        private readonly struct RiverTerrainSample
        {
            public RiverTerrainSample(
                float surfaceUnits,
                bool hasSeaWater,
                int waterTopUnits)
            {
                SurfaceUnits = surfaceUnits;
                HasSeaWater = hasSeaWater;
                WaterTopUnits = waterTopUnits;
            }

            public float SurfaceUnits { get; }
            public bool HasSeaWater { get; }
            public int WaterTopUnits { get; }
        }

        private readonly struct RiverPathSample
        {
            public RiverPathSample(
                double distanceSquared,
                float waterTopUnits,
                float widthCells,
                float bedDepthUnits,
                float waterDepthBaseUnits)
            {
                DistanceSquared = distanceSquared;
                WaterTopUnits = waterTopUnits;
                WidthCells = widthCells;
                BedDepthUnits = bedDepthUnits;
                WaterDepthBaseUnits = waterDepthBaseUnits;
            }

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

        private readonly struct RiverSegment
        {
            private readonly RiverProfilePoint from;
            private readonly RiverProfilePoint to;

            public RiverSegment(
                in RiverProfilePoint from,
                in RiverProfilePoint to)
            {
                this.from = from;
                this.to = to;
            }

            public RiverPathSample Sample(double x, double z)
            {
                var deltaX = to.X - from.X;
                var deltaZ = to.Z - from.Z;
                var lengthSquared = deltaX * deltaX + deltaZ * deltaZ;
                if (lengthSquared <= double.Epsilon)
                {
                    var pointX = x - from.X;
                    var pointZ = z - from.Z;
                    return new RiverPathSample(
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
