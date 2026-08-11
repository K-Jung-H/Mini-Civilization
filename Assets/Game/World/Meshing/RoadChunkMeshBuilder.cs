using System;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Meshing
{
    internal static class RoadChunkMeshBuilder
    {
        public static MeshBuffers Build(
            WorldData world,
            WorldWayPointGraph graph,
            int patchX,
            int patchZ,
            int patchSize,
            float widthRatio,
            WorldSurfaceCatalog catalog,
            MeshBuffers buffers)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            buffers.Clear();
            var startX = patchX * patchSize;
            var startZ = patchZ * patchSize;
            var endX = Math.Min(startX + patchSize, world.Size);
            var endZ = Math.Min(startZ + patchSize, world.Size);
            var origin = new Vector3(
                startX * world.CellSize,
                0f,
                startZ * world.CellSize);
            var halfWidth = world.CellSize * widthRatio * 0.5f;
            var surfaceOffset = world.HeightStep * 0.02f;
            var appearance = MaterialBlendResolver.ResolveTerrainAppearance(
                catalog,
                BiomeType.None,
                SurfaceType.Road);

            for (var index = 0; index < graph.RoadSegments.Count; index++)
            {
                var segment = graph.RoadSegments[index];
                if (segment.OwnerCell.X < startX
                    || segment.OwnerCell.X >= endX
                    || segment.OwnerCell.Z < startZ
                    || segment.OwnerCell.Z >= endZ)
                {
                    continue;
                }

                AddSegment(
                    buffers,
                    segment.Start - origin + Vector3.up * surfaceOffset,
                    segment.End - origin + Vector3.up * surfaceOffset,
                    halfWidth,
                    world.CellSize,
                    appearance);
            }

            return buffers;
        }

        private static void AddSegment(
            MeshBuffers buffers,
            Vector3 start,
            Vector3 end,
            float halfWidth,
            float cellSize,
            in SurfaceAppearance appearance)
        {
            var direction = end - start;
            var horizontal = new Vector3(direction.x, 0f, direction.z);
            if (horizontal.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            horizontal.Normalize();
            var side = new Vector3(-horizontal.z, 0f, horizontal.x)
                * halfWidth;
            var lengthUv = direction.magnitude / cellSize;
            var a = new SurfaceVertex(
                start - side,
                new Vector2(0f, 0f),
                appearance);
            var b = new SurfaceVertex(
                start + side,
                new Vector2(1f, 0f),
                appearance);
            var c = new SurfaceVertex(
                end + side,
                new Vector2(1f, lengthUv),
                appearance);
            var d = new SurfaceVertex(
                end - side,
                new Vector2(0f, lengthUv),
                appearance);
            buffers.AddTriangleFacing(a, b, c, Vector3.up);
            buffers.AddTriangleFacing(a, c, d, Vector3.up);

            AddCap(buffers, start, halfWidth, appearance);
            AddCap(buffers, end, halfWidth, appearance);
        }

        private static void AddCap(
            MeshBuffers buffers,
            Vector3 center,
            float halfWidth,
            in SurfaceAppearance appearance)
        {
            var a = new SurfaceVertex(
                center + new Vector3(-halfWidth, 0f, -halfWidth),
                Vector2.zero,
                appearance);
            var b = new SurfaceVertex(
                center + new Vector3(-halfWidth, 0f, halfWidth),
                Vector2.up,
                appearance);
            var c = new SurfaceVertex(
                center + new Vector3(halfWidth, 0f, halfWidth),
                Vector2.one,
                appearance);
            var d = new SurfaceVertex(
                center + new Vector3(halfWidth, 0f, -halfWidth),
                Vector2.right,
                appearance);
            buffers.AddTriangleFacing(a, b, c, Vector3.up);
            buffers.AddTriangleFacing(a, c, d, Vector3.up);
        }
    }
}
