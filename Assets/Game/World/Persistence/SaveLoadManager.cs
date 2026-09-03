using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Generation.Patterns;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Persistence
{
    [DisallowMultipleComponent]
    public sealed class SaveLoadManager : MonoBehaviour
    {
        private const string TemporarySaveName = "Unsaved World";

        [Header("Initial Load")]
        [SerializeField, HideInInspector] private string initialWorldFolderName;

        private WorldSaveRepository repository;
        private WorldSaveData saveData;
        private WorldPersistenceService persistence;
        private bool isTemporarySession;

        public string ActiveSaveFolderName => repository?.Location.WorldFolderName;
        public string ActiveSaveName => repository == null
            ? "No active save"
            : isTemporarySession
                ? TemporarySaveName
                : repository.Location.WorldFolderName;
        internal string InitialWorldFolderName => initialWorldFolderName;
        public bool HasActiveSession => persistence != null;
        public bool IsTemporarySession => isTemporarySession;
        public bool IsSynchronizing => persistence?.IsSynchronizing == true;
        internal WorldPersistenceService Persistence => persistence;

        public event Action Saved;

        internal WorldGenerationConfiguration ResolveGeneration(
            WorldGenerationSettings generationSettings)
        {
            if (repository != null || saveData != null)
            {
                throw new InvalidOperationException(
                    "Save Load Manager already has an active World Save session.");
            }

            if (!string.IsNullOrWhiteSpace(initialWorldFolderName))
            {
                if (!TryGetInitialSaveLocation(out var location))
                {
                    throw new InvalidOperationException(
                        "Initial World Save location is invalid.");
                }

                repository = new WorldSaveRepository(location);
                if (!repository.TryReadSaveData(out saveData))
                {
                    throw new InvalidOperationException(
                        "Initial World Save does not point to an existing World Save Data file.");
                }

                isTemporarySession = false;
                return saveData.Generation;
            }

            if (generationSettings == null)
            {
                throw new ArgumentNullException(nameof(generationSettings));
            }

            repository = WorldSaveRepository.CreateTemporary();
            saveData = new WorldSaveData(
                Guid.NewGuid(),
                TemporarySaveName,
                generationSettings.CreateConfiguration(),
                new WorldSaveMetadata(
                    1,
                    Array.Empty<CellCoordinate>(),
                    Array.Empty<EntityPersistentState>()));
            repository.WriteSaveData(saveData);
            isTemporarySession = true;
            return saveData.Generation;
        }

        internal void Attach(WorldRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (repository == null || saveData == null)
            {
                throw new InvalidOperationException(
                    "Resolve the World Save session before attaching a runtime.");
            }

            if (persistence != null)
            {
                throw new InvalidOperationException(
                    "Save Load Manager is already attached to a World Runtime.");
            }

            persistence = new WorldPersistenceService(
                repository,
                saveData,
                runtime.Data.ChunkSizeX);
            persistence.Attach(runtime);
        }

        public void Save()
        {
            if (isTemporarySession)
            {
                throw new InvalidOperationException(
                    "Saving a new World requires a Save Name.");
            }

            RequirePersistence().SaveAll();
            Saved?.Invoke();
        }

        public void SaveNewWorld(string saveName)
        {
            if (!isTemporarySession)
            {
                throw new InvalidOperationException(
                    "Saving a new World requires a temporary World session.");
            }

            var destination = WorldSaveRepository.CreateUnique(saveName);
            RequirePersistence().Promote(
                destination,
                destination.Location.WorldFolderName);
            isTemporarySession = false;
            Saved?.Invoke();
        }

        public void SaveAs(string saveName)
        {
            if (isTemporarySession)
            {
                throw new InvalidOperationException(
                    "Save As requires an existing World Save.");
            }

            var destination = WorldSaveRepository.CreateUnique(saveName);
            RequirePersistence().SaveAs(
                destination,
                destination.Location.WorldFolderName);
            Saved?.Invoke();
        }

        internal void MarkDirty(WorldChangeSet changeSet) =>
            RequirePersistence().MarkDirty(changeSet);

        internal void MarkDirty(
            WorldData world,
            IReadOnlyList<CellCoordinate> cells) =>
            RequirePersistence().MarkDirty(world, cells);

        internal IReadOnlyList<WorldSaveDescriptor> GetSavedWorlds() =>
            WorldSaveCatalog.GetWorlds();

        internal void SelectInitialWorld(WorldSaveDescriptor descriptor)
        {
            if (!WorldSaveLocation.TryCreate(
                    descriptor.WorldFolderName,
                    out _))
            {
                throw new ArgumentException(
                    "The selected World Save location is invalid.",
                    nameof(descriptor));
            }

            initialWorldFolderName = descriptor.WorldFolderName;
        }

        internal void ClearInitialWorld() => initialWorldFolderName = null;

        internal bool TryGetInitialSaveLocation(out WorldSaveLocation location) =>
            WorldSaveLocation.TryCreate(
                initialWorldFolderName,
                out location);

        private void OnDestroy()
        {
            if (isTemporarySession)
            {
                repository?.DeletePackage();
            }
        }

        private WorldPersistenceService RequirePersistence() => persistence
            ?? throw new InvalidOperationException(
                "Saving requires an attached World Runtime.");

    }
}
