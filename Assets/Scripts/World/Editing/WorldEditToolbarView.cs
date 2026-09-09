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
        public const int CurrentLayoutVersion = 12;

        [SerializeField, HideInInspector] private int layoutVersion;

        [Header("Panels")]
        [SerializeField] private RectTransform toolbarPanel;
        [SerializeField] private RectTransform expandedContent;

        [Header("Main")]
        [SerializeField] private Button mainButton;
        [SerializeField] private TMP_Text mainButtonLabel;
        [SerializeField] private TMP_FontAsset labelFont;
        [SerializeField] private bool startExpanded;

        [Header("Group Expansion")]
        [SerializeField] private WorldEditToolbarGroup selectModeGroup;
        [SerializeField] private WorldEditToolbarGroup terrainGroup;
        [SerializeField] private WorldEditToolbarGroup entityGroup;
        [SerializeField]
        private WorldEditToolbarGroup environmentEditGroup;
        [SerializeField] private RectTransform[] dividers;

        [Header("History")]
        [SerializeField] private RectTransform historyPanel;
        [SerializeField] private Button undoButton;
        [SerializeField] private Button redoButton;

        [Header("Edit Mode")]
        [SerializeField] private ToggleGroup modeToggleGroup;
        [SerializeField] private Toggle singleSelectionToggle;
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
        public event Action StructureChanged;
        public event Action<bool> ExpandedChanged;
        public event Action<bool> EntityGroupExpandedChanged;
        public event Action<WorldEditAction> EditActionSelected;
        public event Action PropertyCategorySelected;
        public event Action UndoRequested;
        public event Action RedoRequested;

        public bool IsExpanded => isExpanded;
        public bool IsEntityGroupExpanded =>
            isExpanded && entityGroup != null && entityGroup.IsExpanded;
        public int LayoutVersion => layoutVersion;
        public TMP_FontAsset LabelFont => labelFont;
        public RectTransform ToolbarPanel => toolbarPanel;
        public RectTransform ExpandedContent => expandedContent;
        public Button MainButton => mainButton;
        public RectTransform HistoryPanel => historyPanel;
        public Button UndoButton => undoButton;
        public Button RedoButton => redoButton;
        public ToggleGroup ModeToggleGroup => modeToggleGroup;
        public Toggle SingleSelectionToggle => singleSelectionToggle;
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
            selectModeGroup?.Initialize();
            terrainGroup?.Initialize();
            entityGroup?.Initialize();
            environmentEditGroup?.Initialize();
            NormalizeActionGroupExpansion();
            BindEvents();
            EnsureSelectionModeSelected();
            SetExpanded(startExpanded);
        }

        private void OnDisable()
        {
            UnbindEvents();
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

            if (mainButtonLabel != null)
            {
                mainButtonLabel.text = expanded
                    ? "\uD3B8\uC9D1\n\u25C0"
                    : "\uD3B8\uC9D1\n\u25B6";
            }

            RefreshGroupLayout();
            RefreshPropertyDetailPanels();
            SelectionChanged?.Invoke();
            if (changed)
            {
                ExpandedChanged?.Invoke(isExpanded);
            }
        }

        public int GetSelectedModeIndex()
        {
            if (!isExpanded || selectModeGroup == null)
            {
                return 0;
            }

            EnsureSelectionModeSelected();
            if (singleSelectionToggle != null && singleSelectionToggle.isOn)
            {
                return 1;
            }

            if (areaSelectionToggle != null && areaSelectionToggle.isOn)
            {
                return 2;
            }

            return brushToggle != null && brushToggle.isOn ? 3 : 0;
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
            if (!isExpanded
                || propertySections == null)
            {
                return false;
            }

            for (var section = 0; section < propertySections.Length; section++)
            {
                var propertySection = propertySections[section];
                if (propertySection?.CategoryToggle == null
                    || !propertySection.CategoryToggle.isOn
                    || !IsSectionGroupExpanded(section))
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

        public bool TryGetSelectedEditAction(out WorldEditAction action)
        {
            action = default;
            return TryGetSelectedProperty(
                    out var sectionIndex,
                    out var detailIndex)
                && TryCreateEditAction(
                    sectionIndex,
                    detailIndex,
                    out action);
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
            ToggleGroupVisualStyle.RefreshFor(toggle);
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
                        ToggleGroupVisualStyle.RefreshFor(toggle);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                SelectionChanged?.Invoke();
            }
        }

        public void SetBuildingDefinitionSelected(bool selected)
        {
            if (selected)
            {
                SelectSingleMode();
            }

            if (areaSelectionToggle != null)
            {
                areaSelectionToggle.interactable = !selected;
            }

            if (brushToggle != null)
            {
                brushToggle.interactable = !selected;
            }

            EnsureSelectionModeSelected();
            RefreshBrushSizePanel();
            SelectionChanged?.Invoke();
        }

        public void EnsureSelectModeGroupExpanded()
        {
            if (!isExpanded
                || selectModeGroup == null
                || !selectModeGroup.SetExpanded(true))
            {
                return;
            }

            RefreshGroupLayout();
            RefreshPropertyDetailPanels();
            SelectionChanged?.Invoke();
            StructureChanged?.Invoke();
        }

        private void ToggleExpanded()
        {
            SetExpanded(!isExpanded);
            StructureChanged?.Invoke();
        }

        private void ToggleSelectModeGroup()
        {
            ToggleIndependentGroup(selectModeGroup);
        }

        private void ToggleEntityGroup()
        {
            ToggleExclusiveActionGroup(entityGroup);
        }

        private void ToggleTerrainGroup()
        {
            ToggleExclusiveActionGroup(terrainGroup);
        }

        private void ToggleEnvironmentGroup()
        {
            ToggleExclusiveActionGroup(environmentEditGroup);
        }

        private void ToggleIndependentGroup(WorldEditToolbarGroup group)
        {
            if (!isExpanded || group == null)
            {
                return;
            }

            group.Toggle();
            RefreshToolbarStructure();
        }

        private void ToggleExclusiveActionGroup(
            WorldEditToolbarGroup group)
        {
            if (!isExpanded || group == null)
            {
                return;
            }

            var entityGroupWasExpanded = IsEntityGroupExpanded;
            var expand = !group.IsExpanded;
            group.SetExpanded(expand);
            if (expand)
            {
                CollapseOtherActionGroups(group);
            }

            RefreshToolbarStructure();
            if (entityGroupWasExpanded != IsEntityGroupExpanded)
            {
                EntityGroupExpandedChanged?.Invoke(IsEntityGroupExpanded);
            }
        }

        private void RefreshToolbarStructure()
        {
            RefreshGroupLayout();
            RefreshPropertyDetailPanels();
            SelectionChanged?.Invoke();
            StructureChanged?.Invoke();
        }

        private void NormalizeActionGroupExpansion()
        {
            WorldEditToolbarGroup expandedGroup = null;
            if (environmentEditGroup != null && environmentEditGroup.IsExpanded)
            {
                expandedGroup = environmentEditGroup;
            }
            else if (entityGroup != null && entityGroup.IsExpanded)
            {
                expandedGroup = entityGroup;
            }
            else if (terrainGroup != null && terrainGroup.IsExpanded)
            {
                expandedGroup = terrainGroup;
            }

            if (expandedGroup != null)
            {
                CollapseOtherActionGroups(expandedGroup);
            }
        }

        private void CollapseOtherActionGroups(
            WorldEditToolbarGroup expandedGroup)
        {
            if (terrainGroup != null && terrainGroup != expandedGroup)
            {
                terrainGroup.SetExpanded(false);
            }

            if (entityGroup != null && entityGroup != expandedGroup)
            {
                entityGroup.SetExpanded(false);
            }

            if (environmentEditGroup != null && environmentEditGroup != expandedGroup)
            {
                environmentEditGroup.SetExpanded(false);
            }
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

            BindGroupButton(selectModeGroup, ToggleSelectModeGroup);
            BindGroupButton(terrainGroup, ToggleTerrainGroup);
            BindGroupButton(entityGroup, ToggleEntityGroup);
            BindGroupButton(environmentEditGroup, ToggleEnvironmentGroup);

            BindToggle(singleSelectionToggle);
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

            UnbindGroupButton(selectModeGroup, ToggleSelectModeGroup);
            UnbindGroupButton(terrainGroup, ToggleTerrainGroup);
            UnbindGroupButton(entityGroup, ToggleEntityGroup);
            UnbindGroupButton(environmentEditGroup, ToggleEnvironmentGroup);

            UnbindToggle(singleSelectionToggle);
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

        private void OnPropertySelectionChanged(bool isOn)
        {
            RefreshPropertyDetailPanels();
            SelectionChanged?.Invoke();
            if (!isOn)
            {
                return;
            }

            PropertyCategorySelected?.Invoke();
            if (TryGetSelectedEditAction(out var action))
            {
                EditActionSelected?.Invoke(action);
            }
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
            EnsureSelectionModeSelected();
            RefreshBrushSizePanel();
            SelectionChanged?.Invoke();
        }

        private void EnsureSelectionModeSelected()
        {
            if (singleSelectionToggle != null && singleSelectionToggle.isOn
                || areaSelectionToggle != null && areaSelectionToggle.isOn
                || brushToggle != null && brushToggle.isOn)
            {
                return;
            }

            if (areaSelectionToggle != null
                && areaSelectionToggle.interactable)
            {
                areaSelectionToggle.SetIsOnWithoutNotify(true);
                ToggleGroupVisualStyle.RefreshFor(areaSelectionToggle);
                return;
            }

            SelectSingleMode();
        }

        private void SelectSingleMode()
        {
            if (singleSelectionToggle != null)
            {
                singleSelectionToggle.SetIsOnWithoutNotify(true);
                ToggleGroupVisualStyle.RefreshFor(singleSelectionToggle);
            }
        }

        private static void BindGroupButton(
            WorldEditToolbarGroup group,
            UnityAction action)
        {
            var button = group?.ExpandButton;
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }

        private static void UnbindGroupButton(
            WorldEditToolbarGroup group,
            UnityAction action)
        {
            group?.ExpandButton?.onClick.RemoveListener(action);
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
            if (!isOn)
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

            EditActionSelected?.Invoke(action);
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
                    && IsSectionGroupExpanded(
                        Array.IndexOf(propertySections, section))
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
                    && selectModeGroup != null
                    && selectModeGroup.IsExpanded
                    && brushToggle != null
                    && brushToggle.isOn);
            }
        }

        private void RefreshGroupLayout()
        {
            if (expandedContent == null || toolbarPanel == null)
            {
                return;
            }

            selectModeGroup?.RefreshVisibility(isExpanded);
            terrainGroup?.RefreshVisibility(isExpanded);
            entityGroup?.RefreshVisibility(isExpanded);
            environmentEditGroup?.RefreshVisibility(isExpanded);

            if (!isExpanded)
            {
                toolbarPanel.sizeDelta = new Vector2(84f, 84f);
                return;
            }

            var cursor = 8f;
            LayoutGroup(selectModeGroup, ref cursor);
            LayoutDivider(GetDivider(1), ref cursor);
            LayoutGroup(terrainGroup, ref cursor);
            LayoutDivider(GetDivider(2), ref cursor);
            LayoutGroup(entityGroup, ref cursor);
            LayoutDivider(GetDivider(3), ref cursor);
            LayoutGroup(environmentEditGroup, ref cursor);

            var expandedWidth = cursor + 8f;
            expandedContent.sizeDelta = new Vector2(expandedWidth, 84f);
            toolbarPanel.sizeDelta = new Vector2(expandedWidth + 84f, 84f);
            LayoutMainDivider();
        }

        private static void LayoutGroup(
            WorldEditToolbarGroup group,
            ref float cursor)
        {
            if (group == null)
            {
                return;
            }

            var buttonRect = group.ExpandButton?.transform as RectTransform;
            if (buttonRect == null)
            {
                return;
            }

            PlaceRight(buttonRect, cursor, 10f);
            cursor += buttonRect.sizeDelta.x;
            if (!group.IsExpanded || group.Content == null)
            {
                return;
            }

            cursor += 8f;
            PlaceInsideGroup(
                group.Content,
                buttonRect.sizeDelta.x + 8f);
            cursor += group.Content.sizeDelta.x;
        }

        private static void LayoutDivider(
            RectTransform divider,
            ref float cursor)
        {
            if (divider == null)
            {
                return;
            }

            cursor += 8f;
            PlaceRight(divider, cursor, 20f);
            cursor += divider.sizeDelta.x + 8f;
        }

        private void LayoutMainDivider()
        {
            var mainSelectDivider = GetDivider(0);
            if (mainSelectDivider == null)
            {
                return;
            }

            mainSelectDivider.anchorMin = new Vector2(1f, 0f);
            mainSelectDivider.anchorMax = new Vector2(1f, 0f);
            mainSelectDivider.pivot = new Vector2(1f, 0f);
            mainSelectDivider.anchoredPosition = new Vector2(0f, 20f);
        }

        private RectTransform GetDivider(int index) =>
            dividers != null && (uint)index < dividers.Length
                ? dividers[index]
                : null;

        private bool IsSectionGroupExpanded(int sectionIndex)
        {
            return sectionIndex switch
            {
                0 or 1 => terrainGroup != null && terrainGroup.IsExpanded,
                2 => environmentEditGroup != null
                    && environmentEditGroup.IsExpanded,
                _ => false
            };
        }

        private static void PlaceRight(
            RectTransform rect,
            float rightOffset,
            float y)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-rightOffset, y);
        }

        private static void PlaceInsideGroup(
            RectTransform content,
            float rightOffset)
        {
            content.anchorMin = new Vector2(1f, 0f);
            content.anchorMax = new Vector2(1f, 0f);
            content.pivot = new Vector2(1f, 0f);
            content.anchoredPosition = new Vector2(-rightOffset, 0f);
        }
    }
}
