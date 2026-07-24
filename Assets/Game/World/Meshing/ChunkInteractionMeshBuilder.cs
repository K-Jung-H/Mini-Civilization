using System.Collections.Generic;
using MiniCivilization.World.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Meshing
{
    public sealed class ChunkInteractionMeshData
    {
        private readonly List<Vector3> positions = new();
        private readonly List<int> indices = new();
        private readonly List<InteractionTriangleMetadata> metadata = new();
        private readonly Dictionary<Vector3, int> vertexLookup = new();

        public bool IsEmpty => indices.Count == 0;

        internal void AddTriangle(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            in InteractionTriangleMetadata triangleMetadata)
        {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 0.0000001f)
            {
                return;
            }

            indices.Add(AddVertex(a));
            indices.Add(AddVertex(b));
            indices.Add(AddVertex(c));
            metadata.Add(triangleMetadata);
        }

        public Mesh CreateMesh(
            string name,
            out InteractionTriangleMetadata[] triangleMetadata,
            Mesh reusableMesh = null)
        {
            var mesh = reusableMesh != null ? reusableMesh : new Mesh();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = positions.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            triangleMetadata = metadata.ToArray();
            return mesh;
        }

        private int AddVertex(Vector3 position)
        {
            if (vertexLookup.TryGetValue(position, out var existingIndex))
            {
                return existingIndex;
            }

            var index = positions.Count;
            positions.Add(position);
            vertexLookup.Add(position, index);
            return index;
        }
    }

    public static class ChunkInteractionMeshBuilder
    {
        public static ChunkInteractionMeshData Build(
            MeshBuffers terrain,
            WaterChunkMeshBuffers water)
        {
            var result = new ChunkInteractionMeshData();
            Append(
                terrain,
                SurfaceInteractionType.Terrain,
                result);
            Append(
                water.Surface,
                SurfaceInteractionType.Water,
                result);
            Append(
                water.Waterfalls,
                SurfaceInteractionType.Waterfall,
                result);
            return result;
        }

        private static void Append(
            MeshBuffers source,
            SurfaceInteractionType surfaceType,
            ChunkInteractionMeshData target)
        {
            for (var triangleIndex = 0;
                 triangleIndex < source.TriangleCount;
                 triangleIndex++)
            {
                if (!source.TryGetInteractionTriangle(
                        triangleIndex,
                        out var a,
                        out var b,
                        out var c,
                        out var sourceMetadata))
                {
                    continue;
                }

                var metadata = new InteractionTriangleMetadata(
                    sourceMetadata.OwnerCellIndex,
                    surfaceType,
                    sourceMetadata.Role);
                target.AddTriangle(a, b, c, metadata);
            }
        }
    }
}
