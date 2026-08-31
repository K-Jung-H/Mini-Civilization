using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal sealed class TopologyRegion
    {
        private readonly HydrologyCellPlan[] cells;
        private readonly ReadOnlyCollection<HydrologyPlanEndpoint> endpoints;

        public TopologyRegion(
            TopologyRegionKey key,
            int size,
            HydrologyCellPlan[] cells,
            IList<HydrologyPlanEndpoint> endpoints)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            if (cells == null || cells.Length != checked(size * size))
            {
                throw new ArgumentException(
                    "Topology cells must match the Region core size.",
                    nameof(cells));
            }

            Key = key;
            Size = size;
            OriginX = checked(key.X * size);
            OriginZ = checked(key.Z * size);
            this.cells = cells;
            this.endpoints = new ReadOnlyCollection<HydrologyPlanEndpoint>(
                endpoints ?? throw new ArgumentNullException(nameof(endpoints)));
        }

        public TopologyRegionKey Key { get; }
        public int Size { get; }
        public int OriginX { get; }
        public int OriginZ { get; }
        public IReadOnlyList<HydrologyPlanEndpoint> Endpoints => endpoints;

        public HydrologyCellPlan Sample(int worldX, int worldZ)
        {
            var localX = worldX - OriginX;
            var localZ = worldZ - OriginZ;
            if ((uint)localX >= Size || (uint)localZ >= Size)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(worldX),
                    "Topology sample is outside the Region core.");
            }

            return cells[localX + Size * localZ];
        }
    }

    internal static class TopologyRegionBuilder
    {
        private static readonly (int x, int z, float cost)[] GrowthNeighbors =
        {
            (-1, -1, MathF.Sqrt(2f)), (0, -1, 1f), (1, -1, MathF.Sqrt(2f)),
            (-1, 0, 1f),                       (1, 0, 1f),
            (-1, 1, MathF.Sqrt(2f)),  (0, 1, 1f),  (1, 1, MathF.Sqrt(2f))
        };

        private static readonly (int x, int z)[] CardinalNeighbors =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1)
        };

        public static TopologyRegion Build(
            WorldHydrology hydrology,
            TopologyRegionKey key,
            BaseTerrainRegionStore.Scope baseTerrain,
            BasinComponentStore.Scope basins) =>
            TopologySpatialBuilder.Build(hydrology, key, baseTerrain, basins);

        // Retained only until phase 5 removes the previous local-halo planner.
        // The active TopologyRegionStore calls the component-owned overload above.
        private static TopologyRegion BuildLegacy(
            WorldHydrology hydrology,
            TopologyRegionKey key)
        {
            if (hydrology == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            var settings = hydrology.Settings;
            var map = settings.Hydrology.Map;
            var basins = settings.Hydrology.Basins;
            var size = map.PlanningRegionSizeCells;
            var originX = checked(key.X * size);
            var originZ = checked(key.Z * size);
            var halo = GetDependencyHaloCells(map, basins);
            var gridOriginX = checked(originX - halo);
            var gridOriginZ = checked(originZ - halo);
            var gridSize = checked(size + halo * 2);
            var samples = SampleBaseTerrain(
                hydrology,
                gridOriginX,
                gridOriginZ,
                gridSize);
            var coreCells = BuildBaseCoreCells(
                settings,
                samples,
                gridSize,
                halo,
                size);
            var candidates = BuildCandidates(
                settings,
                samples,
                gridOriginX,
                gridOriginZ,
                gridSize);
            var potential = BuildPotential(
                settings,
                gridOriginX,
                gridOriginZ,
                gridSize);
            var reserved = new bool[checked(gridSize * gridSize)];
            var accepted = new List<AcceptedBasin>();
            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var footprint = TryBuildFootprint(
                    settings,
                    candidate,
                    samples,
                    potential,
                    gridSize,
                    reserved);
                if (footprint == null)
                {
                    continue;
                }

                MarkReserved(
                    footprint,
                    reserved,
                    gridSize,
                    basins.MinimumSeparationCells);
                accepted.Add(new AcceptedBasin(candidate, footprint));
            }

            var endpoints = new List<HydrologyPlanEndpoint>();
            for (var index = 0; index < accepted.Count; index++)
            {
                WriteBasin(
                    settings,
                    accepted[index],
                    samples,
                    gridOriginX,
                    gridOriginZ,
                    gridSize,
                    originX,
                    originZ,
                    size,
                    coreCells,
                    endpoints);
            }

            AddSeaEndpoints(
                settings,
                samples,
                gridOriginX,
                gridOriginZ,
                gridSize,
                halo,
                originX,
                originZ,
                size,
                endpoints);
            endpoints.Sort((left, right) => left.Id.CompareTo(right.Id));
            return new TopologyRegion(key, size, coreCells, endpoints);
        }

        public static int GetDependencyHaloCells(
            in HydrologyMapSettingsData map,
            in BasinPatternSettingsData basins) => checked(
            basins.MaximumReachCells
            + basins.MinimumSeparationCells
            + basins.ShoreTransitionCells
            + map.BasinSeedSpacingCells
            + map.RouteSampleSpacingCells);

        private static BaseTerrainSample[] SampleBaseTerrain(
            WorldHydrology hydrology,
            int originX,
            int originZ,
            int size)
        {
            var samples = new BaseTerrainSample[checked(size * size)];
            for (var localZ = 0; localZ < size; localZ++)
            for (var localX = 0; localX < size; localX++)
            {
                samples[localX + size * localZ] = hydrology.SampleBaseTerrain(
                    checked(originX + localX),
                    checked(originZ + localZ));
            }

            return samples;
        }

        private static HydrologyCellPlan[] BuildBaseCoreCells(
            WorldSettingsData settings,
            IReadOnlyList<BaseTerrainSample> samples,
            int gridSize,
            int halo,
            int size)
        {
            var cells = new HydrologyCellPlan[checked(size * size)];
            for (var localZ = 0; localZ < size; localZ++)
            for (var localX = 0; localX < size; localX++)
            {
                var sample = samples[
                    localX + halo + gridSize * (localZ + halo)];
                var terrain = ToHeightUnits(settings, sample.Surface.SurfaceUnits);
                if (!sample.Surface.HasSeaWater)
                {
                    cells[localX + size * localZ] = new HydrologyCellPlan(
                        terrain,
                        0,
                        WaterType.None,
                        default,
                        0f,
                        0f);
                    continue;
                }

                var waterTop = Math.Clamp(
                    sample.Surface.WaterTopUnits,
                    0,
                    MaximumHeightUnits(settings));
                var waterType = waterTop > terrain
                    ? WaterType.Sea
                    : WaterType.None;
                cells[localX + size * localZ] = new HydrologyCellPlan(
                    terrain,
                    waterTop,
                    waterType,
                    default,
                    1f,
                    sample.Terrain.PatternDepthProgress);
            }

            return cells;
        }

        private static float[] BuildPotential(
            WorldSettingsData settings,
            int originX,
            int originZ,
            int size)
        {
            var map = settings.Hydrology.Map;
            var seed = Seed(settings.Seed,
                "Hydrology.Topology.Basin.Potential");
            var potential = new float[checked(size * size)];
            for (var localZ = 0; localZ < size; localZ++)
            for (var localX = 0; localX < size; localX++)
            {
                var value = WorldNoiseFieldSampler.Sample2D(
                    checked(originX + localX),
                    checked(originZ + localZ),
                    map.BasinPotentialField,
                    seed);
                potential[localX + size * localZ] = map.BasinPotentialResponse
                    .Evaluate(ToUnitValue(value, map.BasinPotentialField.Mode));
            }

            return potential;
        }

        private static List<BasinCandidate> BuildCandidates(
            WorldSettingsData settings,
            IReadOnlyList<BaseTerrainSample> samples,
            int gridOriginX,
            int gridOriginZ,
            int gridSize)
        {
            var map = settings.Hydrology.Map;
            var spacing = map.BasinSeedSpacingCells;
            var minimumSeedX = FloorDivide(gridOriginX, spacing) - 1;
            var maximumSeedX = FloorDivide(
                checked(gridOriginX + gridSize - 1),
                spacing) + 1;
            var minimumSeedZ = FloorDivide(gridOriginZ, spacing) - 1;
            var maximumSeedZ = FloorDivide(
                checked(gridOriginZ + gridSize - 1),
                spacing) + 1;
            var candidates = new List<BasinCandidate>();
            for (var seedZ = minimumSeedZ; seedZ <= maximumSeedZ; seedZ++)
            for (var seedX = minimumSeedX; seedX <= maximumSeedX; seedX++)
            {
                TryAddCandidate(
                    candidates,
                    settings,
                    settings.Hydrology.Basins.Lake,
                    WaterType.Lake,
                    seedX,
                    seedZ,
                    spacing,
                    samples,
                    gridOriginX,
                    gridOriginZ,
                    gridSize);
                TryAddCandidate(
                    candidates,
                    settings,
                    settings.Hydrology.Basins.Pond,
                    WaterType.Pond,
                    seedX,
                    seedZ,
                    spacing,
                    samples,
                    gridOriginX,
                    gridOriginZ,
                    gridSize);
            }

            candidates.Sort((left, right) =>
            {
                var priority = right.Priority.CompareTo(left.Priority);
                return priority != 0
                    ? priority
                    : left.Id.CompareTo(right.Id);
            });
            return candidates;
        }

        private static void TryAddCandidate(
            List<BasinCandidate> candidates,
            WorldSettingsData settings,
            in BasinProfileSettingsData profile,
            WaterType type,
            int seedGridX,
            int seedGridZ,
            int spacing,
            IReadOnlyList<BaseTerrainSample> samples,
            int gridOriginX,
            int gridOriginZ,
            int gridSize)
        {
            var typeName = type == WaterType.Lake ? "Lake" : "Pond";
            if (DeterministicNoise.Value01(
                    seedGridX,
                    seedGridZ,
                    Seed(settings.Seed,
                        $"Hydrology.Topology.Basin.{typeName}.Activation"))
                >= profile.Occurrence)
            {
                return;
            }

            var positionSeed = Seed(
                settings.Seed,
                $"Hydrology.Topology.Basin.{typeName}.Position");
            var worldX = checked(seedGridX * spacing + Math.Min(
                spacing - 1,
                (int)(DeterministicNoise.Value01(
                    seedGridX,
                    seedGridZ,
                    positionSeed) * spacing)));
            var worldZ = checked(seedGridZ * spacing + Math.Min(
                spacing - 1,
                (int)(DeterministicNoise.Value01(
                    seedGridZ,
                    seedGridX,
                    positionSeed) * spacing)));
            var localX = worldX - gridOriginX;
            var localZ = worldZ - gridOriginZ;
            if ((uint)localX >= gridSize || (uint)localZ >= gridSize)
            {
                return;
            }

            var sample = samples[localX + gridSize * localZ];
            if (sample.Surface.HasSeaWater)
            {
                return;
            }

            var id = new BasinComponentId(type, seedGridX, seedGridZ);
            var area = ResolveRange(
                profile.AreaCells,
                DeterministicNoise.Value01(
                    worldX,
                    worldZ,
                    Seed(settings.Seed,
                        $"Hydrology.Topology.Basin.{typeName}.Area")));
            var depth = ResolveRange(
                profile.MaximumDepthUnits,
                DeterministicNoise.Value01(
                    worldX,
                    worldZ,
                    Seed(settings.Seed,
                        $"Hydrology.Topology.Basin.{typeName}.Depth")));
            candidates.Add(new BasinCandidate(
                id,
                worldX,
                worldZ,
                localX,
                localZ,
                (int)Math.Round(
                    area,
                    MidpointRounding.AwayFromZero),
                depth,
                DeterministicNoise.Value01(
                    seedGridX,
                    seedGridZ,
                    Seed(settings.Seed,
                        $"Hydrology.Topology.Basin.{typeName}.Priority"))));
        }

        private static List<int> TryBuildFootprint(
            WorldSettingsData settings,
            in BasinCandidate candidate,
            IReadOnlyList<BaseTerrainSample> samples,
            IReadOnlyList<float> potential,
            int gridSize,
            IReadOnlyList<bool> reserved)
        {
            var seedCell = candidate.LocalX + gridSize * candidate.LocalZ;
            if (reserved[seedCell] || samples[seedCell].Surface.HasSeaWater)
            {
                return null;
            }

            var map = settings.Hydrology.Map;
            var distances = new Dictionary<int, float>();
            var footprint = new List<int>(candidate.TargetAreaCells);
            var frontier = new CellCostHeap();
            distances.Add(seedCell, 0f);
            frontier.Push(seedCell, 0f);
            while (frontier.Count > 0 && footprint.Count < candidate.TargetAreaCells)
            {
                var current = frontier.Pop();
                if (!distances.TryGetValue(current.Cell, out var knownCost)
                    || current.Cost != knownCost)
                {
                    continue;
                }

                footprint.Add(current.Cell);
                var currentX = current.Cell % gridSize;
                var currentZ = current.Cell / gridSize;
                for (var direction = 0;
                     direction < GrowthNeighbors.Length;
                     direction++)
                {
                    var neighbor = GrowthNeighbors[direction];
                    var nextX = currentX + neighbor.x;
                    var nextZ = currentZ + neighbor.z;
                    if ((uint)nextX >= gridSize || (uint)nextZ >= gridSize
                        || Math.Max(
                            Math.Abs(nextX - candidate.LocalX),
                            Math.Abs(nextZ - candidate.LocalZ))
                            > settings.Hydrology.Basins.MaximumReachCells)
                    {
                        continue;
                    }

                    var next = nextX + gridSize * nextZ;
                    if (reserved[next] || samples[next].Surface.HasSeaWater)
                    {
                        continue;
                    }

                    var terrainDelta = MathF.Abs(
                        samples[next].Surface.SurfaceUnits
                        - samples[current.Cell].Surface.SurfaceUnits)
                        / WorldGrid.HeightStepsPerCell;
                    var slope = terrainDelta / neighbor.cost;
                    var cost = current.Cost + neighbor.cost
                        + neighbor.cost * (
                        potential[next] * map.BasinPotentialCost
                        + terrainDelta * map.TerrainDeformationCost
                        + slope * map.SlopeCost);
                    if (distances.TryGetValue(next, out var previous)
                        && previous <= cost)
                    {
                        continue;
                    }

                    distances[next] = cost;
                    frontier.Push(next, cost);
                }
            }

            return footprint.Count == candidate.TargetAreaCells
                ? footprint
                : null;
        }

        private static void MarkReserved(
            IReadOnlyList<int> footprint,
            bool[] reserved,
            int gridSize,
            int clearance)
        {
            for (var index = 0; index < footprint.Count; index++)
            {
                var cell = footprint[index];
                var x = cell % gridSize;
                var z = cell / gridSize;
                for (var offsetZ = -clearance;
                     offsetZ <= clearance;
                     offsetZ++)
                for (var offsetX = -clearance;
                     offsetX <= clearance;
                     offsetX++)
                {
                    if (Math.Max(Math.Abs(offsetX), Math.Abs(offsetZ))
                        > clearance)
                    {
                        continue;
                    }

                    var nextX = x + offsetX;
                    var nextZ = z + offsetZ;
                    if ((uint)nextX < gridSize && (uint)nextZ < gridSize)
                    {
                        reserved[nextX + gridSize * nextZ] = true;
                    }
                }
            }
        }

        private static void WriteBasin(
            WorldSettingsData settings,
            in AcceptedBasin basin,
            IReadOnlyList<BaseTerrainSample> samples,
            int gridOriginX,
            int gridOriginZ,
            int gridSize,
            int coreOriginX,
            int coreOriginZ,
            int coreSize,
            HydrologyCellPlan[] coreCells,
            List<HydrologyPlanEndpoint> endpoints)
        {
            var boundary = FindBoundary(basin.Footprint, gridSize);
            var waterTop = SelectWaterTop(
                basin.Footprint,
                boundary,
                samples,
                settings.Hydrology.Basins);
            var interiorDistance = BuildInteriorDistance(
                basin.Footprint,
                boundary,
                gridSize,
                out var maximumInteriorDistance);
            var bedSeed = Seed(settings.Seed,
                "Hydrology.Topology.Basin.Bed");
            var bedAmplitude = ResolveRange(
                settings.Hydrology.Basins.BedAmplitudeUnits,
                DeterministicNoise.Value01(
                    basin.Candidate.WorldX,
                    basin.Candidate.WorldZ,
                    Seed(settings.Seed,
                        "Hydrology.Topology.Basin.BedAmplitude")));
            for (var index = 0; index < basin.Footprint.Count; index++)
            {
                var gridCell = basin.Footprint[index];
                var localX = gridCell % gridSize;
                var localZ = gridCell / gridSize;
                var worldX = checked(gridOriginX + localX);
                var worldZ = checked(gridOriginZ + localZ);
                var progress = maximumInteriorDistance > 0
                    ? interiorDistance[gridCell]
                        / (float)maximumInteriorDistance
                    : 1f;
                var depthProgress = settings.Hydrology.Basins.DepthByInterior
                    .Evaluate(progress);
                var bedDetail = ToSignedValue(
                        WorldNoiseFieldSampler.Sample2D(
                            worldX,
                            worldZ,
                            settings.Hydrology.Basins.BedField,
                            bedSeed),
                        settings.Hydrology.Basins.BedField.Mode)
                    * bedAmplitude * depthProgress;
                var target = ToHeightUnits(
                    settings,
                    waterTop - basin.Candidate.MaximumDepthUnits
                    * depthProgress + bedDetail);
                var waterType = target < waterTop
                    ? basin.Candidate.Id.Type
                    : WaterType.None;
                if (TryGetCoreIndex(
                        worldX,
                        worldZ,
                        coreOriginX,
                        coreOriginZ,
                        coreSize,
                        out var coreIndex))
                {
                    coreCells[coreIndex] = new HydrologyCellPlan(
                        target,
                        waterTop,
                        waterType,
                        basin.Candidate.Id,
                        1f,
                        progress);
                }
            }

            WriteShoreTransition(
                settings,
                basin.Candidate.Id,
                basin.Footprint,
                boundary,
                samples,
                waterTop,
                gridOriginX,
                gridOriginZ,
                gridSize,
                coreOriginX,
                coreOriginZ,
                coreSize,
                coreCells);
            AddBasinEndpoint(
                basin,
                coreCells,
                gridOriginX,
                gridOriginZ,
                gridSize,
                coreOriginX,
                coreOriginZ,
                coreSize,
                waterTop,
                endpoints);
        }

        private static List<int> FindBoundary(
            IReadOnlyList<int> footprint,
            int gridSize)
        {
            var membership = new HashSet<int>(footprint);
            var result = new List<int>();
            for (var index = 0; index < footprint.Count; index++)
            {
                var cell = footprint[index];
                var x = cell % gridSize;
                var z = cell / gridSize;
                if (x == 0 || z == 0 || x == gridSize - 1 || z == gridSize - 1)
                {
                    result.Add(cell);
                    continue;
                }

                for (var direction = 0;
                     direction < CardinalNeighbors.Length;
                     direction++)
                {
                    var neighbor = CardinalNeighbors[direction];
                    if (!membership.Contains(
                            x + neighbor.x + gridSize * (z + neighbor.z)))
                    {
                        result.Add(cell);
                        break;
                    }
                }
            }

            return result;
        }

        private static int SelectWaterTop(
            IReadOnlyList<int> footprint,
            IReadOnlyList<int> boundary,
            IReadOnlyList<BaseTerrainSample> samples,
            in BasinPatternSettingsData settings)
        {
            var minimum = int.MaxValue;
            var maximum = int.MinValue;
            for (var index = 0; index < footprint.Count; index++)
            {
                var surface = (int)MathF.Round(
                    samples[footprint[index]].Surface.SurfaceUnits,
                    MidpointRounding.AwayFromZero);
                minimum = Math.Min(minimum, surface);
                maximum = Math.Max(maximum, surface);
            }

            var boundarySet = new HashSet<int>(boundary);
            var bestUnits = minimum;
            var bestCost = float.PositiveInfinity;
            for (var candidate = minimum; candidate <= maximum; candidate++)
            {
                var cost = 0f;
                for (var index = 0; index < footprint.Count; index++)
                {
                    var cell = footprint[index];
                    var delta = samples[cell].Surface.SurfaceUnits - candidate;
                    cost += delta >= 0f
                        ? delta * settings.CutCost
                        : -delta * settings.FillCost;
                    if (boundarySet.Contains(cell))
                    {
                        cost += MathF.Abs(delta) * settings.RimCost;
                    }
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestUnits = candidate;
                }
            }

            return bestUnits;
        }

        private static Dictionary<int, int> BuildInteriorDistance(
            IReadOnlyList<int> footprint,
            IReadOnlyList<int> boundary,
            int gridSize,
            out int maximumDistance)
        {
            var membership = new HashSet<int>(footprint);
            var distances = new Dictionary<int, int>(footprint.Count);
            var queue = new Queue<int>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distances.Add(boundary[index], 0);
                queue.Enqueue(boundary[index]);
            }

            maximumDistance = 0;
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                var distance = distances[cell];
                var x = cell % gridSize;
                var z = cell / gridSize;
                for (var direction = 0;
                     direction < CardinalNeighbors.Length;
                     direction++)
                {
                    var neighbor = CardinalNeighbors[direction];
                    var nextX = x + neighbor.x;
                    var nextZ = z + neighbor.z;
                    if ((uint)nextX >= gridSize || (uint)nextZ >= gridSize)
                    {
                        continue;
                    }

                    var next = nextX + gridSize * nextZ;
                    if (!membership.Contains(next) || distances.ContainsKey(next))
                    {
                        continue;
                    }

                    var nextDistance = distance + 1;
                    distances.Add(next, nextDistance);
                    maximumDistance = Math.Max(maximumDistance, nextDistance);
                    queue.Enqueue(next);
                }
            }

            return distances;
        }

        private static void WriteShoreTransition(
            WorldSettingsData settings,
            in BasinComponentId component,
            IReadOnlyList<int> footprint,
            IReadOnlyList<int> boundary,
            IReadOnlyList<BaseTerrainSample> samples,
            int waterTop,
            int gridOriginX,
            int gridOriginZ,
            int gridSize,
            int coreOriginX,
            int coreOriginZ,
            int coreSize,
            HydrologyCellPlan[] coreCells)
        {
            var footprintSet = new HashSet<int>(footprint);
            var distance = new Dictionary<int, int>();
            var queue = new Queue<int>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distance.Add(boundary[index], 0);
                queue.Enqueue(boundary[index]);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentDistance = distance[current];
                if (currentDistance >= settings.Hydrology.Basins.ShoreTransitionCells)
                {
                    continue;
                }

                var x = current % gridSize;
                var z = current / gridSize;
                for (var direction = 0;
                     direction < CardinalNeighbors.Length;
                     direction++)
                {
                    var neighbor = CardinalNeighbors[direction];
                    var nextX = x + neighbor.x;
                    var nextZ = z + neighbor.z;
                    if ((uint)nextX >= gridSize || (uint)nextZ >= gridSize)
                    {
                        continue;
                    }

                    var next = nextX + gridSize * nextZ;
                    if (footprintSet.Contains(next) || distance.ContainsKey(next))
                    {
                        continue;
                    }

                    var nextDistance = currentDistance + 1;
                    distance.Add(next, nextDistance);
                    queue.Enqueue(next);
                    var worldX = checked(gridOriginX + nextX);
                    var worldZ = checked(gridOriginZ + nextZ);
                    if (!TryGetCoreIndex(
                            worldX,
                            worldZ,
                            coreOriginX,
                            coreOriginZ,
                            coreSize,
                            out var coreIndex))
                    {
                        continue;
                    }

                    var membership = settings.Hydrology.Basins.ShoreTransition
                        .Evaluate(1f - nextDistance
                            / (float)settings.Hydrology.Basins
                                .ShoreTransitionCells);
                    var currentPlan = coreCells[coreIndex];
                    if (currentPlan.HasWater
                        || currentPlan.BasinComponent.IsValid
                        && (currentPlan.Membership > membership
                            || currentPlan.Membership == membership
                            && currentPlan.BasinComponent.CompareTo(component)
                                <= 0))
                    {
                        continue;
                    }

                    var target = ToHeightUnits(
                        settings,
                        samples[next].Surface.SurfaceUnits
                        + (waterTop - samples[next].Surface.SurfaceUnits)
                            * membership);
                    coreCells[coreIndex] = new HydrologyCellPlan(
                        target,
                        0,
                        WaterType.None,
                        component,
                        membership,
                        0f);
                }
            }
        }

        private static void AddBasinEndpoint(
            in AcceptedBasin basin,
            IReadOnlyList<HydrologyCellPlan> coreCells,
            int gridOriginX,
            int gridOriginZ,
            int gridSize,
            int coreOriginX,
            int coreOriginZ,
            int coreSize,
            int waterTop,
            List<HydrologyPlanEndpoint> endpoints)
        {
            if (!TryGetCoreIndex(
                    basin.Candidate.WorldX,
                    basin.Candidate.WorldZ,
                    coreOriginX,
                    coreOriginZ,
                    coreSize,
                    out _))
            {
                return;
            }

            var selected = -1;
            for (var index = 0; index < basin.Footprint.Count; index++)
            {
                var gridCell = basin.Footprint[index];
                var worldX = checked(gridOriginX + gridCell % gridSize);
                var worldZ = checked(gridOriginZ + gridCell / gridSize);
                if (!TryGetCoreIndex(
                        worldX,
                        worldZ,
                        coreOriginX,
                        coreOriginZ,
                        coreSize,
                        out var coreIndex)
                    || coreCells[coreIndex].WaterType
                        != basin.Candidate.Id.Type)
                {
                    continue;
                }

                if (selected < 0 || gridCell < selected)
                {
                    selected = gridCell;
                }
            }

            if (selected < 0)
            {
                return;
            }

            var endpointX = checked(gridOriginX + selected % gridSize);
            var endpointZ = checked(gridOriginZ + selected / gridSize);
            var kind = basin.Candidate.Id.Type == WaterType.Lake
                ? HydrologyPlanEndpointKind.Lake
                : HydrologyPlanEndpointKind.Pond;
            var id = new HydrologyPlanEndpointId(
                kind,
                endpointX,
                endpointZ,
                basin.Candidate.Id);
            endpoints.Add(new HydrologyPlanEndpoint(id, waterTop));
        }

        private static void AddSeaEndpoints(
            WorldSettingsData settings,
            IReadOnlyList<BaseTerrainSample> samples,
            int gridOriginX,
            int gridOriginZ,
            int gridSize,
            int halo,
            int coreOriginX,
            int coreOriginZ,
            int coreSize,
            List<HydrologyPlanEndpoint> endpoints)
        {
            var spacing = settings.Hydrology.Map.RouteSampleSpacingCells;
            for (var localZ = 0; localZ < coreSize; localZ++)
            for (var localX = 0; localX < coreSize; localX++)
            {
                var worldX = checked(coreOriginX + localX);
                var worldZ = checked(coreOriginZ + localZ);
                if (FloorDivide(worldX, spacing) * spacing != worldX
                    || FloorDivide(worldZ, spacing) * spacing != worldZ)
                {
                    continue;
                }

                var gridX = localX + halo;
                var gridZ = localZ + halo;
                var sample = samples[gridX + gridSize * gridZ];
                if (!sample.Surface.HasSeaWater || !IsSeaCoast(
                        samples,
                        gridX,
                        gridZ,
                        gridSize,
                        spacing))
                {
                    continue;
                }

                var id = new HydrologyPlanEndpointId(
                    HydrologyPlanEndpointKind.Sea,
                    worldX,
                    worldZ,
                    default);
                endpoints.Add(new HydrologyPlanEndpoint(
                    id,
                    sample.Surface.WaterTopUnits));
            }
        }

        private static bool IsSeaCoast(
            IReadOnlyList<BaseTerrainSample> samples,
            int gridX,
            int gridZ,
            int gridSize,
            int spacing)
        {
            return !samples[gridX - spacing + gridSize * gridZ]
                        .Surface.HasSeaWater
                || !samples[gridX + spacing + gridSize * gridZ]
                        .Surface.HasSeaWater
                || !samples[gridX + gridSize * (gridZ - spacing)]
                        .Surface.HasSeaWater
                || !samples[gridX + gridSize * (gridZ + spacing)]
                        .Surface.HasSeaWater;
        }

        private static bool TryGetCoreIndex(
            int worldX,
            int worldZ,
            int originX,
            int originZ,
            int size,
            out int index)
        {
            var localX = worldX - originX;
            var localZ = worldZ - originZ;
            if ((uint)localX >= size || (uint)localZ >= size)
            {
                index = -1;
                return false;
            }

            index = localX + size * localZ;
            return true;
        }

        private static int ToHeightUnits(
            WorldSettingsData settings,
            float value) => Math.Clamp(
            (int)MathF.Round(value, MidpointRounding.AwayFromZero),
            0,
            MaximumHeightUnits(settings));

        private static int MaximumHeightUnits(WorldSettingsData settings) =>
            checked(settings.WorldHeight * WorldGrid.HeightStepsPerCell);

        private static float ResolveRange(
            in WorldSeededRangeSettingsData range,
            float amount) => range.Minimum
                + (range.Maximum - range.Minimum) * amount;

        private static int Seed(int worldSeed, string channel) =>
            DeterministicNoise.DeriveSeed(worldSeed, channel);

        private static float ToUnitValue(float value, WorldNoiseMode mode)
        {
            var unit = mode is WorldNoiseMode.Signed
                or WorldNoiseMode.SignedRidge
                    ? (value + 1f) * 0.5f
                    : value;
            return Math.Clamp(unit, 0f, 1f);
        }

        private static float ToSignedValue(float value, WorldNoiseMode mode) =>
            ToUnitValue(value, mode) * 2f - 1f;

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }

        private readonly struct BasinCandidate
        {
            public BasinCandidate(
                BasinComponentId id,
                int worldX,
                int worldZ,
                int localX,
                int localZ,
                int targetAreaCells,
                float maximumDepthUnits,
                float priority)
            {
                Id = id;
                WorldX = worldX;
                WorldZ = worldZ;
                LocalX = localX;
                LocalZ = localZ;
                TargetAreaCells = targetAreaCells;
                MaximumDepthUnits = maximumDepthUnits;
                Priority = priority;
            }

            public BasinComponentId Id { get; }
            public int WorldX { get; }
            public int WorldZ { get; }
            public int LocalX { get; }
            public int LocalZ { get; }
            public int TargetAreaCells { get; }
            public float MaximumDepthUnits { get; }
            public float Priority { get; }
        }

        private readonly struct AcceptedBasin
        {
            public AcceptedBasin(
                BasinCandidate candidate,
                List<int> footprint)
            {
                Candidate = candidate;
                Footprint = footprint ?? throw new ArgumentNullException(
                    nameof(footprint));
            }

            public BasinCandidate Candidate { get; }
            public List<int> Footprint { get; }
        }

        private readonly struct CellCost
        {
            public CellCost(int cell, float cost)
            {
                Cell = cell;
                Cost = cost;
            }

            public int Cell { get; }
            public float Cost { get; }
        }

        private sealed class CellCostHeap
        {
            private readonly List<CellCost> entries = new();
            public int Count => entries.Count;

            public void Push(int cell, float cost)
            {
                var entry = new CellCost(cell, cost);
                entries.Add(entry);
                var index = entries.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (Compare(entries[parent], entry) <= 0)
                    {
                        break;
                    }

                    entries[index] = entries[parent];
                    index = parent;
                }

                entries[index] = entry;
            }

            public CellCost Pop()
            {
                var root = entries[0];
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
                        && Compare(entries[right], entries[left]) < 0
                            ? right
                            : left;
                    if (Compare(entries[child], last) >= 0)
                    {
                        break;
                    }

                    entries[index] = entries[child];
                    index = child;
                }

                entries[index] = last;
                return root;
            }

            private static int Compare(in CellCost left, in CellCost right)
            {
                var cost = left.Cost.CompareTo(right.Cost);
                return cost != 0 ? cost : left.Cell.CompareTo(right.Cell);
            }
        }
    }
}
