using System;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using Unity.Collections;
using Unity.Jobs;

namespace MiniCivilization.World.Generation
{
    internal sealed class WorldGenerationOperation : WorldOperation
    {
        private enum Phase : byte
        {
            NotStarted,
            Terrain,
            WaterFeatures,
            Biome,
            BuildWorldData,
            PrepareRuntime,
            Ready
        }

        private readonly WorldBuildInput input;
        private WorldBuildData build;
        private Phase phase;

        private JobHandle terrainJob;
        private NativeArray<int> terrainHeights;
        private bool terrainJobScheduled;

        private Task waterFeaturesTask;
        private JobHandle biomeJob;
        private NativeArray<int> biomeSolidHeights;
        private NativeArray<int> biomeWaterSurfaces;
        private NativeArray<SurfaceType> biomeWaterBedSurfaces;
        private NativeArray<WaterType> biomeWaterTypes;
        private NativeArray<CellBiome> biomeCells;
        private NativeArray<SurfaceType> biomeTopSurfaces;
        private bool biomeJobScheduled;

        private Task<WorldData> buildWorldDataTask;
        private Task<WorldRuntime> prepareRuntimeTask;
        private bool disposed;

        public WorldGenerationOperation(WorldBuildInput input)
            : base(WorldOperationKind.Generate, stageCount: 6)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public override void Update()
        {
            if (disposed || IsFailed || IsReadyForActivation)
            {
                return;
            }

            try
            {
                switch (phase)
                {
                    case Phase.NotStarted:
                        StartTerrain();
                        break;
                    case Phase.Terrain:
                        FinishTerrain();
                        break;
                    case Phase.WaterFeatures:
                        FinishWaterFeatures();
                        break;
                    case Phase.Biome:
                        FinishBiomeJob();
                        break;
                    case Phase.BuildWorldData:
                        FinishBuildWorldData();
                        break;
                    case Phase.PrepareRuntime:
                        FinishPrepareRuntime();
                        break;
                }
            }
            catch (Exception exception)
            {
                DisposeNativeBuffers();
                Fail(exception);
            }
        }

        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (terrainJobScheduled)
            {
                terrainJob.Complete();
            }

            if (biomeJobScheduled)
            {
                biomeJob.Complete();
            }

            DisposeNativeBuffers();
        }

        private void StartTerrain()
        {
            build = new WorldBuildData(input);
            BeginStage(WorldOperationStage.Terrain);
            terrainHeights = new NativeArray<int>(
                build.SolidHeights.Length,
                Allocator.Persistent);
            terrainJob = new TerrainJob
            {
                Size = build.Size,
                Height = build.Height,
                TerrainSeed = DeterministicNoise.DeriveSeed(build.Seed, "terrain"),
                MountainSeed = DeterministicNoise.DeriveSeed(build.Seed, "mountains"),
                MountainMaskSeed = DeterministicNoise.DeriveSeed(build.Seed, "mountain-mask"),
                TerrainScale = input.TerrainScale,
                TerrainLayers = input.TerrainLayers,
                TerrainSpacing = input.TerrainSpacing,
                TerrainDetail = input.TerrainDetail,
                BaseHeightUnits = input.BaseHeightUnits,
                HeightVariationUnits = input.HeightVariationUnits,
                EdgeLowering = input.EdgeLowering,
                MountainScale = input.MountainScale,
                MountainHeightUnits = input.MountainHeightUnits,
                MountainCoverage = input.MountainCoverage,
                MountainSteepness = input.MountainSteepness,
                Result = terrainHeights
            }.Schedule(terrainHeights.Length, innerloopBatchCount: 64);
            terrainJobScheduled = true;
            phase = Phase.Terrain;
        }

        private void FinishTerrain()
        {
            if (!terrainJob.IsCompleted)
            {
                return;
            }

            terrainJob.Complete();
            terrainHeights.CopyTo(build.SolidHeights);
            terrainHeights.Dispose();
            terrainJobScheduled = false;
            CompleteCurrentStage();

            BeginStage(WorldOperationStage.WaterFeatures);
            waterFeaturesTask = Task.Run(
                () => WorldGenerationPipeline.BuildWaterFeatures(build));
            phase = Phase.WaterFeatures;
        }

        private void FinishWaterFeatures()
        {
            if (!waterFeaturesTask.IsCompleted)
            {
                return;
            }

            waterFeaturesTask.GetAwaiter().GetResult();
            CompleteCurrentStage();

            BeginStage(WorldOperationStage.Biome);
            StartBiomeJob();
        }

        private void StartBiomeJob()
        {
            biomeSolidHeights = new NativeArray<int>(
                build.SolidHeights,
                Allocator.Persistent);
            biomeWaterSurfaces = new NativeArray<int>(
                build.WaterSurfaces,
                Allocator.Persistent);
            biomeWaterBedSurfaces = new NativeArray<SurfaceType>(
                build.WaterBedSurfaces,
                Allocator.Persistent);
            biomeWaterTypes = new NativeArray<WaterType>(
                build.WaterTypes,
                Allocator.Persistent);
            biomeCells = new NativeArray<CellBiome>(
                build.Biomes.Length,
                Allocator.Persistent);
            biomeTopSurfaces = new NativeArray<SurfaceType>(
                build.TopSurfaces.Length,
                Allocator.Persistent);
            biomeJob = new BiomeJob
            {
                Size = build.Size,
                Height = build.Height,
                SeaLevelUnits = input.SeaLevelUnits,
                ClimateSeed = DeterministicNoise.DeriveSeed(build.Seed, "climate"),
                ColdClimateThreshold = input.ColdClimateThreshold,
                SolidHeights = biomeSolidHeights,
                WaterSurfaces = biomeWaterSurfaces,
                WaterBedSurfaces = biomeWaterBedSurfaces,
                WaterTypes = biomeWaterTypes,
                Biomes = biomeCells,
                TopSurfaces = biomeTopSurfaces
            }.Schedule(biomeCells.Length, innerloopBatchCount: 64);
            biomeJobScheduled = true;
            phase = Phase.Biome;
        }

        private void FinishBiomeJob()
        {
            if (!biomeJob.IsCompleted)
            {
                return;
            }

            biomeJob.Complete();
            biomeCells.CopyTo(build.Biomes);
            biomeTopSurfaces.CopyTo(build.TopSurfaces);
            DisposeBiomeBuffers();
            biomeJobScheduled = false;
            CompleteCurrentStage();

            BeginStage(WorldOperationStage.BuildWorldData);
            buildWorldDataTask = Task.Run(
                () => WorldGenerationPipeline.BuildWorldData(build));
            phase = Phase.BuildWorldData;
        }

        private void FinishBuildWorldData()
        {
            if (!buildWorldDataTask.IsCompleted)
            {
                return;
            }

            var world = buildWorldDataTask.GetAwaiter().GetResult();
            CompleteCurrentStage();

            BeginStage(WorldOperationStage.PrepareRuntime);
            prepareRuntimeTask = Task.Run(() => WorldRuntime.CreatePrepared(world));
            phase = Phase.PrepareRuntime;
        }

        private void FinishPrepareRuntime()
        {
            if (!prepareRuntimeTask.IsCompleted)
            {
                return;
            }

            PreparedRuntime = prepareRuntimeTask.GetAwaiter().GetResult();
            CompleteCurrentStage();
            IsReadyForActivation = true;
            phase = Phase.Ready;
        }

        private void DisposeNativeBuffers()
        {
            if (terrainHeights.IsCreated)
            {
                terrainHeights.Dispose();
            }

            DisposeBiomeBuffers();
            terrainJobScheduled = false;
            biomeJobScheduled = false;
        }

        private void DisposeBiomeBuffers()
        {
            if (biomeSolidHeights.IsCreated) biomeSolidHeights.Dispose();
            if (biomeWaterSurfaces.IsCreated) biomeWaterSurfaces.Dispose();
            if (biomeWaterBedSurfaces.IsCreated) biomeWaterBedSurfaces.Dispose();
            if (biomeWaterTypes.IsCreated) biomeWaterTypes.Dispose();
            if (biomeCells.IsCreated) biomeCells.Dispose();
            if (biomeTopSurfaces.IsCreated) biomeTopSurfaces.Dispose();
        }

        private struct TerrainJob : IJobParallelFor
        {
            public int Size;
            public int Height;
            public int TerrainSeed;
            public int MountainSeed;
            public int MountainMaskSeed;
            public float TerrainScale;
            public int TerrainLayers;
            public float TerrainSpacing;
            public float TerrainDetail;
            public int BaseHeightUnits;
            public int HeightVariationUnits;
            public float EdgeLowering;
            public float MountainScale;
            public int MountainHeightUnits;
            public float MountainCoverage;
            public float MountainSteepness;
            public NativeArray<int> Result;

            public void Execute(int index)
            {
                var x = index % Size;
                var z = index / Size;
                var noise = DeterministicNoise.FractalNoise(
                    x * TerrainScale, z * TerrainScale, TerrainSeed,
                    TerrainLayers, TerrainSpacing, TerrainDetail);
                var ridge = DeterministicNoise.RidgedFractalNoise(
                    x * MountainScale, z * MountainScale, MountainSeed,
                    TerrainLayers, TerrainSpacing, TerrainDetail);
                var maskNoise = DeterministicNoise.FractalNoise(
                    x * MountainScale * 0.4f, z * MountainScale * 0.4f,
                    MountainMaskSeed, 3, 2f, 0.5f);
                var normalizedX = Size > 1
                    ? x / (float)(Size - 1) * 2f - 1f : 0f;
                var normalizedZ = Size > 1
                    ? z / (float)(Size - 1) * 2f - 1f : 0f;
                var edgeDistance = MathF.Max(
                    MathF.Abs(normalizedX),
                    MathF.Abs(normalizedZ));
                var edgeFalloffUnits = Height * WorldGrid.HeightStepsPerCell * 0.35f;
                var edgePenalty = MathF.Pow(edgeDistance, 3f)
                    * edgeFalloffUnits * EdgeLowering;
                var mountainMask = SmoothStep01(
                    (maskNoise - MountainCoverage) / 0.2f);
                var inlandMask = 1f - SmoothStep01(
                    (edgeDistance - 0.62f) / 0.38f);
                var mountainHeight = MathF.Pow(ridge, MountainSteepness)
                    * mountainMask * inlandMask * MountainHeightUnits;
                var height = BaseHeightUnits + (int)MathF.Round(
                    (noise * 2f - 1f) * HeightVariationUnits
                    + mountainHeight - edgePenalty);
                var maximumUnits = Height * WorldGrid.HeightStepsPerCell - 1;
                Result[index] = Math.Clamp(height, 1, maximumUnits);
            }

            private static float SmoothStep01(float value)
            {
                value = Math.Clamp(value, 0f, 1f);
                return value * value * (3f - 2f * value);
            }
        }

        private struct BiomeJob : IJobParallelFor
        {
            public int Size;
            public int Height;
            public int SeaLevelUnits;
            public int ClimateSeed;
            public float ColdClimateThreshold;
            [ReadOnly] public NativeArray<int> SolidHeights;
            [ReadOnly] public NativeArray<int> WaterSurfaces;
            [ReadOnly] public NativeArray<SurfaceType> WaterBedSurfaces;
            [ReadOnly] public NativeArray<WaterType> WaterTypes;
            public NativeArray<CellBiome> Biomes;
            public NativeArray<SurfaceType> TopSurfaces;

            public void Execute(int index)
            {
                var x = index % Size;
                var z = index / Size;
                var groundHeight = SolidHeights[index];
                var altitude = groundHeight /
                    (float)(Height * WorldGrid.HeightStepsPerCell);
                var temperature = BiomeStage.CalculateTemperature(
                    Size,
                    Height,
                    ClimateSeed,
                    x,
                    z,
                    groundHeight);
                var climate = BiomeStage.ResolveClimate(
                    temperature,
                    ColdClimateThreshold);
                Biomes[index] = new CellBiome(
                    climate,
                    BiomeStage.ResolveTerrain(climate, altitude),
                    BiomeStage.ResolveWater(WaterTypes[index]));
                TopSurfaces[index] = WaterBedSurfaces[index] != SurfaceType.None
                    ? WaterBedSurfaces[index]
                    : IsAdjacentToWater(x, z)
                        && Math.Abs(groundHeight - SeaLevelUnits) <= 2
                            ? SurfaceType.Shore
                            : SurfaceType.Ground;
            }

            private bool IsAdjacentToWater(int x, int z)
            {
                return IsWater(x + 1, z)
                    || IsWater(x - 1, z)
                    || IsWater(x, z + 1)
                    || IsWater(x, z - 1);
            }

            private bool IsWater(int x, int z)
            {
                if ((uint)x >= Size || (uint)z >= Size)
                {
                    return false;
                }

                var index = x + Size * z;
                return WaterSurfaces[index] > SolidHeights[index];
            }
        }
    }
}
