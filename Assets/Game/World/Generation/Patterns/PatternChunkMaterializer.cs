using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    public readonly struct PatternTilePair
    {
        public PatternTilePair(
            ClimatePatternTile climate,
            TerrainPatternTile terrain,
            HydrologyPatternTile hydrology)
        {
            if (climate == null) throw new ArgumentNullException(nameof(climate));
            PatternTileComposition.ValidatePair(terrain, hydrology);
            if (!climate.Key.Equals(terrain.Key) || !climate.Bounds.Equals(terrain.Bounds))
                throw new ArgumentException("Climate and Terrain must share a tile core.");
            Terrain = terrain;
            Climate = climate;
            Hydrology = hydrology;
        }

        public ClimatePatternTile Climate { get; }
        public TerrainPatternTile Terrain { get; }
        public HydrologyPatternTile Hydrology { get; }
    }

    internal readonly struct ChunkMaterializationResult
    {
        public ChunkMaterializationResult(
            ChunkCoordinate coordinate,
            IReadOnlyList<CellCoordinate> sourceCells)
        {
            Coordinate = coordinate;
            SourceCells = sourceCells ?? throw new ArgumentNullException(
                nameof(sourceCells));
        }

        public ChunkCoordinate Coordinate { get; }
        public IReadOnlyList<CellCoordinate> SourceCells { get; }
    }

    internal sealed class PatternChunkMaterializer
    {
        private readonly PatternTileGridSettingsData grid;

        public PatternChunkMaterializer(PatternTileGridSettingsData grid)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
        }

        public ChunkMaterializationResult Materialize(
            WorldData world,
            ChunkCoordinate coordinate,
            in PatternTilePair tile)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!ReferenceEquals(world.Settings, grid.World))
            {
                throw new ArgumentException(
                    "Chunk materialization requires the Pattern Tile world settings.",
                    nameof(world));
            }

            if (!world.IsChunkWithinBounds(coordinate))
            {
                throw new ArgumentOutOfRangeException(nameof(coordinate));
            }

            if (world.IsChunkLoaded(coordinate))
            {
                throw new InvalidOperationException(
                    $"Chunk {coordinate} already has materialized WorldData.");
            }

            var expectedTileKey = grid.GetKeyForChunk(coordinate);
            if (!tile.Terrain.Key.Equals(expectedTileKey)
                || !tile.Hydrology.Key.Equals(expectedTileKey))
            {
                throw new ArgumentException(
                    "Chunk materialization requires its intersecting Pattern Tile.",
                    nameof(tile));
            }

            var startX = checked(coordinate.X * world.ChunkSizeX);
            var startZ = checked(coordinate.Z * world.ChunkSizeZ);
            var sourceCells = new List<CellCoordinate>();
            world.EnsureChunkLoaded(coordinate);

            for (var localZ = 0; localZ < world.ChunkSizeZ; localZ++)
            for (var localX = 0; localX < world.ChunkSizeX; localX++)
            {
                var x = checked(startX + localX);
                var z = checked(startZ + localZ);
                WriteColumn(
                    world,
                    tile,
                    x,
                    z,
                    sourceCells);
            }

            // Source roles remain in every water cell; only runnable sources enter the work list.
            for (var index = sourceCells.Count - 1; index >= 0; index--)
                if (!MiniCivilization.World.WaterFlow.WaterSourceFrontierSelector.IsNeeded(world, sourceCells[index]))
                {
                    sourceCells[index] = sourceCells[sourceCells.Count - 1];
                    sourceCells.RemoveAt(sourceCells.Count - 1);
                }
            return new ChunkMaterializationResult(coordinate, sourceCells);
        }

        private static void WriteColumn(
            WorldData world,
            in PatternTilePair tile,
            int x,
            int z,
            ICollection<CellCoordinate> sourceCells)
        {
            var pattern = PatternTileComposition.GetCell(
                tile.Terrain,
                tile.Hydrology,
                x,
                z);
            var climate = tile.Climate.GetCell(x, z);
            var heights = PatternHeightQuantization.FromCurrentPattern(pattern);
            heights.ValidateWorldHeight(world.Height);
            var groundHeight = heights.Ground;
            var hasWater = heights.HasWater;
            var topSurface = hasWater
                ? ToBedSurface(pattern.Hydrology.WaterType)
                : SurfaceType.Ground;
            var biome = new CellBiome(climate.Climate, climate.Biome,
                hasWater ? (WaterBiome)pattern.Hydrology.WaterType : WaterBiome.None);
            for (var y = 0; y < heights.UsedCellCount; y++)
            {
                var baseHeight = y * WorldGrid.HeightStepsPerCell;
                var solidHeight = heights.SolidAt(y);
                var cell = new CellData
                {
                    Biome = biome,
                    Terrain = new TerrainData
                    {
                        Material = solidHeight > 0
                            ? MaterialType.Soil
                            : MaterialType.None,
                        Geology = solidHeight > 0
                            ? MaterialType.Soil
                            : MaterialType.None,
                        Surface = solidHeight > 0
                                  && baseHeight + solidHeight == groundHeight
                            ? topSurface
                            : SurfaceType.None,
                        SolidHeight = solidHeight
                    }
                };
                if (hasWater)
                {
                    var available = WorldGrid.HeightStepsPerCell - solidHeight;
                    var waterHeight = heights.WaterAt(y);
                    if (waterHeight > 0)
                    {
                        cell.Water = new WaterData
                        {
                            Amount = WaterAmount.FromRenderFill(
                                waterHeight,
                                available),
                            Role = WaterRole.Source,
                            Type = pattern.Hydrology.WaterType,
                            Flow = FlowDirection.None
                        };
                        sourceCells.Add(new CellCoordinate(x, y, z));
                    }
                }

                if (cell.HasTerrain || cell.HasWater)
                {
                    world.SetCellBulk(x, y, z, cell);
                }
            }
        }

        private static SurfaceType ToBedSurface(WaterType waterType) =>
            waterType switch
            {
                WaterType.River => SurfaceType.Riverbed,
                WaterType.Lake => SurfaceType.Lakebed,
                WaterType.Pond => SurfaceType.Lakebed,
                WaterType.Sea => SurfaceType.Seabed,
                _ => throw new ArgumentOutOfRangeException(nameof(waterType))
            };
    }
}
