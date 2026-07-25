using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [Serializable]
    public sealed class WorldEditPropertySection
    {
        [SerializeField] private Toggle categoryToggle;
        [SerializeField] private RectTransform detailPanel;
        [SerializeField] private ToggleGroup detailToggleGroup;
        [SerializeField] private Toggle[] detailToggles;

        public Toggle CategoryToggle => categoryToggle;
        public RectTransform DetailPanel => detailPanel;
        public ToggleGroup DetailToggleGroup => detailToggleGroup;
        public IReadOnlyList<Toggle> DetailToggles => detailToggles;

        public WorldEditPropertySection(
            Toggle category,
            RectTransform panel,
            ToggleGroup group,
            Toggle[] toggles)
        {
            categoryToggle = category;
            detailPanel = panel;
            detailToggleGroup = group;
            detailToggles = toggles;
        }
    }

    [DisallowMultipleComponent]
    public sealed class WorldEditToolbarView : MonoBehaviour
    {
        public const int CurrentLayoutVersion = 3;

        [SerializeField, HideInInspector] private int layoutVersion;

        [Header("Panels")]
        [SerializeField] private RectTransform toolbarPanel;
        [SerializeField] private RectTransform expandedContent;
        [SerializeField] private RectTransform propertyDetailPanel;

        [Header("Main")]
        [SerializeField] private Button mainButton;
        [SerializeField] private TMP_Text mainButtonLabel;
        [SerializeField] private TMP_FontAsset labelFont;
        [SerializeField] private bool startExpanded;

        [Header("Edit Mode")]
        [SerializeField] private ToggleGroup modeToggleGroup;
        [SerializeField] private Toggle areaSelectionToggle;
        [SerializeField] private Toggle brushToggle;

        [Header("Property Category")]
        [SerializeField] private ToggleGroup propertyToggleGroup;
        [SerializeField] private WorldEditPropertySection[] propertySections;

        private bool isExpanded;

        public bool IsExpanded => isExpanded;
        public int LayoutVersion => layoutVersion;
        public TMP_FontAsset LabelFont => labelFont;
        public RectTransform ToolbarPanel => toolbarPanel;
        public RectTransform ExpandedContent => expandedContent;
        public RectTransform PropertyDetailPanel => propertyDetailPanel;
        public Button MainButton => mainButton;
        public ToggleGroup ModeToggleGroup => modeToggleGroup;
        public Toggle AreaSelectionToggle => areaSelectionToggle;
        public Toggle BrushToggle => brushToggle;
        public ToggleGroup PropertyToggleGroup => propertyToggleGroup;
        public IReadOnlyList<WorldEditPropertySection> PropertySections =>
            propertySections;

        private void OnEnable()
        {
            BindEvents();
            SetExpanded(startExpanded);
        }

        private void OnDisable()
        {
            UnbindEvents();
        }

        public void Configure(
            RectTransform toolbar,
            RectTransform content,
            RectTransform detailPanel,
            Button menu,
            TMP_Text menuLabel,
            TMP_FontAsset font,
            ToggleGroup modeGroup,
            Toggle areaSelection,
            Toggle brush,
            ToggleGroup propertyGroup,
            WorldEditPropertySection[] sections)
        {
            UnbindEvents();
            toolbarPanel = toolbar;
            expandedContent = content;
            propertyDetailPanel = detailPanel;
            mainButton = menu;
            mainButtonLabel = menuLabel;
            labelFont = font;
            modeToggleGroup = modeGroup;
            areaSelectionToggle = areaSelection;
            brushToggle = brush;
            propertyToggleGroup = propertyGroup;
            propertySections = sections;
            layoutVersion = CurrentLayoutVersion;

            if (isActiveAndEnabled)
            {
                BindEvents();
                SetExpanded(startExpanded);
            }
        }

        public void SetExpanded(bool expanded)
        {
            isExpanded = expanded;
            if (expandedContent != null)
            {
                expandedContent.gameObject.SetActive(expanded);
            }

            if (propertyDetailPanel != null)
            {
                propertyDetailPanel.gameObject.SetActive(expanded);
            }

            if (mainButtonLabel != null)
            {
                mainButtonLabel.text = expanded ? "편집\n◀" : "편집\n▶";
            }

            RefreshPropertyDetailPanels();
        }

        private void ToggleExpanded()
        {
            SetExpanded(!isExpanded);
        }

        private void BindEvents()
        {
            if (mainButton != null)
            {
                mainButton.onClick.RemoveListener(ToggleExpanded);
                mainButton.onClick.AddListener(ToggleExpanded);
            }

            if (propertySections == null)
            {
                return;
            }

            foreach (var section in propertySections)
            {
                if (section?.CategoryToggle == null)
                {
                    continue;
                }

                section.CategoryToggle.onValueChanged.RemoveListener(
                    OnPropertySelectionChanged);
                section.CategoryToggle.onValueChanged.AddListener(
                    OnPropertySelectionChanged);
            }
        }

        private void UnbindEvents()
        {
            if (mainButton != null)
            {
                mainButton.onClick.RemoveListener(ToggleExpanded);
            }

            if (propertySections == null)
            {
                return;
            }

            foreach (var section in propertySections)
            {
                if (section?.CategoryToggle != null)
                {
                    section.CategoryToggle.onValueChanged.RemoveListener(
                        OnPropertySelectionChanged);
                }
            }
        }

        private void OnPropertySelectionChanged(bool _)
        {
            RefreshPropertyDetailPanels();
        }

        private void RefreshPropertyDetailPanels()
        {
            if (propertySections == null)
            {
                return;
            }

            foreach (var section in propertySections)
            {
                if (section?.DetailPanel == null)
                {
                    continue;
                }

                var selected = isExpanded
                    && section.CategoryToggle != null
                    && section.CategoryToggle.isOn;
                section.DetailPanel.gameObject.SetActive(selected);
            }
        }
    }
}
