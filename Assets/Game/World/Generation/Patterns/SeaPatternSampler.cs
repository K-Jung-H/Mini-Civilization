using System;
using MiniCivilization.World.Domain;
namespace MiniCivilization.World.Generation.Patterns
{
    // Sea only fills the Elevation-owned ocean; it never generates a separate bed.
    internal sealed class SeaPatternSampler
    {
        private readonly HydrologyFeatureSettingsData settings;
        private readonly HydrologyFeatureKey key;
        public SeaPatternSampler(HydrologyFeatureSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            key = WaterMapDrawingMath.CreateKey(HydrologyFeatureKind.Sea, 0, 0,
                WaterMapDrawingMath.DeriveSeed(settings.World.Seed, "ocean-fill"));
        }
        public bool TrySample(int x, int z, TerrainPatternCell terrain, out HydrologyDrawingSample sample)
        {
            if (!terrain.HasSeaPattern || terrain.SurfaceHeight >= settings.Sea.SurfaceHeight)
            { sample = default; return false; }
            sample = new HydrologyDrawingSample(key, WaterType.Sea, terrain.SurfaceHeight,
                settings.Sea.SurfaceHeight, 1, 0, true);
            return true;
        }
    }
}
