#nullable enable
using NUnit.Framework;

namespace Hollowmere.Mechanism.Lever.Rules.Tests
{
    [TestFixture]
    public sealed class LeverRulesTests
    {
        [Test]
        public void TwoSeparateTogglesLatchOnThenOff()
        {
            LeverTransition on = LeverRules.Toggle(0, 1);
            Assert.That(on.Accepted, Is.True);
            Assert.That(on.State, Is.EqualTo(1));
            LeverTransition off = LeverRules.Toggle(on.State, 1);
            Assert.That(off.Accepted, Is.True);
            Assert.That(off.State, Is.EqualTo(0));
        }

        [TestCase(-1)]
        [TestCase(2)]
        public void CorruptCommittedStateIsRefusedWithoutNormalization(int state)
        {
            LeverTransition result = LeverRules.Toggle(state, 1);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.State, Is.EqualTo(state));
            Assert.That(result.Refusal, Is.EqualTo(LeverRules.InvalidState));
        }

        [TestCase(0)]
        [TestCase(2)]
        [TestCase(int.MaxValue)]
        public void RequestCannotSmuggleSeveralTurnsOrAnEmptyTurn(int turns)
        {
            LeverTransition result = LeverRules.Toggle(1, turns);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.State, Is.EqualTo(1));
            Assert.That(result.Refusal, Is.EqualTo(LeverRules.InvalidCommand));
        }
    }
}
