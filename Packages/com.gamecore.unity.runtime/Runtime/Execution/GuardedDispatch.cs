#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Runtime
{
    /// <summary>
    /// Host-side hooks of the guarded dispatcher. The group invokes systems and tracks fences; the driver owns the
    /// fault latch, the job ledger records and the step's publication boundary (04 s4, P-031, P-041).
    /// </summary>
    public interface IGuardedDispatchSink
    {
        WorldId World { get; }

        AssemblyEpoch CurrentEpoch { get; }

        LogicalStepId CurrentStep { get; }

        /// <summary>True once a post-write failure latched (P-031); a latched sink refuses further dispatch.</summary>
        bool FaultLatched { get; }

        /// <summary>Records a handle produced by one entry for the current step's job set (P-041).</summary>
        Id128 RecordStepJob(JobHandle handle, StageId stage, FactoryKey systemKey, AssemblyEpoch epoch, LogicalStepId step);

        /// <summary>The step fence completed, so every recorded handle for this step is complete (P-044).</summary>
        void CompleteStepJobs();

        /// <summary>Retains the step's unfinished jobs after a failure so teardown can settle them (P-047, P-048).</summary>
        void RetainStepJobsByQuarantine();

        /// <summary>Latches the world fault after a dispatch failure; later entries must not run (P-031).</summary>
        void OnDispatchFaulted(DiagnosticCode code, FactoryKey failingSystemKey, string detail);
    }

    /// <summary>
    /// Host-owned native fence table indexed by stage (P-041). Every scheduled job returns a handle; a stage's
    /// output replaces its fence with the combination of its incoming edges and its own produced handles, which
    /// prevents a stale prior-step handle or an early-return path from dropping a producer dependency (04 s4).
    /// </summary>
    public sealed class NativeFenceTable : IDisposable
    {
        private NativeArray<JobHandle> stageFences;
        private NativeArray<JobHandle> combineScratch;
        private JobHandle combined;
        private bool disposed;

        public NativeFenceTable(int stageCount)
        {
            if (stageCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stageCount), "A fence table cannot be negative.");
            }

            StageCount = stageCount;
            stageFences = new NativeArray<JobHandle>(stageCount, Allocator.Persistent);
            combineScratch = new NativeArray<JobHandle>(stageCount, Allocator.Persistent);
        }

        public int StageCount { get; }

        public bool IsCreated => !disposed;

        /// <summary>Fence of every stage output recorded this step; the step's publication fence.</summary>
        public JobHandle Combined => combined;

        /// <summary>Forwards one stage's output: dependent stages receive this fence (P-041).</summary>
        public void Store(int stageIndex, JobHandle handle)
        {
            RequireStage(stageIndex);
            stageFences[stageIndex] = handle;
            combined = JobHandle.CombineDependencies(combined, handle);
        }

        /// <summary>Combination of the declared predecessor stages of one entry.</summary>
        public JobHandle CombineIncoming(IReadOnlyList<int> predecessorStages)
        {
            if (predecessorStages == null)
            {
                throw new ArgumentNullException(nameof(predecessorStages));
            }

            if (predecessorStages.Count == 0)
            {
                return default(JobHandle);
            }

            if (predecessorStages.Count == 1)
            {
                RequireStage(predecessorStages[0]);
                return stageFences[predecessorStages[0]];
            }

            for (int i = 0; i < predecessorStages.Count; i++)
            {
                RequireStage(predecessorStages[i]);
                combineScratch[i] = stageFences[predecessorStages[i]];
            }

            return JobHandle.CombineDependencies(combineScratch.GetSubArray(0, predecessorStages.Count));
        }

        /// <summary>Clears every fence at step admission; the table is never captured by a running worker.</summary>
        public void Reset()
        {
            for (int i = 0; i < stageFences.Length; i++)
            {
                stageFences[i] = default(JobHandle);
            }

            combined = default(JobHandle);
        }

        /// <summary>Completes the step fence and clears the table. False when a job completion threw.</summary>
        public bool CompleteAndReset()
        {
            bool ok = true;
            try
            {
                combined.Complete();
            }
            catch (Exception)
            {
                ok = false;
            }

            Reset();
            return ok;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (stageFences.IsCreated)
            {
                stageFences.Dispose();
            }

            if (combineScratch.IsCreated)
            {
                combineScratch.Dispose();
            }
        }

        private void RequireStage(int stageIndex)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NativeFenceTable));
            }

            if (stageIndex < 0 || stageIndex >= StageCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stageIndex),
                    "Stage index " + stageIndex.ToString(CultureInfo.InvariantCulture)
                    + " is outside the fence table of " + StageCount.ToString(CultureInfo.InvariantCulture) + " stages.");
            }
        }
    }

    /// <summary>
    /// Handles retained because a step failed before publication. A faulted world keeps them tracked: its buffers
    /// remain valid until every user ends, and elapsed time never authorizes freeing them (P-047, P-048).
    /// </summary>
    public sealed class RetainedJobHandles : IDisposable
    {
        private NativeArray<Id128> jobIds;
        private NativeArray<JobHandle> handles;
        private int count;
        private bool disposed;

        public RetainedJobHandles(int initialCapacity)
        {
            int capacity = initialCapacity < 1 ? 1 : initialCapacity;
            jobIds = new NativeArray<Id128>(capacity, Allocator.Persistent);
            handles = new NativeArray<JobHandle>(capacity, Allocator.Persistent);
        }

        public int Count => count;

        public int Capacity => jobIds.Length;

        public int CompletionFailureCount { get; private set; }

        public bool IsCreated => !disposed;

        public void Add(Id128 jobId, JobHandle handle)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RetainedJobHandles));
            }

            EnsureCapacity(count + 1);
            jobIds[count] = jobId;
            handles[count] = handle;
            count++;
        }

        public Id128 JobIdAt(int index)
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return jobIds[index];
        }

        public JobHandle HandleAt(int index)
        {
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return handles[index];
        }

        /// <summary>
        /// Settles every retained handle and marks its ledger record completed. A completion that throws is counted
        /// and its record stays retained behind quarantine instead of being reported as a successful disposal
        /// (P-048).
        /// </summary>
        public int CompleteAll(WorldResourceLedger ledger)
        {
            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            int completed = 0;
            for (int i = 0; i < count; i++)
            {
                JobHandle handle = handles[i];
                try
                {
                    handle.Complete();
                    ledger.CompleteJob(jobIds[i]);
                    completed++;
                }
                catch (Exception)
                {
                    CompletionFailureCount++;
                    ledger.RetainJobByQuarantine(jobIds[i]);
                }
            }

            count = 0;
            return completed;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (jobIds.IsCreated)
            {
                jobIds.Dispose();
            }

            if (handles.IsCreated)
            {
                handles.Dispose();
            }
        }

        private void EnsureCapacity(int required)
        {
            if (required <= jobIds.Length)
            {
                return;
            }

            int capacity = jobIds.Length;
            while (capacity < required)
            {
                capacity *= 2;
            }

            var grownIds = new NativeArray<Id128>(capacity, Allocator.Persistent);
            var grownHandles = new NativeArray<JobHandle>(capacity, Allocator.Persistent);
            for (int i = 0; i < count; i++)
            {
                grownIds[i] = jobIds[i];
                grownHandles[i] = handles[i];
            }

            jobIds.Dispose();
            handles.Dispose();
            jobIds = grownIds;
            handles = grownHandles;
        }
    }

    /// <summary>
    /// Base class of every GameCore adapter group: ingress, step and output. It sets
    /// <c>EnableSystemSorting = false</c> and its <c>OnUpdate</c> iterates an explicit ordered dispatch table, so a
    /// managed or unmanaged system exception stops the remaining systems instead of being logged and skipped by the
    /// stock group loop (04 s3, 04 s4, P-031).
    /// </summary>
    public abstract partial class GuardedSystemGroup : ComponentSystemGroup
    {
        private GuardedDispatchPlan plan = GuardedDispatchPlan.Empty;
        private OrderedDispatchTable boundTable = GuardedDispatchPlan.Empty.ToOrderedTable(AssemblyEpoch.Zero);
        private ISystemDispatchCatalog? catalog;
        private IGuardedDispatchSink? sink;

        private readonly HashSet<FactoryKey> dispatchedSet = new HashSet<FactoryKey>();
        private FactoryKey[] dispatchedKeys = Array.Empty<FactoryKey>();
        private bool[] stageDispatched = Array.Empty<bool>();
        private readonly List<FactoryKey> unreachedKeys = new List<FactoryKey>();

        private int dispatchedCount;
        private NativeFenceTable? fences;

        /// <summary>Number of guarded dispatch runs this group executed.</summary>
        public int DispatchRunCount { get; private set; }

        /// <summary>Entries dispatched by the most recent run.</summary>
        public int LastDispatchedCount { get; private set; }

        /// <summary>Entries dispatched since creation; the "no system double-updates" counter (TEST-018).</summary>
        public int TotalDispatchedCount { get; private set; }

        /// <summary>Refused dispatches: the group was unbound or the sink already latched a fault.</summary>
        public int RefusedDispatchCount { get; private set; }

        public GuardedDispatchPlan InstalledPlan => plan;

        public bool IsBound => catalog != null && sink != null && fences != null;

        public AssemblyEpoch BoundEpoch => boundTable.Epoch;

        /// <summary>Native fence table of this group; owned here and disposed with the group (P-041).</summary>
        public NativeFenceTable? Fences => fences;

        internal OrderedDispatchTable BoundTable => boundTable;

        internal HashSet<FactoryKey> DispatchedKeySet => dispatchedSet;

        internal bool[] StageDispatchedFlags => stageDispatched;

        /// <summary>
        /// Installs the epoch's compiled order, its key resolver and the host sink. Binding happens at the assembly
        /// fence; the group never sorts, so the order in <paramref name="newPlan"/> is the executed order.
        /// </summary>
        public void Bind(
            GuardedDispatchPlan newPlan,
            AssemblyEpoch epoch,
            ISystemDispatchCatalog newCatalog,
            IGuardedDispatchSink newSink)
        {
            if (newPlan == null)
            {
                throw new ArgumentNullException(nameof(newPlan));
            }

            if (newCatalog == null)
            {
                throw new ArgumentNullException(nameof(newCatalog));
            }

            if (newSink == null)
            {
                throw new ArgumentNullException(nameof(newSink));
            }

            if (!newPlan.TryValidate(out DiagnosticCode code, out string detail))
            {
                throw new ArgumentException(
                    "The dispatch table is not well formed: " + detail + " (" + code + ").", nameof(newPlan));
            }

            Unbind();

            plan = newPlan;
            boundTable = newPlan.ToOrderedTable(epoch);
            catalog = newCatalog;
            sink = newSink;
            dispatchedKeys = new FactoryKey[newPlan.Entries.Count];
            stageDispatched = new bool[newPlan.StageCount];
            fences = new NativeFenceTable(newPlan.StageCount);
        }

        /// <summary>Removes the binding and releases the fence table. Safe to call repeatedly.</summary>
        public void Unbind()
        {
            catalog = null;
            sink = null;
            plan = GuardedDispatchPlan.Empty;
            boundTable = GuardedDispatchPlan.Empty.ToOrderedTable(AssemblyEpoch.Zero);
            dispatchedKeys = Array.Empty<FactoryKey>();
            stageDispatched = Array.Empty<bool>();
            dispatchedSet.Clear();
            unreachedKeys.Clear();

            if (fences != null)
            {
                fences.Dispose();
                fences = null;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Never sort: static UpdateBefore/UpdateAfter attributes cannot express a newly mounted runtime stage
            // graph, so the compiled order installed at the assembly fence is authoritative (04 s4).
            EnableSystemSorting = false;
        }

        protected override void OnDestroy()
        {
            Unbind();
            base.OnDestroy();
        }

        /// <summary>
        /// Guarded dispatch of the bound table. This deliberately does not call <c>base.OnUpdate()</c>: the stock
        /// group loop catches a system exception, logs it and continues to the next system, which would hide an
        /// authoritative post-write failure (04 s4, P-031).
        /// </summary>
        protected override void OnUpdate()
        {
            IGuardedDispatchSink? currentSink = sink;
            if (currentSink == null || catalog == null || fences == null)
            {
                RefusedDispatchCount++;
                return;
            }

            DispatchRun(currentSink.CurrentEpoch, currentSink.CurrentStep, boundTable);
        }

        /// <summary>Seam entry point: dispatches one step's table through the guarded ordered dispatcher.</summary>
        public DispatchRunResult DispatchOne(StageDispatchRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (catalog == null || sink == null || fences == null)
            {
                return new DispatchRunResult(false, 0, -1, DiagnosticCode.MissingDependency, default(FactoryKey), null);
            }

            if (!request.World.Session.Equals(sink.World.Session))
            {
                return new DispatchRunResult(false, 0, -1, DiagnosticCode.StaleHandle, default(FactoryKey), null);
            }

            if (!request.BaseEpoch.Equals(boundTable.Epoch))
            {
                return new DispatchRunResult(false, 0, -1, DiagnosticCode.StalePlan, default(FactoryKey), null);
            }

            if (!request.Table.IsWellFormed())
            {
                return new DispatchRunResult(false, 0, -1, DiagnosticCode.AmbiguousOrder, default(FactoryKey), null);
            }

            return DispatchRun(request.BaseEpoch, request.Step, boundTable);
        }

        internal DispatchRunResult DispatchRun(AssemblyEpoch epoch, LogicalStepId step, OrderedDispatchTable table)
        {
            IGuardedDispatchSink currentSink = sink!;
            ISystemDispatchCatalog currentCatalog = catalog!;
            NativeFenceTable currentFences = fences!;

            if (currentSink.FaultLatched)
            {
                RefusedDispatchCount++;
                return new DispatchRunResult(false, 0, -1, DiagnosticCode.ApplyFault, default(FactoryKey), null);
            }

            if (!table.IsWellFormed())
            {
                return new DispatchRunResult(false, 0, -1, DiagnosticCode.AmbiguousOrder, default(FactoryKey), null);
            }

            DispatchRunCount++;
            dispatchedCount = 0;
            dispatchedSet.Clear();
            for (int i = 0; i < stageDispatched.Length; i++)
            {
                stageDispatched[i] = false;
            }

            currentFences.Reset();

            IReadOnlyList<GuardedDispatchEntry> entries = plan.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                GuardedDispatchEntry entry = entries[i];

                if (!currentCatalog.TryResolve(entry.SystemKey, out SystemDispatchTarget target) || !target.IsResolved)
                {
                    // A missing registration is an assembly defect, and earlier entries of this step may already
                    // have written authoritative state, so the step can no longer be trusted or published
                    // (P-031, 04 s8).
                    return FailStep(
                        currentSink, entries, i, entry, DiagnosticCode.MissingDependency,
                        "No generated registration resolves this system key.");
                }

                JobHandle incoming;
                try
                {
                    incoming = currentFences.CombineIncoming(entry.PredecessorStages);
                }
                catch (Exception exception)
                {
                    return FailStep(currentSink, entries, i, entry, DiagnosticCode.ApplyFault, Describe(exception));
                }

                JobHandle beforeHandle = default(JobHandle);
                JobHandle output = default(JobHandle);
                bool shouldRun = false;
                bool failed = false;
                string failureDetail = string.Empty;

                try
                {
                    // SystemState is a ref struct holding the live state inline: a value copy would silently lose
                    // the dependency write-back, so dispatch always works through a ref local.
                    ref SystemState state = ref World.Unmanaged.ResolveSystemStateRef(ResolveHandle(target));
                    beforeHandle = state.Dependency;

                    // Every scheduled job returns a handle: the incoming stage edges combine with this system's own
                    // dependency so a producer can never be dropped or run after its consumer (04 s4, P-041).
                    state.Dependency = JobHandle.CombineDependencies(beforeHandle, incoming);
                    shouldRun = state.Enabled && state.ShouldRunSystem();

                    // The invocation is separated from the decision so no ref local is read inside a catch clause.
                    Exception? invocationFailure = null;
                    try
                    {
                        if (shouldRun)
                        {
                            Invoke(target);
                            dispatchedSet.Add(entry.SystemKey);
                            dispatchedKeys[dispatchedCount] = entry.SystemKey;
                            dispatchedCount++;
                        }
                    }
                    catch (Exception exception)
                    {
                        invocationFailure = exception;
                    }

                    output = TryReadDependency(ref state, beforeHandle);
                    if (invocationFailure != null)
                    {
                        failed = true;
                        failureDetail = Describe(invocationFailure);
                    }
                }
                catch (Exception exception)
                {
                    // Resolving the state and reading or writing its dependency failed before any gameplay write.
                    failed = true;
                    failureDetail = Describe(exception);
                    output = beforeHandle;
                }

                // A skipped, disabled or query-empty system forwards its combined incoming fence and produces no new
                // work, so an early return never drops a producer dependency (04 s4).
                if (!output.Equals(beforeHandle))
                {
                    currentSink.RecordStepJob(output, entry.Stage, entry.SystemKey, epoch, step);
                }

                currentFences.Store(entry.StageIndex, output);

                if (failed)
                {
                    // The system wrote authoritative state and then threw: the pending work stays tracked behind
                    // quarantine, nothing after this entry runs, and the step is never published (P-031, P-047).
                    return FailStep(currentSink, entries, i, entry, DiagnosticCode.ApplyFault, failureDetail);
                }

                if (shouldRun)
                {
                    stageDispatched[entry.StageIndex] = true;
                }
            }

            LastDispatchedCount = dispatchedCount;
            TotalDispatchedCount += dispatchedCount;
            return new DispatchRunResult(true, dispatchedCount, -1, DiagnosticCode.None, default(FactoryKey), null);
        }

        private DispatchRunResult FailStep(
            IGuardedDispatchSink currentSink,
            IReadOnlyList<GuardedDispatchEntry> entries,
            int failedIndex,
            GuardedDispatchEntry entry,
            DiagnosticCode code,
            string detail)
        {
            LastDispatchedCount = dispatchedCount;
            TotalDispatchedCount += dispatchedCount;
            currentSink.RetainStepJobsByQuarantine();
            currentSink.OnDispatchFaulted(code, entry.SystemKey, detail);
            return new DispatchRunResult(
                false,
                dispatchedCount,
                entry.DispatchIndex,
                code,
                entry.SystemKey,
                Remaining(entries, failedIndex));
        }

        private static SystemHandle ResolveHandle(SystemDispatchTarget target)
        {
            if (target.Kind == SystemDispatchKind.UnmanagedSystem)
            {
                return target.UnmanagedSystem;
            }

            return target.ManagedSystem!.SystemHandle;
        }

        private void Invoke(SystemDispatchTarget target)
        {
            if (target.Kind == SystemDispatchKind.UnmanagedSystem)
            {
                target.UnmanagedSystem.Update(World.Unmanaged);
                return;
            }

            target.ManagedSystem!.Update();
        }

        private static JobHandle TryReadDependency(ref SystemState state, JobHandle fallback)
        {
            try
            {
                return state.Dependency;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private IReadOnlyList<FactoryKey> Remaining(IReadOnlyList<GuardedDispatchEntry> entries, int failedIndex)
        {
            unreachedKeys.Clear();
            for (int i = failedIndex + 1; i < entries.Count; i++)
            {
                unreachedKeys.Add(entries[i].SystemKey);
            }

            return new List<FactoryKey>(unreachedKeys);
        }

        private static string Describe(Exception exception)
            => exception.GetType().FullName + ": " + exception.Message;
    }

    /// <summary>Adapter ingress boundary: collects adapter input and host completions before a step (04 s3).</summary>
    public sealed partial class GameCoreIngressGroup : GuardedSystemGroup
    {
    }

    /// <summary>
    /// Adapter step boundary: the one gameplay dispatch path. It is explicitly updated by the host and is never a
    /// child of Unity's automatically driven default groups (04 s3).
    /// </summary>
    public sealed partial class GameCoreStepGroup : GuardedSystemGroup
    {
    }

    /// <summary>Adapter output boundary: presentation from the last published image, on host frames even when idle.</summary>
    public sealed partial class GameCoreOutputGroup : GuardedSystemGroup
    {
    }
}
