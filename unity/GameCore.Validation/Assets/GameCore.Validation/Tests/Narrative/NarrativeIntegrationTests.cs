#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;
using NarrativeFacts = GameCore.Gameplay.Narrative.Fixtures.NarrativeFacts;

namespace GameCore.Narrative.Tests
{
    /// <summary>
    /// GC-010 narrative vertical slice, EditMode half: the chapter providers mounted over both catalogs, the derived
    /// binding layout published into a real `Unity.Entities.World`, one choice command committed together with its
    /// durable quest fact, the sibling chapter-two mount, the fully assembled spawned target, an idle world that
    /// performs no step and the genre-neutrality audit of every name the slice registers.
    ///
    /// The scenario itself (`NarrativeScenario`) is shared with the standalone player probe (`-probeNarrative`) and
    /// runs only real modules: GC-010's pure narrative rules, the narrative gameplay providers and command stages,
    /// GC-006's derivation, GC-007's ownership validator and bounded message plane, GC-009's compiler and temporal
    /// drivers, and GC-008's planner and publisher. No seam fixture participates.
    ///
    /// Every case asserts on the facts the scenario observed, so a regression in the slice is reported by value
    /// rather than only by a boolean. The asserted field names are exactly the ones the committed canonical trace
    /// records for the narrative task, read here through the facts object.
    /// </summary>
    [TestFixture]
    public sealed class NarrativeIntegrationTests
    {
        /// <summary>The scenario's step names, in the order the scenario reports them.</summary>
        private static readonly string[] StepNames =
        {
            "narrative-catalog-and-declarations",
            "narrative-world-and-live-targets",
            "narrative-chapter-one-mounted-and-published",
            "narrative-derived-layout-in-entities",
            "narrative-chapter-two-mounted-and-published",
            "narrative-one-choice-command-committed",
            "narrative-duplicate-request-commits-nothing-new",
            "narrative-forward-provider-and-spawned-target",
            "narrative-idle-world-performs-zero-steps",
            "narrative-genre-neutrality",
            "narrative-teardown-settles-and-disposes",
        };

        private static NarrativeScenarioResult generated = null!;
        private static NarrativeScenarioResult fixture = null!;

        [OneTimeSetUp]
        public void RunTheScenarioOncePerCatalog()
        {
            generated = NarrativeScenarioHost.RunGeneratedCatalog();
            fixture = NarrativeScenarioHost.RunFixtureCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            // The scenario tears its own world down; this guarantees a clean registry if a case failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>
        /// The scenario reports exactly the eleven named observations, in the documented order, over both catalogs.
        /// </summary>
        [Test]
        public void TheScenarioReportsEveryNamedStepInOrder()
        {
            foreach (NarrativeScenarioResult run in Runs())
            {
                Assert.That(run.Steps, Is.Not.Empty, "the scenario recorded no observation at all.");
                Assert.That(run.Steps.Count, Is.EqualTo(StepNames.Length),
                    "the scenario must report exactly the eleven narrative observations: " + run.Describe());

                for (int i = 0; i < StepNames.Length; i++)
                {
                    Assert.That(run.Steps[i].Name, Is.EqualTo(StepNames[i]),
                        "step " + i.ToString(CultureInfo.InvariantCulture)
                        + " is not the documented observation: " + run.Describe());
                }

                Assert.That(AssertedStepNames(run.Steps), Is.Empty,
                    "the scenario reported an observation no case asserts on: " + run.Describe());
            }
        }

        /// <summary>The catalog and its two chapter declarations must build and mount as declared.</summary>
        [Test]
        public void TheCatalogAndDeclarationsStepPasses()
        {
            AssertStepPassed("narrative-catalog-and-declarations");
        }

        /// <summary>The world holds the seven live targets of the reference composition and their real storage.</summary>
        [Test]
        public void TheWorldAndLiveTargetsStepPasses()
        {
            AssertStepPassed("narrative-world-and-live-targets");
        }

        /// <summary>Chapter one mounts through the real provider path and publishes its assembly.</summary>
        [Test]
        public void TheChapterOneMountAndPublicationStepPasses()
        {
            AssertStepPassed("narrative-chapter-one-mounted-and-published");
        }

        /// <summary>The derived binding layout exists as real ECS storage on the eligible targets.</summary>
        [Test]
        public void TheDerivedLayoutInEntitiesStepPasses()
        {
            AssertStepPassed("narrative-derived-layout-in-entities");
        }

        /// <summary>Chapter two mounts as a sibling branch without inheriting chapter one's providers.</summary>
        [Test]
        public void TheChapterTwoMountAndPublicationStepPasses()
        {
            AssertStepPassed("narrative-chapter-two-mounted-and-published");
        }

        /// <summary>One admitted choice command commits exactly one consistent result.</summary>
        [Test]
        public void TheOneChoiceCommandCommittedStepPasses()
        {
            AssertStepPassed("narrative-one-choice-command-committed");
        }

        /// <summary>A duplicate request commits nothing new (P-042's idempotence at the command seam).</summary>
        [Test]
        public void TheDuplicateRequestStepPasses()
        {
            AssertStepPassed("narrative-duplicate-request-commits-nothing-new");
        }

        /// <summary>The forward provider's publication spawns the future target fully assembled.</summary>
        [Test]
        public void TheForwardProviderAndSpawnedTargetStepPasses()
        {
            AssertStepPassed("narrative-forward-provider-and-spawned-target");
        }

        /// <summary>An idle world performs no step at all.</summary>
        [Test]
        public void TheIdleWorldStepPasses()
        {
            AssertStepPassed("narrative-idle-world-performs-zero-steps");
        }

        /// <summary>Every registered name of the slice is genre-neutral (P-001, TEST-021).</summary>
        [Test]
        public void TheGenreNeutralityStepPasses()
        {
            AssertStepPassed("narrative-genre-neutrality");
        }

        /// <summary>The world tears down safely, settling its work and leaving the registry as it found it.</summary>
        [Test]
        public void TheTeardownStepPasses()
        {
            AssertStepPassed("narrative-teardown-settles-and-disposes");
        }

        /// <summary>
        /// The world and the chapter-one mount: seven live targets, three of them eligible for the chapter's rules,
        /// and four derived rows installed by the publication (07 section 3.1's tree, P-015).
        /// </summary>
        [Test]
        public void TheWorldCarriesTheSevenLiveTargetsAndChapterOnesDerivedRows()
        {
            foreach (NarrativeScenarioResult run in Runs())
            {
                NarrativeFacts facts = run.Facts;
                Assert.That(facts.LiveTargetCount, Is.EqualTo(7), facts.Describe());
                Assert.That(facts.DerivedTargetCountAfterChapterOne, Is.EqualTo(3),
                    "only the three eligible targets of chapter one derive a contribution (P-015): "
                    + facts.Describe());
                Assert.That(facts.ChapterOneInstalledRows, Is.EqualTo(4), facts.Describe());
            }
        }

        /// <summary>
        /// The spawn publication: eight binding rows in the published assembly, two of them on the spawned target,
        /// carrying the chapter's binding ordinal, and the two migrated conversation slots (P-024, P-029).
        /// </summary>
        [Test]
        public void ThePublicationCarriesTheSpawnedTargetsRowsAndTheMigratedSlots()
        {
            foreach (NarrativeScenarioResult run in Runs())
            {
                NarrativeFacts facts = run.Facts;
                Assert.That(facts.PublishedBindingRowCountAfterSpawn, Is.EqualTo(8), facts.Describe());
                Assert.That(facts.SpawnedBindingRowCount, Is.EqualTo(2), facts.Describe());
                Assert.That(facts.SpawnedBindingValue, Is.EqualTo(1),
                    "the spawned target inherits the binding ordinal the current assembly publishes (P-013, P-024): "
                    + facts.Describe());
                Assert.That(facts.MigratedConversationSlotCount, Is.EqualTo(2),
                    "the registered migration must have run for every migrated live slot (P-029): "
                    + facts.Describe());
            }
        }

        /// <summary>
        /// One committed choice: the gate decision flips from Closed to Open, the durable fact holds its committed
        /// value at the advanced schema version, two committed events carry the transition and one step ran
        /// (P-042, P-044, P-045).
        /// </summary>
        [Test]
        public void OneCommittedChoiceAdvancesTheFactAndTheGateExactlyOnce()
        {
            foreach (NarrativeScenarioResult run in Runs())
            {
                NarrativeFacts facts = run.Facts;
                Assert.That(facts.GateDecisionBeforeCommand, Is.EqualTo(0),
                    "the gate must start Closed before the choice command: " + facts.Describe());
                Assert.That(facts.GateDecisionAfterCommand, Is.EqualTo(1),
                    "a valid choice sets the gate condition fact, so the gate opens (07 section 3.2): "
                    + facts.Describe());
                Assert.That(facts.QuestFactValueAfterCommand, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.QuestFactVersionAfterCommand, Is.EqualTo(2),
                    "the committed fact carries the version the transition produced: " + facts.Describe());
                Assert.That(facts.CommittedEventCountAfterCommand, Is.EqualTo(2),
                    "one transition publishes exactly the committed results it produced (P-044, P-045): "
                    + facts.Describe());
                Assert.That(facts.StepsAfterCommand, Is.EqualTo(1UL), facts.Describe());
            }
        }

        /// <summary>A duplicate request is refused and commits nothing new, so the step count stays at one.</summary>
        [Test]
        public void ADuplicateRequestCommitsNothingNew()
        {
            foreach (NarrativeScenarioResult run in Runs())
            {
                NarrativeFacts facts = run.Facts;
                Assert.That(facts.StepsAfterDuplicate, Is.EqualTo(1UL),
                    "a duplicate request must not advance a step (P-042): " + facts.Describe());
            }
        }

        /// <summary>An idle world performs no step at all over the whole idle window (P-035, P-036).</summary>
        [Test]
        public void AnIdleWorldPerformsNoStep()
        {
            foreach (NarrativeScenarioResult run in Runs())
            {
                NarrativeFacts facts = run.Facts;
                Assert.That(facts.IdleFrames, Is.EqualTo(600), facts.Describe());
                Assert.That(facts.IdleStepsCommitted, Is.Zero, facts.Describe());
            }
        }

        /// <summary>
        /// The genre audit covers every name the slice registers and finds none of the forbidden genre tokens
        /// (P-001, TEST-021).
        /// </summary>
        [Test]
        public void EveryRegisteredNameIsGenreNeutral()
        {
            foreach (NarrativeScenarioResult run in Runs())
            {
                NarrativeFacts facts = run.Facts;
                Assert.That(facts.GenreAuditChecked, Is.EqualTo(NarrativeRegistrations.Count),
                    "the audit must examine every registered name of the slice: " + facts.Describe());
                Assert.That(facts.GenreAuditNeutral, Is.True, facts.Describe());
                Assert.That(facts.ForbiddenGenreNameCount, Is.Zero, facts.Describe());
            }
        }

        private static IEnumerable<NarrativeScenarioResult> Runs()
        {
            yield return generated;
            yield return fixture;
        }

        private static void AssertStepPassed(string stepName)
        {
            int observed = 0;
            foreach (NarrativeScenarioResult run in Runs())
            {
                NarrativeStep? step = FindStep(run, stepName);
                Assert.That(step, Is.Not.Null,
                    "the scenario did not report the observation '" + stepName + "': " + run.Describe());
                if (step == null)
                {
                    continue;
                }

                Assert.That(step.Passed, Is.True, step.Detail);
                observed++;
            }

            Assert.That(observed, Is.EqualTo(2),
                "the observation '" + stepName + "' must be reported over both catalogs.");
        }

        private static NarrativeStep? FindStep(NarrativeScenarioResult run, string stepName)
        {
            for (int i = 0; i < run.Steps.Count; i++)
            {
                if (string.Equals(run.Steps[i].Name, stepName, StringComparison.Ordinal))
                {
                    return run.Steps[i];
                }
            }

            return null;
        }

        private static List<string> AssertedStepNames(IReadOnlyList<NarrativeStep> steps)
        {
            var unknown = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                bool known = false;
                for (int j = 0; j < StepNames.Length; j++)
                {
                    if (string.Equals(steps[i].Name, StepNames[j], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    unknown.Add(steps[i].Name);
                }
            }

            return unknown;
        }
    }
}
