using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Presentation;
using UnityEngine;

namespace MiniCivilization.World.Authoring
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BuildingWayAuthoring : MonoBehaviour
    {
        [SerializeField] private EntityAuthoringSystem authoringSystem;
        [SerializeField] private Transform markerContainer;

        public EntityAuthoringSystem AuthoringSystem => authoringSystem;
        public Transform MarkerContainer => markerContainer;
        public BuildingEntityController TargetController =>
            authoringSystem != null
                ? authoringSystem.EntityPrefab as BuildingEntityController
                : null;

        internal bool TryBake(out string error)
        {
            if (authoringSystem == null)
            {
                error = "Entity Authoring System is not assigned.";
                return false;
            }

            if (markerContainer == null)
            {
                error = "Way Marker Container is not assigned.";
                return false;
            }

            var targetController = TargetController;
            if (targetController == null)
            {
                error = "Entity Authoring System requires a Building Entity Prefab.";
                return false;
            }

            if (authoringSystem.CellBoxPrefab
                is not BuildingEntityAuthoringCellBox)
            {
                error = "Building Authoring requires a Building Entity Authoring Cell Box Prefab.";
                return false;
            }

            if (!TryCollectBuildingCells(
                    out var buildingCells,
                    out var terrainAnchors,
                    out error))
            {
                return false;
            }

            var buildingCellSet = CreateBuildingCellSet(buildingCells);

            var markers = markerContainer.GetComponentsInChildren<
                BuildingWayPointMarker>(true);
            Array.Sort(markers, CompareHierarchy);
            var indices = new Dictionary<BuildingWayPointMarker, int>(
                markers.Length);
            var points = new BuildingWayPointBakeData[markers.Length];
            for (var index = 0; index < markers.Length; index++)
            {
                var marker = markers[index];
                if (marker == null || !indices.TryAdd(marker, index))
                {
                    error = "Way Marker Container contains a duplicated or missing Marker.";
                    return false;
                }

                if (!TryBakePoint(
                        marker,
                        buildingCells,
                        buildingCellSet,
                        out points[index],
                        out error))
                {
                    return false;
                }
            }

            var bakedLinks = new List<BuildingWayBakeData>();
            var bidirectionalLinks = new HashSet<(int, int)>();
            var oneWayLinks = new HashSet<(int, int)>();
            for (var markerIndex = 0;
                 markerIndex < markers.Length;
                 markerIndex++)
            {
                var marker = markers[markerIndex];
                var connections = marker.Connections;
                for (var connectionIndex = 0;
                     connectionIndex < connections.Count;
                     connectionIndex++)
                {
                    var connection = connections[connectionIndex];
                    if (connection.Target == null
                        || !indices.TryGetValue(
                            connection.Target,
                            out var pointB))
                    {
                        error = $"Marker '{marker.name}' references a Marker outside its Container.";
                        return false;
                    }

                    var pointA = markerIndex;
                    if (pointA == pointB)
                    {
                        error = $"Marker '{marker.name}' connects to itself.";
                        return false;
                    }

                    var unordered = pointA < pointB
                        ? (pointA, pointB)
                        : (pointB, pointA);
                    if (connection.OneWay)
                    {
                        if (bidirectionalLinks.Contains(unordered)
                            || oneWayLinks.Contains((pointB, pointA))
                            || !oneWayLinks.Add((pointA, pointB)))
                        {
                            error = $"Marker '{marker.name}' contains a duplicated or conflicting Connection.";
                            return false;
                        }
                    }
                    else if (oneWayLinks.Contains((pointA, pointB))
                        || oneWayLinks.Contains((pointB, pointA))
                        || !bidirectionalLinks.Add(unordered))
                    {
                        error = $"Marker '{marker.name}' contains a duplicated or conflicting Connection.";
                        return false;
                    }

                    bakedLinks.Add(new BuildingWayBakeData
                    {
                        PointA = pointA,
                        PointB = pointB,
                        OneWay = connection.OneWay
                    });
                }
            }

            targetController.SetBakedLayout(
                buildingCells,
                terrainAnchors,
                points,
                bakedLinks.ToArray());
            error = string.Empty;
            return true;
        }

        internal bool TrySnapExternalMarker(
            BuildingWayPointMarker marker,
            out bool moved,
            out string error)
        {
            moved = false;
            if (marker == null)
            {
                error = "Building Way Marker is missing.";
                return false;
            }

            if (marker.ExternalDirection == BuildingWayPointDirection.None)
            {
                error = string.Empty;
                return true;
            }

            if (authoringSystem == null)
            {
                error = "Entity Authoring System is not assigned.";
                return false;
            }

            if (markerContainer == null
                || !marker.transform.IsChildOf(markerContainer))
            {
                error = "Way Marker must be a child of the assigned Way Marker Container.";
                return false;
            }

            var cellSize = authoringSystem.CellSize;
            if (!float.IsFinite(cellSize) || cellSize <= 0f)
            {
                error = "Entity Authoring Cell size must be finite and positive.";
                return false;
            }

            if (!TryCollectBuildingCells(
                    out var buildingCells,
                    out _,
                    out error))
            {
                return false;
            }

            var buildingCellSet = CreateBuildingCellSet(buildingCells);
            var source = authoringSystem.transform.InverseTransformPoint(
                marker.transform.position) / cellSize;
            var nearest = default(Vector3);
            var nearestDistance = float.PositiveInfinity;
            for (var index = 0; index < buildingCells.Length; index++)
            {
                var cellOffset = buildingCells[index].LocalOffset;
                if (buildingCellSet.Contains(
                        cellOffset + GetDirectionOffset(
                            marker.ExternalDirection)))
                {
                    continue;
                }

                var candidate = ClampToCell(source, cellOffset);
                var position = candidate - (Vector3)cellOffset;
                candidate = (Vector3)cellOffset
                    + ProjectToExternalBoundary(
                        marker.ExternalDirection,
                        position);
                var distance = (candidate - source).sqrMagnitude;
                if (distance >= nearestDistance)
                {
                    continue;
                }

                nearest = candidate;
                nearestDistance = distance;
            }

            if (!float.IsFinite(nearestDistance))
            {
                error = $"No Building Cell exposes the {marker.ExternalDirection} side.";
                return false;
            }

            var snapped = authoringSystem.transform.TransformPoint(
                nearest * cellSize);
            if ((snapped - marker.transform.position).sqrMagnitude
                <= 0.000000000001f)
            {
                error = string.Empty;
                return true;
            }

#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(
                marker.transform,
                "Snap Building Way Marker");
#endif
            marker.transform.position = snapped;
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(marker.transform);
#endif
            moved = true;
            error = string.Empty;
            return true;
        }

        private bool TryCollectBuildingCells(
            out BuildingCellBakeData[] buildingCells,
            out TerrainAnchorBakeData[] terrainAnchors,
            out string error)
        {
            var cells = new List<BuildingCellBakeData>();
            var anchors = new List<TerrainAnchorBakeData>();
            var usedOffsets = new HashSet<Vector3Int>();
            var pooledCells = authoringSystem.PooledCells;
            for (var index = 0; index < pooledCells.Count; index++)
            {
                if (pooledCells[index]
                        is not BuildingEntityAuthoringCellBox cell
                    || !cell.gameObject.activeSelf
                    || cell.AuthoringSystem != authoringSystem
                    || cell.BuildingRole == BuildingCellRole.None)
                {
                    continue;
                }

                if (!usedOffsets.Add(cell.LocalOffset))
                {
                    buildingCells = null;
                    terrainAnchors = null;
                    error = $"Entity Authoring contains duplicated Cell {cell.LocalOffset}.";
                    return false;
                }

                if (cell.BuildingRole == BuildingCellRole.Building)
                {
                    cells.Add(new BuildingCellBakeData
                    {
                        LocalOffset = cell.LocalOffset,
                        TerrainHeight = cell.TerrainHeight,
                        MaxTerrainHeightAdjustmentSteps =
                            cell.MaxTerrainHeightAdjustmentSteps
                    });
                }
                else
                {
                    if (!cell.HasValidTerrainAnchor)
                    {
                        buildingCells = null;
                        terrainAnchors = null;
                        error = $"Terrain Anchor {cell.LocalOffset} requires Terrain Height {WorldGrid.HeightStepsPerCell}.";
                        return false;
                    }

                    anchors.Add(new TerrainAnchorBakeData
                    {
                        LocalOffset = cell.LocalOffset,
                        MaxTerrainHeightAdjustmentSteps =
                            cell.MaxTerrainHeightAdjustmentSteps
                    });
                }
            }

            if (cells.Count == 0)
            {
                buildingCells = null;
                terrainAnchors = null;
                error = "Building Authoring requires at least one Building Cell.";
                return false;
            }

            if (!ContainsCenter(cells))
            {
                buildingCells = null;
                terrainAnchors = null;
                error = "Building Authoring Center Cell (0, 0, 0) must have the Building role.";
                return false;
            }

            cells.Sort((left, right) => CompareOffset(
                left.LocalOffset,
                right.LocalOffset));
            anchors.Sort((left, right) => CompareOffset(
                left.LocalOffset,
                right.LocalOffset));
            buildingCells = cells.ToArray();
            terrainAnchors = anchors.ToArray();
            error = string.Empty;
            return true;
        }

        private static HashSet<Vector3Int> CreateBuildingCellSet(
            IReadOnlyList<BuildingCellBakeData> cells)
        {
            var result = new HashSet<Vector3Int>();
            for (var index = 0; index < cells.Count; index++)
            {
                result.Add(cells[index].LocalOffset);
            }

            return result;
        }

        private static bool ContainsCenter(
            IReadOnlyList<BuildingCellBakeData> cells)
        {
            for (var index = 0; index < cells.Count; index++)
            {
                if (cells[index].LocalOffset == Vector3Int.zero)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBakePoint(
            BuildingWayPointMarker marker,
            IReadOnlyList<BuildingCellBakeData> buildingCells,
            ISet<Vector3Int> buildingCellSet,
            out BuildingWayPointBakeData point,
            out string error)
        {
            var cellSize = authoringSystem.CellSize;
            if (!float.IsFinite(cellSize) || cellSize <= 0f)
            {
                point = default;
                error = "Entity Authoring Cell size must be finite and positive.";
                return false;
            }

            var local = authoringSystem.transform.InverseTransformPoint(
                marker.transform.position) / cellSize;
            var offset = default(Vector3Int);
            var nearest = default(Vector3);
            var nearestDistance = float.PositiveInfinity;
            for (var index = 0; index < buildingCells.Count; index++)
            {
                var candidateOffset = buildingCells[index].LocalOffset;
                var candidate = ClampToCell(local, candidateOffset);
                var distance = (candidate - local).sqrMagnitude;
                if (distance >= nearestDistance)
                {
                    continue;
                }

                offset = candidateOffset;
                nearest = candidate;
                nearestDistance = distance;
            }

            const float positionTolerance = 0.000001f;
            if (nearestDistance > positionTolerance * positionTolerance)
            {
                var previous = marker.transform.position;
                var snapped = authoringSystem.transform.TransformPoint(
                    nearest * cellSize);
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(
                    marker.transform,
                    "Snap Building Way Marker");
#endif
                marker.transform.position = snapped;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(marker.transform);
#endif
                Debug.Log(
                    $"Way Marker '{marker.name}' was moved into nearest Building Cell {offset}: "
                    + $"{previous} -> {snapped}.",
                    marker);
                local = nearest;
            }

            var position = local - (Vector3)offset;
            const float boundsTolerance = 0.0001f;
            if (position.x < -0.5f - boundsTolerance
                || position.x > 0.5f + boundsTolerance
                || position.y < -boundsTolerance
                || position.y > 1f + boundsTolerance
                || position.z < -0.5f - boundsTolerance
                || position.z > 0.5f + boundsTolerance)
            {
                point = default;
                error = $"Way Marker '{marker.name}' is outside its resolved local Cell.";
                return false;
            }

            position.x = Mathf.Clamp(position.x, -0.5f, 0.5f);
            position.y = Mathf.Clamp01(position.y);
            position.z = Mathf.Clamp(position.z, -0.5f, 0.5f);
            if (!IsOnExternalBoundary(
                    marker.ExternalDirection,
                    offset,
                    position,
                    buildingCellSet))
            {
                point = default;
                error = $"External Way Marker '{marker.name}' is not on its selected Cell boundary.";
                return false;
            }

            var quantized = QuantizeExternalBoundaryPosition(
                marker.ExternalDirection,
                position);
            if ((quantized - position).sqrMagnitude > positionTolerance * positionTolerance)
            {
                var previous = marker.transform.position;
                var snapped = authoringSystem.transform.TransformPoint(
                    ((Vector3)offset + quantized) * cellSize);
#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(
                    marker.transform,
                    "Quantize Building Way Marker");
#endif
                marker.transform.position = snapped;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(marker.transform);
#endif
                Debug.Log(
                    $"Way Marker '{marker.name}' was quantized on Building Cell {offset}: "
                    + $"{previous} -> {snapped}.",
                    marker);
                position = quantized;
            }

            point = new BuildingWayPointBakeData
            {
                LocalCellOffset = offset,
                LocalPosition = position,
                ExternalDirection = marker.ExternalDirection
            };
            error = string.Empty;
            return true;
        }

        private static Vector3 QuantizeExternalBoundaryPosition(
            BuildingWayPointDirection direction,
            Vector3 position)
        {
            switch (direction)
            {
                case BuildingWayPointDirection.North:
                case BuildingWayPointDirection.South:
                    position.x = QuantizeBoundaryCoordinate(position.x);
                    break;
                case BuildingWayPointDirection.East:
                case BuildingWayPointDirection.West:
                    position.z = QuantizeBoundaryCoordinate(position.z);
                    break;
            }

            return position;
        }

        private static Vector3 ProjectToExternalBoundary(
            BuildingWayPointDirection direction,
            Vector3 position)
        {
            position = QuantizeExternalBoundaryPosition(direction, position);
            switch (direction)
            {
                case BuildingWayPointDirection.North:
                    position.z = 0.5f;
                    break;
                case BuildingWayPointDirection.East:
                    position.x = 0.5f;
                    break;
                case BuildingWayPointDirection.South:
                    position.z = -0.5f;
                    break;
                case BuildingWayPointDirection.West:
                    position.x = -0.5f;
                    break;
            }

            return position;
        }

        private static Vector3Int GetDirectionOffset(
            BuildingWayPointDirection direction) => direction switch
        {
            BuildingWayPointDirection.North => Vector3Int.forward,
            BuildingWayPointDirection.East => Vector3Int.right,
            BuildingWayPointDirection.South => Vector3Int.back,
            BuildingWayPointDirection.West => Vector3Int.left,
            _ => Vector3Int.zero
        };

        private static float QuantizeBoundaryCoordinate(float value) =>
            Mathf.Clamp(Mathf.Round(value * 4f) / 4f, -0.5f, 0.5f);

        private static bool IsOnExternalBoundary(
            BuildingWayPointDirection direction,
            Vector3Int cellOffset,
            Vector3 position,
            ISet<Vector3Int> buildingCells)
        {
            const float tolerance = 0.001f;
            return direction switch
            {
                BuildingWayPointDirection.None => true,
                BuildingWayPointDirection.North =>
                    Mathf.Abs(position.z - 0.5f) <= tolerance
                    && !buildingCells.Contains(cellOffset + Vector3Int.forward),
                BuildingWayPointDirection.East =>
                    Mathf.Abs(position.x - 0.5f) <= tolerance
                    && !buildingCells.Contains(cellOffset + Vector3Int.right),
                BuildingWayPointDirection.South =>
                    Mathf.Abs(position.z + 0.5f) <= tolerance
                    && !buildingCells.Contains(cellOffset + Vector3Int.back),
                BuildingWayPointDirection.West =>
                    Mathf.Abs(position.x + 0.5f) <= tolerance
                    && !buildingCells.Contains(cellOffset + Vector3Int.left),
                _ => false
            };
        }

        private static Vector3 ClampToCell(
            Vector3 position,
            Vector3Int cellOffset) => new(
                Mathf.Clamp(
                    position.x,
                    cellOffset.x - 0.5f,
                    cellOffset.x + 0.5f),
                Mathf.Clamp(
                    position.y,
                    cellOffset.y,
                    cellOffset.y + 1f),
                Mathf.Clamp(
                    position.z,
                    cellOffset.z - 0.5f,
                    cellOffset.z + 0.5f));

        private static int CompareOffset(Vector3Int left, Vector3Int right)
        {
            var y = left.y.CompareTo(right.y);
            if (y != 0)
            {
                return y;
            }

            var z = left.z.CompareTo(right.z);
            return z != 0 ? z : left.x.CompareTo(right.x);
        }

        private static int CompareHierarchy(
            BuildingWayPointMarker left,
            BuildingWayPointMarker right) => string.CompareOrdinal(
                GetHierarchyPath(left != null ? left.transform : null),
                GetHierarchyPath(right != null ? right.transform : null));

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            var path = target.GetSiblingIndex().ToString("D6");
            while (target.parent != null)
            {
                target = target.parent;
                path = target.GetSiblingIndex().ToString("D6") + "/" + path;
            }

            return path;
        }
    }
}
