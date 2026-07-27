using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using UnityEngine;

namespace MiniCivilization.World.Meshing
{
    internal static class TerrainChunkMeshBuilder
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
            WorldExposureCache exposureCache,
            MeshBuffers buffers,
            List<ExposedCell> cells)
        {
            buffers.Clear();
            var startX = patchX * patchSize;
            var startZ = patchZ * patchSize;
            var endX = Math.Min(startX + patchSize, world.Size);
            var endZ = Math.Min(startZ + patchSize, world.Size);

            exposureCache.CopySolidCellsForPatch(
                startX,
                startZ,
                endX,
                endZ,
                cells);

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
                        z);
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
            var outerStart = profile.GetBoundaryHeight(
                directionX,
                directionZ,
                CoreMin);
            var outerEnd = profile.GetBoundaryHeight(
                directionX,
                directionZ,
                CoreMax);
            if (directionX < 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, 0f, CoreMin, outerStart),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, 0f, CoreMax, outerEnd),
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
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, 1f, CoreMax, outerEnd),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, 1f, CoreMin, outerStart));
                return;
            }

            if (directionZ < 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, 0f, outerStart),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMin, current),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMin, current),
                    CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, 0f, outerEnd));
                return;
            }

            AddSurfaceQuad(
                buffers,
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMax, current),
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMin, 1f, outerStart),
                CreateCellVertex(world, catalog, x, y, z, startX, startZ, CoreMax, 1f, outerEnd),
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
            var edgeXHeight = profile.GetBoundaryHeight(
                directionX,
                0,
                shoulderZ);
            var edgeZHeight = profile.GetBoundaryHeight(
                0,
                directionZ,
                shoulderX);
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

            if ((exposure & CellExposureFlags.SolidTop) == 0)
            {
                return;
            }

            AddVolumeCornerClosure(world, catalog, buffers, x, y, z, startX, startZ, cell, topProfile, -1, -1);
            AddVolumeCornerClosure(world, catalog, buffers, x, y, z, startX, startZ, cell, topProfile, 1, -1);
            AddVolumeCornerClosure(world, catalog, buffers, x, y, z, startX, startZ, cell, topProfile, -1, 1);
            AddVolumeCornerClosure(world, catalog, buffers, x, y, z, startX, startZ, cell, topProfile, 1, 1);
        }

        private static void AddVolumeCornerClosure(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in CellData cell,
            in CellSurfaceProfile topProfile,
            int directionX,
            int directionZ)
        {
            if (!CellOccupancyResolver.TryGetSolidSideExposure(
                    world, x, y, z, cell, directionX, 0, out var xInterval)
                || !CellOccupancyResolver.TryGetSolidSideExposure(
                    world, x, y, z, cell, 0, directionZ, out var zInterval))
            {
                return;
            }

            ResolveSolidSideBottomProfile(
                world,
                x,
                z,
                directionX,
                0,
                xInterval.BottomUnits,
                out var xStart,
                out var xShoulderStart,
                out var xShoulderEnd,
                out var xEnd);
            ResolveSolidSideBottomProfile(
                world,
                x,
                z,
                0,
                directionZ,
                zInterval.BottomUnits,
                out var zStart,
                out var zShoulderStart,
                out var zShoulderEnd,
                out var zEnd);

            var xBoundary = new CellSurfaceBoundary(
                xStart,
                xShoulderStart,
                xShoulderEnd,
                xEnd);
            var zBoundary = new CellSurfaceBoundary(
                zStart,
                zShoulderStart,
                zShoulderEnd,
                zEnd);
            var cornerX = directionX < 0 ? 0f : 1f;
            var cornerZ = directionZ < 0 ? 0f : 1f;
            var shoulderX = directionX < 0 ? Shoulder : 1f - Shoulder;
            var shoulderZ = directionZ < 0 ? Shoulder : 1f - Shoulder;
            var cellBottom = y * WorldGrid.HeightStepsPerCell;
            var cellCeiling = (y + 1) * WorldGrid.HeightStepsPerCell;

            var xCornerBottom = ClampSideBottom(
                xBoundary.GetHeight(cornerZ),
                topProfile.GetBoundaryHeight(directionX, 0, cornerZ),
                cellBottom,
                cellCeiling);
            var xShoulderBottom = ClampSideBottom(
                xBoundary.GetHeight(shoulderZ),
                topProfile.GetBoundaryHeight(directionX, 0, shoulderZ),
                cellBottom,
                cellCeiling);
            var zCornerBottom = ClampSideBottom(
                zBoundary.GetHeight(cornerX),
                topProfile.GetBoundaryHeight(0, directionZ, cornerX),
                cellBottom,
                cellCeiling);
            var zShoulderBottom = ClampSideBottom(
                zBoundary.GetHeight(shoulderX),
                topProfile.GetBoundaryHeight(0, directionZ, shoulderX),
                cellBottom,
                cellCeiling);

            if (ApproximatelyEqual(xCornerBottom, xShoulderBottom)
                && ApproximatelyEqual(xCornerBottom, zCornerBottom)
                && ApproximatelyEqual(xCornerBottom, zShoulderBottom))
            {
                return;
            }

            var xCorner = CreateCornerClosureVertex(
                world, catalog, x, y, z, startX, startZ,
                directionX, directionZ, cornerX, cornerZ, xCornerBottom);
            var alongXSide = CreateCornerClosureVertex(
                world, catalog, x, y, z, startX, startZ,
                directionX, directionZ, cornerX, shoulderZ, xShoulderBottom);
            var alongZSide = CreateCornerClosureVertex(
                world, catalog, x, y, z, startX, startZ,
                directionX, directionZ, shoulderX, cornerZ, zShoulderBottom);
            var zCorner = CreateCornerClosureVertex(
                world, catalog, x, y, z, startX, startZ,
                directionX, directionZ, cornerX, cornerZ, zCornerBottom);
            var outward = new Vector3(directionX, 0f, directionZ).normalized;

            buffers.AddTriangleFacing(
                xCorner,
                alongXSide,
                alongZSide,
                outward);
            buffers.AddTriangleFacing(
                xCorner,
                alongZSide,
                zCorner,
                outward);
        }

        private static bool ApproximatelyEqual(float a, float b) =>
            Mathf.Abs(a - b) <= 0.0001f;

        private static float ClampSideBottom(
            float bottom,
            float top,
            int cellBottom,
            int cellCeiling) =>
            Math.Min(Mathf.Clamp(bottom, cellBottom, cellCeiling), top);

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
            var startTop = hasTop
                ? topProfile.GetBoundaryHeight(directionX, directionZ, 0f)
                : interval.TopUnits;
            var shoulderStartTop = hasTop
                ? topProfile.GetBoundaryHeight(directionX, directionZ, Shoulder)
                : interval.TopUnits;
            var shoulderEndTop = hasTop
                ? topProfile.GetBoundaryHeight(directionX, directionZ, 1f - Shoulder)
                : interval.TopUnits;
            var endTop = hasTop
                ? topProfile.GetBoundaryHeight(directionX, directionZ, 1f)
                : interval.TopUnits;
            ResolveSolidSideBottomProfile(
                world,
                x,
                z,
                directionX,
                directionZ,
                interval.BottomUnits,
                out var startBottom,
                out var shoulderStartBottom,
                out var shoulderEndBottom,
                out var endBottom);
            var cellBottom = y * WorldGrid.HeightStepsPerCell;
            var cellCeiling = (y + 1) * WorldGrid.HeightStepsPerCell;
            startBottom = Mathf.Clamp(startBottom, cellBottom, cellCeiling);
            shoulderStartBottom = Mathf.Clamp(shoulderStartBottom, cellBottom, cellCeiling);
            shoulderEndBottom = Mathf.Clamp(shoulderEndBottom, cellBottom, cellCeiling);
            endBottom = Mathf.Clamp(endBottom, cellBottom, cellCeiling);

            AddVolumeSideSegment(
                world, catalog, buffers, x, y, z, startX, startZ,
                directionX, directionZ, 0f, Shoulder,
                startTop, shoulderStartTop, startBottom, shoulderStartBottom);
            AddVolumeSideSegment(
                world, catalog, buffers, x, y, z, startX, startZ,
                directionX, directionZ, Shoulder, 1f - Shoulder,
                shoulderStartTop, shoulderEndTop, shoulderStartBottom, shoulderEndBottom);
            AddVolumeSideSegment(
                world, catalog, buffers, x, y, z, startX, startZ,
                directionX, directionZ, 1f - Shoulder, 1f,
                shoulderEndTop, endTop, shoulderEndBottom, endBottom);
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
            float top0,
            float top1,
            float bottom0,
            float bottom1)
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
            out float startHeight,
            out float shoulderStartHeight,
            out float shoulderEndHeight,
            out float endHeight)
        {
            startHeight = fallbackHeight;
            shoulderStartHeight = fallbackHeight;
            shoulderEndHeight = fallbackHeight;
            endHeight = fallbackHeight;
            if (!CellSurfaceShapeResolver.TryResolveSurfaceAtHeight(
                    world,
                    x + directionX,
                    z + directionZ,
                    fallbackHeight,
                    out var neighborProfile))
            {
                return;
            }

            startHeight = neighborProfile.GetBoundaryHeight(
                -directionX, -directionZ, 0f);
            shoulderStartHeight = neighborProfile.GetBoundaryHeight(
                -directionX, -directionZ, Shoulder);
            shoulderEndHeight = neighborProfile.GetBoundaryHeight(
                -directionX, -directionZ, 1f - Shoulder);
            endHeight = neighborProfile.GetBoundaryHeight(
                -directionX, -directionZ, 1f);
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
            float top0,
            float top1,
            float bottom0,
            float bottom1)
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
            float heightUnits)
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
                    heightUnits * WorldGrid.HeightStep,
                    z - startZ + localZ),
                new Vector2(
                    horizontalUv,
                    heightUnits * WorldGrid.HeightStep),
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

        private static SurfaceVertex CreateCornerClosureVertex(
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
            float heightUnits)
        {
            var worldX = x + localX;
            var worldZ = z + localZ;
            var horizontalUv = worldX * directionZ
                - worldZ * directionX;
            return new SurfaceVertex(
                new Vector3(
                    x - startX + localX,
                    heightUnits * WorldGrid.HeightStep,
                    z - startZ + localZ),
                new Vector2(
                    horizontalUv,
                    heightUnits * WorldGrid.HeightStep),
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
            float heightUnits,
            SurfaceType? surfaceOverride = null)
        {
            return new SurfaceVertex(
                new Vector3(
                    x - startX + localX,
                    heightUnits * WorldGrid.HeightStep,
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
