using System;
using System.Collections.Generic;
using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;
using MiniCivilization.World.Runtime;
using UnityEngine;
using WorldEntityId = MiniCivilization.World.Domain.EntityId;

namespace MiniCivilization.World.Presentation
{
    [DisallowMultipleComponent]
    public sealed class WorldEntityRenderer : MonoBehaviour
    {
        [SerializeField] private Transform entityRoot;

        private readonly Dictionary<WorldEntityId, EntityController> viewsByEntityId = new();
        private readonly Dictionary<RenderGroupKey, EntityController> visibleViewsByGroup = new();
        private readonly List<Entity> entities = new();
        private readonly List<DynamicEntity> movingEntities = new();
        private readonly HashSet<WorldEntityId> pendingEntityIds = new();
        private readonly HashSet<WorldEntityId> interactionEntityIds = new();
        private WorldRuntime runtime;
        private EntityCatalog catalog;

        public WorldRuntime Runtime => runtime;
        public EntityCatalog Catalog => catalog;
        public int ViewCount => viewsByEntityId.Count;

        public bool TryGetView(
            WorldEntityId id,
            out EntityController view) =>
            viewsByEntityId.TryGetValue(id, out view);

        public bool IsWaterSurface(CellCoordinate coordinate)
        {
            if (runtime == null)
            {
                return false;
            }

            var surface = runtime.SurfaceCache.GetSurfaceHeight(
                coordinate.X,
                coordinate.Z);
            return surface.HasWater && surface.WaterCellY == coordinate.Y;
        }

        public void Configure(Transform root)
        {
            entityRoot = root;
        }

        private void LateUpdate()
        {
            if (runtime == null)
            {
                return;
            }

            runtime.Entities.CopyMovingEntitiesTo(movingEntities);
            for (var index = 0; index < movingEntities.Count; index++)
            {
                var entity = movingEntities[index];
                if (viewsByEntityId.TryGetValue(entity.Id, out var view))
                {
                    ApplyRenderPose(entity, view);
                }
            }
        }

        private void OnDestroy()
        {
            Unbind();
        }

        public void Bind(
            WorldRuntime worldRuntime,
            EntityCatalog entityCatalog)
        {
            if (worldRuntime == null)
            {
                throw new ArgumentNullException(nameof(worldRuntime));
            }

            if (entityCatalog == null)
            {
                throw new ArgumentNullException(nameof(entityCatalog));
            }

            Unbind();
            entityCatalog.ValidateCatalog();
            runtime = worldRuntime;
            catalog = entityCatalog;
            runtime.Entities.Changed += OnEntitiesChanged;
            runtime.Entities.PresentationChanged += OnPresentationChanged;

            runtime.Entities.CopyEntitiesTo(entities);
            for (var index = 0; index < entities.Count; index++)
            {
                SynchronizeEntity(entities[index]);
            }
            RefreshVisualGroups();
        }

        public void Unbind()
        {
            if (runtime != null)
            {
                runtime.Entities.Changed -= OnEntitiesChanged;
                runtime.Entities.PresentationChanged -= OnPresentationChanged;
                runtime = null;
            }

            catalog = null;

            foreach (var view in viewsByEntityId.Values)
            {
                if (view == null)
                {
                    continue;
                }

                view.Unbind();
                if (Application.isPlaying)
                {
                    Destroy(view.gameObject);
                }
                else
                {
                    DestroyImmediate(view.gameObject);
                }
            }

            viewsByEntityId.Clear();
            visibleViewsByGroup.Clear();
            entities.Clear();
            movingEntities.Clear();
            pendingEntityIds.Clear();
            interactionEntityIds.Clear();
        }

        private void OnPresentationChanged(WorldEntityId id)
        {
            if (runtime == null
                || !runtime.Entities.TryGet(id, out var entity))
            {
                return;
            }

            SynchronizeEntity(entity);
            RefreshVisualGroups();
        }

        private void OnEntitiesChanged(EntityChangeSet changeSet)
        {
            if (runtime == null || changeSet == null
                || !ReferenceEquals(changeSet.World, runtime.Data))
            {
                return;
            }

            for (var index = 0; index < changeSet.RemovedEntityIds.Count; index++)
            {
                RemoveView(changeSet.RemovedEntityIds[index]);
            }

            pendingEntityIds.Clear();
            AddChangedEntities(changeSet.AddedEntityIds);
            AddChangedEntities(changeSet.MovedEntityIds);
            AddEntitiesAtAffectedCells(changeSet.AffectedCellIndices);

            foreach (var id in pendingEntityIds)
            {
                if (runtime.Entities.TryGet(id, out var entity))
                {
                    SynchronizeEntity(entity);
                }
            }
            RefreshVisualGroups();
        }

        private void AddChangedEntities(IReadOnlyList<WorldEntityId> ids)
        {
            for (var index = 0; index < ids.Count; index++)
            {
                pendingEntityIds.Add(ids[index]);
            }
        }

        private void AddEntitiesAtAffectedCells(IReadOnlyList<int> cellIndices)
        {
            for (var index = 0; index < cellIndices.Count; index++)
            {
                var coordinate = WorldIndex.DecodeCell(runtime.Data, cellIndices[index]);
                var ids = runtime.Entities.GetEntitiesAt(coordinate);
                for (var entityIndex = 0; entityIndex < ids.Count; entityIndex++)
                {
                    pendingEntityIds.Add(ids[entityIndex]);
                }
            }
        }

        private void SynchronizeEntity(Entity entity)
        {
            if (viewsByEntityId.TryGetValue(entity.Id, out var existing))
            {
                existing.RefreshState();
                ApplyRenderPose(entity, existing);
                return;
            }

            var definition = catalog.GetDefinition(entity.TypeKey);
            var prefab = definition.Prefab;

            var parent = entityRoot != null ? entityRoot : transform;
            var view = Instantiate(prefab, parent, false);
            try
            {
                view.name = $"{definition.DisplayName} [{entity.Id}]";
                view.Bind(entity, this);
                ApplyRenderPose(entity, view);
                viewsByEntityId.Add(entity.Id, view);
            }
            catch
            {
                if (Application.isPlaying)
                {
                    Destroy(view.gameObject);
                }
                else
                {
                    DestroyImmediate(view.gameObject);
                }

                throw;
            }
        }

        private void ApplyRenderPose(
            Entity entity,
            EntityController view)
        {
            var heightBasis = view.RenderHeightBasis;
            var position = ResolveCellPosition(
                entity.AnchorCell,
                heightBasis);
            if (entity is DynamicEntity { IsMoving: true } moving)
            {
                var from = ResolveCellPosition(moving.MoveFrom, heightBasis);
                var to = ResolveCellPosition(moving.MoveTo, heightBasis);
                var progress = moving.MoveProgress;
                position = moving.MoveType switch
                {
                    EntityMoveType.HeightTransition =>
                        ResolveHeightTransitionPosition(
                        from,
                        to,
                        progress,
                        view.JumpHeight),
                    _ => Vector3.Lerp(from, to, progress)
                };
            }

            view.ApplyRenderPose(position, entity.Direction);
        }

        private Vector3 ResolveCellPosition(
            CellCoordinate coordinate,
            EntityVisualMotionProfile.RenderHeightBasis heightBasis)
        {
            if (!runtime.Data.TryGetCell(
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z,
                    out var cell))
            {
                throw new InvalidOperationException(
                    $"Entity render Cell {coordinate} is outside the world.");
            }

            var heightUnits = coordinate.Y * WorldGrid.HeightStepsPerCell
                + cell.Terrain.SolidHeight;
            if (heightBasis
                    == EntityVisualMotionProfile.RenderHeightBasis.WaterSurface)
            {
                var surface = runtime.SurfaceCache.GetSurfaceHeight(
                    coordinate.X,
                    coordinate.Z);
                if (surface.HasWater
                    && surface.WaterCellY == coordinate.Y)
                {
                    heightUnits = surface.WaterHeight;
                }
                else if (surface.HasGround)
                {
                    heightUnits = surface.GroundHeight;
                }
            }

            return new Vector3(
                (coordinate.X + 0.5f) * runtime.Data.CellSize,
                heightUnits * runtime.Data.HeightStep,
                (coordinate.Z + 0.5f) * runtime.Data.CellSize);
        }

        private static Vector3 ResolveHeightTransitionPosition(
            Vector3 from,
            Vector3 to,
            float progress,
            float jumpHeight)
        {
            var position = Vector3.Lerp(from, to, progress);
            if (to.y > from.y)
            {
                position.y += jumpHeight
                    * 4f
                    * progress
                    * (1f - progress);
            }
            else if (to.y < from.y)
            {
                position.y = Mathf.Lerp(
                    from.y,
                    to.y,
                    progress * progress);
            }

            return position;
        }

        private void RefreshVisualGroups()
        {
            visibleViewsByGroup.Clear();
            interactionEntityIds.Clear();
            foreach (var pair in viewsByEntityId)
            {
                var entity = pair.Value != null
                    ? pair.Value.BoundEntity
                    : null;
                if (entity == null || !entity.InteractionTargetId.IsValid)
                {
                    continue;
                }

                interactionEntityIds.Add(entity.Id);
                interactionEntityIds.Add(entity.InteractionTargetId);
            }

            foreach (var pair in viewsByEntityId)
            {
                var view = pair.Value;
                var entity = view != null ? view.BoundEntity : null;
                if (entity == null)
                {
                    continue;
                }

                if (entity is DynamicEntity { IsMoving: true }
                    || interactionEntityIds.Contains(entity.Id))
                {
                    view.SetVisualVisible(true);
                    continue;
                }

                var key = new RenderGroupKey(entity);
                if (!visibleViewsByGroup.TryGetValue(key, out var visible))
                {
                    visibleViewsByGroup.Add(key, view);
                    view.SetVisualVisible(true);
                    continue;
                }

                if (entity.Id.CompareTo(visible.BoundEntityId) < 0)
                {
                    visible.SetVisualVisible(false);
                    visibleViewsByGroup[key] = view;
                    view.SetVisualVisible(true);
                }
                else
                {
                    view.SetVisualVisible(false);
                }
            }
        }

        private void RemoveView(WorldEntityId id)
        {
            if (!viewsByEntityId.Remove(id, out var view) || view == null)
            {
                return;
            }

            view.Unbind();
            if (Application.isPlaying)
            {
                Destroy(view.gameObject);
            }
            else
            {
                DestroyImmediate(view.gameObject);
            }
        }

        private readonly struct RenderGroupKey : IEquatable<RenderGroupKey>
        {
            private readonly CellCoordinate cell;
            private readonly EntityTypeKey typeKey;
            private readonly EntityDirection direction;
            private readonly EntityActivityId activity;
            private readonly EntityActivityPhase activityPhase;
            private readonly WorldEntityId interactionTargetId;

            public RenderGroupKey(Entity entity)
            {
                cell = entity.AnchorCell;
                typeKey = entity.TypeKey;
                direction = entity.Direction;
                activity = entity.Activity;
                activityPhase = entity.ActivityPhase;
                interactionTargetId = entity.InteractionTargetId;
            }

            public bool Equals(RenderGroupKey other) =>
                cell.Equals(other.cell)
                && typeKey.Equals(other.typeKey)
                && direction == other.direction
                && activity == other.activity
                && activityPhase == other.activityPhase
                && interactionTargetId == other.interactionTargetId;

            public override bool Equals(object obj) =>
                obj is RenderGroupKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(
                cell,
                typeKey,
                direction,
                activity,
                activityPhase,
                interactionTargetId);
        }
    }
}
