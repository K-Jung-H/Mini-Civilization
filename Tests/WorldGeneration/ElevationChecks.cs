using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;

internal static class ElevationChecks
{
    public static void Run(TerrainPatternSettings authoring)
    {
        var world = new WorldSettingsData(17, WorldType.Infinite, 1, 8, 10, 15, 10, 1, 5, 72,
            new WaterFlowRules(.01f, .05f, .05f));
        var terrain = authoring.CreateData(17);
        var grid = new PatternTileGridSettingsData(world, terrain.PatternTileChunkSpan);
        var store = new PatternMapStore();
        var elevation = new ElevationPatternMapReader(grid, store, new ElevationPatternSettingsData(terrain));
        var climateSettings = new ClimateSettings();
        var climate = new ClimatePatternMapReader(grid, store, elevation, climateSettings);
        var key = new PatternTileKey(-1, 0);
        var elevationTiles = new ElevationPatternTile[12];
        Parallel.For(0, elevationTiles.Length, i => elevationTiles[i] = elevation.Build(key));
        Check(elevationTiles.All(t => ReferenceEquals(t, elevationTiles[0])), "shared elevation build");
        Check(!store.TryGetClimate(key, out _) && !store.TryGetTerrain(key, out _) && !store.TryGetHydrology(key, out _),
            "Elevation does not build downstream maps");
        var climateTile = climate.Build(key);
        Check(!store.TryGetTerrain(key, out _) && !store.TryGetHydrology(key, out _), "Climate cannot generate Terrain/Hydrology");
        var builder = new TerrainPatternTileBuilder(grid, terrain, elevation, climate);
        var terrainTiles = new TerrainPatternTile[8];
        Parallel.For(0, terrainTiles.Length, i => terrainTiles[i] = store.GetOrBuildTerrain(key, builder));
        Check(terrainTiles.All(t => ReferenceEquals(t, terrainTiles[0])), "shared weighted Terrain build");
        Check(ReferenceEquals(climateTile, climate.Build(key)) && ReferenceEquals(elevationTiles[0], elevation.Build(key)),
            "Terrain reuses upstream tiles without rebuilding");
        var first = terrainTiles[0];
        store.Retain(new HashSet<PatternTileKey>());
        Check(!store.TryGetElevation(key, out _) && !store.TryGetClimate(key, out _) && !store.TryGetTerrain(key, out _), "all map eviction");
        var second = store.GetOrBuildTerrain(key, builder);
        for (int z = first.Bounds.MinimumZ; z < first.Bounds.MaximumZExclusive; z++)
        for (int x = first.Bounds.MinimumX; x < first.Bounds.MaximumXExclusive; x++)
            Check(first.GetCell(x,z).Equals(second.GetCell(x,z)), "weighted terrain regeneration");

        var wideSettings = new TerrainPatternSettingsData(17, 2, terrain.TerrainBaseHeight,
            terrain.NoiseRouter, terrain.Region, terrain.BaseSurface, terrain.Smooth,
            terrain.Rugged, terrain.Mountain, terrain.Canyon);
        var wideGrid = new PatternTileGridSettingsData(world, 2);
        var wideStore = new PatternMapStore();
        var wideElevation = new ElevationPatternMapReader(wideGrid, wideStore, new ElevationPatternSettingsData(wideSettings));
        var wideClimate = new ClimatePatternMapReader(wideGrid, wideStore, wideElevation, new ClimateSettings());
        var wideBuilder = new TerrainPatternTileBuilder(wideGrid, wideSettings, wideElevation, wideClimate);
        foreach (var point in new[] { (-9, 7), (-1,-1), (0,0), (8,9), (128,-129) })
        {
            var narrowTile = store.GetOrBuildTerrain(grid.GetKeyForCell(point.Item1, point.Item2), builder);
            var wideTile = wideStore.GetOrBuildTerrain(wideGrid.GetKeyForCell(point.Item1, point.Item2), wideBuilder);
            Check(narrowTile.GetCell(point.Item1,point.Item2).Equals(wideTile.GetCell(point.Item1,point.Item2)),
                "weighted terrain tile span invariance");
        }

        var oceanEvaluator = new ElevationPatternEvaluator(new ElevationPatternSettingsData(terrain));
        var surfaceEvaluator = new TerrainPatternEvaluator(terrain, oceanEvaluator);
        int oceanCount = 0, landCount = 0;
        for (int z = -500; z <= 500; z += 19)
        for (int x = -500; x <= 500; x += 17)
        {
            float value = oceanEvaluator.EvaluateNormalized(x,z);
            Check(value >= -1 && value <= 1, "signed elevation range");
            var surface = surfaceEvaluator.EvaluateSample(x,z);
            if (value < 0)
            {
                oceanCount++;
                Check(surface.HasSeaPattern && surface.SurfaceHeight == oceanEvaluator.GetHeight(x,z), "ocean bed is Elevation without replacement");
            }
            else
            {
                landCount++;
                Check(!surface.HasSeaPattern && surface.SurfaceHeight >= oceanEvaluator.SeaLevel, "terrain preserves land classification");
            }
        }
        Check(oceanCount > 0 && landCount > 0, "signed field generates both continents and oceans");
        var oceanStore = new PatternMapStore();
        var oceanReader = new ElevationPatternMapReader(grid, oceanStore, new ElevationPatternSettingsData(terrain));
        var core = grid.GetCoreBounds(default);
        Check(!oceanReader.IsKnownOcean(core) && !oceanStore.TryGetElevation(default, out _), "ocean optimization never generates tiles");
        oceanStore.GetOrBuildElevation(default, () => new ElevationPatternTile(default, core,
            Enumerable.Repeat(50f, core.Width * core.Height).ToArray()));
        Check(oceanReader.IsKnownOcean(core), "fully submerged cached tile skips candidates");
        Check(!oceanReader.IsKnownOcean(core.Expand(1)), "unknown halo cannot be discarded");

        var boundedStore = new PatternMapStore();
        var demandKey = new PatternTileKey(-1000,0);
        for (int i=0;i<300;i++)
        {
            var dependencyKey = new PatternTileKey(i,0);
            var dependencyBounds = grid.GetCoreBounds(dependencyKey);
            boundedStore.GetOrBuildElevation(dependencyKey, () => new ElevationPatternTile(dependencyKey,dependencyBounds,
                new float[dependencyBounds.Width*dependencyBounds.Height]));
        }
        boundedStore.Retain(new HashSet<PatternTileKey>{demandKey});
        int retainedCells=0;
        for(int i=0;i<300;i++)
            if(boundedStore.TryGetElevation(new PatternTileKey(i,0),out var retainedTile)) retainedCells+=retainedTile.CellCount;
        Check(retainedCells > 0 && retainedCells <= 16384,"auxiliary cache bounded by Cells");
        boundedStore.Retain(new HashSet<PatternTileKey>());
        for(int i=0;i<300;i++) Check(!boundedStore.TryGetElevation(new PatternTileKey(i,0),out _),"idle auxiliary cache release");

        var smoothRules = new FixedClimate(new BiomeTerrainRule { Smooth = 1 });
        var canyonRules = new FixedClimate(new BiomeTerrainRule { Canyon = 1 });
        var flat = new FlatElevation();
        var smooth = new TerrainPatternEvaluator(terrain, flat, smoothRules);
        var canyon = new TerrainPatternEvaluator(terrain, flat, canyonRules);
        for (int z = -512; z <= 512; z += 61)
        for (int x = -512; x <= 512; x += 59)
        {
            Check(smooth.EvaluateSample(x,z).Type == TerrainPatternType.Smooth, "Smooth-only climate weighting");
            Check(canyon.EvaluateSample(x,z).Type == TerrainPatternType.Canyon, "Canyon-only climate weighting");
        }
        var sea = new ElevationPatternEvaluator(new ElevationPatternSettingsData(terrain));
        smooth = new TerrainPatternEvaluator(terrain, sea, smoothRules);
        canyon = new TerrainPatternEvaluator(terrain, sea, canyonRules);
        for (int z = -700; z < 700; z += 73)
        for (int x = -700; x < 700; x += 71)
        {
            var a = smooth.EvaluateSample(x,z); var b = canyon.EvaluateSample(x,z);
            Check(a.HasSeaPattern == b.HasSeaPattern && a.HasSecondarySeaPattern == b.HasSecondarySeaPattern
                && a.SeaRegionKey == b.SeaRegionKey && a.SeaInteriorProgress == b.SeaInteriorProgress,
                "climate weights cannot move ocean regions");
        }

        // Mesa is an upstream, seeded regional variant; no terrain result is consulted.
        var desertSettings = new ClimateSettings { ColdThreshold = 0, HotThreshold = .01f,
            DryThreshold = .95f, ForestThreshold = .96f, WetlandThreshold = .99f,
            AltitudeCoolingPerCell = 0, MountainMinimumHeight = 10000, MesaOccurrence = 1 };
        var mesaStore = new PatternMapStore();
        var mesa = new ClimatePatternMapReader(grid, mesaStore,
            new ElevationPatternMapReader(grid, mesaStore, new ElevationPatternSettingsData(terrain)), desertSettings);
        var mesaCell = mesa.GetClimateCell(-4, 3);
        Check(mesaCell.Biome == TerrainBiome.Desert && mesaCell.Variant == ClimateRegionVariant.Mesa, "Mesa climate variant");
        Check(mesa.GetTerrainRule(-4,3).Canyon > mesa.GetTerrainRule(-4,3).Smooth, "Mesa favors Canyon");
        Check(!mesaStore.TryGetTerrain(grid.GetKeyForCell(-4,3), out _), "Mesa does not require terrain");
        Console.WriteLine("PASS Elevation -> Climate -> weighted Terrain: no reverse builds, parallel/eviction, forced pattern weights, fixed oceans and seeded Mesa");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private sealed class FlatElevation : IElevationPatternMapReader
    {
        public float SeaLevel => 100;
        public float GetHeight(double x, double z) => 160;
        public bool IsSeaRegion(long x, long z) => false;
    }
    private sealed class FixedClimate : IClimatePatternMapReader
    {
        private readonly BiomeTerrainRule rule;
        public FixedClimate(BiomeTerrainRule rule) { this.rule = rule; }
        public ClimatePatternCell GetClimateCell(int x,int z) => new(new ElevationPatternCell(160), .8f,.1f,ClimateBiome.Warm,TerrainBiome.Desert);
        public BiomeHydrologyRule GetHydrologyRule(int x,int z) => new() { BasinOccurrence = 1, BasinArea = 1, RiverOccurrence = 1 };
        public BiomeTerrainRule GetTerrainRule(int x,int z) => rule;
    }
}
