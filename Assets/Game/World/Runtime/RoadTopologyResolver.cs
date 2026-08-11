using System;
using MiniCivilization.World.Domain;
using UnityEngine;

namespace MiniCivilization.World.Runtime
{
    internal readonly struct RoadCellTopology
    {
        public readonly CellCoordinate Cell;
        public readonly RoadData Road;
        public readonly int SurfaceHeightSteps;

        public RoadCellTopology(
            CellCoordinate cell,
            RoadData road,
            int surfaceHeightSteps)
        {
            Cell = cell;
            Road = road;
            SurfaceHeightSteps = surfaceHeightSteps;
        }
    }

    internal static class RoadTopologyResolver
    {
        public static bool TryGetRoad(
            WorldRuntime runtime,
            int x,
            int z,
            out RoadCellTopology road)
        {
            road = default;
            var surface = runtime.SurfaceCache.GetSurfaceHeight(x, z);
            if (!surface.HasGround
                || !runtime.Data.TryGetCell(
                    x,
                    surface.GroundCellY,
                    z,
                    out var cell)
                || !cell.HasRoad)
            {
                return false;
            }

            road = new RoadCellTopology(
                new CellCoordinate(x, surface.GroundCellY, z),
                cell.Road,
                surface.GroundHeight);
            return true;
        }

        public static bool CanConnect(
            WorldSettingsData settings,
            int firstHeightSteps,
            int secondHeightSteps) =>
            Math.Abs(firstHeightSteps - secondHeightSteps)
            <= settings.RoadMaxHeightSteps;

        public static Vector3 ResolveCenter(
            WorldData world,
            in RoadCellTopology road) => new(
                (road.Cell.X + 0.5f) * world.CellSize,
                road.SurfaceHeightSteps * world.HeightStep,
                (road.Cell.Z + 0.5f) * world.CellSize);

        public static Vector3 ResolveSharedBoundary(
            WorldData world,
            in RoadCellTopology first,
            in RoadCellTopology second)
        {
            var x = (first.Cell.X + second.Cell.X + 1f)
                * world.CellSize * 0.5f;
            var z = (first.Cell.Z + second.Cell.Z + 1f)
                * world.CellSize * 0.5f;
            var y = (first.SurfaceHeightSteps + second.SurfaceHeightSteps)
                * world.HeightStep * 0.5f;
            return new Vector3(x, y, z);
        }
    }
}
