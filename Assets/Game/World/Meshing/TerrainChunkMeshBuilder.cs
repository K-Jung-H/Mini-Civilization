using System;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Meshing
{
    public static class TerrainChunkMeshBuilder
    {
        private const float Shoulder = 0.2f;
        private const float CoreMin = Shoulder;
        private const float CoreMax = 1f - Shoulder;

        public static MeshBuffers Build(
            WorldData world,
            int patchX,
            int patchZ,
            WorldSurfaceCatalog catalog)
        {
            var buffers = new MeshBuffers();
            var startX = patchX * world.ChunkSizeX;
            var startZ = patchZ * world.ChunkSizeZ;
            var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
            var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);

            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                if (!world.GetSurfaceColumn(x, z).HasSurface)
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
            var height = world.GetSurfaceColumn(x, z).SolidTopUnits;
            AddSurfaceQuad(
                buffers,
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, CoreMin, height),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, CoreMax, height),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, CoreMax, height),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, CoreMin, height));

            AddShoulder(world, catalog, buffers, x, z, startX, startZ, -1, 0);
            AddShoulder(world, catalog, buffers, x, z, startX, startZ, 1, 0);
            AddShoulder(world, catalog, buffers, x, z, startX, startZ, 0, -1);
            AddShoulder(world, catalog, buffers, x, z, startX, startZ, 0, 1);

            AddCornerJoin(world, catalog, buffers, x, z, startX, startZ, 0f, 0f);
            AddCornerJoin(world, catalog, buffers, x, z, startX, startZ, 1f, 0f);
            AddCornerJoin(world, catalog, buffers, x, z, startX, startZ, 0f, 1f);
            AddCornerJoin(world, catalog, buffers, x, z, startX, startZ, 1f, 1f);
        }

        private static void AddShoulder(
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
            var profile = SurfaceHeightResolver.ResolveEdge(
                world,
                x,
                z,
                directionX,
                directionZ,
                SurfaceLayer.Terrain);

            if (directionX < 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateVertex(world, catalog, x, z, startX, startZ, 0f, CoreMin, profile.OuterHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, 0f, CoreMax, profile.OuterHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, CoreMax, profile.CurrentHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, CoreMin, profile.CurrentHeightUnits));
                return;
            }

            if (directionX > 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, CoreMin, profile.CurrentHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, CoreMax, profile.CurrentHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, 1f, CoreMax, profile.OuterHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, 1f, CoreMin, profile.OuterHeightUnits));
                return;
            }

            if (directionZ < 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, 0f, profile.OuterHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, CoreMin, profile.CurrentHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, CoreMin, profile.CurrentHeightUnits),
                    CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, 0f, profile.OuterHeightUnits));
                return;
            }

            AddSurfaceQuad(
                buffers,
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, CoreMax, profile.CurrentHeightUnits),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, 1f, profile.OuterHeightUnits),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, 1f, profile.OuterHeightUnits),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, CoreMax, profile.CurrentHeightUnits));
        }

        private static void AddCornerJoin(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            float cornerX,
            float cornerZ)
        {
            var inwardX = cornerX <= 0f ? 1f : -1f;
            var inwardZ = cornerZ <= 0f ? 1f : -1f;
            var shoulderX = cornerX + inwardX * Shoulder;
            var shoulderZ = cornerZ + inwardZ * Shoulder;
            var directionX = -Mathf.RoundToInt(inwardX);
            var directionZ = -Mathf.RoundToInt(inwardZ);
            var edgeX = SurfaceHeightResolver.ResolveEdge(
                world, x, z, directionX, 0, SurfaceLayer.Terrain);
            var edgeZ = SurfaceHeightResolver.ResolveEdge(
                world, x, z, 0, directionZ, SurfaceLayer.Terrain);
            var cornerHeight = SurfaceHeightResolver.ResolveCornerHeight(
                world, x, z, cornerX, cornerZ, SurfaceLayer.Terrain);

            var topAlongX = CreateVertex(
                world, catalog, x, z, startX, startZ,
                shoulderX, cornerZ, edgeZ.OuterHeightUnits);
            var topAlongZ = CreateVertex(
                world, catalog, x, z, startX, startZ,
                cornerX, shoulderZ, edgeX.OuterHeightUnits);
            var inner = CreateVertex(
                world, catalog, x, z, startX, startZ,
                shoulderX, shoulderZ, edgeX.CurrentHeightUnits);
            var corner = CreateVertex(
                world, catalog, x, z, startX, startZ,
                cornerX, cornerZ, cornerHeight);

            if (cornerHeight > edgeX.CurrentHeightUnits)
            {
                // Concave 3-high / 1-low corner:
                // replace the usual corner diagonal with the explicit closure
                // triangle (corner, two shoulder endpoints). The remaining
                // triangle keeps the low cell surface complete.
                buffers.AddTriangleFacing(corner, topAlongX, topAlongZ, Vector3.up);
                buffers.AddTriangleFacing(topAlongX, inner, topAlongZ, Vector3.up);
                return;
            }

            buffers.AddTriangleFacing(inner, topAlongX, corner, Vector3.up);
            buffers.AddTriangleFacing(inner, corner, topAlongZ, Vector3.up);
        }

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
            var profile = SurfaceHeightResolver.ResolveEdge(
                world,
                x,
                z,
                directionX,
                directionZ,
                SurfaceLayer.Terrain);
            var bottomHeight = profile.NeighborExists ? profile.NeighborHeightUnits : 0;
            if (profile.CurrentHeightUnits <= bottomHeight)
            {
                return;
            }

            if (!profile.NeighborExists)
            {
                var startCorner = GetBoundaryCornerHeight(
                    world, x, z, directionX, directionZ, 0f, SurfaceLayer.Terrain);
                var endCorner = GetBoundaryCornerHeight(
                    world, x, z, directionX, directionZ, 1f, SurfaceLayer.Terrain);
                AddCliffSegment(
                    world, catalog, buffers, x, z, startX, startZ,
                    directionX, directionZ, 0f, Shoulder,
                    startCorner, profile.OuterHeightUnits,
                    0, 0);
                AddCliffSegment(
                    world, catalog, buffers, x, z, startX, startZ,
                    directionX, directionZ, Shoulder, 1f - Shoulder,
                    profile.OuterHeightUnits, profile.OuterHeightUnits,
                    0, 0);
                AddCliffSegment(
                    world, catalog, buffers, x, z, startX, startZ,
                    directionX, directionZ, 1f - Shoulder, 1f,
                    profile.OuterHeightUnits, endCorner,
                    0, 0);
                return;
            }

            var startTopCorner = GetBoundaryCornerHeight(
                world, x, z, directionX, directionZ, 0f, SurfaceLayer.Terrain);
            var endTopCorner = GetBoundaryCornerHeight(
                world, x, z, directionX, directionZ, 1f, SurfaceLayer.Terrain);
            var startBottomCorner = GetNeighborBoundaryCornerHeight(
                world, x, z, directionX, directionZ, 0f, SurfaceLayer.Terrain);
            var endBottomCorner = GetNeighborBoundaryCornerHeight(
                world, x, z, directionX, directionZ, 1f, SurfaceLayer.Terrain);

            AddCliffSegment(
                world, catalog, buffers, x, z, startX, startZ,
                directionX, directionZ, 0f, Shoulder,
                startTopCorner, profile.OuterHeightUnits,
                startBottomCorner, profile.NeighborHeightUnits);
            AddCliffSegment(
                world, catalog, buffers, x, z, startX, startZ,
                directionX, directionZ, Shoulder, 1f - Shoulder,
                profile.OuterHeightUnits, profile.OuterHeightUnits,
                profile.NeighborHeightUnits, profile.NeighborHeightUnits);
            AddCliffSegment(
                world, catalog, buffers, x, z, startX, startZ,
                directionX, directionZ, 1f - Shoulder, 1f,
                profile.OuterHeightUnits, endTopCorner,
                profile.NeighborHeightUnits, endBottomCorner);
        }

        internal static int GetBoundaryCornerHeight(
            WorldData world,
            int x,
            int z,
            int directionX,
            int directionZ,
            float t,
            SurfaceLayer layer)
        {
            GetBoundaryCoordinates(directionX, directionZ, t, out var u, out var v);
            return SurfaceHeightResolver.ResolveCornerHeight(
                world,
                x,
                z,
                u,
                v,
                layer);
        }

        internal static int GetNeighborBoundaryCornerHeight(
            WorldData world,
            int x,
            int z,
            int directionX,
            int directionZ,
            float t,
            SurfaceLayer layer)
        {
            var neighborX = x + directionX;
            var neighborZ = z + directionZ;
            GetBoundaryCoordinates(-directionX, -directionZ, t, out var u, out var v);
            return SurfaceHeightResolver.ResolveCornerHeight(
                world,
                neighborX,
                neighborZ,
                u,
                v,
                layer);
        }

        private static void AddCliffSegment(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            int directionX,
            int directionZ,
            float t0,
            float t1,
            int top0,
            int top1,
            int bottom0,
            int bottom1)
        {
            GetBoundaryCoordinates(directionX, directionZ, t0, out var u0, out var v0);
            GetBoundaryCoordinates(directionX, directionZ, t1, out var u1, out var v1);
            AddCliffStrip(
                world,
                catalog,
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
                top0,
                top1,
                bottom0,
                bottom1);
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

            var surface0 = MaterialBlendResolver.ResolveTerrain(
                world, catalog, x, z, u0, v0);
            var surface1 = MaterialBlendResolver.ResolveTerrain(
                world, catalog, x, z, u1, v1);
            var cliff0 = MaterialBlendResolver.ResolveTerrain(
                world, catalog, x, z, u0, v0, SurfaceType.Cliff);
            var cliff1 = MaterialBlendResolver.ResolveTerrain(
                world, catalog, x, z, u1, v1, SurfaceType.Cliff);
            var blendBottom0 = Math.Max(bottom0, top0 - 1);
            var blendBottom1 = Math.Max(bottom1, top1 - 1);

            AddVerticalQuad(
                buffers, x, z, startX, startZ,
                directionX, directionZ,
                u0, v0, u1, v1,
                top0, top1, blendBottom0, blendBottom1,
                surface0, surface1, cliff0, cliff1);

            if (blendBottom0 > bottom0 || blendBottom1 > bottom1)
            {
                AddVerticalQuad(
                    buffers, x, z, startX, startZ,
                    directionX, directionZ,
                    u0, v0, u1, v1,
                    blendBottom0, blendBottom1, bottom0, bottom1,
                    cliff0, cliff1, cliff0, cliff1);
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
            var topVertex0 = new SurfaceVertex(
                new Vector3(
                    x - startX + u0,
                    WorldGrid.ToWorldHeight(top0) + verticalOffset,
                    z - startZ + v0),
                new Vector2(u0 + v0, WorldGrid.ToWorldHeight(top0)),
                topAppearance0);
            var topVertex1 = new SurfaceVertex(
                new Vector3(
                    x - startX + u1,
                    WorldGrid.ToWorldHeight(top1) + verticalOffset,
                    z - startZ + v1),
                new Vector2(u1 + v1, WorldGrid.ToWorldHeight(top1)),
                topAppearance1);
            var bottomVertex0 = new SurfaceVertex(
                new Vector3(
                    x - startX + u0,
                    WorldGrid.ToWorldHeight(bottom0) + verticalOffset,
                    z - startZ + v0),
                new Vector2(u0 + v0, WorldGrid.ToWorldHeight(bottom0)),
                bottomAppearance0);
            var bottomVertex1 = new SurfaceVertex(
                new Vector3(
                    x - startX + u1,
                    WorldGrid.ToWorldHeight(bottom1) + verticalOffset,
                    z - startZ + v1),
                new Vector2(u1 + v1, WorldGrid.ToWorldHeight(bottom1)),
                bottomAppearance1);

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

        private static SurfaceVertex CreateVertex(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            int startX,
            int startZ,
            float localX,
            float localZ,
            int heightUnits)
        {
            return new SurfaceVertex(
                new Vector3(
                    x - startX + localX,
                    WorldGrid.ToWorldHeight(heightUnits),
                    z - startZ + localZ),
                new Vector2(x + localX, z + localZ),
                MaterialBlendResolver.ResolveTerrain(
                    world, catalog, x, z, localX, localZ));
        }

        private static void AddSurfaceQuad(
            MeshBuffers buffers,
            in SurfaceVertex a,
            in SurfaceVertex b,
            in SurfaceVertex c,
            in SurfaceVertex d)
        {
            buffers.AddTriangleFacing(a, b, c, Vector3.up);
            buffers.AddTriangleFacing(a, c, d, Vector3.up);
        }

        private static void GetBoundaryCoordinates(
            int directionX,
            int directionZ,
            float t,
            out float u,
            out float v)
        {
            if (directionX < 0) { u = 0f; v = t; return; }
            if (directionX > 0) { u = 1f; v = t; return; }
            if (directionZ < 0) { u = t; v = 0f; return; }
            u = t;
            v = 1f;
        }
    }
}
