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
        private WorldEditToolbarView toolbarView;
        private WorldEditToolState toolState;
        private WorldEditInputController inputController;
        private EntityEditController entityEditController;
        private EntityManager entityManager;

        private readonly List<CellCoordinate> selectedCells = new();
        private readonly List<CellCoordinate> remappedCells = new();
        private readonly List<CellCoordinate> validCells = new();
        private readonly List<CellCoordinate> invalidCells = new();
        private readonly HashSet<int> selectedColumns = new();
        private readonly HashSet<int> selectedTerrainCells = new();
        private readonly Dictionary<int, int> shiftedColumnBottoms = new();

        private bool isSubscribed;

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
            WorldEditToolbarView toolbar,
            WorldEditToolState tools = null,
            WorldEditInputController input = null,
            EntityEditController entityEditor = null,
            EntityManager entities = null)
        {
            Unsubscribe();
            editController = controller;
            selectionState = selections;
            toolbarView = toolbar;
            toolState = tools;
            inputController = input;
            entityEditController = entityEditor;
            entityManager = entities;
            Subscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || toolbarView == null)
            {
                return;
            }

            toolbarView.ExpandedChanged += OnExpandedChanged;
            toolbarView.UndoRequested += OnUndoRequested;
            toolbarView.RedoRequested += OnRedoRequested;
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
            RefreshHistoryButtons();
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (toolbarView != null)
            {
                toolbarView.ExpandedChanged -= OnExpandedChanged;
                toolbarView.UndoRequested -= OnUndoRequested;
                toolbarView.RedoRequested -= OnRedoRequested;
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

        private void OnExpandedChanged(bool expanded)
        {
            if (expanded)
            {
                return;
            }

            inputController?.CancelPending();
            ClearPreview();
        }

        private void OnUndoRequested()
        {
            inputController?.CancelPending();
            editController?.Undo();
            RefreshHistoryButtons();
        }

        private void OnRedoRequested()
        {
            inputController?.CancelPending();
            editController?.Redo();
            RefreshHistoryButtons();
        }

        private void RefreshHistoryButtons()
        {
            toolbarView?.SetHistoryAvailability(
                editController != null && editController.CanUndo,
                editController != null && editController.CanRedo);
        }

        private void OnEditHoverChanged(IWorldCellSelection selection)
        {
            if (inputController != null && inputController.IsPending)
            {
                return;
            }

            RefreshPreview(selection, toolState?.Current ?? default);
        }

        private void OnPendingSelectionChanged(
            IWorldCellSelection selection,
            WorldEditToolSnapshot tool)
        {
            var executable = RefreshPreview(selection, tool);
            inputController?.SetPendingExecutable(executable);
        }

        private void OnExecutionRequested(
            IWorldCellSelection selection,
            WorldEditToolSnapshot tool)
        {
            if (!RefreshPreview(selection, tool))
            {
                inputController?.SetPendingExecutable(false);
                return;
            }

            var applied = tool.IsEntityTool
                ? entityEditController != null
                    && entityEditController.Apply(
                        tool.EntityDefinition,
                        selection)
                : ApplyAction(selection, tool.Action);
            if (!applied)
            {
                var executable = RefreshPreview(selection, tool);
                inputController?.SetPendingExecutable(executable);
                return;
            }

            ClearPreview();
            inputController?.CompletePendingExecution();
        }

        private bool RefreshPreview(
            IWorldCellSelection selection,
            WorldEditToolSnapshot tool)
        {
            var world = editController?.BoundWorld;
            if (selection == null || world == null || !tool.IsReady)
            {
                ClearPreview();
                return false;
            }

            if (tool.IsEntityTool)
            {
                var preview = entityEditController?.Evaluate(
                    tool.EntityDefinition,
                    selection);
                if (preview == null)
                {
                    ClearPreview();
                    return false;
                }

                selectionState.ReplaceEditPreview(
                    CreateSelection(world, preview.PrimaryCells),
                    CreateSelection(world, preview.SecondaryCells),
                    CreateSelection(world, preview.InvalidCells));
                entityEditController.ShowPreview(
                    tool.EntityDefinition,
                    preview);
                return preview.CanExecute;
            }

            entityEditController?.ClearPreview();
            if (tool.Action.PropertyGroup == WorldEditPropertyGroup.Road)
            {
                return RefreshRoadPreview(
                    world,
                    selection,
                    tool.Action.RoadOperation);
            }

            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, world);
            selectionState.ReplaceEditPreview(
                CreateSelection(world, selectedCells),
                null,
                null);
            return tool.Action.IsSupported && selectedCells.Count != 0;
        }

        private bool ApplyAction(
            IWorldCellSelection selection,
            WorldEditAction action)
        {
            var world = editController?.BoundWorld;
            if (world == null || !action.IsSupported)
            {
                return false;
            }

            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, world);
            if (selectedCells.Count == 0)
            {
                return false;
            }

            switch (action.PropertyGroup)
            {
                case WorldEditPropertyGroup.Terrain:
                    ApplyTerrain(world, action.TerrainOperation);
                    return true;
                case WorldEditPropertyGroup.Biome:
                    ApplyBiome(world, action.Biome);
                    return true;
                case WorldEditPropertyGroup.Road:
                    return ApplyRoad(
                        world,
                        action.RoadOperation);
                default:
                    return false;
            }
        }

        private bool RefreshRoadPreview(
            WorldData world,
            IWorldCellSelection selection,
            RoadEditOperation operation)
        {
            selectedCells.Clear();
            validCells.Clear();
            invalidCells.Clear();
            selection.CopyCellsTo(selectedCells, world);
            var entities = entityManager?.Entities;
            for (var index = 0; index < selectedCells.Count; index++)
            {
                var coordinate = selectedCells[index];
                var cell = world.GetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if (operation == RoadEditOperation.Remove)
                {
                    if (cell.HasRoad)
                    {
                        validCells.Add(coordinate);
                    }

                    continue;
                }

                if (IsTopGroundSurface(
                        entityManager?.Runtime,
                        world,
                        coordinate)
                    && (entities == null
                        || !entities.HasBuildingInColumn(
                            coordinate.X,
                            coordinate.Z)))
                {
                    validCells.Add(coordinate);
                }
                else
                {
                    invalidCells.Add(coordinate);
                }
            }

            selectionState.ReplaceEditPreview(
                CreateSelection(world, validCells),
                null,
                CreateSelection(world, invalidCells));
            return validCells.Count != 0;
        }

        private bool ApplyRoad(
            WorldData world,
            RoadEditOperation operation)
        {
            var transaction = editController.BeginTransaction();
            var changed = false;
            var eligible = false;
            try
            {
                var entities = entityManager?.Entities;
                for (var index = 0; index < selectedCells.Count; index++)
                {
                    var coordinate = selectedCells[index];
                    var cell = world.GetCell(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z);
                    if (operation == RoadEditOperation.Remove)
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
                            Type = RoadType.Basic,
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

                return eligible;
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

        private void ApplyTerrain(
            WorldData world,
            TerrainEditOperation operation)
        {
            selectedColumns.Clear();
            selectedTerrainCells.Clear();
            shiftedColumnBottoms.Clear();
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

                            selectedTerrainCells.Add(
                                WorldIndex.EncodeCell(
                                    world,
                                    coordinate.X,
                                    coordinate.Y,
                                    coordinate.Z));
                            var columnIndex = WorldIndex.EncodeColumn(
                                world,
                                coordinate.X,
                                coordinate.Z);
                            if (!selectedColumns.Add(columnIndex))
                            {
                                continue;
                            }

                            if (!transaction.TryGetLowestPendingSolidY(
                                    coordinate.X,
                                    coordinate.Z,
                                    out var lowestSolidY))
                            {
                                continue;
                            }

                            var shifted = operation == TerrainEditOperation.Raise
                                ? transaction.RaiseColumn(
                                    coordinate.X,
                                    coordinate.Z)
                                : transaction.LowerColumn(
                                    coordinate.X,
                                    coordinate.Z);
                            if (shifted)
                            {
                                shiftedColumnBottoms.Add(
                                    columnIndex,
                                    lowestSolidY);
                            }
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(operation),
                            operation,
                            null);
                }

                var changeSet = transaction.Commit();
                if (changeSet != null
                    && shiftedColumnBottoms.Count > 0)
                {
                    RemapShiftedSelection(world, operation);
                }
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

        private void ApplyBiome(WorldData world, BiomeType biome)
        {
            if (biome == BiomeType.None)
            {
                return;
            }

            selectedColumns.Clear();
            var transaction = editController.BeginTransaction();
            try
            {
                for (var index = 0; index < selectedCells.Count; index++)
                {
                    var coordinate = selectedCells[index];
                    var columnIndex = WorldIndex.EncodeColumn(
                        world,
                        coordinate.X,
                        coordinate.Z);
                    if (selectedColumns.Add(columnIndex))
                    {
                        transaction.SetBiome(
                            coordinate.X,
                            coordinate.Z,
                            biome);
                    }
                }

                transaction.Commit();
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

        private void RemapShiftedSelection(
            WorldData world,
            TerrainEditOperation operation)
        {
            remappedCells.Clear();
            for (var index = 0; index < selectedCells.Count; index++)
            {
                var coordinate = selectedCells[index];
                var columnIndex = WorldIndex.EncodeColumn(
                    world,
                    coordinate.X,
                    coordinate.Z);
                var remapped = coordinate;
                if (shiftedColumnBottoms.TryGetValue(
                        columnIndex,
                        out var lowestSolidY)
                    && selectedTerrainCells.Contains(
                        WorldIndex.EncodeCell(
                            world,
                            coordinate.X,
                            coordinate.Y,
                            coordinate.Z)))
                {
                    if (operation == TerrainEditOperation.Raise)
                    {
                        remapped = new CellCoordinate(
                            coordinate.X,
                            coordinate.Y + 1,
                            coordinate.Z);
                    }
                    else if (operation == TerrainEditOperation.Lower)
                    {
                        if (coordinate.Y > lowestSolidY)
                        {
                            remapped = new CellCoordinate(
                                coordinate.X,
                                coordinate.Y - 1,
                                coordinate.Z);
                        }
                    }
                }

                if (world.Contains(remapped.X, remapped.Y, remapped.Z))
                {
                    remappedCells.Add(remapped);
                }
            }

            if (remappedCells.Count == 0)
            {
                selectionState.ClearEditSelected();
                return;
            }

            selectionState.ReplaceEditSelected(
                WorldCellSetSelection.Create(world, remappedCells));
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
