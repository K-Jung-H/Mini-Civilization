using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.Persistence;
using MiniCivilization.World.Presentation;
using MiniCivilization.World.WaterFlow;
using UnityEngine;

namespace MiniCivilization.World.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorldManager : MonoBehaviour
    {
        [Header("Services")]
        [SerializeField] private WorldEditController editController;
        [SerializeField] private WorldWaterFlowController waterFlowController;
        [SerializeField] private WorldRenderer worldRenderer;
        [SerializeField] private EntityManager entityManager;
        [SerializeField] private WorldUIManager uiManager;

        [Header("World Generation Settings")]
        [SerializeField] private WorldGenerationSettings worldGenerationSettings;
        [SerializeField] private Transform streamingTarget;

        [Header("Save / Load")]
        [SerializeField] private SaveLoadManager saveLoadManager;

        private PatternStreamingCoordinator streamingCoordinator;
        private WorldStreamingProgress streamingProgress;
        private WorldGenerationConfiguration generationConfiguration;

        public WorldEditController EditController => editController;
        public WorldWaterFlowController WaterFlowController =>
            waterFlowController;
        public WorldRenderer Renderer => worldRenderer;
        public EntityManager EntityManager => entityManager;
        public WorldGenerationSettings WorldGenerationSettings =>
            worldGenerationSettings;
        public SaveLoadManager SaveLoadManager => saveLoadManager;
        public Transform StreamingTarget => streamingTarget;
        public WorldGenerationConfiguration GenerationConfiguration =>
            generationConfiguration;
        public WorldStreamingProgress StreamingProgress => streamingProgress;
        public long PatternMapRevision => CurrentWorldRuntime == null
            ? 0L
            : CurrentWorldRuntime.PatternMaps.Revision;
        public WorldRuntime CurrentWorldRuntime { get; private set; }
        public WorldData CurrentWorldData => CurrentWorldRuntime?.Data;
        public bool HasWorld => CurrentWorldData != null;
        public bool IsDirty { get; private set; }

        public event Action WorldChanged;
        public event Action<bool> DirtyStateChanged;
        public event Action<EntityChangeSet> EntityChanged;
        public event Action<WorldStreamingProgress> StreamingProgressChanged;

        private void Start()
        {
            uiManager?.Initialize(this);
            if (saveLoadManager == null)
            {
                throw new InvalidOperationException(
                    "World Manager requires a Save Load Manager.");
            }

            saveLoadManager.Saved += OnWorldSaved;
            try
            {
                CreateWorld();
            }
            catch
            {
                saveLoadManager.Saved -= OnWorldSaved;
                throw;
            }
        }

        private void Update()
        {
            if (streamingCoordinator == null)
            {
                return;
            }

            var target = ResolveStreamingTargetChunk();
            worldRenderer?.SetStreamingPriorityTarget(target);
            streamingCoordinator.Update(target);
        }

        public void MarkDirty()
        {
            SetDirty(true);
        }

        public void Configure(
            WorldEditController worldEditor,
            WorldWaterFlowController waterFlow,
            WorldRenderer renderer,
            EntityManager entitiesManager = null,
            WorldUIManager userInterface = null)
        {
            editController = worldEditor;
            waterFlowController = waterFlow;
            worldRenderer = renderer;
            entityManager = entitiesManager;
            uiManager = userInterface;
        }

        public void SetStreamingTarget(Vector3 position)
        {
            if (streamingTarget == null)
            {
                throw new InvalidOperationException(
                    "World Manager has no Streaming Target Transform.");
            }

            streamingTarget.position = position;
        }

        public void SetDebuggerPatternMapDemand(PatternTileBounds bounds)
        {
            if (streamingCoordinator == null)
            {
                throw new InvalidOperationException(
                    "Pattern Map preparation requires an active World Runtime.");
            }

            streamingCoordinator.SetDebuggerPrepareDemand(bounds);
        }

        public void ClearDebuggerPatternMapDemand()
        {
            streamingCoordinator?.ClearDebuggerPrepareDemand();
        }

        public bool TryGetPatternTile(
            PatternTileKey key,
            out PatternTilePair tile)
        {
            if (CurrentWorldRuntime != null)
            {
                return CurrentWorldRuntime.PatternMaps.TryGetPair(key, out tile);
            }

            tile = default;
            return false;
        }

        public bool TryGetTerrainPatternTile(
            PatternTileKey key,
            out TerrainPatternTile tile)
        {
            if (CurrentWorldRuntime != null)
            {
                return CurrentWorldRuntime.PatternMaps.TryGetTerrain(key, out tile);
            }

            tile = null;
            return false;
        }

        public bool TryGetHydrologyPatternTile(
            PatternTileKey key,
            out HydrologyPatternTile tile)
        {
            if (CurrentWorldRuntime != null)
            {
                return CurrentWorldRuntime.PatternMaps.TryGetHydrology(key, out tile);
            }

            tile = null;
            return false;
        }

        private void SetDirty(bool value)
        {
            if (IsDirty == value)
            {
                return;
            }

            IsDirty = value;
            DirtyStateChanged?.Invoke(value);
        }

        private void OnDestroy()
        {
            if (saveLoadManager != null)
            {
                saveLoadManager.Saved -= OnWorldSaved;
            }

            DisposeWorldRuntime();
        }

        private void CreateWorld()
        {
            if (streamingTarget == null)
            {
                throw new InvalidOperationException(
                    "World Manager requires a Streaming Target Transform.");
            }

            if (CurrentWorldRuntime != null)
            {
                throw new InvalidOperationException(
                    "World Manager already has an active World Runtime.");
            }

            generationConfiguration = saveLoadManager.ResolveGeneration(
                worldGenerationSettings);
            var data = new WorldData(generationConfiguration.World);
            var runtime = WorldRuntime.Create(data);
            try
            {
                CurrentWorldRuntime = runtime;
                saveLoadManager.Attach(runtime);
                BindRuntime(runtime);
                streamingCoordinator = new PatternStreamingCoordinator(
                    runtime,
                    generationConfiguration,
                    saveLoadManager.Persistence);
                streamingCoordinator.ProgressChanged += OnStreamingProgressChanged;
                OnStreamingProgressChanged(streamingCoordinator.Progress);
                var target = ResolveStreamingTargetChunk();
                worldRenderer?.SetStreamingPriorityTarget(target);
                streamingCoordinator.Update(target);
                WorldChanged?.Invoke();
            }
            catch
            {
                DisposeStreamingCoordinator();
                UnbindRuntime();
                CurrentWorldRuntime = null;
                generationConfiguration = null;
                throw;
            }
        }

        private void DisposeWorldRuntime()
        {
            DisposeStreamingCoordinator();
            UnbindRuntime();
            CurrentWorldRuntime = null;
            generationConfiguration = null;
        }

        private void DisposeStreamingCoordinator()
        {
            if (streamingCoordinator != null)
            {
                streamingCoordinator.ProgressChanged -= OnStreamingProgressChanged;
                streamingCoordinator.Dispose();
                streamingCoordinator = null;
            }

            OnStreamingProgressChanged(default);
        }

        private void OnStreamingProgressChanged(WorldStreamingProgress progress)
        {
            if (streamingProgress.Equals(progress))
            {
                return;
            }

            streamingProgress = progress;
            StreamingProgressChanged?.Invoke(progress);
        }

        private ChunkCoordinate ResolveStreamingTargetChunk()
        {
            var world = CurrentWorldData;
            if (world == null || streamingTarget == null)
            {
                throw new InvalidOperationException(
                    "Streaming Target resolution requires an active World Runtime.");
            }

            var position = streamingTarget.position;
            if (!float.IsFinite(position.x) || !float.IsFinite(position.z))
            {
                throw new InvalidOperationException(
                    "Streaming Target position must be finite.");
            }

            var cellX = checked((int)MathF.Floor(position.x / world.CellSize));
            var cellZ = checked((int)MathF.Floor(position.z / world.CellSize));
            return WorldCoordinateUtility.ToChunk(
                cellX,
                cellZ,
                world.ChunkSizeX);
        }

        private void BindRuntime(WorldRuntime runtime)
        {
            try
            {
                editController.Bind(runtime);
                waterFlowController.Bind(runtime);
                worldRenderer.Bind(runtime);
                entityManager.Bind(runtime);

                editController.ChangeCommitted += OnEditChanged;
                waterFlowController.ChangeCommitted += OnWaterChanged;
                waterFlowController.StateChanged += OnWaterStateChanged;
                entityManager.Changed += OnEntityChanged;
            }
            catch
            {
                UnbindRuntime();
                throw;
            }
        }

        private void UnbindRuntime()
        {
            if (editController != null)
            {
                editController.ChangeCommitted -= OnEditChanged;
            }

            if (waterFlowController != null)
            {
                waterFlowController.ChangeCommitted -= OnWaterChanged;
                waterFlowController.StateChanged -= OnWaterStateChanged;
            }

            if (entityManager != null)
            {
                entityManager.Changed -= OnEntityChanged;
                entityManager.Unbind();
            }

            worldRenderer?.Unbind();
            waterFlowController?.Unbind();
            editController?.Unbind();
        }

        private void OnEditChanged(WorldChangeSet changeSet)
        {
            if (CurrentWorldRuntime.AffectsWayPointGraph(changeSet))
            {
                CurrentWorldRuntime.RebuildWayPointGraph();
            }

            waterFlowController.ApplyChanges(changeSet);
            worldRenderer.ApplyChanges(changeSet);
            TrackDirty(changeSet);
            MarkDirty();
        }

        private void OnWaterChanged(WorldChangeSet changeSet)
        {
            worldRenderer.ApplyChanges(changeSet);
            TrackDirty(changeSet);
            MarkDirty();
        }

        private void OnWaterStateChanged(WaterFlowState state) =>
            worldRenderer.SetWaterFlowState(state);

        private void OnEntityChanged(EntityChangeSet changeSet)
        {
            worldRenderer.ApplyEntityChanges(changeSet);
            if (!saveLoadManager.IsSynchronizing)
            {
                MarkDirty();
            }

            EntityChanged?.Invoke(changeSet);
        }

        private void TrackDirty(WorldChangeSet changeSet)
        {
            saveLoadManager.MarkDirty(changeSet);
        }

        private void OnWorldSaved() => SetDirty(false);
    }
}
