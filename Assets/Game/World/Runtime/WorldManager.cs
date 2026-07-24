using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Persistence;
using MiniCivilization.World.Presentation;
using UnityEngine;

namespace MiniCivilization.World.Runtime
{
    public enum WorldStartupMode : byte
    {
        None,
        LoadIfExistsOrGenerate
    }

    public sealed class WorldManager : MonoBehaviour
    {
        [SerializeField] private WorldGenerationController generator;
        [SerializeField] private WorldRenderer worldRenderer;
        [SerializeField] private WorldPersistence persistence;
        [SerializeField] private WorldStartupMode startupMode =
            WorldStartupMode.LoadIfExistsOrGenerate;

        private bool hydrologyDirty;
        private readonly HashSet<ChunkCoordinate> hydrologyDirtyChunks = new();

        public WorldGenerationController Generator => generator;
        public WorldRenderer Renderer => worldRenderer;
        public WorldPersistence Persistence => persistence;
        public WorldState CurrentWorld { get; private set; }
        public bool HasWorld => CurrentWorld != null;

        private void Start()
        {
            if (startupMode == WorldStartupMode.LoadIfExistsOrGenerate)
            {
                InitializeWorld();
            }
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

            return persistence.SaveExists()
                ? LoadWorld()
                : GenerateWorld();
        }

        public bool GenerateWorld()
        {
            if (!TryValidateReferences())
            {
                return false;
            }

            try
            {
                ReplaceWorld(generator.Generate());
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        public bool SaveWorld()
        {
            return SaveWorld(persistence != null
                ? persistence.ActiveSavePath
                : null);
        }

        public bool SaveWorld(string path)
        {
            if (!TryValidateReferences())
            {
                return false;
            }

            if (CurrentWorld == null)
            {
                Debug.LogError("There is no active world to save.", this);
                return false;
            }

            try
            {
                persistence.Save(CurrentWorld.Data, path);
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        public bool LoadWorld()
        {
            return LoadWorld(persistence != null
                ? persistence.ActiveSavePath
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
                var loadedWorld = persistence.Load(path);
                ReplaceWorld(new WorldState(loadedWorld));
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
                return false;
            }
        }

        public void ReplaceWorld(WorldState nextWorld)
        {
            if (nextWorld == null)
            {
                throw new System.ArgumentNullException(nameof(nextWorld));
            }

            worldRenderer.Bind(nextWorld.Data);
            UnsubscribeFromCurrentWorld();
            CurrentWorld = nextWorld;
            CurrentWorld.Data.ChunkMarkedDirty += OnChunkMarkedDirty;
            hydrologyDirtyChunks.Clear();
            hydrologyDirty = false;
        }

        public void UnloadWorld()
        {
            worldRenderer?.Unbind();
            UnsubscribeFromCurrentWorld();
            CurrentWorld = null;
            hydrologyDirtyChunks.Clear();
            hydrologyDirty = false;
        }

        public void Configure(
            WorldGenerationController generationController,
            WorldRenderer renderer,
            WorldPersistence saveLoad,
            WorldStartupMode mode)
        {
            generator = generationController;
            worldRenderer = renderer;
            persistence = saveLoad;
            startupMode = mode;
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
            if ((flags & ChunkDirtyFlags.Hydrology) == 0)
            {
                return;
            }

            hydrologyDirtyChunks.Add(coordinate);
            hydrologyDirty = true;
        }

        private void OnDestroy()
        {
            UnloadWorld();
        }
    }
}
