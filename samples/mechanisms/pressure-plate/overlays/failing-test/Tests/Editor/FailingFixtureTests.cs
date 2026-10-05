#nullable enable
// FIXTURE (W-MECH-01 candidate-failing-test): a deliberately failing EditMode test. The staging lane must report a
// failing verdict for this package. It is never part of the real pressure plate package; make-candidate.py layers it
// only into candidate-failing-test.
using NUnit.Framework;

namespace Hollowmere.Mechanism.PressurePlate.Editor.Tests
{
    [TestFixture]
    public sealed class FailingFixtureTests
    {
        [Test]
        public void DeliberateFailure()
        {
            Assert.That(1, Is.EqualTo(2), "deliberate failure fixture");
        }
    }
}
