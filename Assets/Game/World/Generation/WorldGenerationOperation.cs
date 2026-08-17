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
                OriginX = build.OriginX,
                OriginZ = build.OriginZ,
                Parameters = new TerrainFieldParameters(input.Settings),
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
                OriginX = build.OriginX,
                OriginZ = build.OriginZ,
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
            public int OriginX;
            public int OriginZ;
            public TerrainFieldParameters Parameters;
            public NativeArray<int> Result;

            public void Execute(int index)
            {
                Result[index] = Parameters.SampleHeight(
                    OriginX + index % Size,
                    OriginZ + index / Size);
            }
        }

        private struct BiomeJob : IJobParallelFor
        {
            public int Size;
            public int OriginX;
            public int OriginZ;
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
                var localX = index % Size;
                var localZ = index / Size;
                var x = OriginX + localX;
                var z = OriginZ + localZ;
                var groundHeight = SolidHeights[index];
                var altitude = groundHeight /
                    (float)(Height * WorldGrid.HeightStepsPerCell);
                var temperature = DeterministicNoise.FractalNoise(
                    x * 0.00625f,
                    z * 0.00625f,
                    ClimateSeed,
                    4,
                    2f,
                    0.5f);
                temperature = Math.Clamp(
                    temperature * 0.85f + 0.15f - altitude * 0.35f,
                    0f,
                    1f);
                var climate = BiomeStage.ResolveClimate(
                    temperature,
                    ColdClimateThreshold);
                Biomes[index] = new CellBiome(
                    climate,
                    BiomeStage.ResolveTerrain(climate, altitude),
                    BiomeStage.ResolveWater(WaterTypes[index]));
                TopSurfaces[index] = WaterBedSurfaces[index] != SurfaceType.None
                    ? WaterBedSurfaces[index]
                    : IsAdjacentToWater(localX, localZ)
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
