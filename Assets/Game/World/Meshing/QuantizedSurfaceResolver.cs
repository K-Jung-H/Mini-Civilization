using System;

namespace MiniCivilization.World.Meshing
{
    public readonly struct QuantizedEdgeProfile
    {
        public readonly int InnerHeightUnits;
        public readonly int OuterHeightUnits;
        public readonly int NeighborHeightUnits;
        public readonly int VerticalDropUnits;

        public QuantizedEdgeProfile(int innerHeightUnits, int outerHeightUnits, int neighborHeightUnits)
        {
            InnerHeightUnits = innerHeightUnits;
            OuterHeightUnits = outerHeightUnits;
            NeighborHeightUnits = neighborHeightUnits;
            VerticalDropUnits = Math.Max(0, outerHeightUnits - neighborHeightUnits);
        }
    }

    public static class QuantizedSurfaceResolver
    {
        public static QuantizedEdgeProfile Resolve(int currentHeightUnits, int neighborHeightUnits, bool neighborExists)
        {
            if (!neighborExists || currentHeightUnits <= neighborHeightUnits)
            {
                return new QuantizedEdgeProfile(currentHeightUnits, currentHeightUnits, neighborHeightUnits);
            }

            var outer = Math.Max(currentHeightUnits - 1, neighborHeightUnits);
            return new QuantizedEdgeProfile(currentHeightUnits, outer, neighborHeightUnits);
        }
    }
}
