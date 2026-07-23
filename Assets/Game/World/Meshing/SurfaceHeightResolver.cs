using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Meshing
{
    internal enum SurfaceLayer : byte
    {
        Terrain,
        Water
    }

    internal readonly struct SurfaceEdgeProfile
    {
        public readonly int CurrentHeightUnits;
        public readonly int OuterHeightUnits;
        public readonly int NeighborHeightUnits;
        public readonly bool NeighborExists;

        public SurfaceEdgeProfile(
            int currentHeightUnits,
            int outerHeightUnits,
            int neighborHeightUnits,
            bool neighborExists)
        {
            CurrentHeightUnits = currentHeightUnits;
            OuterHeightUnits = outerHeightUnits;
            NeighborHeightUnits = neighborHeightUnits;
            NeighborExists = neighborExists;
        }
    }

    internal static class SurfaceHeightResolver
    {
        public static SurfaceEdgeProfile ResolveEdge(
            WorldData world,
            int x,
            int z,
            int directionX,
            int directionZ,
            SurfaceLayer layer)
        {
            var currentHeight = GetHeight(world.GetSurfaceColumn(x, z), layer);
            var neighborExists = TryGetHeight(
                world,
                x + directionX,
                z + directionZ,
                layer,
                out var neighborHeight);
            var quantized = QuantizedSurfaceResolver.Resolve(
                currentHeight,
                neighborHeight,
                neighborExists);

            return new SurfaceEdgeProfile(
                currentHeight,
                quantized.OuterHeightUnits,
                neighborHeight,
                neighborExists);
        }

        public static int ResolveCornerHeight(
            WorldData world,
            int x,
            int z,
            float cornerX,
            float cornerZ,
            SurfaceLayer layer)
        {
            var currentHeight = GetHeight(world.GetSurfaceColumn(x, z), layer);
            var directionX = cornerX >= 1f ? 1 : -1;
            var directionZ = cornerZ >= 1f ? 1 : -1;
            var lowerX = IsLower(
                world, x + directionX, z, layer, currentHeight);
            var lowerZ = IsLower(
                world, x, z + directionZ, layer, currentHeight);
            var lowerDiagonal = IsLower(
                world, x + directionX, z + directionZ, layer, currentHeight);

            // A concave corner is owned by the low cell whenever both
            // orthogonal neighbors facing this corner are higher. The
            // diagonal column does not determine whether the closure exists.
            if (TryResolveConcaveRiseCorner(
                    world,
                    x,
                    z,
                    directionX,
                    directionZ,
                    layer,
                    currentHeight,
                    out var concaveHeight))
            {
                return concaveHeight;
            }

            // A diagonal difference alone must never carve a corner notch.
            // One descending edge continues only when the diagonal continues
            // the same shoreline/contour. Two descending edges form a convex
            // step-pyramid corner.
            var continuesShoulder = lowerX && lowerZ
                || lowerX && lowerDiagonal
                || lowerZ && lowerDiagonal;
            return continuesShoulder ? currentHeight - 1 : currentHeight;
        }

        private static bool TryResolveConcaveRiseCorner(
            WorldData world,
            int x,
            int z,
            int directionX,
            int directionZ,
            SurfaceLayer layer,
            int currentHeight,
            out int resolvedHeight)
        {
            if (!TryGetHeight(
                    world,
                    x + directionX,
                    z,
                    layer,
                    out var xHeight)
                || !TryGetHeight(
                    world,
                    x,
                    z + directionZ,
                    layer,
                    out var zHeight)
                || !TryGetHeight(
                    world,
                    x + directionX,
                    z + directionZ,
                    layer,
                    out var diagonalHeight)
                || xHeight <= currentHeight
                || zHeight <= currentHeight
                || diagonalHeight <= currentHeight)
            {
                resolvedHeight = currentHeight;
                return false;
            }

            resolvedHeight = System.Math.Min(
                currentHeight + 1,
                System.Math.Min(xHeight, zHeight));
            return true;
        }

        public static bool TryGetHeight(
            WorldData world,
            int x,
            int z,
            SurfaceLayer layer,
            out int height)
        {
            if (!world.ContainsColumn(x, z))
            {
                height = 0;
                return false;
            }

            var column = world.GetSurfaceColumn(x, z);
            if (layer == SurfaceLayer.Terrain)
            {
                height = column.SolidTopUnits;
                return column.HasSurface;
            }

            height = column.WaterTopUnits;
            return column.HasWater;
        }

        private static int GetHeight(in SurfaceColumnData column, SurfaceLayer layer)
            => layer == SurfaceLayer.Terrain ? column.SolidTopUnits : column.WaterTopUnits;

        private static bool IsLower(
            WorldData world,
            int x,
            int z,
            SurfaceLayer layer,
            int currentHeight)
            => TryGetHeight(world, x, z, layer, out var height)
                && height < currentHeight;
    }
}
