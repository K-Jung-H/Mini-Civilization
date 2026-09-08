using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using MiniCivilization.World.WaterFlow;
using MiniCivilization.World.Meshing;

internal static class StreamingRuntimeChecks
{
    static void Main()
    {
        var paths = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "references.txt"));
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = paths.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name.Name);
            return path == null ? null : context.LoadFromAssemblyPath(path);
        };
        Run();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static WorldData NewWorld(int size = 4) => new(new WorldSettingsData(0, WorldType.Infinite,
        1, size, 8, 3, 4, 1, 5, 72, new WaterFlowRules(.01f, .05f, .05f)));

    static CellData Ground(bool water) => new()
    {
        Biome = new CellBiome(ClimateBiome.Temperate, TerrainBiome.Field, water ? WaterBiome.Lake : WaterBiome.None),
        Terrain = new TerrainData { Material = MaterialType.Soil, Geology = MaterialType.Soil, SolidHeight = 3, Surface = SurfaceType.Ground },
        Water = water ? new WaterData { Amount = WaterAmount.FromRenderFill(2, 2), Role = WaterRole.Source, Type = WaterType.Lake } : default
    };

    static void Add(WorldRuntime runtime, ChunkCoordinate chunk, bool wet)
    {
        runtime.BeginChunkPreparation(chunk);
        runtime.Data.EnsureChunkLoaded(chunk);
        var n = runtime.Data.ChunkSizeX;
        for (var z = chunk.Z * n; z < (chunk.Z + 1) * n; z++)
        for (var x = chunk.X * n; x < (chunk.X + 1) * n; x++)
            runtime.Data.SetCellBulk(x, 0, z, Ground(wet));
        runtime.CompleteChunkPreparation(chunk, Array.Empty<CellCoordinate>());
        runtime.ActivateChunk(chunk);
    }

    static string[] Canonical(IReadOnlyList<WaterBody> bodies) => bodies.Select(b =>
        $"{b.VolumeUnits}/{b.SurfaceCellCount}/{b.TouchesWorldEdge}:" + string.Join(";", b.Cells.OrderBy(c => c).Select(c => $"{c.X},{c.Y},{c.Z}")))
        .OrderBy(s => s, StringComparer.Ordinal).ToArray();

    static void Equivalent(WorldRuntime runtime)
    {
        var expected = WaterBodyResolver.ResolvePrepared(runtime);
        Check(Canonical(expected).SequenceEqual(Canonical(runtime.WaterFlowState.WaterBodies)), "Incremental water differs from full connected-component resolve");
        foreach (var body in runtime.WaterFlowState.WaterBodies)
        foreach (var cell in body.Cells)
            Check(runtime.WaterFlowState.TryGetWaterBody(cell.X, cell.Z, out var indexed) && ReferenceEquals(indexed, body), "Body index stale");
    }

    static void Run()
    {
        var runtime = WorldRuntime.Create(NewWorld());
        foreach (var x in new[] { -1, 1, 0, 8 }) { Add(runtime, new ChunkCoordinate(x, 0), true); Equivalent(runtime); }
        var unaffected = runtime.WaterFlowState.WaterBodies.Single(b => b.Cells[0].X >= 32);
        runtime.BeginStreamingChanges();
        runtime.ReleaseChunk(new ChunkCoordinate(0, 0), true);
        runtime.EndStreamingChanges();
        Equivalent(runtime);
        Check(runtime.WaterFlowState.WaterBodies.Count == 3, "Bridge removal must split lake");
        Check(runtime.WaterFlowState.WaterBodies.Contains(unaffected), "Unrelated body rebuilt");
        Add(runtime, default, true); Equivalent(runtime);
        Check(runtime.WaterFlowState.WaterBodies.Count == 2, "Bridge reload must merge lake");
        var random = new Random(1623);
        for (var i = 0; i < 100; i++)
        {
            runtime.BeginStreamingChanges();
            for (var operation = 0; operation < 3; operation++)
            {
                var chunk = new ChunkCoordinate(random.Next(-3, 4), random.Next(-2, 3));
                if (runtime.Data.IsChunkLoaded(chunk)) runtime.ReleaseChunk(chunk, true);
                else Add(runtime, chunk, random.Next(3) != 0);
            }
            runtime.EndStreamingChanges();
            Equivalent(runtime);
        }
        Console.WriteLine("PASS production runtime: 100 mixed load/unload batches, bridge split/merge, negative coordinates, unrelated body retained");
        AllocationCheck();
        MeshAndCacheCheck();
        DryStreamingCheck();
        DependencyCheck();
        MaterialCacheCheck();
    }

    static void AllocationCheck()
    {
        var cell = Ground(false);
        for (var i = 0; i < 100; i++) { cell.Normalize(); _ = cell.Biome.IsValid; }
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            cell.Biome = new CellBiome(ClimateBiome.Temperate, TerrainBiome.Field, WaterBiome.None);
            cell.Normalize();
            if (!cell.Biome.IsValid) throw new Exception();
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == start, "Cell validation allocates");
        for (var i = 0; i < 256; i++)
        {
            bool valid;
            try { _ = new CellBiome((ClimateBiome)i, TerrainBiome.Field, WaterBiome.None); valid = true; }
            catch (ArgumentOutOfRangeException) { valid = false; }
            Check(valid == Enum.IsDefined(typeof(ClimateBiome), (ClimateBiome)i), "Enum validation changed");
        }
        Console.WriteLine("PASS 10,000 Cell construction/normalize/validation: zero managed allocation; invalid enum rejection preserved");
    }

    static void MeshAndCacheCheck()
    {
        var runtime = WorldRuntime.Create(NewWorld(8));
        Add(runtime, default, false);
        var world = runtime.Data;
        var query = new WorldSurfaceQuery(world);
        var exposure = new WorldExposureCache(world);
        exposure.PrepareChunk(default);
        var buffers = new MeshBuffers();
        var cells = new List<ExposedCell>();
        TerrainChunkMeshBuilder.Build(world, 0, 0, 8, null, query, exposure, buffers, cells);
        // All interior top faces are coplanar. Sides and bevels at the loaded boundary remain intact.
        Check(buffers.TriangleCount > 0 && buffers.TriangleCount < 8 * 8 * 18, "Flat terrain fast path not used");
        var watch = Stopwatch.StartNew();
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) TerrainChunkMeshBuilder.Build(world, 0, 0, 8, null, query, exposure, buffers, cells);
        Console.WriteLine($"PERF terrain 8x8: {watch.Elapsed.TotalMilliseconds / 20:F3} ms/build, {(GC.GetAllocatedBytesForCurrentThread()-start)/20} bytes/build, {buffers.TriangleCount} triangles");
        Add(runtime, new ChunkCoordinate(1, 0), true);
        exposure.RebuildPreparedNeighborBoundaries(new ChunkCoordinate(1, 0));
        query.InvalidateChunk(new ChunkCoordinate(1, 0), 8);
        CompareFresh();
        runtime.ReleaseChunk(new ChunkCoordinate(1, 0), true);
        exposure.RebuildPreparedNeighborBoundaries(new ChunkCoordinate(1, 0));
        query.InvalidateChunk(new ChunkCoordinate(1, 0), 8);
        CompareFresh();
        var freshExposure = new WorldExposureCache(world);
        freshExposure.PrepareChunk(default);
        var expectedCells = new List<ExposedCell>();
        var actualCells = new List<ExposedCell>();
        freshExposure.CopySolidCellsForPatch(0, 0, 8, 8, expectedCells);
        exposure.CopySolidCellsForPatch(0, 0, 8, 8, actualCells);
        Check(expectedCells.OrderBy(c => c.Coordinate).SequenceEqual(actualCells.OrderBy(c => c.Coordinate)), "Unload did not expose neighbor faces");
        Console.WriteLine("PASS cached terrain profiles match fresh resolution after adjacent water load/unload");

        void CompareFresh()
        {
            var fresh = new WorldSurfaceQuery(world);
            for (var z = 0; z < 8; z++)
            for (var x = 0; x < 8; x++)
                Check(query.ResolveSolid(x, 0, z).Equals(fresh.ResolveSolid(x, 0, z)), "Stale boundary profile");
        }
    }

    static void DryStreamingCheck()
    {
        var runtime = WorldRuntime.Create(NewWorld(8));
        runtime.BeginStreamingChanges();
        for (var z = -8; z < 8; z++)
        for (var x = -8; x < 8; x++) Add(runtime, new ChunkCoordinate(x, z), false);
        runtime.EndStreamingChanges();
        var changed = new[] { new ChunkCoordinate(-9, 0) };
        var scratch = new WaterBodyResolver.StreamingScratch();
        WaterBodyResolver.RefreshStreaming(runtime, changed, scratch);
        WaterBodyResolver.ResolvePrepared(runtime);
        Measure("dry 256 chunks, full water rescan", () => WaterBodyResolver.ResolvePrepared(runtime));
        Measure("dry 256 chunks, changed chunk refresh", () => WaterBodyResolver.RefreshStreaming(runtime, changed, scratch));
        Check(scratch.Visited.Count == 0 && scratch.Seeds.Count == 0, "Dry unload scanned unchanged columns");

        static void Measure(string name, Action action)
        {
            var watch = Stopwatch.StartNew();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10; i++) action();
            Console.WriteLine($"PERF {name}: {watch.Elapsed.TotalMilliseconds / 10:F3} ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 10} bytes");
        }
    }

    static void DependencyCheck()
    {
        var runtime = WorldRuntime.Create(NewWorld(8));
        var world = runtime.Data;
        Add(runtime, default, false);
        var dependencies = new MiniCivilization.World.Presentation.StreamingPatchDependencies();
        dependencies.CaptureStreamingDependencies(world, 0, 0, 8);
        var neighbor = new ChunkCoordinate(-1, 0);
        Check(!dependencies.HasStreamingDependencyChanged(world, default), "Unchanged active data should not rebuild");
        Add(runtime, neighbor, false);
        Check(dependencies.HasStreamingDependencyChanged(world, neighbor), "New neighbor must rebuild seam");
        dependencies.CaptureStreamingDependencies(world, 0, 0, 8);
        Check(!dependencies.HasStreamingDependencyChanged(world, neighbor), "Already sampled neighbor must not rebuild again");
        runtime.ReleaseChunk(neighbor, true);
        Check(dependencies.HasStreamingDependencyChanged(world, neighbor), "Removed neighbor must rebuild seam");
        Add(runtime, neighbor, false);
        Check(dependencies.HasStreamingDependencyChanged(world, neighbor), "Reload at same coordinate must not reuse stale dependency");
        Check(!dependencies.HasStreamingDependencyChanged(world, new ChunkCoordinate(10, 10)), "Unrelated chunk affects seam");
        dependencies.ClearStreamingDependencies();
        Check(!dependencies.HasStreamingDependencyChanged(world, neighbor), "Pool release retains dependencies");
        Console.WriteLine("PASS boundary dependencies: unchanged/pre-sampled neighbors skipped; unload/reload and negative coordinates invalidate");
    }

    static void MaterialCacheCheck()
    {
        var runtime = WorldRuntime.Create(NewWorld(8));
        Add(runtime, default, false);
        var world = runtime.Data;
        var cell = Ground(false);
        cell.Biome = new CellBiome(ClimateBiome.Warm, TerrainBiome.Desert, WaterBiome.None);
        world.SetCellBulk(2, 0, 2, cell);
        var materials = new TerrainCellMaterials();
        foreach (var x in new[] { 1, 2, 3 })
        {
            materials.BeginCell(world, null, x, 0, 2);
            foreach (var u in new[] { 0f, .2f, .8f, 1f, .3f })
            foreach (var v in new[] { 0f, .2f, .8f, 1f, .7f })
            foreach (var surface in new SurfaceType?[] { null, SurfaceType.Cliff })
            {
                var expected = MaterialBlendResolver.ResolveTerrainCell(world, null, x, 0, 2, u, v, surface);
                Check(materials.Resolve(u, v, surface).Equals(expected), "Cached material differs across biome/override/cell change");
                Check(materials.Resolve(u, v, surface).Equals(expected), "Repeated cached material differs");
            }
            Check(!materials.HasUniformTop(), "Biome seam incorrectly takes uniform fast path");
        }
        Console.WriteLine("PASS material cache: biome boundaries, cliff overrides, non-grid vertices, per-cell reset");
    }
}
