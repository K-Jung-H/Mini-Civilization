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
        private sealed class SourceGeometry
        {
            public Mesh Mesh;
            public uint Version;
            public Vector3[] Vertices;
            public int[] Triangles;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("References")]
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private WorldTileSelectionState selectionState;
        [SerializeField] private MeshFilter highlightFilter;
        [SerializeField] private MeshRenderer highlightRenderer;
        [SerializeField] private Material highlightMaterial;

        [Header("Colors")]
        [SerializeField] private Color hoverColor =
            new(0.1f, 0.9f, 1f, 0.32f);
        [SerializeField] private Color selectedColor =
            new(1f, 0.82f, 0.05f, 0.42f);
        [SerializeField] private Color editHoverColor =
            new(0.08f, 1f, 0.22f, 0.3f);
        [SerializeField] private Color editSelectedColor =
            new(0.05f, 0.9f, 0.16f, 0.48f);

        private readonly List<Vector3> buildVertices = new();
        private readonly List<int> buildTriangles = new();
        private readonly List<CellCoordinate> selectedCells = new();
        private readonly Vector3[] clipA = new Vector3[8];
        private readonly Vector3[] clipB = new Vector3[8];
        private readonly Dictionary<WorldChunkInteractionSurface, SourceGeometry>
            sourceGeometry = new();
        private readonly Dictionary<WorldChunkInteractionSurface, uint>
            activeGeometryVersions = new();

        private WorldTileSelectionState subscribedState;
        private WorldManager subscribedManager;
        private TilePickResult? activeSingle;
        private IWorldCellSelection activeMulti;
        private uint activeSingleGeometryVersion;
        private Mesh runtimeHighlightMesh;
        private MaterialPropertyBlock propertyBlock;

        private void OnEnable()
        {
            Subscribe();
            InitializeRenderer();
            RebuildActive();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            if (activeSingle.HasValue
                && (activeSingle.Value.Surface == null
                    || activeSingleGeometryVersion
                        != activeSingle.Value.Surface.GeometryVersion))
            {
                RebuildActive();
                return;
            }

            if (activeMulti == null)
            {
                return;
            }

            foreach (var pair in activeGeometryVersions)
            {
                if (pair.Key == null || pair.Key.GeometryVersion != pair.Value)
                {
                    RebuildActive();
                    return;
                }
            }
        }

        public void Configure(
            WorldManager manager,
            WorldTileSelectionState state,
            MeshFilter meshFilter,
            MeshRenderer meshRenderer,
            Material material)
        {
            Unsubscribe();
            worldManager = manager;
            selectionState = state;
            highlightFilter = meshFilter;
            highlightRenderer = meshRenderer;
            highlightMaterial = material;
            Subscribe();
            InitializeRenderer();
            RebuildActive();
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
            RebuildActive();

        private void OnEditStateChanged(IWorldCellSelection _) =>
            RebuildActive();

        private void OnWorldChanged(WorldDataAsset _)
        {
            ClearGeometryCache();
            selectionState?.Clear();
        }

        private void RebuildActive()
        {
            activeSingle = null;
            activeMulti = null;
            activeSingleGeometryVersion = 0;
            activeGeometryVersions.Clear();

            if (selectionState?.EditHovered != null)
            {
                activeMulti = selectionState.EditHovered;
                ApplyColor(editHoverColor);
                RebuildMulti(
                    activeMulti,
                    highlightFilter,
                    highlightRenderer,
                    activeGeometryVersions);
                return;
            }

            if (selectionState?.EditSelected != null)
            {
                activeMulti = selectionState.EditSelected;
                ApplyColor(editSelectedColor);
                RebuildMulti(
                    activeMulti,
                    highlightFilter,
                    highlightRenderer,
                    activeGeometryVersions);
                return;
            }

            if (selectionState?.Selected != null)
            {
                activeSingle = selectionState.Selected;
                ApplyColor(selectedColor);
                RebuildSingle(
                    activeSingle,
                    highlightFilter,
                    highlightRenderer,
                    ref activeSingleGeometryVersion);
                return;
            }

            if (selectionState?.Hovered != null)
            {
                activeSingle = selectionState.Hovered;
                ApplyColor(hoverColor);
                RebuildSingle(
                    activeSingle,
                    highlightFilter,
                    highlightRenderer,
                    ref activeSingleGeometryVersion);
                return;
            }

            BeginBuild();
            CompleteBuild(highlightFilter, highlightRenderer);
        }

        private void RebuildSingle(
            TilePickResult? pick,
            MeshFilter targetFilter,
            MeshRenderer targetRenderer,
            ref uint cachedVersion)
        {
            BeginBuild();
            if (!pick.HasValue
                || pick.Value.Surface == null
                || !TryGetWorld(out var world))
            {
                CompleteBuild(targetFilter, targetRenderer);
                cachedVersion = 0;
                return;
            }

            var source = pick.Value.Surface;
            var ownerCellIndex = pick.Value.CellIndex;
            if (pick.Value.SurfaceType == SurfaceInteractionType.Terrain)
            {
                var column = world.GetSurfaceColumn(
                    pick.Value.Cell.X,
                    pick.Value.Cell.Z);
                if (!column.HasSurface)
                {
                    CompleteBuild(targetFilter, targetRenderer);
                    cachedVersion = source.GeometryVersion;
                    return;
                }

                ownerCellIndex = WorldCellIndex.Encode(
                    world,
                    pick.Value.Cell.X,
                    column.SurfaceCellY,
                    pick.Value.Cell.Z);
            }

            if (source.TryGetOwnedTriangleIndices(
                    ownerCellIndex,
                    out var ownedTriangles))
            {
                var geometry = GetSourceGeometry(source);
                for (var index = 0; index < ownedTriangles.Length; index++)
                {
                    var triangleIndex = ownedTriangles[index];
                    if (!source.TryResolveMetadata(
                            triangleIndex,
                            out var metadata)
                        || !IsSameSurfaceGroup(
                            pick.Value.SurfaceType,
                            metadata.SurfaceType))
                    {
                        continue;
                    }

                    if (metadata.SurfaceType == SurfaceInteractionType.Terrain
                        && metadata.Role == SurfaceTriangleRole.Cliff)
                    {
                        AppendCliffCell(
                            source,
                            geometry,
                            triangleIndex,
                            pick.Value.Cell,
                            world,
                            targetFilter.transform);
                    }
                    else if (metadata.OwnerCellIndex == pick.Value.CellIndex)
                    {
                        AppendSourceTriangle(
                            source,
                            geometry,
                            triangleIndex,
                            targetFilter.transform);
                    }
                }
            }

            CompleteBuild(targetFilter, targetRenderer);
            cachedVersion = source.GeometryVersion;
        }

        private void RebuildMulti(
            IWorldCellSelection selection,
            MeshFilter targetFilter,
            MeshRenderer targetRenderer,
            Dictionary<WorldChunkInteractionSurface, uint> versions)
        {
            BeginBuild();
            versions.Clear();
            if (selection == null
                || targetFilter == null
                || !TryGetWorld(out var world)
                || worldManager.Renderer == null)
            {
                CompleteBuild(targetFilter, targetRenderer);
                return;
            }

            if (selection is WorldCellSetSelection)
            {
                selectedCells.Clear();
                selection.CopyCellsTo(selectedCells, world);
                foreach (var coordinate in selectedCells)
                {
                    AppendSparseCell(
                        world,
                        coordinate,
                        targetFilter.transform,
                        versions);
                }
            }
            else
            {
                AppendDenseSelection(
                    selection,
                    world,
                    targetFilter.transform,
                    versions);
            }

            CompleteBuild(targetFilter, targetRenderer);
        }

        private void AppendSparseCell(
            WorldData world,
            CellCoordinate coordinate,
            Transform targetTransform,
            Dictionary<WorldChunkInteractionSurface, uint> versions)
        {
            if (!world.ContainsColumn(coordinate.X, coordinate.Z)
                || !worldManager.Renderer.TryGetInteractionSurface(
                    coordinate.X,
                    coordinate.Z,
                    out var source))
            {
                return;
            }

            versions[source] = source.GeometryVersion;
            var geometry = GetSourceGeometry(source);
            var column = world.GetSurfaceColumn(
                coordinate.X,
                coordinate.Z);
            AppendTerrainCell(
                world,
                coordinate,
                column,
                source,
                geometry,
                targetTransform);
            AppendWaterCell(
                world,
                coordinate,
                column,
                source,
                geometry,
                targetTransform);
        }

        private void AppendDenseSelection(
            IWorldCellSelection selection,
            WorldData world,
            Transform targetTransform,
            Dictionary<WorldChunkInteractionSurface, uint> versions)
        {
            var bounds = selection.Bounds;
            for (var z = bounds.Minimum.Z; z <= bounds.Maximum.Z; z++)
            for (var x = bounds.Minimum.X; x <= bounds.Maximum.X; x++)
            {
                if (!world.ContainsColumn(x, z)
                    || !worldManager.Renderer.TryGetInteractionSurface(
                        x,
                        z,
                        out var source))
                {
                    continue;
                }

                versions[source] = source.GeometryVersion;
                var geometry = GetSourceGeometry(source);
                var column = world.GetSurfaceColumn(x, z);
                AppendTerrainColumn(
                    selection,
                    world,
                    x,
                    z,
                    column,
                    source,
                    geometry,
                    targetTransform);
                AppendWaterColumn(
                    selection,
                    world,
                    x,
                    z,
                    column,
                    source,
                    geometry,
                    targetTransform);
            }
        }

        private void AppendTerrainCell(
            WorldData world,
            CellCoordinate coordinate,
            SurfaceColumnData column,
            WorldChunkInteractionSurface source,
            SourceGeometry geometry,
            Transform targetTransform)
        {
            if (!column.HasSurface)
            {
                return;
            }

            var owner = new CellCoordinate(
                coordinate.X,
                column.SurfaceCellY,
                coordinate.Z);
            var ownerIndex = WorldCellIndex.Encode(
                world,
                owner.X,
                owner.Y,
                owner.Z);
            if (!source.TryGetOwnedTriangleIndices(
                    ownerIndex,
                    out var ownedTriangles))
            {
                return;
            }

            for (var index = 0; index < ownedTriangles.Length; index++)
            {
                var triangleIndex = ownedTriangles[index];
                if (!source.TryResolveMetadata(
                        triangleIndex,
                        out var metadata)
                    || metadata.SurfaceType != SurfaceInteractionType.Terrain)
                {
                    continue;
                }

                if (metadata.Role != SurfaceTriangleRole.Cliff)
                {
                    if (coordinate.Y == column.SurfaceCellY)
                    {
                        AppendSourceTriangle(
                            source,
                            geometry,
                            triangleIndex,
                            targetTransform);
                    }

                    continue;
                }

                AppendCliffCell(
                    source,
                    geometry,
                    triangleIndex,
                    coordinate,
                    world,
                    targetTransform);
            }
        }

        private void AppendWaterCell(
            WorldData world,
            CellCoordinate coordinate,
            SurfaceColumnData column,
            WorldChunkInteractionSurface source,
            SourceGeometry geometry,
            Transform targetTransform)
        {
            if (!column.HasWater)
            {
                return;
            }

            if (coordinate.Y != column.WaterCellY)
            {
                return;
            }

            var owner = new CellCoordinate(
                coordinate.X,
                column.WaterCellY,
                coordinate.Z);
            var ownerIndex = WorldCellIndex.Encode(
                world,
                owner.X,
                owner.Y,
                owner.Z);
            if (!source.TryGetOwnedTriangleIndices(
                    ownerIndex,
                    out var ownedTriangles))
            {
                return;
            }

            for (var index = 0; index < ownedTriangles.Length; index++)
            {
                var triangleIndex = ownedTriangles[index];
                if (!source.TryResolveMetadata(
                        triangleIndex,
                        out var metadata)
                    || (metadata.SurfaceType != SurfaceInteractionType.Water
                        && metadata.SurfaceType
                            != SurfaceInteractionType.Waterfall))
                {
                    continue;
                }

                AppendSourceTriangle(
                    source,
                    geometry,
                    triangleIndex,
                    targetTransform);
            }
        }

        private void AppendTerrainColumn(
            IWorldCellSelection selection,
            WorldData world,
            int x,
            int z,
            SurfaceColumnData column,
            WorldChunkInteractionSurface source,
            SourceGeometry geometry,
            Transform targetTransform)
        {
            if (!column.HasSurface)
            {
                return;
            }

            var owner = new CellCoordinate(x, column.SurfaceCellY, z);
            var ownerIndex = WorldCellIndex.Encode(world, x, owner.Y, z);
            if (!source.TryGetOwnedTriangleIndices(
                    ownerIndex,
                    out var ownedTriangles))
            {
                return;
            }

            for (var index = 0; index < ownedTriangles.Length; index++)
            {
                var triangleIndex = ownedTriangles[index];
                if (!source.TryResolveMetadata(
                        triangleIndex,
                        out var metadata)
                    || metadata.SurfaceType != SurfaceInteractionType.Terrain)
                {
                    continue;
                }

                if (metadata.Role != SurfaceTriangleRole.Cliff)
                {
                    if (selection.Contains(ownerIndex, owner))
                    {
                        AppendSourceTriangle(
                            source,
                            geometry,
                            triangleIndex,
                            targetTransform);
                    }

                    continue;
                }

                for (var y = selection.Bounds.Minimum.Y;
                     y <= selection.Bounds.Maximum.Y;
                     y++)
                {
                    if (!world.Contains(x, y, z))
                    {
                        continue;
                    }

                    var coordinate = new CellCoordinate(x, y, z);
                    var cellIndex = WorldCellIndex.Encode(world, x, y, z);
                    if (selection.Contains(cellIndex, coordinate))
                    {
                        AppendCliffCell(
                            source,
                            geometry,
                            triangleIndex,
                            coordinate,
                            world,
                            targetTransform);
                    }
                }
            }
        }

        private void AppendWaterColumn(
            IWorldCellSelection selection,
            WorldData world,
            int x,
            int z,
            SurfaceColumnData column,
            WorldChunkInteractionSurface source,
            SourceGeometry geometry,
            Transform targetTransform)
        {
            if (!column.HasWater)
            {
                return;
            }

            var owner = new CellCoordinate(x, column.WaterCellY, z);
            var ownerIndex = WorldCellIndex.Encode(world, x, owner.Y, z);
            if (!selection.Contains(ownerIndex, owner)
                || !source.TryGetOwnedTriangleIndices(
                    ownerIndex,
                    out var ownedTriangles))
            {
                return;
            }

            for (var index = 0; index < ownedTriangles.Length; index++)
            {
                var triangleIndex = ownedTriangles[index];
                if (!source.TryResolveMetadata(
                        triangleIndex,
                        out var metadata)
                    || (metadata.SurfaceType != SurfaceInteractionType.Water
                        && metadata.SurfaceType
                            != SurfaceInteractionType.Waterfall))
                {
                    continue;
                }

                AppendSourceTriangle(
                    source,
                    geometry,
                    triangleIndex,
                    targetTransform);
            }
        }

        private void AppendCliffCell(
            WorldChunkInteractionSurface source,
            SourceGeometry geometry,
            int triangleIndex,
            CellCoordinate coordinate,
            WorldData world,
            Transform targetTransform)
        {
            var cell = world.GetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            if (!cell.HasSolid)
            {
                return;
            }

            ReadTriangle(
                geometry,
                triangleIndex,
                out var a,
                out var b,
                out var c);
            clipA[0] = a;
            clipA[1] = b;
            clipA[2] = c;
            var clippedCount = ClipByHeight(
                clipA,
                3,
                clipB,
                coordinate.Y,
                true);
            clippedCount = ClipByHeight(
                clipB,
                clippedCount,
                clipA,
                coordinate.Y + cell.SolidFill * WorldGrid.HeightStep,
                false);
            for (var vertexIndex = 1;
                 vertexIndex < clippedCount - 1;
                 vertexIndex++)
            {
                AppendTriangle(
                    clipA[0],
                    clipA[vertexIndex],
                    clipA[vertexIndex + 1],
                    source.transform,
                    targetTransform);
            }
        }

        private void AppendSourceTriangle(
            WorldChunkInteractionSurface source,
            SourceGeometry geometry,
            int triangleIndex,
            Transform targetTransform)
        {
            ReadTriangle(
                geometry,
                triangleIndex,
                out var a,
                out var b,
                out var c);
            AppendTriangle(
                a,
                b,
                c,
                source.transform,
                targetTransform);
        }

        private static void ReadTriangle(
            SourceGeometry geometry,
            int triangleIndex,
            out Vector3 a,
            out Vector3 b,
            out Vector3 c)
        {
            var start = triangleIndex * 3;
            a = geometry.Vertices[geometry.Triangles[start]];
            b = geometry.Vertices[geometry.Triangles[start + 1]];
            c = geometry.Vertices[geometry.Triangles[start + 2]];
        }

        private SourceGeometry GetSourceGeometry(
            WorldChunkInteractionSurface source)
        {
            var mesh = source.InteractionMesh;
            if (!sourceGeometry.TryGetValue(source, out var geometry)
                || geometry.Mesh != mesh
                || geometry.Version != source.GeometryVersion)
            {
                geometry = new SourceGeometry
                {
                    Mesh = mesh,
                    Version = source.GeometryVersion,
                    Vertices = mesh != null
                        ? mesh.vertices
                        : System.Array.Empty<Vector3>(),
                    Triangles = mesh != null
                        ? mesh.triangles
                        : System.Array.Empty<int>()
                };
                sourceGeometry[source] = geometry;
            }

            return geometry;
        }

        private void BeginBuild()
        {
            buildVertices.Clear();
            buildTriangles.Clear();
        }

        private void CompleteBuild(
            MeshFilter targetFilter,
            MeshRenderer targetRenderer)
        {
            if (targetFilter == null || targetRenderer == null)
            {
                return;
            }

            if (buildTriangles.Count == 0)
            {
                runtimeHighlightMesh?.Clear();
                targetRenderer.enabled = false;
                return;
            }

            if (!Application.isPlaying)
            {
                targetRenderer.enabled = false;
                return;
            }

            if (runtimeHighlightMesh == null)
            {
                runtimeHighlightMesh = new Mesh
                {
                    name = "World Highlight Mesh"
                };
                runtimeHighlightMesh.MarkDynamic();
            }

            if (targetFilter.sharedMesh != runtimeHighlightMesh)
            {
                targetFilter.sharedMesh = runtimeHighlightMesh;
            }

            runtimeHighlightMesh.Clear();
            runtimeHighlightMesh.indexFormat = buildVertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            runtimeHighlightMesh.SetVertices(buildVertices);
            runtimeHighlightMesh.SetTriangles(buildTriangles, 0, true);
            runtimeHighlightMesh.RecalculateBounds();

            targetRenderer.enabled = true;
        }

        private void AppendTriangle(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Transform sourceTransform,
            Transform targetTransform)
        {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude < 0.0000001f)
            {
                return;
            }

            var targetStart = buildVertices.Count;
            buildVertices.Add(targetTransform.InverseTransformPoint(
                sourceTransform.TransformPoint(a)));
            buildVertices.Add(targetTransform.InverseTransformPoint(
                sourceTransform.TransformPoint(b)));
            buildVertices.Add(targetTransform.InverseTransformPoint(
                sourceTransform.TransformPoint(c)));
            buildTriangles.Add(targetStart);
            buildTriangles.Add(targetStart + 1);
            buildTriangles.Add(targetStart + 2);
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

        private bool TryGetWorld(out WorldData world)
        {
            world = worldManager != null
                ? worldManager.CurrentWorldData
                : null;
            return world != null;
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

        private void InitializeRenderer()
        {
            if (highlightRenderer == null)
            {
                return;
            }

            highlightRenderer.sharedMaterial = highlightMaterial;
            highlightRenderer.shadowCastingMode = ShadowCastingMode.Off;
            highlightRenderer.receiveShadows = false;
            highlightRenderer.enabled = false;
        }

        private void ApplyColor(Color color)
        {
            if (highlightRenderer == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            highlightRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            highlightRenderer.SetPropertyBlock(propertyBlock);
        }

        private void ClearGeometryCache()
        {
            sourceGeometry.Clear();
            activeGeometryVersions.Clear();
            activeSingle = null;
            activeMulti = null;
            activeSingleGeometryVersion = 0;
            BeginBuild();
            if (runtimeHighlightMesh != null)
            {
                runtimeHighlightMesh.Clear();
            }

            if (highlightRenderer != null)
            {
                highlightRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ClearGeometryCache();
            if (highlightFilter != null
                && highlightFilter.sharedMesh == runtimeHighlightMesh)
            {
                highlightFilter.sharedMesh = null;
            }

            if (runtimeHighlightMesh != null)
            {
                Destroy(runtimeHighlightMesh);
                runtimeHighlightMesh = null;
            }
        }
    }
}
