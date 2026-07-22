using MiniCivilization.World.Domain;
using MiniCivilization.World.Meshing;
using NUnit.Framework;

namespace MiniCivilization.World.Tests
{
    public sealed class SurfaceHeightResolverTests
    {
        [Test]
        public void SharedCorner_UsesSameHeightForIncidentEqualHeightTerrain()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            world.SetColumnSolidHeightUnits(0, 0, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 0, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(0, 1, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(1, 1, 1, WorldMaterialIds.Grass);

            var a = SurfaceHeightResolver.ResolveVertex(world, 0, 0, 1f, 1f, SurfaceLayer.Terrain);
            var b = SurfaceHeightResolver.ResolveVertex(world, 1, 0, 0f, 1f, SurfaceLayer.Terrain);
            var c = SurfaceHeightResolver.ResolveVertex(world, 0, 1, 1f, 0f, SurfaceLayer.Terrain);

            Assert.That(a, Is.EqualTo(4));
            Assert.That(b, Is.EqualTo(a));
            Assert.That(c, Is.EqualTo(a));
        }
    }
}
