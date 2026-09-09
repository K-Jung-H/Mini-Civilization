using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Generation.Patterns
{
    internal enum HydrologyContributionKind { Sea, Basin, River }

    internal readonly struct HydrologyContribution
    {
        public HydrologyContribution(HydrologyContributionKind kind, HydrologyDrawingSample sample)
        {
            Kind = kind;
            Sample = sample;
        }
        public HydrologyContributionKind Kind { get; }
        public HydrologyDrawingSample Sample { get; }
    }

    internal static class HydrologyOverlapResolver
    {
        // Sea precedence is unchanged. Basin overlaps retain the lower receiving surface.
        public static HydrologyDrawingSample? Resolve(IReadOnlyList<HydrologyContribution> contributions)
        {
            if (contributions == null || contributions.Count == 0) return null;
            var winner = contributions[0];
            for (var i = 1; i < contributions.Count; i++)
            {
                var candidate = contributions[i];
                var priority = Priority(candidate).CompareTo(Priority(winner));
                if (priority < 0 || (priority == 0
                    && candidate.Sample.Key.CompareTo(winner.Sample.Key) < 0))
                    winner = candidate;
            }
            if (winner.Kind == HydrologyContributionKind.Basin && winner.Sample.HasWater)
                return HydrologyHeightSolver.ResolveBasinInterior(contributions);
            if (winner.Kind == HydrologyContributionKind.River && winner.Sample.HasWater)
            {
                var sample = winner.Sample;
                var ground = sample.GroundHeight;
                foreach (var contribution in contributions)
                    if (contribution.Kind == HydrologyContributionKind.River && contribution.Sample.HasWater)
                        ground = Math.Min(ground, contribution.Sample.GroundHeight);
                return HydrologyHeightSolver.ResolveHeights(new HydrologyDrawingSample(sample.Key,
                    sample.WaterType, ground, sample.WaterSurfaceHeight, sample.InteriorInfluence,
                    sample.BoundaryInfluence, true));
            }
            if (winner.Kind != HydrologyContributionKind.Sea)
            {
                // Exterior envelopes compose commutatively, keeping every shore's support.
                var sample = winner.Sample;
                var ground = sample.GroundHeight;
                foreach (var contribution in contributions)
                    if (!contribution.Sample.HasWater)
                        ground = Math.Max(ground, contribution.Sample.GroundHeight);
                return new HydrologyDrawingSample(sample.Key, sample.WaterType, ground, 0,
                    0, sample.BoundaryInfluence, false);
            }
            return winner.Sample;
        }

        private static int Priority(HydrologyContribution contribution) => contribution.Kind switch
        {
            HydrologyContributionKind.Sea => 0,
            HydrologyContributionKind.Basin => contribution.Sample.HasWater ? 1 : 3,
            HydrologyContributionKind.River => contribution.Sample.HasWater ? 2 : 3,
            _ => throw new ArgumentOutOfRangeException(nameof(contribution))
        };
    }
}
