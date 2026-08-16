using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    public sealed class WorldBuildInput
    {
        private WorldBuildInput(WorldGenerationSettings settings, int seed)
        {
            Settings = settings.CreateData(seed);
        }

        public WorldSettingsData Settings { get; }
        public int Seed => Settings.Seed;
        public int WorldSize => Settings.WorldSize;
        public int WorldHeight => Settings.WorldHeight;
        public int ChunkSizeXZ => Settings.ChunkCellCountXZ;
        public int ChunkHeight => Settings.ChunkCellCountY;
        public float TerrainScale => Settings.TerrainScale;
        public int TerrainLayers => Settings.TerrainLayers;
        public float TerrainSpacing => Settings.TerrainSpacing;
        public float TerrainDetail => Settings.TerrainDetail;
        public int BaseHeightUnits => Settings.BaseHeightUnits;
        public int HeightVariationUnits => Settings.HeightVariationUnits;
        public float EdgeLowering => Settings.EdgeLowering;
        public float MountainScale => Settings.MountainScale;
        public int MountainHeightUnits => Settings.MountainHeightUnits;
        public float MountainCoverage => Settings.MountainCoverage;
        public float MountainSteepness => Settings.MountainSteepness;
        public int SeaLevelUnits => Settings.SeaLevelUnits;
        public int RiverCount => Settings.RiverCount;
        public int RiverDepthCells => Settings.RiverDepthCells;
        public int MaximumRiverWidthCells => Settings.MaximumRiverWidthCells;
        public int MaximumRiverDepthCells => Settings.MaximumRiverDepthCells;
        public int LakeCount => Settings.LakeCount;
        public int MinimumInlandLakeDistance => Settings.MinimumInlandLakeDistance;
        public int MinimumInlandLakeArea => Settings.MinimumInlandLakeArea;
        public int MinimumInlandLakeDepthSteps => Settings.MinimumInlandLakeDepthSteps;
        public int PondMaximumArea => Settings.PondMaximumArea;
        public WaterFlowRules WaterFlowRules => Settings.WaterFlowRules;
        public float ColdClimateThreshold => Settings.ColdClimateThreshold;

        public static WorldBuildInput Create(WorldGenerationSettings settings, int seed)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!settings.TryValidate(out var error)) throw new InvalidOperationException(error);
            return new WorldBuildInput(settings, seed);
        }
    }

    public sealed class WorldBuildData
    {
        public WorldBuildData(WorldBuildInput input)
        {
            Input = input ?? throw new ArgumentNullException(nameof(input));
            Size = input.WorldSize;
            Height = input.WorldHeight;
            Seed = input.Seed;
            var columns = checked(Size * Size);
            SolidHeights = new int[columns];
            WaterSurfaces = new int[columns];
            WaterRoles = new WaterRole[columns];
            WaterTypes = new WaterType[columns];
            WaterBedSurfaces = new SurfaceType[columns];
            TopSurfaces = new SurfaceType[columns];
            Biomes = new CellBiome[columns];
        }

        public WorldBuildInput Input { get; }
        public int Size { get; }
        public int Height { get; }
        public int Seed { get; }
        public int[] SolidHeights { get; }
        public int[] WaterSurfaces { get; }
        public WaterRole[] WaterRoles { get; }
        public WaterType[] WaterTypes { get; }
        public SurfaceType[] WaterBedSurfaces { get; }
        public SurfaceType[] TopSurfaces { get; }
        public CellBiome[] Biomes { get; }
        public WaterFlowRules WaterFlowRules => Input.WaterFlowRules;
        public int PondMaximumArea => Input.PondMaximumArea;
        public bool ContainsColumn(int x, int z) => (uint)x < Size && (uint)z < Size;
        public int ToColumnIndex(int x, int z) => x + Size * z;
    }

    public static class WorldDataBuilder
    {
        public static WorldData Build(WorldBuildData build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            var input = build.Input;
            var world = new WorldData(input.Settings);

            for (var z = 0; z < build.Size; z++)
            for (var x = 0; x < build.Size; x++)
            {
                var index = build.ToColumnIndex(x, z);
                WriteColumn(
                    world,
                    x,
                    z,
                    build.SolidHeights[index],
                    build.TopSurfaces[index] == SurfaceType.None
                        ? SurfaceType.Ground
                        : build.TopSurfaces[index],
                    build.WaterSurfaces[index],
                    build.WaterRoles[index],
                    build.WaterTypes[index],
                    build.Biomes[index]);
            }

            return world;
        }

        private static void WriteColumn(
            WorldData world,
            int x,
            int z,
            int solidHeightUnits,
            SurfaceType surface,
            int waterSurfaceUnits,
            WaterRole waterRole,
            WaterType waterType,
            CellBiome biome)
        {
            solidHeightUnits = Math.Clamp(solidHeightUnits, 0, world.Height * WorldGrid.HeightStepsPerCell);
            waterSurfaceUnits = Math.Clamp(waterSurfaceUnits, 0, world.Height * WorldGrid.HeightStepsPerCell);
            for (var y = 0; y < world.Height; y++)
            {
                var baseUnits = y * WorldGrid.HeightStepsPerCell;
                var solidFill = (byte)Math.Clamp(
                    solidHeightUnits - baseUnits,
                    0,
                    WorldGrid.HeightStepsPerCell);
                var cell = new CellData
                {
                    Terrain = new TerrainData { SolidHeight = solidFill }
                };
                if (solidFill > 0)
                {
                    cell.Terrain.Material = y < Math.Max(0, solidHeightUnits / WorldGrid.HeightStepsPerCell - 2)
                        ? MaterialType.Rock : MaterialType.Soil;
                    cell.Terrain.Geology = MaterialType.Rock;
                    cell.Terrain.Surface = solidFill < WorldGrid.HeightStepsPerCell
                        || baseUnits + solidFill == solidHeightUnits
                            ? surface : SurfaceType.None;
                }

                var available = WorldGrid.HeightStepsPerCell - solidFill;
                var desiredTop = Math.Clamp(waterSurfaceUnits - baseUnits, 0, WorldGrid.HeightStepsPerCell);
                var waterFill = (byte)Math.Clamp(desiredTop - solidFill, 0, available);
                if (waterFill > 0 && waterRole == WaterRole.Source)
                {
                    cell.Water = new WaterData
                    {
                        Amount = WaterAmount.FromRenderFill(waterFill, available),
                        Role = waterRole,
                        Type = waterType,
                        Flow = FlowDirection.None
                    };
                }

                if (cell.HasTerrain || cell.HasWater)
                {
                    cell.Biome = biome;
                }

                world.SetCellBulk(x, y, z, cell);
            }
        }
    }

    internal static class TerrainStage
    {
        public static void Build(WorldBuildData build)
        {
            var input = build.Input;
            var terrainSeed = DeterministicNoise.DeriveSeed(build.Seed, "terrain");
            var mountainSeed = DeterministicNoise.DeriveSeed(build.Seed, "mountains");
            var mountainMaskSeed = DeterministicNoise.DeriveSeed(build.Seed, "mountain-mask");
            var maximumUnits = build.Height * WorldGrid.HeightStepsPerCell - 1;
            var edgeFalloffUnits = build.Height * WorldGrid.HeightStepsPerCell * 0.35f;

            for (var z = 0; z < build.Size; z++)
            for (var x = 0; x < build.Size; x++)
            {
                var noise = DeterministicNoise.FractalNoise(
                    x * input.TerrainScale, z * input.TerrainScale, terrainSeed,
                    input.TerrainLayers, input.TerrainSpacing, input.TerrainDetail);
                var ridge = DeterministicNoise.RidgedFractalNoise(
                    x * input.MountainScale, z * input.MountainScale, mountainSeed,
                    input.TerrainLayers, input.TerrainSpacing, input.TerrainDetail);
                var maskNoise = DeterministicNoise.FractalNoise(
                    x * input.MountainScale * 0.4f, z * input.MountainScale * 0.4f,
                    mountainMaskSeed, 3, 2f, 0.5f);
                var normalizedX = build.Size > 1 ? x / (float)(build.Size - 1) * 2f - 1f : 0f;
                var normalizedZ = build.Size > 1 ? z / (float)(build.Size - 1) * 2f - 1f : 0f;
                var edgeDistance = MathF.Max(MathF.Abs(normalizedX), MathF.Abs(normalizedZ));
                var edgePenalty = MathF.Pow(edgeDistance, 3f) * edgeFalloffUnits * input.EdgeLowering;
                var mountainMask = SmoothStep01((maskNoise - input.MountainCoverage) / 0.2f);
                var inlandMask = 1f - SmoothStep01((edgeDistance - 0.62f) / 0.38f);
                var mountainHeight = MathF.Pow(ridge, input.MountainSteepness)
                    * mountainMask * inlandMask * input.MountainHeightUnits;
                var height = input.BaseHeightUnits + (int)MathF.Round(
                    (noise * 2f - 1f) * input.HeightVariationUnits
                    + mountainHeight - edgePenalty);
                build.SolidHeights[build.ToColumnIndex(x, z)] = Math.Clamp(height, 1, maximumUnits);
            }
        }

        private static float SmoothStep01(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return value * value * (3f - 2f * value);
        }
    }

    internal static class WaterFeatureStage
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (0, 1), (-1, 0), (0, -1)
        };

        public static void InitializeSea(WorldBuildData build)
        {
            var visited = new bool[build.SolidHeights.Length];
            var queue = new Queue<(int x, int z)>();
            for (var x = 0; x < build.Size; x++)
            {
                Enqueue(x, 0);
                Enqueue(x, build.Size - 1);
            }

            for (var z = 1; z < build.Size - 1; z++)
            {
                Enqueue(0, z);
                Enqueue(build.Size - 1, z);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var index = build.ToColumnIndex(current.x, current.z);
                build.WaterSurfaces[index] = build.Input.SeaLevelUnits;
                build.WaterRoles[index] = WaterRole.Source;
                build.WaterTypes[index] = WaterType.Sea;
                build.WaterBedSurfaces[index] = SurfaceType.Seabed;
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    Enqueue(current.x + Directions[directionIndex].x, current.z + Directions[directionIndex].z);
                }
            }

            void Enqueue(int x, int z)
            {
                if (!build.ContainsColumn(x, z)) return;
                var index = build.ToColumnIndex(x, z);
                if (visited[index] || build.SolidHeights[index] >= build.Input.SeaLevelUnits) return;
                visited[index] = true;
                queue.Enqueue((x, z));
            }
        }

        public static void ApplyFeaturePlan(
            WorldBuildData build,
            HydrologyFeaturePlan featurePlan)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            DynamicRiverPlanner.ApplyFeaturePlan(
                featurePlan,
                build.SolidHeights,
                build.WaterSurfaces,
                build.WaterRoles,
                build.WaterTypes,
                build.WaterBedSurfaces);
        }
    }

    internal static class HydrologyStage
    {
        public static HydrologyMap Build(WorldBuildData build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            return HydrologyMapBuilder.Build(
                build.Size,
                build.Input.SeaLevelUnits,
                build.SolidHeights,
                build.WaterSurfaces);
        }
    }

    internal static class BiomeStage
    {
        private static readonly (int x, int z)[] Directions =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1)
        };

        public static void Build(WorldBuildData build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));

            var input = build.Input;
            var climateSeed = DeterministicNoise.DeriveSeed(build.Seed, "climate");
            for (var z = 0; z < build.Size; z++)
            for (var x = 0; x < build.Size; x++)
            {
                var index = build.ToColumnIndex(x, z);
                var groundHeight = build.SolidHeights[index];
                var altitude = groundHeight /
                    (float)(build.Height * WorldGrid.HeightStepsPerCell);
                var temperature = CalculateTemperature(
                    build.Size,
                    build.Height,
                    climateSeed,
                    x,
                    z,
                    groundHeight);
                var climate = ResolveClimate(
                    temperature,
                    input.ColdClimateThreshold);
                build.Biomes[index] = new CellBiome(
                    climate,
                    ResolveTerrain(climate, altitude),
                    ResolveWater(build.WaterTypes[index]));
                build.TopSurfaces[index] = build.WaterBedSurfaces[index] != SurfaceType.None
                    ? build.WaterBedSurfaces[index]
                    : IsAdjacentToWater(build, x, z)
                        && Math.Abs(groundHeight - input.SeaLevelUnits) <= 2
                            ? SurfaceType.Shore : SurfaceType.Ground;
            }
        }

        internal static float CalculateTemperature(
            int size,
            int height,
            int climateSeed,
            int x,
            int z,
            int groundHeight)
        {
            var latitude = size > 1
                ? MathF.Abs(z / (float)(size - 1) * 2f - 1f)
                : 0f;
            var altitude = groundHeight /
                (float)(height * WorldGrid.HeightStepsPerCell);
            var variation = DeterministicNoise.FractalNoise(
                x * 0.0125f,
                z * 0.0125f,
                climateSeed,
                3,
                2f,
                0.5f) - 0.5f;
            return Math.Clamp(
                1f - latitude * 0.7f - altitude * 0.45f
                + variation * 0.3f,
                0f,
                1f);
        }

        internal static ClimateBiome ResolveClimate(
            float temperature,
            float coldThreshold)
        {
            if (temperature <= coldThreshold)
            {
                return ClimateBiome.Cold;
            }

            return temperature >= 1f - coldThreshold
                ? ClimateBiome.Warm
                : ClimateBiome.Temperate;
        }

        internal static TerrainBiome ResolveTerrain(
            ClimateBiome climate,
            float altitude)
        {
            if (altitude >= 0.72f)
            {
                return TerrainBiome.Mountain;
            }

            return climate switch
            {
                ClimateBiome.Cold => TerrainBiome.Snow,
                ClimateBiome.Warm => TerrainBiome.Desert,
                _ => TerrainBiome.Field
            };
        }

        internal static WaterBiome ResolveWater(WaterType waterType) =>
            waterType switch
            {
                WaterType.Pond => WaterBiome.Pond,
                WaterType.Lake => WaterBiome.Lake,
                WaterType.Sea => WaterBiome.Sea,
                WaterType.River => WaterBiome.River,
                _ => WaterBiome.None
            };

        private static bool IsAdjacentToWater(WorldBuildData build, int x, int z)
        {
            for (var index = 0; index < Directions.Length; index++)
            {
                var nextX = x + Directions[index].x;
                var nextZ = z + Directions[index].z;
                if (build.ContainsColumn(nextX, nextZ)
                    && build.WaterSurfaces[build.ToColumnIndex(nextX, nextZ)]
                        > build.SolidHeights[build.ToColumnIndex(nextX, nextZ)])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
