using System.Linq;
using MiniCivilization.World.Authoring;
using MiniCivilization.World.Definitions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiniCivilization.World.Editor
{
    public static class WorldTestSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SettingsDirectory = "Assets/Game/World/Settings";
        private const string SettingsPath = SettingsDirectory + "/WorldGenerationSettings.asset";
        private const string CatalogPath = SettingsDirectory + "/WorldSurfaceCatalog.asset";
        private const string TerrainMaterialPath = SettingsDirectory + "/WorldTerrain.mat";
        private const string WaterMaterialPath = SettingsDirectory + "/WorldWater.mat";

        [InitializeOnLoadMethod]
        private static void SetupOnceWhenAssetsAreMissing()
        {
            if (Application.isBatchMode || AssetDatabase.LoadAssetAtPath<WorldGenerationSettings>(SettingsPath) != null)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<WorldGenerationSettings>(SettingsPath) != null)
                {
                    return;
                }

                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.isDirty && activeScene.path != ScenePath)
                {
                    Debug.LogWarning(
                        "World test scene setup was deferred because the active scene has unsaved changes. " +
                        "Run Mini Civilization > Setup World Test Scene after saving it.");
                    return;
                }

                Setup();
            };
        }

        [MenuItem("Mini Civilization/Setup World Test Scene")]
        public static void Setup()
        {
            EnsureSettingsDirectory();

            var settings = LoadOrCreateSettings();
            var catalog = LoadOrCreateAsset<WorldSurfaceCatalog>(CatalogPath);
            var terrainMaterial = LoadOrCreateMaterial(
                TerrainMaterialPath,
                "Mini Civilization/World Terrain Lit",
                "World Terrain Material");
            var waterMaterial = LoadOrCreateMaterial(
                WaterMaterialPath,
                "Mini Civilization/World Water Lit",
                "World Water Material");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var generatorObject = GameObject.Find("World Generator") ?? new GameObject("World Generator");
            var authoring = generatorObject.GetComponent<WorldGeneratorAuthoring>()
                ?? generatorObject.AddComponent<WorldGeneratorAuthoring>();
            authoring.SetSettings(settings);
            authoring.SetSurfaceCatalog(catalog);
            authoring.SetMaterials(terrainMaterial, waterMaterial);

            var serializedAuthoring = new SerializedObject(authoring);
            serializedAuthoring.FindProperty("generateOnStart").boolValue = true;
            serializedAuthoring.FindProperty("generateColliders").boolValue = false;
            serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();

            ConfigureCamera(settings.WorldSize);
            ConfigureDirectionalLight();
            EnsureSceneInBuildSettings();

            EditorUtility.SetDirty(authoring);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = generatorObject;
            Debug.Log("World test scene is ready. Open SampleScene and enter Play Mode.", generatorObject);
        }

        private static WorldGenerationSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldGenerationSettings>(SettingsPath);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<WorldGenerationSettings>();
            settings.ConfigureDimensionsAndSeed(64, 16, 16, 8, 12345);
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Material LoadOrCreateMaterial(string path, string shaderName, string materialName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new MissingReferenceException($"Shader '{shaderName}' was not found.");
            }

            material = new Material(shader) { name = materialName };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ConfigureCamera(int worldSize)
        {
            var cameraObject = GameObject.Find("Main Camera") ?? new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>() ?? cameraObject.AddComponent<Camera>();
            if (cameraObject.GetComponent<AudioListener>() == null)
            {
                cameraObject.AddComponent<AudioListener>();
            }

            var center = new Vector3(worldSize * 0.5f, 2.5f, worldSize * 0.5f);
            camera.transform.position = center + new Vector3(0f, worldSize * 0.95f, -worldSize * 0.78f);
            camera.transform.LookAt(center);
            camera.orthographic = true;
            camera.orthographicSize = worldSize * 0.58f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = worldSize * 4f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.14f, 0.22f, 0.34f, 1f);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(camera.transform);
        }

        private static void ConfigureDirectionalLight()
        {
            var lightObject = GameObject.Find("Directional Light") ?? new GameObject("Directional Light");
            var light = lightObject.GetComponent<Light>() ?? lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.sun = light;
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(light.transform);
        }

        private static void EnsureSettingsDirectory()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/World/Settings"))
            {
                AssetDatabase.CreateFolder("Assets/Game/World", "Settings");
            }
        }

        private static void EnsureSceneInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var existingIndex = scenes.FindIndex(item => item.path == ScenePath);
            if (existingIndex >= 0)
            {
                scenes[existingIndex] = new EditorBuildSettingsScene(ScenePath, true);
            }
            else
            {
                scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
