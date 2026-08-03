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

        public static SurfaceAppearance ResolveWaterCell(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int y,
            int z,
            float localX,
            float localZ)
        {
            return ResolveWaterAppearance(catalog);
        }

        public static SurfaceAppearance ResolveTerrainCell(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int y,
            int z,
            float localX,
            float localZ,
            SurfaceType? surfaceOverride = null)
        {
            ResolveAxis(localX, out var offsetX, out var blendX);
            ResolveAxis(localZ, out var offsetZ, out var blendZ);

            var current = ResolveTerrainCellAppearance(
                world,
                catalog,
                x,
                y,
                z,
                surfaceOverride,
                default,
                false);
            var xAppearance = ResolveTerrainCellAppearance(
                world,
                catalog,
                x + offsetX,
                y,
                z,
                surfaceOverride,
                current,
                true);
            var zAppearance = ResolveTerrainCellAppearance(
                world,
                catalog,
                x,
                y,
                z + offsetZ,
                surfaceOverride,
                current,
                true);
            var diagonalAppearance = ResolveTerrainCellAppearance(
                world,
                catalog,
                x + offsetX,
                y,
                z + offsetZ,
                surfaceOverride,
                current,
                true);

            var row0 = SurfaceAppearance.Lerp(current, xAppearance, blendX);
            var row1 = SurfaceAppearance.Lerp(
                zAppearance,
                diagonalAppearance,
                blendX);
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
            WorldSurfaceCatalog catalog)
        {
            return catalog != null
                ? catalog.ResolveWater()
                : DefaultSurfacePalette.ResolveWater();
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

        private static SurfaceAppearance ResolveTerrainCellAppearance(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int y,
            int z,
            SurfaceType? surfaceOverride,
            in SurfaceAppearance fallback,
            bool useFallback)
        {
            if (!world.ContainsColumn(x, z))
            {
                return useFallback ? fallback : default;
            }

            if (!world.TryGetCell(x, y, z, out var cell) || !cell.HasSolid)
            {
                return useFallback ? fallback : default;
            }

            var surface = cell.Surface != SurfaceType.None
                ? cell.Surface
                : SurfaceType.Ground;

            return ResolveTerrainAppearance(
                catalog,
                world.GetColumnEnvironment(x, z).Biome,
                surfaceOverride ?? surface);
        }
    }
}
