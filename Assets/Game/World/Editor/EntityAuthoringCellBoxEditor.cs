using MiniCivilization.World.Authoring;
using UnityEditor;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(EntityAuthoringCellBox))]
    public sealed class EntityAuthoringCellBoxEditor : UnityEditor.Editor
    {
        private SerializedProperty terrainHeight;
        private SerializedProperty wireColor;
        private SerializedProperty terrainColor;

        private void OnEnable()
        {
            terrainHeight = serializedObject.FindProperty("terrainHeight");
            wireColor = serializedObject.FindProperty("wireColor");
            terrainColor = serializedObject.FindProperty("terrainColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var cellBox = (EntityAuthoringCellBox)target;

            if (cellBox.AuthoringSystem == null)
            {
                EditorGUILayout.PropertyField(terrainHeight);
                EditorGUILayout.PropertyField(wireColor);
                EditorGUILayout.PropertyField(terrainColor);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Local Offset",
                    cellBox.LocalOffset.ToString());
                EditorGUILayout.PropertyField(terrainHeight);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(wireColor);
                    EditorGUILayout.PropertyField(terrainColor);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
