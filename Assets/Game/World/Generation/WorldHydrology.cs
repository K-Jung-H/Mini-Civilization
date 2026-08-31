using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation
{
    internal enum HydrologyPlanEndpointKind : byte
    {
        Natural,
        Pond,
        Lake,
        Sea
    }

    internal readonly struct BasinComponentId :
        IEquatable<BasinComponentId>, IComparable<BasinComponentId>
    {
        public BasinComponentId(
            WaterType type,
            int seedGridX,
            int seedGridZ)
        {
            if (type is not (WaterType.Lake or WaterType.Pond))
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            Type = type;
            SeedGridX = seedGridX;
            SeedGridZ = seedGridZ;
        }

        public WaterType Type { get; }
        public int SeedGridX { get; }
        public int SeedGridZ { get; }
        public bool IsValid => Type is WaterType.Lake or WaterType.Pond;

        public int CompareTo(BasinComponentId other)
        {
            var type = Type.CompareTo(other.Type);
            if (type != 0)
            {
                return type;
            }

            var x = SeedGridX.CompareTo(other.SeedGridX);
            return x != 0 ? x : SeedGridZ.CompareTo(other.SeedGridZ);
        }

        public bool Equals(BasinComponentId other) => Type == other.Type
            && SeedGridX == other.SeedGridX
            && SeedGridZ == other.SeedGridZ;

        public override bool Equals(object obj) => obj is BasinComponentId other
            && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            (byte)Type,
            SeedGridX,
            SeedGridZ);
    }

    internal readonly struct HydrologyPlanEndpointId :
        IEquatable<HydrologyPlanEndpointId>,
        IComparable<HydrologyPlanEndpointId>
    {
        public HydrologyPlanEndpointId(
            HydrologyPlanEndpointKind kind,
            int worldX,
            int worldZ,
            in BasinComponentId basinComponent)
        {
            if (kind is HydrologyPlanEndpointKind.Lake
                or HydrologyPlanEndpointKind.Pond)
            {
                if (!basinComponent.IsValid
                    || basinComponent.Type != ToWaterType(kind))
                {
                    throw new ArgumentException(
                        "A Basin Endpoint must identify its Basin component.",
                        nameof(basinComponent));
                }
            }
            else if (basinComponent.IsValid)
            {
                throw new ArgumentException(
                    "Only Lake and Pond Endpoints can identify a Basin component.",
                    nameof(basinComponent));
            }

            Kind = kind;
            WorldX = worldX;
            WorldZ = worldZ;
            BasinComponent = basinComponent;
        }

        public HydrologyPlanEndpointKind Kind { get; }
        public int WorldX { get; }
        public int WorldZ { get; }
        public BasinComponentId BasinComponent { get; }

        public int CompareTo(HydrologyPlanEndpointId other)
        {
            var kind = Kind.CompareTo(other.Kind);
            if (kind != 0)
            {
                return kind;
            }

            var component = BasinComponent.CompareTo(other.BasinComponent);
            if (component != 0)
            {
                return component;
            }

            var x = WorldX.CompareTo(other.WorldX);
            return x != 0 ? x : WorldZ.CompareTo(other.WorldZ);
        }

        public bool Equals(HydrologyPlanEndpointId other) => Kind == other.Kind
            && WorldX == other.WorldX
            && WorldZ == other.WorldZ
            && BasinComponent.Equals(other.BasinComponent);

        public override bool Equals(object obj) =>
            obj is HydrologyPlanEndpointId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            (byte)Kind,
            WorldX,
            WorldZ,
            BasinComponent);

        private static WaterType ToWaterType(
            HydrologyPlanEndpointKind kind) => kind switch
            {
                HydrologyPlanEndpointKind.Lake => WaterType.Lake,
                HydrologyPlanEndpointKind.Pond => WaterType.Pond,
                _ => WaterType.None
            };
    }

    internal readonly struct HydrologyGraphEdgeId :
        IEquatable<HydrologyGraphEdgeId>,
        IComparable<HydrologyGraphEdgeId>
    {
        public HydrologyGraphEdgeId(
            in HydrologyPlanEndpointId first,
            in HydrologyPlanEndpointId second)
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

        public HydrologyPlanEndpointId First { get; }
        public HydrologyPlanEndpointId Second { get; }

        public bool Equals(HydrologyGraphEdgeId other) =>
            First.Equals(other.First) && Second.Equals(other.Second);

        public int CompareTo(HydrologyGraphEdgeId other)
        {
            var first = First.CompareTo(other.First);
            return first != 0 ? first : Second.CompareTo(other.Second);
        }

        public override bool Equals(object obj) =>
            obj is HydrologyGraphEdgeId other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(First, Second);
    }

    internal readonly struct HydrologyPlanEndpoint
    {
        public HydrologyPlanEndpoint(
            in HydrologyPlanEndpointId id,
            int waterTopUnits)
        {
            if (waterTopUnits < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(waterTopUnits));
            }

            Id = id;
            WaterTopUnits = waterTopUnits;
        }

        public HydrologyPlanEndpointId Id { get; }
        public HydrologyPlanEndpointKind Kind => Id.Kind;
        public int WorldX => Id.WorldX;
        public int WorldZ => Id.WorldZ;
        public BasinComponentId BasinComponent => Id.BasinComponent;
        public int WaterTopUnits { get; }
    }

    internal readonly struct HydrologyCellPlan
    {
        public HydrologyCellPlan(
            int targetTerrainSurfaceUnits,
            int waterTopUnits,
            WaterType waterType,
            in BasinComponentId basinComponent,
            float membership,
            float interiorProgress,
            HydrologyGraphEdgeId? riverEdgeId = null)
        {
            if (targetTerrainSurfaceUnits < 0 || waterTopUnits < 0
                || !float.IsFinite(membership)
                || !float.IsFinite(interiorProgress))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetTerrainSurfaceUnits));
            }

            var hasWater = waterTopUnits > targetTerrainSurfaceUnits;
            if (hasWater != (waterType != WaterType.None))
            {
                throw new ArgumentException(
                    "Water height and WaterType must describe the same cell fact.");
            }

            if (waterType is WaterType.Lake or WaterType.Pond)
            {
                if (!basinComponent.IsValid || basinComponent.Type != waterType)
                {
                    throw new ArgumentException(
                        "Basin water must identify the matching Basin component.",
                        nameof(basinComponent));
                }
            }
            else if (waterType == WaterType.Sea && basinComponent.IsValid)
            {
                throw new ArgumentException(
                    "Sea cells cannot identify a Basin component.",
                    nameof(basinComponent));
            }

            TargetTerrainSurfaceUnits = targetTerrainSurfaceUnits;
            WaterTopUnits = waterTopUnits;
            WaterType = waterType;
            BasinComponent = basinComponent;
            Membership = Math.Clamp(membership, 0f, 1f);
            InteriorProgress = Math.Clamp(interiorProgress, 0f, 1f);
            RiverEdgeId = riverEdgeId;
        }

        public int TargetTerrainSurfaceUnits { get; }
        public int WaterTopUnits { get; }
        public WaterType WaterType { get; }
        public WaterRole WaterRole => HasWater
            ? WaterRole.Source
            : WaterRole.None;
        public BasinComponentId BasinComponent { get; }
        public float Membership { get; }
        public float InteriorProgress { get; }
        public HydrologyGraphEdgeId? RiverEdgeId { get; }
        public bool HasWater => WaterTopUnits > TargetTerrainSurfaceUnits;
        public bool IsBasinProtected => BasinComponent.IsValid;
    }

    internal static class HydrologyPlanIdentity
    {
        // WorldPatternResult keeps this value only for the pattern-map color view.
        // It is derived from the actual component or EdgeId, never allocated by
        // request order or cache lifetime.
        public static int ToDebugComponentId(in HydrologyCellPlan plan)
        {
            if (plan.BasinComponent.IsValid)
            {
                return unchecked((int)DeterministicNoise.Hash(
                    plan.BasinComponent.SeedGridX,
                    plan.BasinComponent.SeedGridZ,
                    (int)plan.BasinComponent.Type));
            }

            return plan.RiverEdgeId.HasValue
                ? unchecked((int)DeterministicNoise.Hash(
                    EndpointHash(plan.RiverEdgeId.Value.First),
                    EndpointHash(plan.RiverEdgeId.Value.Second),
                    (int)WaterType.River))
                : 0;
        }

        private static int EndpointHash(in HydrologyPlanEndpointId endpoint)
        {
            var hash = unchecked((int)DeterministicNoise.Hash(
                endpoint.WorldX,
                endpoint.WorldZ,
                (int)endpoint.Kind));
            return endpoint.BasinComponent.IsValid
                ? unchecked((int)DeterministicNoise.Hash(
                    hash,
                    endpoint.BasinComponent.SeedGridX,
                    endpoint.BasinComponent.SeedGridZ))
                : hash;
        }
    }

    internal readonly struct BaseTerrainSample
    {
        public BaseTerrainSample(
            in WorldFieldSample field,
            in WorldPatternResult terrain,
            in TerrainSurfaceSample surface)
        {
            Field = field;
            Terrain = terrain;
            Surface = surface;
        }

        public WorldFieldSample Field { get; }
        public WorldPatternResult Terrain { get; }
        public TerrainSurfaceSample Surface { get; }
        public bool HasSeaWater => Terrain.WaterType == WaterType.Sea;
        public int SeaWaterTopUnits => Terrain.WaterTopUnits;
    }

    internal readonly struct TopologyRegionKey :
        IEquatable<TopologyRegionKey>
    {
        public TopologyRegionKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public int X { get; }
        public int Z { get; }

        public bool Equals(TopologyRegionKey other) => X == other.X
            && Z == other.Z;

        public override bool Equals(object obj) => obj is TopologyRegionKey other
            && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z);
    }

    internal sealed class WorldHydrology
    {
        private readonly BaseTerrainField baseTerrain;
        private readonly BaseTerrainRegionStore baseTerrainRegions;
        private readonly BasinComponentStore basinComponents;
        private readonly TopologyRegionStore topologyRegions;
        private readonly EndpointCatalogRegionStore endpointCatalogRegions;
        private readonly RiverGraphStoreV2 riverGraph;

        public WorldHydrology(WorldSettingsData settings)
        {
            Settings = settings ?? throw new ArgumentNullException(
                nameof(settings));
            baseTerrain = new BaseTerrainField(settings);
            baseTerrainRegions = new BaseTerrainRegionStore(this);
            basinComponents = new BasinComponentStore(this);
            topologyRegions = new TopologyRegionStore(
                this,
                baseTerrainRegions,
                basinComponents);
            endpointCatalogRegions = new EndpointCatalogRegionStore(this);
            riverGraph = new RiverGraphStoreV2(this);
        }

        public WorldSettingsData Settings { get; }
        internal HydrologyGenerationMetrics Metrics { get; } = new();

        public HydrologyPlanScope BeginPlanScope() => new(
            topologyRegions,
            endpointCatalogRegions,
            riverGraph);

        public TopologyRegionKey GetTopologyRegionKey(int worldX, int worldZ)
        {
            var size = Settings.Hydrology.Map.PlanningRegionSizeCells;
            return new TopologyRegionKey(
                FloorDivide(worldX, size),
                FloorDivide(worldZ, size));
        }

        internal BaseTerrainSample SampleBaseTerrain(int worldX, int worldZ) =>
            baseTerrain.Sample(worldX, worldZ);

        internal HydrologyMetricsSnapshot CaptureMetrics() => Metrics.Capture();

        internal int CachedTopologyRegionCount => topologyRegions.Count;
        internal int CachedBaseTerrainRegionCount => baseTerrainRegions.Count;
        internal int CachedBasinComponentCount => basinComponents.Count;
        internal int CachedEndpointCatalogRegionCount => endpointCatalogRegions.Count;
        internal int CachedRiverGraphSpatialIndexCount => riverGraph.RegionCount;
        internal int CachedRiverEdgePlanCount => riverGraph.EdgePlanCount;

        internal HydrologyPlanScope BeginTopologyPlanScope() => new(
            topologyRegions,
            null,
            null);

        internal HydrologyPlanScope BeginEndpointCatalogPlanScope() => new(
            topologyRegions,
            endpointCatalogRegions,
            null);

        private static int FloorDivide(int value, int divisor)
        {
            var quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }
    }

    internal sealed class BaseTerrainField
    {
        private readonly WorldSettingsData settings;
        private readonly WorldNoiseRouter router;
        private readonly WorldDensityField density;

        public BaseTerrainField(WorldSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(
                nameof(settings));
            router = new WorldNoiseRouter(settings);
            density = new WorldDensityField(settings);
        }

        public BaseTerrainSample Sample(int worldX, int worldZ)
        {
            var field = router.Sample(worldX, worldZ);
            var terrain = WorldPatternResolver.Resolve(
                router,
                worldX,
                worldZ,
                field,
                settings,
                out _);
            var surface = TerrainSurfaceSampler.SampleResolved(
                density,
                settings,
                worldX,
                worldZ,
                field,
                terrain);
            return new BaseTerrainSample(field, terrain, surface);
        }
    }

    internal sealed class HydrologyPlanScope : IDisposable
    {
        private TopologyRegionStore topologyOwner;
        private EndpointCatalogRegionStore catalogOwner;
        private RiverGraphStoreV2 graphOwner;
        private Dictionary<TopologyRegionKey, TopologyRegionStore.Entry>
            acquiredTopology = new();
        private Dictionary<TopologyRegionKey, EndpointCatalogRegionStore.Entry>
            acquiredCatalog = new();
        private Dictionary<TopologyRegionKey, RiverGraphStoreV2.Entry>
            acquiredGraphs = new();

        internal HydrologyPlanScope(
            TopologyRegionStore topologyOwner,
            EndpointCatalogRegionStore catalogOwner,
            RiverGraphStoreV2 graphOwner)
        {
            this.topologyOwner = topologyOwner ?? throw new ArgumentNullException(
                nameof(topologyOwner));
            this.catalogOwner = catalogOwner;
            this.graphOwner = graphOwner;
        }

        public TopologyRegion GetTopologyRegion(TopologyRegionKey key)
        {
            if (topologyOwner == null)
            {
                throw new ObjectDisposedException(nameof(HydrologyPlanScope));
            }

            if (acquiredTopology.TryGetValue(key, out var existing))
            {
                return existing.Plan.Value;
            }

            var entry = topologyOwner.Acquire(key);
            acquiredTopology.Add(key, entry);
            try
            {
                return entry.Plan.Value;
            }
            catch
            {
                acquiredTopology.Remove(key);
                topologyOwner.Release(key, entry);
                throw;
            }
        }

        public RiverGraphSpatialIndexRegion GetRiverGraphSpatialIndexRegion(
            TopologyRegionKey key)
        {
            if (topologyOwner == null)
            {
                throw new ObjectDisposedException(nameof(HydrologyPlanScope));
            }

            if (graphOwner == null)
            {
                throw new InvalidOperationException(
                    "This Plan Scope does not own River Graph Regions.");
            }

            if (acquiredGraphs.TryGetValue(key, out var existing))
            {
                return existing.Plan.Value;
            }

            var entry = graphOwner.Acquire(key);
            acquiredGraphs.Add(key, entry);
            try
            {
                return entry.Plan.Value;
            }
            catch
            {
                acquiredGraphs.Remove(key);
                graphOwner.Release(key, entry);
                throw;
            }
        }

        public EndpointCatalogRegion GetEndpointCatalogRegion(
            TopologyRegionKey key)
        {
            if (topologyOwner == null)
            {
                throw new ObjectDisposedException(nameof(HydrologyPlanScope));
            }

            if (catalogOwner == null)
            {
                throw new InvalidOperationException(
                    "This Plan Scope does not own Endpoint Catalog Regions.");
            }

            if (acquiredCatalog.TryGetValue(key, out var existing))
            {
                return existing.Plan.Value;
            }

            var entry = catalogOwner.Acquire(key);
            acquiredCatalog.Add(key, entry);
            try
            {
                return entry.Plan.Value;
            }
            catch
            {
                acquiredCatalog.Remove(key);
                catalogOwner.Release(key, entry);
                throw;
            }
        }

        public void Dispose()
        {
            if (topologyOwner == null)
            {
                return;
            }

            foreach (var pair in acquiredGraphs)
            {
                graphOwner.Release(pair.Key, pair.Value);
            }

            foreach (var pair in acquiredCatalog)
            {
                catalogOwner.Release(pair.Key, pair.Value);
            }

            foreach (var pair in acquiredTopology)
            {
                topologyOwner.Release(pair.Key, pair.Value);
            }

            acquiredGraphs = null;
            acquiredCatalog = null;
            acquiredTopology = null;
            graphOwner = null;
            catalogOwner = null;
            topologyOwner = null;
        }
    }

    internal sealed class TopologyRegionStore
    {
        private readonly WorldHydrology hydrology;
        private readonly BaseTerrainRegionStore baseTerrainRegions;
        private readonly BasinComponentStore basinComponents;
        private readonly ConcurrentDictionary<TopologyRegionKey, Entry> entries =
            new();

        public TopologyRegionStore(
            WorldHydrology hydrology,
            BaseTerrainRegionStore baseTerrainRegions,
            BasinComponentStore basinComponents)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(
                nameof(hydrology));
            this.baseTerrainRegions = baseTerrainRegions
                ?? throw new ArgumentNullException(nameof(baseTerrainRegions));
            this.basinComponents = basinComponents
                ?? throw new ArgumentNullException(nameof(basinComponents));
        }

        public int Count => entries.Count;

        public HydrologyPlanScope BeginScope() => new(this, null, null);

        public Entry Acquire(TopologyRegionKey key)
        {
            while (true)
            {
                var entry = entries.GetOrAdd(
                    key,
                    CreateEntry);
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

        public void Release(TopologyRegionKey key, Entry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            lock (entry.Gate)
            {
                if (entry.ScopeCount <= 0)
                {
                    throw new InvalidOperationException(
                        "Topology Region Scope ownership is unbalanced.");
                }

                entry.ScopeCount--;
                if (entry.ScopeCount != 0)
                {
                    return;
                }

                entry.Evicted = true;
                entries.TryRemove(key, out _);
            }

            entry.ReleaseDependencies();
        }

        private Entry CreateEntry(TopologyRegionKey key)
        {
            Entry entry = null;
            entry = new Entry(
                baseTerrainRegions,
                basinComponents,
                () =>
                {
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    try
                    {
                        return TopologyRegionBuilder.Build(
                            hydrology,
                            key,
                            entry.BaseTerrainScope,
                            entry.BasinScope);
                    }
                    finally
                    {
                        hydrology.Metrics.RecordTopologyRegion(
                            System.Diagnostics.Stopwatch.GetTimestamp() - started);
                    }
                });
            return entry;
        }

        internal sealed class Entry
        {
            public Entry(
                BaseTerrainRegionStore baseTerrainRegions,
                BasinComponentStore basinComponents,
                Func<TopologyRegion> create)
            {
                BaseTerrainScope = baseTerrainRegions.BeginScope();
                BasinScope = basinComponents.BeginScope(BaseTerrainScope);
                Plan = new Lazy<TopologyRegion>(
                    create ?? throw new ArgumentNullException(nameof(create)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public BaseTerrainRegionStore.Scope BaseTerrainScope { get; }
            public BasinComponentStore.Scope BasinScope { get; }
            public Lazy<TopologyRegion> Plan { get; }
            public int ScopeCount;
            public bool Evicted;

            public void ReleaseDependencies()
            {
                BasinScope.Dispose();
                BaseTerrainScope.Dispose();
            }
        }
    }

    internal sealed class EndpointCatalogRegionStore
    {
        private readonly WorldHydrology hydrology;
        private readonly ConcurrentDictionary<TopologyRegionKey, Entry> entries =
            new();

        public EndpointCatalogRegionStore(WorldHydrology hydrology)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(
                nameof(hydrology));
        }

        public int Count => entries.Count;

        public Entry Acquire(TopologyRegionKey key)
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

        public void Release(TopologyRegionKey key, Entry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            lock (entry.Gate)
            {
                if (entry.ScopeCount <= 0)
                {
                    throw new InvalidOperationException(
                        "Endpoint Catalog Region Scope ownership is unbalanced.");
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

        private Entry CreateEntry(TopologyRegionKey key) => new(
            () =>
            {
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    return EndpointCatalogRegionBuilder.Build(hydrology, key);
                }
                finally
                {
                    hydrology.Metrics.RecordEndpointCatalog(
                        System.Diagnostics.Stopwatch.GetTimestamp() - started);
                }
            });

        internal sealed class Entry
        {
            public Entry(Func<EndpointCatalogRegion> create)
            {
                Plan = new Lazy<EndpointCatalogRegion>(
                    create,
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public Lazy<EndpointCatalogRegion> Plan { get; }
            public int ScopeCount;
            public bool Evicted;
        }
    }

    internal sealed class RiverGraphStore
    {
        private readonly WorldHydrology hydrology;
        private readonly RiverEdgePlanStore edgePlans;
        private readonly ConcurrentDictionary<TopologyRegionKey, Entry> entries =
            new();

        public RiverGraphStore(WorldHydrology hydrology)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(
                nameof(hydrology));
            edgePlans = new RiverEdgePlanStore(hydrology);
        }

        public int RegionCount => entries.Count;
        public int EdgePlanCount => edgePlans.Count;

        public Entry Acquire(TopologyRegionKey key)
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

        public void Release(TopologyRegionKey key, Entry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var releaseEdges = false;
            lock (entry.Gate)
            {
                if (entry.ScopeCount <= 0)
                {
                    throw new InvalidOperationException(
                        "River Graph Region Scope ownership is unbalanced.");
                }

                entry.ScopeCount--;
                if (entry.ScopeCount != 0)
                {
                    return;
                }

                entry.Evicted = true;
                entries.TryRemove(key, out _);
                releaseEdges = true;
            }

            if (releaseEdges)
            {
                entry.ReleaseEdges();
            }
        }

        private Entry CreateEntry(TopologyRegionKey key)
        {
            Entry entry = null;
            entry = new Entry(this,
                () => RiverGraphRegionBuilder.Build(hydrology, key, entry));
            return entry;
        }

        internal sealed class Entry
        {
            private readonly RiverGraphStore owner;
            private readonly Dictionary<HydrologyGraphEdgeId, RiverEdgePlanStore.Entry>
                acquiredEdges = new();

            public Entry(
                RiverGraphStore owner,
                Func<RiverGraphSpatialIndexRegion> create)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                Plan = new Lazy<RiverGraphSpatialIndexRegion>(
                    create ?? throw new ArgumentNullException(nameof(create)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public Lazy<RiverGraphSpatialIndexRegion> Plan { get; }
            public int ScopeCount;
            public bool Evicted;

            internal RiverEdgePlan GetEdgePlan(
                in RiverEdgePlanRequest request,
                RiverEdgePlan geometry)
            {
                if (geometry == null || !geometry.Id.Equals(request.Id))
                {
                    throw new ArgumentException(
                        "River Edge geometry must match its request.",
                        nameof(geometry));
                }

                if (acquiredEdges.TryGetValue(request.Id, out var existing))
                {
                    return existing.Plan.Value;
                }

                var entry = owner.edgePlans.Acquire(request, geometry);
                acquiredEdges.Add(request.Id, entry);
                try
                {
                    return entry.Plan.Value;
                }
                catch
                {
                    acquiredEdges.Remove(request.Id);
                    owner.edgePlans.Release(request.Id, entry);
                    throw;
                }
            }

            internal void ReleaseEdges()
            {
                foreach (var pair in acquiredEdges)
                {
                    owner.edgePlans.Release(pair.Key, pair.Value);
                }

                acquiredEdges.Clear();
            }
        }
    }

    internal sealed class RiverEdgePlanStore
    {
        private readonly WorldHydrology hydrology;
        private readonly ConcurrentDictionary<HydrologyGraphEdgeId, Entry> entries =
            new();

        public RiverEdgePlanStore(WorldHydrology hydrology)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(
                nameof(hydrology));
        }

        public int Count => entries.Count;

        public Entry Acquire(
            in RiverEdgePlanRequest request,
            RiverEdgePlan geometry)
        {
            if (geometry == null || !geometry.Id.Equals(request.Id))
            {
                throw new ArgumentException(
                    "River Edge geometry must match its request.",
                    nameof(geometry));
            }

            var requestCopy = request;
            while (true)
            {
                var entry = entries.GetOrAdd(request.Id, _ => new Entry(
                    () => RiverGraphRegionBuilder.BuildEdgePlan(
                        hydrology,
                        requestCopy,
                        geometry)));
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

        public void Release(HydrologyGraphEdgeId id, Entry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            lock (entry.Gate)
            {
                if (entry.ScopeCount <= 0)
                {
                    throw new InvalidOperationException(
                        "River Edge Plan Scope ownership is unbalanced.");
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

        internal sealed class Entry
        {
            public Entry(Func<RiverEdgePlan> create)
            {
                Plan = new Lazy<RiverEdgePlan>(
                    create ?? throw new ArgumentNullException(nameof(create)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public Lazy<RiverEdgePlan> Plan { get; }
            public int ScopeCount;
            public bool Evicted;
        }
    }
}
