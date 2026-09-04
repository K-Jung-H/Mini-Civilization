using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Persistence
{
    internal sealed class WorldPersistenceService
    {
        private WorldSaveRepository repository;
        private WorldSaveData saveData;
        private readonly int chunkSizeX;
        private readonly Dictionary<ChunkCoordinate, List<EntityPersistentState>>
            deferredEntitiesByChunk = new();
        private readonly Dictionary<ChunkCoordinate, List<CellCoordinate>>
            deferredWaterFrontierByChunk = new();
        private readonly HashSet<ChunkCoordinate> dirtyChunks = new();
        private readonly List<EntityPersistentState> entityBuffer = new();
        private readonly List<ChunkCoordinate> chunkBuffer = new();
        private readonly List<CellCoordinate> frontierBuffer = new();
        private WorldRuntime runtime;
        private ulong nextEntityId = 1;

        public WorldPersistenceService(
            WorldSaveRepository repository,
            WorldSaveData saveData,
            int chunkSizeX)
        {
            this.repository = repository ?? throw new ArgumentNullException(
                nameof(repository));
            this.saveData = saveData ?? throw new ArgumentNullException(
                nameof(saveData));
            if (chunkSizeX <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSizeX));
            }

            this.chunkSizeX = chunkSizeX;
            ApplyRuntimeState(saveData.RuntimeState);
        }

        public bool IsSynchronizing { get; private set; }

        public void Attach(WorldRuntime value)
        {
            runtime = value ?? throw new ArgumentNullException(nameof(value));
            runtime.Entities.RestoreNextEntityId(nextEntityId);
        }

        public bool TryLoadChunk(ChunkCoordinate coordinate)
        {
            EnsureAttached();
            if (!repository.TryReadChunk(coordinate, out var snapshot))
            {
                return false;
            }

            WorldSaveCodec.ApplyChunk(runtime.Data, snapshot);
            return true;
        }

        public void RestoreAvailableEntities()
        {
            EnsureAttached();
            chunkBuffer.Clear();
            foreach (var pair in deferredEntitiesByChunk)
            {
                var restoredAny = false;
                var states = pair.Value;
                for (var index = states.Count - 1; index >= 0; index--)
                {
                    if (!runtime.Entities.CanRestorePersistentState(states[index]))
                    {
                        continue;
                    }

                    IsSynchronizing = true;
                    try
                    {
                        runtime.Entities.RestorePersistentState(states[index]);
                    }
                    finally
                    {
                        IsSynchronizing = false;
                    }

                    states.RemoveAt(index);
                    restoredAny = true;
                }

                if (restoredAny && states.Count == 0)
                {
                    chunkBuffer.Add(pair.Key);
                }
            }

            for (var index = 0; index < chunkBuffer.Count; index++)
            {
                deferredEntitiesByChunk.Remove(chunkBuffer[index]);
            }
        }

        public void RestoreWaterFrontier(ChunkCoordinate coordinate)
        {
            EnsureAttached();
            if (!deferredWaterFrontierByChunk.TryGetValue(
                    coordinate,
                    out var values))
            {
                return;
            }

            runtime.WaterFlowResolver.RestoreChunkFrontier(
                runtime.Data,
                runtime.WaterFlowState,
                values);
            deferredWaterFrontierByChunk.Remove(coordinate);
        }

        public void MarkDirty(WorldChangeSet changeSet)
        {
            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            MarkDirtyCells(changeSet.World, changeSet.ChangedCells);
        }

        private void MarkDirtyCells(
            WorldData world,
            IReadOnlyList<CellCoordinate> cells)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (cells == null)
            {
                throw new ArgumentNullException(nameof(cells));
            }

            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                if (!world.ContainsColumn(cell.X, cell.Z))
                {
                    continue;
                }

                dirtyChunks.Add(WorldCoordinateUtility.ToChunk(
                    cell.X,
                    cell.Z,
                    world.ChunkSizeX));
            }
        }

        public void MarkDirty(ChunkCoordinate coordinate) =>
            dirtyChunks.Add(coordinate);

        public void SaveAll()
        {
            EnsureAttached();
            chunkBuffer.Clear();
            foreach (var coordinate in dirtyChunks)
            {
                if (runtime.Data.IsChunkLoaded(coordinate))
                {
                    chunkBuffer.Add(coordinate);
                }
            }

            chunkBuffer.Sort();
            for (var index = 0; index < chunkBuffer.Count; index++)
            {
                repository.WriteChunk(WorldSaveCodec.CaptureChunk(
                    runtime.Data,
                    chunkBuffer[index]));
            }

            WriteSaveData();
            for (var index = 0; index < chunkBuffer.Count; index++)
            {
                dirtyChunks.Remove(chunkBuffer[index]);
            }
        }

        public void SaveAndDetachChunk(ChunkCoordinate coordinate)
        {
            EnsureAttached();
            if (!runtime.Data.IsChunkLoaded(coordinate))
            {
                return;
            }

            if (dirtyChunks.Contains(coordinate))
            {
                repository.WriteChunk(WorldSaveCodec.CaptureChunk(
                    runtime.Data,
                    coordinate));
            }

            IsSynchronizing = true;
            try
            {
                runtime.Entities.DetachPersistentStatesReferencing(
                    coordinate,
                    entityBuffer);
            }
            finally
            {
                IsSynchronizing = false;
            }

            ReplaceDeferredEntities(entityBuffer);
            runtime.WaterFlowResolver.DetachChunkFrontier(
                runtime.Data,
                runtime.WaterFlowState,
                coordinate,
                frontierBuffer);
            ReplaceDeferredWaterFrontier(coordinate, frontierBuffer);

            WriteSaveData();
            dirtyChunks.Remove(coordinate);
        }

        public void FinalizeTemporarySave(
            WorldSaveRepository destination,
            string saveName)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            var temporaryRepository = repository;
            SaveAll();
            saveData = saveData.WithSaveName(saveName);
            temporaryRepository.WriteSaveData(saveData);
            temporaryRepository.CopyPackageTo(destination);
            repository = destination;
            try
            {
                temporaryRepository.DeletePackage();
            }
            catch (Exception exception) when (exception is System.IO.IOException
                                             || exception is UnauthorizedAccessException)
            {
                // The promoted save is valid; temporary cache cleanup can be retried by the OS.
            }
        }

        public void SaveAs(
            WorldSaveRepository destination,
            string saveName)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            var sourceRepository = repository;
            var sourceSaveData = saveData;
            try
            {
                sourceRepository.CopyPackageTo(destination);
                var newWorldId = Guid.NewGuid();
                destination.RekeyWorldId(newWorldId);
                repository = destination;
                saveData = sourceSaveData
                    .WithWorldId(newWorldId)
                    .WithSaveName(saveName);
                SaveAll();
            }
            catch
            {
                repository = sourceRepository;
                saveData = sourceSaveData;
                destination.DeletePackage();
                throw;
            }
        }

        private WorldSaveRuntimeState CreateRuntimeState()
        {
            entityBuffer.Clear();
            runtime.Entities.CopyPersistentStatesTo(entityBuffer);
            var entities = new List<EntityPersistentState>();
            foreach (var pair in deferredEntitiesByChunk)
            {
                entities.AddRange(pair.Value);
            }

            entities.AddRange(entityBuffer);
            entities.Sort((left, right) => left.Id.CompareTo(right.Id));
            var waterFrontier = new HashSet<CellCoordinate>();
            foreach (var pair in deferredWaterFrontierByChunk)
            {
                waterFrontier.UnionWith(pair.Value);
            }

            waterFrontier.UnionWith(
                runtime.Data.WaterFlowSchedule.FrontierCells);
            var sortedWaterFrontier = new List<CellCoordinate>(waterFrontier);
            sortedWaterFrontier.Sort();
            return new WorldSaveRuntimeState(
                runtime.Entities.NextEntityId,
                sortedWaterFrontier,
                entities);
        }

        private void WriteSaveData()
        {
            saveData = saveData.WithRuntimeState(CreateRuntimeState());
            repository.WriteSaveData(saveData);
        }

        private void ApplyRuntimeState(WorldSaveRuntimeState runtimeState)
        {
            nextEntityId = runtimeState.NextEntityId;
            for (var index = 0;
                 index < runtimeState.WaterFrontier.Count;
                 index++)
            {
                var cell = runtimeState.WaterFrontier[index];
                var coordinate = WorldCoordinateUtility.ToChunk(
                    cell.X,
                    cell.Z,
                    chunkSizeX);
                if (!deferredWaterFrontierByChunk.TryGetValue(
                        coordinate,
                        out var values))
                {
                    values = new List<CellCoordinate>();
                    deferredWaterFrontierByChunk.Add(coordinate, values);
                }

                values.Add(cell);
            }

            for (var index = 0; index < runtimeState.Entities.Count; index++)
            {
                var state = runtimeState.Entities[index];
                var coordinate = WorldCoordinateUtility.ToChunk(
                    state.AnchorCell.X,
                    state.AnchorCell.Z,
                    chunkSizeX);
                if (!deferredEntitiesByChunk.TryGetValue(
                        coordinate,
                        out var values))
                {
                    values = new List<EntityPersistentState>();
                    deferredEntitiesByChunk.Add(coordinate, values);
                }

                values.Add(state);
            }
        }

        private void ReplaceDeferredEntities(
            List<EntityPersistentState> states)
        {
            for (var index = 0; index < states.Count; index++)
            {
                RemoveDeferredEntity(states[index].Id);
            }

            for (var index = 0; index < states.Count; index++)
            {
                var state = states[index];
                var coordinate = WorldCoordinateUtility.ToChunk(
                    state.AnchorCell.X,
                    state.AnchorCell.Z,
                    chunkSizeX);
                if (!deferredEntitiesByChunk.TryGetValue(
                        coordinate,
                        out var values))
                {
                    values = new List<EntityPersistentState>();
                    deferredEntitiesByChunk.Add(coordinate, values);
                }

                values.Add(state);
                values.Sort((left, right) => left.Id.CompareTo(right.Id));
            }
        }

        private void ReplaceDeferredWaterFrontier(
            ChunkCoordinate coordinate,
            List<CellCoordinate> values)
        {
            if (values.Count == 0)
            {
                deferredWaterFrontierByChunk.Remove(coordinate);
                return;
            }

            var copied = new List<CellCoordinate>(values);
            copied.Sort();
            deferredWaterFrontierByChunk[coordinate] = copied;
        }

        private void RemoveDeferredEntity(EntityId id)
        {
            chunkBuffer.Clear();
            foreach (var pair in deferredEntitiesByChunk)
            {
                pair.Value.RemoveAll(state => state.Id == id);
                if (pair.Value.Count == 0)
                {
                    chunkBuffer.Add(pair.Key);
                }
            }

            for (var index = 0; index < chunkBuffer.Count; index++)
            {
                deferredEntitiesByChunk.Remove(chunkBuffer[index]);
            }
        }

        private void EnsureAttached()
        {
            if (runtime == null)
            {
                throw new InvalidOperationException(
                    "World persistence is not attached to a runtime.");
            }
        }
    }
}
