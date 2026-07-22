using System;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Meshing
{
    public static class TerrainChunkMeshBuilder
    {
        private static readonly float[] Knots = { 0f, 0.2f, 0.8f, 1f };

        public static MeshBuffers Build(WorldData world, int patchX, int patchZ, WorldSurfaceCatalog catalog)
        {
            var buffers = new MeshBuffers();
            var startX = patchX * world.ChunkSizeX;
            var startZ = patchZ * world.ChunkSizeZ;
            var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
            var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);

            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                var column = world.GetSurfaceColumn(x, z);
                if (!column.HasSurface)
                {
                    continue;
                }

                AddTop(world, catalog, buffers, x, z, startX, startZ);
                AddCliffs(world, catalog, buffers, x, z, startX, startZ);
            }

            return buffers;
        }

        private static void AddTop(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ)
        {
            for (var segmentZ = 0; segmentZ < 3; segmentZ++)
            for (var segmentX = 0; segmentX < 3; segmentX++)
            {
                var u0 = Knots[segmentX];
                var u1 = Knots[segmentX + 1];
                var v0 = Knots[segmentZ];
                var v1 = Knots[segmentZ + 1];
                var a = CreateTopVertex(world, catalog, x, z, startX, startZ, u0, v0);
                var b = CreateTopVertex(world, catalog, x, z, startX, startZ, u0, v1);
                var c = CreateTopVertex(world, catalog, x, z, startX, startZ, u1, v1);
                var d = CreateTopVertex(world, catalog, x, z, startX, startZ, u1, v0);
                buffers.AddTriangle(a, b, c);
                buffers.AddTriangle(a, c, d);
            }
        }

        private static SurfaceVertex CreateTopVertex(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            int startX,
            int startZ,
            float localX,
            float localZ)
        {
            var heightUnits = GetTopVertexHeightUnits(world, x, z, localX, localZ);
            var appearance = MaterialBlendResolver.ResolveTerrain(world, catalog, x, z, localX, localZ);
            return new SurfaceVertex(
                new Vector3(x - startX + localX, WorldGrid.ToWorldHeight(heightUnits), z - startZ + localZ),
                new Vector2(x + localX, z + localZ),
                appearance);
        }

        private static int GetTopVertexHeightUnits(WorldData world, int x, int z, float localX, float localZ)
            => SurfaceHeightResolver.ResolveVertex(world, x, z, localX, localZ, SurfaceLayer.Terrain);

        private static void AddCliffs(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ)
        {
            AddCliffSide(world, catalog, buffers, x, z, startX, startZ, -1, 0);
            AddCliffSide(world, catalog, buffers, x, z, startX, startZ, 1, 0);
            AddCliffSide(world, catalog, buffers, x, z, startX, startZ, 0, -1);
            AddCliffSide(world, catalog, buffers, x, z, startX, startZ, 0, 1);
        }

        private static void AddCliffSide(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            int directionX,
            int directionZ)
        {
            var currentHeight = world.GetSurfaceColumn(x, z).SolidTopUnits;
            var neighborX = x + directionX;
            var neighborZ = z + directionZ;
            var neighborExists = world.ContainsColumn(neighborX, neighborZ) && world.GetSurfaceColumn(neighborX, neighborZ).HasSurface;
            var neighborHeight = neighborExists ? world.GetSurfaceColumn(neighborX, neighborZ).SolidTopUnits : 0;
            if (currentHeight <= neighborHeight)
            {
                return;
            }

            for (var segment = 0; segment < 3; segment++)
            {
                var t0 = Knots[segment];
                var t1 = Knots[segment + 1];
                GetBoundaryCoordinates(directionX, directionZ, t0, out var u0, out var v0);
                GetBoundaryCoordinates(directionX, directionZ, t1, out var u1, out var v1);
                var top0 = GetTopVertexHeightUnits(world, x, z, u0, v0);
                var top1 = GetTopVertexHeightUnits(world, x, z, u1, v1);
                var bottom0 = neighborExists ? GetNeighborBoundaryHeight(world, neighborX, neighborZ, -directionX, -directionZ, t0) : 0;
                var bottom1 = neighborExists ? GetNeighborBoundaryHeight(world, neighborX, neighborZ, -directionX, -directionZ, t1) : 0;
                AddCliffStrip(world, catalog, buffers, x, z, startX, startZ, directionX, directionZ, u0, v0, u1, v1, top0, top1, bottom0, bottom1);
            }
        }

        private static int GetNeighborBoundaryHeight(WorldData world, int x, int z, int directionX, int directionZ, float t)
        {
            GetBoundaryCoordinates(directionX, directionZ, t, out var u, out var v);
            return GetTopVertexHeightUnits(world, x, z, u, v);
        }

        private static void AddCliffStrip(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            int directionX,
            int directionZ,
            float u0,
            float v0,
            float u1,
            float v1,
            int top0,
            int top1,
            int bottom0,
            int bottom1)
        {
            if (top0 <= bottom0 && top1 <= bottom1)
            {
                return;
            }

            var surface0 = MaterialBlendResolver.ResolveTerrain(world, catalog, x, z, u0, v0);
            var surface1 = MaterialBlendResolver.ResolveTerrain(world, catalog, x, z, u1, v1);
            var rock = MaterialBlendResolver.ResolveTerrainAppearance(catalog, WorldMaterialIds.Rock);
            var blendBottom0 = Math.Max(bottom0, top0 - 1);
            var blendBottom1 = Math.Max(bottom1, top1 - 1);

            AddVerticalQuad(buffers, x, z, startX, startZ, directionX, directionZ, u0, v0, u1, v1, top0, top1, blendBottom0, blendBottom1, surface0, surface1, rock, rock);
            if (blendBottom0 > bottom0 || blendBottom1 > bottom1)
            {
                AddVerticalQuad(buffers, x, z, startX, startZ, directionX, directionZ, u0, v0, u1, v1, blendBottom0, blendBottom1, bottom0, bottom1, rock, rock, rock, rock);
            }
        }

        internal static void AddVerticalQuad(
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            int directionX,
            int directionZ,
            float u0,
            float v0,
            float u1,
            float v1,
            int top0,
            int top1,
            int bottom0,
            int bottom1,
            in SurfaceAppearance topAppearance0,
            in SurfaceAppearance topAppearance1,
            in SurfaceAppearance bottomAppearance0,
            in SurfaceAppearance bottomAppearance1,
            float verticalOffset = 0f)
        {
            var topVertex0 = new SurfaceVertex(new Vector3(x - startX + u0, WorldGrid.ToWorldHeight(top0) + verticalOffset, z - startZ + v0), new Vector2(u0 + v0, WorldGrid.ToWorldHeight(top0)), topAppearance0);
            var topVertex1 = new SurfaceVertex(new Vector3(x - startX + u1, WorldGrid.ToWorldHeight(top1) + verticalOffset, z - startZ + v1), new Vector2(u1 + v1, WorldGrid.ToWorldHeight(top1)), topAppearance1);
            var bottomVertex0 = new SurfaceVertex(new Vector3(x - startX + u0, WorldGrid.ToWorldHeight(bottom0) + verticalOffset, z - startZ + v0), new Vector2(u0 + v0, WorldGrid.ToWorldHeight(bottom0)), bottomAppearance0);
            var bottomVertex1 = new SurfaceVertex(new Vector3(x - startX + u1, WorldGrid.ToWorldHeight(bottom1) + verticalOffset, z - startZ + v1), new Vector2(u1 + v1, WorldGrid.ToWorldHeight(bottom1)), bottomAppearance1);

            if (directionX > 0 || directionZ < 0)
            {
                buffers.AddTriangle(bottomVertex0, topVertex0, topVertex1);
                buffers.AddTriangle(bottomVertex0, topVertex1, bottomVertex1);
            }
            else
            {
                buffers.AddTriangle(bottomVertex0, topVertex1, topVertex0);
                buffers.AddTriangle(bottomVertex0, bottomVertex1, topVertex1);
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
