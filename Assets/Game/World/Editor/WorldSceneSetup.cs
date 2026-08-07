using System.Linq;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Generation;
using MiniCivilization.World.WaterFlow;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Persistence;
using MiniCivilization.World.Presentation;
using MiniCivilization.World.Runtime;
using TMPro;
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
        private const string EntityCatalogPath = SettingsDirectory + "/EntityCatalog.asset";
        private const string TerrainMaterialPath = SettingsDirectory + "/WorldTerrain.mat";
        private const string WaterMaterialPath = SettingsDirectory + "/WorldWater.mat";
        private const string HighlightMaterialPath = SettingsDirectory + "/WorldTileHighlight.mat";

        [MenuItem("Mini Civilization/Setup World Scene")]
        public static void Setup()
        {
            EnsureSettingsDirectory();

            var settings = LoadOrCreateSettings();
            var catalog = LoadOrCreateAsset<WorldSurfaceCatalog>(CatalogPath);
            var entityCatalog = LoadOrCreateAsset<EntityCatalog>(EntityCatalogPath);
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
            var interactionObject = EnsureRoleObject(worldObject, "World Interaction");
            var uiObject = EnsureRoleObject(worldObject, "World UI");

            var manager = GetOrAdd<WorldManager>(managementObject, out _);
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
            var entityEditController = GetOrAdd<
                EntityEditController>(
                editingObject,
                out _);
            var waterFlowController = GetOrAdd<WorldWaterFlowController>(
                waterFlowObject,
                out _);
            var renderer = GetOrAdd<WorldRenderer>(renderingObject, out _);
            var entityRenderer = GetOrAdd<WorldEntityRenderer>(
                entitiesObject,
                out _);
            var entityManager = GetOrAdd<EntityManager>(
                entitiesObject,
                out _);
            var saveController = GetOrAdd<WorldSaveController>(
                saveObject,
                out _);
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
            var infoPresenter = GetOrAdd<WorldTileInfoPresenter>(
                uiObject,
                out _);
            var progressView = GetOrAdd<WorldOperationProgressView>(
                uiObject,
                out _);

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
                renderRoot,
                settings.RenderPatchSizeXZ);
            entityRenderer.Configure(entityRoot);
            entityManager.Configure(entityCatalog, entityRenderer);
            saveController.Configure("Worlds", "default.mcw", true);
            manager.Configure(
                generator,
                editController,
                waterFlowController,
                renderer,
                saveController,
                entityManager);

            var camera = FindOrCreateCamera();
            highlighter.Configure(
                manager,
                selectionState,
                highlightMaterial);
            var infoPanel = EnsureTileInfoCanvas(uiObject.transform);
            EnsureWorldOperationProgressPanel(
                uiObject.transform.Find("Canvas") as RectTransform,
                progressView,
                manager);
            var editToolbar = EnsureWorldEditToolbar(
                uiObject.transform.Find("Canvas") as RectTransform,
                entityCatalog);
            editToolState.Configure(editToolbar);
            editInputController.Configure(
                manager,
                editToolState,
                selectionState);
            editApplyController.Configure(
                editController,
                selectionState,
                editToolbar);
            entityEditController.Configure(
                entityManager,
                selectionState,
                editToolbar.GetComponent<WorldEntityCatalogView>());
            interactionController.Configure(
                camera,
                manager,
                selectionState,
                editToolState,
                settings.WorldSize * 4f);
            infoPresenter.Configure(
                manager,
                selectionState,
                infoProvider,
                infoPanel);
            EnsureEventSystem();
            ConfigureDirectionalLight();
            EnsureSceneInBuildSettings();

            EditorUtility.SetDirty(worldObject);
            EditorUtility.SetDirty(manager);
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
            EditorUtility.SetDirty(infoPresenter);
            EditorUtility.SetDirty(progressView);
            EditorUtility.SetDirty(infoPanel);
            EditorUtility.SetDirty(editToolbar);
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

        private static Transform EnsureChildTransform(
            Transform parent,
            string childName)
        {
            var child = parent.Find(childName);
            if (child == null)
            {
                var childObject = new GameObject(childName);
                child = childObject.transform;
                child.SetParent(parent, false);
            }
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

        private static void EnsureWorldOperationProgressPanel(
            RectTransform canvasTransform,
            WorldOperationProgressView view,
            WorldManager manager)
        {
            if (canvasTransform == null)
            {
                throw new MissingReferenceException(
                    "World UI Canvas was not created.");
            }

            var panelTransform = canvasTransform.Find("World Operation Progress")
                as RectTransform;
            if (panelTransform == null)
            {
                var panelObject = new GameObject(
                    "World Operation Progress",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                panelTransform = (RectTransform)panelObject.transform;
                panelTransform.SetParent(canvasTransform, false);
            }

            panelTransform.anchorMin = new Vector2(0.5f, 1f);
            panelTransform.anchorMax = new Vector2(0.5f, 1f);
            panelTransform.pivot = new Vector2(0.5f, 1f);
            panelTransform.anchoredPosition = new Vector2(0f, -24f);
            panelTransform.sizeDelta = new Vector2(390f, 88f);
            panelTransform.GetComponent<Image>().color = new Color(
                0.045f,
                0.06f,
                0.08f,
                0.94f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var stage = EnsureText(
                panelTransform,
                "Stage",
                font,
                18,
                FontStyle.Bold,
                new Vector2(18f, -14f),
                new Vector2(354f, 30f));
            var completed = EnsureText(
                panelTransform,
                "Completed",
                font,
                14,
                FontStyle.Normal,
                new Vector2(18f, -48f),
                new Vector2(354f, 24f));
            completed.color = new Color(0.68f, 0.75f, 0.82f, 1f);
            view.Configure(manager, panelTransform.gameObject, stage, completed);
            panelTransform.gameObject.SetActive(false);
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

        private static WorldEditToolbarView EnsureWorldEditToolbar(
            RectTransform canvasTransform,
            EntityCatalog entityCatalog)
        {
            if (canvasTransform == null)
            {
                throw new MissingReferenceException(
                    "World UI Canvas was not created.");
            }

            const float size = 64f;
            const float gap = 8f;
            const float toolbarHeight = 84f;
            const float expandedWidth = 818f;
            const float entityGroupRight = -304f;
            var rootWidth = expandedWidth + 94f;
            const float rootHeight = 454f;
            var entityDetailPanelRight =
                -toolbarHeight + entityGroupRight;
            var entityGroupWidth = size * 4f + gap * 3f;
            var backgroundColor = new Color(0.055f, 0.065f, 0.08f, 0.96f);
            var root = EnsureUiRect(
                canvasTransform,
                "World Edit UI",
                typeof(WorldEditToolbarView));
            SetRect(
                root,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-24f, 24f),
                new Vector2(rootWidth, rootHeight));
            var view = root.GetComponent<WorldEditToolbarView>();
            var toolbarFont = ResolveToolbarFont(view, root);

            var toolbar = EnsureUiRect(root, "Toolbar");
            SetRect(
                toolbar,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(expandedWidth + toolbarHeight, toolbarHeight));
            MakePanelTransparent(toolbar);
            RemoveUiChild(toolbar, "Property Group");
            RemoveUiChild(toolbar, "Divider");
            RemoveUiChild(toolbar, "Mode Group");
            RemoveUiChild(toolbar, "Entity Group");
            RemoveUiChild(toolbar, "Main Property Divider");
            RemoveUiChild(toolbar, "Mode Property Divider");
            RemoveUiChild(toolbar, "Entity Property Divider");
            RemoveUiChild(toolbar, "Mode Entity Divider");

            var mainBackground = EnsureUiRect(
                toolbar,
                "Main Button Background",
                typeof(CanvasRenderer),
                typeof(Image));
            SetRect(
                mainBackground,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(toolbarHeight, toolbarHeight));
            ConfigureBackgroundImage(mainBackground, backgroundColor);
            mainBackground.SetAsFirstSibling();

            var mainButton = EnsureToolbarButton(
                toolbar,
                "Main Button",
                "편집",
                toolbarFont,
                new Color(0.88f, 0.31f, 0.52f, 1f));
            SetRect(
                (RectTransform)mainButton.transform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-10f, 10f),
                new Vector2(size, size));
            var mainLabel = mainButton.transform.Find("Label")
                ?.GetComponent<TMP_Text>();

            var expandedContent = EnsureUiRect(
                toolbar,
                "Expanded Content");
            SetRect(
                expandedContent,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-toolbarHeight, 0f),
                new Vector2(expandedWidth, toolbarHeight));
            MakePanelTransparent(expandedContent);
            RemoveUiChild(expandedContent, "Divider");

            var expandedBackground = EnsureUiRect(
                expandedContent,
                "Background Image",
                typeof(CanvasRenderer),
                typeof(Image));
            SetRect(
                expandedBackground,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            ConfigureBackgroundImage(expandedBackground, backgroundColor);
            expandedBackground.SetAsFirstSibling();

            var propertyTransform = EnsureUiRect(
                expandedContent,
                "Property Group",
                typeof(ToggleGroup));
            SetRect(
                propertyTransform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-8f, 10f),
                new Vector2(size * 4f + gap * 3f, size));
            var propertyGroup = propertyTransform.GetComponent<ToggleGroup>();
            propertyGroup.allowSwitchOff = true;
            var terrain = EnsureSquareToggle(
                propertyTransform, "Terrain", "지형",
                toolbarFont,
                new Color(0.48f, 0.31f, 0.18f, 1f), propertyGroup, 0, size, gap);
            var biome = EnsureSquareToggle(
                propertyTransform, "Biome", "바이옴",
                toolbarFont,
                new Color(0.22f, 0.48f, 0.27f, 1f), propertyGroup, 1, size, gap);
            var water = EnsureSquareToggle(
                propertyTransform, "Water", "수면",
                toolbarFont,
                new Color(0.16f, 0.43f, 0.69f, 1f), propertyGroup, 2, size, gap);
            var surface = EnsureSquareToggle(
                propertyTransform, "Surface", "표면",
                toolbarFont,
                new Color(0.38f, 0.41f, 0.46f, 1f), propertyGroup, 3, size, gap);

            var entityPropertyDivider = EnsureUiRect(
                expandedContent,
                "Entity Property Divider",
                typeof(CanvasRenderer),
                typeof(Image));
            SetRightAnchoredDivider(entityPropertyDivider, -296f, toolbarHeight * 0.5f);

            var entityTransform = EnsureUiRect(
                expandedContent,
                "Entity Group",
                typeof(ToggleGroup));
            SetRect(
                entityTransform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(entityGroupRight, 10f),
                new Vector2(entityGroupWidth, size));
            var entityGroup = entityTransform.GetComponent<ToggleGroup>();
            entityGroup.allowSwitchOff = true;
            var nature = EnsureSquareToggle(
                entityTransform, "Nature", "\uC790\uC5F0",
                toolbarFont,
                new Color(0.23f, 0.49f, 0.30f, 1f), entityGroup, 0, size, gap);
            var animal = EnsureSquareToggle(
                entityTransform, "Animal", "\uB3D9\uBB3C",
                toolbarFont,
                new Color(0.58f, 0.38f, 0.20f, 1f), entityGroup, 1, size, gap);
            var human = EnsureSquareToggle(
                entityTransform, "Human", "\uC0AC\uB78C",
                toolbarFont,
                new Color(0.25f, 0.42f, 0.66f, 1f), entityGroup, 2, size, gap);
            var building = EnsureSquareToggle(
                entityTransform, "Building", "\uAC74\uBB3C",
                toolbarFont,
                new Color(0.49f, 0.35f, 0.27f, 1f), entityGroup, 3, size, gap);

            var modeEntityDivider = EnsureUiRect(
                expandedContent,
                "Mode Entity Divider",
                typeof(CanvasRenderer),
                typeof(Image));
            SetRightAnchoredDivider(modeEntityDivider, -592f, toolbarHeight * 0.5f);

            var modeTransform = EnsureUiRect(
                expandedContent,
                "Mode Group",
                typeof(ToggleGroup));
            SetRect(
                modeTransform,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-600f, 10f),
                new Vector2(size * 3f + gap * 2f, size));
            var modeGroup = modeTransform.GetComponent<ToggleGroup>();
            modeGroup.allowSwitchOff = true;
            var single = EnsureSquareToggle(
                modeTransform,
                "Single Selection",
                "\uB2E8\uC77C\n\uC120\uD0DD",
                toolbarFont,
                new Color(0.30f, 0.54f, 0.62f, 1f),
                modeGroup,
                0,
                size,
                gap);
            var area = EnsureSquareToggle(
                modeTransform, "Area Selection", "영역\n선택",
                toolbarFont,
                new Color(0.36f, 0.28f, 0.64f, 1f), modeGroup, 1, size, gap);
            var brush = EnsureSquareToggle(
                modeTransform, "Brush", "브러시",
                toolbarFont,
                new Color(0.77f, 0.39f, 0.16f, 1f), modeGroup, 2, size, gap);

            var mainPropertyDivider = EnsureUiRect(
                expandedContent,
                "Main Property Divider",
                typeof(CanvasRenderer),
                typeof(Image));
            SetRightAnchoredDivider(mainPropertyDivider, 1f, toolbarHeight * 0.5f);
            mainPropertyDivider.SetAsLastSibling();
            ((RectTransform)mainButton.transform).SetAsLastSibling();

            var detailHost = EnsureUiRect(root, "Property Detail Panel");
            SetRect(
                detailHost,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(rootWidth, rootHeight));
            MakePanelTransparent(detailHost);
            RemoveUiChild(detailHost, "Title");
            RemoveUiChild(detailHost, "Detail Group");

            var terrainSection = EnsurePropertySection(
                detailHost,
                terrain,
                "Terrain Details",
                "지형 세부 종류",
                toolbarFont,
                -372f,
                new[] { "올리기", "낮추기", "추가", "제거" },
                new[]
                {
                    new Color(0.55f, 0.37f, 0.21f, 1f),
                    new Color(0.39f, 0.26f, 0.16f, 1f),
                    new Color(0.31f, 0.55f, 0.31f, 1f),
                    new Color(0.64f, 0.25f, 0.22f, 1f)
                });
            var biomeSection = EnsurePropertySection(
                detailHost,
                biome,
                "Biome Details",
                "바이옴 세부 종류",
                toolbarFont,
                -300f,
                new[] { "초원", "숲", "사막", "설원", "습지", "산악" },
                new[]
                {
                    new Color(0.42f, 0.64f, 0.30f, 1f),
                    new Color(0.14f, 0.42f, 0.22f, 1f),
                    new Color(0.76f, 0.61f, 0.29f, 1f),
                    new Color(0.63f, 0.78f, 0.88f, 1f),
                    new Color(0.19f, 0.49f, 0.49f, 1f),
                    new Color(0.42f, 0.43f, 0.46f, 1f)
                });
            var waterSection = EnsurePropertySection(
                detailHost,
                water,
                "Water Details",
                "수면 세부 종류",
                toolbarFont,
                -228f,
                new[] { "없음", "담수", "해수", "습지" },
                new[]
                {
                    new Color(0.31f, 0.34f, 0.38f, 1f),
                    new Color(0.16f, 0.48f, 0.74f, 1f),
                    new Color(0.11f, 0.35f, 0.63f, 1f),
                    new Color(0.18f, 0.48f, 0.47f, 1f)
                });
            var surfaceSection = EnsurePropertySection(
                detailHost,
                surface,
                "Surface Details",
                "표면 세부 종류",
                toolbarFont,
                -228f,
                new[] { "없음", "지면", "절벽", "도로", "강바닥", "호수", "해저", "해안" },
                new[]
                {
                    new Color(0.31f, 0.34f, 0.38f, 1f),
                    new Color(0.39f, 0.51f, 0.27f, 1f),
                    new Color(0.38f, 0.38f, 0.40f, 1f),
                    new Color(0.42f, 0.31f, 0.23f, 1f),
                    new Color(0.32f, 0.31f, 0.27f, 1f),
                    new Color(0.27f, 0.35f, 0.39f, 1f),
                    new Color(0.20f, 0.28f, 0.36f, 1f),
                    new Color(0.72f, 0.63f, 0.43f, 1f)
                });
            var brushSizePanel = EnsureBrushSizeSection(
                detailHost,
                toolbarFont,
                -444f,
                out var brushSizeGroup,
                out var brushSizeToggles);
            var historyPanel = EnsureHistorySection(
                detailHost,
                toolbarFont,
                out var undoButton,
                out var redoButton);

            var definitionScroll = EnsureEntityDefinitionSection(
                detailHost,
                toolbarFont,
                entityDetailPanelRight,
                entityGroupWidth,
                out var definitionContent,
                out var definitionToggleGroup);
            var detailsScroll = EnsureEntityDetailsSection(
                detailHost,
                toolbarFont,
                entityDetailPanelRight,
                entityGroupWidth,
                out var detailsName,
                out var detailsThumbnail,
                out var detailsEmptyText,
                out var entityCreateButton);

            single.SetIsOnWithoutNotify(false);
            area.SetIsOnWithoutNotify(true);
            brush.SetIsOnWithoutNotify(false);
            terrain.SetIsOnWithoutNotify(false);
            biome.SetIsOnWithoutNotify(true);
            water.SetIsOnWithoutNotify(false);
            surface.SetIsOnWithoutNotify(false);
            nature.SetIsOnWithoutNotify(false);
            animal.SetIsOnWithoutNotify(false);
            human.SetIsOnWithoutNotify(false);
            building.SetIsOnWithoutNotify(false);

            var entityCatalogView = GetOrAdd<WorldEntityCatalogView>(
                root.gameObject,
                out _);
            entityCatalogView.Configure(
                entityCatalog,
                view,
                nature,
                animal,
                human,
                building,
                definitionScroll,
                definitionContent,
                definitionToggleGroup,
                detailsScroll,
                detailsName,
                detailsThumbnail,
                detailsEmptyText,
                entityCreateButton,
                toolbarFont);

            view.Configure(
                toolbar,
                expandedContent,
                detailHost,
                mainButton,
                mainLabel,
                historyPanel,
                undoButton,
                redoButton,
                toolbarFont,
                modeGroup,
                single,
                area,
                brush,
                brushSizePanel,
                brushSizeGroup,
                brushSizeToggles,
                propertyGroup,
                new[]
                {
                    terrainSection,
                    biomeSection,
                    waterSection,
                    surfaceSection
                });
            return view;
        }

        private static ScrollRect EnsureEntityDefinitionSection(
            RectTransform detailHost,
            TMP_FontAsset font,
            float alignedRight,
            float width,
            out RectTransform content,
            out ToggleGroup toggleGroup)
        {
            var panel = EnsureUiRect(
                detailHost,
                "Entity Definition List",
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ScrollRect));
            ClearUiChildren(panel);
            SetRect(
                panel,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(alignedRight, 94f),
                new Vector2(width, 176f));
            panel.GetComponent<Image>().color =
                new Color(0.055f, 0.065f, 0.08f, 0.96f);

            var title = EnsureToolbarLabel(
                panel,
                "Title",
                "\uC5D4\uD2F0\uD2F0 \uBAA9\uB85D",
                font,
                15,
                FontStyles.Bold);
            SetBottomLeftRect(
                title.transform as RectTransform,
                10f,
                144f,
                width - 20f,
                24f);

            var scroll = panel.GetComponent<ScrollRect>();
            var viewport = EnsureUiRect(
                panel,
                "Viewport",
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D));
            SetScrollViewport(viewport, 10f, 10f, 34f);
            viewport.GetComponent<Image>().color = Color.clear;

            content = EnsureUiRect(
                viewport,
                "Content",
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter),
                typeof(ToggleGroup));
            ConfigureScrollContent(content);
            toggleGroup = content.GetComponent<ToggleGroup>();
            toggleGroup.allowSwitchOff = true;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 18f;
            return scroll;
        }

        private static ScrollRect EnsureEntityDetailsSection(
            RectTransform detailHost,
            TMP_FontAsset font,
            float alignedRight,
            float width,
            out TMP_Text detailsName,
            out Image detailsThumbnail,
            out TMP_Text emptyText,
            out Button createButton)
        {
            var panel = EnsureUiRect(
                detailHost,
                "Entity Details",
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ScrollRect));
            ClearUiChildren(panel);
            SetRect(
                panel,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(alignedRight, 278f),
                new Vector2(width, 176f));
            panel.GetComponent<Image>().color =
                new Color(0.055f, 0.065f, 0.08f, 0.96f);

            var title = EnsureToolbarLabel(
                panel,
                "Title",
                "\uC5D4\uD2F0\uD2F0 \uC0C1\uC138",
                font,
                15,
                FontStyles.Bold);
            SetBottomLeftRect(
                title.transform as RectTransform,
                10f,
                144f,
                width - 20f,
                24f);

            var scroll = panel.GetComponent<ScrollRect>();
            var viewport = EnsureUiRect(
                panel,
                "Viewport",
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D));
            SetScrollViewport(viewport, 10f, 10f, 34f);
            viewport.GetComponent<Image>().color = Color.clear;

            var content = EnsureUiRect(
                viewport,
                "Content",
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            ConfigureScrollContent(content);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;

            detailsName = CreateDetailsLabel(
                content,
                "Name",
                font,
                17f,
                FontStyles.Bold);
            detailsThumbnail = CreateDetailsThumbnail(content);
            emptyText = CreateDetailsLabel(
                content,
                "Empty",
                font,
                14f,
                FontStyles.Normal);
            emptyText.text = "\uC5D4\uD2F0\uD2F0\uB97C \uC120\uD0DD\uD558\uC138\uC694";
            createButton = EnsureToolbarButton(
                content,
                "Create",
                "\uC0DD\uC131",
                font,
                new Color(0.22f, 0.56f, 0.34f, 1f));
            var createLayout = createButton.gameObject
                .GetComponent<LayoutElement>()
                ?? createButton.gameObject.AddComponent<LayoutElement>();
            createLayout.minHeight = 36f;
            createLayout.preferredHeight = 36f;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 18f;
            return scroll;
        }

        private static void SetScrollViewport(
            RectTransform viewport,
            float horizontalPadding,
            float bottomPadding,
            float topPadding)
        {
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.offsetMin = new Vector2(
                horizontalPadding,
                bottomPadding);
            viewport.offsetMax = new Vector2(
                -horizontalPadding,
                -topPadding);
        }

        private static void ConfigureScrollContent(RectTransform content)
        {
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private static TMP_Text CreateDetailsLabel(
            RectTransform parent,
            string objectName,
            TMP_FontAsset font,
            float fontSize,
            FontStyles fontStyle)
        {
            var rect = EnsureUiRect(
                parent,
                objectName,
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI),
                typeof(LayoutElement));
            var text = rect.GetComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
                text.fontSharedMaterial = font.material;
            }

            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            var layout = rect.GetComponent<LayoutElement>();
            layout.minHeight = 24f;
            layout.preferredHeight = 24f;
            return text;
        }

        private static Image CreateDetailsThumbnail(RectTransform parent)
        {
            var rect = EnsureUiRect(
                parent,
                "Thumbnail",
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement));
            var image = rect.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            var layout = rect.GetComponent<LayoutElement>();
            layout.minHeight = 56f;
            layout.preferredHeight = 56f;
            return image;
        }

        private static RectTransform EnsureHistorySection(
            RectTransform detailHost,
            TMP_FontAsset font,
            out Button undoButton,
            out Button redoButton)
        {
            const float size = 64f;
            const float gap = 8f;
            const float titleHeight = 30f;
            const float titleGap = 6f;
            const float horizontalPadding = 16f;
            const float bottomPadding = 16f;
            const int rowCount = 2;
            var buttonHeight = rowCount * size + (rowCount - 1) * gap;
            var panel = EnsureUiRect(
                detailHost,
                "History Details",
                typeof(CanvasRenderer),
                typeof(Image));
            ClearUiChildren(panel);
            SetRect(
                panel,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(6f, 94f),
                new Vector2(
                    size + horizontalPadding * 2f,
                    bottomPadding + buttonHeight + titleGap + titleHeight));
            panel.GetComponent<Image>().color =
                new Color(0.055f, 0.065f, 0.08f, 0.96f);

            var title = EnsureToolbarLabel(
                panel,
                "Title",
                "\uAE30\uB85D",
                font,
                15,
                FontStyles.Bold);
            SetBottomLeftRect(
                title.transform as RectTransform,
                horizontalPadding,
                bottomPadding + buttonHeight + titleGap,
                size,
                titleHeight);

            undoButton = EnsureToolbarButton(
                panel,
                "Undo",
                "\uC2E4\uD589\n\uCDE8\uC18C",
                font,
                new Color(0.72f, 0.40f, 0.22f, 1f));
            SetBottomLeftRect(
                undoButton.transform as RectTransform,
                horizontalPadding,
                bottomPadding,
                size,
                size);

            redoButton = EnsureToolbarButton(
                panel,
                "Redo",
                "\uB2E4\uC2DC\n\uC2E4\uD589",
                font,
                new Color(0.26f, 0.48f, 0.70f, 1f));
            SetBottomLeftRect(
                redoButton.transform as RectTransform,
                horizontalPadding,
                bottomPadding + size + gap,
                size,
                size);
            undoButton.interactable = false;
            redoButton.interactable = false;
            return panel;
        }

        private static TMP_FontAsset ResolveToolbarFont(
            WorldEditToolbarView view,
            RectTransform root)
        {
            if (view != null && view.LabelFont != null)
            {
                return view.LabelFont;
            }

            var defaultFont = TMP_Settings.defaultFontAsset;
            var existingFont = root
                .GetComponentsInChildren<TextMeshProUGUI>(true)
                .Select(label => label.font)
                .FirstOrDefault(font => font != null && font != defaultFont);
            return existingFont != null ? existingFont : defaultFont;
        }

        private static Toggle EnsureSquareToggle(
            RectTransform parent,
            string objectName,
            string label,
            TMP_FontAsset font,
            Color color,
            ToggleGroup group,
            int column,
            float size,
            float gap)
        {
            var toggle = EnsureToolbarToggle(
                parent,
                objectName,
                label,
                font,
                color,
                group);
            SetBottomLeftRect(
                toggle.transform as RectTransform,
                column * (size + gap),
                0f,
                size,
                size);
            return toggle;
        }

        private static WorldEditPropertySection EnsurePropertySection(
            RectTransform detailHost,
            Toggle category,
            string objectName,
            string title,
            TMP_FontAsset font,
            float alignedX,
            string[] labels,
            Color[] colors)
        {
            const float size = 64f;
            const float gap = 8f;
            const float titleHeight = 30f;
            const float titleGap = 6f;
            const float horizontalPadding = 16f;
            const float bottomPadding = 16f;
            var rows = Mathf.CeilToInt(labels.Length / 2f);
            var buttonHeight = rows * size + Mathf.Max(0, rows - 1) * gap;
            var contentWidth = size * 2f + gap;
            var panel = EnsureUiRect(
                detailHost,
                objectName,
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ToggleGroup));
            ClearUiChildren(panel);
            SetRect(
                panel,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
                new Vector2(alignedX - horizontalPadding, 94f),
                new Vector2(
                    contentWidth + horizontalPadding * 2f,
                    bottomPadding + buttonHeight + titleGap + titleHeight));
            panel.GetComponent<Image>().color =
                new Color(0.055f, 0.065f, 0.08f, 0.96f);
            var group = panel.GetComponent<ToggleGroup>();
            group.allowSwitchOff = true;

            var titleText = EnsureToolbarLabel(
                panel,
                "Title",
                title,
                font,
                15,
                FontStyles.Bold);
            SetBottomLeftRect(
                titleText.transform as RectTransform,
                horizontalPadding,
                bottomPadding + buttonHeight + titleGap,
                contentWidth,
                titleHeight);

            var toggles = new Toggle[labels.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                var toggle = EnsureToolbarToggle(
                    panel,
                    $"Option {i + 1}",
                    labels[i],
                    font,
                    colors[Mathf.Min(i, colors.Length - 1)],
                    group);
                SetBottomLeftRect(
                    toggle.transform as RectTransform,
                    horizontalPadding + (i % 2) * (size + gap),
                    bottomPadding + (i / 2) * (size + gap),
                    size,
                    size);
                toggles[i] = toggle;
            }

            if (toggles.Length > 0)
            {
                toggles[0].SetIsOnWithoutNotify(true);
                for (var i = 1; i < toggles.Length; i++)
                {
                    toggles[i].SetIsOnWithoutNotify(false);
                }
            }

            return new WorldEditPropertySection(category, panel, group, toggles);
        }

        private static RectTransform EnsureBrushSizeSection(
            RectTransform detailHost,
            TMP_FontAsset font,
            float alignedX,
            out ToggleGroup group,
            out Toggle[] toggles)
        {
            const float size = 64f;
            const float gap = 8f;
            const float titleHeight = 30f;
            const float titleGap = 6f;
            const float horizontalPadding = 16f;
            const float bottomPadding = 16f;
            const int rowCount = 3;
            var buttonHeight = rowCount * size + (rowCount - 1) * gap;
            var panel = EnsureUiRect(
                detailHost,
                "Brush Size Details",
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(ToggleGroup));
            ClearUiChildren(panel);
            SetRect(
                panel,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                Vector2.zero,
                new Vector2(alignedX - horizontalPadding, 94f),
                new Vector2(
                    size + horizontalPadding * 2f,
                    bottomPadding + buttonHeight + titleGap + titleHeight));
            panel.GetComponent<Image>().color =
                new Color(0.055f, 0.065f, 0.08f, 0.96f);
            group = panel.GetComponent<ToggleGroup>();
            group.allowSwitchOff = false;

            var title = EnsureToolbarLabel(
                panel,
                "Title",
                "\uD06C\uAE30",
                font,
                15,
                FontStyles.Bold);
            SetBottomLeftRect(
                title.transform as RectTransform,
                horizontalPadding,
                bottomPadding + buttonHeight + titleGap,
                size,
                titleHeight);

            var labels = new[]
            {
                "1\u00D71",
                "2\u00D72",
                "3\u00D73"
            };
            var colors = new[]
            {
                new Color(0.34f, 0.57f, 0.32f, 1f),
                new Color(0.31f, 0.48f, 0.66f, 1f),
                new Color(0.59f, 0.38f, 0.66f, 1f)
            };
            toggles = new Toggle[rowCount];
            for (var index = 0; index < rowCount; index++)
            {
                var toggle = EnsureToolbarToggle(
                    panel,
                    $"Size {index + 1}x{index + 1}",
                    labels[index],
                    font,
                    colors[index],
                    group);
                SetBottomLeftRect(
                    toggle.transform as RectTransform,
                    horizontalPadding,
                    bottomPadding
                    + (rowCount - 1 - index) * (size + gap),
                    size,
                    size);
                toggles[index] = toggle;
            }

            toggles[0].SetIsOnWithoutNotify(true);
            for (var index = 1; index < toggles.Length; index++)
            {
                toggles[index].SetIsOnWithoutNotify(false);
            }

            return panel;
        }

        private static void ConfigureBackgroundImage(
            RectTransform background,
            Color color)
        {
            var image = background.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void SetRightAnchoredDivider(
            RectTransform divider,
            float centerX,
            float centerY)
        {
            SetRect(
                divider,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0.5f),
                new Vector2(centerX, centerY),
                new Vector2(2f, 44f));
            var image = divider.GetComponent<Image>();
            image.color = new Color(0.42f, 0.46f, 0.52f, 0.8f);
            image.raycastTarget = false;
        }

        private static void MakePanelTransparent(RectTransform panel)
        {
            var image = panel.GetComponent<Image>();
            if (image != null)
            {
                image.color = Color.clear;
                image.raycastTarget = false;
            }
        }

        private static void RemoveUiChild(
            RectTransform parent,
            string childName)
        {
            var child = parent.Find(childName);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void ClearUiChildren(RectTransform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
            }
        }

        private static Button EnsureToolbarButton(
            RectTransform parent,
            string objectName,
            string label,
            TMP_FontAsset font,
            Color color)
        {
            var rect = EnsureUiRect(
                parent,
                objectName,
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button));
            var image = rect.GetComponent<Image>();
            image.color = color;
            var button = rect.GetComponent<Button>();
            button.targetGraphic = image;
            ConfigureSelectableColors(button);
            EnsureStretchedLabel(rect, label, font, 16, FontStyles.Bold);
            return button;
        }

        private static Toggle EnsureToolbarToggle(
            RectTransform parent,
            string objectName,
            string label,
            TMP_FontAsset font,
            Color color,
            ToggleGroup group)
        {
            var rect = EnsureUiRect(
                parent,
                objectName,
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Toggle));
            var background = rect.GetComponent<Image>();
            background.color = color;

            var selection = EnsureUiRect(
                rect,
                "Selection",
                typeof(CanvasRenderer),
                typeof(Image));
            selection.anchorMin = Vector2.zero;
            selection.anchorMax = Vector2.one;
            selection.pivot = new Vector2(0.5f, 0.5f);
            selection.anchoredPosition = Vector2.zero;
            selection.sizeDelta = new Vector2(-6f, -6f);
            var selectionImage = selection.GetComponent<Image>();
            selectionImage.color = new Color(1f, 0.86f, 0.24f, 0.34f);
            selectionImage.raycastTarget = false;

            var toggle = rect.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = selectionImage;
            toggle.group = group;
            ConfigureSelectableColors(toggle);
            EnsureStretchedLabel(rect, label, font, 15, FontStyles.Bold);
            return toggle;
        }

        private static TextMeshProUGUI EnsureStretchedLabel(
            RectTransform parent,
            string label,
            TMP_FontAsset font,
            int fontSize,
            FontStyles fontStyle)
        {
            var text = EnsureToolbarLabel(
                parent,
                "Label",
                label,
                font,
                fontSize,
                fontStyle);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return text;
        }

        private static TextMeshProUGUI EnsureToolbarLabel(
            RectTransform parent,
            string objectName,
            string label,
            TMP_FontAsset font,
            int fontSize,
            FontStyles fontStyle)
        {
            var rect = EnsureUiRect(parent, objectName);
            var text = rect.GetComponent<TextMeshProUGUI>();
            if (text == null)
            {
                text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            }

            if (font != null)
            {
                text.font = font;
                text.fontSharedMaterial = font.material;
            }

            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.text = label;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform EnsureUiRect(
            Transform parent,
            string objectName,
            params System.Type[] componentTypes)
        {
            var rect = parent.Find(objectName) as RectTransform;
            if (rect == null)
            {
                var child = new GameObject(objectName, typeof(RectTransform));
                rect = (RectTransform)child.transform;
                rect.SetParent(parent, false);
            }

            foreach (var componentType in componentTypes)
            {
                if (rect.GetComponent(componentType) == null)
                {
                    rect.gameObject.AddComponent(componentType);
                }
            }

            return rect;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void SetBottomLeftRect(
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
        {
            SetRect(
                rect,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                new Vector2(x, y),
                new Vector2(width, height));
        }

        private static void ConfigureSelectableColors(Selectable selectable)
        {
            selectable.transition = Selectable.Transition.ColorTint;
            var colors = selectable.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.93f, 0.93f, 0.93f, 1f);
            colors.pressedColor = new Color(0.76f, 0.76f, 0.76f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            selectable.colors = colors;
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
