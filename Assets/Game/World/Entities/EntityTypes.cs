using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities
{
    public enum EntityMoveType : byte
    {
        Walk,
        Jump
    }

    public abstract class Entity
    {
        protected Entity(EntityData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        public EntityData Data { get; }
        public EntityId Id => Data.Id;
        public EntityTypeKey TypeKey => Data.TypeKey;
        public CellCoordinate AnchorCell => Data.AnchorCell;
        public EntityDirection Direction => Data.Direction;
        public abstract int RenderStateKey { get; }
        internal abstract bool RequiresTick { get; }

        internal abstract void Tick(
            EntityRuntime runtime,
            float deltaTime);
    }

    public abstract class FixedEntity : Entity
    {
        protected FixedEntity(EntityData data) : base(data)
        {
        }
    }

    public abstract class DynamicEntity : Entity
    {
        public bool IsMoving { get; private set; }
        public CellCoordinate MoveFrom { get; private set; }
        public CellCoordinate MoveTo { get; private set; }
        public float MoveProgress { get; private set; }
        public EntityMoveType MoveType { get; private set; }

        protected DynamicEntity(EntityData data) : base(data)
        {
        }

        internal sealed override bool RequiresTick => true;

        public abstract bool CanEnterWorld(
            WorldRuntime runtime,
            CellCoordinate currentCell,
            CellCoordinate nextCell);

        protected bool TryBeginMove(
            EntityRuntime runtime,
            CellCoordinate destination,
            EntityMoveType moveType = EntityMoveType.Walk)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            return runtime.TryBeginMove(this, destination, moveType);
        }

        protected bool AdvanceMove(
            EntityRuntime runtime,
            float progressDelta)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (!IsMoving)
            {
                return false;
            }

            if (progressDelta < 0f
                || float.IsNaN(progressDelta)
                || float.IsInfinity(progressDelta))
            {
                throw new ArgumentOutOfRangeException(nameof(progressDelta));
            }

            MoveProgress = Math.Clamp(
                MoveProgress + progressDelta,
                0f,
                1f);
            if (MoveProgress < 1f)
            {
                return false;
            }

            runtime.CompleteMove(this);
            return true;
        }

        internal void BeginMove(
            CellCoordinate destination,
            EntityMoveType moveType)
        {
            if (IsMoving)
            {
                throw new InvalidOperationException(
                    $"Entity {Id} is already moving.");
            }

            MoveFrom = AnchorCell;
            MoveTo = destination;
            MoveProgress = 0f;
            MoveType = moveType;
            IsMoving = true;
        }

        internal void FinishMove()
        {
            if (!IsMoving)
            {
                throw new InvalidOperationException(
                    $"Entity {Id} is not moving.");
            }

            MoveProgress = 1f;
            IsMoving = false;
        }
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
        private readonly Dictionary<EntityTypeKey, Func<EntityData, Entity>>
            factories = new();

        public static EntityTypeRegistry Shared { get; } = new();

        public void Clear()
        {
            factories.Clear();
        }

        public void Register(
            EntityTypeKey typeKey,
            Func<EntityData, Entity> factory)
        {
            if (!typeKey.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(typeKey));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (!factories.TryAdd(typeKey, factory))
            {
                throw new InvalidOperationException(
                    $"Entity type key {typeKey} is already registered.");
            }
        }

        public Entity Create(EntityData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (!factories.TryGetValue(data.TypeKey, out var factory))
            {
                throw new InvalidOperationException(
                    $"Entity type key {data.TypeKey} is not registered.");
            }

            var entity = factory(data);
            if (entity == null
                || !ReferenceEquals(entity.Data, data)
                || entity.TypeKey != data.TypeKey)
            {
                throw new InvalidOperationException(
                    $"Entity factory for type key {data.TypeKey} returned an invalid entity.");
            }

            return entity;
        }
    }
}
