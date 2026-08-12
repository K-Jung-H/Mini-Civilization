using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities
{
    public enum EntityMoveType : byte
    {
        Walk,
        HeightTransition,
        Swim
    }

    public enum EntityActivityPhase : byte
    {
        None,
        Approach,
        Execute,
        Recover
    }

    public readonly struct EntityActivityId : IEquatable<EntityActivityId>
    {
        public static readonly EntityActivityId None = default;

        public string Name { get; }

        public EntityActivityId(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Entity activity name cannot be empty.",
                    nameof(name));
            }

            Name = name;
        }

        public bool IsValid => !string.IsNullOrEmpty(Name);
        public bool Equals(EntityActivityId other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal);
        public override bool Equals(object obj) =>
            obj is EntityActivityId other && Equals(other);
        public override int GetHashCode() =>
            Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);
        public override string ToString() => Name ?? "None";

        public static bool operator ==(
            EntityActivityId left,
            EntityActivityId right) => left.Equals(right);
        public static bool operator !=(
            EntityActivityId left,
            EntityActivityId right) => !left.Equals(right);
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
        public abstract EntityActivityId Activity { get; }
        public virtual EntityActivityPhase ActivityPhase =>
            EntityActivityPhase.None;
        public virtual EntityId InteractionTargetId => EntityId.None;
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
            CellCoordinate destination)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            return runtime.TryBeginMove(
                this,
                destination,
                ResolveMoveType(runtime, AnchorCell, destination));
        }

        protected virtual EntityMoveType ResolveMoveType(
            EntityRuntime runtime,
            CellCoordinate current,
            CellCoordinate next)
        {
            if (IsWaterSurface(runtime.WorldRuntime, current)
                || IsWaterSurface(runtime.WorldRuntime, next))
            {
                return EntityMoveType.Swim;
            }

            var currentHeight = ResolveMovementHeight(
                runtime.WorldRuntime,
                current);
            var nextHeight = ResolveMovementHeight(
                runtime.WorldRuntime,
                next);
            return nextHeight == currentHeight
                ? EntityMoveType.Walk
                : EntityMoveType.HeightTransition;
        }

        protected static bool IsWaterSurface(
            WorldRuntime runtime,
            CellCoordinate cell)
        {
            var surface = runtime.SurfaceCache.GetSurfaceHeight(
                cell.X,
                cell.Z);
            return surface.HasWater && surface.WaterCellY == cell.Y;
        }

        protected virtual float ResolveMovementHeight(
            WorldRuntime runtime,
            CellCoordinate cell)
        {
            var surface = runtime.SurfaceCache.GetSurfaceHeight(
                cell.X,
                cell.Z);
            if (surface.HasWater && surface.WaterCellY == cell.Y)
            {
                return surface.WaterHeight * runtime.Data.HeightStep;
            }

            if (surface.HasGround && surface.GroundCellY == cell.Y)
            {
                return surface.GroundHeight * runtime.Data.HeightStep;
            }

            if (!runtime.Data.TryGetCell(
                    cell.X,
                    cell.Y,
                    cell.Z,
                    out var data))
            {
                throw new InvalidOperationException(
                    $"Entity movement Cell {cell} is outside the world.");
            }

            return (cell.Y * WorldGrid.HeightStepsPerCell
                + data.Terrain.SolidHeight) * runtime.Data.HeightStep;
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
        private uint randomState;
        private readonly EntityCellMovementRules movementRules;

        protected AnimalEntity(
            EntityData data,
            EntityCellMovementRules movementRules) : base(data)
        {
            this.movementRules = movementRules
                ?? throw new ArgumentNullException(nameof(movementRules));
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
            var surface = runtime.SurfaceCache.GetSurfaceHeight(
                nextCell.X,
                nextCell.Z);
            var entersWater = surface.HasWater
                && surface.WaterCellY == nextCell.Y;
            if (surface.HasWater && !movementRules.CanEnterWater)
            {
                return false;
            }

            if (surface.HasWater
                && movementRules.CanEnterWater
                && !entersWater)
            {
                return false;
            }

            if (!entersWater
                && (!surface.HasGround
                    || surface.GroundCellY != nextCell.Y))
            {
                return false;
            }

            var currentHeight = ResolveMovementHeight(
                runtime,
                currentCell);
            var nextHeight = (entersWater
                ? surface.WaterHeight
                : surface.GroundHeight) * runtime.Data.HeightStep;
            return movementRules.Allows(
                    currentCell,
                    currentHeight,
                    nextCell,
                    nextHeight)
                && CanEnterAdditional(
                    runtime,
                    currentCell,
                    nextCell);
        }

        protected abstract void UpdateState(
            EntityRuntime runtime,
            float deltaTime);

        protected EntityCellMovementRules MovementRules => movementRules;

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

            var worldRuntime = runtime.WorldRuntime;
            var selected = default(CellCoordinate);
            var candidateCount = 0;
            for (var index = 0;
                 index < movementRules.NeighborOffsets.Count;
                 index++)
            {
                var offset = movementRules.NeighborOffsets[index];
                var x = AnchorCell.X + offset.X;
                var z = AnchorCell.Z + offset.Z;
                if (!worldRuntime.Data.ContainsColumn(x, z))
                {
                    continue;
                }

                var surface = worldRuntime.SurfaceCache.GetSurfaceHeight(x, z);
                if (!surface.HasGround
                    || surface.HasWater && !movementRules.CanEnterWater)
                {
                    continue;
                }

                var candidate = new CellCoordinate(
                    x,
                    surface.HasWater
                        ? surface.WaterCellY
                        : surface.GroundCellY,
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

        protected int SelectWeightedIndex(IReadOnlyList<int> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                throw new ArgumentException(
                    "Animal state weights cannot be empty.",
                    nameof(weights));
            }

            var totalWeight = 0;
            for (var index = 0; index < weights.Count; index++)
            {
                if (weights[index] < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(weights));
                }

                totalWeight = checked(totalWeight + weights[index]);
            }

            if (totalWeight == 0)
            {
                throw new ArgumentException(
                    "At least one animal state weight must be greater than zero.",
                    nameof(weights));
            }

            var selectedWeight = NextRandom(totalWeight);
            for (var index = 0; index < weights.Count; index++)
            {
                if (selectedWeight < weights[index])
                {
                    return index;
                }

                selectedWeight -= weights[index];
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
        public abstract int MaxTerrainCorrectionSteps { get; }

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
