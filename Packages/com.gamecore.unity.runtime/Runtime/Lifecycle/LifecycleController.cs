// GameCore.Unity.Runtime.Lifecycle — the one place a caller drives a live installation lifecycle.
//
// P-046's transitions reach a live world through exactly three seams, and this type joins them so a caller never
// has to remember the order:
//
//   1. the composition control lane (`CompositionHost`), which plans a suspend/resume/unmount and publishes the
//      composition revision (P-027, P-051);
//   2. the world's lifecycle half (`UnityLifecycleWorldBinding`), which closes ingress, settles the step, fences
//      users and counts the retraction (P-047, P-048);
//   3. the derived assembly publication (`DerivedAssemblyPipeline`), which carries the retracted rows into the
//      world's published assembly (P-033).
//
// The sequence for a `Suspend`, `Resume` or `Unmount` is therefore fixed here once: submit against the committed
// revision, drain the lane, let the world publish the derived assembly for the same operation, then read the
// lifecycle report of that one publication. `Unload` is the P-048 order on its own, for a caller that removes an
// installation without a composition edit.
//
// Every method returns what the real modules reported; nothing here guesses a success.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Unity.Runtime.Lifecycle
{
    /// <summary>What one lifecycle request did, across the three seams it crossed.</summary>
    public sealed class LifecycleRequestReport
    {
        public LifecycleRequestReport(
            OperationId operation,
            EditAdmission admission,
            IReadOnlyList<PublishedOperation>? published,
            DerivedAssemblyReport? derived,
            LifecycleCommitReport? lifecycle,
            DiagnosticCode code,
            string detail)
        {
            Operation = operation;
            Admission = admission;
            Published = published ?? Array.Empty<PublishedOperation>();
            Derived = derived;
            Lifecycle = lifecycle;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public OperationId Operation { get; }

        /// <summary>The lane's admission decision; a refusal here means nothing else happened (P-050).</summary>
        public EditAdmission Admission { get; }

        /// <summary>What the lane's publication boundary produced for this operation; empty when nothing published.</summary>
        public IReadOnlyList<PublishedOperation> Published { get; }

        /// <summary>The derived assembly publication that carries the retracted rows, when one was requested (P-033).</summary>
        public DerivedAssemblyReport? Derived { get; }

        /// <summary>P-046 lifecycle facts of the publication: activation edges, teardowns, retractions, closure delta.</summary>
        public LifecycleCommitReport? Lifecycle { get; }

        /// <summary>`None` when the request completed; otherwise the first refusal it met.</summary>
        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>True when the lane admitted and published this operation and nothing refused it.</summary>
        public bool Succeeded =>
            Code == DiagnosticCode.None
            && Admission != null
            && Admission.Kind == AdmissionKind.Fresh
            && (Admission.Code == DiagnosticCode.None);

        /// <summary>Consumers that started waiting in this publication, because a required provider left (P-012).</summary>
        public IReadOnlyList<PluginInstanceId> WaitingConsumers =>
            Lifecycle != null ? Lifecycle.Closure.WaitingConsumers : Array.Empty<PluginInstanceId>();

        /// <summary>Consumers resumed by a returned provider in this publication (P-012).</summary>
        public IReadOnlyList<PluginInstanceId> ResumedConsumers =>
            Lifecycle != null ? Lifecycle.Closure.ResumedConsumers : Array.Empty<PluginInstanceId>();

        /// <summary>Resources still retained after this publication; each one is quarantined and reported (P-048).</summary>
        public IReadOnlyList<Id128> Retained => Lifecycle != null ? Lifecycle.Retained : Array.Empty<Id128>();

        public bool HasCleanupErrors => Lifecycle != null && Lifecycle.HasCleanupErrors;

        public string Describe()
        {
            List<string> lines = new List<string>();
            lines.Add("operation=" + Operation.ToString());
            lines.Add("code=" + DiagnosticCodeText.Of(Code));
            lines.Add("admission=" + Admission.Kind.ToString());
            lines.Add("published=" + Published.Count.ToString(CultureInfo.InvariantCulture));
            lines.Add("derived=" + (Derived != null ? Derived.Outcome.ToString() : "none"));
            lines.Add("detail=" + Detail);
            if (Lifecycle != null)
            {
                lines.Add("lifecycle=");
                lines.Add(Lifecycle.Closure.Describe());
            }

            return string.Join("\n", lines);
        }

        public override string ToString() =>
            "lifecycleRequest(" + Operation.ToString() + ", " + DiagnosticCodeText.Of(Code)
            + ", published=" + Published.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// What one world did about an unexpected provider failure. Exactly one of <see cref="Deactivated"/> and
    /// <see cref="WorldFaulted"/> is true, and the two are the two halves of P-012: the safe dependency-closure
    /// deactivation published, or the world stopped admission and faulted because it could not.
    /// </summary>
    public sealed class ProviderFailureOutcome
    {
        public ProviderFailureOutcome(
            PluginInstanceId instance,
            OperationId operation,
            bool deactivated,
            bool worldFaulted,
            ProviderFailureReport deactivation,
            DerivedAssemblyReport? derived,
            DiagnosticCode code,
            string detail)
        {
            Instance = instance;
            Operation = operation;
            Deactivated = deactivated;
            WorldFaulted = worldFaulted;
            Deactivation = deactivation;
            Derived = derived;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public PluginInstanceId Instance { get; }

        public OperationId Operation { get; }

        /// <summary>True when the failed provider and its dependents really left the assembly (P-012, first half).</summary>
        public bool Deactivated { get; }

        /// <summary>True when admission is closed and the world is Faulted (P-012, second half).</summary>
        public bool WorldFaulted { get; }

        /// <summary>The kernel's own report, including which consumers now wait and at which token.</summary>
        public ProviderFailureReport Deactivation { get; }

        /// <summary>The world's derived assembly publication, when one was requested and attempted.</summary>
        public DerivedAssemblyReport? Derived { get; }

        /// <summary>`None` when the deactivation published; otherwise the failure that faulted the world.</summary>
        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>The failed provider's dependents that now wait for a valid provider (P-012).</summary>
        public IReadOnlyList<PluginInstanceId> WaitingConsumers => Deactivation.WaitingConsumers;

        public string Describe() =>
            "providerFailure(" + Instance.ToString() + ", " + DiagnosticCodeText.Of(Code)
            + ", deactivated=" + (Deactivated ? "True" : "False")
            + ", worldFaulted=" + (WorldFaulted ? "True" : "False") + ")";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// Drives one world's installation lifecycle. One instance per world; it holds the lifecycle binding, the job
    /// fence bridge and the coordinator, and it owns no other state.
    /// </summary>
    public sealed class LifecycleController
    {
        private readonly UnityWorldHost world;
        private readonly CompositionHost lane;
        private readonly AssemblyPublisher publisher;
        private readonly DerivedAssemblyPipeline? pipeline;

        public LifecycleController(
            UnityWorldHost world,
            CompositionHost lane,
            AssemblyPublisher publisher,
            DerivedAssemblyPipeline? pipeline)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.lane = lane ?? throw new ArgumentNullException(nameof(lane));
            this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            this.pipeline = pipeline;

            if (!lane.World.Session.Equals(world.World.Session))
            {
                throw new ArgumentException(
                    "The control lane belongs to another world incarnation than the host (P-004).", nameof(lane));
            }

            Binding = new UnityLifecycleWorldBinding(world, publisher);
            JobFence = new LifecycleJobFence(lane.Lifecycle.Jobs);
            lane.Lifecycle.AttachWorldBinding(Binding);
            RefreshIngressOwners();
        }

        /// <summary>
        /// Declares, for every installation the lane has committed, the owners whose command routes close with it
        /// (P-047). A caller mounts through its own helper, so this is called again before every lifecycle submission
        /// and after every publication: an installation mounted since the last call gets its ingress declared without
        /// the caller having to remember, and a redeclaration is idempotent (the same entry yields the same owners).
        /// </summary>
        public int RefreshIngressOwners()
        {
            IReadOnlyList<InstallEntry> installs = lane.Committed.Installs;
            for (int i = 0; i < installs.Count; i++)
            {
                Binding.DeclareIngressOwners(installs[i]);
            }

            return installs.Count;
        }

        public WorldId World => world.World;

        /// <summary>P-046 lifecycle of this world; every decision and counter lives on the coordinator.</summary>
        public InstallationLifecycleCoordinator Lifecycle => lane.Lifecycle;

        /// <summary>The world-side half of the lifecycle seam (P-047, P-048).</summary>
        public UnityLifecycleWorldBinding Binding { get; }

        /// <summary>Bridge from scheduled Unity jobs to the composition job fences (P-041, P-047).</summary>
        public LifecycleJobFence JobFence { get; }

        /// <summary>Requests admitted and published through this controller.</summary>
        public int RequestCount { get; private set; }

        /// <summary>Requests the lane refused before publication (P-050).</summary>
        public int RefusedCount { get; private set; }

        /// <summary>Explicit unloads that ran the P-048 order without a composition edit.</summary>
        public int UnloadCount { get; private set; }

        /// <summary>
        /// Submits one lifecycle edit (suspend, resume or unmount) and completes the publication: the lane plans and
        /// publishes the composition revision, the coordinator commits the P-046 transitions of that publication,
        /// and - when a derived pipeline is attached - the world publishes the assembly that carries the retracted
        /// rows for the same operation (P-033, P-046).
        /// </summary>
        public LifecycleRequestReport Submit(CompositionEditPayload payload, OperationId operation)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            // The installation this request acts on may have been mounted since the last call, so its ingress owners
            // are declared before the close is attempted (P-047).
            RefreshIngressOwners();
            RequestCount++;
            EditAdmission admission = lane.SubmitEdit(payload, operation, lane.Committed.Revision);
            if (admission.Kind != AdmissionKind.Fresh || admission.Code != DiagnosticCode.None || admission.Plan == null)
            {
                RefusedCount++;
                DiagnosticCode refusal = admission.Code != DiagnosticCode.None
                    ? admission.Code
                    : (admission.Kind == AdmissionKind.Retransmission
                        ? DiagnosticCode.IdempotencyConflict
                        : DiagnosticCode.UnsupportedVersion);
                return new LifecycleRequestReport(
                    operation,
                    admission,
                    Array.Empty<PublishedOperation>(),
                    null,
                    null,
                    refusal,
                    "the composition lane refused the lifecycle request: " + admission.Kind);
            }

            IReadOnlyList<PublishedOperation> published = lane.Drain();
            RefreshIngressOwners();
            LifecycleCommitReport? lifecycle = null;
            for (int i = 0; i < published.Count; i++)
            {
                if (published[i].Operation.Equals(operation))
                {
                    lifecycle = published[i].Lifecycle;
                }
            }

            if (lifecycle == null)
            {
                RefusedCount++;
                return new LifecycleRequestReport(
                    operation,
                    admission,
                    published,
                    null,
                    null,
                    admission.Plan.Code != DiagnosticCode.None ? admission.Plan.Code : DiagnosticCode.StalePlan,
                    "the publication boundary produced no lifecycle report for this operation");
            }

            DerivedAssemblyReport? derived = null;
            if (pipeline != null)
            {
                // The world's half of the same publication: the retracted rows leave the published assembly here,
                // at the operation identity the composition publication already used (P-006, P-033).
                derived = pipeline.PublishDerived(operation);
            }

            DiagnosticCode code = derived != null && !derived.Succeeded ? derived.Code : DiagnosticCode.None;
            return new LifecycleRequestReport(
                operation,
                admission,
                published,
                derived,
                lifecycle,
                code,
                derived != null ? derived.Detail : "no derived pipeline attached; the caller publishes the assembly");
        }

        /// <summary>
        /// Runs the whole P-048 order for one installation without a composition edit: close ingress, settle the
        /// current step, fence users and jobs, retract the closure, retire resources in reverse order and admit
        /// quarantines. Used by an explicit unload and by world teardown.
        /// </summary>
        public TeardownReport Unload(PluginInstanceId instance, OperationId operation)
        {
            UnloadCount++;
            JobFence.CompleteAllBlocking(instance);
            return Lifecycle.Unload(instance, operation);
        }

        /// <summary>
        /// Settles retained references of one installation after its users ended, then retires them, and - when
        /// the release ended the last retention - completes the installation's own committed record to `Disposed`,
        /// the same edge a publication-time settle took for its settled teardowns. This is the explicit,
        /// evidence-driven release P-048 allows; it is never reached by elapsing time. An installation with
        /// nothing quarantined is left unchanged, and a record that still cannot settle is reported by the
        /// returned report's retained side, never as a false disposal.
        /// </summary>
        public CleanupReport ReleaseQuarantine(PluginInstanceId instance)
        {
            CleanupReport release = Lifecycle.ReleaseQuarantineFor(instance);
            if (release.Failed.Count == 0
                && release.Quarantined.Count == 0
                && lane.Committed.TryGetInstall(instance, out InstallEntry? entry)
                && entry != null
                && entry.State == InstallationState.Retiring)
            {
                // The explicit release ended the last retention, so the removal's own record can settle now; the
                // lane reports the refusal as a value instead of this method forcing it (P-048).
                lane.SettleRetiredInstall(instance);
            }

            return release;
        }

        /// <summary>
        /// P-012, in full, for one world: an existing Active provider failed unexpectedly.
        ///
        /// FIRST it attempts the safe dependency-closure deactivation through the kernel
        /// (<see cref="CompositionHost.FailActiveProvider"/>): one validated publication marks the provider Failed
        /// and moves every consumer that required it to <c>WaitingForDependencies</c> with its contribution
        /// retracted, so the failed provider is absent from the new revision and epoch and unrelated targets,
        /// stages and state keep running. When a derived pipeline is attached the world's assembly for that same
        /// operation is published too, exactly as <see cref="Submit"/> does it.
        ///
        /// ONLY IF that deactivation cannot publish does the second half apply: the world stops admission and
        /// enters the terminal fault state through the same fail-stop path a postwrite apply failure uses (P-031),
        /// which refuses new commands, publishes no further epoch or snapshot and lets no simulation resume. The
        /// old committed image stays inspectable and recovery is a checkpoint restore into a new session (P-049).
        ///
        /// There is no third outcome: either the deactivation published, or the world faulted. A timeout never
        /// stands in for either (P-012's "no timeout-based unsafe release").
        /// </summary>
        public ProviderFailureOutcome FailProvider(
            PluginInstanceId instance,
            OperationId operation,
            DiagnosticCode code,
            string detail)
        {
            // The deactivation attempt itself is a named boundary: an armed latch stands for "this publication
            // cannot happen", which is exactly P-012's second half. The reach and its classification exist only in
            // a compilation that defines the fault symbol, so a shipping build runs the plain attempt below.
            bool deactivationRefused = false;
            string deactivationRefusalDetail = string.Empty;
#if GAMECORE_FAULT_INJECTION
            try
            {
                FaultReach.Reach(
                    world.Faults,
                    FaultBoundary.ProviderDeactivationPublication,
                    operation,
                    default(ContentHash),
                    "the safe dependency-closure deactivation of " + instance.ToString() + " was injected to fail (P-012)");
            }
            catch (FaultInjectedException injected)
            {
                deactivationRefused = true;
                deactivationRefusalDetail = injected.Message;
            }
#endif

            ProviderFailureReport deactivation = deactivationRefused
                ? ProviderFailureRefused(instance, operation, code, detail, deactivationRefusalDetail)
                : lane.FailActiveProvider(instance, operation, code, detail);
            ProviderFailureCount++;

            if (deactivation.Deactivated)
            {
                // The world's half of the same publication, mirroring `Submit`: the retracted rows leave the
                // published assembly at the operation identity the composition publication used (P-006, P-033).
                DerivedAssemblyReport? derived = pipeline != null ? pipeline.PublishDerived(operation) : null;
                bool derivedFailed = derived != null && !derived.Succeeded;
                if (derivedFailed)
                {
                    // The composition revision published but the world could not carry the retraction into its own
                    // assembly. That is a postwrite failure of this publication, so it takes the same fail-stop
                    // path rather than leaving a half-published world (P-030, P-031).
                    world.EnterFaulted(derived!.Code, "the deactivation published but its derived assembly did not: " + derived.Detail);
                    FaultedProviderCount++;
                    return new ProviderFailureOutcome(instance, operation, false, true, deactivation, derived,
                        derived.Code,
                        "the safe deactivation published in the composition revision, but the world's derived "
                        + "assembly did not; admission is closed and the world is Faulted (P-012, P-031)");
                }

                RefreshIngressOwners();
                return new ProviderFailureOutcome(instance, operation, true, false, deactivation, derived,
                    DiagnosticCode.None,
                    "the safe dependency-closure deactivation published: the failed provider and its required "
                    + "dependents left the assembly in one epoch and the world keeps running (P-012)");
            }

            // The deactivation could not publish. P-012: the world stops admission and faults; the failed provider
            // is never kept active and no partial publication stands in for the deactivation.
            DiagnosticCode faultCode = deactivation.Code != DiagnosticCode.None
                ? deactivation.Code
                : DiagnosticCode.ProviderFailed;
            // P-031/P-052: the world's own fault record says why it faulted, so an operator reading the host sees
            // the refusal that made the deactivation impossible, not an empty string. The caller's detail is kept
            // inside the deactivation report, where the caused-by provenance belongs.
            world.EnterFaulted(
                faultCode,
                "the safe dependency-closure deactivation could not publish, so the failed provider is not kept "
                + "active and the world faults (P-012). " + deactivation.Detail);
            FaultedProviderCount++;
            return new ProviderFailureOutcome(instance, operation, false, true, deactivation, null,
                deactivation.Code != DiagnosticCode.None ? deactivation.Code : DiagnosticCode.ProviderFailed,
                "the safe dependency-closure deactivation could not publish, so admission is closed and the world "
                + "is Faulted; the failed provider is not kept active (P-012). " + deactivation.Detail);
        }

        /// <summary>Provider failures this controller was asked to handle, published or faulting.</summary>
        public int ProviderFailureCount { get; private set; }

        /// <summary>Provider failures whose deactivation could not publish, so the world faulted (P-012).</summary>
        public int FaultedProviderCount { get; private set; }

        /// <summary>
        /// The refusal a deactivation gets when the publication attempt itself failed (P-012). It is built through
        /// the kernel's own report shape so both halves of the requirement are reported identically, and it never
        /// claims a token or a publication: nothing changed.
        /// </summary>
        private static ProviderFailureReport ProviderFailureRefused(
            PluginInstanceId instance,
            OperationId operation,
            DiagnosticCode code,
            string detail,
            string refusalDetail) =>
            new ProviderFailureReport(
                instance,
                operation,
                false,
                null,
                null,
                null,
                DiagnosticCode.ProviderFailed,
                "the safe dependency-closure deactivation could not publish, so the failed provider would still hold "
                + "its place in the committed assembly; the world must stop admission and fault (P-012). Refusal: "
                + refusalDetail + ". Reported cause: " + DiagnosticCodeText.Of(code) + ": " + detail);

        /// <summary>Wires the job fence to the world's step jobs so a step boundary is a real completion point.</summary>
        public void TrackStepJob(
            Id128 jobId,
            PluginInstanceId instance,
            StageId stage,
            FactoryKey systemKey,
            LogicalStepId step,
            IReadOnlyList<Id128>? resourceIds,
            global::Unity.Jobs.JobHandle handle) =>
            JobFence.Track(jobId, instance, stage, systemKey, world.CurrentEpoch, step, resourceIds, handle);

        /// <summary>Declares (or refreshes) which owners an installation's ingress closes with (P-047).</summary>
        public void DeclareIngressOwners(InstallEntry entry) => Binding.DeclareIngressOwners(entry);

        public override string ToString() =>
            "lifecycleController(" + World.ToString() + ", requests="
            + RequestCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
