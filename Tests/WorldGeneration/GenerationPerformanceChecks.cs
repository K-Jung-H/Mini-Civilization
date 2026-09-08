using System;
using System.Diagnostics;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
internal static class GenerationPerformanceChecks
{
    public static void Run(TerrainPatternSettings asset)
    {
        for(int pass=0;pass<4;pass++)
        {
            var world = new WorldSettingsData(17, WorldType.Infinite, 1,8,10,15,10,1,5,72,new WaterFlowRules(.01f,.05f,.05f));
            var data=asset.CreateData(17); var grid=new PatternTileGridSettingsData(world,1);
            var store=new PatternMapStore(); var elevation=new ElevationPatternMapReader(grid,store,new ElevationPatternSettingsData(data));
            var climate=new ClimatePatternMapReader(grid,store,elevation,new ClimateSettings());
            var builder=new TerrainPatternTileBuilder(grid,data,elevation,climate);
            long bytes=GC.GetAllocatedBytesForCurrentThread();var clock=Stopwatch.StartNew();ulong hash=14695981039346656037UL;
            for(int z=-8;z<8;z++) for(int x=-8;x<8;x++)
            {
                var key=new PatternTileKey(x,z);var tile=store.GetOrBuildTerrain(key,builder);
                for(int cz=tile.Bounds.MinimumZ;cz<tile.Bounds.MaximumZExclusive;cz++)
                for(int cx=tile.Bounds.MinimumX;cx<tile.Bounds.MaximumXExclusive;cx++)
                {var c=tile.GetCell(cx,cz);unchecked {hash=(hash^(uint)BitConverter.SingleToInt32Bits(c.SurfaceHeight))*1099511628211UL;hash=(hash^(uint)c.Type)*1099511628211UL;}}
                store.Retain(new HashSet<PatternTileKey>{key});
            }
            if (hash != 2828471048438981871UL) throw new InvalidOperationException("Terrain output differs from pre-optimization baseline.");
            clock.Stop();bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Console.WriteLine($"PERF pass={pass} ms={clock.Elapsed.TotalMilliseconds:F1} allocated={bytes} hash={hash}");
        }
    }
}
