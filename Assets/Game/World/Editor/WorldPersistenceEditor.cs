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
                    "Default Save File",
                    persistence.ConfiguredSavePath,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Current World File",
                    persistence.ActiveSavePath,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(
                    "Default File Exists",
                    File.Exists(persistence.ConfiguredSavePath) ? "Yes" : "No");

                if (GUILayout.Button("Choose Default Save File..."))
                {
                    var currentPath = persistence.ConfiguredSavePath;
                    var path = EditorUtility.SaveFilePanel(
                        "Choose Default World File",
                        Path.GetDirectoryName(currentPath) ?? Application.persistentDataPath,
                        Path.GetFileNameWithoutExtension(currentPath),
                        "mcw");
                    if (!string.IsNullOrEmpty(path))
                    {
                        Undo.RecordObject(persistence, "Change Default World File");
                        persistence.SetConfiguredSavePath(path);
                        EditorUtility.SetDirty(persistence);
                    }
                }

                if (GUILayout.Button("Use PersistentData Default"))
                {
                    Undo.RecordObject(persistence, "Reset Default World File");
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
