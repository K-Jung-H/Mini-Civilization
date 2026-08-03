using System.Text;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;
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
                $"Water: amount {Cell.Water.Amount}/{WaterAmount.Full}, " +
                $"fill {Cell.WaterFill}/{WorldGrid.HeightStepsPerCell}, " +
                $"capacity {WorldGrid.HeightStepsPerCell - Cell.SolidFill}/" +
                $"{WorldGrid.HeightStepsPerCell}");
            if (Cell.HasWater)
            {
                text.AppendLine(
                    $"Water role: {Cell.Water.Role}, flow: {Cell.Water.Direction}");
            }
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
                    $"Water body: #{WaterBody.Id}, " +
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
            WorldData world,
            WaterFlowState waterFlowState,
            in TilePickResult pick)
        {
            var cell = world.GetCell(pick.Cell.X, pick.Cell.Y, pick.Cell.Z);
            var column = world.GetSurfaceColumn(pick.Cell.X, pick.Cell.Z);
            var environment = world.GetColumnEnvironment(
                pick.Cell.X, pick.Cell.Z);
            return new WorldCellInfoSnapshot(
                pick,
                cell,
                column,
                environment,
                FindWaterBody(waterFlowState, pick.Cell));
        }

        private static WaterBody FindWaterBody(
            WaterFlowState waterFlowState,
            CellCoordinate coordinate)
        {
            return waterFlowState != null
                && waterFlowState.TryGetWaterBody(
                    coordinate.X,
                    coordinate.Z,
                    out var waterBody)
                    ? waterBody
                    : null;
        }
    }
}
