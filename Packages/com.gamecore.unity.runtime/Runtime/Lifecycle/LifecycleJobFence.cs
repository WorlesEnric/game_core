// GameCore.Unity.Runtime.Lifecycle — tracked job fences for the lifecycle teardown (P-041, P-047, P-048).
//
// P-047 states the rule twice, once about jobs and once about resources: "Already executing jobs MUST finish
// before storage or code-owned resources are released; job cancellation is cooperative intent, not memory
// reclamation." P-048 adds the consequence: a resource an unfinished job can still reach is quarantined, and
// elapsed time never authorizes freeing it.
//
// This type is the bridge that makes that provable in a real Unity world. A scheduled job is registered here with
// its native `JobHandle` and the resources it may reach; the composition `JobFenceRegistry` records the same fact
// for the teardown sequencer. Completing a job is one event that both completes the handle and stops the fence, so
// a resource can never be released while a handle that reads it is still running.
//
// `CompleteAllBlocking` exists for teardown only. It is the one place a managed call waits on native work, and it
// waits rather than cancels — a cancellation cannot prove that a worker stopped touching the buffer.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Lifecycle
{
    /// <summary>One tracked job: the composition fence record plus the native handle the worker is running under.</summary>
    public readonly struct UnityTrackedJob
    {
        public readonly Id128 JobId;
        public readonly PluginInstanceId Instance;
        public readonly JobHandle Handle;

        public UnityTrackedJob(Id128 jobId, PluginInstanceId instance, JobHandle handle)
        {
            JobId = jobId;
            Instance = instance;
            Handle = handle;
        }

        public override string ToString() =>
            "unityJob(" + JobId.ToString() + ", " + Instance.ToString() + ")";
    }

    /// <summary>
    /// Bridges scheduled Unity jobs into the lifecycle's job fence registry. One instance per composition host.
    /// </summary>
    public sealed class LifecycleJobFence : IDisposable
    {
        private readonly JobFenceRegistry registry;
        private readonly Dictionary<Id128, UnityTrackedJob> handles = new Dictionary<Id128, UnityTrackedJob>();
        private readonly List<Id128> canonicalOrder = new List<Id128>();
        private bool disposed;

        public LifecycleJobFence(JobFenceRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>Jobs registered and not yet completed.</summary>
        public int OutstandingCount => handles.Count;

        /// <summary>Handles waited on by <see cref="CompleteAllBlocking"/> since construction.</summary>
        public int CompletedCount { get; private set; }

        /// <summary>Completions that threw; a failed completion is never reported as a settled job (P-048).</summary>
        public int CompletionFailureCount { get; private set; }

        public int RegisteredCount { get; private set; }

        /// <summary>
        /// Registers one scheduled job with the resources it may reach. The composition record exists before the
        /// handle is returned to the caller, so a teardown that begins immediately after scheduling still sees the
        /// fence (P-041, P-047).
        /// </summary>
        public void Track(
            Id128 jobId,
            PluginInstanceId instance,
            StageId stage,
            FactoryKey systemKey,
            AssemblyEpoch epoch,
            LogicalStepId step,
            IReadOnlyList<Id128>? resourceIds,
            JobHandle handle)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(LifecycleJobFence));
            }

            registry.Track(jobId, instance, stage, systemKey, epoch, step, resourceIds);
            handles.Add(jobId, new UnityTrackedJob(jobId, instance, handle));
            canonicalOrder.Add(jobId);
            canonicalOrder.Sort(CompareIds);
            RegisteredCount++;
        }

        /// <summary>
        /// Records that a job finished and forgets its handle. This does not complete the handle: Unity completes it
        /// where the owner declared, which keeps the engine's dependency graph authoritative (04 section 4).
        /// </summary>
        public bool Complete(Id128 jobId)
        {
            if (!registry.Complete(jobId))
            {
                return false;
            }

            handles.Remove(jobId);
            canonicalOrder.Remove(jobId);
            return true;
        }

        /// <summary>
        /// Waits for the tracked jobs of one installation. Called at a teardown boundary before their resources are
        /// released, so "already executing jobs finish first" is an executed fact rather than an intention (P-047).
        /// A completion that throws is counted and reported, never swallowed.
        /// </summary>
        public int CompleteAllBlocking()
        {
            if (disposed)
            {
                return 0;
            }

            List<Id128> order = new List<Id128>(canonicalOrder);
            int settled = 0;
            for (int i = 0; i < order.Count; i++)
            {
                if (!handles.TryGetValue(order[i], out UnityTrackedJob job))
                {
                    continue;
                }

                try
                {
                    job.Handle.Complete();
                }
                catch (Exception)
                {
                    // A failure here cannot prove the worker stopped, so the fence stays until the caller resolves
                    // it. The resource is retained and the count says so (P-048).
                    CompletionFailureCount++;
                    continue;
                }

                registry.Complete(job.JobId);
                handles.Remove(job.JobId);
                canonicalOrder.Remove(job.JobId);
                CompletedCount++;
                settled++;
            }

            return settled;
        }

        /// <summary>
        /// Waits only for the tracked jobs of one installation. A teardown of one activation never waits on another
        /// activation's work, which is what keeps teardown from becoming a global stop.
        /// </summary>
        public int CompleteAllBlocking(PluginInstanceId instance)
        {
            if (disposed)
            {
                return 0;
            }

            List<Id128> mine = new List<Id128>();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                if (handles.TryGetValue(canonicalOrder[i], out UnityTrackedJob job) && job.Instance.Equals(instance))
                {
                    mine.Add(canonicalOrder[i]);
                }
            }

            int settled = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                if (!handles.TryGetValue(mine[i], out UnityTrackedJob job))
                {
                    continue;
                }

                try
                {
                    job.Handle.Complete();
                }
                catch (Exception)
                {
                    CompletionFailureCount++;
                    continue;
                }

                registry.Complete(job.JobId);
                handles.Remove(job.JobId);
                canonicalOrder.Remove(job.JobId);
                CompletedCount++;
                settled++;
            }

            return settled;
        }

        /// <summary>True when any tracked handle of one installation is still outstanding.</summary>
        public bool HasOutstanding(PluginInstanceId instance)
        {
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                if (handles.TryGetValue(canonicalOrder[i], out UnityTrackedJob job) && job.Instance.Equals(instance))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Outstanding tracked jobs of one installation, in canonical order, for diagnostics.</summary>
        public IReadOnlyList<UnityTrackedJob> OutstandingOf(PluginInstanceId instance)
        {
            List<TrackedJob> records = new List<TrackedJob>(registry.JobsOf(instance));
            List<UnityTrackedJob> mine = new List<UnityTrackedJob>(records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Completed)
                {
                    continue;
                }

                JobHandle handle = handles.TryGetValue(records[i].JobId, out UnityTrackedJob tracked)
                    ? tracked.Handle
                    : default(JobHandle);
                mine.Add(new UnityTrackedJob(records[i].JobId, records[i].Instance, handle));
            }

            return mine;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            // Disposal does not silently drop outstanding handles: an unreleased job means the caller is releasing
            // storage while a worker may still read it, which is exactly what P-048 forbids.
            disposed = true;
            handles.Clear();
            canonicalOrder.Clear();
        }

        private static int CompareIds(Id128 left, Id128 right) => left.CompareTo(right);

        public override string ToString() =>
            "lifecycleJobFence(outstanding=" + OutstandingCount.ToString(CultureInfo.InvariantCulture)
            + ", completed=" + CompletedCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
