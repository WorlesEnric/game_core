#nullable enable
using System.Reflection;
using GameCore.Rules.Gameplay.Entities;
using NUnit.Framework;
namespace GameCore.Rules.Gameplay.P1_7c.Tests
{
    public sealed class SpawnVisibilityTests
    {
        [TestCase(null, 0)]
        [TestCase(false, 0)]
        [TestCase(true, 1)]
        public void P17c_03_SpawnUsesDefaultOrExplicitVisibility(bool? visible, int expected)
        {
            MethodInfo? method = typeof(EntityRules).GetMethod("Spawn", new[] { typeof(EntityState), typeof(bool?) });
            Assert.That(method, Is.Not.Null);
            var result = (EntityTransition)method!.Invoke(null, new object?[] { new EntityState(0, 2, 1700, 0), visible })!;
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.Visible, Is.EqualTo(expected));
            Assert.That(result.State.Variant, Is.EqualTo(2));
            Assert.That(result.State.ScaleMilli, Is.EqualTo(1700));
        }
    }
}
