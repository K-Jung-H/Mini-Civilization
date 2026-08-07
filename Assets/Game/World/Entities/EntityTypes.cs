using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities
{
    public enum EntityCategory : byte
    {
        Nature,
        Animal,
        Human,
        Building
    }

    public static class EntityCategoryInfo
    {
        public static Type GetEntityBaseType(EntityCategory category)
        {
            return category switch
            {
                EntityCategory.Nature => typeof(NatureEntity),
                EntityCategory.Animal => typeof(AnimalEntity),
                EntityCategory.Human => typeof(HumanEntity),
                EntityCategory.Building => typeof(BuildingEntity),
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
        }

        public static bool Supports(
            EntityCategory category,
            Type entityType)
        {
            return entityType != null
                && !entityType.IsAbstract
                && GetEntityBaseType(category).IsAssignableFrom(entityType);
        }
    }

    public abstract class Entity
    {
        protected Entity(EntityData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public EntityData Data { get; }
        public EntityId Id => Data.Id;
        public EntityTypeId TypeId => Data.TypeId;
        public CellCoordinate AnchorCell => Data.AnchorCell;
        public EntityDirection Direction => Data.Direction;
    }

    public abstract class FixedEntity : Entity
    {
        protected FixedEntity(EntityData data) : base(data)
        {
        }
    }

    public abstract class DynamicEntity : Entity
    {
        protected DynamicEntity(EntityData data) : base(data)
        {
        }

        public abstract bool CanEnterWorld(
            WorldRuntime runtime,
            CellCoordinate currentCell,
            CellCoordinate nextCell);
    }

    public abstract class AnimalEntity : DynamicEntity
    {
        protected AnimalEntity(EntityData data) : base(data)
        {
        }
    }

    public abstract class HumanEntity : DynamicEntity
    {
        protected HumanEntity(EntityData data) : base(data)
        {
        }
    }

    public abstract class NatureEntity : FixedEntity
    {
        protected NatureEntity(EntityData data) : base(data)
        {
        }
    }

    public abstract class BuildingEntity : FixedEntity
    {
        protected BuildingEntity(EntityData data) : base(data)
        {
        }

        public abstract BuildingLayout Layout { get; }

        public abstract bool ValidatePlacement(
            in BuildingPlacementContext context);
    }

    public sealed class EntityTypeRegistry
    {
        private readonly Dictionary<EntityTypeId, Func<EntityData, Entity>>
            factories = new();

        public static EntityTypeRegistry Shared { get; } = new();

        public void Clear()
        {
            factories.Clear();
        }

        public void Register(
            EntityTypeId typeId,
            Func<EntityData, Entity> factory)
        {
            if (!typeId.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(typeId));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (!factories.TryAdd(typeId, factory))
            {
                throw new InvalidOperationException(
                    $"Entity type ID {typeId} is already registered.");
            }
        }

        public void Register(EntityTypeId typeId, Type entityType)
        {
            if (entityType == null)
            {
                throw new ArgumentNullException(nameof(entityType));
            }

            if (!typeof(Entity).IsAssignableFrom(entityType)
                || entityType.IsAbstract
                || !entityType.IsSealed)
            {
                throw new ArgumentException(
                    $"Entity type '{entityType.FullName}' must be a sealed " +
                    "Entity class.",
                    nameof(entityType));
            }

            var constructor = entityType.GetConstructor(new[] { typeof(EntityData) });
            if (constructor == null)
            {
                throw new ArgumentException(
                    $"Entity type '{entityType.FullName}' must define a public " +
                    "constructor that accepts EntityData.",
                    nameof(entityType));
            }

            Register(typeId, data =>
            {
                return (Entity)constructor.Invoke(new object[] { data });
            });
        }

        public Entity Create(EntityData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (!factories.TryGetValue(data.TypeId, out var factory))
            {
                throw new InvalidOperationException(
                    $"Entity type ID {data.TypeId} is not registered.");
            }

            var entity = factory(data);
            if (entity == null || !ReferenceEquals(entity.Data, data))
            {
                throw new InvalidOperationException(
                    $"Entity factory for type ID {data.TypeId} returned an invalid entity.");
            }

            return entity;
        }
    }
}
