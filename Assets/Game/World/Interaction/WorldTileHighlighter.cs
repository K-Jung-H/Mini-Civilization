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
        private static readonly int StencilComparisonId =
            Shader.PropertyToID("_StencilComparison");
        private static readonly int StencilPassId =
            Shader.PropertyToID("_StencilPass");

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
        private readonly List<Vector3> outlineVertices = new();
        private readonly List<int> outlineIndices = new();
        private readonly HashSet<CellEdge> outlineEdges = new();

        private WorldTileSelectionState subscribedState;
        private WorldManager subscribedManager;
        private Mesh unitCubeMesh;
        private Mesh primaryOutlineMesh;
        private Mesh secondaryOutlineMesh;
        private Mesh invalidOutlineMesh;
        private Material runtimeMaterial;
        private Material runtimeMaterialSource;
        private Material runtimeOutlineMaterial;
        private Material runtimeOutlineMaterialSource;
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

            EnsureOutlineMeshes();

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
                RebuildSelectionOutline(
                    selectionState.EditPrimaryPreview,
                    world,
                    primaryOutlineMesh);
                RebuildSelectionOutline(
                    selectionState.EditSecondaryPreview,
                    world,
                    secondaryOutlineMesh);
                RebuildSelectionOutline(
                    selectionState.EditInvalidPreview,
                    world,
                    invalidOutlineMesh);
                activeColor = editSelectedColor;
            }
            else if (selectionState?.EditHovered != null)
            {
                activeColor = editHoverColor;
                AppendSelection(
                    selectionState.EditHovered,
                    world,
                    primaryMatrices);
                RebuildSelectionOutline(
                    selectionState.EditHovered,
                    world,
                    primaryOutlineMesh);
            }
            else if (selectionState?.EditSelected != null)
            {
                activeColor = editSelectedColor;
                AppendSelection(
                    selectionState.EditSelected,
                    world,
                    primaryMatrices);
                RebuildSelectionOutline(
                    selectionState.EditSelected,
                    world,
                    primaryOutlineMesh);
            }
            else if (selectionState?.Selected != null)
            {
                activeColor = selectedColor;
                AppendCell(
                    selectionState.Selected.Value.Cell,
                    world.CellSize,
                    primaryMatrices);
                RebuildCellOutline(
                    selectionState.Selected.Value.Cell,
                    world.CellSize,
                    primaryOutlineMesh);
            }
            else if (selectionState?.Hovered != null)
            {
                activeColor = hoverColor;
                AppendCell(
                    selectionState.Hovered.Value.Cell,
                    world.CellSize,
                    primaryMatrices);
                RebuildCellOutline(
                    selectionState.Hovered.Value.Cell,
                    world.CellSize,
                    primaryOutlineMesh);
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
            var paddedSize = size + Vector3.one * (cellPadding * 2f);
            target.Add(
                GetRenderRootMatrix() * Matrix4x4.TRS(
                    center,
                    Quaternion.identity,
                    paddedSize));
        }

        private void RebuildSelectionOutline(
            IWorldCellSelection selection,
            WorldData world,
            Mesh target)
        {
            if (target == null)
            {
                return;
            }

            selectedCells.Clear();
            selection?.CopyCellsTo(selectedCells, world);
            RebuildOutlineMesh(selectedCells, world.CellSize, target);
        }

        private void RebuildCellOutline(
            CellCoordinate coordinate,
            float cellSize,
            Mesh target)
        {
            if (target == null)
            {
                return;
            }

            selectedCells.Clear();
            selectedCells.Add(coordinate);
            RebuildOutlineMesh(selectedCells, cellSize, target);
        }

        private void RebuildOutlineMesh(
            List<CellCoordinate> cells,
            float cellSize,
            Mesh target)
        {
            outlineEdges.Clear();
            outlineVertices.Clear();
            outlineIndices.Clear();

            for (var index = 0; index < cells.Count; index++)
            {
                AddCellOutlineEdges(cells[index]);
            }

            var rootMatrix = GetRenderRootMatrix();
            foreach (var edge in outlineEdges)
            {
                AddOutlineVertex(edge.First, cellSize, rootMatrix);
                AddOutlineVertex(edge.Second, cellSize, rootMatrix);
            }

            target.Clear();
            if (outlineVertices.Count == 0)
            {
                return;
            }

            target.SetVertices(outlineVertices);
            target.SetIndices(outlineIndices, MeshTopology.Lines, 0, false);
            target.RecalculateBounds();
        }

        private void AddCellOutlineEdges(CellCoordinate coordinate)
        {
            var minimum = new Vector3Int(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            var maximum = minimum + Vector3Int.one;
            var bottomBackLeft = minimum;
            var bottomBackRight = new Vector3Int(maximum.x, minimum.y, minimum.z);
            var bottomFrontLeft = new Vector3Int(minimum.x, minimum.y, maximum.z);
            var bottomFrontRight = new Vector3Int(maximum.x, minimum.y, maximum.z);
            var topBackLeft = new Vector3Int(minimum.x, maximum.y, minimum.z);
            var topBackRight = new Vector3Int(maximum.x, maximum.y, minimum.z);
            var topFrontLeft = new Vector3Int(minimum.x, maximum.y, maximum.z);
            var topFrontRight = maximum;

            AddOutlineEdge(bottomBackLeft, bottomBackRight);
            AddOutlineEdge(bottomBackRight, bottomFrontRight);
            AddOutlineEdge(bottomFrontRight, bottomFrontLeft);
            AddOutlineEdge(bottomFrontLeft, bottomBackLeft);
            AddOutlineEdge(topBackLeft, topBackRight);
            AddOutlineEdge(topBackRight, topFrontRight);
            AddOutlineEdge(topFrontRight, topFrontLeft);
            AddOutlineEdge(topFrontLeft, topBackLeft);
            AddOutlineEdge(bottomBackLeft, topBackLeft);
            AddOutlineEdge(bottomBackRight, topBackRight);
            AddOutlineEdge(bottomFrontRight, topFrontRight);
            AddOutlineEdge(bottomFrontLeft, topFrontLeft);
        }

        private void AddOutlineEdge(Vector3Int first, Vector3Int second)
        {
            outlineEdges.Add(new CellEdge(first, second));
        }

        private void AddOutlineVertex(
            Vector3Int point,
            float cellSize,
            Matrix4x4 rootMatrix)
        {
            outlineVertices.Add(rootMatrix.MultiplyPoint3x4(
                new Vector3(point.x * cellSize, point.y * cellSize, point.z * cellSize)));
            outlineIndices.Add(outlineIndices.Count);
        }

        private Matrix4x4 GetRenderRootMatrix()
        {
            return worldManager?.Renderer?.RenderRoot != null
                ? worldManager.Renderer.RenderRoot.localToWorldMatrix
                : Matrix4x4.identity;
        }

        private void RenderInstances()
        {
            if (!Application.isPlaying || !EnsureRuntimeMaterial())
            {
                return;
            }

            if (primaryMatrices.Count > 0
                || secondaryMatrices.Count > 0
                || invalidMatrices.Count > 0)
            {
                EnsureUnitCube();
                RenderBatch(primaryMatrices, activeColor);
                RenderBatch(secondaryMatrices, editSecondaryColor);
                RenderBatch(invalidMatrices, editInvalidColor);
            }

            RenderOutlines();
        }

        private void RenderOutlines()
        {
            if (!EnsureRuntimeOutlineMaterial())
            {
                return;
            }

            RenderOutline(primaryOutlineMesh, activeColor);
            RenderOutline(secondaryOutlineMesh, editSecondaryColor);
            RenderOutline(invalidOutlineMesh, editInvalidColor);
        }

        private void RenderOutline(Mesh mesh, Color color)
        {
            if (mesh == null || mesh.vertexCount == 0)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            propertyBlock.Clear();
            color.a = 1f;
            propertyBlock.SetColor(BaseColorId, color);
            var renderParams = new RenderParams(runtimeOutlineMaterial)
            {
                layer = gameObject.layer,
                matProps = propertyBlock,
                worldBounds = mesh.bounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderMesh(renderParams, mesh, 0, Matrix4x4.identity);
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
            ClearOutlineMeshes();
        }

        private void EnsureOutlineMeshes()
        {
            primaryOutlineMesh ??= CreateOutlineMesh("World Highlight Primary Outline");
            secondaryOutlineMesh ??= CreateOutlineMesh("World Highlight Secondary Outline");
            invalidOutlineMesh ??= CreateOutlineMesh("World Highlight Invalid Outline");
        }

        private static Mesh CreateOutlineMesh(string name)
        {
            return new Mesh
            {
                name = name,
                indexFormat = IndexFormat.UInt32,
                hideFlags = HideFlags.DontSave
            };
        }

        private void ClearOutlineMeshes()
        {
            ClearOutlineMesh(primaryOutlineMesh);
            ClearOutlineMesh(secondaryOutlineMesh);
            ClearOutlineMesh(invalidOutlineMesh);
        }

        private static void ClearOutlineMesh(Mesh mesh)
        {
            if (mesh != null)
            {
                mesh.Clear();
            }
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

        private bool EnsureRuntimeOutlineMaterial()
        {
            if (highlightMaterial == null)
            {
                return false;
            }

            if (runtimeOutlineMaterial != null
                && runtimeOutlineMaterialSource == highlightMaterial)
            {
                return true;
            }

            if (runtimeOutlineMaterial != null)
            {
                Destroy(runtimeOutlineMaterial);
            }

            runtimeOutlineMaterialSource = highlightMaterial;
            runtimeOutlineMaterial = new Material(highlightMaterial)
            {
                name = $"{highlightMaterial.name} (Runtime Outline)",
                hideFlags = HideFlags.DontSave
            };
            runtimeOutlineMaterial.SetFloat(
                StencilComparisonId,
                (float)CompareFunction.Always);
            runtimeOutlineMaterial.SetFloat(
                StencilPassId,
                (float)StencilOp.Keep);
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

        private readonly struct CellEdge : System.IEquatable<CellEdge>
        {
            public CellEdge(Vector3Int first, Vector3Int second)
            {
                if (Compare(first, second) <= 0)
                {
                    First = first;
                    Second = second;
                }
                else
                {
                    First = second;
                    Second = first;
                }
            }

            public Vector3Int First { get; }
            public Vector3Int Second { get; }

            public bool Equals(CellEdge other)
            {
                return First == other.First && Second == other.Second;
            }

            public override bool Equals(object obj)
            {
                return obj is CellEdge other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (First.GetHashCode() * 397) ^ Second.GetHashCode();
                }
            }

            private static int Compare(Vector3Int first, Vector3Int second)
            {
                var x = first.x.CompareTo(second.x);
                if (x != 0)
                {
                    return x;
                }

                var y = first.y.CompareTo(second.y);
                return y != 0 ? y : first.z.CompareTo(second.z);
            }
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

            DestroyOutlineMesh(ref primaryOutlineMesh);
            DestroyOutlineMesh(ref secondaryOutlineMesh);
            DestroyOutlineMesh(ref invalidOutlineMesh);

            if (runtimeOutlineMaterial != null)
            {
                Destroy(runtimeOutlineMaterial);
                runtimeOutlineMaterial = null;
                runtimeOutlineMaterialSource = null;
            }
        }

        private static void DestroyOutlineMesh(ref Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            Destroy(mesh);
            mesh = null;
        }
    }
}
