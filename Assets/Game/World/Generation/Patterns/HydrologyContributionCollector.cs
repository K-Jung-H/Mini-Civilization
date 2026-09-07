using System;
using System.Collections.Generic;
using System.Threading;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    // Collects every local overlap before selection. No completed Hydrology tiles are read.
    internal sealed class HydrologyContributionCollector
    {
        private readonly ITerrainPatternMapReader terrain;
        private readonly SeaPatternSampler sea;
        private readonly int worldHeight;

        public HydrologyContributionCollector(HydrologyFeatureSettingsData settings,
            ITerrainPatternMapReader terrain)
        {
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            sea = new SeaPatternSampler(settings);
            worldHeight = settings.World.WorldHeight;
        }

        public HydrologyDrawingSample?[] Resolve(PatternTileBounds core,
            IReadOnlyList<BasinWaterBrush> basins, IReadOnlyList<RiverWaterBrush> rivers,
            CancellationToken cancellationToken)
        {
            var bounds = HydrologyHeightSolver.ReadBounds(core);
            var count = checked(bounds.Width * bounds.Height);
            var terrainCells = new TerrainPatternCell[count];
            var contributions = new List<HydrologyContribution>[count];
            for (var z = bounds.MinimumZ; z < bounds.MaximumZExclusive; z++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = bounds.MinimumX; x < bounds.MaximumXExclusive; x++)
                {
                    var index = Index(x, z);
                    terrainCells[index] = terrain.GetCell(x, z);
                    if (sea.TrySample(x, z, terrainCells[index], out var sample))
                        Add(index, HydrologyContributionKind.Sea, sample);
                }
            }
            foreach (var brush in basins) Collect(brush, HydrologyContributionKind.Basin);
            foreach (var brush in rivers) Collect(brush, HydrologyContributionKind.River);
            var resolved = new HydrologyDrawingSample?[count];
            for (var i = 0; i < count; i++)
            {
                if (i % bounds.Width == 0) cancellationToken.ThrowIfCancellationRequested();
                resolved[i] = HydrologyOverlapResolver.Resolve(contributions[i]);
            }
            // Read only provisional local surfaces. Contacts do not recursively depend
            // on another contact or on output tile preparation order.
            var output = new HydrologyDrawingSample?[checked(core.Width * core.Height)];
            var neighbours = new List<HydrologyDrawingSample>(4);
            for (var z = core.MinimumZ; z < core.MaximumZExclusive; z++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var x = core.MinimumX; x < core.MaximumXExclusive; x++)
                {
                    var index = Index(x, z);
                    var center = resolved[index];
                    if (!center.HasValue || !center.Value.HasWater)
                    {
                        neighbours.Clear();
                        IncludeNeighbour(x - 1, z); IncludeNeighbour(x + 1, z);
                        IncludeNeighbour(x, z - 1); IncludeNeighbour(x, z + 1);
                        if (neighbours.Count > 0)
                            center = HydrologyHeightSolver.ResolveExterior(center, terrainCells[index].SurfaceHeight, neighbours);
                    }
                    if (center.HasValue)
                    {
                        if (!center.Value.ResolvedHeights)
                            center = HydrologyHeightSolver.ResolveHeights(center.Value);
                        center.Value.GetResolvedHeights().ValidateWorldHeight(worldHeight);
                    }
                    output[x - core.MinimumX + core.Width * (z - core.MinimumZ)] = center;
                }
            }
            return output;

            void IncludeNeighbour(int x, int z)
            {
                var sample = resolved[Index(x, z)];
                if (sample.HasValue && (IsBasin(sample.Value)
                    || sample.Value.Key.Kind == HydrologyFeatureKind.River) && sample.Value.HasWater)
                    neighbours.Add(sample.Value);
            }

            int Index(int x, int z) => x - bounds.MinimumX + bounds.Width * (z - bounds.MinimumZ);
            void Add(int index, HydrologyContributionKind kind, HydrologyDrawingSample sample)
            {
                (contributions[index] ??= new List<HydrologyContribution>()).Add(new HydrologyContribution(kind, sample));
            }
            void Collect(IWaterMapBrush brush, HydrologyContributionKind kind)
            {
                if (!(brush.Bounds is PatternTileBounds extent) || !extent.Intersects(bounds)) return;
                for (var z = Math.Max(bounds.MinimumZ, extent.MinimumZ);
                     z < Math.Min(bounds.MaximumZExclusive, extent.MaximumZExclusive); z++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var x = Math.Max(bounds.MinimumX, extent.MinimumX);
                         x < Math.Min(bounds.MaximumXExclusive, extent.MaximumXExclusive); x++)
                    {
                        var index = Index(x, z);
                        if (brush.TrySample(x, z, terrainCells[index], out var sample)) Add(index, kind, sample);
                    }
                }
            }
        }

        private static bool IsBasin(HydrologyDrawingSample sample) =>
            sample.Key.Kind == HydrologyFeatureKind.Pond || sample.Key.Kind == HydrologyFeatureKind.Lake;
    }
}
