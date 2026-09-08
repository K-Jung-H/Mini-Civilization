using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Meshing;
using MiniCivilization.World.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Presentation
{
    internal sealed class StreamingPatchDependencies
    {
        private readonly System.Collections.Generic.Dictionary<ChunkCoordinate, Chunk> streamingDependencies = new();

        internal bool HasStreamingDependencyChanged(WorldData world, ChunkCoordinate coordinate)
        {
            if (!streamingDependencies.TryGetValue(coordinate, out var previous))
                return false;
            world.TryGetChunk(coordinate, out var current);
            return !ReferenceEquals(previous, current);
        }

        internal void ClearStreamingDependencies() => streamingDependencies.Clear();

        internal void CaptureStreamingDependencies(WorldData world, int patchX, int patchZ, int patchSize)
        {
            streamingDependencies.Clear();
            var radius = WorldSurfaceQuery.HorizontalDependencyRadius;
            var first = WorldCoordinateUtility.ToChunk(patchX * patchSize - radius, patchZ * patchSize - radius, world.ChunkSizeX);
            var last = WorldCoordinateUtility.ToChunk((patchX + 1) * patchSize - 1 + radius, (patchZ + 1) * patchSize - 1 + radius, world.ChunkSizeX);
            for (var z = first.Z; z <= last.Z; z++)
            for (var x = first.X; x <= last.X; x++)
            {
                var coordinate = new ChunkCoordinate(x, z);
                world.TryGetChunk(coordinate, out var chunk);
                streamingDependencies.Add(coordinate, chunk);
            }
        }
    }

    public sealed class WorldRenderPatchView : MonoBehaviour
    {
        private readonly StreamingPatchDependencies streamingDependencies = new();

        internal bool HasStreamingDependencyChanged(WorldData world, ChunkCoordinate coordinate) =>
            streamingDependencies.HasStreamingDependencyChanged(world, coordinate);
        internal void ClearStreamingDependencies() => streamingDependencies.ClearStreamingDependencies();
        private void CaptureStreamingDependencies(WorldData world) =>
            streamingDependencies.CaptureStreamingDependencies(world, patchX, patchZ, patchSize);
#if ENABLE_PROFILER
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage0 = new("World.Mesh.RoadTextures");
#endif

        private static readonly int RoadPatchMapProperty = Shader.PropertyToID(
            "_RoadPatchMap");
        private static readonly int RoadPortOffsetMapProperty = Shader.PropertyToID(
            "_RoadPortOffsetMap");
        private static readonly int RoadPatchParametersProperty = Shader.PropertyToID(
            "_RoadPatchParameters");

        [SerializeField, HideInInspector] private int patchX;
        [SerializeField, HideInInspector] private int patchZ;
        [SerializeField, HideInInspector] private int patchSize;

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
            name = $"World Patch [{patchX}, {patchZ}]";
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

            RequestMeshes(world, catalog, terrainMaterial, waterMaterial, exposureCache, 3, false);
            RebuildRoad(world, roadTopology, roadVisualCatalog);
        }

        internal void RebuildWater(WorldData world, WorldSurfaceCatalog catalog, Material waterMaterial,
            WorldSurfaceQuery surfaceQuery, WorldExposureCache exposureCache, WorldMeshBuildScratch scratch) =>
            RequestMeshes(world, catalog, null, waterMaterial, exposureCache, 2, false);

        internal void RebuildTerrain(WorldData world, WorldSurfaceCatalog catalog, Material terrainMaterial,
            WorldSurfaceQuery surfaceQuery, WorldExposureCache exposureCache, WorldMeshBuildScratch scratch) =>
            RequestMeshes(world, catalog, terrainMaterial, null, exposureCache, 1, false);

        internal void RebuildBoundary(WorldData world, WorldSurfaceCatalog catalog, Material terrainMaterial,
            Material waterMaterial, WorldExposureCache exposureCache) =>
            RequestMeshes(world, catalog, terrainMaterial, waterMaterial, exposureCache, 3, true);

        private static readonly System.Collections.Generic.HashSet<WorldRenderPatchView> pendingJobs = new();
        internal static void ProcessMeshJobs(WorldData world, ChunkCoordinate target, int budget)
        {
            if (world == null) return;
            // Only requested jobs participate; dormant views have no Update callback.
            while (true)
            {
                WorldRenderPatchView stale = null;
                foreach (var view in pendingJobs)
                    if (ReferenceEquals(view.requestedWorld, world) && view.awaitingRefresh && view.meshTask != null && view.meshTask.IsCompleted)
                    { stale = view; break; }
                if (stale == null) break;
                stale.CompleteMesh(false);
            }
            for (var i = 0; i < budget; i++)
            {
                var nearest = FindNearest(world, target, false);
                if (nearest == null || nearest.meshTask == null || !nearest.meshTask.IsCompleted) break;
                nearest.CompleteMesh();
            }
            for (var i = 0; i < budget; i++)
            {
                var nearest = FindNearest(world, target, true);
                if (nearest == null) break;
                if (!nearest.TryStartMesh())
                {
                    // A target change can leave both slots occupied by farther completed work.
                    // Retire one such result without displaying it, then admit the nearer request.
                    WorldRenderPatchView obsolete = null;
                    foreach (var view in pendingJobs)
                        if (ReferenceEquals(view.requestedWorld, world) && view.meshTask != null && view.meshTask.IsCompleted)
                        { obsolete = view; break; }
                    if (obsolete == null) break;
                    obsolete.CompleteMesh(false);
                    if (!nearest.TryStartMesh()) break;
                }
            }
        }
        private static WorldRenderPatchView FindNearest(WorldData world, ChunkCoordinate target, bool waiting)
        {
            WorldRenderPatchView best = null;
            ulong bestDistance = ulong.MaxValue;
            foreach (var view in pendingJobs)
            {
                if (!ReferenceEquals(view.requestedWorld, world) ||
                    view.awaitingRefresh || (waiting && view.meshTask != null)) continue;
                var span = System.Math.Max(1, view.patchSize / world.ChunkSizeX);
                var minX = (long)view.patchX * span;
                var minZ = (long)view.patchZ * span;
                var dx = (ulong)System.Math.Max(0L, System.Math.Max(minX - target.X, target.X - (minX + span - 1)));
                var dz = (ulong)System.Math.Max(0L, System.Math.Max(minZ - target.Z, target.Z - (minZ + span - 1)));
                var x2 = dx * dx; var z2 = dz * dz;
                var distance = ulong.MaxValue - x2 < z2 ? ulong.MaxValue : x2 + z2;
                if (ReferenceEquals(best, null) || distance < bestDistance || (distance == bestDistance &&
                    (view.patchZ < best.patchZ || (view.patchZ == best.patchZ && view.patchX < best.patchX))))
                { best = view; bestDistance = distance; }
            }
            return best;
        }
        private static readonly System.Threading.SemaphoreSlim workerSlots = new(2);
        private System.Threading.Tasks.Task<WorldPatchMeshJob> meshTask;
        private int generation, taskGeneration, requestedKinds;
        private bool requestedFull, awaitingRefresh;
        private WorldData requestedWorld;
        private WorldSurfaceCatalog requestedCatalog;
        private WorldExposureCache requestedExposure;
        private Material requestedTerrainMaterial, requestedWaterMaterial;
        private MeshFilter terrainBoundaryFilter, waterBoundaryFilter;
        private MeshRenderer terrainBoundaryRenderer, waterBoundaryRenderer;

        private void RequestMeshes(WorldData world, WorldSurfaceCatalog catalog, Material terrain,
            Material water, WorldExposureCache exposure, int kinds, bool boundaryOnly)
        {
            generation++;
            awaitingRefresh = false;
            requestedKinds |= kinds;
            requestedFull |= !boundaryOnly;
            requestedWorld = world; requestedCatalog = catalog; requestedExposure = exposure;
            if (terrain != null) requestedTerrainMaterial = terrain;
            if (water != null) requestedWaterMaterial = water;
            pendingJobs.Add(this);
            if (!Application.isPlaying && TryStartMesh())
            {
                meshTask.GetAwaiter().GetResult();
                CompleteMesh();
            }
        }

        private void CompleteMesh(bool apply = true)
        {
            if (meshTask != null && meshTask.IsCompleted)
            {
                var finished = meshTask;
                meshTask = null;
                workerSlots.Release();
                if (finished.IsFaulted)
                {
                    var error = finished.Exception;
                    requestedKinds = 0; requestedFull = false;
                    Debug.LogException(error);
                }
                else if (apply && taskGeneration == generation)
                {
                    var result = finished.Result;
                    if ((result.Kinds & 1) != 0 && result.Full) Apply(result.Terrain, terrainFilter, terrainRenderer, requestedTerrainMaterial, true);
                    if ((result.Kinds & 1) != 0) Apply(result.TerrainBoundary, terrainBoundaryFilter, terrainBoundaryRenderer, requestedTerrainMaterial, true);
                    if ((result.Kinds & 2) != 0 && result.Full) Apply(result.Water, waterFilter, waterRenderer, requestedWaterMaterial, false);
                    if ((result.Kinds & 2) != 0) Apply(result.WaterBoundary, waterBoundaryFilter, waterBoundaryRenderer, requestedWaterMaterial, false);
                    requestedKinds = 0; requestedFull = false;
                }
                if (finished.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) finished.Result.Return();
            }
            if (requestedKinds == 0 || awaitingRefresh) pendingJobs.Remove(this);
        }

        private bool TryStartMesh()
        {
            if (awaitingRefresh || meshTask != null || requestedKinds == 0 || requestedWorld == null || !workerSlots.Wait(0)) return false;
            try
            {
                EnsureChildren();
                var world = requestedWorld;
                var snapshot = new WorldData(world.Settings);
                var active = new System.Collections.Generic.List<ChunkCoordinate>();
                var first = WorldCoordinateUtility.ToChunk(patchX * patchSize - 2, patchZ * patchSize - 2, world.ChunkSizeX);
                var last = WorldCoordinateUtility.ToChunk((patchX + 1) * patchSize + 1, (patchZ + 1) * patchSize + 1, world.ChunkSizeX);
                for (var z = first.Z; z <= last.Z; z++)
                for (var x = first.X; x <= last.X; x++)
                {
                    var coordinate = new ChunkCoordinate(x, z);
                    if (world.TryGetChunk(coordinate, out var chunk)) snapshot.AttachGeneratedChunk(chunk.CopyCells());
                    if (requestedExposure.IsPrepared(coordinate)) active.Add(coordinate);
                }
                var palette = MaterialBlendResolver.CapturePalette(requestedCatalog);
                var px = patchX; var pz = patchZ; var size = patchSize;
                var kinds = requestedKinds; var full = requestedFull;
                taskGeneration = generation;
                CaptureStreamingDependencies(world);
                meshTask = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        return WorldPatchMeshJob.Build(snapshot, active, palette, px, pz, size, kinds, full);
                    }
                    finally { MaterialBlendResolver.WorkerPalette = null; }
                });
                return true;
            }
            catch { workerSlots.Release(); throw; }
        }

        private static void Apply(MeshBuffers buffers, MeshFilter filter, MeshRenderer renderer, Material material, bool terrain)
        {
            if (buffers == null) return;
            filter.sharedMesh = buffers.CreateMesh(terrain ? "Terrain" : "Water", filter.sharedMesh);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = terrain ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.enabled = !buffers.IsEmpty;
        }

        private void OnDisable()
        {
            pendingJobs.Remove(this);
            generation++;
            requestedKinds = 0; requestedFull = false;
            requestedWorld = null; requestedCatalog = null; requestedExposure = null;
            if (meshTask != null)
                _ = meshTask.ContinueWith(task => { try { if (task.IsFaulted) _ = task.Exception; else if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) task.Result.Return(); } finally { workerSlots.Release(); } });
            // Running workers own only immutable snapshots; their stale results are discarded.
            meshTask = null;
            if (terrainRenderer != null) terrainRenderer.enabled = false;
            if (waterRenderer != null) waterRenderer.enabled = false;
            if (terrainBoundaryRenderer != null) terrainBoundaryRenderer.enabled = false;
            if (waterBoundaryRenderer != null) waterBoundaryRenderer.enabled = false;
        }
        internal void InvalidatePendingMesh()
        {
            if (requestedKinds == 0) return;
            generation++;
            awaitingRefresh = true;
            if (meshTask == null) pendingJobs.Remove(this);
        }

        internal void RebuildRoad(
            WorldData world,
            WorldRoadTopology roadTopology,
            RoadVisualCatalog catalog)
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage0.Auto();
#endif
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
            var endX = startX + patchSize;
            var endZ = startZ + patchSize;
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
            terrainBoundaryRenderer.SetPropertyBlock(terrainProperties);
        }

        public void ReleaseMeshes()
        {
            ClearStreamingDependencies();
            ReleaseRoadVisuals();
            var filters = GetComponentsInChildren<MeshFilter>(true);
            for (var index = 0; index < filters.Length; index++)
            {
                DestroyMesh(filters[index]);
            }
        }

        private void OnDestroy() => ReleaseMeshes();

        private void EnsureChildren()
        {
            EnsureRenderChild("Terrain Boundary", ref terrainBoundaryFilter, ref terrainBoundaryRenderer);
            EnsureRenderChild("Water Boundary", ref waterBoundaryFilter, ref waterBoundaryRenderer);
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
                if (terrainBoundaryRenderer != null) terrainBoundaryRenderer.SetPropertyBlock(null);
            }

            ReleaseObject(roadPatchMap);
            ReleaseObject(roadPortOffsetMap);
            roadPatchMap = null;
            roadPortOffsetMap = null;
            roadPatchPixels = null;
            roadPortOffsetPixels = null;
            terrainProperties = null;
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
