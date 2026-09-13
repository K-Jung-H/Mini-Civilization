using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class MainSceneUIView : MonoBehaviour
    {
        private enum WorkspaceTab { Entity, World }
        private enum WorldCategory { Biome, Water, Terrain, Terraform, Road }

        private readonly List<Button> paletteButtons = new();
        private readonly List<Func<bool>> paletteSelectionChecks = new();
        private WorldManager worldManager;
        private WorldEditToolState toolState;
        private WorldEditApplyController editActions;
        private EntityCatalog entityCatalog;

        private RoadVisualCatalog roadVisualCatalog;
        [Header("Serialized Main Scene UI")]

        [SerializeField] private RectTransform palette;
        [SerializeField] private RectTransform toolOptions;
        [SerializeField] private TMP_Text paletteTitle;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button redoButton;
        [SerializeField] private Button entityTabButton;
        [SerializeField] private Button worldTabButton;
        [SerializeField] private RectTransform workspaceBox;
        [SerializeField] private RectTransform simulationBox;
        [SerializeField] private Button workspaceCloseButton;
        [SerializeField] private Button workspaceLauncher;
        [SerializeField] private Button simulationCloseButton;
        [SerializeField] private Button simulationExpandButton;
        [SerializeField] private TMP_Text simulationStatus;
        [SerializeField, Min(1f)] private float simulationExpandedHeight = 180f;
        [SerializeField, Min(1f)] private float simulationCollapsedHeight = 92f;
        [SerializeField, Min(0f)] private float rightColumnGap = 20f;
        private WorkspaceTab activeTab = WorkspaceTab.Entity;
        private EntityCategory? entityCategory;
        private WorldCategory? worldCategory;
        [SerializeField] private WorkspaceItemView itemPrefab;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text activeToolLabel;
        [SerializeField] private Button singleButton;
        [SerializeField] private Button brushButton;
        [SerializeField] private Button areaButton;
        [SerializeField] private Button rectangleButton;
        [SerializeField] private RectTransform brushSizes;
        [SerializeField, Min(1f)] private float toolOptionsCompactHeight = 168f;
        [SerializeField, Min(0f)] private float toolOptionsRowGap = 8f;
        [SerializeField] private Button sizeOneButton;
        [SerializeField] private Button sizeTwoButton;
        [SerializeField] private Button sizeThreeButton;
        [SerializeField] private Sprite[] terraformIcons;
        [SerializeField] private WorldInspectorView inspectorView;
        private bool configured;
        private bool paletteSelectionDirty;
        private bool simulationHasWorld;
        private bool simulationExpanded;

        public void Configure(
            WorldManager manager,
            WorldEditToolState state,
            WorldEditApplyController actions,
            RoadVisualCatalog roads)
        {
            Unsubscribe();
            worldManager = manager;
            toolState = state;
            editActions = actions;
            entityCatalog = manager.EntityManager?.Catalog;
            roadVisualCatalog = roads;


            if (!ValidateLayout())
            {
                Debug.LogError(
                    $"{nameof(MainSceneUIView)} requires the serialized Main Scene UI hierarchy.",
                    this);
                return;
            }
            if (!configured)
            {
                CollapseWorkspace();
            }
            configured = true;
            SetSimulationExpanded(simulationExpanded);
            Subscribe();
            ShowEntityCategories();
            RefreshToolOptions();
        }

        private void OnEnable()
        {
            if (!configured) return;
            Subscribe();
            RefreshToolOptions();
            RefreshPaletteSelection();
            RefreshSimulationStatus();
        }

        private void OnDisable() => Unsubscribe();

        private void Update()
        {
            if (!configured) return;
            if (paletteSelectionDirty) RefreshPaletteSelection();
            if (simulationHasWorld != (worldManager != null && worldManager.HasWorld))
                RefreshSimulationStatus();
        }

        private void Subscribe()
        {
            Unsubscribe();
            if (!isActiveAndEnabled) return;
            if (toolState != null)
            {
                toolState.StateChanged += OnToolStateChanged;
            }
            if (editActions != null)
            {
                editActions.HistoryAvailabilityChanged +=
                    OnHistoryAvailabilityChanged;
            }
            if (worldManager != null)
            {
                worldManager.WorldChanged += OnWorldChanged;
            }
            BindStaticControls();
        }

        private void Unsubscribe()
        {
            if (toolState != null)
            {
                toolState.StateChanged -= OnToolStateChanged;
            }
            if (editActions != null)
            {
                editActions.HistoryAvailabilityChanged -=
                    OnHistoryAvailabilityChanged;
            }
            if (worldManager != null)
            {
                worldManager.WorldChanged -= OnWorldChanged;
            }
            UnbindStaticControls();
        }

        private bool ValidateLayout() =>
            itemPrefab != null && backButton != null && activeToolLabel != null
            && singleButton != null && brushButton != null && areaButton != null
            && rectangleButton != null && brushSizes != null
            && sizeOneButton != null && sizeTwoButton != null && sizeThreeButton != null
            && palette != null
            && toolOptions != null
            && paletteTitle != null
            && undoButton != null
            && redoButton != null
            && entityTabButton != null
            && worldTabButton != null
            && workspaceBox != null && simulationBox != null
            && workspaceCloseButton != null && workspaceLauncher != null
            && simulationCloseButton != null && simulationExpandButton != null
            && simulationStatus != null
            && inspectorView != null;

        private void SelectTab(WorkspaceTab tab)
        {
            activeTab = tab;
            RefreshTabSelection();
            ClearActiveTool();
            if (tab == WorkspaceTab.Entity)
            {
                ShowEntityCategories();
            }
            else
            {
                ShowWorldCategories();
            }
        }

        private void ShowEntityCategories()
        {
            ClearActiveTool();
            activeTab = WorkspaceTab.Entity;
            entityCategory = null;
            SetPaletteTitle("Entity Categories");
            ClearPalette();
            AddPaletteButton("Animal", () => OpenEntityCategory(EntityCategory.Animal));
            AddPaletteButton("Nature", () => OpenEntityCategory(EntityCategory.Nature));
            AddPaletteButton("Human", () => OpenEntityCategory(EntityCategory.Human));
            AddPaletteButton("Building", () => OpenEntityCategory(EntityCategory.Building));
        }

        private void OpenEntityCategory(EntityCategory category)
        {
            ClearActiveTool();
            entityCategory = category;
            SetPaletteTitle($"Entity / {category}");
            ClearPalette();
            var definitions = entityCatalog?.GetDefinitions(category);
            if (definitions == null || definitions.Count == 0)
            {
                AddDisabledPaletteButton("No content available");
                return;
            }
            foreach (var definition in definitions)
            {
                var captured = definition;
                AddPaletteButton(definition.DisplayName,
                    () => ToggleEntityTool(captured),
                    () => ReferenceEquals(
                        toolState?.Current.EntityDefinition,
                        captured), definition.Thumbnail);
            }
        }

        private void ShowWorldCategories()
        {
            ClearActiveTool();
            activeTab = WorkspaceTab.World;
            worldCategory = null;
            SetPaletteTitle("World Categories");
            ClearPalette();
            foreach (WorldCategory category in Enum.GetValues(typeof(WorldCategory)))
            {
                var captured = category;
                AddPaletteButton(category.ToString(), () => OpenWorldCategory(captured));
            }
        }

        private void OpenWorldCategory(WorldCategory category)
        {
            ClearActiveTool();
            worldCategory = category;
            SetPaletteTitle($"World / {category}");
            ClearPalette();
            switch (category)
            {
                case WorldCategory.Terraform:
                    AddWorldAction("Raise", TerrainEditOperation.Raise);
                    AddWorldAction("Lower", TerrainEditOperation.Lower);
                    AddWorldAction("Add", TerrainEditOperation.Add);
                    AddWorldAction("Remove", TerrainEditOperation.Remove);
                    break;
                case WorldCategory.Road:
                    AddRoadActions();
                    break;
                default:
                    AddDisabledPaletteButton("Not supported yet");
                    break;
            }
        }

        private void AddWorldAction(string label, TerrainEditOperation operation) =>
            AddPaletteButton(label, () => ToggleActionTool(
                WorldEditAction.Terrain(operation)),
                () => toolState != null
                    && toolState.Current.Action.Equals(
                        WorldEditAction.Terrain(operation)),
                terraformIcons != null && (int)operation < terraformIcons.Length ? terraformIcons[(int)operation] : null);

        private void AddRoadActions()
        {
            if (roadVisualCatalog != null)
            {
                foreach (var road in roadVisualCatalog.Roads)
                {
                    if (road == null || road.Type == RoadType.None)
                    {
                        continue;
                    }
                    var type = road.Type;
                    AddPaletteButton(road.Name,
                        () => ToggleActionTool(WorldEditAction.SetRoad(type)),
                        () => toolState != null
                            && toolState.Current.Action.Equals(
                                WorldEditAction.SetRoad(type)), road.Thumbnail);
                }
            }
            AddPaletteButton("Remove Road",
                () => ToggleActionTool(WorldEditAction.SetRoad(RoadType.None)),
                () => toolState != null
                    && toolState.Current.Action.Equals(
                        WorldEditAction.SetRoad(RoadType.None)));
        }

        private void ToggleEntityTool(EntityDefinition definition)
        {
            if (toolState == null) return;
            if (ReferenceEquals(toolState.EntityDefinition, definition))
                toolState.ClearActiveTool();
            else
                toolState.SelectEntity(definition);
        }

        private void ToggleActionTool(WorldEditAction action)
        {
            if (toolState == null) return;
            if (toolState.Action.Equals(action))
                toolState.ClearActiveTool();
            else
                toolState.SelectAction(action);
        }

        private void ClearActiveTool()
        {
            toolState?.ClearActiveTool();
            RefreshToolOptions();
        }

        private void OnToolStateChanged(WorldEditToolSnapshot _)
        {
            RefreshToolOptions();
            RefreshPaletteSelection();
        }

        private void OnHistoryAvailabilityChanged(bool canUndo, bool canRedo)
        {
            if (undoButton != null)
            {
                undoButton.interactable = canUndo;
            }
            if (redoButton != null)
            {
                redoButton.interactable = canRedo;
            }
        }

        private void RefreshHistoryAvailability() =>
            OnHistoryAvailabilityChanged(
                editActions != null && editActions.CanUndo,
                editActions != null && editActions.CanRedo);

        private void RefreshToolOptions()
        {
            if (toolOptions == null) return;
            var tool = toolState?.Current ?? default;
            toolOptions.gameObject.SetActive(tool.HasActiveTool);
            if (!tool.HasActiveTool) return;
            activeToolLabel.text = DescribeTool(tool);
            singleButton.gameObject.SetActive(tool.SupportsMode(WorldEditMode.Single));
            brushButton.gameObject.SetActive(tool.SupportsMode(WorldEditMode.Brush));
            areaButton.gameObject.SetActive(tool.SupportsMode(WorldEditMode.Area));
            rectangleButton.gameObject.SetActive(tool.ShowsRectanglePlaceholder);
            rectangleButton.interactable = false;
            SetButtonSelected(singleButton, tool.Mode == WorldEditMode.Single);
            SetButtonSelected(brushButton, tool.Mode == WorldEditMode.Brush);
            SetButtonSelected(areaButton, tool.Mode == WorldEditMode.Area);
            var showBrushSizes = tool.UsesBrushSize;
            brushSizes.gameObject.SetActive(showBrushSizes);
            toolOptions.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                toolOptionsCompactHeight
                + (showBrushSizes ? brushSizes.rect.height + toolOptionsRowGap : 0f));
            SetButtonSelected(sizeOneButton, tool.BrushSize == 1);
            SetButtonSelected(sizeTwoButton, tool.BrushSize == 2);
            SetButtonSelected(sizeThreeButton, tool.BrushSize == 3);
        }
        private static string DescribeTool(WorldEditToolSnapshot tool)
        {
            if (tool.EntityDefinition != null)
            {
                return $"Active: {tool.EntityDefinition.DisplayName}";
            }
            return tool.PropertyGroup == WorldEditPropertyGroup.Road
                ? $"Active: Road ({tool.Action.RoadType})"
                : $"Active: Terraform / {tool.Action.TerrainOperation}";
        }

        private void SetPaletteTitle(string value)
        {
            if (paletteTitle != null)
            {
                paletteTitle.text = value;
            }
        }

        private void ClearPalette()
        {
            backButton.interactable = activeTab == WorkspaceTab.Entity ? entityCategory.HasValue : worldCategory.HasValue;
            RefreshTabSelection();
            paletteButtons.Clear();
            paletteSelectionChecks.Clear();
            ClearChildren(palette);
        }

        private void AddPaletteButton(
            string label,
            Action action,
            Func<bool> isSelected = null, Sprite icon = null)
        {
            paletteButtons.Add(CreateButton(label, palette, action, icon));
            paletteSelectionChecks.Add(isSelected);
            paletteSelectionDirty = true;
        }

        private void AddDisabledPaletteButton(string label)
        {
            var button = CreateButton(label, palette, null);
            button.interactable = false;
            paletteButtons.Add(button);
            paletteSelectionChecks.Add(null);
        }

        private void RefreshTabSelection()
        {
            SetButtonSelected(
                entityTabButton,
                activeTab == WorkspaceTab.Entity);
            SetButtonSelected(
                worldTabButton,
                activeTab == WorkspaceTab.World);
        }

        private void RefreshPaletteSelection()
        {
            paletteSelectionDirty = false;
            for (var index = 0; index < paletteButtons.Count; index++)
            {
                var check = index < paletteSelectionChecks.Count
                    ? paletteSelectionChecks[index]
                    : null;
                SetButtonSelected(
                    paletteButtons[index],
                    check != null && check());
            }
        }

        private static void SetButtonSelected(Button button, bool selected)
        {
            if (button?.targetGraphic is Image image)
            {
                image.color = selected
                    ? new Color(0.22f, 0.48f, 0.72f, 1f)
                    : new Color(0.16f, 0.18f, 0.22f, 1f);
            }
        }

        private void BindStaticControls()
        {
            UnbindStaticControls();
            backButton.onClick.AddListener(BackToCategories);
            singleButton.onClick.AddListener(SelectSingle);
            brushButton.onClick.AddListener(SelectBrush);
            areaButton.onClick.AddListener(SelectArea);
            sizeOneButton.onClick.AddListener(SelectSizeOne);
            sizeTwoButton.onClick.AddListener(SelectSizeTwo);
            sizeThreeButton.onClick.AddListener(SelectSizeThree);
            RefreshHistoryAvailability();
            entityTabButton.onClick.AddListener(SelectEntityTab);
            worldTabButton.onClick.AddListener(SelectWorldTab);
            undoButton.onClick.AddListener(RequestUndo);
            redoButton.onClick.AddListener(RequestRedo);
            workspaceCloseButton.onClick.AddListener(CollapseWorkspace);
            workspaceLauncher.onClick.AddListener(ExpandWorkspace);
            simulationCloseButton.onClick.AddListener(CollapseSimulation);
            simulationExpandButton.onClick.AddListener(ExpandSimulation);
        }

        private void UnbindStaticControls()
        {
            backButton?.onClick.RemoveListener(BackToCategories);
            singleButton?.onClick.RemoveListener(SelectSingle);
            brushButton?.onClick.RemoveListener(SelectBrush);
            areaButton?.onClick.RemoveListener(SelectArea);
            sizeOneButton?.onClick.RemoveListener(SelectSizeOne);
            sizeTwoButton?.onClick.RemoveListener(SelectSizeTwo);
            sizeThreeButton?.onClick.RemoveListener(SelectSizeThree);
            entityTabButton?.onClick.RemoveListener(SelectEntityTab);
            worldTabButton?.onClick.RemoveListener(SelectWorldTab);
            undoButton?.onClick.RemoveListener(RequestUndo);
            redoButton?.onClick.RemoveListener(RequestRedo);
            workspaceCloseButton?.onClick.RemoveListener(CollapseWorkspace);
            workspaceLauncher?.onClick.RemoveListener(ExpandWorkspace);
            simulationCloseButton?.onClick.RemoveListener(CollapseSimulation);
            simulationExpandButton?.onClick.RemoveListener(ExpandSimulation);
        }

        private void BackToCategories() => SelectTab(activeTab);
        private void SelectSingle() => toolState?.SelectMode(WorldEditMode.Single);
        private void SelectBrush() => toolState?.SelectMode(WorldEditMode.Brush);
        private void SelectArea() => toolState?.SelectMode(WorldEditMode.Area);
        private void SelectSizeOne() => toolState?.SelectBrushSize(1);
        private void SelectSizeTwo() => toolState?.SelectBrushSize(2);
        private void SelectSizeThree() => toolState?.SelectBrushSize(3);
        private void SelectEntityTab() => SelectTab(WorkspaceTab.Entity);
        private void SelectWorldTab() => SelectTab(WorkspaceTab.World);
        private void RequestUndo() => editActions?.RequestUndo();
        private void RequestRedo() => editActions?.RequestRedo();
        private void CollapseWorkspace() => SetPanelExpanded(workspaceBox, workspaceLauncher, false);
        private void ExpandWorkspace() => SetPanelExpanded(workspaceBox, workspaceLauncher, true);
        private void CollapseSimulation() => SetSimulationExpanded(false);
        private void ExpandSimulation() => SetSimulationExpanded(true);

        private void OnWorldChanged()
        {
            RefreshSimulationStatus();
        }

        private void SetSimulationExpanded(bool expanded)
        {
            simulationExpanded = expanded;
            simulationBox.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                expanded ? simulationExpandedHeight : simulationCollapsedHeight);
            simulationCloseButton.gameObject.SetActive(expanded);
            simulationExpandButton.gameObject.SetActive(!expanded);

            // All three RectTransforms use the same Canvas and top anchor.
            // Update both Inspector representations even when either is hidden.
            var inspectorTop = simulationBox.anchoredPosition.y
                - simulationBox.rect.height - rightColumnGap;
            inspectorView.SetTop(inspectorTop);
            RefreshSimulationStatus();
        }

        private void RefreshSimulationStatus()
        {
            if (simulationStatus == null) return;
            simulationHasWorld = worldManager != null && worldManager.HasWorld;
            var summary = simulationHasWorld
                ? "World loaded" : "No world loaded";
            simulationStatus.text = simulationExpanded
                ? $"{summary}\n\nSimulation controls are not supported yet."
                : summary;
        }

        private static void SetPanelExpanded(RectTransform box, Button launcher, bool expanded)
        {
            box.gameObject.SetActive(expanded);
            launcher.gameObject.SetActive(!expanded);
        }

        private Button CreateButton(string label, Transform parent, Action action,
            Sprite icon = null)
        {
            var item = Instantiate(itemPrefab, parent);
            item.gameObject.name = label;
            return item.Bind(label, icon, action);
        }
        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (var index = parent.childCount - 1; index >= 0; index--)
            {
                var child = parent.GetChild(index).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }
    }
}
