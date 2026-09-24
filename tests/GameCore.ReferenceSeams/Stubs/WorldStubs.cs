// Test-only deterministic stub (namespace GameCore.TestFixtures) for the W0 reference seam.
// World host, guarded execution driver, resource/job ledger and lifecycle observer doubles for W1 peers.
// Every outcome is reproducible from call order; no clock, thread or worker index participates (P-031, P-035,
// P-036, P-041, P-048).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.TestFixtures
{
    /// <summary>
    /// Deterministic world/job resource ledger: acquisitions, retirements and quarantine are explicit records,
    /// retirement can be requested in reverse acquisition order, and a quarantined resource stays reported
    /// instead of being silently released (P-048).
    /// </summary>
    public sealed class StubResourceLedger
    {
        private readonly Dictionary<Id128, WorldResourceRecord> resources = new Dictionary<Id128, WorldResourceRecord>();
        private readonly List<Id128> acquisitionOrder = new List<Id128>();
        private readonly Dictionary<Id128, JobLedgerRecord> jobs = new Dictionary<Id128, JobLedgerRecord>();
        private readonly List<Id128> jobOrder = new List<Id128>();

        public int RetireCount { get; private set; }

        public int QuarantineCount { get; private set; }

        public int DisposalRequestCount { get; private set; }

        public void Acquire(WorldResourceRecord record)
        {
            if (!resources.ContainsKey(record.ResourceId))
            {
                acquisitionOrder.Add(record.ResourceId);
            }

            resources[record.ResourceId] = record;
        }

        /// <summary>
        /// Marks one resource retired, honouring the single-retirement rule. A quarantined resource stays
        /// retained until its users end, so this refuses instead of freeing it (P-048).
        /// </summary>
        public bool Retire(Id128 resourceId)
        {
            if (!resources.TryGetValue(resourceId, out WorldResourceRecord record))
            {
                return false;
            }

            if (record.State == ResourceRetirementState.Retired || record.State == ResourceRetirementState.Quarantined)
            {
                // Disposing twice is neither attempted nor reported as a second retirement.
                return false;
            }

            RetireCount++;
            resources[resourceId] = WithState(record, ResourceRetirementState.Retired);
            return true;
        }

        /// <summary>Releases a quarantine once every user has ended, allowing a later retirement (P-048).</summary>
        public bool ReleaseQuarantine(Id128 resourceId)
        {
            if (!resources.TryGetValue(resourceId, out WorldResourceRecord record) ||
                record.State != ResourceRetirementState.Quarantined)
            {
                return false;
            }

            resources[resourceId] = WithState(record, ResourceRetirementState.Retiring);
            return true;
        }

        /// <summary>Retains a resource whose users have not ended; it is never freed on a timeout (P-048).</summary>
        public bool Quarantine(Id128 resourceId)
        {
            if (!resources.TryGetValue(resourceId, out WorldResourceRecord record))
            {
                return false;
            }

            QuarantineCount++;
            resources[resourceId] = WithState(record, ResourceRetirementState.Quarantined);
            return true;
        }

        /// <summary>
        /// Retires every resource of one instance in reverse acquisition order, attempting all independent
        /// cleanup and continuing past a failure (P-048).
        /// </summary>
        public IReadOnlyList<Id128> RetireInstanceInReverseAcquisitionOrder(PluginInstanceId instance, Id128 failingResourceId)
        {
            List<Id128> attempted = new List<Id128>();
            List<Id128> mine = new List<Id128>();
            for (int i = 0; i < acquisitionOrder.Count; i++)
            {
                if (resources.TryGetValue(acquisitionOrder[i], out WorldResourceRecord record) && record.Instance.Equals(instance))
                {
                    mine.Add(record.ResourceId);
                }
            }

            for (int i = mine.Count - 1; i >= 0; i--)
            {
                DisposalRequestCount++;
                attempted.Add(mine[i]);
                if (mine[i].Equals(failingResourceId))
                {
                    // The failed release stays retained and quarantined; the rest still attempts cleanup.
                    Quarantine(mine[i]);
                    continue;
                }

                Retire(mine[i]);
            }

            return attempted;
        }

        public void AddJob(JobLedgerRecord record)
        {
            if (!jobs.ContainsKey(record.JobId))
            {
                jobOrder.Add(record.JobId);
            }

            jobs[record.JobId] = record;
        }

        public bool CompleteJob(Id128 jobId)
        {
            if (!jobs.TryGetValue(jobId, out JobLedgerRecord record))
            {
                return false;
            }

            jobs[jobId] = new JobLedgerRecord(
                record.JobId,
                record.Stage,
                record.SystemKey,
                record.Epoch,
                record.Step,
                true,
                record.RetainedByQuarantine);
            return true;
        }

        /// <summary>
        /// Marks an unfinished job as retained by quarantine so its buffers are not released while it may still
        /// read world memory (P-048).
        /// </summary>
        public bool RetainJobByQuarantine(Id128 jobId)
        {
            if (!jobs.TryGetValue(jobId, out JobLedgerRecord record))
            {
                return false;
            }

            jobs[jobId] = new JobLedgerRecord(record.JobId, record.Stage, record.SystemKey, record.Epoch, record.Step, record.Completed, true);
            return true;
        }

        public int OutstandingJobCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < jobOrder.Count; i++)
                {
                    if (jobs.TryGetValue(jobOrder[i], out JobLedgerRecord record) && !record.Completed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public WorldResourceLedgerSnapshot Snapshot(WorldId world, AssemblyEpoch epoch)
        {
            List<WorldResourceRecord> orderedResources = new List<WorldResourceRecord>();
            WorldResourceRecord[] byId = new WorldResourceRecord[resources.Count];
            resources.Values.CopyTo(byId, 0);
            Array.Sort(byId, CompareResources);
            orderedResources.AddRange(byId);

            List<JobLedgerRecord> orderedJobs = new List<JobLedgerRecord>();
            for (int i = 0; i < jobOrder.Count; i++)
            {
                orderedJobs.Add(jobs[jobOrder[i]]);
            }

            ulong quarantinedBytes = 0UL;
            for (int i = 0; i < orderedResources.Count; i++)
            {
                if (orderedResources[i].State == ResourceRetirementState.Quarantined)
                {
                    quarantinedBytes += orderedResources[i].Bytes;
                }
            }

            return new WorldResourceLedgerSnapshot(world, epoch, orderedResources, orderedJobs, quarantinedBytes);
        }

        private static WorldResourceRecord WithState(WorldResourceRecord record, ResourceRetirementState state)
        {
            return new WorldResourceRecord(
                record.ResourceId,
                record.Kind,
                record.Key,
                record.Owner,
                record.Instance,
                state,
                record.DependsOn,
                record.AcquisitionOrdinal,
                record.Bytes);
        }

        private static int CompareResources(WorldResourceRecord left, WorldResourceRecord right) =>
            left.ResourceId.CompareTo(right.ResourceId);
    }

    /// <summary>Stub world host: one world, explicit lifecycle transitions and an inspectable resource ledger.</summary>
    public sealed class StubWorldHost : IWorldHost
    {
        private readonly StubResourceLedger ledger;
        private readonly uint maxStepsPerPump;
        private readonly TimeDebt retainedDebt = TimeDebt.Zero;

        public StubWorldHost(WorldId world, TemporalModel temporalModel, FixedStepSettings? fixedStep, StubResourceLedger ledger)
        {
            World = world;
            TemporalModel = temporalModel;
            FixedStep = fixedStep;
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            maxStepsPerPump = fixedStep?.MaxStepsPerPump ?? 1U;
            Lifecycle = WorldLifecycleState.Created;
        }

        public WorldId World { get; }

        public WorldLifecycleState Lifecycle { get; private set; }

        public AssemblyEpoch CurrentEpoch { get; private set; }

        public LogicalStepId CurrentStep { get; private set; }

        public TemporalModel TemporalModel { get; }

        public FixedStepSettings? FixedStep { get; }

        public uint MaxStepsPerPump => maxStepsPerPump;

        public TimeDebt RetainedDebt => retainedDebt;

        public WorldCreateResult Create(WorldCreateRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.World.Session.Equals(World.Session))
            {
                return new WorldCreateResult(false, World, Lifecycle, DiagnosticCode.StaleHandle, "Request names another world incarnation.");
            }

            if (request.Definition.IsDefault)
            {
                return new WorldCreateResult(false, World, Lifecycle, DiagnosticCode.MissingDependency, "A default world definition id is not a catalog identity.");
            }

            if (request.TemporalModel != TemporalModel)
            {
                return new WorldCreateResult(false, World, Lifecycle, DiagnosticCode.UnsupportedVersion, "Temporal model differs from the host configuration.");
            }

            if (request.TemporalModel == TemporalModel.FixedStep && (request.FixedStep == null || !request.FixedStep.IsValid))
            {
                return new WorldCreateResult(false, World, Lifecycle, DiagnosticCode.UnsupportedVersion, "Fixed-step configuration must declare positive duration, rate and catch-up limit.");
            }

            if (Lifecycle != WorldLifecycleState.Created)
            {
                return new WorldCreateResult(true, World, Lifecycle, DiagnosticCode.None, "World already created; repeated creation returns the same world.");
            }

            CurrentEpoch = AssemblyEpoch.First;
            CurrentStep = LogicalStepId.Zero;

            // Created worlds become Running only after the initial validated assembly publication (P-035).
            Lifecycle = WorldLifecycleState.Running;
            return new WorldCreateResult(true, World, Lifecycle, DiagnosticCode.None, "Created and published the initial assembly.");
        }

        public OperationResult SetRunState(OperationId operation, WorldLifecycleState destination)
        {
            if (destination == Lifecycle)
            {
                return new OperationResult(operation, Outcome.NoChange, DiagnosticCode.None, null);
            }

            if ((destination != WorldLifecycleState.Running && destination != WorldLifecycleState.Paused) ||
                (Lifecycle != WorldLifecycleState.Running && Lifecycle != WorldLifecycleState.Paused))
            {
                // Only Running<->Paused is a supported variant; anything else rejects before any change and is
                // reported with the protocol's unsupported-variant code (P-035, P-051).
                return new OperationResult(operation, Outcome.Rejected, DiagnosticCode.UnsupportedVersion, null);
            }

            Lifecycle = destination;

            // Pause/resume changes host status only: no epoch, revision or step movement (P-035, O-26).
            return new OperationResult(
                operation,
                Outcome.Published,
                DiagnosticCode.None,
                new SnapshotToken(World, CurrentEpoch, CurrentStep));
        }

        public OperationResult Stop(OperationId operation, string reason)
        {
            _ = reason;
            if (Lifecycle == WorldLifecycleState.Disposed)
            {
                return new OperationResult(operation, Outcome.NoChange, DiagnosticCode.None, null);
            }

            Lifecycle = WorldLifecycleState.Stopping;
            int outstanding = ledger.OutstandingJobCount;
            if (outstanding > 0)
            {
                // A stuck user keeps its resources pinned; the timeout only reports, it never authorizes free (P-048).
                return new OperationResult(operation, Outcome.Rejected, DiagnosticCode.TeardownBlocked, null);
            }

            Lifecycle = WorldLifecycleState.Disposed;
            return new OperationResult(operation, Outcome.Published, DiagnosticCode.None, null);
        }

        public WorldResourceLedgerSnapshot ReadResourceLedger() => ledger.Snapshot(World, CurrentEpoch);

        /// <summary>Marks the world faulted; a faulted world accepts no further admission (P-031).</summary>
        public void FaultForFixture() => Lifecycle = WorldLifecycleState.Faulted;

        internal void AdvanceTo(AssemblyEpoch epoch, LogicalStepId step)
        {
            CurrentEpoch = epoch;
            CurrentStep = step;
        }
    }

    /// <summary>
    /// Stub guarded execution driver. Advance retains unspent time as debt instead of lengthening a step, and
    /// dispatch stops at the first failing entry so no later system runs and nothing is published (P-031, P-036).
    /// </summary>
    public sealed class StubExecutionDriver : IExecutionDriver
    {
        private readonly StubWorldHost host;
        private readonly StubResourceLedger ledger;
        private readonly DeterministicIds jobIds = new DeterministicIds(0x6a6f6273UL);
        private readonly List<FactoryKey> dispatched = new List<FactoryKey>();

        public StubExecutionDriver(StubWorldHost host, StubResourceLedger ledger)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public WorldId World => host.World;

        public AssemblyEpoch CurrentEpoch => host.CurrentEpoch;

        public LogicalStepId CurrentStep => host.CurrentStep;

        public bool IsFaulted { get; private set; }

        public DiagnosticCode FaultCode { get; private set; }

        /// <summary>Entries that actually ran, in dispatch order; the failure test asserts what did not run.</summary>
        public IReadOnlyList<FactoryKey> DispatchedSystemKeys => dispatched;

        /// <summary>Dispatch index that must fail, or -1 for a clean run (P-031 fault injection).</summary>
        public int FaultAtDispatchIndex { get; set; } = -1;

        public StepAdvanceResult Advance(StepAdvanceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.World.Session.Equals(host.World.Session))
            {
                return Reject(request, DiagnosticCode.StaleHandle);
            }

            if (IsFaulted)
            {
                return Reject(request, DiagnosticCode.ApplyFault);
            }

            if (!request.BaseEpoch.Equals(host.CurrentEpoch) || !request.ExpectedStep.Equals(host.CurrentStep))
            {
                return Reject(request, DiagnosticCode.StalePlan);
            }

            if (request.RequestedSteps == 0UL)
            {
                // An idle command-driven world advances zero steps and publishes nothing (P-036).
                return new StepAdvanceResult(true, Outcome.NoChange, DiagnosticCode.None, host.CurrentStep, host.CurrentEpoch, request.RetainedDebt, null);
            }

            ulong admitted = request.RequestedSteps;
            if (host.TemporalModel == TemporalModel.FixedStep && admitted > host.MaxStepsPerPump)
            {
                admitted = host.MaxStepsPerPump;
            }

            ulong unspent = request.RequestedSteps - admitted;
            TimeDebt debt = request.RetainedDebt.Add(unspent, out bool accepted);
            if (!accepted)
            {
                return Reject(request, DiagnosticCode.BudgetExceeded);
            }

            ulong nextStep = host.CurrentStep.Value + admitted;
            host.AdvanceTo(host.CurrentEpoch, new LogicalStepId(nextStep));

            SnapshotToken token = new SnapshotToken(host.World, host.CurrentEpoch, host.CurrentStep);

            // A committed step moves the step counter only; the assembly epoch is unchanged (P-006).
            return new StepAdvanceResult(true, Outcome.Published, DiagnosticCode.None, host.CurrentStep, host.CurrentEpoch, debt, token);
        }

        public DispatchRunResult Dispatch(StageDispatchRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (IsFaulted)
            {
                return new DispatchRunResult(false, dispatched.Count, -1, DiagnosticCode.ApplyFault, default(FactoryKey), null);
            }

            if (!request.BaseEpoch.Equals(host.CurrentEpoch))
            {
                return new DispatchRunResult(false, dispatched.Count, -1, DiagnosticCode.StalePlan, default(FactoryKey), null);
            }

            if (!request.Table.IsWellFormed())
            {
                return new DispatchRunResult(false, dispatched.Count, -1, DiagnosticCode.AmbiguousOrder, default(FactoryKey), null);
            }

            for (int i = 0; i < request.Table.Entries.Count; i++)
            {
                SystemDispatchEntry entry = request.Table.Entries[i];
                Id128 jobId = jobIds.NextId();
                ledger.AddJob(new JobLedgerRecord(jobId, entry.Stage, entry.SystemKey, request.BaseEpoch, request.Step, false, false));

                if (i == FaultAtDispatchIndex)
                {
                    // Stop immediately: no later entry runs and no step publishes (P-031).
                    IsFaulted = true;
                    FaultCode = DiagnosticCode.ApplyFault;
                    ledger.RetainJobByQuarantine(jobId);
                    return new DispatchRunResult(false, i, i, DiagnosticCode.ApplyFault, entry.SystemKey, Remaining(request.Table, i));
                }

                dispatched.Add(entry.SystemKey);
                ledger.CompleteJob(jobId);
            }

            return new DispatchRunResult(true, dispatched.Count, -1, DiagnosticCode.None, default(FactoryKey), null);
        }

        private static IReadOnlyList<FactoryKey> Remaining(OrderedDispatchTable table, int failedIndex)
        {
            List<FactoryKey> remaining = new List<FactoryKey>();
            for (int i = failedIndex + 1; i < table.Entries.Count; i++)
            {
                remaining.Add(table.Entries[i].SystemKey);
            }

            return remaining;
        }

        private StepAdvanceResult Reject(StepAdvanceRequest request, DiagnosticCode code) =>
            new StepAdvanceResult(false, Outcome.Rejected, code, host.CurrentStep, host.CurrentEpoch, request.RetainedDebt, null);
    }

    /// <summary>Stub lifecycle observer recording changes and committed steps in delivery order (P-045).</summary>
    public sealed class StubWorldLifecycleObserver : IWorldLifecycleObserver
    {
        private readonly List<WorldLifecycleChange> changes = new List<WorldLifecycleChange>();
        private readonly List<StepCommitEvent> commits = new List<StepCommitEvent>();

        public IReadOnlyList<WorldLifecycleChange> Changes => changes;

        public IReadOnlyList<StepCommitEvent> Commits => commits;

        public void OnWorldLifecycleChanged(WorldLifecycleChange change)
        {
            if (change == null)
            {
                throw new ArgumentNullException(nameof(change));
            }

            changes.Add(change);
        }

        public void OnStepCommitted(StepCommitEvent committed)
        {
            if (committed == null)
            {
                throw new ArgumentNullException(nameof(committed));
            }

            commits.Add(committed);
        }
    }
}
