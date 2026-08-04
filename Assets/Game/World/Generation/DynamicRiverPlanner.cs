using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    internal static class DynamicRiverPlanner
    {
        private const int MaximumChannelCutUnits =
            WorldGrid.HeightStepsPerCell;
        private const int MaximumBankRaiseUnits = 2;

        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        public static HydrologyFeaturePlan BuildFeaturePlan(
            WorldData validationWorld,
            WorldGenerationSettings settings,
            HydrologyMap hydrology,
            IReadOnlyList<BasinPlan> basinPlans,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int worldSeed)
        {
            ValidateArguments(
                validationWorld,
                settings,
                hydrology,
                basinPlans,
                solidHeights,
                seaWaterSurfaces);

            var acceptedBasins = BuildCompatibleBasins(
                validationWorld,
                basinPlans);
            var acceptedChannels = new List<ChannelPlan>();
            var usedChannelColumns = new HashSet<int>();
            var basinByWetColumn = BuildBasinLookup(
                hydrology.ColumnCount,
                acceptedBasins);
            var maximumTraceLength = Math.Max(2, validationWorld.Size);
            if (settings.RiverCount > 0)
            {
                AddLakeOutlets(
                    validationWorld,
                    settings,
                    hydrology,
                    acceptedBasins,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn,
                    maximumTraceLength,
                    acceptedChannels,
                    usedChannelColumns);
                AddHeadwaterChannels(
                    validationWorld,
                    settings,
                    hydrology,
                    acceptedBasins,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn,
                    maximumTraceLength,
                    worldSeed,
                    acceptedChannels,
                    usedChannelColumns);
            }

            if (!HydrologyFeaturePlan.TryCreate(
                    validationWorld.Size,
                    validationWorld.Height,
                    acceptedBasins,
                    acceptedChannels,
                    out var result))
            {
                throw new InvalidOperationException(
                    "Accepted hydrology plans could not be merged.");
            }

            return result;
        }

        public static void ApplyFeaturePlan(
            HydrologyFeaturePlan featurePlan,
            int[] solidHeights,
            int[] waterSurfaces,
            WaterCellRole[] waterRoles,
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
                waterBedSurfaces);

            for (var channelIndex = 0;
                 channelIndex < featurePlan.Channels.Count;
                 channelIndex++)
            {
                var channel = featurePlan.Channels[channelIndex];
                foreach (var columnIndex in channel.ChannelColumnIndices)
                {
                    waterBedSurfaces[columnIndex] = SurfaceType.Riverbed;
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
                    waterRoles[columnIndex] = WaterCellRole.Source;
                }
            }
        }

        private static List<BasinPlan> BuildCompatibleBasins(
            WorldData world,
            IReadOnlyList<BasinPlan> basinPlans)
        {
            var accepted = new List<BasinPlan>(basinPlans.Count);
            for (var index = 0; index < basinPlans.Count; index++)
            {
                var candidate = new List<BasinPlan>(accepted)
                {
                    basinPlans[index]
                };
                if (!HydrologyFeaturePlan.TryCreate(
                        world.Size,
                        world.Height,
                        candidate,
                        Array.Empty<ChannelPlan>(),
                        out var merged))
                {
                    continue;
                }

                if (WaterPlanValidator.Validate(world, merged).IsValid)
                {
                    accepted.Add(basinPlans[index]);
                }
            }

            return accepted;
        }

        private static void AddLakeOutlets(
            WorldData world,
            WorldGenerationSettings settings,
            HydrologyMap hydrology,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            int maximumLength,
            List<ChannelPlan> acceptedChannels,
            HashSet<int> usedColumns)
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
                        maximumLength,
                        acceptedChannels.Count,
                        usedColumns,
                        out var channel))
                {
                    continue;
                }

                TryAcceptChannel(
                    world,
                    basins,
                    channel,
                    acceptedChannels,
                    usedColumns);
            }
        }

        private static void AddHeadwaterChannels(
            WorldData world,
            WorldGenerationSettings settings,
            HydrologyMap hydrology,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            int maximumLength,
            int worldSeed,
            List<ChannelPlan> acceptedChannels,
            HashSet<int> usedColumns)
        {
            var seed = DeterministicNoise.DeriveSeed(
                worldSeed,
                "dynamic-river-headwaters");
            var candidates = new List<HeadwaterCandidate>();
            for (var index = 0; index < hydrology.ColumnCount; index++)
            {
                if (basinByWetColumn[index] >= 0
                    || seaWaterSurfaces[index] > 0
                    || solidHeights[index] < settings.SeaLevelUnits
                        + WorldGrid.HeightStepsPerCell)
                {
                    continue;
                }

                var x = index % world.Size;
                var z = index / world.Size;
                if (x < 2 || z < 2
                    || x >= world.Size - 2 || z >= world.Size - 2)
                {
                    continue;
                }

                if (IsAdjacentToPlannedWater(
                        world.Size,
                        index,
                        seaWaterSurfaces,
                        basinByWetColumn))
                {
                    continue;
                }

                var hasFeasibleBanks = HasFeasibleHeadwaterBanks(
                    world.Size,
                    hydrology,
                    index,
                    solidHeights,
                    seaWaterSurfaces,
                    basinByWetColumn);

                var score = (float)Math.Log(
                        hydrology.GetFlowAccumulation(index) + 1d,
                        2d) * 20f
                    + solidHeights[index] * 0.1f
                    + (hasFeasibleBanks ? 100f : 0f)
                    + DeterministicNoise.Value01(x, z, seed);
                candidates.Add(new HeadwaterCandidate(index, score));
            }

            candidates.Sort(CompareHeadwaters);
            var candidateLimit = Math.Min(
                candidates.Count,
                Math.Max(24, settings.RiverCount * 12));
            for (var candidateIndex = 0;
                 candidateIndex < candidateLimit
                 && acceptedChannels.Count < settings.RiverCount;
                 candidateIndex++)
            {
                var start = candidates[candidateIndex].ColumnIndex;
                if (usedColumns.Contains(start)
                    || !TryTraceChannel(
                        hydrology,
                        start,
                        -1,
                        maximumLength,
                        seaWaterSurfaces,
                        basinByWetColumn,
                        basins,
                        usedColumns,
                        out var trace))
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
                        acceptedChannels.Count,
                        out var channel))
                {
                    continue;
                }

                TryAcceptChannel(
                    world,
                    basins,
                    channel,
                    acceptedChannels,
                    usedColumns);
            }
        }

        private static bool HasFeasibleHeadwaterBanks(
            int size,
            HydrologyMap hydrology,
            int startIndex,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn)
        {
            var sourceSurface = AlignToCellCeiling(
                solidHeights[startIndex]);
            var receiver = hydrology.GetReceiverColumnIndex(startIndex);
            var x = startIndex % size;
            var z = startIndex / size;
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
                if (nextIndex == receiver)
                {
                    continue;
                }

                if (seaWaterSurfaces[nextIndex] > 0
                    || basinByWetColumn[nextIndex] >= 0
                    || sourceSurface - solidHeights[nextIndex]
                        > MaximumBankRaiseUnits)
                {
                    return false;
                }
            }

            return true;
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
            WorldGenerationSettings settings,
            HydrologyMap hydrology,
            BasinPlan sourceBasin,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            int maximumLength,
            int archetypeSlot,
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

            if (!TryTraceChannel(
                    hydrology,
                    shore,
                    sourceBasin.BasinId,
                    maximumLength,
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

            var external = trace.Path.Count > 1
                ? trace.Path[1]
                : shore;
            var outlet = new BasinConnectionPort(
                sourceBasin.BasinId,
                BasinConnectionType.Outlet,
                sourceWetColumn,
                shore,
                external,
                sourceBasin.WaterSurfaceHeightUnits,
                ToDirection(world.Size, sourceWetColumn, shore));
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
                archetypeSlot,
                out channel);
        }

        private static bool TryTraceChannel(
            HydrologyMap hydrology,
            int start,
            int ignoredBasinId,
            int maximumLength,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            IReadOnlyList<BasinPlan> basins,
            HashSet<int> usedColumns,
            out ChannelTrace trace)
        {
            var path = new List<int>();
            var visited = new HashSet<int>();
            var current = start;
            for (var step = 0; step < maximumLength; step++)
            {
                if (current < 0
                    || !visited.Add(current)
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

                current = hydrology.GetReceiverColumnIndex(current);
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

        private static bool TryBuildChannel(
            WorldData world,
            WorldGenerationSettings settings,
            HydrologyMap hydrology,
            ChannelTrace trace,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            bool hasHeadwaterSource,
            BasinConnectionPort? outlet,
            int archetypeSlot,
            out ChannelPlan channel)
        {
            var preferredArchetype = ResolveArchetype(
                world,
                trace,
                solidHeights,
                archetypeSlot);
            var archetypeOrder = BuildArchetypeOrder(preferredArchetype);
            for (var archetypeIndex = 0;
                 archetypeIndex < archetypeOrder.Count;
                 archetypeIndex++)
            {
                var archetype = archetypeOrder[archetypeIndex];
                if (!TryLimitTraceForArchetype(
                        world,
                        trace,
                        archetype,
                        out var archetypeTrace))
                {
                    continue;
                }

                var maximumWidth = ResolveArchetypeMaximumWidth(
                    archetype,
                    settings.MaximumRiverWidthCells);
                for (var widthLimit = maximumWidth;
                     widthLimit >= 1;
                     widthLimit -= 2)
                {
                    if (TryBuildChannelWithWidthLimit(
                            world,
                            settings,
                            hydrology,
                            archetypeTrace,
                            basins,
                            solidHeights,
                            seaWaterSurfaces,
                            basinByWetColumn,
                            hasHeadwaterSource,
                            outlet,
                            archetype,
                            widthLimit,
                            out channel))
                    {
                        return true;
                    }
                }
            }

            channel = null;
            return false;
        }

        private static bool TryLimitTraceForArchetype(
            WorldData world,
            ChannelTrace source,
            RiverChannelArchetype archetype,
            out ChannelTrace result)
        {
            var maximumColumns = archetype
                == RiverChannelArchetype.SourceChannel
                    ? world.Size
                    : WaterFlowReachability.GetSafeHorizontalSpreadCount(
                        world.WaterFlowRules);
            if (maximumColumns < 2)
            {
                result = default;
                return false;
            }

            if (source.Path.Count <= maximumColumns)
            {
                result = source;
                return true;
            }

            var shortenedPath = new int[maximumColumns];
            for (var index = 0; index < maximumColumns; index++)
            {
                shortenedPath[index] = source.Path[index];
            }

            result = new ChannelTrace(
                shortenedPath,
                ChannelTerminal.Independent);
            return true;
        }

        private static bool TryBuildChannelWithWidthLimit(
            WorldData world,
            WorldGenerationSettings settings,
            HydrologyMap hydrology,
            ChannelTrace trace,
            IReadOnlyList<BasinPlan> basins,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> seaWaterSurfaces,
            int[] basinByWetColumn,
            bool hasHeadwaterSource,
            BasinConnectionPort? outlet,
            RiverChannelArchetype archetype,
            int widthLimit,
            out ChannelPlan channel)
        {
            channel = null;
            var widths = BuildSectionWidths(
                trace,
                hydrology,
                widthLimit,
                archetype);
            var depths = BuildSectionDepths(
                widths,
                settings,
                archetype);

            var levels = BuildSurfaceProfile(
                trace,
                solidHeights,
                depths,
                outlet?.InterfaceSurfaceHeightUnits,
                archetype);
            if (levels == null)
            {
                return false;
            }

            var bedHeights = new int[trace.Path.Count];
            for (var pathIndex = 0;
                 pathIndex < trace.Path.Count;
                 pathIndex++)
            {
                var columnIndex = trace.Path[pathIndex];
                bedHeights[pathIndex] = hasHeadwaterSource
                        && pathIndex == 0
                    ? Math.Min(
                        solidHeights[columnIndex],
                        levels[pathIndex] - depths[pathIndex])
                    : levels[pathIndex] - depths[pathIndex];
            }

            var channelCells = new Dictionary<int, ChannelCellProfile>();
            for (var pathIndex = 0;
                 pathIndex < trace.Path.Count;
                 pathIndex++)
            {
                var centerIndex = trace.Path[pathIndex];
                if (!TryAddChannelCell(
                        centerIndex,
                        bedHeights[pathIndex],
                        levels[pathIndex],
                        pathIndex > 0
                            ? Math.Max(
                                levels[pathIndex],
                                levels[pathIndex - 1])
                            : levels[pathIndex],
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
                    var lateralBed = Math.Min(
                        bedHeights[pathIndex],
                        solidHeights[lateralIndex]);
                    return levels[pathIndex] - lateralBed
                            <= settings.MaximumRiverDepthSteps
                        && TryAddChannelCell(
                            lateralIndex,
                            lateralBed,
                            levels[pathIndex],
                            levels[pathIndex],
                            pathIndex,
                            false);
                }
            }

            var terrainTargets = new Dictionary<int, int>();
            var channelColumnSet = new HashSet<int>(channelCells.Keys);
            foreach (var pair in channelCells)
            {
                var columnIndex = pair.Key;
                var profile = pair.Value;
                var bedHeight = profile.BedHeightUnits;
                if (bedHeight < 0
                    || bedHeight > solidHeights[columnIndex]
                    || solidHeights[columnIndex] - bedHeight
                        > MaximumChannelCutUnits)
                {
                    return false;
                }

                terrainTargets[columnIndex] = bedHeight;
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
                    if (seaWaterSurfaces[nextIndex] > 0
                        || basinByWetColumn[nextIndex] >= 0)
                    {
                        if (!isTargetBody
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

                    var bankHeight = Math.Max(
                        solidHeights[nextIndex],
                        profile.SurfaceHeightUnits);
                    if (bankHeight - solidHeights[nextIndex]
                        > MaximumBankRaiseUnits)
                    {
                        return false;
                    }

                    if (terrainTargets.TryGetValue(
                            nextIndex,
                            out var existingTarget))
                    {
                        terrainTargets[nextIndex] = Math.Max(
                            existingTarget,
                            bankHeight);
                    }
                    else
                    {
                        terrainTargets[nextIndex] = bankHeight;
                    }
                }
            }

            var result = new ChannelPlan(
                world.Size,
                world.Height,
                archetype,
                WaterPlanRepairPolicy.Default);
            foreach (var pair in terrainTargets)
            {
                var x = pair.Key % world.Size;
                var z = pair.Key / world.Size;
                result.SetTerrainColumn(new PlannedTerrainColumn(
                    x,
                    z,
                    solidHeights[pair.Key],
                    pair.Value,
                    MaximumChannelCutUnits,
                    MaximumBankRaiseUnits));
            }

            for (var pathIndex = 0; pathIndex < trace.Path.Count; pathIndex++)
            {
                var columnIndex = trace.Path[pathIndex];
                var bedHeight = bedHeights[pathIndex];
                result.AddSection(new ChannelSectionPlan(
                    columnIndex,
                    widths[pathIndex],
                    levels[pathIndex] - bedHeight,
                    levels[pathIndex],
                    hydrology.GetFlowAccumulation(columnIndex)));
            }

            foreach (var pair in channelCells)
            {
                result.AddChannelColumn(pair.Key);
                AddChannelWetCells(
                    result,
                    world.Size,
                    pair.Key,
                    pair.Value.BedHeightUnits,
                    pair.Value.MaximumWaterTopUnits);
            }

            if (archetype == RiverChannelArchetype.SourceChannel)
            {
                if (!AddPersistentChannelSources(result, channelCells))
                {
                    return false;
                }
            }
            else if (hasHeadwaterSource)
            {
                AddHeadwaterSource(result, trace.Path, levels);
            }

            if (outlet.HasValue)
            {
                result.AddConnection(outlet.Value);
            }

            if (trace.Target.BasinId >= 0)
            {
                var shore = trace.Path[^1];
                var external = trace.Path.Count > 1
                    ? trace.Path[^2]
                    : shore;
                result.AddConnection(new BasinConnectionPort(
                    trace.Target.BasinId,
                    BasinConnectionType.Inlet,
                    trace.Target.WetColumnIndex,
                    shore,
                    external,
                    trace.Target.SurfaceHeightUnits,
                    ToDirection(
                        world.Size,
                        shore,
                        trace.Target.WetColumnIndex)));
            }

            if (!HydrologyFeaturePlan.TryCreate(
                    world.Size,
                    world.Height,
                    basins,
                    new[] { result },
                    out var validationPlan)
                || !WaterPlanValidator.Validate(
                    world,
                    validationPlan).IsValid)
            {
                return false;
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
                    channelCells[columnIndex] = new ChannelCellProfile(
                        Math.Min(existing.BedHeightUnits, bedHeight),
                        Math.Max(
                            existing.SurfaceHeightUnits,
                            surfaceHeight),
                        Math.Max(
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
            ChannelTrace trace,
            IReadOnlyList<int> solidHeights,
            IReadOnlyList<int> depthUnits,
            int? fixedStartSurface,
            RiverChannelArchetype archetype)
        {
            var levels = new int[trace.Path.Count];
            var targetSurfaceHeight = trace.Target.HasWaterBody
                ? trace.Target.SurfaceHeightUnits
                : ResolveIndependentTerminalSurface();
            if (archetype == RiverChannelArchetype.SourceChannel
                && (targetSurfaceHeight
                    % WorldGrid.HeightStepsPerCell) != 0)
            {
                return null;
            }

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

            return levels;

            int ResolveIndependentTerminalSurface()
            {
                var lastIndex = trace.Path.Count - 1;
                var solidHeight = solidHeights[trace.Path[lastIndex]];
                if (archetype == RiverChannelArchetype.SourceChannel)
                {
                    return AlignToCellCeiling(solidHeight);
                }

                var surface = solidHeight + depthUnits[lastIndex];
                return archetype == RiverChannelArchetype.SteppedDynamic
                    ? surface
                        / WorldGrid.HeightStepsPerCell
                        * WorldGrid.HeightStepsPerCell
                    : surface;
            }

            int ResolveNaturalSurface(int index)
            {
                var solidHeight = solidHeights[trace.Path[index]];
                var natural = archetype
                    == RiverChannelArchetype.SourceChannel
                        ? AlignToCellCeiling(solidHeight)
                        : solidHeight + depthUnits[index];
                if (archetype == RiverChannelArchetype.LowlandDynamic
                    && index + 1 < levels.Length)
                {
                    natural = Math.Min(natural, levels[index + 1] + 1);
                }
                else if (archetype
                    == RiverChannelArchetype.SteppedDynamic)
                {
                    natural = natural
                        / WorldGrid.HeightStepsPerCell
                        * WorldGrid.HeightStepsPerCell;
                }

                return natural;
            }
        }

        private static int[] BuildSectionWidths(
            ChannelTrace trace,
            HydrologyMap hydrology,
            int maximumWidth,
            RiverChannelArchetype archetype)
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
            for (var pathIndex = 1;
                 pathIndex < trace.Path.Count - 1;
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
                var progress = pathIndex
                    / (double)(trace.Path.Count - 1);
                var widthSignal = archetype switch
                {
                    RiverChannelArchetype.LowlandDynamic => Math.Max(
                        Math.Sqrt(relativeAccumulation),
                        progress * 0.8d),
                    RiverChannelArchetype.SourceChannel => Math.Max(
                        relativeAccumulation,
                        progress * 0.65d),
                    RiverChannelArchetype.MountainDynamic => Math.Max(
                        relativeAccumulation * 0.7d,
                        progress * 0.4d),
                    _ => 0d
                };
                var widthTier = Math.Clamp(
                    (int)Math.Floor(
                        widthSignal * (widthTierCount + 1)),
                    0,
                    widthTierCount);
                widths[pathIndex] = 1 + widthTier * 2;
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

            return widths;
        }

        private static int[] BuildSectionDepths(
            IReadOnlyList<int> widths,
            WorldGenerationSettings settings,
            RiverChannelArchetype archetype)
        {
            var result = new int[widths.Count];
            for (var index = 0; index < widths.Count; index++)
            {
                var widthDepth = (widths[index] - 1) / 2;
                var desiredDepth = archetype switch
                {
                    RiverChannelArchetype.MountainDynamic =>
                        settings.RiverDepthSteps + 1 + widthDepth,
                    RiverChannelArchetype.LowlandDynamic =>
                        Math.Max(1, settings.RiverDepthSteps - 1)
                        + widthDepth,
                    RiverChannelArchetype.SteppedDynamic =>
                        settings.MaximumRiverDepthSteps,
                    RiverChannelArchetype.SourceChannel =>
                        settings.RiverDepthSteps + widthDepth,
                    _ => settings.RiverDepthSteps
                };
                result[index] = Math.Clamp(
                    desiredDepth,
                    1,
                    settings.MaximumRiverDepthSteps);
            }

            return result;
        }

        private static RiverChannelArchetype ResolveArchetype(
            WorldData world,
            ChannelTrace trace,
            IReadOnlyList<int> solidHeights,
            int archetypeSlot)
        {
            var startIndex = trace.Path[0];
            var startHeight = solidHeights[startIndex];
            var relief = Math.Max(
                0,
                startHeight - (trace.Target.HasWaterBody
                    ? trace.Target.SurfaceHeightUnits
                    : solidHeights[trace.Path[^1]]));
            var slope = relief / (float)Math.Max(1, trace.Path.Count);
            var x = startIndex % world.Size;
            var z = startIndex / world.Size;
            var seed = DeterministicNoise.DeriveSeed(
                world.Seed,
                "river-channel-archetype");
            var value = DeterministicNoise.Value01(x, z, seed);
            switch (Math.Abs(archetypeSlot) % 4)
            {
                case 1:
                    return RiverChannelArchetype.SourceChannel;
                case 2:
                    return slope > 0.5f
                        ? RiverChannelArchetype.SteppedDynamic
                        : RiverChannelArchetype.LowlandDynamic;
                case 3:
                    return RiverChannelArchetype.MountainDynamic;
            }

            if (value < 0.2f)
            {
                return RiverChannelArchetype.SourceChannel;
            }

            if (slope >= 1.5f)
            {
                return value < 0.55f
                    ? RiverChannelArchetype.SteppedDynamic
                    : RiverChannelArchetype.MountainDynamic;
            }

            if (slope <= 0.5f)
            {
                return RiverChannelArchetype.LowlandDynamic;
            }

            return value < 0.6f
                ? RiverChannelArchetype.LowlandDynamic
                : RiverChannelArchetype.MountainDynamic;
        }

        private static IReadOnlyList<RiverChannelArchetype>
            BuildArchetypeOrder(RiverChannelArchetype preferred)
        {
            var result = new List<RiverChannelArchetype>(4)
            {
                preferred
            };
            AddIfMissing(RiverChannelArchetype.LowlandDynamic);
            AddIfMissing(RiverChannelArchetype.MountainDynamic);
            AddIfMissing(RiverChannelArchetype.SteppedDynamic);
            AddIfMissing(RiverChannelArchetype.SourceChannel);
            return result;

            void AddIfMissing(RiverChannelArchetype archetype)
            {
                if (!result.Contains(archetype))
                {
                    result.Add(archetype);
                }
            }
        }

        private static int ResolveArchetypeMaximumWidth(
            RiverChannelArchetype archetype,
            int configuredMaximumWidth)
        {
            configuredMaximumWidth = Math.Max(1, configuredMaximumWidth);
            if ((configuredMaximumWidth & 1) == 0)
            {
                configuredMaximumWidth--;
            }

            return archetype switch
            {
                RiverChannelArchetype.SteppedDynamic => 1,
                RiverChannelArchetype.MountainDynamic => Math.Min(
                    3,
                    configuredMaximumWidth),
                RiverChannelArchetype.SourceChannel => Math.Min(
                    3,
                    configuredMaximumWidth),
                _ => configuredMaximumWidth
            };
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

        private static void AddHeadwaterSource(
            ChannelPlan plan,
            IReadOnlyList<int> path,
            IReadOnlyList<int> levels)
        {
            var columnIndex = path[0];
            var x = columnIndex % plan.WorldSize;
            var z = columnIndex / plan.WorldSize;
            var section = plan.Sections[columnIndex];
            var bedHeight = levels[0] - section.CenterDepthUnits;
            var y = bedHeight / WorldGrid.HeightStepsPerCell;
            plan.AddSourceCell(new PlannedWaterCell(
                new CellCoordinate(x, y, z),
                WaterFlowDirectionMask.None));
        }

        private static bool AddPersistentChannelSources(
            ChannelPlan plan,
            IReadOnlyDictionary<int, ChannelCellProfile> channelCells)
        {
            foreach (var pair in channelCells)
            {
                var profile = pair.Value;
                if (profile.SurfaceHeightUnits <= 0
                    || profile.SurfaceHeightUnits
                        % WorldGrid.HeightStepsPerCell != 0)
                {
                    return false;
                }

                var y = profile.SurfaceHeightUnits
                    / WorldGrid.HeightStepsPerCell - 1;
                if ((uint)y >= plan.WorldHeight)
                {
                    return false;
                }

                var x = pair.Key % plan.WorldSize;
                var z = pair.Key / plan.WorldSize;
                plan.AddSourceCell(new PlannedWaterCell(
                    new CellCoordinate(x, y, z),
                    WaterFlowDirectionMask.None));
            }

            return true;
        }

        private static void TryAcceptChannel(
            WorldData world,
            IReadOnlyList<BasinPlan> basins,
            ChannelPlan channel,
            List<ChannelPlan> acceptedChannels,
            HashSet<int> usedColumns)
        {
            var candidateChannels = new List<ChannelPlan>(acceptedChannels)
            {
                channel
            };
            if (!HydrologyFeaturePlan.TryCreate(
                    world.Size,
                    world.Height,
                    basins,
                    candidateChannels,
                    out var merged)
                || !WaterPlanValidator.Validate(world, merged).IsValid)
            {
                return;
            }

            acceptedChannels.Add(channel);
            foreach (var columnIndex in channel.ChannelColumnIndices)
            {
                usedColumns.Add(columnIndex);
            }
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

        private static WaterFlowDirectionMask ToDirection(
            int size,
            int fromIndex,
            int toIndex)
        {
            var fromX = fromIndex % size;
            var fromZ = fromIndex / size;
            var toX = toIndex % size;
            var toZ = toIndex / size;
            if (toX > fromX) return WaterFlowDirectionMask.East;
            if (toX < fromX) return WaterFlowDirectionMask.West;
            if (toZ > fromZ) return WaterFlowDirectionMask.North;
            if (toZ < fromZ) return WaterFlowDirectionMask.South;
            return WaterFlowDirectionMask.None;
        }

        private static int AlignToCellCeiling(int heightUnits)
        {
            var step = WorldGrid.HeightStepsPerCell;
            return ((heightUnits + step - 1) / step) * step;
        }

        private static int CompareHeadwaters(
            HeadwaterCandidate left,
            HeadwaterCandidate right)
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.ColumnIndex.CompareTo(right.ColumnIndex);
        }

        private static void ValidateArguments(
            WorldData world,
            WorldGenerationSettings settings,
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
            public readonly ChannelTerminal Target;

            public ChannelTrace(
                IReadOnlyList<int> path,
                ChannelTerminal target)
            {
                Path = path;
                Target = target;
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
            public readonly float Score;

            public HeadwaterCandidate(int columnIndex, float score)
            {
                ColumnIndex = columnIndex;
                Score = score;
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
