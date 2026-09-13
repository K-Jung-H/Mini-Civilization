using System;
using System.Collections.Generic;
using System.Text;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using EntityId = MiniCivilization.World.Domain.EntityId;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldInspectorView : MonoBehaviour
    {
        private enum InspectorContext { Automatic, Cell, EntityList, Entity }
        private WorldManager worldManager;
        [SerializeField] private WorkspaceItemView itemPrefab;
        private bool configured;
        private readonly List<EntityId> entityIds = new();
        private readonly List<EntityId> refreshedEntityIds = new();
        private WorldTileSelectionState selectionState;
        private WorldCellInfoProvider infoProvider;
        [SerializeField] private TMP_Text inspectorTitle;
        [SerializeField] private TMP_Text inspectorBody;
        [SerializeField] private RectTransform inspectorActions;
        [SerializeField] private RectTransform inspectorBox;
        [SerializeField] private Button inspectorCloseButton;
        [SerializeField] private Button inspectorLauncher;
        private EntityId selectedEntityId;
        private TilePickResult? inspectedPick;
        private WorldRuntime inspectedWorld;
        private InspectorContext inspectorContext;
        private bool inspectorActionsDirty = true;
        private bool entityListDirty = true;
        private readonly List<WorkspaceItemView> inspectorRows = new();
        private int usedInspectorRows;
        private CellData? displayedCellData;
        private CellCoordinate displayedCell;
        private (EntityId, EntityAttributes, CellCoordinate, EntityDirection, EntityActivityId, string)? displayedEntity;
        private float nextInspectorRefresh;
        public void Configure(WorldManager manager, WorldTileSelectionState selections, WorldCellInfoProvider provider)
        {
            Unsubscribe();
            worldManager = manager;
            selectionState = selections;
            infoProvider = provider;
            if (inspectorBox == null || inspectorLauncher == null || inspectorCloseButton == null
                || inspectorTitle == null || inspectorBody == null || inspectorActions == null || itemPrefab == null)
            {
                Debug.LogError("WorldInspectorView requires serialized Inspector references.", this);
                return;
            }
            if (!configured) CollapseInspector();
            configured = true;
            Subscribe();
            RefreshInspector(true);
        }

        private void OnEnable()
        {
            if (!configured) return;
            Subscribe();
            entityListDirty = true;
            RefreshInspector(false);
        }
        private void OnDisable() => Unsubscribe();
        private void Update()
        {
            if (!configured || Time.unscaledTime < nextInspectorRefresh) return;
            nextInspectorRefresh = Time.unscaledTime + 0.2f;
            RefreshInspector(false);
        }
        private void Subscribe()
        {
            Unsubscribe();
            if (!isActiveAndEnabled) return;
            if (selectionState != null) selectionState.SelectionChanged += OnSelectionChanged;
            if (worldManager != null)
            {
                worldManager.EntityChanged += OnEntityChanged;
                worldManager.WorldChanged += OnWorldChanged;
            }
            inspectorCloseButton.onClick.AddListener(CollapseInspector);
            inspectorLauncher.onClick.AddListener(ExpandInspector);
        }
        private void Unsubscribe()
        {
            if (selectionState != null) selectionState.SelectionChanged -= OnSelectionChanged;
            if (worldManager != null)
            {
                worldManager.EntityChanged -= OnEntityChanged;
                worldManager.WorldChanged -= OnWorldChanged;
            }
            inspectorCloseButton?.onClick.RemoveListener(CollapseInspector);
            inspectorLauncher?.onClick.RemoveListener(ExpandInspector);
        }
        private void OnWorldChanged() => RefreshInspector(true);
        private void CollapseInspector()
        {
            inspectorBox.gameObject.SetActive(false);
            inspectorLauncher.gameObject.SetActive(true);
        }
        private void ExpandInspector()
        {
            inspectorBox.gameObject.SetActive(true);
            inspectorLauncher.gameObject.SetActive(false);
            entityListDirty = true;
            RefreshInspector(false);
        }
        public void SetTop(float top)
        {
            var offset = inspectorBox.offsetMax;
            offset.y = top;
            inspectorBox.offsetMax = offset;
            var launcher = (RectTransform)inspectorLauncher.transform;
            var position = launcher.anchoredPosition;
            position.y = top;
            launcher.anchoredPosition = position;
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
                inspectorTitle.text = "INSPECTOR";
                inspectorBody.text = "Selected Cell is unavailable.";
                ResetInspectorRows();
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
                    RefreshInspector(true);
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
    }
}
