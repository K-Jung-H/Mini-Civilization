using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;

namespace MiniCivilization.World.Entities
{
    public readonly struct BuildingOccupiedCell
    {
        private readonly CellOffset[] walkLinks;

        public CellOffset LocalOffset { get; }
        public IReadOnlyList<CellOffset> WalkLinks => walkLinks;

        public BuildingOccupiedCell(
            CellOffset localOffset,
            IReadOnlyList<CellOffset> walkLinks)
        {
            LocalOffset = localOffset;
            this.walkLinks = CopyWalkLinks(walkLinks);
        }

        private static CellOffset[] CopyWalkLinks(
            IReadOnlyList<CellOffset> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<CellOffset>();
            }

            var uniqueLinks = new HashSet<CellOffset>();
            var result = new CellOffset[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                var offset = source[index];
                if (offset == default || !uniqueLinks.Add(offset))
                {
                    throw new ArgumentException(
                        "Building walk links must be unique non-zero offsets.",
                        nameof(source));
                }

                result[index] = offset;
            }

            return result;
        }
    }

    public sealed class BuildingLayout
    {
        private readonly BuildingOccupiedCell[] occupiedCells;
        private readonly CellOffset[] terrainAnchorOffsets;

        public IReadOnlyList<BuildingOccupiedCell> OccupiedCells =>
            occupiedCells;
        public IReadOnlyList<CellOffset> TerrainAnchorOffsets =>
            terrainAnchorOffsets;

        public BuildingLayout(
            IReadOnlyList<BuildingOccupiedCell> occupiedCells,
            IReadOnlyList<CellOffset> terrainAnchorOffsets)
        {
            if (occupiedCells == null || occupiedCells.Count == 0)
            {
                throw new ArgumentException(
                    "A building layout requires at least one occupied Cell.",
                    nameof(occupiedCells));
            }

            this.occupiedCells = new BuildingOccupiedCell[occupiedCells.Count];
            var occupiedOffsets = new HashSet<CellOffset>();
            for (var index = 0; index < occupiedCells.Count; index++)
            {
                var cell = occupiedCells[index];
                if (!occupiedOffsets.Add(cell.LocalOffset))
                {
                    throw new ArgumentException(
                        "Building occupied Cells cannot overlap.",
                        nameof(occupiedCells));
                }

                this.occupiedCells[index] = cell;
            }

            if (terrainAnchorOffsets == null || terrainAnchorOffsets.Count == 0)
            {
                this.terrainAnchorOffsets = Array.Empty<CellOffset>();
                return;
            }

            this.terrainAnchorOffsets = new CellOffset[terrainAnchorOffsets.Count];
            var anchors = new HashSet<CellOffset>();
            for (var index = 0; index < terrainAnchorOffsets.Count; index++)
            {
                var offset = terrainAnchorOffsets[index];
                if (!anchors.Add(offset) || occupiedOffsets.Contains(offset))
                {
                    throw new ArgumentException(
                        "Building Terrain Anchors cannot overlap occupied or anchor Cells.",
                        nameof(terrainAnchorOffsets));
                }

                this.terrainAnchorOffsets[index] = offset;
            }
        }

        public CellCoordinate ToWorld(
            EntityData entity,
            CellOffset localOffset)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            var rotated = Rotate(localOffset, entity.Direction);
            return new CellCoordinate(
                checked(entity.AnchorCell.X + rotated.X),
                checked(entity.AnchorCell.Y + rotated.Y),
                checked(entity.AnchorCell.Z + rotated.Z));
        }

        public CellCoordinate ToWorldWalkLink(
            EntityData entity,
            BuildingOccupiedCell source,
            CellOffset linkOffset)
        {
            return ToWorld(entity, new CellOffset(
                checked(source.LocalOffset.X + linkOffset.X),
                checked(source.LocalOffset.Y + linkOffset.Y),
                checked(source.LocalOffset.Z + linkOffset.Z)));
        }

        private static CellOffset Rotate(
            CellOffset offset,
            EntityDirection direction)
        {
            return direction switch
            {
                EntityDirection.North => offset,
                EntityDirection.East => new CellOffset(
                    offset.Z,
                    offset.Y,
                    -offset.X),
                EntityDirection.South => new CellOffset(
                    -offset.X,
                    offset.Y,
                    -offset.Z),
                EntityDirection.West => new CellOffset(
                    -offset.Z,
                    offset.Y,
                    offset.X),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(direction))
            };
        }
    }

    public readonly struct BuildingPlacementContext
    {
        private readonly WorldData world;
        private readonly EntityRuntime entities;
        private readonly BuildingEntity building;

        internal BuildingPlacementContext(
            WorldData world,
            EntityRuntime entities,
            BuildingEntity building)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.entities = entities
                ?? throw new ArgumentNullException(nameof(entities));
            this.building = building
                ?? throw new ArgumentNullException(nameof(building));
        }

        public CellCoordinate ToWorld(CellOffset localOffset) =>
            building.Layout.ToWorld(building.Data, localOffset);

        public bool TryGetCell(CellOffset localOffset, out CellData cell)
        {
            var coordinate = ToWorld(localOffset);
            return world.TryGetCell(
                coordinate.X,
                coordinate.Y,
                coordinate.Z,
                out cell);
        }

        public bool IsBuildingOccupied(CellOffset localOffset)
        {
            var coordinate = ToWorld(localOffset);
            return entities.IsBuildingOccupied(coordinate);
        }

        public bool IsTerrainAnchored(CellOffset localOffset)
        {
            var coordinate = ToWorld(localOffset);
            return entities.IsTerrainAnchored(coordinate);
        }
    }
}
