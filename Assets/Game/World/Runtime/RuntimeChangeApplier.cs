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
            IReadOnlyList<int> changedCellIndices,
            IReadOnlyList<int> changedColumnIndices,
            IReadOnlyList<ChunkCoordinate> affectedChunks,
            CellBounds affectedBounds,
            bool rebuildNavigationColumns,
            bool rebuildWaterDistances)
        {
            if (changedCellIndices == null)
            {
                throw new ArgumentNullException(nameof(changedCellIndices));
            }

            if (changedColumnIndices == null)
            {
                throw new ArgumentNullException(nameof(changedColumnIndices));
            }

            if (affectedChunks == null)
            {
                throw new ArgumentNullException(nameof(affectedChunks));
            }

            RebuildDerived(
                changeTypes,
                changedColumnIndices,
                rebuildNavigationColumns,
                rebuildWaterDistances);

            var changeId = runtime.AdvanceChangeId();

            return new WorldChangeSet(
                runtime.Data,
                changeId,
                changeTypes,
                changedCellIndices,
                changedColumnIndices,
                affectedChunks,
                affectedBounds);
        }

        public void RebuildDerived(
            WorldChangeType changeTypes,
            IReadOnlyList<int> changedColumnIndices,
            bool rebuildNavigationColumns,
            bool rebuildWaterDistances)
        {
            if (changedColumnIndices == null)
            {
                throw new ArgumentNullException(nameof(changedColumnIndices));
            }

            var surfaceChanged = (changeTypes & (
                WorldChangeType.CellStructure
                | WorldChangeType.Surface
                | WorldChangeType.WaterTopology
                | WorldChangeType.WaterSurface)) != 0;
            if (surfaceChanged)
            {
                for (var index = 0; index < changedColumnIndices.Count; index++)
                {
                    WorldIndex.DecodeColumn(
                        runtime.Data,
                        changedColumnIndices[index],
                        out var x,
                        out var z);
                    runtime.SurfaceCache.Rebuild(x, z);
                }
            }

            if (rebuildNavigationColumns)
            {
                runtime.NavigationCache.RebuildColumns(changedColumnIndices);
            }

            if (rebuildWaterDistances)
            {
                runtime.NavigationCache.RebuildWaterDistances(
                    changedColumnIndices);
            }

        }
    }
}
