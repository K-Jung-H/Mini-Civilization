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
        private readonly List<Vector4> textureLayers = new();
        private readonly List<Vector4> textureWeights = new();
        private readonly List<Vector4> textureScales = new();
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
            BuildTriangleTextureLayout(
                in a.Appearance,
                in b.Appearance,
                in c.Appearance,
                out var layers,
                out var scales,
                out var weightsA,
                out var weightsB,
                out var weightsC);
            AddVertex(a, normal, tangent, layers, weightsA, scales);
            AddVertex(b, normal, tangent, layers, weightsB, scales);
            AddVertex(c, normal, tangent, layers, weightsC, scales);
            var start = positions.Count - 3;
            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
        }

        internal void AddTriangleFacing(
            in SurfaceVertex a,
            in SurfaceVertex b,
            in SurfaceVertex c,
            Vector3 expectedNormal)
        {
            var normal = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (Vector3.Dot(normal, expectedNormal) >= 0f)
            {
                AddTriangle(a, b, c);
            }
            else
            {
                AddTriangle(a, c, b);
            }
        }

        internal void AddQuadFacing(
            in SurfaceVertex topA,
            in SurfaceVertex topB,
            in SurfaceVertex bottomA,
            in SurfaceVertex bottomB,
            Vector3 expectedNormal)
        {
            AddTriangleFacing(bottomA, topA, topB, expectedNormal);
            AddTriangleFacing(bottomA, topB, bottomB, expectedNormal);
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
            mesh.SetUVs(2, textureLayers);
            mesh.SetUVs(3, textureWeights);
            mesh.SetUVs(4, textureScales);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddVertex(
            in SurfaceVertex vertex,
            Vector3 normal,
            Vector4 tangent,
            Vector4 layers,
            Vector4 weights,
            Vector4 scales)
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
            textureLayers.Add(layers);
            textureWeights.Add(weights);
            textureScales.Add(scales);
            colors.Add(vertex.Appearance.Albedo);
        }

        private static void BuildTriangleTextureLayout(
            in SurfaceAppearance a,
            in SurfaceAppearance b,
            in SurfaceAppearance c,
            out Vector4 layers,
            out Vector4 scales,
            out Vector4 weightsA,
            out Vector4 weightsB,
            out Vector4 weightsC)
        {
            layers = Vector4.zero;
            scales = Vector4.one;
            var count = 0;
            AddLayoutLayers(in a, ref layers, ref scales, ref count);
            AddLayoutLayers(in b, ref layers, ref scales, ref count);
            AddLayoutLayers(in c, ref layers, ref scales, ref count);
            weightsA = RemapWeights(in a, layers, count);
            weightsB = RemapWeights(in b, layers, count);
            weightsC = RemapWeights(in c, layers, count);
        }

        private static void AddLayoutLayers(
            in SurfaceAppearance appearance,
            ref Vector4 layers,
            ref Vector4 scales,
            ref int count)
        {
            for (var sourceIndex = 0; sourceIndex < 4 && count < 4; sourceIndex++)
            {
                if (appearance.GetWeight(sourceIndex) <= 0.00001f)
                {
                    continue;
                }

                var layer = appearance.GetLayer(sourceIndex);
                if (FindLayer(layers, count, layer) >= 0)
                {
                    continue;
                }

                layers[count] = layer;
                scales[count] = appearance.GetScale(sourceIndex);
                count++;
            }
        }

        private static Vector4 RemapWeights(
            in SurfaceAppearance appearance,
            Vector4 targetLayers,
            int targetCount)
        {
            var result = Vector4.zero;
            for (var sourceIndex = 0; sourceIndex < 4; sourceIndex++)
            {
                var sourceWeight = appearance.GetWeight(sourceIndex);
                if (sourceWeight <= 0.00001f)
                {
                    continue;
                }

                var targetIndex = FindLayer(
                    targetLayers,
                    targetCount,
                    appearance.GetLayer(sourceIndex));
                if (targetIndex >= 0)
                {
                    result[targetIndex] += sourceWeight;
                }
            }

            var sum = result.x + result.y + result.z + result.w;
            if (sum <= 0.00001f)
            {
                result.x = 1f;
            }
            else
            {
                result /= sum;
            }

            return result;
        }

        private static int FindLayer(Vector4 layers, int count, float layer)
        {
            for (var i = 0; i < count; i++)
            {
                if (Mathf.Abs(layers[i] - layer) < 0.001f)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
