#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using NUnit.Framework;

namespace GameCore.Narrative.Tests
{
    /// <summary>
    /// GC-015's narrative state-policy half, EditMode: state retention, migration and explicit reset inside a real
    /// narrative world.
    ///
    /// The scenario (`NarrativeStatePolicyScenario`) builds the GC-010 narrative world with the real kernel modules
    /// and drives GC-015's policies through it; this fixture asserts on each named observation by value, so a
    /// regression reports what changed rather than only that something did.
    /// </summary>
    [TestFixture]
    public sealed class NarrativeStatePolicyAssertions
    {
        /// <summary>The observations the scenario must produce, in execution order (missing one fails the suite).</summary>
        private static readonly string[] RequiredObservations =
        {
            "gc015-narrative-policy-surface",
            "gc015-narrative-world-and-live-state",
            "gc015-narrative-tuning-keeps-non-default-counters",
            "gc015-narrative-reparent-keeps-unrelated-state",
            "gc015-narrative-declared-surface",
            "gc015-narrative-preserve-dormant",
            "gc015-narrative-remove-derived",
            "gc015-narrative-registered-migration",
            "gc015-narrative-explicit-reset",
            "gc015-narrative-ownership-transfer",
            "gc015-narrative-failed-migration-keeps-old-assembly",
            "gc015-narrative-migration-bounds",
        };

        private static IReadOnlyList<NarrativeStatePolicyStep> steps = null!;

        [OneTimeSetUp]
        public void RunTheScenarioOnce()
        {
            steps = NarrativeStatePolicyScenario.Run();
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

            Assert.That(failures, Is.Empty, "failing GC-015 narrative observations:" + System.Environment.NewLine
                + string.Join(System.Environment.NewLine, failures.ToArray()));
        }

        [Test]
        public void NonDefaultCountersSurviveTuningAndReparenting()
        {
            NarrativeStatePolicyStep tuning = Find("gc015-narrative-tuning-keeps-non-default-counters");
            NarrativeStatePolicyStep reparent = Find("gc015-narrative-reparent-keeps-unrelated-state");

            Assert.That(tuning.Passed, Is.True, tuning.Detail);
            Assert.That(tuning.Detail, Does.Contain("node=4@2"), "the non-default conversation node was retained as-is");
            Assert.That(tuning.Detail, Does.Contain("trailSteps=7"), "the non-default trail counter was retained as-is");
            Assert.That(reparent.Passed, Is.True, reparent.Detail);
            Assert.That(reparent.Detail, Does.Contain("nodeAfterReturn=4@0"), "the moved subtree's siblings kept their state");
        }

        [Test]
        public void EverySlotPolicyRanAgainstTheLiveWorld()
        {
            Assert.That(Find("gc015-narrative-preserve-dormant").Passed, Is.True, Find("gc015-narrative-preserve-dormant").Detail);
            Assert.That(Find("gc015-narrative-remove-derived").Passed, Is.True, Find("gc015-narrative-remove-derived").Detail);
            Assert.That(Find("gc015-narrative-registered-migration").Passed, Is.True, Find("gc015-narrative-registered-migration").Detail);
            Assert.That(Find("gc015-narrative-explicit-reset").Passed, Is.True, Find("gc015-narrative-explicit-reset").Detail);
            Assert.That(Find("gc015-narrative-ownership-transfer").Passed, Is.True, Find("gc015-narrative-ownership-transfer").Detail);
        }

        [Test]
        public void AFailedMigrationLeavesTheOldAssemblyAndItsBoundsObservable()
        {
            NarrativeStatePolicyStep failed = Find("gc015-narrative-failed-migration-keeps-old-assembly");
            NarrativeStatePolicyStep bounds = Find("gc015-narrative-migration-bounds");

            Assert.That(failed.Passed, Is.True, failed.Detail);
            Assert.That(failed.Detail, Does.Contain("UnsupportedVersion"), "the pass failed before any live write");
            Assert.That(bounds.Passed, Is.True, bounds.Detail);
            Assert.That(bounds.Detail, Does.Contain("BudgetExceeded"), "temporary storage is a hard limit (P-022)");
        }

        private static NarrativeStatePolicyStep Find(string name)
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
