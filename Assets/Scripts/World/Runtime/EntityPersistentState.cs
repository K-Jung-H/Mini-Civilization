using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using Vector3 = UnityEngine.Vector3;

namespace MiniCivilization.World.Runtime
{
    internal sealed class WayMovementPlanPersistentState
    {
        public WayMovementPlanPersistentState(
            Vector3[] graphPositions,
            bool startsAtCellCenter,
            bool endsAtCellCenter,
            bool endsInsideBuilding,
            BuildingWayLocation endLocation)
        {
            GraphPositions = graphPositions ?? Array.Empty<Vector3>();
            StartsAtCellCenter = startsAtCellCenter;
            EndsAtCellCenter = endsAtCellCenter;
            EndsInsideBuilding = endsInsideBuilding;
            EndLocation = endLocation;
        }

        public Vector3[] GraphPositions { get; }
        public bool StartsAtCellCenter { get; }
        public bool EndsAtCellCenter { get; }
        public bool EndsInsideBuilding { get; }
        public BuildingWayLocation EndLocation { get; }
    }

    internal sealed class EntityPersistentState
    {
        public EntityPersistentState(
            EntityId id,
            EntityTypeKey typeKey,
            CellCoordinate anchorCell,
            EntityDirection direction,
            byte[] progressPayload,
            bool hasBuildingWayLocation,
            BuildingWayLocation buildingWayLocation,
            WayMovementPlanPersistentState activeWayMove)
        {
            Id = id;
            TypeKey = typeKey;
            AnchorCell = anchorCell;
            Direction = direction;
            ProgressPayload = progressPayload == null
                ? Array.Empty<byte>()
                : (byte[])progressPayload.Clone();
            HasBuildingWayLocation = hasBuildingWayLocation;
            BuildingWayLocation = buildingWayLocation;
            ActiveWayMove = activeWayMove;
        }

        public EntityId Id { get; }
        public EntityTypeKey TypeKey { get; }
        public CellCoordinate AnchorCell { get; }
        public EntityDirection Direction { get; }
        public byte[] ProgressPayload { get; }
        public bool HasBuildingWayLocation { get; }
        public BuildingWayLocation BuildingWayLocation { get; }
        public WayMovementPlanPersistentState ActiveWayMove { get; }
    }
}
