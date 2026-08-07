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

        public int Count => entitiesById.Count;

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

        public bool HasTerrainAnchorInColumn(int x, int z) =>
            world.ContainsColumn(x, z)
            && terrainAnchorColumns.Contains(WorldIndex.EncodeColumn(world, x, z));

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

            return PublishChange(
                new[] { data.Id },
                NoEntityIds,
                NoEntityIds,
                GetIndexedCells(entity));
        }

        public EntityData Create(
            EntityTypeId typeId,
            CellCoordinate anchorCell,
            EntityDirection direction = EntityDirection.North)
        {
            if (!typeId.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(typeId));
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
                typeId,
                anchorCell,
                direction);
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
            return PublishChange(
                NoEntityIds,
                new[] { id },
                NoEntityIds,
                affectedCells);
        }

        public EntityChangeSet Move(EntityId id, CellCoordinate destination)
        {
            if (!entitiesById.TryGetValue(id, out var entity))
            {
                throw new InvalidOperationException(
                    $"Entity ID {id} does not exist in the runtime.");
            }

            if (entity is not DynamicEntity dynamicEntity)
            {
                throw new InvalidOperationException(
                    $"Entity {id} is not dynamic and cannot move.");
            }

            var current = entity.AnchorCell;
            if (current.Equals(destination))
            {
                return null;
            }

            if (!CanEnter(dynamicEntity, current, destination))
            {
                return null;
            }

            RemoveEntityFromCell(id, current);
            entity.Data.MoveTo(destination);
            AddEntityToCell(id, destination);
            return PublishChange(
                NoEntityIds,
                NoEntityIds,
                new[] { id },
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

            var currentHasBuilding = TryGetBuildingCell(
                currentCell,
                out var currentBuilding);
            var nextHasBuilding = TryGetBuildingCell(
                nextCell,
                out var nextBuilding);
            if (currentHasBuilding
                && !currentBuilding.HasWalkLinkTo(nextCell))
            {
                return false;
            }

            if (nextHasBuilding
                && !nextBuilding.HasWalkLinkTo(currentCell))
            {
                return false;
            }

            return entity.CanEnterWorld(runtime, currentCell, nextCell);
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
            }
            catch
            {
                entitiesById.Remove(entity.Id);
                throw;
            }
        }

        private void RemoveRuntimeEntity(Entity entity)
        {
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
            var occupied = new BuildingCellState[layout.OccupiedCells.Count];
            var anchors = new int[layout.TerrainAnchorOffsets.Count];

            for (var index = 0; index < layout.OccupiedCells.Count; index++)
            {
                var localCell = layout.OccupiedCells[index];
                var coordinate = layout.ToWorld(building.Data, localCell.LocalOffset);
                var cellIndex = RequireWorldCell(coordinate, building.Id);
                if (buildingCells.ContainsKey(cellIndex)
                    || terrainAnchorCells.Contains(cellIndex))
                {
                    throw new InvalidOperationException(
                        $"Building {building.Id} overlaps another building Cell or Terrain Anchor.");
                }

                occupied[index] = BuildingCellState.Create(
                    building.Id,
                    coordinate,
                    layout,
                    building.Data,
                    localCell);
            }

            for (var index = 0; index < layout.TerrainAnchorOffsets.Count; index++)
            {
                var coordinate = layout.ToWorld(
                    building.Data,
                    layout.TerrainAnchorOffsets[index]);
                var cellIndex = RequireWorldCell(coordinate, building.Id);
                if (buildingCells.ContainsKey(cellIndex)
                    || terrainAnchorCells.Contains(cellIndex))
                {
                    throw new InvalidOperationException(
                        $"Building {building.Id} overlaps another building Cell or Terrain Anchor.");
                }

                anchors[index] = cellIndex;
            }

            var placement = new BuildingPlacementContext(world, this, building);
            if (!building.ValidatePlacement(placement))
            {
                throw new InvalidOperationException(
                    $"Building {building.Id} does not satisfy its placement conditions.");
            }

            for (var index = 0; index < occupied.Length; index++)
            {
                var state = occupied[index];
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

        private void RemoveBuilding(BuildingEntity building)
        {
            var layout = building.Layout;
            for (var index = 0; index < layout.OccupiedCells.Count; index++)
            {
                var coordinate = layout.ToWorld(
                    building.Data,
                    layout.OccupiedCells[index].LocalOffset);
                var cellIndex = WorldIndex.EncodeCell(
                    world,
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                buildingCells.Remove(cellIndex);
                RemoveEntityFromCell(building.Id, coordinate);
            }

            for (var index = 0; index < layout.TerrainAnchorOffsets.Count; index++)
            {
                var coordinate = layout.ToWorld(
                    building.Data,
                    layout.TerrainAnchorOffsets[index]);
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

            var cells = new CellCoordinate[building.Layout.OccupiedCells.Count];
            for (var index = 0; index < cells.Length; index++)
            {
                cells[index] = building.Layout.ToWorld(
                    building.Data,
                    building.Layout.OccupiedCells[index].LocalOffset);
            }

            return cells;
        }

        private EntityChangeSet PublishChange(
            IReadOnlyList<EntityId> added,
            IReadOnlyList<EntityId> removed,
            IReadOnlyList<EntityId> moved,
            IReadOnlyList<CellCoordinate> affectedCells)
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
                affectedChunks);
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

        private static int CompareEntities(Entity left, Entity right) =>
            left.Id.CompareTo(right.Id);

        private readonly struct BuildingCellState
        {
            private readonly CellCoordinate[] walkLinks;

            public EntityId BuildingId { get; }
            public CellCoordinate Coordinate { get; }

            private BuildingCellState(
                EntityId buildingId,
                CellCoordinate coordinate,
                CellCoordinate[] walkLinks)
            {
                BuildingId = buildingId;
                Coordinate = coordinate;
                this.walkLinks = walkLinks;
            }

            public static BuildingCellState Create(
                EntityId buildingId,
                CellCoordinate coordinate,
                BuildingLayout layout,
                EntityData buildingData,
                BuildingOccupiedCell source)
            {
                var links = new CellCoordinate[source.WalkLinks.Count];
                for (var index = 0; index < links.Length; index++)
                {
                    links[index] = layout.ToWorldWalkLink(
                        buildingData,
                        source,
                        source.WalkLinks[index]);
                }

                return new BuildingCellState(buildingId, coordinate, links);
            }

            public bool HasWalkLinkTo(CellCoordinate destination)
            {
                for (var index = 0; index < walkLinks.Length; index++)
                {
                    if (walkLinks[index].Equals(destination))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
