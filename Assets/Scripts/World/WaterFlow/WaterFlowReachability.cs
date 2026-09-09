using System;
using MiniCivilization.World.Domain;

namespace MiniCivilization.World.WaterFlow
{
    internal static class WaterFlowReachability
    {
        public static int GetMaximumHorizontalSpreadCount(
            in WaterFlowRules rules)
        {
            if (rules.SpreadAmountLoss <= 0
                || rules.MinimumSpreadAmount > WaterAmount.Full)
            {
                return 0;
            }

            return Math.Max(
                0,
                (WaterAmount.Full - rules.MinimumSpreadAmount)
                / rules.SpreadAmountLoss);
        }

        public static int GetSafeHorizontalSpreadCount(
            in WaterFlowRules rules,
            int safetyMargin = 2) =>
            Math.Max(
                0,
                GetMaximumHorizontalSpreadCount(rules)
                - Math.Max(0, safetyMargin));

        public static bool CanFlowDown(
            int sourceY,
            in CellData belowCell,
            in WaterData belowWater) =>
            sourceY > 0
            && belowCell.Terrain.SolidHeight < WorldGrid.HeightStepsPerCell
            && belowWater.Amount < WaterAmount.Full;

        public static bool HasVerticalDropBelow(
            int sourceY,
            in CellData belowCell) =>
            sourceY > 0
            && belowCell.Terrain.SolidHeight < WorldGrid.HeightStepsPerCell;

        public static bool CanReachHorizontally(
            in CellCoordinate donorCoordinate,
            in CellData donorCell,
            in WaterData donorWater,
            in CellCoordinate targetCoordinate,
            in CellData targetCell,
            byte candidateAmount)
        {
            var donorCapacity =
                WorldGrid.HeightStepsPerCell - donorCell.Terrain.SolidHeight;
            var donorTopUnits = donorCoordinate.Y
                * WorldGrid.HeightStepsPerCell
                + donorCell.Terrain.SolidHeight
                + donorWater.Amount
                * donorCapacity
                / (float)WaterAmount.Full;
            var targetFloorUnits = targetCoordinate.Y
                * WorldGrid.HeightStepsPerCell
                + targetCell.Terrain.SolidHeight;
            return candidateAmount > 0
                && targetCell.Terrain.SolidHeight < WorldGrid.HeightStepsPerCell
                && targetFloorUnits < donorTopUnits;
        }
    }
}
