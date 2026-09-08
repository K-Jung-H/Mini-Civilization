using System;
using System.Collections.Generic;
using System.Linq;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.Runtime;
using MiniCivilization.World.Persistence;

// Tests the production coordinator with instrumented lifecycle endpoints.
// Cache/renderer/native Unity work is deliberately not simulated here.
internal static class StreamingChecks
{
    internal static void Run(TerrainPatternSettings terrain, HydrologyFeatureSettings hydrology)
    {
        var world = new WorldSettingsData(0, WorldType.Infinite, 1, 8, 10, 15, 10, 1, 5, 72,
            new WaterFlowRules(.01f, .05f, .05f));
        var t = terrain.CreateData(0);
        var config = new WorldGenerationConfiguration(world, t, hydrology.CreateData(world),
            new PatternTileGridSettingsData(world, t.PatternTileChunkSpan), 0, 1, 1, 3, 2, 1, 2, 2);
        var runtime = new WorldRuntime(new WorldData(world));
        var persistence = new WorldPersistenceService(runtime);
        using var coordinator = new PatternStreamingCoordinator(runtime, config, persistence);
        coordinator.Update(default);
        Check(runtime.Prepared.Count == 3 && runtime.Activated.Count == 1, "independent prepare/activate limits");
        Check(runtime.Activated[0].Equals(default(ChunkCoordinate)), "nearest activation first");
        for (var i = 0; i < 8; i++) coordinator.Update(default);
        Check(coordinator.Progress.Equals(new WorldStreamingProgress(9, 9)), "stationary queue drains");
        var preparedBefore = runtime.Prepared.Count;
        var old = runtime.ChunkRuntimes.Keys.ToArray();
        var target = new ChunkCoordinate(3, 0);
        coordinator.Update(target);
        Check(runtime.Unloaded.Count == 2 && runtime.Unloaded.All(c => c.X == -1 && Math.Abs(c.Z) == 1), "farthest unload first and bounded");
        Check(old.Where(c => runtime.ChunkRuntimes.ContainsKey(c)).All(c => !runtime.ChunkRuntimes[c].SimulationEnabled), "outside simulation disabled before queued unload");
        var retained = new ChunkCoordinate(0,0);
        coordinator.Update(default);
        Check(runtime.GetChunkState(retained) == ChunkState.Active, "return cancels pending unload");
        Check(runtime.Unloaded.Count == 4 && runtime.Unloaded.Skip(2).All(c => c.X >= 2), "reversed target replaces unload demand");
        Check(runtime.Prepared.Skip(preparedBefore).All(c => !c.Equals(retained)), "retained active chunk not prepared again");
        Check(persistence.FlushCount == 2, "save state committed once per unload batch");
        Console.WriteLine("PASS production streaming coordinator: independent limits, nearest activation, farthest unload, target reversal, immediate simulation exclusion, batch save");
        var delayedRuntime = new WorldRuntime(new WorldData(world));
        var delayedStore = new WorldPersistenceService(delayedRuntime);
        var nearestLoad = new System.Threading.Tasks.TaskCompletionSource<Chunk>();
        delayedStore.Loader = c => c.Equals(default(ChunkCoordinate)) ? nearestLoad.Task : System.Threading.Tasks.Task.FromResult<Chunk>(null);
        using var delayed = new PatternStreamingCoordinator(delayedRuntime, config, delayedStore);
        for (var i = 0; i < 100; i++) delayed.Update(default);
        Check(delayedRuntime.Activated.Count == 0, "far completion cannot overtake delayed nearest load");
        Check(delayedStore.LoadCalls.Count <= config.MapBuildConcurrency && delayedStore.LoadCalls.Values.All(n => n == 1), "pending loads bounded and never queried repeatedly");
        nearestLoad.SetResult(null);
        var drain = System.Diagnostics.Stopwatch.StartNew();
        while (delayedRuntime.Activated.Count < 9 && drain.Elapsed.TotalSeconds < 20) { delayed.Update(default); System.Threading.Thread.Yield(); }
        Check(delayedRuntime.Activated.Count == 9 && delayedStore.LoadCalls.Values.All(n => n == 1), "negative save lookup performed once per chunk");
        Check(delayedRuntime.Activated.SequenceEqual(ChunkDemand.Build(delayedRuntime.Data, default, 1)), "reversed completion retains nearest activation order");
        Console.WriteLine("PASS delayed persistence: bounded admission, one negative lookup, nearest activation despite reversed completion");
        var generated = new WorldRuntime(new WorldData(world));
        using var asyncCoordinator = new PatternStreamingCoordinator(generated, config);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (generated.Activated.Count < 9 && deadline.Elapsed.TotalSeconds < 20)
        {
            asyncCoordinator.Update(default);
            System.Threading.Thread.Yield();
        }
        Check(generated.Activated.SequenceEqual(ChunkDemand.Build(generated.Data, default, 1)), "async activation preserves full distance order");
        Check(generated.Activated.Count == 9, "asynchronous materialization drains actual generated chunks");
        Check(generated.Prepared.All(c => generated.Data.IsChunkLoaded(c)), "only complete worker buffers become ready");
        asyncCoordinator.Update(new ChunkCoordinate(12, 12));
        asyncCoordinator.Update(default);
        Check(generated.ChunkRuntimes.Keys.All(c => Math.Abs(c.X) <= 1 && Math.Abs(c.Z) <= 1), "obsolete jobs not attached after target reversal");
        Console.WriteLine("PASS asynchronous Cell worker: actual maps, completion, target reversal");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}

namespace MiniCivilization.World.Runtime
{
    public sealed class WorldRuntime
    {
        public WorldData Data { get; }
        public PatternMapStore PatternMaps { get; } = new();
        public Dictionary<ChunkCoordinate, ChunkRuntime> ChunkRuntimes { get; } = new();
        public List<ChunkCoordinate> Prepared { get; } = new();
        public List<ChunkCoordinate> Activated { get; } = new();
        public List<ChunkCoordinate> Unloaded { get; } = new();
        public WorldRuntime(WorldData data) { Data = data; }
        public ChunkState GetChunkState(ChunkCoordinate c) => ChunkRuntimes.TryGetValue(c,out var r) ? r.State : ChunkState.Unloaded;
        public void BeginStreamingChanges() { }
        public void EndStreamingChanges() { }
        public void BeginChunkPreparation(ChunkCoordinate c)
        { var r = new ChunkRuntime(c); r.SetState(ChunkState.Preparing); ChunkRuntimes.Add(c,r); }
        public void CompleteChunkPreparation(ChunkCoordinate c, IReadOnlyList<CellCoordinate> sources)
        { ChunkRuntimes[c].SetState(ChunkState.Ready); Prepared.Add(c); }
        public void ActivateChunk(ChunkCoordinate c)
        { if (GetChunkState(c) != ChunkState.Ready) throw new InvalidOperationException(); ChunkRuntimes[c].SetState(ChunkState.Active); Activated.Add(c); }
        public void SetChunkSimulationEnabled(ChunkCoordinate c, bool enabled)
        { if (ChunkRuntimes.TryGetValue(c,out var r)) r.SetSimulationEnabled(enabled); }
        public void ReleaseChunk(ChunkCoordinate c, bool unloadWorldData)
        { if (!unloadWorldData) throw new InvalidOperationException(); Data.UnloadChunk(c); ChunkRuntimes.Remove(c); Unloaded.Add(c); }
    }
}
namespace MiniCivilization.World.Persistence
{
    internal sealed class WorldPersistenceService
    {
        private readonly WorldRuntime runtime;
        private bool dirty;
        internal int FlushCount;
        internal Func<ChunkCoordinate, System.Threading.Tasks.Task<Chunk>> Loader;
        internal readonly Dictionary<ChunkCoordinate, int> LoadCalls = new();
        internal bool CanAcceptWrites => true;
        internal WorldPersistenceService(WorldRuntime runtime) { this.runtime = runtime; }
        internal System.Threading.Tasks.Task<Chunk> LoadChunkAsync(ChunkCoordinate c)
        {
            LoadCalls[c] = LoadCalls.TryGetValue(c, out var count) ? count + 1 : 1;
            if (Loader != null) return Loader(c);
            var isolated = new WorldData(runtime.Data.Settings);
            isolated.EnsureChunkLoaded(c);
            isolated.TryGetChunk(c, out var chunk);
            return System.Threading.Tasks.Task.FromResult(chunk);
        }
        internal bool TryLoadChunk(ChunkCoordinate c) { runtime.Data.EnsureChunkLoaded(c); return true; }
        internal void MarkDirty(ChunkCoordinate c) { }
        internal void RestoreWaterFrontier(ChunkCoordinate c) { }
        internal void RestoreAvailableEntities() { }
        internal void SaveAndDetachChunk(ChunkCoordinate c) { dirty = true; }
        internal void FlushDetachedChunks() { if (dirty) FlushCount++; dirty = false; }
    }
}
