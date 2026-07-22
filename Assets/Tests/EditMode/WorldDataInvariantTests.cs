using System;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Hydrology;
using NUnit.Framework;

namespace MiniCivilization.World.Tests
{
    public sealed class WorldDataInvariantTests
    {
        [Test]
        public void SetCell_RejectsUnsupportedWater()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            var floatingWater = new CellData
            {
                WaterFill = 1,
                WaterMaterialId = WorldMaterialIds.FreshWater
            };

            Assert.Throws<InvalidOperationException>(() => world.SetCell(1, 2, 1, floatingWater));
            Assert.That(world.GetCell(1, 2, 1).HasWater, Is.False);
        }

        [Test]
        public void RebuildSurfaceColumn_DoesNotExposeBuriedWater()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            world.SetCell(1, 0, 1, new CellData
            {
                WaterFill = WorldGrid.HeightStepsPerCell,
                WaterMaterialId = WorldMaterialIds.FreshWater
            });
            world.SetCell(1, 1, 1, new CellData
            {
                SolidFill = WorldGrid.HeightStepsPerCell,
                MaterialId = WorldMaterialIds.Rock,
                SurfaceMaterialId = WorldMaterialIds.Rock
            });

            Assert.That(world.GetSurfaceColumn(1, 1).HasWater, Is.False);
        }

        [Test]
        public void WaterfallColumns_AreOneConnectedWaterBody()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            world.SetColumnSolidHeightUnits(1, 1, 2, WorldMaterialIds.Rock);
            world.SetColumnSolidHeightUnits(2, 1, 2, WorldMaterialIds.Rock);
            world.SetColumnWaterSurfaceUnits(1, 1, 8, WorldMaterialIds.FreshWater, CellFlags.River | CellFlags.Waterfall);
            world.SetColumnWaterSurfaceUnits(2, 1, 4, WorldMaterialIds.FreshWater, CellFlags.River);

            var bodies = WaterBodyResolver.Resolve(world);

            Assert.That(bodies.Count, Is.EqualTo(1));
            Assert.That(bodies[0].SurfaceCellCount, Is.EqualTo(2));
        }
    }
}
