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

            var cells = (long)settings.WorldSize
                * settings.WorldSize
                * settings.WorldHeight;
            var halfChunkCount = settings.InitialChunkCountXZ / 2;
            EditorGUILayout.HelpBox(
                $"World type: {settings.WorldType}\n" +
                $"Cell: {settings.CellSize:g} x {settings.CellSize:g} x {settings.CellSize:g}\n" +
                $"Height step: {settings.HeightStep:g}\n" +
                $"Initial Chunks: -{halfChunkCount}..{halfChunkCount} x -{halfChunkCount}..{halfChunkCount}\n" +
                $"Initial Cells: {settings.WorldSize} x {settings.WorldHeight} x {settings.WorldSize}\n" +
                $"Initial Cell capacity: {cells:N0}\n" +
                $"Terrain base height: {settings.TerrainBaseHeightUnits * settings.HeightStep:g}\n" +
                $"Default Sea surface: {settings.DefaultSeaSurfaceUnits * settings.HeightStep:g}",
                MessageType.None);
        }
    }
}
