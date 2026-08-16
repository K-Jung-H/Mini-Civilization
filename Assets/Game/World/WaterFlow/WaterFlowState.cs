using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    public sealed class WaterFlowState
    {
        private readonly WorldData world;
        private readonly int worldSize;
        private readonly int worldHeight;
        private readonly Dictionary<CellColumnCoordinate, int>
            waterBodyIdsByColumn = new();
        private readonly Dictionary<int, WaterBody> waterBodiesById = new();
        private readonly Dictionary<CellCoordinate, WaterData> stagedCells = new();
        private IReadOnlyList<WaterBody> waterBodies = Array.Empty<WaterBody>();

        public IReadOnlyList<WaterBody> WaterBodies => waterBodies;
        public bool IsRecalculating { get; internal set; }

        internal int CellCount => checked(
            worldSize * worldSize * worldHeight);

        internal WaterFlowState(
            WorldData world,
            IReadOnlyList<WaterBody> bodies)
        {
            this.world = world
                ?? throw new ArgumentNullException(nameof(world));
            worldSize = world.Size;
            worldHeight = world.Height;
            ReplaceWaterBodies(bodies);
        }

        public int GetWaterBodyId(int x, int z)
        {
            if (!ContainsColumn(x, z))
            {
                return 0;
            }

            return waterBodyIdsByColumn.TryGetValue(
                new CellColumnCoordinate(x, z),
                out var id)
                ? id
                : 0;
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

        internal bool TryGetWaterBody(int id, out WaterBody waterBody) =>
            waterBodiesById.TryGetValue(id, out waterBody);

        public FlowDirection GetFlowDirection(
            int x,
            int y,
            int z) =>
            ContainsCell(x, y, z)
                ? GetWater(new CellCoordinate(x, y, z)).Flow
                : FlowDirection.None;

        internal WaterData GetWater(CellCoordinate cell) =>
            world.GetCell(cell.X, cell.Y, cell.Z).Water;

        internal FlowDirection GetFlowDirection(CellCoordinate cell) =>
            GetWater(cell).Flow;

        internal bool StageResolvedCell(CellCoordinate cell, WaterData water)
        {
            water.Normalize();
            if (GetWater(cell).Equals(water))
            {
                stagedCells.Remove(cell);
                return false;
            }

            stagedCells[cell] = water;
            return true;
        }

        internal void CancelResolutionPass() => stagedCells.Clear();

        internal void SynchronizeFromPersistent(CellCoordinate cell) =>
            stagedCells.Remove(cell);

        internal IEnumerable<KeyValuePair<CellCoordinate, WaterData>>
            EnumerateStagedCells() => stagedCells;

        internal void ReplaceWaterBodies(IReadOnlyList<WaterBody> bodies)
        {
            waterBodies = bodies ?? Array.Empty<WaterBody>();
            waterBodyIdsByColumn.Clear();
            waterBodiesById.Clear();
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
                    waterBodyIdsByColumn[
                        new CellColumnCoordinate(cell.X, cell.Z)] = body.Id;
                }
            }
        }

        private bool ContainsColumn(int x, int z) =>
            (uint)x < worldSize && (uint)z < worldSize;

        private bool ContainsCell(int x, int y, int z) =>
            (uint)x < worldSize
            && (uint)y < worldHeight
            && (uint)z < worldSize;
    }
}
