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

            EntityAuthoringSystemEditor.RebuildPreview(
                authoring.AuthoringSystem);

            if (authoring.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
            }
        }

    }

    [CustomEditor(typeof(BuildingWayPointMarker))]
    public sealed class BuildingWayPointMarkerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
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
            Handles.color = new Color32(0, 0, 255, 255);
            var size = HandleUtility.GetHandleSize(marker.transform.position)
                * 0.1f;
            Handles.SphereHandleCap(
                0,
                marker.transform.position,
                Quaternion.identity,
                size,
                EventType.Repaint);

            var connections = marker.Connections;
            for (var index = 0; index < connections.Count; index++)
            {
                var connection = connections[index];
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
                var arrowSize = HandleUtility.GetHandleSize(midpoint) * 0.08f;
                Handles.ConeHandleCap(
                    0,
                    midpoint,
                    Quaternion.LookRotation(direction.normalized),
                    arrowSize,
                    EventType.Repaint);
            }

            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }
    }
}
