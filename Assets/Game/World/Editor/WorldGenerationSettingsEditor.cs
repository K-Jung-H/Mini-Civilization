using MiniCivilization.World.Generation;
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
                $"Cell: {settings.CellSize:g} x {settings.CellSize:g} x {settings.CellSize:g}\n" +
                $"Height step: {settings.HeightStep:g}\n" +
                $"World Cells: {settings.WorldSize} x {settings.WorldHeight} x {settings.WorldSize}\n" +
                $"Total Cells: {cells:N0}\n" +
                $"Sea level: {settings.SeaLevelUnits * settings.HeightStep:g}",
                MessageType.None);
        }
    }
}
