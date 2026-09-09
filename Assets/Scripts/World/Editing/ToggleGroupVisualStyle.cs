using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ToggleGroup))]
    public sealed class ToggleGroupVisualStyle : MonoBehaviour
    {
        [Header("Selection Policy")]
        [SerializeField] private bool radioButtonMode;

        [Header("Color Tint")]
        [SerializeField] private ColorBlock unselectedColors = new()
        {
            normalColor = new Color(0.706f, 0.706f, 0.706f, 1f),
            highlightedColor = new Color(0.8f, 0.8f, 0.8f, 1f),
            pressedColor = new Color(0.55f, 0.55f, 0.55f, 1f),
            selectedColor = new Color(0.706f, 0.706f, 0.706f, 1f),
            disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.7f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        [SerializeField] private ColorBlock selectedColors = new()
        {
            normalColor = new Color(1f, 0.5f, 0f, 1f),
            highlightedColor = new Color(1f, 0.62f, 0.15f, 1f),
            pressedColor = new Color(0.8f, 0.35f, 0f, 1f),
            selectedColor = new Color(1f, 0.5f, 0f, 1f),
            disabledColor = new Color(0.45f, 0.28f, 0.1f, 0.7f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };

        private readonly Dictionary<Toggle, UnityAction<bool>> listeners = new();
        private readonly List<Toggle> removedToggles = new();
        private ToggleGroup toggleGroup;

        private void Awake()
        {
            toggleGroup = GetComponent<ToggleGroup>();
        }

        private void OnEnable()
        {
            RefreshVisuals();
        }

        private void OnDisable()
        {
            UnbindToggles();
        }

        private void OnTransformChildrenChanged()
        {
            if (isActiveAndEnabled)
            {
                RefreshVisuals();
            }
        }

        public void RefreshVisuals()
        {
            toggleGroup ??= GetComponent<ToggleGroup>();
            if (toggleGroup == null)
            {
                return;
            }

            toggleGroup.allowSwitchOff = !radioButtonMode;
            var currentToggles = new HashSet<Toggle>();
            var childToggles = GetComponentsInChildren<Toggle>(true);
            foreach (var toggle in childToggles)
            {
                if (toggle == null || toggle.group != toggleGroup)
                {
                    continue;
                }

                currentToggles.Add(toggle);
                Bind(toggle);
                ApplyVisual(toggle);
            }

            removedToggles.Clear();
            foreach (var pair in listeners)
            {
                if (!currentToggles.Contains(pair.Key))
                {
                    removedToggles.Add(pair.Key);
                }
            }

            foreach (var toggle in removedToggles)
            {
                if (toggle != null)
                {
                    toggle.onValueChanged.RemoveListener(listeners[toggle]);
                }

                listeners.Remove(toggle);
            }
        }

        public static void RefreshFor(Toggle toggle)
        {
            if (toggle == null)
            {
                return;
            }

            var style = toggle.GetComponentInParent<ToggleGroupVisualStyle>(
                true);
            style?.RefreshVisuals();
        }

        private void Bind(Toggle toggle)
        {
            if (listeners.ContainsKey(toggle))
            {
                return;
            }

            UnityAction<bool> listener = _ => RefreshVisuals();
            listeners.Add(toggle, listener);
            toggle.onValueChanged.AddListener(listener);
        }

        private void UnbindToggles()
        {
            foreach (var pair in listeners)
            {
                if (pair.Key != null)
                {
                    pair.Key.onValueChanged.RemoveListener(pair.Value);
                }
            }

            listeners.Clear();
            removedToggles.Clear();
        }

        private void ApplyVisual(Toggle toggle)
        {
            var colors = toggle.isOn ? selectedColors : unselectedColors;
            toggle.colors = colors;

            var graphic = toggle.targetGraphic;
            if (graphic == null || !graphic.gameObject.activeInHierarchy)
            {
                return;
            }

            graphic.CrossFadeColor(
                toggle.interactable ? colors.normalColor : colors.disabledColor,
                0f,
                true,
                true);
        }
    }
}
