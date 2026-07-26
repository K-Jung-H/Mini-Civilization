using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
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
            int patchSize,
            WorldSurfaceCatalog catalog,
            WorldExposureCache exposureCache = null)
        {
            var buffers = new WaterChunkMeshBuffers();
            var startX = patchX * patchSize;
            var startZ = patchZ * patchSize;
            var endX = Math.Min(startX + patchSize, world.Size);
            var endZ = Math.Min(startZ + patchSize, world.Size);

            var waterCells = new List<ExposedCell>();
            if (exposureCache != null)
            {
                exposureCache.CopyWaterCellsForPatch(
                    startX,
                    startZ,
                    endX,
                    endZ,
                    waterCells);
            }
            else
            {
                for (var y = 0; y < world.Height; y++)
                for (var z = startZ; z < endZ; z++)
                for (var x = startX; x < endX; x++)
                {
                    if (world.GetCell(x, y, z).HasWater)
                    {
                        var exposure = CellOccupancyResolver.ResolveExposure(
                            world,
                            x,
                            y,
                            z);
                        if (exposure != CellExposureFlags.None)
                        {
                            waterCells.Add(new ExposedCell(
                                new CellCoordinate(x, y, z),
                                exposure));
                        }
                    }
                }
            }

            for (var index = 0; index < waterCells.Count; index++)
            {
                var coordinate = waterCells[index].Coordinate;
                var x = coordinate.X;
                var y = coordinate.Y;
                var z = coordinate.Z;
                var cell = world.GetCell(x, y, z);
                var exposure = waterCells[index].Exposure;
                var ownerCellIndex = WorldCellIndex.Encode(world, x, y, z);
                CellSurfaceProfile profile = default;
                if ((exposure & CellExposureFlags.WaterTop) != 0)
                {
                    profile = CellSurfaceShapeResolver.Resolve(
                        world,
                        x,
                        y,
                        z,
                        CellSurfaceKind.Water);
                    buffers.Surface.CurrentTriangleMetadata =
                        new SurfaceTriangleMetadata(
                            ownerCellIndex,
                            SurfaceTriangleRole.Core,
                            true);
                    AddVolumeWaterTop(
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

                AddVolumeWaterSides(
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
                    profile,
                    ownerCellIndex);

                if ((exposure & CellExposureFlags.WaterBottom) != 0)
                {
                    buffers.Surface.CurrentTriangleMetadata =
                        new SurfaceTriangleMetadata(
                            ownerCellIndex,
                            SurfaceTriangleRole.Bottom,
                            true);
                    AddVolumeWaterBottom(
                        world,
                        catalog,
                        buffers.Surface,
                        x,
                        y,
                        z,
                        startX,
                        startZ,
                        cell);
                }
            }

            // Aprons are visual-only shoreline extensions. They are emitted
            // after logical water and never own interaction triangles.
            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                var column = world.GetSurfaceColumn(x, z);
                if (!column.HasSurface || column.HasWater)
                {
                    continue;
                }

                buffers.Surface.CurrentTriangleMetadata =
                    SurfaceTriangleMetadata.NotInteractive;
                AddRenderApron(world, catalog, buffers, x, z, startX, startZ);
            }

            return buffers;
        }

        private static void AddVolumeWaterTop(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterChunkMeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in CellSurfaceProfile profile)
        {
            var height = profile.CurrentHeightUnits;
            AddSurfaceQuad(
                buffers.Surface,
                CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMin, height),
                CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMax, height),
                CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMax, height),
                CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMin, height));

            AddVolumeWaterShoulder(world, catalog, buffers.Surface, x, y, z, startX, startZ, -1, 0, profile);
            AddVolumeWaterShoulder(world, catalog, buffers.Surface, x, y, z, startX, startZ, 1, 0, profile);
            AddVolumeWaterShoulder(world, catalog, buffers.Surface, x, y, z, startX, startZ, 0, -1, profile);
            AddVolumeWaterShoulder(world, catalog, buffers.Surface, x, y, z, startX, startZ, 0, 1, profile);
            AddVolumeWaterCorner(world, catalog, buffers, x, y, z, startX, startZ, 0f, 0f, profile);
            AddVolumeWaterCorner(world, catalog, buffers, x, y, z, startX, startZ, 1f, 0f, profile);
            AddVolumeWaterCorner(world, catalog, buffers, x, y, z, startX, startZ, 0f, 1f, profile);
            AddVolumeWaterCorner(world, catalog, buffers, x, y, z, startX, startZ, 1f, 1f, profile);
        }

        private static void AddVolumeWaterShoulder(
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
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, 0f, CoreMin, outer),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, 0f, CoreMax, outer),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMax, current),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMin, current));
                return;
            }

            if (directionX > 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMin, current),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMax, current),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, 1f, CoreMax, outer),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, 1f, CoreMin, outer));
                return;
            }

            if (directionZ < 0)
            {
                AddSurfaceQuad(
                    buffers,
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMin, 0f, outer),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMin, current),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMin, current),
                    CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMax, 0f, outer));
                return;
            }

            AddSurfaceQuad(
                buffers,
                CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMin, CoreMax, current),
                CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMin, 1f, outer),
                CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMax, 1f, outer),
                CreateCellWaterVertex(world, catalog, x, y, z, startX, startZ, CoreMax, CoreMax, current));
        }

        private static void AddVolumeWaterCorner(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterChunkMeshBuffers buffers,
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
            var topAlongX = CreateCellWaterVertex(
                world, catalog, x, y, z, startX, startZ,
                shoulderX, cornerZ, edgeZHeight);
            var topAlongZ = CreateCellWaterVertex(
                world, catalog, x, y, z, startX, startZ,
                cornerX, shoulderZ, edgeXHeight);
            var inner = CreateCellWaterVertex(
                world, catalog, x, y, z, startX, startZ,
                shoulderX, shoulderZ, profile.CurrentHeightUnits);
            var corner = CreateCellWaterVertex(
                world, catalog, x, y, z, startX, startZ,
                cornerX, cornerZ, cornerHeight);

            var column = world.GetSurfaceColumn(x, z);
            if (column.HasWater
                && column.WaterCellY == y
                && TryAddApronNotchCap(
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
                    profile.CurrentHeightUnits))
            {
                return;
            }

            buffers.Surface.AddTriangleFacing(corner, topAlongX, topAlongZ, Vector3.up);
            buffers.Surface.AddTriangleFacing(topAlongX, inner, topAlongZ, Vector3.up);
        }

        private static SurfaceVertex CreateCellWaterVertex(
            WorldData world,
            WorldSurfaceCatalog catalog,
            int x,
            int y,
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
                MaterialBlendResolver.ResolveWaterCell(
                    world,
                    catalog,
                    x,
                    y,
                    z,
                    localX,
                    localZ));
        }

        private static void AddVolumeWaterSides(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterChunkMeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in CellData cell,
            CellExposureFlags exposure,
            in CellSurfaceProfile topProfile,
            int ownerCellIndex)
        {
            AddVolumeWaterSide(world, catalog, buffers, x, y, z, startX, startZ, cell, exposure, topProfile, ownerCellIndex, -1, 0, CellExposureFlags.WaterNegativeX);
            AddVolumeWaterSide(world, catalog, buffers, x, y, z, startX, startZ, cell, exposure, topProfile, ownerCellIndex, 1, 0, CellExposureFlags.WaterPositiveX);
            AddVolumeWaterSide(world, catalog, buffers, x, y, z, startX, startZ, cell, exposure, topProfile, ownerCellIndex, 0, -1, CellExposureFlags.WaterNegativeZ);
            AddVolumeWaterSide(world, catalog, buffers, x, y, z, startX, startZ, cell, exposure, topProfile, ownerCellIndex, 0, 1, CellExposureFlags.WaterPositiveZ);
        }

        private static void AddVolumeWaterSide(
            WorldData world,
            WorldSurfaceCatalog catalog,
            WaterChunkMeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in CellData cell,
            CellExposureFlags exposure,
            in CellSurfaceProfile topProfile,
            int ownerCellIndex,
            int directionX,
            int directionZ,
            CellExposureFlags requiredFlag)
        {
            if ((exposure & requiredFlag) == 0
                || !CellOccupancyResolver.TryGetWaterSideExposure(
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

            var hasTop = (exposure & CellExposureFlags.WaterTop) != 0;
            CellSurfaceProfile lowerWaterProfile = default;
            var hasWaterfallConnector = hasTop
                && TryResolveWaterfallTarget(
                    world,
                    x,
                    z,
                    directionX,
                    directionZ,
                    interval.BottomUnits,
                    topProfile.CurrentHeightUnits,
                    out lowerWaterProfile);
            var isWaterfall = interval.TopUnits - interval.BottomUnits >= 2
                || hasWaterfallConnector;
            var target = isWaterfall ? buffers.Waterfalls : buffers.Surface;
            target.CurrentTriangleMetadata = new SurfaceTriangleMetadata(
                ownerCellIndex,
                isWaterfall
                    ? SurfaceTriangleRole.Waterfall
                    : SurfaceTriangleRole.GapFill,
                true);
            var edgeTop = hasTop
                ? topProfile.GetEdgeHeight(directionX, directionZ)
                : interval.TopUnits;
            var startTop = hasTop
                ? GetWaterBoundaryCornerHeight(
                    topProfile,
                    directionX,
                    directionZ,
                    0f)
                : interval.TopUnits;
            var endTop = hasTop
                ? GetWaterBoundaryCornerHeight(
                    topProfile,
                    directionX,
                    directionZ,
                    1f)
                : interval.TopUnits;

            ResolveWaterSideBottomProfile(
                world,
                x,
                z,
                directionX,
                directionZ,
                interval.BottomUnits,
                out var startBottom,
                out var edgeBottom,
                out var endBottom);

            AddWaterSideSegment(world, catalog, target, x, y, z, startX, startZ, directionX, directionZ, 0f, Shoulder, startTop, edgeTop, startBottom, edgeBottom);
            AddWaterSideSegment(world, catalog, target, x, y, z, startX, startZ, directionX, directionZ, Shoulder, 1f - Shoulder, edgeTop, edgeTop, edgeBottom, edgeBottom);
            AddWaterSideSegment(world, catalog, target, x, y, z, startX, startZ, directionX, directionZ, 1f - Shoulder, 1f, edgeTop, endTop, edgeBottom, endBottom);

            if (!hasWaterfallConnector)
            {
                return;
            }

            buffers.Waterfalls.CurrentTriangleMetadata =
                new SurfaceTriangleMetadata(
                    ownerCellIndex,
                    SurfaceTriangleRole.Waterfall,
                    true);
            var lowerEdge = lowerWaterProfile.GetEdgeHeight(
                -directionX,
                -directionZ);
            var lowerStart = GetWaterBoundaryCornerHeight(
                lowerWaterProfile,
                -directionX,
                -directionZ,
                0f);
            var lowerEnd = GetWaterBoundaryCornerHeight(
                lowerWaterProfile,
                -directionX,
                -directionZ,
                1f);
            AddWaterSideSegment(world, catalog, buffers.Waterfalls, x, y, z, startX, startZ, directionX, directionZ, 0f, Shoulder, startBottom, edgeBottom, lowerStart, lowerEdge);
            AddWaterSideSegment(world, catalog, buffers.Waterfalls, x, y, z, startX, startZ, directionX, directionZ, Shoulder, 1f - Shoulder, edgeBottom, edgeBottom, lowerEdge, lowerEdge);
            AddWaterSideSegment(world, catalog, buffers.Waterfalls, x, y, z, startX, startZ, directionX, directionZ, 1f - Shoulder, 1f, edgeBottom, endBottom, lowerEdge, lowerEnd);
        }

        private static void AddWaterSideSegment(
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
            var topVertex0 = CreateCellWaterVerticalVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u0, v0, top0);
            var topVertex1 = CreateCellWaterVerticalVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u1, v1, top1);
            var bottomVertex0 = CreateCellWaterVerticalVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u0, v0, bottom0);
            var bottomVertex1 = CreateCellWaterVerticalVertex(world, catalog, x, y, z, startX, startZ, directionX, directionZ, u1, v1, bottom1);
            var outward = new Vector3(directionX, 0f, directionZ);
            buffers.AddTriangleFacing(bottomVertex0, topVertex0, topVertex1, outward);
            buffers.AddTriangleFacing(bottomVertex0, topVertex1, bottomVertex1, outward);
        }

        private static void ResolveWaterSideBottomProfile(
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
                    CellSurfaceKind.Water,
                    fallbackHeight,
                    out var neighborProfile))
            {
                return;
            }

            edgeHeight = neighborProfile.GetEdgeHeight(
                -directionX,
                -directionZ);
            startHeight = GetWaterBoundaryCornerHeight(
                neighborProfile,
                -directionX,
                -directionZ,
                0f);
            endHeight = GetWaterBoundaryCornerHeight(
                neighborProfile,
                -directionX,
                -directionZ,
                1f);
        }

        private static bool TryResolveWaterfallTarget(
            WorldData world,
            int x,
            int z,
            int directionX,
            int directionZ,
            int upperSideBottom,
            int upperSurfaceHeight,
            out CellSurfaceProfile lowerProfile)
        {
            lowerProfile = default;
            if (!CellSurfaceShapeResolver.TryResolveHighestSurfaceBelow(
                    world,
                    x + directionX,
                    z + directionZ,
                    CellSurfaceKind.Water,
                    upperSideBottom,
                    out var lowerHeight,
                    out lowerProfile))
            {
                return false;
            }

            return upperSurfaceHeight - lowerHeight >= 2;
        }

        private static SurfaceVertex CreateCellWaterVerticalVertex(
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
                    WorldGrid.ToWorldHeight(heightUnits) + SurfaceOffset,
                    z - startZ + localZ),
                new Vector2(
                    horizontalUv,
                    WorldGrid.ToWorldHeight(heightUnits)),
                MaterialBlendResolver.ResolveWaterCell(
                    world,
                    catalog,
                    x,
                    y,
                    z,
                    localX,
                    localZ));
        }

        private static int GetWaterBoundaryCornerHeight(
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

        private static void AddVolumeWaterBottom(
            WorldData world,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers,
            int x,
            int y,
            int z,
            int startX,
            int startZ,
            in CellData cell)
        {
            var height = y * WorldGrid.HeightStepsPerCell + cell.SolidFill;
            var a = CreateCellWaterVertex(
                world, catalog, x, y, z, startX, startZ, 0f, 0f, height);
            var b = CreateCellWaterVertex(
                world, catalog, x, y, z, startX, startZ, 1f, 0f, height);
            var c = CreateCellWaterVertex(
                world, catalog, x, y, z, startX, startZ, 1f, 1f, height);
            var d = CreateCellWaterVertex(
                world, catalog, x, y, z, startX, startZ, 0f, 1f, height);
            buffers.AddTriangleFacing(a, b, c, Vector3.down);
            buffers.AddTriangleFacing(a, c, d, Vector3.down);
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

            var previousMetadata = buffers.CurrentTriangleMetadata;
            buffers.CurrentTriangleMetadata = new SurfaceTriangleMetadata(
                -1,
                SurfaceTriangleRole.ApronBridge,
                false);
            // One owning water tile fills one V-shaped notch. The opposite
            // diagonal water tile produces the second triangle with the same
            // outer base and its own recessed Shoulder vertex.
            buffers.AddTriangleFacing(
                xApronEnd,
                recessedShoulder,
                zApronEnd,
                Vector3.up);
            buffers.CurrentTriangleMetadata = previousMetadata;
            return true;
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

        private static bool HasWater(WorldData world, int x, int z)
            => world.ContainsColumn(x, z)
                && world.GetSurfaceColumn(x, z).HasWater;

        private static bool IsApronTarget(WorldData world, int x, int z)
            => world.ContainsColumn(x, z)
                && world.GetSurfaceColumn(x, z).HasSurface
                && !world.GetSurfaceColumn(x, z).HasWater;

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
