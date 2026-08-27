using System;
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
            for (var z = settings.MinimumChunkCoordinate;
                 z <= settings.MaximumChunkCoordinate;
                 z++)
            for (var x = settings.MinimumChunkCoordinate;
                 x <= settings.MaximumChunkCoordinate;
                 x++)
            {
                var chunk = WorldChunkGenerator.Build(
                    input.CreateChunkInput(new ChunkCoordinate(x, z)));
                WorldDataBuilder.ApplyChunk(world, chunk);
            }

            return world;
        }
    }
}
