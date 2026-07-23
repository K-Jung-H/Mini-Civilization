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
        private const float Shoulder = 0.2f;
        private const float CoreMin = Shoulder;
        private const float CoreMax = 1f - Shoulder;
        private const float SurfaceOffset = -0.01f;

        public static WaterChunkMeshBuffers Build(
            WorldData world,
            int patchX,
            int patchZ,
            WorldSurfaceCatalog catalog)
        {
            var buffers = new WaterChunkMeshBuffers();
            var startX = patchX * world.ChunkSizeX;
            var startZ = patchZ * world.ChunkSizeZ;
            var endX = Math.Min(startX + world.ChunkSizeX, world.Size);
            var endZ = Math.Min(startZ + world.ChunkSizeZ, world.Size);

            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                var column = world.GetSurfaceColumn(x, z);
                if (column.HasWater)
                {
                    AddTop(world, catalog, buffers, x, z, startX, startZ);
                    AddWaterfalls(world, catalog, buffers.Waterfalls, x, z, startX, startZ);
                }
                else if (column.HasSurface)
                {
                    AddRenderApron(world, catalog, buffers, x, z, startX, startZ);
                }
            }

            return buffers;
        }

        private static void AddTop(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterChunkMeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ)
        {
            var height = world.GetSurfaceColumn(x, z).WaterTopUnits;
            AddSurfaceQuad(
                buffers.Surface,
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, CoreMin, height),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMin, CoreMax, height),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, CoreMax, height),
                CreateVertex(world, catalog, x, z, startX, startZ, CoreMax, CoreMin, height));

            AddShoulder(world, catalog, buffers.Surface, x, z, startX, startZ, -1, 0);
            AddShoulder(world, catalog, buffers.Surface, x, z, startX, startZ, 1, 0);
            AddShoulder(world, catalog, buffers.Surface, x, z, startX, startZ, 0, -1);
            AddShoulder(world, catalog, buffers.Surface, x, z, startX, startZ, 0, 1);

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
                SurfaceLayer.Water);

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
            WaterChunkMeshBuffers buffers,
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
                world, x, z, directionX, 0, SurfaceLayer.Water);
            var edgeZ = SurfaceHeightResolver.ResolveEdge(
                world, x, z, 0, directionZ, SurfaceLayer.Water);
            var cornerHeight = ResolveRenderCornerHeight(
                world, x, z, cornerX, cornerZ);

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

            if (TryAddApronNotchCap(
                    world,
                    catalog,
                    buffers.Surface,
                    x,
                    z,
                    startX,
                    startZ,
                    cornerX,
                    cornerZ,
                    shoulderX,
                    shoulderZ,
                    directionX,
                    directionZ,
                    edgeX.CurrentHeightUnits))
            {
                return;
            }

            // Water corners always use the actual tile's C/X/Z/I vertices.
            // Dry cardinal quadrants are covered by the source cell's render
            // apron, so they do not invalidate a connected one-tier rise.
            buffers.Surface.AddTriangleFacing(
                corner, topAlongX, topAlongZ, Vector3.up);
            buffers.Surface.AddTriangleFacing(
                topAlongX, inner, topAlongZ, Vector3.up);

            AddCornerSeam(
                world,
                catalog,
                buffers.Surface,
                x,
                z,
                startX,
                startZ,
                cornerX,
                cornerZ,
                cornerHeight,
                edgeX,
                topAlongZ,
                directionX,
                0);
            AddCornerSeam(
                world,
                catalog,
                buffers.Surface,
                x,
                z,
                startX,
                startZ,
                cornerX,
                cornerZ,
                cornerHeight,
                edgeZ,
                topAlongX,
                0,
                directionZ);
        }

        private static bool TryAddApronNotchCap(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            float cornerX,
            float cornerZ,
            float shoulderX,
            float shoulderZ,
            int directionX,
            int directionZ,
            int heightUnits)
        {
            var xApronX = x + directionX;
            var xApronZ = z;
            var zApronX = x;
            var zApronZ = z + directionZ;
            var diagonalX = x + directionX;
            var diagonalZ = z + directionZ;
            if (!IsApronTarget(world, xApronX, xApronZ)
                || !IsApronTarget(world, zApronX, zApronZ)
                || !HasWater(world, diagonalX, diagonalZ)
                || world.GetSurfaceColumn(diagonalX, diagonalZ).WaterTopUnits
                    != heightUnits)
            {
                return false;
            }

            var waterType = world.GetSurfaceColumn(x, z).Water;
            var xApronEnd = CreateApronVertex(
                catalog,
                x,
                z,
                startX,
                startZ,
                cornerX + directionX * Shoulder,
                shoulderZ,
                heightUnits,
                waterType);
            var recessedShoulder = CreateApronVertex(
                catalog,
                x,
                z,
                startX,
                startZ,
                shoulderX,
                shoulderZ,
                heightUnits,
                waterType);
            var zApronEnd = CreateApronVertex(
                catalog,
                x,
                z,
                startX,
                startZ,
                shoulderX,
                cornerZ + directionZ * Shoulder,
                heightUnits,
                waterType);

            // One owning water tile fills one V-shaped notch. The opposite
            // diagonal water tile produces the second triangle with the same
            // outer base and its own recessed Shoulder vertex.
            buffers.AddTriangleFacing(
                xApronEnd,
                recessedShoulder,
                zApronEnd,
                Vector3.up);
            return true;
        }

        private static void AddCornerSeam(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            float cornerX,
            float cornerZ,
            int cornerHeight,
            in SurfaceEdgeProfile edge,
            in SurfaceVertex boundaryShoulder,
            int directionX,
            int directionZ)
        {
            // Existing water neighbors own their shared boundary. This seam
            // only closes an exposed side of the current actual water tile.
            if (edge.NeighborExists
                || cornerHeight == edge.OuterHeightUnits)
            {
                return;
            }

            var baseCorner = CreateVertex(
                world,
                catalog,
                x,
                z,
                startX,
                startZ,
                cornerX,
                cornerZ,
                edge.OuterHeightUnits);
            var raisedCorner = CreateVertex(
                world,
                catalog,
                x,
                z,
                startX,
                startZ,
                cornerX,
                cornerZ,
                cornerHeight);
            var outward = new Vector3(directionX, 0f, directionZ);

            // All three vertices remain on the current cell's 0..1 boundary;
            // no dry-cell apron vertex participates in this polygon.
            buffers.AddTriangleFacing(
                raisedCorner,
                baseCorner,
                boundaryShoulder,
                outward);
        }

        private static void AddWaterfalls(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ)
        {
            AddWaterfallSide(world, catalog, buffers, x, z, startX, startZ, -1, 0);
            AddWaterfallSide(world, catalog, buffers, x, z, startX, startZ, 1, 0);
            AddWaterfallSide(world, catalog, buffers, x, z, startX, startZ, 0, -1);
            AddWaterfallSide(world, catalog, buffers, x, z, startX, startZ, 0, 1);
        }

        private static void AddWaterfallSide(
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
                SurfaceLayer.Water);
            if (!profile.NeighborExists
                || profile.CurrentHeightUnits - profile.NeighborHeightUnits < 2)
            {
                return;
            }

            var startTopCorner = TerrainChunkMeshBuilder.GetBoundaryCornerHeight(
                world, x, z, directionX, directionZ, 0f, SurfaceLayer.Water);
            var endTopCorner = TerrainChunkMeshBuilder.GetBoundaryCornerHeight(
                world, x, z, directionX, directionZ, 1f, SurfaceLayer.Water);
            var startBottomCorner = TerrainChunkMeshBuilder.GetNeighborBoundaryCornerHeight(
                world, x, z, directionX, directionZ, 0f, SurfaceLayer.Water);
            var endBottomCorner = TerrainChunkMeshBuilder.GetNeighborBoundaryCornerHeight(
                world, x, z, directionX, directionZ, 1f, SurfaceLayer.Water);

            AddWaterfallSegment(
                world, catalog, buffers, x, z, startX, startZ,
                directionX, directionZ, 0f, Shoulder,
                startTopCorner, profile.OuterHeightUnits,
                startBottomCorner, profile.NeighborHeightUnits);
            AddWaterfallSegment(
                world, catalog, buffers, x, z, startX, startZ,
                directionX, directionZ, Shoulder, 1f - Shoulder,
                profile.OuterHeightUnits, profile.OuterHeightUnits,
                profile.NeighborHeightUnits, profile.NeighborHeightUnits);
            AddWaterfallSegment(
                world, catalog, buffers, x, z, startX, startZ,
                directionX, directionZ, 1f - Shoulder, 1f,
                profile.OuterHeightUnits, endTopCorner,
                profile.NeighborHeightUnits, endBottomCorner);
        }

        private static void AddWaterfallSegment(
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
            if (top0 <= bottom0 && top1 <= bottom1)
            {
                return;
            }

            GetBoundaryCoordinates(directionX, directionZ, t0, out var u0, out var v0);
            GetBoundaryCoordinates(directionX, directionZ, t1, out var u1, out var v1);
            var topAppearance0 = MaterialBlendResolver.ResolveWater(
                world, catalog, x, z, u0, v0);
            var topAppearance1 = MaterialBlendResolver.ResolveWater(
                world, catalog, x, z, u1, v1);
            var neighbor = world.GetSurfaceColumn(x + directionX, z + directionZ);
            var bottomAppearance = MaterialBlendResolver.ResolveWaterAppearance(
                catalog,
                neighbor.Water);

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
                top0,
                top1,
                bottom0,
                bottom1,
                topAppearance0,
                topAppearance1,
                bottomAppearance,
                bottomAppearance,
                SurfaceOffset);
        }

        private static void AddRenderApron(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterChunkMeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ)
        {
            AddApronSide(world, catalog, buffers.Surface, x, z, startX, startZ, -1, 0);
            AddApronSide(world, catalog, buffers.Surface, x, z, startX, startZ, 1, 0);
            AddApronSide(world, catalog, buffers.Surface, x, z, startX, startZ, 0, -1);
            AddApronSide(world, catalog, buffers.Surface, x, z, startX, startZ, 0, 1);

            AddApronCorner(world, catalog, buffers, x, z, startX, startZ, 0f, 0f);
            AddApronCorner(world, catalog, buffers, x, z, startX, startZ, 1f, 0f);
            AddApronCorner(world, catalog, buffers, x, z, startX, startZ, 0f, 1f);
            AddApronCorner(world, catalog, buffers, x, z, startX, startZ, 1f, 1f);
        }

        private static void AddApronSide(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            int neighborDirectionX,
            int neighborDirectionZ)
        {
            var sourceX = x + neighborDirectionX;
            var sourceZ = z + neighborDirectionZ;
            if (!HasWater(world, sourceX, sourceZ))
            {
                return;
            }

            var height = ResolveSourceEdgeHeight(
                world,
                sourceX,
                sourceZ,
                -neighborDirectionX,
                -neighborDirectionZ);
            var waterType = world.GetSurfaceColumn(sourceX, sourceZ).Water;

            if (neighborDirectionX < 0)
            {
                AddApronQuad(
                    catalog, buffers, x, z, startX, startZ, waterType,
                    0f, CoreMin, height,
                    0f, CoreMax, height,
                    CoreMin, CoreMax, height,
                    CoreMin, CoreMin, height);
                return;
            }

            if (neighborDirectionX > 0)
            {
                AddApronQuad(
                    catalog, buffers, x, z, startX, startZ, waterType,
                    CoreMax, CoreMin, height,
                    CoreMax, CoreMax, height,
                    1f, CoreMax, height,
                    1f, CoreMin, height);
                return;
            }

            if (neighborDirectionZ < 0)
            {
                AddApronQuad(
                    catalog, buffers, x, z, startX, startZ, waterType,
                    CoreMin, 0f, height,
                    CoreMin, CoreMin, height,
                    CoreMax, CoreMin, height,
                    CoreMax, 0f, height);
                return;
            }

            AddApronQuad(
                catalog, buffers, x, z, startX, startZ, waterType,
                CoreMin, CoreMax, height,
                CoreMin, 1f, height,
                CoreMax, 1f, height,
                CoreMax, CoreMax, height);
        }

        private static void AddApronCorner(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterChunkMeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            float cornerX,
            float cornerZ)
        {
            var directionX = cornerX <= 0f ? -1 : 1;
            var directionZ = cornerZ <= 0f ? -1 : 1;
            var sourceXX = x + directionX;
            var sourceXZ = z;
            var sourceZX = x;
            var sourceZZ = z + directionZ;
            var hasX = HasWater(world, sourceXX, sourceXZ);
            var hasZ = HasWater(world, sourceZX, sourceZZ);
            var diagonalX = x + directionX;
            var diagonalZ = z + directionZ;
            var hasDiagonal = HasWater(world, diagonalX, diagonalZ);
            var inwardX = -directionX;
            var inwardZ = -directionZ;
            var shoulderX = cornerX + inwardX * Shoulder;
            var shoulderZ = cornerZ + inwardZ * Shoulder;

            if (!hasX && !hasZ)
            {
                return;
            }

            if (hasX && hasZ)
            {
                if (!hasDiagonal)
                {
                    // The two diagonal water tiles each own their V-shaped
                    // notch cap; the dry cell must not connect their aprons.
                    return;
                }

                AddSplitApronCorner(
                    world, catalog, buffers.Surface, x, z, startX, startZ,
                    cornerX, cornerZ, shoulderX, shoulderZ,
                    sourceXX, sourceXZ, sourceZX, sourceZZ);
                return;
            }

            var sourceX = hasX ? sourceXX : sourceZX;
            var sourceZ = hasX ? sourceXZ : sourceZZ;
            var edgeHeight = ResolveSourceEdgeHeight(
                world,
                sourceX,
                sourceZ,
                hasX ? -directionX : 0,
                hasZ ? -directionZ : 0);
            // The apron only extends its owning edge. It must not inherit the
            // actual water tile's raised corner or connect across a height step.
            var cornerHeight = edgeHeight;
            var waterType = world.GetSurfaceColumn(sourceX, sourceZ).Water;

            if (hasX)
            {
                AddApronQuad(
                    catalog, buffers.Surface, x, z, startX, startZ, waterType,
                    cornerX, cornerZ, cornerHeight,
                    cornerX, shoulderZ, edgeHeight,
                    shoulderX, shoulderZ, edgeHeight,
                    shoulderX, cornerZ, cornerHeight);
                return;
            }

            AddApronQuad(
                catalog, buffers.Surface, x, z, startX, startZ, waterType,
                cornerX, cornerZ, cornerHeight,
                cornerX, shoulderZ, cornerHeight,
                shoulderX, shoulderZ, edgeHeight,
                shoulderX, cornerZ, edgeHeight);
        }

        private static void AddSplitApronCorner(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            float cornerX,
            float cornerZ,
            float shoulderX,
            float shoulderZ,
            int sourceXX,
            int sourceXZ,
            int sourceZX,
            int sourceZZ)
        {
            var xEdgeHeight = ResolveSourceEdgeHeight(
                world, sourceXX, sourceXZ, x - sourceXX, z - sourceXZ);
            var zEdgeHeight = ResolveSourceEdgeHeight(
                world, sourceZX, sourceZZ, x - sourceZX, z - sourceZZ);
            // Keep each scaled apron on its own source edge plane. Corner-rise
            // polygons belong exclusively to actual water tiles.
            var xCornerHeight = xEdgeHeight;
            var zCornerHeight = zEdgeHeight;
            var xWater = world.GetSurfaceColumn(sourceXX, sourceXZ).Water;
            var zWater = world.GetSurfaceColumn(sourceZX, sourceZZ).Water;

            var xCorner = CreateApronVertex(
                catalog, x, z, startX, startZ,
                cornerX, cornerZ, xCornerHeight, xWater);
            var xInner = CreateApronVertex(
                catalog, x, z, startX, startZ,
                shoulderX, shoulderZ, xEdgeHeight, xWater);
            var xAlongZ = CreateApronVertex(
                catalog, x, z, startX, startZ,
                cornerX, shoulderZ, xEdgeHeight, xWater);
            buffers.AddTriangleFacing(xCorner, xInner, xAlongZ, Vector3.up);

            var zCorner = CreateApronVertex(
                catalog, x, z, startX, startZ,
                cornerX, cornerZ, zCornerHeight, zWater);
            var zAlongX = CreateApronVertex(
                catalog, x, z, startX, startZ,
                shoulderX, cornerZ, zEdgeHeight, zWater);
            var zInner = CreateApronVertex(
                catalog, x, z, startX, startZ,
                shoulderX, shoulderZ, zEdgeHeight, zWater);
            buffers.AddTriangleFacing(zCorner, zAlongX, zInner, Vector3.up);
        }

        private static void AddApronQuad(
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int z,
            int startX,
            int startZ,
            WaterType waterType,
            float ax,
            float az,
            int ah,
            float bx,
            float bz,
            int bh,
            float cx,
            float cz,
            int ch,
            float dx,
            float dz,
            int dh)
        {
            AddSurfaceQuad(
                buffers,
                CreateApronVertex(catalog, x, z, startX, startZ, ax, az, ah, waterType),
                CreateApronVertex(catalog, x, z, startX, startZ, bx, bz, bh, waterType),
                CreateApronVertex(catalog, x, z, startX, startZ, cx, cz, ch, waterType),
                CreateApronVertex(catalog, x, z, startX, startZ, dx, dz, dh, waterType));
        }

        private static SurfaceVertex CreateApronVertex(
            WorldSurfaceCatalog catalog,
            int x,
            int z,
            int startX,
            int startZ,
            float localX,
            float localZ,
            int heightUnits,
            WaterType waterType)
        {
            return new SurfaceVertex(
                new Vector3(
                    x - startX + localX,
                    WorldGrid.ToWorldHeight(heightUnits) + SurfaceOffset,
                    z - startZ + localZ),
                new Vector2(x + localX, z + localZ),
                MaterialBlendResolver.ResolveWaterAppearance(catalog, waterType));
        }

        private static int ResolveSourceEdgeHeight(
            WorldData world,
            int sourceX,
            int sourceZ,
            int directionX,
            int directionZ)
            => SurfaceHeightResolver.ResolveEdge(
                world,
                sourceX,
                sourceZ,
                directionX,
                directionZ,
                SurfaceLayer.Water).OuterHeightUnits;

        private static int ResolveRenderCornerHeight(
            WorldData world,
            int x,
            int z,
            float cornerX,
            float cornerZ)
        {
            var currentHeight = world.GetSurfaceColumn(x, z).WaterTopUnits;
            var resolvedHeight = SurfaceHeightResolver.ResolveCornerHeight(
                world, x, z, cornerX, cornerZ, SurfaceLayer.Water);
            var directionX = cornerX >= 1f ? 1 : -1;
            var directionZ = cornerZ >= 1f ? 1 : -1;

            var hasX = TryGetWaterHeight(
                world, x + directionX, z, out var xHeight);
            var hasZ = TryGetWaterHeight(
                world, x, z + directionZ, out var zHeight);
            var hasDiagonal = TryGetWaterHeight(
                world,
                x + directionX,
                z + directionZ,
                out var diagonalHeight);

            // At least one edge-connected water cell must own the rise.
            // A diagonal-only cell is a saddle and must stay disconnected.
            if (!hasX && !hasZ)
            {
                return resolvedHeight;
            }

            // Existing water quadrants may not be level with or below this
            // cell. Missing cardinal quadrants are intentionally ignored:
            // the current water cell already covers them with a render apron.
            if (hasX && xHeight <= currentHeight
                || hasZ && zHeight <= currentHeight
                || hasDiagonal && diagonalHeight <= currentHeight)
            {
                return resolvedHeight;
            }

            var targetHeight = int.MaxValue;
            if (hasX)
            {
                targetHeight = Math.Min(targetHeight, xHeight);
            }

            if (hasZ)
            {
                targetHeight = Math.Min(targetHeight, zHeight);
            }

            if (hasDiagonal)
            {
                targetHeight = Math.Min(targetHeight, diagonalHeight);
            }

            return Math.Min(currentHeight + 1, targetHeight);
        }

        private static bool TryGetWaterHeight(
            WorldData world,
            int x,
            int z,
            out int height)
        {
            if (!HasWater(world, x, z))
            {
                height = 0;
                return false;
            }

            height = world.GetSurfaceColumn(x, z).WaterTopUnits;
            return true;
        }

        private static bool HasWater(WorldData world, int x, int z)
            => world.ContainsColumn(x, z)
                && world.GetSurfaceColumn(x, z).HasWater;

        private static bool IsApronTarget(WorldData world, int x, int z)
            => world.ContainsColumn(x, z)
                && world.GetSurfaceColumn(x, z).HasSurface
                && !world.GetSurfaceColumn(x, z).HasWater;

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
                    WorldGrid.ToWorldHeight(heightUnits) + SurfaceOffset,
                    z - startZ + localZ),
                new Vector2(x + localX, z + localZ),
                MaterialBlendResolver.ResolveWater(
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
