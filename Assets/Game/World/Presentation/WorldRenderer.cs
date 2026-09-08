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
        RuntimeGenerated
    }

    public sealed class WorldRenderer : MonoBehaviour
    {
#if ENABLE_PROFILER
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage0 = new("World.Render.MeshQueue");
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage1 = new("World.Render.VisibilityChange");
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage2 = new("World.Render.FullPatch");
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage3 = new("World.Render.RemovePatch");
        private static readonly Unity.Profiling.ProfilerMarker ProfileStage4 = new("World.Render.Reprioritize");
#endif

        [Header("Rendering")]
        [SerializeField] private WorldSurfaceCatalog surfaceCatalog;
        [SerializeField] private RoadVisualCatalog roadVisualCatalog;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Transform renderRoot;

        private int meshPatchPerFrame = 2;
        private bool preferCreate = true;
        private readonly Dictionary<Vector2Int, BoundaryRefresh> boundaryRefreshes = new();
        private readonly List<Vector2Int> readyBoundaryRefreshes = new();
        private readonly struct BoundaryRefresh
        {
            public BoundaryRefresh(int first, int last) { First = first; Last = last; }
            public int First { get; }
            public int Last { get; }
        }
        public int PendingCreateCount => pendingCreatePatches.Count;
        public int PendingBoundaryCount => boundaryRefreshes.Count;
        public int PendingRebuildCount => pendingFullPatches.Count + pendingTerrainPatches.Count
            + pendingWaterPatches.Count + pendingRoadPatches.Count;
        public long CoalescedBoundaryRequests { get; private set; }
        public int CreatedPatchesLastFrame { get; private set; }
        public int RebuiltPatchesLastFrame { get; private set; }
        public double MeshWorkMillisecondsLastFrame { get; private set; }


        private readonly RenderPatchPriorityQueue pendingFullPatches = new();
        private readonly RenderPatchPriorityQueue pendingTerrainPatches = new();
        private readonly RenderPatchPriorityQueue pendingWaterPatches = new();
        private readonly RenderPatchPriorityQueue pendingRoadPatches = new();
        private readonly RenderPatchPriorityQueue pendingCreatePatches = new();
        private readonly Dictionary<Vector2Int, WorldRenderPatchView>
            renderedPatchViews = new();
        private readonly Stack<WorldRenderPatchView> patchViewPool = new();
        private readonly WorldMeshBuildScratch meshBuildScratch = new();
        private WorldData boundWorld;
        private WorldRuntime boundRuntime;
        private WaterFlowState boundWaterFlowState;
        private WorldExposureCache exposureCache;
        private WorldSurfaceQuery surfaceQuery;
        private int activeRenderPatchSize;
        private int activeChunksPerPatch;
        private bool hasPriorityTarget;
        private ChunkCoordinate priorityTarget;

        public WorldRenderBindingMode BindingMode { get; private set; }
        public WorldChangeId LastAppliedChangeId { get; private set; }
        public int ActiveRenderPatchSize => activeRenderPatchSize;
        public Transform RenderRoot => renderRoot;
        internal WorldSurfaceQuery SurfaceQuery => surfaceQuery;
        public int RenderedPatchCount => renderedPatchViews.Count;
        public int PooledPatchCount => patchViewPool.Count;

        private void LateUpdate()
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage0.Auto();
#endif
            CreatedPatchesLastFrame = 0;
            RebuiltPatchesLastFrame = 0;
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            FlushBoundaryRefreshes();
            for (var index = 0; index < meshPatchPerFrame; index++)
            {
                var selected = SelectNextMeshQueue();
                if (selected == null) break;
                var remaining = 1;
                if (selected == pendingCreatePatches)
                {
                    BuildPendingStreamPatches(ref remaining);
                    preferCreate = false;
                }
                else
                {
                    RebuildPendingPatches(ref remaining, selected);
                    preferCreate = true;
                }
            }
            WorldRenderPatchView.ProcessMeshJobs(boundWorld, priorityTarget, meshPatchPerFrame);
            MeshWorkMillisecondsLastFrame = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                * 1000d / System.Diagnostics.Stopwatch.Frequency;
        }

        private void FlushBoundaryRefreshes()
        {
            readyBoundaryRefreshes.Clear();
            foreach (var entry in boundaryRefreshes)
                if (Time.frameCount - entry.Value.Last >= 2 || Time.frameCount - entry.Value.First >= 8)
                    readyBoundaryRefreshes.Add(entry.Key);
            foreach (var patch in readyBoundaryRefreshes)
            {
                boundaryRefreshes.Remove(patch);
                if (renderedPatchViews.ContainsKey(patch) && !pendingFullPatches.Contains(patch))
                {
                    QueueFullPatch(patch);
                    boundaryOnlyPatches.Add(patch);
                }
            }
        }

        private readonly HashSet<Vector2Int> boundaryOnlyPatches = new();

        private void QueueFullPatch(Vector2Int patch)
        {
            if (renderedPatchViews.TryGetValue(patch, out var view)) view.InvalidatePendingMesh();
            boundaryOnlyPatches.Remove(patch);
            boundaryRefreshes.Remove(patch);
            pendingFullPatches.Add(patch);
            pendingTerrainPatches.Remove(patch);
            pendingWaterPatches.Remove(patch);
            pendingRoadPatches.Remove(patch);
        }

        private RenderPatchPriorityQueue SelectNextMeshQueue()
        {
            if (boundWorld == null) return null;
            RenderPatchPriorityQueue selected = null;
            RenderPatchQueueEntry nearest = default;
            Consider(pendingFullPatches);
            Consider(pendingTerrainPatches);
            Consider(pendingWaterPatches);
            Consider(pendingRoadPatches);
            if (pendingCreatePatches.Count > 0 && (preferCreate || selected == null))
                return pendingCreatePatches;
            return selected;

            void Consider(RenderPatchPriorityQueue queue)
            {
                if (queue.TryPeek(out var entry)
                    && (selected == null || entry.CompareTo(nearest) < 0))
                {
                    nearest = entry;
                    selected = queue;
                }
            }
        }

        internal void SetMeshPatchPerFrame(int value)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            meshPatchPerFrame = value;
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
            boundRuntime = runtime;
            boundWaterFlowState = runtime.WaterFlowState;
            exposureCache = new WorldExposureCache(world);
            surfaceQuery = new WorldSurfaceQuery(
                world,
                boundWaterFlowState,
                runtime.IsChunkPrepared);
            activeRenderPatchSize = ResolveRenderPatchSize(world);
            activeChunksPerPatch = world.Settings.RenderChunksPerPatch;
            roadVisualCatalog?.ApplyToMaterial(terrainMaterial);
            BindingMode = WorldRenderBindingMode.RuntimeGenerated;
            LastAppliedChangeId = runtime.CurrentChangeId;
            runtime.TerrainRenderStateChanged += OnTerrainRenderStateChanged;
            runtime.StreamingDataChanged += OnStreamingDataChanged;
            foreach (var pair in runtime.ChunkRuntimes)
            {
                if (pair.Value.TerrainRenderingEnabled)
                {
                    OnTerrainRenderStateChanged(pair.Value);
                }
            }
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

        public void SetStreamingPriorityTarget(ChunkCoordinate target)
        {
            hasPriorityTarget = true;
            priorityTarget = target;
            UpdatePatchPriorities();
        }

        private void OnTerrainRenderStateChanged(
            ChunkRuntime chunkRuntime)
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage1.Auto();
#endif
            if (boundRuntime == null
                || chunkRuntime == null
                || activeChunksPerPatch <= 0)
            {
                return;
            }

            var patch = ToPatchCoordinate(chunkRuntime.Coordinate);
            if (!chunkRuntime.TerrainRenderingEnabled)
            {
                exposureCache?.ReleaseChunk(chunkRuntime.Coordinate);
                if (renderedPatchViews.ContainsKey(patch)) QueueFullPatch(patch);
                if (!boundRuntime.HasTerrainRenderingInPatch(
                        patch.x,
                        patch.y,
                        activeChunksPerPatch))
                {
                    pendingCreatePatches.Remove(patch);
                    ReturnPatchToPool(patch);
                }

                return;
            }

            exposureCache?.PrepareChunk(chunkRuntime.Coordinate, refreshNeighbors: false);

            if (renderedPatchViews.ContainsKey(patch))
            {
                QueueFullPatch(patch);
                return;
            }

            pendingCreatePatches.Add(patch);
        }

        private void OnStreamingDataChanged(IReadOnlyCollection<ChunkCoordinate> chunks)
        {
            foreach (var chunk in chunks)
            {
                exposureCache.RebuildPreparedNeighborBoundaries(chunk);
                surfaceQuery.InvalidateChunk(chunk, boundWorld.ChunkSizeX);
                QueueBoundaryPatchRebuilds(chunk);
            }
        }

        private void QueueBoundaryPatchRebuilds(
            ChunkCoordinate coordinate)
        {
            if (activeChunksPerPatch <= 0)
            {
                return;
            }

            for (var z = coordinate.Z - 1; z <= coordinate.Z + 1; z++)
            for (var x = coordinate.X - 1; x <= coordinate.X + 1; x++)
            {
                if (!boundWorld.IsChunkWithinBounds(
                        new ChunkCoordinate(x, z)))
                {
                    continue;
                }

                var patch = ToPatchCoordinate(
                    new ChunkCoordinate(x, z));
                if (!renderedPatchViews.TryGetValue(patch, out var view)
                    || !view.HasStreamingDependencyChanged(boundWorld, coordinate))
                {
                    continue;
                }

                view.InvalidatePendingMesh();
                if (patch == ToPatchCoordinate(coordinate))
                {
                    // Content inside a shared patch must appear/disappear without a debounce.
                    QueueFullPatch(patch);
                    continue;
                }
                if (boundaryRefreshes.TryGetValue(patch, out var refresh))
                {
                    boundaryRefreshes[patch] = new BoundaryRefresh(refresh.First, Time.frameCount);
                    CoalescedBoundaryRequests++;
                }
                else boundaryRefreshes.Add(patch, new BoundaryRefresh(Time.frameCount, Time.frameCount));
            }
        }

        private void BuildPendingStreamPatches(ref int remaining)
        {
            if (boundWorld == null
                || boundRuntime == null
                || BindingMode != WorldRenderBindingMode.RuntimeGenerated)
            {
                return;
            }

            while (remaining > 0)
            {
                if (!pendingCreatePatches.TryTake(out var patch))
                {
                    return;
                }

                if (!IsPatchInsideFiniteBounds(patch)
                    || !boundRuntime.HasTerrainRenderingInPatch(
                        patch.x,
                        patch.y,
                        activeChunksPerPatch))
                {
                    continue;
                }

                if (!renderedPatchViews.TryGetValue(patch, out var view))
                {
                    view = AcquirePatchView();
                    renderedPatchViews.Add(patch, view);
                    remaining--;
                    BuildPatch(view, patch.x, patch.y);
                    CreatedPatchesLastFrame++;
                    pendingFullPatches.Remove(patch);
                    pendingTerrainPatches.Remove(patch);
                    pendingWaterPatches.Remove(patch);
                    pendingRoadPatches.Remove(patch);
                }

            }
        }

        private Vector2Int ToPatchCoordinate(
            ChunkCoordinate coordinate) => new(
            WorldCoordinateUtility.FloorDivide(
                coordinate.X,
                activeChunksPerPatch),
            WorldCoordinateUtility.FloorDivide(
                coordinate.Z,
                activeChunksPerPatch));

        private bool IsPatchInsideFiniteBounds(Vector2Int patch)
        {
            if (boundWorld.IsInfinite)
            {
                return true;
            }

            var startChunkX = patch.x * activeChunksPerPatch;
            var startChunkZ = patch.y * activeChunksPerPatch;
            var endChunkX = startChunkX + activeChunksPerPatch - 1;
            var endChunkZ = startChunkZ + activeChunksPerPatch - 1;
            return endChunkX >= boundWorld.MinimumChunkX
                && startChunkX <= boundWorld.MaximumChunkX
                && endChunkZ >= boundWorld.MinimumChunkZ
                && startChunkZ <= boundWorld.MaximumChunkZ;
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
                WorldChangeType.Material;
            const WorldChangeType waterChanges =
                WorldChangeType.WaterTopology
                | WorldChangeType.WaterSurface;

            var rebuildFull =
                (changeSet.ChangeTypes & geometryChanges) != 0
                || (changeSet.ChangeTypes & materialChanges) != 0;
            var rebuildWater =
                (changeSet.ChangeTypes & waterChanges) != 0;
            var rebuildRoad = changeSet.Includes(WorldChangeType.RoadTopology)
                || (changeSet.ChangeTypes & geometryChanges) != 0;
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
                     index < changeSet.AffectedSections.Count;
                     index++)
                {
                    var coordinate = changeSet.AffectedSections[index];
                    var startX = coordinate.X * boundWorld.ChunkSizeX;
                    var startZ = coordinate.Z * boundWorld.ChunkSizeZ;
                    var patch = new Vector2Int(
                        startX / activeRenderPatchSize,
                        startZ / activeRenderPatchSize);
                    if (renderedPatchViews.TryGetValue(patch, out var changedView)) changedView.InvalidatePendingMesh();
                    boundaryOnlyPatches.Remove(patch);
                    if (rebuildFull)
                    {
                        pendingFullPatches.Add(patch);
                        pendingTerrainPatches.Remove(patch);
                        pendingWaterPatches.Remove(patch);
                        pendingRoadPatches.Remove(patch);
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

            if (rebuildRoad && activeRenderPatchSize > 0)
            {
                QueueRoadPatches(changeSet.ChangedColumns);
            }

            LastAppliedChangeId = changeSet.ChangeId;
        }

        private void RebuildPendingPatches(ref int remaining, RenderPatchPriorityQueue selected)
        {
            if (boundWorld == null
                || (pendingFullPatches.Count == 0
                    && pendingTerrainPatches.Count == 0
                    && pendingWaterPatches.Count == 0
                    && pendingRoadPatches.Count == 0))
            {
                return;
            }

            while (remaining-- > 0)
            {
                if (selected == pendingFullPatches && pendingFullPatches.TryTake(out var patch))
                {
                    pendingTerrainPatches.Remove(patch);
                    pendingWaterPatches.Remove(patch);
                    pendingRoadPatches.Remove(patch);
                    if (ContainsPatch(patch))
                    {
                        var view = renderedPatchViews[patch];
                        RebuiltPatchesLastFrame++;
                        if (boundaryOnlyPatches.Remove(patch))
                        {
                            view.RebuildBoundary(boundWorld, surfaceCatalog, terrainMaterial, waterMaterial, exposureCache);
                            continue;
                        }
                        BuildPatch(
                            view,
                            patch.x,
                            patch.y);
                    }

                    continue;
                }

                if (selected == pendingTerrainPatches && pendingTerrainPatches.TryTake(out patch))
                {
                    var rebuildWaterWithTerrain =
                        pendingWaterPatches.Remove(patch);
                    var rebuildRoadWithTerrain =
                        pendingRoadPatches.Remove(patch);
                    if (!ContainsPatch(patch))
                    {
                        continue;
                    }

                    var terrainView = renderedPatchViews[patch];
                    RebuiltPatchesLastFrame++;
                    RebuildTerrainPatch(terrainView);
                    if (rebuildWaterWithTerrain)
                    {
                        RebuildWaterPatch(terrainView);
                    }

                    if (rebuildRoadWithTerrain)
                    {
                        RebuildRoadPatch(terrainView);
                    }

                    continue;
                }

                if (selected == pendingWaterPatches && pendingWaterPatches.TryTake(out patch))
                {
                    if (!ContainsPatch(patch))
                    {
                        continue;
                    }

                    var view = renderedPatchViews[patch];
                    RebuiltPatchesLastFrame++;
                    RebuildWaterPatch(view);

                    continue;
                }

                if (selected == pendingRoadPatches && pendingRoadPatches.TryTake(out patch)
                    && ContainsPatch(patch))
                {
                    RebuiltPatchesLastFrame++;
                    RebuildRoadPatch(renderedPatchViews[patch]);
                }
            }
        }

        private bool ContainsPatch(Vector2Int patch) =>
            renderedPatchViews.ContainsKey(patch);

        private void RebuildWaterPatch(WorldRenderPatchView view)
        {
            view.RebuildWater(
                boundWorld,
                surfaceCatalog,
                waterMaterial,
                surfaceQuery,
                exposureCache,
                meshBuildScratch);
        }

        private void RebuildTerrainPatch(WorldRenderPatchView view)
        {
            view.RebuildTerrain(
                boundWorld,
                surfaceCatalog,
                terrainMaterial,
                surfaceQuery,
                exposureCache,
                meshBuildScratch);
        }

        private void RebuildRoadPatch(WorldRenderPatchView view)
        {
            view.RebuildRoad(
                boundWorld,
                boundRuntime.RoadTopology,
                roadVisualCatalog);
        }

        public void ApplyEntityChanges(EntityChangeSet changeSet)
        {
            if (changeSet == null
                || changeSet.World != boundWorld
                || !changeSet.WayTopologyChanged
                || activeRenderPatchSize <= 0)
            {
                return;
            }

            var columns = new HashSet<CellColumnCoordinate>();
            for (var index = 0;
                 index < changeSet.AffectedCells.Count;
                 index++)
            {
                var cell = changeSet.AffectedCells[index];
                columns.Add(new CellColumnCoordinate(cell.X, cell.Z));
            }

            QueueRoadPatches(columns);
        }

        private void QueueRoadPatches(
            IReadOnlyCollection<CellColumnCoordinate> changedColumns)
        {
            foreach (var column in changedColumns)
            {
                var centerX = column.X;
                var centerZ = column.Z;
                for (var z = centerZ - 1; z <= centerZ + 1; z++)
                for (var x = centerX - 1; x <= centerX + 1; x++)
                {
                    if (!boundWorld.ContainsColumn(x, z))
                    {
                        continue;
                    }

                    var patch = new Vector2Int(
                        WorldCoordinateUtility.FloorDivide(
                            x,
                            activeRenderPatchSize),
                        WorldCoordinateUtility.FloorDivide(
                            z,
                            activeRenderPatchSize));
                    if (!pendingFullPatches.Contains(patch)
                        && !pendingTerrainPatches.Contains(patch))
                    {
                        pendingRoadPatches.Add(patch);
                    }
                }
            }
        }

        private void QueueTerrainPatchesAffectedByWater(
            WorldChangeSet changeSet)
        {
            for (var index = 0;
                 index < changeSet.ChangedCells.Count;
                 index++)
            {
                var changed = changeSet.ChangedCells[index];
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
                        if (renderedPatchViews.TryGetValue(patch, out var view)) view.InvalidatePendingMesh();
                        pendingTerrainPatches.Add(patch);
                    }
                }
            }
        }

        public void Unbind()
        {
            DetachBoundWorld(clearViews: true);
        }

        public IEnumerable<WorldRenderPatchView> EnumeratePatchViews()
        {
            foreach (var view in renderedPatchViews.Values)
            {
                yield return view;
            }
        }

        private void DetachBoundWorld(bool clearViews)
        {
            if (boundRuntime != null)
            {
                boundRuntime.TerrainRenderStateChanged -= OnTerrainRenderStateChanged;
                boundRuntime.StreamingDataChanged -= OnStreamingDataChanged;
            }

            boundWorld = null;
            boundRuntime = null;
            boundWaterFlowState = null;
            exposureCache = null;
            surfaceQuery = null;
            activeRenderPatchSize = 0;
            activeChunksPerPatch = 0;
            hasPriorityTarget = false;
            priorityTarget = default;
            BindingMode = WorldRenderBindingMode.None;
            LastAppliedChangeId = WorldChangeId.None;
            pendingFullPatches.Clear();
            pendingTerrainPatches.Clear();
            pendingWaterPatches.Clear();
            pendingRoadPatches.Clear();
            pendingCreatePatches.Clear();
            boundaryRefreshes.Clear();
            boundaryOnlyPatches.Clear();
            readyBoundaryRefreshes.Clear();
            preferCreate = true;
            CoalescedBoundaryRequests = 0;
            if (clearViews)
            {
                ClearViews();
            }
            else
            {
                renderedPatchViews.Clear();
                patchViewPool.Clear();
            }
        }

        public void Configure(
            WorldSurfaceCatalog catalog,
            Material terrain,
            Material water,
            Transform root,
            RoadVisualCatalog roads = null)
        {
            surfaceCatalog = catalog;
            if (roads != null)
            {
                roadVisualCatalog = roads;
            }
            terrainMaterial = terrain;
            waterMaterial = water;
            renderRoot = root;
        }

        private void BuildPatch(
            WorldRenderPatchView view,
            int patchX,
            int patchZ)
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage2.Auto();
#endif
            boundaryRefreshes.Remove(new Vector2Int(patchX, patchZ));
            view.Build(
                boundWorld,
                patchX,
                patchZ,
                activeRenderPatchSize,
                surfaceCatalog,
                boundRuntime.RoadTopology,
                roadVisualCatalog,
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

        private WorldRenderPatchView AcquirePatchView()
        {
            WorldRenderPatchView view;
            if (patchViewPool.Count > 0)
            {
                view = patchViewPool.Pop();
                view.gameObject.SetActive(true);
                return view;
            }

            var chunkObject = new GameObject
            {
                hideFlags = HideFlags.DontSave
            };
            chunkObject.transform.SetParent(renderRoot, false);
            view = chunkObject.AddComponent<WorldRenderPatchView>();
            return view;
        }

        private void ReturnPatchToPool(Vector2Int patch)
        {
            boundaryOnlyPatches.Remove(patch);
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage3.Auto();
#endif
            boundaryRefreshes.Remove(patch);
            if (!renderedPatchViews.Remove(patch, out var view)
                || view == null)
            {
                return;
            }

            pendingFullPatches.Remove(patch);
            pendingTerrainPatches.Remove(patch);
            pendingWaterPatches.Remove(patch);
            pendingRoadPatches.Remove(patch);
            view.ClearStreamingDependencies();
            view.gameObject.SetActive(false);
            patchViewPool.Push(view);
        }

        private void ClearViews()
        {
            if (renderRoot == null)
            {
                renderedPatchViews.Clear();
                patchViewPool.Clear();
                return;
            }

            for (var index = renderRoot.childCount - 1; index >= 0; index--)
            {
                var child = renderRoot.GetChild(index);
                if (child.TryGetComponent<WorldRenderPatchView>(out var view))
                {
                    child.gameObject.SetActive(false);
                    view.ReleaseMeshes();
                    ReleaseObject(child.gameObject);
                }
            }

            renderedPatchViews.Clear();
            patchViewPool.Clear();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void UpdatePatchPriorities()
        {
#if ENABLE_PROFILER
            using var profilerScope = ProfileStage4.Auto();
#endif
            pendingFullPatches.SetPriorityTarget(
                priorityTarget,
                activeChunksPerPatch,
                hasPriorityTarget);
            pendingTerrainPatches.SetPriorityTarget(
                priorityTarget,
                activeChunksPerPatch,
                hasPriorityTarget);
            pendingWaterPatches.SetPriorityTarget(
                priorityTarget,
                activeChunksPerPatch,
                hasPriorityTarget);
            pendingRoadPatches.SetPriorityTarget(
                priorityTarget,
                activeChunksPerPatch,
                hasPriorityTarget);
            pendingCreatePatches.SetPriorityTarget(
                priorityTarget,
                activeChunksPerPatch,
                hasPriorityTarget);
        }

        private sealed class RenderPatchPriorityQueue
        {
            private readonly HashSet<Vector2Int> pending = new();
            private readonly SortedSet<RenderPatchQueueEntry> ordered = new();
            private ChunkCoordinate target;
            private int chunksPerPatch;
            private bool hasTarget;

            public int Count => pending.Count;

            public bool Add(Vector2Int patch)
            {
                if (!pending.Add(patch))
                {
                    return false;
                }

                ordered.Add(CreateEntry(patch));
                return true;
            }

            public bool Remove(Vector2Int patch)
            {
                if (!pending.Remove(patch))
                {
                    return false;
                }

                ordered.Remove(CreateEntry(patch));
                return true;
            }

            public bool Contains(Vector2Int patch) => pending.Contains(patch);

            public void Clear()
            {
                pending.Clear();
                ordered.Clear();
            }

            public void SetPriorityTarget(
                ChunkCoordinate nextTarget,
                int nextChunksPerPatch,
                bool nextHasTarget)
            {
                target = nextTarget;
                chunksPerPatch = nextChunksPerPatch;
                hasTarget = nextHasTarget;
                ordered.Clear();
                foreach (var patch in pending)
                {
                    ordered.Add(CreateEntry(patch));
                }
            }

            public bool TryTake(out Vector2Int patch)
            {
                if (ordered.Count == 0)
                {
                    patch = default;
                    return false;
                }

                var entry = ordered.Min;
                ordered.Remove(entry);
                pending.Remove(entry.Patch);
                patch = entry.Patch;
                return true;
            }

            public bool TryPeek(out RenderPatchQueueEntry entry)
            {
                entry = ordered.Count == 0 ? default : ordered.Min;
                return ordered.Count != 0;
            }

            private RenderPatchQueueEntry CreateEntry(Vector2Int patch) => new(
                patch,
                hasTarget && chunksPerPatch > 0
                    ? CalculateDistanceSquared(patch)
                    : 0UL);

            private ulong CalculateDistanceSquared(Vector2Int patch)
            {
                var minimumX = checked(patch.x * chunksPerPatch);
                var minimumZ = checked(patch.y * chunksPerPatch);
                var maximumX = checked(minimumX + chunksPerPatch - 1);
                var maximumZ = checked(minimumZ + chunksPerPatch - 1);
                var x = CalculateAxisDistance(target.X, minimumX, maximumX);
                var z = CalculateAxisDistance(target.Z, minimumZ, maximumZ);
                var xSquared = x * x;
                var zSquared = z * z;
                return ulong.MaxValue - xSquared < zSquared
                    ? ulong.MaxValue
                    : xSquared + zSquared;
            }

            private static ulong CalculateAxisDistance(
                int value,
                int minimum,
                int maximum)
            {
                if (value < minimum)
                {
                    return (ulong)((long)minimum - value);
                }

                if (value > maximum)
                {
                    return (ulong)((long)value - maximum);
                }

                return 0UL;
            }
        }

        private readonly struct RenderPatchQueueEntry : IComparable<RenderPatchQueueEntry>
        {
            public RenderPatchQueueEntry(Vector2Int patch, ulong distanceSquared)
            {
                Patch = patch;
                DistanceSquared = distanceSquared;
            }

            public Vector2Int Patch { get; }
            public ulong DistanceSquared { get; }

            public int CompareTo(RenderPatchQueueEntry other)
            {
                var distance = DistanceSquared.CompareTo(other.DistanceSquared);
                if (distance != 0)
                {
                    return distance;
                }

                var z = Patch.y.CompareTo(other.Patch.y);
                return z != 0 ? z : Patch.x.CompareTo(other.Patch.x);
            }
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
