using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldEditApplyController : MonoBehaviour
    {
        private WorldEditController editController;
        private WorldTileSelectionState selectionState;
        public bool CanUndo => editController != null && editController.CanUndo;
        public bool CanRedo => editController != null && editController.CanRedo;
        public event Action<bool, bool> HistoryAvailabilityChanged;
        private WorldEditToolState toolState;
        private WorldEditInputController inputController;
        private EntityEditController entityEditController;
        private EntityManager entityManager;

        private readonly List<CellCoordinate> selectedCells = new();
        private readonly List<CellCoordinate> validCells = new();
        private readonly List<CellCoordinate> invalidCells = new();
        private readonly HashSet<CellColumnCoordinate> selectedColumns = new();

        private bool isSubscribed;
        private IWorldCellSelection observedSelection;
        private WorldEditToolSnapshot observedTool;
        private WorldData observedWorld;
        private long observedRevision;
        private WorldChangeId observedChange;
        private float nextEvaluationTime;
        private bool displayedCanUndo;
        private bool displayedCanRedo;

        private void Update()
        {
            if (!isSubscribed || Time.unscaledTime < nextEvaluationTime) return;
            nextEvaluationTime = Time.unscaledTime + 0.2f;
            var canUndo = CanUndo;
            var canRedo = CanRedo;
            if (displayedCanUndo != canUndo || displayedCanRedo != canRedo)
                RefreshHistoryButtons();
            IWorldCellSelection selection;
            WorldEditToolSnapshot tool;
            if (inputController == null || !inputController.TryGetPending(out selection, out tool))
            {
                selection = selectionState?.EditHovered;
                tool = toolState?.Current ?? default;
            }
            var world = editController.BoundWorld;
            if (!ReferenceEquals(observedSelection, selection) || !observedTool.Equals(tool)
                || !ReferenceEquals(observedWorld, world)
                || observedRevision != (world?.CellRevision ?? 0)
                || !observedChange.Equals(entityManager?.Runtime?.CurrentChangeId ?? default))
                RefreshPreview(selection, tool);
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        public void Configure(
            WorldEditController controller,
            WorldTileSelectionState selections,
            WorldEditToolState tools = null,
            WorldEditInputController input = null,
            EntityEditController entityEditor = null,
            EntityManager entities = null)
        {
            Unsubscribe();
            editController = controller;
            selectionState = selections;
            toolState = tools;
            inputController = input;
            entityEditController = entityEditor;
            entityManager = entities;
            Subscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || !isActiveAndEnabled || editController == null)
            {
                return;
            }

            if (editController != null)
            {
                editController.HistoryChanged += RefreshHistoryButtons;
            }

            if (selectionState != null)
            {
                selectionState.EditHoverChanged += OnEditHoverChanged;
            }

            if (inputController != null)
            {
                inputController.PendingSelectionChanged +=
                    OnPendingSelectionChanged;
                inputController.ExecutionRequested += OnExecutionRequested;
                inputController.PendingCancelled += ClearPreview;
            }

            isSubscribed = true;
            observedWorld = null;
            observedSelection = null;
            RefreshHistoryButtons();
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (editController != null)
            {
                editController.HistoryChanged -= RefreshHistoryButtons;
            }

            if (selectionState != null)
            {
                selectionState.EditHoverChanged -= OnEditHoverChanged;
            }

            if (inputController != null)
            {
                inputController.PendingSelectionChanged -=
                    OnPendingSelectionChanged;
                inputController.ExecutionRequested -= OnExecutionRequested;
                inputController.PendingCancelled -= ClearPreview;
            }

            isSubscribed = false;
        }

        public void RequestUndo()
        {
            if (!CanUndo) { RefreshHistoryButtons(); return; }
            inputController?.CancelPending();
            editController?.Undo();
            RefreshHistoryButtons();
        }

        public void RequestRedo()
        {
            if (!CanRedo) { RefreshHistoryButtons(); return; }
            inputController?.CancelPending();
            editController?.Redo();
            RefreshHistoryButtons();
        }

        private void RefreshHistoryButtons()
        {
            displayedCanUndo = CanUndo;
            displayedCanRedo = CanRedo;
            HistoryAvailabilityChanged?.Invoke(displayedCanUndo, displayedCanRedo);
        }

        // Borrows evaluation buffers until the next Evaluate call; never retained as preview state.
        private readonly struct Evaluation
        {
            public readonly WorldData World;
            public readonly IReadOnlyList<CellCoordinate> Cells;
            public readonly IReadOnlyList<CellCoordinate> Invalid;
            public readonly EntityPlacementPreview Entity;
            public bool CanExecute => Entity != null ? Entity.CanExecute : Cells != null && Cells.Count > 0;

            public Evaluation(WorldData world, IReadOnlyList<CellCoordinate> cells,
                IReadOnlyList<CellCoordinate> invalid = null, EntityPlacementPreview entity = null)
            {
                World = world;
                Cells = cells;
                Invalid = invalid;
                Entity = entity;
            }
        }

        private void OnEditHoverChanged(IWorldCellSelection selection)
        {
            if (inputController != null && inputController.IsPending) return;
            RefreshPreview(selection, toolState?.Current ?? default);
        }

        private void OnPendingSelectionChanged(IWorldCellSelection selection, WorldEditToolSnapshot tool)
        {
            RefreshPreview(selection, tool);
        }

        private void RefreshPreview(IWorldCellSelection selection, WorldEditToolSnapshot tool)
        {
            // Record the attempted inputs even on failure; retry when inputs change.
            observedSelection = selection;
            observedTool = tool;
            observedWorld = editController?.BoundWorld;
            observedRevision = observedWorld?.CellRevision ?? 0;
            observedChange = entityManager?.Runtime?.CurrentChangeId ?? default;
            inputController?.SetPendingExecutable(false);
            try
            {
                var evaluation = Evaluate(selection, tool);
                Present(evaluation, tool);
                inputController?.SetPendingExecutable(evaluation.CanExecute);
            }
            catch (Exception error)
            {
                inputController?.SetPendingExecutable(false);
                Debug.LogException(error, this);
                // Clear both projections independently if a presentation callback fails.
                try { selectionState?.ClearEditPreview(); }
                catch (Exception cleanupError) { Debug.LogException(cleanupError, this); }
                try { entityEditController?.ClearPreview(); }
                catch (Exception cleanupError) { Debug.LogException(cleanupError, this); }
            }
        }

        internal enum ExecutionResult
        {
            Success,
            PartialSuccess,
            NoChange,
            Rejected,
            Failed
        }

        private void OnExecutionRequested(IWorldCellSelection selection, WorldEditToolSnapshot tool)
        {
            ExecutionResult result;
            try
            {
                // Execution evaluates current data, never a retained preview result.
                var evaluation = Evaluate(selection, tool);
                if (!evaluation.CanExecute)
                    result = ExecutionResult.Rejected;
                else
                {
                    ClearPreview();
                    if (tool.IsEntityTool)
                    {
                        var placement = entityEditController.ApplyEvaluated(tool.EntityDefinition, evaluation.Entity);
                        result = placement.Succeeded > 0
                            ? (placement.Failed > 0 ? ExecutionResult.PartialSuccess : ExecutionResult.Success)
                            : (placement.Failed > 0 ? ExecutionResult.Failed : ExecutionResult.Rejected);
                    }
                    else if (tool.Action.PropertyGroup == WorldEditPropertyGroup.Road)
                        result = ApplyRoad(evaluation.World, tool.Action.RoadType, evaluation.Cells);
                    else
                        result = ApplyTerrain(evaluation.World, tool.Action.TerrainOperation);
                }
            }
            catch (Exception error)
            {
                result = ExecutionResult.Failed;
                Debug.LogException(error, this);
            }

            FinishExecution(result, selection, tool);
        }

        private void FinishExecution(ExecutionResult result,
            IWorldCellSelection selection, WorldEditToolSnapshot tool)
        {
            // A callback may have replaced the world or cancelled this pending operation.
            if (inputController == null
                || !inputController.TryGetPending(out var pending, out var pendingTool)
                || !ReferenceEquals(pending, selection) || !pendingTool.Equals(tool)) return;

            switch (result)
            {
                case ExecutionResult.Success:
                case ExecutionResult.PartialSuccess:
                case ExecutionResult.NoChange:
                    inputController.CompletePendingExecution();
                    if (result == ExecutionResult.NoChange)
                        Debug.Log("World edit completed: no changes.", this);
                    break;
                default:
                    // Leave the selection available for correction or cancellation.
                    RefreshPreview(pending, pendingTool);
                    if (result == ExecutionResult.Rejected)
                        Debug.LogWarning("World edit cannot be applied to the current selection.", this);
                    break;
            }
        }

        private Evaluation Evaluate(IWorldCellSelection selection, WorldEditToolSnapshot tool)
        {
            var world = editController?.BoundWorld;
            if (selection == null || world == null || !tool.IsReady) return default;
            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, world);
            // A partially unloaded selection must not silently apply only its loaded portion.
            foreach (var cell in selectedCells)
                if (!world.Contains(cell.X, cell.Y, cell.Z) || !world.IsChunkLoaded(cell.X, cell.Z))
                    return new Evaluation(world, Array.Empty<CellCoordinate>(), selectedCells);
            if (selectedCells.Count == 0) return default;
            if (tool.IsEntityTool)
            {
                var entity = entityEditController?.EvaluateCells(tool.EntityDefinition, selectedCells);
                return entity == null ? default : new Evaluation(world, null, entity: entity);
            }
            if (tool.Action.PropertyGroup == WorldEditPropertyGroup.Road)
                return EvaluateRoad(world, tool.Action.RoadType);
            return tool.Action.PropertyGroup == WorldEditPropertyGroup.Terrain
                ? new Evaluation(world, selectedCells) : default;
        }

        private void Present(Evaluation evaluation, WorldEditToolSnapshot tool)
        {
            if (evaluation.World == null)
            {
                ClearPreview();
                return;
            }
            var entity = evaluation.Entity;
            selectionState.ReplaceEditPreview(
                CreateSelection(evaluation.World, entity?.PrimaryCells ?? evaluation.Cells),
                CreateSelection(evaluation.World, entity?.SecondaryCells),
                CreateSelection(evaluation.World, entity?.InvalidCells ?? evaluation.Invalid));
            if (entity != null) entityEditController.ShowPreview(tool.EntityDefinition, entity);
            else entityEditController?.ClearPreview();
        }

        private Evaluation EvaluateRoad(WorldData world, RoadType roadType)
        {
            validCells.Clear();
            invalidCells.Clear();
            var entities = entityManager?.Entities;
            foreach (var coordinate in selectedCells)
            {
                var cell = world.GetCell(coordinate.X, coordinate.Y, coordinate.Z);
                if (roadType == RoadType.None)
                {
                    if (cell.HasRoad) validCells.Add(coordinate);
                }
                else if (IsTopGroundSurface(entityManager?.Runtime, world, coordinate)
                    && (entities == null || !entities.HasBuildingInColumn(coordinate.X, coordinate.Z)))
                    validCells.Add(coordinate);
                else invalidCells.Add(coordinate);
            }
            return new Evaluation(world, validCells, invalidCells);
        }
        private ExecutionResult ApplyRoad(
            WorldData world,
            RoadType roadType,
            IReadOnlyList<CellCoordinate> cells)
        {
            var transaction = editController.BeginTransaction();
            var changed = false;
            var eligible = false;
            try
            {
                var entities = entityManager?.Entities;
                for (var index = 0; index < cells.Count; index++)
                {
                    var coordinate = cells[index];
                    var cell = world.GetCell(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z);
                    if (roadType == RoadType.None)
                    {
                        if (cell.HasRoad)
                        {
                            eligible = true;
                            changed |= transaction.SetRoad(
                                coordinate.X,
                                coordinate.Z,
                                default);
                        }

                        continue;
                    }

                    if (!IsTopGroundSurface(
                            entityManager?.Runtime,
                            world,
                            coordinate)
                        || entities != null
                        && entities.HasBuildingInColumn(
                            coordinate.X,
                            coordinate.Z))
                    {
                        continue;
                    }

                    eligible = true;
                    changed |= transaction.SetRoad(
                        coordinate.X,
                        coordinate.Z,
                        new RoadData
                        {
                            Type = roadType,
                            CrossesCenter = true
                        });
                }

                if (changed)
                {
                    transaction.Commit();
                }
                else
                {
                    transaction.Rollback();
                }

                return changed ? ExecutionResult.Success
                    : eligible ? ExecutionResult.NoChange : ExecutionResult.Rejected;
            }
            catch
            {
                if (!transaction.IsCompleted)
                {
                    transaction.Rollback();
                }

                throw;
            }
        }

        private void ClearPreview()
        {
            selectionState?.ClearEditPreview();
            entityEditController?.ClearPreview();
        }

        private static IWorldCellSelection CreateSelection(
            WorldData world,
            IReadOnlyList<CellCoordinate> cells)
        {
            if (world == null || cells == null || cells.Count == 0)
            {
                return null;
            }

            var validCount = 0;
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                if (world.Contains(cell.X, cell.Y, cell.Z))
                {
                    validCount++;
                }
            }

            return validCount == 0
                ? null
                : WorldCellSetSelection.Create(world, cells);
        }

        private static bool IsTopGroundSurface(
            WorldRuntime runtime,
            WorldData world,
            CellCoordinate coordinate)
        {
            if (!world.TryGetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    out var cell)
                || !cell.HasTerrain)
            {
                return false;
            }

            if (runtime != null && ReferenceEquals(runtime.Data, world))
            {
                var surface = runtime.SurfaceCache.GetSurfaceHeight(
                    coordinate.X,
                    coordinate.Z);
                return surface.HasGround
                    && !surface.HasWater
                    && surface.GroundCellY == coordinate.Y;
            }

            for (var y = coordinate.Y + 1; y < world.Height; y++)
            {
                var above = world.GetCell(coordinate.X, y, coordinate.Z);
                if (above.HasTerrain || above.HasWater)
                {
                    return false;
                }
            }

            return !cell.HasWater;
        }

        private ExecutionResult ApplyTerrain(
            WorldData world,
            TerrainEditOperation operation)
        {
            selectedColumns.Clear();
            var transaction = editController.BeginTransaction();
            try
            {
                switch (operation)
                {
                    case TerrainEditOperation.Add:
                        for (var index = 0; index < selectedCells.Count; index++)
                        {
                            var coordinate = selectedCells[index];
                            var current = world.GetCell(
                                coordinate.X,
                                coordinate.Y,
                                coordinate.Z);
                            if (!current.HasTerrain)
                            {
                                transaction.SetCell(
                                    coordinate.X,
                                    coordinate.Y,
                                    coordinate.Z,
                                    CreateTerrainCell(current));
                            }
                        }

                        break;
                    case TerrainEditOperation.Remove:
                        selectedCells.Sort(CompareCellsHighestFirst);
                        for (var index = 0; index < selectedCells.Count; index++)
                        {
                            var coordinate = selectedCells[index];
                            transaction.TryClearCell(
                                coordinate.X,
                                coordinate.Y,
                                coordinate.Z);
                        }

                        break;
                    case TerrainEditOperation.Raise:
                    case TerrainEditOperation.Lower:
                        for (var index = 0; index < selectedCells.Count; index++)
                        {
                            var coordinate = selectedCells[index];
                            if (!world.GetCell(
                                    coordinate.X,
                                    coordinate.Y,
                                    coordinate.Z).HasTerrain)
                            {
                                continue;
                            }

                            var column = new CellColumnCoordinate(
                                coordinate.X,
                                coordinate.Z);
                            if (!selectedColumns.Add(column))
                            {
                                continue;
                            }

                            if (!transaction.TryGetLowestPendingSolidY(
                                    coordinate.X,
                                    coordinate.Z,
                                    out _))
                            {
                                continue;
                            }

                            if (operation == TerrainEditOperation.Raise)
                                transaction.RaiseColumn(coordinate.X, coordinate.Z);
                            else
                                transaction.LowerColumn(coordinate.X, coordinate.Z);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(operation),
                            operation,
                            null);
                }

                var changes = transaction.Commit();
                return changes == null ? ExecutionResult.NoChange : ExecutionResult.Success;
            }
            catch
            {
                if (!transaction.IsCompleted)
                {
                    transaction.Rollback();
                }

                throw;
            }
        }

        private static CellData CreateTerrainCell(CellData current)
        {
            current.Terrain.Material = MaterialType.Soil;
            current.Terrain.Surface = SurfaceType.Ground;
            current.Terrain.Geology = current.Terrain.Geology != MaterialType.None
                ? current.Terrain.Geology
                : MaterialType.Rock;
            current.Terrain.SolidHeight = (byte)WorldGrid.HeightStepsPerCell;
            current.Water = default;
            return current;
        }

        private static int CompareCellsHighestFirst(
            CellCoordinate left,
            CellCoordinate right)
        {
            var yComparison = right.Y.CompareTo(left.Y);
            if (yComparison != 0)
            {
                return yComparison;
            }

            var zComparison = left.Z.CompareTo(right.Z);
            return zComparison != 0
                ? zComparison
                : left.X.CompareTo(right.X);
        }
    }
}


