// GameCore.Composition — `CompositionHost`: the serialized control lane of one world (P-002, P-051).
//
// `CompositionHost` maintains the desired scope/install graph. It is the only place that admits composition
// changes, and it does so in three strictly separated stages:
//
//   1. **Admission.** The operation id and input hash go to the bounded ledger. A retransmission returns its
//      original row; a conflicting reuse is refused; capacity, sequence and retention rules apply here (P-050).
//   2. **Staging.** The edit is planned by the pure applier against the staged tail of the pipeline, producing
//      an immutable proposal (new definition, delta, resolved service closure, listed resources). Public
//      queries still see the committed revision, and staged progress is readable per operation (00 s9).
//   3. **Publication.** `Drain()` publishes pending proposals in admission order at a boundary: one atomic
//      swap of the committed definition, one `CompositionRevision` and `AssemblyEpoch` increment, staged
//      resource gates opened, callback activations registered, removed installations retired in reverse order,
//      and a terminal result per operation (P-030, P-006, P-048).
//
// No ECS storage is touched anywhere in this assembly: the lane produces proposals and ownership records, and
// the Unity publication path consumes them later (GC-004 non-goals).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>Typed outcome of one submission onto the control lane.</summary>
    public sealed class EditAdmission
    {
        public EditAdmission(
            AdmissionKind kind,
            DiagnosticCode code,
            OperationStatusHandle handle,
            OperationLedgerEntry? entry,
            CompositionEditPlan? plan,
            IReadOnlyList<Diagnostic>? diagnostics)
        {
            Kind = kind;
            Code = code;
            Handle = handle;
            Entry = entry;
            Plan = plan;
            Diagnostics = ContractCollections.Freeze(diagnostics);
        }

        public AdmissionKind Kind { get; }

        /// <summary><see cref="DiagnosticCode.None"/> when the edit was admitted for publication.</summary>
        public DiagnosticCode Code { get; }

        public OperationStatusHandle Handle { get; }

        public OperationLedgerEntry? Entry { get; }

        /// <summary>Staged proposal; null when admission or planning refused the edit.</summary>
        public CompositionEditPlan? Plan { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>True when a proposal is waiting for the publication boundary.</summary>
        public bool Staged => Kind == AdmissionKind.Fresh && Plan != null && Plan.Succeeded && !Plan.IsNoChange;

        public bool Rejected => Code != DiagnosticCode.None && Code != DiagnosticCode.Cancelled;
    }

    /// <summary>What one drain pass published for one operation, in admission order.</summary>
    public sealed class PublishedOperation
    {
        public PublishedOperation(OperationId operation, Outcome outcome, DiagnosticCode code, SnapshotToken? token, CleanupReport? cleanup)
        {
            Operation = operation;
            Outcome = outcome;
            Code = code;
            Token = token;
            Cleanup = cleanup;
        }

        public OperationId Operation { get; }

        public Outcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public SnapshotToken? Token { get; }

        /// <summary>Cleanup outcome of retired installations; null when nothing retired.</summary>
        public CleanupReport? Cleanup { get; }
    }

    /// <summary>
    /// The composition host of one world. Implements the frozen `ICompositionHost` seam and adds the production
    /// surface the publication path and the tests need (typed submission, drain, cancellation, resources).
    /// </summary>
    public sealed class CompositionHost : ICompositionHost
    {
        private readonly OperationLedger ledger;
        private readonly ResourceLedger resources = new ResourceLedger();
        private readonly Dictionary<OperationId, ResourcePreparationSet> stagedResources = new Dictionary<OperationId, ResourcePreparationSet>();
        private readonly IPluginManifestSource manifests;
        private readonly IManagedResourceFactory? resourceFactory;
        private CompositionState committed;
        private CompositionState staged;

        public CompositionHost(
            WorldId world,
            ScopeId rootScope,
            CompositionHostSettings settings,
            IPluginManifestSource manifests,
            IManagedResourceFactory? resourceFactory,
            PropagationMode mode)
        {
            if (rootScope.IsDefault)
            {
                throw new ArgumentException("A world requires a real root scope identity (P-010).", nameof(rootScope));
            }

            World = world;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.manifests = manifests ?? throw new ArgumentNullException(nameof(manifests));
            this.resourceFactory = resourceFactory;

            // A fresh session starts at revision/epoch/step 0; the initial publication takes them to 1/1/0 and
            // the logical step stays 0 (05 s2). The world-level mode defaults to Automatic (P-013).
            ScopeRecord root = new ScopeRecord(
                rootScope,
                default(ScopeId),
                0,
                new IsolationSet(false, null),
                new IsolationSet(false, null),
                null,
                null);
            committed = CompositionState.CreateEmpty(world, root, mode);
            staged = committed;
            ledger = new OperationLedger(Settings.Capacity, Settings.Expiry);
            Callbacks = new CallbackGate(world);
        }

        /// <summary>Convenience construction with the protocol defaults of the frozen settings DTOs.</summary>
        public static CompositionHost CreateDefault(
            WorldId world,
            ScopeId rootScope,
            IPluginManifestSource manifests,
            IManagedResourceFactory? resourceFactory) =>
            new CompositionHost(
                world,
                rootScope,
                new CompositionHostSettings(ControlLaneCapacitySettings.Default, OperationExpirySettings.Default),
                manifests,
                resourceFactory,
                PropagationMode.Automatic);

        public WorldId World { get; }

        public CompositionHostSettings Settings { get; }

        /// <summary>The committed, published composition. Staged edits are never visible here (00 s9).</summary>
        public CompositionState Committed => committed;

        /// <summary>Tail of the staged pipeline: committed plus every admitted, unpublished proposal.</summary>
        public CompositionState StagedState => staged;

        /// <summary>World-level propagation mode; Automatic unless an admitted edit changed it (P-013).</summary>
        public PropagationMode Mode => committed.Mode;

        public OperationLedger OperationLedger => ledger;

        /// <summary>Tracked managed-resource ownership of this host, inspectable from tests (GC-004 DoD).</summary>
        public ResourceLedger Resources => resources;

        /// <summary>Callback gate of this world; a publication fence closes it while the swap happens (P-047).</summary>
        public CallbackGate Callbacks { get; }

        /// <summary>How many staged resource gates a publication has opened so far.</summary>
        public int ResourceGatesOpened { get; private set; }

        /// <summary>How many publications this lane has committed.</summary>
        public int PublicationCount { get; private set; }

        /// <summary>Admits one typed edit and plans it against the staged tail of the pipeline.</summary>
        public EditAdmission SubmitEdit(CompositionEditPayload payload, OperationId operation, CompositionRevision expectedRevision)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            FrozenPayload frozen = CompositionEditCodec.Encode(payload);
            return SubmitInternal(frozen, payload, operation, expectedRevision);
        }

        /// <summary>
        /// Frozen seam submission: decodes the payload, admits the operation and plans it. The handle is returned
        /// before the result exists (P-051); `Drain` publishes and `Read` reports.
        /// </summary>
        public OperationStatusHandle Submit(CompositionEditRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ContentHash inputHash = CompositionEditApplier.InputHashOf(request.EditPayload);
            if (!CompositionEditCodec.TryDecode(request.EditPayload, out CompositionEditPayload? payload, out DiagnosticCode decodeCode) ||
                payload == null)
            {
                return RefuseUndecodable(request.Operation, inputHash, decodeCode);
            }

            if (payload.ExpectedKind != request.Kind)
            {
                // The declared kind must agree with the declared subject, so "add" cannot arrive as a "remove".
                return RefuseUndecodable(request.Operation, inputHash, DiagnosticCode.UnsupportedVersion);
            }

            return SubmitInternal(request.EditPayload, payload, request.Operation, request.ExpectedRevision).Handle;
        }

        /// <summary>Retrieves the pending or terminal status of one admitted operation; pure, no mutation.</summary>
        public OperationReadResult Read(OperationStatusHandle handle)
        {
            if (!ledger.TryGet(handle.Operation, out OperationLedgerEntry? entry) || entry == null)
            {
                return ledger.IsExpired(handle.Operation) ? OperationReadResult.Expired() : OperationReadResult.Unknown();
            }

            return OperationReadResult.Found(entry);
        }

        /// <summary>Visible committed composition, including waiting installations (P-012, P-046).</summary>
        public CompositionStateSnapshot Snapshot() => committed.ToSnapshot();

        /// <summary>Null means an unknown scope; this never invents a node or a default policy.</summary>
        public ScopeSnapshot? FindScope(ScopeId scope)
        {
            if (!committed.Scopes.TryGet(scope, out ScopeRecord? record) || record == null)
            {
                return null;
            }

            return record.ToSnapshot(committed.Mode, committed.InstallsAt(scope));
        }

        /// <summary>Null means no such installation is registered in this world.</summary>
        public InstallSnapshot? FindInstall(PluginInstanceId instance) =>
            committed.TryGetInstall(instance, out InstallEntry? entry) && entry != null ? entry.ToSnapshot() : null;

        /// <summary>The staged proposal of one still-unpublished operation; null when there is none (00 s9).</summary>
        public CompositionEditPlan? StagedPlan(OperationId operation) => ledger.RowOf(operation)?.Plan;

        /// <summary>
        /// Publishes every pending proposal in admission order at one boundary. Each publication is one atomic
        /// revision and epoch increment; a publication opens its staged resource gates and registers its
        /// activations, and removed installations retire in reverse activation order (P-030, P-048).
        /// </summary>
        public IReadOnlyList<PublishedOperation> Drain()
        {
            List<PublishedOperation> published = new List<PublishedOperation>();
            IReadOnlyList<OperationId> pending = ledger.PendingInAdmissionOrder();
            for (int i = 0; i < pending.Count; i++)
            {
                PublishedOperation? result = Publish(pending[i]);
                if (result != null)
                {
                    published.Add(result);
                }
            }

            return published;
        }

        /// <summary>Settles one pending operation: crossing `BeginApplying` is the cancellation cutoff (P-051).</summary>
        public PublishedOperation? Publish(OperationId operation)
        {
            OperationLedger.LedgerRow? row = ledger.RowOf(operation);
            if (row == null || row.Phase != LedgerPhase.Pending || row.Plan == null)
            {
                return null;
            }

            CompositionEditPlan plan = row.Plan;
            if (!plan.Succeeded)
            {
                // A rejected proposal made no live writes; publication keeps the old visible revision (00 s9).
                ledger.Settle(operation, Outcome.Rejected, plan.Code, committed.Revision, committed.Epoch, null, committed.Step);
                ReleaseStagedResources(operation);
                return new PublishedOperation(operation, Outcome.Rejected, plan.Code, null, null);
            }

            if (plan.IsNoChange)
            {
                // NoChange increments nothing and publishes no new snapshot (P-006).
                ledger.Settle(operation, Outcome.NoChange, DiagnosticCode.None, committed.Revision, committed.Epoch, null, committed.Step);
                ReleaseStagedResources(operation);
                return new PublishedOperation(operation, Outcome.NoChange, DiagnosticCode.None, null, null);
            }

            if (!committed.Revision.TryIncrement(out CompositionRevision nextRevision) ||
                !committed.Epoch.TryIncrement(out AssemblyEpoch nextEpoch))
            {
                // Counter exhaustion rejects further reconfiguration instead of wrapping (P-005).
                ledger.Settle(operation, Outcome.Rejected, DiagnosticCode.BudgetExceeded, committed.Revision, committed.Epoch, null, committed.Step);
                ReleaseStagedResources(operation);
                return new PublishedOperation(operation, Outcome.Rejected, DiagnosticCode.BudgetExceeded, null, null);
            }

            if (!ledger.BeginApplying(operation))
            {
                return null;
            }

            // The published definition is the plan's After state with the new version domains applied; a
            // publication never increments the logical step (P-006).
            CompositionState next = plan.After.With(revision: nextRevision, epoch: nextEpoch);
            SnapshotToken token = new SnapshotToken(World, nextEpoch, next.Step);

            // The fence is closed across the swap, so no callback is delivered against a half-published view.
            Callbacks.CloseFence();
            committed = next;
            ApplyActivations(plan, next);
            CleanupReport cleanup = RetireRemoved(plan);
            int ready = PublishStagedResources(operation);
            Callbacks.OpenFence();
            PublicationCount++;
            ResourceGatesOpened += ready;

            Outcome outcome = cleanup.HasCleanupErrors ? Outcome.PublishedWithCleanupErrors : Outcome.Published;
            DiagnosticCode code = cleanup.HasCleanupErrors ? DiagnosticCode.ResourceUnavailable : DiagnosticCode.None;
            ledger.Settle(operation, outcome, code, nextRevision, nextEpoch, token, next.Step);

            return new PublishedOperation(operation, outcome, code, token, cleanup);
        }

        /// <summary>
        /// Cancels one operation against the serialized cutoff: a pending operation is `Cancelled` with no new
        /// epoch and its staged resources are released; anything already published is `TooLate` (P-051, O-18).
        /// The cancellation's own identity is the caller's; the lane records the target's terminal result.
        /// </summary>
        public CancelOutcome Cancel(OperationId cancellationOperation, OperationId target)
        {
            _ = cancellationOperation;
            CancelOutcome outcome = ledger.Cancel(target);
            if (outcome == CancelOutcome.Cancelled)
            {
                ReleaseStagedResources(target);
                RebuildStaged();
            }

            return outcome;
        }

        /// <summary>
        /// Stages one managed resource for an admitted operation. The lease is acquired behind a closed gate and
        /// nothing observes it until publication (P-029); a failed preparation releases the earlier ones.
        /// </summary>
        public bool StageResource(
            OperationId operation,
            PluginInstanceId owner,
            ResourceKey resource,
            FrozenPayload config,
            IReadOnlyList<ResourceKey>? dependencies,
            out DiagnosticCode code)
        {
            code = DiagnosticCode.None;
            OperationLedger.LedgerRow? row = ledger.RowOf(operation);
            if (row == null || row.Phase != LedgerPhase.Pending || row.Plan == null || !row.Plan.Succeeded)
            {
                code = DiagnosticCode.StalePlan;
                return false;
            }

            if (resourceFactory == null)
            {
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            if (!stagedResources.TryGetValue(operation, out ResourcePreparationSet? set) || set == null)
            {
                set = new ResourcePreparationSet(resourceFactory, resources, World, owner);
                stagedResources.Add(operation, set);
            }

            InstallationGeneration generation = InstallationGeneration.First;
            ActivationEpoch epoch = ActivationEpoch.First;
            if (committed.TryGetInstall(owner, out InstallEntry? entry) && entry != null)
            {
                generation = entry.Record.Generation;
                epoch = entry.Record.ActivationEpoch;
            }

            AsyncWorkToken token = new AsyncWorkToken(operation, owner, generation, epoch, (uint)set.Count);
            if (!set.TryPrepare(resource, token, config, dependencies, out Id128 leaseId, out code))
            {
                // A failed preparation releases the earlier staged acquisitions in reverse order (P-029).
                set.ReleaseStaged();
                stagedResources.Remove(operation);
                _ = leaseId;
                return false;
            }

            _ = leaseId;
            return true;
        }

        /// <summary>Staged leases of one operation, for inspection before publication (P-029, GC-004 DoD).</summary>
        public IReadOnlyList<StagedLease> StagedLeases(OperationId operation) =>
            stagedResources.TryGetValue(operation, out ResourcePreparationSet? set) && set != null
                ? set.Staged()
                : Array.Empty<StagedLease>();

        /// <summary>Terminal result of one operation with its cleanup and quarantine references (05 s4).</summary>
        public OperationResult? ResultOf(OperationId operation)
        {
            OperationLedger.LedgerRow? row = ledger.RowOf(operation);
            if (row == null || row.Phase != LedgerPhase.Settled)
            {
                return null;
            }

            List<Id128> cleanup = new List<Id128>();
            List<Id128> quarantine = new List<Id128>();
            IReadOnlyList<WorldResourceRecord> records = resources.Records();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].State == ResourceRetirementState.Quarantined)
                {
                    quarantine.Add(records[i].ResourceId);
                }
                else if (records[i].State == ResourceRetirementState.Failed)
                {
                    cleanup.Add(records[i].ResourceId);
                }
            }

            return new OperationResult(
                operation,
                row.Outcome,
                row.Code,
                row.PublishedSnapshot,
                row.Plan != null ? row.Plan.BaseRevision : committed.Revision,
                row.PublishedRevision,
                row.Plan != null ? row.Plan.BaseEpoch : committed.Epoch,
                row.PublishedEpoch,
                null,
                cleanup,
                quarantine);
        }

        /// <summary>Advances this lane's retention window and the committed logical step (P-050, P-006).</summary>
        public int AdvanceSteps(LogicalStepId step)
        {
            committed = committed.With(step: step);
            staged = staged.With(step: step);
            return ledger.AdvanceRetention(step);
        }

        private OperationStatusHandle RefuseUndecodable(OperationId operation, ContentHash inputHash, DiagnosticCode code)
        {
            AdmissionResult refused = ledger.Admit(operation, inputHash, null);
            if (refused.Kind == AdmissionKind.Fresh)
            {
                ledger.Settle(operation, Outcome.Rejected, code, committed.Revision, committed.Epoch, null, committed.Step);
            }

            return refused.Handle;
        }

        private EditAdmission SubmitInternal(
            FrozenPayload frozen,
            CompositionEditPayload payload,
            OperationId operation,
            CompositionRevision expectedRevision)
        {
            ContentHash inputHash = CompositionEditApplier.InputHashOf(frozen);
            AdmissionResult admission = ledger.Admit(operation, inputHash, payload);
            if (!admission.Admitted)
            {
                return new EditAdmission(admission.Kind, admission.Code, admission.Handle, admission.Entry, null, null);
            }

            if (admission.Kind == AdmissionKind.Retransmission)
            {
                // A retransmission returns the original attempt's staged plan; it never starts new work (P-050).
                OperationLedger.LedgerRow? known = ledger.RowOf(operation);
                return new EditAdmission(admission.Kind, DiagnosticCode.None, admission.Handle, admission.Entry, known?.Plan, null);
            }

            CompositionEditPlan plan = CompositionEditApplier.Plan(staged, payload, operation, expectedRevision, inputHash, manifests);
            OperationLedger.LedgerRow? row = ledger.RowOf(operation);
            if (row != null)
            {
                row.Plan = plan;
            }

            if (plan.IsNoChange)
            {
                ledger.Settle(operation, Outcome.NoChange, DiagnosticCode.None, committed.Revision, committed.Epoch, null, committed.Step);
                return new EditAdmission(AdmissionKind.Fresh, DiagnosticCode.None, admission.Handle, RowEntry(operation), plan, plan.Diagnostics);
            }

            // The proposal is staged, not published: queries still see the committed revision (00 s9).
            staged = plan.After;
            return new EditAdmission(AdmissionKind.Fresh, DiagnosticCode.None, admission.Handle, RowEntry(operation), plan, plan.Diagnostics);
        }

        private OperationLedgerEntry? RowEntry(OperationId operation) => ledger.RowOf(operation)?.ToEntry();

        private void ApplyActivations(CompositionEditPlan plan, CompositionState next)
        {
            for (int i = 0; i < plan.RetiredInstances.Count; i++)
            {
                Callbacks.RetireActivation(plan.RetiredInstances[i]);
            }

            IReadOnlyList<InstallEntry> installs = next.Installs;
            for (int i = 0; i < installs.Count; i++)
            {
                InstallEntry entry = installs[i];
                if (entry.State == InstallationState.Active)
                {
                    Callbacks.RegisterActivation(entry.Instance, entry.Record.Generation, entry.Record.ActivationEpoch);
                }
            }
        }

        /// <summary>
        /// Retires the removed installations' leases in reverse activation order: consumers before providers
        /// (P-012) and, within one instance, reverse acquisition order (P-048). A release that throws keeps the
        /// resource retained, so the result can report `PublishedWithCleanupErrors` honestly.
        /// </summary>
        private CleanupReport RetireRemoved(CompositionEditPlan plan)
        {
            if (plan.RetiredInstances.Count == 0)
            {
                return CleanupReport.Empty;
            }

            // The plan already ordered the removals for teardown: consumers before providers (P-012), and
            // within one instance the ledger retires leases in reverse acquisition order (P-048).
            List<Id128> retired = new List<Id128>();
            List<Id128> failed = new List<Id128>();
            List<Id128> quarantined = new List<Id128>();
            for (int i = 0; i < plan.RetiredInstances.Count; i++)
            {
                CleanupReport report = resources.RetireInstance(plan.RetiredInstances[i], null);
                retired.AddRange(report.Retired);
                failed.AddRange(report.Failed);
                quarantined.AddRange(report.Quarantined);
            }

            return new CleanupReport(retired, failed, quarantined);
        }

        private int PublishStagedResources(OperationId operation)
        {
            if (!stagedResources.TryGetValue(operation, out ResourcePreparationSet? set) || set == null)
            {
                return 0;
            }

            // Publication is the only moment a staged gate opens; before it the callbacks stay inert (P-029).
            int ready = set.PublishReady();
            stagedResources.Remove(operation);
            return ready;
        }

        private void ReleaseStagedResources(OperationId operation)
        {
            if (stagedResources.TryGetValue(operation, out ResourcePreparationSet? set) && set != null)
            {
                set.ReleaseStaged();
                stagedResources.Remove(operation);
            }
        }

        /// <summary>
        /// Rebuilds the staged pipeline after a cancellation. The cancelled proposal is removed from the tail and
        /// the remaining ones are replanned against the committed definition, so nothing publishes on top of a
        /// plan that assumed a cancelled scope or installation exists (P-051).
        /// </summary>
        private void RebuildStaged()
        {
            CompositionState next = committed;
            IReadOnlyList<OperationId> pending = ledger.PendingInAdmissionOrder();
            for (int i = 0; i < pending.Count; i++)
            {
                OperationLedger.LedgerRow? row = ledger.RowOf(pending[i]);
                if (row == null || row.Payload == null)
                {
                    continue;
                }

                CompositionEditPlan replanned = CompositionEditApplier.Plan(
                    next,
                    row.Payload,
                    row.Operation,
                    committed.Revision,
                    row.InputHash,
                    manifests);

                row.Plan = replanned;
                if (!replanned.Succeeded)
                {
                    // A dependent proposal whose base disappeared is rejected rather than published blindly.
                    ledger.Settle(row.Operation, Outcome.Rejected, replanned.Code, committed.Revision, committed.Epoch, null, committed.Step);
                    ReleaseStagedResources(row.Operation);
                    continue;
                }

                if (!replanned.IsNoChange)
                {
                    next = replanned.After;
                }
            }

            staged = next;
        }
    }
}
