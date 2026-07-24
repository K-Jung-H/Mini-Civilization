using System;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    [DisallowMultipleComponent]
    public sealed class WorldTileSelectionState : MonoBehaviour
    {
        public TilePickResult? Hovered { get; private set; }
        public TilePickResult? Selected { get; private set; }

        public event Action<TilePickResult?> HoverChanged;
        public event Action<TilePickResult?> SelectionChanged;

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

        public void Clear()
        {
            SetHovered(null);
            SetSelected(null);
        }
    }
}
