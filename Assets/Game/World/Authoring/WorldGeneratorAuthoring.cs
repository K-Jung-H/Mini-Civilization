using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Presentation;
using UnityEngine;

namespace MiniCivilization.World.Authoring
{
    public sealed class WorldGeneratorAuthoring : MonoBehaviour
    {
        [SerializeField] private WorldGenerationSettings settings;
        [SerializeField] private WorldSurfaceCatalog surfaceCatalog;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private bool generateOnStart = true;
        [SerializeField] private bool generateColliders;

        private Transform generatedRoot;
        private Material ownedTerrainMaterial;
        private Material ownedWaterMaterial;
        private WorldChunkView[,] chunkViews;

        public WorldGenerationSettings Settings => settings;
        public WorldGenerationResult CurrentResult { get; private set; }

        private void Start()
        {
            if (generateOnStart && settings != null)
            {
                Generate();
            }
        }

        private void LateUpdate()
        {
            RefreshDirtyChunks();
        }

        public void SetSettings(WorldGenerationSettings value) => settings = value;
        public void SetSurfaceCatalog(WorldSurfaceCatalog value) => surfaceCatalog = value;
        public void SetMaterials(Material terrain, Material water)
        {
            terrainMaterial = terrain;
            waterMaterial = water;
        }

        [ContextMenu("Generate World")]
        public void Generate()
        {
            if (settings == null)
            {
                Debug.LogError("World generation settings are not assigned.", this);
                return;
            }

            if (!settings.TryValidate(out var error))
            {
                Debug.LogError(error, this);
                return;
            }

            ClearGeneratedWorld();
            EnsureMaterials();
            CurrentResult = WorldGenerator.Generate(settings);
            var rootObject = new GameObject("Generated World");
            rootObject.hideFlags = HideFlags.DontSave;
            generatedRoot = rootObject.transform;
            generatedRoot.SetParent(transform, false);

            var world = CurrentResult.World;
            chunkViews = new WorldChunkView[world.ChunkCountX, world.ChunkCountZ];
            for (var patchZ = 0; patchZ < world.ChunkCountZ; patchZ++)
            for (var patchX = 0; patchX < world.ChunkCountX; patchX++)
            {
                var chunkObject = new GameObject();
                chunkObject.hideFlags = HideFlags.DontSave;
                chunkObject.transform.SetParent(generatedRoot, false);
                var view = chunkObject.AddComponent<WorldChunkView>();
                chunkViews[patchX, patchZ] = view;
                view.Build(
                    world,
                    patchX,
                    patchZ,
                    surfaceCatalog,
                    terrainMaterial != null ? terrainMaterial : ownedTerrainMaterial,
                    waterMaterial != null ? waterMaterial : ownedWaterMaterial,
                    generateColliders);
            }

            ClearAllDirtyFlags(world);
        }

        public void RefreshDirtyChunks()
        {
            if (CurrentResult == null || chunkViews == null)
            {
                return;
            }

            var world = CurrentResult.World;
            var hydrologyDirty = false;
            for (var chunkZ = 0; chunkZ < world.ChunkCountZ; chunkZ++)
            for (var chunkY = 0; chunkY < world.ChunkCountY; chunkY++)
            for (var chunkX = 0; chunkX < world.ChunkCountX; chunkX++)
            {
                hydrologyDirty |= (world.GetChunk(chunkX, chunkY, chunkZ).DirtyFlags & ChunkDirtyFlags.Hydrology) != 0;
            }

            if (hydrologyDirty)
            {
                CurrentResult.RefreshWaterBodies();
            }

            const ChunkDirtyFlags renderFlags = ChunkDirtyFlags.Surface
                | ChunkDirtyFlags.TerrainMesh
                | ChunkDirtyFlags.WaterMesh
                | ChunkDirtyFlags.Materials;

            for (var patchZ = 0; patchZ < world.ChunkCountZ; patchZ++)
            for (var patchX = 0; patchX < world.ChunkCountX; patchX++)
            {
                var requiresRebuild = false;
                for (var chunkY = 0; chunkY < world.ChunkCountY; chunkY++)
                {
                    if ((world.GetChunk(patchX, chunkY, patchZ).DirtyFlags & renderFlags) != 0)
                    {
                        requiresRebuild = true;
                        break;
                    }
                }

                if (!requiresRebuild)
                {
                    continue;
                }

                chunkViews[patchX, patchZ].Build(
                    world,
                    patchX,
                    patchZ,
                    surfaceCatalog,
                    terrainMaterial != null ? terrainMaterial : ownedTerrainMaterial,
                    waterMaterial != null ? waterMaterial : ownedWaterMaterial,
                    generateColliders);
            }

            ClearAllDirtyFlags(world);
        }

        [ContextMenu("Clear Generated World")]
        public void ClearGeneratedWorld()
        {
            if (generatedRoot == null)
            {
                var existing = transform.Find("Generated World");
                if (existing != null)
                {
                    generatedRoot = existing;
                }
            }

            if (generatedRoot != null)
            {
                var views = generatedRoot.GetComponentsInChildren<WorldChunkView>(true);
                for (var i = 0; i < views.Length; i++)
                {
                    views[i].ReleaseMeshes();
                }

                ReleaseObject(generatedRoot.gameObject);
                generatedRoot = null;
            }

            CurrentResult = null;
            chunkViews = null;
        }

        private static void ClearAllDirtyFlags(WorldData world)
        {
            foreach (var chunk in world.EnumerateChunks())
            {
                chunk.ClearDirty(ChunkDirtyFlags.All);
            }
        }

        private void EnsureMaterials()
        {
            if (terrainMaterial == null && ownedTerrainMaterial == null)
            {
                var shader = Shader.Find("Mini Civilization/World Terrain Lit");
                if (shader == null) throw new MissingReferenceException("World Terrain Lit shader was not found.");
                ownedTerrainMaterial = new Material(shader) { name = "Runtime World Terrain Material", hideFlags = HideFlags.DontSave };
            }

            if (waterMaterial == null && ownedWaterMaterial == null)
            {
                var shader = Shader.Find("Mini Civilization/World Water Lit");
                if (shader == null) throw new MissingReferenceException("World Water Lit shader was not found.");
                ownedWaterMaterial = new Material(shader) { name = "Runtime World Water Material", hideFlags = HideFlags.DontSave };
            }
        }

        private void OnDestroy()
        {
            ClearGeneratedWorld();
            ReleaseObject(ownedTerrainMaterial);
            ReleaseObject(ownedWaterMaterial);
        }

        private static void ReleaseObject(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
