using System;
using System.Collections.Generic;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    public enum SurfaceInteractionType : byte
    {
        Terrain,
        Water
    }

    public enum CellSurfaceFace : byte
    {
        None = 0,
        Top = 1,
        Bottom = 2,
        NegativeX = 3,
        PositiveX = 4,
        NegativeZ = 5,
        PositiveZ = 6
    }

    public static class WorldCellIndex
    {
        public static int Encode(WorldData world, int x, int y, int z)
            => WorldIndex.EncodeCell(world, x, y, z);

    }

    public interface IWorldCellSelection
    {
        CellBounds Bounds { get; }
        bool Contains(int cellIndex, CellCoordinate coordinate);
        void CopyCellsTo(List<CellCoordinate> target, WorldData world);
    }

    public sealed class WorldCellBoxSelection : IWorldCellSelection
    {
        public CellBounds Bounds { get; }

        private WorldCellBoxSelection(CellBounds bounds)
        {
            Bounds = bounds;
        }

        public static WorldCellBoxSelection Create(
            WorldData world,
            CellBounds bounds)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var minimum = new CellCoordinate(
                Math.Clamp(bounds.Minimum.X, 0, world.Size - 1),
                Math.Clamp(bounds.Minimum.Y, 0, world.Height - 1),
                Math.Clamp(bounds.Minimum.Z, 0, world.Size - 1));
            var maximum = new CellCoordinate(
                Math.Clamp(bounds.Maximum.X, 0, world.Size - 1),
                Math.Clamp(bounds.Maximum.Y, 0, world.Height - 1),
                Math.Clamp(bounds.Maximum.Z, 0, world.Size - 1));
            return new WorldCellBoxSelection(new CellBounds(minimum, maximum));
        }

        public bool Contains(int cellIndex, CellCoordinate coordinate)
        {
            return coordinate.X >= Bounds.Minimum.X
                && coordinate.X <= Bounds.Maximum.X
                && coordinate.Y >= Bounds.Minimum.Y
                && coordinate.Y <= Bounds.Maximum.Y
                && coordinate.Z >= Bounds.Minimum.Z
                && coordinate.Z <= Bounds.Maximum.Z;
        }

        public void CopyCellsTo(
            List<CellCoordinate> target,
            WorldData world)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            for (var y = Bounds.Minimum.Y; y <= Bounds.Maximum.Y; y++)
            for (var z = Bounds.Minimum.Z; z <= Bounds.Maximum.Z; z++)
            for (var x = Bounds.Minimum.X; x <= Bounds.Maximum.X; x++)
            {
                if (world.Contains(x, y, z))
                {
                    target.Add(new CellCoordinate(x, y, z));
                }
            }
        }
    }

    public sealed class WorldCellSetSelection : IWorldCellSelection
    {
        private readonly CellCoordinate[] cells;
        private readonly HashSet<int> cellIndices;

        public CellBounds Bounds { get; }
        public int Count => cells.Length;

        private WorldCellSetSelection(
            CellCoordinate[] cells,
            HashSet<int> cellIndices,
            CellBounds bounds)
        {
            this.cells = cells;
            this.cellIndices = cellIndices;
            Bounds = bounds;
        }

        public static WorldCellSetSelection Create(
            WorldData world,
            IEnumerable<CellCoordinate> coordinates)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (coordinates == null)
            {
                throw new ArgumentNullException(nameof(coordinates));
            }

            var indices = new HashSet<int>();
            var uniqueCells = new List<CellCoordinate>();
            var minimum = new CellCoordinate(
                world.Size - 1,
                world.Height - 1,
                world.Size - 1);
            var maximum = new CellCoordinate(0, 0, 0);
            foreach (var coordinate in coordinates)
            {
                if (!world.Contains(
                        coordinate.X,
                        coordinate.Y,
                        coordinate.Z))
                {
                    continue;
                }

                var index = WorldCellIndex.Encode(
                    world,
                    coordinate.X,
                    coordinate.Y,
                    coordinate.Z);
                if (!indices.Add(index))
                {
                    continue;
                }

                uniqueCells.Add(coordinate);
                minimum = new CellCoordinate(
                    Math.Min(minimum.X, coordinate.X),
                    Math.Min(minimum.Y, coordinate.Y),
                    Math.Min(minimum.Z, coordinate.Z));
                maximum = new CellCoordinate(
                    Math.Max(maximum.X, coordinate.X),
                    Math.Max(maximum.Y, coordinate.Y),
                    Math.Max(maximum.Z, coordinate.Z));
            }

            if (uniqueCells.Count == 0)
            {
                throw new ArgumentException(
                    "The selection must contain at least one world cell.",
                    nameof(coordinates));
            }

            return new WorldCellSetSelection(
                uniqueCells.ToArray(),
                indices,
                new CellBounds(minimum, maximum));
        }

        public bool Contains(int cellIndex, CellCoordinate coordinate) =>
            cellIndices.Contains(cellIndex);

        public void CopyCellsTo(
            List<CellCoordinate> target,
            WorldData world)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            target.AddRange(cells);
        }
    }

    public readonly struct TilePickResult : IEquatable<TilePickResult>
    {
        public readonly CellCoordinate Cell;
        public readonly int CellIndex;
        public readonly SurfaceInteractionType SurfaceType;
        public readonly CellSurfaceFace Face;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitNormal;
        public readonly float Distance;

        public TilePickResult(
            CellCoordinate cell,
            int cellIndex,
            SurfaceInteractionType surfaceType,
            CellSurfaceFace face,
            Vector3 hitPoint,
            Vector3 hitNormal,
            float distance = 0f)
        {
            Cell = cell;
            CellIndex = cellIndex;
            SurfaceType = surfaceType;
            Face = face;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            Distance = distance;
        }

        public bool Equals(TilePickResult other)
        {
            return CellIndex == other.CellIndex
                && SurfaceType == other.SurfaceType
                && Face == other.Face;
        }

        public override bool Equals(object obj)
        {
            return obj is TilePickResult other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(CellIndex, SurfaceType, Face);
        }

        public override string ToString()
        {
            return $"{SurfaceType} {Face} Cell {Cell}";
        }
    }
}
