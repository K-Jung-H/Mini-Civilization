using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;

internal static class ClimateChecks
{
    public static void Run(TerrainPatternSettings terrain, HydrologyFeatureSettings hydrology)
    {
        foreach (int seed in new[] { 0, 1, -1, int.MaxValue })
        {
            var map = new ClimateNoiseMap(seed, "temperature", 512);
            var moisture = new ClimateNoiseMap(seed, "moisture", 512);
            var large = new ClimateNoiseMap(seed, "temperature", 1024);
            var points = Enumerable.Range(-400, 800).Select(i => (X: i * 19, Z: i * -31)).ToArray();
            var samples = points.Select(p => map.GetCell(p.X, p.Z)).ToArray();
            Parallel.For(0, points.Length, i => {
                var p = points[i];
                Check(samples[i] == map.GetCell(p.X, p.Z), "parallel noise determinism");
                Check(samples[i] >= 0 && samples[i] <= 1, "normalized noise");
                Check(samples[i] == large.GetCell(p.X * 2, p.Z * 2), "scale stretches space");
                Check(Math.Abs(samples[i] - map.GetCell(p.X + 1, p.Z)) < .01f, "adjacent continuity");
            });
            Check(points.Where((p,i) => moisture.GetCell(p.X,p.Z) != samples[i]).Count() > 790, "independent channels");
        }
        var world = new WorldSettingsData(0, WorldType.Infinite, 1, 8, 10, 15, 10, 1, 5, 72,
            new WaterFlowRules(.01f, .05f, .05f));
        var grid = new PatternTileGridSettingsData(world, 1);
        var builder = new TerrainPatternTileBuilder(grid, terrain.CreateData(0));
        var store = new PatternMapStore();
        var settings = new ClimateSettings();
        var elevation = new ElevationPatternMapReader(grid, store, new ElevationPatternSettingsData(terrain.CreateData(0)));
        var reader = new ClimatePatternMapReader(grid, store, elevation, settings);
        var key = new PatternTileKey(-1, 0);
        var results = new ClimatePatternTile[16];
        Parallel.For(0, results.Length, i => results[i] = reader.Build(key));
        Check(results.All(t => ReferenceEquals(t, results[0])), "shared climate build");
        var first = results[0];
        settings.TemperatureRegionScaleCells = 1;
        store.Retain(new HashSet<PatternTileKey>());
        Check(!store.TryGetClimate(key, out _), "climate eviction");
        var rebuilt = reader.Build(key);
        for (int z = first.Bounds.MinimumZ; z < first.Bounds.MaximumZExclusive; z++)
        for (int x = first.Bounds.MinimumX; x < first.Bounds.MaximumXExclusive; x++)
        {
            Check(first.GetCell(x,z).Equals(rebuilt.GetCell(x,z)), "snapshot and regeneration");
            Check(first.GetTemperature(x,z) == new ClimateNoiseMap(0,"temperature",512).GetCell(x,z), "raw temperature preserved");
        }
        var data = terrain.CreateData(0);
        var wideData = new TerrainPatternSettingsData(0, 2, data.TerrainBaseHeight, data.NoiseRouter, data.Region,
            data.BaseSurface, data.Smooth, data.Rugged, data.Mountain, data.Canyon);
        var wideGrid = new PatternTileGridSettingsData(world, 2);
        var wideStore = new PatternMapStore();
        var wide = new ClimatePatternMapReader(wideGrid, wideStore,
            new ElevationPatternMapReader(wideGrid, wideStore, new ElevationPatternSettingsData(wideData)), new ClimateSettings());
        for (int x = -16; x < 16; x++)
            Check(reader.GetClimateCell(x,0).Equals(wide.GetClimateCell(x,0)), "climate tile span invariance");
        var retry = new PatternMapStore();
        try { retry.GetOrBuildClimate(key, () => throw new OperationCanceledException()); }
        catch (OperationCanceledException) { }
        Check(ReferenceEquals(first, retry.GetOrBuildClimate(key, () => first)), "failed build retry");

        // Force a dry/hot fixture and verify Climate reaches the actual materialized Cell.
        var desertSettings = new ClimateSettings { ColdThreshold = 0, HotThreshold = .01f,
            DryThreshold = .95f, ForestThreshold = .96f, WetlandThreshold = .99f, AltitudeCoolingPerCell = 0 };
        var bounds = grid.GetCoreBounds(default);
        var dryTerrain = new TerrainPatternTile(default, bounds,
            Enumerable.Repeat(new TerrainPatternCell(TerrainPatternType.Smooth, 20, 0, 0), bounds.Width * bounds.Height).ToArray());
        var dryElevation = new ElevationPatternTile(default, bounds, Enumerable.Repeat(20f, bounds.Width * bounds.Height).ToArray());
        var desert = new ClimatePatternTile(dryElevation, new ClimateNoiseMap(0,"temperature",512),
            new ClimateNoiseMap(0,"moisture",640), desertSettings);
        var dryHydro = new HydrologyPatternTile(default, bounds, Array.Empty<HydrologyFeatureKey>(),
            Enumerable.Repeat(HydrologyPatternCell.None, bounds.Width * bounds.Height).ToArray());
        var cellWorld = new WorldData(world);
        new PatternChunkMaterializer(grid).Materialize(cellWorld, default, new PatternTilePair(desert, dryTerrain, dryHydro));
        var surfaceCell = cellWorld.GetCell(0,3,0);
        Check(surfaceCell.Biome.Terrain == TerrainBiome.Desert && surfaceCell.Biome.Climate == ClimateBiome.Warm
            && surfaceCell.Terrain.Surface == SurfaceType.Ground, "desert surface materialization");
        // Heights are stored in height steps; climate cooling settings are expressed per Cell.
        var coolSettings = new ClimateSettings { AltitudeReferenceHeight = 0, AltitudeCoolingPerCell = .01f };
        var cooled = new ClimatePatternTile(dryElevation, new ClimateNoiseMap(0,"temperature",512),
            new ClimateNoiseMap(0,"moisture",640), coolSettings);
        Check(Math.Abs(cooled.GetTemperature(0,0) - cooled.GetCell(0,0).Temperature - .04f) < .000001f,
            "altitude cooling uses Cell units and preserves raw temperature");

        var hydro = hydrology.CreateData(world);
        var baseline = new WaterBrushFactory(hydro);
        var reducedReader = new RuleReader(.2f, .25f, .3f);
        var reduced = new WaterBrushFactory(hydro, reducedReader);
        var blocked = new WaterBrushFactory(hydro, new RuleReader(0, 1, 0));
        int normalBasins = 0, reducedBasins = 0, normalRivers = 0, reducedRivers = 0;
        var areaMethod = typeof(WaterBrushFactory).GetMethod("ResolveBasinArea", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int z = -30; z <= 30; z++)
        for (int x = -30; x <= 30; x++)
        {
            bool b = baseline.IsBasinCandidate(x,z), r = baseline.IsRiverCandidate(x,z);
            bool rb = reduced.IsBasinCandidate(x,z), rr = reduced.IsRiverCandidate(x,z);
            Check(!rb || b, "basin reduction subset"); Check(!rr || r, "river reduction subset");
            Check(!blocked.IsBasinCandidate(x,z) && !blocked.IsRiverCandidate(x,z), "zero occurrence");
            if (b) normalBasins++; if (rb) reducedBasins++;
            if (r) normalRivers++; if (rr) reducedRivers++;
            int area = (int)areaMethod.Invoke(reduced, new object[] { x,z });
            Check(area <= (int)areaMethod.Invoke(baseline, new object[] { x,z }), "basin area reduction");
            Check(reduced.GetBasinKey(x,z).Kind == (area <= world.PondMaximumArea ? HydrologyFeatureKind.Pond : HydrologyFeatureKind.Lake), "classification after area correction");
        }
        Check(reducedBasins < normalBasins && reducedRivers < normalRivers, "reduced occurrence counts");
        reducedReader.RuleReads = 0;
        reduced.CreateRiver(0,0,reducedReader);
        Check(reducedReader.RuleReads == 0, "river geometry never rechecks climate rules");
        Console.WriteLine("PASS climate: seeded Perlin range/continuity/scale, parallel cache/eviction/retry, tile spans, raw maps, basin area/frequency and river boundary independence");
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private sealed class RuleReader : IClimatePatternMapReader, ITerrainPatternMapReader
    {
        private readonly BiomeHydrologyRule rule;
        public int RuleReads;
        public RuleReader(float basin, float area, float river)
        { rule = new BiomeHydrologyRule { Biome = TerrainBiome.Desert, BasinOccurrence = basin, BasinArea = area, RiverOccurrence = river }; }
        public TerrainPatternCell GetCell(int x,int z) => new(TerrainPatternType.Smooth, 140,0,0);
        public ClimatePatternCell GetClimateCell(int x,int z) => new(new ElevationPatternCell(140), .9f,.1f,ClimateBiome.Warm,TerrainBiome.Desert);
        public BiomeTerrainRule GetTerrainRule(int x,int z) => BiomeTerrainRule.Default;
        public BiomeHydrologyRule GetHydrologyRule(int x,int z) { RuleReads++; return rule; }
    }
}
