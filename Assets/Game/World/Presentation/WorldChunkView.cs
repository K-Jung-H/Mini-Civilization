using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Meshing;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Presentation
{
    public sealed class WorldChunkView : MonoBehaviour
    {
        private MeshFilter terrainFilter;
        private MeshRenderer terrainRenderer;
        private MeshCollider terrainCollider;
        private MeshFilter waterFilter;
        private MeshRenderer waterRenderer;
        private MeshFilter waterfallFilter;
        private MeshRenderer waterfallRenderer;

        public int PatchX { get; private set; }
        public int PatchZ { get; private set; }

        public void Build(
            WorldData world,
            int patchX,
            int patchZ,
            WorldSurfaceCatalog catalog,
            Material terrainMaterial,
            Material waterMaterial,
            bool buildCollider)
        {
            PatchX = patchX;
            PatchZ = patchZ;
            name = $"World Chunk [{patchX}, {patchZ}]";
            transform.localPosition = new Vector3(patchX * world.ChunkSizeX, 0f, patchZ * world.ChunkSizeZ);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            EnsureChildren(buildCollider);
            if (catalog != null)
            {
                catalog.ApplyToMaterials(terrainMaterial, waterMaterial);
            }

            ReplaceMesh(terrainFilter, TerrainChunkMeshBuilder.Build(world, patchX, patchZ, catalog).CreateMesh($"Terrain [{patchX}, {patchZ}]"));
            terrainRenderer.sharedMaterial = terrainMaterial;
            terrainRenderer.shadowCastingMode = ShadowCastingMode.On;
            terrainRenderer.receiveShadows = true;

            if (terrainCollider != null)
            {
                terrainCollider.sharedMesh = null;
                terrainCollider.sharedMesh = terrainFilter.sharedMesh;
            }

            var waterBuffers = WaterChunkMeshBuilder.Build(world, patchX, patchZ, catalog);
            ReplaceMesh(waterFilter, waterBuffers.Surface.CreateMesh($"Water [{patchX}, {patchZ}]"));
            ReplaceMesh(waterfallFilter, waterBuffers.Waterfalls.CreateMesh($"Waterfalls [{patchX}, {patchZ}]"));
            waterRenderer.sharedMaterial = waterMaterial;
            waterfallRenderer.sharedMaterial = waterMaterial;
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterfallRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            waterfallRenderer.receiveShadows = true;
            waterRenderer.enabled = !waterBuffers.Surface.IsEmpty;
            waterfallRenderer.enabled = !waterBuffers.Waterfalls.IsEmpty;
        }

        public void ReleaseMeshes()
        {
            var filters = GetComponentsInChildren<MeshFilter>(true);
            for (var i = 0; i < filters.Length; i++)
            {
                DestroyMesh(filters[i]);
            }
        }

        private void OnDestroy() => ReleaseMeshes();

        private void EnsureChildren(bool buildCollider)
        {
            EnsureRenderChild("Terrain", ref terrainFilter, ref terrainRenderer);
            EnsureRenderChild("Water", ref waterFilter, ref waterRenderer);
            EnsureRenderChild("Waterfalls", ref waterfallFilter, ref waterfallRenderer);

            if (buildCollider)
            {
                terrainCollider = terrainFilter.GetComponent<MeshCollider>();
                if (terrainCollider == null)
                {
                    terrainCollider = terrainFilter.gameObject.AddComponent<MeshCollider>();
                }
            }
            else if (terrainFilter.TryGetComponent<MeshCollider>(out var existingCollider))
            {
                ReleaseObject(existingCollider);
                terrainCollider = null;
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

            filter = child.GetComponent<MeshFilter>();
            if (filter == null) filter = child.gameObject.AddComponent<MeshFilter>();
            renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = child.gameObject.AddComponent<MeshRenderer>();
        }

        private static void ReplaceMesh(MeshFilter filter, Mesh replacement)
        {
            ReleaseObject(filter.sharedMesh);
            filter.sharedMesh = replacement;
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
