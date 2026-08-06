using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;

namespace MiniCivilization.World.Generation
{
    public static class WorldGenerationPipeline
    {
        public static WorldData Build(WorldBuildInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var build = new WorldBuildData(input);
            TerrainStage.Build(build);
            BuildWaterFeatures(build);
            BiomeStage.Build(build);
            return BuildWorldData(build);
        }

        internal static void BuildWaterFeatures(WorldBuildData build)
        {
            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            var input = build.Input;
            WaterFeatureStage.InitializeSea(build);

            var hydrology = HydrologyStage.Build(build);
            var waterValidationContext = WaterPlanValidator.CreateContext(build);
            var validationWorld = waterValidationContext.SourceWorld;
            var lakePlans = InlandLakePlanner.BuildPlans(
                validationWorld,
                input,
                hydrology,
                input.Seed,
                waterValidationContext);
            var featurePlan = DynamicRiverPlanner.BuildFeaturePlan(
                validationWorld,
                input,
                hydrology,
                lakePlans,
                build.SolidHeights,
                build.WaterSurfaces,
                input.Seed,
                waterValidationContext);
            WaterFeatureStage.ApplyFeaturePlan(build, featurePlan);
        }

        internal static WorldData BuildWorldData(WorldBuildData build)
        {
            if (build == null)
            {
                throw new ArgumentNullException(nameof(build));
            }

            var world = WorldDataBuilder.Build(build);
            WaterTypeResolver.RefreshAll(world);
            WaterFlowSolver.PrepareGeneratedWorld(world);
            return world;
        }
    }
}
