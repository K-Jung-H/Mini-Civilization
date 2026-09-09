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
            var column = cell.SurfaceHeight;
            var waterBody = snapshot.WaterBody;
            var waterText = cell.HasWater
                ? $"Amount: {cell.Water.Amount * WaterAmount.Unit:0.00} " +
                  $"({cell.Water.Amount}/{WaterAmount.Full})\n" +
                  $"Fill: {cell.WaterHeight}/{WorldGrid.HeightStepsPerCell}\n" +
                  $"Capacity: {WorldGrid.HeightStepsPerCell - cell.Terrain.SolidHeight}/" +
                  $"{WorldGrid.HeightStepsPerCell}\n" +
                  $"Role: {cell.Water.Role}\n" +
                  $"Type: {cell.Water.Type}\n" +
                  $"Flow: {cell.Water.Flow}\n" +
                  $"Column level: {column.WaterHeight}\n" +
                  (waterBody != null
                      ? $"Body: #{waterBody.Id}\n" +
                        $"Volume: {waterBody.VolumeUnits}\n" +
                        $"Surface cells: {waterBody.SurfaceCellCount}"
                      : "Body: None")
                : "None";

            return new WorldTileInfoViewModel(
                "Selected Cell",
                $"X {snapshot.Pick.Cell.X}   Y {snapshot.Pick.Cell.Y}   Z {snapshot.Pick.Cell.Z}",
                $"Climate: {cell.Biome.Climate}\n" +
                $"Terrain biome: {cell.Biome.Terrain}\n" +
                $"Water biome: {cell.Biome.Water}\n" +
                $"Material: {cell.Terrain.Material}\n" +
                $"Geology: {cell.Terrain.Geology}\n" +
                $"Solid fill: {cell.Terrain.SolidHeight}/{WorldGrid.HeightStepsPerCell}",
                waterText,
                $"Picked surface: {snapshot.Pick.SurfaceType}\n" +
                $"Surface type: {cell.Terrain.Surface}\n" +
                $"Column ground top: {column.GroundHeight}",
                $"Cell: {snapshot.Pick.Cell}\n" +
                $"Resource: {cell.Terrain.ResourceId}\n" +
                $"Open height: {cell.Path.OpenHeight}\n" +
                $"Water distance: {cell.Path.WaterDistance}");
        }
    }
}
