using System;
using System.Diagnostics;
using MiniCivilization.World.Domain;

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

            var world = WorldDataBuilder.CreateWorld(input);
            var settings = input.Settings;
            var generationTimer = Stopwatch.StartNew();
            using var planScope = input.Hydrology.BeginPlanScope();
            for (var z = settings.MinimumChunkCoordinate;
                 z <= settings.MaximumChunkCoordinate;
                 z++)
            for (var x = settings.MinimumChunkCoordinate;
                 x <= settings.MaximumChunkCoordinate;
                 x++)
            {
                var chunk = WorldChunkGenerator.Build(
                    input.CreateChunkInput(
                        new ChunkCoordinate(x, z),
                        planScope));
                input.GenerationTiming.Add(chunk.Timing);
                var applyTimer = Stopwatch.StartNew();
                WorldDataBuilder.ApplyChunk(world, chunk);
                input.GenerationTiming.AddWorldApply(
                    applyTimer.ElapsedMilliseconds);
            }

            input.GenerationTiming.SetPipelineTotal(
                generationTimer.ElapsedMilliseconds);
            return world;
        }
    }
}
