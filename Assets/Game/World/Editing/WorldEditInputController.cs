using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MiniCivilization.World.Editing
{
    public readonly struct WorldEditDragSnapshot
    {
        public readonly WorldEditToolSnapshot Tool;
        public readonly TilePickResult Start;
        public readonly TilePickResult Current;
        public readonly CellBounds Bounds;
        public readonly IWorldCellSelection Selection;

        public WorldEditDragSnapshot(
            WorldEditToolSnapshot tool,
            TilePickResult start,
            TilePickResult current,
            IWorldCellSelection selection = null)
        {
            Tool = tool;
            Start = start;
            Current = current;
            Selection = selection;
            Bounds = selection?.Bounds
                ?? BuildBounds(start.Cell, current.Cell);
        }

        private static CellBounds BuildBounds(
            CellCoordinate start,
            CellCoordinate current)
        {
            return new CellBounds(
                new CellCoordinate(
                    Math.Min(start.X, current.X),
                    Math.Min(start.Y, current.Y),
                    Math.Min(start.Z, current.Z)),
                new CellCoordinate(
                    Math.Max(start.X, current.X),
                    Math.Max(start.Y, current.Y),
                    Math.Max(start.Z, current.Z)));
        }
    }

    [DisallowMultipleComponent]
    public sealed class WorldEditInputController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private WorldEditToolState toolState;
        [SerializeField] private WorldTileSelectionState selectionState;

        private bool isDragging;
        private TilePickResult dragStart;
        private TilePickResult dragCurrent;
        private WorldEditToolSnapshot dragTool;
        private readonly HashSet<int> brushCellIndices = new();
        private readonly List<CellCoordinate> brushCells = new();
        private TilePickResult? brushPreviewAnchor;
        private int brushPreviewSize;

        public bool IsDragging => isDragging;
        public WorldEditDragSnapshot? CurrentDrag =>
            isDragging
                ? new WorldEditDragSnapshot(
                    dragTool,
                    dragStart,
                    dragCurrent,
                    selectionState?.EditHovered)
                : null;

        public event Action<WorldEditDragSnapshot> DragStarted;
        public event Action<WorldEditDragSnapshot> DragChanged;
        public event Action<WorldEditDragSnapshot> DragCompleted;
        public event Action DragCancelled;

        private void OnEnable()
        {
            if (toolState != null)
            {
                toolState.StateChanged += OnToolStateChanged;
            }
        }

        private void OnDisable()
        {
            if (toolState != null)
            {
                toolState.StateChanged -= OnToolStateChanged;
            }

            CancelDrag();
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse == null
                || worldManager == null
                || !worldManager.HasWorld
                || toolState == null
                || selectionState == null)
            {
                CancelDrag();
                return;
            }

            if ((Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
                || mouse.rightButton.wasPressedThisFrame)
            {
                CancelDrag();
                return;
            }

            // Selection shape input belongs to the edit mode itself. Property
            // details are only required when the resulting selection is
            // applied, so changing a property must not disable reselection.
            if (!toolState.CapturesPointer)
            {
                CancelDrag();
                return;
            }

            var pointerOverUi = EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject();
            if (isDragging)
            {
                if (pointerOverUi)
                {
                    CancelDrag();
                    return;
                }

                if (selectionState.Hovered.HasValue)
                {
                    UpdateDrag(selectionState.Hovered.Value);
                }

                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    CompleteDrag();
                }

                return;
            }

            if (pointerOverUi || !selectionState.Hovered.HasValue)
            {
                ClearIdleBrushPreview();
                return;
            }

            var hovered = selectionState.Hovered.Value;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                BeginDrag(hovered);
            }
            else if (toolState.Mode == WorldEditMode.Brush)
            {
                RefreshIdleBrushPreview(hovered);
            }
        }

        public void Configure(
            WorldManager manager,
            WorldEditToolState state,
            WorldTileSelectionState selection)
        {
            if (isActiveAndEnabled && toolState != null)
            {
                toolState.StateChanged -= OnToolStateChanged;
            }

            CancelDrag();
            worldManager = manager;
            toolState = state;
            selectionState = selection;

            if (isActiveAndEnabled && toolState != null)
            {
                toolState.StateChanged += OnToolStateChanged;
            }
        }

        public void CancelDrag()
        {
            selectionState?.ClearEditHovered();
            brushCellIndices.Clear();
            brushCells.Clear();
            brushPreviewAnchor = null;
            if (!isDragging)
            {
                return;
            }

            isDragging = false;
            DragCancelled?.Invoke();
        }

        private void BeginDrag(TilePickResult pick)
        {
            isDragging = true;
            dragStart = pick;
            dragCurrent = pick;
            dragTool = toolState.Current;
            if (dragTool.Mode == WorldEditMode.Brush)
            {
                brushCellIndices.Clear();
                brushCells.Clear();
                AppendBrushSegment(pick, pick);
                RefreshBrushStrokePreview();
            }
            else
            {
                RefreshAreaPreview();
            }

            var snapshot = CreateSnapshot();
            DragStarted?.Invoke(snapshot);
        }

        private void UpdateDrag(TilePickResult pick)
        {
            if (pick.Cell.Equals(dragCurrent.Cell))
            {
                return;
            }

            var previous = dragCurrent;
            dragCurrent = pick;
            if (dragTool.Mode == WorldEditMode.Brush)
            {
                AppendBrushSegment(previous, pick);
                RefreshBrushStrokePreview();
            }
            else
            {
                RefreshAreaPreview();
            }

            var snapshot = CreateSnapshot();
            DragChanged?.Invoke(snapshot);
        }

        private void CompleteDrag()
        {
            var snapshot = CreateSnapshot();
            selectionState.CommitEditHovered();
            isDragging = false;
            brushCellIndices.Clear();
            brushCells.Clear();
            brushPreviewAnchor = null;
            DragCompleted?.Invoke(snapshot);
        }

        private WorldEditDragSnapshot CreateSnapshot() =>
            new(
                dragTool,
                dragStart,
                dragCurrent,
                selectionState?.EditHovered);

        private void RefreshAreaPreview()
        {
            if (worldManager == null || !worldManager.HasWorld)
            {
                selectionState?.ClearEditHovered();
                return;
            }

            var bounds = new WorldEditDragSnapshot(
                dragTool,
                dragStart,
                dragCurrent).Bounds;
            selectionState.ReplaceEditHovered(
                WorldCellBoxSelection.Create(
                    worldManager.CurrentWorldData,
                    bounds));
        }

        private void RefreshIdleBrushPreview(TilePickResult hovered)
        {
            var size = Mathf.Clamp(toolState.BrushSize, 1, 3);
            if (brushPreviewAnchor.HasValue
                && brushPreviewAnchor.Value.Equals(hovered)
                && brushPreviewSize == size
                && selectionState.EditHovered is WorldCellSetSelection)
            {
                return;
            }

            brushCellIndices.Clear();
            brushCells.Clear();
            AddBrushFootprint(hovered.Cell, size);
            RefreshBrushStrokePreview();
            brushPreviewAnchor = hovered;
            brushPreviewSize = size;
        }

        private void ClearIdleBrushPreview()
        {
            if (isDragging || toolState?.Mode != WorldEditMode.Brush)
            {
                return;
            }

            brushPreviewAnchor = null;
            selectionState?.ClearEditHovered();
        }

        private void AppendBrushSegment(
            TilePickResult from,
            TilePickResult to)
        {
            var world = worldManager.CurrentWorldData;
            var deltaX = to.Cell.X - from.Cell.X;
            var deltaZ = to.Cell.Z - from.Cell.Z;
            var steps = Mathf.Max(Mathf.Abs(deltaX), Mathf.Abs(deltaZ));
            steps = Mathf.Max(1, steps);
            for (var step = 0; step <= steps; step++)
            {
                var t = step / (float)steps;
                var x = Mathf.RoundToInt(Mathf.Lerp(
                    from.Cell.X,
                    to.Cell.X,
                    t));
                var z = Mathf.RoundToInt(Mathf.Lerp(
                    from.Cell.Z,
                    to.Cell.Z,
                    t));
                var fallbackY = Mathf.RoundToInt(Mathf.Lerp(
                    from.Cell.Y,
                    to.Cell.Y,
                    t));
                var surfaceType = t < 0.5f
                    ? from.SurfaceType
                    : to.SurfaceType;
                var y = ResolveBrushCellY(
                    world,
                    x,
                    z,
                    fallbackY,
                    surfaceType);
                AddBrushFootprint(
                    new CellCoordinate(x, y, z),
                    Mathf.Clamp(dragTool.BrushSize, 1, 3));
            }
        }

        private static int ResolveBrushCellY(
            WorldData world,
            int x,
            int z,
            int fallbackY,
            SurfaceInteractionType surfaceType)
        {
            if (!world.ContainsColumn(x, z))
            {
                return Mathf.Clamp(fallbackY, 0, world.Height - 1);
            }

            var column = world.GetSurfaceColumn(x, z);
            if (surfaceType == SurfaceInteractionType.Water)
            {
                return column.HasWater
                    ? column.WaterCellY
                    : Mathf.Clamp(fallbackY, 0, world.Height - 1);
            }

            return column.HasSurface
                ? column.SurfaceCellY
                : Mathf.Clamp(fallbackY, 0, world.Height - 1);
        }

        private void AddBrushFootprint(
            CellCoordinate anchor,
            int size)
        {
            var world = worldManager.CurrentWorldData;
            for (var z = anchor.Z; z < anchor.Z + size; z++)
            for (var x = anchor.X; x < anchor.X + size; x++)
            {
                if (!world.Contains(x, anchor.Y, z))
                {
                    continue;
                }

                var coordinate = new CellCoordinate(x, anchor.Y, z);
                var index = WorldCellIndex.Encode(
                    world,
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if (brushCellIndices.Add(index))
                {
                    brushCells.Add(coordinate);
                }
            }
        }

        private void RefreshBrushStrokePreview()
        {
            if (brushCells.Count == 0)
            {
                selectionState.ClearEditHovered();
                return;
            }

            selectionState.ReplaceEditHovered(
                WorldCellSetSelection.Create(
                    worldManager.CurrentWorldData,
                    brushCells));
        }

        private void OnToolStateChanged(WorldEditToolSnapshot next)
        {
            if (isDragging && !dragTool.Equals(next))
            {
                CancelDrag();
                return;
            }

            if (isDragging)
            {
                return;
            }

            brushPreviewAnchor = null;
            selectionState?.ClearEditHovered();
        }
    }
}
