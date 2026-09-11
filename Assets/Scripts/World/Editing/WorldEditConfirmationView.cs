using System;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldEditConfirmationView : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button executeButton;

        private bool isPending;
        private Canvas canvas;

        public event Action CancelRequested;
        public event Action ExecuteRequested;

        private void Awake()
        {
            canvas = panel != null ? panel.GetComponentInParent<Canvas>() : null;
            SetPending(false);
        }
        private void OnEnable()
        {
            cancelButton?.onClick.AddListener(RequestCancel);
            executeButton?.onClick.AddListener(RequestExecute);
        }

        private void OnDisable()
        {
            cancelButton?.onClick.RemoveListener(RequestCancel);
            executeButton?.onClick.RemoveListener(RequestExecute);
        }

        public void SetPending(bool pending, bool executable = false)
        {
            isPending = pending;
            if (cancelButton != null) cancelButton.interactable = pending;
            SetExecutable(executable);
        }

        public void SetExecutable(bool executable)
        {
            if (executeButton != null)
                executeButton.interactable = isPending && executable;
        }
        public bool ContainsScreenPoint(Vector2 screenPosition)
        {
            if (panel == null || !panel.gameObject.activeInHierarchy)
            {
                return false;
            }

            var camera = canvas != null
                && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(
                panel,
                screenPosition,
                camera);
        }

        private void RequestCancel() => CancelRequested?.Invoke();

        private void RequestExecute() => ExecuteRequested?.Invoke();
    }
}

