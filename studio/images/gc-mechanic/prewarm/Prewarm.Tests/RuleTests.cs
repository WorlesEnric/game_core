using NUnit.Framework;

namespace Prewarm.Tests
{
    public sealed class RuleTests
    {
        [Test]
        public void AddsSlotValues() => Assert.That(Rule.Add(2, 3), Is.EqualTo(5));
    }
}
