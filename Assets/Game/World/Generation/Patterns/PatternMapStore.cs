using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MiniCivilization.World.Generation.Patterns
{
    public interface ITerrainPatternMapReader
    {
        TerrainPatternCell GetCell(int absoluteX, int absoluteZ);
    }

    public sealed class PatternMapStore
    {
        private sealed class TerrainBuild
        {
            public readonly TaskCompletionSource<TerrainPatternTile> Completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TerrainPatternTile Tile;
            public Exception Failure;
        }

        private readonly object gate = new();
        private readonly Dictionary<PatternTileKey, TerrainPatternTile>
            terrainTiles = new();
        private readonly Dictionary<PatternTileKey, HydrologyPatternTile>
            hydrologyTiles = new();
        private readonly Dictionary<PatternTileKey, TerrainBuild>
            terrainBuilds = new();
        private long revision;

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
                    && hydrologyTiles.TryGetValue(key, out var hydrology))
                {
                    pair = new PatternTilePair(terrain, hydrology);
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
                        build.Tile = tile;
                        revision++;
                    }

                    build.Completion.TrySetResult(tile);
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        terrainBuilds.Remove(key);
                        build.Failure = exception;
                    }

                    build.Completion.TrySetException(exception);
                }
            }
            else
            {
                build.Completion.Task.Wait(cancellationToken);
            }

            if (build.Failure != null)
            {
                throw new InvalidOperationException(
                    $"Terrain Pattern Tile {key} could not be sealed.",
                    build.Failure);
            }

            return build.Tile ?? throw new InvalidOperationException(
                $"Terrain Pattern Tile {key} did not produce a result.");
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
