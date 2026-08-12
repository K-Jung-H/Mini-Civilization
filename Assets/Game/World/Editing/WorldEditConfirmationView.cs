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

        private RectTransform canvasRect;
        private Canvas canvas;

        public event Action CancelRequested;
        public event Action ExecuteRequested;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>();
            canvasRect = canvas != null
                ? canvas.transform as RectTransform
                : null;
            Hide();
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

        public void Show(Vector2 screenPosition, bool executable)
        {
            if (panel == null)
            {
                return;
            }

            panel.gameObject.SetActive(true);
            SetExecutable(executable);
            PositionAt(screenPosition);
        }

        public void Hide()
        {
            if (panel != null)
            {
                panel.gameObject.SetActive(false);
            }
        }

        public void SetExecutable(bool executable)
        {
            if (executeButton != null)
            {
                executeButton.interactable = executable;
            }
        }

        private void PositionAt(Vector2 screenPosition)
        {
            if (panel == null || canvasRect == null)
            {
                return;
            }

            var camera = canvas != null
                && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    screenPosition,
                    camera,
                    out var localPoint))
            {
                return;
            }

            var canvasBounds = canvasRect.rect;
            var halfSize = panel.rect.size * 0.5f;
            localPoint.x = Mathf.Clamp(
                localPoint.x,
                canvasBounds.xMin + halfSize.x,
                canvasBounds.xMax - halfSize.x);
            localPoint.y = Mathf.Clamp(
                localPoint.y,
                canvasBounds.yMin + halfSize.y,
                canvasBounds.yMax - halfSize.y);
            panel.anchoredPosition = localPoint;
        }

        private void RequestCancel() => CancelRequested?.Invoke();

        private void RequestExecute() => ExecuteRequested?.Invoke();
    }
}
