using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Meshing
{
    internal readonly struct SurfaceVertex
    {
        public readonly Vector3 Position;
        public readonly Vector2 Uv;
        public readonly SurfaceAppearance Appearance;

        public SurfaceVertex(Vector3 position, Vector2 uv, in SurfaceAppearance appearance)
        {
            Position = position;
            Uv = uv;
            Appearance = appearance;
        }
    }

    public sealed class MeshBuffers
    {
        private readonly List<Vector3> positions = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector4> tangents = new();
        private readonly List<Vector2> uv0 = new();
        private readonly List<Vector4> materialParameters = new();
        private readonly List<Color> colors = new();
        private readonly List<int> indices = new();

        public int VertexCount => positions.Count;
        public int TriangleCount => indices.Count / 3;
        public bool IsEmpty => positions.Count == 0;

        internal void AddTriangle(in SurfaceVertex a, in SurfaceVertex b, in SurfaceVertex c)
        {
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (cross.sqrMagnitude < 0.0000001f)
            {
                return;
            }

            var normal = cross.normalized;
            var tangentDirection = Vector3.ProjectOnPlane(Vector3.right, normal);
            if (tangentDirection.sqrMagnitude < 0.0001f)
            {
                tangentDirection = Vector3.ProjectOnPlane(Vector3.forward, normal);
            }

            tangentDirection.Normalize();
            var tangent = new Vector4(tangentDirection.x, tangentDirection.y, tangentDirection.z, 1f);
            AddVertex(a, normal, tangent);
            AddVertex(b, normal, tangent);
            AddVertex(c, normal, tangent);
            var start = positions.Count - 3;
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }

        public Mesh CreateMesh(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.MarkDynamic();
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, materialParameters);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddVertex(in SurfaceVertex vertex, Vector3 normal, Vector4 tangent)
        {
            positions.Add(vertex.Position);
            normals.Add(normal);
            tangents.Add(tangent);
            uv0.Add(vertex.Uv);
            materialParameters.Add(new Vector4(
                vertex.Appearance.Metallic,
                vertex.Appearance.Smoothness,
                vertex.Appearance.Occlusion,
                0f));
            colors.Add(vertex.Appearance.Albedo);
        }
    }
}
