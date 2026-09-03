using System;
using System.Collections.Generic;
using System.Threading;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    internal sealed class HydrologyPatternDrawer
    {
        private readonly PatternTileGridSettingsData grid;
        private readonly ITerrainPatternMapReader terrain;
        private readonly WaterBrushCatalog brushes;
        private readonly WaterBrushFactory brushFactory;
        private readonly WaterMapPainter painter;

        public HydrologyPatternDrawer(
            PatternTileGridSettingsData grid,
            HydrologyFeatureSettingsData settings,
            ITerrainPatternMapReader terrain,
            WaterBrushCatalog brushes)
        {
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

            brushFactory = new WaterBrushFactory(settings);
            painter = new WaterMapPainter(settings, terrain);
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
            var basins = CollectBasins(bounds, cancellationToken);
            var rivers = CollectRivers(bounds, cancellationToken);
            return painter.Paint(key, bounds, basins, rivers, cancellationToken);
        }

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
                    if (!brushFactory.IsBasinCandidate(gridX, gridZ))
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

                    var featureKey = brushFactory.GetBasinKey(gridX, gridZ);
                    result.Add(brushes.GetOrCreateBasin(
                        featureKey,
                        () => brushFactory.CreateBasin(
                            gridX,
                            gridZ,
                            terrain),
                        cancellationToken));
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
                    if (!brushFactory.IsRiverCandidate(gridX, gridZ))
                    {
                        continue;
                    }

                    var featureKey = brushFactory.GetRiverKey(gridX, gridZ);
                    result.Add(brushes.GetOrCreateRiver(
                        featureKey,
                        () => brushFactory.CreateRiver(gridX, gridZ, terrain),
                        cancellationToken));
                }
            }

            result.Sort((left, right) => left.Key.CompareTo(right.Key));
            return result;
        }
    }
}
