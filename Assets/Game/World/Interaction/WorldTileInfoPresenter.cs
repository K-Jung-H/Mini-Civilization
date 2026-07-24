using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    public sealed class WorldTileInfoPresenter : MonoBehaviour
    {
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private WorldTileSelectionState selectionState;

        [Header("Runtime Information")]
        [SerializeField, TextArea(6, 14)] private string hoveredCell;
        [SerializeField, TextArea(6, 14)] private string selectedCell;

        public string HoveredCell => hoveredCell;
        public string SelectedCell => selectedCell;

        private void OnEnable()
        {
            if (selectionState == null)
            {
                return;
            }

            selectionState.HoverChanged += OnHoverChanged;
            selectionState.SelectionChanged += OnSelectionChanged;
            OnHoverChanged(selectionState.Hovered);
            OnSelectionChanged(selectionState.Selected);
        }

        private void OnDisable()
        {
            if (selectionState == null)
            {
                return;
            }

            selectionState.HoverChanged -= OnHoverChanged;
            selectionState.SelectionChanged -= OnSelectionChanged;
        }

        public void Configure(
            WorldManager manager,
            WorldTileSelectionState state)
        {
            worldManager = manager;
            selectionState = state;
        }

        private void OnHoverChanged(TilePickResult? pick)
        {
            hoveredCell = BuildText(pick);
        }

        private void OnSelectionChanged(TilePickResult? pick)
        {
            selectedCell = BuildText(pick);
            if (!string.IsNullOrEmpty(selectedCell))
            {
                Debug.Log(selectedCell, this);
            }
        }

        private string BuildText(TilePickResult? pick)
        {
            if (!pick.HasValue || worldManager == null || !worldManager.HasWorld)
            {
                return string.Empty;
            }

            return WorldCellInfoProvider.Create(
                worldManager.CurrentWorld,
                pick.Value).ToString();
        }
    }
}
