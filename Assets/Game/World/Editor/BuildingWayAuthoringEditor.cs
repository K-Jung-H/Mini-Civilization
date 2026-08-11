using MiniCivilization.World.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(BuildingWayAuthoring))]
    public sealed class BuildingWayAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (!GUILayout.Button("Building Way Bake"))
            {
                return;
            }

            var authoring = (BuildingWayAuthoring)target;
            if (authoring.TargetController != null)
            {
                Undo.RecordObject(
                    authoring.TargetController,
                    "Bake Building Way");
            }

            if (!authoring.TryBake(out var error))
            {
                Debug.LogError(error, authoring);
                return;
            }

            EditorUtility.SetDirty(authoring.TargetController);
            if (PrefabUtility.IsPartOfPrefabAsset(
                    authoring.TargetController))
            {
                PrefabUtility.SavePrefabAsset(
                    authoring.TargetController.gameObject);
            }

            if (authoring.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
            }
        }

        private void OnSceneGUI()
        {
            var authoring = (BuildingWayAuthoring)target;
            var previousColor = Handles.color;
            var previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            Handles.color = new Color(0.15f, 0.8f, 1f, 1f);
            var markerContainer = authoring.MarkerContainer;
            if (markerContainer == null)
            {
                Handles.color = previousColor;
                Handles.zTest = previousZTest;
                return;
            }

            var markers = markerContainer.GetComponentsInChildren<
                BuildingWayPointMarker>(true);
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
                    if (connection.Target == null)
                    {
                        continue;
                    }

                    var start = marker.transform.position;
                    var end = connection.Target.transform.position;
                    Handles.DrawAAPolyLine(4f, start, end);
                    if (!connection.OneWay)
                    {
                        continue;
                    }

                    var direction = end - start;
                    if (direction.sqrMagnitude <= 0f)
                    {
                        continue;
                    }

                    var midpoint = Vector3.Lerp(start, end, 0.65f);
                    var size = HandleUtility.GetHandleSize(midpoint) * 0.08f;
                    Handles.ConeHandleCap(
                        0,
                        midpoint,
                        Quaternion.LookRotation(direction.normalized),
                        size,
                        EventType.Repaint);
                }
            }

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }
    }

    internal static class BuildingWayPointMarkerGizmo
    {
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        private static void DrawMarker(
            BuildingWayPointMarker marker,
            GizmoType gizmoType)
        {
            var previousColor = Handles.color;
            var previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            Handles.color = marker.ExternalDirection
                == MiniCivilization.World.Entities.BuildingWayPointDirection.None
                ? new Color(0.15f, 0.8f, 1f, 1f)
                : new Color(1f, 0.55f, 0.1f, 1f);
            var size = HandleUtility.GetHandleSize(marker.transform.position)
                * 0.06f;
            Handles.SphereHandleCap(
                0,
                marker.transform.position,
                Quaternion.identity,
                size,
                EventType.Repaint);
            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }
    }
}
