// GameCore.Validation.ProbeHost — the GC-017 fault scenario (inject a failure at every apply and cancellation
// boundary).
//
// TEST-016 rows this file implements, from `docs/game-core/08-validation-and-performance.md` — the fault matrix
// every row of which needs *one deterministic injection point*, armed by identity and reached in the real code
// path, never a probability:
//
//   row 1  validation / enumeration refusal                    gc017-validation-fault-rejects-and-keeps-the-old-assembly
//   row 2  resource acquisition or plan preparation            gc017-acquisition-fault-releases-staged-leases
//   row 3  cancellation before and after the cutoff            gc017-cancellation-before-the-cutoff-releases-staged-work
//                                                              gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication
//   row 4  the publication fence settled before the swap       gc017-fence-fault-settles-handles-and-keeps-the-old-assembly
//   row 5  migration before, and failure after, the live write gc017-prewrite-migration-fault-preserves-live-state
//                                                              gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image
//                                                              gc017-gate-installation-fault-stops-after-live-writes
//   row 6  structural playback before the step commit          gc017-structural-playback-fault-stops-the-step-commit
//   row 8  cleanup of a refused operation's staged work        gc017-cleanup-fault-retains-staged-ownership
//                                                              gc017-cleanup-boundary-releases-what-a-refusal-staged
//   row 10 recovery from initial definitions                   gc017-recovery-from-initial-definitions-into-a-new-world
//                                                              gc017-old-callback-after-recovery-is-rejected
//
// Normative anchors: P-002 (the host is the sole world authority; one world, one assembly publisher, one lane),
// P-029 (a preparation or migration failure releases staged leases in reverse dependency order and leaves the old
// assembly intact), P-030 (publication switches the assembly at one serialized commit), P-031 (a failure after the
// first live write faults the world: admission stays closed, no epoch or image publishes, no simulation resumes),
// P-035 (a created world is `Running` only after its initial validated publication), P-047/P-048 (in-flight work is
// fenced before storage is touched and a resource unfinished work may still reach is quarantined and reported,
// never freed on a timeout), P-049 (recovery creates a new `WorldId` from verified initial definitions; old
// callbacks and handles never become valid), P-051 (the serialized cutoff decides a cancellation/publication race),
// P-052 (a failure names its phase and its operation).
//
// Four structural decisions, all forced by the committed kernel rather than chosen here:
//
//   * **One real world per run, and a second, third and fourth only where the fault is terminal.** A postwrite
//     fault (P-031) closes a world forever, so the three postwrite observations each stand up their own real world
//     built by the same chain; a prewrite refusal (P-029) leaves the world running, so every prewrite observation
//     and both cancellation observations run over the first world in the frozen step order.
//   * **The prewrite observations ride one composition publication.** A prewrite refusal is reached *after* the
//     publisher has adopted the composition publication of the current assembly, and `TryAdoptLanePublication`
//     refuses a pair that is already adopted and waiting (P-006: one number is used by one assembly). So the run
//     adopts the family's real edit once, and every armed prewrite attempt publishes a plan for that same adopted
//     pair — which is exactly what "the same edit is refused while a boundary is armed and publishes when it is
//     disarmed" means. The one publication that succeeds consumes the pair and the series stays joined.
//   * **Real staged leases exist only where a caller stages them.** The derived chain's plan resource set acquires
//     nothing (the planner only accounts for what it is given), so the two observations whose whole point is what
//     happens to *staged work* — row 2's release and row 8's retain/release pair — drive a plan the runner builds
//     through the real `AssemblyPlanner`, with real leases acquired through the world's own
//     `StagedResourceGate`, exactly as `W4GateScenario.PublishPolicyCase` drives a policy publication.
//   * **Nothing here re-implements a kernel module and nothing is discovered reflectively.** The chain of §5 is
//     copied verbatim; F-*/P-* facts are asserted as values read from the modules (`AssemblyPublicationReport`,
//     `RecoveryReport`, `FaultTrace`, the world's own ledgers), never as booleans this file invented.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Recovery;
using GameCore.Unity.Runtime.Time;
using PlanningCompositionProposal = GameCore.Planning.CompositionProposal;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Runs the GC-017 fault sequence over one family and one catalog.</summary>
    public static class FaultScenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family scenarios use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>
        /// The scenario's observations, in execution order, without the family qualification. Both families record
        /// exactly these names, in this order, so a renamed, reordered, added or dropped observation fails the
        /// EditMode suite and the player probe instead of shrinking them silently (P-008).
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "gc017-initial-world-lane-and-observer",
            "gc017-validation-fault-rejects-and-keeps-the-old-assembly",
            "gc017-acquisition-fault-releases-staged-leases",
            "gc017-cancellation-before-the-cutoff-releases-staged-work",
            "gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication",
            "gc017-prewrite-migration-fault-preserves-live-state",
            "gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image",
            "gc017-structural-playback-fault-stops-the-step-commit",
            "gc017-gate-installation-fault-stops-after-live-writes",
            "gc017-fence-fault-settles-handles-and-keeps-the-old-assembly",
            "gc017-cleanup-fault-retains-staged-ownership",
            "gc017-cleanup-boundary-releases-what-a-refusal-staged",
            "gc017-recovery-from-initial-definitions-into-a-new-world",
            "gc017-old-callback-after-recovery-is-rejected",
            "gc017-teardown-settles-and-disposes",
        };

        /// <summary>The observation names one family's run records: <c>&lt;label&gt;/&lt;name&gt;</c>.</summary>
        public static string[] QualifiedNames(string label)
        {
            var names = new string[ObservationNames.Length];
            for (int i = 0; i < ObservationNames.Length; i++)
            {
                names[i] = label + "/" + ObservationNames[i];
            }

            return names;
        }

        /// <summary>Runs the whole fault sequence for one family and one catalog.</summary>
        public static FaultScenarioResult Run(IW4GateFamily family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family).Run();
        }

        private sealed class Executor
        {
            /// <summary>Staged lease ceiling of a plan resource gate, in bytes (P-022).</summary>
            private const ulong StagedByteCeiling = 1024UL * 1024UL;

            private const ulong ScratchCapacityBytes = 4096UL;

            private const ulong ScratchBytesPerSlot = 64UL;

            private const ulong PrepareBytesLimit = 1024UL * 1024UL;

            /// <summary>Frames a command-driven world is pumped while it must perform no simulation step (P-036).</summary>
            private const ulong IdlePumpTicks = 1000000UL;

            private const int IdlePumpFrames = 4;

            /// <summary>Leases row 2's plan stages, so "the staged work was released" is a count, not a claim.</summary>
            private const int AcquisitionLeaseCount = 2;

            /// <summary>Leases row 8's plans stage; one lease keeps "retained" and "released" unambiguously equal to 1.</summary>
            private const int CleanupLeaseCount = 1;

            /// <summary>Namespace word of the runner-staged plan resources, so no fixture key is reused.</summary>
            private const ulong PlanResourceNamespace = 0x4743303137524553UL;

            private readonly IW4GateFamily family;
            private readonly List<FaultScenarioStep> steps = new List<FaultScenarioStep>();
            private readonly IdSequence sessionSequence;
            private readonly List<WorldChain> worlds = new List<WorldChain>();

            private ulong operationSequence;
            private int registryBeforeCreate = -1;
            private string lastFailure = string.Empty;

            /// <summary>The first world: every prewrite and both cancellation observations run over it.</summary>
            private WorldChain? primary;

            /// <summary>The world row 5's postwrite fault leaves behind; row 10 recovers from it.</summary>
            private WorldChain? postwriteWorld;


            /// <summary>The recovered world of row 10, and the lane that owns its callback gate (P-049).</summary>
            private WorldChain? recovered;
            private CompositionHost? recoveredLane;

            /// <summary>The pending read-only observer of row 1, re-checked by every prewrite observation (P-030).</summary>
            private PublishedWorldView? observerView;

            /// <summary>The operation row 3 cancelled before the cutoff, re-cancelled by row 4's third control.</summary>
            private OperationId cancelledBeforeCutoff;


            /// <summary>Callback a run-scoped resource factory counts its disposals through (P-048).</summary>
            private RunResources? resources;

            /// <summary>The family's validated compile, shared by every world of this run (GC-007, GC-009).</summary>
            private PipelineDescriptorReport? compiled;

            public Executor(IW4GateFamily family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt);
            }

            public FaultScenarioResult Run()
            {
                // Every fault test begins by asserting the latch implementation is present: without
                // `GAMECORE_FAULT_INJECTION` no boundary could fire and every later step would pass vacuously.
                CreateTheWorldAndTheObserver();
                RefuseTheAssemblyAtTheValidationBoundary();
                RefuseTheAssemblyAtTheAcquisitionBoundary();
                CancelBeforeTheCutoff();
                CancelAfterTheCutoff();
                RefuseTheAssemblyAtTheMigrationBoundary();
                FaultTheWorldAfterItsFirstLiveWrite();
                FaultTheStepAfterItsStructuralPlayback();
                FaultTheWorldWhenItInstallsTheNewSchedule();
                RefuseTheAssemblyAtTheFenceBoundary();
                RetainTheStagedOwnershipOfARefusedCleanup();
                ReleaseWhatARefusalStaged();
                RecoverFromTheFaultedWorld();
                DiscardTheOldCallbackAtTheRecoveredWorld();
                TearDownEveryWorld();

                return new FaultScenarioResult(family.Label, steps);
            }

            // ================================================================== 1. the world, the lane and the observer

            /// <summary>
            /// The real chain of §5, over the family's committed catalog: one world created through the registry, the
            /// family's composite root registration, real ECS target storage seeded through the world's own seeder,
            /// the control lane opened at the world definition's declared tree and joined to the world's published
            /// assembly, the four derivation values registered once, and the assembly publisher that shares the
            /// world's fault latch. A read-only observer is taken here and re-checked by every later step (P-030).
            /// </summary>
            private void CreateTheWorldAndTheObserver()
            {
                const string name = "gc017-initial-world-lane-and-observer";
                try
                {
                    PipelineDescriptorReport descriptorReport = family.CompilePipeline();
                    if (!descriptorReport.Succeeded
                        || descriptorReport.Descriptor == null
                        || descriptorReport.Adaptation == null)
                    {
                        Add(name, false, "the ownership and schedule pipeline refused: "
                            + descriptorReport.Describe());
                        return;
                    }

                    registryBeforeCreate = UnityWorldRegistry.Count;
                    resources = new RunResources(new IdSequence(family.SessionSalt ^ 0x4730313752455346UL));
                    primary = StandUpWorld("primary", NextOperation(default(WorldId)), descriptorReport);
                    if (primary == null)
                    {
                        Add(name, false, "the world or its lane could not be stood up: " + lastFailure);
                        return;
                    }

                    bool faultsCompiledIn = primary.Host.Faults.IsCompiledIn;
                    ulong idleSteps = PumpIdleFrames(primary);
                    bool joined = MatchesPublishedAssembly(primary);

                    PublishedWorldView captured = primary.Publisher.Published;
                    observerView = captured;
                    bool observerConsistent = ObserverConsistent(primary, captured, out string observerText);

                    bool registered = true;
                    for (int i = 0; i < family.AutomaticTargets.Count; i++)
                    {
                        registered &= primary.Targets.Contains(family.AutomaticTargets[i]);
                    }

                    registered &= primary.Targets.Contains(family.MovedTarget)
                        && primary.Targets.Contains(family.IsolatedTarget)
                        && primary.Targets.Contains(family.IneligibleTarget);

                    bool pass = faultsCompiledIn
                        && primary.Host.Lifecycle == WorldLifecycleState.Running
                        && primary.Host.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && primary.Host.CurrentStep.Equals(LogicalStepId.Zero)
                        && primary.Targets.Count > 0
                        && registered
                        && primary.Lane.Committed.Mode == PropagationMode.Automatic
                        && idleSteps == 0UL
                        && joined
                        && observerConsistent
                        && captured.BindingRowCount == 0;

                    Add(name, pass,
                        "session=" + primary.World.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + primary.Targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; scopes=" + primary.Lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; idleFrames=" + IdlePumpFrames.ToString(CultureInfo.InvariantCulture)
                        + "; idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + primary.Host.Lifecycle
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + primary.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneEpoch=" + primary.Lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + joined
                        + "; observerPending=True"
                        + "; observerConsistent=" + observerConsistent
                        + "; " + observerText
                        + "; compiledIn=" + faultsCompiledIn
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 2. row 1: the validation boundary

            /// <summary>
            /// Row 1. `FaultBoundary.Validation` is reached in `AssemblyPublisher.Publish` after the prepared check
            /// and before the revision recheck, so an injected fault is a prewrite refusal: the plan's staged leases
            /// are released, nothing is written, and the previously published epoch, its state and its bindings all
            /// remain active (P-028, P-029).
            ///
            /// The trace's provenance is the refused plan's own identity: the operation of the derivation that
            /// prepared it and the plan hash. It is deliberately not the composition edit's operation — P-052 names
            /// the operation and the plan of the thing that failed, and the thing that failed here is the plan the
            /// publisher refused, not the edit the lane already published.
            /// </summary>
            private void RefuseTheAssemblyAtTheValidationBoundary()
            {
                const string name = "gc017-validation-fault-rejects-and-keeps-the-old-assembly";
                try
                {
                    if (primary == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    AssemblyEpoch epochBefore = primary.Host.CurrentEpoch;
                    CompositionRevision revisionBefore = primary.Publisher.PublishedRevision;
                    int rowsBefore = primary.Publisher.Published.BindingRowCount;
                    PublishedWorldView viewBefore = primary.Publisher.Published;

                    OperationId edit = NextOperation(primary.World);
                    bool staged = SubmitAndDrain(primary, family.MountProvider(), "mount-provider", edit, out string stageFailure);
                    if (!staged)
                    {
                        Add(name, false, stageFailure);
                        return;
                    }

                    AssemblyFaultInjection faults = primary.Host.Faults;
                    faults.Arm(FaultBoundary.Validation);
                    OperationId derivation = NextOperation(primary.World);
                    DerivedAssemblyReport derived = primary.Pipeline.PublishDerived(derivation);
                    AssemblyPublicationReport? publication = derived.Publication;
                    // This is the pair's first derivation, so it is the one that builds the proposal the
                    // acquisition step then publishes without re-deriving (P-006).
                    NotePendingProposal(primary, derived);

                    bool armedAtReach = faults.IsArmed(FaultBoundary.Validation);
                    faults.Disarm(FaultBoundary.Validation);
                    bool disarmed = !faults.IsArmed(FaultBoundary.Validation);

                    bool refused = derived.Outcome == DerivedAssemblyOutcome.Refused
                        && publication != null
                        && publication.Outcome == Outcome.Rejected
                        && publication.Code == DiagnosticCode.ResourceUnavailable
                        && publication.StructuralWrites == 0
                        && !publication.CrossedLiveWriteBoundary
                        && publication.PublishedToken == null;

                    // The observer's pre-fault view is still the published one, byte for byte: same reference, same
                    // epoch, same binding rows (P-030's one switched pointer).
                    // The observer check is invoked unconditionally: an out-variable declared inside a `&&` chain
                    // is only definitely assigned when the chain reached that operand, so reading it afterwards
                    // would be a compile error (and would silently skip the check on a short-circuit path).
                    bool observerKept = MatchesObserver(primary, out string observerText);
                    bool oldAssemblyKept = primary.Publisher.Published.Epoch.Equals(epochBefore)
                        && primary.Publisher.PublishedRevision.Equals(revisionBefore)
                        && primary.Publisher.Published.BindingRowCount == rowsBefore
                        && ReferenceEquals(viewBefore, primary.Publisher.Published)
                        && observerKept;

                    IReadOnlyList<FaultRecord> records = faults.Trace.Of(FaultBoundary.Validation);
                    bool traced = records.Count == 1
                        && records[0].Injected
                        && records[0].Operation.Equals(derivation)
                        && !records[0].Operation.Equals(edit)
                        && !records[0].PlanHash.Equals(ContentHash.Empty);

                    bool pass = armedAtReach
                        && disarmed
                        && refused
                        && oldAssemblyKept
                        && traced
                        && primary.Host.Lifecycle == WorldLifecycleState.Running;

                    Add(name, pass,
                        "edit=mount-provider"
                        + "; armed=True"
                        + "; armedAtReach=" + armedAtReach
                        + "; disarmed=True"
                        + "; derived=" + derived.Outcome + "(" + derived.Code + ")"
                        + "; outcome=" + DescribePublication(publication)
                        + "; structuralWrites=" + (publication != null ? publication.StructuralWrites : -1).ToString(CultureInfo.InvariantCulture)
                        + "; crossedLiveWriteBoundary=" + (publication != null && publication.CrossedLiveWriteBoundary)
                        + "; publishedToken=" + (publication == null || publication.PublishedToken == null ? "<null>" : "set")
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; epochBefore=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; revision=" + primary.Publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + primary.Publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; rowsBefore=" + rowsBefore.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + primary.Host.Lifecycle
                        + "; boundaryReaches=" + faults.ReachCountOf(FaultBoundary.Validation).ToString(CultureInfo.InvariantCulture)
                        + "; injected=" + faults.Trace.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + TraceLineOf(records)
                        + "; " + observerText
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 3. row 2: the acquisition boundary

            /// <summary>
            /// Row 2. The same real edit is published again, now with the acquisition boundary armed on the world's
            /// latch — the instance `StagedResourceGate` is constructed with, so the publisher's refusal and the
            /// gate's refusal value are the same latch. The plan really stages leases through the world's own gate,
            /// the refusal releases every one of them in reverse acquisition order, and the old assembly stays
            /// published (P-029). Disarming and re-publishing the *same* composition publication then succeeds,
            /// which is the falsifying counterpart: without the fault this publication commits (P-006).
            /// </summary>
            private void RefuseTheAssemblyAtTheAcquisitionBoundary()
            {
                const string name = "gc017-acquisition-fault-releases-staged-leases";
                try
                {
                    if (primary == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    AssemblyEpoch epochBefore = primary.Host.CurrentEpoch;
                    int rowsBefore = primary.Publisher.Published.BindingRowCount;
                    AssemblyFaultInjection faults = primary.Host.Faults;
                    bool adoptedBefore = primary.Publisher.HasAdoptedPublication;
                    PlannedPublication? refusedPlan = BuildThePendingPlan(
                        primary, NextOperation(primary.World), AcquisitionLeaseCount, out PlanStaging refusedStaging);
                    if (refusedPlan == null)
                    {
                        Add(name, false, "the plan for the pending composition publication was not built: " + lastFailure);
                        return;
                    }

                    // The leases are staged while the latch is clear, so "the staged work was released" is about
                    // work that really existed when the refusal ran (P-029).
                    faults.Arm(FaultBoundary.Acquisition);
                    bool armedAtReach = faults.IsArmed(FaultBoundary.Acquisition);
                    AssemblyPublicationReport refused = primary.Publisher.Publish(refusedPlan);
                    faults.Disarm(FaultBoundary.Acquisition);
                    bool disarmed = !faults.IsArmed(FaultBoundary.Acquisition);
                    bool released = refusedStaging.Acquisitions != null
                        && refusedStaging.LeaseIds.Count == AcquisitionLeaseCount
                        && refusedStaging.Gate != null
                        && refusedStaging.Gate.AcquiredCount == AcquisitionLeaseCount
                        && refusedStaging.Gate.ReleasedCount == AcquisitionLeaseCount
                        && refusedStaging.Gate.LiveLeaseCount == 0
                        && refusedStaging.Acquisitions.RetainedLeaseIds().Count == 0
                        && !refusedStaging.Acquisitions.CanEmitGameplay;

                    bool refusalHeld = refused.Outcome == Outcome.Rejected
                        && refused.Code == DiagnosticCode.ResourceUnavailable
                        && refused.StructuralWrites == 0
                        && !refused.CrossedLiveWriteBoundary;

                    int rowsAfterRefusal = primary.Publisher.Published.BindingRowCount;
                    bool oldAssemblyIntact = primary.Publisher.Published.Epoch.Equals(epochBefore)
                        && rowsAfterRefusal == rowsBefore
                        && primary.Host.CurrentEpoch.Equals(epochBefore);

                    // The staged work was released and the same edit still publishes: the world's next assembly is
                    // the very composition publication the refusal left adopted and waiting (P-006).
                    PlannedPublication? committedPlan = BuildThePendingPlan(
                        primary, NextOperation(primary.World), AcquisitionLeaseCount, out PlanStaging committedStaging);
                    bool published = committedPlan != null;
                    AssemblyPublicationReport? committed = published ? primary.Publisher.Publish(committedPlan!) : null;
                    bool committedTheSameEdit = published
                        && committed != null
                        && committed.Published
                        && committed.StructuralWrites > 0
                        && committed.PublishedToken != null
                        && primary.Host.CurrentEpoch.Value > epochBefore.Value
                        && primary.Host.CurrentEpoch.Equals(primary.Lane.Committed.Epoch)
                        && MatchesPublishedAssembly(primary)
                        && committedStaging.Gate != null
                        && committedStaging.Gate.AcquiredCount == AcquisitionLeaseCount
                        && committedStaging.Gate.LiveLeaseCount == AcquisitionLeaseCount
                        && committedStaging.Acquisitions != null
                        && committedStaging.Acquisitions.RetainedLeaseIds().Count == AcquisitionLeaseCount;

                    bool rowsAdded = primary.Publisher.Published.BindingRowCount > rowsBefore;

                    bool pass = refusedPlan != null
                        && adoptedBefore
                        && armedAtReach
                        && disarmed
                        && released
                        && refusalHeld
                        && oldAssemblyIntact
                        && committedTheSameEdit
                        && rowsAdded;

                    Add(name, pass,
                        "edit=mount-provider@pending-pair"
                        + "; armed=True"
                        + "; armedAtReach=" + armedAtReach
                        + "; disarmed=True"
                        + "; disarmedChecked=" + disarmed
                        + "; adoptedBefore=" + adoptedBefore
                        + "; stagedLeases=" + refusedStaging.LeaseIds.Count.ToString(CultureInfo.InvariantCulture)
                        + "; stagedLeasesReleased=" + released
                        + "; refused=" + DescribePublication(refused)
                        + "; structuralWrites=" + refused.StructuralWrites.ToString(CultureInfo.InvariantCulture)
                        + "; crossedLiveWriteBoundary=" + refused.CrossedLiveWriteBoundary
                        + "; epochAfterRefusal=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rowsBefore=" + rowsBefore.ToString(CultureInfo.InvariantCulture)
                        + "; committed=" + DescribePublication(committed)
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneEpoch=" + primary.Lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rowsAfter=" + primary.Publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + primary.Host.Lifecycle
                        + "; boundaryReaches=" + faults.ReachCountOf(FaultBoundary.Acquisition).ToString(CultureInfo.InvariantCulture)
                        + "; injected=" + faults.Trace.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + TraceLineOf(faults.Trace.Of(FaultBoundary.Acquisition))
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, exception.GetType().FullName + ": " + exception.Message);
                }
            }

            // ================================================================== 4. row 3: cancellation before the cutoff

            /// <summary>
            /// Row 3, first half. An edit is admitted and still staged — the lane has published nothing — so the
            /// cancellation cutoff still reaches it: the request is `Cancelled`, the staged plan is gone, the lane's
            /// drain is empty, no epoch is published and the world's assembly is untouched (P-051). Cancelling is a
            /// ledger decision, not a rollback: the operation's terminal entry says `Cancelled` and the published
            /// world state never moved.
            /// </summary>
            private void CancelBeforeTheCutoff()
            {
                const string name = "gc017-cancellation-before-the-cutoff-releases-staged-work";
                try
                {
                    if (primary == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    AssemblyEpoch epochBefore = primary.Host.CurrentEpoch;
                    CompositionRevision revisionBefore = primary.Lane.Committed.Revision;
                    int rowsBefore = primary.Publisher.Published.BindingRowCount;

                    OperationId operation = NextOperation(primary.World);
                    EditAdmission admission = primary.Lane.SubmitEdit(
                        family.SpareScopeEdits[0], operation, primary.Lane.Committed.Revision);
                    if (!admission.Staged)
                    {
                        Add(name, false, "the lane refused the edit before the cutoff: "
                            + admission.Kind + "/" + admission.Code);
                        return;
                    }

                    bool stagedPlanPresent = primary.Lane.StagedPlan(operation) != null;
                    int decisionsBefore = primary.Lane.OperationLedger.CancelledCount;
                    CancelOutcome outcome = primary.Lane.Cancel(NextOperation(primary.World), operation);
                    int decisionsAfter = primary.Lane.OperationLedger.CancelledCount;
                    bool stagedPlanCleared = primary.Lane.StagedPlan(operation) == null;
                    IReadOnlyList<PublishedOperation> drained = primary.Lane.Drain();
                    OperationResult? result = primary.Lane.ResultOf(operation);

                    cancelledBeforeCutoff = operation;

                    bool pass = stagedPlanPresent
                        && outcome == CancelOutcome.Cancelled
                        && stagedPlanCleared
                        && drained.Count == 0
                        && result != null
                        && result.Outcome == Outcome.Cancelled
                        && result.Code == DiagnosticCode.Cancelled
                        && decisionsAfter == decisionsBefore + 1
                        && primary.Host.CurrentEpoch.Equals(epochBefore)
                        && primary.Lane.Committed.Revision.Equals(revisionBefore)
                        && primary.Publisher.Published.Epoch.Equals(epochBefore)
                        && primary.Publisher.Published.BindingRowCount == rowsBefore
                        && primary.Host.Lifecycle == WorldLifecycleState.Running;

                    Add(name, pass,
                        "edit=spare-scope"
                        + "; stagedPlanPresent=" + stagedPlanPresent
                        + "; cancelOutcome=" + outcome
                        + "; stagedPlanCleared=" + stagedPlanCleared
                        + "; drainEmpty=" + (drained.Count == 0)
                        + "; ledgerOutcome=" + (result != null ? result.Outcome.ToString() : "<none>")
                        + "; ledgerCode=" + (result != null ? result.Code.ToString() : "<none>")
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneRevision=" + primary.Lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + primary.Publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; cancelDecisions=" + decisionsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + decisionsAfter.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Row 3, second half. The same shape after the publication boundary: `Drain` crosses the serialized
            /// cutoff, so a later cancellation request is `TooLate` and the target's terminal result stands — it is
            /// never restated as a rollback, the staged leases the publication opened stay live, and the world's
            /// epoch is still the pre-assembly one until the composition publication is derived (P-051, P-029).
            /// Two controls ride the same lane: an unknown target is `Unknown`, and cancelling an already-cancelled
            /// target returns `Cancelled` again without a second cutoff decision (P-050).
            /// </summary>
            private void CancelAfterTheCutoff()
            {
                const string name = "gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication";
                try
                {
                    if (primary == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    AssemblyEpoch epochBefore = primary.Host.CurrentEpoch;

                    OperationId operation = NextOperation(primary.World);
                    EditAdmission admission = primary.Lane.SubmitEdit(
                        family.MountInstall(family.UnloadManifest, family.UnloadInstall, family.UnloadScope),
                        operation,
                        primary.Lane.Committed.Revision);
                    if (!admission.Staged)
                    {
                        Add(name, false, "the lane refused the unload mount: " + admission.Kind + "/" + admission.Code);
                        return;
                    }

                    // A real staged lease of the lane, so "the staged leases stay live" is a value rather than an
                    // intention: it is prepared behind a closed gate and only publication opens it (P-029).
                    bool stagedLease = StageOneLaneLease(primary, operation, out string leaseFailure);
                    if (!stagedLease)
                    {
                        Add(name, false, leaseFailure);
                        return;
                    }

                    IReadOnlyList<PublishedOperation> drained = primary.Lane.Drain();
                    OperationResult? target = primary.Lane.ResultOf(operation);
                    int liveLeasesAfterPublication = primary.Lane.Resources.LiveLeaseCount;
                    int rowsBeforeAssembly = primary.Publisher.Published.BindingRowCount;

                    OperationId cancellation = NextOperation(primary.World);
                    CancelOutcome outcome = primary.Lane.Cancel(cancellation, operation);
                    OperationResult? cancellationRow = primary.Lane.ResultOf(cancellation);
                    OperationResult? targetAfter = primary.Lane.ResultOf(operation);
                    int liveLeasesAfterCancel = primary.Lane.Resources.LiveLeaseCount;

                    // Control 1: an operation this lane never admitted is `Unknown`, not a rollback.
                    CancelOutcome unknown = primary.Lane.Cancel(NextOperation(primary.World), NextOperation(primary.World));

                    // Control 2: a repeated cancel of an already-cancelled target returns the same terminal result
                    // and decides nothing a second time.
                    int decisionsAtRepeat = primary.Lane.OperationLedger.CancelledCount;
                    CancelOutcome repeated = primary.Lane.Cancel(NextOperation(primary.World), cancelledBeforeCutoff);
                    int decisionsAfterRepeat = primary.Lane.OperationLedger.CancelledCount;

                    // The world is still at the pre-assembly epoch: the composition publication is committed on the
                    // lane, and only the derived publication turns it into the world's next assembly (P-006).
                    bool worldStillBehind = primary.Host.CurrentEpoch.Equals(epochBefore)
                        && primary.Publisher.Published.BindingRowCount == rowsBeforeAssembly;
                    DerivedAssemblyReport derived = primary.Pipeline.PublishDerived(NextOperation(primary.World));
                    bool worldAdvanced = derived.Outcome == DerivedAssemblyOutcome.Published
                        && primary.Host.CurrentEpoch.Value > epochBefore.Value
                        && primary.Host.CurrentEpoch.Equals(primary.Lane.Committed.Epoch)
                        && MatchesPublishedAssembly(primary);

                    bool pass = drained.Count == 1
                        && drained[0].Outcome == Outcome.Published
                        && target != null
                        && target.Outcome == Outcome.Published
                        && target.Code == DiagnosticCode.None
                        && outcome == CancelOutcome.TooLate
                        && cancellationRow != null
                        && cancellationRow.Code == DiagnosticCode.TooLate
                        && cancellationRow.Outcome == Outcome.Rejected
                        && targetAfter != null
                        && targetAfter.Outcome == Outcome.Published
                        && liveLeasesAfterPublication == 1
                        && liveLeasesAfterCancel == 1
                        && unknown == CancelOutcome.Unknown
                        && repeated == CancelOutcome.Cancelled
                        && decisionsAfterRepeat == decisionsAtRepeat
                        && worldStillBehind
                        && worldAdvanced;

                    Add(name, pass,
                        "cancelOutcome=" + outcome
                        + "; drained=" + drained.Count.ToString(CultureInfo.InvariantCulture)
                        + "; targetOutcome=" + (target != null ? target.Outcome.ToString() : "<none>")
                        + "; targetStaysPublished=" + (targetAfter != null && targetAfter.Outcome == Outcome.Published)
                        + "; cancelCode=" + (cancellationRow != null ? cancellationRow.Code.ToString() : "<none>")
                        + "; liveLeases=" + liveLeasesAfterPublication.ToString(CultureInfo.InvariantCulture)
                        + "->" + liveLeasesAfterCancel.ToString(CultureInfo.InvariantCulture)
                        + "; unknownCancelOutcome=" + unknown
                        + "; repeatedCancelOutcome=" + repeated
                        + "; cancelDecisions=" + decisionsAtRepeat.ToString(CultureInfo.InvariantCulture)
                        + "->" + decisionsAfterRepeat.ToString(CultureInfo.InvariantCulture)
                        + "; worldStillBehind=" + worldStillBehind
                        + "; epochBefore=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneEpoch=" + primary.Lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldAdvanced=" + worldAdvanced
                        + "; worldState=" + primary.Host.Lifecycle
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 5. row 5: the migration boundary

            /// <summary>
            /// Row 5, prewrite half. The migration boundary sits at the top of `AssemblyPublisher`'s migration
            /// stage — after the fence, before any live write — so an armed fault is a prewrite refusal with
            /// `ResourceUnavailable`: nothing is written, the live slots keep the values *and the versions* they
            /// had, and the world stays `Running` and still commits a step afterwards (P-029, P-031).
            ///
            /// Which slot the plan migrates is the family's own declaration, never a version this step invents.
            /// A live row put back to an older version by hand would make the planner reject the *whole plan* with
            /// `MigrationRequired` before the boundary is ever reached (P-032), so the migration this step rides
            /// is one the revision really declares: <see cref="FindDeclaredMigration"/> looks the descriptor's
            /// slots up for one that declares a version-change policy with a registered handler, and seeds that
            /// slot to the handler's source version through the world's own seeder. The narrative family's
            /// conversation domain declares exactly that (version 2 with a registered 1->2 handler), so its
            /// observation also asserts the planned-migration count and the handler's source version. A family
            /// whose revision declares no migration — the card catalog does not — still proves the boundary at its
            /// real position, over a plan the publisher really prepares and refuses inside the migration stage.
            /// </summary>
            private void RefuseTheAssemblyAtTheMigrationBoundary()
            {
                const string name = "gc017-prewrite-migration-fault-preserves-live-state";
                try
                {
                    if (primary == null || resources == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    DeclaredMigration declared = FindDeclaredMigration(primary);
                    uint seedVersion = declared.Found ? declared.FromVersion : 0U;
                    DiagnosticCode seedCode = DiagnosticCode.None;
                    string seedDetail = string.Empty;
                    bool slotSeeded = !declared.Found
                        || primary.Seeder.TrySeedSlot(
                            declared.Slot.Target,
                            declared.Slot.Owner,
                            declared.Slot.Slot,
                            declared.FromVersion,
                            declared.Value,
                            out seedCode,
                            out seedDetail);
                    if (!slotSeeded)
                    {
                        Add(name, false, "the declared migratable slot could not be moved to its handler's source version: "
                            + seedCode + ": " + seedDetail);
                        return;
                    }

                    // The live slot rows as the world owns them right now, so "live state was kept" is a comparison
                    // against what really existed before the refused derivation ran, not against a remembered
                    // constant (P-029).
                    IReadOnlyList<LiveSlotState> slotsBefore = primary.Seeder.ReadLiveSlots(TargetIds(primary));

                    OperationId edit = NextOperation(primary.World);
                    bool staged = SubmitAndDrain(
                        primary, family.MountSecondProvider(), "mount-second-provider", edit, out string stageFailure);
                    if (!staged)
                    {
                        Add(name, false, stageFailure);
                        return;
                    }

                    AssemblyFaultInjection faults = primary.Host.Faults;
                    AssemblyEpoch epochBefore = primary.Host.CurrentEpoch;
                    int reachesBefore = faults.ReachCountOf(FaultBoundary.Migration);
                    faults.Arm(FaultBoundary.Migration);
                    OperationId derivation = NextOperation(primary.World);
                    DerivedAssemblyReport derived = primary.Pipeline.PublishDerived(derivation);
                    AssemblyPublicationReport? publication = derived.Publication;
                    faults.Disarm(FaultBoundary.Migration);

                    // This is the derivation that adopts the pair a refusal then leaves pending, so it is the one that
                    // records the module's own proposal for the fence and cleanup steps (P-006).
                    NotePendingProposal(primary, derived);

                    int migrationsPlanned = derived.Plan != null ? derived.Plan.Migrations.Count : -1;
                    IReadOnlyList<FaultRecord> records = faults.Trace.Of(FaultBoundary.Migration);
                    int reaches = faults.ReachCountOf(FaultBoundary.Migration) - reachesBefore;

                    // The armed refusal happened inside the migration stage: the plan was prepared (or it could not
                    // have reached any boundary), the reach fired exactly once for this publication, and the record
                    // carries the plan's own identity (P-052).
                    bool boundaryReached = reaches == 1
                        && records.Count >= 1
                        && records[records.Count - 1].Injected
                        && records[records.Count - 1].Operation.Equals(derivation)
                        && !records[records.Count - 1].PlanHash.Equals(ContentHash.Empty);

                    // A family that declares a migration proves the deeper fact: this plan really staged one, on
                    // scratch, for the slot and source version its handler declares (P-029, P-032).
                    bool migrationPlanned = !declared.Found || migrationsPlanned >= 1;

                    // Every live slot row is exactly what it was — same key, same version, same value — because a
                    // prewrite refusal touches nothing the publisher's migration stage had not already copied
                    // (P-029). For a family whose revision declares a migration, its seeded row is additionally the
                    // handler's source version, which is what makes the refusal "before the migration ran".
                    IReadOnlyList<LiveSlotState> slotsAfter = primary.Seeder.ReadLiveSlots(TargetIds(primary));
                    bool liveStateKept = LiveSlotsEqual(slotsBefore, slotsAfter)
                        && (!declared.Found
                            || (ReadSlot(primary, declared.Slot, out int _, out uint seededVersion)
                                && seededVersion == declared.FromVersion));

                    int stepsBefore = primary.Host.Driver.CommittedStepCount;
                    ulong pumped = PumpOneFrame(primary);
                    bool stillPumping = pumped == 1UL
                        && primary.Host.Driver.CommittedStepCount == stepsBefore + 1
                        && primary.Host.Lifecycle == WorldLifecycleState.Running;

                    bool pass = migrationPlanned
                        && boundaryReached
                        && derived.Outcome == DerivedAssemblyOutcome.Refused
                        && publication != null
                        && publication.Outcome == Outcome.Rejected
                        && publication.Code == DiagnosticCode.ResourceUnavailable
                        && publication.StructuralWrites == 0
                        && !publication.CrossedLiveWriteBoundary
                        && publication.MigratedSlots == 0
                        && primary.Host.CurrentEpoch.Equals(epochBefore)
                        && liveStateKept
                        && stillPumping;

                    int liveValue = declared.Found ? declared.Value : 0;
                    uint liveVersion = declared.Found ? declared.FromVersion : 0U;
                    if (declared.Found && ReadSlot(primary, declared.Slot, out int kept, out uint keptVersion))
                    {
                        liveValue = kept;
                        liveVersion = keptVersion;
                    }

                    Add(name, pass,
                        "edit=mount-second-provider"
                        + "; slot=" + (declared.Found ? declared.Slot.ToString() : "<none-declared>")
                        + "; declaredVersion=" + declared.ToVersion.ToString(CultureInfo.InvariantCulture)
                        + "; handlerFromVersion=" + seedVersion.ToString(CultureInfo.InvariantCulture)
                        + "; migrationsPlanned=" + migrationsPlanned.ToString(CultureInfo.InvariantCulture)
                        + "; armed=True"
                        + "; outcome=" + DescribePublication(publication)
                        + "; structuralWrites=" + (publication != null ? publication.StructuralWrites : -1).ToString(CultureInfo.InvariantCulture)
                        + "; crossedLiveWriteBoundary=" + (publication != null && publication.CrossedLiveWriteBoundary)
                        + "; migratedSlots=" + (publication != null ? publication.MigratedSlots : -1).ToString(CultureInfo.InvariantCulture)
                        + "; liveSlotVersion=" + liveVersion.ToString(CultureInfo.InvariantCulture)
                        + "; liveSlotValue=" + liveValue.ToString(CultureInfo.InvariantCulture)
                        + "; liveStateKept=" + liveStateKept
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + primary.Host.Lifecycle
                        + "; pumpedSteps=" + pumped.ToString(CultureInfo.InvariantCulture)
                        + "; stillPumping=" + stillPumping
                        + "; boundaryReaches=" + reaches.ToString(CultureInfo.InvariantCulture)
                        + "; injected=" + faults.Trace.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + TraceLineOf(records)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 6. row 5: after the first live write

            /// <summary>
            /// Row 5, postwrite half. The fault is injected after the apply stage's last authoritative write and its
            /// stamp, so the world faults: `Outcome.Faulted`, code `ApplyFault`, the live-write boundary really
            /// crossed, no epoch and no image published, `PublishedToken` null, the last committed view still the
            /// only safe observation, and `PumpFrame` refusing every later frame (P-031). A faulted world is
            /// terminal, so this observation stands up its own world rather than pretending the first one continued.
            ///
            /// The fault this path latches is the *world's*: `AssemblyPublisher` refuses through
            /// `EnterFaulted`, so the observable terminal state is the host's `FaultCode`/`FaultCount` beside the
            /// `Faulted` lifecycle. The execution driver latches only its own faults (a throwing system, a failed
            /// commit), which is why no driver assertion belongs here (P-031, P-052).
            /// </summary>
            private void FaultTheWorldAfterItsFirstLiveWrite()
            {
                const string name = "gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image";
                try
                {
                    WorldChain? chain = StandUpWorld("postwrite", NextOperation(default(WorldId)), CompileOnce());
                    postwriteWorld = chain;
                    if (chain == null)
                    {
                        Add(name, false, "the second world could not be stood up: " + lastFailure);
                        return;
                    }

                    OperationId edit = NextOperation(chain.World);
                    bool staged = SubmitAndDrain(
                        chain, family.MountProvider(), "mount-provider", edit, out string stageFailure);
                    if (!staged)
                    {
                        Add(name, false, stageFailure);
                        return;
                    }

                    AssemblyEpoch epochBefore = chain.Host.CurrentEpoch;
                    int imagesBefore = chain.Host.Publications.PublishedCount;
                    int faultsBefore = chain.Host.FaultCount;
                    PublishedWorldView lastCommitted = chain.Publisher.Published;
                    int brokenBefore = chain.Publisher.Faults.InjectedCount;

                    chain.Host.Faults.Arm(FaultBoundary.FirstLiveWrite);
                    DerivedAssemblyReport derived = chain.Pipeline.PublishDerived(NextOperation(chain.World));
                    AssemblyPublicationReport? publication = derived.Publication;
                    chain.Host.Faults.Disarm(FaultBoundary.FirstLiveWrite);

                    WorldPumpResult after = chain.Host.PumpFrame(IdlePumpTicks);
                    bool lastViewReadable = ReferenceEquals(lastCommitted, chain.Publisher.Published)
                        && chain.Host.Publications.Retained.Count > 0
                        && chain.Host.Publications.Last != null;

                    bool pass = derived.Outcome == DerivedAssemblyOutcome.Refused
                        && publication != null
                        && publication.Outcome == Outcome.Faulted
                        && publication.Code == DiagnosticCode.ApplyFault
                        && publication.CrossedLiveWriteBoundary
                        && publication.PublishedToken == null
                        && publication.StructuralWrites > 0
                        && chain.Host.Lifecycle == WorldLifecycleState.Faulted
                        && chain.Host.FaultCode == DiagnosticCode.ApplyFault
                        && chain.Host.FaultDetail.Length > 0
                        && chain.Host.FaultCount == faultsBefore + 1
                        && chain.Host.CurrentEpoch.Equals(epochBefore)
                        && chain.Host.Publications.PublishedCount == imagesBefore
                        && lastViewReadable
                        && !after.Pumped
                        && publication.Detail.Length > 0
                        && chain.Publisher.Faults.InjectedCount == brokenBefore + 1;

                    Add(name, pass,
                        "world=fresh"
                        + "; armed=True"
                        + "; outcome=" + DescribePublication(publication)
                        + "; crossedLiveWriteBoundary=" + (publication != null && publication.CrossedLiveWriteBoundary)
                        + "; structuralWrites=" + (publication != null ? publication.StructuralWrites : -1).ToString(CultureInfo.InvariantCulture)
                        + "; publishedToken=" + (publication == null || publication.PublishedToken == null ? "<null>" : "set")
                        + "; worldState=" + chain.Host.Lifecycle
                        + "; hostFaultCode=" + chain.Host.FaultCode
                        + "; hostFaultDetail=" + chain.Host.FaultDetail
                        + "; faultCount=" + chain.Host.FaultCount.ToString(CultureInfo.InvariantCulture)
                        + " (was " + faultsBefore.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; epoch=" + chain.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; epochBefore=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; images=" + chain.Host.Publications.PublishedCount.ToString(CultureInfo.InvariantCulture)
                        + " (was " + imagesBefore.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; lastViewReadable=" + lastViewReadable
                        + "; pumpRefused=" + !after.Pumped
                        + "; injected=" + chain.Publisher.Faults.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + TraceLineOf(chain.Publisher.Faults.Trace.Of(FaultBoundary.FirstLiveWrite))
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 7. row 6: structural playback

            /// <summary>
            /// Row 6. The boundary is reached between a step's systems and its commit: the structural work they
            /// recorded has played back, their tracked handles were settled by the step fence, and the step's
            /// committed output does not exist yet. An armed fault therefore stops the commit — the advance is not
            /// accepted, `PublishedSnapshot` is null, the driver latches `ApplyFault`, the world faults, the logical
            /// step does not move and no step image publishes — while the ledger shows no abandoned work and no
            /// handle retained (P-031, P-041, P-044, P-047).
            ///
            /// The one ledger fact that is *not* zero here is retained resources, and that is the protocol: a
            /// faulted world never releases what unfinished work may still reach, so its staged leases stay
            /// retained until `Stop` settles them (P-048). Teardown observes that settlement — the final step
            /// asserts the same ledger at zero after every world was stopped and disposed.
            /// </summary>
            private void FaultTheStepAfterItsStructuralPlayback()
            {
                const string name = "gc017-structural-playback-fault-stops-the-step-commit";
                try
                {
                    WorldChain? chain = StandUpWorld("playback", NextOperation(default(WorldId)), CompileOnce());
                    if (chain == null)
                    {
                        Add(name, false, "the third world could not be stood up: " + lastFailure);
                        return;
                    }

                    LogicalStepId stepBefore = chain.Host.CurrentStep;
                    AssemblyEpoch epochBefore = chain.Host.CurrentEpoch;
                    int imagesBefore = chain.Host.Publications.PublishedCount;
                    int dispatchedBefore = chain.Host.StepGroup.TotalDispatchedCount;
                    int retainedResourcesBefore = chain.Host.Ledger.RetainedResourceCount;

                    chain.Host.Faults.Arm(FaultBoundary.StructuralPlayback);
                    chain.Host.NotifyCommandAdmitted(1U);
                    TimeFrameReport frame = chain.Time.PumpFrame(IdlePumpTicks);
                    chain.Host.Faults.Disarm(FaultBoundary.StructuralPlayback);

                    StepAdvanceResult? advance = frame.Pump.Advance;
                    int outstanding = chain.Host.Ledger.OutstandingJobCount;
                    int quarantined = chain.Host.Ledger.QuarantinedJobCount;
                    int retainedHandles = chain.Host.Driver.RetainedJobs.Count;
                    int retainedResourcesAfter = chain.Host.Ledger.RetainedResourceCount;

                    bool pass = frame.StepsCommitted == 0UL
                        && advance != null
                        && !advance.Accepted
                        && advance.Outcome == Outcome.Faulted
                        && advance.PublishedSnapshot == null
                        && chain.Host.Driver.IsFaulted
                        && chain.Host.Driver.FaultCode == DiagnosticCode.ApplyFault
                        && chain.Host.Lifecycle == WorldLifecycleState.Faulted
                        && chain.Host.CurrentStep.Equals(stepBefore)
                        && chain.Host.CurrentEpoch.Equals(epochBefore)
                        && chain.Host.Publications.PublishedCount == imagesBefore
                        && chain.Host.StepGroup.TotalDispatchedCount > dispatchedBefore
                        && outstanding == 0
                        && quarantined == 0
                        && retainedHandles == 0
                        // P-048: the faulted world releases nothing unfinished work may still reach. Its host
                        // resources (world storage, identity index, the message plane, the system registrations)
                        // were acquired at creation and stay retained until `Stop` settles them; the teardown
                        // observation is where that settlement reaches zero.
                        && retainedResourcesBefore > 0
                        && retainedResourcesAfter == retainedResourcesBefore;

                    Add(name, pass,
                        "world=fresh"
                        + "; armed=True"
                        + "; stepsCommitted=" + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                        + "; accepted=" + (advance != null && advance.Accepted)
                        + "; advanceOutcome=" + (advance != null ? advance.Outcome.ToString() : "<none>")
                        + "; publishedSnapshot=" + (advance == null || advance.PublishedSnapshot == null ? "<null>" : "set")
                        + "; driverFaulted=" + chain.Host.Driver.IsFaulted
                        + "; driverCode=" + chain.Host.Driver.FaultCode
                        + "; worldState=" + chain.Host.Lifecycle
                        + "; step=" + chain.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; stepBefore=" + stepBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + chain.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; images=" + chain.Host.Publications.PublishedCount.ToString(CultureInfo.InvariantCulture)
                        + " (was " + imagesBefore.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; dispatched=" + dispatchedBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + chain.Host.StepGroup.TotalDispatchedCount.ToString(CultureInfo.InvariantCulture)
                        + "; ledgerJobs=" + chain.Host.Ledger.JobCount.ToString(CultureInfo.InvariantCulture)
                        + "; outstandingJobs=" + outstanding.ToString(CultureInfo.InvariantCulture)
                        + "; quarantinedJobs=" + quarantined.ToString(CultureInfo.InvariantCulture)
                        + "; retainedResources=" + retainedResourcesAfter.ToString(CultureInfo.InvariantCulture)
                        + " (was " + retainedResourcesBefore.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; boundaryReaches=" + chain.Host.Faults.ReachCountOf(FaultBoundary.StructuralPlayback).ToString(CultureInfo.InvariantCulture)
                        + "; injected=" + chain.Host.Faults.Trace.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + TraceLineOf(chain.Host.Faults.Trace.Of(FaultBoundary.StructuralPlayback))
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 8. row 5: the gate installation

            /// <summary>
            /// Row 5's third face. Installing the new execution graph and its closed ingress gates happens after the
            /// apply stage wrote live storage, so a fault here has exactly the postwrite shape: the world faults, no
            /// epoch or image publishes, the token is null, the live-write boundary was crossed and the boundary was
            /// reached exactly once (P-030, P-031). Like every publisher postwrite path, the terminal state is the
            /// host's own fault latch (`EnterFaulted`), not the driver's (P-052).
            /// </summary>
            private void FaultTheWorldWhenItInstallsTheNewSchedule()
            {
                const string name = "gc017-gate-installation-fault-stops-after-live-writes";
                try
                {
                    WorldChain? chain = StandUpWorld("gate-install", NextOperation(default(WorldId)), CompileOnce());
                    if (chain == null)
                    {
                        Add(name, false, "the fourth world could not be stood up: " + lastFailure);
                        return;
                    }

                    OperationId edit = NextOperation(chain.World);
                    bool staged = SubmitAndDrain(
                        chain, family.MountProvider(), "mount-provider", edit, out string stageFailure);
                    if (!staged)
                    {
                        Add(name, false, stageFailure);
                        return;
                    }

                    AssemblyEpoch epochBefore = chain.Host.CurrentEpoch;
                    int imagesBefore = chain.Host.Publications.PublishedCount;
                    int faultsBefore = chain.Host.FaultCount;
                    PublishedWorldView lastCommitted = chain.Publisher.Published;

                    chain.Host.Faults.Arm(FaultBoundary.GateInstallation);
                    DerivedAssemblyReport derived = chain.Pipeline.PublishDerived(NextOperation(chain.World));
                    AssemblyPublicationReport? publication = derived.Publication;
                    chain.Host.Faults.Disarm(FaultBoundary.GateInstallation);

                    int reaches = chain.Host.Faults.ReachCountOf(FaultBoundary.GateInstallation);
                    WorldPumpResult after = chain.Host.PumpFrame(IdlePumpTicks);

                    bool pass = reaches == 1
                        && publication != null
                        && publication.Outcome == Outcome.Faulted
                        && publication.Code == DiagnosticCode.ApplyFault
                        && publication.CrossedLiveWriteBoundary
                        && publication.PublishedToken == null
                        && publication.StructuralWrites > 0
                        && chain.Host.Lifecycle == WorldLifecycleState.Faulted
                        && chain.Host.FaultCode == DiagnosticCode.ApplyFault
                        && chain.Host.FaultCount == faultsBefore + 1
                        && chain.Host.CurrentEpoch.Equals(epochBefore)
                        && chain.Host.Publications.PublishedCount == imagesBefore
                        && ReferenceEquals(lastCommitted, chain.Publisher.Published)
                        && !after.Pumped;

                    Add(name, pass,
                        "world=fresh"
                        + "; armed=True"
                        + "; boundaryReaches=" + reaches.ToString(CultureInfo.InvariantCulture)
                        + "; outcome=" + DescribePublication(publication)
                        + "; crossedLiveWriteBoundary=" + (publication != null && publication.CrossedLiveWriteBoundary)
                        + "; structuralWrites=" + (publication != null ? publication.StructuralWrites : -1).ToString(CultureInfo.InvariantCulture)
                        + "; publishedToken=" + (publication == null || publication.PublishedToken == null ? "<null>" : "set")
                        + "; worldState=" + chain.Host.Lifecycle
                        + "; hostFaultCode=" + chain.Host.FaultCode
                        + "; faultCount=" + chain.Host.FaultCount.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + chain.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; images=" + chain.Host.Publications.PublishedCount.ToString(CultureInfo.InvariantCulture)
                        + "; pumpRefused=" + !after.Pumped
                        + "; injected=" + chain.Host.Faults.Trace.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + TraceLineOf(chain.Host.Faults.Trace.Of(FaultBoundary.GateInstallation))
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 9. row 4: the fence

            /// <summary>
            /// Row 4. `FenceOldAssembly` runs before the boundary is reached, so the tracked handles of the old
            /// assembly are already settled when the fault fires: the refusal reports the fence's own drained count
            /// alongside `StructuralWrites == 0`, and the old assembly stays the published one with the world still
            /// `Running` (P-030, P-041, P-047).
            /// </summary>
            private void RefuseTheAssemblyAtTheFenceBoundary()
            {
                const string name = "gc017-fence-fault-settles-handles-and-keeps-the-old-assembly";
                try
                {
                    if (primary == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    AssemblyEpoch epochBefore = primary.Host.CurrentEpoch;
                    int rowsBefore = primary.Publisher.Published.BindingRowCount;
                    AssemblyFaultInjection faults = primary.Host.Faults;

                    PlannedPublication? plan = BuildThePendingPlan(
                        primary, NextOperation(primary.World), 0, out PlanStaging staging);
                    if (plan == null)
                    {
                        Add(name, false, "the plan for the pending composition publication was not built: " + lastFailure);
                        return;
                    }

                    // The fence boundary sits on the common path of every publication that gets past the acquisition
                    // boundary, so its reach count is already positive here; what this step proves is that *this*
                    // publication reached it exactly once, which is a delta and not an absolute.
                    int fenceReachesBefore = faults.ReachCountOf(FaultBoundary.Fence);
                    faults.Arm(FaultBoundary.Fence);
                    AssemblyPublicationReport refused = primary.Publisher.Publish(plan);
                    faults.Disarm(FaultBoundary.Fence);
                    int fenceReachesDelta = faults.ReachCountOf(FaultBoundary.Fence) - fenceReachesBefore;

                    bool pass = refused.Outcome == Outcome.Rejected
                        && refused.Code == DiagnosticCode.ResourceUnavailable
                        && refused.StructuralWrites == 0
                        && !refused.CrossedLiveWriteBoundary
                        && primary.Publisher.Published.Epoch.Equals(epochBefore)
                        && primary.Publisher.Published.BindingRowCount == rowsBefore
                        && staging.Plan != null
                        && staging.Plan.State.Phase == PlanPhase.Rejected
                        && fenceReachesDelta == 1
                        && primary.Host.Lifecycle == WorldLifecycleState.Running
                        && PendingRefusalHeld(primary, epochBefore);
                    Add(name, pass,
                        "fence=prewrite-refusal"
                        + "; outcome=" + DescribePublication(refused)
                        + "; drainedHandles=" + refused.DrainedHandles.ToString(CultureInfo.InvariantCulture)
                        + "; planPhase=" + (staging.Plan != null ? staging.Plan.State.Phase.ToString() : "<none>")
                        + "; fenceReachesDelta=" + fenceReachesDelta.ToString(CultureInfo.InvariantCulture)
                        + "; structuralWrites=" + refused.StructuralWrites.ToString(CultureInfo.InvariantCulture)
                        + "; crossedLiveWriteBoundary=" + refused.CrossedLiveWriteBoundary
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + primary.Publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + primary.Host.Lifecycle
                        + "; pendingRefusalHeld=" + PendingRefusalHeld(primary, epochBefore)
                        + "; adoptedPair=" + primary.Lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "/" + primary.Lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; boundaryReaches=" + faults.ReachCountOf(FaultBoundary.Fence).ToString(CultureInfo.InvariantCulture)
                        + "; injected=" + faults.Trace.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + TraceLineOf(faults.Trace.Of(FaultBoundary.Fence))
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 10. row 8: cleanup retains

            /// <summary>
            /// Row 8, first half. A prewrite refusal releases the plan's staged leases — unless the *cleanup* boundary
            /// is armed, in which case P-048's rule wins: the release is not attempted, nothing is reported as
            /// disposed, and every staged lease stays retained and is reported as a quarantine reference. Elapsed
            /// time never authorizes freeing what cannot be proven safe.
            /// </summary>
            private void RetainTheStagedOwnershipOfARefusedCleanup()
            {
                const string name = "gc017-cleanup-fault-retains-staged-ownership";
                try
                {
                    if (primary == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    AssemblyEpoch epochBefore = primary.Host.CurrentEpoch;
                    int rowsBefore = primary.Publisher.Published.BindingRowCount;
                    AssemblyFaultInjection faults = primary.Host.Faults;

                    PlannedPublication? plan = BuildThePendingPlan(
                        primary, NextOperation(primary.World), CleanupLeaseCount, out PlanStaging staging);
                    if (plan == null)
                    {
                        Add(name, false, "the plan for the pending composition publication was not built: " + lastFailure);
                        return;
                    }

                    faults.Arm(FaultBoundary.Cleanup);
                    faults.FailDuringMigration = true;
                    AssemblyPublicationReport refused = primary.Publisher.Publish(plan);
                    faults.FailDuringMigration = false;
                    faults.Disarm(FaultBoundary.Cleanup);

                    int retainedStaged = refused.Record.QuarantineReferences.Count;
                    int retainedInSet = staging.Acquisitions != null
                        ? staging.Acquisitions.RetainedLeaseIds().Count
                        : 0;

                    bool pass = refused.Outcome == Outcome.Rejected
                        && staging.LeaseIds.Count == CleanupLeaseCount
                        && retainedStaged == CleanupLeaseCount
                        && retainedInSet == CleanupLeaseCount
                        && staging.Gate != null
                        && staging.Gate.LiveLeaseCount == CleanupLeaseCount
                        && staging.Gate.ReleasedCount == 0
                        && primary.Publisher.Published.Epoch.Equals(epochBefore)
                        && primary.Publisher.Published.BindingRowCount == rowsBefore
                        && primary.Host.Lifecycle == WorldLifecycleState.Running;

                    Add(name, pass,
                        "cleanupArmed=True"
                        + "; migrationRefused=True"
                        + "; outcome=" + DescribePublication(refused)
                        + "; stagedLeases=" + staging.LeaseIds.Count.ToString(CultureInfo.InvariantCulture)
                        + "; retainedStaged=" + retainedStaged.ToString(CultureInfo.InvariantCulture)
                        + "; retainedInSet=" + retainedInSet.ToString(CultureInfo.InvariantCulture)
                        + "; gateLiveLeases=" + (staging.Gate != null ? staging.Gate.LiveLeaseCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; gateReleased=" + (staging.Gate != null ? staging.Gate.ReleasedCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; crossedLiveWriteBoundary=" + refused.CrossedLiveWriteBoundary
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + primary.Publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + primary.Host.Lifecycle
                        + "; boundaryReaches=" + faults.ReachCountOf(FaultBoundary.Cleanup).ToString(CultureInfo.InvariantCulture)
                        + "; injected=" + faults.Trace.InjectedCount.ToString(CultureInfo.InvariantCulture)
                        + "; traceRecord=" + TraceLineOf(faults.Trace.Of(FaultBoundary.Cleanup))
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 11. row 8: cleanup releases

            /// <summary>
            /// Row 8's falsifying counterpart: the same refusal with the cleanup boundary disarmed. The release is
            /// attempted, the plan's staged leases come back empty, and the world keeps its old assembly — so the
            /// previous observation's "retained" is a statement about the injected fault and not about the release
            /// path being unreachable (P-048).
            /// </summary>
            private void ReleaseWhatARefusalStaged()
            {
                const string name = "gc017-cleanup-boundary-releases-what-a-refusal-staged";
                try
                {
                    if (primary == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    AssemblyEpoch epochBefore = primary.Host.CurrentEpoch;
                    int rowsBefore = primary.Publisher.Published.BindingRowCount;
                    AssemblyFaultInjection faults = primary.Host.Faults;
                    bool cleanupDisarmed = !faults.IsArmed(FaultBoundary.Cleanup);

                    PlannedPublication? plan = BuildThePendingPlan(
                        primary, NextOperation(primary.World), CleanupLeaseCount, out PlanStaging staging);
                    if (plan == null)
                    {
                        Add(name, false, "the plan for the pending composition publication was not built: " + lastFailure);
                        return;
                    }

                    faults.FailDuringMigration = true;
                    AssemblyPublicationReport refused = primary.Publisher.Publish(plan);
                    faults.FailDuringMigration = false;

                    int retainedStaged = staging.Acquisitions != null
                        ? staging.Acquisitions.RetainedLeaseIds().Count
                        : -1;
                    int quarantined = refused.Record.QuarantineReferences.Count;
                    int failed = refused.Record.CleanupReferences.Count;
                    bool pass = cleanupDisarmed
                        && refused.Outcome == Outcome.Rejected
                        && refused.Code == DiagnosticCode.ResourceUnavailable
                        && staging.LeaseIds.Count == CleanupLeaseCount
                        && staging.Gate != null
                        && staging.Gate.AcquiredCount == CleanupLeaseCount
                        && staging.Gate.ReleasedCount == CleanupLeaseCount
                        && staging.Gate.LiveLeaseCount == 0
                        && retainedStaged == 0
                        && quarantined == 0
                        && failed == 0
                        && primary.Publisher.Published.Epoch.Equals(epochBefore)
                        && primary.Publisher.Published.BindingRowCount == rowsBefore
                        && primary.Host.Lifecycle == WorldLifecycleState.Running;

                    Add(name, pass,
                        "cleanupArmed=False"
                        + "; migrationRefused=True"
                        + "; outcome=" + DescribePublication(refused)
                        + "; stagedLeases=" + staging.LeaseIds.Count.ToString(CultureInfo.InvariantCulture)
                        + "; stagedLeasesReleased=" + (staging.Gate != null && staging.Gate.ReleasedCount == CleanupLeaseCount)
                        + "; retainedStaged=" + retainedStaged.ToString(CultureInfo.InvariantCulture)
                        + "; gateReleased=" + (staging.Gate != null ? staging.Gate.ReleasedCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; gateLiveLeases=" + (staging.Gate != null ? staging.Gate.LiveLeaseCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; cleanupFailures=" + failed.ToString(CultureInfo.InvariantCulture)
                        + "; quarantines=" + quarantined.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + primary.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + primary.Publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; worldState=" + primary.Host.Lifecycle
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 12. row 10: recovery

            /// <summary>
            /// Row 10. The postwrite-faulted world is the recovery source; the destination is a caller-reserved fresh
            /// session created from the *same* initial definition, and a reference-repair step exists but is not
            /// needed (the initial-definition source rebuilds from the compiled catalog). The source is never
            /// resumed and never repaired: it stays `Faulted`, still refuses `PumpFrame`, and a handle minted for it
            /// cannot be resolved by the destination's own publisher, because a recovered world is a new incarnation
            /// (P-004, P-005, P-031, P-035, P-049).
            /// </summary>
            private void RecoverFromTheFaultedWorld()
            {
                const string name = "gc017-recovery-from-initial-definitions-into-a-new-world";
                try
                {
                    if (postwriteWorld == null)
                    {
                        Add(name, false, "the faulted source world is missing");
                        return;
                    }

                    WorldChain source = postwriteWorld;
                    WorldId destination = new WorldId(sessionSequence.Next());
                    OperationId recovery = NextOperation(destination);
                    var request = new RecoveryRequest(
                        source.World,
                        destination,
                        source.Host.Request.Definition,
                        source.Host.Request.TemporalModel,
                        source.Host.Request.Mode,
                        source.Host.Request.CatalogHash,
                        recovery,
                        source.Host.Request.FixedStep);

                    int registryBefore = UnityWorldRegistry.Count;
                    RecoveryReport report = InitialDefinitionRecovery.Recover(request, source.Registration, null);
                    int registryAfter = UnityWorldRegistry.Count;

                    UnityWorldHost? destinationHost = report.DestinationHost;
                    if (destinationHost == null)
                    {
                        Add(name, false, "recovery produced no destination: " + report.Describe());
                        return;
                    }

                    WorldPumpResult sourcePump = source.Host.PumpFrame(IdlePumpTicks);

                    // The destination needs its own assembly path before a handle question can be asked of it, and
                    // a lane before its callback gate exists: both are the caller's, exactly as in the first world.
                    recovered = AttachToRecoveredWorld(destinationHost, source.DescriptorReport);
                    if (recovered == null)
                    {
                        Add(name, false, "the destination could not be given its own assembly path: " + lastFailure);
                        return;
                    }

                    recoveredLane = recovered.Lane;

                    // A real handle of the source world: it names the source's session, so the destination's own
                    // registry refuses it instead of re-mapping a reused slot (P-005).
                    bool minted = source.Registry.TryGetHandle(source.Targets.Targets[0].Target, out TargetHandle handle);
                    bool resolvable = recovered.Publisher.TryResolveHandle(handle, out TargetId _, out global::Unity.Entities.Entity _);

                    bool freshIncarnation = report.DestinationIsFreshIncarnation
                        && !recovered.World.Session.Equals(source.World.Session)
                        && recovered.Host.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && recovered.Host.CurrentStep.Equals(LogicalStepId.Zero)
                        && recovered.Host.Lifecycle == WorldLifecycleState.Running;

                    bool sourceUnchanged = report.SourceUnchanged
                        && source.Host.Lifecycle == WorldLifecycleState.Faulted
                        && !sourcePump.Pumped;

                    bool pass = report.Recovered
                        && report.Outcome == Outcome.Published
                        && registryAfter == registryBefore + 1
                        && report.RegistryCountAfter == report.RegistryCountBefore + 1
                        && freshIncarnation
                        && sourceUnchanged
                        && minted
                        && !resolvable;

                    Add(name, pass,
                        "recovered=" + report.Recovered
                        + "; outcome=" + report.Outcome
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + "; destination=" + recovered.World.Session.ToString()
                        + "; destinationEpoch=" + recovered.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; destinationStep=" + recovered.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; destinationState=" + recovered.Host.Lifecycle
                        + "; freshIncarnation=" + freshIncarnation
                        + "; sourceState=" + source.Host.Lifecycle
                        + "; sourceUnchanged=" + sourceUnchanged
                        + "; sourcePumpRefused=" + !sourcePump.Pumped
                        + "; sourceHandleMinted=" + minted
                        + "; handleResolved=" + resolvable
                        + "; repairs=" + report.RepairsAttempted.ToString(CultureInfo.InvariantCulture)
                        + "; " + report.Describe()
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 13. row 10: the old callback

            /// <summary>
            /// Row 10's second face. An async work token stamped by the *source* world and a managed lease over it
            /// reach the destination's callback gate: the world is checked first, so the completion is
            /// `DiscardForeignWorld` — a completion from a foreign incarnation can release its own resources but can
            /// never publish into the recovered world (P-004, P-047). Disposing the lease releases exactly what it
            /// owned, once, and the destination's epoch and step do not move.
            /// </summary>
            private void DiscardTheOldCallbackAtTheRecoveredWorld()
            {
                const string name = "gc017-old-callback-after-recovery-is-rejected";
                try
                {
                    if (postwriteWorld == null || recovered == null || recoveredLane == null)
                    {
                        Add(name, false, "the source world, the recovered world or its lane is missing");
                        return;
                    }

                    AssemblyEpoch epochBefore = recovered.Host.CurrentEpoch;
                    LogicalStepId stepBefore = recovered.Host.CurrentStep;

                    // The token's operation names the SOURCE incarnation: that is the whole point of the case, and
                    // `AsyncWorkToken`'s first argument is an `OperationId`, not a `WorldId` (P-050).
                    OperationId sourceOperation = NextOperation(postwriteWorld.World);
                    var token = new AsyncWorkToken(
                        sourceOperation,
                        family.UnloadInstall,
                        InstallationGeneration.First,
                        new ActivationEpoch(1UL),
                        0U);

                    int disposed = 0;
                    var lease = new ManagedResourceLease(
                        new ResourceKey(new Id128(PlanResourceNamespace, 0xC0170001UL)),
                        new Id128(PlanResourceNamespace, 0xC0170002UL),
                        token,
                        new FactoryKey(new Id128(PlanResourceNamespace, 0xC0170003UL), 1U),
                        new ManagedResourceGate(),
                        _ => disposed++);

                    CallbackGateDecision decision = recoveredLane.Callbacks.Evaluate(token);
                    lease.Dispose();
                    lease.Dispose();
                    bool releasedOnce = disposed == 1 && lease.DisposeCount == 1 && lease.IsDisposed;

                    bool pass = decision == CallbackGateDecision.DiscardForeignWorld
                        && token.Operation.World.Session.Equals(postwriteWorld.World.Session)
                        && !recoveredLane.Callbacks.World.Session.Equals(postwriteWorld.World.Session)
                        && releasedOnce
                        && recovered.Host.CurrentEpoch.Equals(epochBefore)
                        && recovered.Host.CurrentStep.Equals(stepBefore)
                        && recovered.Host.Lifecycle == WorldLifecycleState.Running;

                    Add(name, pass,
                        "disposition=" + decision
                        + "; tokenWorld=" + token.Operation.World.Session.ToString()
                        + "; destinationWorld=" + recovered.World.Session.ToString()
                        + "; disposed=" + disposed.ToString(CultureInfo.InvariantCulture)
                        + "; disposeCount=" + lease.DisposeCount.ToString(CultureInfo.InvariantCulture)
                        + "; isDisposed=" + lease.IsDisposed
                        + "; destinationEpoch=" + recovered.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; destinationStep=" + recovered.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; destinationState=" + recovered.Host.Lifecycle
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 14. teardown

            /// <summary>
            /// Every world this run created is stopped and disposed, including the ones a fault left terminal and the
            /// recovered one: no tracked job stays outstanding, no resource stays retained, and the registry returns
            /// to the count it had before the first creation (P-047, P-048).
            /// </summary>
            private void TearDownEveryWorld()
            {
                const string name = "gc017-teardown-settles-and-disposes";
                try
                {
                    int worldsCreated = worlds.Count;
                    var stopped = new List<string>(worldsCreated);
                    int outstanding = 0;
                    int retained = 0;
                    bool everyStopAccepted = true;

                    for (int i = 0; i < worlds.Count; i++)
                    {
                        WorldChain chain = worlds[i];
                        chain.Time.Clear(out int discarded, out int wakes);
                        _ = discarded;
                        _ = wakes;

                        OperationResult stop = chain.Host.Stop(
                            NextOperation(chain.World), "gc017 fault scenario teardown");
                        chain.Host.Dispose();
                        outstanding += chain.Host.Ledger.OutstandingJobCount;
                        retained += chain.Host.Ledger.RetainedResourceCount;
                        everyStopAccepted &= stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange;
                        stopped.Add(chain.Label + "=" + stop.Outcome);
                    }

                    int registryAfter = UnityWorldRegistry.Count;
                    bool pass = worldsCreated >= 4
                        && everyStopAccepted
                        && outstanding == 0
                        && retained == 0
                        && registryAfter == registryBeforeCreate;

                    Add(name, pass,
                        "worlds=" + worldsCreated.ToString(CultureInfo.InvariantCulture)
                        + "; stopped=" + Join(stopped)
                        + "; outstandingJobs=" + outstanding.ToString(CultureInfo.InvariantCulture)
                        + "; retainedResources=" + retained.ToString(CultureInfo.InvariantCulture)
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== world stand-up (§5)

            /// <summary>
            /// Stands one world up through the real chain of §5 and records it for teardown. Nothing here invents a
            /// module: the descriptor comes from the family's own compile, the registration from the family's own
            /// generated registrations, and the lane, bridge, pipeline and time driver are the production types.
            /// </summary>
            private WorldChain? StandUpWorld(string label, OperationId operation, PipelineDescriptorReport descriptorReport)
            {
                if (!descriptorReport.Succeeded
                    || descriptorReport.Descriptor == null
                    || descriptorReport.Adaptation == null
                    || descriptorReport.Adaptation.NativeTable == null)
                {
                    lastFailure = "the ownership and schedule pipeline refused: " + descriptorReport.Describe();
                    return null;
                }
                WorldId world = new WorldId(sessionSequence.Next());
                WorldCreateRequest request = family.CreateRequest(world, operation);
                UnityWorldRegistration registration = family.CreateRegistration(descriptorReport.Adaptation!);
                bool created = UnityWorldRegistry.TryCreate(
                    request, registration, out UnityWorldHost? host, out WorldCreateResult createResult);
                if (!created || host == null)
                {
                    lastFailure = "world creation failed: " + createResult.Code + ": " + createResult.Detail;
                    return null;
                }

                var registry = new TargetRegistry(world, 32);
                var publisher = new AssemblyPublisher(
                    host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptorReport.Descriptor!);
                var targets = new LiveTargetIndex(publisher.Recipes);
                var seeder = new LiveTargetSeeder(host, registry, targets);
                IDerivationValueSource values = family.CreateValues();

                var manifestSource = new FamilyManifestSource(
                    new CatalogManifestSource(family.Catalog, family.Declarations), family.ExtraManifests);
                var validator = new DerivationModeSwitchValidator(values, () => TargetView(targets));
                CompositionHost lane = CompositionHost.CreateDefault(
                    world,
                    family.WorldRootScope,
                    manifestSource,
                    resources,
                    family.LaneSeed,
                    validator);
                var bridge = new WorldCompositionBridge(host, lane, publisher);
                var pipeline = new DerivedAssemblyPipeline(
                    host,
                    lane,
                    publisher,
                    targets,
                    seeder,
                    values,
                    null,
                    null,
                    publisher.Migrations,
                    new StagedResourceGate(StagedByteCeiling, family.Issuer, host.Faults),
                    DefaultBudget());
                var time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                time.AdoptResourceTable(descriptorReport.Adaptation!.NativeTable!);

                var chain = new WorldChain(
                    label, host, lane, publisher, registry, targets, seeder, pipeline, bridge, time, descriptorReport);
                worlds.Add(chain);

                bool seeded = family.SeedTargets(new Gc013WorldContext(host, targets, seeder));
                bool casesSeeded = true;
                for (int i = 0; i < family.SlotCases.Count; i++)
                {
                    casesSeeded &= family.SeedSlotCase(family.SlotCases[i], seeder);
                }

                if (!seeded || !casesSeeded || targets.Count == 0)
                {
                    lastFailure = "the world was created but its declared targets were not seeded ("
                        + targets.Count.ToString(CultureInfo.InvariantCulture) + " live targets)";
                    return chain;
                }

                return chain;
            }

            /// <summary>
            /// Gives a recovered world its own assembly path and its own control lane. The recovery contract creates
            /// and exposes the world (P-049); the caller owns everything above the host, exactly as it does for the
            /// first world, so the destination's publisher and callback gate are real instances of the same types.
            /// </summary>
            private WorldChain? AttachToRecoveredWorld(UnityWorldHost host, PipelineDescriptorReport descriptorReport)
            {
                if (descriptorReport.Descriptor == null || descriptorReport.Adaptation == null)
                {
                    lastFailure = "the source's compile report cannot describe the destination's assembly surface";
                    return null;
                }

                var registry = new TargetRegistry(host.World, 32);
                var publisher = new AssemblyPublisher(
                    host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptorReport.Descriptor!);
                var targets = new LiveTargetIndex(publisher.Recipes);
                var seeder = new LiveTargetSeeder(host, registry, targets);
                IDerivationValueSource values = family.CreateValues();
                var manifestSource = new FamilyManifestSource(
                    new CatalogManifestSource(family.Catalog, family.Declarations), family.ExtraManifests);
                var validator = new DerivationModeSwitchValidator(values, () => TargetView(targets));
                CompositionHost lane = CompositionHost.CreateDefault(
                    host.World,
                    family.WorldRootScope,
                    manifestSource,
                    resources,
                    family.LaneSeed,
                    validator);
                var bridge = new WorldCompositionBridge(host, lane, publisher);
                var pipeline = new DerivedAssemblyPipeline(
                    host,
                    lane,
                    publisher,
                    targets,
                    seeder,
                    values,
                    null,
                    null,
                    publisher.Migrations,
                    new StagedResourceGate(StagedByteCeiling, family.Issuer, host.Faults),
                    DefaultBudget());
                var time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);

                var chain = new WorldChain(
                    "recovered", host, lane, publisher, registry, targets, seeder, pipeline, bridge, time,
                    descriptorReport);
                worlds.Add(chain);
                return chain;
            }

            private static IReadOnlyList<DerivationTarget> TargetView(LiveTargetIndex targets)
            {
                DerivationInputTargets view = targets.BuildDerivationTargets();
                return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
            }

            private static PlanBudget DefaultBudget() =>
                new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot);

            // ================================================================== the control lane and the publication

            /// <summary>
            /// The family's compiled ownership and schedule surface, computed once: the descriptor belongs to the
            /// family's declarations, not to one world, so every world of this run is created against the same
            /// validated compile (GC-007, GC-009).
            /// </summary>
            private PipelineDescriptorReport CompileOnce() => compiled ??= family.CompilePipeline();

            /// <summary>Admits one edit and drains it across the publication boundary, without deriving for it.</summary>
            private bool SubmitAndDrain(
                WorldChain chain,
                CompositionEditPayload payload,
                string label,
                OperationId operation,
                out string failure)
            {
                failure = string.Empty;
                EditAdmission admission = chain.Lane.SubmitEdit(payload, operation, chain.Lane.Committed.Revision);
                if (!admission.Staged)
                {
                    failure = label + ": the lane refused the edit (" + admission.Kind + "/" + admission.Code + ")";
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = chain.Lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    failure = label + ": the publication was refused ("
                        + (published.Count > 0
                            ? published[0].Outcome.ToString() + "/" + published[0].Code
                            : "none")
                        + ")";
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Builds one plan the runner itself publishes, for the composition publication the lane has already
            /// committed. Two facts force this shape rather than `PublishDerived`:
            ///
            ///   * a prewrite refusal leaves the composition publication *adopted and pending* — the publisher
            ///     records the adoption before it reaches any boundary and only clears it when a publication
            ///     commits — so the pair can never be adopted a second time (P-006: one number, one assembly). The
            ///     real chain still supplies the proposal: `DerivedAssemblyPipeline.Derive` translates the lane's
            ///     committed state and then refuses the *already-pending* pair at its own adoption step, so
            ///     `report.Proposal` is the module's own proposal and nothing is re-derived here;
            ///   * real staged leases exist only when a caller stages them: `InertAcquisitionSet.TryAcquire` is the
            ///     API GC-008 publishes for a plan's inert acquisitions, and the plan resource gate is the world's
            ///     own `StagedResourceGate`, constructed with the world's fault latch so one arm covers the whole
            ///     apply boundary. The two observations whose subject is staged work need that.
            ///
            /// Everything else is the production module: `AssemblyPlanner.Build` prepares the plan and
            /// `AssemblyPublisher.Publish` applies it, exactly as `W4GateScenario.PublishPolicyCase` drives a
            /// publication the runner owns. The caller arms the latch *after* the leases are staged, so "the staged
            /// work was released" is always a statement about work that really existed when the refusal ran (P-029).
            /// </summary>
            private PlannedPublication? BuildThePendingPlan(
                WorldChain chain,
                OperationId operation,
                int stagedLeases,
                out PlanStaging staging)
            {
                staging = default(PlanStaging);

                DerivedAssemblyReport derived = chain.Pipeline.Derive(operation);
                NotePendingProposal(chain, derived);

                DerivationProposalReport? proposal = derived.Proposal ?? PendingProposalFor(chain);
                if (proposal == null || proposal.Proposal == null)
                {
                    lastFailure = "the real chain holds no proposal for the pending composition publication: "
                        + derived.Describe();
                    return null;
                }

                return BuildPlan(chain, operation, proposal.Proposal, stagedLeases, out staging);
            }

            /// <summary>
            /// Records the real pipeline's own proposal for a composition publication this world has adopted and not
            /// yet answered with an assembly. `DerivedAssemblyPipeline.Derive` reports `NoTargetChange` with a null
            /// `Proposal` once the committed composition is unchanged, and a prewrite refusal deliberately leaves the
            /// pair adopted-and-pending, so the runner's own plan for that pair must be built from the module's own
            /// proposal: nothing is re-derived and no second interpretation of the composition is introduced
            /// (P-002, P-006). Every site that can leave a pair pending calls this, so the two cannot drift.
            /// </summary>
            private static void NotePendingProposal(WorldChain chain, DerivedAssemblyReport derived)
            {
                if (derived != null && derived.Proposal != null && derived.Proposal.Proposal != null)
                {
                    chain.PendingProposal = derived.Proposal;
                }
            }

            /// <summary>
            /// The cached proposal, but only while it still describes the composition the world publishes now. A
            /// cached proposal whose base pair has moved on would make the planner reject `StalePlan` rather than
            /// publish a stale assembly, which is honest but useless as evidence, so this returns null and the
            /// caller fails loudly with the reason (P-006, P-028).
            /// </summary>
            private static DerivationProposalReport? PendingProposalFor(WorldChain chain)
            {
                DerivationProposalReport? proposal = chain.PendingProposal;
                if (proposal == null || proposal.Proposal == null)
                {
                    return null;
                }

                // `CompositionProposal` states the pair it was derived against as (`ExpectedRevision`, `BaseEpoch`).
                if (!proposal.Proposal.BaseEpoch.Equals(chain.Host.CurrentEpoch)
                    || !proposal.Proposal.ExpectedRevision.Equals(chain.Publisher.PublishedRevision))
                {
                    return null;
                }

                return proposal;
            }

            /// <summary>
            /// Builds one plan for the runner's own publication and stages its resources through the world's gate.
            /// The acquisition set is filled before the plan is published and the gate is the world's, so a refusal
            /// that reaches the cleanup boundary releases exactly these leases in reverse acquisition order (P-029).
            /// </summary>
            private PlannedPublication? BuildPlan(
                WorldChain chain,
                OperationId operation,
                PlanningCompositionProposal proposal,
                int stagedLeases,
                out PlanStaging staging)
            {
                staging = default(PlanStaging);
                var gate = new StagedResourceGate(StagedByteCeiling, family.Issuer, chain.Host.Faults);
                var acquisitions = new InertAcquisitionSet(gate, operation);
                var leaseIds = new List<Id128>(stagedLeases);
                for (int i = 0; i < stagedLeases; i++)
                {
                    var resource = new ResourceKey(new Id128(PlanResourceNamespace, 0x5150UL + (ulong)i));
                    if (!acquisitions.TryAcquire(resource, 64UL, null, out DiagnosticCode acquireCode))
                    {
                        lastFailure = "the plan's resource gate refused staged lease " + i.ToString(CultureInfo.InvariantCulture)
                            + ": " + acquireCode;
                        return null;
                    }

                    leaseIds.Add(acquisitions.Leases[acquisitions.Count - 1].LeaseId);
                }

                PlannedPublication planned = AssemblyPlanner.Build(
                    proposal,
                    chain.Publisher.Descriptor,
                    chain.Publisher.PublishedRevision,
                    chain.Host.CurrentEpoch,
                    chain.Publisher.Published.Bindings,
                    chain.Publisher.Published.Rules,
                    chain.Targets.PlannerTargets(),
                    chain.Seeder.ReadLiveSlots(TargetIds(chain)),
                    chain.Publisher.Migrations,
                    new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot),
                    acquisitions,
                    DefaultBudget());

                staging = new PlanStaging(planned, gate, acquisitions, leaseIds);
                if (planned.IsRejected)
                {
                    lastFailure = "the plan was rejected: " + planned.State.Code + ": " + planned.State.Detail;
                    return null;
                }

                return planned;
            }

            /// <summary>Stages one managed lease of the control lane behind a closed gate (P-029).</summary>
            private bool StageOneLaneLease(WorldChain chain, OperationId operation, out string failure)
            {
                failure = string.Empty;
                var resource = new ResourceKey(new Id128(PlanResourceNamespace, 0xC0170100UL));
                if (!chain.Lane.StageResource(
                        operation,
                        family.UnloadInstall,
                        resource,
                        new FrozenPayload(Array.Empty<byte>()),
                        null,
                        out DiagnosticCode code))
                {
                    failure = "the lane refused to stage a resource for its own publication: " + code;
                    return false;
                }

                return true;
            }

            private static IReadOnlyList<TargetId> TargetIds(WorldChain chain)
            {
                IReadOnlyList<LiveTarget> live = chain.Targets.Targets;
                var ids = new List<TargetId>(live.Count);
                for (int i = 0; i < live.Count; i++)
                {
                    ids.Add(live[i].Target);
                }

                return ids;
            }

            /// <summary>
            /// The invariant a prewrite refusal leaves behind: the composition publication was adopted and is still
            /// pending, so the lane is exactly one publication ahead of the world and the world published nothing.
            /// `MatchesPublishedAssembly` is the wrong check here — it is true only after an assembly committed, and
            /// an uncommitted pair is deliberately not that (P-006, P-029).
            /// </summary>
            private static bool PendingRefusalHeld(WorldChain chain, AssemblyEpoch worldEpochBefore) =>
                chain.Publisher.HasAdoptedPublication
                && chain.Host.CurrentEpoch.Equals(worldEpochBefore)
                && chain.Publisher.Published.Epoch.Equals(worldEpochBefore)
                && chain.Lane.Committed.Epoch.Value == worldEpochBefore.Value + 1UL
                && chain.Lane.Committed.Revision.Value == chain.Lane.Committed.Epoch.Value;

            private bool MatchesPublishedAssembly(WorldChain chain) =>
                AssemblyPublisher.MatchesPublishedAssembly(
                    chain.Lane.Committed.Revision,
                    chain.Lane.Committed.Epoch,
                    chain.Publisher.PublishedRevision,
                    chain.Host.CurrentEpoch);

            private static bool ReadSlot(WorldChain chain, StateSlotKey slot, out int value, out uint version)
            {
                value = 0;
                version = 0;
                IReadOnlyList<TargetSlotState> rows = chain.Publisher.ReadSlotStates(slot.Target);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Owner.Equals(slot.Owner) && rows[i].Slot.Equals(slot.Slot))
                    {
                        value = rows[i].Value;
                        version = rows[i].SchemaVersion;
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// The migration this revision really declares, if it declares one: the first descriptor slot that
            /// carries a version-change policy key the world's own migration registry resolves to a registered
            /// handler (P-032, P-029). The handler's source version is the version the plan migrates *from*, and
            /// the current live value of that slot is the value the migration must leave untouched. A family whose
            /// revision declares no migration gets <see cref="DeclaredMigration.None"/>, and the migration
            /// observation then proves the boundary at its real position without claiming a migration was staged.
            /// </summary>
            private static DeclaredMigration FindDeclaredMigration(WorldChain chain)
            {
                IReadOnlyList<OwnedSlotSpec> slots = chain.Publisher.Descriptor.Slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    OwnedSlotSpec spec = slots[i];
                    if (!spec.HasVersionChangePolicy
                        || !chain.Publisher.Migrations.TryFind(spec.VersionChangePolicy, out ISlotMigration? handler)
                        || handler == null)
                    {
                        continue;
                    }

                    IReadOnlyList<LiveTarget> live = chain.Targets.Targets;
                    for (int t = 0; t < live.Count; t++)
                    {
                        StateSlotKey candidate = new StateSlotKey(live[t].Target, spec.Owner, spec.Slot);
                        if (ReadSlot(chain, candidate, out int value, out uint _))
                        {
                            return new DeclaredMigration(candidate, value, handler.FromVersion, spec.Schema.Version);
                        }
                    }
                }

                return DeclaredMigration.None;
            }

            /// <summary>Whether two live-slot snapshots hold the same rows — same key, version and value.</summary>
            private static bool LiveSlotsEqual(IReadOnlyList<LiveSlotState> before, IReadOnlyList<LiveSlotState> after)
            {
                if (before.Count != after.Count)
                {
                    return false;
                }

                for (int i = 0; i < before.Count; i++)
                {
                    bool found = false;
                    for (int j = 0; j < after.Count; j++)
                    {
                        if (before[i].Slot.Equals(after[j].Slot)
                            && before[i].SchemaVersion == after[j].SchemaVersion
                            && before[i].Value == after[j].Value)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        return false;
                    }
                }

                return true;
            }

            private static ulong PumpIdleFrames(WorldChain chain)
            {
                ulong committed = 0UL;
                for (int i = 0; i < IdlePumpFrames; i++)
                {
                    committed += chain.Time.PumpFrame(IdlePumpTicks).StepsCommitted;
                }

                return committed;
            }

            private static ulong PumpOneFrame(WorldChain chain)
            {
                chain.Host.NotifyCommandAdmitted(1U);
                return chain.Time.PumpFrame(IdlePumpTicks).StepsCommitted;
            }

            /// <summary>
            /// Epoch, revision, row-count and token consistency of one captured read-only observer, re-checked by
            /// every prewrite observation: a view a reader captured must still describe the published assembly (P-030).
            /// </summary>
            private static bool ObserverConsistent(WorldChain chain, PublishedWorldView view, out string detail)
            {
                PublishedWorldView now = chain.Publisher.Published;
                bool ok = view.Epoch.Equals(chain.Host.CurrentEpoch)
                    && view.Revision.Equals(chain.Publisher.PublishedRevision)
                    && view.Token.World.Session.Equals(chain.World.Session)
                    && view.Epoch.Equals(now.Epoch)
                    && view.Revision.Equals(now.Revision)
                    && view.BindingRowCount == now.BindingRowCount;
                detail = "observerEpoch=" + view.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + "; hostEpoch=" + chain.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + "; observerRevision=" + view.Revision.Value.ToString(CultureInfo.InvariantCulture)
                    + "; publisherRevision=" + chain.Publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "; observerRows=" + view.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                    + "; publishedRows=" + now.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                    + "; observerOwnsWorld=" + view.Token.World.Session.Equals(chain.World.Session);
                return ok;
            }

            /// <summary>
            /// Whether the observer taken in row 1 is still the published assembly: same reference, same epoch,
            /// same binding rows. A later publication makes it false, which is exactly what P-030's one switched
            /// pointer means for a reader that captured one.
            /// </summary>
            private bool MatchesObserver(WorldChain chain, out string detail)
            {
                PublishedWorldView? captured = observerView;
                if (captured == null)
                {
                    detail = "observer=<none>";
                    return false;
                }

                bool consistent = ObserverConsistent(chain, captured, out detail);
                bool sameReference = ReferenceEquals(captured, chain.Publisher.Published);
                detail = detail + "; observerIsPublished=" + sameReference;
                return consistent && sameReference;
            }

            // ================================================================== plumbing

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }

            private void Add(string bareName, bool passed, string detail)
            {
                lastFailure = string.Empty;
                steps.Add(new FaultScenarioStep(family.Label + "/" + bareName, passed, detail ?? string.Empty));
            }

            private string DescribeFailure()
                => lastFailure.Length == 0 ? string.Empty : "; failure=" + lastFailure;

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;

            private static string DescribePublication(AssemblyPublicationReport? report)
                => report == null
                    ? "<none>"
                    : report.Outcome + "(" + report.Code + ")/writes="
                        + report.StructuralWrites.ToString(CultureInfo.InvariantCulture);

            /// <summary>
            /// The injected record of one boundary, verbatim: `boundary`, `op`, `plan` and `fired` are TEST-016 row
            /// 2's required provenance, and an evidence file greps them (P-052).
            /// </summary>
            private static string TraceLineOf(IReadOnlyList<FaultRecord> records)
            {
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    if (records[i].Injected)
                    {
                        return records[i].ToLine();
                    }
                }

                return "<none>";
            }

            private static string Join(IReadOnlyList<string> values)
            {
                if (values.Count == 0)
                {
                    return "<none>";
                }

                return string.Join(",", (IEnumerable<string>)values);
            }
        }

        /// <summary>
        /// One world's real chain of §5, held together so teardown can stop and dispose it and so a step can read the
        /// module reports of the same world. Every member is a production type; nothing here is a model of one.
        /// </summary>
        private sealed class WorldChain
        {
            public WorldChain(
                string label,
                UnityWorldHost host,
                CompositionHost lane,
                AssemblyPublisher publisher,
                TargetRegistry registry,
                LiveTargetIndex targets,
                LiveTargetSeeder seeder,
                DerivedAssemblyPipeline pipeline,
                WorldCompositionBridge bridge,
                WorldTimeDriver time,
                PipelineDescriptorReport descriptorReport)
            {
                Label = label;
                Host = host;
                Lane = lane;
                Publisher = publisher;
                Registry = registry;
                Targets = targets;
                Seeder = seeder;
                Pipeline = pipeline;
                Bridge = bridge;
                Time = time;
                DescriptorReport = descriptorReport;
            }

            public string Label { get; }

            public UnityWorldHost Host { get; }

            public WorldId World => Host.World;

            public UnityWorldRegistration Registration => Host.Registration;

            public CompositionHost Lane { get; }

            public AssemblyPublisher Publisher { get; }

            public TargetRegistry Registry { get; }

            public LiveTargetIndex Targets { get; }

            public LiveTargetSeeder Seeder { get; }

            public DerivedAssemblyPipeline Pipeline { get; }

            public WorldCompositionBridge Bridge { get; }

            public WorldTimeDriver Time { get; }

            public PipelineDescriptorReport DescriptorReport { get; }

            /// <summary>
            /// The last proposal the real pipeline's own `Derive` produced for a composition publication this world
            /// has adopted and not yet answered with an assembly. It is kept because `DerivedAssemblyPipeline.Derive`
            /// reports `NoTargetChange` with a null `Proposal` once the committed composition is unchanged, and a
            /// prewrite refusal deliberately leaves the pair adopted-and-pending: the runner's own plan for that
            /// pair must be built from the module's own proposal, so nothing is re-derived and no second
            /// interpretation of the composition is introduced (P-002, P-006).
            /// </summary>
            public DerivationProposalReport? PendingProposal { get; set; }
        }

        /// <summary>
        /// One runner-built plan with the staged acquisitions it was published with, so a step asserts what happened
        /// to the staged work by identity rather than by intent (P-029, P-048).
        /// </summary>
        private readonly struct PlanStaging
        {
            public PlanStaging(
                PlannedPublication? plan,
                StagedResourceGate? gate,
                InertAcquisitionSet? acquisitions,
                IReadOnlyList<Id128> leaseIds)
            {
                Plan = plan;
                Gate = gate;
                Acquisitions = acquisitions;
                LeaseIds = leaseIds ?? Array.Empty<Id128>();
            }

            public PlannedPublication? Plan { get; }

            public StagedResourceGate? Gate { get; }

            public InertAcquisitionSet? Acquisitions { get; }

            public IReadOnlyList<Id128> LeaseIds { get; }
        }

        /// <summary>
        /// One migration this revision really declares: the live slot the plan migrates, the value that live row
        /// holds, and the version pair of its registered handler (`from` the plan migrates out of, `to` the
        /// descriptor declares) (P-029, P-032). <see cref="None"/> is the honest answer for a revision that
        /// declares no version-change policy at all.
        /// </summary>
        private readonly struct DeclaredMigration
        {
            public DeclaredMigration(StateSlotKey slot, int value, uint fromVersion, uint toVersion)
            {
                Slot = slot;
                Value = value;
                FromVersion = fromVersion;
                ToVersion = toVersion;
                Found = true;
            }

            /// <summary>The "no migration is declared" value; `default` already has `Found == false`.</summary>
            public static DeclaredMigration None => default;

            public bool Found { get; }

            public StateSlotKey Slot { get; }

            public int Value { get; }

            public uint FromVersion { get; }

            public uint ToVersion { get; }
        }

        /// <summary>
        /// The lane's manifest source: the catalog's own declarations plus the family's extra manifests (the
        /// lifecycle pair, the compatible provider, the unload installation and the state-policy provider). A
        /// manifest added here resolves for a mount of its plugin type exactly as a catalog declaration does (P-009).
        /// It is `W4GateScenario`'s private source re-declared here because that type is private to the gate.
        /// </summary>
        private sealed class FamilyManifestSource : IPluginManifestSource
        {
            private readonly CatalogManifestSource catalog;
            private readonly Dictionary<Id128, PluginManifest> added = new Dictionary<Id128, PluginManifest>();

            public FamilyManifestSource(CatalogManifestSource catalog, IReadOnlyList<PluginManifest> extra)
            {
                this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                if (extra == null)
                {
                    throw new ArgumentNullException(nameof(extra));
                }

                for (int i = 0; i < extra.Count; i++)
                {
                    if (extra[i] != null)
                    {
                        added[extra[i].PluginTypeId.Value] = extra[i];
                    }
                }
            }

            public bool TryGetManifest(PluginTypeId pluginType, out PluginManifest? manifest)
            {
                if (added.TryGetValue(pluginType.Value, out PluginManifest? found) && found != null)
                {
                    manifest = found;
                    return true;
                }

                return catalog.TryGetManifest(pluginType, out manifest);
            }

            public bool TryGetConfigDefaults(SchemaRef schema, out ConfigDocument? defaults) =>
                catalog.TryGetConfigDefaults(schema, out defaults);
        }

        /// <summary>
        /// The run's managed-resource factory: counted preparations and recorded disposals, so a step can ask how
        /// many leases the lane really prepared and how many were released (P-007, P-048).
        /// </summary>
        private sealed class RunResources : IManagedResourceFactory
        {
            private readonly IdSequence ids;

            public RunResources(IdSequence ids)
            {
                this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            }

            public int PrepareCount { get; private set; }

            public int DisposeCount { get; private set; }

            public FactoryKey DisposerKey { get; } = new FactoryKey(new Id128(0x4730313752455346UL, 1UL), 1U);

            public IManagedResourceLease Prepare(ManagedResourceRequest request)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                PrepareCount++;
                return new ManagedResourceLease(
                    request.Resource,
                    ids.Next(),
                    request.Token,
                    DisposerKey,
                    new ManagedResourceGate(),
                    _ => DisposeCount++);
            }
        }
    }
}
