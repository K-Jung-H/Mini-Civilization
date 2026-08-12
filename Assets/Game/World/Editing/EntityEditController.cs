using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Editing
{
    public sealed class EntityPlacementPreview
    {
        public bool CanExecute { get; }
        public IReadOnlyList<CellCoordinate> PrimaryCells { get; }
        public IReadOnlyList<CellCoordinate> SecondaryCells { get; }
        public IReadOnlyList<CellCoordinate> InvalidCells { get; }
        public IReadOnlyList<CellCoordinate> EntityAnchors { get; }

        internal EntityPlacementPreview(
            bool canExecute,
            IReadOnlyList<CellCoordinate> primaryCells,
            IReadOnlyList<CellCoordinate> secondaryCells,
            IReadOnlyList<CellCoordinate> invalidCells,
            IReadOnlyList<CellCoordinate> entityAnchors)
        {
            CanExecute = canExecute;
            PrimaryCells = primaryCells ?? Array.Empty<CellCoordinate>();
            SecondaryCells = secondaryCells ?? Array.Empty<CellCoordinate>();
            InvalidCells = invalidCells ?? Array.Empty<CellCoordinate>();
            EntityAnchors = entityAnchors ?? Array.Empty<CellCoordinate>();
        }
    }

    [DisallowMultipleComponent]
    public sealed class EntityEditController : MonoBehaviour
    {
        private static readonly MiniCivilization.World.Domain.EntityId
            PreviewEntityId =
            new(ulong.MaxValue);

        private EntityManager entityManager;
        private WorldEditController worldEditController;

        private readonly List<CellCoordinate> selectedCells = new();
        private readonly List<CellCoordinate> validCells = new();
        private readonly List<CellCoordinate> invalidCells = new();

        public void Configure(
            EntityManager manager,
            WorldEditController worldEditor)
        {
            entityManager = manager;
            worldEditController = worldEditor;
        }

        public EntityPlacementPreview Evaluate(
            EntityDefinition definition,
            IWorldCellSelection selection)
        {
            var runtime = entityManager?.Runtime;
            var entities = entityManager?.Entities;
            if (!TryGetTypeKey(
                    definition,
                    runtime,
                    entities,
                    selection,
                    out var typeKey))
            {
                return new EntityPlacementPreview(
                    false,
                    null,
                    null,
                    null,
                    null);
            }

            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, runtime.Data);
            if (typeKey.Category == EntityCategory.Building)
            {
                return EvaluateBuilding(
                    typeKey,
                    selectedCells[0],
                    entities);
            }

            validCells.Clear();
            invalidCells.Clear();
            for (var index = 0; index < selectedCells.Count; index++)
            {
                var coordinate = selectedCells[index];
                if (IsTopGroundSurface(runtime, coordinate))
                {
                    validCells.Add(coordinate);
                }
                else
                {
                    invalidCells.Add(coordinate);
                }
            }

            var valid = validCells.ToArray();
            return new EntityPlacementPreview(
                valid.Length != 0,
                valid,
                null,
                invalidCells.ToArray(),
                valid);
        }

        public bool Apply(
            EntityDefinition definition,
            IWorldCellSelection selection)
        {
            var runtime = entityManager?.Runtime;
            var entities = entityManager?.Entities;
            if (!TryGetTypeKey(
                    definition,
                    runtime,
                    entities,
                    selection,
                    out var typeKey))
            {
                return false;
            }

            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, runtime.Data);
            if (typeKey.Category == EntityCategory.Building)
            {
                return TryPlaceBuilding(
                    entities,
                    typeKey,
                    selectedCells[0]);
            }

            var added = false;
            for (var index = 0; index < selectedCells.Count; index++)
            {
                var coordinate = selectedCells[index];
                if (!IsTopGroundSurface(runtime, coordinate))
                {
                    continue;
                }

                entities.Add(entities.Create(typeKey, coordinate));
                added = true;
            }

            return added;
        }

        public void ShowPreview(
            EntityDefinition definition,
            EntityPlacementPreview preview)
        {
            entityManager?.Renderer?.ShowPlacementPreview(
                definition,
                preview?.EntityAnchors);
        }

        public void ClearPreview() =>
            entityManager?.Renderer?.HidePlacementPreview();

        private EntityPlacementPreview EvaluateBuilding(
            EntityTypeKey typeKey,
            CellCoordinate centerCell,
            EntityRuntime entities)
        {
            var data = new EntityData(
                PreviewEntityId,
                typeKey,
                centerCell);
            var placement = entities.EvaluateBuildingPlacement(data);
            return new EntityPlacementPreview(
                placement.CanPlace,
                Copy(placement.BuildingCells),
                Copy(placement.TerrainAnchorCells),
                Copy(placement.InvalidCells),
                new[] { centerCell });
        }

        private bool TryGetTypeKey(
            EntityDefinition definition,
            WorldRuntime runtime,
            EntityRuntime entities,
            IWorldCellSelection selection,
            out EntityTypeKey typeKey)
        {
            typeKey = default;
            if (definition == null
                || runtime == null
                || entities == null
                || selection == null
                || entityManager.Catalog == null
                || !entityManager.Catalog.TryGetTypeKey(
                    definition,
                    out typeKey))
            {
                return false;
            }

            selectedCells.Clear();
            selection.CopyCellsTo(selectedCells, runtime.Data);
            return selectedCells.Count != 0
                && (typeKey.Category != EntityCategory.Building
                    || selectedCells.Count == 1);
        }

        private bool TryPlaceBuilding(
            EntityRuntime entities,
            EntityTypeKey typeKey,
            CellCoordinate centerCell)
        {
            if (worldEditController == null)
            {
                return false;
            }

            var data = entities.Create(typeKey, centerCell);
            var placement = entities.EvaluateBuildingPlacement(data);
            if (!placement.CanPlace)
            {
                return false;
            }

            var transaction = worldEditController.BeginTransaction();
            try
            {
                for (var index = 0;
                     index < placement.RoadCells.Count;
                     index++)
                {
                    var roadCell = placement.RoadCells[index];
                    if (!transaction.SetRoad(
                            roadCell.X,
                            roadCell.Z,
                            default))
                    {
                        transaction.Rollback();
                        return false;
                    }
                }

                for (var index = 0;
                     index < placement.TerrainCorrections.Count;
                     index++)
                {
                    var correction = placement.TerrainCorrections[index];
                    transaction.SetSolidHeight(
                        correction.X,
                        correction.Z,
                        correction.TargetHeightSteps,
                        correction.Surface);
                }

                worldEditController.CommitWithoutHistory(transaction);
            }
            catch
            {
                if (!transaction.IsCompleted)
                {
                    transaction.Rollback();
                }

                throw;
            }

            entities.Add(data);
            return true;
        }

        private static CellCoordinate[] Copy(
            IReadOnlyList<CellCoordinate> source)
        {
            var copy = new CellCoordinate[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return copy;
        }

        private static bool IsTopGroundSurface(
            WorldRuntime runtime,
            CellCoordinate coordinate)
        {
            if (!runtime.Data.TryGetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    out var cell)
                || !cell.HasTerrain)
            {
                return false;
            }

            var surface = runtime.SurfaceCache.GetSurfaceHeight(
                coordinate.X,
                coordinate.Z);
            return surface.HasGround
                && !surface.HasWater
                && surface.GroundCellY == coordinate.Y;
        }
    }
}
