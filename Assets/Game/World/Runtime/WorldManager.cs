using System;
using System.IO;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Generation;
using MiniCivilization.World.WaterFlow;
using MiniCivilization.World.Persistence;
using MiniCivilization.World.Presentation;
using UnityEngine;

namespace MiniCivilization.World.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorldManager : MonoBehaviour
    {
        [Header("Current World")]
        [SerializeField] private WorldDataAsset currentWorldDataAsset;

        [Header("Services")]
        [SerializeField] private WorldGenerationController generator;
        [SerializeField] private WorldEditController editController;
        [SerializeField] private WorldWaterFlowController waterFlowController;
        [SerializeField] private WorldRenderer worldRenderer;
        [SerializeField] private EntityManager entityManager;
        [SerializeField] private WorldSaveController saveController;
        [SerializeField] private WorldUIManager uiManager;

        public WorldGenerationController Generator => generator;
        public WorldEditController EditController => editController;
        public WorldWaterFlowController WaterFlowController =>
            waterFlowController;
        public WorldRenderer Renderer => worldRenderer;
        public EntityManager EntityManager => entityManager;
        public WorldSaveController SaveController => saveController;
        public WorldDataAsset CurrentWorldDataAsset => currentWorldDataAsset;
        public WorldRuntime CurrentWorldRuntime { get; private set; }
        public WorldData CurrentWorldData => CurrentWorldRuntime?.Data;
        public bool HasWorld => CurrentWorldData != null;
        public bool IsDirty { get; private set; }
        public WorldOperationProgress CurrentOperationProgress =>
            activeWorldOperation?.Progress ?? default;

        public event Action<WorldDataAsset> WorldChanged;
        public event Action<bool> DirtyStateChanged;
        public event Action<WorldOperationProgress> OperationProgressChanged;
        public event Action<EntityChangeSet> EntityChanged;

        private WorldOperation activeWorldOperation;
        private void Start()
        {
            uiManager?.Initialize(this);

            if (CurrentWorldData != null)
            {
                return;
            }

            InitializeWorld();
        }

        public bool InitializeWorld()
        {
            if (!TryValidateReferences())
            {
                return false;
            }

            if (currentWorldDataAsset != null && currentWorldDataAsset.HasData)
            {
                try
                {
                    var startupAsset = Application.isPlaying
                        ? currentWorldDataAsset.CreateRuntimeWorkingCopy()
                        : currentWorldDataAsset;
                    ActivateWorldAsset(
                        startupAsset,
                        preferPreparedScene: true,
                        markDirty: false);
                    return true;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    return false;
                }
            }

            return GenerateWorld();
        }

        public bool GenerateWorld()
        {
            if (!TryValidateReferences())
            {
                return false;
            }

            if (!Application.isPlaying)
            {
                return GenerateWorldImmediately();
            }

            try
            {
                ConfigureEntityFactories();
                return StartWorldOperation(
                    new WorldGenerationOperation(generator.CreateBuildInput()));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        public bool GenerateWorld(int seed)
        {
            if (generator == null)
            {
                Debug.LogError("WorldManager requires an assigned Generator.", this);
                return false;
            }

            generator.SetSeed(seed);
            return GenerateWorld();
        }

        public bool SaveWorld()
        {
            if (saveController == null || !saveController.HasActiveSavePath)
            {
                Debug.LogError(
                    "The current world has no active save path. Use SaveWorldAs(path) first.",
                    this);
                return false;
            }

            return SaveWorld(saveController.ActiveSavePath);
        }

        public bool SaveWorld(string path)
        {
            if (!TryValidateReferences())
            {
                return false;
            }

            if (!HasWorld || currentWorldDataAsset == null)
            {
                Debug.LogError("There is no active world to save.", this);
                return false;
            }

            try
            {
                saveController.Save(currentWorldDataAsset, path);
                SetDirty(false);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        public bool SaveWorldAs(string path)
        {
            return SaveWorld(path);
        }

        public bool LoadWorld()
        {
            return LoadWorld(saveController != null
                ? saveController.SavePath
                : null);
        }

        public bool LoadWorld(string path)
        {
            if (!TryValidateReferences())
            {
                return false;
            }

            if (!Application.isPlaying)
            {
                return LoadWorldImmediately(path);
            }

            try
            {
                ConfigureEntityFactories();
                return StartWorldOperation(new WorldLoadOperation(path));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        public void SetCurrentWorldAsset(
            WorldDataAsset asset,
            bool preferPreparedScene = true,
            bool markDirty = false)
        {
            if (asset == null)
            {
                UnloadWorld();
                return;
            }

            CancelActiveWorldOperation();
            ActivateWorldAsset(asset, preferPreparedScene, markDirty);
        }

        public void MarkDirty()
        {
            SetDirty(true);
        }

        public void UnloadWorld()
        {
            CancelActiveWorldOperation();
            UnbindRuntime();
            var previousAsset = currentWorldDataAsset;
            CurrentWorldRuntime = null;
            currentWorldDataAsset = null;
            SetDirty(false);
            WorldChanged?.Invoke(null);
            ReleaseRuntimeAsset(previousAsset);
        }

        public void Configure(
            WorldGenerationController generationController,
            WorldEditController worldEditor,
            WorldWaterFlowController waterFlow,
            WorldRenderer renderer,
            WorldSaveController saveLoad,
            EntityManager entitiesManager = null,
            WorldUIManager userInterface = null)
        {
            generator = generationController;
            editController = worldEditor;
            waterFlowController = waterFlow;
            worldRenderer = renderer;
            saveController = saveLoad;
            entityManager = entitiesManager;
            uiManager = userInterface;
        }

        private void ActivateWorldAsset(
            WorldDataAsset nextAsset,
            bool preferPreparedScene,
            bool markDirty)
        {
            if (nextAsset == null)
            {
                throw new ArgumentNullException(nameof(nextAsset));
            }

            if (!nextAsset.HasData)
            {
                throw new InvalidOperationException(
                    $"World data asset '{nextAsset.name}' is empty.");
            }

            if (worldRenderer == null)
            {
                throw new MissingReferenceException(
                    "WorldManager requires an assigned Renderer.");
            }

            ConfigureEntityFactories();

            ActivatePreparedWorldAsset(
                nextAsset,
                WorldRuntime.CreatePrepared(nextAsset.Data),
                preferPreparedScene,
                markDirty);
        }

        private void ActivatePreparedWorldAsset(
            WorldDataAsset nextAsset,
            WorldRuntime runtime,
            bool preferPreparedScene,
            bool markDirty)
        {
            if (nextAsset == null)
            {
                throw new ArgumentNullException(nameof(nextAsset));
            }

            if (runtime == null || !ReferenceEquals(runtime.Data, nextAsset.Data))
            {
                throw new ArgumentException(
                    "The prepared runtime does not belong to the supplied world asset.",
                    nameof(runtime));
            }

            var previousAsset = currentWorldDataAsset;
            UnbindRuntime();
            BindRuntime(runtime, nextAsset, preferPreparedScene);
            currentWorldDataAsset = nextAsset;
            CurrentWorldRuntime = runtime;

            SetDirty(markDirty);
            WorldChanged?.Invoke(currentWorldDataAsset);
            if (previousAsset != nextAsset)
            {
                ReleaseRuntimeAsset(previousAsset);
            }
        }

        private void Update()
        {
            var operation = activeWorldOperation;
            if (operation == null)
            {
                return;
            }

            operation.Update();
            PublishOperationProgress(operation);
            if (operation.IsFailed)
            {
                Debug.LogException(operation.Failure, this);
                FinishWorldOperation(operation);
                return;
            }

            if (!operation.IsReadyForActivation)
            {
                return;
            }

            if (!operation.IsMeshStageStarted)
            {
                operation.BeginMeshStage();
                PublishOperationProgress(operation);
                return;
            }

            try
            {
                var runtime = operation.PreparedRuntime;
                var asset = CreateRuntimeAsset(
                    runtime.Data,
                    operation.Kind == WorldOperationKind.Load
                        ? Path.GetFileNameWithoutExtension(
                            ((WorldLoadOperation)operation).Path)
                        : $"World {runtime.Data.Seed}");
                ActivatePreparedWorldAsset(
                    asset,
                    runtime,
                    preferPreparedScene: false,
                    markDirty: operation.Kind == WorldOperationKind.Generate);
                if (operation.Kind == WorldOperationKind.Generate)
                {
                    saveController.ClearActiveSavePath();
                    Debug.Log("[WorldStartup] Generation complete", this);
                }
                else
                {
                    saveController.SetActiveSavePath(
                        ((WorldLoadOperation)operation).Path);
                }

                operation.Complete();
                PublishOperationProgress(operation);
                FinishWorldOperation(operation);
            }
            catch (Exception exception)
            {
                operation.FailBeforeActivation(exception);
                PublishOperationProgress(operation);
                Debug.LogException(exception, this);
                FinishWorldOperation(operation);
            }
        }

        private bool GenerateWorldImmediately()
        {
            try
            {
                var generatedAsset = generator.GenerateDataAsset();
                saveController.ClearActiveSavePath();
                ActivateWorldAsset(
                    generatedAsset,
                    preferPreparedScene: false,
                    markDirty: true);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        private bool LoadWorldImmediately(string path)
        {
            try
            {
                var loadedAsset = saveController.LoadDataAsset(path);
                ActivateWorldAsset(
                    loadedAsset,
                    preferPreparedScene: false,
                    markDirty: false);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        private bool StartWorldOperation(WorldOperation operation)
        {
            if (activeWorldOperation != null)
            {
                Debug.LogWarning(
                    "A world generation or load operation is already running.",
                    this);
                operation.Dispose();
                return false;
            }

            activeWorldOperation = operation;
            return true;
        }

        private void PublishOperationProgress(WorldOperation operation)
        {
            if (operation != activeWorldOperation
                || !operation.TryConsumeProgressChange(out var progress))
            {
                return;
            }

            OperationProgressChanged?.Invoke(progress);
        }

        private void FinishWorldOperation(WorldOperation operation)
        {
            if (operation != activeWorldOperation)
            {
                return;
            }

            operation.Dispose();
            activeWorldOperation = null;
        }

        private void CancelActiveWorldOperation()
        {
            if (activeWorldOperation == null)
            {
                return;
            }

            activeWorldOperation.Dispose();
            activeWorldOperation = null;
            OperationProgressChanged?.Invoke(default);
        }

        private static WorldDataAsset CreateRuntimeAsset(
            WorldData world,
            string assetName)
        {
            var asset = ScriptableObject.CreateInstance<WorldDataAsset>();
            asset.name = assetName;
            asset.hideFlags = HideFlags.DontSave;
            asset.Initialize(world, captureSerializedData: false);
            return asset;
        }

        private bool TryValidateReferences()
        {
            if (generator != null
                && editController != null
                && waterFlowController != null
                && worldRenderer != null
                && entityManager != null
                && saveController != null)
            {
                return true;
            }

            Debug.LogError(
                "WorldManager requires assigned Generation, Editing, Water Flow, " +
                "Renderer, Entity Manager, and Save components.",
                this);
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

        private static void ReleaseRuntimeAsset(WorldDataAsset asset)
        {
            if (asset == null || (asset.hideFlags & HideFlags.DontSave) == 0)
            {
                return;
            }

            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }

        private void OnDestroy()
        {
            CancelActiveWorldOperation();
            UnbindRuntime();
        }

        private void BindRuntime(
            WorldRuntime runtime,
            WorldDataAsset asset,
            bool preferPreparedScene)
        {
            try
            {
                editController.Bind(runtime);
                waterFlowController.Bind(runtime);
                var adoptedPreparedScene = preferPreparedScene
                    && asset.HasPreparedRenderCache
                    && worldRenderer.TryAdoptPreparedWorld(
                        runtime,
                        asset.PreparedPatchSize,
                        asset.PreparedPatchCount);
                if (!adoptedPreparedScene)
                {
                    worldRenderer.Bind(runtime);
                }

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
            waterFlowController.ApplyChanges(changeSet);
            worldRenderer.ApplyChanges(changeSet);
            MarkDirty();
        }

        private void OnWaterChanged(WorldChangeSet changeSet)
        {
            worldRenderer.ApplyChanges(changeSet);
            MarkDirty();
        }

        private void OnWaterStateChanged(WaterFlowState state) =>
            worldRenderer.SetWaterFlowState(state);

        private void ConfigureEntityFactories()
        {
            if (entityManager == null)
            {
                throw new MissingReferenceException(
                    "WorldManager requires an Entity Manager.");
            }

            entityManager.ConfigureEntityFactories();
        }

        private void OnEntityChanged(EntityChangeSet changeSet)
        {
            MarkDirty();
            EntityChanged?.Invoke(changeSet);
        }
    }
}
