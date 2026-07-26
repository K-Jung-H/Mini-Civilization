using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Hydrology
{
    public enum WaterFlowDirection : sbyte
    {
        None = -1,
        East = 0,
        North = 1,
        West = 2,
        South = 3
    }

    [Flags]
    public enum WaterfallEdgeFlags : byte
    {
        None = 0,
        East = 1 << 0,
        North = 1 << 1,
        West = 1 << 2,
        South = 1 << 3
    }

    public sealed class HydrologyState
    {
        private readonly int worldSize;
        private readonly int[] waterBodyIdsByColumn;
        private readonly Dictionary<int, WaterBody> waterBodiesById = new();
        private readonly Queue<int> activeColumns = new();
        private readonly bool[] queuedColumns;
        private readonly WaterFlowDirection[] flowDirections;
        private readonly int[] upstreamColumns;
        private readonly WaterfallEdgeFlags[] waterfallEdges;
        private readonly byte[] stableTicks;
        private IReadOnlyList<WaterBody> waterBodies = Array.Empty<WaterBody>();

        public IReadOnlyList<WaterBody> WaterBodies => waterBodies;
        public int ActiveColumnCount => activeColumns.Count;
        public bool HasActiveColumns => activeColumns.Count > 0;

        internal HydrologyState(WorldData world, IReadOnlyList<WaterBody> bodies)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            worldSize = world.Size;
            var columnCount = checked(world.Size * world.Size);
            waterBodyIdsByColumn = new int[columnCount];
            queuedColumns = new bool[columnCount];
            flowDirections = new WaterFlowDirection[columnCount];
            upstreamColumns = new int[columnCount];
            waterfallEdges = new WaterfallEdgeFlags[columnCount];
            stableTicks = new byte[columnCount];
            Array.Fill(flowDirections, WaterFlowDirection.None);
            Array.Fill(upstreamColumns, -1);
            ReplaceWaterBodies(bodies);
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

        public WaterFlowDirection GetFlowDirection(int x, int z) =>
            ContainsColumn(x, z)
                ? flowDirections[x + worldSize * z]
                : WaterFlowDirection.None;

        public WaterfallEdgeFlags GetWaterfallEdges(int x, int z) =>
            ContainsColumn(x, z)
                ? waterfallEdges[x + worldSize * z]
                : WaterfallEdgeFlags.None;

        internal WaterFlowDirection GetFlowDirection(int columnIndex) =>
            flowDirections[columnIndex];

        internal void SetFlowDirection(
            int columnIndex,
            WaterFlowDirection direction) =>
            flowDirections[columnIndex] = direction;

        internal int GetUpstream(int columnIndex) => upstreamColumns[columnIndex];
        internal void SetUpstream(int columnIndex, int upstreamColumn) =>
            upstreamColumns[columnIndex] = upstreamColumn;

        internal bool ClearUpstreamIfMatches(
            int columnIndex,
            int expectedUpstream)
        {
            if (upstreamColumns[columnIndex] != expectedUpstream)
            {
                return false;
            }

            upstreamColumns[columnIndex] = -1;
            return true;
        }

        internal void SetWaterfallEdges(
            int columnIndex,
            WaterfallEdgeFlags flags) =>
            waterfallEdges[columnIndex] = flags;

        internal byte IncrementStableTicks(int columnIndex)
        {
            if (stableTicks[columnIndex] < byte.MaxValue)
            {
                stableTicks[columnIndex]++;
            }

            return stableTicks[columnIndex];
        }

        internal void ResetStableTicks(int columnIndex) => stableTicks[columnIndex] = 0;

        internal void Enqueue(int columnIndex)
        {
            if ((uint)columnIndex >= queuedColumns.Length || queuedColumns[columnIndex])
            {
                return;
            }

            queuedColumns[columnIndex] = true;
            activeColumns.Enqueue(columnIndex);
        }

        internal bool TryDequeue(out int columnIndex)
        {
            if (activeColumns.Count == 0)
            {
                columnIndex = -1;
                return false;
            }

            columnIndex = activeColumns.Dequeue();
            queuedColumns[columnIndex] = false;
            return true;
        }

        internal void EnqueueColumnAndNeighbors(int columnIndex)
        {
            Enqueue(columnIndex);
            var z = columnIndex / worldSize;
            var x = columnIndex - z * worldSize;
            if (x + 1 < worldSize) Enqueue(columnIndex + 1);
            if (x > 0) Enqueue(columnIndex - 1);
            if (z + 1 < worldSize) Enqueue(columnIndex + worldSize);
            if (z > 0) Enqueue(columnIndex - worldSize);
        }

        internal void ClearActiveColumns()
        {
            activeColumns.Clear();
            Array.Clear(queuedColumns, 0, queuedColumns.Length);
            Array.Clear(stableTicks, 0, stableTicks.Length);
        }

        internal void ReplaceWaterBodies(IReadOnlyList<WaterBody> bodies)
        {
            waterBodies = bodies ?? Array.Empty<WaterBody>();
            Array.Clear(waterBodyIdsByColumn, 0, waterBodyIdsByColumn.Length);
            waterBodiesById.Clear();
            for (var bodyIndex = 0; bodyIndex < waterBodies.Count; bodyIndex++)
            {
                var body = waterBodies[bodyIndex];
                waterBodiesById[body.Id] = body;
                for (var cellIndex = 0; cellIndex < body.Cells.Count; cellIndex++)
                {
                    var cell = body.Cells[cellIndex];
                    waterBodyIdsByColumn[cell.X + worldSize * cell.Z] = body.Id;
                }
            }
        }

        private bool ContainsColumn(int x, int z) =>
            (uint)x < worldSize && (uint)z < worldSize;
    }
}
