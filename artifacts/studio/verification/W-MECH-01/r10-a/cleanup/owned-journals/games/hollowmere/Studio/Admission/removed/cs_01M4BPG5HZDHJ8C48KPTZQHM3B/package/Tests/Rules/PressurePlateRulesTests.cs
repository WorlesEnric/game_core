#nullable enable
// Hollowmere.Mechanism.PressurePlate.Rules.Tests - the pure press transition (W-MECH-01 sample).
// Unity-free: runs as a Unity EditMode test and under plain `dotnet test` (netstandard2.1 rules + net8.0 NUnit).
using NUnit.Framework;

namespace Hollowmere.Mechanism.PressurePlate.Rules.Tests
{
    [TestFixture]
    public sealed class PressurePlateRulesTests
    {
        private static readonly PlateSpec One = new PlateSpec(1, 2);
        private static readonly PlateSpec Two = new PlateSpec(2, 3);

        [Test]
        public void Initial_IsEmptyAndReleased()
        {
            PlateState state = PressurePlateRules.Initial();
            Assert.That(state.Weight, Is.EqualTo(0));
            Assert.That(state.Pressed, Is.EqualTo(0));
            Assert.That(state.IsPressed, Is.False);
        }

        [Test]
        public void StepOn_AtThreshold_Presses()
        {
            PlatePressResult result = PressurePlateRules.Press(PressurePlateRules.Initial(), true, One);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State, Is.EqualTo(new PlateState(1, 1)));
            Assert.That(result.Transition, Is.EqualTo(PlateTransition.Pressed));
            Assert.That(result.Refusal, Is.Empty);
        }

        [Test]
        public void StepOn_BelowThreshold_AddsWeightWithoutTransition()
        {
            PlatePressResult result = PressurePlateRules.Press(PressurePlateRules.Initial(), true, Two);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State, Is.EqualTo(new PlateState(1, 0)));
            Assert.That(result.Transition, Is.EqualTo(PlateTransition.None));
        }

        [Test]
        public void StepOn_AboveThreshold_StaysPressedWithoutTransition()
        {
            PlatePressResult result = PressurePlateRules.Press(new PlateState(2, 1), true, Two);
            Assert.That(result.State, Is.EqualTo(new PlateState(3, 1)));
            Assert.That(result.Transition, Is.EqualTo(PlateTransition.None));
        }

        [Test]
        public void StepOff_BelowThreshold_Releases()
        {
            PlatePressResult result = PressurePlateRules.Press(new PlateState(1, 1), false, One);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.State, Is.EqualTo(new PlateState(0, 0)));
            Assert.That(result.Transition, Is.EqualTo(PlateTransition.Released));
        }

        [Test]
        public void StepOff_StillAtThreshold_StaysPressed()
        {
            PlatePressResult result = PressurePlateRules.Press(new PlateState(3, 1), false, Two);
            Assert.That(result.State, Is.EqualTo(new PlateState(2, 1)));
            Assert.That(result.Transition, Is.EqualTo(PlateTransition.None));
        }

        [Test]
        public void StepOff_EmptyPlate_IsRefusedNotLoaded()
        {
            PlateState empty = PressurePlateRules.Initial();
            PlatePressResult result = PressurePlateRules.Press(empty, false, One);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Refusal, Is.EqualTo(PlateRefusals.NotLoaded));
            Assert.That(result.State, Is.EqualTo(empty), "a refused press changes nothing");
            Assert.That(result.Transition, Is.EqualTo(PlateTransition.None));
        }

        [Test]
        public void StepOn_FullPlate_IsRefusedOverloaded()
        {
            var full = new PlateState(2, 1);
            PlatePressResult result = PressurePlateRules.Press(full, true, One);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Refusal, Is.EqualTo(PlateRefusals.Overloaded));
            Assert.That(result.State, Is.EqualTo(full));
        }

        [Test]
        public void Unknown_IsRefusedUnknown()
        {
            PlatePressResult result = PressurePlateRules.Unknown();
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Refusal, Is.EqualTo(PlateRefusals.Unknown));
        }

        [TestCase(0, 2)]
        [TestCase(-1, 2)]
        [TestCase(3, 2)]
        public void InvalidDefinition_IsRefused(int threshold, int maxWeight)
        {
            var spec = new PlateSpec(threshold, maxWeight);
            Assert.That(spec.IsValid, Is.False);
            PlatePressResult result = PressurePlateRules.Press(PressurePlateRules.Initial(), true, spec);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Refusal, Is.EqualTo(PlateRefusals.InvalidDefinition));
        }

        [Test]
        public void RefusalCodes_AreStable()
        {
            Assert.That(PlateRefusals.NotLoaded, Is.EqualTo("plate.not-loaded"));
            Assert.That(PlateRefusals.Overloaded, Is.EqualTo("plate.overloaded"));
            Assert.That(PlateRefusals.Unknown, Is.EqualTo("plate.unknown"));
        }

        [Test]
        public void SmokeSchedule_EndsWhereTheSessionExpects()
        {
            // The press sequence of PressurePlateSmoke on its two plates, replayed on the pure rules.
            var specA = new PlateSpec(1, 2);
            var specB = new PlateSpec(2, 3);
            PlateState a = PressurePlateRules.Initial();
            PlateState b = PressurePlateRules.Initial();

            a = Accept(a, true, specA, PlateTransition.Pressed);
            b = Accept(b, true, specB, PlateTransition.None);
            b = Accept(b, true, specB, PlateTransition.Pressed);
            a = Accept(a, false, specA, PlateTransition.Released);
            Assert.That(PressurePlateRules.Press(a, false, specA).Refusal, Is.EqualTo(PlateRefusals.NotLoaded));
            b = Accept(b, true, specB, PlateTransition.None);
            Assert.That(PressurePlateRules.Press(b, true, specB).Refusal, Is.EqualTo(PlateRefusals.Overloaded));
            b = Accept(b, false, specB, PlateTransition.None);
            b = Accept(b, false, specB, PlateTransition.Released);

            Assert.That(a, Is.EqualTo(new PlateState(0, 0)));
            Assert.That(b, Is.EqualTo(new PlateState(1, 0)));
        }

        [Test]
        public void Press_IsDeterministic()
        {
            for (int weight = 0; weight <= 3; weight++)
            {
                for (int pressed = 0; pressed <= 1; pressed++)
                {
                    var state = new PlateState(weight, pressed);
                    PlatePressResult first = PressurePlateRules.Press(state, true, Two);
                    PlatePressResult second = PressurePlateRules.Press(state, true, Two);
                    Assert.That(first.State, Is.EqualTo(second.State));
                    Assert.That(first.Transition, Is.EqualTo(second.Transition));
                    Assert.That(first.Refusal, Is.EqualTo(second.Refusal));
                }
            }
        }

        private static PlateState Accept(PlateState state, bool load, PlateSpec spec, PlateTransition expected)
        {
            PlatePressResult result = PressurePlateRules.Press(state, load, spec);
            Assert.That(result.Accepted, Is.True, "press " + (load ? "on" : "off") + " from " + state + " refused: " + result.Refusal);
            Assert.That(result.Transition, Is.EqualTo(expected), "transition from " + state);
            return result.State;
        }
    }
}
