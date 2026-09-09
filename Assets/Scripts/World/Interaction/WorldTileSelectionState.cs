using System;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    public sealed class WorldTileSelectionState : MonoBehaviour
    {
        public TilePickResult? Hovered { get; private set; }
        public TilePickResult? Selected { get; private set; }
        public IWorldCellSelection EditHovered { get; private set; }
        public IWorldCellSelection EditSelected { get; private set; }
        public IWorldCellSelection EditPrimaryPreview { get; private set; }
        public IWorldCellSelection EditSecondaryPreview { get; private set; }
        public IWorldCellSelection EditInvalidPreview { get; private set; }

        public event Action<TilePickResult?> HoverChanged;
        public event Action<TilePickResult?> SelectionChanged;
        public event Action<IWorldCellSelection> EditHoverChanged;
        public event Action<IWorldCellSelection> EditSelectionChanged;
        public event Action EditPreviewChanged;

        public void SetHovered(TilePickResult? next)
        {
            if (Nullable.Equals(Hovered, next))
            {
                return;
            }

            Hovered = next;
            HoverChanged?.Invoke(Hovered);
        }

        public void SetSelected(TilePickResult? next)
        {
            if (Nullable.Equals(Selected, next))
            {
                return;
            }

            Selected = next;
            SelectionChanged?.Invoke(Selected);
        }

        public void SelectHovered()
        {
            SetSelected(Hovered);
        }

        public void ReplaceEditHovered(IWorldCellSelection next)
        {
            if (ReferenceEquals(EditHovered, next))
            {
                return;
            }

            EditHovered = next;
            EditHoverChanged?.Invoke(EditHovered);
        }

        public void CommitEditHovered()
        {
            if (EditHovered == null)
            {
                return;
            }

            EditSelected = EditHovered;
            EditSelectionChanged?.Invoke(EditSelected);
            ReplaceEditHovered(null);
        }

        public void ReplaceEditSelected(IWorldCellSelection next)
        {
            if (ReferenceEquals(EditSelected, next))
            {
                return;
            }

            EditSelected = next;
            EditSelectionChanged?.Invoke(EditSelected);
        }

        public void ClearEditHovered() => ReplaceEditHovered(null);

        public void ClearEditSelected()
        {
            ReplaceEditSelected(null);
        }

        public void ReplaceEditPreview(
            IWorldCellSelection primary,
            IWorldCellSelection secondary,
            IWorldCellSelection invalid)
        {
            if (ReferenceEquals(EditPrimaryPreview, primary)
                && ReferenceEquals(EditSecondaryPreview, secondary)
                && ReferenceEquals(EditInvalidPreview, invalid))
            {
                return;
            }

            EditPrimaryPreview = primary;
            EditSecondaryPreview = secondary;
            EditInvalidPreview = invalid;
            EditPreviewChanged?.Invoke();
        }

        public void ClearEditPreview() =>
            ReplaceEditPreview(null, null, null);

        public void Clear()
        {
            SetHovered(null);
            SetSelected(null);
            ClearEditHovered();
            ClearEditSelected();
            ClearEditPreview();
        }
    }
}
