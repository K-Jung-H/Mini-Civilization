using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using Vector3 = UnityEngine.Vector3;

namespace MiniCivilization.World.Runtime
{
    internal sealed class WayMovementPlanPersistentState
    {
        private readonly Vector3[] graphPositions;

        public WayMovementPlanPersistentState(
            Vector3[] graphPositions,
            bool startsAtCellCenter,
            bool endsAtCellCenter,
            bool endsInsideBuilding,
            BuildingWayLocation endLocation)
        {
            this.graphPositions = graphPositions == null
                ? Array.Empty<Vector3>()
                : (Vector3[])graphPositions.Clone();
            StartsAtCellCenter = startsAtCellCenter;
            EndsAtCellCenter = endsAtCellCenter;
            EndsInsideBuilding = endsInsideBuilding;
            EndLocation = endLocation;
        }

        public Vector3[] GraphPositions => (Vector3[])graphPositions.Clone();
        public bool StartsAtCellCenter { get; }
        public bool EndsAtCellCenter { get; }
        public bool EndsInsideBuilding { get; }
        public BuildingWayLocation EndLocation { get; }
    }

    internal sealed class EntityPersistentState
    {
        private readonly byte[] progressPayload;

        public EntityPersistentState(
            EntityId id,
            EntityTypeKey typeKey,
            CellCoordinate anchorCell,
            EntityDirection direction,
            EntityAttributes attributes,
            byte[] progressPayload,
            bool hasBuildingWayLocation,
            BuildingWayLocation buildingWayLocation,
            WayMovementPlanPersistentState activeWayMove)
        {
            Id = id;
            TypeKey = typeKey;
            AnchorCell = anchorCell;
            Direction = direction;
            Attributes = attributes ?? EntityAttributes.Empty;
            this.progressPayload = progressPayload == null
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
        public EntityAttributes Attributes { get; }
        public byte[] ProgressPayload => (byte[])progressPayload.Clone();
        public bool HasBuildingWayLocation { get; }
        public BuildingWayLocation BuildingWayLocation { get; }
        public WayMovementPlanPersistentState ActiveWayMove { get; }
    }
}
