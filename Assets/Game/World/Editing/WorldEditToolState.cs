using System;
using UnityEngine;

namespace MiniCivilization.World.Editing
{
    public enum WorldEditMode : byte
    {
        None,
        Area,
        Brush
    }

    public enum WorldEditPropertyGroup : byte
    {
        None,
        Terrain,
        Biome,
        Water,
        Surface
    }

    public readonly struct WorldEditToolSnapshot :
        IEquatable<WorldEditToolSnapshot>
    {
        public readonly WorldEditMode Mode;
        public readonly WorldEditPropertyGroup PropertyGroup;
        public readonly int DetailIndex;
        public readonly int BrushSize;

        public bool CapturesPointer => Mode != WorldEditMode.None;
        public bool IsReady =>
            CapturesPointer
            && PropertyGroup != WorldEditPropertyGroup.None
            && DetailIndex >= 0;

        public WorldEditToolSnapshot(
            WorldEditMode mode,
            WorldEditPropertyGroup propertyGroup,
            int detailIndex,
            int brushSize = 1)
        {
            Mode = mode;
            PropertyGroup = propertyGroup;
            DetailIndex = detailIndex;
            BrushSize = Math.Clamp(brushSize, 1, 3);
        }

        public bool Equals(WorldEditToolSnapshot other)
        {
            return Mode == other.Mode
                && PropertyGroup == other.PropertyGroup
                && DetailIndex == other.DetailIndex
                && BrushSize == other.BrushSize;
        }

        public override bool Equals(object obj) =>
            obj is WorldEditToolSnapshot other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                (byte)Mode,
                (byte)PropertyGroup,
                DetailIndex,
                BrushSize);
    }

    [DisallowMultipleComponent]
    public sealed class WorldEditToolState : MonoBehaviour
    {
        [SerializeField] private WorldEditToolbarView toolbarView;

        private WorldEditToolSnapshot current;
        private bool isSubscribed;

        public WorldEditToolSnapshot Current => current;
        public WorldEditMode Mode => current.Mode;
        public WorldEditPropertyGroup PropertyGroup => current.PropertyGroup;
        public int DetailIndex => current.DetailIndex;
        public int BrushSize => current.BrushSize;
        public bool CapturesPointer => current.CapturesPointer;
        public bool IsToolReady => current.IsReady;

        public event Action<WorldEditToolSnapshot> StateChanged;

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(WorldEditToolbarView view)
        {
            Unsubscribe();
            toolbarView = view;
            Subscribe();
            Refresh();
        }

        private void Subscribe()
        {
            if (isSubscribed || toolbarView == null)
            {
                return;
            }

            toolbarView.SelectionChanged += Refresh;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (toolbarView != null)
            {
                toolbarView.SelectionChanged -= Refresh;
            }

            isSubscribed = false;
        }

        private void Refresh()
        {
            var next = ReadToolbarState();
            if (current.Equals(next))
            {
                return;
            }

            current = next;
            StateChanged?.Invoke(current);
        }

        private WorldEditToolSnapshot ReadToolbarState()
        {
            if (toolbarView == null)
            {
                return default;
            }

            var mode = toolbarView.GetSelectedModeIndex() switch
            {
                1 => WorldEditMode.Area,
                2 => WorldEditMode.Brush,
                _ => WorldEditMode.None
            };
            var brushSize = toolbarView.GetSelectedBrushSize();
            if (!toolbarView.TryGetSelectedProperty(
                    out var sectionIndex,
                    out var detailIndex))
            {
                return new WorldEditToolSnapshot(
                    mode,
                    WorldEditPropertyGroup.None,
                    -1,
                    brushSize);
            }

            var propertyGroup = sectionIndex switch
            {
                0 => WorldEditPropertyGroup.Terrain,
                1 => WorldEditPropertyGroup.Biome,
                2 => WorldEditPropertyGroup.Water,
                3 => WorldEditPropertyGroup.Surface,
                _ => WorldEditPropertyGroup.None
            };
            return new WorldEditToolSnapshot(
                mode,
                propertyGroup,
                detailIndex,
                brushSize);
        }
    }
}
