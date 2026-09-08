using System;
using System.Threading;

namespace MiniCivilization.World.Generation.Patterns
{
    // Extracted from the existing authoring settings. These inputs are evaluated only by Elevation.
    public sealed class ElevationPatternSettingsData
    {
        public ElevationPatternSettingsData(TerrainPatternSettingsData source, float seaLevel = 100f, float maximumLandRise = 250f, float maximumDepth = 90f)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (!float.IsFinite(seaLevel) || !float.IsFinite(maximumLandRise) || !float.IsFinite(maximumDepth)
                || seaLevel <= 0 || maximumLandRise <= 0 || maximumDepth <= 0 || maximumDepth > seaLevel)
                throw new ArgumentException("Invalid elevation height limits.");
            SeaLevel = seaLevel; MaximumLandRise = maximumLandRise; MaximumDepth = maximumDepth;
            WorldSeed = source.WorldSeed;
            NoiseRouter = source.NoiseRouter;
        }
        public float SeaLevel { get; }
        public float MaximumLandRise { get; }
        public float MaximumDepth { get; }
        public int WorldSeed { get; }
        public TerrainNoiseRouterData NoiseRouter { get; }
    }

    public readonly struct ElevationPatternCell
    {
        public ElevationPatternCell(float height)
        {
            if (!float.IsFinite(height)) throw new ArgumentOutOfRangeException(nameof(height));
            Height = height;
        }
        // Same height-step units as Terrain; convert only at authoring/presentation boundaries.
        public float Height { get; }
    }

    public sealed class ElevationPatternEvaluator : IElevationPatternMapReader
    {
        private readonly ElevationPatternSettingsData settings;
        private readonly int continentalnessSeed;
        public ElevationPatternEvaluator(ElevationPatternSettingsData settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            continentalnessSeed = PatternNoise.DeriveSeed(settings.WorldSeed, "world-router-continentalness");
        }
        public float GetHeight(double x, double z) => EvaluateHeight(x, z);
        public float EvaluateHeight(double x, double z)
        {
            float value = EvaluateNormalized(x, z);
            return settings.SeaLevel + value * (value < 0 ? settings.MaximumDepth : settings.MaximumLandRise);
        }
        public float EvaluateNormalized(double x, double z) => Math.Clamp(
            PatternNoise.SampleNormalized(x, z, settings.NoiseRouter.Continentalness, continentalnessSeed) * 2f - 1f, -1f, 1f);
        public float SeaLevel => settings.SeaLevel;
    }

    public sealed class ElevationPatternTile
    {
        private readonly float[] heights;
        public ElevationPatternTile(PatternTileKey key, PatternTileBounds bounds, float[] heights, float seaLevel = 100f, float maximumLandRise = 250f, float maximumDepth = 90f)
        {
            if (heights == null || heights.Length != checked(bounds.Width * bounds.Height))
                throw new ArgumentException("Elevation tile dimensions disagree.", nameof(heights));
            foreach (var height in heights)
                if (!float.IsFinite(height)) throw new ArgumentException("Elevation must be finite.", nameof(heights));
            Key = key; Bounds = bounds; this.heights = (float[])heights.Clone();
            SeaLevel = seaLevel; MaximumLandRise = maximumLandRise; MaximumDepth = maximumDepth;
            MaximumHeight = float.NegativeInfinity;
            foreach (var height in heights) MaximumHeight = Math.Max(MaximumHeight, height);
        }
        public float SeaLevel { get; }
        public float MaximumLandRise { get; }
        public float MaximumDepth { get; }
        public float MaximumHeight { get; }
        public float GetNormalized(int x, int z) { float delta = GetCell(x,z).Height - SeaLevel; return Math.Clamp(delta / (delta < 0 ? MaximumDepth : MaximumLandRise), -1f, 1f); }
        public PatternTileKey Key { get; }
        public PatternTileBounds Bounds { get; }
        public int CellCount => heights.Length;
        public ElevationPatternCell GetCell(int x, int z)
        {
            if (!Bounds.Contains(x, z)) throw new ArgumentOutOfRangeException(nameof(x));
            return new ElevationPatternCell(heights[(z - Bounds.MinimumZ) * Bounds.Width + x - Bounds.MinimumX]);
        }
    }

    public interface IElevationPatternMapReader
    {
        float SeaLevel { get; }
        float GetHeight(double x, double z);
    }

    public sealed class ElevationPatternMapReader : IElevationPatternMapReader
    {
        private readonly PatternTileGridSettingsData grid;
        private readonly PatternMapStore store;
        private readonly ElevationPatternEvaluator evaluator;
        private readonly ElevationPatternSettingsData settings;
        public float SeaLevel => settings.SeaLevel;
        public ElevationPatternMapReader(PatternTileGridSettingsData grid, PatternMapStore store, ElevationPatternSettingsData settings)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (grid.World.Seed != settings.WorldSeed) throw new ArgumentException("Elevation seed disagrees with world.");
            this.settings = settings;
            evaluator = new ElevationPatternEvaluator(settings);
        }
        public ElevationPatternTile Build(PatternTileKey key, CancellationToken token = default)
        {
            if (store.TryGetElevation(key, out var cached)) return cached;
            return BuildMissing(key, token);
        }
        private ElevationPatternTile BuildMissing(PatternTileKey key, CancellationToken token) => store.GetOrBuildElevation(key, () =>
        {
            var bounds = grid.GetCoreBounds(key);
            var heights = new float[checked(bounds.Width * bounds.Height)];
            for (int z = bounds.MinimumZ; z < bounds.MaximumZExclusive; z++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = bounds.MinimumX; x < bounds.MaximumXExclusive; x++)
                    heights[(z - bounds.MinimumZ) * bounds.Width + x - bounds.MinimumX] = evaluator.EvaluateHeight(x, z);
            }
            return new ElevationPatternTile(key, bounds, heights, settings.SeaLevel, settings.MaximumLandRise, settings.MaximumDepth);
        });
        public float GetHeight(double x, double z)
        {
            // Fractional samples support continuous evaluation tools; production tiles use integer coordinates.
            if (x != Math.Floor(x) || z != Math.Floor(z)) return evaluator.EvaluateHeight(x, z);
            int cellX = checked((int)x), cellZ = checked((int)z);
            return Build(grid.GetKeyForCell(cellX, cellZ)).GetCell(cellX, cellZ).Height;
        }
        // One reader per tile job; no mutable state is shared between workers.
        internal IElevationPatternMapReader CreateReadScope() => new ReadScope(this);
        private sealed class ReadScope : IElevationPatternMapReader
        {
            private readonly ElevationPatternMapReader owner;
            private readonly System.Collections.Generic.Dictionary<PatternTileKey, ElevationPatternTile> tiles = new();
            public ReadScope(ElevationPatternMapReader owner) { this.owner = owner; }
            public float SeaLevel => owner.SeaLevel;
            public float GetHeight(double x, double z)
            {
                if (x != Math.Floor(x) || z != Math.Floor(z)) return owner.GetHeight(x,z);
                int cx=checked((int)x), cz=checked((int)z);
                var key=owner.grid.GetKeyForCell(cx,cz);
                if (!tiles.TryGetValue(key,out var tile)) { tile=owner.Build(key); tiles.Add(key,tile); }
                return tile.GetCell(cx,cz).Height;
            }
        }
        public bool IsKnownOcean(PatternTileBounds bounds)
        {
            var min = grid.GetKeyForCell(bounds.MinimumX, bounds.MinimumZ);
            var max = grid.GetKeyForCell(bounds.MaximumXExclusive - 1, bounds.MaximumZExclusive - 1);
            for (int z = min.Z; z <= max.Z; z++)
            for (int x = min.X; x <= max.X; x++)
                if (!store.TryGetElevation(new PatternTileKey(x,z), out var tile) || tile.MaximumHeight >= SeaLevel) return false;
            return true;
        }
    }
}
