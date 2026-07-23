using MiniCivilization.World.Domain;
using MiniCivilization.World.Meshing;
using NUnit.Framework;

namespace MiniCivilization.World.Tests
{
    public sealed class SurfaceHeightResolverTests
    {
        [Test]
        public void CornerHeight_ConcaveDropRaisesOnlyTheLowCellCorner()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            world.SetColumnSolidHeightUnits(0, 0, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 0, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(0, 1, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 1, 1, WorldMaterialIds.Grass);

            var a = SurfaceHeightResolver.ResolveCornerHeight(world, 0, 0, 1f, 1f, SurfaceLayer.Terrain);
            var b = SurfaceHeightResolver.ResolveCornerHeight(world, 1, 0, 0f, 1f, SurfaceLayer.Terrain);
            var c = SurfaceHeightResolver.ResolveCornerHeight(world, 0, 1, 1f, 0f, SurfaceLayer.Terrain);
            var low = SurfaceHeightResolver.ResolveCornerHeight(world, 1, 1, 0f, 0f, SurfaceLayer.Terrain);

            Assert.That(a, Is.EqualTo(5));
            Assert.That(b, Is.EqualTo(a));
            Assert.That(c, Is.EqualTo(a));
            Assert.That(low, Is.EqualTo(2));
        }

        [Test]
        public void CornerHeight_DiagonalDifferenceWithoutConcavePlateauDoesNotCreateNotch()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            world.SetColumnSolidHeightUnits(0, 0, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 0, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(0, 1, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 1, 1, WorldMaterialIds.Grass);

            var height = SurfaceHeightResolver.ResolveCornerHeight(
                world, 0, 0, 1f, 1f, SurfaceLayer.Terrain);

            Assert.That(height, Is.EqualTo(5));
        }

        [Test]
        public void CornerHeight_ConcaveDropDoesNotRequireMatchingDiagonalHeight()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            world.SetColumnSolidHeightUnits(0, 0, 3, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 0, 6, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(0, 1, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 1, 1, WorldMaterialIds.Grass);

            var height = SurfaceHeightResolver.ResolveCornerHeight(
                world, 1, 1, 0f, 0f, SurfaceLayer.Terrain);

            Assert.That(height, Is.EqualTo(2));
        }

        [Test]
        public void CornerHeight_DiagonalSaddleDoesNotCreateConcaveClosure()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            world.SetColumnSolidHeightUnits(0, 0, 1, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 0, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(0, 1, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 1, 1, WorldMaterialIds.Grass);

            var height = SurfaceHeightResolver.ResolveCornerHeight(
                world, 0, 0, 1f, 1f, SurfaceLayer.Terrain);

            Assert.That(height, Is.EqualTo(1));
        }
    }
}
