using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [CreateAssetMenu(fileName = "EntityCatalog", menuName = "Mini Civilization/Entity Catalog")]
    public sealed class EntityCatalog : ScriptableObject
    {
        [SerializeField] private NatureEntityContainer nature;
        [SerializeField] private AnimalEntityContainer animals;
        [SerializeField] private HumanEntityContainer humans;
        [SerializeField] private BuildingEntityContainer buildings;

        private readonly Dictionary<EntityTypeKey, EntityDefinition>
            definitionsByTypeKey = new();
        private readonly Dictionary<EntityDefinition, EntityTypeKey>
            typeKeysByDefinition = new();
        private bool runtimeIndexValid;

        private static readonly IReadOnlyList<EntityDefinition> EmptyDefinitions =
            Array.Empty<EntityDefinition>();

        public IReadOnlyList<EntityDefinition> NatureDefinitions =>
            GetDefinitions(EntityCategory.Nature);
        public IReadOnlyList<EntityDefinition> AnimalDefinitions =>
            GetDefinitions(EntityCategory.Animal);
        public IReadOnlyList<EntityDefinition> HumanDefinitions =>
            GetDefinitions(EntityCategory.Human);
        public IReadOnlyList<EntityDefinition> BuildingDefinitions =>
            GetDefinitions(EntityCategory.Building);

        private void OnEnable()
        {
            runtimeIndexValid = false;
        }

        private void OnValidate()
        {
            runtimeIndexValid = false;
        }

        public IReadOnlyList<EntityDefinition> GetDefinitions(
            EntityCategory category)
        {
            return GetContainer(category)?.Definitions ?? EmptyDefinitions;
        }

        public bool TryGetDefinition(
            EntityTypeKey typeKey,
            out EntityDefinition definition)
        {
            EnsureRuntimeIndex();
            return definitionsByTypeKey.TryGetValue(typeKey, out definition);
        }

        public EntityDefinition GetDefinition(EntityTypeKey typeKey)
        {
            if (!TryGetDefinition(typeKey, out var definition))
            {
                throw new InvalidOperationException(
                    $"Entity type key {typeKey} is not present in catalog '{name}'.");
            }

            return definition;
        }

        public bool TryGetTypeKey(
            EntityDefinition definition,
            out EntityTypeKey typeKey)
        {
            EnsureRuntimeIndex();
            if (definition == null)
            {
                typeKey = EntityTypeKey.None;
                return false;
            }

            return typeKeysByDefinition.TryGetValue(definition, out typeKey);
        }

        public EntityTypeKey GetTypeKey(EntityDefinition definition)
        {
            if (!TryGetTypeKey(definition, out var typeKey))
            {
                throw new InvalidOperationException(
                    $"Entity definition '{definition?.name}' is not present in catalog '{name}'.");
            }

            return typeKey;
        }

        public void ValidateCatalog()
        {
            runtimeIndexValid = false;
            EnsureRuntimeIndex();
        }

        public void RegisterEntityFactories(EntityTypeRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            EnsureRuntimeIndex();
            foreach (var pair in definitionsByTypeKey)
            {
                registry.Register(
                    pair.Key,
                    pair.Value.Prefab.CreateStateMachine);
            }
        }

        private void EnsureRuntimeIndex()
        {
            if (runtimeIndexValid)
            {
                return;
            }

            definitionsByTypeKey.Clear();
            typeKeysByDefinition.Clear();

            foreach (var category in Categories)
            {
                var container = GetContainer(category);
                if (container == null)
                {
                    continue;
                }

                for (var index = 0; index < container.Definitions.Count; index++)
                {
                    var definition = container.Definitions[index];
                    ValidateDefinition(category, definition);
                    var typeKey = definition.Prefab.TypeKey;
                    if (!typeKeysByDefinition.TryAdd(definition, typeKey))
                    {
                        throw new InvalidOperationException(
                            $"Entity definition '{definition.name}' is listed more than once in catalog '{name}'.");
                    }

                    if (!definitionsByTypeKey.TryAdd(typeKey, definition))
                    {
                        throw new InvalidOperationException(
                            $"Entity type key '{typeKey}' is listed more than once in catalog '{name}'.");
                    }
                }
            }

            runtimeIndexValid = true;
        }

        private static void ValidateDefinition(
            EntityCategory category,
            EntityDefinition definition)
        {
            if (definition == null)
            {
                throw new InvalidOperationException(
                    $"Entity catalog contains an empty {category} definition.");
            }

            var prefab = definition.Prefab;
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Entity definition '{definition.name}' has no Prefab.");
            }

            if (prefab.Category != category
                || !prefab.TypeKey.IsValid
                || !prefab.HasValidEntityType
                || !prefab.HasValidVisualRoot
                || prefab.TypeKey.Category != category)
            {
                throw new InvalidOperationException(
                    $"Entity definition '{definition.name}' does not match its "
                    + $"{category} Controller Prefab or Visual Root.");
            }
        }

        private EntityDefinitionContainer GetContainer(EntityCategory category)
        {
            return category switch
            {
                EntityCategory.Nature => nature,
                EntityCategory.Animal => animals,
                EntityCategory.Human => humans,
                EntityCategory.Building => buildings,
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
        }

        private static readonly EntityCategory[] Categories =
        {
            EntityCategory.Nature,
            EntityCategory.Animal,
            EntityCategory.Human,
            EntityCategory.Building
        };
    }
}
