using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    public enum WaterBodyType : byte
    {
        Pond,
        Lake,
        Sea
    }

    public sealed class WaterBody
    {
        public int Id { get; }
        public WaterBodyType Type { get; internal set; }
        public int VolumeUnits { get; internal set; }
        public int SurfaceCellCount { get; internal set; }
        public bool TouchesWorldEdge { get; internal set; }
        public IReadOnlyList<CellCoordinate> Cells => cells;

        private readonly List<CellCoordinate> cells = new();

        internal WaterBody(int id)
        {
            Id = id;
        }

        internal void Add(CellCoordinate coordinate) => cells.Add(coordinate);
    }
}
