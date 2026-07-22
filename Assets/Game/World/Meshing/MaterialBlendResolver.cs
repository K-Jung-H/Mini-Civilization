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
            var currentId = world.GetSurfaceColumn(x, z).SurfaceMaterialId;
            ResolveAxis(localX, out var offsetX, out var blendX);
            ResolveAxis(localZ, out var offsetZ, out var blendZ);

            var current = ResolveTerrainAppearance(catalog, currentId);
            var xAppearance = ResolveTerrainNeighbor(world, catalog, x + offsetX, z, current);
            var zAppearance = ResolveTerrainNeighbor(world, catalog, x, z + offsetZ, current);
            var diagonalAppearance = ResolveTerrainNeighbor(world, catalog, x + offsetX, z + offsetZ, current);

            var row0 = SurfaceAppearance.Lerp(current, xAppearance, blendX);
            var row1 = SurfaceAppearance.Lerp(zAppearance, diagonalAppearance, blendX);
            return SurfaceAppearance.Lerp(row0, row1, blendZ);
        }

        public static SurfaceAppearance ResolveWater(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            float localX,
            float localZ)
        {
            var currentId = world.GetSurfaceColumn(x, z).WaterMaterialId;
            ResolveAxis(localX, out var offsetX, out var blendX);
            ResolveAxis(localZ, out var offsetZ, out var blendZ);

            var current = ResolveWaterAppearance(catalog, currentId);
            var xAppearance = ResolveWaterNeighbor(world, catalog, x + offsetX, z, current);
            var zAppearance = ResolveWaterNeighbor(world, catalog, x, z + offsetZ, current);
            var diagonalAppearance = ResolveWaterNeighbor(world, catalog, x + offsetX, z + offsetZ, current);

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
            in SurfaceAppearance fallback)
        {
            if (!world.ContainsColumn(x, z))
            {
                return fallback;
            }

            var column = world.GetSurfaceColumn(x, z);
            return column.HasSurface ? ResolveTerrainAppearance(catalog, column.SurfaceMaterialId) : fallback;
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
            return column.HasWater ? ResolveWaterAppearance(catalog, column.WaterMaterialId) : fallback;
        }

        internal static SurfaceAppearance ResolveTerrainAppearance(WorldSurfaceCatalog catalog, ushort id)
            => catalog != null ? catalog.ResolveTerrain(id) : DefaultSurfacePalette.ResolveTerrain(id);

        internal static SurfaceAppearance ResolveWaterAppearance(WorldSurfaceCatalog catalog, ushort id)
            => catalog != null ? catalog.ResolveWater(id) : DefaultSurfacePalette.ResolveWater(id);
    }
}
