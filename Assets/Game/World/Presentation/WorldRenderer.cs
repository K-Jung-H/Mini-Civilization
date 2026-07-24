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

        private readonly HashSet<Vector2Int> dirtyRenderPatches = new();
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
            if (boundWorld == null || chunkViews == null || dirtyRenderPatches.Count == 0)
            {
                return;
            }

            foreach (var patch in dirtyRenderPatches)
            {
                if ((uint)patch.x >= chunkViews.GetLength(0)
                    || (uint)patch.y >= chunkViews.GetLength(1))
                {
                    continue;
                }

                BuildPatch(chunkViews[patch.x, patch.y], patch.x, patch.y);
            }

            foreach (var coordinate in dirtyLogicalChunks)
            {
                boundWorld.GetChunk(coordinate.X, coordinate.Y, coordinate.Z)
                    .ClearDirty(ChunkDirtyFlags.All);
            }

            dirtyRenderPatches.Clear();
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
            dirtyRenderPatches.Clear();
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
            bool buildColliders)
        {
            surfaceCatalog = catalog;
            terrainMaterial = terrain;
            waterMaterial = water;
            waterfallMaterial = waterfall;
            renderRoot = root;
            renderPatchSizeXZ = Math.Max(1, patchSize);
            generateColliders = buildColliders;
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
                BuildPatch(view, patchX, patchZ);
            }

            ClearAllDirtyFlags(boundWorld);
        }

        private void BuildPatch(WorldChunkView view, int patchX, int patchZ)
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
                generateColliders);
        }

        private void OnChunkMarkedDirty(
            ChunkCoordinate coordinate,
            ChunkDirtyFlags flags)
        {
            const ChunkDirtyFlags renderFlags = ChunkDirtyFlags.Surface
                | ChunkDirtyFlags.TerrainMesh
                | ChunkDirtyFlags.WaterMesh
                | ChunkDirtyFlags.Materials;
            if ((flags & renderFlags) == 0 || activeRenderPatchSize <= 0)
            {
                return;
            }

            dirtyLogicalChunks.Add(coordinate);
            var startX = coordinate.X * boundWorld.ChunkSizeX;
            var startZ = coordinate.Z * boundWorld.ChunkSizeZ;
            dirtyRenderPatches.Add(new Vector2Int(
                startX / activeRenderPatchSize,
                startZ / activeRenderPatchSize));
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
