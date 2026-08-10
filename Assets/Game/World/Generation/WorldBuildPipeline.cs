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
        public float DesertMoistureThreshold => Settings.DesertMoistureThreshold;
        public float WetlandMoistureThreshold => Settings.WetlandMoistureThreshold;
        public float SnowTemperatureThreshold => Settings.SnowTemperatureThreshold;
        public int WaterMoistureRadius => Settings.WaterMoistureRadius;

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
            Environments = new EnvironmentData[columns];
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
        public EnvironmentData[] Environments { get; }
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
                    build.WaterTypes[index]);
                world.SetEnvironment(x, z, build.Environments[index]);
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
            WaterType waterType)
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
            Apply(build, BuildWaterDistanceField(build));
        }

        internal static int[] BuildWaterDistanceField(WorldBuildData build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));

            var radius = build.Input.WaterMoistureRadius;
            var result = new int[build.Size * build.Size];
            Array.Fill(result, radius + 1);
            var queue = new Queue<int>();
            for (var index = 0; index < result.Length; index++)
            {
                if (build.WaterSurfaces[index] > build.SolidHeights[index]
                    || build.WaterBedSurfaces[index] != SurfaceType.None)
                {
                    result[index] = 0;
                    queue.Enqueue(index);
                }
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                if (result[index] >= radius) continue;
                var x = index % build.Size;
                var z = index / build.Size;
                for (var directionIndex = 0; directionIndex < Directions.Length; directionIndex++)
                {
                    var nextX = x + Directions[directionIndex].x;
                    var nextZ = z + Directions[directionIndex].z;
                    if (!build.ContainsColumn(nextX, nextZ)) continue;
                    var next = build.ToColumnIndex(nextX, nextZ);
                    if (result[next] <= result[index] + 1) continue;
                    result[next] = result[index] + 1;
                    queue.Enqueue(next);
                }
            }

            return result;
        }

        internal static void Apply(
            WorldBuildData build,
            IReadOnlyList<int> distances)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (distances == null
                || distances.Count != build.Size * build.Size)
            {
                throw new ArgumentException(
                    "Water distance data does not match the world size.",
                    nameof(distances));
            }

            var input = build.Input;
            var climateSeed = DeterministicNoise.DeriveSeed(build.Seed, "climate");
            for (var z = 0; z < build.Size; z++)
            for (var x = 0; x < build.Size; x++)
            {
                var index = build.ToColumnIndex(x, z);
                var groundHeight = build.SolidHeights[index];
                var latitude = build.Size > 1 ? MathF.Abs(z / (float)(build.Size - 1) * 2f - 1f) : 0f;
                var altitude = groundHeight / (float)(build.Height * WorldGrid.HeightStepsPerCell);
                var temperature = Math.Clamp(1f - latitude * 0.7f - altitude * 0.45f, 0f, 1f);
                var moistureNoise = DeterministicNoise.FractalNoise(x * 0.025f, z * 0.025f, climateSeed, 3, 2f, 0.5f);
                var distance = distances[index];
                var waterInfluence = distance > input.WaterMoistureRadius
                    ? 0f : 1f - distance / (float)(input.WaterMoistureRadius + 1);
                var moisture = Math.Clamp(moistureNoise * 0.65f + waterInfluence * 0.55f, 0f, 1f);
                var biome = temperature <= input.SnowTemperatureThreshold ? BiomeType.Snow
                    : altitude >= 0.72f ? BiomeType.Mountain
                    : moisture <= input.DesertMoistureThreshold ? BiomeType.Desert
                    : moisture >= input.WetlandMoistureThreshold && waterInfluence > 0f ? BiomeType.Wetland
                    : BiomeType.Grassland;
                build.Environments[index] = new EnvironmentData
                {
                    Biome = biome,
                    Temperature = (byte)MathF.Round(temperature * byte.MaxValue),
                    Moisture = (byte)MathF.Round(moisture * byte.MaxValue),
                    Fertility = (byte)MathF.Round(Math.Clamp(
                        moisture * (1f - MathF.Abs(temperature - 0.58f)), 0f, 1f) * byte.MaxValue)
                };
                build.TopSurfaces[index] = build.WaterBedSurfaces[index] != SurfaceType.None
                    ? build.WaterBedSurfaces[index]
                    : IsAdjacentToWater(build, x, z)
                        && Math.Abs(groundHeight - input.SeaLevelUnits) <= 2
                            ? SurfaceType.Shore : SurfaceType.Ground;
            }
        }

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
