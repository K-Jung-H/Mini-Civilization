using System;
using System.Collections.Generic;

namespace MiniCivilization.World.Domain
{
    public static class WorldDataValidator
    {
        public static void Validate(WorldData world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            for (var y = 0; y < world.Height; y++)
            for (var z = 0; z < world.Size; z++)
            for (var x = 0; x < world.Size; x++)
            {
                ValidateCell(world.GetCell(x, y, z), x, y, z);
            }

            var cellCount = checked(world.Size * world.Size * world.Height);
            for (var index = 0;
                 index < world.WaterFlowSchedule.FrontierCellIndices.Count;
                 index++)
            {
                if ((uint)world.WaterFlowSchedule.FrontierCellIndices[index]
                    >= (uint)cellCount)
                {
                    throw new InvalidOperationException(
                    "Water flow frontier contains a Cell outside the world.");
                }
            }

            var entityIds = new HashSet<EntityId>();
            for (var index = 0; index < world.Entities.Count; index++)
            {
                var entity = world.Entities[index];
                if (entity == null)
                {
                    throw new InvalidOperationException(
                        "World entities cannot contain a null entry.");
                }

                if (!entity.Id.IsValid || !entity.TypeId.IsValid)
                {
                    throw new InvalidOperationException(
                        "World entity has an invalid ID or type ID.");
                }

                if (!entityIds.Add(entity.Id))
                {
                    throw new InvalidOperationException(
                        $"World contains the duplicated entity ID {entity.Id}.");
                }

                if (!world.Contains(
                        entity.AnchorCell.X,
                        entity.AnchorCell.Y,
                        entity.AnchorCell.Z))
                {
                    throw new InvalidOperationException(
                        $"Entity {entity.Id} anchor is outside the world.");
                }

                if (!Enum.IsDefined(
                        typeof(EntityDirection),
                        entity.Direction))
                {
                    throw new InvalidOperationException(
                        $"Entity {entity.Id} has an invalid direction.");
                }
            }
        }

        private static void ValidateCell(CellData cell, int x, int y, int z)
        {
            if (cell.Terrain.SolidHeight > WorldGrid.HeightStepsPerCell)
            {
                throw new InvalidOperationException(
                    $"Cell ({x}, {y}, {z}) has an invalid solid height.");
            }

            var water = cell.Water;
            if (water.Amount > WaterAmount.Full)
            {
                throw new InvalidOperationException(
                    $"Cell ({x}, {y}, {z}) has an invalid water amount.");
            }

            if (water.Amount == 0)
            {
                if (water.Role != WaterRole.None
                    || water.Type != WaterType.None
                    || water.Flow != FlowDirection.None)
                {
                    throw new InvalidOperationException(
                        $"Empty Cell ({x}, {y}, {z}) has water metadata.");
                }

                return;
            }

            if (water.Role is not WaterRole.Source and not WaterRole.Dynamic)
            {
                throw new InvalidOperationException(
                    $"Water Cell ({x}, {y}, {z}) has no valid role.");
            }

            if (water.Type is not WaterType.Pond
                and not WaterType.Lake
                and not WaterType.Sea
                and not WaterType.River)
            {
                throw new InvalidOperationException(
                    $"Water Cell ({x}, {y}, {z}) has no valid type.");
            }

            const FlowDirection validFlow = FlowDirection.East
                | FlowDirection.North
                | FlowDirection.West
                | FlowDirection.South
                | FlowDirection.Down;
            if ((water.Flow & ~validFlow) != 0)
            {
                throw new InvalidOperationException(
                    $"Water Cell ({x}, {y}, {z}) has an invalid flow direction.");
            }
        }
    }
}
