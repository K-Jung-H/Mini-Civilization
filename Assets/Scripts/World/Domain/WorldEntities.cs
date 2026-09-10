using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MiniCivilization.World.Domain
{
    public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
    {
        public static readonly EntityId None = new(0);

        public ulong Value { get; }

        public EntityId(ulong value)
        {
            Value = value;
        }

        public bool IsValid => Value != 0;
        public int CompareTo(EntityId other) => Value.CompareTo(other.Value);
        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) =>
            obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator ==(EntityId left, EntityId right) =>
            left.Equals(right);
        public static bool operator !=(EntityId left, EntityId right) =>
            !left.Equals(right);
    }

    public enum EntityCategory : byte
    {
        Nature,
        Animal,
        Human,
        Building
    }

    public readonly struct EntityTypeKey : IEquatable<EntityTypeKey>
    {
        public static readonly EntityTypeKey None = default;

        public EntityCategory Category { get; }
        public ushort Value { get; }

        public EntityTypeKey(EntityCategory category, ushort value)
        {
            Category = category;
            Value = value;
        }

        public bool IsValid => Value != 0
            && Category is EntityCategory.Nature
                or EntityCategory.Animal
                or EntityCategory.Human
                or EntityCategory.Building;
        public bool Equals(EntityTypeKey other) =>
            Category == other.Category && Value == other.Value;
        public override bool Equals(object obj) =>
            obj is EntityTypeKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Category, Value);
        public override string ToString() => $"{Category}:{Value}";

        public static bool operator ==(EntityTypeKey left, EntityTypeKey right) =>
            left.Equals(right);
        public static bool operator !=(EntityTypeKey left, EntityTypeKey right) =>
            !left.Equals(right);
    }

    public enum EntityDirection : byte
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    public readonly struct CellOffset : IEquatable<CellOffset>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public CellOffset(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(CellOffset other) =>
            X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) =>
            obj is CellOffset other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";

        public static bool operator ==(CellOffset left, CellOffset right) =>
            left.Equals(right);
        public static bool operator !=(CellOffset left, CellOffset right) =>
            !left.Equals(right);
    }

    public sealed class EntityData
    {
        public EntityId Id { get; }
        public EntityTypeKey TypeKey { get; }
        public CellCoordinate AnchorCell { get; private set; }
        public EntityDirection Direction { get; private set; }
        public EntityAttributes Attributes { get; }

        public EntityData(
            EntityId id,
            EntityTypeKey typeKey,
            CellCoordinate anchorCell,
            EntityDirection direction = EntityDirection.North,
            EntityAttributes attributes = null)
        {
            if (!id.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(id));
            }

            if (!typeKey.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(typeKey));
            }

            if (!Enum.IsDefined(typeof(EntityDirection), direction))
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            Id = id;
            TypeKey = typeKey;
            AnchorCell = anchorCell;
            Direction = direction;
            Attributes = attributes ?? EntityAttributes.Empty;
        }

        internal void MoveTo(CellCoordinate anchorCell) => AnchorCell = anchorCell;

        internal void SetDirection(EntityDirection direction)
        {
            if (!Enum.IsDefined(typeof(EntityDirection), direction))
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }

            Direction = direction;
        }
    }

    public readonly struct EntityTrait
    {
        public string Id { get; }
        public float Value { get; }

        public EntityTrait(string id, float value)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException(
                    "Entity trait ID cannot be empty.",
                    nameof(id));
            }

            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            Id = id;
            Value = value;
        }
    }

    public sealed class EntityAttributes
    {
        private static readonly EntityTrait[] NoTraits =
            Array.Empty<EntityTrait>();
        private readonly EntityTrait[] traits;
        private readonly ReadOnlyCollection<EntityTrait> readOnlyTraits;
        private readonly Dictionary<string, float> traitsById;

        public static EntityAttributes Empty { get; } =
            new(string.Empty, 0, NoTraits);

        public string Name { get; }
        public int Age { get; }
        public IReadOnlyList<EntityTrait> Traits => readOnlyTraits;

        public EntityAttributes(
            string name,
            int age,
            IReadOnlyList<EntityTrait> traits)
        {
            if (age < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(age));
            }

            Name = name ?? string.Empty;
            Age = age;
            this.traits = new EntityTrait[traits?.Count ?? 0];
            readOnlyTraits = Array.AsReadOnly(this.traits);
            traitsById = new Dictionary<string, float>(
                this.traits.Length,
                StringComparer.Ordinal);
            for (var index = 0; index < this.traits.Length; index++)
            {
                var trait = traits[index];
                if (!traitsById.TryAdd(trait.Id, trait.Value))
                {
                    throw new ArgumentException(
                        $"Entity trait ID '{trait.Id}' is duplicated.",
                        nameof(traits));
                }

                this.traits[index] = trait;
            }
        }

        public bool TryGetTrait(string id, out float value)
        {
            if (id == null)
            {
                value = default;
                return false;
            }

            return traitsById.TryGetValue(id, out value);
        }
    }
}
