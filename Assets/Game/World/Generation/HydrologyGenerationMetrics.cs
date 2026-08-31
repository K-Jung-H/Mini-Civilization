using System.Diagnostics;
using System.Threading;

namespace MiniCivilization.World.Generation
{
    /// <summary>
    /// Immutable cumulative counters.  Parent plan times are inclusive: use the
    /// nested counters to locate work, not by summing every value together.
    /// </summary>
    internal readonly struct HydrologyMetricsSnapshot
    {
        public HydrologyMetricsSnapshot(
            long baseTerrainRegionCount,
            long baseTerrainRegionTicks,
            long basinComponentCount,
            long basinComponentTicks,
            long topologyRegionCount,
            long topologyRegionTicks,
            long endpointCatalogCount,
            long endpointCatalogTicks,
            long riverProposalCount,
            long riverProposalTicks,
            long riverRouteSearchCount,
            long riverRouteSearchTicks,
            long riverActivityCount,
            long riverActivityTicks,
            long riverSpatialIndexCount,
            long riverSpatialIndexTicks,
            long topologyRasterTicks,
            long riverRasterTicks)
        {
            BaseTerrainRegionCount = baseTerrainRegionCount;
            BaseTerrainRegionTicks = baseTerrainRegionTicks;
            BasinComponentCount = basinComponentCount;
            BasinComponentTicks = basinComponentTicks;
            TopologyRegionCount = topologyRegionCount;
            TopologyRegionTicks = topologyRegionTicks;
            EndpointCatalogCount = endpointCatalogCount;
            EndpointCatalogTicks = endpointCatalogTicks;
            RiverProposalCount = riverProposalCount;
            RiverProposalTicks = riverProposalTicks;
            RiverRouteSearchCount = riverRouteSearchCount;
            RiverRouteSearchTicks = riverRouteSearchTicks;
            RiverActivityCount = riverActivityCount;
            RiverActivityTicks = riverActivityTicks;
            RiverSpatialIndexCount = riverSpatialIndexCount;
            RiverSpatialIndexTicks = riverSpatialIndexTicks;
            TopologyRasterTicks = topologyRasterTicks;
            RiverRasterTicks = riverRasterTicks;
        }

        public long BaseTerrainRegionCount { get; }
        public long BaseTerrainRegionTicks { get; }
        public long BasinComponentCount { get; }
        public long BasinComponentTicks { get; }
        public long TopologyRegionCount { get; }
        public long TopologyRegionTicks { get; }
        public long EndpointCatalogCount { get; }
        public long EndpointCatalogTicks { get; }
        public long RiverProposalCount { get; }
        public long RiverProposalTicks { get; }
        public long RiverRouteSearchCount { get; }
        public long RiverRouteSearchTicks { get; }
        public long RiverActivityCount { get; }
        public long RiverActivityTicks { get; }
        public long RiverSpatialIndexCount { get; }
        public long RiverSpatialIndexTicks { get; }
        public long TopologyRasterTicks { get; }
        public long RiverRasterTicks { get; }

        public HydrologyMetricsSnapshot Delta(in HydrologyMetricsSnapshot earlier) =>
            new(
                BaseTerrainRegionCount - earlier.BaseTerrainRegionCount,
                BaseTerrainRegionTicks - earlier.BaseTerrainRegionTicks,
                BasinComponentCount - earlier.BasinComponentCount,
                BasinComponentTicks - earlier.BasinComponentTicks,
                TopologyRegionCount - earlier.TopologyRegionCount,
                TopologyRegionTicks - earlier.TopologyRegionTicks,
                EndpointCatalogCount - earlier.EndpointCatalogCount,
                EndpointCatalogTicks - earlier.EndpointCatalogTicks,
                RiverProposalCount - earlier.RiverProposalCount,
                RiverProposalTicks - earlier.RiverProposalTicks,
                RiverRouteSearchCount - earlier.RiverRouteSearchCount,
                RiverRouteSearchTicks - earlier.RiverRouteSearchTicks,
                RiverActivityCount - earlier.RiverActivityCount,
                RiverActivityTicks - earlier.RiverActivityTicks,
                RiverSpatialIndexCount - earlier.RiverSpatialIndexCount,
                RiverSpatialIndexTicks - earlier.RiverSpatialIndexTicks,
                TopologyRasterTicks - earlier.TopologyRasterTicks,
                RiverRasterTicks - earlier.RiverRasterTicks);

        public HydrologyMetricsSnapshot Add(in HydrologyMetricsSnapshot other) =>
            new(
                BaseTerrainRegionCount + other.BaseTerrainRegionCount,
                BaseTerrainRegionTicks + other.BaseTerrainRegionTicks,
                BasinComponentCount + other.BasinComponentCount,
                BasinComponentTicks + other.BasinComponentTicks,
                TopologyRegionCount + other.TopologyRegionCount,
                TopologyRegionTicks + other.TopologyRegionTicks,
                EndpointCatalogCount + other.EndpointCatalogCount,
                EndpointCatalogTicks + other.EndpointCatalogTicks,
                RiverProposalCount + other.RiverProposalCount,
                RiverProposalTicks + other.RiverProposalTicks,
                RiverRouteSearchCount + other.RiverRouteSearchCount,
                RiverRouteSearchTicks + other.RiverRouteSearchTicks,
                RiverActivityCount + other.RiverActivityCount,
                RiverActivityTicks + other.RiverActivityTicks,
                RiverSpatialIndexCount + other.RiverSpatialIndexCount,
                RiverSpatialIndexTicks + other.RiverSpatialIndexTicks,
                TopologyRasterTicks + other.TopologyRasterTicks,
                RiverRasterTicks + other.RiverRasterTicks);

        public string ToLogFragment() =>
            $"baseRegion={BaseTerrainRegionCount}/{Milliseconds(BaseTerrainRegionTicks)}ms, "
            + $"basin={BasinComponentCount}/{Milliseconds(BasinComponentTicks)}ms, "
            + $"topology={TopologyRegionCount}/{Milliseconds(TopologyRegionTicks)}ms, "
            + $"endpoint={EndpointCatalogCount}/{Milliseconds(EndpointCatalogTicks)}ms, "
            + $"proposal={RiverProposalCount}/{Milliseconds(RiverProposalTicks)}ms, "
            + $"routeSearch={RiverRouteSearchCount}/{Milliseconds(RiverRouteSearchTicks)}ms, "
            + $"activity={RiverActivityCount}/{Milliseconds(RiverActivityTicks)}ms, "
            + $"spatialIndex={RiverSpatialIndexCount}/{Milliseconds(RiverSpatialIndexTicks)}ms, "
            + $"topologyRaster={Milliseconds(TopologyRasterTicks)}ms, "
            + $"riverRaster={Milliseconds(RiverRasterTicks)}ms";

        private static long Milliseconds(long ticks) =>
            checked(ticks * 1000 / Stopwatch.Frequency);
    }

    internal sealed class HydrologyGenerationMetrics
    {
        private long baseTerrainRegionCount;
        private long baseTerrainRegionTicks;
        private long basinComponentCount;
        private long basinComponentTicks;
        private long topologyRegionCount;
        private long topologyRegionTicks;
        private long endpointCatalogCount;
        private long endpointCatalogTicks;
        private long riverProposalCount;
        private long riverProposalTicks;
        private long riverRouteSearchCount;
        private long riverRouteSearchTicks;
        private long riverActivityCount;
        private long riverActivityTicks;
        private long riverSpatialIndexCount;
        private long riverSpatialIndexTicks;
        private long topologyRasterTicks;
        private long riverRasterTicks;

        public HydrologyMetricsSnapshot Capture() => new(
            Interlocked.Read(ref baseTerrainRegionCount),
            Interlocked.Read(ref baseTerrainRegionTicks),
            Interlocked.Read(ref basinComponentCount),
            Interlocked.Read(ref basinComponentTicks),
            Interlocked.Read(ref topologyRegionCount),
            Interlocked.Read(ref topologyRegionTicks),
            Interlocked.Read(ref endpointCatalogCount),
            Interlocked.Read(ref endpointCatalogTicks),
            Interlocked.Read(ref riverProposalCount),
            Interlocked.Read(ref riverProposalTicks),
            Interlocked.Read(ref riverRouteSearchCount),
            Interlocked.Read(ref riverRouteSearchTicks),
            Interlocked.Read(ref riverActivityCount),
            Interlocked.Read(ref riverActivityTicks),
            Interlocked.Read(ref riverSpatialIndexCount),
            Interlocked.Read(ref riverSpatialIndexTicks),
            Interlocked.Read(ref topologyRasterTicks),
            Interlocked.Read(ref riverRasterTicks));

        public void RecordBaseTerrainRegion(long ticks) =>
            Record(ref baseTerrainRegionCount, ref baseTerrainRegionTicks, ticks);
        public void RecordBasinComponent(long ticks) =>
            Record(ref basinComponentCount, ref basinComponentTicks, ticks);
        public void RecordTopologyRegion(long ticks) =>
            Record(ref topologyRegionCount, ref topologyRegionTicks, ticks);
        public void RecordEndpointCatalog(long ticks) =>
            Record(ref endpointCatalogCount, ref endpointCatalogTicks, ticks);
        public void RecordRiverProposal(long ticks) =>
            Record(ref riverProposalCount, ref riverProposalTicks, ticks);
        public void RecordRiverRouteSearch(long ticks) =>
            Record(ref riverRouteSearchCount, ref riverRouteSearchTicks, ticks);
        public void RecordRiverActivity(long ticks) =>
            Record(ref riverActivityCount, ref riverActivityTicks, ticks);
        public void RecordRiverSpatialIndex(long ticks) =>
            Record(ref riverSpatialIndexCount, ref riverSpatialIndexTicks, ticks);
        public void RecordTopologyRaster(long ticks) =>
            Interlocked.Add(ref topologyRasterTicks, ticks);
        public void RecordRiverRaster(long ticks) =>
            Interlocked.Add(ref riverRasterTicks, ticks);

        private static void Record(ref long count, ref long totalTicks, long ticks)
        {
            Interlocked.Increment(ref count);
            Interlocked.Add(ref totalTicks, ticks);
        }
    }
}
