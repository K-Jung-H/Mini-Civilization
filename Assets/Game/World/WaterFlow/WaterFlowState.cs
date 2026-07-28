using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    /// <summary>
    /// Runtime overlay used only while a queued water recalculation is in
    /// progress. Stable water state remains authoritative in CellData.Water.
    /// </summary>
    public sealed class WaterFlowState
    {
        private readonly WorldData world;
        private readonly int worldSize;
        private readonly int worldHeight;
        private readonly int[] waterBodyIdsByColumn;
        private readonly Dictionary<int, WaterBody> waterBodiesById = new();
        private readonly Dictionary<int, WaterCellData> resolvedCells = new();
        private readonly Dictionary<int, WaterCellData> nextResolvedCells = new();
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
            waterBodyIdsByColumn = new int[checked(world.Size * world.Size)];
            ReplaceWaterBodies(bodies);
        }

        public int GetWaterBodyId(int x, int z)
        {
            if (!ContainsColumn(x, z))
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

        internal bool TryGetWaterBody(int id, out WaterBody waterBody) =>
            waterBodiesById.TryGetValue(id, out waterBody);

        public WaterFlowDirectionMask GetFlowDirection(
            int x,
            int y,
            int z) =>
            ContainsCell(x, y, z)
                ? GetWater(WorldIndex.EncodeCell(world, x, y, z)).Direction
                : WaterFlowDirectionMask.None;

        internal WaterCellData GetWater(int cellIndex)
        {
            if (resolvedCells.TryGetValue(cellIndex, out var water))
            {
                return water;
            }

            var coordinate = WorldIndex.DecodeCell(world, cellIndex);
            return world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z).Water;
        }

        internal WaterFlowDirectionMask GetFlowDirection(int cellIndex) =>
            GetWater(cellIndex).Direction;

        internal bool StageResolvedCell(int cellIndex, WaterCellData water)
        {
            water.Normalize();
            if (GetWater(cellIndex).Equals(water))
            {
                return false;
            }

            nextResolvedCells[cellIndex] = water;
            return true;
        }

        internal void ApplyResolutionPass()
        {
            foreach (var pair in nextResolvedCells)
            {
                resolvedCells[pair.Key] = pair.Value;
            }

            nextResolvedCells.Clear();
        }

        internal void CancelResolutionPass() => nextResolvedCells.Clear();

        internal void SynchronizeFromPersistent(int cellIndex) =>
            resolvedCells.Remove(cellIndex);

        internal IEnumerable<KeyValuePair<int, WaterCellData>>
            EnumerateResolvedCells() => resolvedCells;

        internal void ClearResolvedCells()
        {
            resolvedCells.Clear();
            nextResolvedCells.Clear();
        }

        internal void ReplaceWaterBodies(IReadOnlyList<WaterBody> bodies)
        {
            waterBodies = bodies ?? Array.Empty<WaterBody>();
            Array.Clear(
                waterBodyIdsByColumn,
                0,
                waterBodyIdsByColumn.Length);
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
                        cell.X + worldSize * cell.Z] = body.Id;
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
