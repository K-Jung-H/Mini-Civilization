using System;
using System.Collections.Generic;
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

                if (!TryBakePoint(marker, out points[index], out error))
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

            targetController.SetBakedWays(points, bakedLinks.ToArray());
            error = string.Empty;
            return true;
        }

        private bool TryBakePoint(
            BuildingWayPointMarker marker,
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
            var offset = new Vector3Int(
                Mathf.FloorToInt(local.x + 0.5f),
                Mathf.FloorToInt(local.y + 0.0001f),
                Mathf.FloorToInt(local.z + 0.5f));
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
                    position))
            {
                point = default;
                error = $"External Way Marker '{marker.name}' is not on its selected Cell boundary.";
                return false;
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

        private static bool IsOnExternalBoundary(
            BuildingWayPointDirection direction,
            Vector3 position)
        {
            const float tolerance = 0.001f;
            return direction switch
            {
                BuildingWayPointDirection.None => true,
                BuildingWayPointDirection.North =>
                    Mathf.Abs(position.z - 0.5f) <= tolerance,
                BuildingWayPointDirection.East =>
                    Mathf.Abs(position.x - 0.5f) <= tolerance,
                BuildingWayPointDirection.South =>
                    Mathf.Abs(position.z + 0.5f) <= tolerance,
                BuildingWayPointDirection.West =>
                    Mathf.Abs(position.x + 0.5f) <= tolerance,
                _ => false
            };
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
