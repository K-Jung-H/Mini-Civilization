using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Meshing
{
    // Owned by mesh scratch, reset per cell; never retains world data across builds.
    internal sealed class TerrainCellMaterials
    {
        private readonly SurfaceAppearance[] values = new SurfaceAppearance[32];
        private uint resolved;
        private WorldData world;
        private WorldSurfaceCatalog catalog;
        private int x, y, z;
        internal void Release() { world = null; catalog = null; resolved = 0; }

        internal void BeginCell(WorldData world, WorldSurfaceCatalog catalog, int x, int y, int z)
        {
            this.world = world;
            this.catalog = catalog;
            this.x = x; this.y = y; this.z = z;
            resolved = 0;
        }

        internal SurfaceAppearance Resolve(float localX, float localZ, SurfaceType? surface)
        {
            var ix = Index(localX);
            var iz = Index(localZ);
            if (ix < 0 || iz < 0 || (surface.HasValue && surface != SurfaceType.Cliff))
                return MaterialBlendResolver.ResolveTerrainCell(world, catalog, x, y, z, localX, localZ, surface);
            var index = ix + iz * 4 + (surface.HasValue ? 16 : 0);
            var bit = 1u << index;
            if ((resolved & bit) == 0)
            {
                values[index] = MaterialBlendResolver.ResolveTerrainCell(world, catalog, x, y, z, localX, localZ, surface);
                resolved |= bit;
            }
            return values[index];
        }

        internal bool HasUniformTop()
        {
            var current = world.GetCell(x, y, z);
            var surface = current.Terrain.Surface;
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (!world.TryGetCell(x + dx, y, z + dz, out var other) || !other.HasTerrain) continue;
                if (other.Biome.Terrain != current.Biome.Terrain || other.Terrain.Surface != surface) return false;
            }
            return true;
        }

        private static int Index(float value) => value == 0f ? 0 : value == 0.2f ? 1
            : value == 0.8f ? 2 : value == 1f ? 3 : -1;
    }

    public static class MaterialBlendResolver
    {
        [System.ThreadStatic] internal static SurfaceAppearance[] WorkerPalette;
        internal static SurfaceAppearance[] CapturePalette(WorldSurfaceCatalog catalog)
        {
            var result = new SurfaceAppearance[65];
            for (var biome = 0; biome < 8; biome++)
            for (var surface = 0; surface < 8; surface++)
                result[biome * 8 + surface] = ResolveTerrainAppearance(catalog, (TerrainBiome)biome, (SurfaceType)surface);
            result[64] = ResolveWaterAppearance(catalog);
            return result;
        }
        private const float BlendBand = 0.2f;
        private const float TotalBlendWidth = BlendBand * 2f;

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
            if (offsetX == 0 && offsetZ == 0) return current;
            var xAppearance = offsetX == 0 ? current : ResolveTerrainCellAppearance(
                world,
                catalog,
                x + offsetX,
                y,
                z,
                surfaceOverride,
                current,
                true);
            if (offsetZ == 0) return SurfaceAppearance.Lerp(current, xAppearance, blendX);
            var zAppearance = ResolveTerrainCellAppearance(
                world,
                catalog,
                x,
                y,
                z + offsetZ,
                surfaceOverride,
                current,
                true);
            if (offsetX == 0) return SurfaceAppearance.Lerp(current, zAppearance, blendZ);
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
            TerrainBiome biome,
            SurfaceType surface)
        {
            if (WorkerPalette != null) return WorkerPalette[(int)biome * 8 + (int)surface];
            return catalog != null
                ? catalog.ResolveTerrain(biome, surface)
                : DefaultSurfacePalette.ResolveTerrain(biome, surface);
        }

        internal static SurfaceAppearance ResolveWaterAppearance(
            WorldSurfaceCatalog catalog)
        {
            if (WorkerPalette != null) return WorkerPalette[64];
            return catalog != null
                ? catalog.ResolveWater()
                : DefaultSurfacePalette.ResolveWater();
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

            if (!world.TryGetCell(x, y, z, out var cell) || !cell.HasTerrain)
            {
                return useFallback ? fallback : default;
            }

            var surface = cell.Terrain.Surface != SurfaceType.None
                ? cell.Terrain.Surface
                : SurfaceType.Ground;

            return ResolveTerrainAppearance(
                catalog,
                cell.Biome.Terrain,
                surfaceOverride ?? surface);
        }

    }
}
