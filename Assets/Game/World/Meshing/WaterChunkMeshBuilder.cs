using System;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Meshing
{
    public sealed class WaterChunkMeshBuffers
    {
        public MeshBuffers Surface { get; } = new();
        public MeshBuffers Waterfalls { get; } = new();
    }

    public static class WaterChunkMeshBuilder
    {
        private static readonly float[] Knots = { 0f, 0.2f, 0.8f, 1f };
        private const float SurfaceOffset = 0.002f;

        public static WaterChunkMeshBuffers Build(WorldData world, int patchX, int patchZ, WorldSurfaceCatalog catalog)
        {
            var buffers = new WaterChunkMeshBuffers();
            var startX = patchX * world.ChunkSizeX;
            var startZ = patchZ * world.ChunkSizeZ;
            var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
            var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);

            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                if (!world.GetSurfaceColumn(x, z).HasWater)
                {
                    continue;
                }

                AddSurface(world, catalog, buffers.Surface, x, z, startX, startZ);
                AddWaterfalls(world, catalog, buffers.Waterfalls, x, z, startX, startZ);
            }

            return buffers;
        }

        private static void AddSurface(WorldData world, WorldSurfaceCatalog catalog, MeshBuffers buffers, int x, int z, int startX, int startZ)
        {
            for (var segmentZ = 0; segmentZ < 3; segmentZ++)
            for (var segmentX = 0; segmentX < 3; segmentX++)
            {
                var u0 = Knots[segmentX];
                var u1 = Knots[segmentX + 1];
                var v0 = Knots[segmentZ];
                var v1 = Knots[segmentZ + 1];
                var a = CreateWaterVertex(world, catalog, x, z, startX, startZ, u0, v0);
                var b = CreateWaterVertex(world, catalog, x, z, startX, startZ, u0, v1);
                var c = CreateWaterVertex(world, catalog, x, z, startX, startZ, u1, v1);
                var d = CreateWaterVertex(world, catalog, x, z, startX, startZ, u1, v0);
                buffers.AddTriangle(a, b, c);
                buffers.AddTriangle(a, c, d);
            }
        }

        private static SurfaceVertex CreateWaterVertex(WorldData world, WorldSurfaceCatalog catalog, int x, int z, int startX, int startZ, float localX, float localZ)
        {
            var height = GetWaterVertexHeightUnits(world, x, z, localX, localZ);
            var appearance = MaterialBlendResolver.ResolveWater(world, catalog, x, z, localX, localZ);
            return new SurfaceVertex(
                new Vector3(x - startX + localX, WorldGrid.ToWorldHeight(height) + SurfaceOffset, z - startZ + localZ),
                new Vector2(x + localX, z + localZ),
                appearance);
        }

        private static int GetWaterVertexHeightUnits(WorldData world, int x, int z, float localX, float localZ)
            => SurfaceHeightResolver.ResolveVertex(world, x, z, localX, localZ, SurfaceLayer.Water);

        private static void AddWaterfalls(WorldData world, WorldSurfaceCatalog catalog, MeshBuffers buffers, int x, int z, int startX, int startZ)
        {
            AddWaterfallSide(world, catalog, buffers, x, z, startX, startZ, -1, 0);
            AddWaterfallSide(world, catalog, buffers, x, z, startX, startZ, 1, 0);
            AddWaterfallSide(world, catalog, buffers, x, z, startX, startZ, 0, -1);
            AddWaterfallSide(world, catalog, buffers, x, z, startX, startZ, 0, 1);
        }

        private static void AddWaterfallSide(WorldData world, WorldSurfaceCatalog catalog, MeshBuffers buffers, int x, int z, int startX, int startZ, int directionX, int directionZ)
        {
            var current = world.GetSurfaceColumn(x, z);
            var neighborX = x + directionX;
            var neighborZ = z + directionZ;
            if (!world.ContainsColumn(neighborX, neighborZ))
            {
                return;
            }

            var neighbor = world.GetSurfaceColumn(neighborX, neighborZ);
            if (!neighbor.HasWater || current.WaterTopUnits - neighbor.WaterTopUnits < 2)
            {
                return;
            }

            var topUnits = current.WaterTopUnits - 1;
            var bottomUnits = neighbor.WaterTopUnits;
            var topAppearance = MaterialBlendResolver.ResolveWaterAppearance(catalog, current.WaterMaterialId);
            var bottomAppearance = MaterialBlendResolver.ResolveWaterAppearance(catalog, neighbor.WaterMaterialId);

            for (var segment = 0; segment < 3; segment++)
            {
                var t0 = Knots[segment];
                var t1 = Knots[segment + 1];
                GetBoundaryCoordinates(directionX, directionZ, t0, out var u0, out var v0);
                GetBoundaryCoordinates(directionX, directionZ, t1, out var u1, out var v1);
                TerrainChunkMeshBuilder.AddVerticalQuad(
                    buffers,
                    x,
                    z,
                    startX,
                    startZ,
                    directionX,
                    directionZ,
                    u0,
                    v0,
                    u1,
                    v1,
                    topUnits,
                    topUnits,
                    bottomUnits,
                    bottomUnits,
                    topAppearance,
                    topAppearance,
                    bottomAppearance,
                    bottomAppearance,
                    SurfaceOffset);
            }
        }

        private static void GetBoundaryCoordinates(int directionX, int directionZ, float t, out float u, out float v)
        {
            if (directionX < 0) { u = 0f; v = t; return; }
            if (directionX > 0) { u = 1f; v = t; return; }
            if (directionZ < 0) { u = t; v = 0f; return; }
            u = t;
            v = 1f;
        }
    }
}
