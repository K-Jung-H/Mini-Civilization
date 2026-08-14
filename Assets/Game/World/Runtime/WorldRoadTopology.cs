using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using UnityEngine;

namespace MiniCivilization.World.Runtime
{
    [Flags]
    internal enum RoadConnectionMask : byte
    {
        None = 0,
        West = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        North = 1 << 3
    }

    internal enum RoadDirection : byte
    {
        West,
        East,
        South,
        North
    }

    internal enum RoadConnectionTarget : byte
    {
        None,
        Road,
        Building
    }

    internal readonly struct RoadPortConnection
    {
        public readonly RoadConnectionTarget Target;
        public readonly int NeighborColumn;
        public readonly BuildingWayLocation BuildingLocation;
        public readonly byte BoundaryOffsetIndex;

        public RoadPortConnection(
            RoadConnectionTarget target,
            int neighborColumn,
            BuildingWayLocation buildingLocation,
            byte boundaryOffsetIndex)
        {
            Target = target;
            NeighborColumn = neighborColumn;
            BuildingLocation = buildingLocation;
            BoundaryOffsetIndex = boundaryOffsetIndex;
        }

        public bool IsConnected => Target != RoadConnectionTarget.None;
    }

    internal readonly struct RoadTopologyCell
    {
        public readonly RoadCellTopology Road;
        public readonly RoadPortConnection West;
        public readonly RoadPortConnection East;
        public readonly RoadPortConnection South;
        public readonly RoadPortConnection North;

        public RoadTopologyCell(
            RoadCellTopology road,
            RoadPortConnection west,
            RoadPortConnection east,
            RoadPortConnection south,
            RoadPortConnection north)
        {
            Road = road;
            West = west;
            East = east;
            South = south;
            North = north;
        }

        public RoadConnectionMask Connections =>
            GetMask(West, RoadConnectionMask.West)
            | GetMask(East, RoadConnectionMask.East)
            | GetMask(South, RoadConnectionMask.South)
            | GetMask(North, RoadConnectionMask.North);

        public RoadPortConnection GetConnection(RoadDirection direction) =>
            direction switch
            {
                RoadDirection.West => West,
                RoadDirection.East => East,
                RoadDirection.South => South,
                RoadDirection.North => North,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };

        private static RoadConnectionMask GetMask(
            RoadPortConnection connection,
            RoadConnectionMask mask) =>
            connection.IsConnected ? mask : RoadConnectionMask.None;
    }

    internal sealed class WorldRoadTopology
    {
        private readonly Dictionary<int, RoadTopologyCell> cellsByColumn;

        private WorldRoadTopology(
            WorldData world,
            List<RoadTopologyCell> cells,
            Dictionary<int, RoadTopologyCell> cellsByColumn)
        {
            this.world = world;
            Cells = cells;
            this.cellsByColumn = cellsByColumn;
        }

        public IReadOnlyList<RoadTopologyCell> Cells { get; }

        public bool TryGet(
            int x,
            int z,
            out RoadTopologyCell cell) =>
            cellsByColumn.TryGetValue(
                WorldIndex.EncodeColumn(world, x, z),
                out cell);

        private readonly WorldData world;

        internal static WorldRoadTopology Build(
            WorldRuntime runtime,
            EntityRuntime entities)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (entities == null)
            {
                throw new ArgumentNullException(nameof(entities));
            }

            var roadsByColumn = new Dictionary<int, RoadCellTopology>();
            var roads = new List<RoadCellTopology>();
            for (var z = 0; z < runtime.Data.Size; z++)
            for (var x = 0; x < runtime.Data.Size; x++)
            {
                if (!RoadTopologyResolver.TryGetRoad(runtime, x, z, out var road))
                {
                    continue;
                }

                roadsByColumn.Add(WorldIndex.EncodeColumn(runtime.Data, x, z), road);
                roads.Add(road);
            }

            var ports = CollectBuildingPorts(runtime, entities);
            var cells = new List<RoadTopologyCell>(roads.Count);
            var cellsByColumn = new Dictionary<int, RoadTopologyCell>(roads.Count);
            for (var index = 0; index < roads.Count; index++)
            {
                var road = roads[index];
                var cell = new RoadTopologyCell(
                    road,
                    ResolveConnection(runtime, roadsByColumn, ports, road, RoadDirection.West),
                    ResolveConnection(runtime, roadsByColumn, ports, road, RoadDirection.East),
                    ResolveConnection(runtime, roadsByColumn, ports, road, RoadDirection.South),
                    ResolveConnection(runtime, roadsByColumn, ports, road, RoadDirection.North));
                cells.Add(cell);
                cellsByColumn.Add(
                    WorldIndex.EncodeColumn(runtime.Data, road.Cell.X, road.Cell.Z),
                    cell);
            }

            return new WorldRoadTopology(runtime.Data, cells, cellsByColumn);
        }

        private static List<BuildingRoadPort> CollectBuildingPorts(
            WorldRuntime runtime,
            EntityRuntime entities)
        {
            var entityBuffer = new List<Entity>();
            entities.CopyEntitiesTo(entityBuffer);
            var ports = new List<BuildingRoadPort>();
            for (var entityIndex = 0; entityIndex < entityBuffer.Count; entityIndex++)
            {
                if (entityBuffer[entityIndex] is not BuildingEntity building)
                {
                    continue;
                }

                var layout = building.Layout;
                for (var pointIndex = 0; pointIndex < layout.WayPoints.Count; pointIndex++)
                {
                    var point = layout.WayPoints[pointIndex];
                    if (!point.IsExternal)
                    {
                        continue;
                    }

                    var buildingCell = layout.ToWorld(
                        building.Data,
                        point.LocalCellOffset);
                    var direction = layout.ToWorldDirection(
                        point.ExternalDirection,
                        building.Direction);
                    var offset = GetOffset(direction);
                    var worldPosition = layout.ToWorldPosition(
                        runtime.Data,
                        building.Data,
                        point);
                    var heightSteps = Mathf.RoundToInt(
                        worldPosition.y / runtime.Data.HeightStep);
                    ports.Add(new BuildingRoadPort(
                        new BuildingWayLocation(building.Id, pointIndex),
                        buildingCell,
                        new CellCoordinate(
                            buildingCell.X + offset.x,
                            buildingCell.Y,
                            buildingCell.Z + offset.y),
                        heightSteps,
                        ResolveBoundaryOffsetIndex(
                            runtime.Data,
                            buildingCell,
                            worldPosition,
                            direction)));
                }
            }

            return ports;
        }

        private static RoadPortConnection ResolveConnection(
            WorldRuntime runtime,
            IReadOnlyDictionary<int, RoadCellTopology> roadsByColumn,
            IReadOnlyList<BuildingRoadPort> ports,
            RoadCellTopology road,
            RoadDirection direction)
        {
            var offset = GetOffset(direction);
            var neighborX = road.Cell.X + offset.x;
            var neighborZ = road.Cell.Z + offset.y;
            if (!runtime.Data.ContainsColumn(neighborX, neighborZ))
            {
                return default;
            }

            var neighborColumn = WorldIndex.EncodeColumn(
                runtime.Data,
                neighborX,
                neighborZ);
            if (roadsByColumn.TryGetValue(neighborColumn, out var neighborRoad)
                && RoadTopologyResolver.CanConnect(
                    runtime.Data.Settings,
                    road.SurfaceHeightSteps,
                    neighborRoad.SurfaceHeightSteps))
            {
                return new RoadPortConnection(
                    RoadConnectionTarget.Road,
                    neighborColumn,
                    default,
                    2);
            }

            for (var index = 0; index < ports.Count; index++)
            {
                var port = ports[index];
                if (port.OutsideCell.X != road.Cell.X
                    || port.OutsideCell.Z != road.Cell.Z
                    || port.BuildingCell.X != neighborX
                    || port.BuildingCell.Z != neighborZ
                    || !RoadTopologyResolver.CanConnect(
                        runtime.Data.Settings,
                        road.SurfaceHeightSteps,
                        port.HeightSteps))
                {
                    continue;
                }

                return new RoadPortConnection(
                    RoadConnectionTarget.Building,
                    -1,
                    port.Location,
                    port.BoundaryOffsetIndex);
            }

            return default;
        }

        private static Vector2Int GetOffset(RoadDirection direction) =>
            direction switch
            {
                RoadDirection.West => Vector2Int.left,
                RoadDirection.East => Vector2Int.right,
                RoadDirection.South => Vector2Int.down,
                RoadDirection.North => Vector2Int.up,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };

        private static Vector2Int GetOffset(BuildingWayPointDirection direction) =>
            direction switch
            {
                BuildingWayPointDirection.North => Vector2Int.up,
                BuildingWayPointDirection.East => Vector2Int.right,
                BuildingWayPointDirection.South => Vector2Int.down,
                BuildingWayPointDirection.West => Vector2Int.left,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };

        private static byte ResolveBoundaryOffsetIndex(
            WorldData world,
            CellCoordinate buildingCell,
            Vector3 position,
            BuildingWayPointDirection direction)
        {
            var local = direction is BuildingWayPointDirection.North
                or BuildingWayPointDirection.South
                ? position.x / world.CellSize - buildingCell.X
                : position.z / world.CellSize - buildingCell.Z;
            return (byte)Mathf.Clamp(Mathf.RoundToInt(local * 4f), 0, 4);
        }

        private readonly struct BuildingRoadPort
        {
            public readonly BuildingWayLocation Location;
            public readonly CellCoordinate BuildingCell;
            public readonly CellCoordinate OutsideCell;
            public readonly int HeightSteps;
            public readonly byte BoundaryOffsetIndex;

            public BuildingRoadPort(
                BuildingWayLocation location,
                CellCoordinate buildingCell,
                CellCoordinate outsideCell,
                int heightSteps,
                byte boundaryOffsetIndex)
            {
                Location = location;
                BuildingCell = buildingCell;
                OutsideCell = outsideCell;
                HeightSteps = heightSteps;
                BoundaryOffsetIndex = boundaryOffsetIndex;
            }
        }
    }
}
