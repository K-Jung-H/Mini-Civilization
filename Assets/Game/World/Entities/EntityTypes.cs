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

    public readonly struct WeightedState<TState>
        where TState : struct, Enum
    {
        public TState State { get; }
        public int Weight { get; }

        public WeightedState(TState state, int weight)
        {
            if (weight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(weight));
            }

            State = state;
            Weight = weight;
        }
    }

    public sealed class AnimalMovementRules
    {
        private static readonly CellOffset[] CardinalDirections =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 0, 1),
            new(0, 0, -1)
        };

        private readonly CellOffset[] neighborOffsets;

        public IReadOnlyList<CellOffset> NeighborOffsets => neighborOffsets;
        public int MaxCellYDifference { get; }

        public AnimalMovementRules(
            IReadOnlyList<CellOffset> neighborOffsets,
            int maxCellYDifference)
        {
            if (neighborOffsets == null || neighborOffsets.Count == 0)
            {
                throw new ArgumentException(
                    "Animal movement requires at least one neighbor offset.",
                    nameof(neighborOffsets));
            }

            if (maxCellYDifference < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxCellYDifference));
            }

            this.neighborOffsets = new CellOffset[neighborOffsets.Count];
            var uniqueOffsets = new HashSet<CellOffset>();
            for (var index = 0; index < neighborOffsets.Count; index++)
            {
                var offset = neighborOffsets[index];
                if (offset == default || !uniqueOffsets.Add(offset))
                {
                    throw new ArgumentException(
                        "Animal neighbor offsets must be unique and non-zero.",
                        nameof(neighborOffsets));
                }

                this.neighborOffsets[index] = offset;
            }

            MaxCellYDifference = maxCellYDifference;
        }

        public static AnimalMovementRules Cardinal4(
            int maxCellYDifference) =>
            new(CardinalDirections, maxCellYDifference);

        internal bool Allows(
            CellCoordinate current,
            CellCoordinate next)
        {
            if (Math.Abs(next.Y - current.Y) > MaxCellYDifference)
            {
                return false;
            }

            var differenceX = next.X - current.X;
            var differenceZ = next.Z - current.Z;
            for (var index = 0; index < neighborOffsets.Length; index++)
            {
                var offset = neighborOffsets[index];
                if (offset.X == differenceX && offset.Z == differenceZ)
                {
                    return true;
                }
            }

            return false;
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

            var differenceX = destination.X - AnchorCell.X;
            var differenceZ = destination.Z - AnchorCell.Z;
            if (differenceX > 0 && differenceZ == 0)
            {
                Data.SetDirection(EntityDirection.East);
            }
            else if (differenceX < 0 && differenceZ == 0)
            {
                Data.SetDirection(EntityDirection.West);
            }
            else if (differenceZ > 0 && differenceX == 0)
            {
                Data.SetDirection(EntityDirection.North);
            }
            else if (differenceZ < 0 && differenceX == 0)
            {
                Data.SetDirection(EntityDirection.South);
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
        private static readonly AnimalMovementRules DefaultMovementRules =
            AnimalMovementRules.Cardinal4(1);

        private uint randomState;

        protected AnimalEntity(EntityData data) : base(data)
        {
            var seed = data.Id.Value
                ^ ((ulong)data.TypeKey.Value << 32)
                ^ ((ulong)data.TypeKey.Category << 56);
            randomState = (uint)(seed ^ (seed >> 32));
            if (randomState == 0)
            {
                randomState = 0x9E3779B9u;
            }
        }

        internal sealed override void Tick(
            EntityRuntime runtime,
            float deltaTime)
        {
            UpdateState(runtime, deltaTime);
        }

        public sealed override bool CanEnterWorld(
            WorldRuntime runtime,
            CellCoordinate currentCell,
            CellCoordinate nextCell)
        {
            var rules = ResolveMovementRules()
                ?? throw new InvalidOperationException(
                    $"Animal {Id} does not define movement rules.");
            return rules.Allows(currentCell, nextCell)
                && CanEnterAdditional(
                    runtime,
                    currentCell,
                    nextCell);
        }

        protected abstract void UpdateState(
            EntityRuntime runtime,
            float deltaTime);

        protected virtual AnimalMovementRules ResolveMovementRules() =>
            DefaultMovementRules;

        protected virtual bool CanEnterAdditional(
            WorldRuntime runtime,
            CellCoordinate currentCell,
            CellCoordinate nextCell) => true;

        protected virtual bool TrySelectMoveDestination(
            EntityRuntime runtime,
            out CellCoordinate destination)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            var rules = ResolveMovementRules()
                ?? throw new InvalidOperationException(
                    $"Animal {Id} does not define movement rules.");
            var worldRuntime = runtime.WorldRuntime;
            var selected = default(CellCoordinate);
            var candidateCount = 0;
            for (var index = 0; index < rules.NeighborOffsets.Count; index++)
            {
                var offset = rules.NeighborOffsets[index];
                var x = AnchorCell.X + offset.X;
                var z = AnchorCell.Z + offset.Z;
                if (!worldRuntime.Data.ContainsColumn(x, z))
                {
                    continue;
                }

                var surface = worldRuntime.SurfaceCache.GetSurfaceHeight(x, z);
                if (!surface.HasGround)
                {
                    continue;
                }

                var candidate = new CellCoordinate(
                    x,
                    surface.GroundCellY,
                    z);
                if (!runtime.CanEnter(this, AnchorCell, candidate))
                {
                    continue;
                }

                candidateCount++;
                if (NextRandom(candidateCount) == 0)
                {
                    selected = candidate;
                }
            }

            destination = selected;
            return candidateCount > 0;
        }

        protected TState SelectWeightedState<TState>(
            IReadOnlyList<WeightedState<TState>> states)
            where TState : struct, Enum
        {
            if (states == null || states.Count == 0)
            {
                throw new ArgumentException(
                    "Animal state weights cannot be empty.",
                    nameof(states));
            }

            var totalWeight = 0;
            for (var index = 0; index < states.Count; index++)
            {
                totalWeight = checked(totalWeight + states[index].Weight);
            }

            var selectedWeight = NextRandom(totalWeight);
            for (var index = 0; index < states.Count; index++)
            {
                var state = states[index];
                if (selectedWeight < state.Weight)
                {
                    return state.State;
                }

                selectedWeight -= state.Weight;
            }

            throw new InvalidOperationException(
                "Animal state weight selection failed.");
        }

        private int NextRandom(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMaximum));
            }

            var state = randomState;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            randomState = state;
            return (int)(state % (uint)exclusiveMaximum);
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
