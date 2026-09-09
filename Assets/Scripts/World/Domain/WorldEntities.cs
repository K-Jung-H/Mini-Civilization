using System;

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

        public EntityData(
            EntityId id,
            EntityTypeKey typeKey,
            CellCoordinate anchorCell,
            EntityDirection direction = EntityDirection.North)
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
}
