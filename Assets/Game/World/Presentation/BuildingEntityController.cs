using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Building;
using UnityEngine;

namespace MiniCivilization.World.Presentation
{
    public enum BuildingEntityType : ushort
    {
        None = 0,
        House = 1
    }

    [Serializable]
    public struct BuildingWayPointBakeData
    {
        public Vector3Int LocalCellOffset;
        public Vector3 LocalPosition;
        public BuildingWayPointDirection ExternalDirection;
    }

    [Serializable]
    public struct BuildingWayBakeData
    {
        [Min(0)] public int PointA;
        [Min(0)] public int PointB;
        public bool OneWay;
    }

    public sealed class BuildingEntityController : AnimatedEntityController
    {
        [SerializeField] private BuildingEntityType entityType;
        [SerializeField] private Vector3Int[] occupiedCells =
        {
            new(0, 1, 0)
        };
        [SerializeField] private Vector3Int[] terrainAnchorOffsets =
        {
            Vector3Int.zero
        };
        [SerializeField] private BuildingWayPointBakeData[] localWayPoints =
            Array.Empty<BuildingWayPointBakeData>();
        [SerializeField] private BuildingWayBakeData[] localWays =
            Array.Empty<BuildingWayBakeData>();

        private BuildingLayout cachedLayout;

        public override EntityTypeKey TypeKey => new(
            EntityCategory.Building,
            (ushort)entityType);
        public override string EntityTypeName => entityType.ToString();
        public override bool HasValidEntityType =>
            entityType is BuildingEntityType.House;

        public override Entity CreateStateMachine(EntityData data)
        {
            var layout = GetLayout();
            return entityType switch
            {
                BuildingEntityType.House => new HouseEntity(data, layout),
                _ => throw new InvalidOperationException(
                    $"Unsupported Building Entity type: {entityType}.")
            };
        }

        internal void SetBakedWays(
            BuildingWayPointBakeData[] points,
            BuildingWayBakeData[] ways)
        {
            localWayPoints = points ?? Array.Empty<BuildingWayPointBakeData>();
            localWays = ways ?? Array.Empty<BuildingWayBakeData>();
            cachedLayout = null;
        }

        private BuildingLayout GetLayout()
        {
            if (cachedLayout != null)
            {
                return cachedLayout;
            }

            var occupied = new BuildingOccupiedCell[occupiedCells?.Length ?? 0];
            for (var index = 0; index < occupied.Length; index++)
            {
                occupied[index] = new BuildingOccupiedCell(
                    ToOffset(occupiedCells[index]));
            }

            var anchors = new CellOffset[terrainAnchorOffsets?.Length ?? 0];
            for (var index = 0; index < anchors.Length; index++)
            {
                anchors[index] = ToOffset(terrainAnchorOffsets[index]);
            }

            var points = new BuildingWayPoint[localWayPoints?.Length ?? 0];
            for (var index = 0; index < points.Length; index++)
            {
                var point = localWayPoints[index];
                points[index] = new BuildingWayPoint(
                    ToOffset(point.LocalCellOffset),
                    point.LocalPosition,
                    point.ExternalDirection);
            }

            var ways = new BuildingWay[localWays?.Length ?? 0];
            for (var index = 0; index < ways.Length; index++)
            {
                var way = localWays[index];
                ways[index] = new BuildingWay(
                    way.PointA,
                    way.PointB,
                    way.OneWay);
            }

            cachedLayout = new BuildingLayout(
                occupied,
                anchors,
                points,
                ways);
            return cachedLayout;
        }

        private void OnValidate()
        {
            cachedLayout = null;
        }

        private static CellOffset ToOffset(Vector3Int value) =>
            new(value.x, value.y, value.z);
    }
}
