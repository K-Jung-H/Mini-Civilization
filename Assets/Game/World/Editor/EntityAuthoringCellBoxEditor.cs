using MiniCivilization.World.Authoring;
using UnityEditor;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(EntityAuthoringCellBox), true)]
    public class EntityAuthoringCellBoxEditor : UnityEditor.Editor
    {
        private SerializedProperty terrainHeight;
        private SerializedProperty wireColor;
        private SerializedProperty terrainColor;

        protected virtual void OnEnable()
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
                DrawAdditionalProperties(cellBox);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(wireColor);
                    EditorGUILayout.PropertyField(terrainColor);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawAdditionalProperties(
            EntityAuthoringCellBox cellBox)
        {
        }
    }

    [CustomEditor(typeof(BuildingEntityAuthoringCellBox))]
    public sealed class BuildingEntityAuthoringCellBoxEditor
        : EntityAuthoringCellBoxEditor
    {
        private SerializedProperty buildingRole;
        private SerializedProperty maxTerrainHeightAdjustmentSteps;

        protected override void OnEnable()
        {
            base.OnEnable();
            buildingRole = serializedObject.FindProperty("buildingRole");
            maxTerrainHeightAdjustmentSteps = serializedObject.FindProperty(
                "maxTerrainHeightAdjustmentSteps");
        }

        protected override void DrawAdditionalProperties(
            EntityAuthoringCellBox cellBox)
        {
            EditorGUILayout.PropertyField(buildingRole);
            var role = (BuildingCellRole)buildingRole.enumValueIndex;
            if (role == BuildingCellRole.TerrainAnchor
                || role == BuildingCellRole.Building)
            {
                EditorGUILayout.PropertyField(maxTerrainHeightAdjustmentSteps);
            }
        }
    }
}
