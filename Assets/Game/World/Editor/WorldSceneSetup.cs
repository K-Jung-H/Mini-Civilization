using System.Linq;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Persistence;
using MiniCivilization.World.Presentation;
using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiniCivilization.World.Editor
{
    public static class WorldSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SettingsDirectory = "Assets/Game/World/Settings";
        private const string SettingsPath = SettingsDirectory + "/WorldGenerationSettings.asset";
        private const string CatalogPath = SettingsDirectory + "/WorldSurfaceCatalog.asset";
        private const string TerrainMaterialPath = SettingsDirectory + "/WorldTerrain.mat";
        private const string WaterMaterialPath = SettingsDirectory + "/WorldWater.mat";
        private const string WaterfallMaterialPath = SettingsDirectory + "/WorldWaterfall.mat";

        [MenuItem("Mini Civilization/Setup World Scene")]
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
            var waterfallMaterial = LoadOrCreateMaterial(
                WaterfallMaterialPath,
                "Mini Civilization/World Water Lit",
                "World Waterfall Material");
            waterfallMaterial.SetFloat("_DepthBiasFactor", -1f);
            waterfallMaterial.SetFloat("_DepthBiasUnits", -1f);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var worldObject = GameObject.Find("World System")
                ?? GameObject.Find("World Generator")
                ?? new GameObject("World System");
            worldObject.name = "World System";

            var manager = GetOrAdd<WorldManager>(worldObject, out _);
            var generator = GetOrAdd<WorldGenerationController>(
                worldObject,
                out var generatorCreated);
            var renderer = GetOrAdd<WorldRenderer>(worldObject, out _);
            var persistence = GetOrAdd<WorldPersistence>(worldObject, out _);

            var renderRoot = worldObject.transform.Find("Render Root");
            if (renderRoot == null)
            {
                var renderRootObject = new GameObject("Render Root");
                renderRoot = renderRootObject.transform;
                renderRoot.SetParent(worldObject.transform, false);
            }

            generator.SetSettings(settings);
            if (generatorCreated)
            {
                generator.SetSeed(1177230571);
            }

            renderer.Configure(
                catalog,
                terrainMaterial,
                waterMaterial,
                waterfallMaterial,
                renderRoot,
                settings.RenderPatchSizeXZ,
                false);
            persistence.Configure("Worlds", "default.mcw", true);
            manager.Configure(
                generator,
                renderer,
                persistence,
                WorldStartupMode.LoadIfExistsOrGenerate);

            ConfigureCamera(settings.WorldSize);
            ConfigureDirectionalLight();
            EnsureSceneInBuildSettings();

            EditorUtility.SetDirty(worldObject);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(generator);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(persistence);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = worldObject;
        }

        private static WorldGenerationSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldGenerationSettings>(SettingsPath);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<WorldGenerationSettings>();
            settings.ConfigureDimensions(64, 16, 16, 8);
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

        private static Material LoadOrCreateMaterial(
            string path,
            string shaderName,
            string materialName)
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

        private static T GetOrAdd<T>(GameObject target, out bool created)
            where T : Component
        {
            var component = target.GetComponent<T>();
            created = component == null;
            return component != null ? component : target.AddComponent<T>();
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
            camera.transform.position =
                center + new Vector3(0f, worldSize * 0.95f, -worldSize * 0.78f);
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
            var lightObject = GameObject.Find("Directional Light")
                ?? new GameObject("Directional Light");
            var light = lightObject.GetComponent<Light>()
                ?? lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            light.shadows = LightShadows.Soft;
            light.shadowBias = 0.1f;
            light.shadowNormalBias = 0f;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.sun = light;
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(light.transform);
        }

        private static void EnsureSettingsDirectory()
        {
            if (!AssetDatabase.IsValidFolder(SettingsDirectory))
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
