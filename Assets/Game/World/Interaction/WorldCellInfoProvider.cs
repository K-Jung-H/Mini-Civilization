using System.Text;
using MiniCivilization.World.Domain;
using MiniCivilization.World.WaterFlow;
using UnityEngine;

namespace MiniCivilization.World.Interaction
{
    public readonly struct WorldCellInfoSnapshot
    {
        public readonly TilePickResult Pick;
        public readonly CellView Cell;
        public readonly WaterBody WaterBody;

        public WorldCellInfoSnapshot(
            TilePickResult pick,
            CellView cell,
            WaterBody waterBody)
        {
            Pick = pick;
            Cell = cell;
            WaterBody = waterBody;
        }

        public override string ToString()
        {
            var text = new StringBuilder(320);
            text.AppendLine($"Cell: {Pick.Cell}");
            text.AppendLine($"Picked surface: {Pick.SurfaceType}");
            text.AppendLine(
                $"Terrain: {Cell.Terrain.SolidHeight}/{WorldGrid.HeightStepsPerCell} ({Cell.Terrain.Material}, {Cell.Terrain.Surface})");
            text.AppendLine(
                $"Water: amount {Cell.Water.Amount}/{WaterAmount.Full}, " +
                $"fill {Cell.WaterHeight}/{WorldGrid.HeightStepsPerCell}, " +
                $"capacity {WorldGrid.HeightStepsPerCell - Cell.Terrain.SolidHeight}/" +
                $"{WorldGrid.HeightStepsPerCell}");
            if (Cell.HasWater)
            {
                text.AppendLine(
                    $"Water role: {Cell.Water.Role}, type: {Cell.Water.Type}, flow: {Cell.Water.Flow}");
            }
            text.AppendLine($"Geology: {Cell.Terrain.Geology}, Resource: {Cell.Terrain.ResourceId}");
            text.AppendLine(
                $"Column tops: ground {Cell.SurfaceHeight.GroundHeight}, water {Cell.SurfaceHeight.WaterHeight}");
            text.AppendLine(
                $"Biome: {Cell.Environment.Biome}, Temperature: {Cell.Environment.Temperature}, " +
                $"Moisture: {Cell.Environment.Moisture}, Fertility: {Cell.Environment.Fertility}");
            text.AppendLine(
                $"Path: open {Cell.Path.OpenHeight}, water distance {Cell.Path.WaterDistance}");
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
            return new WorldCellInfoSnapshot(
                pick,
                world.Context.GetCell(pick.Cell),
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
