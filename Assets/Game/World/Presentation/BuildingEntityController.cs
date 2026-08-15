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
    public struct BuildingCellBakeData
    {
        public Vector3Int LocalOffset;
        [Range(0, WorldGrid.HeightStepsPerCell)]
        public int TerrainHeight;
        [Min(0)] public int MaxHeightAdjustmentSteps;
    }

    [Serializable]
    public struct BuildingCellTerrainBakeData
    {
        [Range(0, WorldGrid.HeightStepsPerCell)]
        public int TerrainHeight;
        [Min(0)] public int MaxHeightAdjustmentSteps;
    }

    [Serializable]
    public struct BuildingWayPointBakeData
    {
        public Vector3Int LocalCellOffset;
        public Vector3 LocalPosition;
        public BuildingWayPointDirection ExternalDirection;
    }

    [Serializable]
    public struct TerrainAnchorBakeData
    {
        public Vector3Int LocalOffset;
        [FormerlySerializedAs("MaxTerrainCorrectionSteps")]
        [Min(0)] public int MaxHeightAdjustmentSteps;
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
        [SerializeField] private BuildingCellTerrainBakeData[]
            buildingCellTerrain = Array.Empty<BuildingCellTerrainBakeData>();
        [SerializeField] private TerrainAnchorBakeData[] terrainAnchors =
            Array.Empty<TerrainAnchorBakeData>();
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

        internal void SetBakedLayout(
            BuildingCellBakeData[] cells,
            TerrainAnchorBakeData[] anchors,
            BuildingWayPointBakeData[] points,
            BuildingWayBakeData[] ways)
        {
            cells ??= Array.Empty<BuildingCellBakeData>();
            buildingCells = new Vector3Int[cells.Length];
            buildingCellTerrain = new BuildingCellTerrainBakeData[cells.Length];
            for (var index = 0; index < cells.Length; index++)
            {
                var cell = cells[index];
                buildingCells[index] = cell.LocalOffset;
                buildingCellTerrain[index] = new BuildingCellTerrainBakeData
                {
                    TerrainHeight = Mathf.Clamp(
                        cell.TerrainHeight,
                        0,
                        WorldGrid.HeightStepsPerCell),
                    MaxHeightAdjustmentSteps = Mathf.Max(
                        0,
                        cell.MaxHeightAdjustmentSteps)
                };
            }

            terrainAnchors = anchors ?? Array.Empty<TerrainAnchorBakeData>();
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
                var terrain = buildingCellTerrain != null
                    && index < buildingCellTerrain.Length
                    ? buildingCellTerrain[index]
                    : default;
                cells[index] = new BuildingCell(
                    ToOffset(buildingCells[index]),
                    Mathf.Clamp(
                        terrain.TerrainHeight,
                        0,
                        WorldGrid.HeightStepsPerCell),
                    Mathf.Max(0, terrain.MaxHeightAdjustmentSteps));
            }

            var anchors = new TerrainAnchorCell[
                terrainAnchors?.Length ?? 0];
            for (var index = 0; index < anchors.Length; index++)
            {
                var anchor = terrainAnchors[index];
                anchors[index] = new TerrainAnchorCell(
                    ToOffset(anchor.LocalOffset),
                    Mathf.Max(0, anchor.MaxHeightAdjustmentSteps));
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
            for (var index = 0;
                 index < buildingCellTerrain?.Length;
                 index++)
            {
                var terrain = buildingCellTerrain[index];
                terrain.TerrainHeight = Mathf.Clamp(
                    terrain.TerrainHeight,
                    0,
                    WorldGrid.HeightStepsPerCell);
                terrain.MaxHeightAdjustmentSteps = Mathf.Max(
                    0,
                    terrain.MaxHeightAdjustmentSteps);
                buildingCellTerrain[index] = terrain;
            }

            for (var index = 0;
                 index < terrainAnchors?.Length;
                 index++)
            {
                var anchor = terrainAnchors[index];
                anchor.MaxHeightAdjustmentSteps = Math.Max(
                    0,
                    anchor.MaxHeightAdjustmentSteps);
                terrainAnchors[index] = anchor;
            }

            cachedLayout = null;
        }

        private static CellOffset ToOffset(Vector3Int value) =>
            new(value.x, value.y, value.z);
    }
}
