using System;
using System.Threading;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Generation.Patterns
{
    [Serializable]
    public sealed class ClimateSettings
    {
        [Min(1)] public float TemperatureRegionScaleCells = 512f;
        [Min(1)] public float MoistureRegionScaleCells = 640f;
        [Min(0)] public float AltitudeCoolingPerCell = 0.003f;
        public float AltitudeReferenceHeight = 20f;
        [Range(0, 1)] public float ColdThreshold = 0.3f;
        [Range(0, 1)] public float HotThreshold = 0.65f;
        [Range(0, 1)] public float DryThreshold = 0.35f;
        [Range(0, 1)] public float ForestThreshold = 0.6f;
        [Range(0, 1)] public float WetlandThreshold = 0.75f;
        public float WetlandMaximumHeight = 22f;
        [Min(0)] public float MountainMinimumHeight = 55f;
        [Min(1)] public int VariantRegionScaleCells = 256;
        [Range(0, 1)] public float MesaOccurrence = 0.25f;
        public BiomeTerrainRule[] TerrainRules =
        {
            new() { Biome = TerrainBiome.Desert, Smooth = 6, Rugged = 2, Mountain = 1, Canyon = 1 },
            new() { Biome = TerrainBiome.Desert, Variant = ClimateRegionVariant.Mesa, Smooth = 1, Rugged = 2, Mountain = 1, Canyon = 6 },
            new() { Biome = TerrainBiome.Field, Smooth = 6, Rugged = 2, Mountain = 1, Canyon = 1 },
            new() { Biome = TerrainBiome.Forest, Smooth = 3, Rugged = 4, Mountain = 2, Canyon = 1 }
        };
        public BiomeHydrologyRule[] HydrologyRules =
        {
            new() { Biome = TerrainBiome.Desert, BasinOccurrence = 0.2f,
                BasinArea = 0.4f, RiverOccurrence = 0.3f }
        };

        public ClimateSettings Snapshot()
        {
            var copy = (ClimateSettings)MemberwiseClone();
            copy.HydrologyRules = (BiomeHydrologyRule[])(HydrologyRules
                ?? throw new ArgumentException("Climate hydrology rules are required.")).Clone();
            copy.TerrainRules = (BiomeTerrainRule[])(TerrainRules
                ?? throw new ArgumentException("Climate terrain rules are required.")).Clone();
            copy.Validate();
            return copy;
        }

        private void Validate()
        {
            var values = new[] { TemperatureRegionScaleCells, MoistureRegionScaleCells,
                AltitudeCoolingPerCell, AltitudeReferenceHeight, ColdThreshold, HotThreshold,
                DryThreshold, ForestThreshold, WetlandThreshold, WetlandMaximumHeight,
                MountainMinimumHeight, MesaOccurrence };
            foreach (var value in values)
                if (!float.IsFinite(value)) throw new ArgumentException("Climate settings must be finite.");
            if (TemperatureRegionScaleCells < 1 || MoistureRegionScaleCells < 1
                || AltitudeCoolingPerCell < 0 || ColdThreshold < 0 || HotThreshold > 1
                || ColdThreshold >= HotThreshold || DryThreshold < 0
                || DryThreshold >= ForestThreshold || ForestThreshold > WetlandThreshold
                || WetlandThreshold > 1 || MountainMinimumHeight < 0
                || VariantRegionScaleCells < 1 || MesaOccurrence < 0 || MesaOccurrence > 1)
                throw new ArgumentException("Climate thresholds or scales are invalid.");
            var terrainSeen = new System.Collections.Generic.HashSet<(TerrainBiome, ClimateRegionVariant)>();
            foreach (var rule in TerrainRules)
            {
                rule.Validate();
                if (!terrainSeen.Add((rule.Biome, rule.Variant)))
                    throw new ArgumentException("Duplicate climate terrain rule.");
            }
            var seen = new System.Collections.Generic.HashSet<TerrainBiome>();
            foreach (var rule in HydrologyRules)
                if (!Enum.IsDefined(typeof(TerrainBiome), rule.Biome) || !seen.Add(rule.Biome)
                    || !ValidMultiplier(rule.BasinOccurrence) || !ValidMultiplier(rule.BasinArea)
                    || !ValidMultiplier(rule.RiverOccurrence))
                    throw new ArgumentException("Climate hydrology rules are invalid or duplicated.");
        }

        // Reduction-only area rules preserve the existing maximum basin query bounds.
        private static bool ValidMultiplier(float value) => float.IsFinite(value) && value >= 0 && value <= 1;
        public BiomeHydrologyRule Rule(TerrainBiome biome)
        {
            foreach (var rule in HydrologyRules) if (rule.Biome == biome) return rule;
            return new BiomeHydrologyRule { Biome = biome, BasinOccurrence = 1, BasinArea = 1, RiverOccurrence = 1 };
        }
    }

    public enum ClimateRegionVariant : byte { Standard, Mesa }

    [Serializable]
    public struct BiomeTerrainRule
    {
        public TerrainBiome Biome;
        public ClimateRegionVariant Variant;
        [Min(0)] public float Smooth;
        [Min(0)] public float Rugged;
        [Min(0)] public float Mountain;
        [Min(0)] public float Canyon;
        public static BiomeTerrainRule Default => new() { Smooth = 1, Rugged = 1, Mountain = 1, Canyon = 1 };
        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(TerrainBiome), Biome) || !Enum.IsDefined(typeof(ClimateRegionVariant), Variant)
                || !Valid(Smooth) || !Valid(Rugged) || !Valid(Mountain) || !Valid(Canyon)
                || (double)Smooth + Rugged + Mountain + Canyon <= 0)
                throw new ArgumentException("Terrain multipliers must be finite, nonnegative and not all zero.");
        }
        private static bool Valid(float value) => float.IsFinite(value) && value >= 0;
    }

    [Serializable]
    public struct BiomeHydrologyRule
    {
        public TerrainBiome Biome;
        [Range(0, 1)] public float BasinOccurrence;
        [Range(0, 1)] public float BasinArea;
        [Range(0, 1)] public float RiverOccurrence;
    }

    // Stateless gradient Perlin: no Unity random state, tile-local normalization or permutation period.
    public sealed class ClimateNoiseMap
    {
        private readonly int seed;
        private readonly double scale;
        public ClimateNoiseMap(int worldSeed, string channel, float regionScaleCells)
        {
            if (!float.IsFinite(regionScaleCells) || regionScaleCells < 1)
                throw new ArgumentOutOfRangeException(nameof(regionScaleCells));
            seed = WaterMapDrawingMath.DeriveSeed(worldSeed, channel);
            scale = regionScaleCells;
        }
        public float GetCell(int x, int z)
        {
            double px = x / scale + 0.371, pz = z / scale + 0.619;
            int ix = (int)Math.Floor(px), iz = (int)Math.Floor(pz);
            double dx = px - ix, dz = pz - iz, u = Fade(dx), v = Fade(dz);
            double a = Lerp(Gradient(ix, iz, dx, dz), Gradient(unchecked(ix + 1), iz, dx - 1, dz), u);
            double b = Lerp(Gradient(ix, unchecked(iz + 1), dx, dz - 1), Gradient(unchecked(ix + 1), unchecked(iz + 1), dx - 1, dz - 1), u);
            return (float)Math.Clamp(0.5 + Lerp(a, b, v) * 0.5, 0, 1);
        }
        private double Gradient(int x, int z, double dx, double dz)
        {
            uint h;
            unchecked
            {
                h = (uint)seed ^ (uint)x * 0x9e3779b9u ^ (uint)z * 0x85ebca6bu;
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15;
                h *= 0x846ca68bu; h ^= h >> 16;
            }
            return (h & 7) switch { 0 => dx, 1 => -dx, 2 => dz, 3 => -dz,
                4 => (dx + dz) * 0.7071067811865476, 5 => (dx - dz) * 0.7071067811865476,
                6 => (-dx + dz) * 0.7071067811865476, _ => (-dx - dz) * 0.7071067811865476 };
        }
        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }

    public readonly struct ClimatePatternCell
    {
        public ClimatePatternCell(ElevationPatternCell elevation, float temperature, float moisture, ClimateBiome climate, TerrainBiome biome, ClimateRegionVariant variant = ClimateRegionVariant.Standard)
        { Elevation = elevation; Temperature = temperature; Moisture = moisture; Climate = climate; Biome = biome; Variant = variant; }
        public ElevationPatternCell Elevation { get; }
        public ClimateRegionVariant Variant { get; }
        public float Temperature { get; }
        public float Moisture { get; }
        public ClimateBiome Climate { get; }
        public TerrainBiome Biome { get; }
    }

    public sealed class ClimatePatternTile
    {
        private readonly float[] temperatures, moistures, correctedTemperatures;
        private readonly TerrainBiome[] biomes;
        private readonly ClimateBiome[] climates;
        private readonly ClimateRegionVariant[] variants;
        public ElevationPatternTile Elevation { get; }
        public PatternTileKey Key => Elevation.Key;
        public PatternTileBounds Bounds => Elevation.Bounds;
        internal ClimatePatternTile(ElevationPatternTile elevation, ClimateNoiseMap temperature,
            ClimateNoiseMap moisture, ClimateSettings settings, CancellationToken token = default, int worldSeed = 0)
        {
            Elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
            variants = new ClimateRegionVariant[elevation.CellCount];
            int variantSeed = PatternNoise.DeriveSeed(worldSeed, "climate-region-variant");
            temperatures = new float[elevation.CellCount]; moistures = new float[elevation.CellCount];
            correctedTemperatures = new float[elevation.CellCount];
            biomes = new TerrainBiome[elevation.CellCount]; climates = new ClimateBiome[elevation.CellCount];
            for (int z = Bounds.MinimumZ; z < Bounds.MaximumZExclusive; z++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = Bounds.MinimumX; x < Bounds.MaximumXExclusive; x++)
                {
                    int i = Index(x, z);
                    temperatures[i] = temperature.GetCell(x, z);
                    moistures[i] = moisture.GetCell(x, z);
                    var t = elevation.GetCell(x, z);
                    float heightCells = t.Height / WorldGrid.HeightStepsPerCell;
                    float heat = Math.Clamp(temperatures[i] - Math.Max(0, heightCells - settings.AltitudeReferenceHeight)
                        * settings.AltitudeCoolingPerCell, 0, 1);
                    correctedTemperatures[i] = heat;
                    climates[i] = heat <= settings.ColdThreshold ? ClimateBiome.Cold
                        : heat >= settings.HotThreshold ? ClimateBiome.Warm : ClimateBiome.Temperate;
                    biomes[i] = heat <= settings.ColdThreshold ? TerrainBiome.Snow
                        : heightCells >= settings.MountainMinimumHeight ? TerrainBiome.Mountain
                        : heat >= settings.HotThreshold && moistures[i] <= settings.DryThreshold ? TerrainBiome.Desert
                        : moistures[i] >= settings.WetlandThreshold && heightCells <= settings.WetlandMaximumHeight ? TerrainBiome.Wetland
                        : moistures[i] >= settings.ForestThreshold ? TerrainBiome.Forest : TerrainBiome.Field;
                    if (biomes[i] == TerrainBiome.Desert && PatternNoise.Value01(
                        WorldCoordinateUtility.FloorDivide(x, settings.VariantRegionScaleCells),
                        WorldCoordinateUtility.FloorDivide(z, settings.VariantRegionScaleCells), variantSeed) < settings.MesaOccurrence)
                        variants[i] = ClimateRegionVariant.Mesa;
                }
            }
        }
        private int Index(int x, int z)
        {
            if (!Bounds.Contains(x, z)) throw new ArgumentOutOfRangeException(nameof(x));
            return (z - Bounds.MinimumZ) * Bounds.Width + x - Bounds.MinimumX;
        }
        public float GetTemperature(int x, int z) => temperatures[Index(x, z)];
        public float GetMoisture(int x, int z) => moistures[Index(x, z)];
        public ClimatePatternCell GetCell(int x, int z)
        {
            int i = Index(x, z);
            return new ClimatePatternCell(Elevation.GetCell(x, z), correctedTemperatures[i], moistures[i], climates[i], biomes[i], variants[i]);
        }
    }

    public interface IClimatePatternMapReader
    {
        ClimatePatternCell GetClimateCell(int x, int z);
        BiomeHydrologyRule GetHydrologyRule(int x, int z);
        BiomeTerrainRule GetTerrainRule(int x, int z);
    }

    public sealed class ClimatePatternMapReader : IClimatePatternMapReader
    {
        private readonly PatternTileGridSettingsData grid;
        private readonly PatternMapStore store;
        private readonly ElevationPatternMapReader elevation;
        private readonly ClimateSettings settings;
        private readonly ClimateNoiseMap temperature, moisture;
        private readonly object ruleGate = new();
        private readonly System.Collections.Generic.Dictionary<(int X,int Z), BiomeTerrainRule> terrainRules = new();
        private readonly System.Collections.Generic.Queue<(int X,int Z)> ruleOrder = new();
        private const int MaximumCachedRules = 512;
        public ClimatePatternMapReader(PatternTileGridSettingsData grid, PatternMapStore store,
            ElevationPatternMapReader elevation, ClimateSettings settings)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.elevation = elevation ?? throw new ArgumentNullException(nameof(elevation));
            this.settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Snapshot();
            temperature = new ClimateNoiseMap(grid.World.Seed, "temperature", settings.TemperatureRegionScaleCells);
            moisture = new ClimateNoiseMap(grid.World.Seed, "moisture", settings.MoistureRegionScaleCells);
        }
        public ClimatePatternTile Build(PatternTileKey key, CancellationToken token = default)
        {
            if (store.TryGetClimate(key, out var cached)) return cached;
            return BuildMissing(key, token);
        }
        private ClimatePatternTile BuildMissing(PatternTileKey key, CancellationToken token) =>
            store.GetOrBuildClimate(key, () => new ClimatePatternTile(
                elevation.Build(key, token), temperature, moisture, settings, token, grid.World.Seed));
        public ClimatePatternCell GetClimateCell(int x, int z) => Build(grid.GetKeyForCell(x, z)).GetCell(x, z);
        public BiomeHydrologyRule GetHydrologyRule(int x, int z) => settings.Rule(GetClimateCell(x, z).Biome);
        public BiomeTerrainRule GetTerrainRule(int x, int z)
        {
            lock (ruleGate)
            {
                if (terrainRules.TryGetValue((x,z),out var rule)) return rule;
                rule = ResolveTerrainRule(x,z);
                if (terrainRules.Count >= MaximumCachedRules) terrainRules.Remove(ruleOrder.Dequeue());
                terrainRules.Add((x,z),rule); ruleOrder.Enqueue((x,z));
                return rule;
            }
        }
        private BiomeTerrainRule ResolveTerrainRule(int x, int z)
        {
            var cell = GetClimateCell(x, z);
            foreach (var rule in settings.TerrainRules)
                if (rule.Biome == cell.Biome && rule.Variant == cell.Variant) return rule;
            foreach (var rule in settings.TerrainRules)
                if (rule.Biome == cell.Biome && rule.Variant == ClimateRegionVariant.Standard) return rule;
            return BiomeTerrainRule.Default;
        }
    }

}
