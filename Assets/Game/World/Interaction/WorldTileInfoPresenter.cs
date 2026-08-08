using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    public sealed class WorldTileInfoPresenter : MonoBehaviour
    {
        [Header("Data")]
        private WorldManager worldManager;
        private WorldTileSelectionState selectionState;
        private WorldCellInfoProvider infoProvider;

        [Header("View")]
        private WorldTileInfoPanel infoPanel;

        private void OnEnable()
        {
            if (selectionState != null)
            {
                selectionState.SelectionChanged += OnSelectionChanged;
            }

            if (infoPanel != null)
            {
                infoPanel.CloseRequested += OnCloseRequested;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (selectionState != null)
            {
                selectionState.SelectionChanged -= OnSelectionChanged;
            }

            if (infoPanel != null)
            {
                infoPanel.CloseRequested -= OnCloseRequested;
            }
        }

        public void Configure(
            WorldManager manager,
            WorldTileSelectionState state,
            WorldCellInfoProvider provider,
            WorldTileInfoPanel panel)
        {
            if (isActiveAndEnabled)
            {
                if (selectionState != null)
                {
                    selectionState.SelectionChanged -= OnSelectionChanged;
                }

                if (infoPanel != null)
                {
                    infoPanel.CloseRequested -= OnCloseRequested;
                }
            }

            worldManager = manager;
            selectionState = state;
            infoProvider = provider;
            infoPanel = panel;

            if (isActiveAndEnabled)
            {
                if (selectionState != null)
                {
                    selectionState.SelectionChanged += OnSelectionChanged;
                }

                if (infoPanel != null)
                {
                    infoPanel.CloseRequested += OnCloseRequested;
                }

                Refresh();
            }
        }

        private void OnSelectionChanged(TilePickResult? _)
        {
            Refresh();
        }

        private void OnCloseRequested()
        {
            selectionState?.SetSelected(null);
        }

        private void Refresh()
        {
            var selected = selectionState?.Selected;
            if (!selected.HasValue
                || worldManager == null
                || !worldManager.HasWorld
                || infoProvider == null
                || infoPanel == null)
            {
                infoPanel?.Hide();
                return;
            }

            var snapshot = infoProvider.Create(
                worldManager.CurrentWorldRuntime,
                selected.Value);
            var model = WorldTileInfoViewModel.FromSnapshot(snapshot);
            infoPanel.Show(model);
        }
    }
}
