// GameCore.Composition — the P-048 teardown sequencer and its report.
//
// P-048 fixes the order and this type executes exactly that order, one named step at a time, so a caller reads
// which step produced which fact instead of trusting a summary:
//
//   1. close ingress            (P-047: new callbacks/commands of the retiring activation stop)
//   2. settle the current step  (P-030: reach the commit/fault boundary, never mid-step)
//   3. fence readers and jobs   (P-047: unfinished work keeps its resources)
//   4. retract the closure      (P-046/P-012: the active behavior this installation contributed)
//   5. retire resources         (P-048: reverse dependency order, reverse acquisition order within an instance)
//   6. admit quarantines        (06 s6: a bounded, observable registry, never a silent drop)
//
// Two protocol rules shape the code rather than the comments:
//
//   * **`Disposed` is not a claim.** An instance is only settled when every required resource is settled, so a
//     fence that is still held, a disposer that threw, or a full quarantine registry all keep the instance in
//     `Retiring` and report `TeardownBlocked` (P-048).
//   * **Nothing here reads a clock.** "Elapsed timeout only reports `TeardownBlocked`, never authorizes free" is
//     true by construction: there is no time source in this file.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>One step of the P-048 order, with its own outcome, so the order is inspectable.</summary>
    public sealed class TeardownStep
    {
        public TeardownStep(string name, bool completed, string detail)
        {
            Name = name;
            Completed = completed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Completed { get; }

        public string Detail { get; }

        public override string ToString() => Name + (Completed ? "=ok" : "=blocked") + (Detail.Length != 0 ? "(" + Detail + ")" : string.Empty);
    }

    /// <summary>
    /// Outcome of one teardown pass over one installation. `Disposed` is reported only when every step completed
    /// and every resource settled; otherwise the report says what held it (P-048).
    /// </summary>
    public sealed class TeardownReport
    {
        public TeardownReport(
            PluginInstanceId instance,
            OperationId operation,
            ActivationStamp stamp,
            IReadOnlyList<TeardownStep>? steps,
            CleanupReport cleanup,
            IReadOnlyList<Id128>? fencedResources,
            IReadOnlyList<Id128>? quarantined,
            IReadOnlyList<Id128>? failedReleases,
            ContributionRetraction retraction,
            int closedRoutes,
            int cancelledPending,
            int outstandingJobs,
            DiagnosticCode code,
            bool disposeSettled)
        {
            Instance = instance;
            Operation = operation;
            Stamp = stamp;
            Steps = ContractCollections.Freeze(steps);
            Cleanup = cleanup;
            FencedResources = ContractCollections.Freeze(fencedResources);
            Quarantined = ContractCollections.Freeze(quarantined);
            FailedReleases = ContractCollections.Freeze(failedReleases);
            Retraction = retraction;
            ClosedRoutes = closedRoutes;
            CancelledPending = cancelledPending;
            OutstandingJobs = outstandingJobs;
            Code = code;
            DisposeSettled = disposeSettled;
        }

        public PluginInstanceId Instance { get; }

        /// <summary>The operation whose publication caused this teardown (P-050).</summary>
        public OperationId Operation { get; }

        /// <summary>Generation and epoch the teardown acted on; a stale stamp tears down nothing (P-005).</summary>
        public ActivationStamp Stamp { get; }

        public IReadOnlyList<TeardownStep> Steps { get; }

        /// <summary>Retired, failed and quarantined leases of this pass (P-048).</summary>
        public CleanupReport Cleanup { get; }

        /// <summary>Resources reported by unfinished work at the fence step; each one is quarantined.</summary>
        public IReadOnlyList<Id128> FencedResources { get; }

        /// <summary>Retained references admitted to (or already in) the quarantine registry (06 s6).</summary>
        public IReadOnlyList<Id128> Quarantined { get; }

        /// <summary>Releases that threw; the resource stays retained, never reported as disposed (P-048).</summary>
        public IReadOnlyList<Id128> FailedReleases { get; }

        /// <summary>
        /// Step 4's full record: the rows this installation had attributed in the published assembly, whose command
        /// routes closed, and whether this pass published the retraction or the caller still owes that publication.
        /// </summary>
        public ContributionRetraction Retraction { get; }

        /// <summary>Derived rows actually removed by this pass; zero when the caller owns the publication.</summary>
        public int RetractedContributions => Retraction.RetractedRows;

        /// <summary>Command routes closed by step 1 (P-047).</summary>
        public int ClosedRoutes { get; }

        /// <summary>Accepted-but-unexecuted commands that finished `Cancelled(RouteRetired)` (P-047).</summary>
        public int CancelledPending { get; }

        /// <summary>Tracked jobs of this installation still unfinished at the fence step (P-047).</summary>
        public int OutstandingJobs { get; }

        /// <summary>`None` when everything settled; `TeardownBlocked` when something is still retained.</summary>
        public DiagnosticCode Code { get; }

        /// <summary>True only when the installation may become `Disposed`; a false value is never a false success.</summary>
        public bool DisposeSettled { get; }

        public bool Blocked => Code == DiagnosticCode.TeardownBlocked;

        public bool HasCleanupErrors => FailedReleases.Count != 0;

        /// <summary>True when a user (job/reader) still holds a resource: the buffer must not be released (P-047).</summary>
        public bool BlockedByJobFence => FencedResources.Count != 0;

        /// <summary>The step names that completed, in order; evidence that the P-048 order ran.</summary>
        public IReadOnlyList<string> CompletedSteps()
        {
            List<string> names = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i].Completed)
                {
                    names.Add(Steps[i].Name);
                }
            }

            return names;
        }

        public string Describe()
        {
            List<string> lines = new List<string>();
            lines.Add("instance=" + Instance.ToString());
            lines.Add("code=" + DiagnosticCodeText.Of(Code));
            lines.Add("disposeSettled=" + (DisposeSettled ? "True" : "False"));
            lines.Add("steps=" + string.Join(",", CompletedSteps()));
            lines.Add("retired=" + Cleanup.Retired.Count.ToString(CultureInfo.InvariantCulture));
            lines.Add("failed=" + FailedReleases.Count.ToString(CultureInfo.InvariantCulture));
            lines.Add("quarantined=" + Quarantined.Count.ToString(CultureInfo.InvariantCulture));
            lines.Add("fenced=" + FencedResources.Count.ToString(CultureInfo.InvariantCulture));
            lines.Add("outstandingJobs=" + OutstandingJobs.ToString(CultureInfo.InvariantCulture));
            lines.Add("retractedContributions=" + RetractedContributions.ToString(CultureInfo.InvariantCulture));
            lines.Add("attributedContributions=" + Retraction.AttributedRows.ToString(CultureInfo.InvariantCulture));
            lines.Add("retractionPublishedByBinding=" + (Retraction.PublishedByBinding ? "True" : "False"));
            lines.Add("closedRoutes=" + ClosedRoutes.ToString(CultureInfo.InvariantCulture));
            return string.Join("\n", lines);
        }

        public override string ToString() =>
            "teardown(" + Instance.ToString() + ", " + DiagnosticCodeText.Of(Code)
            + (DisposeSettled ? ", disposed" : ", retaining " + Quarantined.Count.ToString(CultureInfo.InvariantCulture)) + ")";
    }

    /// <summary>
    /// Executes the P-048 teardown order for one installation over the real resource, job, quarantine and world
    /// ledgers. It owns no state of its own: every fact it reports is read from those ledgers.
    /// </summary>
    public sealed class TeardownSequencer
    {
        private readonly ResourceLedger resources;
        private readonly JobFenceRegistry jobs;
        private readonly QuarantineRegistry quarantine;
        private readonly ICallbackGate callbacks;
        private readonly ILifecycleWorldBinding binding;

        public TeardownSequencer(
            ResourceLedger resources,
            JobFenceRegistry jobs,
            QuarantineRegistry quarantine,
            ICallbackGate callbacks,
            ILifecycleWorldBinding binding)
        {
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            this.quarantine = quarantine ?? throw new ArgumentNullException(nameof(quarantine));
            this.callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            this.binding = binding ?? throw new ArgumentNullException(nameof(binding));
        }

        /// <summary>Teardown passes executed; one per retired or unloaded installation.</summary>
        public int PassCount { get; private set; }

        /// <summary>Passes that could not settle every resource and therefore reported `TeardownBlocked`.</summary>
        public int BlockedCount { get; private set; }

        /// <summary>Passes that retracted at least one derived contribution.</summary>
        public int RetractionCount { get; private set; }

        /// <summary>
        /// Runs the whole P-048 order for one activation. <paramref name="settleStep"/> is false when the caller is
        /// already the publication boundary: the assembly is being published inside this call, so asking the world
        /// to settle a step would be asking it to wait for itself (P-030).
        /// </summary>
        public TeardownReport Unload(
            PluginInstanceId instance,
            ActivationStamp stamp,
            OperationId operation,
            bool settleStep)
        {
            PassCount++;
            List<TeardownStep> steps = new List<TeardownStep>();

            // 1. Close ingress: new managed callbacks and commands from this activation stop here (P-047). The gate
            //    is retired before any resource is touched, so a completion that arrives during teardown is
            //    discarded at dispatch and again at completion.
            LifecycleIngressClosure closure = binding.CloseIngress(instance, stamp);
            bool gateRetired = callbacks.RetireActivation(instance);
            steps.Add(new TeardownStep(
                "close-ingress",
                true,
                "routes=" + closure.ClosedRoutes.ToString(CultureInfo.InvariantCulture)
                + ", cancelledPending=" + closure.CancelledPending.ToString(CultureInfo.InvariantCulture)
                + ", gate=" + (gateRetired ? "retired" : "absent")));

            // 2. Settle the current step: in-flight stage/step execution reaches its commit/fault boundary before
            //    the assembly changes (P-030, P-047).
            bool stepSettled = true;
            if (settleStep)
            {
                LifecycleStepSettlement settlement = binding.SettleCurrentStep();
                stepSettled = settlement.Settled && !settlement.Faulted;
                steps.Add(new TeardownStep(
                    "settle-step",
                    stepSettled,
                    "committedStep=" + settlement.CommittedStep.Value.ToString(CultureInfo.InvariantCulture)
                    + (settlement.Detail.Length != 0 ? ", " + settlement.Detail : string.Empty)));
            }
            else
            {
                steps.Add(new TeardownStep("settle-step", true, "at-publication-boundary"));
            }

            // 3. Fence tracked readers and jobs. Whatever they may still reach is retained, not freed (P-047).
            IReadOnlyList<Id128> fenced = Merge(binding.FenceUsers(instance, stamp), jobs.OutstandingResourcesFor(instance));
            int outstanding = CountOutstanding(instance);
            steps.Add(new TeardownStep(
                "fence-users",
                true,
                "outstandingJobs=" + outstanding.ToString(CultureInfo.InvariantCulture)
                + ", fencedResources=" + fenced.Count.ToString(CultureInfo.InvariantCulture)));

            // 4. Retract the closure: the active behavior this installation contributed stops with this
            //    publication - its owners' command routes close here, and its derived binding rows retract with
            //    the assembly this publication carries. Its state slots are not this task's to migrate (GC-015).
            ContributionRetraction retraction = binding.RetractContributions(instance, stamp, operation);
            if (retraction.Retracted || retraction.AttributedRows != 0 || retraction.ClosedRoutes != 0)
            {
                RetractionCount++;
            }

            steps.Add(new TeardownStep("retract-closure", true, retraction.ToString()));

            // 5. Retire resources: reverse dependency order, consumers before providers, reverse acquisition order
            //    within the installation; a fenced resource is quarantined instead of released (P-048).
            CleanupReport cleanup = resources.RetireInstance(instance, fenced, stamp);
            steps.Add(new TeardownStep(
                "retire-resources",
                cleanup.Failed.Count == 0,
                "retired=" + cleanup.Retired.Count.ToString(CultureInfo.InvariantCulture)
                + ", failed=" + cleanup.Failed.Count.ToString(CultureInfo.InvariantCulture)
                + ", quarantined=" + cleanup.Quarantined.Count.ToString(CultureInfo.InvariantCulture)));

            // 6. Admit every still-retained reference to the bounded quarantine registry. A full registry refuses
            //    the admission and the instance stays `Retiring` rather than dropping the reference (06 s6).
            List<Id128> quarantined = Merge(cleanup.Quarantined, RemainingRetained(instance));
            bool quarantineRefused = false;
            for (int i = 0; i < quarantined.Count; i++)
            {
                QuarantineAdmission admission = quarantine.Admit(
                    quarantined[i],
                    KeyOf(quarantined[i]),
                    instance,
                    operation,
                    ResourceRetirementState.Quarantined,
                    BytesOf(quarantined[i]),
                    ReasonOf(quarantined[i], fenced, cleanup.Failed));
                if (!admission.Admitted && admission.Code == DiagnosticCode.TeardownBlocked)
                {
                    quarantineRefused = true;
                }
            }

            // The jobs that fenced these resources are recorded as retained, so a later safe teardown knows what
            // still holds them instead of guessing from elapsed time (P-048).
            for (int i = 0; i < fenced.Count; i++)
            {
                jobs.RetainByQuarantineFor(instance, fenced[i]);
            }

            steps.Add(new TeardownStep(
                "admit-quarantine",
                !quarantineRefused && quarantined.Count == 0,
                "registrySize=" + quarantine.Count.ToString(CultureInfo.InvariantCulture)
                + (quarantineRefused ? ", refused=exhausted" : string.Empty)));

            bool settleAll = stepSettled && !quarantineRefused && quarantined.Count == 0 && cleanup.Failed.Count == 0;
            DiagnosticCode code = settleAll
                ? DiagnosticCode.None
                : (quarantined.Count != 0 || quarantineRefused ? DiagnosticCode.TeardownBlocked : DiagnosticCode.ResourceUnavailable);
            if (!settleAll)
            {
                BlockedCount++;
            }

            return new TeardownReport(
                instance,
                operation,
                stamp,
                steps,
                cleanup,
                fenced,
                quarantined,
                cleanup.Failed,
                retraction,
                closure.ClosedRoutes,
                closure.CancelledPending,
                outstanding,
                code,
                settleAll);
        }

        /// <summary>
        /// Settles retained references of one installation whose users have ended, then asks the resource ledger
        /// for one more release attempt. This is the explicit retry P-048 allows, never a timeout-driven free.
        /// </summary>
        public CleanupReport ReleaseQuarantineFor(PluginInstanceId instance)
        {
            IReadOnlyList<QuarantinedResource> retained = quarantine.EntriesFor(instance);
            List<Id128> retired = new List<Id128>();
            List<Id128> failed = new List<Id128>();
            List<Id128> stillRetained = new List<Id128>();
            for (int i = 0; i < retained.Count; i++)
            {
                Id128 resourceId = retained[i].ResourceId;
                if (jobs.IsResourceFenced(resourceId))
                {
                    // A user still holds it: the reference stays, and it stays reported.
                    stillRetained.Add(resourceId);
                    continue;
                }

                if (resources.ReleaseQuarantine(resourceId) && resources.Retire(resourceId))
                {
                    quarantine.Release(resourceId);
                    retired.Add(resourceId);
                }
                else
                {
                    failed.Add(resourceId);
                    stillRetained.Add(resourceId);
                }
            }

            return new CleanupReport(retired, failed, stillRetained);
        }

        private int CountOutstanding(PluginInstanceId instance)
        {
            IReadOnlyList<TrackedJob> mine = jobs.JobsOf(instance);
            int count = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                if (!mine[i].Completed)
                {
                    count++;
                }
            }

            return count;
        }

        private List<Id128> RemainingRetained(PluginInstanceId instance)
        {
            List<Id128> retained = new List<Id128>();
            IReadOnlyList<WorldResourceRecord> records = resources.Records();
            for (int i = 0; i < records.Count; i++)
            {
                WorldResourceRecord record = records[i];
                if (record.Instance.Equals(instance) && record.IsRetained && record.State == ResourceRetirementState.Quarantined)
                {
                    retained.Add(record.ResourceId);
                }
            }

            return retained;
        }

        private ResourceKey KeyOf(Id128 resourceId) =>
            resources.TryGetRecord(resourceId, out WorldResourceRecord record) ? record.Key : default(ResourceKey);

        private ulong BytesOf(Id128 resourceId) =>
            resources.TryGetRecord(resourceId, out WorldResourceRecord record) ? record.Bytes : 0UL;

        private static string ReasonOf(Id128 resourceId, IReadOnlyList<Id128> fenced, IReadOnlyList<Id128> failed)
        {
            if (Contains(fenced, resourceId))
            {
                return "unfinished work still reaches this resource (P-047)";
            }

            return Contains(failed, resourceId)
                ? "the release threw; the reference stays retained (P-048)"
                : "retained by teardown (P-048)";
        }

        private static List<Id128> Merge(IReadOnlyList<Id128> left, IReadOnlyList<Id128> right)
        {
            List<Id128> merged = new List<Id128>(left.Count + right.Count);
            for (int i = 0; i < left.Count; i++)
            {
                if (!Contains(merged, left[i]))
                {
                    merged.Add(left[i]);
                }
            }

            for (int i = 0; i < right.Count; i++)
            {
                if (!Contains(merged, right[i]))
                {
                    merged.Add(right[i]);
                }
            }

            return merged;
        }

        private static bool Contains(IReadOnlyList<Id128> ids, Id128 candidate)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
