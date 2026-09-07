using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    internal sealed class BasinWaterBrush : IWaterMapBrush
    {
        private readonly BasinDrawingGeometry geometry;
        private readonly float maximumDepth;
        private readonly float bedAmplitude;
        private readonly BasinFeatureSettingsData settings;

        public BasinWaterBrush(
            HydrologyFeatureKey key,
            WaterType waterType,
            BasinDrawingGeometry geometry,
            float maximumDepth,
            float bedAmplitude,
            BasinFeatureSettingsData settings)
        {
            Key = key;
            WaterType = waterType;
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            this.maximumDepth = maximumDepth;
            this.bedAmplitude = bedAmplitude;
            this.settings = settings;
        }

        public HydrologyFeatureKey Key { get; }
        public PatternTileBounds? Bounds => geometry.Bounds;
        public WaterType WaterType { get; }

        public bool TrySample(
            int x,
            int z,
            TerrainPatternCell terrainCell,
            out HydrologyDrawingSample sample)
        {
            if (terrainCell.HasSeaPattern)
            {
                sample = default;
                return false;
            }

            var coordinate = CoordinateKey(x, z);
            if (!geometry.InteriorProgress.TryGetValue(
                    coordinate,
                    out var interior))
            {
                if (!geometry.ShoreMembership.TryGetValue(
                        coordinate,
                        out var membership))
                {
                    sample = default;
                    return false;
                }

                var ground = terrainCell.SurfaceHeight
                    + (geometry.SurfaceHeight + 1f - terrainCell.SurfaceHeight)
                    * membership;
                sample = new HydrologyDrawingSample(
                    Key,
                    WaterType.None,
                    ground,
                    0f,
                    0f,
                    membership,
                    false);
                return true;
            }

            var bedNoise = WaterMapDrawingMath.SampleSigned(
                x,
                z,
                settings.BedField,
                WaterMapDrawingMath.DeriveSeed(
                    unchecked((int)Key.Identity.SeedSalt),
                    "bed")) * bedAmplitude;
            var depth = 1f + settings.DepthByInterior.Evaluate(interior)
                * (Math.Min(geometry.SurfaceHeight, maximumDepth + bedNoise) - 1f);
            sample = new HydrologyDrawingSample(
                Key,
                WaterType,
                geometry.SurfaceHeight - depth,
                geometry.SurfaceHeight,
                interior,
                1f - settings.ShoreTransition.Evaluate(interior),
                depth > 0f);
            return true;
        }

        private static long CoordinateKey(int x, int z) =>
            ((long)x << 32) ^ (uint)z;
    }

    internal sealed class BasinDrawingGeometry
    {
        public static BasinDrawingGeometry Empty { get; } = new(
            0f,
            new Dictionary<long, float>(),
            new Dictionary<long, float>());

        public BasinDrawingGeometry(
            float surfaceHeight,
            IReadOnlyDictionary<long, float> interiorProgress,
            IReadOnlyDictionary<long, float> shoreMembership)
        {
            SurfaceHeight = surfaceHeight;
            InteriorProgress = interiorProgress
                ?? throw new ArgumentNullException(nameof(interiorProgress));
            ShoreMembership = shoreMembership
                ?? throw new ArgumentNullException(nameof(shoreMembership));
            var minimumX = int.MaxValue;
            var minimumZ = int.MaxValue;
            var maximumX = int.MinValue;
            var maximumZ = int.MinValue;
            foreach (var coordinate in interiorProgress.Keys) Include(coordinate);
            foreach (var coordinate in shoreMembership.Keys) Include(coordinate);
            Bounds = minimumX == int.MaxValue ? null : new PatternTileBounds(
                minimumX, minimumZ, checked(maximumX + 1), checked(maximumZ + 1));

            void Include(long coordinate)
            {
                var x = (int)(coordinate >> 32);
                var z = (int)coordinate;
                minimumX = Math.Min(minimumX, x);
                minimumZ = Math.Min(minimumZ, z);
                maximumX = Math.Max(maximumX, x);
                maximumZ = Math.Max(maximumZ, z);
            }
        }

        public PatternTileBounds? Bounds { get; }
        public float SurfaceHeight { get; }
        public IReadOnlyDictionary<long, float> InteriorProgress { get; }
        public IReadOnlyDictionary<long, float> ShoreMembership { get; }
    }

}
