using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
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
            int patchSize,
            WorldSurfaceCatalog catalog,
            WorldExposureCache exposureCache = null)
        {
            var buffers = new MeshBuffers();
            var startX = patchX * patchSize;
            var startZ = patchZ * patchSize;
            var endX = Math.Min(startX + patchSize, world.Size);
            var endZ = Math.Min(startZ + patchSize, world.Size);

            var cells = new List<ExposedCell>();
            if (exposureCache != null)
            {
                exposureCache.CopySolidCellsForPatch(
                    startX,
                    startZ,
                    endX,
                    endZ,
                    cells);
            }
            else
            {
                for (var y = 0; y < world.Height; y++)
                for (var z = startZ; z < endZ; z++)
                for (var x = startX; x < endX; x++)
                {
                    if (world.GetCell(x, y, z).HasSolid)
                    {
                        var exposure = CellOccupancyResolver.ResolveExposure(
                            world,
                            x,
                            y,
                            z);
                        if (exposure != CellExposureFlags.None)
                        {
                            cells.Add(new ExposedCell(
                                new CellCoordinate(x, y, z),
                                exposure));
                        }
                    }
                }
            }

            for (var index = 0; index < cells.Count; index++)
            {
                var coordinate = cells[index].Coordinate;
                var x = coordinate.X;
                var y = coordinate.Y;
                var z = coordinate.Z;
                var cell = world.GetCell(x, y, z);
                var exposure = cells[index].Exposure;
                var ownerCellIndex = WorldCellIndex.Encode(world, x, y, z);
                CellSurfaceProfile profile = default;
                if ((exposure & CellExposureFlags.SolidTop) != 0)
                {
                    profile = CellSurfaceShapeResolver.Resolve(
                        world,
                        x,
                        y,
                        z,
                        CellSurfaceKind.Solid);
                    buffers.CurrentTriangleMetadata =
                        new SurfaceTriangleMetadata(
                            ownerCellIndex,
                            SurfaceTriangleRole.Core,
                            true);
                    AddVolumeTop(
                        world,
                        catalog,
                        buffers,
                        x,
                        y,
                        z,
                        startX,
                        startZ,
                        profile);
                }

                buffers.CurrentTriangleMetadata = new SurfaceTriangleMetadata(
                    ownerCellIndex,
                    SurfaceTriangleRole.Cliff,
                    true);
                AddVolumeSides(
                    world,
                    catalog,
                    buffers,
                    x,
                    y,
                    z,
                    startX,
                    startZ,
                    cell,
                    exposure,
                    profile);

                if ((exposure & CellExposureFlags.SolidBottom) != 0)
                {
                    buffers.CurrentTriangleMetadata =
                        new SurfaceTriangleMetadata(
                            ownerCellIndex,
                            SurfaceTriangleRole.Bottom,
                            true);
                    AddVolumeBottom(
                        world,
                        catalog,
                        buffers,
                        x,
                        y,
                        z,
                        startX,
                        startZ);
                }
            }

            return buffers;
        }

        private static void AddVolumeTop(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in CellSurfaceProfile profile)
        {
            var height = profile.CurrentHeightUnits;
            AddSurfaceQuad(
                buffers,
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMin, height),
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMax, height),
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMax, height),
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMin, height));

            AddVolumeShoulder(world, catalog, buffers, x, y, z, startX, startZ, -1, 0, profile);
            AddVolumeShoulder(world, catalog, buffers, x, y, z, startX, startZ, 1, 0, profile);
            AddVolumeShoulder(world, catalog, buffers, x, y, z, startX, startZ, 0, -1, profile);
            AddVolumeShoulder(world, catalog, buffers, x, y, z, startX, startZ, 0, 1, profile);

            AddVolumeCorner(world, catalog, buffers, x, y, z, startX, startZ, 0f, 0f, profile);
            AddVolumeCorner(world, catalog, buffers, x, y, z, startX, startZ, 1f, 0f, profile);
            AddVolumeCorner(world, catalog, buffers, x, y, z, startX, startZ, 0f, 1f, profile);
            AddVolumeCorner(world, catalog, buffers, x, y, z, startX, startZ, 1f, 1f, profile);
        }

        private static void AddVolumeShoulder(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            int directionX,
            int directionZ,
            in CellSurfaceProfile profile)
        {
            var current = profile.CurrentHeightUnits;
            var outer = profile.GetEdgeHeight(directionX, directionZ);
            if (directionX < 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, 0f, CoreMin, outer),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, 0f, CoreMax, outer),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMax, current),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMin, current));
                return;
            }

            if (directionX > 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMin, current),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMax, current),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, 1f, CoreMax, outer),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, 1f, CoreMin, outer));
                return;
            }

            if (directionZ < 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, 0f, outer),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMin, current),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMin, current),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, 0f, outer));
                return;
            }

            AddSurfaceQuad(
                buffers,
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMax, current),
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, 1f, outer),
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, 1f, outer),
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMax, current));
        }

        private static void AddVolumeCorner(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            float cornerX,
            float cornerZ,
            in CellSurfaceProfile profile)
        {
            var inwardX = cornerX <= 0f ? 1f : -1f;
            var inwardZ = cornerZ <= 0f ? 1f : -1f;
            var shoulderX = cornerX + inwardX * Shoulder;
            var shoulderZ = cornerZ + inwardZ * Shoulder;
            var directionX = -Mathf.RoundToInt(inwardX);
            var directionZ = -Mathf.RoundToInt(inwardZ);
            var edgeXHeight = profile.GetEdgeHeight(directionX, 0);
            var edgeZHeight = profile.GetEdgeHeight(0, directionZ);
            var cornerHeight = profile.GetCornerHeight(cornerX, cornerZ);

            var topAlongX = CreateCellVertex(
                world, catalog, x, y, z, startX, startZ,
                shoulderX, cornerZ, edgeZHeight);
            var topAlongZ = CreateCellVertex(
                world, catalog, x, y, z, startX, startZ,
                cornerX, shoulderZ, edgeXHeight);
            var inner = CreateCellVertex(
                world, catalog, x, y, z, startX, startZ,
                shoulderX, shoulderZ, profile.CurrentHeightUnits);
            var corner = CreateCellVertex(
                world, catalog, x, y, z, startX, startZ,
                cornerX, cornerZ, cornerHeight);

            if (cornerHeight > profile.CurrentHeightUnits)
            {
                buffers.AddTriangleFacing(corner, topAlongX, topAlongZ, Vector3.up);
                buffers.AddTriangleFacing(topAlongX, inner, topAlongZ, Vector3.up);
                return;
            }

            buffers.AddTriangleFacing(inner, topAlongX, corner, Vector3.up);
            buffers.AddTriangleFacing(inner, corner, topAlongZ, Vector3.up);
        }

        private static void AddVolumeSides(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in CellData cell,
            CellExposureFlags exposure,
            in CellSurfaceProfile topProfile)
        {
            AddVolumeSide(world, catalog, buffers, x, y, z, startX, startZ, cell, exposure, topProfile, -1, 0, CellExposureFlags.SolidNegativeX);
            AddVolumeSide(world, catalog, buffers, x, y, z, startX, startZ, cell, exposure, topProfile, 1, 0, CellExposureFlags.SolidPositiveX);
            AddVolumeSide(world, catalog, buffers, x, y, z, startX, startZ, cell, exposure, topProfile, 0, -1, CellExposureFlags.SolidNegativeZ);
            AddVolumeSide(world, catalog, buffers, x, y, z, startX, startZ, cell, exposure, topProfile, 0, 1, CellExposureFlags.SolidPositiveZ);
        }

        private static void AddVolumeSide(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in CellData cell,
            CellExposureFlags exposure,
            in CellSurfaceProfile topProfile,
            int directionX,
            int directionZ,
            CellExposureFlags requiredFlag)
        {
            if ((exposure & requiredFlag) == 0
                || !CellOccupancyResolver.TryGetSolidSideExposure(
                    world,
                    x,
                    y,
                    z,
                    cell,
                    directionX,
                    directionZ,
                    out var interval))
            {
                return;
            }

            var hasTop = (exposure & CellExposureFlags.SolidTop) != 0;
            var edgeTop = hasTop
                ? topProfile.GetEdgeHeight(directionX, directionZ)
                : interval.TopUnits;
            var startTop = hasTop
                ? GetProfileBoundaryCornerHeight(topProfile, directionX, directionZ, 0f)
                : interval.TopUnits;
            var endTop = hasTop
                ? GetProfileBoundaryCornerHeight(topProfile, directionX, directionZ, 1f)
                : interval.TopUnits;
            ResolveSolidSideBottomProfile(
                world,
                x,
                z,
                directionX,
                directionZ,
                interval.BottomUnits,
                out var startBottom,
                out var edgeBottom,
                out var endBottom);

            AddVolumeSideSegment(
                world, catalog, buffers, x, y, z, startX, startZ,
                directionX, directionZ, 0f, Shoulder,
                startTop, edgeTop, startBottom, edgeBottom);
            AddVolumeSideSegment(
                world, catalog, buffers, x, y, z, startX, startZ,
                directionX, directionZ, Shoulder, 1f - Shoulder,
                edgeTop, edgeTop, edgeBottom, edgeBottom);
            AddVolumeSideSegment(
                world, catalog, buffers, x, y, z, startX, startZ,
                directionX, directionZ, 1f - Shoulder, 1f,
                edgeTop, endTop, edgeBottom, endBottom);
        }

        private static void AddVolumeSideSegment(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
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
            bottom0 = Math.Min(bottom0, top0);
            bottom1 = Math.Min(bottom1, top1);
            if (top0 <= bottom0 && top1 <= bottom1)
            {
                return;
            }

            GetBoundaryCoordinates(directionX, directionZ, t0, out var u0, out var v0);
            GetBoundaryCoordinates(directionX, directionZ, t1, out var u1, out var v1);
            AddCellVerticalQuad(
                world,
                catalog,
                buffers,
                x,
                y,
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

        private static void ResolveSolidSideBottomProfile(
            WorldData world,
            int x,
            int z,
            int directionX,
            int directionZ,
            int fallbackHeight,
            out int startHeight,
            out int edgeHeight,
            out int endHeight)
        {
            startHeight = fallbackHeight;
            edgeHeight = fallbackHeight;
            endHeight = fallbackHeight;
            if (!CellSurfaceShapeResolver.TryResolveSurfaceAtHeight(
                    world,
                    x + directionX,
                    z + directionZ,
                    CellSurfaceKind.Solid,
                    fallbackHeight,
                    out var neighborProfile))
            {
                return;
            }

            edgeHeight = neighborProfile.GetEdgeHeight(
                -directionX,
                -directionZ);
            startHeight = GetProfileBoundaryCornerHeight(
                neighborProfile,
                -directionX,
                -directionZ,
                0f);
            endHeight = GetProfileBoundaryCornerHeight(
                neighborProfile,
                -directionX,
                -directionZ,
                1f);
        }

        private static void AddVolumeBottom(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ)
        {
            var height = y * WorldGrid.HeightStepsPerCell;
            var a = CreateCellVertex(world, catalog, x, y, z, startX, startZ, 0f, 0f, height, SurfaceType.Cliff);
            var b = CreateCellVertex(world, catalog, x, y, z, startX, startZ, 1f, 0f, height, SurfaceType.Cliff);
            var c = CreateCellVertex(world, catalog, x, y, z, startX, startZ, 1f, 1f, height, SurfaceType.Cliff);
            var d = CreateCellVertex(world, catalog, x, y, z, startX, startZ, 0f, 1f, height, SurfaceType.Cliff);
            buffers.AddTriangleFacing(a, b, c, Vector3.down);
            buffers.AddTriangleFacing(a, c, d, Vector3.down);
        }

        private static int GetProfileBoundaryCornerHeight(
            in CellSurfaceProfile profile,
            int directionX,
            int directionZ,
            float t)
        {
            if (directionX < 0)
                return profile.GetCornerHeight(0f, t <= 0f ? 0f : 1f);
            if (directionX > 0)
                return profile.GetCornerHeight(1f, t <= 0f ? 0f : 1f);
            if (directionZ < 0)
                return profile.GetCornerHeight(t <= 0f ? 0f : 1f, 0f);
            return profile.GetCornerHeight(t <= 0f ? 0f : 1f, 1f);
        }

        private static void AddCellVerticalQuad(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
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
            var topVertex0 = CreateCellVerticalVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u0, v0, top0);
            var topVertex1 = CreateCellVerticalVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u1, v1, top1);
            var bottomVertex0 = CreateCellVerticalVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u0, v0, bottom0);
            var bottomVertex1 = CreateCellVerticalVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u1, v1, bottom1);
            var outward = new Vector3(directionX, 0f, directionZ);
            buffers.AddTriangleFacing(bottomVertex0, topVertex0, topVertex1, outward);
            buffers.AddTriangleFacing(bottomVertex0, topVertex1, bottomVertex1, outward);
        }

        private static SurfaceVertex CreateCellVerticalVertex(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            int directionX,
            int directionZ,
            float localX,
            float localZ,
            int heightUnits)
        {
            var worldX = x + localX;
            var worldZ = z + localZ;
            var horizontalUv = directionX < 0
                ? worldZ
                : directionX > 0
                    ? -worldZ
                    : directionZ < 0
                        ? -worldX
                        : worldX;
            return new SurfaceVertex(
                new Vector3(
                    x - startX + localX,
                    WorldGrid.ToWorldHeight(heightUnits),
                    z - startZ + localZ),
                new Vector2(
                    horizontalUv,
                    WorldGrid.ToWorldHeight(heightUnits)),
                MaterialBlendResolver.ResolveTerrainCell(
                    world,
                    catalog,
                    x,
                    y,
                    z,
                    localX,
                    localZ,
                    SurfaceType.Cliff));
        }

        private static SurfaceVertex CreateCellVertex(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            float localX,
            float localZ,
            int heightUnits,
            SurfaceType? surfaceOverride = null)
        {
            return new SurfaceVertex(
                new Vector3(
                    x - startX + localX,
                    WorldGrid.ToWorldHeight(heightUnits),
                    z - startZ + localZ),
                new Vector2(x + localX, z + localZ),
                MaterialBlendResolver.ResolveTerrainCell(
                    world,
                    catalog,
                    x,
                    y,
                    z,
                    localX,
                    localZ,
                    surfaceOverride));
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
