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
        [SerializeField] private WorldEditController editController;
        [SerializeField] private WorldTileSelectionState selectionState;
        [SerializeField] private WorldEditToolbarView toolbarView;

        private readonly List<CellCoordinate> selectedCells = new();
        private readonly List<CellCoordinate> remappedCells = new();
        private readonly HashSet<int> selectedColumns = new();
        private readonly HashSet<int> shiftedColumns = new();

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
                toolbarView.EditActionRequested -= OnEditActionRequested;
            }

            isSubscribed = false;
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
            shiftedColumns.Clear();
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
                            if (!current.HasSolid)
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
                        for (var index = 0; index < selectedCells.Count; index++)
                        {
                            var coordinate = selectedCells[index];
                            transaction.SetCell(
                                coordinate.X,
                                coordinate.Y,
                                coordinate.Z,
                                default);
                        }

                        break;
                    case TerrainEditOperation.Raise:
                    case TerrainEditOperation.Lower:
                        for (var index = 0; index < selectedCells.Count; index++)
                        {
                            var coordinate = selectedCells[index];
                            var columnIndex = WorldIndex.EncodeColumn(
                                world,
                                coordinate.X,
                                coordinate.Z);
                            if (!selectedColumns.Add(columnIndex))
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
                                shiftedColumns.Add(columnIndex);
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
                    && shiftedColumns.Count > 0)
                {
                    RemapShiftedSelection(
                        world,
                        operation == TerrainEditOperation.Raise ? 1 : -1);
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

        private void RemapShiftedSelection(WorldData world, int deltaY)
        {
            remappedCells.Clear();
            for (var index = 0; index < selectedCells.Count; index++)
            {
                var coordinate = selectedCells[index];
                var columnIndex = WorldIndex.EncodeColumn(
                    world,
                    coordinate.X,
                    coordinate.Z);
                var remapped = shiftedColumns.Contains(columnIndex)
                    ? new CellCoordinate(
                        coordinate.X,
                        coordinate.Y + deltaY,
                        coordinate.Z)
                    : coordinate;
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
            current.Material = CellMaterialType.Soil;
            current.Surface = SurfaceType.Ground;
            current.Geology = current.Geology != CellMaterialType.None
                ? current.Geology
                : CellMaterialType.Rock;
            current.SolidFill = (byte)WorldGrid.HeightStepsPerCell;
            current.Water = WaterType.None;
            current.WaterFill = 0;
            current.Flags &= ~(CellFlags.River | CellFlags.Waterfall);
            current.Flags |= CellFlags.Generated;
            return current;
        }
    }
}
