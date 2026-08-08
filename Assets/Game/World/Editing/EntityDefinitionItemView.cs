using System;
using MiniCivilization.World.Definitions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class EntityDefinitionItemView : MonoBehaviour
    {
        [SerializeField] private Toggle selectionToggle;
        [SerializeField] private Image thumbnailImage;
        [SerializeField] private TMP_Text nameLabel;
        [SerializeField] private Button addButton;

        private EntityDefinition definition;
        private Action<EntityDefinition, bool> selectionChanged;
        private Action<EntityDefinition> addRequested;
        private UnityAction<bool> selectionListener;
        private UnityAction addListener;

        public EntityDefinition Definition => definition;

        private void OnDestroy()
        {
            Clear();
        }

        public void Bind(
            EntityDefinition nextDefinition,
            ToggleGroup toggleGroup,
            Action<EntityDefinition, bool> onSelectionChanged,
            Action<EntityDefinition> onAddRequested)
        {
            if (nextDefinition == null)
            {
                throw new ArgumentNullException(nameof(nextDefinition));
            }

            Clear();
            definition = nextDefinition;
            selectionChanged = onSelectionChanged;
            addRequested = onAddRequested;
            gameObject.name = nextDefinition.DisplayName;

            if (thumbnailImage != null)
            {
                thumbnailImage.sprite = nextDefinition.Thumbnail;
                thumbnailImage.enabled = nextDefinition.Thumbnail != null;
            }

            if (nameLabel != null)
            {
                nameLabel.text = nextDefinition.DisplayName;
            }

            if (selectionToggle != null)
            {
                selectionToggle.group = toggleGroup;
                selectionToggle.SetIsOnWithoutNotify(false);
                selectionListener = isOn =>
                    selectionChanged?.Invoke(definition, isOn);
                selectionToggle.onValueChanged.AddListener(selectionListener);
            }

            if (addButton != null)
            {
                addListener = () => addRequested?.Invoke(definition);
                addButton.onClick.AddListener(addListener);
            }
        }

        public void Clear()
        {
            if (selectionToggle != null && selectionListener != null)
            {
                selectionToggle.onValueChanged.RemoveListener(selectionListener);
            }

            if (addButton != null && addListener != null)
            {
                addButton.onClick.RemoveListener(addListener);
            }

            definition = null;
            selectionChanged = null;
            addRequested = null;
            selectionListener = null;
            addListener = null;
        }

        public void SetAddInteractable(bool interactable)
        {
            if (addButton != null)
            {
                addButton.interactable = interactable;
            }
        }
    }
}
