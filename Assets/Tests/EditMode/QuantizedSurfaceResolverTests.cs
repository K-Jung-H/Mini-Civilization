using MiniCivilization.World.Meshing;
using NUnit.Framework;

namespace MiniCivilization.World.Tests
{
    public sealed class QuantizedSurfaceResolverTests
    {
        [Test]
        public void SameHeight_RemainsFlat()
        {
            var profile = QuantizedSurfaceResolver.Resolve(5, 5, true);
            Assert.That(profile.OuterHeightUnits, Is.EqualTo(5));
            Assert.That(profile.VerticalDropUnits, Is.Zero);
        }

        [Test]
        public void OneStepDrop_UsesSlopeOnly()
        {
            var profile = QuantizedSurfaceResolver.Resolve(5, 4, true);
            Assert.That(profile.OuterHeightUnits, Is.EqualTo(4));
            Assert.That(profile.VerticalDropUnits, Is.Zero);
        }

        [Test]
        public void MultipleStepDrop_UsesOneSlopeStepThenVerticalDrop()
        {
            var profile = QuantizedSurfaceResolver.Resolve(5, 2, true);
            Assert.That(profile.OuterHeightUnits, Is.EqualTo(4));
            Assert.That(profile.VerticalDropUnits, Is.EqualTo(2));
        }
    }
}
