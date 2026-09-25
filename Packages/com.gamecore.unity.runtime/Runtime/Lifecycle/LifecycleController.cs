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
            Published = published;
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
        /// Settles retained references of one installation after its users ended, then retires them. This is the
        /// explicit, evidence-driven release P-048 allows; it is never reached by elapsing time.
        /// </summary>
        public CleanupReport ReleaseQuarantine(PluginInstanceId instance) => Lifecycle.ReleaseQuarantineFor(instance);

        /// <summary>Wires the job fence to the world's step jobs so a step boundary is a real completion point.</summary>
        public void TrackStepJob(
            Id128 jobId,
            PluginInstanceId instance,
            StageId stage,
            FactoryKey systemKey,
            LogicalStepId step,
            IReadOnlyList<Id128>? resourceIds,
            Unity.Jobs.JobHandle handle) =>
            JobFence.Track(jobId, instance, stage, systemKey, world.CurrentEpoch, step, resourceIds, handle);

        /// <summary>Declares (or refreshes) which owners an installation's ingress closes with (P-047).</summary>
        public void DeclareIngressOwners(InstallEntry entry) => Binding.DeclareIngressOwners(entry);

        public override string ToString() =>
            "lifecycleController(" + World.ToString() + ", requests="
            + RequestCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
