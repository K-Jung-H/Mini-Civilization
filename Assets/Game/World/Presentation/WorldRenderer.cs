using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    public sealed class WorldRenderer : MonoBehaviour
    {
        [Header("Rendering")]
        [SerializeField] private WorldSurfaceCatalog surfaceCatalog;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Material waterfallMaterial;
        [SerializeField] private Transform renderRoot;

        [Header("Mesh")]
        [SerializeField, Min(1)] private int renderPatchSizeXZ = 32;
        [SerializeField] private bool generateColliders;
        [SerializeField, Range(0, 31)] private int interactionLayer = 8;

        private readonly HashSet<Vector2Int> dirtyGeometryPatches = new();
        private readonly HashSet<Vector2Int> dirtyMaterialPatches = new();
        private readonly HashSet<ChunkCoordinate> dirtyLogicalChunks = new();
        private WorldChunkView[,] chunkViews;
        private WorldData boundWorld;
        private int activeRenderPatchSize;

        public WorldData BoundWorld => boundWorld;
        public int RenderedPatchCount => chunkViews == null
            ? 0
            : chunkViews.GetLength(0) * chunkViews.GetLength(1);

        private void LateUpdate()
        {
            RefreshDirtyChunks();
        }

        public void Bind(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            ValidateReferences();
            Unbind();

            boundWorld = world;
            activeRenderPatchSize = ResolveRenderPatchSize(world);
            boundWorld.ChunkMarkedDirty += OnChunkMarkedDirty;
            BuildAllPatches();
        }

        public void RebuildAll()
        {
            if (boundWorld == null)
            {
                return;
            }

            ClearViews();
            BuildAllPatches();
        }

        public void RefreshDirtyChunks()
        {
            if (boundWorld == null
                || chunkViews == null
                || (dirtyGeometryPatches.Count == 0
                    && dirtyMaterialPatches.Count == 0))
            {
                return;
            }

            foreach (var patch in dirtyGeometryPatches)
            {
                if ((uint)patch.x >= chunkViews.GetLength(0)
                    || (uint)patch.y >= chunkViews.GetLength(1))
                {
                    continue;
                }

                BuildPatch(
                    chunkViews[patch.x, patch.y],
                    patch.x,
                    patch.y,
                    true);
            }

            foreach (var patch in dirtyMaterialPatches)
            {
                if (dirtyGeometryPatches.Contains(patch)
                    || (uint)patch.x >= chunkViews.GetLength(0)
                    || (uint)patch.y >= chunkViews.GetLength(1))
                {
                    continue;
                }

                BuildPatch(
                    chunkViews[patch.x, patch.y],
                    patch.x,
                    patch.y,
                    false);
            }

            const ChunkDirtyFlags handledFlags = ChunkDirtyFlags.Surface
                | ChunkDirtyFlags.TerrainMesh
                | ChunkDirtyFlags.WaterMesh
                | ChunkDirtyFlags.Materials;
            foreach (var coordinate in dirtyLogicalChunks)
            {
                boundWorld.GetChunk(coordinate.X, coordinate.Y, coordinate.Z)
                    .ClearDirty(handledFlags);
            }

            dirtyGeometryPatches.Clear();
            dirtyMaterialPatches.Clear();
            dirtyLogicalChunks.Clear();
        }

        public void Unbind()
        {
            if (boundWorld != null)
            {
                boundWorld.ChunkMarkedDirty -= OnChunkMarkedDirty;
            }

            boundWorld = null;
            activeRenderPatchSize = 0;
            dirtyGeometryPatches.Clear();
            dirtyMaterialPatches.Clear();
            dirtyLogicalChunks.Clear();
            ClearViews();
        }

        public void Configure(
            WorldSurfaceCatalog catalog,
            Material terrain,
            Material water,
            Material waterfall,
            Transform root,
            int patchSize,
            bool buildColliders,
            int colliderLayer = 8)
        {
            surfaceCatalog = catalog;
            terrainMaterial = terrain;
            waterMaterial = water;
            waterfallMaterial = waterfall;
            renderRoot = root;
            renderPatchSizeXZ = Math.Max(1, patchSize);
            generateColliders = buildColliders;
            interactionLayer = Math.Clamp(colliderLayer, 0, 31);
        }

        private void BuildAllPatches()
        {
            var patchCount = boundWorld.Size / activeRenderPatchSize;
            chunkViews = new WorldChunkView[patchCount, patchCount];
            for (var patchZ = 0; patchZ < patchCount; patchZ++)
            for (var patchX = 0; patchX < patchCount; patchX++)
            {
                var chunkObject = new GameObject
                {
                    hideFlags = HideFlags.DontSave
                };
                chunkObject.transform.SetParent(renderRoot, false);
                var view = chunkObject.AddComponent<WorldChunkView>();
                chunkViews[patchX, patchZ] = view;
                BuildPatch(view, patchX, patchZ, true);
            }

            ClearAllDirtyFlags(boundWorld);
        }

        private void BuildPatch(
            WorldChunkView view,
            int patchX,
            int patchZ,
            bool rebuildInteraction)
        {
            view.Build(
                boundWorld,
                patchX,
                patchZ,
                activeRenderPatchSize,
                surfaceCatalog,
                terrainMaterial,
                waterMaterial,
                waterfallMaterial,
                generateColliders,
                interactionLayer,
                rebuildInteraction);
        }

        private void OnChunkMarkedDirty(
            ChunkCoordinate coordinate,
            ChunkDirtyFlags flags)
        {
            const ChunkDirtyFlags geometryFlags = ChunkDirtyFlags.Surface
                | ChunkDirtyFlags.TerrainMesh
                | ChunkDirtyFlags.WaterMesh;
            const ChunkDirtyFlags renderFlags = geometryFlags
                | ChunkDirtyFlags.Materials;
            if ((flags & renderFlags) == 0 || activeRenderPatchSize <= 0)
            {
                return;
            }

            dirtyLogicalChunks.Add(coordinate);
            var startX = coordinate.X * boundWorld.ChunkSizeX;
            var startZ = coordinate.Z * boundWorld.ChunkSizeZ;
            var patch = new Vector2Int(
                startX / activeRenderPatchSize,
                startZ / activeRenderPatchSize);
            if ((flags & geometryFlags) != 0)
            {
                dirtyGeometryPatches.Add(patch);
            }
            else if ((flags & ChunkDirtyFlags.Materials) != 0)
            {
                dirtyMaterialPatches.Add(patch);
            }
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
                || waterMaterial == null
                || waterfallMaterial == null)
            {
                throw new MissingReferenceException(
                    "Terrain, water, and waterfall materials must all be assigned.");
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

        private static void ClearAllDirtyFlags(WorldData world)
        {
            foreach (var chunk in world.EnumerateChunks())
            {
                chunk.ClearDirty(ChunkDirtyFlags.All);
            }
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
