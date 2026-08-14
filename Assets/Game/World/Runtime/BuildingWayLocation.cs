using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Runtime
{
    internal readonly struct BuildingWayLocation :
        IEquatable<BuildingWayLocation>
    {
        public readonly EntityId BuildingId;
        public readonly int LocalPointIndex;

        public BuildingWayLocation(
            EntityId buildingId,
            int localPointIndex)
        {
            BuildingId = buildingId;
            LocalPointIndex = localPointIndex;
        }

        public bool Equals(BuildingWayLocation other) =>
            BuildingId == other.BuildingId
            && LocalPointIndex == other.LocalPointIndex;

        public override bool Equals(object obj) =>
            obj is BuildingWayLocation other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            BuildingId,
            LocalPointIndex);
    }
}
