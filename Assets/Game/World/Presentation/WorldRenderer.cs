using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Meshing;
using MiniCivilization.World.WaterFlow;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    public enum WorldRenderBindingMode : byte
    {
        None,
        PreparedScene,
        RuntimeGenerated
    }

    public sealed class WorldRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private WorldSurfaceCatalog surfaceCatalog;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Transform renderRoot;

        [Header("Mesh")]
        [SerializeField, Min(1)] private int renderPatchSizeXZ = 32;

        private readonly HashSet<Vector2Int> pendingGeometryPatches = new();
        private readonly HashSet<Vector2Int> pendingMaterialPatches = new();
        private readonly WorldMeshBuildScratch meshBuildScratch = new();
        private WorldChunkView[,] chunkViews;
        private WorldData boundWorld;
        private WaterFlowState boundWaterFlowState;
        private WorldExposureCache exposureCache;
        private WorldSurfaceQuery surfaceQuery;
        private int activeRenderPatchSize;

        public WorldData BoundWorld => boundWorld;
        public WorldRenderBindingMode BindingMode { get; private set; }
        public WorldChangeId LastAppliedChangeId { get; private set; }
        public int ActiveRenderPatchSize => activeRenderPatchSize;
        public Transform RenderRoot => renderRoot;
        internal WorldSurfaceQuery SurfaceQuery => surfaceQuery;
        public int RenderedPatchCount => chunkViews == null
            ? 0
            : chunkViews.GetLength(0) * chunkViews.GetLength(1);

        private void LateUpdate()
        {
            RebuildPendingPatches();
        }

        public void BuildRuntimeWorld(
            WorldData world,
            WaterFlowState waterFlowState)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            ValidateReferences();
            Unbind();

            boundWorld = world;
            boundWaterFlowState = waterFlowState;
            exposureCache = new WorldExposureCache(world);
            surfaceQuery = new WorldSurfaceQuery(
                world,
                waterFlowState);
            activeRenderPatchSize = ResolveRenderPatchSize(world);
            BindingMode = WorldRenderBindingMode.RuntimeGenerated;
            LastAppliedChangeId = world.CurrentChangeId;
            BuildAllPatches(persistentSceneObjects: false);
        }

        public void PrepareWorldInScene(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            ValidateReferences();
            Unbind();
            boundWorld = world;
            exposureCache = new WorldExposureCache(world);
            surfaceQuery = new WorldSurfaceQuery(world);
            activeRenderPatchSize = ResolveRenderPatchSize(world);
            BindingMode = WorldRenderBindingMode.PreparedScene;
            LastAppliedChangeId = world.CurrentChangeId;
            BuildAllPatches(persistentSceneObjects: true);
        }

        public bool TryAdoptPreparedWorld(
            WorldData world,
            int preparedPatchSize,
            int preparedPatchCount,
            WaterFlowState waterFlowState)
        {
            if (world == null
                || preparedPatchSize <= 0
                || preparedPatchCount <= 0)
            {
                return false;
            }

            ValidateReferences();
            DetachBoundWorld(clearViews: false);
            var countPerAxis = world.Size / preparedPatchSize;
            if (world.Size % preparedPatchSize != 0
                || checked(countPerAxis * countPerAxis) != preparedPatchCount)
            {
                return false;
            }

            var preparedViews = renderRoot.GetComponentsInChildren<WorldChunkView>(
                includeInactive: true);
            if (preparedViews.Length != preparedPatchCount)
            {
                return false;
            }

            var adoptedViews = new WorldChunkView[countPerAxis, countPerAxis];
            for (var index = 0; index < preparedViews.Length; index++)
            {
                var view = preparedViews[index];
                if (view.PatchSize != preparedPatchSize
                    || (uint)view.PatchX >= countPerAxis
                    || (uint)view.PatchZ >= countPerAxis
                    || adoptedViews[view.PatchX, view.PatchZ] != null
                    || !view.AdoptPrepared())
                {
                    return false;
                }

                adoptedViews[view.PatchX, view.PatchZ] = view;
            }

            boundWorld = world;
            boundWaterFlowState = waterFlowState;
            exposureCache = new WorldExposureCache(world);
            surfaceQuery = new WorldSurfaceQuery(
                world,
                waterFlowState);
            activeRenderPatchSize = preparedPatchSize;
            chunkViews = adoptedViews;
            BindingMode = WorldRenderBindingMode.PreparedScene;
            LastAppliedChangeId = world.CurrentChangeId;
            return true;
        }

        public void SetWaterFlowState(WaterFlowState waterFlowState)
        {
            if (ReferenceEquals(boundWaterFlowState, waterFlowState))
            {
                return;
            }

            boundWaterFlowState = waterFlowState;
            surfaceQuery?.SetWaterFlowState(waterFlowState);
        }

        public void ApplyChanges(WorldChangeSet changeSet)
        {
            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            if (changeSet.World != boundWorld)
            {
                throw new InvalidOperationException(
                    "The change set belongs to a different world.");
            }

            if (changeSet.ChangeId <= LastAppliedChangeId)
            {
                return;
            }

            const WorldChangeType geometryChanges =
                WorldChangeType.CellStructure
                | WorldChangeType.Surface;
            const WorldChangeType materialChanges =
                WorldChangeType.Material
                | WorldChangeType.Environment;
            var rebuildSharedGeometry =
                (changeSet.ChangeTypes & geometryChanges) != 0
                ||
                (changeSet.ChangeTypes
                    & (WorldChangeType.WaterTopology
                        | WorldChangeType.WaterSurface)) != 0;
            var rebuildMaterials =
                (changeSet.ChangeTypes & materialChanges) != 0;
            if (rebuildSharedGeometry)
            {
                exposureCache?.ApplyChanges(changeSet);
                surfaceQuery?.InvalidateRegion(changeSet.AffectedBounds);
            }

            if ((rebuildSharedGeometry || rebuildMaterials)
                && activeRenderPatchSize > 0)
            {
                for (var index = 0;
                     index < changeSet.AffectedChunks.Count;
                     index++)
                {
                    var coordinate = changeSet.AffectedChunks[index];
                    var startX = coordinate.X * boundWorld.ChunkSizeX;
                    var startZ = coordinate.Z * boundWorld.ChunkSizeZ;
                    var patch = new Vector2Int(
                        startX / activeRenderPatchSize,
                        startZ / activeRenderPatchSize);
                    if (rebuildSharedGeometry)
                    {
                        pendingGeometryPatches.Add(patch);
                    }
                    else
                    {
                        pendingMaterialPatches.Add(patch);
                    }
                }
            }

            LastAppliedChangeId = changeSet.ChangeId;
        }

        public void RebuildPendingPatches()
        {
            if (boundWorld == null
                || chunkViews == null
                || (pendingGeometryPatches.Count == 0
                    && pendingMaterialPatches.Count == 0))
            {
                return;
            }

            foreach (var patch in pendingGeometryPatches)
            {
                if ((uint)patch.x >= chunkViews.GetLength(0)
                    || (uint)patch.y >= chunkViews.GetLength(1))
                {
                    continue;
                }

                BuildPatch(
                    chunkViews[patch.x, patch.y],
                    patch.x,
                    patch.y);
            }

            foreach (var patch in pendingMaterialPatches)
            {
                if (pendingGeometryPatches.Contains(patch)
                    || (uint)patch.x >= chunkViews.GetLength(0)
                    || (uint)patch.y >= chunkViews.GetLength(1))
                {
                    continue;
                }

                BuildPatch(
                    chunkViews[patch.x, patch.y],
                    patch.x,
                    patch.y);
            }

            pendingGeometryPatches.Clear();
            pendingMaterialPatches.Clear();
        }

        public void Unbind()
        {
            DetachBoundWorld(clearViews: true);
        }

        public IEnumerable<WorldChunkView> EnumerateChunkViews()
        {
            if (chunkViews == null)
            {
                yield break;
            }

            for (var z = 0; z < chunkViews.GetLength(1); z++)
            for (var x = 0; x < chunkViews.GetLength(0); x++)
            {
                if (chunkViews[x, z] != null)
                {
                    yield return chunkViews[x, z];
                }
            }
        }

        private void DetachBoundWorld(bool clearViews)
        {
            boundWorld = null;
            boundWaterFlowState = null;
            exposureCache = null;
            surfaceQuery = null;
            activeRenderPatchSize = 0;
            BindingMode = WorldRenderBindingMode.None;
            LastAppliedChangeId = WorldChangeId.None;
            pendingGeometryPatches.Clear();
            pendingMaterialPatches.Clear();
            if (clearViews)
            {
                ClearViews();
            }
            else
            {
                chunkViews = null;
            }
        }

        public void Configure(
            WorldSurfaceCatalog catalog,
            Material terrain,
            Material water,
            Transform root,
            int patchSize)
        {
            surfaceCatalog = catalog;
            terrainMaterial = terrain;
            waterMaterial = water;
            renderRoot = root;
            renderPatchSizeXZ = Math.Max(1, patchSize);
        }

        private void BuildAllPatches(bool persistentSceneObjects)
        {
            var patchCount = boundWorld.Size / activeRenderPatchSize;
            chunkViews = new WorldChunkView[patchCount, patchCount];
            for (var patchZ = 0; patchZ < patchCount; patchZ++)
            for (var patchX = 0; patchX < patchCount; patchX++)
            {
                var chunkObject = new GameObject
                {
                    hideFlags = persistentSceneObjects
                        ? HideFlags.None
                        : HideFlags.DontSave
                };
                chunkObject.transform.SetParent(renderRoot, false);
                var view = chunkObject.AddComponent<WorldChunkView>();
                chunkViews[patchX, patchZ] = view;
                BuildPatch(view, patchX, patchZ);
            }
        }

        private void BuildPatch(
            WorldChunkView view,
            int patchX,
            int patchZ)
        {
            view.Build(
                boundWorld,
                patchX,
                patchZ,
                activeRenderPatchSize,
                surfaceCatalog,
                terrainMaterial,
                waterMaterial,
                surfaceQuery,
                exposureCache,
                meshBuildScratch);
        }

        private int ResolveRenderPatchSize(WorldData world)
        {
            return renderPatchSizeXZ >= world.ChunkSizeX
                && renderPatchSizeXZ % world.ChunkSizeX == 0
                && world.Size % renderPatchSizeXZ == 0
                    ? renderPatchSizeXZ
                    : world.ChunkSizeX;
        }

        private void ValidateReferences()
        {
            if (surfaceCatalog == null)
            {
                throw new MissingReferenceException("World surface catalog is not assigned.");
            }

            if (terrainMaterial == null
                || waterMaterial == null)
            {
                throw new MissingReferenceException(
                    "Terrain and water materials must both be assigned.");
            }

            if (renderRoot == null)
            {
                throw new MissingReferenceException("World render root is not assigned.");
            }
        }

        private void ClearViews()
        {
            if (renderRoot == null)
            {
                chunkViews = null;
                return;
            }

            for (var index = renderRoot.childCount - 1; index >= 0; index--)
            {
                var child = renderRoot.GetChild(index);
                if (child.TryGetComponent<WorldChunkView>(out var view))
                {
                    child.gameObject.SetActive(false);
                    view.ReleaseMeshes();
                    ReleaseObject(child.gameObject);
                }
            }

            chunkViews = null;
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private static void ReleaseObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
