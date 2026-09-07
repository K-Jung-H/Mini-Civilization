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
                    pair = new PatternTilePair(climate, hydrology);
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

        // Called by the scheduler only after all preparation jobs have completed.
        internal void Retain(ISet<PatternTileKey> required)
        {
            var bounds = new List<PatternTileBounds>();
            lock (gate)
            {
                if (terrainBuilds.Count != 0) return;
                var removed = false;
                foreach (var key in new List<PatternTileKey>(climateTiles.Keys))
                    if (!required.Contains(key)) removed |= climateTiles.Remove(key);
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
