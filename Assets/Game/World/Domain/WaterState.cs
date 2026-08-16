using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    [Serializable]
    public sealed class WaterFlowScheduleData
    {
        private CellCoordinate[] frontierCells = Array.Empty<CellCoordinate>();

        public IReadOnlyList<CellCoordinate> FrontierCells => frontierCells;
        public bool HasPendingFlow => frontierCells.Length > 0;

        internal void ReplaceFrontier(IReadOnlyCollection<CellCoordinate> values)
        {
            if (values == null || values.Count == 0)
            {
                frontierCells = Array.Empty<CellCoordinate>();
                return;
            }

            frontierCells = new CellCoordinate[values.Count];
            var index = 0;
            foreach (var value in values)
            {
                frontierCells[index++] = value;
            }
        }
    }
}
