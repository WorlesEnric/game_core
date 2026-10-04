// GameCore.Rules.Gameplay.Tests - pure entity transitions (P1.1). Built by plain dotnet
// (dotnet/tests/GameCore.Rules.Gameplay.Tests) and, when the package is testable, as Unity EditMode tests.
#nullable enable
using GameCore.Rules.Gameplay.Entities;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests
{
    public sealed class EntityRulesTests
    {
        private static EntityState Alive(int variant = 0) => EntityRules.Placed(variant, 1000, true, true);

        private static EntityState Dead(int variant = 0) => EntityRules.Placed(variant, 1000, true, false);

        [Test]
        public void Spawn_MakesADeadEntityAliveAndVisible_KeepingItsVariantAndScale()
        {
            EntityState hidden = new EntityState(0, 2, 1500, 0);
            EntityTransition result = EntityRules.Spawn(hidden);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State.Alive, Is.EqualTo(1));
            Assert.That(result.State.Visible, Is.EqualTo(1));
            Assert.That(result.State.Variant, Is.EqualTo(2));
            Assert.That(result.State.ScaleMilli, Is.EqualTo(1500));
        }

        [Test]
        public void Spawn_RefusesAnAliveEntity_AndChangesNothing()
        {
            EntityState alive = Alive(1);
            EntityTransition result = EntityRules.Spawn(alive);
            Assert.That(result.Refusal, Is.EqualTo(EntityRefusal.AlreadyAlive));
            Assert.That(result.State.Variant, Is.EqualTo(1));
            Assert.That(result.State.Alive, Is.EqualTo(1));
        }

        [Test]
        public void Despawn_IsLogical_AndRespawnRestoresTheOverriddenState()
        {
            EntityState overridden = new EntityState(1, 3, 2500, 1);
            EntityTransition despawned = EntityRules.Despawn(overridden);
            Assert.That(despawned.Accepted, Is.True);
            Assert.That(despawned.State.Alive, Is.EqualTo(0));
            Assert.That(despawned.State.Variant, Is.EqualTo(3));
            Assert.That(despawned.State.ScaleMilli, Is.EqualTo(2500));

            EntityTransition respawned = EntityRules.Spawn(despawned.State);
            Assert.That(respawned.Accepted, Is.True);
            Assert.That(respawned.State.Variant, Is.EqualTo(3));
            Assert.That(respawned.State.ScaleMilli, Is.EqualTo(2500));
        }

        [Test]
        public void Despawn_RefusesADeadEntity()
        {
            Assert.That(EntityRules.Despawn(Dead()).Refusal, Is.EqualTo(EntityRefusal.AlreadyDead));
        }

        [TestCase(-1, 3, EntityRefusal.VariantOutOfRange)]
        [TestCase(3, 3, EntityRefusal.VariantOutOfRange)]
        [TestCase(0, 3, EntityRefusal.Unchanged)]
        [TestCase(2, 3, EntityRefusal.None)]
        [TestCase(1, 0, EntityRefusal.VariantOutOfRange)]
        [TestCase(0, 0, EntityRefusal.Unchanged)]
        public void SetVariant_ValidatesTheIndexAgainstTheDefinition(int variant, int count, EntityRefusal expected)
        {
            EntityTransition result = EntityRules.SetVariant(Alive(0), variant, count);
            Assert.That(result.Refusal, Is.EqualTo(expected));
            if (expected == EntityRefusal.None)
            {
                Assert.That(result.State.Variant, Is.EqualTo(variant));
            }
        }

        [Test]
        public void SetVariant_RefusesADeadEntity()
        {
            Assert.That(EntityRules.SetVariant(Dead(0), 1, 2).Refusal, Is.EqualTo(EntityRefusal.NotAlive));
        }

        [Test]
        public void SetVisible_TogglesAndRefusesANoOp()
        {
            EntityTransition hidden = EntityRules.SetVisible(Alive(), false);
            Assert.That(hidden.Accepted, Is.True);
            Assert.That(EntityRules.IsPresented(hidden.State), Is.False);
            Assert.That(EntityRules.SetVisible(hidden.State, false).Refusal, Is.EqualTo(EntityRefusal.Unchanged));
            Assert.That(EntityRules.IsPresented(EntityRules.SetVisible(hidden.State, true).State), Is.True);
        }

        [TestCase(0, EntityRefusal.ScaleOutOfRange)]
        [TestCase(100001, EntityRefusal.ScaleOutOfRange)]
        [TestCase(1000, EntityRefusal.Unchanged)]
        [TestCase(1, EntityRefusal.None)]
        [TestCase(100000, EntityRefusal.None)]
        public void SetScale_StaysWithinTheAuthoredRange(int scale, EntityRefusal expected)
        {
            Assert.That(EntityRules.SetScale(Alive(), scale).Refusal, Is.EqualTo(expected));
        }

        [Test]
        public void IsPresented_RequiresAliveAndVisible()
        {
            Assert.That(EntityRules.IsPresented(new EntityState(1, 0, 1000, 1)), Is.True);
            Assert.That(EntityRules.IsPresented(new EntityState(0, 0, 1000, 1)), Is.False);
            Assert.That(EntityRules.IsPresented(new EntityState(1, 0, 1000, 0)), Is.False);
        }
    }
}
