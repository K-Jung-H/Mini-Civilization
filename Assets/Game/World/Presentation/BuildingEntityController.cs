using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Entities.Building;
using UnityEngine;
using UnityEngine.Serialization;

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
        [SerializeField]
        [FormerlySerializedAs("occupiedCells")]
        private Vector3Int[] buildingCells = Array.Empty<Vector3Int>();
        [SerializeField]
        [FormerlySerializedAs("terrainAnchorOffsets")]
        private Vector3Int[] terrainAnchorCells = Array.Empty<Vector3Int>();
        [SerializeField] private BuildingWayPointBakeData[] localWayPoints =
            Array.Empty<BuildingWayPointBakeData>();
        [SerializeField] private BuildingWayBakeData[] localWays =
            Array.Empty<BuildingWayBakeData>();
        [SerializeField, Min(0)]
        [Tooltip("배치 시 허용하는 지형 상승·하강 최대 단계입니다. 한 단계는 Cell 높이의 1/5입니다.")]
        [InspectorName("최대 지형 보정 단계")]
        private int maxTerrainCorrectionSteps;

        private BuildingLayout cachedLayout;

        public override EntityTypeKey TypeKey => new(
            EntityCategory.Building,
            (ushort)entityType);
        public override string EntityTypeName => entityType.ToString();
        public override bool HasValidEntityType =>
            entityType is BuildingEntityType.House;
        public int MaxTerrainCorrectionSteps => maxTerrainCorrectionSteps;

        public override Entity CreateStateMachine(EntityData data)
        {
            var layout = GetLayout();
            return entityType switch
            {
                BuildingEntityType.House => new HouseEntity(
                    data,
                    layout,
                    maxTerrainCorrectionSteps),
                _ => throw new InvalidOperationException(
                    $"Unsupported Building Entity type: {entityType}.")
            };
        }

        internal void SetBakedLayout(
            Vector3Int[] cells,
            Vector3Int[] anchors,
            BuildingWayPointBakeData[] points,
            BuildingWayBakeData[] ways)
        {
            buildingCells = cells ?? Array.Empty<Vector3Int>();
            terrainAnchorCells = anchors ?? Array.Empty<Vector3Int>();
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

            var cells = new BuildingCell[buildingCells?.Length ?? 0];
            for (var index = 0; index < cells.Length; index++)
            {
                cells[index] = new BuildingCell(
                    ToOffset(buildingCells[index]));
            }

            var anchors = new CellOffset[terrainAnchorCells?.Length ?? 0];
            for (var index = 0; index < anchors.Length; index++)
            {
                anchors[index] = ToOffset(terrainAnchorCells[index]);
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
                cells,
                anchors,
                points,
                ways);
            return cachedLayout;
        }

        private void OnValidate()
        {
            maxTerrainCorrectionSteps = Math.Max(
                0,
                maxTerrainCorrectionSteps);
            cachedLayout = null;
        }

        private static CellOffset ToOffset(Vector3Int value) =>
            new(value.x, value.y, value.z);
    }
}
