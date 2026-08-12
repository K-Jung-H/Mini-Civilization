using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldEntityCatalogView : MonoBehaviour
    {
        [Header("Data")]
        private EntityCatalog entityCatalog;
        private WorldEditToolbarView toolbarView;

        [Header("Category")]
        [SerializeField] private Toggle natureToggle;
        [SerializeField] private Toggle animalToggle;
        [SerializeField] private Toggle humanToggle;
        [SerializeField] private Toggle buildingToggle;

        [Header("Definition List")]
        [SerializeField] private ScrollRect definitionScroll;
        [SerializeField] private RectTransform definitionContent;
        [SerializeField] private ToggleGroup definitionToggleGroup;
        [SerializeField] private EntityDefinitionItemView definitionItemPrefab;

        [Header("Details")]
        [SerializeField] private ScrollRect detailsScroll;
        [SerializeField] private TMP_Text detailsName;
        [SerializeField] private Image detailsThumbnail;
        [SerializeField] private TMP_Text detailsType;
        [SerializeField] private TMP_Text detailsEmptyText;

        private readonly Dictionary<Toggle, UnityAction<bool>> categoryListeners = new();
        private readonly List<EntityDefinitionItemView> definitionItems = new();
        private int activeDefinitionItemCount;
        private EntityCategory? selectedCategory;
        private EntityDefinition selectedDefinition;
        private bool isSubscribed;

        public EntityCatalog Catalog => entityCatalog;
        public EntityCategory? SelectedCategory => selectedCategory;
        public EntityCategory? ActiveCategory =>
            toolbarView == null
            || (toolbarView.IsExpanded
                && toolbarView.IsEntityGroupExpanded)
                ? selectedCategory
                : null;
        public EntityDefinition SelectedDefinition => selectedDefinition;

        public event Action<EntityCategory?> ActiveCategoryChanged;
        public event Action<EntityDefinition> DefinitionSelected;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Initialize(
            EntityCatalog catalog,
            WorldEditToolbarView toolbar)
        {
            Unsubscribe();
            entityCatalog = catalog;
            toolbarView = toolbar;
            if (definitionToggleGroup != null)
            {
                definitionToggleGroup.allowSwitchOff = true;
            }

            if (Application.isPlaying && isActiveAndEnabled)
            {
                Subscribe();
                Refresh();
            }
        }

        private void Subscribe()
        {
            if (isSubscribed)
            {
                return;
            }

            if (toolbarView != null)
            {
                toolbarView.ExpandedChanged += OnToolbarExpandedChanged;
                toolbarView.EntityGroupExpandedChanged +=
                    OnEntityGroupExpandedChanged;
            }

            BindCategoryToggle(natureToggle, EntityCategory.Nature);
            BindCategoryToggle(animalToggle, EntityCategory.Animal);
            BindCategoryToggle(humanToggle, EntityCategory.Human);
            BindCategoryToggle(buildingToggle, EntityCategory.Building);
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (toolbarView != null)
            {
                toolbarView.ExpandedChanged -= OnToolbarExpandedChanged;
                toolbarView.EntityGroupExpandedChanged -=
                    OnEntityGroupExpandedChanged;
            }

            foreach (var pair in categoryListeners)
            {
                if (pair.Key != null)
                {
                    pair.Key.onValueChanged.RemoveListener(pair.Value);
                }
            }

            categoryListeners.Clear();
            isSubscribed = false;
        }

        private void BindCategoryToggle(
            Toggle toggle,
            EntityCategory category)
        {
            if (toggle == null || categoryListeners.ContainsKey(toggle))
            {
                return;
            }

            UnityAction<bool> listener = _ => OnCategoryChanged();
            categoryListeners.Add(toggle, listener);
            toggle.onValueChanged.AddListener(listener);
        }

        private void OnToolbarExpandedChanged(bool _)
        {
            RefreshVisibility();
            ActiveCategoryChanged?.Invoke(ActiveCategory);
        }

        private void OnEntityGroupExpandedChanged(bool expanded)
        {
            if (!expanded)
            {
                ClearSelectedDefinition();
            }

            RefreshVisibility();
            ActiveCategoryChanged?.Invoke(ActiveCategory);
        }

        private void OnCategoryChanged()
        {
            var nextCategory = ReadSelectedCategory();
            if (selectedCategory == nextCategory)
            {
                RefreshVisibility();
                return;
            }

            selectedCategory = nextCategory;
            ClearSelectedDefinition();
            RebuildDefinitionList();
            RefreshDetails();
            RefreshVisibility();
            ActiveCategoryChanged?.Invoke(ActiveCategory);
        }

        private void Refresh()
        {
            selectedCategory = ReadSelectedCategory();
            ClearSelectedDefinition();
            RebuildDefinitionList();
            RefreshDetails();
            RefreshVisibility();
            ActiveCategoryChanged?.Invoke(ActiveCategory);
        }

        private EntityCategory? ReadSelectedCategory()
        {
            if (natureToggle != null && natureToggle.isOn)
            {
                return EntityCategory.Nature;
            }

            if (animalToggle != null && animalToggle.isOn)
            {
                return EntityCategory.Animal;
            }

            if (humanToggle != null && humanToggle.isOn)
            {
                return EntityCategory.Human;
            }

            return buildingToggle != null && buildingToggle.isOn
                ? EntityCategory.Building
                : null;
        }

        private void RebuildDefinitionList()
        {
            ClearDefinitionItems();
            if (!selectedCategory.HasValue
                || entityCatalog == null
                || definitionContent == null
                || definitionItemPrefab == null)
            {
                return;
            }

            var definitions = entityCatalog.GetDefinitions(
                selectedCategory.Value);
            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                if (definition != null)
                {
                    BindDefinitionItem(definition);
                }
            }

            if (definitionScroll != null)
            {
                definitionScroll.verticalNormalizedPosition = 1f;
            }
        }

        private void ClearDefinitionItems()
        {
            if (definitionContent == null)
            {
                return;
            }

            for (var index = 0; index < definitionItems.Count; index++)
            {
                var item = definitionItems[index];
                if (item != null)
                {
                    item.Clear();
                    item.gameObject.SetActive(false);
                }
            }

            activeDefinitionItemCount = 0;
        }

        private void BindDefinitionItem(EntityDefinition definition)
        {
            var item = GetOrCreateDefinitionItem(activeDefinitionItemCount);
            if (item == null)
            {
                return;
            }

            activeDefinitionItemCount++;
            item.gameObject.SetActive(true);
            item.Bind(
                definition,
                definitionToggleGroup,
                OnDefinitionSelectionChanged);
        }

        private EntityDefinitionItemView GetOrCreateDefinitionItem(int index)
        {
            if (index < definitionItems.Count)
            {
                return definitionItems[index];
            }

            if (definitionItemPrefab == null || definitionContent == null)
            {
                return null;
            }

            var item = Instantiate(definitionItemPrefab, definitionContent);
            definitionItems.Add(item);
            return item;
        }

        private void OnDefinitionSelectionChanged(
            EntityDefinition definition,
            bool isOn)
        {
            if (isOn)
            {
                SetSelectedDefinition(definition);
            }
            else if (ReferenceEquals(selectedDefinition, definition))
            {
                SetSelectedDefinition(null);
            }
        }

        private void SetSelectedDefinition(EntityDefinition definition)
        {
            if (ReferenceEquals(selectedDefinition, definition))
            {
                return;
            }

            selectedDefinition = definition;
            RefreshDetails();
            DefinitionSelected?.Invoke(selectedDefinition);
        }

        public void ClearSelectedDefinition()
        {
            if (selectedDefinition == null)
            {
                return;
            }

            for (var index = 0; index < activeDefinitionItemCount; index++)
            {
                var item = definitionItems[index];
                if (item?.Definition == selectedDefinition)
                {
                    item.SetSelectedWithoutNotify(false);
                    break;
                }
            }

            SetSelectedDefinition(null);
        }

        private void RefreshDetails()
        {
            var definition = selectedDefinition;
            if (detailsName != null)
            {
                detailsName.text = definition?.DisplayName ?? string.Empty;
                detailsName.gameObject.SetActive(definition != null);
            }

            if (detailsThumbnail != null)
            {
                detailsThumbnail.sprite = definition?.Thumbnail;
                detailsThumbnail.gameObject.SetActive(
                    definition?.Thumbnail != null);
            }

            if (detailsType != null)
            {
                var controller = definition?.Prefab;
                detailsType.text = controller?.EntityTypeName ?? string.Empty;
                detailsType.gameObject.SetActive(controller != null);
            }

            if (detailsEmptyText != null)
            {
                detailsEmptyText.gameObject.SetActive(definition == null);
            }

            if (detailsScroll != null)
            {
                detailsScroll.verticalNormalizedPosition = 1f;
            }
        }

        private void RefreshVisibility()
        {
            var visible = toolbarView == null
                || (toolbarView.IsExpanded
                    && toolbarView.IsEntityGroupExpanded);
            if (definitionScroll != null)
            {
                definitionScroll.gameObject.SetActive(
                    visible && selectedCategory.HasValue);
            }

            if (detailsScroll != null)
            {
                detailsScroll.gameObject.SetActive(
                    visible && selectedCategory.HasValue);
            }
        }
    }
}
