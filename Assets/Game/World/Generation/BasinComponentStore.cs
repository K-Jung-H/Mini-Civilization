using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    /// <summary>
    /// A base-terrain Region is the only owner that samples density/surface for
    /// its core.  Basin and topology plans share it by scope instead of sampling
    /// overlapping halo rectangles independently.
    /// </summary>
    internal sealed class BaseTerrainRegion
    {
        private readonly BaseTerrainSample[] samples;

        public BaseTerrainRegion(
            TopologyRegionKey key,
            int size,
            BaseTerrainSample[] samples)
        {
            if (size <= 0 || samples == null || samples.Length != checked(size * size))
            {
                throw new ArgumentOutOfRangeException(nameof(samples));
            }

            Key = key;
            Size = size;
            OriginX = checked(key.X * size);
            OriginZ = checked(key.Z * size);
            this.samples = samples;
        }

        public TopologyRegionKey Key { get; }
        public int Size { get; }
        public int OriginX { get; }
        public int OriginZ { get; }

        public BaseTerrainSample Sample(int worldX, int worldZ)
        {
            var localX = worldX - OriginX;
            var localZ = worldZ - OriginZ;
            if ((uint)localX >= Size || (uint)localZ >= Size)
            {
                throw new ArgumentOutOfRangeException(nameof(worldX));
            }

            return samples[localX + Size * localZ];
        }
    }

    internal sealed class BaseTerrainRegionStore
    {
        private readonly WorldHydrology hydrology;
        private readonly ConcurrentDictionary<TopologyRegionKey, Entry> entries = new();

        public BaseTerrainRegionStore(WorldHydrology hydrology)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
        }

        public int Count => entries.Count;
        public Scope BeginScope() => new(this);

        private Entry Acquire(TopologyRegionKey key)
        {
            while (true)
            {
                var entry = entries.GetOrAdd(key, CreateEntry);
                lock (entry.Gate)
                {
                    if (entry.Evicted)
                    {
                        continue;
                    }

                    entry.ScopeCount++;
                    return entry;
                }
            }
        }

        private void Release(TopologyRegionKey key, Entry entry)
        {
            lock (entry.Gate)
            {
                if (entry.ScopeCount <= 0)
                {
                    throw new InvalidOperationException(
                        "Base Terrain Region Scope ownership is unbalanced.");
                }

                entry.ScopeCount--;
                if (entry.ScopeCount != 0)
                {
                    return;
                }

                entry.Evicted = true;
                entries.TryRemove(key, out _);
            }
        }

        private Entry CreateEntry(TopologyRegionKey key) => new(() =>
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var size = hydrology.Settings.Hydrology.Map.PlanningRegionSizeCells;
                var originX = checked(key.X * size);
                var originZ = checked(key.Z * size);
                var samples = new BaseTerrainSample[checked(size * size)];
                for (var localZ = 0; localZ < size; localZ++)
                for (var localX = 0; localX < size; localX++)
                {
                    samples[localX + size * localZ] = hydrology.SampleBaseTerrain(
                        checked(originX + localX),
                        checked(originZ + localZ));
                }

                return new BaseTerrainRegion(key, size, samples);
            }
            finally
            {
                hydrology.Metrics.RecordBaseTerrainRegion(
                    Stopwatch.GetTimestamp() - started);
            }
        });

        internal sealed class Scope : IDisposable
        {
            private BaseTerrainRegionStore owner;
            private Dictionary<TopologyRegionKey, Entry> acquired = new();

            internal Scope(BaseTerrainRegionStore owner)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public BaseTerrainSample Sample(int worldX, int worldZ)
            {
                if (owner == null)
                {
                    throw new ObjectDisposedException(nameof(Scope));
                }

                var key = owner.hydrology.GetTopologyRegionKey(worldX, worldZ);
                if (!acquired.TryGetValue(key, out var entry))
                {
                    entry = owner.Acquire(key);
                    acquired.Add(key, entry);
                    try
                    {
                        return entry.Plan.Value.Sample(worldX, worldZ);
                    }
                    catch
                    {
                        acquired.Remove(key);
                        owner.Release(key, entry);
                        throw;
                    }
                }

                return entry.Plan.Value.Sample(worldX, worldZ);
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }

                foreach (var pair in acquired)
                {
                    owner.Release(pair.Key, pair.Value);
                }

                acquired = null;
                owner = null;
            }
        }

        private sealed class Entry
        {
            public Entry(Func<BaseTerrainRegion> create)
            {
                Plan = new Lazy<BaseTerrainRegion>(
                    create ?? throw new ArgumentNullException(nameof(create)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public Lazy<BaseTerrainRegion> Plan { get; }
            public int ScopeCount;
            public bool Evicted;
        }
    }

    internal readonly struct BasinCell
    {
        public BasinCell(int worldX, int worldZ, float interiorProgress)
        {
            WorldX = worldX;
            WorldZ = worldZ;
            InteriorProgress = Math.Clamp(interiorProgress, 0f, 1f);
        }

        public int WorldX { get; }
        public int WorldZ { get; }
        public float InteriorProgress { get; }
    }

    /// <summary>
    /// One candidate's full, coordinate-owned Basin facts.  This contains no
    /// request Region and can therefore be reused by every overlapping topology
    /// core without reproducing its footprint growth.
    /// </summary>
    internal sealed class BasinComponent
    {
        private readonly ReadOnlyCollection<BasinCell> footprint;
        private readonly ReadOnlyCollection<BasinCell> boundary;
        private readonly HashSet<long> footprintSet;

        public BasinComponent(
            BasinComponentId id,
            bool isCandidate,
            float priority,
            int seedWorldX,
            int seedWorldZ,
            float maximumDepthUnits,
            int waterTopUnits,
            float bedAmplitudeUnits,
            IList<BasinCell> footprint,
            IList<BasinCell> boundary)
        {
            Id = id;
            IsCandidate = isCandidate;
            Priority = priority;
            SeedWorldX = seedWorldX;
            SeedWorldZ = seedWorldZ;
            MaximumDepthUnits = maximumDepthUnits;
            WaterTopUnits = waterTopUnits;
            BedAmplitudeUnits = bedAmplitudeUnits;
            this.footprint = new ReadOnlyCollection<BasinCell>(
                footprint ?? throw new ArgumentNullException(nameof(footprint)));
            this.boundary = new ReadOnlyCollection<BasinCell>(
                boundary ?? throw new ArgumentNullException(nameof(boundary)));
            footprintSet = new HashSet<long>();
            MinimumX = int.MaxValue;
            MinimumZ = int.MaxValue;
            MaximumX = int.MinValue;
            MaximumZ = int.MinValue;
            for (var index = 0; index < this.footprint.Count; index++)
            {
                var cell = this.footprint[index];
                footprintSet.Add(CellKey(cell.WorldX, cell.WorldZ));
                MinimumX = Math.Min(MinimumX, cell.WorldX);
                MinimumZ = Math.Min(MinimumZ, cell.WorldZ);
                MaximumX = Math.Max(MaximumX, cell.WorldX);
                MaximumZ = Math.Max(MaximumZ, cell.WorldZ);
            }

            if (this.footprint.Count == 0)
            {
                MinimumX = MaximumX = seedWorldX;
                MinimumZ = MaximumZ = seedWorldZ;
            }
        }

        public BasinComponentId Id { get; }
        public bool IsCandidate { get; }
        public float Priority { get; }
        public int SeedWorldX { get; }
        public int SeedWorldZ { get; }
        public float MaximumDepthUnits { get; }
        public int WaterTopUnits { get; }
        public float BedAmplitudeUnits { get; }
        public int MinimumX { get; }
        public int MinimumZ { get; }
        public int MaximumX { get; }
        public int MaximumZ { get; }
        public IReadOnlyList<BasinCell> Footprint => footprint;
        public IReadOnlyList<BasinCell> Boundary => boundary;

        public bool Contains(int worldX, int worldZ) =>
            footprintSet.Contains(CellKey(worldX, worldZ));

        public static long CellKey(int worldX, int worldZ) =>
            ((long)worldX << 32) ^ (uint)worldZ;
    }

    internal sealed class BasinComponentStore
    {
        private readonly WorldHydrology hydrology;
        private readonly ConcurrentDictionary<BasinComponentId, Entry> entries = new();

        public BasinComponentStore(WorldHydrology hydrology)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
        }

        public int Count => entries.Count;

        public Scope BeginScope(BaseTerrainRegionStore.Scope baseTerrain) =>
            new(this, baseTerrain);

        private Entry Acquire(
            BasinComponentId id,
            Func<BasinComponent> create)
        {
            while (true)
            {
                var entry = entries.GetOrAdd(id, _ => new Entry(create));
                lock (entry.Gate)
                {
                    if (entry.Evicted)
                    {
                        continue;
                    }

                    entry.ScopeCount++;
                    return entry;
                }
            }
        }

        private void Release(BasinComponentId id, Entry entry)
        {
            lock (entry.Gate)
            {
                if (entry.ScopeCount <= 0)
                {
                    throw new InvalidOperationException(
                        "Basin Component Scope ownership is unbalanced.");
                }

                entry.ScopeCount--;
                if (entry.ScopeCount != 0)
                {
                    return;
                }

                entry.Evicted = true;
                entries.TryRemove(id, out _);
            }
        }

        internal sealed class Scope : IDisposable
        {
            private BasinComponentStore owner;
            private readonly BaseTerrainRegionStore.Scope baseTerrain;
            private Dictionary<BasinComponentId, Entry> acquired = new();
            private readonly Dictionary<BasinComponentId, bool> activity = new();
            private readonly HashSet<BasinComponentId> resolving = new();

            internal Scope(
                BasinComponentStore owner,
                BaseTerrainRegionStore.Scope baseTerrain)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                this.baseTerrain = baseTerrain ?? throw new ArgumentNullException(
                    nameof(baseTerrain));
            }

            public BasinComponent Get(BasinComponentId id)
            {
                if (owner == null)
                {
                    throw new ObjectDisposedException(nameof(Scope));
                }

                if (!acquired.TryGetValue(id, out var entry))
                {
                    var store = owner;
                    entry = store.Acquire(
                        id,
                        () =>
                        {
                            var started = Stopwatch.GetTimestamp();
                            try
                            {
                                return BasinComponentBuilder.Build(
                                    store.hydrology.Settings,
                                    id,
                                    baseTerrain);
                            }
                            finally
                            {
                                store.hydrology.Metrics.RecordBasinComponent(
                                    Stopwatch.GetTimestamp() - started);
                            }
                        });
                    acquired.Add(id, entry);
                    try
                    {
                        return entry.Plan.Value;
                    }
                    catch
                    {
                        acquired.Remove(id);
                        owner.Release(id, entry);
                        throw;
                    }
                }

                return entry.Plan.Value;
            }

            public bool IsActive(BasinComponentId id)
            {
                if (activity.TryGetValue(id, out var result))
                {
                    return result;
                }

                if (!resolving.Add(id))
                {
                    throw new InvalidOperationException(
                        "Basin priority resolution must be acyclic.");
                }

                try
                {
                    var component = Get(id);
                    result = component.IsCandidate;
                    if (result)
                    {
                        foreach (var otherId in EnumerateConflictCandidates(component))
                        {
                            if (otherId.Equals(id))
                            {
                                continue;
                            }

                            var other = Get(otherId);
                            if (!other.IsCandidate
                                || !IsHigherPriority(other, component)
                                || !CanConflict(component, other)
                                || !IsActive(otherId))
                            {
                                continue;
                            }

                            result = false;
                            break;
                        }
                    }

                    activity.Add(id, result);
                    return result;
                }
                finally
                {
                    resolving.Remove(id);
                }
            }

            public List<BasinComponent> CollectActiveAffecting(
                int minimumX,
                int minimumZ,
                int maximumX,
                int maximumZ)
            {
                if (owner == null)
                {
                    throw new ObjectDisposedException(nameof(Scope));
                }

                var settings = owner.hydrology.Settings;
                var reach = checked(settings.Hydrology.Basins.MaximumReachCells
                    + settings.Hydrology.Basins.ShoreTransitionCells);
                var spacing = settings.Hydrology.Map.BasinSeedSpacingCells;
                var minimumSeedX = FloorDivide(
                    checked(minimumX - reach - (spacing - 1)), spacing);
                var maximumSeedX = FloorDivide(checked(maximumX + reach), spacing);
                var minimumSeedZ = FloorDivide(
                    checked(minimumZ - reach - (spacing - 1)), spacing);
                var maximumSeedZ = FloorDivide(checked(maximumZ + reach), spacing);
                var result = new List<BasinComponent>();
                for (var seedZ = minimumSeedZ; seedZ <= maximumSeedZ; seedZ++)
                for (var seedX = minimumSeedX; seedX <= maximumSeedX; seedX++)
                {
                    AddIfActive(result, new BasinComponentId(
                        WaterType.Lake, seedX, seedZ));
                    AddIfActive(result, new BasinComponentId(
                        WaterType.Pond, seedX, seedZ));
                }

                result.Sort((left, right) => left.Id.CompareTo(right.Id));
                return result;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }

                foreach (var pair in acquired)
                {
                    owner.Release(pair.Key, pair.Value);
                }

                activity.Clear();
                resolving.Clear();
                acquired = null;
                owner = null;
            }

            private void AddIfActive(
                List<BasinComponent> result,
                BasinComponentId id)
            {
                if (IsActive(id))
                {
                    result.Add(Get(id));
                }
            }

            private IEnumerable<BasinComponentId> EnumerateConflictCandidates(
                BasinComponent component)
            {
                var settings = owner.hydrology.Settings;
                var spacing = settings.Hydrology.Map.BasinSeedSpacingCells;
                var distance = checked(settings.Hydrology.Basins.MaximumReachCells * 2
                    + settings.Hydrology.Basins.MinimumSeparationCells);
                var gridRadius = checked((distance + spacing - 1) / spacing);
                for (var seedZ = component.Id.SeedGridZ - gridRadius;
                     seedZ <= component.Id.SeedGridZ + gridRadius;
                     seedZ++)
                for (var seedX = component.Id.SeedGridX - gridRadius;
                     seedX <= component.Id.SeedGridX + gridRadius;
                     seedX++)
                {
                    yield return new BasinComponentId(WaterType.Lake, seedX, seedZ);
                    yield return new BasinComponentId(WaterType.Pond, seedX, seedZ);
                }
            }

            private bool CanConflict(BasinComponent first, BasinComponent second)
            {
                var clearance = owner.hydrology.Settings.Hydrology.Basins
                    .MinimumSeparationCells;
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
                        if (Math.Max(Math.Abs(offsetX), Math.Abs(offsetZ))
                            <= clearance
                            && second.Contains(
                                checked(cell.WorldX + offsetX),
                                checked(cell.WorldZ + offsetZ)))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private static bool IsHigherPriority(
                BasinComponent candidate,
                BasinComponent other) => candidate.Priority > other.Priority
                    || candidate.Priority == other.Priority
                    && candidate.Id.CompareTo(other.Id) < 0;

            private static int FloorDivide(int value, int divisor)
            {
                var quotient = value / divisor;
                return value % divisor < 0 ? quotient - 1 : quotient;
            }
        }

        private sealed class Entry
        {
            public Entry(Func<BasinComponent> create)
            {
                Plan = new Lazy<BasinComponent>(
                    create ?? throw new ArgumentNullException(nameof(create)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public Lazy<BasinComponent> Plan { get; }
            public int ScopeCount;
            public bool Evicted;
        }
    }

    internal static class BasinComponentBuilder
    {
        private static readonly (int x, int z, float cost)[] GrowthNeighbors =
        {
            (-1, -1, 1.41421356f), (0, -1, 1f), (1, -1, 1.41421356f),
            (-1, 0, 1f),                       (1, 0, 1f),
            (-1, 1, 1.41421356f),  (0, 1, 1f),  (1, 1, 1.41421356f)
        };

        private static readonly (int x, int z)[] CardinalNeighbors =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1)
        };

        public static BasinComponent Build(
            WorldSettingsData settings,
            BasinComponentId id,
            BaseTerrainRegionStore.Scope baseTerrain)
        {
            var profile = id.Type == WaterType.Lake
                ? settings.Hydrology.Basins.Lake
                : settings.Hydrology.Basins.Pond;
            var typeName = id.Type == WaterType.Lake ? "Lake" : "Pond";
            var spacing = settings.Hydrology.Map.BasinSeedSpacingCells;
            var seedWorldX = checked(id.SeedGridX * spacing + Math.Min(
                spacing - 1,
                (int)(DeterministicNoise.Value01(
                    id.SeedGridX,
                    id.SeedGridZ,
                    Seed(settings.Seed, $"Hydrology.Topology.Basin.{typeName}.Position"))
                    * spacing)));
            var seedWorldZ = checked(id.SeedGridZ * spacing + Math.Min(
                spacing - 1,
                (int)(DeterministicNoise.Value01(
                    id.SeedGridZ,
                    id.SeedGridX,
                    Seed(settings.Seed, $"Hydrology.Topology.Basin.{typeName}.Position"))
                    * spacing)));
            var priority = DeterministicNoise.Value01(
                id.SeedGridX,
                id.SeedGridZ,
                Seed(settings.Seed, $"Hydrology.Topology.Basin.{typeName}.Priority"));
            if (DeterministicNoise.Value01(
                    id.SeedGridX,
                    id.SeedGridZ,
                    Seed(settings.Seed, $"Hydrology.Topology.Basin.{typeName}.Activation"))
                >= profile.Occurrence
                || baseTerrain.Sample(seedWorldX, seedWorldZ).Surface.HasSeaWater)
            {
                return Inactive(id, priority, seedWorldX, seedWorldZ);
            }

            var reach = settings.Hydrology.Basins.MaximumReachCells;
            var size = checked(reach * 2 + 1);
            var originX = checked(seedWorldX - reach);
            var originZ = checked(seedWorldZ - reach);
            var samples = new BaseTerrainSample[checked(size * size)];
            var potential = new float[samples.Length];
            var potentialSeed = Seed(settings.Seed,
                "Hydrology.Topology.Basin.Potential");
            for (var localZ = 0; localZ < size; localZ++)
            for (var localX = 0; localX < size; localX++)
            {
                var worldX = checked(originX + localX);
                var worldZ = checked(originZ + localZ);
                var cell = localX + size * localZ;
                samples[cell] = baseTerrain.Sample(worldX, worldZ);
                var value = WorldNoiseFieldSampler.Sample2D(
                    worldX,
                    worldZ,
                    settings.Hydrology.Map.BasinPotentialField,
                    potentialSeed);
                potential[cell] = settings.Hydrology.Map.BasinPotentialResponse
                    .Evaluate(ToUnit(value,
                        settings.Hydrology.Map.BasinPotentialField.Mode));
            }

            var targetArea = (int)Math.Round(
                ResolveRange(profile.AreaCells,
                    DeterministicNoise.Value01(
                        seedWorldX,
                        seedWorldZ,
                        Seed(settings.Seed,
                            $"Hydrology.Topology.Basin.{typeName}.Area"))),
                MidpointRounding.AwayFromZero);
            var maximumDepth = ResolveRange(profile.MaximumDepthUnits,
                DeterministicNoise.Value01(
                    seedWorldX,
                    seedWorldZ,
                    Seed(settings.Seed,
                        $"Hydrology.Topology.Basin.{typeName}.Depth")));
            var footprint = BuildFootprint(
                settings,
                samples,
                potential,
                size,
                reach,
                targetArea);
            if (footprint == null)
            {
                return Inactive(id, priority, seedWorldX, seedWorldZ);
            }

            var boundary = FindBoundary(footprint, size);
            var waterTop = SelectWaterTop(
                footprint,
                boundary,
                samples,
                settings.Hydrology.Basins);
            var interiorDistance = BuildInteriorDistance(
                footprint,
                boundary,
                size,
                out var maximumInteriorDistance);
            var cells = new List<BasinCell>(footprint.Count);
            var boundaryCells = new List<BasinCell>(boundary.Count);
            var boundarySet = new HashSet<int>(boundary);
            for (var index = 0; index < footprint.Count; index++)
            {
                var localCell = footprint[index];
                var localX = localCell % size;
                var localZ = localCell / size;
                var progress = maximumInteriorDistance > 0
                    ? interiorDistance[localCell] / (float)maximumInteriorDistance
                    : 1f;
                var cell = new BasinCell(
                    checked(originX + localX),
                    checked(originZ + localZ),
                    progress);
                cells.Add(cell);
                if (boundarySet.Contains(localCell))
                {
                    boundaryCells.Add(cell);
                }
            }

            var amplitude = ResolveRange(
                settings.Hydrology.Basins.BedAmplitudeUnits,
                DeterministicNoise.Value01(
                    seedWorldX,
                    seedWorldZ,
                    Seed(settings.Seed, "Hydrology.Topology.Basin.BedAmplitude")));
            return new BasinComponent(
                id,
                true,
                priority,
                seedWorldX,
                seedWorldZ,
                maximumDepth,
                waterTop,
                amplitude,
                cells,
                boundaryCells);
        }

        private static BasinComponent Inactive(
            BasinComponentId id,
            float priority,
            int seedWorldX,
            int seedWorldZ) => new(
                id,
                false,
                priority,
                seedWorldX,
                seedWorldZ,
                0f,
                0,
                0f,
                Array.Empty<BasinCell>(),
                Array.Empty<BasinCell>());

        private static List<int> BuildFootprint(
            WorldSettingsData settings,
            IReadOnlyList<BaseTerrainSample> samples,
            IReadOnlyList<float> potential,
            int size,
            int reach,
            int targetArea)
        {
            var seedCell = reach + size * reach;
            if (samples[seedCell].Surface.HasSeaWater)
            {
                return null;
            }

            var distances = new Dictionary<int, float>();
            var footprint = new List<int>(targetArea);
            var frontier = new BasinCellCostHeap();
            distances.Add(seedCell, 0f);
            frontier.Push(seedCell, 0f);
            while (frontier.Count > 0 && footprint.Count < targetArea)
            {
                var current = frontier.Pop();
                if (!distances.TryGetValue(current.Cell, out var known)
                    || current.Cost != known)
                {
                    continue;
                }

                footprint.Add(current.Cell);
                var currentX = current.Cell % size;
                var currentZ = current.Cell / size;
                for (var direction = 0; direction < GrowthNeighbors.Length; direction++)
                {
                    var neighbor = GrowthNeighbors[direction];
                    var nextX = currentX + neighbor.x;
                    var nextZ = currentZ + neighbor.z;
                    if ((uint)nextX >= size || (uint)nextZ >= size)
                    {
                        continue;
                    }

                    var next = nextX + size * nextZ;
                    if (samples[next].Surface.HasSeaWater)
                    {
                        continue;
                    }

                    var terrainDelta = MathF.Abs(
                        samples[next].Surface.SurfaceUnits
                        - samples[current.Cell].Surface.SurfaceUnits)
                        / WorldGrid.HeightStepsPerCell;
                    var slope = terrainDelta / neighbor.cost;
                    var cost = current.Cost + neighbor.cost + neighbor.cost * (
                        potential[next] * settings.Hydrology.Map.BasinPotentialCost
                        + terrainDelta * settings.Hydrology.Map.TerrainDeformationCost
                        + slope * settings.Hydrology.Map.SlopeCost);
                    if (distances.TryGetValue(next, out var previous)
                        && previous <= cost)
                    {
                        continue;
                    }

                    distances[next] = cost;
                    frontier.Push(next, cost);
                }
            }

            return footprint.Count == targetArea ? footprint : null;
        }

        private static List<int> FindBoundary(IReadOnlyList<int> footprint, int size)
        {
            var membership = new HashSet<int>(footprint);
            var result = new List<int>();
            for (var index = 0; index < footprint.Count; index++)
            {
                var cell = footprint[index];
                var x = cell % size;
                var z = cell / size;
                for (var direction = 0; direction < CardinalNeighbors.Length; direction++)
                {
                    var neighbor = CardinalNeighbors[direction];
                    var nextX = x + neighbor.x;
                    var nextZ = z + neighbor.z;
                    if ((uint)nextX >= size || (uint)nextZ >= size
                        || !membership.Contains(nextX + size * nextZ))
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
            int size,
            out int maximumDistance)
        {
            var membership = new HashSet<int>(footprint);
            var distance = new Dictionary<int, int>(footprint.Count);
            var queue = new Queue<int>();
            for (var index = 0; index < boundary.Count; index++)
            {
                distance.Add(boundary[index], 0);
                queue.Enqueue(boundary[index]);
            }

            maximumDistance = 0;
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                var current = distance[cell];
                var x = cell % size;
                var z = cell / size;
                for (var direction = 0; direction < CardinalNeighbors.Length; direction++)
                {
                    var neighbor = CardinalNeighbors[direction];
                    var nextX = x + neighbor.x;
                    var nextZ = z + neighbor.z;
                    if ((uint)nextX >= size || (uint)nextZ >= size)
                    {
                        continue;
                    }

                    var next = nextX + size * nextZ;
                    if (!membership.Contains(next) || distance.ContainsKey(next))
                    {
                        continue;
                    }

                    var nextDistance = current + 1;
                    distance.Add(next, nextDistance);
                    maximumDistance = Math.Max(maximumDistance, nextDistance);
                    queue.Enqueue(next);
                }
            }

            return distance;
        }

        private static float ResolveRange(
            in WorldSeededRangeSettingsData range,
            float amount) => range.Minimum + (range.Maximum - range.Minimum) * amount;

        private static int Seed(int worldSeed, string channel) =>
            DeterministicNoise.DeriveSeed(worldSeed, channel);

        private static float ToUnit(float value, WorldNoiseMode mode)
        {
            var unit = mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge
                ? (value + 1f) * 0.5f
                : value;
            return Math.Clamp(unit, 0f, 1f);
        }

        private readonly struct BasinCellCost
        {
            public BasinCellCost(int cell, float cost)
            {
                Cell = cell;
                Cost = cost;
            }

            public int Cell { get; }
            public float Cost { get; }
        }

        private sealed class BasinCellCostHeap
        {
            private readonly List<BasinCellCost> entries = new();
            public int Count => entries.Count;

            public void Push(int cell, float cost)
            {
                var entry = new BasinCellCost(cell, cost);
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

            public BasinCellCost Pop()
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

            private static int Compare(in BasinCellCost left, in BasinCellCost right)
            {
                var cost = left.Cost.CompareTo(right.Cost);
                return cost != 0 ? cost : left.Cell.CompareTo(right.Cell);
            }
        }
    }

    /// <summary>
    /// Projects independently resolved Basin Components and base-terrain facts
    /// into one topology core.  It never allocates a halo raster or runs Basin
    /// growth; those responsibilities belong to their coordinate owners.
    /// </summary>
    internal static class TopologySpatialBuilder
    {
        private static readonly (int x, int z)[] CardinalNeighbors =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1)
        };

        public static TopologyRegion Build(
            WorldHydrology hydrology,
            TopologyRegionKey key,
            BaseTerrainRegionStore.Scope baseTerrain,
            BasinComponentStore.Scope basins)
        {
            if (hydrology == null || baseTerrain == null || basins == null)
            {
                throw new ArgumentNullException(nameof(hydrology));
            }

            var settings = hydrology.Settings;
            var size = settings.Hydrology.Map.PlanningRegionSizeCells;
            var originX = checked(key.X * size);
            var originZ = checked(key.Z * size);
            var cells = new HydrologyCellPlan[checked(size * size)];
            for (var localZ = 0; localZ < size; localZ++)
            for (var localX = 0; localX < size; localX++)
            {
                var sample = baseTerrain.Sample(
                    checked(originX + localX),
                    checked(originZ + localZ));
                cells[localX + size * localZ] = BuildBaseCell(settings, sample);
            }

            var endpoints = new List<HydrologyPlanEndpoint>();
            var components = basins.CollectActiveAffecting(
                originX,
                originZ,
                checked(originX + size - 1),
                checked(originZ + size - 1));
            for (var index = 0; index < components.Count; index++)
            {
                WriteBasinWater(
                    settings,
                    components[index],
                    originX,
                    originZ,
                    size,
                    cells);
            }

            for (var index = 0; index < components.Count; index++)
            {
                WriteShoreTransition(
                    settings,
                    components[index],
                    baseTerrain,
                    originX,
                    originZ,
                    size,
                    cells);
                AddBasinEndpoint(
                    components[index],
                    originX,
                    originZ,
                    size,
                    cells,
                    endpoints);
            }

            AddSeaEndpoints(
                settings,
                baseTerrain,
                originX,
                originZ,
                size,
                endpoints);
            endpoints.Sort((left, right) => left.Id.CompareTo(right.Id));
            return new TopologyRegion(key, size, cells, endpoints);
        }

        private static HydrologyCellPlan BuildBaseCell(
            WorldSettingsData settings,
            in BaseTerrainSample sample)
        {
            var terrain = ToHeightUnits(settings, sample.Surface.SurfaceUnits);
            if (!sample.Surface.HasSeaWater)
            {
                return new HydrologyCellPlan(
                    terrain,
                    0,
                    WaterType.None,
                    default,
                    0f,
                    0f);
            }

            var waterTop = Math.Clamp(
                sample.Surface.WaterTopUnits,
                0,
                MaximumHeightUnits(settings));
            return new HydrologyCellPlan(
                terrain,
                waterTop,
                waterTop > terrain ? WaterType.Sea : WaterType.None,
                default,
                1f,
                sample.Terrain.PatternDepthProgress);
        }

        private static void WriteBasinWater(
            WorldSettingsData settings,
            BasinComponent component,
            int originX,
            int originZ,
            int size,
            HydrologyCellPlan[] cells)
        {
            for (var index = 0; index < component.Footprint.Count; index++)
            {
                var basinCell = component.Footprint[index];
                if (!TryGetCoreIndex(
                        basinCell.WorldX,
                        basinCell.WorldZ,
                        originX,
                        originZ,
                        size,
                        out var coreIndex))
                {
                    continue;
                }

                if (cells[coreIndex].HasWater)
                {
                    continue;
                }

                var target = ResolveBasinFloor(
                    settings,
                    component,
                    basinCell);
                cells[coreIndex] = new HydrologyCellPlan(
                    target,
                    component.WaterTopUnits,
                    target < component.WaterTopUnits ? component.Id.Type : WaterType.None,
                    component.Id,
                    1f,
                    basinCell.InteriorProgress);
            }
        }

        private static int ResolveBasinFloor(
            WorldSettingsData settings,
            BasinComponent component,
            in BasinCell cell)
        {
            var depthProgress = settings.Hydrology.Basins.DepthByInterior
                .Evaluate(cell.InteriorProgress);
            var bed = ToSigned(WorldNoiseFieldSampler.Sample2D(
                    cell.WorldX,
                    cell.WorldZ,
                    settings.Hydrology.Basins.BedField,
                    DeterministicNoise.DeriveSeed(
                        settings.Seed,
                        "Hydrology.Topology.Basin.Bed")),
                settings.Hydrology.Basins.BedField.Mode)
                * component.BedAmplitudeUnits * depthProgress;
            return ToHeightUnits(
                settings,
                component.WaterTopUnits
                - component.MaximumDepthUnits * depthProgress
                + bed);
        }

        private static void WriteShoreTransition(
            WorldSettingsData settings,
            BasinComponent component,
            BaseTerrainRegionStore.Scope baseTerrain,
            int originX,
            int originZ,
            int size,
            HydrologyCellPlan[] cells)
        {
            var maximumDistance = settings.Hydrology.Basins.ShoreTransitionCells;
            if (maximumDistance <= 0)
            {
                return;
            }

            var distance = new Dictionary<long, int>();
            var queue = new Queue<BasinCell>();
            for (var index = 0; index < component.Boundary.Count; index++)
            {
                var cell = component.Boundary[index];
                distance.Add(BasinComponent.CellKey(cell.WorldX, cell.WorldZ), 0);
                queue.Enqueue(cell);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentKey = BasinComponent.CellKey(current.WorldX, current.WorldZ);
                var currentDistance = distance[currentKey];
                if (currentDistance >= maximumDistance)
                {
                    continue;
                }

                for (var direction = 0; direction < CardinalNeighbors.Length; direction++)
                {
                    var offset = CardinalNeighbors[direction];
                    var worldX = checked(current.WorldX + offset.x);
                    var worldZ = checked(current.WorldZ + offset.z);
                    var nextKey = BasinComponent.CellKey(worldX, worldZ);
                    if (component.Contains(worldX, worldZ)
                        || distance.ContainsKey(nextKey))
                    {
                        continue;
                    }

                    var nextDistance = currentDistance + 1;
                    distance.Add(nextKey, nextDistance);
                    queue.Enqueue(new BasinCell(worldX, worldZ, 0f));
                    if (!TryGetCoreIndex(
                            worldX,
                            worldZ,
                            originX,
                            originZ,
                            size,
                            out var coreIndex))
                    {
                        continue;
                    }

                    var membership = settings.Hydrology.Basins.ShoreTransition
                        .Evaluate(1f - nextDistance / (float)maximumDistance);
                    var currentPlan = cells[coreIndex];
                    if (currentPlan.HasWater
                        || currentPlan.BasinComponent.IsValid
                        && (currentPlan.Membership > membership
                            || currentPlan.Membership == membership
                            && currentPlan.BasinComponent.CompareTo(component.Id) <= 0))
                    {
                        continue;
                    }

                    var sample = baseTerrain.Sample(worldX, worldZ);
                    var target = ToHeightUnits(
                        settings,
                        sample.Surface.SurfaceUnits
                        + (component.WaterTopUnits - sample.Surface.SurfaceUnits)
                            * membership);
                    cells[coreIndex] = new HydrologyCellPlan(
                        target,
                        0,
                        WaterType.None,
                        component.Id,
                        membership,
                        0f);
                }
            }
        }

        private static void AddBasinEndpoint(
            BasinComponent component,
            int originX,
            int originZ,
            int size,
            IReadOnlyList<HydrologyCellPlan> cells,
            List<HydrologyPlanEndpoint> endpoints)
        {
            if (!TryGetCoreIndex(
                    component.SeedWorldX,
                    component.SeedWorldZ,
                    originX,
                    originZ,
                    size,
                    out _))
            {
                return;
            }

            var selected = -1;
            for (var index = 0; index < component.Footprint.Count; index++)
            {
                var cell = component.Footprint[index];
                if (!TryGetCoreIndex(
                        cell.WorldX,
                        cell.WorldZ,
                        originX,
                        originZ,
                        size,
                        out var coreIndex)
                    || cells[coreIndex].WaterType != component.Id.Type
                    || selected >= 0 && coreIndex >= selected)
                {
                    continue;
                }

                selected = coreIndex;
            }

            if (selected < 0)
            {
                return;
            }

            var localX = selected % size;
            var localZ = selected / size;
            var kind = component.Id.Type == WaterType.Lake
                ? HydrologyPlanEndpointKind.Lake
                : HydrologyPlanEndpointKind.Pond;
            endpoints.Add(new HydrologyPlanEndpoint(
                new HydrologyPlanEndpointId(
                    kind,
                    checked(originX + localX),
                    checked(originZ + localZ),
                    component.Id),
                component.WaterTopUnits));
        }

        private static void AddSeaEndpoints(
            WorldSettingsData settings,
            BaseTerrainRegionStore.Scope baseTerrain,
            int originX,
            int originZ,
            int size,
            List<HydrologyPlanEndpoint> endpoints)
        {
            var spacing = settings.Hydrology.Map.RouteSampleSpacingCells;
            for (var localZ = 0; localZ < size; localZ++)
            for (var localX = 0; localX < size; localX++)
            {
                var worldX = checked(originX + localX);
                var worldZ = checked(originZ + localZ);
                if (FloorDivide(worldX, spacing) * spacing != worldX
                    || FloorDivide(worldZ, spacing) * spacing != worldZ)
                {
                    continue;
                }

                var sample = baseTerrain.Sample(worldX, worldZ);
                if (!sample.Surface.HasSeaWater
                    || baseTerrain.Sample(checked(worldX - spacing), worldZ)
                        .Surface.HasSeaWater
                    && baseTerrain.Sample(checked(worldX + spacing), worldZ)
                        .Surface.HasSeaWater
                    && baseTerrain.Sample(worldX, checked(worldZ - spacing))
                        .Surface.HasSeaWater
                    && baseTerrain.Sample(worldX, checked(worldZ + spacing))
                        .Surface.HasSeaWater)
                {
                    continue;
                }

                endpoints.Add(new HydrologyPlanEndpoint(
                    new HydrologyPlanEndpointId(
                        HydrologyPlanEndpointKind.Sea,
                        worldX,
                        worldZ,
                        default),
                    sample.Surface.WaterTopUnits));
            }
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

        private static int ToHeightUnits(WorldSettingsData settings, float value) =>
            Math.Clamp(
                (int)MathF.Round(value, MidpointRounding.AwayFromZero),
                0,
                MaximumHeightUnits(settings));

        private static int MaximumHeightUnits(WorldSettingsData settings) =>
            checked(settings.WorldHeight * WorldGrid.HeightStepsPerCell);

        private static float ToSigned(float value, WorldNoiseMode mode)
        {
            var unit = mode is WorldNoiseMode.Signed or WorldNoiseMode.SignedRidge
                ? (value + 1f) * 0.5f
                : value;
            return Math.Clamp(unit, 0f, 1f) * 2f - 1f;
        }

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }
    }
}
