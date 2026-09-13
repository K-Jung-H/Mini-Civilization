using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
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
            selectedCells.Clear();
            if (selection != null && entityManager?.Runtime != null)
                selection.CopyCellsTo(selectedCells, entityManager.Runtime.Data);
            return EvaluateCells(definition, selectedCells);
        }

        internal EntityPlacementPreview EvaluateCells(EntityDefinition definition, IReadOnlyList<CellCoordinate> cells)
        {
            var runtime = entityManager?.Runtime;
            var entities = entityManager?.Entities;
            if (!TryGetTypeKey(
                    definition,
                    runtime,
                    entities,
                    cells,
                    out var typeKey))
            {
                return new EntityPlacementPreview(
                    false,
                    null,
                    null,
                    null,
                    null);
            }

            if (typeKey.Category == EntityCategory.Building)
            {
                return EvaluateBuilding(
                    typeKey,
                    cells[0],
                    entities);
            }

            validCells.Clear();
            invalidCells.Clear();
            for (var index = 0; index < cells.Count; index++)
            {
                var coordinate = cells[index];
                if (HasGroundPlacementSupport(runtime.Data, coordinate))
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

        public bool Apply(EntityDefinition definition, IWorldCellSelection selection) =>
            ApplyEvaluated(definition, Evaluate(definition, selection)).Succeeded > 0;

        internal readonly struct PlacementResult
        {
            public readonly int Succeeded;
            public readonly int Failed;
            public PlacementResult(int succeeded, int failed)
            {
                Succeeded = succeeded;
                Failed = failed;
            }
        }

        internal PlacementResult ApplyEvaluated(EntityDefinition definition, EntityPlacementPreview evaluation)
        {
            var entities = entityManager?.Entities;
            if (evaluation == null || !evaluation.CanExecute || entities == null
                || definition == null || entityManager.Catalog == null
                || !entityManager.Catalog.TryGetTypeKey(definition, out var typeKey))
                return default;

            var succeeded = 0;
            var failed = typeKey.Category == EntityCategory.Building ? 0 : evaluation.InvalidCells.Count;
            if (failed > 0)
                Debug.LogWarning($"Entity placement: {failed} cells rejected by ground/water support rules.", this);
            foreach (var coordinate in evaluation.EntityAnchors)
            {
                EntityData data = null;
                try
                {
                    if (typeKey.Category == EntityCategory.Building)
                    {
                        if (!TryPlaceBuilding(entities, typeKey, coordinate))
                            throw new InvalidOperationException("Building placement was rejected.");
                    }
                    else
                    {
                        if (!ReferenceEquals(entities, entityManager.Entities)
                            || !HasGroundPlacementSupport(entityManager.Runtime.Data, coordinate))
                            throw new InvalidOperationException("Placement target is no longer available.");
                        data = entities.Create(typeKey, coordinate);
                        entities.Add(data);
                        if (!entities.TryGet(data.Id, out _))
                            throw new InvalidOperationException("Entity registration did not remain available.");
                    }
                    succeeded++;
                }
                catch (Exception error)
                {
                    failed++;
                    Debug.LogWarning($"Entity placement failed at {coordinate}: {error}", this);
                    // Add can throw from a notification after registration. Remove that attempt only.
                    if (data != null && entities.TryGet(data.Id, out _))
                    {
                        try { entities.Remove(data.Id); }
                        catch (Exception cleanupError) { Debug.LogException(cleanupError, this); }
                    }
                }
            }
            Debug.Log($"Entity placement completed: success={succeeded}, failed={failed}.", this);
            return new PlacementResult(succeeded, failed);
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
            EntitySystem entities)
        {
            var data = new EntityData(
                PreviewEntityId,
                typeKey,
                centerCell);
            var placement = entities.EvaluateBuildingPlacement(data);
            validCells.Clear();
            invalidCells.Clear();
            for (var index = 0;
                 index < placement.InvalidCells.Count;
                 index++)
            {
                invalidCells.Add(placement.InvalidCells[index]);
            }

            for (var index = 0;
                 index < placement.TerrainAnchorCells.Count;
                 index++)
            {
                var terrainAnchor = placement.TerrainAnchorCells[index];
                if (!invalidCells.Contains(terrainAnchor))
                {
                    validCells.Add(terrainAnchor);
                }
            }

            return new EntityPlacementPreview(
                placement.CanPlace,
                Copy(placement.BuildingCells),
                Copy(validCells),
                Copy(invalidCells),
                new[] { centerCell });
        }

        private bool TryGetTypeKey(
            EntityDefinition definition,
            WorldRuntime runtime,
            EntitySystem entities,
            IReadOnlyList<CellCoordinate> cells,
            out EntityTypeKey typeKey)
        {
            typeKey = default;
            if (definition == null
                || runtime == null
                || entities == null
                || cells == null
                || entityManager.Catalog == null
                || !entityManager.Catalog.TryGetTypeKey(
                    definition,
                    out typeKey))
            {
                return false;
            }

            foreach (var cell in cells)
                if (!runtime.Data.Contains(cell.X, cell.Y, cell.Z)
                    || !runtime.Data.IsChunkLoaded(cell.X, cell.Z)) return false;
            return cells.Count != 0
                && (typeKey.Category != EntityCategory.Building
                    || cells.Count == 1);
        }

        private bool TryPlaceBuilding(
            EntitySystem entities,
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

                worldEditController.CommitExternalChange(transaction, () =>
                {
                    try
                    {
                        entities.Add(data);
                    }
                    catch
                    {
                        // Add may fail while publishing an already registered entity.
                        if (entities.TryGet(data.Id, out _))
                            entities.Remove(data.Id);
                        throw;
                    }
                });
            }
            catch
            {
                if (!transaction.IsCompleted)
                {
                    transaction.Rollback();
                }

                throw;
            }

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

        private static bool HasGroundPlacementSupport(
            WorldData world,
            CellCoordinate coordinate)
        {
            return world != null
                && world.TryGetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    out var cell)
                && !cell.HasWater
                && EntityGroundSupport.TryResolve(
                    world,
                    coordinate,
                    out _);
        }
    }
}
