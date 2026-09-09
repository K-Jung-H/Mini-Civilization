using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Persistence
{
    internal sealed class WorldSaveWriteQueue
    {
        private readonly object gate = new();
        private readonly Queue<Action> actions = new();
        private bool running;
        private Exception failure;
        internal Task Pending { get; private set; } = Task.CompletedTask;
        internal int Count { get { lock (gate) return actions.Count; } }
        internal Task Enqueue(Action action)
        {
            lock (gate)
            {
                if (failure != null) throw new InvalidOperationException("Previous save failed; complete/retry pending writes before continuing.", failure);
                actions.Enqueue(action);
                Start();
                return Pending;
            }
        }
        private void Start()
        {
            if (running || actions.Count == 0) return;
            running = true;
            Pending = Task.Run(() =>
            {
                while (true)
                {
                    Action action;
                    lock (gate)
                    {
                        if (actions.Count == 0) { running = false; return; }
                        action = actions.Peek();
                    }
                    try { action(); }
                    catch (Exception error)
                    {
                        lock (gate) { failure = error; running = false; }
                        throw;
                    }
                    lock (gate) actions.Dequeue();
                }
            });
        }
        internal void Complete()
        {
            Task pending;
            lock (gate) { failure = null; Start(); pending = Pending; }
            pending.GetAwaiter().GetResult();
        }
    }

    internal sealed class WorldPersistenceService
    {
        private WorldSaveRepository repository;
        private WorldSaveData saveData;
        private readonly int chunkSizeX;
        private readonly Dictionary<ChunkCoordinate, List<EntityPersistentState>>
            deferredEntitiesByChunk = new();
        private readonly Dictionary<ChunkCoordinate, List<CellCoordinate>>
            deferredWaterFrontierByChunk = new();
        private readonly Dictionary<ChunkCoordinate, CellCoordinate[]> deferredFrontierSnapshots = new();
        private readonly HashSet<ChunkCoordinate> dirtyChunks = new();
        private readonly List<EntityPersistentState> entityBuffer = new();
        private readonly List<ChunkCoordinate> chunkBuffer = new();
        private readonly List<CellCoordinate> frontierBuffer = new();
        private WorldRuntime runtime;
        private ulong nextEntityId = 1;
        private readonly WorldSaveWriteQueue writer = new();
        private Task writes => writer.Pending;
        internal bool CanAcceptWrites => writer.Count < 8;
        private readonly Dictionary<ChunkCoordinate, Task> chunkWrites = new();
        private readonly Queue<(ChunkCoordinate Chunk, Task Write)> pendingChunkWrites = new();
        private Task metadataWrite = Task.CompletedTask;
        private double metadataPendingSince, lastMetadataChange;
        private static double Now => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
        private const double MetadataQuietSeconds = 0.25;
        private const double MetadataMaxDelaySeconds = 1.0;
        private readonly Dictionary<EntityId, EntityPersistentState> entitySnapshots = new();
        private readonly HashSet<EntityId> dirtyEntities = new();
        internal void CompleteWrites()
        {
            // Retry a failed write before enqueuing the final metadata checkpoint.
            if (writes.IsFaulted) writer.Complete();
            if (runtime != null) FlushDetachedChunks(force: true);
            writer.Complete();
        }
        private void QueueWrite(Action write) => writer.Enqueue(write);
        private void OnEntityChanged(EntityChangeSet change)
        {
            if (IsSynchronizing) return;
            foreach (var id in change.AddedEntityIds) dirtyEntities.Add(id);
            foreach (var id in change.MovedEntityIds) dirtyEntities.Add(id);
            foreach (var id in change.RemovedEntityIds) { dirtyEntities.Remove(id); entitySnapshots.Remove(id); }
        }
        private void OnEntityPresentationChanged(EntityId id) => dirtyEntities.Add(id);


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
            foreach (var pair in deferredWaterFrontierByChunk) deferredFrontierSnapshots[pair.Key] = pair.Value.ToArray();
            foreach (var entity in saveData.RuntimeState.Entities) entitySnapshots[entity.Id] = entity;
        }

        public bool IsSynchronizing { get; private set; }

        public void Attach(WorldRuntime value)
        {
            runtime = value ?? throw new ArgumentNullException(nameof(value));
            runtime.Entities.RestoreNextEntityId(nextEntityId);
            runtime.Entities.Changed += OnEntityChanged;
            runtime.Entities.PresentationChanged += OnEntityPresentationChanged;
            runtime.Entities.CopyPersistentStatesTo(entityBuffer);
            foreach (var entity in entityBuffer) entitySnapshots[entity.Id] = entity;
        }

        internal Task<Chunk> LoadChunkAsync(ChunkCoordinate coordinate)
        {
            EnsureAttached();
            var result = new TaskCompletionSource<Chunk>(TaskCreationOptions.RunContinuationsAsynchronously);
            var settings = runtime.Data.Settings;
            var source = repository;
            // Reads share the ordered writer queue: no main-thread wait and no region read/write race.
            QueueWrite(() =>
            {
                try
                {
                    if (!source.TryReadChunk(coordinate, out var snapshot)) { result.SetResult(null); return; }
                    var isolated = new WorldData(settings);
                    WorldSaveCodec.ApplyChunk(isolated, snapshot);
                    isolated.TryGetChunk(coordinate, out var chunk);
                    result.SetResult(chunk);
                }
                catch (Exception error) { result.SetException(error); }
            });
            return result.Task;
        }

        public bool TryLoadChunk(ChunkCoordinate coordinate)
        {
            EnsureAttached();
            if (chunkWrites.Remove(coordinate, out var pending))
            {
                if (pending.IsFaulted) CompleteWrites();
                else pending.GetAwaiter().GetResult();
            }
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
            deferredFrontierSnapshots.Remove(coordinate);
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
            CompleteWrites();
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

        private bool detachedChunksPendingSave;

        internal void FlushDetachedChunks(bool force = false)
        {
            if (writes.IsFaulted) writer.Complete();
            while (pendingChunkWrites.Count > 0 && pendingChunkWrites.Peek().Write.IsCompletedSuccessfully)
            {
                var completed = pendingChunkWrites.Dequeue();
                if (chunkWrites.TryGetValue(completed.Chunk, out var current) && ReferenceEquals(current, completed.Write))
                    chunkWrites.Remove(completed.Chunk);
            }
            if (!detachedChunksPendingSave) return;
            var now = Now;
            if (!force && (!metadataWrite.IsCompleted ||
                (now - lastMetadataChange < MetadataQuietSeconds && now - metadataPendingSince < MetadataMaxDelaySeconds))) return;
            foreach (var id in dirtyEntities)
            {
                var state = runtime.Entities.CapturePersistentState(id);
                if (state != null) entitySnapshots[id] = state;
            }
            dirtyEntities.Clear();
            var entities = new List<EntityPersistentState>(entitySnapshots.Values);
            var frontier = runtime.WaterFlowResolver.CaptureFrontierChunks();
            foreach (var pair in deferredFrontierSnapshots) frontier[pair.Key] = pair.Value;
            var nextId = runtime.Entities.NextEntityId;
            var metadata = saveData;
            var destination = repository;
            QueueWrite(() =>
            {
                entities.Sort((a, b) => a.Id.CompareTo(b.Id));
                var cells = new List<CellCoordinate>();
                foreach (var values in frontier.Values) cells.AddRange(values);
                cells.Sort();
                destination.WriteSaveData(metadata.WithRuntimeState(new WorldSaveRuntimeState(nextId, cells, entities)));
            });
            metadataWrite = writes;
            detachedChunksPendingSave = false;
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
                var snapshot = WorldSaveCodec.CaptureChunk(runtime.Data, coordinate);
                var destination = repository;
                QueueWrite(() => destination.WriteChunk(snapshot));
                chunkWrites[coordinate] = writes;
                pendingChunkWrites.Enqueue((coordinate, writes));
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
            foreach (var entity in entityBuffer) entitySnapshots[entity.Id] = entity;
            runtime.WaterFlowResolver.DetachChunkFrontier(
                runtime.Data,
                runtime.WaterFlowState,
                coordinate,
                frontierBuffer);
            ReplaceDeferredWaterFrontier(coordinate, frontierBuffer);

            // Empty terrain chunks do not change the global entity/frontier snapshot.
            // Preserve the write whenever either the previous or current snapshot may contain state.
            var needsMetadata = entityBuffer.Count > 0 || frontierBuffer.Count > 0
                || deferredEntitiesByChunk.Count > 0 || deferredWaterFrontierByChunk.Count > 0
                || saveData.RuntimeState.Entities.Count > 0 || saveData.RuntimeState.WaterFrontier.Count > 0
                || runtime.Data.WaterFlowSchedule.HasPendingFlow
                || runtime.Entities.NextEntityId != saveData.RuntimeState.NextEntityId;
            if (needsMetadata)
            {
                var now = Now;
                if (!detachedChunksPendingSave) metadataPendingSince = now;
                lastMetadataChange = now;
                detachedChunksPendingSave = true;
            }
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

            CompleteWrites();
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
            entitySnapshots.Clear();
            foreach (var entity in saveData.RuntimeState.Entities) entitySnapshots[entity.Id] = entity;
            dirtyEntities.Clear();
            repository.WriteSaveData(saveData);
            detachedChunksPendingSave = false;
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
            deferredFrontierSnapshots.Remove(coordinate);
                return;
            }

            var copied = new List<CellCoordinate>(values);
            copied.Sort();
            deferredWaterFrontierByChunk[coordinate] = copied;
            deferredFrontierSnapshots[coordinate] = copied.ToArray();
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
