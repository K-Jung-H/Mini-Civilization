using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldEditActionItemView : MonoBehaviour
    {
        [SerializeField] private Toggle selectionToggle;
        [SerializeField] private Image thumbnailImage;
        [SerializeField] private TMP_Text label;

        private WorldEditAction action;
        private Action<WorldEditAction, bool> selectionChanged;
        private UnityAction<bool> selectionListener;

        private void Awake()
        {
            ResolveComponents();
        }

        private void ResolveComponents()
        {
            selectionToggle ??= GetComponent<Toggle>();
            label ??= GetComponentInChildren<TMP_Text>(true);
        }

        private void OnDestroy()
        {
            ResolveComponents();
            Clear();
        }

        public void Bind(
            WorldEditAction nextAction,
            string displayName,
            Sprite thumbnail,
            ToggleGroup toggleGroup,
            Action<WorldEditAction, bool> onSelectionChanged)
        {
            Clear();
            action = nextAction;
            selectionChanged = onSelectionChanged;
            gameObject.name = displayName;

            if (label != null)
            {
                label.text = displayName;
            }

            if (thumbnailImage != null)
            {
                thumbnailImage.sprite = thumbnail;
                thumbnailImage.gameObject.SetActive(thumbnail != null);
            }

            if (selectionToggle != null)
            {
                selectionToggle.group = toggleGroup;
                selectionToggle.SetIsOnWithoutNotify(false);
                ToggleGroupVisualStyle.RefreshFor(selectionToggle);
                selectionListener = isOn => selectionChanged?.Invoke(
                    action,
                    isOn);
                selectionToggle.onValueChanged.AddListener(selectionListener);
            }
        }

        public void SetSelectedWithoutNotify(bool selected)
        {
            ResolveComponents();
            selectionToggle?.SetIsOnWithoutNotify(selected);
            ToggleGroupVisualStyle.RefreshFor(selectionToggle);
        }

        public void Clear()
        {
            ResolveComponents();
            if (selectionToggle != null && selectionListener != null)
            {
                selectionToggle.onValueChanged.RemoveListener(selectionListener);
            }

            selectionChanged = null;
            selectionListener = null;
            if (thumbnailImage != null)
            {
                thumbnailImage.sprite = null;
                thumbnailImage.gameObject.SetActive(false);
            }
        }
    }
}
