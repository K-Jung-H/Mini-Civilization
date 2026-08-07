using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Entities;
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
        [SerializeField] private EntityCatalog entityCatalog;
        [SerializeField] private WorldEditToolbarView toolbarView;

        [Header("Category")]
        [SerializeField] private Toggle natureToggle;
        [SerializeField] private Toggle animalToggle;
        [SerializeField] private Toggle humanToggle;
        [SerializeField] private Toggle buildingToggle;

        [Header("Definition List")]
        [SerializeField] private ScrollRect definitionScroll;
        [SerializeField] private RectTransform definitionContent;
        [SerializeField] private ToggleGroup definitionToggleGroup;

        [Header("Details")]
        [SerializeField] private ScrollRect detailsScroll;
        [SerializeField] private TMP_Text detailsName;
        [SerializeField] private Image detailsThumbnail;
        [SerializeField] private TMP_Text detailsEmptyText;

        [Header("Style")]
        [SerializeField] private TMP_FontAsset labelFont;

        private readonly Dictionary<Toggle, UnityAction<bool>> categoryListeners = new();
        private EntityCategory? selectedCategory;
        private EntityDefinition selectedDefinition;
        private bool isSubscribed;

        public EntityCatalog Catalog => entityCatalog;
        public EntityCategory? SelectedCategory => selectedCategory;
        public EntityDefinition SelectedDefinition => selectedDefinition;

        public event Action<EntityDefinition> DefinitionSelected;

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            EntityCatalog catalog,
            WorldEditToolbarView toolbar,
            Toggle nature,
            Toggle animal,
            Toggle human,
            Toggle building,
            ScrollRect definitions,
            RectTransform definitionsContent,
            ToggleGroup definitionsGroup,
            ScrollRect details,
            TMP_Text nameLabel,
            Image thumbnailImage,
            TMP_Text emptyLabel,
            TMP_FontAsset font)
        {
            Unsubscribe();
            entityCatalog = catalog;
            toolbarView = toolbar;
            natureToggle = nature;
            animalToggle = animal;
            humanToggle = human;
            buildingToggle = building;
            definitionScroll = definitions;
            definitionContent = definitionsContent;
            definitionToggleGroup = definitionsGroup;
            detailsScroll = details;
            detailsName = nameLabel;
            detailsThumbnail = thumbnailImage;
            detailsEmptyText = emptyLabel;
            labelFont = font;

            if (isActiveAndEnabled)
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
            selectedDefinition = null;
            RebuildDefinitionList();
            RefreshDetails();
            RefreshVisibility();
        }

        private void Refresh()
        {
            selectedCategory = ReadSelectedCategory();
            selectedDefinition = null;
            RebuildDefinitionList();
            RefreshDetails();
            RefreshVisibility();
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
                || definitionContent == null)
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
                    CreateDefinitionToggle(definition);
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

            for (var index = definitionContent.childCount - 1;
                 index >= 0;
                 index--)
            {
                var child = definitionContent.GetChild(index).gameObject;
                child.transform.SetParent(null, false);
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private void CreateDefinitionToggle(EntityDefinition definition)
        {
            var item = new GameObject(
                definition.DisplayName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Toggle),
                typeof(LayoutElement));
            var rect = (RectTransform)item.transform;
            rect.SetParent(definitionContent, false);

            var background = item.GetComponent<Image>();
            background.color = new Color(0.18f, 0.23f, 0.31f, 1f);
            var layout = item.GetComponent<LayoutElement>();
            layout.minHeight = 58f;
            layout.preferredHeight = 58f;

            var thumbnail = CreateThumbnail(rect);
            thumbnail.sprite = definition.Thumbnail;
            thumbnail.enabled = definition.Thumbnail != null;

            var label = CreateLabel(rect, definition.DisplayName);
            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.group = definitionToggleGroup;
            toggle.transition = Selectable.Transition.ColorTint;
            var colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.7f, 0.78f, 0.9f, 1f);
            colors.selectedColor = new Color(1f, 0.86f, 0.24f, 1f);
            colors.colorMultiplier = 1f;
            toggle.colors = colors;
            toggle.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    SetSelectedDefinition(definition);
                }
                else if (ReferenceEquals(selectedDefinition, definition))
                {
                    SetSelectedDefinition(null);
                }
            });
        }

        private Image CreateThumbnail(RectTransform parent)
        {
            var item = new GameObject(
                "Thumbnail",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            var rect = (RectTransform)item.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(8f, 0f);
            rect.sizeDelta = new Vector2(42f, 42f);
            var image = item.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private TMP_Text CreateLabel(RectTransform parent, string text)
        {
            var item = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            var rect = (RectTransform)item.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(60f, 0f);
            rect.offsetMax = new Vector2(-8f, 0f);
            var label = item.GetComponent<TextMeshProUGUI>();
            if (labelFont != null)
            {
                label.font = labelFont;
                label.fontSharedMaterial = labelFont.material;
            }

            label.fontSize = 16f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.text = string.IsNullOrWhiteSpace(text)
                ? "Unnamed Entity"
                : text;
            label.raycastTarget = false;
            return label;
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

        private void RefreshDetails()
        {
            var definition = selectedDefinition;
            if (detailsName != null)
            {
                detailsName.text = definition?.DisplayName ?? string.Empty;
            }

            if (detailsThumbnail != null)
            {
                detailsThumbnail.sprite = definition?.Thumbnail;
                detailsThumbnail.enabled = definition?.Thumbnail != null;
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
            var visible = toolbarView == null || toolbarView.IsExpanded;
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
