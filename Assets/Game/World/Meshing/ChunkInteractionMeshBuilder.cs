using System.Collections.Generic;
using MiniCivilization.World.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Meshing
{
    internal sealed class ChunkInteractionMeshData
    {
        private readonly List<Vector3> positions = new();
        private readonly List<int> indices = new();
        private readonly List<InteractionTriangleMetadata> metadata = new();
        private readonly Dictionary<Vector3, int> vertexLookup = new();
        private InteractionTriangleMetadata[] metadataBuffer =
            System.Array.Empty<InteractionTriangleMetadata>();

        public bool IsEmpty => indices.Count == 0;

        internal void Clear()
        {
            positions.Clear();
            indices.Clear();
            metadata.Clear();
            vertexLookup.Clear();
        }

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
            if (metadataBuffer.Length != metadata.Count)
            {
                metadataBuffer = new InteractionTriangleMetadata[metadata.Count];
            }

            metadata.CopyTo(metadataBuffer, 0);
            triangleMetadata = metadataBuffer;
            return mesh;
        }

        internal void AppendTo(ChunkInteractionMeshData target)
        {
            for (var triangleIndex = 0;
                 triangleIndex < metadata.Count;
                 triangleIndex++)
            {
                var indexStart = triangleIndex * 3;
                target.AddTriangle(
                    positions[indices[indexStart]],
                    positions[indices[indexStart + 1]],
                    positions[indices[indexStart + 2]],
                    metadata[triangleIndex]);
            }
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

    internal static class ChunkInteractionMeshBuilder
    {
        internal static void BuildTerrainCache(
            MeshBuffers terrain,
            ChunkInteractionMeshData target)
        {
            target.Clear();
            Append(
                terrain,
                SurfaceInteractionType.Terrain,
                target);
        }

        internal static ChunkInteractionMeshData BuildFromTerrainCache(
            ChunkInteractionMeshData terrain,
            MeshBuffers water,
            ChunkInteractionMeshData reusableData)
        {
            reusableData.Clear();
            terrain.AppendTo(reusableData);
            Append(
                water,
                SurfaceInteractionType.Water,
                reusableData);
            return reusableData;
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
