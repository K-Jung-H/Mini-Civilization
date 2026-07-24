using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Hydrology;

namespace MiniCivilization.World.Runtime
{
    public sealed class WorldState
    {
        public WorldData Data { get; }
        public IReadOnlyList<WaterBody> WaterBodies { get; private set; }

        public WorldState(
            WorldData data,
            IReadOnlyList<WaterBody> waterBodies = null)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            WaterBodies = waterBodies ?? WaterBodyResolver.Resolve(data);
        }

        public void RefreshWaterBodies()
        {
            WaterBodies = WaterBodyResolver.Resolve(Data);
        }
    }
}
