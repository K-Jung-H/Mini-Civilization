using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    internal static class DynamicRiverPlanner
    {
        private const int MaximumBankRepairPasses = 4;

        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        public static HydrologyFeaturePlan BuildFeaturePlan(
            WorldData validationWorld,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            IReadOnlyList<BasinPlan> basinPlans,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int worldSeed,
            WaterPlanValidationContext validationContext)
        {
            ValidateArguments(
                validationWorld,
                settings,
                hydrology,
                basinPlans,
                solidHeights,
                seaWaterSurfaces);

            var acceptedBasins = basinPlans;
            var acceptedChannels = new List<ChannelPlan>();
            var usedChannelColumns = new HashSet<int>();
            var basinByWetColumn = BuildBasinLookup(
                hydrology.ColumnCount,
                acceptedBasins);
            if (settings.RiverCount > 0)
            {
                AddHeadwaterChannels(
                    validationWorld,
                    settings,
                    hydrology,
                    acceptedBasins,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn,
                    worldSeed,
                    acceptedChannels,
                    usedChannelColumns,
                    validationContext);
                AddLakeOutlets(
                    validationWorld,
                    settings,
                    hydrology,
                    acceptedBasins,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn,
                    acceptedChannels,
                    usedChannelColumns,
                    validationContext);
            }

            if (!HydrologyFeaturePlan.TryCreate(
                    validationWorld.Size,
                    validationWorld.Height,
                    acceptedBasins,
                    acceptedChannels,
                    out var result))
            {
                throw new InvalidOperationException(
                    "Accepted water feature plans could not be merged.");
            }

            if (!WaterPlanValidator.Validate(
                    validationWorld,
                    result,
                    validationContext).IsValid)
            {
                throw new InvalidOperationException(
                    "Accepted water feature plans failed final validation.");
            }

            return result;
        }

        public static void ApplyFeaturePlan(
            HydrologyFeaturePlan featurePlan,
            int[] solidHeights,
            int[] waterSurfaces,
            WaterRole[] waterRoles,
            WaterType[] waterTypes,
            SurfaceType[] waterBedSurfaces)
        {
            if (featurePlan == null)
            {
                throw new ArgumentNullException(nameof(featurePlan));
            }

            foreach (var pair in featurePlan.TerrainColumns)
            {
                solidHeights[pair.Key] = pair.Value.TargetHeightUnits;
            }

            InlandLakePlanner.ApplyPlans(
                featurePlan.Basins,
                featurePlan.WorldSize,
                waterSurfaces,
                waterRoles,
                waterTypes,
                waterBedSurfaces);

            for (var channelIndex = 0;
                 channelIndex < featurePlan.Channels.Count;
                 channelIndex++)
            {
                var channel = featurePlan.Channels[channelIndex];
                foreach (var columnIndex in channel.ChannelColumnIndices)
                {
                    waterBedSurfaces[columnIndex] = SurfaceType.Riverbed;
                    waterTypes[columnIndex] = WaterType.River;
                }

                foreach (var pair in channel.SourceCells)
                {
                    var source = pair.Value;
                    var columnIndex = source.Coordinate.X
                        + featurePlan.WorldSize * source.Coordinate.Z;
                    var sourceSurface = (source.Coordinate.Y + 1)
                        * WorldGrid.HeightStepsPerCell;
                    waterSurfaces[columnIndex] = Math.Max(
                        waterSurfaces[columnIndex],
                        sourceSurface);
                    waterRoles[columnIndex] = WaterRole.Source;
                    waterTypes[columnIndex] = WaterType.River;
                }
            }
        }

        private static void AddLakeOutlets(
            WorldData world,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            List<ChannelPlan> acceptedChannels,
            HashSet<int> usedColumns,
            WaterPlanValidationContext validationContext)
        {
            for (var basinIndex = 0;
                 basinIndex < basins.Count
                 && acceptedChannels.Count < settings.RiverCount;
                 basinIndex++)
            {
                var basin = basins[basinIndex];
                if (!TryBuildOutletChannel(
                        world,
                        settings,
                        hydrology,
                        basin,
                        basins,
                        solidHeights,
                        seaWaterSurfaces,
                        basinByWetColumn,
                        acceptedChannels.Count,
                        usedColumns,
                        out var channel))
                {
                    continue;
                }

                TryAcceptChannel(
                    world,
                    basins,
                    solidHeights,
                    channel,
                    acceptedChannels,
                    usedColumns,
                    validationContext);
            }
        }

        private static void AddHeadwaterChannels(
            WorldData world,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            int worldSeed,
            List<ChannelPlan> acceptedChannels,
            HashSet<int> usedColumns,
            WaterPlanValidationContext validationContext)
        {
            var candidateSeed = DeterministicNoise.DeriveSeed(
                worldSeed,
                "river-spatial-candidates");
            var orderSeed = DeterministicNoise.DeriveSeed(
                worldSeed,
                "river-candidate-order");
            var targetSectorCount = Math.Max(
                4,
                (int)Math.Ceiling(Math.Sqrt(
                    Math.Max(16, settings.RiverCount * 6))));
            var sectorSize = Math.Max(
                4,
                (world.Size + targetSectorCount - 1)
                / targetSectorCount);
            var candidatesBySector = CreateHeadwaterCandidates(
                world,
                settings,
                hydrology,
                solidHeights,
                seaWaterSurfaces,
                basinByWetColumn,
                candidateSeed,
                orderSeed,
                targetSectorCount,
                sectorSize);

            var candidates = new List<HeadwaterCandidate>(
                candidatesBySector.Values);
            candidates.Sort(CompareHeadwaters);
            var candidateLimit = Math.Min(
                candidates.Count,
                Math.Max(16, settings.RiverCount * 8));
            for (var candidateIndex = 0;
                 candidateIndex < candidateLimit
                 && acceptedChannels.Count < settings.RiverCount;
                 candidateIndex++)
            {
                var start = candidates[candidateIndex].ColumnIndex;
                if (usedColumns.Contains(start))
                {
                    continue;
                }

                var generationProfile = ResolveGenerationProfile(
                    hydrology,
                    start,
                    solidHeights,
                    candidateIndex,
                    world.Seed);
                var traced = generationProfile.WaterMode
                        == RiverWaterMode.Source
                    ? TryTraceSourceChannel(
                        world,
                        hydrology,
                        start,
                        solidHeights,
                        seaWaterSurfaces,
                        basinByWetColumn,
                        basins,
                        usedColumns,
                        candidateIndex,
                        out var trace)
                    : TryTraceDynamicChannel(
                        hydrology,
                        start,
                        -1,
                        solidHeights,
                        seaWaterSurfaces,
                        basinByWetColumn,
                        basins,
                        usedColumns,
                        out trace);
                if (!traced)
                {
                    continue;
                }

                if (!TryBuildChannel(
                        world,
                        settings,
                        hydrology,
                        trace,
                        basins,
                        solidHeights,
                        seaWaterSurfaces,
                        basinByWetColumn,
                        true,
                        null,
                        generationProfile,
                        out var channel))
                {
                    continue;
                }

                TryAcceptChannel(
                    world,
                    basins,
                    solidHeights,
                    channel,
                    acceptedChannels,
                    usedColumns,
                    validationContext);
            }
        }

        private static Dictionary<int, HeadwaterCandidate>
            CreateHeadwaterCandidates(
                WorldData world,
                WorldBuildInput settings,
                HydrologyMap hydrology,
                IReadOnlyList<int> solidHeights,
                IReadOnlyList<int> seaWaterSurfaces,
                int[] basinByWetColumn,
                int candidateSeed,
                int orderSeed,
                int targetSectorCount,
                int sectorSize)
        {
            var candidateByColumn = new HeadwaterCandidate[
                hydrology.ColumnCount];
            var hasCandidate = new bool[hydrology.ColumnCount];
            Parallel.For(0, hydrology.ColumnCount, index =>
            {
                if (basinByWetColumn[index] >= 0
                    || seaWaterSurfaces[index] > 0
                    || solidHeights[index] < settings.SeaLevelUnits
                        + WorldGrid.HeightStepsPerCell)
                {
                    return;
                }

                var x = index % world.Size;
                var z = index / world.Size;
                if (x < 2 || z < 2
                    || x >= world.Size - 2 || z >= world.Size - 2
                    || IsAdjacentToPlannedWater(
                        world.Size,
                        index,
                        seaWaterSurfaces,
                        basinByWetColumn))
                {
                    return;
                }

                var localFitness = (float)Math.Log(
                        hydrology.GetFlowAccumulation(index) + 1d,
                        2d) * 2f
                    + Math.Min(
                        hydrology.GetSeaDistance(index),
                        world.Size / 3) * 0.1f
                    + DeterministicNoise.Value01(
                        x,
                        z,
                        candidateSeed) * 5f;
                var sectorX = x / sectorSize;
                var sectorZ = z / sectorSize;
                candidateByColumn[index] = new HeadwaterCandidate(
                    index,
                    localFitness,
                    DeterministicNoise.Value01(
                        sectorX,
                        sectorZ,
                        orderSeed));
                hasCandidate[index] = true;
            });

            var candidatesBySector = new Dictionary<
                int,
                HeadwaterCandidate>();
            for (var index = 0; index < candidateByColumn.Length; index++)
            {
                if (!hasCandidate[index])
                {
                    continue;
                }

                var sectorX = (index % world.Size) / sectorSize;
                var sectorZ = (index / world.Size) / sectorSize;
                var sectorIndex = sectorX + targetSectorCount * sectorZ;
                var candidate = candidateByColumn[index];
                if (!candidatesBySector.TryGetValue(
                        sectorIndex,
                        out var existing)
                    || candidate.Fitness > existing.Fitness)
                {
                    candidatesBySector[sectorIndex] = candidate;
                }
            }

            return candidatesBySector;
        }

        private static bool IsAdjacentToPlannedWater(
            int size,
            int columnIndex,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn)
        {
            var x = columnIndex % size;
            var z = columnIndex / size;
            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                var nextX = x + Directions[directionIndex].x;
                var nextZ = z + Directions[directionIndex].z;
                if ((uint)nextX >= size || (uint)nextZ >= size)
                {
                    continue;
                }

                var nextIndex = nextX + size * nextZ;
                if (seaWaterSurfaces[nextIndex] > 0
                    || basinByWetColumn[nextIndex] >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryBuildOutletChannel(
            WorldData world,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            BasinPlan sourceBasin,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            int profileSlot,
            HashSet<int> usedColumns,
            out ChannelPlan channel)
        {
            channel = null;
            var shore = sourceBasin.OutletColumnIndex;
            if (shore < 0
                || basinByWetColumn[shore] >= 0
                || seaWaterSurfaces[shore] > 0
                || usedColumns.Contains(shore)
                || !TryFindAdjacentBasinWetColumn(
                    world.Size,
                    shore,
                    sourceBasin.BasinId,
                    basinByWetColumn,
                    out var sourceWetColumn))
            {
                return false;
            }

            if (!TryTraceDynamicChannel(
                    hydrology,
                    shore,
                    sourceBasin.BasinId,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn,
                    basins,
                    usedColumns,
                    out var trace)
                || (trace.Target.HasWaterBody
                    && trace.Target.SurfaceHeightUnits
                        > sourceBasin.WaterSurfaceHeightUnits))
            {
                return false;
            }

            var outlet = new BasinConnectionPort(
                sourceWetColumn,
                shore,
                sourceBasin.WaterSurfaceHeightUnits);
            var profile = ResolveGenerationProfile(
                hydrology,
                shore,
                solidHeights,
                profileSlot,
                world.Seed);
            return TryBuildChannel(
                world,
                settings,
                hydrology,
                trace,
                basins,
                solidHeights,
                seaWaterSurfaces,
                basinByWetColumn,
                false,
                outlet,
                profile,
                out channel);
        }

        private static bool TryTraceDynamicChannel(
            HydrologyMap hydrology,
            int start,
            int ignoredBasinId,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            IReadOnlyList<BasinPlan> basins,
            HashSet<int> usedColumns,
            out ChannelTrace trace)
        {
            var path = new List<int>();
            var visited = new HashSet<int>();
            var current = start;
            while (current >= 0)
            {
                if (!visited.Add(current)
                    || usedColumns.Contains(current)
                    || basinByWetColumn[current] >= 0
                    || seaWaterSurfaces[current] > 0)
                {
                    break;
                }

                path.Add(current);
                if (TryFindAdjacentWaterBody(
                        hydrology.Size,
                        current,
                        ignoredBasinId,
                        seaWaterSurfaces,
                        basinByWetColumn,
                        basins,
                        out var target))
                {
                    if (path.Count >= 2)
                    {
                        trace = new ChannelTrace(path, target);
                        return true;
                    }

                    break;
                }

                if (!TryChooseDynamicNext(
                        hydrology,
                        current,
                        solidHeights,
                        seaWaterSurfaces,
                        basinByWetColumn,
                        usedColumns,
                        visited,
                        out current))
                {
                    break;
                }
            }

            if (path.Count >= 2)
            {
                trace = new ChannelTrace(
                    path,
                    ChannelTerminal.Independent);
                return true;
            }

            trace = default;
            return false;
        }

        private static bool TryChooseDynamicNext(
            HydrologyMap hydrology,
            int current,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            HashSet<int> usedColumns,
            HashSet<int> visited,
            out int next)
        {
            var preferred = hydrology.GetReceiverColumnIndex(current);
            var currentX = current % hydrology.Size;
            var currentZ = current / hydrology.Size;
            var bestCost = float.MaxValue;
            next = -1;
            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                var nextX = currentX + Directions[directionIndex].x;
                var nextZ = currentZ + Directions[directionIndex].z;
                if (!hydrology.Contains(nextX, nextZ))
                {
                    continue;
                }

                var candidate = hydrology.ToIndex(nextX, nextZ);
                if (visited.Contains(candidate)
                    || usedColumns.Contains(candidate)
                    || seaWaterSurfaces[candidate] > 0
                    || basinByWetColumn[candidate] >= 0)
                {
                    continue;
                }

                var rise = solidHeights[candidate]
                    - solidHeights[current];

                var cost = candidate == preferred ? 0f : 4f;
                cost += Math.Max(0, rise) * 2f;
                cost += hydrology.GetFilledHeightUnits(candidate) * 0.001f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    next = candidate;
                }
            }

            return next >= 0;
        }

        private static bool TryTraceSourceChannel(
            WorldData world,
            HydrologyMap hydrology,
            int start,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            IReadOnlyList<BasinPlan> basins,
            HashSet<int> usedColumns,
            int routeSlot,
            out ChannelTrace trace)
        {
            var directionSeed = DeterministicNoise.DeriveSeed(
                world.Seed,
                "source-river-direction");
            var startX = start % world.Size;
            var startZ = start / world.Size;
            var forwardDirection = (int)(DeterministicNoise.Hash(
                startX,
                startZ,
                directionSeed + routeSlot) % (uint)Directions.Length);
            var visited = new HashSet<int> { start };
            var backward = ExtendSourceFront(
                (forwardDirection + 2) % Directions.Length,
                out var startTarget);
            var forward = ExtendSourceFront(
                forwardDirection,
                out var endTarget);
            if (backward.Count + forward.Count < 1)
            {
                trace = default;
                return false;
            }

            backward.Reverse();
            var path = new List<int>(
                backward.Count + 1 + forward.Count);
            path.AddRange(backward);
            path.Add(start);
            path.AddRange(forward);
            trace = new ChannelTrace(path, startTarget, endTarget);
            return path.Count >= 2;

            List<int> ExtendSourceFront(
                int initialDirection,
                out ChannelTerminal terminal)
            {
                var result = new List<int>();
                var current = start;
                var previousDirection = initialDirection;
                terminal = ChannelTerminal.Independent;
                while (true)
                {
                    if (result.Count > 0
                        && TryFindAdjacentWaterBody(
                            world.Size,
                            current,
                            -1,
                            seaWaterSurfaces,
                            basinByWetColumn,
                            basins,
                            out terminal))
                    {
                        break;
                    }

                    if (!TryChooseSourceNext(
                            current,
                            previousDirection,
                            out var next,
                            out var nextDirection))
                    {
                        terminal = ChannelTerminal.Independent;
                        break;
                    }

                    visited.Add(next);
                    result.Add(next);
                    current = next;
                    previousDirection = nextDirection;
                }

                return result;
            }

            bool TryChooseSourceNext(
                int current,
                int previousDirection,
                out int next,
                out int nextDirection)
            {
                var currentX = current % world.Size;
                var currentZ = current / world.Size;
                var receiver = hydrology.GetReceiverColumnIndex(current);
                var routeSeed = DeterministicNoise.DeriveSeed(
                    world.Seed,
                    "source-river-route");
                var bestCost = float.MaxValue;
                next = -1;
                nextDirection = previousDirection;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    if (directionIndex
                        == (previousDirection + 2) % Directions.Length)
                    {
                        continue;
                    }

                    var nextX = currentX + Directions[directionIndex].x;
                    var nextZ = currentZ + Directions[directionIndex].z;
                    if (!hydrology.Contains(nextX, nextZ))
                    {
                        continue;
                    }

                    var candidate = hydrology.ToIndex(nextX, nextZ);
                    if (visited.Contains(candidate)
                        || usedColumns.Contains(candidate)
                        || seaWaterSurfaces[candidate] > 0
                        || basinByWetColumn[candidate] >= 0)
                    {
                        continue;
                    }

                    var turnCost = directionIndex == previousDirection
                        ? 0f
                        : 1.5f;
                    var terrainCost = Math.Abs(
                        solidHeights[candidate]
                        - solidHeights[current]) * 0.35f;
                    var hydrologyCost = candidate == receiver
                        ? -0.35f
                        : 0.15f;
                    var variation = DeterministicNoise.ValueNoise(
                        nextX * 0.15f,
                        nextZ * 0.15f,
                        routeSeed + routeSlot) * 0.9f;
                    var cost = turnCost
                        + terrainCost
                        + hydrologyCost
                        + variation;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        next = candidate;
                        nextDirection = directionIndex;
                    }
                }

                return next >= 0;
            }
        }

        private static bool TryBuildChannel(
            WorldData world,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            ChannelTrace trace,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            bool hasHeadwaterSource,
            BasinConnectionPort? outlet,
            RiverGenerationProfile preferredProfile,
            out ChannelPlan channel)
        {
            var profileOrder = BuildGenerationProfileOrder(preferredProfile);
            for (var profileIndex = 0;
                 profileIndex < profileOrder.Count;
                 profileIndex++)
            {
                var profile = profileOrder[profileIndex];
                var maximumWidth = ResolveMaximumWidth(
                    settings.MaximumRiverWidthCells);
                if (TryBuildChannelWithWidthLimit(
                        world,
                        settings,
                        hydrology,
                        trace,
                        basins,
                        solidHeights,
                        seaWaterSurfaces,
                        basinByWetColumn,
                        hasHeadwaterSource,
                        outlet,
                        profile,
                        maximumWidth,
                        out channel))
                {
                    return true;
                }
            }

            channel = null;
            return false;
        }

        private static bool TryBuildChannelWithWidthLimit(
            WorldData world,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            ChannelTrace trace,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            bool hasHeadwaterSource,
            BasinConnectionPort? outlet,
            RiverGenerationProfile generationProfile,
            int widthLimit,
            out ChannelPlan channel)
        {
            channel = null;
            var widths = BuildSectionWidths(
                trace,
                hydrology,
                widthLimit,
                generationProfile.WaterMode,
                generationProfile.TerrainStyle,
                world.Seed);
            ConstrainSectionWidths(
                widths,
                trace,
                hydrology,
                seaWaterSurfaces,
                basinByWetColumn);
            var levels = BuildSurfaceProfile(
                world,
                trace,
                widths,
                hydrology,
                solidHeights,
                seaWaterSurfaces,
                basinByWetColumn,
                outlet?.InterfaceSurfaceHeightUnits,
                generationProfile);
            if (levels == null)
            {
                return false;
            }
            var depths = BuildSectionDepths(
                widths,
                settings,
                generationProfile.TerrainStyle,
                world.Seed,
                world.Size,
                trace);

            var bedHeights = new int[trace.Path.Count];
            var channelCells = new Dictionary<int, ChannelCellProfile>();
            for (var pathIndex = 0;
                 pathIndex < trace.Path.Count;
                 pathIndex++)
            {
                var centerIndex = trace.Path[pathIndex];
                bedHeights[pathIndex] = ResolveChannelBedHeight(
                    centerIndex,
                    levels[pathIndex],
                    depths[pathIndex]);
                if (!TryAddChannelCell(
                        centerIndex,
                        bedHeights[pathIndex],
                        levels[pathIndex],
                        ResolveMaximumWaterTop(pathIndex),
                        pathIndex,
                        true))
                {
                    return false;
                }

                var radius = widths[pathIndex] / 2;
                if (radius == 0)
                {
                    continue;
                }

                ResolvePerpendicular(
                    trace.Path,
                    world.Size,
                    pathIndex,
                    out var perpendicularX,
                    out var perpendicularZ);
                var centerX = centerIndex % world.Size;
                var centerZ = centerIndex / world.Size;
                for (var offset = 1; offset <= radius; offset++)
                {
                    if (!TryAddLateral(offset)
                        || !TryAddLateral(-offset))
                    {
                        return false;
                    }
                }

                bool TryAddLateral(int offset)
                {
                    var x = centerX + perpendicularX * offset;
                    var z = centerZ + perpendicularZ * offset;
                    if (!hydrology.Contains(x, z))
                    {
                        return false;
                    }

                    var lateralIndex = hydrology.ToIndex(x, z);
                    var lateralDepth = ResolveCrossSectionDepth(
                        widths[pathIndex],
                        depths[pathIndex],
                        Math.Abs(offset));
                    var lateralBed = ResolveChannelBedHeight(
                        lateralIndex,
                        levels[pathIndex],
                        lateralDepth);
                    return TryAddChannelCell(
                            lateralIndex,
                            lateralBed,
                            levels[pathIndex],
                            levels[pathIndex],
                            pathIndex,
                            false);
                }

                int ResolveChannelBedHeight(
                    int columnIndex,
                    int surfaceHeight,
                    int depthCells)
                {
                    var originalHeight = solidHeights[columnIndex];
                    var desiredBedHeight = Math.Max(
                        0,
                        surfaceHeight
                        - depthCells * WorldGrid.HeightStepsPerCell);
                    int resolvedBedHeight;
                    if (desiredBedHeight >= originalHeight)
                    {
                        resolvedBedHeight = desiredBedHeight;
                    }
                    else if (originalHeight
                        <= WorldGrid.HeightStepsPerCell)
                    {
                        resolvedBedHeight = originalHeight;
                    }
                    else
                    {
                        resolvedBedHeight = Math.Max(
                            WorldGrid.HeightStepsPerCell,
                            desiredBedHeight);
                    }

                    return resolvedBedHeight;
                }

                int ResolveMaximumWaterTop(int index)
                {
                    if (generationProfile.WaterMode
                        == RiverWaterMode.Source)
                    {
                        return levels[index];
                    }

                    var maximum = levels[index];
                    if (index > 0)
                    {
                        maximum = Math.Max(maximum, levels[index - 1]);
                    }

                    if (index + 1 < levels.Length)
                    {
                        maximum = Math.Max(maximum, levels[index + 1]);
                    }

                    return maximum;
                }
            }

            var channelColumnSet = new HashSet<int>(channelCells.Keys);
            foreach (var profile in channelCells.Values)
            {
                if (profile.SurfaceHeightUnits
                    <= profile.BedHeightUnits)
                {
                    return false;
                }
            }

            for (var pathIndex = 0;
                 pathIndex < trace.Path.Count;
                 pathIndex++)
            {
                var centerProfile = channelCells[trace.Path[pathIndex]];
                levels[pathIndex] = centerProfile.SurfaceHeightUnits;
                bedHeights[pathIndex] = centerProfile.BedHeightUnits;
            }

            var sourceTransitionTops = generationProfile.WaterMode
                    == RiverWaterMode.Source
                ? BuildSourceTransitionTops(
                    trace,
                    widths,
                    levels,
                    hydrology,
                    channelColumnSet)
                : null;

            var terrainTargets = new Dictionary<int, int>();
            foreach (var pair in channelCells)
            {
                var columnIndex = pair.Key;
                var profile = pair.Value;
                var bedHeight = profile.BedHeightUnits;
                if (bedHeight < 0)
                {
                    return false;
                }

                terrainTargets[columnIndex] = bedHeight;
            }

            if (!TryBuildRiverCorridorTerrain(
                    world,
                    settings,
                    generationProfile.TerrainStyle,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn,
                    channelColumnSet,
                    channelCells,
                    sourceTransitionTops,
                    terrainTargets))
            {
                return false;
            }

            foreach (var pair in channelCells)
            {
                var columnIndex = pair.Key;
                var profile = pair.Value;
                var x = columnIndex % world.Size;
                var z = columnIndex / world.Size;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var nextX = x + Directions[directionIndex].x;
                    var nextZ = z + Directions[directionIndex].z;
                    if (!hydrology.Contains(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = hydrology.ToIndex(nextX, nextZ);
                    if (channelColumnSet.Contains(nextIndex))
                    {
                        continue;
                    }

                    var isTargetBody = nextIndex
                        == trace.Target.WetColumnIndex
                        && profile.IsCenterline
                        && profile.PathIndex == trace.Path.Count - 1;
                    var isStartBody = nextIndex
                        == trace.StartTarget.WetColumnIndex
                        && profile.IsCenterline
                        && profile.PathIndex == 0;
                    if (seaWaterSurfaces[nextIndex] > 0
                        || basinByWetColumn[nextIndex] >= 0)
                    {
                        if (!isTargetBody
                            && !isStartBody
                            && !(outlet.HasValue
                                && nextIndex
                                    == outlet.Value.BasinWetColumnIndex
                                && profile.IsCenterline
                                && profile.PathIndex == 0))
                        {
                            return false;
                        }

                        continue;
                    }

                }
            }

            if (generationProfile.WaterMode == RiverWaterMode.Source
                && !TryAddSourceContainmentBanks(
                    world,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn,
                    channelColumnSet,
                    channelCells,
                    sourceTransitionTops,
                    terrainTargets))
            {
                return false;
            }

            var result = new ChannelPlan(
                world.Size,
                world.Height);
            foreach (var pair in terrainTargets)
            {
                var x = pair.Key % world.Size;
                var z = pair.Key / world.Size;
                result.SetTerrainColumn(new PlannedTerrainColumn(
                    x,
                    z,
                    pair.Value));
            }

            foreach (var pair in channelCells)
            {
                result.AddChannelColumn(pair.Key);
                var maximumWaterTop = pair.Value.MaximumWaterTopUnits;
                if (sourceTransitionTops != null
                    && sourceTransitionTops.TryGetValue(
                        pair.Key,
                        out var transitionTop))
                {
                    maximumWaterTop = Math.Max(
                        maximumWaterTop,
                        transitionTop);
                }

                AddChannelWetCells(
                    result,
                    world.Size,
                    pair.Key,
                    pair.Value.BedHeightUnits,
                    maximumWaterTop);
            }

            if (generationProfile.WaterMode == RiverWaterMode.Source)
            {
                if (!AddPersistentChannelSources(
                        result,
                        channelCells,
                        sourceTransitionTops))
                {
                    return false;
                }
            }
            else if (hasHeadwaterSource)
            {
                AddHeadwaterSources(
                    result,
                    trace.Path,
                    bedHeights,
                    levels);
            }

            if (outlet.HasValue)
            {
                result.AddConnection(outlet.Value);
            }


            if (trace.StartTarget.BasinId >= 0)
            {
                var shore = trace.Path[0];
                result.AddConnection(new BasinConnectionPort(
                    trace.StartTarget.WetColumnIndex,
                    shore,
                    trace.StartTarget.SurfaceHeightUnits));
            }

            if (trace.Target.BasinId >= 0)
            {
                var shore = trace.Path[^1];
                result.AddConnection(new BasinConnectionPort(
                    trace.Target.WetColumnIndex,
                    shore,
                    trace.Target.SurfaceHeightUnits));
            }

            channel = result;
            return true;

            bool TryAddChannelCell(
                int columnIndex,
                int bedHeight,
                int surfaceHeight,
                int maximumWaterTop,
                int pathIndex,
                bool isCenterline)
            {
                if (seaWaterSurfaces[columnIndex] > 0
                    || basinByWetColumn[columnIndex] >= 0)
                {
                    return false;
                }

                if (channelCells.TryGetValue(
                        columnIndex,
                        out var existing))
                {
                    var mergedSurface = generationProfile.WaterMode
                            == RiverWaterMode.Source
                        ? Math.Min(
                            existing.SurfaceHeightUnits,
                            surfaceHeight)
                        : Math.Max(
                            existing.SurfaceHeightUnits,
                            surfaceHeight);
                    channelCells[columnIndex] = new ChannelCellProfile(
                        Math.Min(existing.BedHeightUnits, bedHeight),
                        mergedSurface,
                        generationProfile.WaterMode
                                == RiverWaterMode.Source
                            ? mergedSurface
                            : Math.Max(
                                existing.MaximumWaterTopUnits,
                                maximumWaterTop),
                        Math.Min(existing.PathIndex, pathIndex),
                        existing.IsCenterline || isCenterline);
                }
                else
                {
                    channelCells[columnIndex] = new ChannelCellProfile(
                        bedHeight,
                        surfaceHeight,
                        maximumWaterTop,
                        pathIndex,
                        isCenterline);
                }

                return true;
            }
        }

        private static int[] BuildSurfaceProfile(
            WorldData world,
            ChannelTrace trace,
            IReadOnlyList<int> widths,
            HydrologyMap hydrology,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            int? fixedStartSurface,
            RiverGenerationProfile generationProfile)
        {
            var levels = new int[trace.Path.Count];
            if (generationProfile.WaterMode == RiverWaterMode.Source)
            {
                return BuildSourceCorridorSurfaceProfile(
                    world,
                    trace,
                    widths,
                    hydrology,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn,
                    fixedStartSurface);
            }

            var targetSurfaceHeight = trace.Target.HasWaterBody
                ? trace.Target.SurfaceHeightUnits
                : ResolveIndependentTerminalSurface();
            levels[^1] = targetSurfaceHeight;
            for (var index = trace.Path.Count - 2; index >= 0; index--)
            {
                var naturalSurface = ResolveNaturalSurface(index);
                levels[index] = Math.Max(levels[index + 1], naturalSurface);
            }

            if (!fixedStartSurface.HasValue)
            {
                var sourceSurface = AlignToCellCeiling(
                    solidHeights[trace.Path[0]]);
                if (levels.Length > 1
                    && sourceSurface < levels[1])
                {
                    return null;
                }

                levels[0] = sourceSurface;
            }

            if (fixedStartSurface.HasValue)
            {
                if (targetSurfaceHeight
                        > fixedStartSurface.Value)
                {
                    return null;
                }

                levels[0] = fixedStartSurface.Value;
                for (var index = 1; index < levels.Length - 1; index++)
                {
                    var naturalSurface = ResolveNaturalSurface(index);
                    levels[index] = Math.Max(
                        targetSurfaceHeight,
                        Math.Min(levels[index - 1], naturalSurface));
                }

                levels[^1] = targetSurfaceHeight;
                if (levels.Length > 1
                    && levels[^1] > levels[^2])
                {
                    return null;
                }
            }

            if (generationProfile.WaterMode == RiverWaterMode.Dynamic
                && !HasReachableDynamicSurfaceProfile(levels))
            {
                return null;
            }

            return levels;

            int ResolveIndependentTerminalSurface()
            {
                var lastIndex = trace.Path.Count - 1;
                var solidHeight = solidHeights[trace.Path[lastIndex]];
                var surface = solidHeight;
                return generationProfile.TerrainStyle
                        == RiverTerrainStyle.Stepped
                    ? surface
                        / WorldGrid.HeightStepsPerCell
                        * WorldGrid.HeightStepsPerCell
                    : surface;
            }

            int ResolveNaturalSurface(int index)
            {
                var solidHeight = solidHeights[trace.Path[index]];
                var natural = solidHeight;
                if (generationProfile.TerrainStyle
                        == RiverTerrainStyle.Lowland
                    && index + 1 < levels.Length)
                {
                    natural = Math.Min(natural, levels[index + 1] + 1);
                }
                else if (generationProfile.TerrainStyle
                    == RiverTerrainStyle.Stepped)
                {
                    natural = natural
                        / WorldGrid.HeightStepsPerCell
                        * WorldGrid.HeightStepsPerCell;
                }

                return natural;
            }

            bool HasReachableDynamicSurfaceProfile(
                IReadOnlyList<int> surfaceLevels)
            {
                var maximumFlatRun = Math.Max(
                    1,
                    WaterFlowReachability.GetSafeHorizontalSpreadCount(
                        world.WaterFlowRules));
                var flatRun = 1;
                for (var index = 1; index < surfaceLevels.Count; index++)
                {
                    if (surfaceLevels[index] > surfaceLevels[index - 1])
                    {
                        return false;
                    }

                    flatRun = surfaceLevels[index]
                            == surfaceLevels[index - 1]
                        ? flatRun + 1
                        : 1;
                    if (flatRun > maximumFlatRun)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private static int[] BuildSourceCorridorSurfaceProfile(
            WorldData world,
            ChannelTrace trace,
            IReadOnlyList<int> widths,
            HydrologyMap hydrology,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            int? fixedStartSurface)
        {
            var count = trace.Path.Count;
            var naturalCorridorHeights = new int[count];
            for (var pathIndex = 0; pathIndex < count; pathIndex++)
            {
                ResolvePerpendicular(
                    trace.Path,
                    world.Size,
                    pathIndex,
                    out var perpendicularX,
                    out var perpendicularZ);
                var centerIndex = trace.Path[pathIndex];
                var centerX = centerIndex % world.Size;
                var centerZ = centerIndex / world.Size;
                var sampleRadius = widths[pathIndex] / 2 + 1;
                var minimumHeight = solidHeights[centerIndex];
                for (var offset = -sampleRadius;
                     offset <= sampleRadius;
                     offset++)
                {
                    var x = centerX + perpendicularX * offset;
                    var z = centerZ + perpendicularZ * offset;
                    if (!hydrology.Contains(x, z))
                    {
                        continue;
                    }

                    var columnIndex = hydrology.ToIndex(x, z);
                    if (seaWaterSurfaces[columnIndex] > 0
                        || basinByWetColumn[columnIndex] >= 0)
                    {
                        continue;
                    }

                    minimumHeight = Math.Min(
                        minimumHeight,
                        solidHeights[columnIndex]);
                }

                naturalCorridorHeights[pathIndex] = minimumHeight;
            }

            var smoothed = new int[count];
            var smoothingRadius = Math.Clamp(count / 12, 2, 6);
            for (var pathIndex = 0; pathIndex < count; pathIndex++)
            {
                var start = Math.Max(0, pathIndex - smoothingRadius);
                var end = Math.Min(count - 1, pathIndex + smoothingRadius);
                var sum = 0;
                var minimum = int.MaxValue;
                for (var sampleIndex = start;
                     sampleIndex <= end;
                     sampleIndex++)
                {
                    var height = naturalCorridorHeights[sampleIndex];
                    sum += height;
                    minimum = Math.Min(minimum, height);
                }

                var average = sum / (end - start + 1);
                smoothed[pathIndex] = (average * 2 + minimum) / 3;
            }

            var hasStartAnchor = fixedStartSurface.HasValue
                || trace.StartTarget.HasWaterBody;
            var rawStartAnchor = fixedStartSurface
                ?? (trace.StartTarget.HasWaterBody
                    ? trace.StartTarget.SurfaceHeightUnits
                    : 0);
            var startAnchor = hasStartAnchor
                ? AlignToCellCeiling(rawStartAnchor)
                : 0;
            var hasEndAnchor = trace.Target.HasWaterBody;
            var endAnchor = hasEndAnchor
                ? AlignToCellCeiling(
                    trace.Target.SurfaceHeightUnits)
                : 0;
            var maximumGradeUnits = 1;
            if (hasStartAnchor && hasEndAnchor && count > 1)
            {
                maximumGradeUnits = Math.Max(
                    maximumGradeUnits,
                    (Math.Abs(endAnchor - startAnchor) + count - 2)
                    / (count - 1));
                if (maximumGradeUnits > WorldGrid.HeightStepsPerCell)
                {
                    return null;
                }
            }

            for (var pass = 0; pass < 8; pass++)
            {
                if (hasStartAnchor)
                {
                    smoothed[0] = startAnchor;
                }

                for (var pathIndex = 1;
                     pathIndex < count;
                     pathIndex++)
                {
                    if (hasEndAnchor && pathIndex == count - 1)
                    {
                        continue;
                    }

                    smoothed[pathIndex] = Math.Clamp(
                        smoothed[pathIndex],
                        smoothed[pathIndex - 1] - maximumGradeUnits,
                        smoothed[pathIndex - 1] + maximumGradeUnits);
                }

                if (hasEndAnchor)
                {
                    smoothed[^1] = endAnchor;
                }

                for (var pathIndex = count - 2;
                     pathIndex >= 0;
                     pathIndex--)
                {
                    if (hasStartAnchor && pathIndex == 0)
                    {
                        continue;
                    }

                    smoothed[pathIndex] = Math.Clamp(
                        smoothed[pathIndex],
                        smoothed[pathIndex + 1] - maximumGradeUnits,
                        smoothed[pathIndex + 1] + maximumGradeUnits);
                }
            }

            var levels = new int[count];
            for (var pathIndex = 0; pathIndex < count; pathIndex++)
            {
                levels[pathIndex] = Math.Max(
                    WorldGrid.HeightStepsPerCell,
                    AlignToCellFloor(smoothed[pathIndex]));
            }

            if (hasStartAnchor)
            {
                levels[0] = startAnchor;
            }

            if (hasEndAnchor)
            {
                levels[^1] = endAnchor;
            }

            return levels;
        }

        private static int[] BuildSectionWidths(
            ChannelTrace trace,
            HydrologyMap hydrology,
            int maximumWidth,
            RiverWaterMode waterMode,
            RiverTerrainStyle terrainStyle,
            int worldSeed)
        {
            maximumWidth = Math.Max(1, maximumWidth);
            if ((maximumWidth & 1) == 0)
            {
                maximumWidth--;
            }

            var widths = new int[trace.Path.Count];
            Array.Fill(widths, 1);
            if (maximumWidth == 1 || trace.Path.Count <= 2)
            {
                return widths;
            }

            var minimumLogAccumulation = double.MaxValue;
            var maximumLogAccumulation = double.MinValue;
            for (var pathIndex = 0;
                 pathIndex < trace.Path.Count;
                 pathIndex++)
            {
                var logAccumulation = Math.Log(
                    hydrology.GetFlowAccumulation(trace.Path[pathIndex]) + 1d,
                    2d);
                minimumLogAccumulation = Math.Min(
                    minimumLogAccumulation,
                    logAccumulation);
                maximumLogAccumulation = Math.Max(
                    maximumLogAccumulation,
                    logAccumulation);
            }

            var widthTierCount = (maximumWidth - 1) / 2;
            var variationSeed = DeterministicNoise.DeriveSeed(
                worldSeed,
                "river-channel-width");
            var fullWidthLength = Math.Max(
                4d,
                hydrology.Size * 0.75d);
            var relativeLength = Math.Clamp(
                (trace.Path.Count - 2d) / (fullWidthLength - 2d),
                0d,
                1d);
            for (var pathIndex = 0;
                 pathIndex < trace.Path.Count;
                 pathIndex++)
            {
                var logAccumulation = Math.Log(
                    hydrology.GetFlowAccumulation(trace.Path[pathIndex]) + 1d,
                    2d);
                var accumulationRange = maximumLogAccumulation
                    - minimumLogAccumulation;
                var relativeAccumulation = accumulationRange > 0.0001d
                    ? (logAccumulation - minimumLogAccumulation)
                        / accumulationRange
                    : 0d;
                var columnIndex = trace.Path[pathIndex];
                var x = columnIndex % hydrology.Size;
                var z = columnIndex / hydrology.Size;
                var variation = DeterministicNoise.ValueNoise(
                    x * 0.2f,
                    z * 0.2f,
                    variationSeed);
                var pathPosition = pathIndex
                    / (double)(trace.Path.Count - 1);
                var centerWeight = Math.Sin(Math.PI * pathPosition);
                var structuralWidth = relativeLength
                    * (0.25d + centerWeight * 0.75d);
                var styleBias = terrainStyle switch
                {
                    RiverTerrainStyle.Lowland => 0.1d,
                    RiverTerrainStyle.Mountain => -0.1d,
                    _ => 0d
                };
                var accumulationWeight = waterMode
                        == RiverWaterMode.Source
                    ? 0.1d
                    : 0.25d;
                var structuralWeight = waterMode
                        == RiverWaterMode.Source
                    ? 0.75d
                    : 0.6d;
                var variationWeight = 1d
                    - accumulationWeight
                    - structuralWeight;
                var widthSignal = Math.Clamp(
                    relativeAccumulation * accumulationWeight
                    + structuralWidth * structuralWeight
                    + variation * variationWeight
                    + styleBias,
                    0d,
                    1d);
                var widthTier = Math.Clamp(
                    (int)Math.Round(widthSignal * widthTierCount),
                    0,
                    widthTierCount);
                widths[pathIndex] = 1 + widthTier * 2;
            }

            widths[0] = 1;
            widths[^1] = 1;

            for (var pathIndex = 1;
                 pathIndex < widths.Length;
                 pathIndex++)
            {
                widths[pathIndex] = Math.Min(
                    widths[pathIndex],
                    widths[pathIndex - 1] + 2);
            }

            for (var pathIndex = widths.Length - 2;
                 pathIndex >= 0;
                 pathIndex--)
            {
                widths[pathIndex] = Math.Min(
                    widths[pathIndex],
                    widths[pathIndex + 1] + 2);
            }

            return widths;
        }

        private static bool TryBuildRiverCorridorTerrain(
            WorldData world,
            WorldBuildInput settings,
            RiverTerrainStyle terrainStyle,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            HashSet<int> channelColumns,
            IReadOnlyDictionary<int, ChannelCellProfile> channelCells,
            IReadOnlyDictionary<int, int> transitionTops,
            Dictionary<int, int> terrainTargets)
        {
            var columnCount = checked(world.Size * world.Size);
            var distances = new int[columnCount];
            var owners = new int[columnCount];
            Array.Fill(distances, -1);
            Array.Fill(owners, -1);
            var frontier = new Queue<int>();
            foreach (var columnIndex in channelColumns)
            {
                distances[columnIndex] = 0;
                owners[columnIndex] = columnIndex;
                frontier.Enqueue(columnIndex);
            }

            var maximumBlendDistance = Math.Max(
                6,
                settings.MaximumRiverWidthCells * 2);
            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                var distance = distances[current];
                if (distance >= maximumBlendDistance)
                {
                    continue;
                }

                var x = current % world.Size;
                var z = current / world.Size;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var nextX = x + Directions[directionIndex].x;
                    var nextZ = z + Directions[directionIndex].z;
                    if (!world.ContainsColumn(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = nextX + world.Size * nextZ;
                    if (seaWaterSurfaces[nextIndex] > 0
                        || basinByWetColumn[nextIndex] >= 0)
                    {
                        continue;
                    }

                    var nextDistance = distance + 1;
                    if (distances[nextIndex] >= 0
                        && distances[nextIndex] <= nextDistance)
                    {
                        continue;
                    }

                    distances[nextIndex] = nextDistance;
                    owners[nextIndex] = owners[current];
                    frontier.Enqueue(nextIndex);
                }
            }

            var maximumWorldHeight = checked(
                world.Height * WorldGrid.HeightStepsPerCell);
            for (var columnIndex = 0;
                 columnIndex < columnCount;
                 columnIndex++)
            {
                var distance = distances[columnIndex];
                if (distance <= 0 || distance > maximumBlendDistance)
                {
                    continue;
                }

                var ownerIndex = owners[columnIndex];
                if (ownerIndex < 0
                    || !channelCells.TryGetValue(
                        ownerIndex,
                        out var ownerProfile))
                {
                    continue;
                }

                var ownerSurfaceHeight = ownerProfile.SurfaceHeightUnits;
                if (transitionTops != null
                    && transitionTops.TryGetValue(
                        ownerIndex,
                        out var transitionTop))
                {
                    ownerSurfaceHeight = Math.Max(
                        ownerSurfaceHeight,
                        transitionTop);
                }

                var localTerrainStyle = ResolveLocalTerrainStyle(
                    ownerIndex);
                var blendDistance = localTerrainStyle switch
                {
                    RiverTerrainStyle.Lowland => Math.Max(
                        6,
                        settings.MaximumRiverWidthCells * 2),
                    RiverTerrainStyle.Mountain => Math.Max(
                        3,
                        settings.MaximumRiverWidthCells),
                    _ => Math.Max(
                        4,
                        settings.MaximumRiverWidthCells + 2)
                };
                if (distance > blendDistance)
                {
                    continue;
                }

                var originalHeight = solidHeights[columnIndex];
                var innerHeight = localTerrainStyle switch
                {
                    RiverTerrainStyle.Mountain => Math.Max(
                        ownerSurfaceHeight,
                        Math.Min(
                            originalHeight,
                            ownerSurfaceHeight
                            + WorldGrid.HeightStepsPerCell)),
                    RiverTerrainStyle.Stepped => Math.Max(
                        ownerSurfaceHeight,
                        Math.Min(
                            originalHeight,
                            ownerSurfaceHeight + 2)),
                    _ => ownerSurfaceHeight
                };
                var blend = blendDistance <= 1
                    ? 1f
                    : (distance - 1f) / (blendDistance - 1f);
                var targetHeight = distance == 1
                    ? innerHeight
                    : (int)MathF.Round(
                        innerHeight
                        + (originalHeight - innerHeight) * blend);
                targetHeight = Math.Clamp(
                    targetHeight,
                    WorldGrid.HeightStepsPerCell,
                    maximumWorldHeight);
                if (targetHeight != originalHeight)
                {
                    terrainTargets[columnIndex] = targetHeight;
                }
            }

            return true;

            RiverTerrainStyle ResolveLocalTerrainStyle(int columnIndex)
            {
                var centerX = columnIndex % world.Size;
                var centerZ = columnIndex / world.Size;
                var minimumHeight = int.MaxValue;
                var maximumHeight = int.MinValue;
                for (var offsetZ = -2; offsetZ <= 2; offsetZ++)
                for (var offsetX = -2; offsetX <= 2; offsetX++)
                {
                    var x = centerX + offsetX;
                    var z = centerZ + offsetZ;
                    if (!world.ContainsColumn(x, z))
                    {
                        continue;
                    }

                    var height = solidHeights[x + world.Size * z];
                    minimumHeight = Math.Min(minimumHeight, height);
                    maximumHeight = Math.Max(maximumHeight, height);
                }

                var relief = maximumHeight - minimumHeight;
                if (relief >= WorldGrid.HeightStepsPerCell * 3)
                {
                    return RiverTerrainStyle.Mountain;
                }

                if (relief <= WorldGrid.HeightStepsPerCell)
                {
                    return RiverTerrainStyle.Lowland;
                }

                return terrainStyle;
            }
        }

        private static bool TryAddSourceContainmentBanks(
            WorldData world,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            HashSet<int> channelColumns,
            IReadOnlyDictionary<int, ChannelCellProfile> channelCells,
            IReadOnlyDictionary<int, int> transitionTops,
            Dictionary<int, int> terrainTargets)
        {
            var maximumWorldHeight = checked(
                world.Height * WorldGrid.HeightStepsPerCell);
            foreach (var pair in channelCells)
            {
                var columnIndex = pair.Key;
                var profile = pair.Value;
                if (transitionTops != null
                    && transitionTops.TryGetValue(
                        columnIndex,
                        out var transitionTop)
                    && transitionTop > profile.SurfaceHeightUnits)
                {
                    profile = new ChannelCellProfile(
                        profile.BedHeightUnits,
                        transitionTop,
                        transitionTop,
                        profile.PathIndex,
                        profile.IsCenterline);
                }
                var x = columnIndex % world.Size;
                var z = columnIndex / world.Size;
                for (var directionIndex = 0;
                     directionIndex < Directions.Length;
                     directionIndex++)
                {
                    var nextX = x + Directions[directionIndex].x;
                    var nextZ = z + Directions[directionIndex].z;
                    if (!world.ContainsColumn(nextX, nextZ))
                    {
                        continue;
                    }

                    var nextIndex = nextX + world.Size * nextZ;
                    if (channelColumns.Contains(nextIndex)
                        || seaWaterSurfaces[nextIndex] > 0
                        || basinByWetColumn[nextIndex] >= 0)
                    {
                        continue;
                    }

                    var targetHeight = terrainTargets.TryGetValue(
                        nextIndex,
                        out var plannedHeight)
                        ? plannedHeight
                        : solidHeights[nextIndex];
                    if (!CanSourceReachTerrainColumn(
                            world,
                            columnIndex,
                            profile,
                            nextIndex,
                            targetHeight))
                    {
                        continue;
                    }

                    targetHeight = Math.Max(
                        targetHeight,
                        profile.SurfaceHeightUnits);
                    if (targetHeight > maximumWorldHeight)
                    {
                        return false;
                    }

                    terrainTargets[nextIndex] = targetHeight;
                }
            }

            return true;
        }

        private static bool CanSourceReachTerrainColumn(
            WorldData world,
            int sourceColumnIndex,
            ChannelCellProfile sourceProfile,
            int targetColumnIndex,
            int targetHeightUnits)
        {
            if (sourceProfile.SurfaceHeightUnits
                    <= sourceProfile.BedHeightUnits
                || sourceProfile.SurfaceHeightUnits
                    % WorldGrid.HeightStepsPerCell != 0)
            {
                return false;
            }

            var y = sourceProfile.SurfaceHeightUnits
                / WorldGrid.HeightStepsPerCell - 1;
            if ((uint)y >= world.Height)
            {
                return false;
            }

            var baseHeight = y * WorldGrid.HeightStepsPerCell;
            var sourceCell = new CellData
            {
                Terrain = new TerrainData
                {
                    SolidHeight = checked((byte)Math.Clamp(
                        sourceProfile.BedHeightUnits - baseHeight,
                        0,
                        WorldGrid.HeightStepsPerCell))
                }
            };
            var sourceWater = new WaterData
            {
                Amount = WaterAmount.Full,
                Role = WaterRole.Source,
                Type = WaterType.Pond
            };
            var targetCell = new CellData
            {
                Terrain = new TerrainData
                {
                    SolidHeight = checked((byte)Math.Clamp(
                        targetHeightUnits - baseHeight,
                        0,
                        WorldGrid.HeightStepsPerCell))
                }
            };
            return WaterFlowReachability.CanReachHorizontally(
                new CellCoordinate(
                    sourceColumnIndex % world.Size,
                    y,
                    sourceColumnIndex / world.Size),
                sourceCell,
                sourceWater,
                new CellCoordinate(
                    targetColumnIndex % world.Size,
                    y,
                    targetColumnIndex / world.Size),
                targetCell,
                WaterAmount.Full);
        }

        private static void ConstrainSectionWidths(
            int[] widths,
            ChannelTrace trace,
            HydrologyMap hydrology,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn)
        {
            for (var pathIndex = 0;
                 pathIndex < widths.Length;
                 pathIndex++)
            {
                while (widths[pathIndex] > 1
                    && !CanUseWidth(pathIndex, widths[pathIndex]))
                {
                    widths[pathIndex] -= 2;
                }
            }

            for (var pathIndex = 1;
                 pathIndex < widths.Length;
                 pathIndex++)
            {
                widths[pathIndex] = Math.Min(
                    widths[pathIndex],
                    widths[pathIndex - 1] + 2);
            }

            for (var pathIndex = widths.Length - 2;
                 pathIndex >= 0;
                 pathIndex--)
            {
                widths[pathIndex] = Math.Min(
                    widths[pathIndex],
                    widths[pathIndex + 1] + 2);
            }

            bool CanUseWidth(int pathIndex, int width)
            {
                ResolvePerpendicular(
                    trace.Path,
                    hydrology.Size,
                    pathIndex,
                    out var perpendicularX,
                    out var perpendicularZ);
                var centerIndex = trace.Path[pathIndex];
                var centerX = centerIndex % hydrology.Size;
                var centerZ = centerIndex / hydrology.Size;
                var radius = width / 2;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    var x = centerX + perpendicularX * offset;
                    var z = centerZ + perpendicularZ * offset;
                    if (!hydrology.Contains(x, z))
                    {
                        return false;
                    }

                    var columnIndex = hydrology.ToIndex(x, z);
                    if (seaWaterSurfaces[columnIndex] > 0
                        || basinByWetColumn[columnIndex] >= 0)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private static int[] BuildSectionDepths(
            IReadOnlyList<int> widths,
            WorldBuildInput settings,
            RiverTerrainStyle terrainStyle,
            int worldSeed,
            int worldSize,
            ChannelTrace trace)
        {
            var result = new int[widths.Count];
            var shapeSeed = DeterministicNoise.DeriveSeed(
                worldSeed,
                "river-cross-section");
            for (var index = 0; index < widths.Count; index++)
            {
                var desiredDepth = widths[index] switch
                {
                    3 => 2,
                    >= 5 => ResolveWideCenterDepth(index),
                    _ => terrainStyle == RiverTerrainStyle.Mountain
                        ? settings.RiverDepthCells + 1
                        : settings.RiverDepthCells
                };
                result[index] = Math.Clamp(
                    desiredDepth,
                    1,
                    settings.MaximumRiverDepthCells);
            }

            return result;

            int ResolveWideCenterDepth(int pathIndex)
            {
                if (terrainStyle == RiverTerrainStyle.Lowland)
                {
                    return 2;
                }

                if (terrainStyle == RiverTerrainStyle.Mountain)
                {
                    return 3;
                }

                var columnIndex = trace.Path[pathIndex];
                var x = columnIndex % worldSize;
                var z = columnIndex / worldSize;
                return DeterministicNoise.Value01(x, z, shapeSeed) < 0.5f
                    ? 2
                    : 3;
            }
        }

        private static int ResolveCrossSectionDepth(
            int width,
            int centerDepth,
            int lateralOffset)
        {
            if (lateralOffset <= 0)
            {
                return centerDepth;
            }

            if (width <= 3 || lateralOffset >= width / 2)
            {
                return 1;
            }

            return Math.Min(2, centerDepth);
        }

        private static RiverGenerationProfile ResolveGenerationProfile(
            HydrologyMap hydrology,
            int startIndex,
            IReadOnlyList<int> solidHeights,
            int profileSlot,
            int worldSeed)
        {
            var receiver = hydrology.GetReceiverColumnIndex(startIndex);
            var localRelief = receiver >= 0
                ? Math.Abs(
                    solidHeights[startIndex]
                    - solidHeights[receiver])
                : 0;
            RiverTerrainStyle terrainStyle;
            switch (Math.Abs(profileSlot) % 3)
            {
                case 1:
                    terrainStyle = RiverTerrainStyle.Lowland;
                    break;
                case 2:
                    terrainStyle = RiverTerrainStyle.Stepped;
                    break;
                default:
                    terrainStyle = localRelief >= 3
                        ? RiverTerrainStyle.Mountain
                        : localRelief <= 1
                            ? RiverTerrainStyle.Lowland
                            : RiverTerrainStyle.Stepped;
                    break;
            }

            var x = startIndex % hydrology.Size;
            var z = startIndex / hydrology.Size;
            var waterModeSeed = DeterministicNoise.DeriveSeed(
                worldSeed,
                "river-water-mode");
            var waterMode = DeterministicNoise.Value01(
                    x,
                    z,
                    waterModeSeed + profileSlot) < 0.5f
                ? RiverWaterMode.Dynamic
                : RiverWaterMode.Source;
            return new RiverGenerationProfile(waterMode, terrainStyle);
        }

        private static IReadOnlyList<RiverGenerationProfile>
            BuildGenerationProfileOrder(RiverGenerationProfile preferred)
        {
            var result = new List<RiverGenerationProfile>(3)
            {
                preferred
            };
            AddIfMissing(preferred.WaterMode, RiverTerrainStyle.Lowland);
            AddIfMissing(preferred.WaterMode, RiverTerrainStyle.Mountain);
            AddIfMissing(preferred.WaterMode, RiverTerrainStyle.Stepped);
            return result;

            void AddIfMissing(
                RiverWaterMode waterMode,
                RiverTerrainStyle terrainStyle)
            {
                for (var index = 0; index < result.Count; index++)
                {
                    if (result[index].WaterMode == waterMode
                        && result[index].TerrainStyle == terrainStyle)
                    {
                        return;
                    }
                }

                result.Add(new RiverGenerationProfile(
                    waterMode,
                    terrainStyle));
            }
        }

        private static int ResolveMaximumWidth(int configuredMaximumWidth)
        {
            configuredMaximumWidth = Math.Max(1, configuredMaximumWidth);
            if ((configuredMaximumWidth & 1) == 0)
            {
                configuredMaximumWidth--;
            }

            return configuredMaximumWidth;
        }

        private static void ResolvePerpendicular(
            IReadOnlyList<int> path,
            int size,
            int pathIndex,
            out int perpendicularX,
            out int perpendicularZ)
        {
            int fromIndex;
            int toIndex;
            if (pathIndex + 1 < path.Count)
            {
                fromIndex = path[pathIndex];
                toIndex = path[pathIndex + 1];
            }
            else
            {
                fromIndex = path[pathIndex - 1];
                toIndex = path[pathIndex];
            }

            var deltaX = toIndex % size - fromIndex % size;
            if (deltaX != 0)
            {
                perpendicularX = 0;
                perpendicularZ = 1;
            }
            else
            {
                perpendicularX = 1;
                perpendicularZ = 0;
            }
        }

        private static Dictionary<int, int> BuildSourceTransitionTops(
            ChannelTrace trace,
            IReadOnlyList<int> widths,
            IReadOnlyList<int> surfaceLevels,
            HydrologyMap hydrology,
            HashSet<int> channelColumns)
        {
            var result = new Dictionary<int, int>();
            for (var pathIndex = 1;
                 pathIndex < trace.Path.Count;
                 pathIndex++)
            {
                var previousSurface = surfaceLevels[pathIndex - 1];
                var currentSurface = surfaceLevels[pathIndex];
                if (previousSurface == currentSurface)
                {
                    continue;
                }

                var transitionIndex = currentSurface < previousSurface
                    ? pathIndex
                    : pathIndex - 1;
                var maximumWaterTop = Math.Max(
                    previousSurface,
                    currentSurface);
                ResolvePerpendicular(
                    trace.Path,
                    hydrology.Size,
                    transitionIndex,
                    out var perpendicularX,
                    out var perpendicularZ);
                var centerIndex = trace.Path[transitionIndex];
                var centerX = centerIndex % hydrology.Size;
                var centerZ = centerIndex / hydrology.Size;
                var radius = widths[transitionIndex] / 2;
                for (var offset = -radius; offset <= radius; offset++)
                {
                    var x = centerX + perpendicularX * offset;
                    var z = centerZ + perpendicularZ * offset;
                    if (!hydrology.Contains(x, z))
                    {
                        continue;
                    }

                    var columnIndex = hydrology.ToIndex(x, z);
                    if (!channelColumns.Contains(columnIndex))
                    {
                        continue;
                    }

                    if (!result.TryGetValue(
                            columnIndex,
                            out var existingTop)
                        || maximumWaterTop > existingTop)
                    {
                        result[columnIndex] = maximumWaterTop;
                    }
                }
            }

            return result;
        }

        private static void AddChannelWetCells(
            ChannelPlan plan,
            int size,
            int columnIndex,
            int bedHeight,
            int maximumWaterTop)
        {
            var x = columnIndex % size;
            var z = columnIndex / size;
            var firstY = Math.Max(
                0,
                bedHeight / WorldGrid.HeightStepsPerCell);
            var lastY = Math.Min(
                plan.WorldHeight - 1,
                (maximumWaterTop - 1)
                    / WorldGrid.HeightStepsPerCell);
            for (var y = firstY; y <= lastY; y++)
            {
                plan.AddAllowedWetCell(new CellCoordinate(x, y, z));
            }

            plan.AddRequiredWetCell(new CellCoordinate(x, firstY, z));
        }

        private static void AddHeadwaterSources(
            ChannelPlan plan,
            IReadOnlyList<int> path,
            IReadOnlyList<int> bedHeights,
            IReadOnlyList<int> levels)
        {
            var columnIndex = path[0];
            var x = columnIndex % plan.WorldSize;
            var z = columnIndex / plan.WorldSize;
            var firstY = Math.Max(
                0,
                bedHeights[0] / WorldGrid.HeightStepsPerCell);
            var lastY = Math.Min(
                plan.WorldHeight - 1,
                (levels[0] - 1) / WorldGrid.HeightStepsPerCell);
            for (var y = firstY; y <= lastY; y++)
            {
                plan.AddSourceCell(new PlannedWaterCell(
                    new CellCoordinate(x, y, z),
                    FlowDirection.None,
                    WaterType.River));
            }
        }

        private static bool AddPersistentChannelSources(
            ChannelPlan plan,
            IReadOnlyDictionary<int, ChannelCellProfile> channelCells,
            IReadOnlyDictionary<int, int> transitionTops)
        {
            foreach (var pair in channelCells)
            {
                if (transitionTops != null
                    && transitionTops.ContainsKey(pair.Key))
                {
                    continue;
                }

                var profile = pair.Value;
                if (profile.SurfaceHeightUnits <= 0
                    || profile.SurfaceHeightUnits
                        % WorldGrid.HeightStepsPerCell != 0)
                {
                    return false;
                }

                var firstY = Math.Max(
                    0,
                    profile.BedHeightUnits
                    / WorldGrid.HeightStepsPerCell);
                var lastY = profile.SurfaceHeightUnits
                    / WorldGrid.HeightStepsPerCell - 1;
                if ((uint)lastY >= plan.WorldHeight)
                {
                    return false;
                }

                var x = pair.Key % plan.WorldSize;
                var z = pair.Key / plan.WorldSize;
                for (var y = firstY; y <= lastY; y++)
                {
                    plan.AddSourceCell(new PlannedWaterCell(
                        new CellCoordinate(x, y, z),
                        FlowDirection.None,
                        WaterType.River));
                }
            }

            return true;
        }

        private static void TryAcceptChannel(
            WorldData world,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            ChannelPlan channel,
            List<ChannelPlan> acceptedChannels,
            HashSet<int> usedColumns,
            WaterPlanValidationContext validationContext)
        {
            for (var repairPass = 0;
                 repairPass <= MaximumBankRepairPasses;
                 repairPass++)
            {
                var candidateChannels = new List<ChannelPlan>(
                    acceptedChannels)
                {
                    channel
                };
                if (!HydrologyFeaturePlan.TryCreate(
                        world.Size,
                        world.Height,
                        basins,
                        candidateChannels,
                        out var candidatePlan))
                {
                    return;
                }

                var validation = WaterPlanValidator.Validate(
                    world,
                    candidatePlan,
                    validationContext);
                if (validation.IsValid)
                {
                    acceptedChannels.Add(channel);
                    foreach (var columnIndex in channel.ChannelColumnIndices)
                    {
                        usedColumns.Add(columnIndex);
                    }

                    return;
                }

                if (repairPass == MaximumBankRepairPasses
                    || !TryReinforceLeakingBanks(
                        world,
                        solidHeights,
                        basins,
                        channel,
                        validation.LeakedCellIndices))
                {
                    return;
                }
            }
        }

        private static bool TryReinforceLeakingBanks(
            WorldData world,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<BasinPlan> basins,
            ChannelPlan channel,
            IReadOnlyList<int> leakedCellIndices)
        {
            if (leakedCellIndices == null
                || leakedCellIndices.Count == 0)
            {
                return false;
            }

            var basinWetColumns = new HashSet<int>();
            var channelColumns = new HashSet<int>(
                channel.ChannelColumnIndices);
            for (var basinIndex = 0;
                 basinIndex < basins.Count;
                 basinIndex++)
            {
                var wetColumns = basins[basinIndex].WetColumnIndices;
                for (var wetIndex = 0;
                     wetIndex < wetColumns.Count;
                     wetIndex++)
                {
                    basinWetColumns.Add(wetColumns[wetIndex]);
                }
            }

            var requiredBankHeights = new Dictionary<int, int>();
            var maximumWorldHeight = checked(
                world.Height * WorldGrid.HeightStepsPerCell);
            for (var leakIndex = 0;
                 leakIndex < leakedCellIndices.Count;
                 leakIndex++)
            {
                var coordinate = WorldIndex.DecodeCell(
                    world,
                    leakedCellIndices[leakIndex]);
                var columnIndex = coordinate.X
                    + world.Size * coordinate.Z;
                if (channelColumns.Contains(columnIndex)
                    || basinWetColumns.Contains(columnIndex)
                    || !IsAdjacentToChannel(
                        world.Size,
                        columnIndex,
                        channelColumns))
                {
                    continue;
                }

                var requiredHeight = Math.Min(
                    maximumWorldHeight,
                    (coordinate.Y + 1)
                    * WorldGrid.HeightStepsPerCell);
                if (!requiredBankHeights.TryGetValue(
                        columnIndex,
                        out var existingHeight)
                    || requiredHeight > existingHeight)
                {
                    requiredBankHeights[columnIndex] = requiredHeight;
                }
            }

            var changed = false;
            foreach (var pair in requiredBankHeights)
            {
                var targetHeight = Math.Max(
                    solidHeights[pair.Key],
                    pair.Value);
                if (channel.TerrainColumns.TryGetValue(
                        pair.Key,
                        out var existingColumn))
                {
                    targetHeight = Math.Max(
                        targetHeight,
                        existingColumn.TargetHeightUnits);
                    if (targetHeight
                        == existingColumn.TargetHeightUnits)
                    {
                        continue;
                    }
                }

                var x = pair.Key % world.Size;
                var z = pair.Key / world.Size;
                channel.SetTerrainColumn(new PlannedTerrainColumn(
                    x,
                    z,
                    targetHeight));
                changed = true;
            }

            return changed;
        }

        private static bool IsAdjacentToChannel(
            int size,
            int columnIndex,
            HashSet<int> channelColumns)
        {
            var x = columnIndex % size;
            var z = columnIndex / size;
            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                var nextX = x + Directions[directionIndex].x;
                var nextZ = z + Directions[directionIndex].z;
                if ((uint)nextX >= size || (uint)nextZ >= size)
                {
                    continue;
                }

                if (channelColumns.Contains(nextX + size * nextZ))
                {
                    return true;
                }
            }

            return false;
        }

        private static int[] BuildBasinLookup(
            int columnCount,
            IReadOnlyList<BasinPlan> basins)
        {
            var result = new int[columnCount];
            Array.Fill(result, -1);
            for (var basinIndex = 0; basinIndex < basins.Count; basinIndex++)
            {
                var basin = basins[basinIndex];
                for (var wetIndex = 0;
                     wetIndex < basin.WetColumnIndices.Count;
                     wetIndex++)
                {
                    result[basin.WetColumnIndices[wetIndex]] = basin.BasinId;
                }
            }

            return result;
        }

        private static bool TryFindAdjacentWaterBody(
            int size,
            int columnIndex,
            int ignoredBasinId,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            IReadOnlyList<BasinPlan> basins,
            out ChannelTerminal target)
        {
            var x = columnIndex % size;
            var z = columnIndex / size;
            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                var nextX = x + Directions[directionIndex].x;
                var nextZ = z + Directions[directionIndex].z;
                if ((uint)nextX >= size || (uint)nextZ >= size)
                {
                    continue;
                }

                var nextIndex = nextX + size * nextZ;
                var basinId = basinByWetColumn[nextIndex];
                if (basinId >= 0 && basinId != ignoredBasinId)
                {
                    var basin = FindBasin(basins, basinId);
                    target = new ChannelTerminal(
                        basinId,
                        nextIndex,
                        basin.WaterSurfaceHeightUnits);
                    return true;
                }

                if (seaWaterSurfaces[nextIndex] > 0)
                {
                    target = new ChannelTerminal(
                        -1,
                        nextIndex,
                        seaWaterSurfaces[nextIndex]);
                    return true;
                }
            }

            target = default;
            return false;
        }

        private static bool TryFindAdjacentBasinWetColumn(
            int size,
            int columnIndex,
            int basinId,
            int[] basinByWetColumn,
            out int wetColumnIndex)
        {
            var x = columnIndex % size;
            var z = columnIndex / size;
            for (var directionIndex = 0;
                 directionIndex < Directions.Length;
                 directionIndex++)
            {
                var nextX = x + Directions[directionIndex].x;
                var nextZ = z + Directions[directionIndex].z;
                if ((uint)nextX >= size || (uint)nextZ >= size)
                {
                    continue;
                }

                var nextIndex = nextX + size * nextZ;
                if (basinByWetColumn[nextIndex] == basinId)
                {
                    wetColumnIndex = nextIndex;
                    return true;
                }
            }

            wetColumnIndex = -1;
            return false;
        }

        private static BasinPlan FindBasin(
            IReadOnlyList<BasinPlan> basins,
            int basinId)
        {
            for (var index = 0; index < basins.Count; index++)
            {
                if (basins[index].BasinId == basinId)
                {
                    return basins[index];
                }
            }

            throw new InvalidOperationException(
                $"Basin {basinId} was not found.");
        }

        private static int AlignToCellCeiling(int heightUnits)
        {
            var step = WorldGrid.HeightStepsPerCell;
            return ((heightUnits + step - 1) / step) * step;
        }

        private static int AlignToCellFloor(int heightUnits)
        {
            var step = WorldGrid.HeightStepsPerCell;
            return Math.Max(0, heightUnits) / step * step;
        }

        private static int CompareHeadwaters(
            HeadwaterCandidate left,
            HeadwaterCandidate right)
        {
            var orderComparison = left.Order.CompareTo(right.Order);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            var fitnessComparison = right.Fitness.CompareTo(left.Fitness);
            return fitnessComparison != 0
                ? fitnessComparison
                : left.ColumnIndex.CompareTo(right.ColumnIndex);
        }

        private static void ValidateArguments(
            WorldData world,
            WorldBuildInput settings,
            HydrologyMap hydrology,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces)
        {
            if (world == null || settings == null || hydrology == null
                || basins == null || solidHeights == null
                || seaWaterSurfaces == null)
            {
                throw new ArgumentNullException(
                    "Dynamic river planning inputs cannot be null.");
            }

            if (hydrology.Size != world.Size
                || solidHeights.Count != hydrology.ColumnCount
                || seaWaterSurfaces.Count != hydrology.ColumnCount)
            {
                throw new ArgumentException(
                    "Dynamic river planning dimensions do not match.");
            }
        }

        private readonly struct ChannelTrace
        {
            public readonly IReadOnlyList<int> Path;
            public readonly ChannelTerminal StartTarget;
            public readonly ChannelTerminal Target;

            public ChannelTrace(
                IReadOnlyList<int> path,
                ChannelTerminal target) : this(
                    path,
                    ChannelTerminal.Independent,
                    target)
            {
            }

            public ChannelTrace(
                IReadOnlyList<int> path,
                ChannelTerminal startTarget,
                ChannelTerminal target)
            {
                Path = path;
                StartTarget = startTarget;
                Target = target;
            }
        }

        private readonly struct RiverGenerationProfile
        {
            public readonly RiverWaterMode WaterMode;
            public readonly RiverTerrainStyle TerrainStyle;

            public RiverGenerationProfile(
                RiverWaterMode waterMode,
                RiverTerrainStyle terrainStyle)
            {
                WaterMode = waterMode;
                TerrainStyle = terrainStyle;
            }
        }

        private enum ChannelTerminalType : byte
        {
            Independent = 0,
            LakeInlet = 1,
            SeaOutlet = 2
        }

        private readonly struct ChannelTerminal
        {
            public readonly ChannelTerminalType Type;
            public readonly int BasinId;
            public readonly int WetColumnIndex;
            public readonly int SurfaceHeightUnits;
            public bool HasWaterBody =>
                Type != ChannelTerminalType.Independent;

            public static ChannelTerminal Independent => new(
                ChannelTerminalType.Independent,
                -1,
                -1,
                0);

            public ChannelTerminal(
                int basinId,
                int wetColumnIndex,
                int surfaceHeightUnits)
                : this(
                    basinId >= 0
                        ? ChannelTerminalType.LakeInlet
                        : ChannelTerminalType.SeaOutlet,
                    basinId,
                    wetColumnIndex,
                    surfaceHeightUnits)
            {
            }

            private ChannelTerminal(
                ChannelTerminalType type,
                int basinId,
                int wetColumnIndex,
                int surfaceHeightUnits)
            {
                Type = type;
                BasinId = basinId;
                WetColumnIndex = wetColumnIndex;
                SurfaceHeightUnits = surfaceHeightUnits;
            }
        }

        private readonly struct HeadwaterCandidate
        {
            public readonly int ColumnIndex;
            public readonly float Fitness;
            public readonly float Order;

            public HeadwaterCandidate(
                int columnIndex,
                float fitness,
                float order)
            {
                ColumnIndex = columnIndex;
                Fitness = fitness;
                Order = order;
            }
        }

        private readonly struct ChannelCellProfile
        {
            public readonly int BedHeightUnits;
            public readonly int SurfaceHeightUnits;
            public readonly int MaximumWaterTopUnits;
            public readonly int PathIndex;
            public readonly bool IsCenterline;

            public ChannelCellProfile(
                int bedHeightUnits,
                int surfaceHeightUnits,
                int maximumWaterTopUnits,
                int pathIndex,
                bool isCenterline)
            {
                BedHeightUnits = bedHeightUnits;
                SurfaceHeightUnits = surfaceHeightUnits;
                MaximumWaterTopUnits = maximumWaterTopUnits;
                PathIndex = pathIndex;
                IsCenterline = isCenterline;
            }
        }
    }
}
