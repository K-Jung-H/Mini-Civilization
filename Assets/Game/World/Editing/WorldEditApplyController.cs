using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using UnityEngine;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class WorldEditApplyController : MonoBehaviour
    {
        [Header("References")]
        private WorldEditController editController;
        private WorldTileSelectionState selectionState;
        private WorldEditToolbarView toolbarView;

        private readonly List<CellCoordinate> selectedCells = new();
        private readonly List<CellCoordinate> remappedCells = new();
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
            WorldEditToolbarView toolbar)
        {
            Unsubscribe();
            editController = controller;
            selectionState = selections;
            toolbarView = toolbar;
            Subscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || toolbarView == null)
            {
                return;
            }

            toolbarView.EditActionRequested += OnEditActionRequested;
            toolbarView.ExpandedChanged += OnExpandedChanged;
            toolbarView.UndoRequested += OnUndoRequested;
            toolbarView.RedoRequested += OnRedoRequested;
            if (editController != null)
            {
                editController.HistoryChanged += RefreshHistoryButtons;
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
                toolbarView.EditActionRequested -= OnEditActionRequested;
                toolbarView.ExpandedChanged -= OnExpandedChanged;
                toolbarView.UndoRequested -= OnUndoRequested;
                toolbarView.RedoRequested -= OnRedoRequested;
            }

            if (editController != null)
            {
                editController.HistoryChanged -= RefreshHistoryButtons;
            }

            isSubscribed = false;
        }

        private void OnExpandedChanged(bool expanded)
        {
            if (expanded)
            {
                return;
            }

            selectionState?.ClearEditSelected();
            editController?.ClearHistory();
            RefreshHistoryButtons();
        }

        private void OnUndoRequested()
        {
            editController?.Undo();
            RefreshHistoryButtons();
        }

        private void OnRedoRequested()
        {
            editController?.Redo();
            RefreshHistoryButtons();
        }

        private void RefreshHistoryButtons()
        {
            toolbarView?.SetHistoryAvailability(
                editController != null && editController.CanUndo,
                editController != null && editController.CanRedo);
        }

        private void OnEditActionRequested(WorldEditAction action)
        {
            var selection = selectionState != null
                ? selectionState.EditSelected
                : null;
            var world = editController != null
                ? editController.BoundWorld
                : null;
            if (!action.IsSupported || selection == null || world == null)
            {
                toolbarView?.ClearActiveEditAction();
                return;
            }

            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, world);
            if (selectedCells.Count == 0)
            {
                toolbarView?.ClearActiveEditAction();
                return;
            }

            toolbarView.SetActiveEditAction(action);
            switch (action.PropertyGroup)
            {
                case WorldEditPropertyGroup.Terrain:
                    ApplyTerrain(world, action.TerrainOperation);
                    break;
                case WorldEditPropertyGroup.Biome:
                    ApplyBiome(world, action.Biome);
                    break;
            }
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
