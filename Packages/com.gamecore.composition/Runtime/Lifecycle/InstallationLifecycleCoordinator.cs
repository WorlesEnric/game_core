// GameCore.Composition — the installation lifecycle coordinator: P-046 transitions over the control/publication
// path, with the P-048 teardown sequences they imply.
//
// One publication can do several lifecycle things at once, and P-012/P-046 require them to happen *together*:
// a provider is removed, its consumers start waiting and retract their contributions in the same publication, a
// replacement is committed while its predecessor retires, and a suspension retracts active behavior while
// keeping the installation. This type reads one frozen `CompositionEditPlan` and performs exactly the
// consequences the plan declares:
//
//   * **Staging** (`Stage`) prepares a candidate activation for an in-place replacement while the old Active
//     activation keeps running (P-046). The installation generation does not change; the activation epoch does.
//   * **Commit** (`Commit`) walks the plan once: activates mounts and dependency returns, suspends, retracts the
//     contribution of everything that stops contributing, retires predecessors and removed installations through
//     the P-048 sequencer, and reports the service-closure delta of the whole publication.
//   * **Abort** (`Abort`) is the prewrite abort: a cancelled or failed-preparation replacement releases its
//     candidate and reopens the old activation's gates, which is the diagram's one path back to `Active` (06 s1).
//
// Every lifecycle edge is requested through `InstallationStateMachine`, so an edge the P-046 table does not have
// is refused as a value and changes nothing. Repeating a commit for the same operation is refused by identity, so
// a retransmission can never publish a second time (P-050).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>Bounds of the lifecycle ledgers; a bounded quarantine registry is required by 06 s6.</summary>
    public sealed class LifecycleSettings
    {
        public LifecycleSettings(int quarantineCapacity, ulong quarantineByteCapacity)
        {
            if (quarantineCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quarantineCapacity), "A quarantine registry is bounded by a positive entry count (06 s6).");
            }

            QuarantineCapacity = quarantineCapacity;
            QuarantineByteCapacity = quarantineByteCapacity;
        }

        /// <summary>Retained references the quarantine registry accepts before it refuses admission (06 s6).</summary>
        public int QuarantineCapacity { get; }

        /// <summary>Retained bytes the quarantine registry accepts before it refuses admission; 0 means unbounded.</summary>
        public ulong QuarantineByteCapacity { get; }

        public static LifecycleSettings Default { get; } = new LifecycleSettings(4096, 64UL * 1024UL * 1024UL);
    }

    /// <summary>One candidate activation staged by a plan, as the staging report records it.</summary>
    public readonly struct StagedCandidate
    {
        public readonly PluginInstanceId Instance;
        public readonly InstallationGeneration Generation;
        public readonly ActivationEpoch ActivationEpoch;

        public StagedCandidate(PluginInstanceId instance, InstallationGeneration generation, ActivationEpoch activationEpoch)
        {
            Instance = instance;
            Generation = generation;
            ActivationEpoch = activationEpoch;
        }

        public override string ToString() =>
            Instance.ToString() + ":gen" + Generation.Value.ToString(CultureInfo.InvariantCulture)
            + ":epoch" + ActivationEpoch.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Outcome of the staging phase of one plan; a refusal names the edge it refused (P-046).</summary>
    public sealed class LifecycleStageReport
    {
        public LifecycleStageReport(
            OperationId operation,
            IReadOnlyList<StagedCandidate>? candidates,
            IReadOnlyList<LifecycleTransition>? refused,
            IReadOnlyList<PluginInstanceId>? retractOnCommit)
        {
            Operation = operation;
            Candidates = ContractCollections.Freeze(candidates);
            Refused = ContractCollections.Freeze(refused);
            RetractOnCommit = ContractCollections.Freeze(retractOnCommit);
        }

        public OperationId Operation { get; }

        /// <summary>Candidates staged while their predecessor keeps running (P-046).</summary>
        public IReadOnlyList<StagedCandidate> Candidates { get; }

        /// <summary>Staging requests the P-046 table refused; their installations are untouched.</summary>
        public IReadOnlyList<LifecycleTransition> Refused { get; }

        /// <summary>Installations whose active contribution will retract at commit (P-012, P-046).</summary>
        public IReadOnlyList<PluginInstanceId> RetractOnCommit { get; }

        public bool Succeeded => Refused.Count == 0;

        public bool StagedAnything => Candidates.Count != 0;
    }

    /// <summary>Outcome of one committed publication: what became active, what retracted, what was torn down.</summary>
    public sealed class LifecycleCommitReport
    {
        public LifecycleCommitReport(
            OperationId operation,
            SnapshotToken? token,
            DiagnosticCode code,
            IReadOnlyList<LifecycleEdge>? edges,
            IReadOnlyList<TeardownReport>? teardowns,
            IReadOnlyList<ContributionRetraction>? retractions,
            CleanupReport cleanup,
            ServiceClosureDelta closure,
            IReadOnlyList<Id128>? retained)
        {
            Operation = operation;
            Token = token;
            Code = code;
            Edges = ContractCollections.Freeze(edges);
            Teardowns = ContractCollections.Freeze(teardowns);
            Retractions = ContractCollections.Freeze(retractions);
            Cleanup = cleanup;
            Closure = closure;
            Retained = ContractCollections.Freeze(retained);
        }

        public OperationId Operation { get; }

        public SnapshotToken? Token { get; }

        /// <summary>`None` when the whole publication settled; `PublishedWithCleanupErrors`' code otherwise (05 s4).</summary>
        public DiagnosticCode Code { get; }

        public IReadOnlyList<LifecycleEdge> Edges { get; }

        /// <summary>
        /// One retraction per installation whose active behavior stopped contributing in this publication: a
        /// suspension, or an installation that lost a required provider. A removal's retraction is part of its
        /// teardown report, so it is not repeated here.
        /// </summary>
        public IReadOnlyList<ContributionRetraction> Retractions { get; }

        /// <summary>One P-048 pass per predecessor or removed installation, in execution order.</summary>
        public IReadOnlyList<TeardownReport> Teardowns { get; }

        /// <summary>Aggregated retired/failed/quarantined leases of every teardown in this publication.</summary>
        public CleanupReport Cleanup { get; }

        /// <summary>The service-closure consequence of this publication (P-012, P-026).</summary>
        public ServiceClosureDelta Closure { get; }

        /// <summary>Resources still retained after this publication; each one is quarantined and reported (P-048).</summary>
        public IReadOnlyList<Id128> Retained { get; }

        public bool HasCleanupErrors => Cleanup.HasCleanupErrors;

        /// <summary>True while something is still retained; the installation stays `Retiring` (P-048).</summary>
        public bool Blocked => Retained.Count != 0;

        /// <summary>True when a job fence was the reason a resource could not be released (P-047).</summary>
        public bool BlockedByJobFence
        {
            get
            {
                for (int i = 0; i < Teardowns.Count; i++)
                {
                    if (Teardowns[i].BlockedByJobFence)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>What a caller records as the operation outcome (05 s4: the three are not the same fact).</summary>
        public Outcome Outcome => Cleanup.HasCleanupErrors ? Outcome.PublishedWithCleanupErrors : Outcome.Published;
    }

    /// <summary>Outcome of a prewrite abort: which candidates were released and which gates reopened.</summary>
    public sealed class LifecycleAbortReport
    {
        public LifecycleAbortReport(
            OperationId operation,
            DiagnosticCode code,
            IReadOnlyList<PluginInstanceId>? abortedCandidates,
            IReadOnlyList<PluginInstanceId>? reopened)
        {
            Operation = operation;
            Code = code;
            AbortedCandidates = ContractCollections.Freeze(abortedCandidates);
            Reopened = ContractCollections.Freeze(reopened);
        }

        public OperationId Operation { get; }

        public DiagnosticCode Code { get; }

        /// <summary>Candidates released without touching the running activation (P-046, P-029).</summary>
        public IReadOnlyList<PluginInstanceId> AbortedCandidates { get; }

        /// <summary>Activations whose gates reopened because the abort happened before any live write (06 s1).</summary>
        public IReadOnlyList<PluginInstanceId> Reopened { get; }

        public bool ChangedAnything => AbortedCandidates.Count != 0 || Reopened.Count != 0;
    }

    /// <summary>
    /// Drives the P-046 lifecycle of one composition host over the frozen plan contract. It owns the activation,
    /// job-fence and quarantine ledgers, records attempts, and performs teardown through the P-048 sequencer.
    /// </summary>
    public sealed class InstallationLifecycleCoordinator
    {
        private readonly HashSet<Id128> committedOperations = new HashSet<Id128>();
        private readonly Dictionary<Id128, List<PluginInstanceId>> stagedCandidates = new Dictionary<Id128, List<PluginInstanceId>>();
        private readonly List<Diagnostic> diagnostics = new List<Diagnostic>();

        public InstallationLifecycleCoordinator(
            WorldId world,
            ResourceLedger resources,
            ICallbackGate callbacks,
            LifecycleSettings settings,
            ILifecycleWorldBinding? binding)
        {
            World = world;
            Resources = resources ?? throw new ArgumentNullException(nameof(resources));
            Callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Binding = binding ?? new CompositionOnlyLifecycleBinding();
            Activations = new ActivationLedger();
            Jobs = new JobFenceRegistry();
            Quarantine = new QuarantineRegistry(Settings.QuarantineCapacity, Settings.QuarantineByteCapacity);
            Teardown = new TeardownSequencer(Resources, Jobs, Quarantine, Callbacks, Binding);
        }

        public WorldId World { get; }

        public ResourceLedger Resources { get; }

        public ActivationLedger Activations { get; }

        public JobFenceRegistry Jobs { get; }

        public QuarantineRegistry Quarantine { get; }

        /// <summary>The P-048 teardown sequencer this coordinator uses; one pass per retired activation.</summary>
        public TeardownSequencer Teardown { get; }

        public ICallbackGate Callbacks { get; }

        public LifecycleSettings Settings { get; }

        /// <summary>The world-side binding; replaced once a live world joins this lane (P-048 steps 1-4).</summary>
        public ILifecycleWorldBinding Binding { get; private set; }

        /// <summary>Publications this coordinator committed; a repeated commit for one operation is refused.</summary>
        public int CommitCount { get; private set; }

        public int StageCount { get; private set; }

        public int AbortCount { get; private set; }

        public int UnloadCount { get; private set; }

        /// <summary>Commits refused because the operation had already been committed (P-050).</summary>
        public int RepeatCommitRefusalCount { get; private set; }

        /// <summary>Staging requests refused by the P-046 table.</summary>
        public int RefusedStageCount { get; private set; }

        /// <summary>Lifecycle edges refused at commit time; each one left its installation unchanged.</summary>
        public int RefusedCommitEdgeCount { get; private set; }

        public DiagnosticCode LastCode { get; private set; } = DiagnosticCode.None;

        public string LastDetail { get; private set; } = string.Empty;

        /// <summary>Structured diagnostics of every refusal this coordinator recorded, newest last (P-052).</summary>
        public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

        /// <summary>
        /// Attaches the live world binding. A lane that has already published cannot swap its binding: the
        /// activation stamps and resource epochs of the published assembly belong to the binding that produced them.
        /// </summary>
        public void AttachWorldBinding(ILifecycleWorldBinding binding)
        {
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            if (CommitCount != 0)
            {
                throw new InvalidOperationException("A lifecycle binding cannot be exchanged after this lane has published an assembly (P-030).");
            }

            Binding = binding;
        }

        /// <summary>
        /// Phase 1 of a plan: stage the candidate activation of every in-place replacement or reconfiguration, and
        /// record which installations will retract their contribution at commit. The running activations are not
        /// touched, so the old assembly keeps executing while the candidate prepares (P-046, P-029).
        /// </summary>
        public LifecycleStageReport Stage(CompositionEditPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            StageCount++;
            List<StagedCandidate> candidates = new List<StagedCandidate>();
            List<LifecycleTransition> refused = new List<LifecycleTransition>();
            List<PluginInstanceId> retract = new List<PluginInstanceId>();
            List<PluginInstanceId> staged = new List<PluginInstanceId>();

            if (plan.Succeeded)
            {
                IReadOnlyList<InstallEntry> after = plan.After.Installs;
                for (int i = 0; i < after.Count; i++)
                {
                    InstallEntry entry = after[i];
                    InstallEntry? before = null;
                    if (plan.Before.TryGetInstall(entry.Instance, out InstallEntry? found) && found != null)
                    {
                        before = found;
                    }

                    if (IsInPlaceReplacement(before, entry))
                    {
                        LifecycleTransition stage = Activations.StageCandidate(
                            entry.Instance,
                            entry.Record.Generation,
                            entry.Record.ActivationEpoch,
                            plan.Operation);

                        if (stage.Allowed)
                        {
                            staged.Add(entry.Instance);
                            candidates.Add(new StagedCandidate(entry.Instance, entry.Record.Generation, entry.Record.ActivationEpoch));
                        }
                        else
                        {
                            RefusedStageCount++;
                            refused.Add(stage);
                            Note(
                                stage.Code,
                                plan.Operation,
                                "the staged candidate for " + entry.Instance.ToString() + " was refused: "
                                + stage.From + " -> " + stage.To + " is not a legal replacement edge (P-046)");
                        }
                    }

                    if (before != null && before.State == InstallationState.Active && entry.State != InstallationState.Active)
                    {
                        retract.Add(entry.Instance);
                    }
                }
            }

            stagedCandidates[plan.Operation] = staged;
            return new LifecycleStageReport(plan.Operation, candidates, refused, retract);
        }

        /// <summary>Records one structured refusal with the operation identity and phase P-052 requires.</summary>
        private void Note(DiagnosticCode code, OperationId operation, string summary) =>
            diagnostics.Add(Diagnostic.Create(code, OperationPhase.Validation, operation, summary));

        /// <summary>
        /// Phase 2 of a plan: commit the publication. This is the point where "same publication" is true for the
        /// whole closure — consumers of a lost provider are waiting and retracted, a replacement's predecessor is
        /// retiring, and every removed installation has run its P-048 order.
        /// </summary>
        public LifecycleCommitReport Commit(CompositionEditPlan plan, SnapshotToken? token)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            ServiceClosureDelta closure = ServiceClosureDelta.Compute(plan);
            if (!committedOperations.Add(plan.Operation))
            {
                // A retransmission never reaches here (the lane returns the original row); this guard exists so a
                // caller that repeats a commit by identity publishes nothing a second time (P-050).
                RepeatCommitRefusalCount++;
                LastCode = DiagnosticCode.IdempotencyConflict;
                LastDetail = "operation " + plan.Operation.ToString() + " already committed its lifecycle effects (P-050)";
                Note(DiagnosticCode.IdempotencyConflict, plan.Operation, LastDetail);
                return new LifecycleCommitReport(
                    plan.Operation, token, DiagnosticCode.IdempotencyConflict, null, null, null, CleanupReport.Empty, closure, null);
            }

            List<LifecycleEdge> edges = new List<LifecycleEdge>();
            List<TeardownReport> teardowns = new List<TeardownReport>();
            List<ContributionRetraction> retractions = new List<ContributionRetraction>();
            List<Id128> retired = new List<Id128>();
            List<Id128> failed = new List<Id128>();
            List<Id128> quarantined = new List<Id128>();

            IReadOnlyList<InstallEntry> after = plan.After.Installs;
            for (int i = 0; i < after.Count; i++)
            {
                InstallEntry entry = after[i];
                plan.Before.TryGetInstall(entry.Instance, out InstallEntry? before);
                ApplyTransition(plan, before, entry, edges, teardowns, retractions, retired, failed, quarantined);
            }

            // Removed and displaced installations run the P-048 order: close ingress, fence users, retract the
            // closure, retire resources in reverse order, admit quarantines.
            for (int i = 0; i < plan.RetiredInstances.Count; i++)
            {
                PluginInstanceId instance = plan.RetiredInstances[i];
                if (!plan.Before.TryGetInstall(instance, out InstallEntry? before) || before == null)
                {
                    continue;
                }

                TeardownReport teardown = Teardown.Unload(
                    instance,
                    new ActivationStamp(before.Record.Generation, before.Record.ActivationEpoch),
                    plan.Operation,
                    false);
                teardowns.Add(teardown);
                Append(teardown.Cleanup.Retired, retired);
                Append(teardown.Cleanup.Failed, failed);
                Append(teardown.Quarantined, quarantined);
                Activations.Retire(instance);
                Activations.Settle(instance, teardown.DisposeSettled && Quarantine.EntriesFor(instance).Count == 0);
            }

            stagedCandidates.Remove(plan.Operation);

            CleanupReport cleanup = new CleanupReport(retired, failed, quarantined);
            DiagnosticCode code = cleanup.HasCleanupErrors ? DiagnosticCode.ResourceUnavailable : DiagnosticCode.None;
            LastCode = code;
            LastDetail = cleanup.HasCleanupErrors
                ? "publication committed with cleanup errors; retained references stay quarantined (P-048)"
                : string.Empty;
            CommitCount++;

            List<Id128> retained = new List<Id128>();
            for (int i = 0; i < quarantined.Count; i++)
            {
                retained.Add(quarantined[i]);
            }

            return new LifecycleCommitReport(plan.Operation, token, code, edges, teardowns, retractions, cleanup, closure, retained);
        }

        /// <summary>
        /// The prewrite abort of one plan: release staged candidates and reopen the old activation's gates. Called
        /// for a cancelled plan and for a plan that failed before any live write, so the old assembly stays usable.
        /// </summary>
        public LifecycleAbortReport Abort(CompositionEditPlan plan, DiagnosticCode code)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            AbortCount++;
            List<PluginInstanceId> aborted = new List<PluginInstanceId>();
            List<PluginInstanceId> reopened = new List<PluginInstanceId>();

            if (stagedCandidates.TryGetValue(plan.Operation, out List<PluginInstanceId>? staged) && staged != null)
            {
                for (int i = 0; i < staged.Count; i++)
                {
                    if (Activations.AbortCandidate(staged[i], out ActivationAttempt? _))
                    {
                        aborted.Add(staged[i]);
                    }
                }
            }

            // A prewrite abort leaves the old activation in charge; the edge Quiescing -> Active is the diagram's
            // one path back, so an activation that was quiesced for this plan reopens its gates (06 s1).
            IReadOnlyList<InstallEntry> before = plan.Before.Installs;
            for (int i = 0; i < before.Count; i++)
            {
                InstallEntry entry = before[i];
                if (!Activations.TryGetCurrent(entry.Instance, out ActivationAttempt? current) || current == null)
                {
                    continue;
                }

                if (current.State != InstallationState.Quiescing)
                {
                    continue;
                }

                LifecycleTransition reopen = Activations.ReopenFromQuiescing(entry.Instance);
                if (reopen.Allowed)
                {
                    Binding.ReopenIngress(entry.Instance, current.Stamp());
                    Callbacks.RegisterActivation(entry.Instance, current.Generation, current.ActivationEpoch);
                    reopened.Add(entry.Instance);
                }
            }

            stagedCandidates.Remove(plan.Operation);
            LastCode = code;
            LastDetail = "prewrite abort: " + aborted.Count.ToString(CultureInfo.InvariantCulture)
                + " candidate(s) released, " + reopened.Count.ToString(CultureInfo.InvariantCulture) + " gate(s) reopened";
            return new LifecycleAbortReport(plan.Operation, code, aborted, reopened);
        }

        /// <summary>
        /// Unloads one installation through the whole P-048 order without a composition plan: the path an explicit
        /// unload or a world shutdown takes. The world's own boundary is asked to settle the current step first.
        /// </summary>
        public TeardownReport Unload(PluginInstanceId instance, OperationId operation)
        {
            UnloadCount++;
            ActivationStamp stamp = default(ActivationStamp);
            if (Activations.TryGetCurrent(instance, out ActivationAttempt? current) && current != null)
            {
                stamp = current.Stamp();
            }

            TeardownReport report = Teardown.Unload(instance, stamp, operation, true);
            Activations.Retire(instance);
            Activations.Settle(instance, report.DisposeSettled && Quarantine.EntriesFor(instance).Count == 0);
            LastCode = report.Code;
            LastDetail = report.Describe();
            return report;
        }

        /// <summary>
        /// A late completion: evaluated against the world, the fence, liveness and the activation stamp, in that
        /// order (P-047). A suspended or retired installation has no registered activation, so its old tokens are
        /// discarded rather than delivered — which is what stops a late completion resurrecting it.
        /// </summary>
        public CallbackGateDecision EvaluateCompletion(AsyncWorkToken token) => Callbacks.Evaluate(token);

        /// <summary>True when an installation currently holds execution authority (Active or Quiescing) (P-046).</summary>
        public bool HoldsAuthority(PluginInstanceId instance) =>
            Activations.TryGetCurrent(instance, out ActivationAttempt? current) && current != null && current.HoldsAuthority;

        /// <summary>Settles retained references of one installation after its users ended (explicit, never by timeout).</summary>
        public CleanupReport ReleaseQuarantineFor(PluginInstanceId instance) => Teardown.ReleaseQuarantineFor(instance);

        /// <summary>
        /// Every resource still quarantined for one installation; a caller uses this to decide between refusing
        /// acquisition and stopping the world when the registry is exhausted (06 s6).
        /// </summary>
        public int RetainedCountFor(PluginInstanceId instance) => Quarantine.EntriesFor(instance).Count;

        private void ApplyTransition(
            CompositionEditPlan plan,
            InstallEntry? before,
            InstallEntry entry,
            List<LifecycleEdge> edges,
            List<TeardownReport> teardowns,
            List<ContributionRetraction> retractions,
            List<Id128> retired,
            List<Id128> failed,
            List<Id128> quarantined)
        {
            InstallationState previous = before != null ? before.State : InstallationState.Registered;
            LifecycleTransition transition;

            switch (entry.State)
            {
                case InstallationState.Active:
                {
                    if (before == null)
                    {
                        // A mount: registration plus attempted activation, published as one assembly (P-046).
                        transition = Activations.Activate(entry.Instance, entry.Record.Generation, entry.Record.ActivationEpoch, plan.Operation);
                    }
                    else if (IsInPlaceReplacement(before, entry))
                    {
                        // The candidate staged during preparation takes over; its predecessor retires through the
                        // P-048 order with the *old* stamp, so its leases retire under the epoch that acquired them.
                        transition = Activations.CommitCandidate(entry.Instance, out ActivationAttempt? displaced);
                        if (transition.Allowed && displaced != null)
                        {
                            // The displaced activation retires under its OWN stamp, so the leases it acquired under
                            // the old activation epoch are exactly the ones retired (P-005, P-048). The installation
                            // stays Active: the replacement is in place, not a removal.
                            TeardownReport teardown = Teardown.Unload(
                                entry.Instance,
                                displaced.Stamp(),
                                plan.Operation,
                                false);
                            teardowns.Add(teardown);
                            Append(teardown.Cleanup.Retired, retired);
                            Append(teardown.Cleanup.Failed, failed);
                            Append(teardown.Quarantined, quarantined);
                        }
                    }
                    else if (before.State == InstallationState.WaitingForDependencies)
                    {
                        // P-012: the required provider returned, so the consumer resumes automatically.
                        transition = Activations.ResumeFromWaiting(entry.Instance, entry.Record.Generation, entry.Record.ActivationEpoch, plan.Operation);
                    }
                    else if (before.State == InstallationState.Suspended || before.State == InstallationState.Failed)
                    {
                        transition = Activations.Resume(entry.Instance, entry.Record.Generation, entry.Record.ActivationEpoch, plan.Operation);
                    }
                    else
                    {
                        transition = LifecycleTransition.Permit(previous, entry.State);
                    }

                    Callbacks.RegisterActivation(entry.Instance, entry.Record.Generation, entry.Record.ActivationEpoch);
                    break;
                }

                case InstallationState.Suspended:
                {
                    // P-046: suspension retracts active behavior and keeps the installation and its configuration.
                    transition = Activations.Suspend(entry.Instance);
                    retractions.Add(Retract(entry.Instance, entry, plan.Operation));
                    break;
                }

                case InstallationState.WaitingForDependencies:
                {
                    if (before == null || !Activations.TryGetCurrent(entry.Instance, out ActivationAttempt? _))
                    {
                        // A mount whose required provider is absent: registration plus a published waiting
                        // instance with no active contributions (P-046), not a half-active plugin.
                        transition = Activations.WaitOnRegistration(
                            entry.Instance,
                            entry.Record.Generation,
                            entry.Record.ActivationEpoch,
                            plan.Operation);
                        retractions.Add(Retract(entry.Instance, entry, plan.Operation));
                    }
                    else
                    {
                        // P-012: a required provider left, so this consumer waits and retracts in the same publication.
                        transition = Activations.WaitForDependencies(entry.Instance);
                        retractions.Add(Retract(entry.Instance, entry, plan.Operation));
                    }

                    break;
                }

                case InstallationState.Retiring:
                case InstallationState.Disposed:
                {
                    // A published removal already ran its teardown in the retired list; nothing to add here.
                    transition = Activations.TryGetCurrent(entry.Instance, out ActivationAttempt? live) && live != null
                        ? Activations.Retire(entry.Instance)
                        : LifecycleTransition.Permit(previous, entry.State);
                    break;
                }

                default:
                {
                    transition = LifecycleTransition.Permit(previous, entry.State);
                    break;
                }
            }

            if (!transition.Allowed)
            {
                RefusedCommitEdgeCount++;
                LastCode = transition.Code;
                LastDetail = "the installation " + entry.Instance.ToString() + " could not enter " + entry.State
                    + " from " + transition.From + " (P-046)";
                Note(transition.Code, plan.Operation, LastDetail);
                return;
            }

            if (previous != entry.State)
            {
                edges.Add(new LifecycleEdge(entry.Instance, previous, entry.State));
            }
        }

        private ContributionRetraction Retract(PluginInstanceId instance, InstallEntry entry, OperationId operation)
        {
            // P-046/P-047: a suspension or a lost required provider stops ingress and retracts the active
            // contribution. The gate is retired so a late completion cannot reacquire authority.
            ActivationStamp stamp = new ActivationStamp(entry.Record.Generation, entry.Record.ActivationEpoch);
            Callbacks.RetireActivation(instance);
            Binding.CloseIngress(instance, stamp);
            return Binding.RetractContributions(instance, stamp, operation);
        }

        private static bool IsInPlaceReplacement(InstallEntry? before, InstallEntry after)
        {
            // P-046/P-025: the same installation identity, still active, with a new activation epoch. The
            // generation is unchanged for an in-place update (P-005), so the epoch is the signal.
            return before != null
                && before.State == InstallationState.Active
                && after.State == InstallationState.Active
                && before.Record.Generation.Equals(after.Record.Generation)
                && !before.Record.ActivationEpoch.Equals(after.Record.ActivationEpoch);
        }

        private static void Append(IReadOnlyList<Id128> source, List<Id128> into)
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (!into.Contains(source[i]))
                {
                    into.Add(source[i]);
                }
            }
        }
    }
}
