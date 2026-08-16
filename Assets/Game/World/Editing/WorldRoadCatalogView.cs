using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldRoadCatalogView : MonoBehaviour
    {
        [SerializeField] private ScrollRect catalogScroll;
        [SerializeField] private RectTransform catalogContent;
        [SerializeField] private ToggleGroup selectionToggleGroup;
        [SerializeField] private WorldEditActionItemView roadItemPrefab;
        [SerializeField] private WorldEditActionItemView removeItem;

        private readonly HashSet<RoadType> boundRoadTypes = new();
        private readonly List<WorldEditActionItemView> roadItems = new();
        private WorldEditAction selectedAction;
        private bool hasSelectedAction;
        private RoadVisualCatalog catalog;
        private bool initialized;

        public event Action<WorldEditAction, bool> ActionSelectionChanged;

        private void OnEnable()
        {
            if (Application.isPlaying && initialized && catalogScroll != null)
            {
                catalogScroll.verticalNormalizedPosition = 1f;
            }
        }

        public void Initialize(RoadVisualCatalog source)
        {
            catalog = source;
            initialized = true;
            RebuildItems();
        }

        public bool TryGetSelectedAction(out WorldEditAction action)
        {
            if (!isActiveAndEnabled || !hasSelectedAction)
            {
                action = default;
                return false;
            }

            action = selectedAction;
            return true;
        }

        public void ClearSelection()
        {
            var wasSelected = hasSelectedAction;
            SetItemsSelectedWithoutNotify(false);
            selectedAction = default;
            hasSelectedAction = false;
            if (wasSelected)
            {
                ActionSelectionChanged?.Invoke(default, false);
            }
        }

        private void RebuildItems()
        {
            ClearSelection();
            boundRoadTypes.Clear();
            ClearRoadItems();
            ClearRemoveItem();
            if (catalog == null)
            {
                return;
            }

            var itemIndex = 0;
            var roads = catalog.Roads;
            for (var index = 0; index < roads.Count; index++)
            {
                var definition = roads[index];
                if (definition == null
                    || definition.Type == RoadType.None
                    || !boundRoadTypes.Add(definition.Type))
                {
                    continue;
                }

                var item = GetOrCreateRoadItem(itemIndex++);
                if (item == null)
                {
                    continue;
                }

                item.gameObject.SetActive(true);
                item.Bind(
                    WorldEditAction.SetRoad(definition.Type),
                    definition.Name,
                    definition.Thumbnail,
                    selectionToggleGroup,
                    OnItemSelectionChanged);
            }

            if (removeItem != null)
            {
                removeItem.gameObject.SetActive(true);
                removeItem.Bind(
                    WorldEditAction.SetRoad(RoadType.None),
                    "도로 제거",
                    null,
                    selectionToggleGroup,
                    OnItemSelectionChanged);
            }

            ToggleGroupVisualStyle.RefreshFor(
                removeItem != null
                    ? removeItem.GetComponent<Toggle>()
                    : null);
            if (catalogScroll != null)
            {
                catalogScroll.verticalNormalizedPosition = 1f;
            }
        }

        private void OnItemSelectionChanged(
            WorldEditAction action,
            bool isOn)
        {
            if (isOn)
            {
                selectedAction = action;
                hasSelectedAction = true;
            }
            else if (hasSelectedAction && selectedAction.Equals(action))
            {
                selectedAction = default;
                hasSelectedAction = false;
            }

            ActionSelectionChanged?.Invoke(selectedAction, hasSelectedAction);
        }

        private WorldEditActionItemView GetOrCreateRoadItem(int index)
        {
            if (index < roadItems.Count)
            {
                return roadItems[index];
            }

            if (roadItemPrefab == null || catalogContent == null)
            {
                Debug.LogError(
                    $"{nameof(WorldRoadCatalogView)} requires an item prefab and content transform.",
                    this);
                return null;
            }

            var item = Instantiate(roadItemPrefab, catalogContent);
            roadItems.Add(item);
            return item;
        }

        private void ClearRoadItems()
        {
            for (var index = 0; index < roadItems.Count; index++)
            {
                var item = roadItems[index];
                if (item == null)
                {
                    continue;
                }

                item.Clear();
                item.gameObject.SetActive(false);
            }
        }

        private void ClearRemoveItem()
        {
            if (removeItem == null)
            {
                return;
            }

            removeItem.Clear();
            removeItem.gameObject.SetActive(false);
        }

        private void SetItemsSelectedWithoutNotify(bool selected)
        {
            for (var index = 0; index < roadItems.Count; index++)
            {
                roadItems[index]?.SetSelectedWithoutNotify(selected);
            }

            removeItem?.SetSelectedWithoutNotify(selected);
        }
    }
}
