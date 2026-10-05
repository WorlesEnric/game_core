// GameCore.Rules.Gameplay.Tests - P1.7a (A6): trigger occupancy as a bitmask of actor bits. A repeated enter or exit
// by the same actor is refused instead of drifting the count, the count is the popcount, first/last transitions follow
// the count, actor key 0 is refused, and keys equal mod 31 share one bit (the documented limitation).
#nullable enable
using GameCore.Rules.Gameplay.Interaction;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.P1_7a
{
    public sealed class OccupancyRulesTests
    {
        [Test]
        public void BitOf_IsOneBitPerKeyModThirtyOne_AndNoneForKeyZero()
        {
            Assert.That(OccupancyRules.BitOf(0), Is.Zero, "actor key 0 has no bit");
            Assert.That(OccupancyRules.BitOf(1), Is.EqualTo(1 << 1));
            Assert.That(OccupancyRules.BitOf(30), Is.EqualTo(1 << 30));
            Assert.That(OccupancyRules.BitOf(31), Is.EqualTo(1), "31 mod 31 is bit 0");
            Assert.That(OccupancyRules.BitOf(32), Is.EqualTo(OccupancyRules.BitOf(1)));
            Assert.That(OccupancyRules.BitOf(-1), Is.EqualTo(1 << (int)(uint.MaxValue % 31u)), "the key is taken unsigned");
            Assert.That(OccupancyRules.BitOf(int.MinValue), Is.GreaterThan(0), "the sign bit is never used");
        }

        [Test]
        public void Count_IsThePopcountOfTheLowThirtyOneBits()
        {
            Assert.That(OccupancyRules.Count(0), Is.Zero);
            Assert.That(OccupancyRules.Count(0b1011), Is.EqualTo(3));
            Assert.That(OccupancyRules.Count(int.MaxValue), Is.EqualTo(31));
            Assert.That(OccupancyRules.Count(-1), Is.EqualTo(31), "a negative legacy value is masked to 31 bits");
        }

        [Test]
        public void EnterAndExit_SetAndClearTheActorBit_WithFirstAndLast()
        {
            OccupancyOutcome a = OccupancyRules.Transit(0, 7, true, 0);
            Assert.That(a.Accepted, Is.True);
            Assert.That(a.Mask, Is.EqualTo(OccupancyRules.BitOf(7)));
            Assert.That(a.Occupants, Is.EqualTo(1));
            Assert.That(a.FirstEntered, Is.True);
            Assert.That(a.LastExited, Is.False);

            OccupancyOutcome b = OccupancyRules.Transit(a.Mask, 9, true, 0);
            Assert.That(b.Accepted, Is.True);
            Assert.That(b.Occupants, Is.EqualTo(2));
            Assert.That(b.FirstEntered, Is.False, "only the entry into an empty volume is first");
            Assert.That(OccupancyRules.Contains(b.Mask, 7) && OccupancyRules.Contains(b.Mask, 9), Is.True);

            OccupancyOutcome outA = OccupancyRules.Transit(b.Mask, 7, false, 0);
            Assert.That(outA.Accepted, Is.True);
            Assert.That(outA.Occupants, Is.EqualTo(1));
            Assert.That(outA.LastExited, Is.False);
            Assert.That(OccupancyRules.Contains(outA.Mask, 7), Is.False);

            OccupancyOutcome outB = OccupancyRules.Transit(outA.Mask, 9, false, 0);
            Assert.That(outB.Accepted, Is.True);
            Assert.That(outB.Mask, Is.Zero);
            Assert.That(outB.Occupants, Is.Zero);
            Assert.That(outB.LastExited, Is.True);
            Assert.That(outB.FirstEntered, Is.False);
        }

        [Test]
        public void RepeatedEnter_IsRefusedAsAlreadyInside_AndKeepsTheMask()
        {
            int mask = OccupancyRules.Transit(0, 5, true, 0).Mask;
            OccupancyOutcome again = OccupancyRules.Transit(mask, 5, true, 0);
            Assert.That(again.Accepted, Is.False);
            Assert.That(again.Refusal, Is.EqualTo(OccupancyRefusal.AlreadyInside));
            Assert.That(again.Mask, Is.EqualTo(mask));
            Assert.That(again.Occupants, Is.EqualTo(1), "a duplicate enter never inflates the count");
            Assert.That(again.FirstEntered, Is.False);
        }

        [Test]
        public void ExitWithoutEnter_IsRefusedAsNotInside()
        {
            OccupancyOutcome empty = OccupancyRules.Transit(0, 5, false, 0);
            Assert.That(empty.Refusal, Is.EqualTo(OccupancyRefusal.NotInside));
            Assert.That(empty.LastExited, Is.False);

            int other = OccupancyRules.Transit(0, 6, true, 0).Mask;
            OccupancyOutcome stranger = OccupancyRules.Transit(other, 5, false, 0);
            Assert.That(stranger.Refusal, Is.EqualTo(OccupancyRefusal.NotInside), "another actor's exit cannot empty the volume");
            Assert.That(stranger.Mask, Is.EqualTo(other));
            Assert.That(stranger.Occupants, Is.EqualTo(1));
        }

        [Test]
        public void EnterWhenFull_IsRefused_AndZeroMeansUnlimited()
        {
            int mask = OccupancyRules.Transit(0, 1, true, 2).Mask;
            mask = OccupancyRules.Transit(mask, 2, true, 2).Mask;
            OccupancyOutcome third = OccupancyRules.Transit(mask, 3, true, 2);
            Assert.That(third.Refusal, Is.EqualTo(OccupancyRefusal.Full));
            Assert.That(third.Occupants, Is.EqualTo(2));
            Assert.That(OccupancyRules.Transit(mask, 1, false, 2).Accepted, Is.True, "an exit from a full volume is accepted");
            Assert.That(OccupancyRules.Transit(mask, 3, true, 0).Accepted, Is.True, "maxOccupants 0 is unlimited");
        }

        [Test]
        public void ActorKeyZero_IsRefused()
        {
            Assert.That(OccupancyRules.Transit(0, 0, true, 0).Refusal, Is.EqualTo(OccupancyRefusal.NoActor));
            Assert.That(OccupancyRules.Transit(OccupancyRules.BitOf(31), 0, false, 0).Refusal, Is.EqualTo(OccupancyRefusal.NoActor));
            Assert.That(OccupancyRules.Contains(-1, 0), Is.False);
        }

        [Test]
        public void KeysEqualModThirtyOne_ShareOneBit()
        {
            int mask = OccupancyRules.Transit(0, 3, true, 0).Mask;
            Assert.That(OccupancyRules.Transit(mask, 34, true, 0).Refusal, Is.EqualTo(OccupancyRefusal.AlreadyInside), "34 collides with 3");
            OccupancyOutcome leave = OccupancyRules.Transit(mask, 34, false, 0);
            Assert.That(leave.Accepted, Is.True, "the colliding key's exit clears the shared bit");
            Assert.That(leave.LastExited, Is.True);
        }

        [Test]
        public void NegativeLegacyMask_IsSanitised()
        {
            OccupancyOutcome refused = OccupancyRules.Transit(-1, 4, true, 0);
            Assert.That(refused.Refusal, Is.EqualTo(OccupancyRefusal.AlreadyInside));
            Assert.That(refused.Mask, Is.EqualTo(int.MaxValue), "the sign bit is dropped");
            OccupancyOutcome leave = OccupancyRules.Transit(-1, 4, false, 0);
            Assert.That(leave.Mask, Is.GreaterThanOrEqualTo(0));
            Assert.That(leave.Occupants, Is.EqualTo(30));
        }

        [Test]
        public void TriggerRules_KeepsTheCountOverload_AndForwardsTheMaskOverload()
        {
            TriggerOutcome count = TriggerRules.Transit(1, true, 0);
            Assert.That(count.Occupants, Is.EqualTo(2));
            OccupancyOutcome mask = TriggerRules.Transit(0, 8, true, 0);
            Assert.That(mask.Mask, Is.EqualTo(OccupancyRules.BitOf(8)));
            Assert.That(mask.FirstEntered, Is.True);
        }
    }
}
