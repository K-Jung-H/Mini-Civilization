using System;
using System.Collections.Generic;
using System.Threading;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    internal sealed class HydrologyPatternDrawer
    {
        private readonly PatternTileGridSettingsData grid;
        private readonly ElevationPatternMapReader elevation;
        private readonly ITerrainPatternMapReader terrain;
        private readonly WaterBrushCatalog brushes;
        private readonly WaterBrushFactory brushFactory;
        private readonly WaterMapPainter painter;
        private readonly HydrologyContributionCollector collector;

        public HydrologyPatternDrawer(
            PatternTileGridSettingsData grid,
            HydrologyFeatureSettingsData settings,
            ITerrainPatternMapReader terrain,
            WaterBrushCatalog brushes, IClimatePatternMapReader climate = null, ElevationPatternMapReader elevation = null)
        {
            this.elevation = elevation;
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            this.brushes = brushes ?? throw new ArgumentNullException(nameof(brushes));
            if (grid.World.Seed != settings.World.Seed)
            {
                throw new ArgumentException(
                    "Pattern Tile and Hydrology settings disagree.",
                    nameof(settings));
            }

            brushFactory = new WaterBrushFactory(settings, climate);
            collector = new HydrologyContributionCollector(settings, terrain);
            painter = new WaterMapPainter();
        }

        public HydrologyPatternTile Draw(
            PatternTileKey key,
            CancellationToken cancellationToken = default)
        {
            if (!grid.IsOutputAllowed(key))
            {
                throw new ArgumentOutOfRangeException(nameof(key));
            }

            var bounds = grid.GetCoreBounds(key);
            var readBounds = HydrologyHeightSolver.ReadBounds(bounds);
            var basins = CollectBasins(readBounds, cancellationToken);
            var rivers = CollectRivers(readBounds, cancellationToken);
            var resolved = collector.Resolve(bounds, basins, rivers, cancellationToken);
            return painter.Paint(key, bounds, resolved, cancellationToken);
        }

        private bool KnownOcean(int x, int z, int radius) => elevation != null && elevation.IsKnownOcean(
            new PatternTileBounds(checked(x - radius), checked(z - radius), checked(x + radius + 1), checked(z + radius + 1)));

        private List<BasinWaterBrush> CollectBasins(
            PatternTileBounds bounds,
            CancellationToken cancellationToken)
        {
            var basinSpacing = brushFactory.BasinCandidateSpacingCells;
            var padding = brushFactory.BasinPaddingCells;
            var minimumGridX = WorldCoordinateUtility.FloorDivide(
                checked(bounds.MinimumX - padding),
                basinSpacing);
            var maximumGridX = WorldCoordinateUtility.FloorDivide(
                checked(bounds.MaximumXExclusive - 1 + padding),
                basinSpacing);
            var minimumGridZ = WorldCoordinateUtility.FloorDivide(
                checked(bounds.MinimumZ - padding),
                basinSpacing);
            var maximumGridZ = WorldCoordinateUtility.FloorDivide(
                checked(bounds.MaximumZExclusive - 1 + padding),
                basinSpacing);
            var result = new List<BasinWaterBrush>();
            for (var gridZ = minimumGridZ; gridZ <= maximumGridZ; gridZ++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var gridX = minimumGridX; gridX <= maximumGridX; gridX++)
                {
                    if (!brushFactory.CanBasinAffect(gridX, gridZ, bounds)
                        || !brushFactory.IsBasinCandidate(gridX, gridZ))
                    {
                        continue;
                    }

                    var ownerX = checked(gridX * basinSpacing);
                    var ownerZ = checked(gridZ * basinSpacing);
                    var anchorTerrain = terrain.GetCell(ownerX, ownerZ);
                    if (anchorTerrain.HasSeaPattern
                        || anchorTerrain.HasSecondarySeaPattern)
                    {
                        continue;
                    }

                    if (KnownOcean(ownerX, ownerZ, brushFactory.BasinPaddingCells)) continue;
                    var featureKey = brushFactory.GetBasinKey(gridX, gridZ);
                    var brush = brushes.GetOrCreateBasin(
                        featureKey,
                        () => brushFactory.CreateBasin(
                            gridX,
                            gridZ,
                            terrain),
                        cancellationToken);
                    if (brush.Bounds is PatternTileBounds brushBounds && brushBounds.Intersects(bounds))
                    {
                        result.Add(brush);
                    }
                }
            }

            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result;
        }

        private List<RiverWaterBrush> CollectRivers(
            PatternTileBounds bounds,
            CancellationToken cancellationToken)
        {
            var riverSpacing = brushFactory.RiverCandidateSpacingCells;
            var padding = brushFactory.RiverPaddingCells;
            var minimumGridX = WorldCoordinateUtility.FloorDivide(
                checked(bounds.MinimumX - padding),
                riverSpacing);
            var maximumGridX = WorldCoordinateUtility.FloorDivide(
                checked(bounds.MaximumXExclusive - 1 + padding),
                riverSpacing);
            var minimumGridZ = WorldCoordinateUtility.FloorDivide(
                checked(bounds.MinimumZ - padding),
                riverSpacing);
            var maximumGridZ = WorldCoordinateUtility.FloorDivide(
                checked(bounds.MaximumZExclusive - 1 + padding),
                riverSpacing);
            var result = new List<RiverWaterBrush>();
            for (var gridZ = minimumGridZ; gridZ <= maximumGridZ; gridZ++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var gridX = minimumGridX; gridX <= maximumGridX; gridX++)
                {
                    if (!brushFactory.CanRiverAffect(gridX, gridZ, bounds)
                        || !brushFactory.IsRiverCandidate(gridX, gridZ))
                    {
                        continue;
                    }

                    if (KnownOcean(checked(gridX * riverSpacing), checked(gridZ * riverSpacing), brushFactory.RiverPaddingCells)) continue;
                    var featureKey = brushFactory.GetRiverKey(gridX, gridZ);
                    var brush = brushes.GetOrCreateRiver(
                        featureKey,
                        () => brushFactory.CreateRiver(gridX, gridZ, terrain),
                        cancellationToken);
                    if (brush.Bounds is PatternTileBounds brushBounds && brushBounds.Intersects(bounds))
                    {
                        result.Add(brush);
                    }
                }
            }

            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result;
        }
    }
}
