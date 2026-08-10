using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    public sealed class WorldTileHighlighter : MonoBehaviour
    {
        private const int MaxInstancesPerDraw = 1023;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("References")]
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private WorldTileSelectionState selectionState;
        [SerializeField] private Material highlightMaterial;

        [Header("Shape")]
        [SerializeField, Min(0f)] private float cellPadding = 0.005f;

        [Header("Colors")]
        [SerializeField] private Color hoverColor =
            new(0.1f, 0.9f, 1f, 0.32f);
        [SerializeField] private Color selectedColor =
            new(1f, 0.82f, 0.05f, 0.42f);
        [SerializeField] private Color editHoverColor =
            new(0.08f, 1f, 0.22f, 0.3f);
        [SerializeField] private Color editSelectedColor =
            new(0.05f, 0.9f, 0.16f, 0.48f);

        private readonly List<CellCoordinate> selectedCells = new();
        private readonly List<Matrix4x4> instanceMatrices = new();

        private WorldTileSelectionState subscribedState;
        private WorldManager subscribedManager;
        private Mesh unitCubeMesh;
        private Material runtimeMaterial;
        private Material runtimeMaterialSource;
        private MaterialPropertyBlock propertyBlock;
        private Bounds instanceBounds;
        private Color activeColor;
        private bool instancesDirty = true;

        private void OnEnable()
        {
            Subscribe();
            instancesDirty = true;
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (instancesDirty)
            {
                RebuildInstances();
            }

            RenderInstances();
        }

        public void Configure(
            WorldManager manager,
            WorldTileSelectionState state,
            Material material)
        {
            Unsubscribe();
            worldManager = manager;
            selectionState = state;
            highlightMaterial = material;
            Subscribe();
            instancesDirty = true;
        }

        private void Subscribe()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (selectionState != null && subscribedState != selectionState)
            {
                subscribedState = selectionState;
                subscribedState.HoverChanged += OnSelectionStateChanged;
                subscribedState.SelectionChanged += OnSelectionStateChanged;
                subscribedState.EditHoverChanged += OnEditStateChanged;
                subscribedState.EditSelectionChanged += OnEditStateChanged;
            }

            if (worldManager != null && subscribedManager != worldManager)
            {
                subscribedManager = worldManager;
                subscribedManager.WorldChanged += OnWorldChanged;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedState != null)
            {
                subscribedState.HoverChanged -= OnSelectionStateChanged;
                subscribedState.SelectionChanged -= OnSelectionStateChanged;
                subscribedState.EditHoverChanged -= OnEditStateChanged;
                subscribedState.EditSelectionChanged -= OnEditStateChanged;
                subscribedState = null;
            }

            if (subscribedManager != null)
            {
                subscribedManager.WorldChanged -= OnWorldChanged;
                subscribedManager = null;
            }
        }

        private void OnSelectionStateChanged(TilePickResult? _) =>
            instancesDirty = true;

        private void OnEditStateChanged(IWorldCellSelection _) =>
            instancesDirty = true;

        private void OnWorldChanged(WorldDataAsset _)
        {
            instanceMatrices.Clear();
            selectionState?.Clear();
            instancesDirty = true;
        }

        private void RebuildInstances()
        {
            instancesDirty = false;
            instanceMatrices.Clear();
            if (!TryGetWorld(out var world))
            {
                return;
            }

            if (selectionState?.EditHovered != null)
            {
                activeColor = editHoverColor;
                AppendSelection(selectionState.EditHovered, world);
            }
            else if (selectionState?.EditSelected != null)
            {
                activeColor = editSelectedColor;
                AppendSelection(selectionState.EditSelected, world);
            }
            else if (selectionState?.Selected != null)
            {
                activeColor = selectedColor;
                AppendCell(selectionState.Selected.Value.Cell, world.CellSize);
            }
            else if (selectionState?.Hovered != null)
            {
                activeColor = hoverColor;
                AppendCell(selectionState.Hovered.Value.Cell, world.CellSize);
            }

            RecalculateInstanceBounds();
        }

        private void AppendSelection(
            IWorldCellSelection selection,
            WorldData world)
        {
            if (selection is WorldCellBoxSelection box)
            {
                AppendBox(box.Bounds, world.CellSize);
                return;
            }

            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, world);
            for (var index = 0; index < selectedCells.Count; index++)
            {
                AppendCell(selectedCells[index], world.CellSize);
            }
        }

        private void AppendCell(CellCoordinate coordinate, float cellSize)
        {
            AppendLocalBox(
                new Vector3(
                    (coordinate.X + 0.5f) * cellSize,
                    (coordinate.Y + 0.5f) * cellSize,
                    (coordinate.Z + 0.5f) * cellSize),
                Vector3.one * cellSize);
        }

        private void AppendBox(in CellBounds bounds, float cellSize)
        {
            var size = new Vector3(
                bounds.Maximum.X - bounds.Minimum.X + 1,
                bounds.Maximum.Y - bounds.Minimum.Y + 1,
                bounds.Maximum.Z - bounds.Minimum.Z + 1) * cellSize;
            var center = new Vector3(
                (bounds.Minimum.X + bounds.Maximum.X + 1) * 0.5f,
                (bounds.Minimum.Y + bounds.Maximum.Y + 1) * 0.5f,
                (bounds.Minimum.Z + bounds.Maximum.Z + 1) * 0.5f)
                * cellSize;
            AppendLocalBox(center, size);
        }

        private void AppendLocalBox(Vector3 center, Vector3 size)
        {
            var rootMatrix = Matrix4x4.identity;
            if (worldManager?.Renderer?.RenderRoot != null)
            {
                rootMatrix = worldManager.Renderer.RenderRoot.localToWorldMatrix;
            }

            var paddedSize = size + Vector3.one * (cellPadding * 2f);
            instanceMatrices.Add(
                rootMatrix * Matrix4x4.TRS(
                    center,
                    Quaternion.identity,
                    paddedSize));
        }

        private void RenderInstances()
        {
            if (instanceMatrices.Count == 0
                || !EnsureRuntimeMaterial()
                || !Application.isPlaying)
            {
                return;
            }

            EnsureUnitCube();
            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetColor(BaseColorId, activeColor);

            var renderParams = new RenderParams(runtimeMaterial)
            {
                layer = gameObject.layer,
                matProps = propertyBlock,
                worldBounds = instanceBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };

            for (var start = 0; start < instanceMatrices.Count;
                 start += MaxInstancesPerDraw)
            {
                var count = Mathf.Min(
                    MaxInstancesPerDraw,
                    instanceMatrices.Count - start);
                Graphics.RenderMeshInstanced(
                    renderParams,
                    unitCubeMesh,
                    0,
                    instanceMatrices,
                    count,
                    start);
            }
        }

        private void RecalculateInstanceBounds()
        {
            if (instanceMatrices.Count == 0)
            {
                instanceBounds = default;
                return;
            }

            var minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            var maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            for (var matrixIndex = 0;
                 matrixIndex < instanceMatrices.Count;
                 matrixIndex++)
            {
                var matrix = instanceMatrices[matrixIndex];
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3(
                        (corner & 1) == 0 ? -0.5f : 0.5f,
                        (corner & 2) == 0 ? -0.5f : 0.5f,
                        (corner & 4) == 0 ? -0.5f : 0.5f));
                    minimum = Vector3.Min(minimum, point);
                    maximum = Vector3.Max(maximum, point);
                }
            }

            instanceBounds = new Bounds(
                (minimum + maximum) * 0.5f,
                maximum - minimum);
        }

        private bool TryGetWorld(out WorldData world)
        {
            world = worldManager != null
                ? worldManager.CurrentWorldData
                : null;
            return world != null;
        }

        private void EnsureUnitCube()
        {
            if (unitCubeMesh != null)
            {
                return;
            }

            var vertices = new List<Vector3>(24);
            var triangles = new List<int>(36);
            AddFace(vertices, triangles,
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f));
            AddFace(vertices, triangles,
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f));
            AddFace(vertices, triangles,
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f));
            AddFace(vertices, triangles,
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f));
            AddFace(vertices, triangles,
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f));
            AddFace(vertices, triangles,
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f));

            unitCubeMesh = new Mesh
            {
                name = "World Highlight Unit Cube",
                hideFlags = HideFlags.DontSave
            };
            unitCubeMesh.SetVertices(vertices);
            unitCubeMesh.SetTriangles(triangles, 0, true);
            unitCubeMesh.RecalculateBounds();
        }

        private bool EnsureRuntimeMaterial()
        {
            if (highlightMaterial == null)
            {
                return false;
            }

            if (runtimeMaterial != null
                && runtimeMaterialSource == highlightMaterial)
            {
                return true;
            }

            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }

            runtimeMaterialSource = highlightMaterial;
            runtimeMaterial = new Material(highlightMaterial)
            {
                name = $"{highlightMaterial.name} (Runtime Instanced)",
                enableInstancing = true,
                hideFlags = HideFlags.DontSave
            };
            return true;
        }

        private static void AddFace(
            List<Vector3> vertices,
            List<int> triangles,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            var start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (unitCubeMesh != null)
            {
                Destroy(unitCubeMesh);
                unitCubeMesh = null;
            }

            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
                runtimeMaterial = null;
                runtimeMaterialSource = null;
            }
        }
    }
}
