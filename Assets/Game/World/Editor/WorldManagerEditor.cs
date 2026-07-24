using System.IO;
using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldManager))]
    public sealed class WorldManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var manager = (WorldManager)target;

            EditorGUILayout.Space();
            if (manager.Generator == null
                || manager.Renderer == null
                || manager.Persistence == null)
            {
                EditorGUILayout.HelpBox(
                    "Generation, Renderer, and Persistence references must be assigned.",
                    MessageType.Error);
            }

            if (manager.HasWorld)
            {
                var world = manager.CurrentWorld.Data;
                EditorGUILayout.HelpBox(
                    $"Active world: {world.Size} x {world.Size} x {world.Height}\n" +
                    $"Seed: {world.Seed}\n" +
                    $"Water bodies: {manager.CurrentWorld.WaterBodies.Count}",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("No active world.", MessageType.Info);
            }

            if (manager.Persistence != null)
            {
                EditorGUILayout.LabelField(
                    "Current World File",
                    manager.Persistence.ActiveSavePath,
                    EditorStyles.wordWrappedLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate"))
                {
                    manager.GenerateWorld();
                }

                using (new EditorGUI.DisabledScope(!manager.HasWorld))
                {
                    if (GUILayout.Button("Save As..."))
                    {
                        SaveAs(manager);
                    }
                }

                if (GUILayout.Button("Load..."))
                {
                    LoadFromFile(manager);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!manager.HasWorld))
                {
                    if (GUILayout.Button("Save Current File"))
                    {
                        manager.SaveWorld();
                    }

                    if (GUILayout.Button("Unload"))
                    {
                        manager.UnloadWorld();
                    }
                }
            }
        }

        private static void SaveAs(WorldManager manager)
        {
            var currentPath = manager.Persistence.ActiveSavePath;
            var path = EditorUtility.SaveFilePanel(
                "Save World",
                Path.GetDirectoryName(currentPath) ?? Application.persistentDataPath,
                Path.GetFileNameWithoutExtension(currentPath),
                "mcw");
            if (!string.IsNullOrEmpty(path))
            {
                manager.SaveWorld(path);
            }
        }

        private static void LoadFromFile(WorldManager manager)
        {
            var currentPath = manager.Persistence.ActiveSavePath;
            var path = EditorUtility.OpenFilePanel(
                "Load World",
                Path.GetDirectoryName(currentPath) ?? Application.persistentDataPath,
                "mcw");
            if (!string.IsNullOrEmpty(path))
            {
                manager.LoadWorld(path);
            }
        }
    }
}
