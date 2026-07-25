using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation;
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
        [SerializeField] private WorldRenderer worldRenderer;
        [SerializeField] private WorldPersistence persistence;

        private bool hydrologyDirty;
        private readonly HashSet<ChunkCoordinate> hydrologyDirtyChunks = new();

        public WorldGenerationController Generator => generator;
        public WorldRenderer Renderer => worldRenderer;
        public WorldPersistence Persistence => persistence;
        public WorldDataAsset CurrentWorldDataAsset => currentWorldDataAsset;
        public WorldState CurrentWorld { get; private set; }
        public bool HasWorld => CurrentWorld != null;
        public bool IsDirty { get; private set; }

        public event Action<WorldDataAsset> WorldChanged;
        public event Action<bool> DirtyStateChanged;

        private void Start()
        {
            if (CurrentWorld != null)
            {
                return;
            }

            InitializeWorld();
        }

        private void LateUpdate()
        {
            if (!hydrologyDirty || CurrentWorld == null)
            {
                return;
            }

            CurrentWorld.RefreshWaterBodies();
            foreach (var coordinate in hydrologyDirtyChunks)
            {
                CurrentWorld.Data.GetChunk(coordinate.X, coordinate.Y, coordinate.Z)
                    .ClearDirty(ChunkDirtyFlags.Hydrology);
            }

            hydrologyDirtyChunks.Clear();
            hydrologyDirty = false;
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
                persistence.ClearActiveSavePath();
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
            if (persistence == null || !persistence.HasActiveSavePath)
            {
                Debug.LogError(
                    "The current world has no active save path. Use SaveWorldAs(path) first.",
                    this);
                return false;
            }

            return SaveWorld(persistence.ActiveSavePath);
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
                persistence.Save(currentWorldDataAsset, path);
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
            return LoadWorld(persistence != null
                ? persistence.SavePath
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
                var loadedAsset = persistence.LoadDataAsset(path);
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

        public void ReplaceWorld(WorldState nextWorld)
        {
            if (nextWorld == null)
            {
                throw new ArgumentNullException(nameof(nextWorld));
            }

            var asset = ScriptableObject.CreateInstance<WorldDataAsset>();
            asset.name = $"World {nextWorld.Data.Seed}";
            asset.hideFlags = HideFlags.DontSave;
            asset.Initialize(nextWorld.Data);
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
            UnsubscribeFromCurrentWorld();
            var previousAsset = currentWorldDataAsset;
            CurrentWorld = null;
            currentWorldDataAsset = null;
            hydrologyDirtyChunks.Clear();
            hydrologyDirty = false;
            SetDirty(false);
            WorldChanged?.Invoke(null);
            ReleaseRuntimeAsset(previousAsset);
        }

        public void Configure(
            WorldGenerationController generationController,
            WorldRenderer renderer,
            WorldPersistence saveLoad)
        {
            generator = generationController;
            worldRenderer = renderer;
            persistence = saveLoad;
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

            UnsubscribeFromCurrentWorld();
            var previousAsset = currentWorldDataAsset;
            currentWorldDataAsset = nextAsset;
            CurrentWorld = new WorldState(nextAsset.Data);
            CurrentWorld.Data.ChunkMarkedDirty += OnChunkMarkedDirty;
            hydrologyDirtyChunks.Clear();
            hydrologyDirty = false;

            var adoptedPreparedScene = preferPreparedScene
                && nextAsset.HasPreparedRenderCache
                && worldRenderer.TryAdoptPreparedWorld(
                    nextAsset.Data,
                    nextAsset.PreparedPatchSize,
                    nextAsset.PreparedPatchCount);
            if (!adoptedPreparedScene)
            {
                worldRenderer.BuildRuntimeWorld(nextAsset.Data);
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
            if (generator != null && worldRenderer != null && persistence != null)
            {
                return true;
            }

            Debug.LogError(
                "WorldManager requires assigned Generation, Renderer, and Persistence components.",
                this);
            return false;
        }

        private void UnsubscribeFromCurrentWorld()
        {
            if (CurrentWorld != null)
            {
                CurrentWorld.Data.ChunkMarkedDirty -= OnChunkMarkedDirty;
            }
        }

        private void OnChunkMarkedDirty(
            ChunkCoordinate coordinate,
            ChunkDirtyFlags flags)
        {
            SetDirty(true);
            if ((flags & ChunkDirtyFlags.Hydrology) == 0)
            {
                return;
            }

            hydrologyDirtyChunks.Add(coordinate);
            hydrologyDirty = true;
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
            UnsubscribeFromCurrentWorld();
        }
    }
}
