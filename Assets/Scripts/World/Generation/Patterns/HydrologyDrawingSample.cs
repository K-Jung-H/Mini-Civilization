using MiniCivilization.World.Domain;
namespace MiniCivilization.World.Generation.Patterns
{
    internal readonly struct HydrologyDrawingSample
    {
        public HydrologyDrawingSample(
            HydrologyFeatureKey key,
            WaterType waterType,
            float groundHeight,
            float waterSurfaceHeight,
            float interiorInfluence,
            float boundaryInfluence,
            bool hasWater,
            bool resolvedHeights = false)
        {
            Key = key;
            WaterType = waterType;
            GroundHeight = groundHeight;
            WaterSurfaceHeight = waterSurfaceHeight;
            InteriorInfluence = interiorInfluence;
            BoundaryInfluence = boundaryInfluence;
            HasWater = hasWater;
            ResolvedHeights = resolvedHeights;
        }

        public HydrologyFeatureKey Key { get; }
        public WaterType WaterType { get; }
        public float GroundHeight { get; }
        public float WaterSurfaceHeight { get; }
        public float InteriorInfluence { get; }
        public float BoundaryInfluence { get; }
        public bool HasWater { get; }
        public bool ResolvedHeights { get; }

        public PatternColumnHeights GetResolvedHeights()
        {
            if (!ResolvedHeights)
                throw new System.InvalidOperationException("Sample heights have not been resolved.");
            return HasWater
                ? PatternColumnHeights.Wet(checked((int)GroundHeight), checked((int)WaterSurfaceHeight))
                : PatternColumnHeights.Dry(checked((int)GroundHeight));
        }
    }

}
