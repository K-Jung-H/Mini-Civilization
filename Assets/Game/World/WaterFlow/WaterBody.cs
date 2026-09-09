using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    public sealed class WaterBody
    {
        public int Id { get; }
        public int VolumeUnits { get; internal set; }
        public int SurfaceCellCount { get; internal set; }
        public bool TouchesWorldEdge { get; internal set; }
        public IReadOnlyList<CellCoordinate> Cells => composedCells ?? cells;
        private IReadOnlyList<CellCoordinate> composedCells;

        private readonly List<CellCoordinate> cells = new();

        internal WaterBody(int id)
        {
            Id = id;
        }

        internal void Add(CellCoordinate coordinate) => cells.Add(coordinate);

        // Used only while assembling an unpublished graph result.
        internal void AddPart(WaterBody part)
        {
            if (composedCells == null) composedCells = new SegmentedCells();
            ((SegmentedCells)composedCells).Append(part);
            VolumeUnits += part.VolumeUnits;
            SurfaceCellCount += part.SurfaceCellCount;
            TouchesWorldEdge |= part.TouchesWorldEdge;
        }

        internal WaterBody(int id, IReadOnlyList<WaterBody> parts) : this(id)
        {
            composedCells = new SegmentedCells(parts);
            foreach (var part in parts)
            {
                VolumeUnits += part.VolumeUnits;
                SurfaceCellCount += part.SurfaceCellCount;
                TouchesWorldEdge |= part.TouchesWorldEdge;
            }
        }

        private sealed class SegmentedCells : IReadOnlyList<CellCoordinate>
        {
            private readonly List<WaterBody> parts = new();
            private readonly List<int> ends = new();
            public int Count { get; private set; }
            internal SegmentedCells() { }
            internal void Append(WaterBody part)
            {
                parts.Add(part);
                Count += part.Cells.Count;
                ends.Add(Count);
            }
            internal SegmentedCells(IReadOnlyList<WaterBody> parts)
            {
                foreach (var part in parts) Append(part);
            }
            public CellCoordinate this[int index]
            {
                get
                {
                    if ((uint)index >= Count) throw new System.ArgumentOutOfRangeException(nameof(index));
                    var part = ends.BinarySearch(index + 1);
                    if (part < 0) part = ~part;
                    return parts[part].Cells[index - (part == 0 ? 0 : ends[part - 1])];
                }
            }
            public IEnumerator<CellCoordinate> GetEnumerator()
            { foreach (var part in parts) foreach (var cell in part.Cells) yield return cell; }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
