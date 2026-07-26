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
        public const int CurrentLayoutVersion = 4;

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

        [Header("Brush Size")]
        [SerializeField] private RectTransform brushSizePanel;
        [SerializeField] private ToggleGroup brushSizeToggleGroup;
        [SerializeField] private Toggle[] brushSizeToggles;

        [Header("Property Category")]
        [SerializeField] private ToggleGroup propertyToggleGroup;
        [SerializeField] private WorldEditPropertySection[] propertySections;

        private bool isExpanded;

        public event Action SelectionChanged;

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
        public RectTransform BrushSizePanel => brushSizePanel;
        public ToggleGroup BrushSizeToggleGroup => brushSizeToggleGroup;
        public IReadOnlyList<Toggle> BrushSizeToggles => brushSizeToggles;
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
            RectTransform sizePanel,
            ToggleGroup sizeGroup,
            Toggle[] sizeToggles,
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
            brushSizePanel = sizePanel;
            brushSizeToggleGroup = sizeGroup;
            brushSizeToggles = sizeToggles;
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
            SelectionChanged?.Invoke();
        }

        public int GetSelectedModeIndex()
        {
            if (!isExpanded)
            {
                return 0;
            }

            if (areaSelectionToggle != null && areaSelectionToggle.isOn)
            {
                return 1;
            }

            return brushToggle != null && brushToggle.isOn ? 2 : 0;
        }

        public int GetSelectedBrushSize()
        {
            if (brushSizeToggles == null)
            {
                return 1;
            }

            for (var index = 0; index < brushSizeToggles.Length; index++)
            {
                if (brushSizeToggles[index] != null
                    && brushSizeToggles[index].isOn)
                {
                    return index + 1;
                }
            }

            return 1;
        }

        public bool TryGetSelectedProperty(
            out int sectionIndex,
            out int detailIndex)
        {
            sectionIndex = -1;
            detailIndex = -1;
            if (!isExpanded || propertySections == null)
            {
                return false;
            }

            for (var section = 0; section < propertySections.Length; section++)
            {
                var propertySection = propertySections[section];
                if (propertySection?.CategoryToggle == null
                    || !propertySection.CategoryToggle.isOn)
                {
                    continue;
                }

                sectionIndex = section;
                var toggles = propertySection.DetailToggles;
                for (var detail = 0; detail < toggles.Count; detail++)
                {
                    if (toggles[detail] != null && toggles[detail].isOn)
                    {
                        detailIndex = detail;
                        return true;
                    }
                }

                return false;
            }

            return false;
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

            BindToggle(areaSelectionToggle);
            BindToggle(brushToggle);
            if (brushSizeToggles != null)
            {
                foreach (var sizeToggle in brushSizeToggles)
                {
                    BindToggle(sizeToggle);
                }
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
                foreach (var detailToggle in section.DetailToggles)
                {
                    BindToggle(detailToggle);
                }
            }
        }

        private void UnbindEvents()
        {
            if (mainButton != null)
            {
                mainButton.onClick.RemoveListener(ToggleExpanded);
            }

            UnbindToggle(areaSelectionToggle);
            UnbindToggle(brushToggle);
            if (brushSizeToggles != null)
            {
                foreach (var sizeToggle in brushSizeToggles)
                {
                    UnbindToggle(sizeToggle);
                }
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

                if (section == null)
                {
                    continue;
                }

                foreach (var detailToggle in section.DetailToggles)
                {
                    UnbindToggle(detailToggle);
                }
            }
        }

        private void OnPropertySelectionChanged(bool _)
        {
            RefreshPropertyDetailPanels();
            SelectionChanged?.Invoke();
        }

        private void OnSelectionChanged(bool _)
        {
            RefreshBrushSizePanel();
            SelectionChanged?.Invoke();
        }

        private void BindToggle(Toggle toggle)
        {
            if (toggle == null)
            {
                return;
            }

            toggle.onValueChanged.RemoveListener(OnSelectionChanged);
            toggle.onValueChanged.AddListener(OnSelectionChanged);
        }

        private void UnbindToggle(Toggle toggle)
        {
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveListener(OnSelectionChanged);
            }
        }

        private void RefreshPropertyDetailPanels()
        {
            RefreshBrushSizePanel();
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

        private void RefreshBrushSizePanel()
        {
            if (brushSizePanel != null)
            {
                brushSizePanel.gameObject.SetActive(
                    isExpanded
                    && brushToggle != null
                    && brushToggle.isOn);
            }
        }
    }
}
