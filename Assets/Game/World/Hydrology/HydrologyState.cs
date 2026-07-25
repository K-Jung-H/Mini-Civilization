using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Hydrology
{
    public sealed class HydrologyState
    {
        private readonly int worldSize;
        private readonly int[] waterBodyIdsByColumn;
        private readonly Dictionary<int, WaterBody> waterBodiesById;

        public IReadOnlyList<WaterBody> WaterBodies { get; }

        internal HydrologyState(
            WorldData world,
            IReadOnlyList<WaterBody> waterBodies)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            worldSize = world.Size;
            WaterBodies = waterBodies
                ?? throw new ArgumentNullException(nameof(waterBodies));
            waterBodyIdsByColumn = new int[world.Size * world.Size];
            waterBodiesById = new Dictionary<int, WaterBody>(waterBodies.Count);

            for (var bodyIndex = 0;
                 bodyIndex < waterBodies.Count;
                 bodyIndex++)
            {
                var body = waterBodies[bodyIndex];
                waterBodiesById[body.Id] = body;
                for (var cellIndex = 0;
                     cellIndex < body.Cells.Count;
                     cellIndex++)
                {
                    var cell = body.Cells[cellIndex];
                    waterBodyIdsByColumn[cell.X + world.Size * cell.Z] =
                        body.Id;
                }
            }
        }

        public int GetWaterBodyId(int x, int z)
        {
            if ((uint)x >= worldSize || (uint)z >= worldSize)
            {
                return 0;
            }

            return waterBodyIdsByColumn[x + worldSize * z];
        }

        public bool TryGetWaterBody(int x, int z, out WaterBody waterBody)
        {
            var id = GetWaterBodyId(x, z);
            if (id == 0)
            {
                waterBody = null;
                return false;
            }

            return waterBodiesById.TryGetValue(id, out waterBody);
        }
    }
}
