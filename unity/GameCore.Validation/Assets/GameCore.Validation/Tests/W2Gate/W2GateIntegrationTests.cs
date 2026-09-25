#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.W2Gate.Tests
{
    /// <summary>
    /// Wave 2 integration gate, EditMode half (docs/game-core/09-implementation-guide.md, "Wave 2 — Automatic
    /// assembly and generic execution"):
    ///
    ///   "Use all real W2 outputs together: mount a provider, derive a compatible target, publish its real Entities
    ///    layout and compiled schedule, execute one bounded command, observe one consistent result, spawn a future
    ///    target, and run the player smoke path. Independent seam fixtures do not substitute for this integration."
    ///
    /// The scenario itself (`W2GateScenario`) is shared with the standalone player probe (`-probeW2Gate`) and runs
    /// only real modules: GC-006's derivation over the committed composition, GC-007's ownership validator, slot
    /// policies and bounded message plane, GC-009's schedule compiler and temporal drivers, and GC-008's planner and
    /// publisher into a real `Unity.Entities.World`. No Wave 2 seam fixture participates.
    ///
    /// Every case asserts on the facts the scenario observed, so a regression in the integration is reported by
    /// value rather than only by a boolean.
    /// </summary>
    [TestFixture]
    public sealed class W2GateIntegrationTests
    {
        private static W2GateScenarioResult generated = null!;
        private static W2GateScenarioResult fixture = null!;

        [OneTimeSetUp]
        public void RunTheGateOncePerCatalog()
        {
            generated = W2GateScenarioHost.RunGeneratedCatalog();
            fixture = W2GateScenarioHost.RunFixtureCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            // The scenario tears its own world down; this guarantees a clean registry if a case failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>Every named observation of the generated-catalog run must pass.</summary>
        [Test]
        public void EveryGateCheckPassesOverTheGeneratedCatalog()
        {
            AssertStepsPassed(generated);
        }

        /// <summary>Every named observation of the fixture-catalog run must pass.</summary>
        [Test]
        public void EveryGateCheckPassesOverTheFixtureCatalog()
        {
            AssertStepsPassed(fixture);
        }

        /// <summary>
        /// GC-009 and GC-007: the real compiler produced one stable schedule over the declared stages, with a buffer
        /// edge from the producing stage to the consuming one, and GC-007 proved the per-partition writers disjoint
        /// and the ordered writers ordered (P-034, P-040, P-043).
        /// </summary>
        [Test]
        public void TheRealCompilerAndValidatorsProducedOneExecutableGraph()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.CompiledStageCount, Is.EqualTo(4), facts.Describe());
                Assert.That(facts.CompiledSystemCount, Is.EqualTo(5), facts.Describe());
                Assert.That(facts.ScheduleHash, Is.Not.Empty, facts.Describe());
                Assert.That(facts.BufferEdgeSettleBeforeProject, Is.True,
                    "the declared buffer must produce a real producer-to-consumer stage edge (P-041, P-043): " + facts.Describe());
                Assert.That(facts.SettleStageIndex, Is.LessThan(facts.ProjectStageIndex), facts.Describe());
                Assert.That(facts.OwnershipDomainCount, Is.GreaterThanOrEqualTo(3), facts.Describe());
                Assert.That(facts.TraitWritersProvablyDisjoint, Is.True,
                    "two per-partition writers of one domain must be provably disjoint (P-034, P-040): " + facts.Describe());
                Assert.That(facts.TrailWritersOrderedByStage, Is.True,
                    "two whole-schema writers of one domain must be ordered by the compiled stage order (P-040): " + facts.Describe());
                Assert.That(facts.ValidatedSlotPolicyCount, Is.GreaterThanOrEqualTo(3), facts.Describe());
                Assert.That(facts.DescriptorRevision, Is.Not.Empty, facts.Describe());
            }
        }

        /// <summary>
        /// The mount publishes a real assembly in one epoch: the composition publication's numbers are the world's,
        /// the derived rows are installed on the eligible targets, and the plan migrated the live state on scratch
        /// (P-006, P-013, P-029, P-030).
        /// </summary>
        [Test]
        public void TheMountPublishesTheDerivedAssemblyAtTheLanesOwnEpoch()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.DerivedTargetCount, Is.EqualTo(2),
                    "exactly the two eligible targets must derive a contribution (P-015): " + facts.Describe());
                Assert.That(facts.ProposalMountCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.ProposalCapabilityCount, Is.EqualTo(2), facts.Describe());
                Assert.That(facts.PlanInstalledRowCount, Is.EqualTo(2), facts.Describe());
                Assert.That(facts.PlanMigratedSlotCount, Is.EqualTo(4),
                    "every seeded target's live slot must be migrated on scratch (P-029): " + facts.Describe());
                Assert.That(facts.PublicationOutcome, Is.EqualTo("Published"), facts.Describe());
                Assert.That(facts.LaneRevisionAfterMount, Is.EqualTo(2UL), facts.Describe());
                Assert.That(facts.LaneEpochAfterMount, Is.EqualTo(2UL), facts.Describe());
                Assert.That(facts.WorldEpochAfterMount, Is.EqualTo(2UL),
                    "P-006 has one publication series, so the world publishes the composition epoch itself: " + facts.Describe());
                Assert.That(facts.CountersJoinedAfterMount, Is.True, facts.Describe());
            }
        }

        /// <summary>
        /// The derived layout exists as real ECS storage: an eligible target carries the derived row, an ineligible
        /// one keeps exactly its base recipe, and the migrated slot value is the live value advanced by the
        /// registered migration (P-015, P-029, P-032, P-034).
        /// </summary>
        [Test]
        public void TheDerivedLayoutAndTheMigratedStateAreReal()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.MaraBindingRowCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.MaraBindingValue, Is.EqualTo(W2GateDeclarations.VillagerBindingValue), facts.Describe());
                Assert.That(facts.MaraBindingIsActive, Is.True, facts.Describe());
                Assert.That(facts.MaraStampEpoch, Is.EqualTo(facts.WorldEpochAfterMount),
                    "a target's stamp names the assembly it was published in (P-030): " + facts.Describe());
                Assert.That(facts.GateBindingRowCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.GateBindingValue, Is.EqualTo(W2GateDeclarations.GateBindingValue), facts.Describe());
                Assert.That(facts.CrowdBindingRowCount, Is.Zero,
                    "an ineligible recipe keeps exactly its base recipe (P-015): " + facts.Describe());
                Assert.That(facts.EncounterBindingRowCount, Is.Zero, facts.Describe());
                Assert.That(facts.MigratedSlotValue, Is.EqualTo(W2GateKeys.MigratedQuestValue),
                    "the migrated value is the seeded value plus the registered delta (P-029, P-032): " + facts.Describe());
                Assert.That(facts.MigratedSlotSchemaVersion, Is.EqualTo(W2GateKeys.QuestDomain.Version), facts.Describe());
                Assert.That(facts.MigratedSlotCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.MigrationInvocations, Is.GreaterThanOrEqualTo(4),
                    "the registered migration must have run for every migrated live slot (P-029): " + facts.Describe());
            }
        }

        /// <summary>
        /// One bounded command through the real message plane commits exactly one consistent result: the
        /// authoritative slot holds the committed value, one committed event carries it, the ledger row is settled
        /// and the step image is published (P-042, P-044, P-045).
        /// </summary>
        [Test]
        public void OneBoundedCommandCommitsExactlyOneConsistentResult()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.CommandAdmitted, Is.True, facts.Describe());
                Assert.That(facts.StepsAfterCommand, Is.EqualTo(1UL), facts.Describe());
                Assert.That(facts.ProgressOnTarget, Is.EqualTo(W2GateKeys.CommandedQuestValue),
                    "the migrated value plus the committed command's delta: " + facts.Describe());
                Assert.That(facts.CommandSlotSchemaVersion, Is.EqualTo(W2GateKeys.QuestDomain.Version), facts.Describe());
                Assert.That(facts.CommittedEventCount, Is.EqualTo(1),
                    "a step publishes exactly the committed results it produced (P-044, P-045): " + facts.Describe());
                Assert.That(facts.CommittedEventPayload, Is.EqualTo(facts.ProgressOnTarget),
                    "the committed output and the authoritative state must be the same value: " + facts.Describe());
                Assert.That(facts.CommittedEventStep, Is.EqualTo(1UL), facts.Describe());
                Assert.That(facts.CommittedEventEpoch, Is.EqualTo(facts.WorldEpochAfterMount), facts.Describe());
                Assert.That(facts.LedgerRowCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.LedgerCommittedCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.LedgerPendingCount, Is.Zero, facts.Describe());
                Assert.That(facts.PublishedImageForFirstStep, Is.True, facts.Describe());
            }
        }

        /// <summary>
        /// The compiled order really ran through GC-005's guarded dispatch, the dependent read waited for the
        /// producer's native fence, and both per-partition writers ran (P-040, P-041).
        /// </summary>
        [Test]
        public void TheCompiledOrderAndTheNativeProducerFenceReallyRan()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.StepGroupDispatchRuns, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.StepGroupDispatchedEntries, Is.EqualTo(5),
                    "every compiled entry must run exactly once per step (P-039, P-040): " + facts.Describe());
                Assert.That(facts.TrailSteps, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.TrailWaitedOnNativeFence, Is.True,
                    "the dependent reader must combine the producer's native fence (P-041): " + facts.Describe());
                Assert.That(facts.TrailProjectedValue, Is.EqualTo(facts.SettleJobCount),
                    "the value read is the value the producer job wrote: " + facts.Describe());
                Assert.That(facts.TraitLeftValue, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.TraitRightValue, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.NativeProducedCount, Is.GreaterThanOrEqualTo(1), facts.Describe());
                Assert.That(facts.NativeUnproducedReadCount, Is.Zero, facts.Describe());
            }
        }

        /// <summary>
        /// A registered plugin wake is demand for exactly one further step and commits no request, so the committed
        /// result of the command step is still the only one (P-036, P-038).
        /// </summary>
        [Test]
        public void ARegisteredWakeAdvancesOneStepWithoutCommittingARequest()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.WakeScheduled, Is.True, facts.Describe());
                Assert.That(facts.WakeDueCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.StepsAfterWake, Is.EqualTo(2UL), facts.Describe());
                Assert.That(facts.CommittedEventCountAfterWake, Is.EqualTo(1),
                    "a wake-driven step commits no request, so it publishes no further result (P-042): " + facts.Describe());
            }
        }

        /// <summary>
        /// The forward provider's composition publication carries no derivable target change, and the spawn publishes
        /// at that same publication: the future target appears in one epoch with its complete effective assembly and
        /// both counters end joined (P-006, P-013, P-024).
        /// </summary>
        [Test]
        public void AFutureTargetAppearsFullyAssembledAtOneEpoch()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.ForwardDerivationHadNoTargetChange, Is.True,
                    "a provider whose rule matches no live target changes no target's assembly: " + facts.Describe());
                Assert.That(facts.LaneEpochAfterForward, Is.EqualTo(3UL), facts.Describe());
                Assert.That(facts.WorldEpochAfterSpawn, Is.EqualTo(3UL), facts.Describe());
                Assert.That(facts.CountersJoinedAfterSpawn, Is.True, facts.Describe());
                Assert.That(facts.RecipeApplyCount, Is.EqualTo(1),
                    "the recipe's base layout is installed once, inside the publication fence (P-024): " + facts.Describe());
                Assert.That(facts.SpawnedBindingRowCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.SpawnedBindingValue, Is.EqualTo(W2GateDeclarations.VillagerBindingValue),
                    "the spawned target inherits the rule the current assembly publishes (P-013, P-024): " + facts.Describe());
                Assert.That(facts.SpawnedStampEpoch, Is.EqualTo(facts.WorldEpochAfterSpawn), facts.Describe());
                Assert.That(facts.SpawnedStampPublished, Is.True,
                    "a target is only visible once its assembly is complete (P-024): " + facts.Describe());
                Assert.That(facts.SpawnedTargetInPublishedView, Is.True, facts.Describe());
                Assert.That(facts.PublishedBindingRowCount, Is.EqualTo(3), facts.Describe());
            }
        }

        /// <summary>
        /// An idle world performs no step at all: no step, no image, no dispatch run and no demand (P-035, P-036).
        /// </summary>
        [Test]
        public void AnIdleWorldPerformsNoStep()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.IdleFrames, Is.GreaterThan(0), facts.Describe());
                Assert.That(facts.IdleStepsCommitted, Is.Zero, facts.Describe());
                Assert.That(facts.IdleDispatchRuns, Is.Zero, facts.Describe());
                Assert.That(facts.PendingDemandAfterIdle, Is.Zero, facts.Describe());
            }
        }

        /// <summary>
        /// The gate's world tears down safely: its retained jobs settled, no host resource stayed retained and the
        /// registry returned to its pre-gate size (P-047, P-048).
        /// </summary>
        [Test]
        public void TeardownSettlesWorkAndLeavesNoWorldRegistered()
        {
            foreach (W2GateScenarioResult run in Runs())
            {
                W2GateFacts facts = run.Facts;
                Assert.That(facts.OutstandingJobsAfterTeardown, Is.Zero, facts.Describe());
                Assert.That(facts.RetainedResourcesAfterTeardown, Is.Zero, facts.Describe());
                Assert.That(facts.RegistryAfterTeardown, Is.EqualTo(facts.RegistryBeforeCreate), facts.Describe());
            }
        }

        private static IEnumerable<W2GateScenarioResult> Runs()
        {
            yield return generated;
            yield return fixture;
        }

        private static void AssertStepsPassed(W2GateScenarioResult result)
        {
            Assert.That(result.Steps, Is.Not.Empty, "the gate recorded no observation at all.");

            var failed = new List<string>();
            for (int i = 0; i < result.Steps.Count; i++)
            {
                W2GateStep step = result.Steps[i];
                if (!step.Passed)
                {
                    failed.Add(step.ToString());
                }
            }

            Assert.That(failed, Is.Empty, "failed gate checks: " + string.Join(" | ", failed.ToArray()));
            Assert.That(result.AllPassed, Is.True, result.Describe());
            Assert.That(result.Facts.ScheduleHash, Is.Not.Empty, result.Facts.Describe());
        }
    }
}
