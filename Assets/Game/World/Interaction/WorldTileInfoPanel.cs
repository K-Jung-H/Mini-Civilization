using System;
using UnityEngine;
using UnityEngine.UI;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    public sealed class WorldTileInfoPanel : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject panelRoot;

        [Header("Text")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text coordinateText;
        [SerializeField] private Text terrainText;
        [SerializeField] private Text waterText;
        [SerializeField] private Text surfaceText;
        [SerializeField] private Text debugText;

        [Header("Controls")]
        [SerializeField] private Button closeButton;
        [SerializeField] private bool showDebugSection = true;

        private WorldTileInfoViewModel fallbackModel;
        private bool fallbackVisible;

        public event Action CloseRequested;

        private void OnEnable()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(RequestClose);
            }
        }

        private void OnDisable()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(RequestClose);
            }
        }

        public void Show(in WorldTileInfoViewModel model)
        {
            fallbackModel = model;
            fallbackVisible = true;
            SetText(titleText, model.Title);
            SetText(coordinateText, model.Coordinate);
            SetText(terrainText, model.Terrain);
            SetText(waterText, model.Water);
            SetText(surfaceText, model.Surface);
            SetText(debugText, showDebugSection ? model.Debug : string.Empty);
            if (debugText != null)
            {
                debugText.gameObject.SetActive(showDebugSection);
            }

            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }
        }

        public void Hide()
        {
            fallbackVisible = false;
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        public void Configure(
            GameObject root,
            Text title,
            Text coordinate,
            Text terrain,
            Text water,
            Text surface,
            Text debug,
            Button close)
        {
            panelRoot = root;
            titleText = title;
            coordinateText = coordinate;
            terrainText = terrain;
            waterText = water;
            surfaceText = surface;
            debugText = debug;
            closeButton = close;
        }

        private void RequestClose()
        {
            CloseRequested?.Invoke();
        }

        private void OnGUI()
        {
            if (panelRoot != null || !fallbackVisible)
            {
                return;
            }

            var width = Mathf.Min(390f, Screen.width - 32f);
            var height = Mathf.Min(650f, Screen.height - 32f);
            GUILayout.BeginArea(
                new Rect(Screen.width - width - 16f, 16f, width, height),
                GUI.skin.box);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(fallbackModel.Title, GUI.skin.label);
                if (GUILayout.Button("\u00D7", GUILayout.Width(32f)))
                {
                    RequestClose();
                }
            }

            GUILayout.Label(fallbackModel.Coordinate);
            GUILayout.Space(8f);
            GUILayout.Label(fallbackModel.Terrain);
            GUILayout.Space(8f);
            GUILayout.Label(fallbackModel.Water);
            GUILayout.Space(8f);
            GUILayout.Label(fallbackModel.Surface);
            if (showDebugSection)
            {
                GUILayout.Space(8f);
                GUILayout.Label(fallbackModel.Debug);
            }

            GUILayout.EndArea();
        }

        private static void SetText(Text target, string value)
        {
            if (target != null)
            {
                target.text = value ?? string.Empty;
            }
        }
    }
}
