using System;
using System.IO;
using MiniCivilization.World.Authoring;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using UnityEditor;
using UnityEngine;

namespace MiniCivilization.World.Editor
{
    [CustomEditor(typeof(WorldGeneratorAuthoring))]
    public sealed class WorldGeneratorAuthoringEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var authoring = (WorldGeneratorAuthoring)target;
            EditorGUILayout.Space();

            if (authoring.Settings == null)
            {
                EditorGUILayout.HelpBox("Create or assign WorldGenerationSettings before generating a world.", MessageType.Info);
                if (GUILayout.Button("Create Default Settings"))
                {
                    CreateDefaultAssets(authoring);
                }

                return;
            }

            if (!authoring.Settings.TryValidate(out var error))
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            else
            {
                var settings = authoring.Settings;
                var cellCount = (long)settings.WorldSize * settings.WorldSize * settings.WorldHeight;
                EditorGUILayout.HelpBox(
                    $"Cells: {cellCount:N0}\nHorizontal chunks: {settings.WorldSize / settings.ChunkSizeXZ} × {settings.WorldSize / settings.ChunkSizeXZ}\nHeight quantum: {WorldGrid.HeightStep:0.0}",
                    MessageType.None);
            }

            using (new EditorGUI.DisabledScope(!authoring.Settings.TryValidate(out _)))
            {
                if (GUILayout.Button("Generate World"))
                {
                    authoring.Generate();
                    EditorUtility.SetDirty(authoring);
                }

                if (GUILayout.Button("Randomize Seed and Generate"))
                {
                    Undo.RecordObject(authoring.Settings, "Randomize World Seed");
                    authoring.Settings.SetSeed(unchecked((int)DateTime.UtcNow.Ticks));
                    EditorUtility.SetDirty(authoring.Settings);
                    authoring.Generate();
                }
            }

            if (GUILayout.Button("Clear Generated World"))
            {
                authoring.ClearGeneratedWorld();
            }
        }

        private static void CreateDefaultAssets(WorldGeneratorAuthoring authoring)
        {
            const string directory = "Assets/Game/World/Settings";
            Directory.CreateDirectory(directory);

            var settings = CreateInstance<WorldGenerationSettings>();
            var settingsPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/WorldGenerationSettings.asset");
            AssetDatabase.CreateAsset(settings, settingsPath);

            var catalog = CreateInstance<WorldSurfaceCatalog>();
            var catalogPath = AssetDatabase.GenerateUniqueAssetPath($"{directory}/WorldSurfaceCatalog.asset");
            AssetDatabase.CreateAsset(catalog, catalogPath);

            var terrainShader = Shader.Find("Mini Civilization/World Terrain Lit");
            var waterShader = Shader.Find("Mini Civilization/World Water Lit");
            Material terrainMaterial = null;
            Material waterMaterial = null;
            if (terrainShader != null)
            {
                terrainMaterial = new Material(terrainShader) { name = "World Terrain Material" };
                AssetDatabase.CreateAsset(terrainMaterial, AssetDatabase.GenerateUniqueAssetPath($"{directory}/WorldTerrain.mat"));
            }

            if (waterShader != null)
            {
                waterMaterial = new Material(waterShader) { name = "World Water Material" };
                AssetDatabase.CreateAsset(waterMaterial, AssetDatabase.GenerateUniqueAssetPath($"{directory}/WorldWater.mat"));
            }

            Undo.RecordObject(authoring, "Assign World Generation Assets");
            authoring.SetSettings(settings);
            authoring.SetSurfaceCatalog(catalog);
            authoring.SetMaterials(terrainMaterial, waterMaterial);
            EditorUtility.SetDirty(authoring);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
        }
    }
}
