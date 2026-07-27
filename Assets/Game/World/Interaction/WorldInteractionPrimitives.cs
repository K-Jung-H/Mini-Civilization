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

    public enum SurfaceTriangleRole : byte
    {
        Core,
        Cliff,
        GapFill,
        FallingWater,
        Bottom = 7
    }

    public readonly struct SurfaceTriangleMetadata
    {
        public static readonly SurfaceTriangleMetadata NotInteractive =
            new(-1, SurfaceTriangleRole.Core, false);

        public readonly int OwnerCellIndex;
        public readonly SurfaceTriangleRole Role;
        public readonly bool IsInteractive;

        public SurfaceTriangleMetadata(
            int ownerCellIndex,
            SurfaceTriangleRole role,
            bool isInteractive)
        {
            OwnerCellIndex = ownerCellIndex;
            Role = role;
            IsInteractive = isInteractive;
        }
    }

    [Serializable]
    public struct InteractionTriangleMetadata
    {
        [SerializeField] private int ownerCellIndex;
        [SerializeField] private SurfaceInteractionType surfaceType;
        [SerializeField] private SurfaceTriangleRole role;

        public readonly int OwnerCellIndex => ownerCellIndex;
        public readonly SurfaceInteractionType SurfaceType => surfaceType;
        public readonly SurfaceTriangleRole Role => role;

        public InteractionTriangleMetadata(
            int ownerCellIndex,
            SurfaceInteractionType surfaceType,
            SurfaceTriangleRole role)
        {
            this.ownerCellIndex = ownerCellIndex;
            this.surfaceType = surfaceType;
            this.role = role;
        }
    }

    public static class WorldCellIndex
    {
        public static int Encode(WorldData world, int x, int y, int z)
            => WorldIndex.EncodeCell(world, x, y, z);

        public static CellCoordinate Decode(WorldData world, int index)
            => WorldIndex.DecodeCell(world, index);
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
        public readonly WorldChunkInteractionSurface Surface;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitNormal;

        public TilePickResult(
            CellCoordinate cell,
            int cellIndex,
            SurfaceInteractionType surfaceType,
            WorldChunkInteractionSurface surface,
            Vector3 hitPoint,
            Vector3 hitNormal)
        {
            Cell = cell;
            CellIndex = cellIndex;
            SurfaceType = surfaceType;
            Surface = surface;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
        }

        public bool Equals(TilePickResult other)
        {
            return CellIndex == other.CellIndex
                && SurfaceType == other.SurfaceType
                && Surface == other.Surface;
        }

        public override bool Equals(object obj)
        {
            return obj is TilePickResult other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(CellIndex, SurfaceType, Surface);
        }

        public override string ToString()
        {
            return $"{SurfaceType} Cell {Cell}";
        }
    }
}
