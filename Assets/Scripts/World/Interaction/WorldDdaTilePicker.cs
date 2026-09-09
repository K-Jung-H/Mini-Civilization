using MiniCivilization.World.Domain;
using MiniCivilization.World.Meshing;
using MiniCivilization.World.Presentation;
using UnityEngine;
using Unity.Profiling;

namespace MiniCivilization.World.Interaction
{
    public static class WorldDdaTilePicker
    {
        private const float BoundaryEpsilon = 0.0001f;
        private static readonly ProfilerMarker PickerMarker =
            new("World Picking.DDA Surface Query");

        public static bool TryPick(
            WorldData world,
            WorldRenderer renderer,
            Ray worldRay,
            float maxDistance,
            out TilePickResult result)
        {
            using var marker = PickerMarker.Auto();
            result = default;
            if (world == null
                || renderer == null
                || renderer.SurfaceQuery == null
                || maxDistance <= 0f)
            {
                return false;
            }

            var root = renderer.RenderRoot;
            var localOrigin = root != null
                ? root.InverseTransformPoint(worldRay.origin)
                : worldRay.origin;
            var localDirectionUnnormalized = root != null
                ? root.InverseTransformVector(worldRay.direction)
                : worldRay.direction;
            var directionScale = localDirectionUnnormalized.magnitude;
            if (directionScale <= Mathf.Epsilon)
            {
                return false;
            }

            var localRay = new Ray(
                localOrigin,
                localDirectionUnnormalized / directionScale);
            var maximumLocalDistance = maxDistance * directionScale;
            var cellSize = world.CellSize;
            if (!TryResolveHorizontalBounds(
                    world,
                    out var minimumCellX,
                    out var maximumCellX,
                    out var minimumCellZ,
                    out var maximumCellZ))
            {
                return false;
            }

            if (!TryIntersectBounds(
                    localRay,
                    new Vector3(
                        minimumCellX * cellSize,
                        0f,
                        minimumCellZ * cellSize),
                    new Vector3(
                        (maximumCellX + 1) * cellSize,
                        world.Height * cellSize,
                        (maximumCellZ + 1) * cellSize),
                    out var boundsEntry,
                    out var boundsExit)
                || boundsExit < 0f
                || boundsEntry > maximumLocalDistance)
            {
                return false;
            }

            var traversalStart = Mathf.Max(0f, boundsEntry);
            var traversalEnd = Mathf.Min(boundsExit, maximumLocalDistance);
            var startPoint = localRay.GetPoint(
                Mathf.Min(
                    traversalEnd,
                    traversalStart + BoundaryEpsilon));
            var x = Mathf.Clamp(
                Mathf.FloorToInt(startPoint.x / cellSize),
                minimumCellX,
                maximumCellX);
            var y = Mathf.Clamp(
                Mathf.FloorToInt(startPoint.y / cellSize),
                0,
                world.Height - 1);
            var z = Mathf.Clamp(
                Mathf.FloorToInt(startPoint.z / cellSize),
                minimumCellZ,
                maximumCellZ);

            InitializeAxis(
                localRay.origin.x,
                localRay.direction.x,
                x,
                cellSize,
                out var stepX,
                out var nextX,
                out var deltaX);
            InitializeAxis(
                localRay.origin.y,
                localRay.direction.y,
                y,
                cellSize,
                out var stepY,
                out var nextY,
                out var deltaY);
            InitializeAxis(
                localRay.origin.z,
                localRay.direction.z,
                z,
                cellSize,
                out var stepZ,
                out var nextZ,
                out var deltaZ);

            var cellEntry = traversalStart;
            while (world.Contains(x, y, z)
                   && cellEntry <= traversalEnd + BoundaryEpsilon)
            {
                var cellExit = Mathf.Min(nextX, Mathf.Min(nextY, nextZ));
                cellExit = Mathf.Min(cellExit, traversalEnd);
                var coordinate = new CellCoordinate(x, y, z);
                if (world.IsChunkLoaded(x, z)
                    && renderer.SurfaceQuery.TryRaycastCell(
                        localRay,
                        coordinate,
                        cellEntry - BoundaryEpsilon,
                        cellExit + BoundaryEpsilon,
                        out var surfaceHit))
                {
                    var worldPoint = root != null
                        ? root.TransformPoint(surfaceHit.Point)
                        : surfaceHit.Point;
                    var worldNormal = root != null
                        ? root.localToWorldMatrix.inverse.transpose
                            .MultiplyVector(surfaceHit.Normal).normalized
                        : surfaceHit.Normal;
                    var worldDistance = Vector3.Distance(
                        worldRay.origin,
                        worldPoint);
                    if (worldDistance <= maxDistance + BoundaryEpsilon)
                    {
                        result = new TilePickResult(
                            coordinate,
                            surfaceHit.SurfaceType,
                            surfaceHit.Face,
                            worldPoint,
                            worldNormal,
                            worldDistance);
                        return true;
                    }
                }

                if (cellExit >= traversalEnd - BoundaryEpsilon)
                {
                    break;
                }

                var threshold = cellExit + BoundaryEpsilon;
                if (nextX <= threshold)
                {
                    x += stepX;
                    nextX += deltaX;
                }

                if (nextY <= threshold)
                {
                    y += stepY;
                    nextY += deltaY;
                }

                if (nextZ <= threshold)
                {
                    z += stepZ;
                    nextZ += deltaZ;
                }

                cellEntry = cellExit;
            }

            return false;
        }

        private static bool TryResolveHorizontalBounds(
            WorldData world,
            out int minimumCellX,
            out int maximumCellX,
            out int minimumCellZ,
            out int maximumCellZ)
        {
            if (!world.IsInfinite)
            {
                minimumCellX = world.MinimumCellX;
                maximumCellX = world.MaximumCellXExclusive - 1;
                minimumCellZ = world.MinimumCellZ;
                maximumCellZ = world.MaximumCellZExclusive - 1;
                return true;
            }

            var hasChunk = false;
            var minimumChunkX = int.MaxValue;
            var maximumChunkX = int.MinValue;
            var minimumChunkZ = int.MaxValue;
            var maximumChunkZ = int.MinValue;
            foreach (var chunk in world.EnumerateLoadedChunks())
            {
                hasChunk = true;
                minimumChunkX = System.Math.Min(
                    minimumChunkX,
                    chunk.Coordinate.X);
                maximumChunkX = System.Math.Max(
                    maximumChunkX,
                    chunk.Coordinate.X);
                minimumChunkZ = System.Math.Min(
                    minimumChunkZ,
                    chunk.Coordinate.Z);
                maximumChunkZ = System.Math.Max(
                    maximumChunkZ,
                    chunk.Coordinate.Z);
            }

            minimumCellX = minimumChunkX * world.ChunkSizeX;
            maximumCellX = (maximumChunkX + 1) * world.ChunkSizeX - 1;
            minimumCellZ = minimumChunkZ * world.ChunkSizeZ;
            maximumCellZ = (maximumChunkZ + 1) * world.ChunkSizeZ - 1;
            return hasChunk;
        }

        private static void InitializeAxis(
            float origin,
            float direction,
            int cell,
            float cellSize,
            out int step,
            out float nextBoundary,
            out float delta)
        {
            if (direction > BoundaryEpsilon)
            {
                step = 1;
                nextBoundary = ((cell + 1) * cellSize - origin) / direction;
                delta = cellSize / direction;
            }
            else if (direction < -BoundaryEpsilon)
            {
                step = -1;
                nextBoundary = (cell * cellSize - origin) / direction;
                delta = -cellSize / direction;
            }
            else
            {
                step = 0;
                nextBoundary = float.PositiveInfinity;
                delta = float.PositiveInfinity;
            }
        }

        private static bool TryIntersectBounds(
            Ray ray,
            Vector3 minimum,
            Vector3 maximum,
            out float entry,
            out float exit)
        {
            entry = float.NegativeInfinity;
            exit = float.PositiveInfinity;
            return IntersectAxis(
                    ray.origin.x,
                    ray.direction.x,
                    minimum.x,
                    maximum.x,
                    ref entry,
                    ref exit)
                && IntersectAxis(
                    ray.origin.y,
                    ray.direction.y,
                    minimum.y,
                    maximum.y,
                    ref entry,
                    ref exit)
                && IntersectAxis(
                    ray.origin.z,
                    ray.direction.z,
                    minimum.z,
                    maximum.z,
                    ref entry,
                    ref exit)
                && entry <= exit;
        }

        private static bool IntersectAxis(
            float origin,
            float direction,
            float minimum,
            float maximum,
            ref float entry,
            ref float exit)
        {
            if (Mathf.Abs(direction) <= BoundaryEpsilon)
            {
                return origin >= minimum && origin <= maximum;
            }

            var inverse = 1f / direction;
            var first = (minimum - origin) * inverse;
            var second = (maximum - origin) * inverse;
            if (first > second)
            {
                (first, second) = (second, first);
            }

            entry = Mathf.Max(entry, first);
            exit = Mathf.Min(exit, second);
            return entry <= exit;
        }
    }
}
