using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    [Serializable]
    public sealed class WaterFlowScheduleData
    {
        private int[] frontierCellIndices = Array.Empty<int>();

        public IReadOnlyList<int> FrontierCellIndices => frontierCellIndices;
        public bool HasPendingFlow => frontierCellIndices.Length > 0;

        internal void ReplaceFrontier(IReadOnlyCollection<int> values)
        {
            if (values == null || values.Count == 0)
            {
                frontierCellIndices = Array.Empty<int>();
                return;
            }

            frontierCellIndices = new int[values.Count];
            var index = 0;
            foreach (var value in values)
            {
                frontierCellIndices[index++] = value;
            }
        }
    }
}
