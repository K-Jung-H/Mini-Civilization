using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Entities
{
    public readonly struct EntityGroundSupport
    {
        public CellCoordinate TerrainCell { get; }
        public int SurfaceHeightUnits { get; }

        private EntityGroundSupport(
            CellCoordinate terrainCell,
            int surfaceHeightUnits)
        {
            TerrainCell = terrainCell;
            SurfaceHeightUnits = surfaceHeightUnits;
        }

        public static bool TryResolve(
            WorldData world,
            CellCoordinate placementCell,
            out EntityGroundSupport support)
        {
            support = default;
            if (world == null
                || !world.TryGetCell(
                    placementCell.X,
                    placementCell.Y,
                    placementCell.Z,
                    out var cell))
            {
                return false;
            }

            var solidHeight = cell.Terrain.SolidHeight;
            if (solidHeight > 0
                && solidHeight < WorldGrid.HeightStepsPerCell)
            {
                support = new EntityGroundSupport(
                    placementCell,
                    placementCell.Y * WorldGrid.HeightStepsPerCell
                        + solidHeight);
                return true;
            }

            if (solidHeight != 0 || placementCell.Y <= 0
                || !world.TryGetCell(
                    placementCell.X,
                    placementCell.Y - 1,
                    placementCell.Z,
                    out var below)
                || below.Terrain.SolidHeight
                    != WorldGrid.HeightStepsPerCell)
            {
                return false;
            }

            support = new EntityGroundSupport(
                new CellCoordinate(
                    placementCell.X,
                    placementCell.Y - 1,
                    placementCell.Z),
                placementCell.Y * WorldGrid.HeightStepsPerCell);
            return true;
        }

        public static bool TryResolveTopPlacementCell(
            WorldData world,
            CellCoordinate terrainSurfaceCell,
            out CellCoordinate placementCell)
        {
            placementCell = default;
            if (world == null
                || !world.TryGetCell(
                    terrainSurfaceCell.X,
                    terrainSurfaceCell.Y,
                    terrainSurfaceCell.Z,
                    out var terrainCell)
                || terrainCell.Terrain.SolidHeight == 0)
            {
                return false;
            }

            placementCell = terrainCell.Terrain.SolidHeight
                == WorldGrid.HeightStepsPerCell
                ? new CellCoordinate(
                    terrainSurfaceCell.X,
                    terrainSurfaceCell.Y + 1,
                    terrainSurfaceCell.Z)
                : terrainSurfaceCell;
            return world.Contains(
                placementCell.X,
                placementCell.Y,
                placementCell.Z)
                && TryResolve(world, placementCell, out _);
        }
    }
}
