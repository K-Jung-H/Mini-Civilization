using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldEditWireframeRenderer : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("References")]
        [SerializeField] private WorldTileSelectionState selectionState;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;
        [SerializeField] private Material wireframeMaterial;

        [Header("Appearance")]
        [SerializeField, Min(1f)] private float targetPixelThickness = 3f;
        [SerializeField, Min(0.005f)] private float minimumEdgeThickness = 0.025f;
        [SerializeField, Min(0.005f)] private float maximumEdgeThickness = 0.16f;
        [SerializeField, Min(0f)] private float boundsPadding = 0.015f;
        [SerializeField] private Color wireframeColor =
            new(0.05f, 1f, 0.18f, 0.98f);

        private readonly List<Vector3> vertices = new(96);
        private readonly List<int> triangles = new(432);
        private Mesh wireframeMesh;
        private WorldTileSelectionState subscribedState;
        private CellBounds currentBounds;
        private bool hasBounds;
        private float currentThickness;

        private void OnEnable()
        {
            Subscribe();
            InitializeRenderer();
            RefreshSelection(selectionState?.EditHovered);
        }

        private void OnDisable()
        {
            Unsubscribe();
            Hide();
        }

        private void LateUpdate()
        {
            if (!hasBounds || viewCamera == null)
            {
                return;
            }

            var nextThickness = ResolveEdgeThickness(currentBounds);
            if (Mathf.Abs(nextThickness - currentThickness)
                <= Mathf.Max(0.001f, currentThickness * 0.08f))
            {
                return;
            }

            BuildWireframe(currentBounds, nextThickness);
        }

        public void Configure(
            WorldTileSelectionState state,
            Camera camera,
            MeshFilter filter,
            MeshRenderer targetRenderer,
            Material material)
        {
            Unsubscribe();
            selectionState = state;
            viewCamera = camera;
            meshFilter = filter;
            meshRenderer = targetRenderer;
            wireframeMaterial = material;
            Subscribe();
            InitializeRenderer();
            RefreshSelection(selectionState?.EditHovered);
        }

        public void Show(CellBounds bounds)
        {
            if (meshFilter == null || meshRenderer == null)
            {
                return;
            }

            EnsureMesh();
            hasBounds = true;
            currentBounds = bounds;
            BuildWireframe(bounds, ResolveEdgeThickness(bounds));
            meshRenderer.enabled = true;
        }

        public void Hide()
        {
            hasBounds = false;
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }
        }

        private void InitializeRenderer()
        {
            if (meshRenderer == null)
            {
                return;
            }

            meshRenderer.sharedMaterial = wireframeMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, wireframeColor);
            meshRenderer.SetPropertyBlock(properties);
        }

        private void EnsureMesh()
        {
            if (wireframeMesh != null)
            {
                return;
            }

            wireframeMesh = new Mesh
            {
                name = "World Edit Selection Wireframe"
            };
            wireframeMesh.MarkDynamic();
            meshFilter.sharedMesh = wireframeMesh;
        }

        private void BuildWireframe(CellBounds bounds, float edgeThickness)
        {
            vertices.Clear();
            triangles.Clear();

            var padding = Mathf.Max(0f, boundsPadding);
            var minimum = new Vector3(
                bounds.Minimum.X - padding,
                bounds.Minimum.Y - padding,
                bounds.Minimum.Z - padding);
            var maximum = new Vector3(
                bounds.Maximum.X + 1f + padding,
                bounds.Maximum.Y + 1f + padding,
                bounds.Maximum.Z + 1f + padding);
            var halfThickness = Mathf.Max(0.005f, edgeThickness) * 0.5f;
            currentThickness = edgeThickness;

            AddXAxisEdges(minimum, maximum, halfThickness);
            AddYAxisEdges(minimum, maximum, halfThickness);
            AddZAxisEdges(minimum, maximum, halfThickness);

            wireframeMesh.Clear();
            wireframeMesh.SetVertices(vertices);
            wireframeMesh.SetTriangles(triangles, 0, true);
            wireframeMesh.RecalculateBounds();
        }

        private float ResolveEdgeThickness(CellBounds bounds)
        {
            if (viewCamera == null || viewCamera.pixelHeight <= 0)
            {
                return Mathf.Clamp(
                    minimumEdgeThickness,
                    minimumEdgeThickness,
                    maximumEdgeThickness);
            }

            float worldHeight;
            if (viewCamera.orthographic)
            {
                worldHeight = viewCamera.orthographicSize * 2f;
            }
            else
            {
                var center = new Vector3(
                    (bounds.Minimum.X + bounds.Maximum.X + 1f) * 0.5f,
                    (bounds.Minimum.Y + bounds.Maximum.Y + 1f) * 0.5f,
                    (bounds.Minimum.Z + bounds.Maximum.Z + 1f) * 0.5f);
                var distance = Mathf.Max(
                    viewCamera.nearClipPlane,
                    Vector3.Distance(viewCamera.transform.position, center));
                worldHeight = 2f * distance * Mathf.Tan(
                    viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }

            var desired = worldHeight
                / viewCamera.pixelHeight
                * Mathf.Max(1f, targetPixelThickness);
            return Mathf.Clamp(
                desired,
                Mathf.Max(0.005f, minimumEdgeThickness),
                Mathf.Max(minimumEdgeThickness, maximumEdgeThickness));
        }

        private void Subscribe()
        {
            if (!isActiveAndEnabled
                || selectionState == null
                || subscribedState == selectionState)
            {
                return;
            }

            Unsubscribe();
            subscribedState = selectionState;
            subscribedState.EditHoverChanged += RefreshSelection;
        }

        private void Unsubscribe()
        {
            if (subscribedState == null)
            {
                return;
            }

            subscribedState.EditHoverChanged -= RefreshSelection;
            subscribedState = null;
        }

        private void RefreshSelection(IWorldCellSelection selection)
        {
            if (selection == null || selection is WorldCellSetSelection)
            {
                Hide();
                return;
            }

            Show(selection.Bounds);
        }

        private void AddXAxisEdges(
            Vector3 minimum,
            Vector3 maximum,
            float halfThickness)
        {
            for (var y = 0; y < 2; y++)
            for (var z = 0; z < 2; z++)
            {
                var edgeY = y == 0 ? minimum.y : maximum.y;
                var edgeZ = z == 0 ? minimum.z : maximum.z;
                AddBox(
                    new Vector3(
                        minimum.x,
                        edgeY - halfThickness,
                        edgeZ - halfThickness),
                    new Vector3(
                        maximum.x,
                        edgeY + halfThickness,
                        edgeZ + halfThickness));
            }
        }

        private void AddYAxisEdges(
            Vector3 minimum,
            Vector3 maximum,
            float halfThickness)
        {
            for (var x = 0; x < 2; x++)
            for (var z = 0; z < 2; z++)
            {
                var edgeX = x == 0 ? minimum.x : maximum.x;
                var edgeZ = z == 0 ? minimum.z : maximum.z;
                AddBox(
                    new Vector3(
                        edgeX - halfThickness,
                        minimum.y,
                        edgeZ - halfThickness),
                    new Vector3(
                        edgeX + halfThickness,
                        maximum.y,
                        edgeZ + halfThickness));
            }
        }

        private void AddZAxisEdges(
            Vector3 minimum,
            Vector3 maximum,
            float halfThickness)
        {
            for (var x = 0; x < 2; x++)
            for (var y = 0; y < 2; y++)
            {
                var edgeX = x == 0 ? minimum.x : maximum.x;
                var edgeY = y == 0 ? minimum.y : maximum.y;
                AddBox(
                    new Vector3(
                        edgeX - halfThickness,
                        edgeY - halfThickness,
                        minimum.z),
                    new Vector3(
                        edgeX + halfThickness,
                        edgeY + halfThickness,
                        maximum.z));
            }
        }

        private void AddBox(Vector3 minimum, Vector3 maximum)
        {
            var start = vertices.Count;
            vertices.Add(new Vector3(minimum.x, minimum.y, minimum.z));
            vertices.Add(new Vector3(maximum.x, minimum.y, minimum.z));
            vertices.Add(new Vector3(maximum.x, maximum.y, minimum.z));
            vertices.Add(new Vector3(minimum.x, maximum.y, minimum.z));
            vertices.Add(new Vector3(minimum.x, minimum.y, maximum.z));
            vertices.Add(new Vector3(maximum.x, minimum.y, maximum.z));
            vertices.Add(new Vector3(maximum.x, maximum.y, maximum.z));
            vertices.Add(new Vector3(minimum.x, maximum.y, maximum.z));

            AddQuad(start, 0, 2, 1, 3);
            AddQuad(start, 4, 5, 6, 7);
            AddQuad(start, 0, 1, 5, 4);
            AddQuad(start, 3, 7, 6, 2);
            AddQuad(start, 0, 4, 7, 3);
            AddQuad(start, 1, 2, 6, 5);
        }

        private void AddQuad(
            int start,
            int a,
            int b,
            int c,
            int d)
        {
            triangles.Add(start + a);
            triangles.Add(start + b);
            triangles.Add(start + c);
            triangles.Add(start + a);
            triangles.Add(start + d);
            triangles.Add(start + b);
        }

        private void OnDestroy()
        {
            if (wireframeMesh == null)
            {
                return;
            }

            if (meshFilter != null)
            {
                meshFilter.sharedMesh = null;
            }

            if (Application.isPlaying)
            {
                Destroy(wireframeMesh);
            }
            else
            {
                DestroyImmediate(wireframeMesh);
            }
        }
    }
}
