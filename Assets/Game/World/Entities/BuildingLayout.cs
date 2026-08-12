using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Entities
{
    public enum BuildingWayPointDirection : byte
    {
        None = 0,
        North = 1,
        East = 2,
        South = 3,
        West = 4
    }

    public readonly struct BuildingCell
    {
        public CellOffset LocalOffset { get; }

        public BuildingCell(CellOffset localOffset)
        {
            LocalOffset = localOffset;
        }
    }

    public readonly struct BuildingWayPoint
    {
        public CellOffset LocalCellOffset { get; }
        public Vector3 LocalPosition { get; }
        public BuildingWayPointDirection ExternalDirection { get; }

        public BuildingWayPoint(
            CellOffset localCellOffset,
            Vector3 localPosition,
            BuildingWayPointDirection externalDirection)
        {
            if (!IsFinite(localPosition))
            {
                throw new ArgumentOutOfRangeException(nameof(localPosition));
            }

            if (localPosition.x < -0.5f
                || localPosition.x > 0.5f
                || localPosition.y < 0f
                || localPosition.y > 1f
                || localPosition.z < -0.5f
                || localPosition.z > 0.5f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(localPosition),
                    "Building WayPoint position must stay inside its local Cell.");
            }

            if (!Enum.IsDefined(typeof(BuildingWayPointDirection), externalDirection))
            {
                throw new ArgumentOutOfRangeException(nameof(externalDirection));
            }

            LocalCellOffset = localCellOffset;
            LocalPosition = localPosition;
            ExternalDirection = externalDirection;
            if (!IsOnExternalBoundary(externalDirection, localPosition))
            {
                throw new ArgumentException(
                    "External Building WayPoint must be placed on its selected Cell boundary.",
                    nameof(localPosition));
            }
        }

        public bool IsExternal =>
            ExternalDirection != BuildingWayPointDirection.None;

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x)
            && float.IsFinite(value.y)
            && float.IsFinite(value.z);

        private static bool IsOnExternalBoundary(
            BuildingWayPointDirection direction,
            Vector3 position)
        {
            const float tolerance = 0.001f;
            return direction switch
            {
                BuildingWayPointDirection.None => true,
                BuildingWayPointDirection.North =>
                    Mathf.Abs(position.z - 0.5f) <= tolerance,
                BuildingWayPointDirection.East =>
                    Mathf.Abs(position.x - 0.5f) <= tolerance,
                BuildingWayPointDirection.South =>
                    Mathf.Abs(position.z + 0.5f) <= tolerance,
                BuildingWayPointDirection.West =>
                    Mathf.Abs(position.x + 0.5f) <= tolerance,
                _ => false
            };
        }
    }

    public readonly struct BuildingWay
    {
        public int PointA { get; }
        public int PointB { get; }
        public bool OneWay { get; }

        public BuildingWay(int pointA, int pointB, bool oneWay = false)
        {
            if (pointA < 0 || pointB < 0 || pointA == pointB)
            {
                throw new ArgumentOutOfRangeException(nameof(pointA));
            }

            PointA = pointA;
            PointB = pointB;
            OneWay = oneWay;
        }
    }

    public sealed class BuildingLayout
    {
        private readonly BuildingCell[] buildingCells;
        private readonly CellOffset[] terrainAnchorCells;
        private readonly BuildingWayPoint[] wayPoints;
        private readonly BuildingWay[] ways;

        public IReadOnlyList<BuildingCell> BuildingCells => buildingCells;
        public IReadOnlyList<CellOffset> TerrainAnchorCells =>
            terrainAnchorCells;
        public IReadOnlyList<BuildingWayPoint> WayPoints => wayPoints;
        public IReadOnlyList<BuildingWay> Ways => ways;

        public BuildingLayout(
            IReadOnlyList<BuildingCell> buildingCells,
            IReadOnlyList<CellOffset> terrainAnchorCells,
            IReadOnlyList<BuildingWayPoint> wayPoints = null,
            IReadOnlyList<BuildingWay> ways = null)
        {
            if (buildingCells == null || buildingCells.Count == 0)
            {
                throw new ArgumentException(
                    "A building layout requires at least one Building Cell.",
                    nameof(buildingCells));
            }

            this.buildingCells = new BuildingCell[buildingCells.Count];
            var buildingOffsets = new HashSet<CellOffset>();
            for (var index = 0; index < buildingCells.Count; index++)
            {
                var cell = buildingCells[index];
                if (!buildingOffsets.Add(cell.LocalOffset))
                {
                    throw new ArgumentException(
                        "Building Cells cannot overlap.",
                        nameof(buildingCells));
                }

                this.buildingCells[index] = cell;
            }

            this.terrainAnchorCells = CopyAnchors(
                terrainAnchorCells,
                buildingOffsets);
            this.wayPoints = CopyWayPoints(
                wayPoints,
                buildingOffsets);
            this.ways = CopyWays(ways, this.wayPoints.Length);
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

        public Vector3 ToWorldPosition(
            WorldData world,
            EntityData entity,
            BuildingWayPoint point)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var cell = ToWorld(entity, point.LocalCellOffset);
            var local = Rotate(point.LocalPosition, entity.Direction);
            return new Vector3(
                (cell.X + 0.5f + local.x) * world.CellSize,
                (cell.Y + local.y) * world.CellSize,
                (cell.Z + 0.5f + local.z) * world.CellSize);
        }

        public BuildingWayPointDirection ToWorldDirection(
            BuildingWayPointDirection direction,
            EntityDirection entityDirection)
        {
            if (direction == BuildingWayPointDirection.None)
            {
                return direction;
            }

            var turns = entityDirection switch
            {
                EntityDirection.North => 0,
                EntityDirection.East => 1,
                EntityDirection.South => 2,
                EntityDirection.West => 3,
                _ => throw new ArgumentOutOfRangeException(nameof(entityDirection))
            };
            var value = ((int)direction - 1 + turns) % 4;
            return (BuildingWayPointDirection)(value + 1);
        }

        private static CellOffset[] CopyAnchors(
            IReadOnlyList<CellOffset> source,
            ISet<CellOffset> buildingOffsets)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<CellOffset>();
            }

            var result = new CellOffset[source.Count];
            var anchors = new HashSet<CellOffset>();
            for (var index = 0; index < source.Count; index++)
            {
                var offset = source[index];
                if (!anchors.Add(offset) || buildingOffsets.Contains(offset))
                {
                    throw new ArgumentException(
                        "Terrain Anchor Cells cannot overlap Building or other Terrain Anchor Cells.",
                        nameof(source));
                }

                result[index] = offset;
            }

            return result;
        }

        private static BuildingWayPoint[] CopyWayPoints(
            IReadOnlyList<BuildingWayPoint> source,
            ISet<CellOffset> buildingOffsets)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<BuildingWayPoint>();
            }

            var result = new BuildingWayPoint[source.Count];
            for (var index = 0; index < source.Count; index++)
            {
                if (!buildingOffsets.Contains(source[index].LocalCellOffset))
                {
                    throw new ArgumentException(
                        "Building WayPoints must belong to a Building Cell.",
                        nameof(source));
                }

                result[index] = source[index];
            }

            return result;
        }

        private static BuildingWay[] CopyWays(
            IReadOnlyList<BuildingWay> source,
            int pointCount)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<BuildingWay>();
            }

            var result = new BuildingWay[source.Count];
            var unique = new HashSet<(int, int, bool)>();
            for (var index = 0; index < source.Count; index++)
            {
                var way = source[index];
                if ((uint)way.PointA >= pointCount
                    || (uint)way.PointB >= pointCount)
                {
                    throw new ArgumentException(
                        "Building Way references a missing WayPoint.",
                        nameof(source));
                }

                var key = way.OneWay || way.PointA < way.PointB
                    ? (way.PointA, way.PointB, way.OneWay)
                    : (way.PointB, way.PointA, way.OneWay);
                if (!unique.Add(key))
                {
                    throw new ArgumentException(
                        "Building Ways cannot contain duplicate links.",
                        nameof(source));
                }

                result[index] = way;
            }

            return result;
        }

        private static CellOffset Rotate(
            CellOffset offset,
            EntityDirection direction) => direction switch
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
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };

        private static Vector3 Rotate(
            Vector3 position,
            EntityDirection direction) => direction switch
            {
                EntityDirection.North => position,
                EntityDirection.East => new Vector3(
                    position.z,
                    position.y,
                    -position.x),
                EntityDirection.South => new Vector3(
                    -position.x,
                    position.y,
                    -position.z),
                EntityDirection.West => new Vector3(
                    -position.z,
                    position.y,
                    position.x),
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
    }

    public readonly struct BuildingTerrainCorrection
    {
        public int X { get; }
        public int Z { get; }
        public int CurrentHeightSteps { get; }
        public int TargetHeightSteps { get; }
        public SurfaceType Surface { get; }

        internal BuildingTerrainCorrection(
            int x,
            int z,
            int currentHeightSteps,
            int targetHeightSteps,
            SurfaceType surface)
        {
            X = x;
            Z = z;
            CurrentHeightSteps = currentHeightSteps;
            TargetHeightSteps = targetHeightSteps;
            Surface = surface;
        }
    }

    public sealed class BuildingPlacementResult
    {
        private readonly CellCoordinate[] buildingCells;
        private readonly CellCoordinate[] terrainAnchorCells;
        private readonly BuildingTerrainCorrection[] terrainCorrections;
        private readonly CellCoordinate[] roadCells;
        private readonly CellCoordinate[] invalidCells;

        public bool CanPlace => invalidCells.Length == 0;
        public IReadOnlyList<CellCoordinate> BuildingCells => buildingCells;
        public IReadOnlyList<CellCoordinate> TerrainAnchorCells =>
            terrainAnchorCells;
        public IReadOnlyList<BuildingTerrainCorrection> TerrainCorrections =>
            terrainCorrections;
        public IReadOnlyList<CellCoordinate> RoadCells => roadCells;
        public IReadOnlyList<CellCoordinate> InvalidCells => invalidCells;

        internal BuildingPlacementResult(
            CellCoordinate[] buildingCells,
            CellCoordinate[] terrainAnchorCells,
            BuildingTerrainCorrection[] terrainCorrections,
            CellCoordinate[] roadCells,
            CellCoordinate[] invalidCells)
        {
            this.buildingCells = buildingCells
                ?? throw new ArgumentNullException(nameof(buildingCells));
            this.terrainAnchorCells = terrainAnchorCells
                ?? throw new ArgumentNullException(nameof(terrainAnchorCells));
            this.terrainCorrections = terrainCorrections
                ?? throw new ArgumentNullException(nameof(terrainCorrections));
            this.roadCells = roadCells
                ?? throw new ArgumentNullException(nameof(roadCells));
            this.invalidCells = invalidCells
                ?? throw new ArgumentNullException(nameof(invalidCells));
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
