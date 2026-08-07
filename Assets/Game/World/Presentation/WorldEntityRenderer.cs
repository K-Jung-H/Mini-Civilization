using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Runtime;
using UnityEngine;
using WorldEntityId = MiniCivilization.World.Domain.EntityId;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldEntityRenderer : MonoBehaviour
    {
        [SerializeField] private Transform entityRoot;

        private readonly Dictionary<WorldEntityId, EntityController> viewsByEntityId = new();
        private readonly List<Entity> entities = new();
        private readonly HashSet<WorldEntityId> pendingEntityIds = new();
        private WorldRuntime runtime;
        private EntityCatalog catalog;

        public WorldRuntime Runtime => runtime;
        public EntityCatalog Catalog => catalog;
        public int ViewCount => viewsByEntityId.Count;

        public void Configure(Transform root)
        {
            entityRoot = root;
        }

        private void OnDestroy()
        {
            Unbind();
        }

        public void Bind(
            WorldRuntime worldRuntime,
            EntityCatalog entityCatalog)
        {
            if (worldRuntime == null)
            {
                throw new ArgumentNullException(nameof(worldRuntime));
            }

            if (entityCatalog == null)
            {
                throw new ArgumentNullException(nameof(entityCatalog));
            }

            Unbind();
            entityCatalog.ValidateCatalog();
            runtime = worldRuntime;
            catalog = entityCatalog;
            runtime.Entities.Changed += OnEntitiesChanged;

            runtime.Entities.CopyEntitiesTo(entities);
            for (var index = 0; index < entities.Count; index++)
            {
                SynchronizeEntity(entities[index]);
            }
        }

        public void Unbind()
        {
            if (runtime != null)
            {
                runtime.Entities.Changed -= OnEntitiesChanged;
                runtime = null;
            }

            catalog = null;

            foreach (var view in viewsByEntityId.Values)
            {
                if (view == null)
                {
                    continue;
                }

                view.Unbind();
                if (Application.isPlaying)
                {
                    Destroy(view.gameObject);
                }
                else
                {
                    DestroyImmediate(view.gameObject);
                }
            }

            viewsByEntityId.Clear();
            entities.Clear();
            pendingEntityIds.Clear();
        }

        private void OnEntitiesChanged(EntityChangeSet changeSet)
        {
            if (runtime == null || changeSet == null
                || !ReferenceEquals(changeSet.World, runtime.Data))
            {
                return;
            }

            for (var index = 0; index < changeSet.RemovedEntityIds.Count; index++)
            {
                RemoveView(changeSet.RemovedEntityIds[index]);
            }

            pendingEntityIds.Clear();
            AddChangedEntities(changeSet.AddedEntityIds);
            AddChangedEntities(changeSet.MovedEntityIds);
            AddEntitiesAtAffectedCells(changeSet.AffectedCellIndices);

            foreach (var id in pendingEntityIds)
            {
                if (runtime.Entities.TryGet(id, out var entity))
                {
                    SynchronizeEntity(entity);
                }
            }
        }

        private void AddChangedEntities(IReadOnlyList<WorldEntityId> ids)
        {
            for (var index = 0; index < ids.Count; index++)
            {
                pendingEntityIds.Add(ids[index]);
            }
        }

        private void AddEntitiesAtAffectedCells(IReadOnlyList<int> cellIndices)
        {
            for (var index = 0; index < cellIndices.Count; index++)
            {
                var coordinate = WorldIndex.DecodeCell(runtime.Data, cellIndices[index]);
                var ids = runtime.Entities.GetEntitiesAt(coordinate);
                for (var entityIndex = 0; entityIndex < ids.Count; entityIndex++)
                {
                    pendingEntityIds.Add(ids[entityIndex]);
                }
            }
        }

        private void SynchronizeEntity(Entity entity)
        {
            if (!ShouldRender(entity))
            {
                RemoveView(entity.Id);
                return;
            }

            if (viewsByEntityId.TryGetValue(entity.Id, out var existing))
            {
                existing.Refresh();
                return;
            }

            var definition = catalog.GetDefinition(entity.TypeId);
            var prefab = definition.Prefab;

            var parent = entityRoot != null ? entityRoot : transform;
            var view = Instantiate(prefab, parent, false);
            try
            {
                view.name = $"{definition.DisplayName} [{entity.Id}]";
                view.Bind(entity);
                viewsByEntityId.Add(entity.Id, view);
            }
            catch
            {
                if (Application.isPlaying)
                {
                    Destroy(view.gameObject);
                }
                else
                {
                    DestroyImmediate(view.gameObject);
                }

                throw;
            }
        }

        private bool ShouldRender(Entity entity)
        {
            if (entity is BuildingEntity)
            {
                return true;
            }

            var ids = runtime.Entities.GetEntitiesAt(entity.AnchorCell);
            for (var index = 0; index < ids.Count; index++)
            {
                if (ids[index].CompareTo(entity.Id) >= 0
                    || !runtime.Entities.TryGet(ids[index], out var other)
                    || other.TypeId != entity.TypeId)
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private void RemoveView(WorldEntityId id)
        {
            if (!viewsByEntityId.Remove(id, out var view) || view == null)
            {
                return;
            }

            view.Unbind();
            if (Application.isPlaying)
            {
                Destroy(view.gameObject);
            }
            else
            {
                DestroyImmediate(view.gameObject);
            }
        }
    }
}
