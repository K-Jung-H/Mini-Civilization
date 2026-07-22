using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Meshing
{
    internal enum SurfaceLayer : byte
    {
        Terrain,
        Water
    }

    internal static class SurfaceHeightResolver
    {
        public static int ResolveVertex(
            WorldData world,
            int x,
            int z,
            float localX,
            float localZ,
            SurfaceLayer layer)
        {
            var currentHeight = GetHeight(world.GetSurfaceColumn(x, z), layer);
            var onXBoundary = localX <= 0f || localX >= 1f;
            var onZBoundary = localZ <= 0f || localZ >= 1f;

            if (onXBoundary && onZBoundary)
            {
                return ResolveSharedCorner(world, x, z, localX, localZ, currentHeight, layer);
            }

            if (onXBoundary)
            {
                var offsetX = localX <= 0f ? -1 : 1;
                currentHeight = ResolveAgainstColumn(world, x + offsetX, z, currentHeight, layer);
            }

            if (onZBoundary)
            {
                var offsetZ = localZ <= 0f ? -1 : 1;
                currentHeight = ResolveAgainstColumn(world, x, z + offsetZ, currentHeight, layer);
            }

            return currentHeight;
        }

        private static int ResolveSharedCorner(
            WorldData world,
            int x,
            int z,
            float localX,
            float localZ,
            int currentHeight,
            SurfaceLayer layer)
        {
            var vertexX = x + (localX >= 1f ? 1 : 0);
            var vertexZ = z + (localZ >= 1f ? 1 : 0);
            var resolved = currentHeight;

            // All cells of the same height incident to a grid corner must resolve that
            // corner from the same four-column set. This keeps equal-height edges sealed
            // while lower tiers retain their own vertex for the vertical cliff face.
            for (var incidentZ = vertexZ - 1; incidentZ <= vertexZ; incidentZ++)
            for (var incidentX = vertexX - 1; incidentX <= vertexX; incidentX++)
            {
                resolved = ResolveAgainstColumn(world, incidentX, incidentZ, resolved, layer, currentHeight);
            }

            return resolved;
        }

        private static int ResolveAgainstColumn(
            WorldData world,
            int neighborX,
            int neighborZ,
            int currentResolvedHeight,
            SurfaceLayer layer,
            int profileHeight = -1)
        {
            if (!world.ContainsColumn(neighborX, neighborZ))
            {
                return currentResolvedHeight;
            }

            var column = world.GetSurfaceColumn(neighborX, neighborZ);
            if (!HasLayer(column, layer))
            {
                return currentResolvedHeight;
            }

            var currentHeight = profileHeight >= 0 ? profileHeight : currentResolvedHeight;
            var neighborHeight = GetHeight(column, layer);
            if (neighborHeight >= currentHeight)
            {
                return currentResolvedHeight;
            }

            var outerHeight = QuantizedSurfaceResolver.Resolve(currentHeight, neighborHeight, true).OuterHeightUnits;
            return Math.Min(currentResolvedHeight, outerHeight);
        }

        private static bool HasLayer(in SurfaceColumnData column, SurfaceLayer layer)
            => layer == SurfaceLayer.Terrain ? column.HasSurface : column.HasWater;

        private static int GetHeight(in SurfaceColumnData column, SurfaceLayer layer)
            => layer == SurfaceLayer.Terrain ? column.SolidTopUnits : column.WaterTopUnits;
    }
}
