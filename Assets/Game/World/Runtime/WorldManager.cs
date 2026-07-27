using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Generation;
using MiniCivilization.World.WaterFlow;
using MiniCivilization.World.Persistence;
using MiniCivilization.World.Presentation;
using UnityEngine;
using UnityEngine.Serialization;

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
        [FormerlySerializedAs("persistence")]
        [SerializeField] private WorldSaveController saveController;

        private WorldEditController subscribedEditController;
        private WorldWaterFlowController subscribedWaterFlowController;

        public WorldGenerationController Generator => generator;
        public WorldEditController EditController => editController;
        public WorldWaterFlowController WaterFlowController =>
            waterFlowController;
        public WorldRenderer Renderer => worldRenderer;
        public WorldSaveController SaveController => saveController;
        public WorldDataAsset CurrentWorldDataAsset => currentWorldDataAsset;
        public WorldData CurrentWorldData { get; private set; }
        public bool HasWorld => CurrentWorldData != null;
        public bool IsDirty { get; private set; }

        public event Action<WorldDataAsset> WorldChanged;
        public event Action<bool> DirtyStateChanged;

        private void Start()
        {
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

        public void ReplaceWorld(WorldData nextWorld)
        {
            if (nextWorld == null)
            {
                throw new ArgumentNullException(nameof(nextWorld));
            }

            var asset = ScriptableObject.CreateInstance<WorldDataAsset>();
            asset.name = $"World {nextWorld.Seed}";
            asset.hideFlags = HideFlags.DontSave;
            asset.Initialize(nextWorld);
            ActivateWorldAsset(
                asset,
                preferPreparedScene: false,
                markDirty: true);
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

            ActivateWorldAsset(asset, preferPreparedScene, markDirty);
        }

        public void MarkDirty()
        {
            SetDirty(true);
        }

        public void UnloadWorld()
        {
            worldRenderer?.Unbind();
            waterFlowController?.Unbind();
            editController?.Unbind();
            var previousAsset = currentWorldDataAsset;
            CurrentWorldData = null;
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
            WorldSaveController saveLoad)
        {
            generator = generationController;
            editController = worldEditor;
            waterFlowController = waterFlow;
            worldRenderer = renderer;
            saveController = saveLoad;
            SubscribeToEditController();
            SubscribeToWaterFlowController();
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

            var previousAsset = currentWorldDataAsset;
            currentWorldDataAsset = nextAsset;
            CurrentWorldData = nextAsset.Data;
            SubscribeToEditController();
            SubscribeToWaterFlowController();
            editController.Bind(nextAsset.Data);
            waterFlowController.Bind(nextAsset.Data);

            var adoptedPreparedScene = preferPreparedScene
                && nextAsset.HasPreparedRenderCache
                && worldRenderer.TryAdoptPreparedWorld(
                    nextAsset.Data,
                    nextAsset.PreparedPatchSize,
                    nextAsset.PreparedPatchCount,
                    waterFlowController.State);
            if (!adoptedPreparedScene)
            {
                worldRenderer.BuildRuntimeWorld(
                    nextAsset.Data,
                    waterFlowController.State);
            }

            SetDirty(markDirty);
            WorldChanged?.Invoke(currentWorldDataAsset);
            if (previousAsset != nextAsset)
            {
                ReleaseRuntimeAsset(previousAsset);
            }
        }

        private bool TryValidateReferences()
        {
            if (generator != null
                && editController != null
                && waterFlowController != null
                && worldRenderer != null
                && saveController != null)
            {
                SubscribeToEditController();
                SubscribeToWaterFlowController();
                return true;
            }

            Debug.LogError(
                "WorldManager requires assigned Generation, Editing, Water Flow, " +
                "Renderer, and Save components.",
                this);
            return false;
        }

        private void SubscribeToEditController()
        {
            if (subscribedEditController == editController)
            {
                return;
            }

            if (subscribedEditController != null)
            {
                subscribedEditController.ChangeCommitted -= OnWorldEdited;
            }

            subscribedEditController = editController;
            if (subscribedEditController != null)
            {
                subscribedEditController.ChangeCommitted += OnWorldEdited;
            }
        }

        private void OnWorldEdited(WorldChangeSet changeSet)
        {
            if (changeSet == null || changeSet.World != CurrentWorldData)
            {
                return;
            }

            waterFlowController.ApplyChanges(changeSet);
            worldRenderer.ApplyChanges(changeSet);
            SetDirty(true);
        }

        private void SubscribeToWaterFlowController()
        {
            if (subscribedWaterFlowController == waterFlowController)
            {
                return;
            }

            if (subscribedWaterFlowController != null)
            {
                subscribedWaterFlowController.ChangeCommitted -=
                    OnWaterFlowChanged;
                subscribedWaterFlowController.StateChanged -=
                    OnWaterFlowStateChanged;
            }

            subscribedWaterFlowController = waterFlowController;
            if (subscribedWaterFlowController != null)
            {
                subscribedWaterFlowController.ChangeCommitted +=
                    OnWaterFlowChanged;
                subscribedWaterFlowController.StateChanged +=
                    OnWaterFlowStateChanged;
            }
        }

        private void OnWaterFlowStateChanged(WaterFlowState state)
        {
            worldRenderer?.SetWaterFlowState(state);
        }

        private void OnWaterFlowChanged(WorldChangeSet changeSet)
        {
            if (changeSet == null || changeSet.World != CurrentWorldData)
            {
                return;
            }

            worldRenderer.ApplyChanges(changeSet);
            SetDirty(true);
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
            if (subscribedEditController != null)
            {
                subscribedEditController.ChangeCommitted -= OnWorldEdited;
            }


            if (subscribedWaterFlowController != null)
            {
                subscribedWaterFlowController.ChangeCommitted -=
                    OnWaterFlowChanged;
                subscribedWaterFlowController.StateChanged -=
                    OnWaterFlowStateChanged;
            }
        }
    }
}
