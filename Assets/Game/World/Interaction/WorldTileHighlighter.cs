using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    public sealed class WorldTileHighlighter : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("References")]
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private WorldTileSelectionState selectionState;
        [SerializeField] private MeshFilter hoverFilter;
        [SerializeField] private MeshRenderer hoverRenderer;
        [SerializeField] private MeshFilter selectedFilter;
        [SerializeField] private MeshRenderer selectedRenderer;
        [SerializeField] private Material highlightMaterial;

        [Header("Colors")]
        [SerializeField] private Color hoverColor =
            new(0.1f, 0.9f, 1f, 0.32f);
        [SerializeField] private Color selectedColor =
            new(1f, 0.82f, 0.05f, 0.42f);

        private uint hoverGeometryVersion;
        private uint selectedGeometryVersion;

        private void OnEnable()
        {
            if (selectionState != null)
            {
                selectionState.HoverChanged += OnHoverChanged;
                selectionState.SelectionChanged += OnSelectionChanged;
            }

            InitializeRenderer(hoverRenderer, hoverColor);
            InitializeRenderer(selectedRenderer, selectedColor);
            RefreshAll();
        }

        private void OnDisable()
        {
            if (selectionState != null)
            {
                selectionState.HoverChanged -= OnHoverChanged;
                selectionState.SelectionChanged -= OnSelectionChanged;
            }
        }

        private void LateUpdate()
        {
            RefreshIfGeometryChanged(
                selectionState?.Hovered,
                ref hoverGeometryVersion,
                true);
            RefreshIfGeometryChanged(
                selectionState?.Selected,
                ref selectedGeometryVersion,
                false);
        }

        public void Configure(
            WorldManager manager,
            WorldTileSelectionState state,
            MeshFilter hoverMeshFilter,
            MeshRenderer hoverMeshRenderer,
            MeshFilter selectedMeshFilter,
            MeshRenderer selectedMeshRenderer,
            Material material)
        {
            worldManager = manager;
            selectionState = state;
            hoverFilter = hoverMeshFilter;
            hoverRenderer = hoverMeshRenderer;
            selectedFilter = selectedMeshFilter;
            selectedRenderer = selectedMeshRenderer;
            highlightMaterial = material;
        }

        private void OnHoverChanged(TilePickResult? _)
        {
            RebuildHover();
        }

        private void OnSelectionChanged(TilePickResult? _)
        {
            RebuildSelected();
            RebuildHover();
        }

        private void RefreshAll()
        {
            RebuildSelected();
            RebuildHover();
        }

        private void RebuildHover()
        {
            var hovered = selectionState?.Hovered;
            if (hovered.HasValue
                && selectionState.Selected.HasValue
                && hovered.Value.CellIndex == selectionState.Selected.Value.CellIndex
                && IsSameSurfaceGroup(
                    hovered.Value.SurfaceType,
                    selectionState.Selected.Value.SurfaceType))
            {
                hovered = null;
            }

            Rebuild(hovered, hoverFilter, hoverRenderer, ref hoverGeometryVersion);
        }

        private void RebuildSelected()
        {
            Rebuild(
                selectionState?.Selected,
                selectedFilter,
                selectedRenderer,
                ref selectedGeometryVersion);
        }

        private void Rebuild(
            TilePickResult? pick,
            MeshFilter targetFilter,
            MeshRenderer targetRenderer,
            ref uint cachedVersion)
        {
            if (targetFilter == null || targetRenderer == null)
            {
                return;
            }

            if (!pick.HasValue
                || pick.Value.Surface == null
                || pick.Value.Surface.InteractionMesh == null
                || worldManager == null
                || !worldManager.HasWorld)
            {
                targetRenderer.enabled = false;
                cachedVersion = 0;
                return;
            }

            var sourceSurface = pick.Value.Surface;
            var sourceMesh = sourceSurface.InteractionMesh;
            var sourceVertices = sourceMesh.vertices;
            var sourceTriangles = sourceMesh.triangles;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var sourceTransform = sourceSurface.transform;
            var targetTransform = targetFilter.transform;
            var world = worldManager.CurrentWorldData;
            var clipA = new Vector3[8];
            var clipB = new Vector3[8];

            for (var triangleIndex = 0;
                 triangleIndex < sourceTriangles.Length / 3;
                 triangleIndex++)
            {
                if (!sourceSurface.TryResolveMetadata(
                        triangleIndex,
                        out var metadata)
                    || !IsSameSurfaceGroup(
                        pick.Value.SurfaceType,
                        metadata.SurfaceType))
                {
                    continue;
                }

                var sourceStart = triangleIndex * 3;
                var a = sourceVertices[sourceTriangles[sourceStart]];
                var b = sourceVertices[sourceTriangles[sourceStart + 1]];
                var c = sourceVertices[sourceTriangles[sourceStart + 2]];
                if (metadata.SurfaceType == SurfaceInteractionType.Terrain
                    && metadata.Role == SurfaceTriangleRole.Cliff)
                {
                    var owner = WorldCellIndex.Decode(
                        world,
                        metadata.OwnerCellIndex);
                    if (owner.X != pick.Value.Cell.X
                        || owner.Z != pick.Value.Cell.Z)
                    {
                        continue;
                    }

                    var cell = world.GetCell(
                        pick.Value.Cell.X,
                        pick.Value.Cell.Y,
                        pick.Value.Cell.Z);
                    var minimumHeight = pick.Value.Cell.Y;
                    var maximumHeight = minimumHeight
                        + cell.SolidFill * WorldGrid.HeightStep;
                    clipA[0] = a;
                    clipA[1] = b;
                    clipA[2] = c;
                    var clippedCount = ClipByHeight(
                        clipA,
                        3,
                        clipB,
                        minimumHeight,
                        true);
                    clippedCount = ClipByHeight(
                        clipB,
                        clippedCount,
                        clipA,
                        maximumHeight,
                        false);
                    for (var vertexIndex = 1;
                         vertexIndex < clippedCount - 1;
                         vertexIndex++)
                    {
                        AppendTriangle(
                            clipA[0],
                            clipA[vertexIndex],
                            clipA[vertexIndex + 1],
                            sourceTransform,
                            targetTransform,
                            vertices,
                            triangles);
                    }

                    continue;
                }

                if (metadata.OwnerCellIndex != pick.Value.CellIndex)
                {
                    continue;
                }

                AppendTriangle(
                    a,
                    b,
                    c,
                    sourceTransform,
                    targetTransform,
                    vertices,
                    triangles);
            }

            var targetMesh = targetFilter.sharedMesh;
            if (targetMesh == null)
            {
                targetMesh = new Mesh { name = targetFilter.name + " Mesh" };
                targetFilter.sharedMesh = targetMesh;
            }

            targetMesh.Clear();
            targetMesh.SetVertices(vertices);
            targetMesh.SetTriangles(triangles, 0, true);
            if (triangles.Count > 0)
            {
                targetMesh.RecalculateNormals();
                targetMesh.RecalculateBounds();
            }

            targetRenderer.enabled = triangles.Count > 0;
            cachedVersion = sourceSurface.GeometryVersion;
        }

        private static void AppendTriangle(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Transform sourceTransform,
            Transform targetTransform,
            List<Vector3> vertices,
            List<int> triangles)
        {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 0.0000001f)
            {
                return;
            }

            var targetStart = vertices.Count;
            AddTransformedVertex(
                a,
                sourceTransform,
                targetTransform,
                vertices);
            AddTransformedVertex(
                b,
                sourceTransform,
                targetTransform,
                vertices);
            AddTransformedVertex(
                c,
                sourceTransform,
                targetTransform,
                vertices);
            triangles.Add(targetStart);
            triangles.Add(targetStart + 1);
            triangles.Add(targetStart + 2);
        }

        private static void AddTransformedVertex(
            Vector3 sourcePosition,
            Transform sourceTransform,
            Transform targetTransform,
            List<Vector3> vertices)
        {
            vertices.Add(targetTransform.InverseTransformPoint(
                sourceTransform.TransformPoint(sourcePosition)));
        }

        private static int ClipByHeight(
            Vector3[] input,
            int inputCount,
            Vector3[] output,
            float boundary,
            bool keepAbove)
        {
            if (inputCount == 0)
            {
                return 0;
            }

            const float epsilon = 0.00001f;
            var outputCount = 0;
            var previous = input[inputCount - 1];
            var previousInside = keepAbove
                ? previous.y >= boundary - epsilon
                : previous.y <= boundary + epsilon;
            for (var index = 0; index < inputCount; index++)
            {
                var current = input[index];
                var currentInside = keepAbove
                    ? current.y >= boundary - epsilon
                    : current.y <= boundary + epsilon;
                if (currentInside != previousInside)
                {
                    var heightDelta = current.y - previous.y;
                    var t = Mathf.Abs(heightDelta) <= epsilon
                        ? 0f
                        : (boundary - previous.y) / heightDelta;
                    output[outputCount++] = Vector3.LerpUnclamped(
                        previous,
                        current,
                        t);
                }

                if (currentInside)
                {
                    output[outputCount++] = current;
                }

                previous = current;
                previousInside = currentInside;
            }

            return outputCount;
        }

        private static bool IsSameSurfaceGroup(
            SurfaceInteractionType selected,
            SurfaceInteractionType candidate)
        {
            if (selected == SurfaceInteractionType.Terrain)
            {
                return candidate == SurfaceInteractionType.Terrain;
            }

            return candidate == SurfaceInteractionType.Water
                || candidate == SurfaceInteractionType.Waterfall;
        }

        private void RefreshIfGeometryChanged(
            TilePickResult? pick,
            ref uint cachedVersion,
            bool hover)
        {
            if (!pick.HasValue || pick.Value.Surface == null)
            {
                return;
            }

            if (cachedVersion != pick.Value.Surface.GeometryVersion)
            {
                if (hover) RebuildHover();
                else RebuildSelected();
            }
        }

        private void InitializeRenderer(MeshRenderer target, Color color)
        {
            if (target == null)
            {
                return;
            }

            target.sharedMaterial = highlightMaterial;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            ReleaseMesh(hoverFilter);
            ReleaseMesh(selectedFilter);
        }

        private static void ReleaseMesh(MeshFilter filter)
        {
            if (filter == null || filter.sharedMesh == null)
            {
                return;
            }

            var mesh = filter.sharedMesh;
            filter.sharedMesh = null;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
