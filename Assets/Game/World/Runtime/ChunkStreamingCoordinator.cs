using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Generation.Streaming;

namespace MiniCivilization.World.Runtime
{
    internal sealed class ChunkStreamingCoordinator
    {
        private readonly WorldData world;
        private readonly StreamingFeatureWorld features;
        private readonly Queue<ChunkCoordinate> pendingBuilds = new();
        private readonly HashSet<ChunkCoordinate> pendingBuildSet = new();
        private readonly List<ChunkCoordinate> candidates = new();
        private StreamingChunkDemand demand = StreamingChunkDemand.Empty;
        private Task<WorldChunkBuildData> activeBuild;
        private ChunkCoordinate activeCoordinate;

        public ChunkStreamingCoordinator(WorldData world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            features = new StreamingFeatureWorld(world.Settings);
        }

        public bool HasWork => activeBuild != null || pendingBuilds.Count > 0;

        public void SetDemand(StreamingChunkDemand value)
        {
            demand = value ?? throw new ArgumentNullException(nameof(value));
            features.SetLeaseChunks(demand.PreparedChunks);
            RebuildPendingQueue();
            StartNextBuild();
        }

        public bool TryTakeCompleted(out WorldChunkBuildData build)
        {
            if (activeBuild == null || !activeBuild.IsCompleted)
            {
                build = null;
                return false;
            }

            var completed = activeBuild;
            activeBuild = null;
            try
            {
                build = completed.GetAwaiter().GetResult();
                return true;
            }
            catch
            {
                RebuildPendingQueue();
                throw;
            }
            finally
            {
                StartNextBuild();
            }
        }

        public bool IsPrepared(ChunkCoordinate coordinate) =>
            demand.IsPrepared(coordinate);

        internal StreamingPatternMapSession OpenPatternMapSession(
            in WorldCellRectangle rectangle) =>
            features.OpenPatternMapSession(rectangle);

        public void Clear()
        {
            demand = StreamingChunkDemand.Empty;
            pendingBuilds.Clear();
            pendingBuildSet.Clear();
            candidates.Clear();
            features.SetLeaseChunks(Array.Empty<ChunkCoordinate>());
        }

        private void StartNextBuild()
        {
            if (activeBuild != null)
            {
                return;
            }

            while (pendingBuilds.Count > 0)
            {
                var coordinate = pendingBuilds.Dequeue();
                pendingBuildSet.Remove(coordinate);
                if (!demand.IsPrepared(coordinate)
                    || world.IsChunkLoaded(coordinate))
                {
                    continue;
                }

                activeCoordinate = coordinate;
                activeBuild = Task.Run(() => StreamingWorldChunkMaterializer.Build(
                    features,
                    coordinate));
                return;
            }
        }

        private void RebuildPendingQueue()
        {
            pendingBuilds.Clear();
            pendingBuildSet.Clear();
            candidates.Clear();
            foreach (var coordinate in demand.PreparedChunks)
            {
                if (!world.IsChunkLoaded(coordinate))
                {
                    candidates.Add(coordinate);
                }
            }

            candidates.Sort(ComparePriority);
            for (var index = 0; index < candidates.Count; index++)
            {
                var coordinate = candidates[index];
                if (activeBuild != null && coordinate.Equals(activeCoordinate))
                {
                    continue;
                }

                if (pendingBuildSet.Add(coordinate))
                {
                    pendingBuilds.Enqueue(coordinate);
                }
            }
        }

        private int ComparePriority(
            ChunkCoordinate left,
            ChunkCoordinate right)
        {
            var distance = DistanceSquared(left, demand.Center).CompareTo(
                DistanceSquared(right, demand.Center));
            if (distance != 0)
            {
                return distance;
            }

            var x = left.X.CompareTo(right.X);
            return x != 0 ? x : left.Z.CompareTo(right.Z);
        }

        private static long DistanceSquared(
            ChunkCoordinate coordinate,
            ChunkCoordinate center)
        {
            var x = (long)coordinate.X - center.X;
            var z = (long)coordinate.Z - center.Z;
            return x * x + z * z;
        }
    }
}
