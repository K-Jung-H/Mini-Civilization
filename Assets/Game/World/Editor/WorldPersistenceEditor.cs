using System;
using System.IO;
using MiniCivilization.World.Persistence;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldPersistence))]
    public sealed class WorldPersistenceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var persistence = (WorldPersistence)target;
            EditorGUILayout.Space();

            try
            {
                EditorGUILayout.LabelField(
                    "Startup Save File",
                    persistence.ConfiguredSavePath,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Current World File",
                    persistence.ActiveSavePath,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Startup File Exists",
                    File.Exists(persistence.ConfiguredSavePath) ? "Yes" : "No");

                if (GUILayout.Button("Choose Startup Save File..."))
                {
                    var currentPath = persistence.ConfiguredSavePath;
                    var path = EditorUtility.SaveFilePanel(
                        "Choose Startup World File",
                        Path.GetDirectoryName(currentPath) ?? Application.persistentDataPath,
                        Path.GetFileNameWithoutExtension(currentPath),
                        "mcw");
                    if (!string.IsNullOrEmpty(path))
                    {
                        Undo.RecordObject(persistence, "Change Startup World File");
                        persistence.SetConfiguredSavePath(path);
                        EditorUtility.SetDirty(persistence);
                    }
                }

                if (GUILayout.Button("Use PersistentData Default"))
                {
                    Undo.RecordObject(persistence, "Reset Startup World File");
                    persistence.ClearConfiguredSavePathOverride();
                    EditorUtility.SetDirty(persistence);
                }
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }
        }
    }
}
