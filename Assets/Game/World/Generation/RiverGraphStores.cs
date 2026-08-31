using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MiniCivilization.World.Generation
{
    /// <summary>
    /// The active graph is assembled from three independently owned facts:
    /// anchor-owned proposals, one geometry per EdgeId, and one final activity
    /// decision per EdgeId.  Activity never re-runs candidate selection or routing.
    /// </summary>
    internal sealed class RiverGraphStoreV2
    {
        private readonly WorldHydrology hydrology;
        private readonly RiverRoutePlanStore routes;
        private readonly RiverProposalRegionStore proposals;
        private readonly RiverEdgeActivityStore activities;
        private readonly ConcurrentDictionary<TopologyRegionKey, Entry> entries =
            new();

        public RiverGraphStoreV2(WorldHydrology hydrology)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
            routes = new RiverRoutePlanStore(this.hydrology);
            proposals = new RiverProposalRegionStore(this.hydrology, routes);
            activities = new RiverEdgeActivityStore(this.hydrology, proposals);
        }

        public int RegionCount => entries.Count;
        public int EdgePlanCount => routes.Count;

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
                        "River Graph Region Scope ownership is unbalanced.");
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
                routes,
                activities,
                () =>
                {
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        return RiverGraphRegionBuilder.BuildSpatialIndex(
                            hydrology,
                            key,
                            proposals,
                            entry);
                    }
                    finally
                    {
                        hydrology.Metrics.RecordRiverSpatialIndex(
                            Stopwatch.GetTimestamp() - started);
                    }
                });
            return entry;
        }

        internal sealed class Entry
        {
            private readonly RiverRoutePlanStore routes;
            private readonly RiverEdgeActivityStore activities;
            private readonly Dictionary<HydrologyGraphEdgeId, RiverRoutePlanStore.Entry>
                acquiredRoutes = new();
            private readonly Dictionary<HydrologyGraphEdgeId, RiverEdgeActivityStore.Entry>
                acquiredActivities = new();

            public Entry(
                RiverRoutePlanStore routes,
                RiverEdgeActivityStore activities,
                Func<RiverGraphSpatialIndexRegion> create)
            {
                this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
                this.activities = activities ?? throw new ArgumentNullException(nameof(activities));
                Plan = new Lazy<RiverGraphSpatialIndexRegion>(
                    create ?? throw new ArgumentNullException(nameof(create)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public Lazy<RiverGraphSpatialIndexRegion> Plan { get; }
            public int ScopeCount;
            public bool Evicted;

            internal RiverEdgePlan GetRoute(
                in RiverEdgePlanRequest request,
                RiverEdgePlan geometry)
            {
                if (acquiredRoutes.TryGetValue(request.Id, out var existing))
                {
                    return existing.Plan.Value;
                }

                var entry = routes.Acquire(request, geometry);
                acquiredRoutes.Add(request.Id, entry);
                try
                {
                    return entry.Plan.Value;
                }
                catch
                {
                    acquiredRoutes.Remove(request.Id);
                    routes.Release(request.Id, entry);
                    throw;
                }
            }

            internal bool IsActive(RiverEdgePlan route)
            {
                if (acquiredActivities.TryGetValue(route.Id, out var existing))
                {
                    return existing.Plan.Value.IsActive;
                }

                var entry = activities.Acquire(route);
                acquiredActivities.Add(route.Id, entry);
                try
                {
                    return entry.Plan.Value.IsActive;
                }
                catch
                {
                    acquiredActivities.Remove(route.Id);
                    activities.Release(route.Id, entry);
                    throw;
                }
            }

            internal void ReleaseDependencies()
            {
                foreach (var pair in acquiredActivities)
                {
                    activities.Release(pair.Key, pair.Value);
                }

                foreach (var pair in acquiredRoutes)
                {
                    routes.Release(pair.Key, pair.Value);
                }

                acquiredActivities.Clear();
                acquiredRoutes.Clear();
            }
        }
    }

    internal sealed class RiverProposalRegion
    {
        public RiverProposalRegion(
            TopologyRegionKey key,
            int size,
            IReadOnlyList<RiverEdgePlan> routes)
        {
            Key = key;
            Size = size;
            OriginX = checked(key.X * size);
            OriginZ = checked(key.Z * size);
            Routes = routes ?? throw new ArgumentNullException(nameof(routes));
        }

        public TopologyRegionKey Key { get; }
        public int Size { get; }
        public int OriginX { get; }
        public int OriginZ { get; }
        public IReadOnlyList<RiverEdgePlan> Routes { get; }
    }

    internal sealed class RiverProposalRegionStore
    {
        private readonly WorldHydrology hydrology;
        private readonly RiverRoutePlanStore routes;
        private readonly ConcurrentDictionary<TopologyRegionKey, Entry> entries =
            new();

        public RiverProposalRegionStore(
            WorldHydrology hydrology,
            RiverRoutePlanStore routes)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
            this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
        }

        public ProposalScope BeginScope() => new(this);

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
                        "River Proposal Region Scope ownership is unbalanced.");
                }

                entry.ScopeCount--;
                if (entry.ScopeCount != 0)
                {
                    return;
                }

                entry.Evicted = true;
                entries.TryRemove(key, out _);
            }

            entry.ReleaseRoutes();
        }

        private Entry CreateEntry(TopologyRegionKey key)
        {
            Entry entry = null;
            entry = new Entry(
                routes,
                hydrology,
                () =>
                {
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        return RiverGraphRegionBuilder.BuildProposalRegion(
                            hydrology,
                            key,
                            entry);
                    }
                    finally
                    {
                        hydrology.Metrics.RecordRiverProposal(
                            Stopwatch.GetTimestamp() - started);
                    }
                });
            return entry;
        }

        internal sealed class ProposalScope : IDisposable
        {
            private RiverProposalRegionStore owner;
            private Dictionary<TopologyRegionKey, Entry> acquired = new();

            internal ProposalScope(RiverProposalRegionStore owner)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public RiverProposalRegion Get(TopologyRegionKey key)
            {
                if (owner == null)
                {
                    throw new ObjectDisposedException(nameof(ProposalScope));
                }

                if (acquired.TryGetValue(key, out var existing))
                {
                    return existing.Plan.Value;
                }

                var entry = owner.Acquire(key);
                acquired.Add(key, entry);
                try
                {
                    return entry.Plan.Value;
                }
                catch
                {
                    acquired.Remove(key);
                    owner.Release(key, entry);
                    throw;
                }
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

        internal sealed class Entry
        {
            private readonly RiverRoutePlanStore routes;
            private readonly Dictionary<HydrologyGraphEdgeId, RiverRoutePlanStore.Entry>
                acquiredRoutes = new();

            public Entry(
                RiverRoutePlanStore routes,
                WorldHydrology hydrology,
                Func<RiverProposalRegion> create)
            {
                this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
                TopologyScope = (hydrology ?? throw new ArgumentNullException(
                    nameof(hydrology))).BeginTopologyPlanScope();
                EndpointScope = hydrology.BeginEndpointCatalogPlanScope();
                Plan = new Lazy<RiverProposalRegion>(
                    create ?? throw new ArgumentNullException(nameof(create)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public HydrologyPlanScope TopologyScope { get; }
            public HydrologyPlanScope EndpointScope { get; }
            public Lazy<RiverProposalRegion> Plan { get; }
            public int ScopeCount;
            public bool Evicted;

            internal RiverEdgePlan GetRoute(
                in RiverEdgePlanRequest request,
                RiverEdgePlan geometry)
            {
                if (acquiredRoutes.TryGetValue(request.Id, out var existing))
                {
                    return existing.Plan.Value;
                }

                var entry = routes.Acquire(request, geometry);
                acquiredRoutes.Add(request.Id, entry);
                try
                {
                    return entry.Plan.Value;
                }
                catch
                {
                    acquiredRoutes.Remove(request.Id);
                    routes.Release(request.Id, entry);
                    throw;
                }
            }

            internal void ReleaseRoutes()
            {
                foreach (var pair in acquiredRoutes)
                {
                    routes.Release(pair.Key, pair.Value);
                }

                acquiredRoutes.Clear();
                EndpointScope.Dispose();
                TopologyScope.Dispose();
            }
        }
    }

    internal sealed class RiverRoutePlanStore
    {
        private readonly ConcurrentDictionary<HydrologyGraphEdgeId, Entry> entries =
            new();

        public RiverRoutePlanStore(WorldHydrology hydrology)
        {
            _ = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
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

            while (true)
            {
                var entry = entries.GetOrAdd(request.Id, _ => new Entry(geometry));
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
            lock (entry.Gate)
            {
                if (entry.ScopeCount <= 0)
                {
                    throw new InvalidOperationException(
                        "River Route Scope ownership is unbalanced.");
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
            public Entry(RiverEdgePlan route)
            {
                Plan = new Lazy<RiverEdgePlan>(
                    () => route,
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public Lazy<RiverEdgePlan> Plan { get; }
            public int ScopeCount;
            public bool Evicted;
        }
    }

    internal readonly struct RiverEdgeActivity
    {
        public RiverEdgeActivity(bool isActive)
        {
            IsActive = isActive;
        }

        public bool IsActive { get; }
    }

    internal sealed class RiverEdgeActivityStore
    {
        private readonly WorldHydrology hydrology;
        private readonly RiverProposalRegionStore proposals;
        private readonly ConcurrentDictionary<HydrologyGraphEdgeId, Entry> entries =
            new();

        public RiverEdgeActivityStore(
            WorldHydrology hydrology,
            RiverProposalRegionStore proposals)
        {
            this.hydrology = hydrology ?? throw new ArgumentNullException(nameof(hydrology));
            this.proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
        }

        public Entry Acquire(RiverEdgePlan route)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            while (true)
            {
                var entry = entries.GetOrAdd(route.Id, _ => CreateEntry(route));
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

        private Entry CreateEntry(RiverEdgePlan route)
        {
            Entry entry = null;
            entry = new Entry(
                proposals,
                () =>
                {
                    var started = Stopwatch.GetTimestamp();
                    try
                    {
                        return RiverGraphRegionBuilder.ResolveActivity(
                            hydrology,
                            route,
                            entry.ProposalScope);
                    }
                    finally
                    {
                        hydrology.Metrics.RecordRiverActivity(
                            Stopwatch.GetTimestamp() - started);
                    }
                });
            return entry;
        }

        public void Release(HydrologyGraphEdgeId id, Entry entry)
        {
            lock (entry.Gate)
            {
                if (entry.ScopeCount <= 0)
                {
                    throw new InvalidOperationException(
                        "River Activity Scope ownership is unbalanced.");
                }

                entry.ScopeCount--;
                if (entry.ScopeCount != 0)
                {
                    return;
                }

                entry.Evicted = true;
                entries.TryRemove(id, out _);
            }

            entry.Dispose();
        }

        internal sealed class Entry : IDisposable
        {
            public Entry(
                RiverProposalRegionStore proposals,
                Func<RiverEdgeActivity> create)
            {
                ProposalScope = proposals.BeginScope();
                Plan = new Lazy<RiverEdgeActivity>(
                    create ?? throw new ArgumentNullException(nameof(create)),
                    LazyThreadSafetyMode.ExecutionAndPublication);
            }

            public object Gate { get; } = new();
            public RiverProposalRegionStore.ProposalScope ProposalScope { get; }
            public Lazy<RiverEdgeActivity> Plan { get; }
            public int ScopeCount;
            public bool Evicted;

            public void Dispose() => ProposalScope.Dispose();
        }
    }
}
