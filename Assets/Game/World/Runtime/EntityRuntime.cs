using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Entities;

namespace MiniCivilization.World.Runtime
{
    public sealed class EntityRuntime
    {
        private static readonly EntityId[] NoEntityIds = Array.Empty<EntityId>();

        private readonly WorldRuntime runtime;
        private readonly WorldData world;
        private readonly EntityTypeRegistry registry;
        private readonly Dictionary<EntityId, Entity> entitiesById = new();
        private readonly Dictionary<int, List<EntityId>> entityIdsByCell = new();
        private readonly Dictionary<int, BuildingCellState> buildingCells = new();
        private readonly HashSet<int> terrainAnchorCells = new();
        private readonly HashSet<int> terrainAnchorColumns = new();
        private readonly List<Entity> tickEntities = new();
        private readonly HashSet<EntityId> movingEntityIds = new();
        private readonly Dictionary<EntityId, BuildingWayLocation>
            buildingWayLocations = new();
        private readonly Dictionary<EntityId, WayMovementPlan>
            activeWayMoves = new();
        private ulong nextEntityId = 1;

        internal EntityRuntime(
            WorldRuntime runtime,
            EntityTypeRegistry registry)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            world = runtime.Data;
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));

            for (var index = 0; index < world.Entities.Count; index++)
            {
                var data = world.Entities[index];
                ReserveEntityId(data.Id);
                AddRuntimeEntity(registry.Create(data));
            }
        }

        public event Action<EntityChangeSet> Changed;
        public event Action<EntityId> PresentationChanged;

        public int Count => entitiesById.Count;
        internal WorldRuntime WorldRuntime => runtime;

        public bool TryGet(EntityId id, out Entity entity) =>
            entitiesById.TryGetValue(id, out entity);

        public void CopyEntitiesTo(List<Entity> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            foreach (var entity in entitiesById.Values)
            {
                target.Add(entity);
            }

            target.Sort(CompareEntities);
        }

        public void CopyMovingEntitiesTo(List<DynamicEntity> target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.Clear();
            foreach (var id in movingEntityIds)
            {
                if (entitiesById.TryGetValue(id, out var entity)
                    && entity is DynamicEntity dynamicEntity
                    && dynamicEntity.IsMoving)
                {
                    target.Add(dynamicEntity);
                }
            }

            target.Sort(CompareEntities);
        }

        internal void Tick(float deltaTime)
        {
            if (deltaTime < 0f
                || float.IsNaN(deltaTime)
                || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            for (var index = 0; index < tickEntities.Count; index++)
            {
                var entity = tickEntities[index];
                var previousActivity = entity.Activity;
                var previousPhase = entity.ActivityPhase;
                var previousTarget = entity.InteractionTargetId;
                entity.Tick(this, deltaTime);
                if ((previousActivity != entity.Activity
                        || previousPhase != entity.ActivityPhase
                        || previousTarget != entity.InteractionTargetId)
                    && entitiesById.TryGetValue(entity.Id, out var current)
                    && ReferenceEquals(current, entity))
                {
                    PresentationChanged?.Invoke(entity.Id);
                }
            }
        }

        public IReadOnlyList<EntityId> GetEntitiesAt(CellCoordinate coordinate)
        {
            if (!world.Contains(coordinate.X, coordinate.Y, coordinate.Z))
            {
                return NoEntityIds;
            }

            return entityIdsByCell.TryGetValue(
                WorldIndex.EncodeCell(
                    world,
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z),
                out var ids)
                ? ids
                : NoEntityIds;
        }

        public bool IsBuildingOccupied(CellCoordinate coordinate) =>
            world.Contains(coordinate.X, coordinate.Y, coordinate.Z)
            && buildingCells.ContainsKey(WorldIndex.EncodeCell(
                world,
                coordinate.X,
                coordinate.Y,
                coordinate.Z));

        public bool IsTerrainAnchored(CellCoordinate coordinate) =>
            world.Contains(coordinate.X, coordinate.Y, coordinate.Z)
            && terrainAnchorCells.Contains(WorldIndex.EncodeCell(
                world,
                coordinate.X,
                coordinate.Y,
                coordinate.Z));

        public bool IsTerrainProtected(CellCoordinate coordinate) =>
            IsBuildingOccupied(coordinate)
            || IsTerrainAnchored(coordinate);

        public bool HasTerrainAnchorInColumn(int x, int z) =>
            world.ContainsColumn(x, z)
            && terrainAnchorColumns.Contains(WorldIndex.EncodeColumn(world, x, z));

        public bool HasTerrainProtectedInColumn(int x, int z) =>
            HasBuildingInColumn(x, z)
            || HasTerrainAnchorInColumn(x, z);

        public bool HasBuildingInColumn(int x, int z)
        {
            if (!world.ContainsColumn(x, z))
            {
                return false;
            }

            for (var y = 0; y < world.Height; y++)
            {
                if (buildingCells.ContainsKey(
                    WorldIndex.EncodeCell(world, x, y, z)))
                {
                    return true;
                }
            }

            return false;
        }

        public EntityChangeSet Add(EntityData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (entitiesById.ContainsKey(data.Id))
            {
                throw new InvalidOperationException(
                    $"Entity ID {data.Id} already exists in the runtime.");
            }

            var entity = registry.Create(data);
            world.AddEntity(data);
            try
            {
                AddRuntimeEntity(entity);
            }
            catch
            {
                world.RemoveEntity(data.Id);
                throw;
            }

            if (entity is BuildingEntity && runtime.WayPointGraph != null)
            {
                runtime.RebuildWayPointGraph();
            }

            return PublishChange(
                new[] { data.Id },
                NoEntityIds,
                NoEntityIds,
                GetIndexedCells(entity),
                wayTopologyChanged: entity is BuildingEntity);
        }

        public EntityData Create(
            EntityTypeKey typeKey,
            CellCoordinate anchorCell,
            EntityDirection direction = EntityDirection.North)
        {
            if (!typeKey.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(typeKey));
            }

            if (!world.Contains(
                    anchorCell.X,
                    anchorCell.Y,
                    anchorCell.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(anchorCell));
            }

            return new EntityData(
                AllocateEntityId(),
                typeKey,
                anchorCell,
                direction);
        }

        public BuildingPlacementResult EvaluateBuildingPlacement(
            EntityData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (registry.Create(data) is not BuildingEntity building)
            {
                throw new ArgumentException(
                    $"Entity type key {data.TypeKey} is not a Building type.",
                    nameof(data));
            }

            var layout = building.Layout
                ?? throw new InvalidOperationException(
                    $"Building type {data.TypeKey} does not define a layout.");
            var hasCenterCell = false;
            var centerTerrainHeight = 0;
            for (var index = 0; index < layout.BuildingCells.Count; index++)
            {
                if (layout.BuildingCells[index].LocalOffset.Equals(default))
                {
                    hasCenterCell = true;
                    centerTerrainHeight = layout.BuildingCells[index]
                        .TerrainHeight;
                    break;
                }
            }

            if (!hasCenterCell)
            {
                throw new InvalidOperationException(
                    $"Building type {data.TypeKey} must contain Building Cell (0, 0, 0).");
            }

            var buildingWorldCells = new CellCoordinate[
                layout.BuildingCells.Count];
            var anchorWorldCells = new CellCoordinate[
                layout.TerrainAnchorCells.Count];
            var columns = new Dictionary<int, BuildingPlacementColumn>();
            var invalidCells = new HashSet<CellCoordinate>();
            var centerSurface = runtime.SurfaceCache.GetSurfaceHeight(
                data.AnchorCell.X,
                data.AnchorCell.Z);
            if (!centerSurface.HasGround)
            {
                invalidCells.Add(data.AnchorCell);
            }

            for (var index = 0; index < layout.BuildingCells.Count; index++)
            {
                var localCell = layout.BuildingCells[index];
                var coordinate = layout.ToWorld(data, localCell.LocalOffset);
                buildingWorldCells[index] = coordinate;
                if (!world.Contains(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z))
                {
                    invalidCells.Add(coordinate);
                    continue;
                }

                if (IsBuildingOccupied(coordinate)
                    || IsTerrainAnchored(coordinate))
                {
                    invalidCells.Add(coordinate);
                }

                var column = GetOrCreatePlacementColumn(
                    columns,
                    coordinate);
                var targetHeight = checked(
                    centerSurface.GroundHeight
                    + localCell.LocalOffset.Y
                        * WorldGrid.HeightStepsPerCell
                    + localCell.TerrainHeight
                    - centerTerrainHeight);
                column.SetBuildingTarget(
                    targetHeight,
                    coordinate,
                    localCell.MaxHeightAdjustmentSteps);
            }

            for (var index = 0;
                 index < layout.TerrainAnchorCells.Count;
                 index++)
            {
                var terrainAnchor = layout.TerrainAnchorCells[index];
                var coordinate = layout.ToWorld(
                    data,
                    terrainAnchor.LocalOffset);
                anchorWorldCells[index] = coordinate;
                if (!world.Contains(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z))
                {
                    invalidCells.Add(coordinate);
                    continue;
                }

                if (IsBuildingOccupied(coordinate)
                    || IsTerrainAnchored(coordinate))
                {
                    invalidCells.Add(coordinate);
                }

                var column = GetOrCreatePlacementColumn(
                    columns,
                    coordinate);
                column.RequireAnchorHeight(
                    checked((coordinate.Y + 1)
                        * WorldGrid.HeightStepsPerCell),
                    coordinate);
                if (world.GetCell(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z).Terrain.SolidHeight
                    < WorldGrid.HeightStepsPerCell)
                {
                    column.RequiresRebuild = true;
                }
            }

            var terrainCorrections = new List<BuildingTerrainCorrection>();
            var roadCells = new List<CellCoordinate>();
            foreach (var column in columns.Values)
            {
                if (!column.Surface.HasGround)
                {
                    invalidCells.Add(column.RepresentativeCell);
                    continue;
                }

                var targetHeight = column.HasBuildingTarget
                    ? column.BuildingTargetHeight
                    : Math.Max(
                        column.Surface.GroundHeight,
                        column.MinimumAnchorHeight);
                if (column.HasBuildingTarget
                    && targetHeight < column.MinimumAnchorHeight)
                {
                    invalidCells.Add(column.AnchorRepresentative);
                }

                if (targetHeight <= 0
                    || targetHeight
                        > world.Height * WorldGrid.HeightStepsPerCell)
                {
                    invalidCells.Add(column.RepresentativeCell);
                }

                column.TargetHeight = targetHeight;
                var changesTerrain = targetHeight
                    != column.Surface.GroundHeight
                    || column.RequiresRebuild;
                if (changesTerrain
                    && HasTerrainAnchorInColumn(column.X, column.Z))
                {
                    invalidCells.Add(column.RepresentativeCell);
                }

                if (changesTerrain)
                {
                    terrainCorrections.Add(new BuildingTerrainCorrection(
                        column.X,
                        column.Z,
                        column.Surface.GroundHeight,
                        targetHeight,
                        ResolveSurfaceType(column)));
                }
            }

            for (var index = 0;
                 index < layout.TerrainAnchorCells.Count;
                 index++)
            {
                var coordinate = anchorWorldCells[index];
                if (!world.Contains(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z))
                {
                    continue;
                }

                var column = columns[WorldIndex.EncodeColumn(
                    world,
                    coordinate.X,
                    coordinate.Z)];
                var correctionSteps = Math.Abs(
                    column.TargetHeight - column.Surface.GroundHeight);
                if (correctionSteps
                    > layout.TerrainAnchorCells[index]
                        .MaxHeightAdjustmentSteps)
                {
                    invalidCells.Add(coordinate);
                }
            }

            foreach (var column in columns.Values)
            {
                if (column.MaxHeightAdjustmentSteps < 0)
                {
                    continue;
                }

                var adjustmentSteps = Math.Abs(
                    column.TargetHeight - column.Surface.GroundHeight);
                if (adjustmentSteps
                    > column.MaxHeightAdjustmentSteps)
                {
                    invalidCells.Add(column.RepresentativeCell);
                }
            }

            for (var index = 0; index < anchorWorldCells.Length; index++)
            {
                var coordinate = anchorWorldCells[index];
                if (!world.Contains(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z))
                {
                    continue;
                }

                var column = columns[WorldIndex.EncodeColumn(
                    world,
                    coordinate.X,
                    coordinate.Z)];
                var projectedHeight = Math.Clamp(
                    column.TargetHeight
                        - coordinate.Y * WorldGrid.HeightStepsPerCell,
                    0,
                    WorldGrid.HeightStepsPerCell);
                if (projectedHeight < WorldGrid.HeightStepsPerCell)
                {
                    invalidCells.Add(coordinate);
                }
            }

            var buildingColumns = new HashSet<int>();
            for (var index = 0; index < buildingWorldCells.Length; index++)
            {
                var coordinate = buildingWorldCells[index];
                if (!world.ContainsColumn(coordinate.X, coordinate.Z))
                {
                    continue;
                }

                var columnIndex = WorldIndex.EncodeColumn(
                    world,
                    coordinate.X,
                    coordinate.Z);
                if (!buildingColumns.Add(columnIndex))
                {
                    continue;
                }

                var surface = runtime.SurfaceCache.GetSurfaceHeight(
                    coordinate.X,
                    coordinate.Z);
                if (!surface.HasGround
                    || !world.TryGetCell(
                        coordinate.X,
                        surface.GroundCellY,
                        coordinate.Z,
                        out var surfaceCell)
                    || !surfaceCell.HasRoad)
                {
                    continue;
                }

                var roadCell = new CellCoordinate(
                    coordinate.X,
                    surface.GroundCellY,
                    coordinate.Z);
                roadCells.Add(roadCell);
                if (IsTerrainAnchored(roadCell))
                {
                    invalidCells.Add(roadCell);
                }
            }

            var placementContext = new BuildingPlacementContext(
                world,
                this,
                building);
            if (!building.ValidatePlacement(placementContext))
            {
                invalidCells.Add(data.AnchorCell);
            }

            Array.Sort(buildingWorldCells, CompareCells);
            Array.Sort(anchorWorldCells, CompareCells);
            terrainCorrections.Sort(CompareTerrainCorrections);
            roadCells.Sort(CompareCells);
            var invalidWorldCells = new CellCoordinate[invalidCells.Count];
            invalidCells.CopyTo(invalidWorldCells);
            Array.Sort(invalidWorldCells, CompareCells);
            return new BuildingPlacementResult(
                buildingWorldCells,
                anchorWorldCells,
                terrainCorrections.ToArray(),
                roadCells.ToArray(),
                invalidWorldCells);
        }

        public EntityChangeSet Remove(EntityId id)
        {
            if (!entitiesById.TryGetValue(id, out var entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {id} does not exist in the runtime.");
            }

            var affectedCells = GetIndexedCells(entity);
            RemoveRuntimeEntity(entity);
            world.RemoveEntity(id);
            if (entity is BuildingEntity && runtime.WayPointGraph != null)
            {
                runtime.RebuildWayPointGraph();
            }

            return PublishChange(
                NoEntityIds,
                new[] { id },
                NoEntityIds,
                affectedCells,
                wayTopologyChanged: entity is BuildingEntity);
        }

        internal bool TryBeginMove(
            DynamicEntity entity,
            CellCoordinate destination,
            EntityMoveType moveType)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!Enum.IsDefined(typeof(EntityMoveType), moveType))
            {
                throw new ArgumentOutOfRangeException(nameof(moveType));
            }

            if (!entitiesById.TryGetValue(entity.Id, out var registered)
                || !ReferenceEquals(registered, entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {entity.Id} does not belong to this runtime.");
            }

            if (entity.IsMoving)
            {
                return false;
            }

            var current = entity.AnchorCell;
            if (current.Equals(destination))
            {
                return false;
            }

            if (!TryResolveMove(
                    entity,
                    current,
                    destination,
                    out var wayPlan))
            {
                return false;
            }

            if (wayPlan != null)
            {
                activeWayMoves.Add(entity.Id, wayPlan);
            }

            entity.BeginMove(destination, moveType);
            movingEntityIds.Add(entity.Id);
            PresentationChanged?.Invoke(entity.Id);
            return true;
        }

        internal EntityChangeSet CompleteMove(DynamicEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!entitiesById.TryGetValue(entity.Id, out var registered)
                || !ReferenceEquals(registered, entity)
                || !entity.IsMoving
                || !movingEntityIds.Contains(entity.Id))
            {
                throw new InvalidOperationException(
                    $"Entity ID {entity.Id} has no active movement in this runtime.");
            }

            var current = entity.AnchorCell;
            var destination = entity.MoveTo;
            RemoveEntityFromCell(entity.Id, current);
            entity.Data.MoveTo(destination);
            AddEntityToCell(entity.Id, destination);
            entity.FinishMove();
            movingEntityIds.Remove(entity.Id);
            if (activeWayMoves.Remove(entity.Id, out var wayPlan))
            {
                if (wayPlan.EndsInsideBuilding)
                {
                    buildingWayLocations[entity.Id] = wayPlan.EndLocation;
                }
                else
                {
                    buildingWayLocations.Remove(entity.Id);
                }
            }

            return PublishChange(
                NoEntityIds,
                NoEntityIds,
                new[] { entity.Id },
                new[] { current, destination });
        }

        public bool CanEnter(
            DynamicEntity entity,
            CellCoordinate currentCell,
            CellCoordinate nextCell)
        {
            if (entity == null
                || !world.Contains(
                    currentCell.X,
                    currentCell.Y,
                    currentCell.Z)
                || !world.Contains(nextCell.X, nextCell.Y, nextCell.Z))
            {
                return false;
            }

            return TryResolveMove(
                entity,
                currentCell,
                nextCell,
                out _);
        }

        internal bool TryGetBuildingWayPosition(
            EntityId id,
            out UnityEngine.Vector3 position)
        {
            if (runtime.WayPointGraph != null
                && buildingWayLocations.TryGetValue(id, out var location))
            {
                return runtime.WayPointGraph.TryGetPosition(
                    location,
                    out position);
            }

            position = default;
            return false;
        }

        internal bool TryGetActiveWayMove(
            EntityId id,
            out WayMovementPlan plan) =>
            activeWayMoves.TryGetValue(id, out plan);

        internal void RestoreBuildingWayLocations(
            WorldWayPointGraph graph)
        {
            if (graph == null)
            {
                throw new ArgumentNullException(nameof(graph));
            }

            var removed = new List<EntityId>();
            foreach (var pair in buildingWayLocations)
            {
                if (!entitiesById.ContainsKey(pair.Key)
                    || !graph.TryGetPosition(pair.Value, out _))
                {
                    removed.Add(pair.Key);
                }
            }

            for (var index = 0; index < removed.Count; index++)
            {
                buildingWayLocations.Remove(removed[index]);
            }

            foreach (var entity in entitiesById.Values)
            {
                if (entity is not DynamicEntity
                    || buildingWayLocations.ContainsKey(entity.Id)
                    || !TryGetBuildingCell(entity.AnchorCell, out _)
                    || !graph.TryGetInitialLocation(
                        world,
                        entity.AnchorCell,
                        out var location))
                {
                    continue;
                }

                buildingWayLocations.Add(entity.Id, location);
            }
        }

        private bool TryResolveMove(
            DynamicEntity entity,
            CellCoordinate currentCell,
            CellCoordinate nextCell,
            out WayMovementPlan wayPlan)
        {
            wayPlan = null;
            var currentHasBuilding = TryGetBuildingCell(currentCell, out _);
            var nextHasBuilding = TryGetBuildingCell(nextCell, out _);
            if (!currentHasBuilding && !nextHasBuilding)
            {
                return entity.CanEnterWorld(
                    runtime,
                    currentCell,
                    nextCell);
            }

            if (runtime.WayPointGraph == null)
            {
                return false;
            }

            var hasLocation = buildingWayLocations.TryGetValue(
                entity.Id,
                out var currentLocation);
            return runtime.WayPointGraph.TryPlan(
                world,
                currentCell,
                nextCell,
                hasLocation,
                currentLocation,
                out wayPlan);
        }

        private bool TryGetBuildingCell(
            CellCoordinate coordinate,
            out BuildingCellState cell)
        {
            if (!world.Contains(coordinate.X, coordinate.Y, coordinate.Z))
            {
                cell = default;
                return false;
            }

            return buildingCells.TryGetValue(
                WorldIndex.EncodeCell(
                    world,
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z),
                out cell);
        }

        private EntityId AllocateEntityId()
        {
            if (nextEntityId == 0)
            {
                throw new InvalidOperationException(
                    "The world has exhausted Entity IDs.");
            }

            var id = new EntityId(nextEntityId);
            nextEntityId = nextEntityId == ulong.MaxValue
                ? 0
                : nextEntityId + 1;
            return id;
        }

        private BuildingPlacementColumn GetOrCreatePlacementColumn(
            Dictionary<int, BuildingPlacementColumn> columns,
            CellCoordinate representativeCell)
        {
            var columnIndex = WorldIndex.EncodeColumn(
                world,
                representativeCell.X,
                representativeCell.Z);
            if (columns.TryGetValue(columnIndex, out var column))
            {
                return column;
            }

            column = new BuildingPlacementColumn(
                representativeCell,
                runtime.SurfaceCache.GetSurfaceHeight(
                    representativeCell.X,
                    representativeCell.Z));
            columns.Add(columnIndex, column);
            return column;
        }

        private SurfaceType ResolveSurfaceType(
            BuildingPlacementColumn column)
        {
            if (!column.Surface.HasGround
                || !world.TryGetCell(
                    column.X,
                    column.Surface.GroundCellY,
                    column.Z,
                    out var cell))
            {
                return SurfaceType.Ground;
            }

            return cell.Terrain.Surface;
        }

        private void ReserveEntityId(EntityId id)
        {
            if (nextEntityId == 0 || id.Value < nextEntityId)
            {
                return;
            }

            nextEntityId = id.Value == ulong.MaxValue
                ? 0
                : id.Value + 1;
        }

        private void AddRuntimeEntity(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!entitiesById.TryAdd(entity.Id, entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {entity.Id} already exists in the runtime.");
            }

            try
            {
                if (entity is BuildingEntity building)
                {
                    AddBuilding(building);
                }
                else
                {
                    AddEntityToCell(entity.Id, entity.AnchorCell);
                }

                if (entity.RequiresTick)
                {
                    tickEntities.Add(entity);
                    tickEntities.Sort(CompareEntities);
                }
            }
            catch
            {
                tickEntities.Remove(entity);
                entitiesById.Remove(entity.Id);
                throw;
            }
        }

        private void RemoveRuntimeEntity(Entity entity)
        {
            tickEntities.Remove(entity);
            movingEntityIds.Remove(entity.Id);
            activeWayMoves.Remove(entity.Id);
            buildingWayLocations.Remove(entity.Id);
            if (entity is BuildingEntity building)
            {
                RemoveBuilding(building);
            }
            else
            {
                RemoveEntityFromCell(entity.Id, entity.AnchorCell);
            }

            entitiesById.Remove(entity.Id);
        }

        private void AddBuilding(BuildingEntity building)
        {
            var layout = building.Layout
                ?? throw new InvalidOperationException(
                    $"Building {building.Id} does not define a layout.");
            var buildingStates = new BuildingCellState[
                layout.BuildingCells.Count];
            var anchors = new int[layout.TerrainAnchorCells.Count];
            var roadCells = CollectRoadCells(layout, building.Data);
            if (roadCells.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Building {building.Id} overlaps Road data. "
                    + "Roads must be removed through the world edit path before adding the Building.");
            }

            for (var index = 0; index < layout.BuildingCells.Count; index++)
            {
                var localCell = layout.BuildingCells[index];
                var coordinate = layout.ToWorld(building.Data, localCell.LocalOffset);
                var cellIndex = RequireWorldCell(coordinate, building.Id);
                if (buildingCells.ContainsKey(cellIndex)
                    || terrainAnchorCells.Contains(cellIndex))
                {
                    throw new InvalidOperationException(
                        $"Building {building.Id} overlaps another building Cell or Terrain Anchor.");
                }

                buildingStates[index] = new BuildingCellState(
                    building.Id,
                    coordinate);
            }

            for (var index = 0; index < layout.TerrainAnchorCells.Count; index++)
            {
                var coordinate = layout.ToWorld(
                    building.Data,
                    layout.TerrainAnchorCells[index].LocalOffset);
                var cellIndex = RequireWorldCell(coordinate, building.Id);
                if (buildingCells.ContainsKey(cellIndex)
                    || terrainAnchorCells.Contains(cellIndex))
                {
                    throw new InvalidOperationException(
                        $"Building {building.Id} overlaps another building Cell or Terrain Anchor.");
                }

                if (world.GetCell(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z).Terrain.SolidHeight
                    != WorldGrid.HeightStepsPerCell)
                {
                    throw new InvalidOperationException(
                        $"Building {building.Id} Terrain Anchor {coordinate} must be fully filled.");
                }

                anchors[index] = cellIndex;
            }

            var placement = new BuildingPlacementContext(world, this, building);
            if (!building.ValidatePlacement(placement))
            {
                throw new InvalidOperationException(
                    $"Building {building.Id} does not satisfy its placement conditions.");
            }

            for (var index = 0; index < buildingStates.Length; index++)
            {
                var state = buildingStates[index];
                buildingCells.Add(
                    WorldIndex.EncodeCell(
                        world,
                        state.Coordinate.X,
                        state.Coordinate.Y,
                        state.Coordinate.Z),
                    state);
                AddEntityToCell(building.Id, state.Coordinate);
            }

            for (var index = 0; index < anchors.Length; index++)
            {
                terrainAnchorCells.Add(anchors[index]);
                WorldIndex.DecodeColumn(
                    world,
                    anchors[index] % (world.Size * world.Size),
                    out var x,
                    out var z);
                terrainAnchorColumns.Add(WorldIndex.EncodeColumn(world, x, z));
            }

        }

        private List<CellCoordinate> CollectRoadCells(
            BuildingLayout layout,
            EntityData buildingData)
        {
            var result = new List<CellCoordinate>();
            var columns = new HashSet<int>();
            for (var index = 0; index < layout.BuildingCells.Count; index++)
            {
                var buildingCell = layout.ToWorld(
                    buildingData,
                    layout.BuildingCells[index].LocalOffset);
                var columnIndex = WorldIndex.EncodeColumn(
                    world,
                    buildingCell.X,
                    buildingCell.Z);
                if (!columns.Add(columnIndex))
                {
                    continue;
                }

                var surface = runtime.SurfaceCache.GetSurfaceHeight(
                    buildingCell.X,
                    buildingCell.Z);
                if (!surface.HasGround
                    || !world.TryGetCell(
                        buildingCell.X,
                        surface.GroundCellY,
                        buildingCell.Z,
                        out var cell)
                    || !cell.HasRoad)
                {
                    continue;
                }

                result.Add(new CellCoordinate(
                    buildingCell.X,
                    surface.GroundCellY,
                    buildingCell.Z));
            }

            return result;
        }

        private void RemoveBuilding(BuildingEntity building)
        {
            var layout = building.Layout;
            for (var index = 0; index < layout.BuildingCells.Count; index++)
            {
                var coordinate = layout.ToWorld(
                    building.Data,
                    layout.BuildingCells[index].LocalOffset);
                var cellIndex = WorldIndex.EncodeCell(
                    world,
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                buildingCells.Remove(cellIndex);
                RemoveEntityFromCell(building.Id, coordinate);
            }

            for (var index = 0; index < layout.TerrainAnchorCells.Count; index++)
            {
                var coordinate = layout.ToWorld(
                    building.Data,
                    layout.TerrainAnchorCells[index].LocalOffset);
                var cellIndex = WorldIndex.EncodeCell(
                    world,
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                terrainAnchorCells.Remove(cellIndex);
                var columnIndex = WorldIndex.EncodeColumn(world, coordinate.X, coordinate.Z);
                if (!HasTerrainAnchorInColumnExcept(columnIndex, cellIndex))
                {
                    terrainAnchorColumns.Remove(columnIndex);
                }
            }
        }

        private bool HasTerrainAnchorInColumnExcept(
            int columnIndex,
            int excludedCellIndex)
        {
            foreach (var cellIndex in terrainAnchorCells)
            {
                if (cellIndex == excludedCellIndex)
                {
                    continue;
                }

                WorldIndex.DecodeCell(world, cellIndex);
                if (cellIndex % (world.Size * world.Size) == columnIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private int RequireWorldCell(CellCoordinate coordinate, EntityId id)
        {
            if (!world.Contains(coordinate.X, coordinate.Y, coordinate.Z))
            {
                throw new InvalidOperationException(
                    $"Building {id} references a Cell outside the world.");
            }

            return WorldIndex.EncodeCell(
                world,
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
        }

        private void AddEntityToCell(EntityId id, CellCoordinate coordinate)
        {
            var cellIndex = WorldIndex.EncodeCell(
                world,
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            if (!entityIdsByCell.TryGetValue(cellIndex, out var ids))
            {
                ids = new List<EntityId>();
                entityIdsByCell.Add(cellIndex, ids);
            }

            ids.Add(id);
            ids.Sort();
        }

        private void RemoveEntityFromCell(EntityId id, CellCoordinate coordinate)
        {
            var cellIndex = WorldIndex.EncodeCell(
                world,
                coordinate.X,
                coordinate.Y,
                coordinate.Z);
            if (!entityIdsByCell.TryGetValue(cellIndex, out var ids)
                || !ids.Remove(id))
            {
                throw new InvalidOperationException(
                    $"Entity ID {id} is not indexed at Cell {coordinate}.");
            }

            if (ids.Count == 0)
            {
                entityIdsByCell.Remove(cellIndex);
            }
        }

        private CellCoordinate[] GetIndexedCells(Entity entity)
        {
            if (entity is not BuildingEntity building)
            {
                return new[] { entity.AnchorCell };
            }

            var cells = new CellCoordinate[building.Layout.BuildingCells.Count];
            for (var index = 0; index < cells.Length; index++)
            {
                cells[index] = building.Layout.ToWorld(
                    building.Data,
                    building.Layout.BuildingCells[index].LocalOffset);
            }

            return cells;
        }

        private EntityChangeSet PublishChange(
            IReadOnlyList<EntityId> added,
            IReadOnlyList<EntityId> removed,
            IReadOnlyList<EntityId> moved,
            IReadOnlyList<CellCoordinate> affectedCells,
            bool wayTopologyChanged = false)
        {
            var uniqueCells = new HashSet<int>();
            var chunks = new HashSet<ChunkCoordinate>();
            for (var index = 0; index < affectedCells.Count; index++)
            {
                var coordinate = affectedCells[index];
                var cellIndex = WorldIndex.EncodeCell(
                    world,
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if (!uniqueCells.Add(cellIndex))
                {
                    continue;
                }

                chunks.Add(new ChunkCoordinate(
                    coordinate.X / world.ChunkSizeX,
                    coordinate.Y / world.ChunkSizeY,
                    coordinate.Z / world.ChunkSizeZ));
            }

            var cellIndices = new int[uniqueCells.Count];
            uniqueCells.CopyTo(cellIndices);
            Array.Sort(cellIndices);
            var affectedChunks = new ChunkCoordinate[chunks.Count];
            chunks.CopyTo(affectedChunks);
            Array.Sort(affectedChunks, CompareChunks);

            var changeSet = new EntityChangeSet(
                world,
                runtime.AdvanceChangeId(),
                CopyAndSort(added),
                CopyAndSort(removed),
                CopyAndSort(moved),
                cellIndices,
                affectedChunks,
                wayTopologyChanged);
            Changed?.Invoke(changeSet);
            return changeSet;
        }

        private static EntityId[] CopyAndSort(IReadOnlyList<EntityId> ids)
        {
            var result = new EntityId[ids.Count];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = ids[index];
            }

            Array.Sort(result);
            return result;
        }

        private static int CompareChunks(
            ChunkCoordinate left,
            ChunkCoordinate right)
        {
            var y = left.Y.CompareTo(right.Y);
            if (y != 0)
            {
                return y;
            }

            var z = left.Z.CompareTo(right.Z);
            return z != 0 ? z : left.X.CompareTo(right.X);
        }

        private static int CompareCells(
            CellCoordinate left,
            CellCoordinate right)
        {
            var y = left.Y.CompareTo(right.Y);
            if (y != 0)
            {
                return y;
            }

            var z = left.Z.CompareTo(right.Z);
            return z != 0 ? z : left.X.CompareTo(right.X);
        }

        private static int CompareTerrainCorrections(
            BuildingTerrainCorrection left,
            BuildingTerrainCorrection right)
        {
            var z = left.Z.CompareTo(right.Z);
            return z != 0 ? z : left.X.CompareTo(right.X);
        }

        private static int CompareEntities(Entity left, Entity right) =>
            left.Id.CompareTo(right.Id);

        private sealed class BuildingPlacementColumn
        {
            public int X => RepresentativeCell.X;
            public int Z => RepresentativeCell.Z;
            public CellCoordinate RepresentativeCell { get; private set; }
            public CellCoordinate AnchorRepresentative { get; private set; }
            public SurfaceHeightData Surface { get; }
            public bool HasBuildingTarget { get; private set; }
            public int BuildingTargetHeight { get; private set; }
            public int MaxHeightAdjustmentSteps { get; private set; } = -1;
            public int MinimumAnchorHeight { get; private set; }
            public bool RequiresRebuild { get; set; }
            public int TargetHeight { get; set; }

            public BuildingPlacementColumn(
                CellCoordinate representativeCell,
                SurfaceHeightData surface)
            {
                RepresentativeCell = representativeCell;
                AnchorRepresentative = representativeCell;
                Surface = surface;
            }

            public void SetBuildingTarget(
                int height,
                CellCoordinate representativeCell,
                int maxHeightAdjustmentSteps)
            {
                if (HasBuildingTarget
                    && BuildingTargetHeight <= height)
                {
                    return;
                }

                HasBuildingTarget = true;
                BuildingTargetHeight = height;
                RepresentativeCell = representativeCell;
                MaxHeightAdjustmentSteps = maxHeightAdjustmentSteps;
            }

            public void RequireAnchorHeight(
                int height,
                CellCoordinate representativeCell)
            {
                if (MinimumAnchorHeight >= height)
                {
                    return;
                }

                MinimumAnchorHeight = height;
                AnchorRepresentative = representativeCell;
            }
        }

        private readonly struct BuildingCellState
        {
            public EntityId BuildingId { get; }
            public CellCoordinate Coordinate { get; }

            public BuildingCellState(
                EntityId buildingId,
                CellCoordinate coordinate)
            {
                BuildingId = buildingId;
                Coordinate = coordinate;
            }
        }
    }
}
