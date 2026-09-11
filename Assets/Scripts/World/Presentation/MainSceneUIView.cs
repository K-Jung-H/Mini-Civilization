using System;
using System.Collections.Generic;
using System.Text;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Editing;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using EntityId = MiniCivilization.World.Domain.EntityId;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class MainSceneUIView : MonoBehaviour
    {
        private enum WorkspaceTab { Entity, World }
        private enum WorldCategory { Biome, Water, Terrain, Terraform, Road }
        private enum InspectorContext { Automatic, Cell, EntityList, Entity }

        private readonly List<Button> paletteButtons = new();
        private readonly List<Func<bool>> paletteSelectionChecks = new();
        private readonly List<EntityId> entityIds = new();
        private readonly List<EntityId> refreshedEntityIds = new();
        private WorldManager worldManager;
        private WorldEditToolState toolState;
        private WorldEditApplyController editActions;
        private EntityCatalog entityCatalog;

        private RoadVisualCatalog roadVisualCatalog;
        private WorldTileSelectionState selectionState;
        private WorldCellInfoProvider infoProvider;
        [Header("Serialized Main Scene UI")]

        [SerializeField] private RectTransform palette;
        [SerializeField] private RectTransform toolOptions;
        [SerializeField] private TMP_Text paletteTitle;
        [SerializeField] private TMP_Text inspectorTitle;
        [SerializeField] private TMP_Text inspectorBody;
        [SerializeField] private RectTransform inspectorActions;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button redoButton;
        [SerializeField] private Button entityTabButton;
        [SerializeField] private Button worldTabButton;
        [SerializeField] private RectTransform workspaceBox;
        [SerializeField] private RectTransform simulationBox;
        [SerializeField] private RectTransform inspectorBox;
        [SerializeField] private Button workspaceCloseButton;
        [SerializeField] private Button workspaceLauncher;
        [SerializeField] private Button simulationCloseButton;
        [SerializeField] private Button simulationExpandButton;
        [SerializeField] private TMP_Text simulationStatus;
        [SerializeField, Min(1f)] private float simulationExpandedHeight = 180f;
        [SerializeField, Min(1f)] private float simulationCollapsedHeight = 92f;
        [SerializeField, Min(0f)] private float rightColumnGap = 20f;
        [SerializeField] private Button inspectorCloseButton;
        [SerializeField] private Button inspectorLauncher;
        private WorkspaceTab activeTab = WorkspaceTab.Entity;
        private EntityCategory? entityCategory;
        private WorldCategory? worldCategory;
        private EntityId selectedEntityId;
        private TilePickResult? inspectedPick;
        private WorldRuntime inspectedWorld;
        private InspectorContext inspectorContext;
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
        private bool configured;
        private bool inspectorActionsDirty = true;
        private bool paletteSelectionDirty;
        private bool simulationHasWorld;
        private bool simulationExpanded;
        private bool entityListDirty = true;
        private readonly List<WorkspaceItemView> inspectorRows = new();
        private int usedInspectorRows;
        private CellData? displayedCellData;
        private CellCoordinate displayedCell;
        private (EntityId, EntityAttributes, CellCoordinate, EntityDirection, EntityActivityId, string)? displayedEntity;
        private float nextInspectorRefresh;

        public void Configure(
            WorldManager manager,
            WorldEditToolState state,
            WorldEditApplyController actions,
            RoadVisualCatalog roads,
            WorldTileSelectionState selections,
            WorldCellInfoProvider provider)
        {
            Unsubscribe();
            worldManager = manager;
            toolState = state;
            editActions = actions;
            entityCatalog = manager.EntityManager?.Catalog;
            roadVisualCatalog = roads;
            selectionState = selections;
            infoProvider = provider;


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
                CollapseInspector();
            }
            configured = true;
            SetSimulationExpanded(simulationExpanded);
            Subscribe();
            ShowEntityCategories();
            RefreshToolOptions();
            RefreshInspector(true);
        }

        private void OnEnable()
        {
            if (!configured) return;
            Subscribe();
            RefreshToolOptions();
            RefreshPaletteSelection();
            entityListDirty = true;
            RefreshInspector(false);
            RefreshSimulationStatus();
        }

        private void OnDisable() => Unsubscribe();

        private void Update()
        {
            if (!configured) return;
            if (paletteSelectionDirty) RefreshPaletteSelection();
            if (simulationHasWorld != (worldManager != null && worldManager.HasWorld))
                RefreshSimulationStatus();
            if (Time.unscaledTime >= nextInspectorRefresh)
            {
                nextInspectorRefresh = Time.unscaledTime + 0.2f;
                RefreshInspector(false);
            }
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
            if (selectionState != null)
            {
                selectionState.SelectionChanged += OnSelectionChanged;
            }
            if (worldManager != null)
            {
                worldManager.EntityChanged += OnEntityChanged;
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
            if (selectionState != null)
            {
                selectionState.SelectionChanged -= OnSelectionChanged;
            }
            if (worldManager != null)
            {
                worldManager.EntityChanged -= OnEntityChanged;
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
            && inspectorTitle != null
            && inspectorBody != null
            && inspectorActions != null
            && undoButton != null
            && redoButton != null
            && entityTabButton != null
            && worldTabButton != null
            && workspaceBox != null && simulationBox != null && inspectorBox != null
            && workspaceCloseButton != null && workspaceLauncher != null
            && simulationCloseButton != null && simulationExpandButton != null
            && simulationStatus != null
            && inspectorCloseButton != null && inspectorLauncher != null;

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
            var building = IsBuildingTool(tool);
            brushButton.gameObject.SetActive(!building);
            areaButton.gameObject.SetActive(!building);
            rectangleButton.gameObject.SetActive(!building);
            rectangleButton.interactable = false;
            SetButtonSelected(singleButton, tool.Mode == WorldEditMode.Single);
            SetButtonSelected(brushButton, tool.Mode == WorldEditMode.Brush);
            SetButtonSelected(areaButton, tool.Mode == WorldEditMode.Area);
            var showBrushSizes = tool.Mode == WorldEditMode.Brush;
            brushSizes.gameObject.SetActive(showBrushSizes);
            toolOptions.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                toolOptionsCompactHeight
                + (showBrushSizes ? brushSizes.rect.height + toolOptionsRowGap : 0f));
            SetButtonSelected(sizeOneButton, tool.BrushSize == 1);
            SetButtonSelected(sizeTwoButton, tool.BrushSize == 2);
            SetButtonSelected(sizeThreeButton, tool.BrushSize == 3);
        }
        private static bool IsBuildingTool(WorldEditToolSnapshot tool) =>
            tool.EntityDefinition != null
            && tool.EntityDefinition.TypeKey.Category == EntityCategory.Building;

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

        private void OnSelectionChanged(TilePickResult? pick)
        {
            entityListDirty = true;
            displayedCellData = null;
            displayedEntity = null;
            inspectedPick = pick;
            selectedEntityId = EntityId.None;
            inspectorContext = InspectorContext.Automatic;
            RefreshInspector(true);
        }

        private void OnEntityChanged(EntityChangeSet changeSet)
        {
            if (!inspectedPick.HasValue || changeSet == null)
            {
                return;
            }

            var selectedCell = inspectedPick.Value.Cell;
            for (var index = 0; index < changeSet.AffectedCells.Count; index++)
            {
                if (!changeSet.AffectedCells[index].Equals(selectedCell))
                {
                    continue;
                }

                // The periodic refresh compares IDs before rebuilding action rows.
                entityListDirty = true;
                return;
            }
        }

        private void RefreshInspector(bool rebuildActions)
        {
            inspectorActionsDirty |= rebuildActions;
            if (inspectorBox == null || !inspectorBox.gameObject.activeInHierarchy) return;
            rebuildActions = inspectorActionsDirty;
            inspectorActionsDirty = false;
            if (inspectorBody == null || worldManager == null)
            {
                return;
            }
            if (!ReferenceEquals(inspectedWorld, worldManager.CurrentWorldRuntime))
            {
                inspectedWorld = worldManager.CurrentWorldRuntime;
                entityListDirty = true;
                displayedCellData = null;
                displayedEntity = null;
                inspectedPick = null;
                selectedEntityId = EntityId.None;
                inspectorContext = InspectorContext.Automatic;
                rebuildActions = true;
            }
            if (!worldManager.HasWorld)
            {
                inspectedPick = null;
                selectedEntityId = EntityId.None;
                entityIds.Clear();
                refreshedEntityIds.Clear();
                inspectorContext = InspectorContext.Automatic;
                inspectorTitle.text = "INSPECTOR";
                inspectorBody.text = "No world is loaded.";
                ResetInspectorRows();
                return;
            }
            var pick = selectionState?.Selected;
            if (!pick.HasValue)
            {
                inspectedPick = null;
                selectedEntityId = EntityId.None;
                entityIds.Clear();
                refreshedEntityIds.Clear();
                inspectorContext = InspectorContext.Automatic;
                inspectorTitle.text = "INSPECTOR";
                inspectorBody.text = "Select a Cell.";
                ResetInspectorRows();
                return;
            }
            var world = worldManager.CurrentWorldData;
            var cell = pick.Value.Cell;
            if (!world.Contains(cell.X, cell.Y, cell.Z) || !world.IsChunkLoaded(cell.X, cell.Z))
            {
                selectionState.SetSelected(null);
                return;
            }
            if (!inspectedPick.HasValue || !inspectedPick.Value.Equals(pick.Value))
            {
                entityListDirty = true;
                displayedCellData = null;
                displayedEntity = null;
                inspectedPick = pick;
                selectedEntityId = EntityId.None;
                inspectorContext = InspectorContext.Automatic;
                rebuildActions = true;
            }

            var entities = worldManager.EntityManager?.Entities;
            if (entityListDirty || rebuildActions)
            {
                entityListDirty = false;
                refreshedEntityIds.Clear();
                var ids = entities?.GetEntitiesAt(pick.Value.Cell);
                if (ids != null)
                {
                    refreshedEntityIds.AddRange(ids);
                    refreshedEntityIds.Sort();
                }
                var entityListChanged = !ListsEqual(
                    entityIds,
                    refreshedEntityIds);
                if (entityListChanged)
                {
                    entityIds.Clear();
                    entityIds.AddRange(refreshedEntityIds);
                    rebuildActions = true;

                    if (inspectorContext == InspectorContext.EntityList)
                    {
                        if (entityIds.Count == 1)
                        {
                            selectedEntityId = entityIds[0];
                            inspectorContext = InspectorContext.Entity;
                        }
                        else if (entityIds.Count == 0)
                        {
                            inspectorContext = InspectorContext.Cell;
                        }
                    }
                }
            }
            if (selectedEntityId.IsValid
                && (entities == null || !entities.TryGet(selectedEntityId, out _)))
            {
                selectedEntityId = EntityId.None;
                inspectorContext = entityIds.Count > 1
                    ? InspectorContext.EntityList
                    : InspectorContext.Cell;
                rebuildActions = true;
            }
            if (inspectorContext == InspectorContext.Automatic)
            {
                if (entityIds.Count == 1)
                {
                    selectedEntityId = entityIds[0];
                    inspectorContext = InspectorContext.Entity;
                }
                else
                {
                    inspectorContext = entityIds.Count > 1
                        ? InspectorContext.EntityList
                        : InspectorContext.Cell;
                }
                rebuildActions = true;
            }
            if (inspectorContext == InspectorContext.Entity
                && selectedEntityId.IsValid
                && entities != null
                && entities.TryGet(selectedEntityId, out var entity))
            {
                ShowEntity(entity, rebuildActions);
                return;
            }
            ShowCell(pick.Value, rebuildActions);
        }

        private void ShowCell(TilePickResult pick, bool rebuildActions)
        {
            inspectorTitle.text = entityIds.Count > 1
                ? $"CELL — {entityIds.Count} ENTITIES"
                : "CELL";
            var data = worldManager.CurrentWorldData.GetCell(pick.Cell.X, pick.Cell.Y, pick.Cell.Z);
            if (!displayedCellData.HasValue || !displayedCell.Equals(pick.Cell)
                || !displayedCellData.Value.Equals(data) || displayedEntity.HasValue)
            {
                displayedCellData = data;
                displayedCell = pick.Cell;
                displayedEntity = null;
                inspectorBody.text = infoProvider == null ? pick.Cell.ToString()
                    : FormatCell(infoProvider.Create(worldManager.CurrentWorldRuntime, pick));
            }
            if (!rebuildActions)
            {
                return;
            }
            ResetInspectorRows();
            if (entityIds.Count == 0)
            {
                return;
            }
            foreach (var id in entityIds)
            {
                var captured = id;
                var label = $"Entity #{id}";
                if (worldManager.EntityManager.Entities.TryGet(id, out var entity)
                    && worldManager.EntityManager.Catalog.TryGetDefinition(
                        entity.TypeKey, out var definition))
                {
                    label = $"{definition.DisplayName}  #{id}";
                }
                BindInspectorRow(label, () =>
                {
                    selectedEntityId = captured;
                    inspectorContext = InspectorContext.Entity;
                    RefreshInspector(true);
                });
            }
        }

        private void ShowEntity(EntityRuntime entity, bool rebuildActions)
        {
            var definitionName = entity.TypeKey.ToString();
            EntityDefinition definition = null;
            worldManager.EntityManager?.Catalog?.TryGetDefinition(
                entity.TypeKey, out definition);
            if (definition != null)
            {
                definitionName = definition.DisplayName;
            }
            inspectorTitle.text = $"ENTITY — {definitionName}";
            var attributes = entity.Data.Attributes;
            var display = (entity.Id, attributes, entity.AnchorCell, entity.Direction, entity.Activity, definitionName);
            if (!displayedEntity.HasValue || !displayedEntity.Value.Equals(display))
            {
                displayedEntity = display;
                var text = new StringBuilder(256)
                    .AppendLine("<b>Identity</b>")
                    .AppendLine($"Name: {attributes.Name}")
                    .AppendLine($"Age: {attributes.Age}")
                    .AppendLine($"Kind: {definitionName}")
                    .AppendLine("\n<b>Current state</b>")
                    .AppendLine($"Cell: {entity.AnchorCell}")
                    .AppendLine($"Direction: {entity.Direction}")
                    .AppendLine($"Activity: {entity.Activity}")
                    .AppendLine("\n<b>Traits</b>");
                foreach (var trait in attributes.Traits)
                {
                    text.AppendLine($"{trait.Id}: {trait.Value:0.##}");
                }
                inspectorBody.text = text.ToString();
            }
            if (!rebuildActions)
            {
                return;
            }
            ResetInspectorRows();
            BindInspectorRow(entityIds.Count > 1 ? "< Entity List" : "< Cell Info",
                () =>
                {
                    selectedEntityId = EntityId.None;
                    inspectorContext = entityIds.Count > 1
                        ? InspectorContext.EntityList
                        : InspectorContext.Cell;
                    ShowCell(inspectedPick.Value, true);
                });
        }

        private static string FormatCell(WorldCellInfoSnapshot snapshot)
        {
            var cell = snapshot.Cell;
            var water = cell.HasWater
                ? $"Type: {cell.Water.Type}\nFill: {cell.WaterHeight}/{WorldGrid.HeightStepsPerCell}"
                : "None";
            return $"<b>Location</b>\n{snapshot.Pick.Cell}\n\n" +
                $"<b>Terrain</b>\nMaterial: {cell.Terrain.Material}\nSurface: {cell.Terrain.Surface}\n" +
                $"Fill: {cell.Terrain.SolidHeight}/{WorldGrid.HeightStepsPerCell}\n\n" +
                $"<b>Environment</b>\nClimate: {cell.Biome.Climate}\nBiome: {cell.Biome.Terrain}\n\n" +
                $"<b>Water</b>\n{water}\n";
        }
        private static bool ListsEqual(
            IReadOnlyList<EntityId> left,
            IReadOnlyList<EntityId> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }
            for (var index = 0; index < left.Count; index++)
            {
                if (!left[index].Equals(right[index]))
                {
                    return false;
                }
            }
            return true;
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
            inspectorCloseButton.onClick.AddListener(CollapseInspector);
            inspectorLauncher.onClick.AddListener(ExpandInspector);
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
            inspectorCloseButton?.onClick.RemoveListener(CollapseInspector);
            inspectorLauncher?.onClick.RemoveListener(ExpandInspector);
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
        private void CollapseInspector() => SetPanelExpanded(inspectorBox, inspectorLauncher, false);
        private void ExpandInspector()
        {
            SetPanelExpanded(inspectorBox, inspectorLauncher, true);
            entityListDirty = true;
            RefreshInspector(false);
        }

        private void OnWorldChanged()
        {
            inspectorActionsDirty = true;
            RefreshInspector(true);
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
            var inspectorOffset = inspectorBox.offsetMax;
            inspectorOffset.y = inspectorTop;
            inspectorBox.offsetMax = inspectorOffset;
            var launcher = (RectTransform)inspectorLauncher.transform;
            var launcherPosition = launcher.anchoredPosition;
            launcherPosition.y = inspectorTop;
            launcher.anchoredPosition = launcherPosition;
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

        private void ResetInspectorRows()
        {
            usedInspectorRows = 0;
            foreach (var row in inspectorRows) row.gameObject.SetActive(false);
        }

        private void BindInspectorRow(string label, Action action)
        {
            if (usedInspectorRows == inspectorRows.Count)
                inspectorRows.Add(Instantiate(itemPrefab, inspectorActions));
            var row = inspectorRows[usedInspectorRows++];
            row.Bind(label, null, action);
            row.gameObject.SetActive(true);
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
