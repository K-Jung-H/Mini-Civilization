using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    internal interface IWaterMapBrush
    {
        HydrologyFeatureKey Key { get; }
        PatternTileBounds? Bounds { get; }
        bool TrySample(int x, int z, TerrainPatternCell terrainCell, out HydrologyDrawingSample sample);
    }

    internal sealed class WaterBrushCatalog
    {
        private sealed class PendingBrush
        {
            public TaskCompletionSource<IWaterMapBrush> Completion { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly object gate = new();
        private readonly Dictionary<HydrologyFeatureKey, IWaterMapBrush> brushes = new();
        private readonly Dictionary<HydrologyFeatureKey, PendingBrush> pending = new();

        internal void Retain(IReadOnlyList<PatternTileBounds> required)
        {
            lock (gate)
            {
                if (pending.Count != 0) return;
                foreach (var key in new List<HydrologyFeatureKey>(brushes.Keys))
                {
                    var keep = false;
                    if (brushes[key].Bounds is PatternTileBounds bounds)
                        foreach (var area in required)
                            if (bounds.Intersects(area)) { keep = true; break; }
                    if (!keep) brushes.Remove(key);
                }
            }
        }

        public BasinWaterBrush GetOrCreateBasin(
            HydrologyFeatureKey key,
            Func<BasinWaterBrush> create,
            CancellationToken cancellationToken)
        {
            return GetOrCreate(key, create, cancellationToken);
        }

        public RiverWaterBrush GetOrCreateRiver(
            HydrologyFeatureKey key,
            Func<RiverWaterBrush> create,
            CancellationToken cancellationToken)
        {
            return GetOrCreate(key, create, cancellationToken);
        }

        private T GetOrCreate<T>(
            HydrologyFeatureKey key,
            Func<T> create,
            CancellationToken cancellationToken)
            where T : class, IWaterMapBrush
        {
            if (create == null)
            {
                throw new ArgumentNullException(nameof(create));
            }

            cancellationToken.ThrowIfCancellationRequested();
            PendingBrush build = null;
            var buildHere = false;
            lock (gate)
            {
                if (brushes.TryGetValue(key, out var existing))
                {
                    return existing as T ?? throw new InvalidOperationException(
                        "Water Brush key has an incompatible brush type.");
                }

                if (!pending.TryGetValue(key, out build))
                {
                    build = new PendingBrush();
                    pending.Add(key, build);
                    buildHere = true;
                }
            }

            if (buildHere)
            {
                try
                {
                    var brush = create();
                    lock (gate)
                    {
                        brushes.Add(key, brush);
                        pending.Remove(key);
                    }

                    build.Completion.TrySetResult(brush);
                    return brush;
                }
                catch (Exception exception)
                {
                    lock (gate)
                    {
                        pending.Remove(key);
                    }

                    build.Completion.TrySetException(exception);
                    throw;
                }
            }

            build.Completion.Task.Wait(cancellationToken);
            return build.Completion.Task.GetAwaiter().GetResult() as T
                ?? throw new InvalidOperationException(
                    "Water Brush key has an incompatible brush type.");
        }
    }

}
