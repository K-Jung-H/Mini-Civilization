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
        [SerializeField] private WorldEditConfirmationView confirmationView;
        [SerializeField] private WorldEditToolbarView toolbarView;

        private bool isDragging;
        private bool isPending;
        private bool pendingExecutable;
        private TilePickResult dragStart;
        private TilePickResult dragCurrent;
        private WorldEditToolSnapshot dragTool;
        private readonly HashSet<CellCoordinate> brushCellIndices = new();
        private readonly List<CellCoordinate> brushCells = new();
        private TilePickResult? idlePreviewAnchor;
        private int brushPreviewSize;
        private WorldEditToolSnapshot pendingTool;

        public bool IsDragging => isDragging;
        public bool IsPending => isPending;
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
        public event Action<IWorldCellSelection, WorldEditToolSnapshot>
            PendingSelectionChanged;
        public event Action<IWorldCellSelection, WorldEditToolSnapshot>
            ExecutionRequested;
        public event Action PendingCancelled;

        private void OnEnable()
        {
            if (toolState != null)
            {
                toolState.StateChanged += OnToolStateChanged;
            }

            BindToolbarView();
            BindConfirmationView();
        }

        private void OnDisable()
        {
            if (toolState != null)
            {
                toolState.StateChanged -= OnToolStateChanged;
            }

            UnbindToolbarView();
            UnbindConfirmationView();
            CancelPending();
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
                if (isPending)
                {
                    CancelPending();
                }
                else
                {
                    CancelDrag();
                }
                return;
            }

            if (isPending)
            {
                if (mouse.leftButton.wasPressedThisFrame
                    && (confirmationView == null
                        || !confirmationView.ContainsScreenPoint(
                            mouse.position.ReadValue())))
                {
                    CancelPending();
                    return;
                }

                selectionState.ClearEditHovered();
                return;
            }

            if (!toolState.IsToolReady)
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
            else
            {
                RefreshSinglePreview(hovered);
            }
        }

        public void Configure(
            WorldManager manager,
            WorldEditToolState state,
            WorldTileSelectionState selection,
            WorldEditConfirmationView confirmation = null,
            WorldEditToolbarView toolbar = null)
        {
            if (isActiveAndEnabled && toolState != null)
            {
                toolState.StateChanged -= OnToolStateChanged;
            }

            UnbindToolbarView();
            UnbindConfirmationView();
            CancelPending();
            CancelDrag();
            worldManager = manager;
            toolState = state;
            selectionState = selection;
            confirmationView = confirmation;
            toolbarView = toolbar;

            if (isActiveAndEnabled && toolState != null)
            {
                toolState.StateChanged += OnToolStateChanged;
            }

            BindToolbarView();
            BindConfirmationView();
        }

        public void SetPendingExecutable(bool executable)
        {
            if (!isPending)
            {
                return;
            }

            pendingExecutable = executable;
            confirmationView?.SetExecutable(executable);
        }

        public void CompletePendingExecution()
        {
            if (!isPending)
            {
                return;
            }

            isPending = false;
            pendingExecutable = false;
            selectionState?.ClearEditSelected();
            selectionState?.ClearEditPreview();
            confirmationView?.Hide();
        }

        public void CancelPending()
        {
            if (!isPending
                && selectionState?.EditSelected == null)
            {
                confirmationView?.Hide();
                return;
            }

            isPending = false;
            pendingExecutable = false;
            selectionState?.ClearEditSelected();
            selectionState?.ClearEditPreview();
            confirmationView?.Hide();
            PendingCancelled?.Invoke();
        }

        public void CancelDrag()
        {
            selectionState?.ClearEditHovered();
            brushCellIndices.Clear();
            brushCells.Clear();
            idlePreviewAnchor = null;
            if (!isDragging)
            {
                return;
            }

            isDragging = false;
            DragCancelled?.Invoke();
        }

        private void BeginDrag(TilePickResult pick)
        {
            dragTool = toolState.Current;
            if (!TryResolveToolCell(pick, dragTool, out _))
            {
                selectionState?.ClearEditHovered();
                return;
            }

            isDragging = true;
            dragStart = pick;
            dragCurrent = pick;
            if (dragTool.Mode == WorldEditMode.Single)
            {
                RefreshSinglePreview(pick);
            }
            else if (dragTool.Mode == WorldEditMode.Brush)
            {
                brushCellIndices.Clear();
                brushCells.Clear();
                AppendBrushSegment(pick, pick);
                RefreshBrushStrokePreview();
            }
            else if (dragTool.Mode == WorldEditMode.Area)
            {
                RefreshAreaPreview();
            }

            var snapshot = CreateSnapshot();
            DragStarted?.Invoke(snapshot);
        }

        private void UpdateDrag(TilePickResult pick)
        {
            if (!TryResolveToolCell(pick, dragTool, out _))
            {
                selectionState?.ClearEditHovered();
                return;
            }

            if (pick.Equals(dragCurrent))
            {
                return;
            }

            var previous = dragCurrent;
            dragCurrent = pick;
            if (dragTool.Mode == WorldEditMode.Single)
            {
                RefreshSinglePreview(pick);
            }
            else if (dragTool.Mode == WorldEditMode.Brush)
            {
                AppendBrushSegment(previous, pick);
                RefreshBrushStrokePreview();
            }
            else if (dragTool.Mode == WorldEditMode.Area)
            {
                RefreshAreaPreview();
            }

            var snapshot = CreateSnapshot();
            DragChanged?.Invoke(snapshot);
        }

        private void CompleteDrag()
        {
            var snapshot = CreateSnapshot();
            var selection = selectionState?.EditHovered;
            isDragging = false;
            brushCellIndices.Clear();
            brushCells.Clear();
            idlePreviewAnchor = null;
            if (selection == null)
            {
                DragCompleted?.Invoke(snapshot);
                return;
            }

            pendingTool = dragTool;
            isPending = true;
            pendingExecutable = false;
            var screenPosition = Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : Vector2.zero;
            confirmationView?.Show(screenPosition, false);
            selectionState.CommitEditHovered();
            PendingSelectionChanged?.Invoke(
                selectionState.EditSelected,
                pendingTool);
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

            if (!TryResolveToolCell(
                    dragStart,
                    dragTool,
                    out var startCell)
                || !TryResolveToolCell(
                    dragCurrent,
                    dragTool,
                    out var currentCell))
            {
                selectionState?.ClearEditHovered();
                return;
            }

            var bounds = new CellBounds(
                new CellCoordinate(
                    Math.Min(startCell.X, currentCell.X),
                    Math.Min(startCell.Y, currentCell.Y),
                    Math.Min(startCell.Z, currentCell.Z)),
                new CellCoordinate(
                    Math.Max(startCell.X, currentCell.X),
                    Math.Max(startCell.Y, currentCell.Y),
                    Math.Max(startCell.Z, currentCell.Z)));
            selectionState.ReplaceEditHovered(
                WorldCellBoxSelection.Create(
                    worldManager.CurrentWorldData,
                    bounds));
        }

        private void RefreshIdleBrushPreview(TilePickResult hovered)
        {
            var size = Mathf.Clamp(toolState.BrushSize, 1, 3);
            if (idlePreviewAnchor.HasValue
                && idlePreviewAnchor.Value.Equals(hovered)
                && brushPreviewSize == size
                && selectionState.EditHovered is WorldCellSetSelection)
            {
                return;
            }

            brushCellIndices.Clear();
            brushCells.Clear();
            if (!TryResolveToolCell(
                    hovered,
                    toolState.Current,
                    out var placementCell))
            {
                selectionState?.ClearEditHovered();
                return;
            }

            AddBrushFootprint(
                placementCell,
                size,
                toolState.Current.CellSelectionPolicy);
            RefreshBrushStrokePreview();
            idlePreviewAnchor = hovered;
            brushPreviewSize = size;
        }

        private void ClearIdleBrushPreview()
        {
            if (isDragging)
            {
                return;
            }

            idlePreviewAnchor = null;
            selectionState?.ClearEditHovered();
        }

        private void AppendBrushSegment(
            TilePickResult from,
            TilePickResult to)
        {
            if (!TryResolveToolCell(from, dragTool, out var fromCell)
                || !TryResolveToolCell(to, dragTool, out var toCell))
            {
                return;
            }

            var world = worldManager.CurrentWorldData;
            var deltaX = toCell.X - fromCell.X;
            var deltaZ = toCell.Z - fromCell.Z;
            var steps = Mathf.Max(Mathf.Abs(deltaX), Mathf.Abs(deltaZ));
            steps = Mathf.Max(1, steps);
            for (var step = 0; step <= steps; step++)
            {
                var t = step / (float)steps;
                var x = Mathf.RoundToInt(Mathf.Lerp(
                    fromCell.X,
                    toCell.X,
                    t));
                var z = Mathf.RoundToInt(Mathf.Lerp(
                    fromCell.Z,
                    toCell.Z,
                    t));
                var fallbackY = Mathf.RoundToInt(Mathf.Lerp(
                    fromCell.Y,
                    toCell.Y,
                    t));
                AddBrushFootprint(
                    new CellCoordinate(x, fallbackY, z),
                    Mathf.Clamp(dragTool.BrushSize, 1, 3),
                    dragTool.CellSelectionPolicy);
            }
        }

        private void RefreshSinglePreview(TilePickResult pick)
        {
            var source = pick;
            if (!isDragging
                && idlePreviewAnchor.HasValue
                && idlePreviewAnchor.Value.Equals(source)
                && selectionState?.EditHovered != null)
            {
                return;
            }

            var tool = isDragging ? dragTool : toolState.Current;
            if (worldManager == null
                || !worldManager.HasWorld
                || selectionState == null)
            {
                return;
            }

            if (!TryResolveToolCell(pick, tool, out var selectedCell))
            {
                selectionState.ClearEditHovered();
                return;
            }

            selectionState.ReplaceEditHovered(
                WorldCellSetSelection.Create(
                    worldManager.CurrentWorldData,
                    new[] { selectedCell }));
            if (!isDragging)
            {
                idlePreviewAnchor = source;
            }
        }

        private bool TryResolveToolCell(
            TilePickResult pick,
            WorldEditToolSnapshot tool,
            out CellCoordinate cell)
        {
            var world = worldManager?.CurrentWorldData;
            return WorldEditCellSelectionResolver.TryResolve(
                world,
                pick,
                tool.CellSelectionPolicy,
                out cell);
        }

        private void AddBrushFootprint(
            CellCoordinate anchor,
            int size,
            WorldEditCellSelectionPolicy policy)
        {
            var world = worldManager.CurrentWorldData;
            var minimumOffset = -(size / 2);
            for (var z = anchor.Z + minimumOffset;
                 z < anchor.Z + minimumOffset + size;
                 z++)
            for (var x = anchor.X + minimumOffset;
                 x < anchor.X + minimumOffset + size;
                 x++)
            {
                if (!world.TryGetCell(x, anchor.Y, z, out var cell)
                    || (policy == WorldEditCellSelectionPolicy.SurfaceCell
                        && !cell.HasTerrain
                        && !cell.HasWater))
                {
                    continue;
                }

                var coordinate = new CellCoordinate(x, anchor.Y, z);
                if (brushCellIndices.Add(coordinate))
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
            if (isPending && !pendingTool.Equals(next))
            {
                CancelPending();
                CancelDrag();
                return;
            }

            if (isDragging && !dragTool.Equals(next))
            {
                CancelDrag();
                return;
            }

            if (isDragging)
            {
                return;
            }

            idlePreviewAnchor = null;
            selectionState?.ClearEditHovered();
        }

        private void BindConfirmationView()
        {
            if (confirmationView == null)
            {
                return;
            }

            confirmationView.CancelRequested -= CancelPending;
            confirmationView.CancelRequested += CancelPending;
            confirmationView.ExecuteRequested -= RequestExecution;
            confirmationView.ExecuteRequested += RequestExecution;
        }

        private void UnbindConfirmationView()
        {
            if (confirmationView == null)
            {
                return;
            }

            confirmationView.CancelRequested -= CancelPending;
            confirmationView.ExecuteRequested -= RequestExecution;
        }

        private void BindToolbarView()
        {
            if (toolbarView == null)
            {
                return;
            }

            toolbarView.StructureChanged -= OnToolbarStructureChanged;
            toolbarView.StructureChanged += OnToolbarStructureChanged;
        }

        private void UnbindToolbarView()
        {
            if (toolbarView != null)
            {
                toolbarView.StructureChanged -= OnToolbarStructureChanged;
            }
        }

        private void OnToolbarStructureChanged()
        {
            CancelPending();
            CancelDrag();
        }

        private void RequestExecution()
        {
            if (!isPending
                || !pendingExecutable
                || selectionState?.EditSelected == null)
            {
                return;
            }

            ExecutionRequested?.Invoke(
                selectionState.EditSelected,
                pendingTool);
        }
    }
}
