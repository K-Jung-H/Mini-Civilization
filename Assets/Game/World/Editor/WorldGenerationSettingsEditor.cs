using MiniCivilization.World.Authoring;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldGenerationSettings))]
    public sealed class WorldGenerationSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var settings = (WorldGenerationSettings)target;
            EditorGUILayout.Space();

            if (!settings.TryValidate(out var error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                return;
            }

            var cells = (long)settings.WorldSize * settings.WorldSize * settings.WorldHeight;
            EditorGUILayout.HelpBox(
                $"Total cells: {cells:N0}\nVertical height units: {settings.WorldHeight * 5}\nSea level units: {settings.SeaLevelUnits}",
                MessageType.None);
        }
    }
}
