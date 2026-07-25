using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    public enum SurfaceInteractionType : byte
    {
        Terrain,
        Water,
        Waterfall
    }

    public enum SurfaceTriangleRole : byte
    {
        Core,
        Cliff,
        GapFill,
        Waterfall,
        ShoreApron,
        ApronCornerJoin,
        ApronBridge
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
        {
            if (!world.Contains(x, y, z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    $"Cell ({x}, {y}, {z}) is outside the world.");
            }

            return x + world.Size * (z + world.Size * y);
        }

        public static CellCoordinate Decode(WorldData world, int index)
        {
            var cellCount = checked(world.Size * world.Size * world.Height);
            if ((uint)index >= cellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var layerSize = world.Size * world.Size;
            var y = index / layerSize;
            var layerIndex = index - y * layerSize;
            var z = layerIndex / world.Size;
            var x = layerIndex - z * world.Size;
            return new CellCoordinate(x, y, z);
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
