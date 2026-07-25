using System.Text;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Hydrology;
using MiniCivilization.World.Runtime;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    public readonly struct WorldCellInfoSnapshot
    {
        public readonly TilePickResult Pick;
        public readonly CellData Cell;
        public readonly SurfaceColumnData Column;
        public readonly ColumnEnvironmentData Environment;
        public readonly WaterBody WaterBody;

        public WorldCellInfoSnapshot(
            TilePickResult pick,
            CellData cell,
            SurfaceColumnData column,
            ColumnEnvironmentData environment,
            WaterBody waterBody)
        {
            Pick = pick;
            Cell = cell;
            Column = column;
            Environment = environment;
            WaterBody = waterBody;
        }

        public override string ToString()
        {
            var text = new StringBuilder(320);
            text.AppendLine($"Cell: {Pick.Cell}");
            text.AppendLine($"Picked surface: {Pick.SurfaceType}");
            text.AppendLine(
                $"Solid: {Cell.SolidFill}/{WorldGrid.HeightStepsPerCell} ({Cell.Material}, {Cell.Surface})");
            text.AppendLine(
                $"Water: {Cell.WaterFill}/{WorldGrid.HeightStepsPerCell} ({Cell.Water})");
            text.AppendLine($"Geology: {Cell.Geology}, Deposit: {Cell.DepositIndex}");
            text.AppendLine($"Flags: {Cell.Flags}");
            text.AppendLine(
                $"Column tops: ground {Column.SolidTopUnits}, water {Column.WaterTopUnits}");
            text.AppendLine(
                $"Biome: {Environment.Biome}, Temperature: {Environment.Temperature}, " +
                $"Moisture: {Environment.Moisture}, Fertility: {Environment.Fertility}");
            if (WaterBody != null)
            {
                text.Append(
                    $"Water body: #{WaterBody.Id} {WaterBody.Type}, " +
                    $"volume {WaterBody.VolumeUnits}, surface cells {WaterBody.SurfaceCellCount}");
            }
            else
            {
                text.Append("Water body: none");
            }

            return text.ToString();
        }
    }

    [DisallowMultipleComponent]
    public sealed class WorldCellInfoProvider : MonoBehaviour
    {
        public WorldCellInfoSnapshot Create(
            WorldState worldState,
            in TilePickResult pick)
        {
            var world = worldState.Data;
            var cell = world.GetCell(pick.Cell.X, pick.Cell.Y, pick.Cell.Z);
            var column = world.GetSurfaceColumn(pick.Cell.X, pick.Cell.Z);
            var environment = world.GetColumnEnvironment(
                pick.Cell.X, pick.Cell.Z);
            return new WorldCellInfoSnapshot(
                pick,
                cell,
                column,
                environment,
                FindWaterBody(worldState, pick.Cell));
        }

        private static WaterBody FindWaterBody(
            WorldState worldState,
            CellCoordinate coordinate)
        {
            var bodies = worldState.WaterBodies;
            for (var bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
            {
                var body = bodies[bodyIndex];
                for (var cellIndex = 0; cellIndex < body.Cells.Count; cellIndex++)
                {
                    if (body.Cells[cellIndex].Equals(coordinate))
                    {
                        return body;
                    }
                }
            }

            return null;
        }
    }
}
