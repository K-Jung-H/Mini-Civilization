using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
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
        public const int CurrentLayoutVersion = 6;

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

        [Header("History")]
        [SerializeField] private RectTransform historyPanel;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button redoButton;

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
        private readonly Dictionary<Toggle, UnityAction<bool>>
            editActionListeners = new();

        public event Action SelectionChanged;
        public event Action<bool> ExpandedChanged;
        public event Action<WorldEditAction> EditActionRequested;
        public event Action UndoRequested;
        public event Action RedoRequested;

        public bool IsExpanded => isExpanded;
        public int LayoutVersion => layoutVersion;
        public TMP_FontAsset LabelFont => labelFont;
        public RectTransform ToolbarPanel => toolbarPanel;
        public RectTransform ExpandedContent => expandedContent;
        public RectTransform PropertyDetailPanel => propertyDetailPanel;
        public Button MainButton => mainButton;
        public RectTransform HistoryPanel => historyPanel;
        public Button UndoButton => undoButton;
        public Button RedoButton => redoButton;
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
            RectTransform history,
            Button undo,
            Button redo,
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
            historyPanel = history;
            undoButton = undo;
            redoButton = redo;
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

        public void SetHistoryAvailability(bool canUndo, bool canRedo)
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

        public void SetExpanded(bool expanded)
        {
            var changed = isExpanded != expanded;
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
            if (changed)
            {
                ExpandedChanged?.Invoke(isExpanded);
            }
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

        public void SetActiveEditAction(WorldEditAction action)
        {
            if (!TryGetActionLocation(
                    action,
                    out var sectionIndex,
                    out var detailIndex))
            {
                return;
            }

            var toggle = propertySections[sectionIndex]
                .DetailToggles[detailIndex];
            if (toggle == null || toggle.isOn)
            {
                return;
            }

            toggle.SetIsOnWithoutNotify(true);
            SelectionChanged?.Invoke();
        }

        public void ClearActiveEditAction()
        {
            var changed = false;
            if (propertySections != null)
            {
                foreach (var section in propertySections)
                {
                    if (section == null)
                    {
                        continue;
                    }

                    foreach (var toggle in section.DetailToggles)
                    {
                        if (toggle == null || !toggle.isOn)
                        {
                            continue;
                        }

                        toggle.SetIsOnWithoutNotify(false);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                SelectionChanged?.Invoke();
            }
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

            if (undoButton != null)
            {
                undoButton.onClick.RemoveListener(RequestUndo);
                undoButton.onClick.AddListener(RequestUndo);
            }

            if (redoButton != null)
            {
                redoButton.onClick.RemoveListener(RequestRedo);
                redoButton.onClick.AddListener(RequestRedo);
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

            for (var sectionIndex = 0;
                 sectionIndex < propertySections.Length;
                 sectionIndex++)
            {
                var section = propertySections[sectionIndex];
                if (section?.CategoryToggle == null)
                {
                    continue;
                }

                section.CategoryToggle.onValueChanged.RemoveListener(
                    OnPropertySelectionChanged);
                section.CategoryToggle.onValueChanged.AddListener(
                    OnPropertySelectionChanged);
                for (var detailIndex = 0;
                     detailIndex < section.DetailToggles.Count;
                     detailIndex++)
                {
                    var detailToggle = section.DetailToggles[detailIndex];
                    BindToggle(detailToggle);
                    BindEditActionToggle(
                        detailToggle,
                        sectionIndex,
                        detailIndex);
                }
            }
        }

        private void UnbindEvents()
        {
            UnbindEditActionToggles();
            if (mainButton != null)
            {
                mainButton.onClick.RemoveListener(ToggleExpanded);
            }

            if (undoButton != null)
            {
                undoButton.onClick.RemoveListener(RequestUndo);
            }

            if (redoButton != null)
            {
                redoButton.onClick.RemoveListener(RequestRedo);
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

        private void RequestUndo()
        {
            UndoRequested?.Invoke();
        }

        private void RequestRedo()
        {
            RedoRequested?.Invoke();
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

        private void BindEditActionToggle(
            Toggle toggle,
            int sectionIndex,
            int detailIndex)
        {
            if (toggle == null || editActionListeners.ContainsKey(toggle))
            {
                return;
            }

            UnityAction<bool> listener = isOn => OnEditActionToggleChanged(
                toggle,
                sectionIndex,
                detailIndex,
                isOn);
            editActionListeners.Add(toggle, listener);
            toggle.onValueChanged.AddListener(listener);
        }

        private void UnbindEditActionToggles()
        {
            foreach (var pair in editActionListeners)
            {
                if (pair.Key != null)
                {
                    pair.Key.onValueChanged.RemoveListener(pair.Value);
                }
            }

            editActionListeners.Clear();
        }

        private void OnEditActionToggleChanged(
            Toggle source,
            int sectionIndex,
            int detailIndex,
            bool isOn)
        {
            if (!isOn
                && HasAnotherActiveDetail(sectionIndex, source))
            {
                return;
            }

            if (!TryCreateEditAction(
                    sectionIndex,
                    detailIndex,
                    out var action))
            {
                return;
            }

            EditActionRequested?.Invoke(action);
        }

        private bool HasAnotherActiveDetail(
            int sectionIndex,
            Toggle source)
        {
            if (propertySections == null
                || (uint)sectionIndex >= propertySections.Length
                || propertySections[sectionIndex] == null)
            {
                return false;
            }

            foreach (var toggle in propertySections[sectionIndex].DetailToggles)
            {
                if (toggle != null && toggle != source && toggle.isOn)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreateEditAction(
            int sectionIndex,
            int detailIndex,
            out WorldEditAction action)
        {
            action = default;
            if (sectionIndex == 0
                && (uint)detailIndex <= (uint)TerrainEditOperation.Remove)
            {
                action = WorldEditAction.Terrain(
                    (TerrainEditOperation)detailIndex);
                return true;
            }

            if (sectionIndex == 1
                && detailIndex >= 0
                && detailIndex < (int)BiomeType.Mountain)
            {
                action = WorldEditAction.SetBiome(
                    (BiomeType)(detailIndex + 1));
                return true;
            }

            return false;
        }

        private bool TryGetActionLocation(
            WorldEditAction action,
            out int sectionIndex,
            out int detailIndex)
        {
            sectionIndex = -1;
            detailIndex = -1;
            switch (action.PropertyGroup)
            {
                case WorldEditPropertyGroup.Terrain:
                    sectionIndex = 0;
                    detailIndex = (int)action.TerrainOperation;
                    break;
                case WorldEditPropertyGroup.Biome
                    when action.Biome > BiomeType.None:
                    sectionIndex = 1;
                    detailIndex = (int)action.Biome - 1;
                    break;
                default:
                    return false;
            }

            return propertySections != null
                && (uint)sectionIndex < propertySections.Length
                && propertySections[sectionIndex] != null
                && (uint)detailIndex
                    < propertySections[sectionIndex].DetailToggles.Count;
        }

        private void RefreshPropertyDetailPanels()
        {
            RefreshBrushSizePanel();
            if (historyPanel != null)
            {
                historyPanel.gameObject.SetActive(isExpanded);
            }

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
