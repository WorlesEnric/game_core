#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using NUnit.Framework;

namespace GameCore.Cards.Tests
{
    /// <summary>
    /// GC-015's card state-policy half, EditMode: state retention, migration and explicit reset inside a real card
    /// world.
    ///
    /// `CardStatePolicyScenario` builds the GC-011 market with the real kernel modules and drives GC-015's policies
    /// through it; this fixture asserts on each named observation by value.
    /// </summary>
    [TestFixture]
    public sealed class CardStatePolicyAssertions
    {
        /// <summary>The observations the scenario must produce, in execution order (missing one fails the suite).</summary>
        private static readonly string[] RequiredObservations =
        {
            "gc015-cards-policy-surface",
            "gc015-cards-world-and-live-state",
            "gc015-cards-tuning-keeps-non-default-counters",
            "gc015-cards-reparent-keeps-state",
            "gc015-cards-declared-surface",
            "gc015-cards-preserve-dormant",
            "gc015-cards-remove-derived",
            "gc015-cards-registered-migration",
            "gc015-cards-explicit-reset",
            "gc015-cards-ownership-transfer",
            "gc015-cards-failed-migration-keeps-old-assembly",
            "gc015-cards-migration-bounds",
        };

        private static IReadOnlyList<CardStatePolicyStep> steps = null!;

        [OneTimeSetUp]
        public void RunTheScenarioOnce()
        {
            steps = CardStatePolicyScenario.Run();
        }

        [TearDown]
        public void TearDown()
        {
            // The scenario publishes into its own world; this guarantees a clean registry if a case failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        [Test]
        public void TheScenarioProducesEveryRequiredObservation()
        {
            Assert.That(steps.Count, Is.EqualTo(RequiredObservations.Length),
                "the scenario must produce exactly the required observations: " + Describe());

            for (int i = 0; i < RequiredObservations.Length; i++)
            {
                Assert.That(steps[i].Name, Is.EqualTo(RequiredObservations[i]), "observation " + i.ToString() + " name");
            }
        }

        [Test]
        public void EveryObservationHolds()
        {
            var failures = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Passed)
                {
                    failures.Add(steps[i].ToString());
                }
            }

            Assert.That(failures, Is.Empty, "failing GC-015 card observations:" + System.Environment.NewLine
                + string.Join(System.Environment.NewLine, failures.ToArray()));
        }

        [Test]
        public void NonDefaultTableAndSeatCountersSurviveTuningAndReparenting()
        {
            CardStatePolicyStep tuning = Find("gc015-cards-tuning-keeps-non-default-counters");
            CardStatePolicyStep reparent = Find("gc015-cards-reparent-keeps-state");

            Assert.That(tuning.Passed, Is.True, tuning.Detail);
            Assert.That(tuning.Detail, Does.Contain("turnNumber=6"), "the table's turn number was retained as-is");
            Assert.That(tuning.Detail, Does.Contain("seatScore=4"), "the seat's score slot was retained as-is");
            Assert.That(reparent.Passed, Is.True, reparent.Detail);
            Assert.That(reparent.Detail, Does.Contain("practiceUnderLeagueB=True"), "the reparent really moved the subtree");
        }

        [Test]
        public void EverySlotPolicyRanAgainstTheLiveWorld()
        {
            Assert.That(Find("gc015-cards-preserve-dormant").Passed, Is.True, Find("gc015-cards-preserve-dormant").Detail);
            Assert.That(Find("gc015-cards-remove-derived").Passed, Is.True, Find("gc015-cards-remove-derived").Detail);
            Assert.That(Find("gc015-cards-registered-migration").Passed, Is.True, Find("gc015-cards-registered-migration").Detail);
            Assert.That(Find("gc015-cards-explicit-reset").Passed, Is.True, Find("gc015-cards-explicit-reset").Detail);
            Assert.That(Find("gc015-cards-ownership-transfer").Passed, Is.True, Find("gc015-cards-ownership-transfer").Detail);
        }

        [Test]
        public void AFailedMigrationLeavesTheOldAssemblyAndItsBoundsObservable()
        {
            CardStatePolicyStep failed = Find("gc015-cards-failed-migration-keeps-old-assembly");
            CardStatePolicyStep bounds = Find("gc015-cards-migration-bounds");

            Assert.That(failed.Passed, Is.True, failed.Detail);
            Assert.That(failed.Detail, Does.Contain("UnsupportedVersion"), "the pass failed before any live write");
            Assert.That(bounds.Passed, Is.True, bounds.Detail);
            Assert.That(bounds.Detail, Does.Contain("BudgetExceeded"), "temporary storage is a hard limit (P-022)");
        }

        private static CardStatePolicyStep Find(string name)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (string.Equals(steps[i].Name, name, System.StringComparison.Ordinal))
                {
                    return steps[i];
                }
            }

            Assert.Fail("the scenario produced no observation named " + name + ":" + System.Environment.NewLine + Describe());
            return null!;
        }

        private static string Describe()
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                text.Append(System.Environment.NewLine).Append(steps[i].ToString());
            }

            return text.ToString();
        }
    }
}
