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
        [SerializeField] private Color editSecondaryColor =
            new(0.08f, 0.72f, 1f, 0.48f);
        [SerializeField] private Color editInvalidColor =
            new(1f, 0.12f, 0.08f, 0.52f);

        private readonly List<CellCoordinate> selectedCells = new();
        private readonly List<Matrix4x4> primaryMatrices = new();
        private readonly List<Matrix4x4> secondaryMatrices = new();
        private readonly List<Matrix4x4> invalidMatrices = new();

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
                subscribedState.EditPreviewChanged += OnEditPreviewChanged;
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
                subscribedState.EditPreviewChanged -= OnEditPreviewChanged;
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

        private void OnEditPreviewChanged() => instancesDirty = true;

        private void OnWorldChanged(WorldDataAsset _)
        {
            ClearMatrices();
            selectionState?.Clear();
            instancesDirty = true;
        }

        private void RebuildInstances()
        {
            instancesDirty = false;
            ClearMatrices();
            if (!TryGetWorld(out var world))
            {
                return;
            }

            if (selectionState?.EditPrimaryPreview != null
                || selectionState?.EditSecondaryPreview != null
                || selectionState?.EditInvalidPreview != null)
            {
                AppendSelection(
                    selectionState.EditPrimaryPreview,
                    world,
                    primaryMatrices);
                AppendSelection(
                    selectionState.EditSecondaryPreview,
                    world,
                    secondaryMatrices);
                AppendSelection(
                    selectionState.EditInvalidPreview,
                    world,
                    invalidMatrices);
                activeColor = editSelectedColor;
            }
            else if (selectionState?.EditHovered != null)
            {
                activeColor = editHoverColor;
                AppendSelection(
                    selectionState.EditHovered,
                    world,
                    primaryMatrices);
            }
            else if (selectionState?.EditSelected != null)
            {
                activeColor = editSelectedColor;
                AppendSelection(
                    selectionState.EditSelected,
                    world,
                    primaryMatrices);
            }
            else if (selectionState?.Selected != null)
            {
                activeColor = selectedColor;
                AppendCell(
                    selectionState.Selected.Value.Cell,
                    world.CellSize,
                    primaryMatrices);
            }
            else if (selectionState?.Hovered != null)
            {
                activeColor = hoverColor;
                AppendCell(
                    selectionState.Hovered.Value.Cell,
                    world.CellSize,
                    primaryMatrices);
            }

            RecalculateInstanceBounds();
        }

        private void AppendSelection(
            IWorldCellSelection selection,
            WorldData world,
            List<Matrix4x4> target)
        {
            if (selection == null)
            {
                return;
            }

            if (selection is WorldCellBoxSelection box)
            {
                AppendBox(box.Bounds, world.CellSize, target);
                return;
            }

            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, world);
            for (var index = 0; index < selectedCells.Count; index++)
            {
                AppendCell(selectedCells[index], world.CellSize, target);
            }
        }

        private void AppendCell(
            CellCoordinate coordinate,
            float cellSize,
            List<Matrix4x4> target)
        {
            AppendLocalBox(
                new Vector3(
                    (coordinate.X + 0.5f) * cellSize,
                    (coordinate.Y + 0.5f) * cellSize,
                    (coordinate.Z + 0.5f) * cellSize),
                Vector3.one * cellSize,
                target);
        }

        private void AppendBox(
            in CellBounds bounds,
            float cellSize,
            List<Matrix4x4> target)
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
            AppendLocalBox(center, size, target);
        }

        private void AppendLocalBox(
            Vector3 center,
            Vector3 size,
            List<Matrix4x4> target)
        {
            var rootMatrix = Matrix4x4.identity;
            if (worldManager?.Renderer?.RenderRoot != null)
            {
                rootMatrix = worldManager.Renderer.RenderRoot.localToWorldMatrix;
            }

            var paddedSize = size + Vector3.one * (cellPadding * 2f);
            target.Add(
                rootMatrix * Matrix4x4.TRS(
                    center,
                    Quaternion.identity,
                    paddedSize));
        }

        private void RenderInstances()
        {
            if ((primaryMatrices.Count == 0
                && secondaryMatrices.Count == 0
                && invalidMatrices.Count == 0)
                || !EnsureRuntimeMaterial()
                || !Application.isPlaying)
            {
                return;
            }

            EnsureUnitCube();
            RenderBatch(primaryMatrices, activeColor);
            RenderBatch(secondaryMatrices, editSecondaryColor);
            RenderBatch(invalidMatrices, editInvalidColor);
        }

        private void RenderBatch(List<Matrix4x4> matrices, Color color)
        {
            if (matrices.Count == 0)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            propertyBlock.SetColor(BaseColorId, color);
            var renderParams = new RenderParams(runtimeMaterial)
            {
                layer = gameObject.layer,
                matProps = propertyBlock,
                worldBounds = instanceBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };

            for (var start = 0; start < matrices.Count;
                 start += MaxInstancesPerDraw)
            {
                var count = Mathf.Min(
                    MaxInstancesPerDraw,
                    matrices.Count - start);
                Graphics.RenderMeshInstanced(
                    renderParams,
                    unitCubeMesh,
                    0,
                    matrices,
                    count,
                    start);
            }
        }

        private void RecalculateInstanceBounds()
        {
            if (primaryMatrices.Count == 0
                && secondaryMatrices.Count == 0
                && invalidMatrices.Count == 0)
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
            EncapsulateMatrices(primaryMatrices, ref minimum, ref maximum);
            EncapsulateMatrices(secondaryMatrices, ref minimum, ref maximum);
            EncapsulateMatrices(invalidMatrices, ref minimum, ref maximum);

            instanceBounds = new Bounds(
                (minimum + maximum) * 0.5f,
                maximum - minimum);
        }

        private static void EncapsulateMatrices(
            List<Matrix4x4> matrices,
            ref Vector3 minimum,
            ref Vector3 maximum)
        {
            for (var matrixIndex = 0;
                 matrixIndex < matrices.Count;
                 matrixIndex++)
            {
                var matrix = matrices[matrixIndex];
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

        }

        private void ClearMatrices()
        {
            primaryMatrices.Clear();
            secondaryMatrices.Clear();
            invalidMatrices.Clear();
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
