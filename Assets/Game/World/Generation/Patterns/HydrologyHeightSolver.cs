using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Generation.Patterns
{
    // Pure local formulas. No sealed output or recursively resolved neighbour is read.
    internal static class HydrologyHeightSolver
    {
        public const int Halo = 1;

        public static PatternTileBounds ReadBounds(PatternTileBounds core) => new(
            checked(core.MinimumX - Halo), checked(core.MinimumZ - Halo),
            checked(core.MaximumXExclusive + Halo), checked(core.MaximumZExclusive + Halo));

        public static HydrologyDrawingSample ResolveBasinInterior(
            IReadOnlyList<HydrologyContribution> inputs)
        {
            // A lower basin owns the overlap as the receiving reach. Never average
            // its surface upward beyond that basin's Terrain-derived upper bound.
            var found = false;
            var representative = default(HydrologyDrawingSample);
            foreach (var input in inputs)
            {
                if (input.Kind != HydrologyContributionKind.Basin || !input.Sample.HasWater) continue;
                var sample = input.Sample;
                if (!found || sample.WaterSurfaceHeight < representative.WaterSurfaceHeight
                    || (sample.WaterSurfaceHeight == representative.WaterSurfaceHeight
                        && (sample.InteriorInfluence > representative.InteriorInfluence
                            || (sample.InteriorInfluence == representative.InteriorInfluence
                                && sample.Key.CompareTo(representative.Key) < 0))))
                    representative = sample;
                found = true;
            }
            if (!found) throw new ArgumentException("No basin interior.", nameof(inputs));
            var water = PatternHeightQuantization.Round(representative.WaterSurfaceHeight);
            var depthUnits = PatternHeightQuantization.Round(representative.WaterSurfaceHeight - representative.GroundHeight);
            // Basin footprint is defined as wet geometry with a one-unit minimum depth.
            // Invalid producers are rejected, never repaired here.
            if (depthUnits < 1) throw new InvalidOperationException("Basin footprint depth is below one HeightUnit.");
            return Resolved(representative, PatternColumnHeights.Wet(checked(water - depthUnits), water),
                representative.InteriorInfluence);
        }

        public static HydrologyDrawingSample ResolveExterior(
            HydrologyDrawingSample? shore, float terrainHeight,
            IReadOnlyList<HydrologyDrawingSample> wetNeighbours)
        {
            var ground = PatternHeightQuantization.Round(shore?.GroundHeight ?? terrainHeight);
            var owner = shore ?? wetNeighbours[0];
            foreach (var neighbour in wetNeighbours)
            {
                ground = Math.Max(ground, checked(PatternHeightQuantization.Round(neighbour.WaterSurfaceHeight) + 1));
                if (neighbour.Key.CompareTo(owner.Key) < 0) owner = neighbour;
            }
            return Resolved(owner, PatternColumnHeights.Dry(ground), 0);
        }

        public static float RiverGround(float surface, float depthBelowWater, float cross)
        {
            // The stroke defines the water cavity, regardless of the original bank height.
            // Its dry exterior is resolved together with all neighbouring Source columns.
            return surface - (1f + (Math.Max(1f, depthBelowWater) - 1f) * cross);
        }

        public static HydrologyDrawingSample ResolveHeights(HydrologyDrawingSample target)
        {
            var ground = PatternHeightQuantization.Round(target.GroundHeight);
            var water = PatternHeightQuantization.Round(target.WaterSurfaceHeight);
            // The integer cross section defines the wet mask; it is never promoted after drawing.
            return Resolved(target, water > ground ? PatternColumnHeights.Wet(ground, water)
                : PatternColumnHeights.Dry(ground), target.InteriorInfluence);
        }

        public static HydrologyDrawingSample Resolved(HydrologyDrawingSample source,
            PatternColumnHeights heights, float interior)
        {
            if ((double)(float)heights.Ground != heights.Ground
                || (double)(float)heights.WaterSurface != heights.WaterSurface)
                throw new InvalidOperationException("Resolved height cannot be represented by the current map storage.");
            return new HydrologyDrawingSample(source.Key, heights.HasWater ? source.WaterType : WaterType.None,
                heights.Ground, heights.WaterSurface, interior, source.BoundaryInfluence,
                heights.HasWater, true);
        }
    }
}
