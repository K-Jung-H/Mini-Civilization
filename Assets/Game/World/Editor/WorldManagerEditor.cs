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
        }

        private static void DrawStatus(WorldManager manager)
        {
            if (manager.Generator == null
                || manager.EditController == null
                || manager.WaterFlowController == null
                || manager.Renderer == null
                || manager.StreamingController == null
                || manager.EntityManager == null
                || manager.SaveController == null)
            {
                EditorGUILayout.HelpBox(
                    "Generation, Editing, Water Flow, Renderer, Streaming, " +
                    "Entity Manager, and Save references must be assigned.",
                    MessageType.Error);
            }

            if (manager.HasWorld)
            {
                var world = manager.CurrentWorldData;
                var runtime = manager.CurrentWorldRuntime;
                var waterBodyCount =
                    manager.WaterFlowController?.State?.WaterBodies.Count ?? 0;
                var streaming = manager.StreamingController;
                var streamingCenter = streaming != null && streaming.HasCenter
                    ? streaming.CurrentCenter.ToString()
                    : "None";
                EditorGUILayout.HelpBox(
                    $"Active world: {world.Size} x {world.Size} x {world.Height}\n" +
                    $"Seed: {world.Seed}\n" +
                    $"Water bodies: {waterBodyCount}\n" +
                    $"Streaming center: {streamingCenter}\n" +
                    $"Prepared cache columns: {runtime.SurfaceCache.PreparedColumnCount}\n" +
                    $"Rendered/Pooled patches: {manager.Renderer.RenderedPatchCount}/" +
                    $"{manager.Renderer.PooledPatchCount}\n" +
                    $"Change ID: {runtime.CurrentChangeId}\n" +
                    $"Dirty: {(manager.IsDirty ? "Yes" : "No")}\n" +
                    $"Renderer: {manager.Renderer.BindingMode}",
                    MessageType.None);
            }
            else if (manager.CurrentWorldDataAsset != null)
            {
                EditorGUILayout.HelpBox(
                    "A WorldDataAsset is assigned. It will be activated when Play starts.",
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

    }
}
