using System;
using System.IO;
using MiniCivilization.World.Persistence;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldSaveController))]
    public sealed class WorldSaveControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var saveController = (WorldSaveController)target;
            EditorGUILayout.Space();

            try
            {
                EditorGUILayout.LabelField(
                    "Default Save File",
                    saveController.ConfiguredSavePath,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Current World File",
                    saveController.ActiveSavePath,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Default File Exists",
                    File.Exists(saveController.ConfiguredSavePath) ? "Yes" : "No");

                if (GUILayout.Button("Choose Default Save File..."))
                {
                    var currentPath = saveController.ConfiguredSavePath;
                    var path = EditorUtility.SaveFilePanel(
                        "Choose Default World File",
                        Path.GetDirectoryName(currentPath) ?? Application.persistentDataPath,
                        Path.GetFileNameWithoutExtension(currentPath),
                        "mcw");
                    if (!string.IsNullOrEmpty(path))
                    {
                        Undo.RecordObject(saveController, "Change Default World File");
                        saveController.SetConfiguredSavePath(path);
                        EditorUtility.SetDirty(saveController);
                    }
                }

                if (GUILayout.Button("Use PersistentData Default"))
                {
                    Undo.RecordObject(saveController, "Reset Default World File");
                    saveController.ClearConfiguredSavePathOverride();
                    EditorUtility.SetDirty(saveController);
                }
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }
        }
    }
}
