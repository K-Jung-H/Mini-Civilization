using System;
using System.Collections.Generic;
using MiniCivilization.World.Persistence;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(SaveLoadManager))]
    public sealed class SaveLoadManagerEditor : UnityEditor.Editor
    {
        private const string DefaultSaveName = "World";

        private readonly List<WorldSaveDescriptor> worldSaves = new();
        private bool catalogLoaded;
        private string catalogError;
        private string pendingSaveName = DefaultSaveName;
        private SaveNameRequest saveNameRequest;
        private GUIStyle activeSaveNameStyle;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var manager = (SaveLoadManager)target;

            EditorGUILayout.Space();
            if (!Application.isPlaying)
            {
                DrawInitialWorldSelection(manager);
                return;
            }

            EditorGUILayout.LabelField(
                "Active Save File",
                manager.ActiveSaveName,
                ActiveSaveNameStyle);

            if (!manager.HasActiveSession)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (manager.IsTemporarySession)
            {
                if (GUILayout.Button("Save"))
                {
                    saveNameRequest = SaveNameRequest.NewWorld;
                }
            }
            else
            {
                if (GUILayout.Button("Save"))
                {
                    TrySave(manager);
                }

                if (GUILayout.Button("Save As"))
                {
                    saveNameRequest = SaveNameRequest.SaveAs;
                }
            }

            EditorGUILayout.EndHorizontal();
            DrawSaveNameRequest(manager);
        }

        private GUIStyle ActiveSaveNameStyle => activeSaveNameStyle ??=
            new GUIStyle(EditorStyles.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };

        private void DrawInitialWorldSelection(SaveLoadManager manager)
        {
            EditorGUILayout.LabelField("Initial World", EditorStyles.boldLabel);
            if (!catalogLoaded)
            {
                RefreshWorldSaves(manager);
            }

            if (GUILayout.Button("Refresh World Saves"))
            {
                RefreshWorldSaves(manager);
            }

            if (!string.IsNullOrEmpty(catalogError))
            {
                EditorGUILayout.HelpBox(catalogError, MessageType.Error);
                return;
            }

            if (worldSaves.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrWhiteSpace(manager.InitialSaveFolderName)
                        ? "No saved worlds exist under WorldSaves."
                        : "The selected initial world no longer exists under WorldSaves.",
                    MessageType.Info);
                if (!string.IsNullOrWhiteSpace(manager.InitialSaveFolderName)
                    && GUILayout.Button("Use World Generation Settings"))
                {
                    Undo.RecordObject(manager, "Clear Initial World Save");
                    manager.ClearInitialWorld();
                    EditorUtility.SetDirty(manager);
                }

                return;
            }

            var labels = new string[worldSaves.Count + 1];
            labels[0] = "<Use World Generation Settings>";
            var selectedIndex = 0;
            for (var index = 0; index < worldSaves.Count; index++)
            {
                var descriptor = worldSaves[index];
                labels[index + 1] = descriptor.SaveName == descriptor.WorldFolderName
                    ? descriptor.SaveName
                    : $"{descriptor.SaveName} ({descriptor.WorldFolderName})";
                if (string.Equals(manager.InitialSaveFolderName,
                        descriptor.WorldFolderName,
                        StringComparison.Ordinal))
                {
                    selectedIndex = index + 1;
                }
            }

            EditorGUI.BeginChangeCheck();
            var nextIndex = EditorGUILayout.Popup(
                "Saved World",
                selectedIndex,
                labels);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            Undo.RecordObject(manager, "Select Initial World Save");
            if (nextIndex == 0)
            {
                manager.ClearInitialWorld();
            }
            else
            {
                manager.SelectInitialWorld(worldSaves[nextIndex - 1]);
            }

            EditorUtility.SetDirty(manager);
        }

        private void RefreshWorldSaves(SaveLoadManager manager)
        {
            catalogLoaded = true;
            catalogError = null;
            worldSaves.Clear();
            try
            {
                worldSaves.AddRange(manager.GetSavedWorlds());
            }
            catch (Exception exception)
            {
                catalogError = exception.Message;
            }
        }

        private void DrawSaveNameRequest(SaveLoadManager manager)
        {
            if (saveNameRequest == SaveNameRequest.None)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                saveNameRequest == SaveNameRequest.SaveAs
                    ? "Save As"
                    : "Save New World",
                EditorStyles.boldLabel);
            pendingSaveName = EditorGUILayout.TextField(
                "Save Name",
                pendingSaveName);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm"))
            {
                if (TrySaveWithName(manager, saveNameRequest, pendingSaveName))
                {
                    saveNameRequest = SaveNameRequest.None;
                }
            }

            if (GUILayout.Button("Cancel"))
            {
                saveNameRequest = SaveNameRequest.None;
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void TrySave(SaveLoadManager manager)
        {
            try
            {
                manager.Save();
                Debug.Log(
                    $"World saved: WorldSaves/{manager.ActiveSaveFolderName}",
                    manager);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, manager);
            }
        }

        private static bool TrySaveWithName(
            SaveLoadManager manager,
            SaveNameRequest request,
            string saveName)
        {
            try
            {
                if (request == SaveNameRequest.SaveAs)
                {
                    manager.SaveAs(saveName);
                    Debug.Log(
                        $"World saved as: WorldSaves/{manager.ActiveSaveFolderName}",
                        manager);
                }
                else
                {
                    manager.SaveNewWorld(saveName);
                    Debug.Log(
                        $"World saved: WorldSaves/{manager.ActiveSaveFolderName}",
                        manager);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, manager);
                return false;
            }
        }

        private enum SaveNameRequest
        {
            None,
            NewWorld,
            SaveAs
        }
    }
}
