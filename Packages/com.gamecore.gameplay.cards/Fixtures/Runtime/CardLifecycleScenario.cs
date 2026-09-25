// GameCore.Gameplay.Cards.Fixtures — the GC-014 card-family installation lifecycle scenario.
//
// The gate sentence this file implements, from `docs/game-core/09-implementation-guide.md` (GC-014):
// "All installation transitions of the P-046 table over the control/publication path (activation,
// reconfiguration, replacement, suspend/resume, unload), invalid transitions rejected", with the P-012
// provider-loss/return consequence, the P-047 late-completion and blocked-job rules and the P-048 teardown
// accounting. Both families prove it in a real Unity world; this is the card half.
//
// Every observation runs the real modules over the real card composition, exactly as the GC-011 market
// scenario does:
//
//   * GC-004's `CompositionHost` admits, plans and publishes the composition edits, and its
//     `InstallationLifecycleCoordinator` owns the P-046 activation ledger, the P-048 teardown sequencer, the
//     job fences and the bounded quarantine registry;
//   * GC-007's ownership validator and GC-009's compiler build the descriptor the card world is created with;
//   * GC-011's seeding, recipes and derived assembly pipeline install the real `cards.set-bonus` binding rows
//     in real ECS storage, and the unload publication retracts them again (P-033);
//   * GC-014's `LifecycleController` drives suspend/resume/unmount through the three seams the protocol names
//     (control lane, world lifecycle binding, derived assembly) and its `LifecycleJobFence` bridges tracked
//     work into the composition fences (P-047, P-048).
//
// Three design decisions are worth naming, because a reader will ask about them:
//
//   * **The subject installation.** The scenario's provider is the family's own scoring provider
//     (`CardVocabulary.FestivalScoring` at `cards.league-a`), because that is the installation whose derived
//     contribution is observable: it owns the `cards.set-bonus` rows of the two League A seats, so a suspend,
//     a resume, a replacement and an unload each move a count a caller can read.
//   * **The consumer/provider pair of steps 4 and 5.** The card family's real declarations declare the table
//     runtime's dependency on the definition-lookup contract as *optional*
//     (`CardTableFixture.TableRuntimeDeclaration` passes `required: false`), so it produces no required edge to
//     lose. The scenario therefore adds its own test-only pair through real manifests: a scoring-style consumer
//     that declares a **required** `ServiceDependency` on `CardTableKeys.LookupContract`, and the real
//     `CardTableDeclarations.RuleLibrary` manifest (same contract and factory, its own plugin type and
//     installation identity) as the provider. The consumer's capability is its own
//     (`cards.lifecycle-score`, the registered Int32 sum reducer and the registered predicate), so it never
//     contends with the family's `cards.set-bonus` rows and its contribution count stays a deterministic fact.
//   * **Both catalogs.** The committed generated card catalog lives in the Unity project
//     (`GameCore.Validation.GeneratedCards`) and this fixtures package cannot reference an Assets assembly, so
//     both entry points run over this package's generated-style `CardCatalogTable` with two independent
//     declaration sets of the same shape. `RunGeneratedCatalog` and `RunFixtureCatalog` therefore differ in
//     nothing but their identities; a caller that has the committed catalog in hand mounts it through the same
//     `CardCatalogTable`-shaped path.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Lifecycle;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Gameplay.Cards.Fixtures
{
    /// <summary>One named lifecycle observation: what was checked and the values the verdict was computed from.</summary>
    public sealed class CardLifecycleStep
    {
        /// <summary>Builds one observation.</summary>
        public CardLifecycleStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Stable observation name.</summary>
        public string Name { get; }

        /// <summary>Whether the observation held.</summary>
        public bool Passed { get; }

        /// <summary>The observed values the verdict was computed from.</summary>
        public string Detail { get; }

        /// <summary>One-line form.</summary>
        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// The facts one lifecycle run observed, keyed by the frozen names of the GC-014 contract. Every value is
    /// read from live module state at the moment its own comment names, so a caller asserts on the observation
    /// rather than on a boolean. Keys are always held in canonical ordinal order, and <see cref="Describe"/>
    /// renders exactly one `key=value` line per key in that order.
    /// </summary>
    public sealed class CardLifecycleFacts
    {
        private readonly SortedDictionary<string, string> values =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Records a textual fact.</summary>
        public void Set(string key, string value)
        {
            values[key] = value ?? string.Empty;
        }

        /// <summary>Records an invariant integer fact.</summary>
        public void Set(string key, long value)
        {
            values[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Records a boolean fact in the repository's `bool.ToString()` form.</summary>
        public void Set(string key, bool value)
        {
            values[key] = value.ToString();
        }

        /// <summary>The recorded value of one key; a miss is a defect and throws rather than inventing a value.</summary>
        public string ValueOf(string key)
        {
            if (!values.TryGetValue(key, out string? value))
            {
                throw new KeyNotFoundException("The card lifecycle facts carry no key '" + key + "'.");
            }

            return value;
        }

        /// <summary>True when this run recorded the key at all.</summary>
        public bool Has(string key) => values.ContainsKey(key);

        /// <summary>Every recorded key in canonical (ordinal) order.</summary>
        public IReadOnlyList<string> Keys
        {
            get
            {
                var keys = new List<string>(values.Count);
                foreach (KeyValuePair<string, string> pair in values)
                {
                    keys.Add(pair.Key);
                }

                return keys;
            }
        }

        /// <summary>Stable digest: one `key=value` line per key, in canonical key order.</summary>
        public string Describe()
        {
            var lines = new List<string>(values.Count);
            foreach (KeyValuePair<string, string> pair in values)
            {
                lines.Add(pair.Key + "=" + pair.Value);
            }

            return string.Join("\n", lines.ToArray());
        }
    }

    /// <summary>Full result of one lifecycle run: the named observations plus the facts they were read from.</summary>
    public sealed class CardLifecycleScenarioResult
    {
        /// <summary>Builds one result.</summary>
        public CardLifecycleScenarioResult(IReadOnlyList<CardLifecycleStep> steps, CardLifecycleFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        /// <summary>The named observations, in execution order.</summary>
        public IReadOnlyList<CardLifecycleStep> Steps { get; }

        /// <summary>The facts every verdict was computed from.</summary>
        public CardLifecycleFacts Facts { get; }

        /// <summary>True when every observation passed and there was at least one.</summary>
        public bool AllPassed
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return Steps.Count > 0;
            }
        }

        /// <summary>One-line digest naming every failed observation.</summary>
        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].Name + " (" + Steps[i].Detail + ")");
                }
            }

            return failed.Count == 0
                ? Steps.Count.ToString(CultureInfo.InvariantCulture) + " card lifecycle checks passed"
                : failed.Count.ToString(CultureInfo.InvariantCulture) + " card lifecycle check(s) failed: "
                    + string.Join(" | ", failed.ToArray());
        }
    }

    /// <summary>
    /// The names the card lifecycle scenario publishes: its twelve observations and the keys of every fact it
    /// records. Both are frozen by the GC-014 contract, so a caller asserts on them directly.
    /// </summary>
    public static class CardLifecycleKeys
    {
        // ---------------------------------------------------------------- the twelve observations

        /// <summary>Step 1: the owned world, the joined lane and the active provider.</summary>
        public const string StepWorld = "cards-lifecycle-world-and-provider";

        /// <summary>Step 2: suspension retracts the active behavior and discards a late completion (P-046, P-047).</summary>
        public const string StepSuspend = "cards-lifecycle-suspend-retracts-behavior";

        /// <summary>Step 3: resume rederives the current ancestry and restores the contribution (P-046).</summary>
        public const string StepResume = "cards-lifecycle-resume-restores-behavior";

        /// <summary>Step 4: losing a required provider makes its consumer wait in the same publication (P-012).</summary>
        public const string StepProviderLoss = "cards-lifecycle-provider-loss-makes-consumers-wait";

        /// <summary>Step 5: a compatible provider's return resumes the consumer in the same publication (P-012).</summary>
        public const string StepProviderReturn = "cards-lifecycle-provider-return-resumes-consumers";

        /// <summary>Step 6: an in-place replacement stages a candidate while the old activation runs (P-046).</summary>
        public const string StepReplacement = "cards-lifecycle-replacement-stages-while-old-runs";

        /// <summary>Step 7: unload closes ingress, retracts the contribution and retires the leases (P-047, P-048).</summary>
        public const string StepUnload = "cards-lifecycle-unload-closes-ingress-and-retracts";

        /// <summary>Step 8: an outstanding job prevents buffer release until it completes (P-047, P-048).</summary>
        public const string StepJobFence = "cards-lifecycle-blocked-job-prevents-buffer-release";

        /// <summary>Step 9: every invalid transition is refused as a value (P-046).</summary>
        public const string StepInvalidTransitions = "cards-lifecycle-invalid-transitions-rejected";

        /// <summary>Step 10: repeated operations obey the identity ledger (P-050, P-051).</summary>
        public const string StepRepeatedOperations = "cards-lifecycle-repeated-operations-obey-ledger";

        /// <summary>Step 11: teardown settles every job and returns the registry to its baseline (P-047, P-048).</summary>
        public const string StepTeardown = "cards-lifecycle-teardown-settles-and-disposes";

        /// <summary>Step 12: the run carries its own fact digest.</summary>
        public const string StepFacts = "cards-lifecycle-facts";

        // ---------------------------------------------------------------- world and provider

        /// <summary>`InstallationState.ToString()` of the host world's lifecycle.</summary>
        public const string FactWorldLifecycle = "worldLifecycle";

        /// <summary>True when the lane's published revision/epoch pair equals the world's (P-006).</summary>
        public const string FactLaneJoined = "laneJoined";

        /// <summary>The provider's published state before the suspend.</summary>
        public const string FactProviderStateBefore = "providerStateBefore";

        /// <summary>Derived binding rows of the published assembly attributed to the provider.</summary>
        public const string FactProviderRowsBefore = "providerRowsBefore";

        /// <summary>`UnityWorldRegistry.Count` captured before this run created its world.</summary>
        public const string FactRegistryBeforeCreate = "registryBeforeCreate";

        // ---------------------------------------------------------------- suspend

        /// <summary>The provider's published state after the suspend publication.</summary>
        public const string FactSuspendState = "suspendState";

        /// <summary>Rows still attributed to the provider after the suspend publication (must be zero).</summary>
        public const string FactSuspendRowsAfter = "suspendRowsAfter";

        /// <summary>Command routes the suspend publication closed (P-047).</summary>
        public const string FactSuspendClosedRoutes = "suspendClosedRoutes";

        /// <summary>The decision a completion stamped for the pre-suspend epoch received (P-047).</summary>
        public const string FactSuspendLateCompletion = "suspendLateCompletion";

        /// <summary>Live callback activations the suspended installation holds afterwards (zero, P-047).</summary>
        public const string FactSuspendGateLiveActivations = "suspendGateLiveActivations";

        // ---------------------------------------------------------------- resume

        /// <summary>The provider's published state after the resume publication.</summary>
        public const string FactResumeState = "resumeState";

        /// <summary>Rows attributed to the provider after the resume, back to the pre-suspend count.</summary>
        public const string FactResumeRowsAfter = "resumeRowsAfter";

        // ---------------------------------------------------------------- provider loss

        /// <summary>The consumer's published state in the publication that removed its provider (P-012).</summary>
        public const string FactLossConsumerState = "lossConsumerState";

        /// <summary>Consumers `PublishedOperation.WaitingConsumers` named in that same publication.</summary>
        public const string FactLossWaitingConsumers = "lossWaitingConsumers";

        /// <summary>Service bindings the waiting consumer still holds (zero, P-012).</summary>
        public const string FactLossConsumerBindings = "lossConsumerBindings";

        /// <summary>Attributed rows the consumer's retraction reported in that publication.</summary>
        public const string FactLossRetractedRows = "lossRetractedRows";

        // ---------------------------------------------------------------- provider return

        /// <summary>The consumer's published state in the publication that returned a provider (P-012).</summary>
        public const string FactReturnConsumerState = "returnConsumerState";

        /// <summary>Consumers `PublishedOperation.ResumedConsumers` named in that publication.</summary>
        public const string FactReturnResumedConsumers = "returnResumedConsumers";

        /// <summary>Rows attributed to the consumer again after the return.</summary>
        public const string FactReturnRows = "returnRows";

        // ---------------------------------------------------------------- replacement

        /// <summary>Candidates the replacement staged while the old activation kept running (P-046).</summary>
        public const string FactReplacementStagedCandidates = "replacementStagedCandidates";

        /// <summary>True when the old activation still held authority when the candidate staged (P-046).</summary>
        public const string FactReplacementOldHoldsAuthority = "replacementOldHoldsAuthority";

        /// <summary>True when the in-place replacement advanced the activation epoch (P-006).</summary>
        public const string FactReplacementEpochChanged = "replacementEpochChanged";

        /// <summary>True when the in-place replacement kept the installation generation (P-005, P-046).</summary>
        public const string FactReplacementGenerationUnchanged = "replacementGenerationUnchanged";

        /// <summary>The installation's published state after the replacement.</summary>
        public const string FactReplacementStateAfter = "replacementStateAfter";

        /// <summary>Rows attributed to the installation after the replacement.</summary>
        public const string FactReplacementRowsAfter = "replacementRowsAfter";

        // ---------------------------------------------------------------- unload

        /// <summary>The installation's published state after the unload publication.</summary>
        public const string FactUnloadState = "unloadState";

        /// <summary>Installations whose ingress this publication's close closed (P-047).</summary>
        public const string FactUnloadIngressClosed = "unloadIngressClosed";

        /// <summary>Attributed rows the unload's teardown retracted.</summary>
        public const string FactUnloadRetractedRows = "unloadRetractedRows";

        /// <summary>Leases the unload publication retired (P-048).</summary>
        public const string FactUnloadRetiredLeases = "unloadRetiredLeases";

        /// <summary>Resources the unload publication quarantined; zero when nothing is retained (P-048).</summary>
        public const string FactUnloadQuarantined = "unloadQuarantined";

        /// <summary>The decision a completion stamped for the retired activation received (P-047).</summary>
        public const string FactUnloadLateCompletion = "unloadLateCompletion";

        // ---------------------------------------------------------------- the blocked job

        /// <summary>Tracked jobs still outstanding when the blocked unload returned (P-047).</summary>
        public const string FactFenceOutstandingJobs = "fenceOutstandingJobs";

        /// <summary>The blocked teardown's refusal code, which is `TeardownBlocked` (P-048).</summary>
        public const string FactFenceBlockedCode = "fenceBlockedCode";

        /// <summary>True when the fenced lease stayed retained while the job owned its buffer (P-048).</summary>
        public const string FactFenceRetainedWhileOutstanding = "fenceRetainedWhileOutstanding";

        /// <summary>The teardown's `DisposeSettled`, which is false while a job owns the buffer (P-048).</summary>
        public const string FactFenceDisposeSettledWhileOutstanding = "fenceDisposeSettledWhileOutstanding";

        /// <summary>Quarantined references before the job completed (P-048).</summary>
        public const string FactFenceQuarantineBeforeRelease = "fenceQuarantineBeforeRelease";

        /// <summary>True when completing the job and releasing the quarantine retired the lease once (P-048).</summary>
        public const string FactFenceReleasedAfterCompletion = "fenceReleasedAfterCompletion";

        /// <summary>References still quarantined for the installation after the release (zero, P-048).</summary>
        public const string FactFenceQuarantineAfterRelease = "fenceQuarantineAfterRelease";

        // ---------------------------------------------------------------- invalid transitions

        /// <summary>How many of the six invalid transitions were refused as values.</summary>
        public const string FactInvalidRejectedCount = "invalidRejectedCount";

        /// <summary>True when every refusal left the published state and the lane revision unchanged.</summary>
        public const string FactInvalidStateUnchanged = "invalidStateUnchanged";

        /// <summary>The code of the refused second suspend (P-046).</summary>
        public const string FactInvalidSuspendTwiceCode = "invalidSuspendTwiceCode";

        /// <summary>The code of the refused resume of an active installation (P-046).</summary>
        public const string FactInvalidResumeActiveCode = "invalidResumeActiveCode";

        /// <summary>The code of the refused unmount of a disposed installation (P-046).</summary>
        public const string FactInvalidUnmountDisposedCode = "invalidUnmountDisposedCode";

        /// <summary>The code of the refused reconfigure of a disposed installation (P-046).</summary>
        public const string FactInvalidReconfigureDisposedCode = "invalidReconfigureDisposedCode";

        /// <summary>The code of the refused remount over a live installation identity (P-004).</summary>
        public const string FactInvalidRemountLiveIdentityCode = "invalidRemountLiveIdentityCode";

        /// <summary>The code of the refused teardown edge from a state with no teardown path (P-046).</summary>
        public const string FactInvalidTeardownPathRefusedCode = "invalidTeardownPathRefusedCode";

        // ---------------------------------------------------------------- repeated operations

        /// <summary>The code of the refused repeat of an already-settled suspend (P-050).</summary>
        public const string FactRepeatSuspendRefusedCode = "repeatSuspendRefusedCode";

        /// <summary>The admission kind a re-submitted operation identity and payload receives (P-050).</summary>
        public const string FactRepeatRetransmissionKind = "repeatRetransmissionKind";

        /// <summary>True when the retransmission returned the original recorded outcome (P-050).</summary>
        public const string FactRepeatReconfigureSameOutcome = "repeatReconfigureSameOutcome";

        /// <summary>The code of the refused second unmount of a disposed installation (P-050).</summary>
        public const string FactRepeatUnmountRefusedCode = "repeatUnmountRefusedCode";

        /// <summary>Rows the lane's operation ledger holds at the end of the run (P-050).</summary>
        public const string FactLedgerRowCount = "ledgerRowCount";

        // ---------------------------------------------------------------- teardown

        /// <summary>`UnityWorldRegistry.Count` after the run tore its world down.</summary>
        public const string FactRegistryAfterTeardown = "registryAfterTeardown";

        /// <summary>Tracked jobs the world still owed after teardown; zero (P-048).</summary>
        public const string FactOutstandingJobsAfterTeardown = "outstandingJobsAfterTeardown";

        /// <summary>Resources the world still retained after teardown; zero (P-048).</summary>
        public const string FactRetainedResourcesAfterTeardown = "retainedResourcesAfterTeardown";

        /// <summary>Logical steps an idle command-driven world committed; zero (P-036).</summary>
        public const string FactIdleSteps = "idleSteps";

        // ---------------------------------------------------------------- the card semantics

        /// <summary>Seat A's live score after the unload; the seeded score, because no command was admitted.</summary>
        public const string FactScoreAfterUnload = "scoreAfterUnload";

        /// <summary>The table's live version after the unload; the seeded version, because nothing settled.</summary>
        public const string FactTableVersionAfterUnload = "tableVersionAfterUnload";
    }

    /// <summary>
    /// The lifecycle edit payloads of the card family: the suspend/resume pair of the P-046 table and the
    /// reconfiguration of an in-place replacement. A mount and an unmount already exist on
    /// `CardTablePayloads`, so they are reused rather than restated here.
    /// </summary>
    public static class CardLifecyclePayloads
    {
        /// <summary>O-06: suspend one installation, retaining its definition and configuration (P-046).</summary>
        public static CompositionEditPayload Suspend(PluginInstanceId instance)
        {
            return Lifecycle(CompositionEditSubject.InstallSuspend, instance);
        }

        /// <summary>O-04: resume an explicitly suspended installation (P-046).</summary>
        public static CompositionEditPayload Resume(PluginInstanceId instance)
        {
            return Lifecycle(CompositionEditSubject.InstallResume, instance);
        }

        /// <summary>
        /// O-05: reconfigure one installation. The declared configuration hash is the canonical hash of the
        /// effective configuration the applier recomputes (schema defaults, the stored effective configuration,
        /// then this patch), exactly as `CompositionEditApplier.PlanReconfigure` composes it (P-020).
        /// </summary>
        public static CompositionEditPayload Reconfigure(
            PluginInstanceId instance,
            DefinitionRevision configRevision,
            ContentHash configHash,
            ConfigDocument patch)
        {
            if (patch == null)
            {
                throw new ArgumentNullException(nameof(patch));
            }

            return new CompositionEditPayload(
                CompositionEditSubject.InstallReconfigure,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                configRevision,
                configHash,
                patch,
                0,
                null,
                PropagationMode.Automatic);
        }

        private static CompositionEditPayload Lifecycle(CompositionEditSubject subject, PluginInstanceId instance)
        {
            if (instance.IsDefault)
            {
                throw new ArgumentException("A lifecycle edit names one real installation identity (P-004).", nameof(instance));
            }

            return new CompositionEditPayload(
                subject,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }
    }

    /// <summary>
    /// Runs the GC-014 card-family installation lifecycle over the real kernel and the real card composition,
    /// twice: once through <see cref="RunGeneratedCatalog"/> and once through <see cref="RunFixtureCatalog"/>.
    /// Nothing here fabricates a verdict: every observation is a conjunction of values read from the live
    /// composition lane, the world host, the activation ledger, the resource ledger and the quarantined set.
    /// </summary>
    public static class CardLifecycleScenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, so no two observations collide.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>The twelve observations, in the order the frozen contract fixes.</summary>
        private static readonly string[] StepNames =
        {
            CardLifecycleKeys.StepWorld,
            CardLifecycleKeys.StepSuspend,
            CardLifecycleKeys.StepResume,
            CardLifecycleKeys.StepProviderLoss,
            CardLifecycleKeys.StepProviderReturn,
            CardLifecycleKeys.StepReplacement,
            CardLifecycleKeys.StepUnload,
            CardLifecycleKeys.StepJobFence,
            CardLifecycleKeys.StepInvalidTransitions,
            CardLifecycleKeys.StepRepeatedOperations,
            CardLifecycleKeys.StepTeardown,
            CardLifecycleKeys.StepFacts,
        };

        /// <summary>Runs the scenario over the generated-style card catalog this package carries.</summary>
        public static CardLifecycleScenarioResult RunGeneratedCatalog()
        {
            return RunCatalog("generated-style catalog");
        }

        /// <summary>Runs the scenario over the same generated-style catalog with the run's fixture identities.</summary>
        public static CardLifecycleScenarioResult RunFixtureCatalog()
        {
            return RunCatalog("fixture catalog");
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the first run keeps its names and the
        /// second is prefixed with <see cref="FixtureRunPrefix"/> so no two observations collide.
        /// </summary>
        public static IReadOnlyList<CardLifecycleStep> RunBoth(
            out CardLifecycleFacts generatedFacts,
            out CardLifecycleFacts fixtureFacts)
        {
            CardLifecycleScenarioResult generated = RunGeneratedCatalog();
            generatedFacts = generated.Facts;

            CardLifecycleScenarioResult fixture = RunFixtureCatalog();
            fixtureFacts = fixture.Facts;

            var combined = new List<CardLifecycleStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                CardLifecycleStep step = fixture.Steps[i];
                combined.Add(new CardLifecycleStep(FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }

        private static CardLifecycleScenarioResult RunCatalog(string label)
        {
            try
            {
                CatalogBuildResult build = CardCatalogTable.Build();
                if (build.Catalog == null)
                {
                    return Failed("the " + label + " was rejected: " + build.Describe());
                }

                return new Executor(build.Catalog, LifecycleDeclarations()).Run();
            }
            catch (Exception exception)
            {
                return Failed("the " + label + " run did not start: " + Describe(exception));
            }
        }

        /// <summary>
        /// A run that could not start still reports every documented observation as failed, so a caller always
        /// sees the twelve names and the fact keys it asserts on (never a shorter suite).
        /// </summary>
        private static CardLifecycleScenarioResult Failed(string detail)
        {
            var facts = new CardLifecycleFacts();
            SeedDefaults(facts);

            var steps = new List<CardLifecycleStep>(StepNames.Length);
            for (int i = 0; i < StepNames.Length; i++)
            {
                steps.Add(new CardLifecycleStep(StepNames[i], false, detail));
            }

            return new CardLifecycleScenarioResult(steps, facts);
        }

        /// <summary>Every fact key seeded to a parseable value, so a failed run still carries the full bag.</summary>
        private static void SeedDefaults(CardLifecycleFacts facts)
        {
            const string unset = "<unset>";

            facts.Set(CardLifecycleKeys.FactWorldLifecycle, unset);
            facts.Set(CardLifecycleKeys.FactLaneJoined, false);
            facts.Set(CardLifecycleKeys.FactProviderStateBefore, unset);
            facts.Set(CardLifecycleKeys.FactProviderRowsBefore, -1L);
            facts.Set(CardLifecycleKeys.FactRegistryBeforeCreate, -1L);

            facts.Set(CardLifecycleKeys.FactSuspendState, unset);
            facts.Set(CardLifecycleKeys.FactSuspendRowsAfter, -1L);
            facts.Set(CardLifecycleKeys.FactSuspendClosedRoutes, -1L);
            facts.Set(CardLifecycleKeys.FactSuspendLateCompletion, unset);
            facts.Set(CardLifecycleKeys.FactSuspendGateLiveActivations, -1L);

            facts.Set(CardLifecycleKeys.FactResumeState, unset);
            facts.Set(CardLifecycleKeys.FactResumeRowsAfter, -1L);

            facts.Set(CardLifecycleKeys.FactLossConsumerState, unset);
            facts.Set(CardLifecycleKeys.FactLossWaitingConsumers, -1L);
            facts.Set(CardLifecycleKeys.FactLossConsumerBindings, -1L);
            facts.Set(CardLifecycleKeys.FactLossRetractedRows, -1L);

            facts.Set(CardLifecycleKeys.FactReturnConsumerState, unset);
            facts.Set(CardLifecycleKeys.FactReturnResumedConsumers, -1L);
            facts.Set(CardLifecycleKeys.FactReturnRows, -1L);

            facts.Set(CardLifecycleKeys.FactReplacementStagedCandidates, -1L);
            facts.Set(CardLifecycleKeys.FactReplacementOldHoldsAuthority, false);
            facts.Set(CardLifecycleKeys.FactReplacementEpochChanged, false);
            facts.Set(CardLifecycleKeys.FactReplacementGenerationUnchanged, false);
            facts.Set(CardLifecycleKeys.FactReplacementStateAfter, unset);
            facts.Set(CardLifecycleKeys.FactReplacementRowsAfter, -1L);

            facts.Set(CardLifecycleKeys.FactUnloadState, unset);
            facts.Set(CardLifecycleKeys.FactUnloadIngressClosed, -1L);
            facts.Set(CardLifecycleKeys.FactUnloadRetractedRows, -1L);
            facts.Set(CardLifecycleKeys.FactUnloadRetiredLeases, -1L);
            facts.Set(CardLifecycleKeys.FactUnloadQuarantined, -1L);
            facts.Set(CardLifecycleKeys.FactUnloadLateCompletion, unset);

            facts.Set(CardLifecycleKeys.FactFenceOutstandingJobs, -1L);
            facts.Set(CardLifecycleKeys.FactFenceBlockedCode, unset);
            facts.Set(CardLifecycleKeys.FactFenceRetainedWhileOutstanding, false);
            facts.Set(CardLifecycleKeys.FactFenceDisposeSettledWhileOutstanding, false);
            facts.Set(CardLifecycleKeys.FactFenceQuarantineBeforeRelease, -1L);
            facts.Set(CardLifecycleKeys.FactFenceReleasedAfterCompletion, false);
            facts.Set(CardLifecycleKeys.FactFenceQuarantineAfterRelease, -1L);

            facts.Set(CardLifecycleKeys.FactInvalidRejectedCount, -1L);
            facts.Set(CardLifecycleKeys.FactInvalidStateUnchanged, false);
            facts.Set(CardLifecycleKeys.FactInvalidSuspendTwiceCode, unset);
            facts.Set(CardLifecycleKeys.FactInvalidResumeActiveCode, unset);
            facts.Set(CardLifecycleKeys.FactInvalidUnmountDisposedCode, unset);
            facts.Set(CardLifecycleKeys.FactInvalidReconfigureDisposedCode, unset);
            facts.Set(CardLifecycleKeys.FactInvalidRemountLiveIdentityCode, unset);
            facts.Set(CardLifecycleKeys.FactInvalidTeardownPathRefusedCode, unset);

            facts.Set(CardLifecycleKeys.FactRepeatSuspendRefusedCode, unset);
            facts.Set(CardLifecycleKeys.FactRepeatRetransmissionKind, unset);
            facts.Set(CardLifecycleKeys.FactRepeatReconfigureSameOutcome, false);
            facts.Set(CardLifecycleKeys.FactRepeatUnmountRefusedCode, unset);
            facts.Set(CardLifecycleKeys.FactLedgerRowCount, -1L);

            facts.Set(CardLifecycleKeys.FactRegistryAfterTeardown, -1L);
            facts.Set(CardLifecycleKeys.FactOutstandingJobsAfterTeardown, -1L);
            facts.Set(CardLifecycleKeys.FactRetainedResourcesAfterTeardown, -1L);
            facts.Set(CardLifecycleKeys.FactIdleSteps, -1L);

            facts.Set(CardLifecycleKeys.FactScoreAfterUnload, -1L);
            facts.Set(CardLifecycleKeys.FactTableVersionAfterUnload, -1L);
        }

        /// <summary>
        /// The declaration set of one lifecycle run: the family's real card declarations plus the scenario's
        /// test-only provider/consumer pair. A mount resolves its manifest through the catalog by plugin type
        /// (P-009), so every declaration a mount names must appear here.
        /// </summary>
        private static IReadOnlyList<CatalogPluginDeclaration> LifecycleDeclarations()
        {
            return new List<CatalogPluginDeclaration>
            {
                CardTableFixture.TableRuntimeDeclaration(),
                CardTableFixture.ScoringDeclaration(true),
                CardTableFixture.ScoringDeclaration(false),
                CardTableFixture.RuleLibraryDeclaration(),
                Executor.ConsumerDeclaration(),
                Executor.RestoredLookupDeclaration(),
                Executor.ProbeDeclaration(),
            };
        }

        private static string Describe(Exception exception) =>
            "unhandled " + exception.GetType().FullName + ": " + exception.Message;

        /// <summary>
        /// One run: it owns the world it creates, the lane it joins, the pipeline it publishes through and the
        /// lifecycle controller it drives, and it reports twelve observations plus one fact bag.
        /// </summary>
        private sealed class Executor
        {
            /// <summary>Host ticks one pump is handed; a command-driven world captures its origin at its first pump.</summary>
            private const ulong HostTicks = 1_000_000UL;

            /// <summary>Frames the world is pumped while it must stay still (the P-036 idle proof).</summary>
            private const int IdleFrames = 8;

            private const ulong ScratchCapacityBytes = 4096UL;

            private const ulong ScratchBytesPerSlot = 64UL;

            private const ulong StagedByteCeiling = 1024UL * 1024UL;

            /// <summary>Seats the seeded market holds; the seeder's own count, asserted in step 1.</summary>
            private const int SeededSeatCount = 4;

            /// <summary>
            /// `cards.set-bonus` rows the family's festival provider owns: one for each League A seat. The
            /// eligible-but-isolated practice seat receives none, because its scope's isolation set names
            /// `cards.set-bonus` (P-016), and the scoreboard advertises no card schema at all (07 s2.1).
            /// </summary>
            private const int ExpectedProviderRows = 2;

            private const int LifecycleProbeBonus = 5;

            private const string ConsumerStableName = "cards.lifecycle-consumer";

            private const string LookupStableName = "cards.lifecycle-lookup-restored";

            private const string ProbeStableName = "cards.lifecycle-probe";

            private const string ScratchScopeStableName = "cards.lifecycle-scratch";

            /// <summary>Rule names of the two test-only providers; each rule identity is declared once (P-021).</summary>
            private const string ConsumerRuleStableName = "cards.lifecycle-consumer.score";

            private const string ProbeRuleStableName = "cards.lifecycle-probe.score";

            /// <summary>Category salts of the run's deterministic identities, so two runs are byte-identical.</summary>
            private const ulong SessionSalt = 0x434152444C494645UL;

            private const ulong LeaseSalt = 0x6C656173654C4946UL;

            private const ulong JobSalt = 0x6A6F626C49464531UL;

            private readonly CatalogManifestSource manifests;
            private readonly CardSeatApplier seatApplier = new CardSeatApplier();
            private readonly MarketTableApplier tableApplier = new MarketTableApplier();
            private readonly CardDerivationValueSource values = CardDerivationValueSource.Default();
            private readonly LifecycleResourceFactory resourceFactory;
            private readonly List<CardLifecycleStep> steps = new List<CardLifecycleStep>();
            private readonly CardLifecycleFacts facts = new CardLifecycleFacts();
            private readonly IdSequence sessionSequence = new IdSequence(SessionSalt);
            private readonly IdSequence leaseSequence = new IdSequence(LeaseSalt);

            private UnityWorldHost? host;
            private CardTableModule? module;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private WorldCompositionBridge? bridge;
            private LifecycleController? controller;
            private PipelineDescriptorReport? descriptorReport;
            private WorldTimeDriver? time;
            private ulong operationSequence;
            private int admittedOperations;
            private int registryBeforeCreate;

            // The subject installation of steps 1 to 7: the family's own scoring provider at `cards.league-a`.
            private readonly PluginInstanceId provider = CardTableFixture.FestivalScoringInstance;

            // Step 4's consumer: a scoring-style provider that *requires* the definition-lookup contract, and
            // the compatible provider that returns in step 5 (the real rule-library manifest, own identity).
            private readonly PluginInstanceId consumer = CardTableKeys.Instance(ConsumerStableName);
            private readonly PluginInstanceId restoredLookup = CardTableKeys.Instance(LookupStableName);

            // Step 8's fresh installation and the scratch scope both step 7's staging edit and step 8 mount in.
            private readonly PluginInstanceId probe = CardTableKeys.Instance(ProbeStableName);
            private readonly ScopeId scratch = CardIdentity.Scope(ScratchScopeStableName);

            // Step 6's reconfiguration, kept so step 10 can retransmit it and read its original outcome.
            private CompositionEditPayload? reconfigurePayload;
            private OperationId reconfigureOperation;
            private string reconfigureOutcome = string.Empty;

            private bool teardownAttempted;

            /// <summary>Creates one run over one already-built catalog.</summary>
            public Executor(ImmutableCatalog catalog, IReadOnlyList<CatalogPluginDeclaration> declarations)
            {
                if (catalog == null)
                {
                    throw new ArgumentNullException(nameof(catalog));
                }

                manifests = new CatalogManifestSource(catalog, declarations);
                resourceFactory = new LifecycleResourceFactory(new IdSequence(LeaseSalt + 1UL), CardTableKeys.PluginFactoryKey);
            }

            /// <summary>Runs the twelve observations in order; no exception leaves this method.</summary>
            public CardLifecycleScenarioResult Run()
            {
                SeedDefaults(facts);
                facts.Set(CardLifecycleKeys.FactRegistryBeforeCreate, UnityWorldRegistry.Count);

                WorldAndProvider();
                SuspendRetractsBehavior();
                ResumeRestoresBehavior();
                ProviderLossMakesConsumersWait();
                ProviderReturnResumesConsumers();
                ReplacementStagesWhileOldRuns();
                UnloadClosesIngressAndRetracts();
                BlockedJobPreventsBufferRelease();
                InvalidTransitionsRejected();
                RepeatedOperationsObeyLedger();
                TeardownSettlesAndDisposes();
                FactsStep();

                return new CardLifecycleScenarioResult(steps, facts);
            }

            // ---------------------------------------------------------------- the test-only declarations

            /// <summary>
            /// Step 4's consumer: a scoring-style provider whose declared capability is its own
            /// (`cards.lifecycle-score`) and whose dependency on the definition-lookup contract is **required**,
            /// so removing the rule library leaves it `WaitingForDependencies` in the same publication (P-012).
            /// </summary>
            public static CatalogPluginDeclaration ConsumerDeclaration() =>
                new CatalogPluginDeclaration(
                    Manifest(CardTableKeys.PluginType(ConsumerStableName), ConsumerRuleStableName, RequiredLookup()),
                    ConfigDocument.Empty);

            /// <summary>
            /// Step 5's compatible provider: the real rule-library declaration with its own plugin type and
            /// instance identity, so the returning provider exports the same contract through its own
            /// installation identity (P-009, P-012).
            /// </summary>
            public static CatalogPluginDeclaration RestoredLookupDeclaration() =>
                new CatalogPluginDeclaration(
                    CardTableDeclarations.RuleLibrary(
                        CardTableKeys.PluginType(LookupStableName),
                        CardTableKeys.PluginFactoryKey,
                        CardTableKeys.ConfigSchema),
                    ConfigDocument.Empty);

            /// <summary>
            /// Step 8's fresh installation: the same declaration shape as the consumer, without a dependency, so
            /// it publishes `Active` on its own and can own a staged buffer before any job exists.
            /// </summary>
            public static CatalogPluginDeclaration ProbeDeclaration() =>
                new CatalogPluginDeclaration(
                    Manifest(CardTableKeys.PluginType(ProbeStableName), ProbeRuleStableName, null),
                    ConfigDocument.Empty);

            private static PluginManifest Manifest(
                PluginTypeId pluginType,
                string ruleStableName,
                IReadOnlyList<ServiceDependency>? dependencies)
            {
                return new PluginManifest(
                    pluginType,
                    "1.0.0",
                    ContentHash.Empty,
                    new SupportedProtocolRange(1, 0, 0),
                    null,
                    CardTableKeys.ConfigSchema,
                    CardTableKeys.PluginFactoryKey,
                    null,
                    dependencies,
                    new List<CapabilityContract> { ScoreContract() },
                    new List<DerivationRule> { ScoreRule(ruleStableName) },
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            /// <summary>
            /// The test-only capability both lifecycle providers declare: one output slot, `Additive`, and the
            /// card rules package's *registered* Int32 sum reducer and always-accepting predicate, so the
            /// derivation runs the registered path exactly as the family's own scoring contract does (P-017,
            /// P-019, P-028).
            /// </summary>
            private static CapabilityContract ScoreContract()
            {
                return new CapabilityContract(
                    CardIdentity.CapabilityRef("cards.lifecycle-score"),
                    CardVocabulary.BonusStratum,
                    new List<OutputSlotSchema>
                    {
                        new OutputSlotSchema(
                            CardIdentity.Slot("cards.lifecycle-score.slot-0"),
                            CardIdentity.SchemaRef("cards.lifecycle-score-value")),
                    },
                    new List<SlotCompositionPolicy>
                    {
                        new SlotCompositionPolicy(
                            CardIdentity.Slot("cards.lifecycle-score.slot-0"),
                            CompositionPolicy.Additive,
                            CardVocabulary.BonusReducerKey),
                    },
                    null);
            }

            /// <summary>
            /// One lifecycle provider's rule: the seat recipe is its selector, so its contribution reaches every
            /// seat beneath its scope and nothing else (P-015).
            /// </summary>
            private static DerivationRule ScoreRule(string ruleStableName)
            {
                return new DerivationRule(
                    CardIdentity.Rule(ruleStableName),
                    CardIdentity.CapabilityRef("cards.lifecycle-score"),
                    CardVocabulary.BonusStratum,
                    1U,
                    new List<SchemaRef> { CardVocabulary.SelectorSchema(CardVocabulary.CardSeatRecipe) },
                    CardVocabulary.AlwaysPredicateKey,
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Additive,
                    CardTableDeclarations.WriteInt32(LifecycleProbeBonus));
            }

            /// <summary>
            /// The consumer's required dependency on the real definition-lookup contract. The card family's own
            /// table-runtime declaration declares the same contract as optional, which is why the scenario
            /// declares its own consumer instead of relying on that edge.
            /// </summary>
            private static IReadOnlyList<ServiceDependency> RequiredLookup()
            {
                return new List<ServiceDependency>
                {
                    new ServiceDependency(
                        CardTableKeys.LookupContract,
                        new VersionRange(1U, 1U),
                        true,
                        ServiceResolutionDomain.AncestorsAndSelf,
                        default(ProviderInstallationId),
                        default(FactoryKey)),
                };
            }

            // ---------------------------------------------------------------- 1. the world and its provider

            private void WorldAndProvider()
            {
                const string name = CardLifecycleKeys.StepWorld;
                try
                {
                    registryBeforeCreate = UnityWorldRegistry.Count;
                    facts.Set(CardLifecycleKeys.FactRegistryBeforeCreate, registryBeforeCreate);

                    CompileOwnershipAndSchedule();
                    if (descriptorReport == null || descriptorReport.Descriptor == null || descriptorReport.Adaptation == null)
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the ownership/schedule compile produced no usable descriptor"));
                        return;
                    }

                    WorldId world = NextSession();
                    WorldCreateRequest request = CardTableRegistration.CommandDrivenRequest(
                        world, NextOperation(world), ContentHash.Empty);
                    UnityWorldRegistration registration = CardTableRegistration.Create(
                        descriptorReport.Adaptation!,
                        CardTableRegistration.Systems());

                    bool created = UnityWorldRegistry.TryCreate(
                        request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        steps.Add(new CardLifecycleStep(name, false, "world creation failed: " + result.Code + ": " + result.Detail));
                        return;
                    }

                    module = CardTableModule.Attach(host);
                    registry = new TargetRegistry(world, 16);
                    SpawnRecipeCatalog recipes = CardTableRecipes.Catalog(seatApplier, tableApplier);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        recipes,
                        new MigrationRegistry(new List<ISlotMigration>()),
                        descriptorReport.Descriptor);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    // The lane joins the world's published initial assembly (P-006): the same series, no offset.
                    lane = CompositionHost.CreateDefault(
                        world,
                        CardMarketComposition.MatchScope,
                        manifests,
                        resourceFactory,
                        CompositionLaneSeed.InitialAssembly);
                    bridge = new WorldCompositionBridge(host, lane, publisher);
                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        values,
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, CardTableKeys.Issuer),
                        new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot));
                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);

                    // The controller attaches the world's lifecycle binding to the lane, and that attachment is
                    // only legal before the lane has published anything (P-030), so it happens first.
                    controller = new LifecycleController(host, lane, publisher, pipeline);

                    bool scopesCreated = PublishEdits(CardMarketComposition.ScopeCreates(), out string scopeFailure);
                    if (!scopesCreated)
                    {
                        steps.Add(new CardLifecycleStep(name, false, scopeFailure));
                        return;
                    }

                    if (!CardTableFixture.SeedMarket(seeder, module, out DiagnosticCode seedCode, out string seedDetail))
                    {
                        steps.Add(new CardLifecycleStep(name, false, "seeding failed: " + seedCode + ": " + seedDetail));
                        return;
                    }

                    // The table runtime and the rule library live at the match root, exactly as the family's
                    // scenario mounts them; the two scoring providers mount at their own leagues (07 s2.1).
                    var installs = new List<CompositionEditPayload>
                    {
                        CardTablePayloads.Mount(
                            CardTableFixture.TableRuntimeDeclaration().Manifest,
                            CardTableFixture.TableRuntimeInstance,
                            CardMarketComposition.MatchScope),
                        CardTablePayloads.Mount(
                            CardTableFixture.RuleLibraryDeclaration().Manifest,
                            CardTableFixture.RuleLibraryInstance,
                            CardMarketComposition.MatchScope),
                    };
                    if (!PublishEdits(installs, out string installFailure))
                    {
                        steps.Add(new CardLifecycleStep(name, false, installFailure));
                        return;
                    }

                    if (!PublishEdits(CardTableFixture.MarketMounts(true, true), out string providerFailure))
                    {
                        steps.Add(new CardLifecycleStep(name, false, providerFailure));
                        return;
                    }

                    facts.Set(CardLifecycleKeys.FactWorldLifecycle, host.Lifecycle.ToString());
                    bool joined = AssemblyPublisher.MatchesPublishedAssembly(
                        lane.Committed.Revision,
                        lane.Committed.Epoch,
                        publisher.PublishedRevision,
                        host.CurrentEpoch);
                    facts.Set(CardLifecycleKeys.FactLaneJoined, joined);
                    facts.Set(CardLifecycleKeys.FactProviderStateBefore, StateTextOf(provider));
                    facts.Set(CardLifecycleKeys.FactProviderRowsBefore, AttributedRowsOf(provider));

                    bool pass = host.Lifecycle == WorldLifecycleState.Running
                        && joined
                        && StateTextOf(provider) == InstallationState.Active.ToString()
                        && AttributedRowsOf(provider) == ExpectedProviderRows
                        && module.SeatCount == SeededSeatCount
                        && UnityWorldRegistry.Count == registryBeforeCreate + 1
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && values.ReductionCount > 0;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "lifecycle=" + host.Lifecycle
                        + "; world=" + host.World.Session.ToString()
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; registryNow=" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; lane=" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "/" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + joined
                        + "; provider=" + StateTextOf(provider)
                        + "; providerRows=" + AttributedRowsOf(provider).ToString(CultureInfo.InvariantCulture)
                        + "; seats=" + module.SeatCount.ToString(CultureInfo.InvariantCulture)
                        + "; reductions=" + values.ReductionCount.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            /// <summary>GC-007's ownership validation and GC-009's compilation over the card declarations.</summary>
            private void CompileOwnershipAndSchedule()
            {
                descriptorReport = OwnershipSchedulePipeline.Build(
                    CardTableFixture.Manifests(),
                    CardTableRegistration.DispatchKinds(),
                    new SlotMigrationRegistry());
            }

            // ---------------------------------------------------------------- 2. suspend

            private void SuspendRetractsBehavior()
            {
                const string name = CardLifecycleKeys.StepSuspend;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    long generation = CommittedGenerationOf(provider);
                    long epoch = CommittedEpochOf(provider);
                    int gateBefore = lane!.Callbacks.LiveActivationCount;

                    OperationId operation = NextOperation();
                    LifecycleRequestReport report = SubmitLifecycle(CardLifecyclePayloads.Suspend(provider), operation);

                    // The provider's own live activation is what suspension retires (P-047); the gate's total
                    // count must therefore have fallen by exactly the one entry the install held.
                    bool gateRetired = !lane.Callbacks.TryGetActivation(provider, out ActivationStamp _);
                    int gateAfter = lane.Callbacks.LiveActivationCount;
                    facts.Set(CardLifecycleKeys.FactSuspendGateLiveActivations, gateRetired ? 0L : 1L);

                    facts.Set(CardLifecycleKeys.FactSuspendState, StateTextOf(provider));
                    facts.Set(CardLifecycleKeys.FactSuspendRowsAfter, AttributedRowsOf(provider));

                    ContributionRetraction? retraction = RetractionOf(report.Lifecycle, provider);
                    facts.Set(CardLifecycleKeys.FactSuspendClosedRoutes, retraction != null ? retraction.ClosedRoutes : -1L);

                    var lateToken = new AsyncWorkToken(operation, provider, new InstallationGeneration((ulong)generation), new ActivationEpoch((ulong)epoch), 0U);
                    CallbackGateDecision decision = controller!.Lifecycle.EvaluateCompletion(lateToken);
                    facts.Set(CardLifecycleKeys.FactSuspendLateCompletion, decision.ToString());

                    bool pass = report.Succeeded
                        && StateTextOf(provider) == InstallationState.Suspended.ToString()
                        && !controller.Lifecycle.HoldsAuthority(provider)
                        && gateRetired
                        && gateAfter < gateBefore
                        && retraction != null
                        && retraction.AttributedRows == ExpectedProviderRows
                        && retraction.ClosedRoutes >= 0
                        && AttributedRowsOf(provider) == 0L
                        && host!.Lifecycle == WorldLifecycleState.Running
                        && (decision == CallbackGateDecision.DiscardRetiredRoute
                            || decision == CallbackGateDecision.DiscardStaleActivation);

                    steps.Add(new CardLifecycleStep(name, pass,
                        "request=" + report.Code
                        + "; state=" + StateTextOf(provider)
                        + "; authority=" + controller.Lifecycle.HoldsAuthority(provider)
                        + "; gate=" + gateBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + gateAfter.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + facts.ValueOf(CardLifecycleKeys.FactProviderRowsBefore)
                        + "->" + AttributedRowsOf(provider).ToString(CultureInfo.InvariantCulture)
                        + "; retracted=" + (retraction != null ? retraction.AttributedRows.ToString(CultureInfo.InvariantCulture) : "<none>")
                        + "; closedRoutes=" + facts.ValueOf(CardLifecycleKeys.FactSuspendClosedRoutes)
                        + "; lateCompletion=" + decision));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            // ---------------------------------------------------------------- 3. resume

            private void ResumeRestoresBehavior()
            {
                const string name = CardLifecycleKeys.StepResume;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    OperationId operation = NextOperation();
                    LifecycleRequestReport report = SubmitLifecycle(CardLifecyclePayloads.Resume(provider), operation);

                    facts.Set(CardLifecycleKeys.FactResumeState, StateTextOf(provider));
                    facts.Set(CardLifecycleKeys.FactResumeRowsAfter, AttributedRowsOf(provider));

                    // A completion stamped for the *resumed* activation must be dispatched (P-047): the resume
                    // registered the new epoch, so the gate accepts work issued under it.
                    var freshToken = new AsyncWorkToken(
                        operation,
                        provider,
                        new InstallationGeneration((ulong)CommittedGenerationOf(provider)),
                        new ActivationEpoch((ulong)CommittedEpochOf(provider)),
                        0U);
                    CallbackGateDecision decision = controller!.Lifecycle.EvaluateCompletion(freshToken);

                    bool authority = controller.Lifecycle.HoldsAuthority(provider);
                    bool pass = report.Succeeded
                        && StateTextOf(provider) == InstallationState.Active.ToString()
                        && authority
                        && AttributedRowsOf(provider) == ExpectedProviderRows
                        && decision == CallbackGateDecision.Dispatch
                        && host!.Lifecycle == WorldLifecycleState.Running;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "request=" + report.Code
                        + "; state=" + StateTextOf(provider)
                        + "; authority=" + authority
                        + "; rows=" + facts.ValueOf(CardLifecycleKeys.FactProviderRowsBefore)
                        + "->" + AttributedRowsOf(provider).ToString(CultureInfo.InvariantCulture)
                        + "; freshCompletion=" + decision
                        + "; epoch=" + CommittedEpochOf(provider).ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            // ---------------------------------------------------------------- 4. provider loss

            private void ProviderLossMakesConsumersWait()
            {
                const string name = CardLifecycleKeys.StepProviderLoss;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    // The consumer joins the world at the match root, where the rule library already exports the
                    // lookup contract it requires, so its own mount publication is the one that binds it.
                    EditOutcome mounted = PublishEdit(
                        CardTablePayloads.Mount(ConsumerDeclaration().Manifest, consumer, CardMarketComposition.MatchScope),
                        NextOperation());
                    if (!mounted.Succeeded)
                    {
                        steps.Add(new CardLifecycleStep(name, false, mounted.Failure));
                        return;
                    }

                    long rowsBefore = AttributedRowsOf(consumer);
                    long bindingsBefore = BindingsOf(consumer);

                    // Removing the required provider is one publication; the wait happens in that same one.
                    EditOutcome removal = PublishEdit(CardTablePayloads.Unmount(CardTableFixture.RuleLibraryInstance), NextOperation());
                    if (!removal.Succeeded)
                    {
                        steps.Add(new CardLifecycleStep(name, false, removal.Failure));
                        return;
                    }

                    bool waits = removal.Lifecycle != null && removal.Lifecycle.Closure.Waits(consumer);
                    int waiting = removal.Lifecycle != null ? removal.Lifecycle.Closure.WaitingConsumers.Count : -1;
                    ContributionRetraction? retraction = RetractionOf(removal.Lifecycle, consumer);

                    facts.Set(CardLifecycleKeys.FactLossConsumerState, StateTextOf(consumer));
                    facts.Set(CardLifecycleKeys.FactLossWaitingConsumers, waiting);
                    facts.Set(CardLifecycleKeys.FactLossConsumerBindings, BindingsOf(consumer));
                    facts.Set(CardLifecycleKeys.FactLossRetractedRows, retraction != null ? retraction.AttributedRows : -1L);

                    bool pass = StateTextOf(consumer) == InstallationState.WaitingForDependencies.ToString()
                        && waiting == 1
                        && waits
                        && BindingsOf(consumer) == 0
                        && rowsBefore > 0
                        && bindingsBefore > 0
                        && retraction != null
                        && retraction.AttributedRows == rowsBefore
                        && AttributedRowsOf(consumer) == 0L;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "consumer=" + StateTextOf(consumer)
                        + "; waitingConsumers=" + waiting.ToString(CultureInfo.InvariantCulture)
                        + "; waits=" + waits
                        + "; bindings=" + bindingsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + BindingsOf(consumer).ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + rowsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + AttributedRowsOf(consumer).ToString(CultureInfo.InvariantCulture)
                        + "; retracted=" + facts.ValueOf(CardLifecycleKeys.FactLossRetractedRows)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            // ---------------------------------------------------------------- 5. provider return

            private void ProviderReturnResumesConsumers()
            {
                const string name = CardLifecycleKeys.StepProviderReturn;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    EditOutcome restored = PublishEdit(
                        CardTablePayloads.Mount(RestoredLookupDeclaration().Manifest, restoredLookup, CardMarketComposition.MatchScope),
                        NextOperation());
                    if (!restored.Succeeded)
                    {
                        steps.Add(new CardLifecycleStep(name, false, restored.Failure));
                        return;
                    }

                    bool resumed = restored.Lifecycle != null && restored.Lifecycle.Closure.Resumed(consumer);
                    int resumedCount = restored.Lifecycle != null ? restored.Lifecycle.Closure.ResumedConsumers.Count : -1;

                    facts.Set(CardLifecycleKeys.FactReturnConsumerState, StateTextOf(consumer));
                    facts.Set(CardLifecycleKeys.FactReturnResumedConsumers, resumedCount);
                    facts.Set(CardLifecycleKeys.FactReturnRows, AttributedRowsOf(consumer));

                    bool pass = StateTextOf(consumer) == InstallationState.Active.ToString()
                        && resumedCount == 1
                        && resumed
                        && BindingsOf(consumer) > 0
                        && AttributedRowsOf(consumer) == ReturnedRows()
                        && AttributedRowsOf(consumer) > 0L;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "consumer=" + StateTextOf(consumer)
                        + "; resumedConsumers=" + resumedCount.ToString(CultureInfo.InvariantCulture)
                        + "; resumes=" + resumed
                        + "; bindings=" + BindingsOf(consumer).ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + AttributedRowsOf(consumer).ToString(CultureInfo.InvariantCulture)
                        + "; lost=" + facts.ValueOf(CardLifecycleKeys.FactLossRetractedRows)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            /// <summary>The rows the consumer lost in step 4, which step 5 must restore exactly.</summary>
            private long ReturnedRows()
            {
                string lost = facts.ValueOf(CardLifecycleKeys.FactLossRetractedRows);
                return long.TryParse(lost, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : -1L;
            }

            // ---------------------------------------------------------------- 6. in-place replacement

            private void ReplacementStagesWhileOldRuns()
            {
                const string name = CardLifecycleKeys.StepReplacement;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    InstallEntry? entry = CommittedEntryOf(provider);
                    if (entry == null)
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the provider is not a committed installation"));
                        return;
                    }

                    ulong epochBefore = entry.Record.ActivationEpoch.Value;
                    ulong generationBefore = entry.Record.Generation.Value;
                    if (!entry.Record.ConfigRevision.TryIncrement(out DefinitionRevision nextRevision))
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the configuration revision is exhausted"));
                        return;
                    }

                    CompositionEditPayload payload = CardLifecyclePayloads.Reconfigure(
                        provider,
                        nextRevision,
                        ComposedConfigHashOf(entry),
                        ConfigDocument.Empty);
                    OperationId operation = NextOperation();

                    // Phase 1 of the plan: the candidate is staged and the running activation is untouched (P-046).
                    EditAdmission admission = Submit(payload, operation);
                    bool stagedOne = controller!.Lifecycle.Activations.StagedCandidateCount == 1;
                    bool oldHeldAuthority = controller.Lifecycle.HoldsAuthority(provider);
                    facts.Set(CardLifecycleKeys.FactReplacementStagedCandidates, stagedOne ? 1L : 0L);
                    facts.Set(CardLifecycleKeys.FactReplacementOldHoldsAuthority, oldHeldAuthority);

                    if (!admission.Staged)
                    {
                        steps.Add(new CardLifecycleStep(name, false,
                            "the reconfigure was refused: " + admission.Kind + "/" + DiagnosticCodeText.Of(admission.Code)));
                        return;
                    }

                    // Phase 2: one publication commits the candidate and retires the displaced activation's epoch.
                    IReadOnlyList<PublishedOperation> published = lane!.Drain();
                    PublishedOperation? operationResult = FindPublished(published, operation);
                    if (operationResult == null || operationResult.Outcome == Outcome.Rejected)
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the reconfigure publication was refused"));
                        return;
                    }

                    if (!PublishDerived(operation, out string derivedFailure))
                    {
                        steps.Add(new CardLifecycleStep(name, false, derivedFailure));
                        return;
                    }

                    reconfigurePayload = payload;
                    reconfigureOperation = operation;
                    reconfigureOutcome = operationResult.Outcome.ToString();

                    InstallEntry? after = CommittedEntryOf(provider);
                    bool epochChanged = after != null && after.Record.ActivationEpoch.Value != epochBefore;
                    bool generationUnchanged = after != null && after.Record.Generation.Value == generationBefore;
                    bool displacedOldActivation = TeardownOf(operationResult.Lifecycle, provider, epochBefore) != null;

                    facts.Set(CardLifecycleKeys.FactReplacementEpochChanged, epochChanged);
                    facts.Set(CardLifecycleKeys.FactReplacementGenerationUnchanged, generationUnchanged);
                    facts.Set(CardLifecycleKeys.FactReplacementStateAfter, StateTextOf(provider));
                    facts.Set(CardLifecycleKeys.FactReplacementRowsAfter, AttributedRowsOf(provider));

                    bool pass = admission.Kind == AdmissionKind.Fresh
                        && stagedOne
                        && oldHeldAuthority
                        && displacedOldActivation
                        && epochChanged
                        && generationUnchanged
                        && StateTextOf(provider) == InstallationState.Active.ToString()
                        && AttributedRowsOf(provider) == ExpectedProviderRows
                        && operationResult.Lifecycle != null;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "stagedCandidate=" + stagedOne
                        + "; oldHoldsAuthority=" + oldHeldAuthority
                        + "; epoch=" + epochBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + (after != null ? after.Record.ActivationEpoch.Value : 0UL).ToString(CultureInfo.InvariantCulture)
                        + "; generation=" + generationBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + (after != null ? after.Record.Generation.Value : 0UL).ToString(CultureInfo.InvariantCulture)
                        + "; state=" + StateTextOf(provider)
                        + "; rows=" + AttributedRowsOf(provider).ToString(CultureInfo.InvariantCulture)
                        + "; displacedTeardown=" + displacedOldActivation
                        + "; outcome=" + operationResult.Outcome));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// The hash the applier recomputes for a reconfiguration of this entry: the declared schema defaults,
            /// the installation's stored effective configuration, then the (empty) local patch (P-020).
            /// </summary>
            private ContentHash ComposedConfigHashOf(InstallEntry entry)
            {
                ConfigComposeResult composed = ConfigComposer.Compose(new[]
                {
                    new ConfigLayer(
                        ConfigLayerOrigin.SchemaDefaults,
                        entry.Manifest.ConfigSchema.Id.Value,
                        SchemaDefaultsOf(entry)),
                    new ConfigLayer(ConfigLayerOrigin.InheritedContribution, entry.Instance.Value, entry.Config),
                    new ConfigLayer(ConfigLayerOrigin.LocalPatch, entry.Instance.Value, ConfigDocument.Empty),
                });

                return ConfigDocumentCodec.HashOf(composed.Value);
            }

            private ConfigDocument SchemaDefaultsOf(InstallEntry entry)
            {
                return manifests.TryGetConfigDefaults(entry.Manifest.ConfigSchema, out ConfigDocument? defaults) && defaults != null
                    ? defaults
                    : ConfigDocument.Empty;
            }

            // ---------------------------------------------------------------- 7. unload

            private void UnloadClosesIngressAndRetracts()
            {
                const string name = CardLifecycleKeys.StepUnload;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    // A real lease owned by the provider, acquired on one benign publication: the unload then has
                    // something to retire in reverse acquisition order (P-048).
                    if (!StageProviderLease())
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the provider's staged lease was refused"));
                        return;
                    }

                    Id128 leaseId = providerLease;
                    bool heldBefore = lane!.Resources.RetainedCountFor(provider) >= 1;
                    long generation = CommittedGenerationOf(provider);
                    long epoch = CommittedEpochOf(provider);
                    int closedBefore = controller!.Binding.ClosedInstallationCount;

                    EditOutcome unload = PublishEdit(CardTablePayloads.Unmount(provider), NextOperation());
                    if (!unload.Succeeded)
                    {
                        steps.Add(new CardLifecycleStep(name, false, unload.Failure));
                        return;
                    }

                    int closedDelta = controller.Binding.ClosedInstallationCount - closedBefore;
                    TeardownReport? teardown = TeardownOf(unload.Lifecycle, provider, (ulong)epoch);

                    facts.Set(CardLifecycleKeys.FactUnloadState, StateTextOf(provider));
                    facts.Set(CardLifecycleKeys.FactUnloadIngressClosed, closedDelta);
                    facts.Set(CardLifecycleKeys.FactUnloadRetractedRows,
                        teardown != null ? teardown.Retraction.AttributedRows : -1L);
                    facts.Set(CardLifecycleKeys.FactUnloadRetiredLeases,
                        unload.Lifecycle != null ? unload.Lifecycle.Cleanup.Retired.Count : -1L);
                    facts.Set(CardLifecycleKeys.FactUnloadQuarantined,
                        unload.Lifecycle != null ? unload.Lifecycle.Cleanup.Quarantined.Count : -1L);

                    var lateToken = new AsyncWorkToken(
                        unload.Operation,
                        provider,
                        new InstallationGeneration((ulong)generation),
                        new ActivationEpoch((ulong)epoch),
                        0U);
                    CallbackGateDecision decision = controller.Lifecycle.EvaluateCompletion(lateToken);
                    facts.Set(CardLifecycleKeys.FactUnloadLateCompletion, decision.ToString());

                    facts.Set(CardLifecycleKeys.FactScoreAfterUnload, SeatScoreOf(CardTableKeys.SeatAOrdinal));
                    facts.Set(CardLifecycleKeys.FactTableVersionAfterUnload, TableVersionOf());

                    bool released = lane.Resources.RetainedCountFor(provider) == 0;
                    bool pass = heldBefore
                        && StateTextOf(provider) == InstallationState.Disposed.ToString()
                        && closedDelta >= 1
                        && teardown != null
                        && teardown.Retraction.AttributedRows == ExpectedProviderRows
                        && teardown.ClosedRoutes >= 0
                        && unload.Lifecycle != null
                        && unload.Lifecycle.Cleanup.Retired.Count >= 1
                        && unload.Lifecycle.Cleanup.Quarantined.Count == 0
                        && released
                        && (decision == CallbackGateDecision.DiscardRetiredRoute
                            || decision == CallbackGateDecision.DiscardStaleActivation)
                        && SeatScoreOf(CardTableKeys.SeatAOrdinal) == CardTableKeys.SeededSeatScore
                        && TableVersionOf() == CardTableKeys.SeededTableVersion;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "state=" + StateTextOf(provider)
                        + "; ingressClosed+=" + closedDelta.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + (teardown != null ? teardown.Retraction.AttributedRows : -1L).ToString(CultureInfo.InvariantCulture)
                        + "/" + facts.ValueOf(CardLifecycleKeys.FactProviderRowsBefore)
                        + "; retiredLeases=" + facts.ValueOf(CardLifecycleKeys.FactUnloadRetiredLeases)
                        + "; quarantined=" + facts.ValueOf(CardLifecycleKeys.FactUnloadQuarantined)
                        + "; retained=" + lane.Resources.RetainedCountFor(provider).ToString(CultureInfo.InvariantCulture)
                        + "; leaseHeld=" + heldBefore
                        + "; lease=" + leaseId.ToString()
                        + "; lateCompletion=" + decision
                        + "; score=" + SeatScoreOf(CardTableKeys.SeatAOrdinal).ToString(CultureInfo.InvariantCulture)
                        + "; tableVersion=" + TableVersionOf().ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            // ---------------------------------------------------------------- 8. the blocked job

            private void BlockedJobPreventsBufferRelease()
            {
                const string name = CardLifecycleKeys.StepJobFence;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    // A fresh installation in the scratch scope (no target lives there, so it contributes no
                    // row and its own mount is the only thing this publication changes).
                    OperationId mountOperation = NextOperation();
                    EditAdmission admission = Submit(
                        CardTablePayloads.Mount(ProbeDeclaration().Manifest, probe, scratch),
                        mountOperation);
                    if (!admission.Staged)
                    {
                        steps.Add(new CardLifecycleStep(name, false,
                            "the probe's mount was refused: " + admission.Kind + "/" + DiagnosticCodeText.Of(admission.Code)));
                        return;
                    }

                    Id128 leaseId = NextLease();
                    if (!lane!.StageResource(
                            mountOperation,
                            probe,
                            new ResourceKey(leaseId),
                            CardTableDeclarations.WriteInt32(0),
                            null,
                            out DiagnosticCode stageCode))
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the probe's staged lease was refused: " + stageCode));
                        return;
                    }

                    IReadOnlyList<PublishedOperation> mountPublished = lane.Drain();
                    PublishedOperation? mountedProbe = FindPublished(mountPublished, mountOperation);
                    if (mountedProbe == null || mountedProbe.Outcome == Outcome.Rejected)
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the probe's mount publication was refused"));
                        return;
                    }

                    if (!PublishDerived(mountOperation, out string derivedFailure))
                    {
                        steps.Add(new CardLifecycleStep(name, false, derivedFailure));
                        return;
                    }

                    // The tracked job names the lease the probe owns, and it stays outstanding: the fence is the
                    // composition job registry the teardown sequencer reads (P-047, P-048). It is deliberately not
                    // registered through the Unity job bridge, because that bridge completes a job at a teardown
                    // boundary and there is no native job here to complete.
                    Id128 jobId = new Id128(JobSalt, ++jobSequence);
                    controller!.Lifecycle.Jobs.Track(
                        jobId,
                        probe,
                        CardTableKeys.InputStage,
                        CardTableKeys.InputSystem,
                        host!.CurrentEpoch,
                        host.CurrentStep,
                        new List<Id128> { leaseId });

                    TeardownReport teardown = controller.Unload(probe, NextOperation());

                    bool ledgerState = LedgerStateTextOf(probe) == InstallationState.Retiring.ToString();
                    bool retained = lane.Resources.RetainedCountFor(probe) >= 1
                        && lane.Resources.TryGetRecord(leaseId, out WorldResourceRecord record)
                        && record.State == ResourceRetirementState.Quarantined;

                    long outstandingBefore = controller.Lifecycle.Jobs.OutstandingCount;
                    facts.Set(CardLifecycleKeys.FactFenceOutstandingJobs, outstandingBefore);
                    facts.Set(CardLifecycleKeys.FactFenceBlockedCode, DiagnosticCodeText.Of(teardown.Code));
                    facts.Set(CardLifecycleKeys.FactFenceRetainedWhileOutstanding, retained);
                    facts.Set(CardLifecycleKeys.FactFenceDisposeSettledWhileOutstanding, teardown.DisposeSettled);
                    int quarantineBefore = controller.Lifecycle.Quarantine.Count;
                    facts.Set(CardLifecycleKeys.FactFenceQuarantineBeforeRelease, quarantineBefore);

                    long retiredBefore = lane.Resources.RetiredCount;
                    bool completed = controller.JobFence.Complete(jobId);
                    CleanupReport cleanup = controller.ReleaseQuarantine(probe);
                    long retiredDelta = lane.Resources.RetiredCount - retiredBefore;
                    int quarantineAfter = controller.Lifecycle.Quarantine.EntriesFor(probe).Count;

                    facts.Set(CardLifecycleKeys.FactFenceReleasedAfterCompletion, completed && retiredDelta == 1L);
                    facts.Set(CardLifecycleKeys.FactFenceQuarantineAfterRelease, quarantineAfter);

                    bool pass = outstandingBefore >= 1
                        && teardown.BlockedByJobFence
                        && teardown.Code == DiagnosticCode.TeardownBlocked
                        && !teardown.DisposeSettled
                        && teardown.Blocked
                        && ledgerState
                        && retained
                        && quarantineBefore >= 1
                        && completed
                        && cleanup.Retired.Count == 1
                        && retiredDelta == 1L
                        && quarantineAfter == 0
                        && lane.Resources.RetainedCountFor(probe) == 0;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "outstanding=" + facts.ValueOf(CardLifecycleKeys.FactFenceOutstandingJobs)
                        + "; code=" + facts.ValueOf(CardLifecycleKeys.FactFenceBlockedCode)
                        + "; disposeSettled=" + teardown.DisposeSettled
                        + "; blockedByFence=" + teardown.BlockedByJobFence
                        + "; state=" + LedgerStateTextOf(probe)
                        + "; retained=" + retained
                        + "; quarantined=" + facts.ValueOf(CardLifecycleKeys.FactFenceQuarantineBeforeRelease)
                        + "; completed=" + completed
                        + "; retired+=" + retiredDelta.ToString(CultureInfo.InvariantCulture)
                        + "; quarantineAfter=" + quarantineAfter.ToString(CultureInfo.InvariantCulture)
                        + "; lease=" + leaseId.ToString()));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            // ---------------------------------------------------------------- 9. invalid transitions

            private void InvalidTransitionsRejected()
            {
                const string name = CardLifecycleKeys.StepInvalidTransitions;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    ulong revisionAtStart = lane!.Committed.Revision.Value;
                    var refused = new List<string>(6);
                    bool stateUnchanged = true;

                    // (1) resume an active installation: `Active` has no `Preparing` successor (06 s1, P-046).
                    stateUnchanged &= Refuse(CardLifecyclePayloads.Resume(consumer), consumer, refused);
                    facts.Set(CardLifecycleKeys.FactInvalidResumeActiveCode, LastRefusedCode(refused));

                    // (2) suspend an already suspended installation, and suspend it twice in one step.
                    if (!PublishEdit(CardLifecyclePayloads.Suspend(restoredLookup), NextOperation()).Succeeded)
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the setup suspend of the restored provider was refused"));
                        return;
                    }

                    // Every refusal below must leave this revision exactly where it is; each refusal already
                    // checked its own invariance, so this second baseline proves the aggregate claim too.
                    ulong revisionBeforeRefusals = lane.Committed.Revision.Value;

                    stateUnchanged &= Refuse(CardLifecyclePayloads.Suspend(restoredLookup), restoredLookup, refused);
                    facts.Set(CardLifecycleKeys.FactInvalidSuspendTwiceCode, LastRefusedCode(refused));

                    // (3) unmount an installation this run already disposed in step 4.
                    stateUnchanged &= Refuse(CardTablePayloads.Unmount(CardTableFixture.RuleLibraryInstance), CardTableFixture.RuleLibraryInstance, refused);
                    facts.Set(CardLifecycleKeys.FactInvalidUnmountDisposedCode, LastRefusedCode(refused));

                    // (4) reconfigure the same disposed installation.
                    InstallEntry? disposed = CommittedEntryOf(CardTableFixture.RuleLibraryInstance);
                    DefinitionRevision revision = disposed != null
                        ? new DefinitionRevision(disposed.Record.ConfigRevision.Value + 1UL)
                        : DefinitionRevision.First;
                    stateUnchanged &= Refuse(
                        CardLifecyclePayloads.Reconfigure(
                            CardTableFixture.RuleLibraryInstance,
                            revision,
                            disposed != null ? ComposedConfigHashOf(disposed) : ContentHash.Empty,
                            ConfigDocument.Empty),
                        CardTableFixture.RuleLibraryInstance,
                        refused);
                    facts.Set(CardLifecycleKeys.FactInvalidReconfigureDisposedCode, LastRefusedCode(refused));

                    // (5) mount over an installation identity that is still live (P-004).
                    stateUnchanged &= Refuse(
                        CardTablePayloads.Mount(ConsumerDeclaration().Manifest, consumer, CardMarketComposition.MatchScope),
                        consumer,
                        refused);
                    facts.Set(CardLifecycleKeys.FactInvalidRemountLiveIdentityCode, LastRefusedCode(refused));

                    // (6) the teardown edge of a state the diagram gives no path from: `Preparing` has no
                    // `Retiring` successor, so both the request and the path search are refused as values.
                    LifecycleTransition noPath = InstallationStateMachine.Request(
                        InstallationState.Preparing, InstallationState.Retiring);
                    bool noTeardownPath = !InstallationStateMachine.TryTeardownPath(
                        InstallationState.Preparing, out IReadOnlyList<InstallationState>? _);
                    bool pathRefused = !noPath.Allowed && noPath.Code == DiagnosticCode.OwnershipConflict && noTeardownPath;
                    facts.Set(CardLifecycleKeys.FactInvalidTeardownPathRefusedCode, DiagnosticCodeText.Of(noPath.Code));
                    if (pathRefused)
                    {
                        refused.Add(DiagnosticCodeText.Of(noPath.Code));
                    }

                    // The step's own setup suspend (a real, lawful publication) legitimately moved the revision and
                    // how far it moved is not this observation's subject; what it proves is that no refusal moved it
                    // and that the installation it refused against kept its published state.
                    bool revisionHeld = lane.Committed.Revision.Value == revisionBeforeRefusals
                        && StateTextOf(CardTableFixture.RuleLibraryInstance) == InstallationState.Disposed.ToString();

                    facts.Set(CardLifecycleKeys.FactInvalidRejectedCount, refused.Count);
                    facts.Set(CardLifecycleKeys.FactInvalidStateUnchanged, stateUnchanged && revisionHeld);

                    bool pass = refused.Count == 6
                        && stateUnchanged
                        && revisionHeld
                        && pathRefused
                        && AllCodesAreRealCodes(refused);

                    steps.Add(new CardLifecycleStep(name, pass,
                        "rejected=" + refused.Count.ToString(CultureInfo.InvariantCulture)
                        + "; codes=[" + string.Join(",", refused.ToArray()) + "]"
                        + "; stateUnchanged=" + stateUnchanged
                        + "; revision=" + revisionAtStart.ToString(CultureInfo.InvariantCulture)
                        + "->" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            // ---------------------------------------------------------------- 10. repeated operations

            private void RepeatedOperationsObeyLedger()
            {
                const string name = CardLifecycleKeys.StepRepeatedOperations;
                try
                {
                    if (!Ready(out string missing))
                    {
                        steps.Add(new CardLifecycleStep(name, false, missing));
                        return;
                    }

                    // (1) A settled suspend: the quiet provider is still active, so suspending it is legal; a new
                    // operation repeating it is refused and leaves the installation where it was (P-050, P-046).
                    PluginInstanceId quiet = CardTableFixture.QuietScoringInstance;
                    EditOutcome first = PublishEdit(CardLifecyclePayloads.Suspend(quiet), NextOperation());
                    if (!first.Succeeded)
                    {
                        steps.Add(new CardLifecycleStep(name, false, "the first suspend of the quiet provider was refused"));
                        return;
                    }

                    string stateAfterFirst = StateTextOf(quiet);
                    var repeated = new List<string>(2);
                    bool repeatRefused = Refuse(CardLifecyclePayloads.Suspend(quiet), quiet, repeated);
                    facts.Set(CardLifecycleKeys.FactRepeatSuspendRefusedCode, LastRefusedCode(repeated));

                    // (2) The same operation identity and payload returns the original row and repeats nothing.
                    bool sameOutcome = false;
                    string retransmissionKind = "<none>";
                    if (reconfigurePayload != null)
                    {
                        CompositionEditPayload payload = reconfigurePayload;
                        EditAdmission retransmission = lane!.SubmitEdit(payload, reconfigureOperation, lane.Committed.Revision);
                        retransmissionKind = retransmission.Kind.ToString();
                        OperationReadResult read = lane.Read(retransmission.Handle);
                        OperationLedgerEntry? recorded = read.Entry;
                        WorldExecutionReport reported = bridge!.Report(reconfigureOperation);
                        sameOutcome = retransmission.Kind == AdmissionKind.Retransmission
                            && read.Outcome == OperationReadOutcome.Found
                            && recorded != null
                            && string.Equals(recorded.Outcome.ToString(), reconfigureOutcome, StringComparison.Ordinal)
                            && string.Equals(reported.OperationOutcome.ToString(), reconfigureOutcome, StringComparison.Ordinal);
                    }

                    facts.Set(CardLifecycleKeys.FactRepeatRetransmissionKind, retransmissionKind);
                    facts.Set(CardLifecycleKeys.FactRepeatReconfigureSameOutcome, sameOutcome);

                    // (3) A second unmount of the installation step 7 disposed (P-046, P-050).
                    var secondUnmount = new List<string>(1);
                    bool unmountRefused = Refuse(CardTablePayloads.Unmount(provider), provider, secondUnmount);
                    facts.Set(CardLifecycleKeys.FactRepeatUnmountRefusedCode, LastRefusedCode(secondUnmount));

                    facts.Set(CardLifecycleKeys.FactLedgerRowCount, lane!.OperationLedger.RowCount);

                    bool pass = repeatRefused
                        && StateTextOf(quiet) == stateAfterFirst
                        && sameOutcome
                        && unmountRefused
                        && lane.OperationLedger.RowCount == admittedOperations
                        && lane.OperationLedger.RowCount > 0;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "repeatSuspend=" + facts.ValueOf(CardLifecycleKeys.FactRepeatSuspendRefusedCode)
                        + "; quietState=" + stateAfterFirst + "->" + StateTextOf(quiet)
                        + "; retransmission=" + retransmissionKind
                        + "; sameOutcome=" + sameOutcome
                        + "; outcome=" + reconfigureOutcome
                        + "; repeatUnmount=" + facts.ValueOf(CardLifecycleKeys.FactRepeatUnmountRefusedCode)
                        + "; rows=" + lane.OperationLedger.RowCount.ToString(CultureInfo.InvariantCulture)
                        + "/" + admittedOperations.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            // ---------------------------------------------------------------- 11. teardown

            private void TeardownSettlesAndDisposes()
            {
                const string name = CardLifecycleKeys.StepTeardown;
                if (teardownAttempted)
                {
                    return;
                }

                teardownAttempted = true;
                try
                {
                    if (host == null || time == null)
                    {
                        steps.Add(new CardLifecycleStep(name, false, "no world to tear down"));
                        return;
                    }

                    // The P-036 clause first: a command-driven world with no admitted command commits no step.
                    ulong committed = 0UL;
                    for (int i = 0; i < IdleFrames; i++)
                    {
                        committed += time.PumpFrame(HostTicks).StepsCommitted;
                    }

                    facts.Set(CardLifecycleKeys.FactIdleSteps, (long)committed);

                    bool unmounted = true;
                    string unmountFailure = string.Empty;
                    if (lane != null)
                    {
                        var live = new List<PluginInstanceId>();
                        IReadOnlyList<InstallEntry> installs = lane.Committed.Installs;
                        for (int i = 0; i < installs.Count; i++)
                        {
                            if (installs[i].State != InstallationState.Disposed)
                            {
                                live.Add(installs[i].Instance);
                            }
                        }

                        for (int i = 0; i < live.Count; i++)
                        {
                            EditOutcome outcome = PublishEdit(CardTablePayloads.Unmount(live[i]), NextOperation());
                            if (!outcome.Succeeded)
                            {
                                unmounted = false;
                                unmountFailure = outcome.Failure;
                                break;
                            }
                        }
                    }

                    time.Clear(out int _, out int _);
                    CardTableModule.DetachAll();
                    OperationResult stop = host.Stop(NextOperation(host.World), "gc-014 card lifecycle teardown");
                    UnityWorldHost stopped = host;
                    stopped.Dispose();

                    long outstanding = stopped.Ledger.OutstandingJobCount;
                    long retained = stopped.Ledger.RetainedResourceCount;
                    int registryAfter = UnityWorldRegistry.Count;

                    facts.Set(CardLifecycleKeys.FactOutstandingJobsAfterTeardown, outstanding);
                    facts.Set(CardLifecycleKeys.FactRetainedResourcesAfterTeardown, retained);
                    facts.Set(CardLifecycleKeys.FactRegistryAfterTeardown, registryAfter);

                    bool pass = committed == 0UL
                        && host.PendingDemand == 0UL
                        && unmounted
                        && (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        && outstanding == 0L
                        && retained == 0L
                        && registryAfter == registryBeforeCreate;

                    steps.Add(new CardLifecycleStep(name, pass,
                        "idleFrames=" + IdleFrames.ToString(CultureInfo.InvariantCulture)
                        + "; idleSteps=" + committed.ToString(CultureInfo.InvariantCulture)
                        + "; unmounted=" + unmounted
                        + (unmountFailure.Length != 0 ? " (" + unmountFailure + ")" : string.Empty)
                        + "; stop=" + stop.Outcome + "(" + DiagnosticCodeText.Of(stop.Code) + ")"
                        + "; outstandingAfter=" + outstanding.ToString(CultureInfo.InvariantCulture)
                        + "; retainedAfter=" + retained.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardLifecycleStep(name, false, Describe(exception)));
                }
            }

            // ---------------------------------------------------------------- 12. the fact digest

            private void FactsStep()
            {
                steps.Add(new CardLifecycleStep(CardLifecycleKeys.StepFacts, true, facts.Describe()));
            }

            // ---------------------------------------------------------------- the shared edit path

            /// <summary>
            /// Applies one composition edit and publishes the world's assembly for that same publication. P-006 has
            /// one publication series, so the two halves always happen together; a `NoTargetChange` derivation is a
            /// valid answer for the world's half (the publication still stands).
            /// </summary>
            private EditOutcome PublishEdit(CompositionEditPayload payload, OperationId operation)
            {
                var outcome = new EditOutcome { Operation = operation };
                if (lane == null || pipeline == null || publisher == null || host == null)
                {
                    outcome.Failure = "the world or its pipeline is missing";
                    return outcome;
                }

                EditAdmission admission = Submit(payload, operation);
                outcome.Admission = admission;
                if (!admission.Staged)
                {
                    outcome.Failure = "the lane refused the " + payload.Subject + " edit: " + admission.Kind
                        + "/" + DiagnosticCodeText.Of(admission.Code);
                    return outcome;
                }

                outcome.Published = lane.Drain();
                PublishedOperation? published = FindPublished(outcome.Published, operation);
                if (published == null || published.Outcome == Outcome.Rejected)
                {
                    outcome.Failure = "the publication boundary refused the " + payload.Subject + " edit";
                    return outcome;
                }

                outcome.Lifecycle = published.Lifecycle;
                outcome.Outcome = published.Outcome;

                if (!PublishDerived(operation, out string derivedFailure))
                {
                    outcome.Failure = derivedFailure;
                    return outcome;
                }

                if (!AssemblyPublisher.MatchesPublishedAssembly(
                        lane.Committed.Revision, lane.Committed.Epoch, publisher.PublishedRevision, host.CurrentEpoch))
                {
                    outcome.Failure = "the lane and the world assembly counters are not joined after a "
                        + payload.Subject + " edit";
                    return outcome;
                }

                return outcome;
            }

            /// <summary>The world's half of one publication: the derived assembly, or the unchanged-join answer.</summary>
            private bool PublishDerived(OperationId operation, out string failure)
            {
                failure = "the world refused the derived assembly";
                if (pipeline == null || publisher == null || lane == null)
                {
                    failure = "the world or its pipeline is missing";
                    return false;
                }

                DerivedAssemblyReport report = pipeline.PublishDerived(operation);
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    failure = "the world refused the derived assembly: " + report.Describe();
                    return false;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                        operation, lane.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        failure = "the unchanged assembly publication was refused: " + unchanged.Detail;
                        return false;
                    }
                }

                failure = string.Empty;
                return true;
            }

            /// <summary>Applies a sequence of edits, each with its own publication, in admission order.</summary>
            private bool PublishEdits(IReadOnlyList<CompositionEditPayload> payloads, out string failure)
            {
                failure = string.Empty;
                for (int i = 0; i < payloads.Count; i++)
                {
                    EditOutcome outcome = PublishEdit(payloads[i], NextOperation());
                    if (!outcome.Succeeded)
                    {
                        failure = outcome.Failure;
                        return false;
                    }
                }

                return true;
            }

            /// <summary>Submits one lifecycle edit through the controller, which owns the three-seam sequence.</summary>
            private LifecycleRequestReport SubmitLifecycle(CompositionEditPayload payload, OperationId operation)
            {
                admittedOperations++;
                return controller!.Submit(payload, operation);
            }

            /// <summary>Admits one edit on the lane, counting the ledger row it creates (P-050).</summary>
            private EditAdmission Submit(CompositionEditPayload payload, OperationId operation)
            {
                admittedOperations++;
                return lane!.SubmitEdit(payload, operation, lane.Committed.Revision);
            }

            /// <summary>
            /// Submits one edit that must be refused and records its refusal code. The refusal is a value: the
            /// admission is not staged, its code is not `None`, the installation keeps its published state and the
            /// lane publishes no revision (P-046, P-051).
            /// </summary>
            private bool Refuse(CompositionEditPayload payload, PluginInstanceId subject, List<string> refused)
            {
                string stateBefore = StateTextOf(subject);
                ulong revisionBefore = lane!.Committed.Revision.Value;
                EditAdmission admission = Submit(payload, NextOperation());

                bool refusedAsValue = !admission.Staged
                    && admission.Code != DiagnosticCode.None
                    && admission.Kind == AdmissionKind.Fresh;
                if (refusedAsValue)
                {
                    refused.Add(DiagnosticCodeText.Of(admission.Code));
                }

                return refusedAsValue
                    && lane.Committed.Revision.Value == revisionBefore
                    && string.Equals(StateTextOf(subject), stateBefore, StringComparison.Ordinal);
            }

            private static string LastRefusedCode(List<string> refused)
            {
                return refused.Count != 0 ? refused[refused.Count - 1] : "<none>";
            }

            private static bool AllCodesAreRealCodes(List<string> refused)
            {
                string none = DiagnosticCodeText.Of(DiagnosticCode.None);
                for (int i = 0; i < refused.Count; i++)
                {
                    if (string.Equals(refused[i], none, StringComparison.Ordinal) || refused[i] == "<none>")
                    {
                        return false;
                    }
                }

                return true;
            }

            // ---------------------------------------------------------------- the world's facts

            private bool Ready(out string missing)
            {
                if (host == null || lane == null || publisher == null || pipeline == null || controller == null || module == null)
                {
                    missing = "an earlier observation left the world incomplete";
                    return false;
                }

                missing = string.Empty;
                return true;
            }

            private WorldId NextSession() => new WorldId(sessionSequence.Next());

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, CardTableFixture.Issuer, operationSequence);
            }

            private OperationId NextOperation() => NextOperation(host!.World);

            private Id128 NextLease() => leaseSequence.Next();

            /// <summary>Rows of the published assembly attributed to one installation (P-017, P-033).</summary>
            private long AttributedRowsOf(PluginInstanceId instance)
            {
                return controller != null ? controller.Binding.CountAttributedRows(instance) : -1L;
            }

            private string StateTextOf(PluginInstanceId instance)
            {
                InstallSnapshot? snapshot = lane != null ? lane.FindInstall(instance) : null;
                return snapshot != null ? snapshot.State.ToString() : "<missing>";
            }

            /// <summary>The activation ledger's own state, which an explicit unload moves without a composition edit.</summary>
            private string LedgerStateTextOf(PluginInstanceId instance)
            {
                return controller != null && controller.Lifecycle.Activations.TryGetCurrent(instance, out ActivationAttempt? attempt)
                    && attempt != null
                    ? attempt.State.ToString()
                    : "<missing>";
            }

            private long BindingsOf(PluginInstanceId instance)
            {
                InstallSnapshot? snapshot = lane != null ? lane.FindInstall(instance) : null;
                return snapshot != null ? snapshot.Bindings.Count : -1L;
            }

            private InstallEntry? CommittedEntryOf(PluginInstanceId instance)
            {
                return lane != null && lane.Committed.TryGetInstall(instance, out InstallEntry? entry) ? entry : null;
            }

            private long CommittedGenerationOf(PluginInstanceId instance)
            {
                InstallEntry? entry = CommittedEntryOf(instance);
                return entry != null ? (long)entry.Record.Generation.Value : 0L;
            }

            private long CommittedEpochOf(PluginInstanceId instance)
            {
                InstallEntry? entry = CommittedEntryOf(instance);
                return entry != null ? (long)entry.Record.ActivationEpoch.Value : 0L;
            }

            private int SeatScoreOf(uint ordinal)
            {
                if (host == null || module == null || !module.TrySeat(ordinal, out Entity seat))
                {
                    return int.MinValue;
                }

                return host.EntityWorld.EntityManager.GetComponentData<CardSeatState>(seat).Score;
            }

            /// <summary>The table's live version; the seeded version while no settlement has committed (P-044).</summary>
            private uint TableVersionOf()
            {
                if (host == null || module == null)
                {
                    return 0U;
                }

                return CardTableAccess.ReadTable(host.EntityWorld.EntityManager, module.TableEntity).TableVersion;
            }

            private static PublishedOperation? FindPublished(IReadOnlyList<PublishedOperation> published, OperationId operation)
            {
                for (int i = 0; i < published.Count; i++)
                {
                    if (published[i].Operation.Equals(operation))
                    {
                        return published[i];
                    }
                }

                return null;
            }

            private static ContributionRetraction? RetractionOf(LifecycleCommitReport? report, PluginInstanceId instance)
            {
                if (report == null)
                {
                    return null;
                }

                for (int i = 0; i < report.Retractions.Count; i++)
                {
                    if (report.Retractions[i].Instance.Equals(instance))
                    {
                        return report.Retractions[i];
                    }
                }

                return null;
            }

            /// <summary>
            /// The P-048 pass one publication ran for an installation, optionally narrowed to the activation epoch
            /// it acted on: a replacement's displaced activation tears down under the *old* epoch (P-005, P-048).
            /// </summary>
            private static TeardownReport? TeardownOf(LifecycleCommitReport? report, PluginInstanceId instance, ulong epoch)
            {
                if (report == null)
                {
                    return null;
                }

                for (int i = 0; i < report.Teardowns.Count; i++)
                {
                    TeardownReport teardown = report.Teardowns[i];
                    if (teardown.Instance.Equals(instance) && teardown.Stamp.ActivationEpoch.Value == epoch)
                    {
                        return teardown;
                    }
                }

                return null;
            }

            /// <summary>The next tracked job identity of this run (P-041).</summary>
            private ulong jobSequence;

            /// <summary>The lease step 7 staged for the provider, retired by the unload publication (P-048).</summary>
            private Id128 providerLease;

            /// <summary>
            /// Stages one real managed lease for the provider on one benign publication, so the unload step has a
            /// lease of that instance to retire in reverse acquisition order (P-048). The edit is a scratch scope
            /// creation: a real composition change that touches no installation's state and no target's assembly.
            /// </summary>
            private bool StageProviderLease()
            {
                if (lane == null)
                {
                    return false;
                }

                OperationId operation = NextOperation();
                EditAdmission admission = Submit(
                    CardTablePayloads.ScopeCreate(scratch, CardMarketComposition.MatchScope, false),
                    operation);
                if (!admission.Staged)
                {
                    return false;
                }

                providerLease = NextLease();
                if (!lane.StageResource(
                        operation,
                        provider,
                        new ResourceKey(providerLease),
                        CardTableDeclarations.WriteInt32(0),
                        null,
                        out DiagnosticCode _))
                {
                    return false;
                }

                if (!PublishEditAfterStage(operation))
                {
                    return false;
                }

                return lane.Resources.RetainedCountFor(provider) >= 1;
            }

            /// <summary>Publishes the already-admitted operation a staged lease belongs to.</summary>
            private bool PublishEditAfterStage(OperationId operation)
            {
                if (lane == null)
                {
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                PublishedOperation? result = FindPublished(published, operation);
                return result != null && result.Outcome != Outcome.Rejected && PublishDerived(operation, out string _);
            }

            /// <summary>The outcome of one edit across the three seams, with the failure it stopped at.</summary>
            private sealed class EditOutcome
            {
                public OperationId Operation { get; set; }

                public EditAdmission? Admission { get; set; }

                public IReadOnlyList<PublishedOperation> Published { get; set; } = Array.Empty<PublishedOperation>();

                public LifecycleCommitReport? Lifecycle { get; set; }

                public Outcome Outcome { get; set; }

                public string Failure { get; set; } = string.Empty;

                public bool Succeeded => Failure.Length == 0;
            }
        }

        /// <summary>
        /// The managed-resource factory of one lifecycle run: deterministic lease ids, counted preparations and
        /// recorded disposals, so "retire in reverse acquisition order, dispose each lease at most once" is an
        /// observable fact rather than a claim (P-048).
        /// </summary>
        private sealed class LifecycleResourceFactory : IManagedResourceFactory
        {
            private readonly IdSequence leases;
            private readonly FactoryKey disposer;

            public LifecycleResourceFactory(IdSequence leases, FactoryKey disposer)
            {
                this.leases = leases ?? throw new ArgumentNullException(nameof(leases));
                this.disposer = disposer;
            }

            /// <summary>Leases prepared by this factory.</summary>
            public int PrepareCount { get; private set; }

            /// <summary>Leases whose disposal ran to completion.</summary>
            public int DisposeCount { get; private set; }

            /// <summary>Disposed lease ids in disposal order; the reverse-acquisition-order evidence (P-048).</summary>
            public List<Id128> DisposedOrder { get; } = new List<Id128>();

            /// <inheritdoc />
            public IManagedResourceLease Prepare(ManagedResourceRequest request)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                PrepareCount++;
                Id128 leaseId = leases.Next();
                return new ManagedResourceLease(
                    request.Resource,
                    leaseId,
                    request.Token,
                    disposer,
                    new ManagedResourceGate(),
                    OnDisposed);
            }

            private void OnDisposed(Id128 leaseId)
            {
                DisposedOrder.Add(leaseId);
                DisposeCount++;
            }
        }
    }
}
