using MiniCivilization.World.Domain;

namespace MiniCivilization.World.Interaction
{
    public readonly struct WorldTileInfoViewModel
    {
        public readonly string Title;
        public readonly string Coordinate;
        public readonly string Terrain;
        public readonly string Water;
        public readonly string Surface;
        public readonly string Debug;

        public WorldTileInfoViewModel(
            string title,
            string coordinate,
            string terrain,
            string water,
            string surface,
            string debug)
        {
            Title = title;
            Coordinate = coordinate;
            Terrain = terrain;
            Water = water;
            Surface = surface;
            Debug = debug;
        }

        public static WorldTileInfoViewModel FromSnapshot(
            in WorldCellInfoSnapshot snapshot)
        {
            var cell = snapshot.Cell;
            var column = snapshot.Column;
            var environment = snapshot.Environment;
            var waterBody = snapshot.WaterBody;
            var waterText = cell.HasWater
                ? $"Type: {cell.Water.Type}\n" +
                  $"Amount: {cell.Water.Amount * WaterAmount.Unit:0.00} " +
                  $"({cell.Water.Amount}/{WaterAmount.Full})\n" +
                  $"Fill: {cell.WaterFill}/{WorldGrid.HeightStepsPerCell}\n" +
                  $"Capacity: {WorldGrid.HeightStepsPerCell - cell.SolidFill}/" +
                  $"{WorldGrid.HeightStepsPerCell}\n" +
                  $"Role: {cell.Water.Role}\n" +
                  $"Flow: {cell.Water.Direction}\n" +
                  $"Column level: {column.WaterTopUnits}\n" +
                  (waterBody != null
                      ? $"Body: #{waterBody.Id} {waterBody.Type}\n" +
                        $"Volume: {waterBody.VolumeUnits}\n" +
                        $"Surface cells: {waterBody.SurfaceCellCount}"
                      : "Body: None")
                : "None";

            return new WorldTileInfoViewModel(
                "Selected Cell",
                $"X {snapshot.Pick.Cell.X}   Y {snapshot.Pick.Cell.Y}   Z {snapshot.Pick.Cell.Z}",
                $"Biome: {environment.Biome}\n" +
                $"Material: {cell.Material}\n" +
                $"Geology: {cell.Geology}\n" +
                $"Solid fill: {cell.SolidFill}/{WorldGrid.HeightStepsPerCell}\n" +
                $"Temperature: {environment.Temperature}\n" +
                $"Moisture: {environment.Moisture}\n" +
                $"Fertility: {environment.Fertility}",
                waterText,
                $"Picked surface: {snapshot.Pick.SurfaceType}\n" +
                $"Surface type: {cell.Surface}\n" +
                $"Column ground top: {column.SolidTopUnits}",
                $"Cell index: {snapshot.Pick.CellIndex}\n" +
                $"Deposit: {cell.DepositIndex}\n" +
                $"Flags: {cell.Flags}");
        }
    }
}
