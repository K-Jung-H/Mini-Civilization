using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniCivilization.World.Generation.Patterns
{
    public sealed class PatternMapStore
    {
        private sealed class TerrainBuild
        {
            public readonly TaskCompletionSource<TerrainPatternTile> Completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly object gate = new();
        private readonly Dictionary<PatternTileKey, TerrainPatternTile>
            terrainTiles = new();
        private readonly Dictionary<PatternTileKey, HydrologyPatternTile>
            hydrologyTiles = new();
        private readonly Dictionary<PatternTileKey, TerrainBuild>
            terrainBuilds = new();
        private long revision;
        private readonly Dictionary<PatternTileKey, Lazy<ElevationPatternTile>> elevationTiles = new();
        public ElevationPatternTile GetOrBuildElevation(PatternTileKey key, Func<ElevationPatternTile> build)
        {
            Lazy<ElevationPatternTile> entry;
            lock (gate)
            {
                if (!elevationTiles.TryGetValue(key, out entry))
                {
                    entry = new Lazy<ElevationPatternTile>(() => {
                        var tile = build();
                        lock (gate) revision++;
                        return tile;
                    }, LazyThreadSafetyMode.ExecutionAndPublication);
                    elevationTiles.Add(key, entry);
                }
            }
            try { return entry.Value; }
            catch
            {
                lock (gate)
                    if (elevationTiles.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                        elevationTiles.Remove(key);
                throw;
            }
        }
        public bool TryGetElevation(PatternTileKey key, out ElevationPatternTile tile)
        {
            lock (gate)
            {
                if (elevationTiles.TryGetValue(key, out var entry) && entry.IsValueCreated)
                { tile = entry.Value; return true; }
            }
            tile = null; return false;
        }

        private readonly Dictionary<PatternTileKey, Lazy<ClimatePatternTile>> climateTiles = new();
        public ClimatePatternTile GetOrBuildClimate(PatternTileKey key, Func<ClimatePatternTile> build)
        {
            Lazy<ClimatePatternTile> entry;
            lock (gate)
            {
                if (!climateTiles.TryGetValue(key, out entry))
                {
                    entry = new Lazy<ClimatePatternTile>(() => {
                        var tile = build();
                        lock (gate) revision++;
                        return tile;
                    }, LazyThreadSafetyMode.ExecutionAndPublication);
                    climateTiles.Add(key, entry);
                }
            }
            try { return entry.Value; }
            catch
            {
                lock (gate)
                    if (climateTiles.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                        climateTiles.Remove(key);
                throw;
            }
        }
        public bool TryGetClimate(PatternTileKey key, out ClimatePatternTile tile)
        {
            lock (gate)
            {
                if (climateTiles.TryGetValue(key, out var entry) && entry.IsValueCreated)
                { tile = entry.Value; return true; }
            }
            tile = null; return false;
        }

        internal WaterBrushCatalog WaterBrushes { get; } = new();

        public long Revision
        {
            get
            {
                lock (gate)
                {
                    return revision;
                }
            }
        }

        public int TerrainTileCount
        {
            get
            {
                lock (gate)
                {
                    return terrainTiles.Count;
                }
            }
        }

        public int HydrologyTileCount
        {
            get
            {
                lock (gate)
                {
                    return hydrologyTiles.Count;
                }
            }
        }

        public bool TryGetTerrain(
            PatternTileKey key,
            out TerrainPatternTile tile)
        {
            lock (gate)
            {
                return terrainTiles.TryGetValue(key, out tile);
            }
        }

        public bool TryGetHydrology(
            PatternTileKey key,
            out HydrologyPatternTile tile)
        {
            lock (gate)
            {
                return hydrologyTiles.TryGetValue(key, out tile);
            }
        }

        public bool TryGetPair(PatternTileKey key, out PatternTilePair pair)
        {
            lock (gate)
            {
                if (terrainTiles.TryGetValue(key, out var terrain)
                    && hydrologyTiles.TryGetValue(key, out var hydrology)
                    && TryGetClimate(key, out var climate))
                {
                    pair = new PatternTilePair(climate, terrain, hydrology);
                    return true;
                }
            }

            pair = default;
            return false;
        }

        public TerrainPatternTile GetOrBuildTerrain(
            PatternTileKey key,
            TerrainPatternTileBuilder builder,
            CancellationToken cancellationToken = default)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            TerrainBuild build;
            var buildHere = false;
            lock (gate)
            {
                if (terrainTiles.TryGetValue(key, out var tile))
                {
                    return tile;
                }

                if (!terrainBuilds.TryGetValue(key, out build))
                {
                    build = new TerrainBuild();
                    terrainBuilds.Add(key, build);
                    buildHere = true;
                }
            }

            if (buildHere)
            {
                try
                {
                    var tile = builder.Build(key, cancellationToken);
                    lock (gate)
                    {
                        terrainTiles.Add(key, tile);
                        terrainBuilds.Remove(key);
                        revision++;
                    }

                    build.Completion.TrySetResult(tile);
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        terrainBuilds.Remove(key);
                    }

                    build.Completion.TrySetException(new InvalidOperationException(
                        $"Terrain Pattern Tile {key} could not be sealed.", exception));
                }
            }
            else
            {
                try
                {
                    build.Completion.Task.Wait(cancellationToken);
                }
                catch (AggregateException)
                {
                    // Read the original failure through the common result path below.
                }
            }

            return build.Completion.Task.GetAwaiter().GetResult();
        }

        public void SealHydrology(HydrologyPatternTile tile)
        {
            if (tile == null)
            {
                throw new ArgumentNullException(nameof(tile));
            }

            lock (gate)
            {
                if (!terrainTiles.TryGetValue(tile.Key, out var terrain))
                {
                    throw new InvalidOperationException(
                        "Hydrology Pattern Tile requires its sealed Terrain Pattern Tile.");
                }

                PatternTileComposition.ValidatePair(terrain, tile);
                if (!hydrologyTiles.ContainsKey(tile.Key))
                {
                    hydrologyTiles.Add(tile.Key, tile);
                    revision++;
                }
            }
        }

        // Bound auxiliary caches by total Cells rather than tile count (tile spans are configurable).
        private static bool RetainAuxiliary<T>(Dictionary<PatternTileKey, Lazy<T>> tiles, ISet<PatternTileKey> required)
        {
            const int maximumAuxiliaryCells = 16384;
            int keptCells = 0;
            bool removed = false;
            var keys = new List<PatternTileKey>(tiles.Keys);
            // Always retain explicit demand; the remaining dependency budget is bounded.
            for (int i=keys.Count-1;i>=0;i--)
            {
                var key=keys[i];
                if (required.Contains(key)) continue;
                var entry=tiles[key];
                int cells=0;
                if (entry.IsValueCreated)
                {
                    if (entry.Value is ElevationPatternTile e) cells=e.CellCount;
                    else if (entry.Value is ClimatePatternTile c) cells=c.Elevation.CellCount;
                }
                if (required.Count != 0 && cells > 0 && cells <= maximumAuxiliaryCells-keptCells)
                { keptCells+=cells; continue; }
                removed |= tiles.Remove(key);
            }
            return removed;
        }

        // Called by the scheduler only after all preparation jobs have completed.
        internal void Retain(ISet<PatternTileKey> required)
        {
            var bounds = new List<PatternTileBounds>();
            lock (gate)
            {
                if (terrainBuilds.Count != 0) return;
                var removed = false;
                removed |= RetainAuxiliary(elevationTiles, required);
                removed |= RetainAuxiliary(climateTiles, required);
                foreach (var key in new List<PatternTileKey>(hydrologyTiles.Keys))
                    if (!required.Contains(key)) removed |= hydrologyTiles.Remove(key);
                foreach (var key in new List<PatternTileKey>(terrainTiles.Keys))
                    if (!required.Contains(key)) removed |= terrainTiles.Remove(key);
                foreach (var tile in terrainTiles.Values)
                    bounds.Add(HydrologyHeightSolver.ReadBounds(tile.Bounds));
                if (removed) revision++;
            }
            WaterBrushes.Retain(bounds);
        }
    }

    public sealed class TerrainPatternMapReader : ITerrainPatternMapReader
    {
        private readonly PatternTileGridSettingsData grid;
        private readonly PatternMapStore store;
        private readonly TerrainPatternTileBuilder builder;

        public TerrainPatternMapReader(
            PatternTileGridSettingsData grid,
            PatternMapStore store,
            TerrainPatternTileBuilder builder)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public TerrainPatternCell GetCell(int absoluteX, int absoluteZ)
        {
            var key = grid.GetKeyForCell(absoluteX, absoluteZ);
            var tile = store.GetOrBuildTerrain(key, builder);
            return tile.GetCell(absoluteX, absoluteZ);
        }
    }
}
