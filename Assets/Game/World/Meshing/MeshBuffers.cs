using System.Collections.Generic;
using System;
using MiniCivilization.World.Definitions;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Meshing
{
    internal sealed class WorldMeshBuildScratch
    {
        public MeshBuffers Terrain { get; } = new();
        public MeshBuffers Water { get; } = new();
        public List<ExposedCell> SolidCells { get; } = new();
        public List<ExposedCell> WaterCells { get; } = new();
        public HashSet<int> WaterCellIndices { get; } = new();
    }

    internal readonly struct SurfaceVertex
    {
        public readonly Vector3 Position;
        public readonly Vector2 Uv;
        public readonly SurfaceAppearance Appearance;
        public readonly Vector4 Flow;

        public SurfaceVertex(Vector3 position, Vector2 uv, in SurfaceAppearance appearance)
            : this(position, uv, appearance, Vector4.zero)
        {
        }

        public SurfaceVertex(
            Vector3 position,
            Vector2 uv,
            in SurfaceAppearance appearance,
            Vector4 flow)
        {
            Position = position;
            Uv = uv;
            Appearance = appearance;
            Flow = flow;
        }
    }

    internal sealed class MeshBuffers
    {
        private readonly List<Vector3> positions = new();
        private readonly List<Vector3> normals = new();
        private readonly List<Vector4> tangents = new();
        private readonly List<Vector2> uv0 = new();
        private readonly List<Vector4> materialParameters = new();
        private readonly List<Vector2> textureLayers = new();
        private readonly List<Vector2> textureWeights = new();
        private readonly List<Vector2> textureScales = new();
        private readonly List<Vector4> flowData = new();
        private readonly List<Color32> colors = new();
        private readonly List<int> indices = new();
        private readonly Dictionary<MeshVertexKey, int> vertexLookup = new();

        public int VertexCount => positions.Count;
        public int TriangleCount => indices.Count / 3;
        public bool IsEmpty => positions.Count == 0;
        public void Clear()
        {
            positions.Clear();
            normals.Clear();
            tangents.Clear();
            uv0.Clear();
            materialParameters.Clear();
            textureLayers.Clear();
            textureWeights.Clear();
            textureScales.Clear();
            flowData.Clear();
            colors.Clear();
            indices.Clear();
            vertexLookup.Clear();
        }

        internal void AddTriangle(in SurfaceVertex a, in SurfaceVertex b, in SurfaceVertex c)
        {
            var cross = Vector3.Cross(b.Position - a.Position, c.Position - a.Position);
            if (cross.sqrMagnitude < 0.0000001f)
            {
                return;
            }

            var normal = cross.normalized;
            var tangent = ResolveTangent(in a, in b, in c, normal);
            BuildTriangleTextureLayout(
                in a.Appearance,
                in b.Appearance,
                in c.Appearance,
                out var layers,
                out var scales,
                out var weightsA,
                out var weightsB,
                out var weightsC);
            indices.Add(AddVertex(a, normal, tangent, layers, weightsA, scales));
            indices.Add(AddVertex(b, normal, tangent, layers, weightsB, scales));
            indices.Add(AddVertex(c, normal, tangent, layers, weightsC, scales));
        }

        private static Vector4 ResolveTangent(
            in SurfaceVertex a,
            in SurfaceVertex b,
            in SurfaceVertex c,
            Vector3 normal)
        {
            var edge1 = b.Position - a.Position;
            var edge2 = c.Position - a.Position;
            var uvEdge1 = b.Uv - a.Uv;
            var uvEdge2 = c.Uv - a.Uv;
            var determinant = uvEdge1.x * uvEdge2.y
                - uvEdge1.y * uvEdge2.x;

            if (Mathf.Abs(determinant) > 0.000001f)
            {
                var inverse = 1f / determinant;
                var tangentDirection =
                    (edge1 * uvEdge2.y - edge2 * uvEdge1.y) * inverse;
                var bitangentDirection =
                    (edge2 * uvEdge1.x - edge1 * uvEdge2.x) * inverse;
                tangentDirection = Vector3.ProjectOnPlane(
                    tangentDirection,
                    normal);
                if (tangentDirection.sqrMagnitude > 0.000001f)
                {
                    tangentDirection.Normalize();
                    var handedness = Vector3.Dot(
                        Vector3.Cross(normal, tangentDirection),
                        bitangentDirection) < 0f
                            ? -1f
                            : 1f;
                    return new Vector4(
                        tangentDirection.x,
                        tangentDirection.y,
                        tangentDirection.z,
                        handedness);
                }
            }

            var fallback = Vector3.ProjectOnPlane(Vector3.right, normal);
            if (fallback.sqrMagnitude < 0.0001f)
            {
                fallback = Vector3.ProjectOnPlane(Vector3.forward, normal);
            }

            fallback.Normalize();
            return new Vector4(fallback.x, fallback.y, fallback.z, 1f);
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

        public Mesh CreateMesh(string name, Mesh reusableMesh = null)
        {
            var mesh = reusableMesh != null ? reusableMesh : new Mesh();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, materialParameters);
            mesh.SetUVs(2, textureLayers);
            mesh.SetUVs(3, textureWeights);
            mesh.SetUVs(4, textureScales);
            mesh.SetUVs(5, flowData);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        private int AddVertex(
            in SurfaceVertex vertex,
            Vector3 normal,
            Vector4 tangent,
            Vector2 layers,
            Vector2 weights,
            Vector2 scales)
        {
            var material = new Vector4(
                vertex.Appearance.Metallic,
                vertex.Appearance.Smoothness,
                vertex.Appearance.Occlusion,
                0f);
            var color = (Color32)vertex.Appearance.Albedo;
            var key = new MeshVertexKey(
                vertex.Position,
                normal,
                tangent,
                vertex.Uv,
                material,
                layers,
                weights,
                scales,
                vertex.Flow,
                color);
            if (vertexLookup.TryGetValue(key, out var existingIndex))
            {
                return existingIndex;
            }

            var index = positions.Count;
            vertexLookup.Add(key, index);
            positions.Add(vertex.Position);
            normals.Add(normal);
            tangents.Add(tangent);
            uv0.Add(vertex.Uv);
            materialParameters.Add(material);
            textureLayers.Add(layers);
            textureWeights.Add(weights);
            textureScales.Add(scales);
            flowData.Add(vertex.Flow);
            colors.Add(color);
            return index;
        }

        private static void BuildTriangleTextureLayout(
            in SurfaceAppearance a,
            in SurfaceAppearance b,
            in SurfaceAppearance c,
            out Vector2 layers,
            out Vector2 scales,
            out Vector2 weightsA,
            out Vector2 weightsB,
            out Vector2 weightsC)
        {
            var count = 1;
            if (!TryFindStrongestLayer(in a, in b, in c, false, 0f, out var firstLayer, out var firstScale))
            {
                firstLayer = 0f;
                firstScale = 1f;
            }

            var secondLayer = firstLayer;
            var secondScale = firstScale;
            if (TryFindStrongestLayer(
                    in a,
                    in b,
                    in c,
                    true,
                    firstLayer,
                    out var resolvedSecondLayer,
                    out var resolvedSecondScale))
            {
                count = 2;
                secondLayer = resolvedSecondLayer;
                secondScale = resolvedSecondScale;
            }

            layers = new Vector2(firstLayer, secondLayer);
            scales = new Vector2(firstScale, secondScale);
            weightsA = RemapWeights(in a, layers, count);
            weightsB = RemapWeights(in b, layers, count);
            weightsC = RemapWeights(in c, layers, count);
        }

        private static bool TryFindStrongestLayer(
            in SurfaceAppearance a,
            in SurfaceAppearance b,
            in SurfaceAppearance c,
            bool hasExcludedLayer,
            float excludedLayer,
            out float strongestLayer,
            out float strongestScale)
        {
            strongestLayer = 0f;
            strongestScale = 1f;
            var strongestWeight = -1f;
            ConsiderAppearanceLayers(
                in a, in a, in b, in c, hasExcludedLayer, excludedLayer,
                ref strongestLayer, ref strongestScale, ref strongestWeight);
            ConsiderAppearanceLayers(
                in b, in a, in b, in c, hasExcludedLayer, excludedLayer,
                ref strongestLayer, ref strongestScale, ref strongestWeight);
            ConsiderAppearanceLayers(
                in c, in a, in b, in c, hasExcludedLayer, excludedLayer,
                ref strongestLayer, ref strongestScale, ref strongestWeight);
            return strongestWeight >= 0f;
        }

        private static void ConsiderAppearanceLayers(
            in SurfaceAppearance appearance,
            in SurfaceAppearance a,
            in SurfaceAppearance b,
            in SurfaceAppearance c,
            bool hasExcludedLayer,
            float excludedLayer,
            ref float strongestLayer,
            ref float strongestScale,
            ref float strongestWeight)
        {
            for (var sourceIndex = 0; sourceIndex < 2; sourceIndex++)
            {
                var sourceWeight = appearance.GetWeight(sourceIndex);
                if (sourceWeight <= 0.00001f)
                {
                    continue;
                }

                var layer = appearance.GetLayer(sourceIndex);
                if (hasExcludedLayer && Mathf.Abs(layer - excludedLayer) < 0.001f)
                {
                    continue;
                }

                var aggregateWeight =
                    GetLayerWeight(in a, layer) +
                    GetLayerWeight(in b, layer) +
                    GetLayerWeight(in c, layer);
                if (aggregateWeight <= strongestWeight)
                {
                    continue;
                }

                strongestLayer = layer;
                strongestScale = appearance.GetScale(sourceIndex);
                strongestWeight = aggregateWeight;
            }
        }

        private static float GetLayerWeight(in SurfaceAppearance appearance, float layer)
        {
            var result = 0f;
            for (var sourceIndex = 0; sourceIndex < 2; sourceIndex++)
            {
                if (Mathf.Abs(appearance.GetLayer(sourceIndex) - layer) < 0.001f)
                {
                    result += appearance.GetWeight(sourceIndex);
                }
            }

            return result;
        }

        private static Vector2 RemapWeights(
            in SurfaceAppearance appearance,
            Vector2 targetLayers,
            int targetCount)
        {
            var result = Vector2.zero;
            for (var sourceIndex = 0; sourceIndex < 2; sourceIndex++)
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

            var sum = result.x + result.y;
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

        private static int FindLayer(Vector2 layers, int count, float layer)
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

        private readonly struct MeshVertexKey : IEquatable<MeshVertexKey>
        {
            private readonly Vector3 position;
            private readonly Vector3 normal;
            private readonly Vector4 tangent;
            private readonly Vector2 uv;
            private readonly Vector4 material;
            private readonly Vector2 layers;
            private readonly Vector2 weights;
            private readonly Vector2 scales;
            private readonly Vector4 flow;
            private readonly Color32 color;

            public MeshVertexKey(
                Vector3 position,
                Vector3 normal,
                Vector4 tangent,
                Vector2 uv,
                Vector4 material,
                Vector2 layers,
                Vector2 weights,
                Vector2 scales,
                Vector4 flow,
                Color32 color)
            {
                this.position = position;
                this.normal = normal;
                this.tangent = tangent;
                this.uv = uv;
                this.material = material;
                this.layers = layers;
                this.weights = weights;
                this.scales = scales;
                this.flow = flow;
                this.color = color;
            }

            public bool Equals(MeshVertexKey other)
            {
                return position.Equals(other.position)
                    && normal.Equals(other.normal)
                    && tangent.Equals(other.tangent)
                    && uv.Equals(other.uv)
                    && material.Equals(other.material)
                    && layers.Equals(other.layers)
                    && weights.Equals(other.weights)
                    && scales.Equals(other.scales)
                    && flow.Equals(other.flow)
                    && color.Equals(other.color);
            }

            public override bool Equals(object obj) => obj is MeshVertexKey other && Equals(other);

            public override int GetHashCode()
            {
                var geometryHash = HashCode.Combine(position, normal, tangent, uv);
                var surfaceHash = HashCode.Combine(
                    material,
                    layers,
                    weights,
                    scales);
                return HashCode.Combine(
                    geometryHash,
                    surfaceHash,
                    flow,
                    color);
            }
        }
    }
}
