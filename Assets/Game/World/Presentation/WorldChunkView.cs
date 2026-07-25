using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Meshing;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Presentation
{
    public sealed class WorldChunkView : MonoBehaviour
    {
        [SerializeField, HideInInspector] private int patchX;
        [SerializeField, HideInInspector] private int patchZ;
        [SerializeField, HideInInspector] private int patchSize;
        [SerializeField, HideInInspector] private bool preparedReadOnly;

        private MeshFilter terrainFilter;
        private MeshRenderer terrainRenderer;
        private MeshFilter waterFilter;
        private MeshRenderer waterRenderer;
        private MeshFilter waterfallFilter;
        private MeshRenderer waterfallRenderer;
        private WorldChunkInteractionSurface interactionSurface;

        public int PatchX => patchX;
        public int PatchZ => patchZ;
        public int PatchSize => patchSize;
        public bool IsPrepared => preparedReadOnly;

        public void Build(
            WorldData world,
            int patchX,
            int patchZ,
            int patchSize,
            WorldSurfaceCatalog catalog,
            Material terrainMaterial,
            Material waterMaterial,
            Material waterfallMaterial,
            bool buildCollider,
            int interactionLayer,
            bool rebuildInteraction)
        {
            this.patchX = patchX;
            this.patchZ = patchZ;
            this.patchSize = patchSize;
            name = $"World Chunk [{patchX}, {patchZ}]";
            transform.localPosition = new Vector3(patchX * patchSize, 0f, patchZ * patchSize);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            EnsureChildren(buildCollider, interactionLayer);
            if (catalog != null)
            {
                catalog.ApplyToMaterials(
                    terrainMaterial,
                    waterMaterial,
                    waterfallMaterial);
            }

            var replacePreparedMeshes = preparedReadOnly;
            var terrainBuffers = TerrainChunkMeshBuilder.Build(
                world, patchX, patchZ, patchSize, catalog);
            terrainFilter.sharedMesh = terrainBuffers.CreateMesh(
                $"Terrain [{patchX}, {patchZ}]",
                replacePreparedMeshes ? null : terrainFilter.sharedMesh);
            terrainRenderer.sharedMaterial = terrainMaterial;
            terrainRenderer.shadowCastingMode = ShadowCastingMode.On;
            terrainRenderer.receiveShadows = true;

            var waterBuffers = WaterChunkMeshBuilder.Build(
                world, patchX, patchZ, patchSize, catalog);
            waterFilter.sharedMesh = waterBuffers.Surface.CreateMesh(
                $"Water [{patchX}, {patchZ}]",
                replacePreparedMeshes ? null : waterFilter.sharedMesh);
            waterfallFilter.sharedMesh = waterBuffers.Waterfalls.CreateMesh(
                $"Waterfalls [{patchX}, {patchZ}]",
                replacePreparedMeshes ? null : waterfallFilter.sharedMesh);
            waterRenderer.sharedMaterial = waterMaterial;
            waterfallRenderer.sharedMaterial = waterfallMaterial;
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterfallRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            waterfallRenderer.receiveShadows = true;
            waterRenderer.enabled = !waterBuffers.Surface.IsEmpty;
            waterfallRenderer.enabled = !waterBuffers.Waterfalls.IsEmpty;

            if (interactionSurface != null && rebuildInteraction)
            {
                var interactionData = ChunkInteractionMeshBuilder.Build(
                    terrainBuffers,
                    waterBuffers);
                var interactionMesh = interactionData.CreateMesh(
                    $"Interaction [{patchX}, {patchZ}]",
                    out var metadata,
                    replacePreparedMeshes
                        ? null
                        : interactionSurface.ReusableMesh);
                interactionSurface.Bind(interactionMesh, metadata);
            }

            preparedReadOnly = false;
        }

        public void ReleaseMeshes()
        {
            ReleaseInteraction(interactionSurface);

            if (preparedReadOnly)
            {
                return;
            }

            var filters = GetComponentsInChildren<MeshFilter>(true);
            for (var i = 0; i < filters.Length; i++)
            {
                DestroyMesh(filters[i]);
            }
        }

        private void OnDestroy() => ReleaseMeshes();

        public bool AdoptPrepared(int interactionLayer)
        {
            terrainFilter = FindFilter("Terrain", out terrainRenderer);
            waterFilter = FindFilter("Water", out waterRenderer);
            waterfallFilter = FindFilter("Waterfalls", out waterfallRenderer);
            var interaction = transform.Find("Interaction");
            interactionSurface = interaction != null
                ? interaction.GetComponent<WorldChunkInteractionSurface>()
                : null;
            if (terrainFilter == null
                || waterFilter == null
                || waterfallFilter == null
                || terrainFilter.sharedMesh == null
                || waterFilter.sharedMesh == null
                || waterfallFilter.sharedMesh == null
                || (interactionSurface != null
                    && interactionSurface.InteractionMesh == null))
            {
                return false;
            }

            if (interactionSurface != null)
            {
                interactionSurface.gameObject.layer = interactionLayer;
                interactionSurface.RestorePreparedBinding();
            }

            preparedReadOnly = true;
            return true;
        }

        public void MarkPrepared()
        {
            preparedReadOnly = true;
            interactionSurface?.MarkPrepared();
        }

        public IEnumerable<Mesh> EnumerateMeshes()
        {
            CacheExistingChildren();
            if (terrainFilter != null && terrainFilter.sharedMesh != null)
            {
                yield return terrainFilter.sharedMesh;
            }

            if (waterFilter != null && waterFilter.sharedMesh != null)
            {
                yield return waterFilter.sharedMesh;
            }

            if (waterfallFilter != null && waterfallFilter.sharedMesh != null)
            {
                yield return waterfallFilter.sharedMesh;
            }

            if (interactionSurface != null
                && interactionSurface.InteractionMesh != null)
            {
                yield return interactionSurface.InteractionMesh;
            }
        }

        private void EnsureChildren(bool buildCollider, int interactionLayer)
        {
            EnsureRenderChild("Terrain", ref terrainFilter, ref terrainRenderer);
            EnsureRenderChild("Water", ref waterFilter, ref waterRenderer);
            EnsureRenderChild("Waterfalls", ref waterfallFilter, ref waterfallRenderer);
            RemoveLegacyInteraction(terrainFilter.gameObject);
            RemoveLegacyInteraction(waterFilter.gameObject);
            RemoveLegacyInteraction(waterfallFilter.gameObject);

            if (buildCollider)
            {
                interactionSurface = EnsureInteraction(
                    interactionLayer);
            }
            else
            {
                RemoveInteraction();
            }
        }

        private void CacheExistingChildren()
        {
            terrainFilter = FindFilter("Terrain", out terrainRenderer);
            waterFilter = FindFilter("Water", out waterRenderer);
            waterfallFilter = FindFilter("Waterfalls", out waterfallRenderer);
            var interaction = transform.Find("Interaction");
            interactionSurface = interaction != null
                ? interaction.GetComponent<WorldChunkInteractionSurface>()
                : null;
        }

        private MeshFilter FindFilter(
            string childName,
            out MeshRenderer renderer)
        {
            var child = transform.Find(childName);
            renderer = child != null
                ? child.GetComponent<MeshRenderer>()
                : null;
            return child != null ? child.GetComponent<MeshFilter>() : null;
        }

        private WorldChunkInteractionSurface EnsureInteraction(
            int interactionLayer)
        {
            var child = transform.Find("Interaction");
            if (child == null)
            {
                var childObject = new GameObject("Interaction");
                child = childObject.transform;
                child.SetParent(transform, false);
            }

            child.gameObject.layer = interactionLayer;
            if (!child.TryGetComponent<MeshCollider>(out _))
            {
                child.gameObject.AddComponent<MeshCollider>();
            }

            if (!child.TryGetComponent<WorldChunkInteractionSurface>(out var surface))
            {
                surface = child.gameObject.AddComponent<WorldChunkInteractionSurface>();
            }
            return surface;
        }

        private void RemoveInteraction()
        {
            var child = transform.Find("Interaction");
            if (interactionSurface == null && child != null)
            {
                child.TryGetComponent(out interactionSurface);
            }

            if (interactionSurface != null)
            {
                interactionSurface.Release();
                interactionSurface = null;
            }

            if (child != null)
            {
                ReleaseObject(child.gameObject);
            }
        }

        private static void RemoveLegacyInteraction(GameObject target)
        {
            if (target.TryGetComponent<WorldChunkInteractionSurface>(out var surface))
            {
                surface.Release();
                ReleaseObject(surface);
            }

            if (target.TryGetComponent<MeshCollider>(out var collider))
            {
                collider.sharedMesh = null;
                ReleaseObject(collider);
            }
        }

        private static void ReleaseInteraction(
            WorldChunkInteractionSurface interaction)
        {
            if (interaction != null)
            {
                interaction.Release();
            }
        }

        private void EnsureRenderChild(string childName, ref MeshFilter filter, ref MeshRenderer renderer)
        {
            var child = transform.Find(childName);
            if (child == null)
            {
                var childObject = new GameObject(childName);
                child = childObject.transform;
                child.SetParent(transform, false);
            }

            child.gameObject.layer = gameObject.layer;
            filter = child.GetComponent<MeshFilter>();
            if (filter == null) filter = child.gameObject.AddComponent<MeshFilter>();
            renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = child.gameObject.AddComponent<MeshRenderer>();
        }

        private static void DestroyMesh(MeshFilter filter)
        {
            if (filter == null)
            {
                return;
            }

            ReleaseObject(filter.sharedMesh);
            filter.sharedMesh = null;
        }

        private static void ReleaseObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }
    }
}
