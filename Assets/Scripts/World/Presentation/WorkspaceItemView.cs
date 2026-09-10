using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Presentation
{
    public sealed class WorkspaceItemView : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image thumbnail;
        [SerializeField] private TMP_Text fallback;

        public Button Bind(string title, Sprite icon, Action action)
        {
            label.text = title;
            thumbnail.sprite = icon;
            thumbnail.enabled = icon != null;
            fallback.gameObject.SetActive(icon == null);
            fallback.text = title switch
            {
                "Raise" => "+Y",
                "Lower" => "-Y",
                "Add" => "+",
                "Remove" or "Remove Road" => "-",
                _ => string.IsNullOrEmpty(title) ? "?" : title.Substring(0, 1)
            };
            button.onClick.RemoveAllListeners();
            button.interactable = action != null;
            if (action != null) button.onClick.AddListener(() => action());
            return button;
        }
    }
}

