using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Editing
{
    [DisallowMultipleComponent]
    public sealed class EntityEditController : MonoBehaviour
    {
        private EntityManager entityManager;
        private WorldTileSelectionState selectionState;
        private WorldEntityCatalogView catalogView;

        private readonly List<CellCoordinate> selectedCells = new();
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
            EntityManager manager,
            WorldTileSelectionState selections,
            WorldEntityCatalogView catalog)
        {
            Unsubscribe();
            entityManager = manager;
            selectionState = selections;
            catalogView = catalog;
            Subscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || catalogView == null)
            {
                return;
            }

            catalogView.CreationRequested += OnCreationRequested;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed)
            {
                return;
            }

            if (catalogView != null)
            {
                catalogView.CreationRequested -= OnCreationRequested;
            }

            isSubscribed = false;
        }

        private void OnCreationRequested(EntityDefinition definition)
        {
            var runtime = entityManager?.Runtime;
            var entities = entityManager?.Entities;
            if (definition == null
                || runtime == null
                || entities == null
                || entityManager.Catalog == null
                || !entityManager.Catalog.TryGetTypeKey(
                    definition,
                    out var entityTypeKey)
                || selectionState?.EditSelected == null)
            {
                return;
            }

            selectedCells.Clear();
            selectionState.EditSelected.CopyCellsTo(
                selectedCells,
                runtime.Data);
            for (var index = 0; index < selectedCells.Count; index++)
            {
                var coordinate = selectedCells[index];
                if (!IsTopGroundSurface(runtime, coordinate))
                {
                    continue;
                }

                var data = entities.Create(entityTypeKey, coordinate);
                entities.Add(data);
            }
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
                && surface.GroundCellY == coordinate.Y;
        }
    }
}
