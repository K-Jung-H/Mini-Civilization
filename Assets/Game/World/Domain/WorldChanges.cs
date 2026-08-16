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
        public IReadOnlyList<CellCoordinate> ChangedCells { get; }
        public IReadOnlyList<CellColumnCoordinate> ChangedColumns { get; }
        public IReadOnlyList<ChunkCoordinate> AffectedChunks { get; }
        public CellBounds AffectedBounds { get; }

        internal WorldChangeSet(
            WorldData world,
            WorldChangeId changeId,
            WorldChangeType changeTypes,
            IReadOnlyList<CellCoordinate> changedCells,
            IReadOnlyList<CellColumnCoordinate> changedColumns,
            IReadOnlyList<ChunkCoordinate> affectedChunks,
            CellBounds affectedBounds)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            ChangeId = changeId;
            ChangeTypes = changeTypes;
            ChangedCells = changedCells
                ?? throw new ArgumentNullException(nameof(changedCells));
            ChangedColumns = changedColumns
                ?? throw new ArgumentNullException(nameof(changedColumns));
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
        public IReadOnlyList<CellCoordinate> AffectedCells { get; }
        public IReadOnlyList<ChunkCoordinate> AffectedChunks { get; }
        public bool WayTopologyChanged { get; }

        internal EntityChangeSet(
            WorldData world,
            WorldChangeId changeId,
            IReadOnlyList<EntityId> addedEntityIds,
            IReadOnlyList<EntityId> removedEntityIds,
            IReadOnlyList<EntityId> movedEntityIds,
            IReadOnlyList<CellCoordinate> affectedCells,
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
            AffectedCells = affectedCells
                ?? throw new ArgumentNullException(nameof(affectedCells));
            AffectedChunks = affectedChunks
                ?? throw new ArgumentNullException(nameof(affectedChunks));
            WayTopologyChanged = wayTopologyChanged;
        }
    }

}
