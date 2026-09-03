using System;
using System.Threading;

namespace MiniCivilization.World.Generation.Patterns
{
    internal sealed class HydrologyPatternTileBuilder
    {
        private readonly HydrologyPatternDrawer drawer;

        public HydrologyPatternTileBuilder(
            PatternTileGridSettingsData grid,
            HydrologyFeatureSettingsData hydrologySettings,
            ITerrainPatternMapReader terrain,
            WaterBrushCatalog brushes)
        {
            drawer = new HydrologyPatternDrawer(
                grid,
                hydrologySettings,
                terrain,
                brushes);
        }

        public HydrologyPatternTile Build(
            PatternTileKey key,
            CancellationToken cancellationToken = default) => drawer.Draw(
            key,
            cancellationToken);
    }
}
