using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Runtime
{
    public sealed class RuntimeChangeApplier
    {
        private readonly WorldRuntime runtime;

        internal RuntimeChangeApplier(WorldRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public WorldChangeSet Apply(
            WorldChangeType changeTypes,
            IReadOnlyList<CellCoordinate> changedCells,
            IReadOnlyList<CellColumnCoordinate> changedColumns,
            IReadOnlyList<ChunkCoordinate> affectedChunks,
            CellBounds affectedBounds,
            bool rebuildNavigationColumns,
            bool rebuildWaterDistances)
        {
            if (changedCells == null)
            {
                throw new ArgumentNullException(nameof(changedCells));
            }

            if (changedColumns == null)
            {
                throw new ArgumentNullException(nameof(changedColumns));
            }

            if (affectedChunks == null)
            {
                throw new ArgumentNullException(nameof(affectedChunks));
            }

            RebuildDerived(
                changeTypes,
                changedColumns,
                rebuildNavigationColumns,
                rebuildWaterDistances);

            var changeId = runtime.AdvanceChangeId();

            return new WorldChangeSet(
                runtime.Data,
                changeId,
                changeTypes,
                changedCells,
                changedColumns,
                affectedChunks,
                affectedBounds);
        }

        public void RebuildDerived(
            WorldChangeType changeTypes,
            IReadOnlyList<CellColumnCoordinate> changedColumns,
            bool rebuildNavigationColumns,
            bool rebuildWaterDistances)
        {
            if (changedColumns == null)
            {
                throw new ArgumentNullException(nameof(changedColumns));
            }

            var surfaceChanged = (changeTypes & (
                WorldChangeType.CellStructure
                | WorldChangeType.Surface
                | WorldChangeType.WaterTopology
                | WorldChangeType.WaterSurface)) != 0;
            if (surfaceChanged)
            {
                for (var index = 0; index < changedColumns.Count; index++)
                {
                    var column = changedColumns[index];
                    runtime.SurfaceCache.Rebuild(column.X, column.Z);
                }
            }

            if (rebuildNavigationColumns)
            {
                runtime.NavigationCache.RebuildColumns(changedColumns);
            }

            if (rebuildWaterDistances)
            {
                runtime.NavigationCache.RebuildWaterDistances(
                    changedColumns);
            }

        }
    }
}
