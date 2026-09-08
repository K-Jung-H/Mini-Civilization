using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    public sealed class WaterFlowState
    {
        private readonly WorldData world;
        private readonly Dictionary<CellColumnCoordinate, int>
            waterBodyIdsByColumn = new();
        private readonly Dictionary<int, WaterBody> waterBodiesById = new();
        private readonly Dictionary<CellCoordinate, WaterData> stagedCells = new();
        private IReadOnlyList<WaterBody> waterBodies = Array.Empty<WaterBody>();
        private int nextBodyId = 1;
        internal object TopologyGraph { get; set; }
        private Func<int, int, int> graphLookup;

        private readonly List<WaterBody> graphBodies = new();
        private readonly Dictionary<int, int> graphBodyPositions = new();

        internal void UpdateGraphBodies(IReadOnlyCollection<int> removed, IReadOnlyList<WaterBody> added,
            Func<int, int, int> lookup)
        {
            if (graphLookup == null)
            {
                graphBodies.Clear(); graphBodyPositions.Clear();
                waterBodyIdsByColumn.Clear(); waterBodiesById.Clear();
                waterBodies = graphBodies;
                graphLookup = lookup;
            }
            foreach (var id in removed)
            {
                if (!graphBodyPositions.Remove(id, out var index)) continue;
                var lastIndex = graphBodies.Count - 1;
                if (index != lastIndex)
                {
                    var last = graphBodies[lastIndex];
                    graphBodies[index] = last;
                    graphBodyPositions[last.Id] = index;
                }
                graphBodies.RemoveAt(lastIndex);
                waterBodiesById.Remove(id);
            }
            foreach (var body in added)
            {
                graphBodyPositions.Add(body.Id, graphBodies.Count);
                graphBodies.Add(body);
                waterBodiesById.Add(body.Id, body);
            }
        }

        internal int GetIndexedWaterBodyId(int x, int z) =>
            graphLookup != null ? graphLookup(x, z) :
            waterBodyIdsByColumn.TryGetValue(new CellColumnCoordinate(x, z), out var id) ? id : 0;

        internal int AllocateWaterBodyId() => nextBodyId++;

        internal void MergeWaterBodies(HashSet<int> connected, WaterBody addition)
        {
            var target = addition;
            foreach (var id in connected)
                if (waterBodiesById.TryGetValue(id, out var body) && body.Cells.Count > target.Cells.Count)
                    target = body;
            var updated = new List<WaterBody>(waterBodies.Count + 1);
            foreach (var body in waterBodies)
            {
                if (!connected.Contains(body.Id)) { updated.Add(body); continue; }
                if (body == target) continue;
                Append(body);
                waterBodiesById.Remove(body.Id);
            }
            if (target != addition) Append(addition);
            else
                foreach (var cell in addition.Cells)
                    waterBodyIdsByColumn[new CellColumnCoordinate(cell.X, cell.Z)] = target.Id;
            updated.Add(target);
            waterBodiesById[target.Id] = target;
            waterBodies = updated;

            void Append(WaterBody source)
            {
                foreach (var cell in source.Cells)
                {
                    target.Add(cell);
                    waterBodyIdsByColumn[new CellColumnCoordinate(cell.X, cell.Z)] = target.Id;
                }
                target.VolumeUnits += source.VolumeUnits;
                target.SurfaceCellCount += source.SurfaceCellCount;
                target.TouchesWorldEdge |= source.TouchesWorldEdge;
            }
        }

        internal void ReplaceAffectedWaterBodies(HashSet<int> removed, IReadOnlyList<WaterBody> added)
        {
            var updated = new List<WaterBody>(waterBodies.Count + added.Count);
            foreach (var body in waterBodies)
            {
                if (!removed.Contains(body.Id)) { updated.Add(body); continue; }
                waterBodiesById.Remove(body.Id);
                foreach (var cell in body.Cells)
                    waterBodyIdsByColumn.Remove(new CellColumnCoordinate(cell.X, cell.Z));
            }
            foreach (var body in added)
            {
                updated.Add(body);
                waterBodiesById.Add(body.Id, body);
                foreach (var cell in body.Cells)
                    waterBodyIdsByColumn[new CellColumnCoordinate(cell.X, cell.Z)] = body.Id;
            }
            waterBodies = updated;
        }

        public IReadOnlyList<WaterBody> WaterBodies => waterBodies;
        public bool IsRecalculating { get; internal set; }

        internal WaterFlowState(
            WorldData world,
            IReadOnlyList<WaterBody> bodies)
        {
            this.world = world
                ?? throw new ArgumentNullException(nameof(world));
            ReplaceWaterBodies(bodies);
        }

        public int GetWaterBodyId(int x, int z)
        {
            if (!ContainsColumn(x, z))
            {
                return 0;
            }

            return GetIndexedWaterBodyId(x, z);
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

        internal bool BelongsTo(WorldData candidate) =>
            ReferenceEquals(world, candidate);

        internal IEnumerable<KeyValuePair<CellCoordinate, WaterData>>
            EnumerateStagedCells() => stagedCells;

        internal void ReplaceWaterBodies(IReadOnlyList<WaterBody> bodies)
        {
            graphLookup = null;
            TopologyGraph = null;
            graphBodies.Clear(); graphBodyPositions.Clear();
            waterBodies = bodies ?? Array.Empty<WaterBody>();
            waterBodyIdsByColumn.Clear();
            waterBodiesById.Clear();
            nextBodyId = 1;
            for (var bodyIndex = 0;
                 bodyIndex < waterBodies.Count;
                 bodyIndex++)
            {
                var body = waterBodies[bodyIndex];
                nextBodyId = Math.Max(nextBodyId, body.Id + 1);
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
            world.IsColumnLoaded(x, z);

        private bool ContainsCell(int x, int y, int z) =>
            world.IsValidHeight(y) && world.IsColumnLoaded(x, z);
    }
}
