using System;
using System.Collections.Generic;
using System.Linq;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.WaterFlow;

internal static class WaterSimulationChecks
{
    public static void Run()
    {
        Check(StreamingBatch(false).SequenceEqual(StreamingBatch(true)), "batched frontier final water equivalence");
        Console.WriteLine("PASS real water streaming batch: active wave detach/restore, frontier and final water equivalence");
        foreach (var surface in new[] { 13, 15, 16, 24 })
        foreach (var budget in new[] { 1, 4096 })
        {
            var world = Build((x, z) => z == 0 && x >= -2 && x <= 2
                ? PatternColumnHeights.Wet(surface - 3, surface)
                : PatternColumnHeights.Dry(surface + 1));
            RunToRest(world, budget, c => c.Z == 0 && c.X >= -2 && c.X <= 2
                && PatternColumnHeights.Wet(surface - 3, surface).WaterAt(c.Y) > 0);
        }

        // High Source at x=-1 supplies empty cells above a lower Source at x=0.
        // Side terrain contains the entire fall, not only the lower water surface.
        foreach (var high in new[] { 16, 24, 25, 26 })
        {
            var first = Fall(high, 1, false);
            var second = Fall(high, 4096, true);
            Check(first.SequenceEqual(second), "wave budget / chunk preparation determinism");
            Check(first.SequenceEqual(Fall(high,4096,false,true)), "simulation reactivation");
        }
        // Both water surfaces occupy y=2. The resolver preserves both Sources;
        // there is no empty Cell in which a Dynamic waterfall can be generated.
        var shallow = Build((x,z) => z != 0 || x < -2 || x > 2
            ? PatternColumnHeights.Dry(15)
            : PatternColumnHeights.Wet(10, x < 0 ? 14 : 12));
        RunToRest(shallow,4096,c => c.Z == 0 && c.X >= -2 && c.X <= 2 && c.Y == 2);
        Check(Coordinates(shallow).All(c => shallow.GetCell(c.X,c.Y,c.Z).Water.Role != WaterRole.Dynamic),
            "same-Y Source steps do not create Dynamic waterfalls");

        // Negative control: a lowered exterior is a real supply path. It must be
        // detected during waves, rather than declared safe from the initial map.
        var open = Build((x,z) => z == 0 && x >= -2 && x <= 2
            ? PatternColumnHeights.Wet(10,14) : PatternColumnHeights.Dry(10));
        var escaped = false;
        RunToRest(open,4096,c =>
        {
            if (c.Z != 0 || c.X < -2 || c.X > 2) escaped = true;
            return true;
        });
        Check(escaped, "negative control must detect actual exterior supply");
        Console.WriteLine("PASS real Materializer/WaterFlowResolver: partial Filled pools, Source preservation, contained falls, wave budgets and chunk order");
        Console.WriteLine("PASS negative controls: open bank spreads; same-Y Source step produces no Dynamic waterfall");
    }

    private static WaterData[] StreamingBatch(bool batch)
    {
        var world = Build((x,z) => z == 0 && x >= -2 && x <= 2
            ? PatternColumnHeights.Wet(x < 0 ? 21 : 7, x < 0 ? 24 : 10)
            : PatternColumnHeights.Dry(25));
        var state = new WaterFlowState(world, Array.Empty<WaterBody>());
        var enabled = true;
        var resolver = new WaterFlowResolver(world.ChunkSizeX, c => enabled);
        var cells = Coordinates(world).ToArray();
        resolver.RestoreFrontier(world, state, cells);
        var parameters = new WaterFlowParameters(world.WaterFlowRules);
        resolver.Step(world, state, parameters, 1, out _);
        Check(resolver.IsWaveInProgress, "streaming fixture has active wave");
        if (batch) resolver.BeginStreamingChanges();
        var detached = new List<CellCoordinate>();
        resolver.DetachChunkFrontier(world, state, new ChunkCoordinate(0,0), detached);
        Check(detached.Count > 0, "streaming fixture detaches real frontier");
        resolver.RestoreChunkFrontier(world, state, detached);
        enabled = false;
        resolver.OnSimulationSetChanged(world, state);
        enabled = true;
        resolver.OnSimulationSetChanged(world, state);
        if (batch) resolver.EndStreamingChanges(world, state);
        Check(world.WaterFlowSchedule.FrontierCells.SequenceEqual(cells.OrderBy(c => c)),
            "cancelled wave and detached frontier preserved exactly");
        var count = 0;
        while (resolver.HasWork)
        {
            Check(++count < 10000 && resolver.HasRunnableWork, "streaming batch settles");
            resolver.Step(world, state, parameters, 4096, out _);
        }
        return cells.Select(c => world.GetCell(c.X,c.Y,c.Z).Water).ToArray();
    }

    private static WaterData[] Fall(int high, int budget, bool reverse, bool pauseRight = false)
    {
        var world = Build((x, z) => z != 0 || x < -2 || x > 2
            ? PatternColumnHeights.Dry(high + 1)
            : x < 0 ? PatternColumnHeights.Wet(high - 3, high)
            : PatternColumnHeights.Wet(7, 10), reverse);
        Check(!world.GetCell(0, 2, 0).HasWater, "fall must start empty");
        RunToRest(world, budget, c => c.Z == 0 && c.X >= -2 && c.X <= 2
            && (c.X < 0 ? PatternColumnHeights.Wet(high - 3, high).WaterAt(c.Y) > 0
                : c.X == 0 ? c.Y >= 1 && c.Y < (high + 4) / 5
                : c.Y == 1), pauseRight);
        Check(world.GetCell(0, 2, 0).Water.Role == WaterRole.Dynamic,
            "fall reaches lower Source through initially empty cell");
        return Coordinates(world).Select(c => world.GetCell(c.X,c.Y,c.Z).Water).ToArray();
    }

    internal static void VerifyGenerated(Func<int,int,PatternColumnHeights> column, int height, int chunkCount)
    {
        var world = Build(column, false, height, chunkCount);
        RunToRest(world,4096,c =>
        {
            var own = column(c.X,c.Z);
            if (!own.HasWater) return false;
            var ceiling = own.WaterSurface;
            foreach (var offset in new[] { (-1,0), (1,0), (0,-1), (0,1) })
            {
                var adjacent = column(c.X+offset.Item1,c.Z+offset.Item2);
                if (adjacent.HasWater) ceiling = Math.Max(ceiling,adjacent.WaterSurface);
            }
            return c.Y >= own.Ground / 5 && c.Y < (ceiling+4)/5;
        });
        for (var z = world.Settings.MinimumCellCoordinate+1; z < world.Settings.MaximumCellCoordinateExclusive-1; z++)
        for (var x = world.Settings.MinimumCellCoordinate+1; x < world.Settings.MaximumCellCoordinateExclusive-1; x++)
        {
            var own = column(x,z);
            if (!own.HasWater) continue;
            var y = (own.WaterSurface-1)/5;
            foreach (var offset in new[] { (-1,0),(1,0),(0,-1),(0,1) })
            {
                var nx = x+offset.Item1; var nz = z+offset.Item2;
                var next = column(nx,nz);
                if (!next.HasWater || next.WaterSurface >= own.WaterSurface || next.WaterAt(y) > 0) continue;
                for (var fallY = y; fallY >= (next.WaterSurface+4)/5; fallY--)
                    Check(world.GetCell(nx,fallY,nz).Water.Role == WaterRole.Dynamic,
                        $"generated fall must connect at {nx},{fallY},{nz}");
            }
        }
    }

    private static WorldData Build(Func<int,int,PatternColumnHeights> column, bool reverse = false,
        int height = 8, int chunkCount = 3)
    {
        var settings = new WorldSettingsData(0, WorldType.Finite, 1, 4, height, chunkCount, 1, 1, 1, 100, WaterFlowRules.Default);
        var world = new WorldData(settings);
        var grid = new PatternTileGridSettingsData(settings, 1);
        var materializer = new PatternChunkMaterializer(grid);
        var chunks = (from z in Enumerable.Range(-chunkCount/2,chunkCount) from x in Enumerable.Range(-chunkCount/2,chunkCount)
                      select new ChunkCoordinate(x,z)).ToArray();
        if (reverse) Array.Reverse(chunks);
        var feature = HydrologyFeatureKey.FromIdentity(new WaterFeatureIdentity(HydrologyFeatureKind.River,0,0,0,0));
        foreach (var chunk in chunks)
        {
            var key = grid.GetKeyForChunk(chunk);
            var bounds = grid.GetCoreBounds(key);
            var terrain = new TerrainPatternCell[16];
            var hydro = new HydrologyPatternCell[16];
            for (var z = bounds.MinimumZ; z < bounds.MaximumZExclusive; z++)
            for (var x = bounds.MinimumX; x < bounds.MaximumXExclusive; x++)
            {
                var index = x-bounds.MinimumX + 4*(z-bounds.MinimumZ);
                var heights = column(x,z);
                terrain[index] = new TerrainPatternCell(default, 20, 0, 0);
                hydro[index] = HydrologyPatternCell.CreateResolved(heights,
                    heights.HasWater ? WaterType.River : WaterType.None, 0, 1, 0);
            }
            materializer.Materialize(world, chunk, new PatternTilePair(
                new ClimatePatternTile(new TerrainPatternTile(key,bounds,terrain),
                    new ClimateNoiseMap(1, "temperature", 512), new ClimateNoiseMap(1, "moisture", 640), new ClimateSettings()),
                new HydrologyPatternTile(key,bounds,new[] { feature },hydro)));
            for (var z = bounds.MinimumZ; z < bounds.MaximumZExclusive; z++)
            for (var x = bounds.MinimumX; x < bounds.MaximumXExclusive; x++)
            for (var y = 0; y < world.Height; y++)
            {
                var expected = column(x,z);
                var actual = world.GetCell(x,y,z);
                Check(actual.Terrain.SolidHeight == expected.SolidAt(y), "materialized Terrain Filled");
                Check(actual.WaterHeight == expected.WaterAt(y), "materialized Source occupancy");
                Check(!actual.HasWater || actual.Water.Role == WaterRole.Source, "initial Source only");
            }
        }
        return world;
    }

    private static void RunToRest(WorldData world, int budget, Func<CellCoordinate,bool> allowed,
        bool pauseRight = false)
    {
        var cells = Coordinates(world).ToArray();
        var sources = cells.Where(c => world.GetCell(c.X,c.Y,c.Z).HasWater)
            .ToDictionary(c => c, c => world.GetCell(c.X,c.Y,c.Z).Water);
        Check(sources.Values.All(w => w.Role == WaterRole.Source), "generation creates only Source");
        var state = new WaterFlowState(world, Array.Empty<WaterBody>());
        var resumed = !pauseRight;
        var resolver = new WaterFlowResolver(world.ChunkSizeX, c => resumed || c.X < 0);
        resolver.RestoreFrontier(world,state,cells);
        var parameters = new WaterFlowParameters(world.WaterFlowRules);
        var calls = 0;
        while (resolver.HasWork)
        {
            if (!resolver.HasRunnableWork)
            {
                Check(!resumed, "unprocessed frontier is not stability");
                resumed = true;
                resolver.OnSimulationSetChanged(world,state);
                continue;
            }
            Check(++calls < 100000, "simulation failed to settle");
            if (!resolver.Step(world,state,parameters,budget,out _)) continue;
            foreach (var c in cells)
            {
                var cell = world.GetCell(c.X,c.Y,c.Z);
                Check(cell.Terrain.SolidHeight + cell.WaterHeight <= 5, "Cell capacity");
                Check(!cell.HasWater || allowed(c), $"unexpected water at {c.X},{c.Y},{c.Z}");
                if (sources.TryGetValue(c,out var initial))
                    Check(cell.Water.Role == WaterRole.Source && cell.Water.Amount == initial.Amount,
                        "Source role and amount remain fixed");
            }
        }
        Check(!resolver.HasWork, "unprocessed frontier is not stability");
    }

    private static IEnumerable<CellCoordinate> Coordinates(WorldData world)
    {
        for (var z = world.Settings.MinimumCellCoordinate; z < world.Settings.MaximumCellCoordinateExclusive; z++)
        for (var x = world.Settings.MinimumCellCoordinate; x < world.Settings.MaximumCellCoordinateExclusive; x++)
        for (var y = 0; y < world.Height; y++) yield return new CellCoordinate(x,y,z);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
