using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using UnityEngine;
using WorldEntityId = MiniCivilization.World.Domain.EntityId;

namespace MiniCivilization.World.Runtime
{
    public readonly struct RoadVisualSegment
    {
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public RoadType RoadType { get; }
        public CellCoordinate OwnerCell { get; }

        internal RoadVisualSegment(
            Vector3 start,
            Vector3 end,
            RoadType roadType,
            CellCoordinate ownerCell)
        {
            Start = start;
            End = end;
            RoadType = roadType;
            OwnerCell = ownerCell;
        }
    }

    internal readonly struct BuildingWayLocation :
        IEquatable<BuildingWayLocation>
    {
        public readonly WorldEntityId BuildingId;
        public readonly int LocalPointIndex;

        public BuildingWayLocation(
            WorldEntityId buildingId,
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

    internal sealed class WayMovementPlan
    {
        public Vector3[] GraphPositions { get; }
        public bool StartsAtCellCenter { get; }
        public bool EndsAtCellCenter { get; }
        public bool EndsInsideBuilding { get; }
        public BuildingWayLocation EndLocation { get; }

        public WayMovementPlan(
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
    }

    public sealed class WorldWayPointGraph
    {
        private readonly Dictionary<BuildingWayLocation, int>
            nodeByBuildingPoint;
        private readonly Dictionary<int, List<int>> buildingNodesByCell;
        private readonly List<ExternalPort> externalPorts;
        private readonly Dictionary<int, BuildingWayLocation>
            buildingLocationByNode;

        private WorldWayPointGraph(
            Vector3[] positions,
            int[] neighborOffsets,
            int[] neighbors,
            RoadVisualSegment[] roadSegments,
            Dictionary<BuildingWayLocation, int> nodeByBuildingPoint,
            Dictionary<int, List<int>> buildingNodesByCell,
            List<ExternalPort> externalPorts,
            Dictionary<int, BuildingWayLocation> buildingLocationByNode)
        {
            Positions = positions;
            NeighborOffsets = neighborOffsets;
            Neighbors = neighbors;
            RoadSegments = roadSegments;
            this.nodeByBuildingPoint = nodeByBuildingPoint;
            this.buildingNodesByCell = buildingNodesByCell;
            this.externalPorts = externalPorts;
            this.buildingLocationByNode = buildingLocationByNode;
        }

        public IReadOnlyList<Vector3> Positions { get; }
        public IReadOnlyList<int> NeighborOffsets { get; }
        public IReadOnlyList<int> Neighbors { get; }
        public IReadOnlyList<RoadVisualSegment> RoadSegments { get; }

        internal static WorldWayPointGraph Build(
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

            var builder = new Builder(runtime);
            entities.CopyEntitiesTo(builder.EntityBuffer);
            builder.AddBuildings();
            builder.AddRoads();
            builder.ConnectBuildingPorts();
            return builder.Compile();
        }

        internal bool TryGetPosition(
            BuildingWayLocation location,
            out Vector3 position)
        {
            if (nodeByBuildingPoint.TryGetValue(location, out var node))
            {
                position = Positions[node];
                return true;
            }

            position = default;
            return false;
        }

        internal bool TryGetInitialLocation(
            WorldData world,
            CellCoordinate cell,
            out BuildingWayLocation location)
        {
            location = default;
            if (!world.Contains(cell.X, cell.Y, cell.Z)
                || !buildingNodesByCell.TryGetValue(
                    WorldIndex.EncodeCell(world, cell.X, cell.Y, cell.Z),
                    out var nodes)
                || nodes.Count == 0)
            {
                return false;
            }

            var node = nodes[0];
            return buildingLocationByNode.TryGetValue(node, out location);
        }

        internal bool TryPlan(
            WorldData world,
            CellCoordinate currentCell,
            CellCoordinate nextCell,
            bool hasCurrentLocation,
            BuildingWayLocation currentLocation,
            out WayMovementPlan plan)
        {
            plan = null;
            var currentNode = -1;
            var currentIsBuilding = hasCurrentLocation
                && nodeByBuildingPoint.TryGetValue(
                    currentLocation,
                    out currentNode);
            var nextCellIndex = WorldIndex.EncodeCell(
                world,
                nextCell.X,
                nextCell.Y,
                nextCell.Z);
            var nextIsBuilding = buildingNodesByCell.TryGetValue(
                nextCellIndex,
                out var nextNodes)
                && nextNodes.Count != 0;

            if (!currentIsBuilding && !nextIsBuilding)
            {
                return false;
            }

            if (!currentIsBuilding)
            {
                var entry = FindExternalPort(nextCell, currentCell);
                if (entry < 0)
                {
                    return false;
                }

                var end = buildingLocationByNode[entry];
                plan = new WayMovementPlan(
                    new[] { Positions[entry] },
                    startsAtCellCenter: true,
                    endsAtCellCenter: false,
                    endsInsideBuilding: true,
                    end);
                return true;
            }

            if (!nextIsBuilding)
            {
                var exit = FindExternalPort(currentCell, nextCell);
                if (exit < 0
                    || !TryFindPath(currentNode, new[] { exit }, out var path))
                {
                    return false;
                }

                plan = new WayMovementPlan(
                    CopyPositions(path),
                    startsAtCellCenter: false,
                    endsAtCellCenter: true,
                    endsInsideBuilding: false,
                    default);
                return true;
            }

            if (!TryFindPath(currentNode, nextNodes, out var internalPath))
            {
                return false;
            }

            var targetNode = internalPath[internalPath.Count - 1];
            plan = new WayMovementPlan(
                CopyPositions(internalPath),
                startsAtCellCenter: false,
                endsAtCellCenter: false,
                endsInsideBuilding: true,
                buildingLocationByNode[targetNode]);
            return true;
        }

        private int FindExternalPort(
            CellCoordinate buildingCell,
            CellCoordinate outsideCell)
        {
            for (var index = 0; index < externalPorts.Count; index++)
            {
                var port = externalPorts[index];
                if (port.BuildingCell.Equals(buildingCell)
                    && port.OutsideCell.X == outsideCell.X
                    && port.OutsideCell.Z == outsideCell.Z)
                {
                    return port.Node;
                }
            }

            return -1;
        }

        private bool TryFindPath(
            int start,
            IReadOnlyList<int> targets,
            out List<int> path)
        {
            path = null;
            var targetSet = new HashSet<int>();
            for (var index = 0; index < targets.Count; index++)
            {
                targetSet.Add(targets[index]);
            }

            var previous = new int[Positions.Count];
            Array.Fill(previous, -2);
            var queue = new Queue<int>();
            previous[start] = -1;
            queue.Enqueue(start);
            var found = -1;
            while (queue.Count != 0)
            {
                var node = queue.Dequeue();
                if (targetSet.Contains(node))
                {
                    found = node;
                    break;
                }

                for (var offset = NeighborOffsets[node];
                     offset < NeighborOffsets[node + 1];
                     offset++)
                {
                    var neighbor = Neighbors[offset];
                    if (previous[neighbor] != -2)
                    {
                        continue;
                    }

                    previous[neighbor] = node;
                    queue.Enqueue(neighbor);
                }
            }

            if (found < 0)
            {
                return false;
            }

            path = new List<int>();
            for (var node = found; node >= 0; node = previous[node])
            {
                path.Add(node);
            }

            path.Reverse();
            return true;
        }

        private Vector3[] CopyPositions(IReadOnlyList<int> path)
        {
            var result = new Vector3[path.Count];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = Positions[path[index]];
            }

            return result;
        }

        private readonly struct ExternalPort
        {
            public readonly int Node;
            public readonly CellCoordinate BuildingCell;
            public readonly CellCoordinate OutsideCell;
            public readonly int HeightSteps;

            public ExternalPort(
                int node,
                CellCoordinate buildingCell,
                CellCoordinate outsideCell,
                int heightSteps)
            {
                Node = node;
                BuildingCell = buildingCell;
                OutsideCell = outsideCell;
                HeightSteps = heightSteps;
            }
        }

        private sealed class Builder
        {
            private readonly WorldRuntime runtime;
            private readonly List<Vector3> positions = new();
            private readonly List<HashSet<int>> adjacency = new();
            private readonly Dictionary<int, BuildingWayLocation>
                buildingLocationByNode = new();
            private readonly List<RoadVisualSegment> roadSegments = new();
            private readonly Dictionary<BuildingWayLocation, int>
                nodeByBuildingPoint = new();
            private readonly Dictionary<int, List<int>> buildingNodesByCell =
                new();
            private readonly List<ExternalPort> externalPorts = new();
            private readonly Dictionary<RoadBoundaryKey, int> roadBoundaryNodes =
                new();
            private readonly Dictionary<int, RoadCellTopology> roadsByColumn =
                new();

            public Builder(WorldRuntime runtime)
            {
                this.runtime = runtime;
            }

            public List<Entity> EntityBuffer { get; } = new();

            public void AddBuildings()
            {
                for (var entityIndex = 0;
                     entityIndex < EntityBuffer.Count;
                     entityIndex++)
                {
                    if (EntityBuffer[entityIndex]
                        is not BuildingEntity building)
                    {
                        continue;
                    }

                    var layout = building.Layout;
                    var localNodes = new int[layout.WayPoints.Count];
                    for (var pointIndex = 0;
                         pointIndex < layout.WayPoints.Count;
                         pointIndex++)
                    {
                        var point = layout.WayPoints[pointIndex];
                        var location = new BuildingWayLocation(
                            building.Id,
                            pointIndex);
                        var node = AddNode(layout.ToWorldPosition(
                            runtime.Data,
                            building.Data,
                            point));
                        localNodes[pointIndex] = node;
                        nodeByBuildingPoint.Add(location, node);
                        buildingLocationByNode.Add(node, location);

                        var cell = layout.ToWorld(
                            building.Data,
                            point.LocalCellOffset);
                        var cellIndex = WorldIndex.EncodeCell(
                            runtime.Data,
                            cell.X,
                            cell.Y,
                            cell.Z);
                        if (!buildingNodesByCell.TryGetValue(
                                cellIndex,
                                out var nodes))
                        {
                            nodes = new List<int>();
                            buildingNodesByCell.Add(cellIndex, nodes);
                        }

                        nodes.Add(node);
                        if (point.IsExternal)
                        {
                            AddExternalPort(
                                node,
                                cell,
                                layout.ToWorldDirection(
                                    point.ExternalDirection,
                                    building.Direction));
                        }
                    }

                    for (var wayIndex = 0;
                         wayIndex < layout.Ways.Count;
                         wayIndex++)
                    {
                        var way = layout.Ways[wayIndex];
                        AddEdge(
                            localNodes[way.PointA],
                            localNodes[way.PointB],
                            !way.OneWay);
                    }
                }
            }

            public void AddRoads()
            {
                for (var z = 0; z < runtime.Data.Size; z++)
                for (var x = 0; x < runtime.Data.Size; x++)
                {
                    if (RoadTopologyResolver.TryGetRoad(
                            runtime,
                            x,
                            z,
                            out var road))
                    {
                        roadsByColumn.Add(
                            WorldIndex.EncodeColumn(runtime.Data, x, z),
                            road);
                    }
                }

                foreach (var pair in roadsByColumn)
                {
                    AddRoad(pair.Value);
                }
            }

            public void ConnectBuildingPorts()
            {
                for (var firstIndex = 0;
                     firstIndex < externalPorts.Count;
                     firstIndex++)
                {
                    var first = externalPorts[firstIndex];
                    for (var secondIndex = firstIndex + 1;
                         secondIndex < externalPorts.Count;
                         secondIndex++)
                    {
                        var second = externalPorts[secondIndex];
                        if (!first.BuildingCell.Equals(second.OutsideCell)
                            || !first.OutsideCell.Equals(second.BuildingCell)
                            || (positions[first.Node] - positions[second.Node])
                                .sqrMagnitude > 0.000001f)
                        {
                            continue;
                        }

                        AddEdge(first.Node, second.Node, true);
                    }
                }
            }

            public WorldWayPointGraph Compile()
            {
                var offsets = new int[positions.Count + 1];
                var neighborCount = 0;
                for (var index = 0; index < adjacency.Count; index++)
                {
                    offsets[index] = neighborCount;
                    neighborCount = checked(
                        neighborCount + adjacency[index].Count);
                }

                offsets[positions.Count] = neighborCount;
                var neighbors = new int[neighborCount];
                var write = 0;
                for (var index = 0; index < adjacency.Count; index++)
                {
                    var values = new int[adjacency[index].Count];
                    adjacency[index].CopyTo(values);
                    Array.Sort(values);
                    Array.Copy(values, 0, neighbors, write, values.Length);
                    write += values.Length;
                }

                return new WorldWayPointGraph(
                    positions.ToArray(),
                    offsets,
                    neighbors,
                    roadSegments.ToArray(),
                    nodeByBuildingPoint,
                    buildingNodesByCell,
                    externalPorts,
                    buildingLocationByNode);
            }

            private void AddRoad(in RoadCellTopology road)
            {
                var portNodes = new List<int>(4);
                AddRoadPort(road, -1, 0, portNodes);
                AddRoadPort(road, 1, 0, portNodes);
                AddRoadPort(road, 0, -1, portNodes);
                AddRoadPort(road, 0, 1, portNodes);
                if (road.Road.CrossesCenter && portNodes.Count != 0)
                {
                    var center = AddNode(
                        RoadTopologyResolver.ResolveCenter(
                            runtime.Data,
                            road));
                    for (var index = 0; index < portNodes.Count; index++)
                    {
                        AddRoadEdge(
                            center,
                            portNodes[index],
                            road.Road.Type,
                            road.Cell);
                    }
                }
                else if (portNodes.Count == 2)
                {
                    AddRoadEdge(
                        portNodes[0],
                        portNodes[1],
                        road.Road.Type,
                        road.Cell);
                }
            }

            private void AddRoadPort(
                in RoadCellTopology road,
                int directionX,
                int directionZ,
                ICollection<int> target)
            {
                var neighborX = road.Cell.X + directionX;
                var neighborZ = road.Cell.Z + directionZ;
                if (!runtime.Data.ContainsColumn(neighborX, neighborZ))
                {
                    return;
                }

                var neighborColumn = WorldIndex.EncodeColumn(
                    runtime.Data,
                    neighborX,
                    neighborZ);
                if (roadsByColumn.TryGetValue(
                        neighborColumn,
                        out var neighborRoad)
                    && RoadTopologyResolver.CanConnect(
                        runtime.Data.Settings,
                        road.SurfaceHeightSteps,
                        neighborRoad.SurfaceHeightSteps))
                {
                    var key = new RoadBoundaryKey(
                        WorldIndex.EncodeColumn(
                            runtime.Data,
                            road.Cell.X,
                            road.Cell.Z),
                        neighborColumn);
                    if (!roadBoundaryNodes.TryGetValue(key, out var node))
                    {
                        node = AddNode(
                            RoadTopologyResolver.ResolveSharedBoundary(
                                runtime.Data,
                                road,
                                neighborRoad));
                        roadBoundaryNodes.Add(key, node);
                    }

                    target.Add(node);
                    return;
                }

                for (var index = 0; index < externalPorts.Count; index++)
                {
                    var port = externalPorts[index];
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

                    target.Add(port.Node);
                    return;
                }
            }

            private void AddExternalPort(
                int node,
                CellCoordinate buildingCell,
                BuildingWayPointDirection direction)
            {
                var offset = direction switch
                {
                    BuildingWayPointDirection.North => (0, 1),
                    BuildingWayPointDirection.East => (1, 0),
                    BuildingWayPointDirection.South => (0, -1),
                    BuildingWayPointDirection.West => (-1, 0),
                    _ => throw new ArgumentOutOfRangeException(nameof(direction))
                };
                var position = positions[node];
                var heightSteps = Mathf.RoundToInt(
                    position.y / runtime.Data.HeightStep);
                externalPorts.Add(new ExternalPort(
                    node,
                    buildingCell,
                    new CellCoordinate(
                        buildingCell.X + offset.Item1,
                        buildingCell.Y,
                        buildingCell.Z + offset.Item2),
                    heightSteps));
            }

            private int AddNode(Vector3 position)
            {
                var index = positions.Count;
                positions.Add(position);
                adjacency.Add(new HashSet<int>());
                return index;
            }

            private void AddRoadEdge(
                int a,
                int b,
                RoadType roadType,
                CellCoordinate ownerCell)
            {
                if (a == b)
                {
                    return;
                }

                AddEdge(a, b, true);
                roadSegments.Add(new RoadVisualSegment(
                    positions[a],
                    positions[b],
                    roadType,
                    ownerCell));
            }

            private void AddEdge(int a, int b, bool bidirectional)
            {
                adjacency[a].Add(b);
                if (bidirectional)
                {
                    adjacency[b].Add(a);
                }
            }

            private readonly struct RoadBoundaryKey :
                IEquatable<RoadBoundaryKey>
            {
                private readonly int first;
                private readonly int second;

                public RoadBoundaryKey(int a, int b)
                {
                    first = Math.Min(a, b);
                    second = Math.Max(a, b);
                }

                public bool Equals(RoadBoundaryKey other) =>
                    first == other.first && second == other.second;

                public override bool Equals(object obj) =>
                    obj is RoadBoundaryKey other && Equals(other);

                public override int GetHashCode() => HashCode.Combine(
                    first,
                    second);
            }
        }
    }
}
