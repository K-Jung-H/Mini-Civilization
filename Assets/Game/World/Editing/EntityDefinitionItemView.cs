using System;
using MiniCivilization.World.Definitions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class EntityDefinitionItemView : MonoBehaviour
    {
        [SerializeField] private Toggle selectionToggle;
        [SerializeField] private Image thumbnailImage;
        [SerializeField] private TMP_Text nameLabel;

        private EntityDefinition definition;
        private Action<EntityDefinition, bool> selectionChanged;
        private UnityEngine.Events.UnityAction<bool> selectionListener;

        public EntityDefinition Definition => definition;

        public void SetSelectedWithoutNotify(bool selected)
        {
            selectionToggle?.SetIsOnWithoutNotify(selected);
            ToggleGroupVisualStyle.RefreshFor(selectionToggle);
        }

        private void OnDestroy()
        {
            Clear();
        }

        public void Bind(
            EntityDefinition nextDefinition,
            ToggleGroup toggleGroup,
            Action<EntityDefinition, bool> onSelectionChanged)
        {
            if (nextDefinition == null)
            {
                throw new ArgumentNullException(nameof(nextDefinition));
            }

            Clear();
            definition = nextDefinition;
            selectionChanged = onSelectionChanged;
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
                ToggleGroupVisualStyle.RefreshFor(selectionToggle);
                selectionListener = isOn =>
                {
                    selectionChanged?.Invoke(definition, isOn);
                };
                selectionToggle.onValueChanged.AddListener(selectionListener);
            }
        }

        public void Clear()
        {
            if (selectionToggle != null && selectionListener != null)
            {
                selectionToggle.onValueChanged.RemoveListener(selectionListener);
            }

            definition = null;
            selectionChanged = null;
            selectionListener = null;
        }
    }
}
