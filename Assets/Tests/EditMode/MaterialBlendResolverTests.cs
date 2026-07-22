using MiniCivilization.World.Definitions;
using MiniCivilization.World.Domain;
using MiniCivilization.World.Meshing;
using NUnit.Framework;
using UnityEngine;

namespace MiniCivilization.World.Tests
{
    public sealed class MaterialBlendResolverTests
    {
        [Test]
        public void SharedTerrainEdge_BlendsBothCellAppearancesEqually()
        {
            var world = new WorldData(4, 4, 2, 2, 2, 1);
            world.SetColumnSolidHeightUnits(1, 1, 5, WorldMaterialIds.Grass);
            world.SetColumnSolidHeightUnits(2, 1, 5, WorldMaterialIds.Sand);
            world.RebuildAllSurfaceColumns();

            var appearance = MaterialBlendResolver.ResolveTerrain(world, null, 1, 1, 1f, 0.5f);
            var expected = Color.Lerp(
                DefaultSurfacePalette.ResolveTerrain(WorldMaterialIds.Grass).Albedo,
                DefaultSurfacePalette.ResolveTerrain(WorldMaterialIds.Sand).Albedo,
                0.5f);

            Assert.That(appearance.Albedo.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(appearance.Albedo.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(appearance.Albedo.b, Is.EqualTo(expected.b).Within(0.0001f));
        }
    }
}
