using System.Linq;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Generation;
using MiniCivilization.World.Hydrology;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Persistence;
using MiniCivilization.World.Presentation;
using MiniCivilization.World.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        private const string HighlightMaterialPath = SettingsDirectory + "/WorldTileHighlight.mat";
        private const int InteractionLayer = 8;

        [InitializeOnLoadMethod]
        private static void QueueRequiredSceneMigration()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode
                    || SceneManager.GetActiveScene().path != ScenePath)
                {
                    return;
                }

                var root = GameObject.Find("World System");
                if (root == null
                    || (root.transform.Find("World Management") != null
                        && root.transform.Find("World Editing") != null
                        && root.transform.Find("World Hydrology") != null
                        && root.transform.Find("World Save") != null
                        && root.transform.Find("World UI/Canvas") != null))
                {
                    return;
                }

                Setup();
            };
        }

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
            var highlightMaterial = LoadOrCreateMaterial(
                HighlightMaterialPath,
                "Mini Civilization/World Tile Highlight",
                "World Tile Highlight Material");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var worldObject = GameObject.Find("World System")
                ?? GameObject.Find("World Generator")
                ?? new GameObject("World System");
            worldObject.name = "World System";

            var managementObject = EnsureRoleObject(worldObject, "World Management");
            var generationObject = EnsureRoleObject(worldObject, "World Generation");
            var editingObject = EnsureRoleObject(worldObject, "World Editing");
            var hydrologyObject = EnsureRoleObject(worldObject, "World Hydrology");
            var renderingObject = EnsureRoleObject(worldObject, "World Rendering");
            var saveObject = EnsureRenamedRoleObject(
                worldObject,
                "World Save",
                "World Persistence");
            var interactionObject = EnsureRoleObject(worldObject, "World Interaction");
            var uiObject = EnsureRoleObject(worldObject, "World UI");

            var manager = MoveOrAdd<WorldManager>(
                worldObject,
                managementObject,
                out _);
            var generator = MoveOrAdd<WorldGenerationController>(
                worldObject,
                generationObject,
                out var generatorCreated);
            var editController = MoveOrAdd<WorldEditController>(
                worldObject,
                editingObject,
                out _);
            var hydrologyController = MoveOrAdd<WorldHydrologyController>(
                worldObject,
                hydrologyObject,
                out _);
            var renderer = MoveOrAdd<WorldRenderer>(
                worldObject,
                renderingObject,
                out _);
            var saveController = MoveOrAdd<WorldSaveController>(
                worldObject,
                saveObject,
                out _);
            var selectionState = MoveOrAdd<WorldTileSelectionState>(
                worldObject,
                interactionObject,
                out _);
            var interactionController = MoveOrAdd<WorldInteractionController>(
                worldObject,
                interactionObject,
                out _);
            var highlighter = MoveOrAdd<WorldTileHighlighter>(
                worldObject,
                interactionObject,
                out _);
            var infoProvider = MoveOrAdd<WorldCellInfoProvider>(
                worldObject,
                interactionObject,
                out _);
            var infoPresenter = MoveOrAdd<WorldTileInfoPresenter>(
                worldObject,
                uiObject,
                out _);

            var renderRoot = EnsureChildTransform(
                renderingObject.transform,
                worldObject.transform,
                "Render Root");
            var highlightRoot = EnsureChildTransform(
                interactionObject.transform,
                worldObject.transform,
                "Highlight Root");

            var hoverHighlight = EnsureHighlightChild(highlightRoot, "Hover Highlight");
            var selectedHighlight = EnsureHighlightChild(highlightRoot, "Selected Highlight");

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
                true,
                InteractionLayer);
            saveController.Configure("Worlds", "default.mcw", true);
            manager.Configure(
                generator,
                editController,
                hydrologyController,
                renderer,
                saveController);

            var camera = ConfigureCamera(settings.WorldSize);
            interactionController.Configure(
                camera,
                manager,
                selectionState,
                1 << InteractionLayer,
                settings.WorldSize * 4f);
            highlighter.Configure(
                manager,
                selectionState,
                hoverHighlight.Filter,
                hoverHighlight.Renderer,
                selectedHighlight.Filter,
                selectedHighlight.Renderer,
                highlightMaterial);
            var infoPanel = EnsureTileInfoCanvas(uiObject.transform);
            infoPresenter.Configure(
                manager,
                selectionState,
                infoProvider,
                infoPanel);
            EnsureEventSystem();
            ConfigureInteractionLayer();
            ConfigureDirectionalLight();
            EnsureSceneInBuildSettings();

            EditorUtility.SetDirty(worldObject);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(generator);
            EditorUtility.SetDirty(editController);
            EditorUtility.SetDirty(hydrologyController);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(saveController);
            EditorUtility.SetDirty(selectionState);
            EditorUtility.SetDirty(interactionController);
            EditorUtility.SetDirty(highlighter);
            EditorUtility.SetDirty(infoProvider);
            EditorUtility.SetDirty(infoPresenter);
            EditorUtility.SetDirty(infoPanel);
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

        private static GameObject EnsureRenamedRoleObject(
            GameObject worldRoot,
            string roleName,
            string legacyRoleName)
        {
            var current = worldRoot.transform.Find(roleName);
            if (current != null)
            {
                return current.gameObject;
            }

            var legacy = worldRoot.transform.Find(legacyRoleName);
            if (legacy != null)
            {
                legacy.name = roleName;
                return legacy.gameObject;
            }

            return EnsureRoleObject(worldRoot, roleName);
        }

        private static T MoveOrAdd<T>(
            GameObject legacyOwner,
            GameObject target,
            out bool created)
            where T : Component
        {
            var component = target.GetComponent<T>();
            if (component != null)
            {
                created = false;
                return component;
            }

            var legacy = legacyOwner.GetComponent<T>();
            if (legacy != null)
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(legacy);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(target);
                Object.DestroyImmediate(legacy);
                created = false;
                return target.GetComponent<T>();
            }

            created = true;
            return target.AddComponent<T>();
        }

        private static Transform EnsureChildTransform(
            Transform expectedParent,
            Transform legacyParent,
            string childName)
        {
            var child = expectedParent.Find(childName)
                ?? legacyParent.Find(childName);
            if (child == null)
            {
                var childObject = new GameObject(childName);
                child = childObject.transform;
            }

            child.SetParent(expectedParent, false);
            return child;
        }

        private static WorldTileInfoPanel EnsureTileInfoCanvas(
            Transform uiRoot)
        {
            var canvasTransform = uiRoot.Find("Canvas") as RectTransform;
            if (canvasTransform == null)
            {
                var canvasObject = new GameObject(
                    "Canvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                canvasTransform = (RectTransform)canvasObject.transform;
                canvasTransform.SetParent(uiRoot, false);
            }

            var canvas = canvasTransform.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasTransform.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var panelTransform = canvasTransform.Find("Tile Info Panel")
                as RectTransform;
            if (panelTransform == null)
            {
                var panelObject = new GameObject(
                    "Tile Info Panel",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(WorldTileInfoPanel));
                panelTransform = (RectTransform)panelObject.transform;
                panelTransform.SetParent(canvasTransform, false);
            }

            panelTransform.anchorMin = new Vector2(1f, 1f);
            panelTransform.anchorMax = new Vector2(1f, 1f);
            panelTransform.pivot = new Vector2(1f, 1f);
            panelTransform.anchoredPosition = new Vector2(-24f, -24f);
            panelTransform.sizeDelta = new Vector2(390f, 680f);
            var panelImage = panelTransform.GetComponent<Image>();
            panelImage.color = new Color(0.045f, 0.06f, 0.08f, 0.94f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var title = EnsureText(
                panelTransform,
                "Title",
                font,
                22,
                FontStyle.Bold,
                new Vector2(18f, -14f),
                new Vector2(310f, 34f));
            var coordinate = EnsureText(
                panelTransform,
                "Coordinate",
                font,
                17,
                FontStyle.Bold,
                new Vector2(18f, -58f),
                new Vector2(354f, 30f));
            var terrain = EnsureText(
                panelTransform,
                "Terrain",
                font,
                15,
                FontStyle.Normal,
                new Vector2(18f, -102f),
                new Vector2(354f, 176f));
            var water = EnsureText(
                panelTransform,
                "Water",
                font,
                15,
                FontStyle.Normal,
                new Vector2(18f, -286f),
                new Vector2(354f, 144f));
            var surface = EnsureText(
                panelTransform,
                "Surface",
                font,
                15,
                FontStyle.Normal,
                new Vector2(18f, -438f),
                new Vector2(354f, 92f));
            var debug = EnsureText(
                panelTransform,
                "Debug",
                font,
                13,
                FontStyle.Normal,
                new Vector2(18f, -538f),
                new Vector2(354f, 118f));
            debug.color = new Color(0.68f, 0.75f, 0.82f, 1f);

            var closeButton = EnsureCloseButton(
                panelTransform,
                font);
            var panel = panelTransform.GetComponent<WorldTileInfoPanel>();
            panel.Configure(
                panelTransform.gameObject,
                title,
                coordinate,
                terrain,
                water,
                surface,
                debug,
                closeButton);
            panelTransform.gameObject.SetActive(false);
            return panel;
        }

        private static Text EnsureText(
            RectTransform parent,
            string objectName,
            Font font,
            int fontSize,
            FontStyle fontStyle,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            var child = parent.Find(objectName) as RectTransform;
            if (child == null)
            {
                var textObject = new GameObject(
                    objectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                child = (RectTransform)textObject.transform;
                child.SetParent(parent, false);
            }

            child.anchorMin = new Vector2(0f, 1f);
            child.anchorMax = new Vector2(0f, 1f);
            child.pivot = new Vector2(0f, 1f);
            child.anchoredPosition = anchoredPosition;
            child.sizeDelta = size;
            var text = child.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Button EnsureCloseButton(
            RectTransform parent,
            Font font)
        {
            var child = parent.Find("Close Button") as RectTransform;
            if (child == null)
            {
                var buttonObject = new GameObject(
                    "Close Button",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                child = (RectTransform)buttonObject.transform;
                child.SetParent(parent, false);
            }

            child.anchorMin = new Vector2(1f, 1f);
            child.anchorMax = new Vector2(1f, 1f);
            child.pivot = new Vector2(1f, 1f);
            child.anchoredPosition = new Vector2(-12f, -12f);
            child.sizeDelta = new Vector2(36f, 32f);
            child.GetComponent<Image>().color =
                new Color(0.18f, 0.22f, 0.27f, 1f);
            var label = EnsureText(
                child,
                "Label",
                font,
                20,
                FontStyle.Bold,
                Vector2.zero,
                child.sizeDelta);
            var labelTransform = (RectTransform)label.transform;
            labelTransform.anchorMin = Vector2.zero;
            labelTransform.anchorMax = Vector2.one;
            labelTransform.pivot = new Vector2(0.5f, 0.5f);
            labelTransform.anchoredPosition = Vector2.zero;
            labelTransform.sizeDelta = Vector2.zero;
            label.alignment = TextAnchor.MiddleCenter;
            label.text = "\u00D7";
            return child.GetComponent<Button>();
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem != null)
            {
                if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                {
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                }

                return;
            }

            var eventObject = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            EditorUtility.SetDirty(eventObject);
        }

        private static Camera ConfigureCamera(int worldSize)
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
            return camera;
        }

        private static (MeshFilter Filter, MeshRenderer Renderer)
            EnsureHighlightChild(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                var childObject = new GameObject(childName);
                childObject.layer = 2;
                child = childObject.transform;
                child.SetParent(parent, false);
            }

            var filter = child.GetComponent<MeshFilter>()
                ?? child.gameObject.AddComponent<MeshFilter>();
            var renderer = child.GetComponent<MeshRenderer>()
                ?? child.gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return (filter, renderer);
        }

        private static void ConfigureInteractionLayer()
        {
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            layers.GetArrayElementAtIndex(InteractionLayer).stringValue =
                "WorldInteraction";
            tagManager.ApplyModifiedProperties();
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
