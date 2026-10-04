// GameCore.Rules.Gameplay.Tests - interaction rules (P1.3): kinds' state machines, lock and conditions, cooldown,
// uses, range, explicit state changes and trigger occupancy.
#nullable enable
using GameCore.Rules.Gameplay.Interaction;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.Interaction
{
    public sealed class InteractionRulesTests
    {
        private static InteractableProfile Door(int cooldown = 0, int maxUses = 0, int range = 0) =>
            new InteractableProfile(InteractableKind.Door, maxUses, cooldown, range);

        private static InteractableSnapshot Fresh(InteractableKind kind, bool locked = false) =>
            new InteractableSnapshot(InteractionRules.InitialState(kind, locked), 0, 0);

        private static InteractionOutcome Use(InteractableSnapshot state, InteractableProfile profile) =>
            InteractionRules.Use(state, profile, 0, ConditionAnswer.Unknown, ConditionAnswer.Unknown);

        [Test]
        public void InitialStates_FollowTheKind()
        {
            Assert.That(InteractionRules.InitialState(InteractableKind.Door, false), Is.EqualTo(InteractableStates.Closed));
            Assert.That(InteractionRules.InitialState(InteractableKind.Gate, true), Is.EqualTo(InteractableStates.Locked));
            Assert.That(InteractionRules.InitialState(InteractableKind.Switch, false), Is.EqualTo(InteractableStates.Off));
            Assert.That(InteractionRules.InitialState(InteractableKind.Examinable, true), Is.EqualTo(InteractableStates.Idle), "only doors and gates lock");
            Assert.That(InteractionRules.InitialState(InteractableKind.Point, false), Is.EqualTo(InteractableStates.Idle));
        }

        [Test]
        public void Door_TogglesClosedAndOpen_CountingUses()
        {
            InteractionOutcome open = Use(Fresh(InteractableKind.Door), Door());
            Assert.That(open.Succeeded, Is.True);
            Assert.That(open.StateChanged, Is.True);
            Assert.That(open.After.State, Is.EqualTo(InteractableStates.Open));
            InteractionOutcome close = Use(open.After, Door());
            Assert.That(close.After.State, Is.EqualTo(InteractableStates.Closed));
            Assert.That(close.After.Uses, Is.EqualTo(2));
        }

        [Test]
        public void Locked_StaysLockedUnlessTheUnlockConditionIsTrue()
        {
            InteractableSnapshot gate = Fresh(InteractableKind.Gate, true);
            var profile = new InteractableProfile(InteractableKind.Gate, 0, 0, 0);
            Assert.That(InteractionRules.Use(gate, profile, 0, ConditionAnswer.Unknown, ConditionAnswer.Unknown).Refusal, Is.EqualTo(InteractionRefusal.Locked));
            Assert.That(InteractionRules.Use(gate, profile, 0, ConditionAnswer.True, ConditionAnswer.False).Refusal, Is.EqualTo(InteractionRefusal.Locked));
            InteractionOutcome unlocked = InteractionRules.Use(gate, profile, 0, ConditionAnswer.Unknown, ConditionAnswer.True);
            Assert.That(unlocked.Succeeded, Is.True);
            Assert.That(unlocked.After.State, Is.EqualTo(InteractableStates.Open));
            Assert.That(InteractionRefusals.Code(InteractionRefusal.Locked), Is.EqualTo("interaction.locked"));
        }

        [Test]
        public void Precondition_PassesUnlessAffirmativelyFalse()
        {
            InteractableSnapshot door = Fresh(InteractableKind.Door);
            Assert.That(InteractionRules.Use(door, Door(), 0, ConditionAnswer.Unknown, ConditionAnswer.Unknown).Succeeded, Is.True, "null evaluator: always allowed");
            Assert.That(InteractionRules.Use(door, Door(), 0, ConditionAnswer.True, ConditionAnswer.Unknown).Succeeded, Is.True);
            Assert.That(InteractionRules.Use(door, Door(), 0, ConditionAnswer.False, ConditionAnswer.Unknown).Refusal, Is.EqualTo(InteractionRefusal.ConditionFailed));
        }

        [Test]
        public void Cooldown_RefusesUntilTicksRunItDown()
        {
            InteractionOutcome used = Use(Fresh(InteractableKind.Switch), new InteractableProfile(InteractableKind.Switch, 0, 500, 0));
            Assert.That(used.After.State, Is.EqualTo(InteractableStates.On));
            Assert.That(used.After.CooldownMilliseconds, Is.EqualTo(500));
            var profile = new InteractableProfile(InteractableKind.Switch, 0, 500, 0);
            Assert.That(Use(used.After, profile).Refusal, Is.EqualTo(InteractionRefusal.CoolingDown));
            InteractableSnapshot cooled = InteractionRules.Tick(InteractionRules.Tick(used.After, 300), 300);
            Assert.That(cooled.CooldownMilliseconds, Is.EqualTo(0));
            Assert.That(Use(cooled, profile).After.State, Is.EqualTo(InteractableStates.Off));
        }

        [Test]
        public void MaxUses_ExhaustsTheInteractable()
        {
            var profile = new InteractableProfile(InteractableKind.Examinable, 2, 0, 0);
            InteractableSnapshot well = Fresh(InteractableKind.Examinable);
            well = Use(well, profile).After;
            well = Use(well, profile).After;
            Assert.That(well.State, Is.EqualTo(InteractableStates.Idle), "examining never changes the state");
            Assert.That(well.Uses, Is.EqualTo(2));
            Assert.That(Use(well, profile).Refusal, Is.EqualTo(InteractionRefusal.UsesExhausted));
        }

        [Test]
        public void Point_IsUsedOnce()
        {
            var profile = new InteractableProfile(InteractableKind.Point, 0, 0, 0);
            InteractionOutcome first = Use(Fresh(InteractableKind.Point), profile);
            Assert.That(first.After.State, Is.EqualTo(InteractableStates.Used));
            Assert.That(Use(first.After, profile).Refusal, Is.EqualTo(InteractionRefusal.AlreadyUsed));
        }

        [Test]
        public void Range_RefusesADistantActor()
        {
            InteractableSnapshot door = Fresh(InteractableKind.Door);
            Assert.That(InteractionRules.Use(door, Door(range: 2000), 2500, ConditionAnswer.Unknown, ConditionAnswer.Unknown).Refusal, Is.EqualTo(InteractionRefusal.OutOfRange));
            Assert.That(InteractionRules.Use(door, Door(range: 2000), 1500, ConditionAnswer.Unknown, ConditionAnswer.Unknown).Succeeded, Is.True);
        }

        [Test]
        public void Broken_RefusesEverything_AndOrderIsBrokenFirst()
        {
            var broken = new InteractableSnapshot(InteractableStates.Broken, 0, 900);
            Assert.That(InteractionRules.Use(broken, Door(range: 10), 5000, ConditionAnswer.False, ConditionAnswer.False).Refusal, Is.EqualTo(InteractionRefusal.Broken));
        }

        [Test]
        public void SetState_AcceptsOnlyLegalStatesOfTheKind()
        {
            InteractableSnapshot gate = Fresh(InteractableKind.Gate, true);
            InteractionOutcome opened = InteractionRules.SetState(gate, InteractableKind.Gate, InteractableStates.Open);
            Assert.That(opened.Succeeded, Is.True);
            Assert.That(InteractionRules.SetState(opened.After, InteractableKind.Gate, InteractableStates.Open).Refusal, Is.EqualTo(InteractionRefusal.Unchanged));
            Assert.That(InteractionRules.SetState(gate, InteractableKind.Gate, InteractableStates.On).Refusal, Is.EqualTo(InteractionRefusal.InvalidState));
            Assert.That(InteractionRules.SetState(gate, InteractableKind.Gate, InteractableStates.Broken).Succeeded, Is.True);
            Assert.That(InteractionRules.IsLegal(InteractableKind.Examinable, InteractableStates.Open), Is.False);
        }

        [Test]
        public void StateNames_RoundTrip()
        {
            for (int state = 0; state < InteractableStates.Count; state++)
            {
                Assert.That(InteractableStates.TryParse(InteractableStates.Name(state), out int parsed), Is.True);
                Assert.That(parsed, Is.EqualTo(state));
            }

            Assert.That(InteractableStates.TryParse("ajar", out int _), Is.False);
        }

        [Test]
        public void Trigger_CountsOccupants_AndReportsFirstEntryAndLastExit()
        {
            TriggerOutcome first = TriggerRules.Transit(0, true, 0);
            Assert.That(first.FirstEntered, Is.True);
            TriggerOutcome second = TriggerRules.Transit(first.Occupants, true, 0);
            Assert.That(second.FirstEntered, Is.False);
            Assert.That(second.Occupants, Is.EqualTo(2));
            TriggerOutcome out1 = TriggerRules.Transit(2, false, 0);
            Assert.That(out1.LastExited, Is.False);
            TriggerOutcome out2 = TriggerRules.Transit(1, false, 0);
            Assert.That(out2.LastExited, Is.True);
            Assert.That(TriggerRules.Transit(0, false, 0).Accepted, Is.False, "an exit from an empty volume is refused");
            Assert.That(TriggerRules.Transit(1, true, 1).Accepted, Is.False, "capacity");
        }
    }
}
