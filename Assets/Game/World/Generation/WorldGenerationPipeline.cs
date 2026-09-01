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

            return WorldDataBuilder.CreateWorld(input);
        }
    }
}
