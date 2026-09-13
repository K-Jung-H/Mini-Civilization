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
    [DisallowMultipleComponent]
    public sealed class WorldEditInputController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private WorldManager worldManager;
        [SerializeField] private WorldEditToolState toolState;
        [SerializeField] private WorldTileSelectionState selectionState;
        [SerializeField] private WorldEditConfirmationView confirmationView;

        private bool isDragging;
        private WorldRuntime observedRuntime;
        private IWorldCellSelection pendingSelection;
        private TilePickResult dragStart;
        private TilePickResult dragCurrent;
        private WorldEditToolSnapshot dragTool;
        private readonly HashSet<CellCoordinate> brushCellIndices = new();
        private readonly List<CellCoordinate> brushCells = new();
        private TilePickResult? idlePreviewAnchor;
        private int brushPreviewSize;
        private long idlePreviewRevision;
        private WorldEditToolSnapshot pendingTool;

        public bool IsPending => pendingSelection != null;
        internal bool TryGetPending(out IWorldCellSelection selection, out WorldEditToolSnapshot tool)
        {
            selection = pendingSelection;
            tool = pendingTool;
            return IsPending;
        }
        public event Action<IWorldCellSelection, WorldEditToolSnapshot>
            PendingSelectionChanged;
        public event Action<IWorldCellSelection, WorldEditToolSnapshot>
            ExecutionRequested;
        public event Action PendingCancelled;

        private void OnEnable()
        {
            if (worldManager != null) worldManager.WorldChanged += SynchronizeWorld;
            SynchronizeWorld();
            if (toolState != null)
            {
                toolState.StateChanged += OnToolStateChanged;
            }

            BindConfirmationView();
        }

        private void OnDisable()
        {
            if (worldManager != null) worldManager.WorldChanged -= SynchronizeWorld;
            if (toolState != null)
            {
                toolState.StateChanged -= OnToolStateChanged;
            }

            UnbindConfirmationView();
            CancelPending();
            CancelDrag();
        }

        private void LateUpdate()
        {
            SynchronizeWorld();
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
                if (IsPending)
                {
                    CancelPending();
                }
                else
                {
                    CancelDrag();
                }
                return;
            }

            if (IsPending)
            {
                if (mouse.leftButton.wasPressedThisFrame
                    && (EventSystem.current == null
                        || !EventSystem.current.IsPointerOverGameObject())
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
            WorldEditConfirmationView confirmation = null)
        {
            if (worldManager != null) worldManager.WorldChanged -= SynchronizeWorld;
            if (isActiveAndEnabled && toolState != null)
            {
                toolState.StateChanged -= OnToolStateChanged;
            }

            UnbindConfirmationView();
            CancelPending();
            CancelDrag();
            worldManager = manager;
            toolState = state;
            selectionState = selection;
            confirmationView = confirmation;
            observedRuntime = manager != null ? manager.CurrentWorldRuntime : null;
            if (isActiveAndEnabled && worldManager != null)
                worldManager.WorldChanged += SynchronizeWorld;

            if (isActiveAndEnabled && toolState != null)
            {
                toolState.StateChanged += OnToolStateChanged;
            }

            BindConfirmationView();
        }

        public void SetPendingExecutable(bool executable)
        {
            if (!IsPending)
            {
                return;
            }

            confirmationView?.SetExecutable(executable);
        }

        public void CompletePendingExecution()
        {
            EndPending(false);
        }

        public void CancelPending()
        {
            EndPending(true);
        }

        private void EndPending(bool cancelled)
        {
            var hadPending = IsPending;
            if (!hadPending)
            {
                confirmationView?.SetPending(false);
                return;
            }
            pendingSelection = null;
            pendingTool = default;
            selectionState?.ClearEditSelected();
            selectionState?.ClearEditPreview();
            confirmationView?.SetPending(false);
            if (cancelled) PendingCancelled?.Invoke();
        }

        public void CancelDrag()
        {
            selectionState?.ClearEditHovered();
            brushCellIndices.Clear();
            brushCells.Clear();
            idlePreviewAnchor = null;
            isDragging = false;
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

        }

        private void CompleteDrag()
        {
            var selection = selectionState?.EditHovered;
            isDragging = false;
            brushCellIndices.Clear();
            brushCells.Clear();
            idlePreviewAnchor = null;
            if (selection == null)
            {
                    return;
            }

            pendingTool = dragTool;
            pendingSelection = selection;
            confirmationView?.SetPending(true);
            selectionState.CommitEditHovered();
            PendingSelectionChanged?.Invoke(
                pendingSelection,
                pendingTool);
        }

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
            var size = toolState.BrushSize;
            if (idlePreviewAnchor.HasValue
                && idlePreviewAnchor.Value.Equals(hovered)
                && idlePreviewRevision == worldManager.CurrentWorldData.CellRevision
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
            idlePreviewRevision = worldManager.CurrentWorldData.CellRevision;
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
                    dragTool.BrushSize,
                    dragTool.CellSelectionPolicy);
            }
        }

        private void RefreshSinglePreview(TilePickResult pick)
        {
            var source = pick;
            if (!isDragging
                && idlePreviewAnchor.HasValue
                && idlePreviewAnchor.Value.Equals(source)
                && idlePreviewRevision == worldManager.CurrentWorldData.CellRevision
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
                idlePreviewRevision = worldManager.CurrentWorldData.CellRevision;
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
            if (IsPending && !pendingTool.Equals(next))
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
            if (!isActiveAndEnabled || confirmationView == null)
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

        private void RequestExecution()
        {
            SynchronizeWorld();
            if (!IsPending)
            {
                return;
            }

            ExecutionRequested?.Invoke(
                pendingSelection,
                pendingTool);
        }

        private void SynchronizeWorld()
        {
            var runtime = worldManager != null ? worldManager.CurrentWorldRuntime : null;
            if (ReferenceEquals(observedRuntime, runtime)) return;
            observedRuntime = runtime;
            CancelPending();
            CancelDrag();
        }
    }
}



