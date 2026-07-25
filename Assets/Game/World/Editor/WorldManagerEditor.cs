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
            DrawStatus(manager);
            EditorGUILayout.Space();
            DrawWorldActions(manager);
            EditorGUILayout.Space();
            DrawPreparedSceneActions(manager);
        }

        private static void DrawStatus(WorldManager manager)
        {
            if (manager.Generator == null
                || manager.EditController == null
                || manager.HydrologyController == null
                || manager.Renderer == null
                || manager.SaveController == null)
            {
                EditorGUILayout.HelpBox(
                    "Generation, Editing, Hydrology, Renderer, and Save " +
                    "references must be assigned.",
                    MessageType.Error);
            }

            if (manager.HasWorld)
            {
                var world = manager.CurrentWorldData;
                var waterBodyCount =
                    manager.HydrologyController?.State?.WaterBodies.Count ?? 0;
                EditorGUILayout.HelpBox(
                    $"Active world: {world.Size} x {world.Size} x {world.Height}\n" +
                    $"Seed: {world.Seed}\n" +
                    $"Water bodies: {waterBodyCount}\n" +
                    $"Change ID: {world.CurrentChangeId}\n" +
                    $"Dirty: {(manager.IsDirty ? "Yes" : "No")}\n" +
                    $"Renderer: {manager.Renderer.BindingMode}",
                    MessageType.None);
            }
            else if (manager.CurrentWorldDataAsset != null)
            {
                EditorGUILayout.HelpBox(
                    "A WorldDataAsset is assigned. It will be activated when Play starts, " +
                    "or by preparing the scene.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No WorldDataAsset is assigned. A new world will be generated on Play.",
                    MessageType.Info);
            }

            if (manager.SaveController != null)
            {
                EditorGUILayout.LabelField(
                    "Active World File",
                    manager.SaveController.ActiveSavePath,
                    EditorStyles.wordWrappedLabel);
            }
        }

        private static void DrawWorldActions(WorldManager manager)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate New"))
                {
                    manager.GenerateWorld();
                }

                if (GUILayout.Button("Load..."))
                {
                    LoadFromFile(manager);
                }
            }

            using (new EditorGUI.DisabledScope(!manager.HasWorld))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save"))
                {
                    if (manager.SaveController.HasActiveSavePath)
                    {
                        manager.SaveWorld();
                    }
                    else
                    {
                        SaveAs(manager);
                    }
                }

                if (GUILayout.Button("Save As..."))
                {
                    SaveAs(manager);
                }

                if (GUILayout.Button("Unload"))
                {
                    manager.UnloadWorld();
                }
            }
        }

        private static void DrawPreparedSceneActions(WorldManager manager)
        {
            EditorGUILayout.LabelField(
                "Editor Prepared Scene",
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!manager.HasWorld))
            {
                if (GUILayout.Button("Prepare Current World In Scene"))
                {
                    PrepareCurrentWorld(manager);
                }
            }

            var asset = manager.CurrentWorldDataAsset;
            using (new EditorGUI.DisabledScope(
                       asset == null || !asset.HasPreparedRenderCache))
            {
                if (GUILayout.Button("Remove Prepared Render Cache"))
                {
                    WorldPreparedRenderCacheUtility.Remove(manager, asset);
                }
            }
        }

        private static void SaveAs(WorldManager manager)
        {
            var currentPath = manager.SaveController.ActiveSavePath;
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                currentPath = manager.SaveController.ConfiguredSavePath;
            }

            var path = EditorUtility.SaveFilePanel(
                "Save World",
                Path.GetDirectoryName(currentPath) ?? Application.persistentDataPath,
                Path.GetFileNameWithoutExtension(currentPath),
                "mcw");
            if (!string.IsNullOrEmpty(path))
            {
                manager.SaveWorldAs(path);
            }
        }

        private static void LoadFromFile(WorldManager manager)
        {
            var currentPath = manager.SaveController.ActiveSavePath;
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                currentPath = manager.SaveController.ConfiguredSavePath;
            }

            var path = EditorUtility.OpenFilePanel(
                "Load World",
                Path.GetDirectoryName(currentPath) ?? Application.persistentDataPath,
                "mcw");
            if (!string.IsNullOrEmpty(path))
            {
                manager.LoadWorld(path);
            }
        }

        private static void PrepareCurrentWorld(WorldManager manager)
        {
            var asset = manager.CurrentWorldDataAsset;
            if (!AssetDatabase.Contains(asset))
            {
                const string directory = "Assets/Game/World/Data";
                if (!AssetDatabase.IsValidFolder(directory))
                {
                    AssetDatabase.CreateFolder("Assets/Game/World", "Data");
                }

                var path = EditorUtility.SaveFilePanelInProject(
                    "Create WorldDataAsset",
                    asset != null ? asset.name : "WorldData",
                    "asset",
                    "Choose where the prepared WorldDataAsset will be stored.",
                    directory);
                if (string.IsNullOrEmpty(path))
                {
                    return;
                }

                asset = WorldPreparedRenderCacheUtility.EnsurePersistentAsset(
                    manager,
                    path);
            }

            WorldPreparedRenderCacheUtility.Prepare(manager, asset);
        }
    }
}
