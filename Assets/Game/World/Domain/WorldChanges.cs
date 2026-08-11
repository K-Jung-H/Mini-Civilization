using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    public readonly struct WorldChangeId :
        IEquatable<WorldChangeId>,
        IComparable<WorldChangeId>
    {
        public static readonly WorldChangeId None = new(0);

        public ulong Value { get; }

        public WorldChangeId(ulong value)
        {
            Value = value;
        }

        public int CompareTo(WorldChangeId other) => Value.CompareTo(other.Value);
        public bool Equals(WorldChangeId other) => Value == other.Value;
        public override bool Equals(object obj) =>
            obj is WorldChangeId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();

        public static bool operator <(WorldChangeId left, WorldChangeId right) =>
            left.Value < right.Value;
        public static bool operator >(WorldChangeId left, WorldChangeId right) =>
            left.Value > right.Value;
        public static bool operator <=(WorldChangeId left, WorldChangeId right) =>
            left.Value <= right.Value;
        public static bool operator >=(WorldChangeId left, WorldChangeId right) =>
            left.Value >= right.Value;
        public static bool operator ==(WorldChangeId left, WorldChangeId right) =>
            left.Equals(right);
        public static bool operator !=(WorldChangeId left, WorldChangeId right) =>
            !left.Equals(right);
    }

    [Flags]
    public enum WorldChangeType : ushort
    {
        None = 0,
        CellStructure = 1 << 0,
        Surface = 1 << 1,
        Material = 1 << 2,
        WaterTopology = 1 << 3,
        Environment = 1 << 4,
        Navigation = 1 << 5,
        Ecology = 1 << 6,
        Occupancy = 1 << 7,
        WaterSurface = 1 << 8,
        RoadTopology = 1 << 9
    }

    public readonly struct CellBounds : IEquatable<CellBounds>
    {
        public readonly CellCoordinate Minimum;
        public readonly CellCoordinate Maximum;

        public CellBounds(CellCoordinate minimum, CellCoordinate maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }

        public bool Equals(CellBounds other) =>
            Minimum.Equals(other.Minimum) && Maximum.Equals(other.Maximum);
        public override bool Equals(object obj) =>
            obj is CellBounds other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Minimum, Maximum);
    }

    public sealed class WorldChangeSet
    {
        public WorldData World { get; }
        public WorldChangeId ChangeId { get; }
        public WorldChangeType ChangeTypes { get; }
        public IReadOnlyList<int> ChangedCellIndices { get; }
        public IReadOnlyList<int> ChangedColumnIndices { get; }
        public IReadOnlyList<ChunkCoordinate> AffectedChunks { get; }
        public CellBounds AffectedBounds { get; }

        internal WorldChangeSet(
            WorldData world,
            WorldChangeId changeId,
            WorldChangeType changeTypes,
            IReadOnlyList<int> changedCellIndices,
            IReadOnlyList<int> changedColumnIndices,
            IReadOnlyList<ChunkCoordinate> affectedChunks,
            CellBounds affectedBounds)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            ChangeId = changeId;
            ChangeTypes = changeTypes;
            ChangedCellIndices = changedCellIndices
                ?? throw new ArgumentNullException(nameof(changedCellIndices));
            ChangedColumnIndices = changedColumnIndices
                ?? throw new ArgumentNullException(nameof(changedColumnIndices));
            AffectedChunks = affectedChunks
                ?? throw new ArgumentNullException(nameof(affectedChunks));
            AffectedBounds = affectedBounds;
        }

        public bool Includes(WorldChangeType changeType) =>
            (ChangeTypes & changeType) != 0;
    }

    public sealed class EntityChangeSet
    {
        public WorldData World { get; }
        public WorldChangeId ChangeId { get; }
        public IReadOnlyList<EntityId> AddedEntityIds { get; }
        public IReadOnlyList<EntityId> RemovedEntityIds { get; }
        public IReadOnlyList<EntityId> MovedEntityIds { get; }
        public IReadOnlyList<int> AffectedCellIndices { get; }
        public IReadOnlyList<ChunkCoordinate> AffectedChunks { get; }
        public bool WayTopologyChanged { get; }

        internal EntityChangeSet(
            WorldData world,
            WorldChangeId changeId,
            IReadOnlyList<EntityId> addedEntityIds,
            IReadOnlyList<EntityId> removedEntityIds,
            IReadOnlyList<EntityId> movedEntityIds,
            IReadOnlyList<int> affectedCellIndices,
            IReadOnlyList<ChunkCoordinate> affectedChunks,
            bool wayTopologyChanged)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            ChangeId = changeId;
            AddedEntityIds = addedEntityIds
                ?? throw new ArgumentNullException(nameof(addedEntityIds));
            RemovedEntityIds = removedEntityIds
                ?? throw new ArgumentNullException(nameof(removedEntityIds));
            MovedEntityIds = movedEntityIds
                ?? throw new ArgumentNullException(nameof(movedEntityIds));
            AffectedCellIndices = affectedCellIndices
                ?? throw new ArgumentNullException(nameof(affectedCellIndices));
            AffectedChunks = affectedChunks
                ?? throw new ArgumentNullException(nameof(affectedChunks));
            WayTopologyChanged = wayTopologyChanged;
        }
    }

    public static class WorldIndex
    {
        public static int EncodeCell(WorldData world, int x, int y, int z)
        {
            if (!world.Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Cell ({x}, {y}, {z}) is outside the world.");
            }

            return x + world.Size * (z + world.Size * y);
        }

        public static CellCoordinate DecodeCell(WorldData world, int index)
        {
            var cellCount = checked(world.Size * world.Size * world.Height);
            if ((uint)index >= cellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var columnCount = world.Size * world.Size;
            var y = index / columnCount;
            var columnIndex = index - y * columnCount;
            var z = columnIndex / world.Size;
            var x = columnIndex - z * world.Size;
            return new CellCoordinate(x, y, z);
        }

        public static int EncodeColumn(WorldData world, int x, int z)
        {
            if (!world.ContainsColumn(x, z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Column ({x}, {z}) is outside the world.");
            }

            return x + world.Size * z;
        }

        public static void DecodeColumn(
            WorldData world,
            int index,
            out int x,
            out int z)
        {
            var columnCount = checked(world.Size * world.Size);
            if ((uint)index >= columnCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            z = index / world.Size;
            x = index - z * world.Size;
        }
    }
}
