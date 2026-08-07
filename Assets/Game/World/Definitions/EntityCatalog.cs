using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Presentation;
using UnityEngine;

namespace MiniCivilization.World.Definitions
{
    [Serializable]
    public sealed class EntityDefinition
    {
        [SerializeField] private EntityController prefab;
        [SerializeField] private Sprite thumbnail;
        [SerializeField] private string displayName;
        [SerializeField, HideInInspector] private ushort entityTypeId;
        [SerializeField, HideInInspector] private string entityClassName;

        public EntityController Prefab => prefab;
        public Sprite Thumbnail => thumbnail;
        public string DisplayName => displayName;
        public EntityTypeId TypeId => new(entityTypeId);
        public string EntityClassName => entityClassName;

        internal EntityDefinition(EntityTypeId typeId, Type entityClass)
        {
            entityTypeId = typeId.Value;
            entityClassName = entityClass?.AssemblyQualifiedName;
            displayName = ToDisplayName(entityClass);
        }

        public Type ResolveEntityClass()
        {
            return string.IsNullOrWhiteSpace(entityClassName)
                ? null
                : Type.GetType(entityClassName, throwOnError: false);
        }

        private static string ToDisplayName(Type entityClass)
        {
            if (entityClass == null)
            {
                return string.Empty;
            }

            const string entitySuffix = "Entity";
            return entityClass.Name.EndsWith(
                entitySuffix,
                StringComparison.Ordinal)
                ? entityClass.Name.Substring(
                    0,
                    entityClass.Name.Length - entitySuffix.Length)
                : entityClass.Name;
        }
    }

    [Serializable]
    public sealed class EntityDefinitionGroup
    {
        [SerializeField] private List<EntityDefinition> definitions = new();

        public IReadOnlyList<EntityDefinition> Definitions => definitions;

        internal void Add(EntityDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            definitions.Add(definition);
        }

        internal bool Remove(EntityTypeId typeId)
        {
            for (var index = 0; index < definitions.Count; index++)
            {
                if (definitions[index].TypeId == typeId)
                {
                    definitions.RemoveAt(index);
                    return true;
                }
            }

            return false;
        }
    }

    [CreateAssetMenu(fileName = "EntityCatalog", menuName = "Mini Civilization/Entity Catalog")]
    public sealed class EntityCatalog : ScriptableObject
    {
        [SerializeField] private EntityDefinitionGroup nature = new();
        [SerializeField] private EntityDefinitionGroup animals = new();
        [SerializeField] private EntityDefinitionGroup humans = new();
        [SerializeField] private EntityDefinitionGroup buildings = new();
        [SerializeField, HideInInspector, Min(1)] private ushort nextEntityTypeId = 1;

        private readonly Dictionary<EntityTypeId, EntityDefinition>
            definitionsByTypeId = new();
        private bool runtimeIndexValid;

        public IReadOnlyList<EntityDefinition> NatureDefinitions => nature.Definitions;
        public IReadOnlyList<EntityDefinition> AnimalDefinitions => animals.Definitions;
        public IReadOnlyList<EntityDefinition> HumanDefinitions => humans.Definitions;
        public IReadOnlyList<EntityDefinition> BuildingDefinitions => buildings.Definitions;

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
            return GetGroup(category).Definitions;
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
                registry.Register(
                    pair.Key,
                    pair.Value.ResolveEntityClass());
            }
        }

        public EntityDefinition AddDefinition(
            EntityCategory category,
            Type entityClass)
        {
            ValidateEntityClass(category, entityClass);
            if (FindDefinitionByClass(entityClass) != null)
            {
                throw new InvalidOperationException(
                    $"Entity class '{entityClass.FullName}' is already in catalog '{name}'.");
            }

            var definition = new EntityDefinition(
                AllocateEntityTypeId(),
                entityClass);
            GetGroup(category).Add(definition);
            runtimeIndexValid = false;
            return definition;
        }

        public bool RemoveDefinition(EntityTypeId typeId)
        {
            if (!typeId.IsValid)
            {
                return false;
            }

            foreach (var category in Categories)
            {
                if (GetGroup(category).Remove(typeId))
                {
                    runtimeIndexValid = false;
                    return true;
                }
            }

            return false;
        }

        public void InvalidateRuntimeIndex()
        {
            runtimeIndexValid = false;
        }

        private void EnsureRuntimeIndex()
        {
            if (runtimeIndexValid)
            {
                return;
            }

            definitionsByTypeId.Clear();
            var entityClasses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var category in Categories)
            {
                var definitions = GetGroup(category).Definitions;
                for (var index = 0; index < definitions.Count; index++)
                {
                    var definition = definitions[index];
                    if (definition == null)
                    {
                        throw new InvalidOperationException(
                            $"Entity catalog '{name}' contains an empty {category} definition.");
                    }

                    ValidateDefinition(category, definition);
                    if (!definitionsByTypeId.TryAdd(definition.TypeId, definition))
                    {
                        throw new InvalidOperationException(
                            $"Entity type ID {definition.TypeId} is duplicated in catalog '{name}'.");
                    }

                    if (!entityClasses.Add(definition.EntityClassName))
                    {
                        throw new InvalidOperationException(
                            $"Entity class '{definition.EntityClassName}' is duplicated in catalog '{name}'.");
                    }
                }
            }

            runtimeIndexValid = true;
        }

        private void ValidateDefinition(
            EntityCategory category,
            EntityDefinition definition)
        {
            if (!definition.TypeId.IsValid)
            {
                throw new InvalidOperationException(
                    $"Entity catalog '{name}' contains an unassigned type ID.");
            }

            var entityClass = definition.ResolveEntityClass();
            ValidateEntityClass(category, entityClass);
            if (definition.Prefab == null)
            {
                throw new InvalidOperationException(
                    $"Entity type {definition.TypeId} has no Prefab.");
            }

            if (definition.Prefab.Category != category
                || !definition.Prefab.SupportsEntityType(entityClass))
            {
                throw new InvalidOperationException(
                    $"Entity type {definition.TypeId} does not match its {category} Controller Prefab.");
            }
        }

        private static void ValidateEntityClass(
            EntityCategory category,
            Type entityClass)
        {
            if (entityClass == null
                || !entityClass.IsSealed
                || !EntityCategoryInfo.Supports(category, entityClass))
            {
                throw new ArgumentException(
                    $"The selected type must be a sealed {category} Entity class.",
                    nameof(entityClass));
            }

            if (entityClass.GetConstructor(new[] { typeof(EntityData) }) == null)
            {
                throw new ArgumentException(
                    $"Entity type '{entityClass.FullName}' must define a public " +
                    "constructor that accepts EntityData.",
                    nameof(entityClass));
            }
        }

        private EntityDefinition FindDefinitionByClass(Type entityClass)
        {
            var className = entityClass.AssemblyQualifiedName;
            foreach (var category in Categories)
            {
                var definitions = GetGroup(category).Definitions;
                for (var index = 0; index < definitions.Count; index++)
                {
                    if (definitions[index]?.EntityClassName == className)
                    {
                        return definitions[index];
                    }
                }
            }

            return null;
        }

        private EntityTypeId AllocateEntityTypeId()
        {
            if (nextEntityTypeId == 0)
            {
                throw new InvalidOperationException(
                    $"Entity catalog '{name}' has exhausted Entity Type IDs.");
            }

            var typeId = new EntityTypeId(nextEntityTypeId);
            nextEntityTypeId = nextEntityTypeId == ushort.MaxValue
                ? (ushort)0
                : (ushort)(nextEntityTypeId + 1);
            return typeId;
        }

        private EntityDefinitionGroup GetGroup(EntityCategory category)
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
