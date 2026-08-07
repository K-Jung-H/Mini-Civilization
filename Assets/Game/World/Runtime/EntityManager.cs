using System;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Presentation;
using UnityEngine;

namespace MiniCivilization.World.Runtime
{
    [DisallowMultipleComponent]
    public sealed class EntityManager : MonoBehaviour
    {
        [SerializeField] private EntityCatalog entityCatalog;
        [SerializeField] private WorldEntityRenderer entityRenderer;

        private WorldRuntime runtime;
        private EntityRuntime entities;

        public EntityCatalog Catalog => entityCatalog;
        public WorldRuntime Runtime => runtime;
        public EntityRuntime Entities => entities;

        public event Action<EntityChangeSet> Changed;

        public void Configure(
            EntityCatalog catalog,
            WorldEntityRenderer renderer)
        {
            entityCatalog = catalog;
            entityRenderer = renderer;
        }

        public void ConfigureEntityTypes()
        {
            EntityTypeRegistry.Shared.Clear();
            if (entityCatalog != null)
            {
                entityCatalog.RegisterEntityTypes(EntityTypeRegistry.Shared);
            }
        }

        public void Bind(WorldRuntime worldRuntime)
        {
            if (worldRuntime == null)
            {
                throw new ArgumentNullException(nameof(worldRuntime));
            }

            if (entityCatalog == null && worldRuntime.Data.Entities.Count != 0)
            {
                throw new MissingReferenceException(
                    "EntityManager requires an Entity Catalog when the world contains entities.");
            }

            Unbind();
            runtime = worldRuntime;
            entities = worldRuntime.Entities;
            try
            {
                entities.Changed += OnEntitiesChanged;
                if (entityRenderer != null && entityCatalog != null)
                {
                    entityRenderer.Bind(worldRuntime, entityCatalog);
                }
            }
            catch
            {
                Unbind();
                throw;
            }
        }

        public void Unbind()
        {
            if (entities != null)
            {
                entities.Changed -= OnEntitiesChanged;
                entities = null;
            }

            entityRenderer?.Unbind();
            runtime = null;
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void OnEntitiesChanged(EntityChangeSet changeSet)
        {
            Changed?.Invoke(changeSet);
        }
    }
}
