using MiniCivilization.World.Domain;
using MiniCivilization.World.Interaction;

namespace MiniCivilization.World.Editing
{
    public static class WorldEditCellSelectionResolver
    {
        public static bool TryResolve(
            WorldData world,
            in TilePickResult pick,
            WorldEditCellSelectionPolicy policy,
            out CellCoordinate cell)
        {
            cell = default;
            if (world == null
                || !world.TryGetCell(
                    pick.Cell.X,
                    pick.Cell.Y,
                    pick.Cell.Z,
                    out var pickedCell))
            {
                return false;
            }

            if (policy == WorldEditCellSelectionPolicy.SurfaceCell)
            {
                cell = pick.Cell;
                return true;
            }

            if (pickedCell.Terrain.SolidHeight
                < WorldGrid.HeightStepsPerCell)
            {
                cell = pick.Cell;
                return true;
            }

            cell = pick.Face switch
            {
                CellSurfaceFace.Top => new CellCoordinate(
                    pick.Cell.X,
                    pick.Cell.Y + 1,
                    pick.Cell.Z),
                CellSurfaceFace.Bottom => new CellCoordinate(
                    pick.Cell.X,
                    pick.Cell.Y - 1,
                    pick.Cell.Z),
                CellSurfaceFace.NegativeX => new CellCoordinate(
                    pick.Cell.X - 1,
                    pick.Cell.Y,
                    pick.Cell.Z),
                CellSurfaceFace.PositiveX => new CellCoordinate(
                    pick.Cell.X + 1,
                    pick.Cell.Y,
                    pick.Cell.Z),
                CellSurfaceFace.NegativeZ => new CellCoordinate(
                    pick.Cell.X,
                    pick.Cell.Y,
                    pick.Cell.Z - 1),
                CellSurfaceFace.PositiveZ => new CellCoordinate(
                    pick.Cell.X,
                    pick.Cell.Y,
                    pick.Cell.Z + 1),
                _ => default
            };
            return pick.Face != CellSurfaceFace.None
                && world.Contains(cell.X, cell.Y, cell.Z);
        }
    }
}
