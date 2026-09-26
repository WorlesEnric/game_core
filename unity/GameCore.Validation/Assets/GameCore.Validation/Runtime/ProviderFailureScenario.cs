// GameCore.Validation.ProbeHost — the P-012 unexpected-provider-failure observations of the GC-017 fault run.
//
// The two observations this file records are appended to `FaultScenario.ObservationNames` at the end of the frozen
// sequence, after `gc017-old-callback-after-recovery-is-rejected` and before `gc017-teardown-settles-and-disposes`,
// and they execute in exactly that position (`FaultScenario.Executor.Run` calls this file's `Run` between the
// recovery rows and teardown). The appended names are the whole of P-012's requirement, and they are one pair:
//
//   P-012 (a) the safe dependency-closure deactivation published
//                                                 gc017-provider-failure-publishes-a-safe-deactivation
//   P-012 (b) the deactivation could not publish, so the world faulted
//                                                 gc017-provider-failure-that-cannot-publish-faults-the-world
//
// Why this is a host of its own rather than two more steps of `FaultScenario.Executor`: both observations need a
// world whose required-provider pair is Active and whose composition is otherwise quiescent, and branch (b) leaves
// its world `Faulted`, which is terminal (P-031). The fault scenario's own worlds are already spoken for — the
// primary world carries an adopted-and-pending composition publication after its prewrite refusals, and the
// postwrite world is the recovery row's source — so each branch stands up its own world through the same chain of
// section 5 that `FaultScenario.StandUpWorld` builds (registry, descriptor, publisher, lane, pipeline, time driver)
// with the one type no other row needs: the `LifecycleController`, the seam P-012's provider failure reaches the
// world through. Nothing here is a model of a kernel module and nothing is discovered reflectively.
//
// Normative anchors: P-002 (one host per world, created through the registry), P-004 (a `WorldId` is a fresh
// session, never reused), P-005 (destroy/recreate invalidates handles even when a stable ID is restored), P-006
// (one number, one assembly: the deactivation is one validated publication at one serialized commit), P-011 (the
// consumer's required dependency is a real `ServiceDependency` resolved by the real resolver), P-012 (unexpected
// provider failure: the safe dependency-closure deactivation, or fail-stop because it could not publish; never a
// timeout-based unsafe release), P-029 (a refusal releases its staged work and leaves the old assembly), P-030
// (publication switches the assembly at one commit), P-031 (a failure after the first live write faults the world:
// admission stays closed, no epoch or image publishes, no simulation resumes), P-033 (the derived assembly carries
// the retraction), P-035 (a created world is `Running` only after its initial validated publication), P-046
// (`Active -> Failed` is the permitted transition), P-047/P-048 (a faulted world's resources stay retained until it
// is stopped) and P-049 (recovery creates a new `WorldId` from verified initial definitions; the source is never
// resumed and old handles never become valid).
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
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Lifecycle;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Recovery;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The two P-012 provider-failure observations of the GC-017 fault run, over one family and one catalog. Both
    /// stand their own world up through the real chain of section 5 and drive the failure through the world's own
    /// <see cref="LifecycleController"/>, so what they report is what the production path did rather than what this
    /// file intended.
    /// </summary>
    public static class ProviderFailureScenario
    {
        /// <summary>P-012's first half: the safe dependency-closure deactivation published (frozen bare name).</summary>
        public const string PublishedDeactivationName = "gc017-provider-failure-publishes-a-safe-deactivation";

        /// <summary>P-012's second half: the deactivation could not publish, so the world faulted.</summary>
        public const string FaultedWorldName = "gc017-provider-failure-that-cannot-publish-faults-the-world";

        /// <summary>
        /// Runs both observations for one family and returns them in execution order, as bare names: the caller
        /// qualifies them with the family label exactly as it qualifies every other observation of the run.
        /// </summary>
        public static IReadOnlyList<FaultScenarioStep> Run(IW4GateFamily family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Runner(family).Run();
        }

        /// <summary>
        /// One run's state: the family, the id sequences, the world chain of the current observation and the two
        /// recorded verdicts. It owns every world it creates, and it closes them all before it returns (P-047).
        /// </summary>
        private sealed class Runner
        {
            /// <summary>Staged lease ceiling of a plan resource gate, in bytes (P-022).</summary>
            private const ulong StagedByteCeiling = 1024UL * 1024UL;

            private const ulong PrepareBytesLimit = 1024UL * 1024UL;

            private const ulong ScratchCapacityBytes = 4096UL;

            private const ulong ScratchBytesPerSlot = 64UL;

            /// <summary>Ticks one idle frame is pumped with, so a refused pump is a refusal and not a no-op.</summary>
            private const ulong IdlePumpTicks = 1000000UL;

            private const uint TargetRegistryCapacity = 32U;

            /// <summary>
            /// Session-id salt of this host, so its worlds can never collide with the fault scenario's own worlds:
            /// `FaultScenario` sequences sessions from `family.SessionSalt` and would hand this host a session id it
            /// already owns, which `UnityWorldRegistry.TryCreate` answers with the live host instead of a new world.
            /// </summary>
            private const ulong SessionSaltMask = 0x4730313750464149UL;

            /// <summary>Namespace of the resource factory's lease ids and disposer key, distinct from every other.</summary>
            private const ulong ResourceSaltMask = 0x4730313750465245UL;

            private readonly IW4GateFamily family;
            private readonly List<FaultScenarioStep> steps = new List<FaultScenarioStep>(2);
            private readonly List<Rig> rigs = new List<Rig>();
            private readonly IdSequence sessionSequence;
            private readonly ProviderFailureResources resources;

            private PipelineDescriptorReport? compiled;
            private ulong operationSequence;
            private string lastFailure = string.Empty;

            public Runner(IW4GateFamily family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt ^ SessionSaltMask);
                resources = new ProviderFailureResources(new IdSequence(family.SessionSalt ^ ResourceSaltMask));
            }

            public IReadOnlyList<FaultScenarioStep> Run()
            {
                Observe(PublishedDeactivationName, PublishesASafeDeactivation);
                Observe(FaultedWorldName, FaultsTheWorldWhenTheDeactivationCannotPublish);
                TearDownRigs();
                return steps;
            }

            // ================================================================== (a) the deactivation published

            /// <summary>
            /// P-012's first half, in full. A world is created and its required-provider pair mounted, so the
            /// consumer really satisfied a required `ServiceDependency` against a real provider before the failure
            /// (P-011). `LifecycleController.FailProvider` is then called on the ACTIVE provider with the latch
            /// disarmed and the failure is asked to publish the safe dependency-closure deactivation:
            ///
            ///   * the failed installation is `Failed` in the committed composition, it holds no authority and its
            ///     attributed rows are gone — this is the sentence under test, "an already-live provider cannot be
            ///     kept active" (P-012, P-046);
            ///   * its consumer is `WaitingForDependencies` with its contribution retracted, and the deactivation's
            ///     own report names exactly it (P-012, P-033);
            ///   * an unrelated installation that required neither stayed `Active` and is not named as waiting, so
            ///     the deactivation is a dependency closure and not a world-wide stop;
            ///   * the world is still `Running`, still pumps frames, and its assembly epoch advanced to the lane's
            ///     committed epoch — the publication happened and the world did not fault (P-006, P-030).
            /// </summary>
            private void PublishesASafeDeactivation()
            {
                const string name = PublishedDeactivationName;
                Rig? chain = StandUp("provider-failure-deactivation", NextOperation(default(WorldId)));
                if (chain == null)
                {
                    Record(name, false, "the world could not be stood up: " + lastFailure);
                    return;
                }

                if (!MountTheRequiredPair(chain, out string mountFailure))
                {
                    Record(name, false, mountFailure);
                    return;
                }

                PluginInstanceId failedProvider = family.RequiredProviderInstall;
                PluginInstanceId consumer = family.RequiredConsumerInstall;
                PluginInstanceId unrelated = family.LifecycleProviderInstall;

                AssemblyEpoch epochBefore = chain.Host.CurrentEpoch;
                int faultCountBefore = chain.Host.FaultCount;
                string providerStateBefore = StateOf(chain, failedProvider);
                string consumerStateBefore = StateOf(chain, consumer);
                string unrelatedStateBefore = StateOf(chain, unrelated);
                int providerRowsBefore = AttributedRows(chain, failedProvider);
                int consumerRowsBefore = AttributedRows(chain, consumer);
                int consumerBindingsBefore = BindingCount(chain, consumer);

                ProviderFailureOutcome outcome = chain.Controller.FailProvider(
                    failedProvider,
                    NextOperation(chain.World),
                    DiagnosticCode.ProviderFailed,
                    "P-012: the live provider failed unexpectedly");

                string providerStateAfter = StateOf(chain, failedProvider);
                string consumerStateAfter = StateOf(chain, consumer);
                string unrelatedStateAfter = StateOf(chain, unrelated);
                int providerRowsAfter = AttributedRows(chain, failedProvider);
                int consumerRowsAfter = AttributedRows(chain, consumer);
                int consumerBindingsAfter = BindingCount(chain, consumer);

                // "The failed provider is NOT kept active", read from the modules rather than asserted as a boolean:
                // the committed state is `Failed`, the coordinator no longer holds its authority, and every binding
                // row it attributed is gone from the published assembly (P-012, P-046).
                bool failedProviderLeft = providerStateBefore == InstallationState.Active.ToString()
                    && providerStateAfter == InstallationState.Failed.ToString()
                    && !chain.Lane.Lifecycle.HoldsAuthority(failedProvider)
                    && providerRowsBefore > 0
                    && providerRowsAfter == 0;

                bool consumerWaits = consumerStateBefore == InstallationState.Active.ToString()
                    && consumerStateAfter == InstallationState.WaitingForDependencies.ToString()
                    && consumerRowsBefore > 0
                    && consumerRowsAfter == 0
                    && consumerBindingsBefore > 0
                    && consumerBindingsAfter == 0
                    && NamesExactly(outcome.WaitingConsumers, consumer);

                bool unrelatedKept = unrelatedStateBefore == InstallationState.Active.ToString()
                    && unrelatedStateAfter == InstallationState.Active.ToString()
                    && !Names(outcome.WaitingConsumers, unrelated);

                bool onePublicationJoined = chain.Host.CurrentEpoch.Value > epochBefore.Value
                    && chain.Host.CurrentEpoch.Equals(chain.Lane.Committed.Epoch)
                    && MatchesPublishedAssembly(chain)
                    && outcome.Deactivation.Token != null
                    && outcome.Derived != null
                    && outcome.Derived.Outcome != DerivedAssemblyOutcome.Refused;

                // The falsifying counterpart of branch (b)'s "no simulation resumes": this world still pumps a frame
                // after the deactivation, because nothing faulted.
                WorldPumpResult pump = chain.Host.PumpFrame(IdlePumpTicks);

                bool pass = outcome.Deactivated
                    && !outcome.WorldFaulted
                    && outcome.Code == DiagnosticCode.None
                    && outcome.Deactivation.Deactivated
                    && outcome.Deactivation.Code == DiagnosticCode.None
                    && chain.Controller.ProviderFailureCount == 1
                    && chain.Controller.FaultedProviderCount == 0
                    && chain.Host.Lifecycle == WorldLifecycleState.Running
                    && chain.Host.FaultCount == faultCountBefore
                    && failedProviderLeft
                    && consumerWaits
                    && unrelatedKept
                    && onePublicationJoined
                    && pump.Pumped;

                Record(name, pass,
                    "failedProviderState=" + providerStateBefore + "->" + providerStateAfter
                    + "; holdsAuthority=" + chain.Lane.Lifecycle.HoldsAuthority(failedProvider)
                    + "; providerRows=" + providerRowsBefore.ToString(CultureInfo.InvariantCulture)
                    + "->" + providerRowsAfter.ToString(CultureInfo.InvariantCulture)
                    + "; consumerState=" + consumerStateBefore + "->" + consumerStateAfter
                    + "; consumerRows=" + consumerRowsBefore.ToString(CultureInfo.InvariantCulture)
                    + "->" + consumerRowsAfter.ToString(CultureInfo.InvariantCulture)
                    + "; consumerBindings=" + consumerBindingsBefore.ToString(CultureInfo.InvariantCulture)
                    + "->" + consumerBindingsAfter.ToString(CultureInfo.InvariantCulture)
                    + "; waitingConsumers=" + Describe(outcome.WaitingConsumers)
                    + "; namedExactly=" + NamesExactly(outcome.WaitingConsumers, consumer)
                    + "; unrelatedState=" + unrelatedStateBefore + "->" + unrelatedStateAfter
                    + "; unrelatedNamed=" + Names(outcome.WaitingConsumers, unrelated)
                    + "; deactivated=" + outcome.Deactivated
                    + "; worldFaulted=" + outcome.WorldFaulted
                    + "; outcomeCode=" + outcome.Code
                    + "; deactivationCode=" + outcome.Deactivation.Code
                    + "; deactivationToken=" + (outcome.Deactivation.Token != null ? "set" : "<null>")
                    + "; derived=" + (outcome.Derived != null ? outcome.Derived.Outcome.ToString() : "<none>")
                    + "; epoch=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                    + "->" + chain.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + "; worldState=" + chain.Host.Lifecycle
                    + "; pumpAccepted=" + pump.Pumped
                    + "; faultCount=" + chain.Host.FaultCount.ToString(CultureInfo.InvariantCulture)
                    + "; providerFailures=" + chain.Controller.ProviderFailureCount.ToString(CultureInfo.InvariantCulture)
                    + "; faultedProviderFailures=" + chain.Controller.FaultedProviderCount.ToString(CultureInfo.InvariantCulture));
            }

            // ================================================================== (b) the deactivation could not publish

            /// <summary>
            /// P-012's second half, in full. A second, independent world is created and its required-provider pair
            /// mounted, and then `FaultBoundary.ProviderDeactivationPublication` is armed on that world's own latch
            /// before `LifecycleController.FailProvider` is called: the deactivation attempt itself is the boundary,
            /// so an armed latch means "this publication cannot happen" and the fail-stop half must apply.
            ///
            /// The observation asserts the terminal state (`Faulted`, the fault code the controller passed, exactly
            /// one fault), that nothing published because of the failure (the committed composition revision and the
            /// assembly epoch and image count are all unchanged and no publication was adopted), that admission is
            /// really closed (a command submitted through the host's own ingress is refused with the fault code and
            /// creates no request-ledger row, and a pumped frame is refused), that the old world never resumes, and
            /// that the checkpoint capture + restore into a NEW session still works, through the same
            /// `InitialDefinitionRecovery` entry point the recovery row uses rather than a second restore path
            /// (P-029, P-030, P-031, P-049).
            /// </summary>
            private void FaultsTheWorldWhenTheDeactivationCannotPublish()
            {
                const string name = FaultedWorldName;
                Rig? chain = StandUp("provider-failure-fault", NextOperation(default(WorldId)));
                if (chain == null)
                {
                    Record(name, false, "the world could not be stood up: " + lastFailure);
                    return;
                }

                if (!MountTheRequiredPair(chain, out string mountFailure))
                {
                    Record(name, false, mountFailure);
                    return;
                }

                PluginInstanceId failedProvider = family.RequiredProviderInstall;

                AssemblyFaultInjection faults = chain.Host.Faults;
                faults.Arm(FaultBoundary.ProviderDeactivationPublication);
                bool armedAtReach = faults.IsArmed(FaultBoundary.ProviderDeactivationPublication);

                AssemblyEpoch epochBefore = chain.Host.CurrentEpoch;
                CompositionRevision revisionBefore = chain.Lane.Committed.Revision;
                int imagesBefore = chain.Host.Publications.PublishedCount;
                int faultCountBefore = chain.Host.FaultCount;
                bool adoptedBefore = chain.Publisher.HasAdoptedPublication;
                string providerStateBefore = StateOf(chain, failedProvider);
                int reachesBefore = faults.ReachCountOf(FaultBoundary.ProviderDeactivationPublication);

                ProviderFailureOutcome outcome = chain.Controller.FailProvider(
                    failedProvider,
                    NextOperation(chain.World),
                    DiagnosticCode.ProviderFailed,
                    "P-012: the live provider failed unexpectedly and its deactivation could not publish");

                faults.Disarm(FaultBoundary.ProviderDeactivationPublication);
                bool disarmed = !faults.IsArmed(FaultBoundary.ProviderDeactivationPublication);

                string providerStateAfter = StateOf(chain, failedProvider);
                int reaches = faults.ReachCountOf(FaultBoundary.ProviderDeactivationPublication) - reachesBefore;

                // "No epoch or image publishes": the committed composition, the world's epoch and the published
                // image count are all exactly what they were, and no composition publication was adopted for the
                // deactivation (P-030, P-031).
                bool nothingPublished = chain.Lane.Committed.Revision.Equals(revisionBefore)
                    && chain.Host.CurrentEpoch.Equals(epochBefore)
                    && chain.Host.Publications.PublishedCount == imagesBefore
                    && !chain.Publisher.HasAdoptedPublication
                    && !adoptedBefore
                    && providerStateAfter == InstallationState.Active.ToString();

                // Closed admission, through the host's own ingress. The route and schema are deliberately default:
                // `UnityWorldHost.Submit` decides on the lifecycle before it validates the envelope or resolves the
                // route, and a refusal that came from anywhere else would report a different code — the assertion
                // pins the fault's own code, and the request ledger proves no admission row was created for work the
                // world cannot run (P-031, P-042).
                WorldMessagePlane? plane = chain.Host.Messages;
                bool planePresent = plane != null;
                int requestRowsBefore = planePresent ? plane!.Requests.RowCount : -1;
                var envelope = new CommandEnvelope(
                    NextOperation(chain.World),
                    default(RouteId),
                    family.MovedTarget,
                    default(SchemaRef),
                    null,
                    new FrozenPayload(Array.Empty<byte>()));
                CommandAdmissionReceipt receipt = chain.Host.Submit(envelope);
                int requestRowsAfter = planePresent ? plane!.Requests.RowCount : -1;

                bool admissionClosed = planePresent
                    && !receipt.Admitted
                    && receipt.Result.Kind == RequestResultKind.Rejected
                    && receipt.Result.Reason == DiagnosticCode.ProviderFailed
                    && requestRowsAfter == requestRowsBefore;

                WorldPumpResult after = chain.Host.PumpFrame(IdlePumpTicks);
                bool neverResumes = !after.Pumped;

                bool restored = RecoverIntoANewSession(chain, out string restoreDetail, out string restoreFailure);

                bool pass = !outcome.Deactivated
                    && outcome.WorldFaulted
                    && outcome.Code == DiagnosticCode.ProviderFailed
                    && outcome.Deactivation.Deactivated == false
                    && chain.Host.Lifecycle == WorldLifecycleState.Faulted
                    && chain.Host.FaultCode == DiagnosticCode.ProviderFailed
                    && chain.Host.FaultDetail.Length > 0
                    && chain.Host.FaultCount == faultCountBefore + 1
                    && chain.Controller.ProviderFailureCount == 1
                    && chain.Controller.FaultedProviderCount == 1
                    && armedAtReach
                    && disarmed
                    && reaches == 1
                    && nothingPublished
                    && providerStateBefore == InstallationState.Active.ToString()
                    && admissionClosed
                    && neverResumes
                    && restored;

                Record(name, pass,
                    "armed=True"
                    + "; armedAtReach=" + armedAtReach
                    + "; disarmed=True"
                    + "; boundaryReaches=" + reaches.ToString(CultureInfo.InvariantCulture)
                    + "; deactivated=" + outcome.Deactivated
                    + "; worldFaulted=" + outcome.WorldFaulted
                    + "; outcomeCode=" + outcome.Code
                    + "; deactivationCode=" + outcome.Deactivation.Code
                    + "; failedProviderState=" + providerStateBefore + "->" + providerStateAfter
                    + "; worldState=" + chain.Host.Lifecycle
                    + "; hostFaultCode=" + chain.Host.FaultCode
                    + "; hostFaultDetail=" + chain.Host.FaultDetail
                    + "; faultCount=" + chain.Host.FaultCount.ToString(CultureInfo.InvariantCulture)
                    + " (was " + faultCountBefore.ToString(CultureInfo.InvariantCulture) + ")"
                    + "; providerFailures=" + chain.Controller.ProviderFailureCount.ToString(CultureInfo.InvariantCulture)
                    + "; faultedProviderFailures=" + chain.Controller.FaultedProviderCount.ToString(CultureInfo.InvariantCulture)
                    + "; laneRevision=" + revisionBefore.Value.ToString(CultureInfo.InvariantCulture)
                    + "; epoch=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                    + "; images=" + imagesBefore.ToString(CultureInfo.InvariantCulture)
                    + "; adoptedBefore=" + adoptedBefore
                    + "; adoptedAfter=" + chain.Publisher.HasAdoptedPublication
                    + "; planePresent=" + planePresent
                    + "; submitAdmitted=" + receipt.Admitted
                    + "; submitKind=" + receipt.Result.Kind
                    + "; submitReason=" + receipt.Result.Reason
                    + "; requestRows=" + requestRowsBefore.ToString(CultureInfo.InvariantCulture)
                    + "->" + requestRowsAfter.ToString(CultureInfo.InvariantCulture)
                    + "; pumpRefused=" + !after.Pumped
                    + "; " + restoreDetail
                    + (restored ? string.Empty : "; restoreFailure=" + restoreFailure));
            }

            /// <summary>
            /// The checkpoint capture + restore of branch (b), through the same `InitialDefinitionRecovery` entry
            /// point the recovery row uses. The destination is a caller-reserved fresh session, the source stays
            /// `Faulted` and is never resumed, and a handle minted by the source cannot be resolved by the
            /// destination's own registry even though the same stable identity is live there — a recovered world is
            /// a new incarnation (P-004, P-005, P-031, P-049).
            /// </summary>
            private bool RecoverIntoANewSession(Rig source, out string detail, out string failure)
            {
                detail = string.Empty;
                failure = string.Empty;
                Rig? destination = null;
                try
                {
                    WorldId destinationWorld = new WorldId(sessionSequence.Next());
                    OperationId operation = NextOperation(destinationWorld);
                    var request = new RecoveryRequest(
                        source.World,
                        destinationWorld,
                        source.Host.Request.Definition,
                        source.Host.Request.TemporalModel,
                        source.Host.Request.Mode,
                        source.Host.Request.CatalogHash,
                        operation,
                        source.Host.Request.FixedStep);

                    int registryBefore = UnityWorldRegistry.Count;
                    RecoveryReport report = InitialDefinitionRecovery.Recover(
                        request, family.CreateRegistration(source.DescriptorReport.Adaptation!), null);
                    int registryAfter = UnityWorldRegistry.Count;

                    if (report.DestinationHost == null)
                    {
                        failure = "recovery produced no destination: " + report.Describe();
                        detail = "recovered=False; " + report.Describe();
                        return false;
                    }

                    destination = Attach("provider-failure-recovered", report.DestinationHost!, source.DescriptorReport);
                    if (destination == null)
                    {
                        failure = "the recovered world could not be given its own assembly surface: " + lastFailure;
                        detail = "recovered=True; " + report.Describe();
                        return false;
                    }

                    if (!SeedTheWorld(destination))
                    {
                        failure = "the recovered world's declared targets were not seeded: " + lastFailure;
                        detail = "recovered=True; " + report.Describe();
                        return false;
                    }

                    bool minted = source.Registry.TryGetHandle(
                        source.Targets.Targets[0].Target, out TargetHandle handle);
                    bool identityLiveAtDestination = destination.Registry.IsLive(source.Targets.Targets[0].Target);
                    int staleBefore = destination.Registry.StaleRejectionCount;
                    bool resolvable = destination.Publisher.TryResolveHandle(
                        handle, out TargetId _, out global::Unity.Entities.Entity _);
                    bool refusedAsForeignIncarnation =
                        !resolvable && destination.Registry.StaleRejectionCount == staleBefore + 1;

                    bool pass = report.Recovered
                        && report.Outcome == Outcome.Published
                        && registryAfter == registryBefore + 1
                        && report.RegistryCountAfter == report.RegistryCountBefore + 1
                        && report.DestinationIsFreshIncarnation
                        && report.SourceUnchanged
                        && !destination.World.Session.Equals(source.World.Session)
                        && destination.Host.Lifecycle == WorldLifecycleState.Running
                        && destination.Host.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && destination.Host.CurrentStep.Equals(LogicalStepId.Zero)
                        && minted
                        && identityLiveAtDestination
                        && refusedAsForeignIncarnation;

                    detail = "recovered=" + report.Recovered
                        + "; recoveredOutcome=" + report.Outcome
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + "; destination=" + report.DestinationHost!.World.Session.ToString()
                        + "; destinationState=" + destination.Host.Lifecycle
                        + "; destinationEpoch=" + destination.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; destinationStep=" + destination.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; freshIncarnation=" + report.DestinationIsFreshIncarnation
                        + "; sourceState=" + source.Host.Lifecycle
                        + "; sourceUnchanged=" + report.SourceUnchanged
                        + "; sourceHandleMinted=" + minted
                        + "; identityLiveAtDestination=" + identityLiveAtDestination
                        + "; handleResolved=" + resolvable
                        + "; refusedAsForeignIncarnation=" + refusedAsForeignIncarnation
                        + "; repairs=" + report.RepairsAttempted.ToString(CultureInfo.InvariantCulture);

                    if (!pass)
                    {
                        failure = "the checkpoint capture + restore into a new session did not hold: " + report.Describe();
                    }

                    return pass;
                }
                finally
                {
                    if (destination != null)
                    {
                        Close(destination);
                    }
                }
            }

            // ================================================================== the fixture

            /// <summary>
            /// Mounts the three installations the two branches act on, in the order the dependency needs: the
            /// required provider, the unrelated genre provider, then the consumer that requires the first — so the
            /// consumer's required dependency really resolves before the failure (P-011). The pair's resolution is
            /// asserted from the committed entry (declared required dependency, at least one binding) because a
            /// mount that published is not the same fact as a dependency that resolved.
            /// </summary>
            private bool MountTheRequiredPair(Rig chain, out string failure)
            {
                failure = string.Empty;
                if (!Submit(
                        chain,
                        family.MountInstall(
                            family.RequiredProviderManifest, family.RequiredProviderInstall, family.RequiredProviderScope),
                        "mount-required-provider",
                        out failure))
                {
                    return false;
                }

                if (!Submit(
                        chain,
                        family.MountInstall(
                            family.LifecycleProviderManifest, family.LifecycleProviderInstall, family.LifecycleProviderScope),
                        "mount-unrelated-provider",
                        out failure))
                {
                    return false;
                }

                if (!Submit(
                        chain,
                        family.MountInstall(
                            family.RequiredConsumerManifest, family.RequiredConsumerInstall, family.RequiredConsumerScope),
                        "mount-required-consumer",
                        out failure))
                {
                    return false;
                }

                if (!DeclaresRequiredDependency(chain, family.RequiredConsumerInstall, out failure))
                {
                    failure = "the required pair did not resolve before the failure: " + failure;
                    return false;
                }

                if (!MatchesPublishedAssembly(chain))
                {
                    failure = "the required pair's publications did not leave the world joined to the committed composition";
                    return false;
                }

                return true;
            }

            /// <summary>Admits one lifecycle edit and completes its publication through the controller (P-046).</summary>
            private bool Submit(Rig chain, CompositionEditPayload payload, string label, out string failure)
            {
                failure = string.Empty;
                LifecycleRequestReport report = chain.Controller.Submit(payload, NextOperation(chain.World));
                if (!report.Succeeded
                    || report.Derived == null
                    || report.Derived.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    failure = label + ": the lifecycle publication was refused or did not publish ("
                        + report.Code + ": " + report.Detail + ")";
                    return false;
                }

                return true;
            }

            /// <summary>Whether one installation declares the required service and really resolved a binding for it.</summary>
            private bool DeclaresRequiredDependency(Rig chain, PluginInstanceId consumer, out string failure)
            {
                failure = string.Empty;
                if (!chain.Lane.Committed.TryGetInstall(consumer, out InstallEntry? entry) || entry == null)
                {
                    failure = "the consumer is not installed in the committed composition";
                    return false;
                }

                bool declared = false;
                IReadOnlyList<ServiceDependency> dependencies = entry.Manifest.ServiceDependencies;
                for (int i = 0; i < dependencies.Count; i++)
                {
                    if (dependencies[i].Required
                        && dependencies[i].Contract.ContractId.Equals(family.RequiredService.ContractId))
                    {
                        declared = true;
                        break;
                    }
                }

                if (!declared)
                {
                    failure = "the consumer declares no required dependency on the service it is supposed to wait for";
                    return false;
                }

                if (entry.Bindings.Count == 0)
                {
                    failure = "the consumer resolved no binding to the mounted required provider";
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Stands one world up through the real chain of section 5 and records it for teardown. The descriptor
            /// comes from the family's own compile, the registration from the family's own generated registrations,
            /// and every other member is the production type: nothing here is a model of a kernel module.
            /// </summary>
            private Rig? StandUp(string label, OperationId operation)
            {
                PipelineDescriptorReport descriptorReport = CompileOnce();
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

                Rig? chain = Attach(label, host, descriptorReport);
                if (chain == null)
                {
                    return null;
                }

                return SeedTheWorld(chain) ? chain : null;
            }

            /// <summary>
            /// Gives one host its own assembly path, control lane, derivation pipeline, time driver and lifecycle
            /// controller. The controller owns the one lifecycle binding of the world and is built before the first
            /// publication, because a lane that has already published cannot swap its binding (P-030). The same
            /// method serves a freshly created world and a recovered one: a recovered world is a new incarnation
            /// with the same caller-owned surface.
            /// </summary>
            private Rig? Attach(string label, UnityWorldHost host, PipelineDescriptorReport descriptorReport)
            {
                if (descriptorReport.Descriptor == null || descriptorReport.Adaptation == null)
                {
                    lastFailure = "the compile report cannot describe this host's assembly surface";
                    return null;
                }

                var registry = new TargetRegistry(host.World, TargetRegistryCapacity);
                var publisher = new AssemblyPublisher(
                    host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptorReport.Descriptor!);
                var targets = new LiveTargetIndex(publisher.Recipes);
                var seeder = new LiveTargetSeeder(host, registry, targets);
                IDerivationValueSource values = family.CreateValues();
                var manifestSource = new ManifestSource(
                    new CatalogManifestSource(family.Catalog, family.Declarations), family.ExtraManifests);
                var validator = new DerivationModeSwitchValidator(values, () => TargetView(targets));
                CompositionHost lane = CompositionHost.CreateDefault(
                    host.World, family.WorldRootScope, manifestSource, resources, family.LaneSeed, validator);
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
                var controller = new LifecycleController(host, lane, publisher, pipeline);

                var chain = new Rig(
                    label, host, lane, publisher, registry, targets, seeder, pipeline, bridge, time, controller,
                    descriptorReport);
                rigs.Add(chain);
                return chain;
            }

            /// <summary>Seeds the family's declared targets, so the world owns the storage its derivations act on.</summary>
            private bool SeedTheWorld(Rig chain)
            {
                if (!family.SeedTargets(new Gc013WorldContext(chain.Host, chain.Targets, chain.Seeder)))
                {
                    lastFailure = "the family refused to seed its declared targets";
                    return false;
                }

                if (chain.Targets.Count == 0)
                {
                    lastFailure = "the world was created but owns no live target";
                    return false;
                }

                return true;
            }

            // ================================================================== teardown and plumbing

            /// <summary>
            /// Stops and disposes every world this host created, including the faulted one and the recovered one.
            /// Every world is the caller's to settle (P-047, P-048), and the fault run's own teardown observation
            /// asserts the registry returned to its baseline, so a leak here fails there.
            /// </summary>
            private void TearDownRigs()
            {
                while (rigs.Count > 0)
                {
                    Close(rigs[rigs.Count - 1]);
                }
            }

            private void Close(Rig rig)
            {
                rig.Time.Clear(out int discarded, out int wakes);
                _ = discarded;
                _ = wakes;
                rig.Host.Stop(NextOperation(rig.World), "provider-failure scenario teardown");
                rig.Host.Dispose();
                rigs.Remove(rig);
            }

            private void Observe(string bareName, Action body)
            {
                lastFailure = string.Empty;
                try
                {
                    body();
                }
                catch (Exception exception)
                {
                    lastFailure = DescribeException(exception);
                }

                if (!Recorded(bareName))
                {
                    Record(
                        bareName,
                        false,
                        lastFailure.Length == 0 ? "the observation recorded no verdict" : lastFailure);
                }
            }

            private void Record(string bareName, bool passed, string detail)
            {
                steps.Add(new FaultScenarioStep(bareName, passed, detail ?? string.Empty));
            }

            private bool Recorded(string bareName)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (string.Equals(steps[i].Name, bareName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }

            private PipelineDescriptorReport CompileOnce() => compiled ??= family.CompilePipeline();

            private static IReadOnlyList<DerivationTarget> TargetView(LiveTargetIndex targets)
            {
                DerivationInputTargets view = targets.BuildDerivationTargets();
                return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
            }

            private static PlanBudget DefaultBudget() =>
                new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot);

            private static string StateOf(Rig chain, PluginInstanceId instance)
            {
                if (!chain.Lane.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return "<not-installed>";
                }

                return entry.State.ToString();
            }

            /// <summary>Rows of the published assembly attributed to one installation (P-017, P-033).</summary>
            private static int AttributedRows(Rig chain, PluginInstanceId instance) =>
                chain.Controller.Binding.CountAttributedRows(instance);

            /// <summary>Live binding rows of the published assembly one installation is the provider of (P-033).</summary>
            private static int BindingCount(Rig chain, PluginInstanceId instance)
            {
                TargetBindingTable table = chain.Publisher.Published.Bindings;
                int count = 0;
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    if (table.Rows[i].Provider.Value.Equals(instance.Value))
                    {
                        count++;
                    }
                }

                return count;
            }

            /// <summary>Whether the world's published assembly is the lane's committed composition (P-006, P-030).</summary>
            private static bool MatchesPublishedAssembly(Rig chain) =>
                AssemblyPublisher.MatchesPublishedAssembly(
                    chain.Lane.Committed.Revision,
                    chain.Lane.Committed.Epoch,
                    chain.Publisher.PublishedRevision,
                    chain.Host.CurrentEpoch);

            private static bool NamesExactly(IReadOnlyList<PluginInstanceId> instances, PluginInstanceId expected) =>
                instances.Count == 1 && instances[0].Value.Equals(expected.Value);

            private static bool Names(IReadOnlyList<PluginInstanceId> instances, PluginInstanceId candidate)
            {
                for (int i = 0; i < instances.Count; i++)
                {
                    if (instances[i].Value.Equals(candidate.Value))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static string Describe(IReadOnlyList<PluginInstanceId> instances)
            {
                if (instances.Count == 0)
                {
                    return "<none>";
                }

                var text = new List<string>(instances.Count);
                for (int i = 0; i < instances.Count; i++)
                {
                    text.Add(instances[i].ToString());
                }

                return string.Join(",", text.ToArray());
            }

            private static string DescribeException(Exception exception) =>
                "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }

        /// <summary>
        /// One world's real chain of section 5, held together so teardown can stop and dispose it and so an
        /// observation can read the module reports of the same world. Every member is a production type.
        /// </summary>
        private sealed class Rig
        {
            public Rig(
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
                LifecycleController controller,
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
                Controller = controller;
                DescriptorReport = descriptorReport;
            }

            public string Label { get; }

            public UnityWorldHost Host { get; }

            public WorldId World => Host.World;

            public CompositionHost Lane { get; }

            public AssemblyPublisher Publisher { get; }

            public TargetRegistry Registry { get; }

            public LiveTargetIndex Targets { get; }

            public LiveTargetSeeder Seeder { get; }

            public DerivedAssemblyPipeline Pipeline { get; }

            public WorldCompositionBridge Bridge { get; }

            public WorldTimeDriver Time { get; }

            public LifecycleController Controller { get; }

            public PipelineDescriptorReport DescriptorReport { get; }
        }

        /// <summary>
        /// The lane's manifest source: the catalog's own declarations plus the family's extra manifests (the four
        /// lifecycle-shaped manifests and the state-policy provider). A manifest added here resolves for a mount of
        /// its plugin type exactly as a catalog declaration does (P-009). It mirrors the private source
        /// `FaultScenario` and `W4GateScenario` each declare, because both are private to their own runner.
        /// </summary>
        private sealed class ManifestSource : IPluginManifestSource
        {
            private readonly CatalogManifestSource catalog;
            private readonly Dictionary<Id128, PluginManifest> added = new Dictionary<Id128, PluginManifest>();

            public ManifestSource(CatalogManifestSource catalog, IReadOnlyList<PluginManifest> extra)
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
        /// The host's managed-resource factory: counted preparations and recorded disposals, so a lease a
        /// publication really prepared can be traced to its release (P-007, P-048).
        /// </summary>
        private sealed class ProviderFailureResources : IManagedResourceFactory
        {
            private readonly IdSequence ids;

            public ProviderFailureResources(IdSequence ids)
            {
                this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            }

            public int PrepareCount { get; private set; }

            public int DisposeCount { get; private set; }

            public FactoryKey DisposerKey { get; } =
                new FactoryKey(new Id128(0x4730313750465245UL, 1UL), 1U);

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
