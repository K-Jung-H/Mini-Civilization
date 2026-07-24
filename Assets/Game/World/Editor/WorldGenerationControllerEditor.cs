using MiniCivilization.World.Generation;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldGenerationController))]
    public sealed class WorldGenerationControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var controller = (WorldGenerationController)target;
            EditorGUILayout.Space();

            if (controller.Settings == null)
            {
                EditorGUILayout.HelpBox(
                    "WorldGenerationSettings is not assigned.",
                    MessageType.Error);
            }
            else if (!controller.Settings.TryValidate(out var error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }

            if (GUILayout.Button("Random Seed"))
            {
                Undo.RecordObject(controller, "Randomize World Seed");
                controller.RandomizeSeed();
                EditorUtility.SetDirty(controller);
            }
        }
    }
}
