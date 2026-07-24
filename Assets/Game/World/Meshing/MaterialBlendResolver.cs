using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Meshing
{
    public static class MaterialBlendResolver
    {
        private const float BlendBand = 0.2f;
        private const float TotalBlendWidth = BlendBand * 2f;

        public static SurfaceAppearance ResolveTerrain(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            float localX,
            float localZ)
        {
            return ResolveTerrain(world, catalog, x, z, localX, localZ, null);
        }

        public static SurfaceAppearance ResolveTerrain(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            float localX,
            float localZ,
            SurfaceType surfaceOverride)
        {
            return ResolveTerrain(world, catalog, x, z, localX, localZ, (SurfaceType?)surfaceOverride);
        }

        public static SurfaceAppearance ResolveWater(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            float localX,
            float localZ)
        {
            ResolveAxis(localX, out var offsetX, out var blendX);
            ResolveAxis(localZ, out var offsetZ, out var blendZ);

            var currentColumn = world.GetSurfaceColumn(x, z);
            var current = ResolveWaterAppearance(catalog, currentColumn.Water);
            var xAppearance = ResolveWaterNeighbor(world, catalog, x + offsetX, z, current);
            var zAppearance = ResolveWaterNeighbor(world, catalog, x, z + offsetZ, current);
            var diagonalAppearance = ResolveWaterNeighbor(world, catalog, x + offsetX, z + offsetZ, current);

            var row0 = SurfaceAppearance.Lerp(current, xAppearance, blendX);
            var row1 = SurfaceAppearance.Lerp(zAppearance, diagonalAppearance, blendX);
            return SurfaceAppearance.Lerp(row0, row1, blendZ);
        }

        internal static SurfaceAppearance ResolveTerrainAppearance(
            WorldSurfaceCatalog catalog,
            BiomeType biome,
            SurfaceType surface)
        {
            return catalog != null
                ? catalog.ResolveTerrain(biome, surface)
                : DefaultSurfacePalette.ResolveTerrain(biome, surface);
        }

        internal static SurfaceAppearance ResolveWaterAppearance(
            WorldSurfaceCatalog catalog,
            WaterType water)
        {
            return catalog != null
                ? catalog.ResolveWater(water)
                : DefaultSurfacePalette.ResolveWater(water);
        }

        private static SurfaceAppearance ResolveTerrain(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            float localX,
            float localZ,
            SurfaceType? surfaceOverride)
        {
            ResolveAxis(localX, out var offsetX, out var blendX);
            ResolveAxis(localZ, out var offsetZ, out var blendZ);

            var currentColumn = world.GetSurfaceColumn(x, z);
            var currentSurface = surfaceOverride ?? currentColumn.Surface;
            var current = ResolveTerrainAppearance(
                catalog,
                world.GetColumnEnvironment(x, z).Biome,
                currentSurface);
            var xAppearance = ResolveTerrainNeighbor(
                world, catalog, x + offsetX, z, surfaceOverride, current);
            var zAppearance = ResolveTerrainNeighbor(
                world, catalog, x, z + offsetZ, surfaceOverride, current);
            var diagonalAppearance = ResolveTerrainNeighbor(
                world, catalog, x + offsetX, z + offsetZ, surfaceOverride, current);

            var row0 = SurfaceAppearance.Lerp(current, xAppearance, blendX);
            var row1 = SurfaceAppearance.Lerp(zAppearance, diagonalAppearance, blendX);
            return SurfaceAppearance.Lerp(row0, row1, blendZ);
        }

        private static void ResolveAxis(float localCoordinate, out int neighborOffset, out float blend)
        {
            if (localCoordinate < BlendBand)
            {
                neighborOffset = -1;
                blend = (BlendBand - localCoordinate) / TotalBlendWidth;
            }
            else if (localCoordinate > 1f - BlendBand)
            {
                neighborOffset = 1;
                blend = (localCoordinate - (1f - BlendBand)) / TotalBlendWidth;
            }
            else
            {
                neighborOffset = 0;
                blend = 0f;
            }
        }

        private static SurfaceAppearance ResolveTerrainNeighbor(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            SurfaceType? surfaceOverride,
            in SurfaceAppearance fallback)
        {
            if (!world.ContainsColumn(x, z))
            {
                return fallback;
            }

            var column = world.GetSurfaceColumn(x, z);
            if (!column.HasSurface)
            {
                return fallback;
            }

            return ResolveTerrainAppearance(
                catalog,
                world.GetColumnEnvironment(x, z).Biome,
                surfaceOverride ?? column.Surface);
        }

        private static SurfaceAppearance ResolveWaterNeighbor(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            in SurfaceAppearance fallback)
        {
            if (!world.ContainsColumn(x, z))
            {
                return fallback;
            }

            var column = world.GetSurfaceColumn(x, z);
            return column.HasWater
                ? ResolveWaterAppearance(catalog, column.Water)
                : fallback;
        }
    }
}
