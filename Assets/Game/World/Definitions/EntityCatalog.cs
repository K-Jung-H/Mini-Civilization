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

        private readonly Dictionary<EntityTypeId, EntityDefinition>
            definitionsByTypeId = new();
        private readonly Dictionary<EntityDefinition, EntityTypeId>
            typeIdsByDefinition = new();
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
            EntityTypeId typeId,
            out EntityDefinition definition)
        {
            EnsureRuntimeIndex();
            return definitionsByTypeId.TryGetValue(typeId, out definition);
        }

        public EntityDefinition GetDefinition(EntityTypeId typeId)
        {
            if (!TryGetDefinition(typeId, out var definition))
            {
                throw new InvalidOperationException(
                    $"Entity type ID {typeId} is not present in catalog '{name}'.");
            }

            return definition;
        }

        public bool TryGetTypeId(
            EntityDefinition definition,
            out EntityTypeId typeId)
        {
            EnsureRuntimeIndex();
            if (definition == null)
            {
                typeId = EntityTypeId.None;
                return false;
            }

            return typeIdsByDefinition.TryGetValue(definition, out typeId);
        }

        public EntityTypeId GetTypeId(EntityDefinition definition)
        {
            if (!TryGetTypeId(definition, out var typeId))
            {
                throw new InvalidOperationException(
                    $"Entity definition '{definition?.name}' is not present in catalog '{name}'.");
            }

            return typeId;
        }

        public void ValidateCatalog()
        {
            runtimeIndexValid = false;
            EnsureRuntimeIndex();
        }

        public void RegisterEntityTypes(EntityTypeRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            EnsureRuntimeIndex();
            foreach (var pair in definitionsByTypeId)
            {
                registry.Register(pair.Key, pair.Value.Prefab.EntityClass);
            }
        }

        private void EnsureRuntimeIndex()
        {
            if (runtimeIndexValid)
            {
                return;
            }

            definitionsByTypeId.Clear();
            typeIdsByDefinition.Clear();
            var entityClasses = new HashSet<Type>();
            var nextTypeId = (ushort)1;

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
                    if (!typeIdsByDefinition.TryAdd(
                            definition,
                            AllocateTypeId(ref nextTypeId)))
                    {
                        throw new InvalidOperationException(
                            $"Entity definition '{definition.name}' is listed more than once in catalog '{name}'.");
                    }

                    var entityClass = definition.Prefab.EntityClass;
                    if (!entityClasses.Add(entityClass))
                    {
                        throw new InvalidOperationException(
                            $"Entity class '{entityClass.FullName}' is listed more than once in catalog '{name}'.");
                    }

                    var typeId = typeIdsByDefinition[definition];
                    definitionsByTypeId.Add(typeId, definition);
                }
            }

            runtimeIndexValid = true;
        }

        private static EntityTypeId AllocateTypeId(ref ushort nextTypeId)
        {
            if (nextTypeId == 0)
            {
                throw new InvalidOperationException(
                    "Entity catalog has exhausted Entity Type IDs.");
            }

            var typeId = new EntityTypeId(nextTypeId);
            nextTypeId = nextTypeId == ushort.MaxValue
                ? (ushort)0
                : (ushort)(nextTypeId + 1);
            return typeId;
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

            var entityClass = prefab.EntityClass;
            if (prefab.Category != category
                || entityClass == null
                || !entityClass.IsSealed
                || !EntityCategoryInfo.Supports(category, entityClass)
                || entityClass.GetConstructor(new[] { typeof(EntityData) }) == null)
            {
                throw new InvalidOperationException(
                    $"Entity definition '{definition.name}' does not match its {category} Controller Prefab.");
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
