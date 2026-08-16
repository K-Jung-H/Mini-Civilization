using System.Linq;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Persistence;
using MiniCivilization.World.Presentation;
using MiniCivilization.World.Runtime;
using MiniCivilization.World.WaterFlow;
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
        private const string SettingsPath =
            SettingsDirectory + "/WorldGenerationSettings.asset";
        private const string CatalogPath =
            SettingsDirectory + "/WorldSurfaceCatalog.asset";
        private const string EntityCatalogPath =
            SettingsDirectory + "/EntityCatalog.asset";
        private const string TerrainMaterialPath =
            SettingsDirectory + "/WorldTerrain.mat";
        private const string WaterMaterialPath =
            SettingsDirectory + "/WorldWater.mat";
        private const string HighlightMaterialPath =
            SettingsDirectory + "/WorldTileHighlight.mat";

        [MenuItem("Mini Civilization/Setup World Scene")]
        public static void Setup()
        {
            EnsureSettingsDirectory();

            var settings = LoadOrCreateSettings();
            var catalog = LoadOrCreateAsset<WorldSurfaceCatalog>(CatalogPath);
            var entityCatalog = LoadOrCreateAsset<EntityCatalog>(
                EntityCatalogPath);
            var terrainMaterial = LoadOrCreateMaterial(
                TerrainMaterialPath,
                "Mini Civilization/World Terrain Lit",
                "World Terrain Material");
            var waterMaterial = LoadOrCreateMaterial(
                WaterMaterialPath,
                "Mini Civilization/World Water Lit",
                "World Water Material");
            var highlightMaterial = LoadOrCreateMaterial(
                HighlightMaterialPath,
                "Mini Civilization/World Tile Highlight",
                "World Tile Highlight Material");
            if (highlightMaterial.HasProperty("_ZTest"))
            {
                highlightMaterial.SetFloat(
                    "_ZTest",
                    (float)UnityEngine.Rendering.CompareFunction.LessEqual);
            }

            var activeScene = SceneManager.GetActiveScene();
            var scene = activeScene.IsValid() && activeScene.path == ScenePath
                ? activeScene
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var worldObject = GameObject.Find("World System")
                ?? new GameObject("World System");
            worldObject.name = "World System";

            var managementObject = EnsureRoleObject(worldObject, "World Management");
            var generationObject = EnsureRoleObject(worldObject, "World Generation");
            var editingObject = EnsureRoleObject(worldObject, "World Editing");
            var waterFlowObject = EnsureRoleObject(
                worldObject,
                "World Water Flow");
            var renderingObject = EnsureRoleObject(worldObject, "World Rendering");
            var entitiesObject = EnsureRoleObject(worldObject, "World Entities");
            var saveObject = EnsureRoleObject(worldObject, "World Save");
            var interactionObject = EnsureRoleObject(
                worldObject,
                "World Interaction");
            var uiObject = EnsureRoleObject(worldObject, "World UI");

            var manager = GetOrAdd<WorldManager>(managementObject, out _);
            var streamingController = GetOrAdd<WorldChunkStreamingController>(
                managementObject,
                out _);
            var generator = GetOrAdd<WorldGenerationController>(
                generationObject,
                out var generatorCreated);
            var editController = GetOrAdd<WorldEditController>(
                editingObject,
                out _);
            var editToolState = GetOrAdd<WorldEditToolState>(
                editingObject,
                out _);
            var editInputController = GetOrAdd<WorldEditInputController>(
                editingObject,
                out _);
            var editApplyController = GetOrAdd<WorldEditApplyController>(
                editingObject,
                out _);
            var entityEditController = GetOrAdd<EntityEditController>(
                editingObject,
                out _);
            var waterFlowController = GetOrAdd<WorldWaterFlowController>(
                waterFlowObject,
                out _);
            var renderer = GetOrAdd<WorldRenderer>(renderingObject, out _);
            var entityRenderer = GetOrAdd<WorldEntityRenderer>(
                entitiesObject,
                out _);
            var entityManager = GetOrAdd<EntityManager>(entitiesObject, out _);
            var saveController = GetOrAdd<WorldSaveController>(saveObject, out _);
            var selectionState = GetOrAdd<WorldTileSelectionState>(
                interactionObject,
                out _);
            var interactionController = GetOrAdd<WorldInteractionController>(
                interactionObject,
                out _);
            var highlighter = GetOrAdd<WorldTileHighlighter>(
                interactionObject,
                out _);
            var infoProvider = GetOrAdd<WorldCellInfoProvider>(
                interactionObject,
                out _);
            var uiManager = GetOrAdd<WorldUIManager>(uiObject, out _);

            var renderRoot = EnsureChildTransform(
                renderingObject.transform,
                "Render Root");
            var entityRoot = EnsureChildTransform(
                entitiesObject.transform,
                "Entity Root");
            generator.SetSettings(settings);
            if (generatorCreated)
            {
                generator.SetSeed(1177230571);
            }

            renderer.Configure(
                catalog,
                terrainMaterial,
                waterMaterial,
                renderRoot);
            entityRenderer.Configure(entityRoot);
            entityManager.Configure(entityCatalog, entityRenderer);
            streamingController.Configure(null, renderRoot, 1, 0);
            saveController.Configure("Worlds", "default.mcw", true);
            manager.Configure(
                generator,
                editController,
                waterFlowController,
                renderer,
                saveController,
                entityManager,
                uiManager,
                streamingController);
            uiManager.Configure(
                editToolState,
                editApplyController,
                entityEditController,
                selectionState,
                infoProvider);

            var camera = FindOrCreateCamera();
            highlighter.Configure(manager, selectionState, highlightMaterial);
            editInputController.Configure(manager, editToolState, selectionState);
            interactionController.Configure(
                camera,
                manager,
                selectionState,
                editToolState,
                settings.WorldSize * settings.CellSize * 4f);
            ConfigureDirectionalLight();
            EnsureSceneInBuildSettings();

            EditorUtility.SetDirty(worldObject);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(streamingController);
            EditorUtility.SetDirty(generator);
            EditorUtility.SetDirty(editController);
            EditorUtility.SetDirty(editToolState);
            EditorUtility.SetDirty(editInputController);
            EditorUtility.SetDirty(editApplyController);
            EditorUtility.SetDirty(entityEditController);
            EditorUtility.SetDirty(waterFlowController);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(entityRenderer);
            EditorUtility.SetDirty(entityManager);
            EditorUtility.SetDirty(saveController);
            EditorUtility.SetDirty(selectionState);
            EditorUtility.SetDirty(interactionController);
            EditorUtility.SetDirty(highlighter);
            EditorUtility.SetDirty(infoProvider);
            EditorUtility.SetDirty(uiManager);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = worldObject;
        }

        private static WorldGenerationSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WorldGenerationSettings>(
                SettingsPath);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<WorldGenerationSettings>();
            settings.ConfigureDimensions(64, 16, 16, 8);
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static T LoadOrCreateAsset<T>(string path)
            where T : ScriptableObject
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
                throw new MissingReferenceException(
                    $"Shader '{shaderName}' was not found.");
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

        private static GameObject EnsureRoleObject(
            GameObject worldRoot,
            string roleName)
        {
            var child = worldRoot.transform.Find(roleName);
            if (child != null)
            {
                return child.gameObject;
            }

            var roleObject = new GameObject(roleName);
            roleObject.transform.SetParent(worldRoot.transform, false);
            return roleObject;
        }

        private static Transform EnsureChildTransform(
            Transform parent,
            string childName)
        {
            var child = parent.Find(childName);
            if (child != null)
            {
                return child;
            }

            var childObject = new GameObject(childName);
            child = childObject.transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Camera FindOrCreateCamera()
        {
            var camera = Camera.main;
            if (camera != null)
            {
                return camera;
            }

            var cameraObject = GameObject.Find("Main Camera");
            if (cameraObject != null
                && cameraObject.TryGetComponent(out camera))
            {
                return camera;
            }

            cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(0f, 1f, -10f);
            return camera;
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
