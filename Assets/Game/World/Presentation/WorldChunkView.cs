using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Meshing;
using MiniCivilization.World.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Presentation
{
    public sealed class WorldChunkView : MonoBehaviour
    {
        private static readonly int RoadPatchMapProperty = Shader.PropertyToID(
            "_RoadPatchMap");
        private static readonly int RoadPortOffsetMapProperty = Shader.PropertyToID(
            "_RoadPortOffsetMap");
        private static readonly int RoadPatchParametersProperty = Shader.PropertyToID(
            "_RoadPatchParameters");

        [SerializeField, HideInInspector] private int patchX;
        [SerializeField, HideInInspector] private int patchZ;
        [SerializeField, HideInInspector] private int patchSize;
        [SerializeField, HideInInspector] private bool preparedReadOnly;

        private MeshFilter terrainFilter;
        private MeshRenderer terrainRenderer;
        private MeshFilter waterFilter;
        private MeshRenderer waterRenderer;
        private Texture2D roadPatchMap;
        private Texture2D roadPortOffsetMap;
        private Color[] roadPatchPixels;
        private Color[] roadPortOffsetPixels;
        private MaterialPropertyBlock terrainProperties;

        public int PatchX => patchX;
        public int PatchZ => patchZ;
        public int PatchSize => patchSize;
        public bool IsPrepared => preparedReadOnly;

        internal void Build(
            WorldData world,
            int patchX,
            int patchZ,
            int patchSize,
            WorldSurfaceCatalog catalog,
            WorldRoadTopology roadTopology,
            RoadVisualCatalog roadVisualCatalog,
            Material terrainMaterial,
            Material waterMaterial,
            WorldSurfaceQuery surfaceQuery,
            WorldExposureCache exposureCache,
            WorldMeshBuildScratch scratch)
        {
            this.patchX = patchX;
            this.patchZ = patchZ;
            this.patchSize = patchSize;
            name = $"World Chunk [{patchX}, {patchZ}]";
            transform.localPosition = new Vector3(
                patchX * patchSize * world.CellSize,
                0f,
                patchZ * patchSize * world.CellSize);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            EnsureChildren();
            if (catalog != null)
            {
                catalog.ApplyToMaterials(
                    terrainMaterial,
                    waterMaterial);
            }

            var replacePreparedMeshes = preparedReadOnly;
            var terrainBuffers = TerrainChunkMeshBuilder.Build(
                world,
                patchX,
                patchZ,
                patchSize,
                catalog,
                surfaceQuery,
                exposureCache,
                scratch.Terrain,
                scratch.SolidCells);
            terrainFilter.sharedMesh = terrainBuffers.CreateMesh(
                $"Terrain [{patchX}, {patchZ}]",
                replacePreparedMeshes ? null : terrainFilter.sharedMesh);
            terrainRenderer.sharedMaterial = terrainMaterial;
            terrainRenderer.shadowCastingMode = ShadowCastingMode.On;
            terrainRenderer.receiveShadows = true;
            terrainRenderer.enabled = !terrainBuffers.IsEmpty;
            RebuildRoad(world, roadTopology, roadVisualCatalog);

            var waterBuffers = WaterChunkMeshBuilder.Build(
                world,
                patchX,
                patchZ,
                patchSize,
                catalog,
                surfaceQuery,
                exposureCache,
                scratch.Water,
                scratch.WaterCells,
                scratch.WaterCellIndices);
            waterFilter.sharedMesh = waterBuffers.CreateMesh(
                $"Water [{patchX}, {patchZ}]",
                replacePreparedMeshes ? null : waterFilter.sharedMesh);
            waterRenderer.sharedMaterial = waterMaterial;
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            waterRenderer.enabled = !waterBuffers.IsEmpty;

            preparedReadOnly = false;
        }

        internal void RebuildWater(
            WorldData world,
            WorldSurfaceCatalog catalog,
            Material waterMaterial,
            WorldSurfaceQuery surfaceQuery,
            WorldExposureCache exposureCache,
            WorldMeshBuildScratch scratch)
        {
            if (preparedReadOnly)
            {
                throw new System.InvalidOperationException(
                    "Prepared patches must be converted with a full rebuild.");
            }

            EnsureChildren();
            var waterBuffers = WaterChunkMeshBuilder.Build(
                world,
                patchX,
                patchZ,
                patchSize,
                catalog,
                surfaceQuery,
                exposureCache,
                scratch.Water,
                scratch.WaterCells,
                scratch.WaterCellIndices);
            waterFilter.sharedMesh = waterBuffers.CreateMesh(
                $"Water [{patchX}, {patchZ}]",
                waterFilter.sharedMesh);
            waterRenderer.sharedMaterial = waterMaterial;
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.receiveShadows = true;
            waterRenderer.enabled = !waterBuffers.IsEmpty;
        }

        internal void RebuildTerrain(
            WorldData world,
            WorldSurfaceCatalog catalog,
            Material terrainMaterial,
            WorldSurfaceQuery surfaceQuery,
            WorldExposureCache exposureCache,
            WorldMeshBuildScratch scratch)
        {
            if (preparedReadOnly)
            {
                throw new System.InvalidOperationException(
                    "Prepared patches must be converted with a full rebuild.");
            }

            EnsureChildren();
            var terrainBuffers = TerrainChunkMeshBuilder.Build(
                world,
                patchX,
                patchZ,
                patchSize,
                catalog,
                surfaceQuery,
                exposureCache,
                scratch.Terrain,
                scratch.SolidCells);
            terrainFilter.sharedMesh = terrainBuffers.CreateMesh(
                $"Terrain [{patchX}, {patchZ}]",
                terrainFilter.sharedMesh);
            terrainRenderer.sharedMaterial = terrainMaterial;
            terrainRenderer.shadowCastingMode = ShadowCastingMode.On;
            terrainRenderer.receiveShadows = true;
            terrainRenderer.enabled = !terrainBuffers.IsEmpty;
        }

        internal void RebuildRoad(
            WorldData world,
            WorldRoadTopology roadTopology,
            RoadVisualCatalog catalog)
        {
            EnsureChildren();
            if (world == null
                || roadTopology == null
                || catalog == null
                || !catalog.ShapeMaskArrayAvailable)
            {
                ReleaseRoadVisuals();
                return;
            }

            EnsureRoadMaps();
            System.Array.Clear(roadPatchPixels, 0, roadPatchPixels.Length);
            System.Array.Clear(
                roadPortOffsetPixels,
                0,
                roadPortOffsetPixels.Length);
            var startX = patchX * patchSize;
            var startZ = patchZ * patchSize;
            var endX = Mathf.Min(startX + patchSize, world.Size);
            var endZ = Mathf.Min(startZ + patchSize, world.Size);
            for (var z = startZ; z < endZ; z++)
            for (var x = startX; x < endX; x++)
            {
                if (!roadTopology.TryGet(x, z, out var road)
                    || !road.Road.Road.CrossesCenter
                    || !catalog.TryResolve(road.Road.Road.Type, out var visual))
                {
                    continue;
                }

                var index = x - startX + (z - startZ) * patchSize;
                roadPatchPixels[index] = new Color(
                    (byte)road.Connections,
                    visual.CornerMaskLayer,
                    visual.Surface.GetLayer(0),
                    visual.Surface.GetScale(0));
                roadPortOffsetPixels[index] = new Color(
                    road.West.BoundaryOffsetIndex,
                    road.East.BoundaryOffsetIndex,
                    road.South.BoundaryOffsetIndex,
                    road.North.BoundaryOffsetIndex);
            }

            roadPatchMap.SetPixels(roadPatchPixels);
            roadPatchMap.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            roadPortOffsetMap.SetPixels(roadPortOffsetPixels);
            roadPortOffsetMap.Apply(
                updateMipmaps: false,
                makeNoLongerReadable: false);
            terrainProperties ??= new MaterialPropertyBlock();
            terrainProperties.Clear();
            terrainProperties.SetTexture(RoadPatchMapProperty, roadPatchMap);
            terrainProperties.SetTexture(
                RoadPortOffsetMapProperty,
                roadPortOffsetMap);
            terrainProperties.SetVector(
                RoadPatchParametersProperty,
                new Vector4(
                    startX * world.CellSize,
                    startZ * world.CellSize,
                    world.CellSize,
                    patchSize));
            terrainRenderer.SetPropertyBlock(terrainProperties);
        }

        public void ReleaseMeshes()
        {
            ReleaseRoadVisuals();
            if (preparedReadOnly)
            {
                return;
            }

            var filters = GetComponentsInChildren<MeshFilter>(true);
            for (var index = 0; index < filters.Length; index++)
            {
                DestroyMesh(filters[index]);
            }
        }

        private void OnDestroy() => ReleaseMeshes();

        public bool AdoptPrepared()
        {
            terrainFilter = FindFilter("Terrain", out terrainRenderer);
            waterFilter = FindFilter("Water", out waterRenderer);
            if (terrainFilter == null
                || waterFilter == null
                || terrainFilter.sharedMesh == null
                || waterFilter.sharedMesh == null
                || (waterFilter.sharedMesh.vertexCount > 0
                    && !waterFilter.sharedMesh.HasVertexAttribute(
                        VertexAttribute.TexCoord5)))
            {
                return false;
            }

            preparedReadOnly = true;
            return true;
        }

        public void MarkPrepared()
        {
            preparedReadOnly = true;
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

        }

        private void EnsureChildren()
        {
            EnsureRenderChild(
                "Terrain",
                ref terrainFilter,
                ref terrainRenderer);
            EnsureRenderChild(
                "Water",
                ref waterFilter,
                ref waterRenderer);
        }

        private void EnsureRoadMaps()
        {
            var requiresReplacement = roadPatchMap == null
                || roadPatchMap.width != patchSize
                || roadPatchMap.height != patchSize;
            if (!requiresReplacement)
            {
                return;
            }

            ReleaseRoadVisuals();
            roadPatchMap = CreateRoadMap("Road Patch Map");
            roadPortOffsetMap = CreateRoadMap("Road Port Offset Map");
            var pixelCount = checked(patchSize * patchSize);
            roadPatchPixels = new Color[pixelCount];
            roadPortOffsetPixels = new Color[pixelCount];
        }

        private Texture2D CreateRoadMap(string mapName) => new(
            patchSize,
            patchSize,
            TextureFormat.RGBAHalf,
            mipChain: false,
            linear: true)
        {
            name = $"{mapName} [{patchX}, {patchZ}]",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point,
            anisoLevel = 0,
            hideFlags = HideFlags.DontSave
        };

        private void ReleaseRoadVisuals()
        {
            if (terrainRenderer != null)
            {
                terrainRenderer.SetPropertyBlock(null);
            }

            ReleaseObject(roadPatchMap);
            ReleaseObject(roadPortOffsetMap);
            roadPatchMap = null;
            roadPortOffsetMap = null;
            roadPatchPixels = null;
            roadPortOffsetPixels = null;
            terrainProperties = null;
        }

        private void CacheExistingChildren()
        {
            terrainFilter = FindFilter("Terrain", out terrainRenderer);
            waterFilter = FindFilter("Water", out waterRenderer);
        }

        private MeshFilter FindFilter(
            string childName,
            out MeshRenderer renderer)
        {
            var child = transform.Find(childName);
            renderer = child != null
                ? child.GetComponent<MeshRenderer>()
                : null;
            return child != null
                ? child.GetComponent<MeshFilter>()
                : null;
        }

        private void EnsureRenderChild(
            string childName,
            ref MeshFilter filter,
            ref MeshRenderer renderer)
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
            if (filter == null)
            {
                filter = child.gameObject.AddComponent<MeshFilter>();
            }

            renderer = child.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = child.gameObject.AddComponent<MeshRenderer>();
            }
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

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
