using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniCivilization.World.Generation.Patterns;

namespace MiniCivilization.World.Runtime
{
    internal sealed class PatternMapPreparationScheduler : IDisposable
    {
        private readonly PatternMapStore store;
        private readonly TerrainPatternTileBuilder terrainBuilder;
        private readonly HydrologyPatternTileBuilder hydrologyBuilder;
        private readonly int maximumConcurrentBuilds;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Dictionary<PatternTileKey, Task<TerrainPatternTile>>
            terrainBuilds = new();
        private readonly Dictionary<PatternTileKey, Task<HydrologyPatternTile>>
            hydrologyBuilds = new();
        private readonly PatternMapDemand streamingDemand = new();
        private readonly PatternMapDemand debuggerDemand = new();
        private bool disposed;

        public PatternMapPreparationScheduler(
            PatternMapStore store,
            TerrainPatternTileBuilder terrainBuilder,
            HydrologyPatternTileBuilder hydrologyBuilder,
            int maximumConcurrentBuilds)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.terrainBuilder = terrainBuilder
                ?? throw new ArgumentNullException(nameof(terrainBuilder));
            this.hydrologyBuilder = hydrologyBuilder
                ?? throw new ArgumentNullException(nameof(hydrologyBuilder));
            if (maximumConcurrentBuilds <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumConcurrentBuilds));
            }

            this.maximumConcurrentBuilds = maximumConcurrentBuilds;
        }

        public int ActiveBuildCount => terrainBuilds.Count + hydrologyBuilds.Count;

        public void SetStreamingDemand(
            IReadOnlyList<PatternTileKey> demand,
            PatternTileKey anchor) => ReplaceDemand(
            streamingDemand,
            demand,
            anchor,
            nameof(demand));

        public void SetDebuggerDemand(
            IReadOnlyList<PatternTileKey> demand,
            PatternTileKey anchor) => ReplaceDemand(
            debuggerDemand,
            demand,
            anchor,
            nameof(demand));

        public void Update()
        {
            ThrowIfDisposed();
            CollectTerrainBuilds();
            CollectHydrologyBuilds();
            StartBuilds();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            cancellation.Cancel();
            cancellation.Dispose();
            terrainBuilds.Clear();
            hydrologyBuilds.Clear();
            streamingDemand.Keys.Clear();
            debuggerDemand.Keys.Clear();
        }

        private void CollectTerrainBuilds()
        {
            var completed = new List<PatternTileKey>();
            foreach (var pair in terrainBuilds)
            {
                if (pair.Value.IsCompleted)
                {
                    completed.Add(pair.Key);
                }
            }

            for (var index = 0; index < completed.Count; index++)
            {
                var task = terrainBuilds[completed[index]];
                terrainBuilds.Remove(completed[index]);
                if (task.IsCanceled)
                {
                    continue;
                }

                task.GetAwaiter().GetResult();
            }
        }

        private void CollectHydrologyBuilds()
        {
            var completed = new List<PatternTileKey>();
            foreach (var pair in hydrologyBuilds)
            {
                if (pair.Value.IsCompleted)
                {
                    completed.Add(pair.Key);
                }
            }

            for (var index = 0; index < completed.Count; index++)
            {
                var key = completed[index];
                var task = hydrologyBuilds[key];
                hydrologyBuilds.Remove(key);
                if (task.IsCanceled)
                {
                    continue;
                }

                store.SealHydrology(task.GetAwaiter().GetResult());
            }
        }

        private void StartBuilds()
        {
            var ordered = BuildOrderedDemand();
            for (var index = 0;
                 index < ordered.Count && ActiveBuildCount < maximumConcurrentBuilds;
                 index++)
            {
                var key = ordered[index];
                if (!store.TryGetTerrain(key, out _))
                {
                    StartTerrainBuild(key);
                    continue;
                }

                if (!store.TryGetHydrology(key, out _))
                {
                    StartHydrologyBuild(key);
                }
            }
        }

        private void StartTerrainBuild(PatternTileKey key)
        {
            if (terrainBuilds.ContainsKey(key))
            {
                return;
            }

            var token = cancellation.Token;
            terrainBuilds.Add(key, Task.Run(
                () => store.GetOrBuildTerrain(key, terrainBuilder, token),
                token));
        }

        private void StartHydrologyBuild(PatternTileKey key)
        {
            if (hydrologyBuilds.ContainsKey(key))
            {
                return;
            }

            var token = cancellation.Token;
            hydrologyBuilds.Add(key, Task.Run(
                () => hydrologyBuilder.Build(key, token),
                token));
        }

        private List<PatternTileKey> BuildOrderedDemand()
        {
            var distances = new Dictionary<PatternTileKey, ulong>(
                streamingDemand.Keys.Count + debuggerDemand.Keys.Count);
            AddDemand(streamingDemand, distances);
            AddDemand(debuggerDemand, distances);
            var result = new List<PatternTileKey>(distances.Keys);
            result.Sort((left, right) =>
            {
                var distance = distances[left].CompareTo(distances[right]);
                return distance != 0 ? distance : left.CompareTo(right);
            });
            return result;
        }

        private static void AddDemand(
            PatternMapDemand source,
            IDictionary<PatternTileKey, ulong> distances)
        {
            for (var index = 0; index < source.Keys.Count; index++)
            {
                var key = source.Keys[index];
                var distance = CalculateDistanceSquared(key, source.Anchor);
                if (!distances.TryGetValue(key, out var current)
                    || distance < current)
                {
                    distances[key] = distance;
                }
            }
        }

        private static void ReplaceDemand(
            PatternMapDemand target,
            IReadOnlyList<PatternTileKey> source,
            PatternTileKey anchor,
            string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            target.Keys.Clear();
            target.Anchor = anchor;
            var seen = new HashSet<PatternTileKey>();
            for (var index = 0; index < source.Count; index++)
            {
                if (seen.Add(source[index]))
                {
                    target.Keys.Add(source[index]);
                }
            }
        }

        private static ulong CalculateDistanceSquared(
            PatternTileKey key,
            PatternTileKey anchor)
        {
            var x = AbsoluteDistance(key.X, anchor.X);
            var z = AbsoluteDistance(key.Z, anchor.Z);
            var xSquared = x * x;
            var zSquared = z * z;
            return ulong.MaxValue - xSquared < zSquared
                ? ulong.MaxValue
                : xSquared + zSquared;
        }

        private static ulong AbsoluteDistance(int left, int right)
        {
            var distance = (long)left - right;
            return distance < 0L
                ? (ulong)-distance
                : (ulong)distance;
        }

        private sealed class PatternMapDemand
        {
            public List<PatternTileKey> Keys { get; } = new();
            public PatternTileKey Anchor { get; set; }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(PatternMapPreparationScheduler));
            }
        }
    }
}
