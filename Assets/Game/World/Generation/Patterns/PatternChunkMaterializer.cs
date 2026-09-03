using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    public readonly struct PatternTilePair
    {
        public PatternTilePair(
            TerrainPatternTile terrain,
            HydrologyPatternTile hydrology)
        {
            PatternTileComposition.ValidatePair(terrain, hydrology);
            Terrain = terrain;
            Hydrology = hydrology;
        }

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
            var groundHeight = ToGroundHeightUnits(pattern.GroundHeight);
            var waterSurfaceHeight = pattern.Hydrology.HasWater
                ? ToWaterSurfaceHeightUnits(
                    pattern.Hydrology.WaterSurfaceHeight)
                : 0;
            var maximumHeight = checked(
                world.Height * WorldGrid.HeightStepsPerCell);
            if (groundHeight < 0 || groundHeight > maximumHeight
                || waterSurfaceHeight < 0
                || waterSurfaceHeight > maximumHeight)
            {
                throw new InvalidOperationException(
                    "Pattern Tile height is outside the configured world height.");
            }

            if (pattern.Hydrology.HasWater
                && waterSurfaceHeight < groundHeight)
            {
                throw new InvalidOperationException(
                    "Hydrology water surface is below its final ground height.");
            }

            var hasWater = pattern.Hydrology.HasWater
                && waterSurfaceHeight > groundHeight;
            var topSurface = hasWater
                ? ToBedSurface(pattern.Hydrology.WaterType)
                : SurfaceType.Ground;
            var usedHeight = Math.Max(groundHeight, waterSurfaceHeight);
            var usedCellCount = Math.Min(
                world.Height,
                (usedHeight + WorldGrid.HeightStepsPerCell - 1)
                / WorldGrid.HeightStepsPerCell);
            for (var y = 0; y < usedCellCount; y++)
            {
                var baseHeight = y * WorldGrid.HeightStepsPerCell;
                var solidHeight = (byte)Math.Clamp(
                    groundHeight - baseHeight,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var cell = new CellData
                {
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
                    var waterHeight = (byte)Math.Clamp(
                        waterSurfaceHeight - baseHeight - solidHeight,
                        0,
                        available);
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

        private static int ToGroundHeightUnits(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException(
                    "Pattern Tile height is not finite.");
            }

            return checked((int)MathF.Round(
                value,
                MidpointRounding.AwayFromZero));
        }

        private static int ToWaterSurfaceHeightUnits(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException(
                    "Pattern Tile height is not finite.");
            }

            return checked((int)MathF.Round(
                value,
                MidpointRounding.AwayFromZero));
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
