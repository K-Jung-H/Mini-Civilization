using System;
using System.Collections.Generic;
using System.Threading;
namespace MiniCivilization.World.Generation.Patterns
{
    // Records resolved values only; no feature selection or terrain sampling.
    internal sealed class WaterMapPainter
    {
        public HydrologyPatternTile Paint(PatternTileKey key, PatternTileBounds bounds,
            IReadOnlyList<HydrologyDrawingSample?> resolved, CancellationToken cancellationToken)
        {
            var count = checked(bounds.Width * bounds.Height);
            if (resolved == null || resolved.Count != count)
                throw new ArgumentException("Resolved samples must cover the output core.", nameof(resolved));
            // Assign feature indices in the original pixel order, independent of drawing order.
            var featureIndices = new Dictionary<HydrologyFeatureKey, int>();
            var features = new List<HydrologyFeatureKey>();
            var cells = new HydrologyPatternCell[count];
            for (var index = 0; index < count; index++)
            {
                if (index % bounds.Width == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!resolved[index].HasValue)
                {
                    cells[index] = HydrologyPatternCell.None;
                    continue;
                }
                var sample = resolved[index].Value;
                if (!featureIndices.TryGetValue(sample.Key, out var featureIndex))
                {
                    featureIndex = features.Count;
                    featureIndices.Add(sample.Key, featureIndex);
                    features.Add(sample.Key);
                }
                cells[index] = sample.ResolvedHeights
                    ? HydrologyPatternCell.CreateResolved(sample.GetResolvedHeights(),
                        sample.WaterType, featureIndex, sample.InteriorInfluence, sample.BoundaryInfluence)
                    : sample.HasWater
                    ? HydrologyPatternCell.CreateWater(
                        sample.WaterType, featureIndex, sample.GroundHeight,
                        sample.WaterSurfaceHeight, sample.InteriorInfluence, sample.BoundaryInfluence)
                    : HydrologyPatternCell.CreateGroundOverride(
                        featureIndex, sample.GroundHeight, sample.BoundaryInfluence);
            }
            return new HydrologyPatternTile(key, bounds, features.ToArray(), cells);
        }

    }
}
