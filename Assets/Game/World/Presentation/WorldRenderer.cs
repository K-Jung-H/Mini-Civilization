using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
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
        [SerializeField, Min(1)] private int maxPatchRebuildsPerFrame = 2;

        private readonly HashSet<Vector2Int> pendingFullPatches = new();
        private readonly HashSet<Vector2Int> pendingTerrainPatches = new();
        private readonly HashSet<Vector2Int> pendingWaterPatches = new();
        private readonly WorldMeshBuildScratch meshBuildScratch = new();
        private WorldChunkView[,] chunkViews;
        private WorldData boundWorld;
        private WaterFlowState boundWaterFlowState;
        private WorldExposureCache exposureCache;
        private WorldSurfaceQuery surfaceQuery;
        private int activeRenderPatchSize;

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

        private void OnValidate()
        {
            maxPatchRebuildsPerFrame = Math.Max(
                1,
                maxPatchRebuildsPerFrame);
        }

        public void Bind(WorldRuntime runtime)
        {
            var world = runtime?.Data;
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            ValidateReferences();
            Unbind();

            boundWorld = world;
            boundWaterFlowState = runtime.WaterFlowState;
            exposureCache = new WorldExposureCache(world);
            surfaceQuery = new WorldSurfaceQuery(
                world,
                boundWaterFlowState);
            activeRenderPatchSize = ResolveRenderPatchSize(world);
            BindingMode = WorldRenderBindingMode.RuntimeGenerated;
            LastAppliedChangeId = runtime.CurrentChangeId;
            BuildAllPatches(persistentSceneObjects: false);
        }

        public void PrepareWorldInScene(WorldRuntime runtime)
        {
            var world = runtime?.Data;
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
            LastAppliedChangeId = runtime.CurrentChangeId;
            BuildAllPatches(persistentSceneObjects: true);
        }

        public bool TryAdoptPreparedWorld(
            WorldRuntime runtime,
            int preparedPatchSize,
            int preparedPatchCount)
        {
            var world = runtime?.Data;
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
            boundWaterFlowState = runtime.WaterFlowState;
            exposureCache = new WorldExposureCache(world);
            surfaceQuery = new WorldSurfaceQuery(
                world,
                boundWaterFlowState);
            activeRenderPatchSize = preparedPatchSize;
            chunkViews = adoptedViews;
            BindingMode = WorldRenderBindingMode.PreparedScene;
            LastAppliedChangeId = runtime.CurrentChangeId;
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
            const WorldChangeType waterChanges =
                WorldChangeType.WaterTopology
                | WorldChangeType.WaterSurface;
            var rebuildFull =
                (changeSet.ChangeTypes & geometryChanges) != 0
                || (changeSet.ChangeTypes & materialChanges) != 0;
            var rebuildWater =
                (changeSet.ChangeTypes & waterChanges) != 0;
            var invalidateGeometry =
                (changeSet.ChangeTypes
                    & (geometryChanges | waterChanges)) != 0;
            if (invalidateGeometry)
            {
                exposureCache?.ApplyChanges(changeSet);
                surfaceQuery?.InvalidateRegion(changeSet.AffectedBounds);
            }

            if ((rebuildFull || rebuildWater)
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
                    if (rebuildFull)
                    {
                        pendingFullPatches.Add(patch);
                        pendingTerrainPatches.Remove(patch);
                        pendingWaterPatches.Remove(patch);
                    }
                    else if (!pendingFullPatches.Contains(patch))
                    {
                        pendingWaterPatches.Add(patch);
                    }
                }
            }

            if (!rebuildFull
                && rebuildWater
                && activeRenderPatchSize > 0)
            {
                QueueTerrainPatchesAffectedByWater(changeSet);
            }

            LastAppliedChangeId = changeSet.ChangeId;
        }

        public void RebuildPendingPatches()
        {
            if (boundWorld == null
                || chunkViews == null
                || (pendingFullPatches.Count == 0
                    && pendingTerrainPatches.Count == 0
                    && pendingWaterPatches.Count == 0))
            {
                return;
            }

            for (var rebuildIndex = 0;
                 rebuildIndex < maxPatchRebuildsPerFrame;
                 rebuildIndex++)
            {
                if (TryTakePatch(pendingFullPatches, out var patch))
                {
                    pendingTerrainPatches.Remove(patch);
                    pendingWaterPatches.Remove(patch);
                    if (ContainsPatch(patch))
                    {
                        BuildPatch(
                            chunkViews[patch.x, patch.y],
                            patch.x,
                            patch.y);
                    }

                    continue;
                }

                if (TryTakePatch(pendingTerrainPatches, out patch))
                {
                    var rebuildWaterWithTerrain =
                        pendingWaterPatches.Remove(patch);
                    if (!ContainsPatch(patch))
                    {
                        continue;
                    }

                    var terrainView = chunkViews[patch.x, patch.y];
                    if (terrainView.IsPrepared)
                    {
                        BuildPatch(terrainView, patch.x, patch.y);
                    }
                    else
                    {
                        RebuildTerrainPatch(terrainView);
                        if (rebuildWaterWithTerrain)
                        {
                            RebuildWaterPatch(terrainView);
                        }
                    }

                    continue;
                }

                if (!TryTakePatch(pendingWaterPatches, out patch))
                {
                    break;
                }

                if (!ContainsPatch(patch))
                {
                    continue;
                }

                var view = chunkViews[patch.x, patch.y];
                if (view.IsPrepared)
                {
                    BuildPatch(view, patch.x, patch.y);
                }
                else
                {
                    RebuildWaterPatch(view);
                }
            }
        }

        private bool ContainsPatch(Vector2Int patch) =>
            (uint)patch.x < chunkViews.GetLength(0)
            && (uint)patch.y < chunkViews.GetLength(1);

        private static bool TryTakePatch(
            HashSet<Vector2Int> patches,
            out Vector2Int patch)
        {
            var enumerator = patches.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                enumerator.Dispose();
                patch = default;
                return false;
            }

            patch = enumerator.Current;
            enumerator.Dispose();
            patches.Remove(patch);
            return true;
        }

        private void RebuildWaterPatch(WorldChunkView view)
        {
            view.RebuildWater(
                boundWorld,
                surfaceCatalog,
                waterMaterial,
                surfaceQuery,
                exposureCache,
                meshBuildScratch);
        }

        private void RebuildTerrainPatch(WorldChunkView view)
        {
            view.RebuildTerrain(
                boundWorld,
                surfaceCatalog,
                terrainMaterial,
                surfaceQuery,
                exposureCache,
                meshBuildScratch);
        }

        private void QueueTerrainPatchesAffectedByWater(
            WorldChangeSet changeSet)
        {
            for (var index = 0;
                 index < changeSet.ChangedCellIndices.Count;
                 index++)
            {
                var changed = WorldIndex.DecodeCell(
                    boundWorld,
                    changeSet.ChangedCellIndices[index]);
                var minimumY = Math.Max(0, changed.Y - 1);
                for (var y = minimumY; y <= changed.Y; y++)
                for (var z = changed.Z - 1; z <= changed.Z + 1; z++)
                for (var x = changed.X - 1; x <= changed.X + 1; x++)
                {
                    if (!boundWorld.TryGetCell(x, y, z, out var cell)
                        || !cell.HasTerrain)
                    {
                        continue;
                    }

                    var patch = new Vector2Int(
                        x / activeRenderPatchSize,
                        z / activeRenderPatchSize);
                    if (!pendingFullPatches.Contains(patch))
                    {
                        pendingTerrainPatches.Add(patch);
                    }
                }
            }
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
            pendingFullPatches.Clear();
            pendingTerrainPatches.Clear();
            pendingWaterPatches.Clear();
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
            Transform root)
        {
            surfaceCatalog = catalog;
            terrainMaterial = terrain;
            waterMaterial = water;
            renderRoot = root;
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
            return world.Settings.RenderPatchSizeXZ;
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
