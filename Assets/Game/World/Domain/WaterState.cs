using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    [Serializable]
    public sealed class WaterFlowScheduleData
    {
        private CellCoordinate[] frontierCells = Array.Empty<CellCoordinate>();
        private Func<CellCoordinate[]> snapshot;
        private Func<bool> hasPending;

        public IReadOnlyList<CellCoordinate> FrontierCells
        {
            get
            {
                if (snapshot != null)
                {
                    frontierCells = snapshot();
                    snapshot = null;
                }
                return frontierCells;
            }
        }
        public bool HasPendingFlow => hasPending?.Invoke() ?? frontierCells.Length > 0;

        internal void InvalidateSnapshot(Func<CellCoordinate[]> capture, Func<bool> pending)
        {
            snapshot = capture;
            hasPending = pending;
        }

        internal void ReplaceFrontier(IReadOnlyCollection<CellCoordinate> values)
        {
            snapshot = null;
            hasPending = null;
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
