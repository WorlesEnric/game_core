// GameCore.Composition — the tracked-job fence registry (P-047, P-048).
//
// P-047 and P-048 make one statement in two places: "Already executing jobs MUST finish before storage or
// code-owned resources are released", and "Resources still reachable by unfinished work are quarantined and
// retained; elapsed timeout only reports `TeardownBlocked`, never authorizes free".
//
// The registry is what makes that provable rather than asserted. A host that schedules native work registers the
// job here together with the resources that work may still reach. Teardown then asks this registry which of an
// installation's resources are still fenced, and a fenced resource is quarantined instead of released. Completing
// a job is a separate event, so the same reference can never be released twice: a completed job no longer fences
// anything, and the registry only ever reports the outstanding set.
//
// Nothing here reads a clock. "Elapsed time" cannot influence the answer, which is the point.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>One tracked unit of native work and the resources it may still reach (P-041, P-047).</summary>
    public sealed class TrackedJob
    {
        public TrackedJob(
            Id128 jobId,
            PluginInstanceId instance,
            StageId stage,
            FactoryKey systemKey,
            AssemblyEpoch epoch,
            LogicalStepId step,
            IReadOnlyList<Id128>? resourceIds)
        {
            JobId = jobId;
            Instance = instance;
            Stage = stage;
            SystemKey = systemKey;
            Epoch = epoch;
            Step = step;
            ResourceIds = ContractCollections.Freeze(resourceIds);
        }

        /// <summary>Process-local job identity; the host keeps the native handle beside this record (03 s1).</summary>
        public Id128 JobId { get; }

        public PluginInstanceId Instance { get; }

        public StageId Stage { get; }

        public FactoryKey SystemKey { get; }

        /// <summary>Assembly epoch the work was scheduled under; an old-epoch completion is stale (P-047).</summary>
        public AssemblyEpoch Epoch { get; }

        public LogicalStepId Step { get; }

        /// <summary>Resources this job may still reach; they stay pinned until it completes (P-048).</summary>
        public IReadOnlyList<Id128> ResourceIds { get; }

        public bool Completed { get; internal set; }

        /// <summary>True once teardown found the job unfinished and retained its resources (P-048).</summary>
        public bool RetainedByQuarantine { get; internal set; }

        public override string ToString() =>
            "job(" + JobId.ToString() + ", " + Instance.ToString() + ", resources="
            + ResourceIds.Count.ToString(CultureInfo.InvariantCulture) + (Completed ? ", completed" : ", outstanding") + ")";
    }

    /// <summary>
    /// Tracked jobs of one composition host. A job fences the resources the work may still reach, so a teardown
    /// that begins while the job runs reports the fence instead of releasing memory the job is reading (P-047).
    /// </summary>
    public sealed class JobFenceRegistry
    {
        private readonly Dictionary<Id128, TrackedJob> jobs = new Dictionary<Id128, TrackedJob>();
        private readonly List<Id128> canonicalOrder = new List<Id128>();

        public int TrackedCount => jobs.Count;

        /// <summary>Jobs that have not completed; every one of them still fences its resources.</summary>
        public int OutstandingCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < canonicalOrder.Count; i++)
                {
                    if (!jobs[canonicalOrder[i]].Completed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int CompletedCount { get; private set; }

        /// <summary>Jobs a teardown found unfinished and therefore retained behind quarantine (P-048).</summary>
        public int QuarantinedJobCount { get; private set; }

        public int RegisteredCount { get; private set; }

        public int ReleasedCount { get; private set; }

        /// <summary>Completions for a job id this registry never tracked, or a repeat completion.</summary>
        public int RejectedCompletionCount { get; private set; }

        /// <summary>
        /// Registers one scheduled job with the resources it may reach. The registration exists before the job can
        /// be observed, so a teardown that races the scheduler sees the fence either way.
        /// </summary>
        public TrackedJob Track(
            Id128 jobId,
            PluginInstanceId instance,
            StageId stage,
            FactoryKey systemKey,
            AssemblyEpoch epoch,
            LogicalStepId step,
            IReadOnlyList<Id128>? resourceIds)
        {
            if (jobs.ContainsKey(jobId))
            {
                throw new InvalidOperationException("Job identity " + jobId.ToString() + " is already tracked; a job registers once.");
            }

            TrackedJob job = new TrackedJob(jobId, instance, stage, systemKey, epoch, step, resourceIds);
            jobs.Add(jobId, job);
            canonicalOrder.Add(jobId);
            canonicalOrder.Sort(CompareIds);
            RegisteredCount++;
            return job;
        }

        /// <summary>
        /// Records that a job finished. Completion is the only event that stops the job fencing its resources, so a
        /// repeated completion is refused rather than counted twice (P-048: dispose at most once).
        /// </summary>
        public bool Complete(Id128 jobId)
        {
            if (!jobs.TryGetValue(jobId, out TrackedJob job) || job.Completed)
            {
                RejectedCompletionCount++;
                return false;
            }

            job.Completed = true;
            CompletedCount++;
            return true;
        }

        public bool IsOutstanding(Id128 jobId) =>
            jobs.TryGetValue(jobId, out TrackedJob job) && !job.Completed;

        public bool TryGet(Id128 jobId, out TrackedJob? job)
        {
            if (jobs.TryGetValue(jobId, out TrackedJob found))
            {
                job = found;
                return true;
            }

            job = null;
            return false;
        }

        /// <summary>
        /// The resources still reachable by this installation's unfinished work, in canonical order. Teardown hands
        /// exactly this list to the resource ledger, which is how a blocked job prevents its buffer's release.
        /// </summary>
        public IReadOnlyList<Id128> OutstandingResourcesFor(PluginInstanceId instance)
        {
            List<Id128> fenced = new List<Id128>();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                TrackedJob job = jobs[canonicalOrder[i]];
                if (job.Completed || !job.Instance.Equals(instance))
                {
                    continue;
                }

                for (int r = 0; r < job.ResourceIds.Count; r++)
                {
                    if (!Contains(fenced, job.ResourceIds[r]))
                    {
                        fenced.Add(job.ResourceIds[r]);
                    }
                }
            }

            return fenced;
        }

        /// <summary>Every resource still reachable by any unfinished work of this world, in canonical order.</summary>
        public IReadOnlyList<Id128> OutstandingResourceIds()
        {
            List<Id128> fenced = new List<Id128>();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                TrackedJob job = jobs[canonicalOrder[i]];
                if (job.Completed)
                {
                    continue;
                }

                for (int r = 0; r < job.ResourceIds.Count; r++)
                {
                    if (!Contains(fenced, job.ResourceIds[r]))
                    {
                        fenced.Add(job.ResourceIds[r]);
                    }
                }
            }

            return fenced;
        }

        /// <summary>Tracked jobs of one installation in canonical order, for diagnostics.</summary>
        public IReadOnlyList<TrackedJob> JobsOf(PluginInstanceId instance)
        {
            List<TrackedJob> mine = new List<TrackedJob>();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                TrackedJob job = jobs[canonicalOrder[i]];
                if (job.Instance.Equals(instance))
                {
                    mine.Add(job);
                }
            }

            return mine;
        }

        /// <summary>
        /// Marks one unfinished job as retained by quarantine: its buffers stay valid until it completes (P-047).
        /// This is a record of a fact teardown observed, not a substitute for waiting.
        /// </summary>
        public bool RetainByQuarantine(Id128 jobId)
        {
            if (!jobs.TryGetValue(jobId, out TrackedJob job) || job.Completed)
            {
                return false;
            }

            if (!job.RetainedByQuarantine)
            {
                job.RetainedByQuarantine = true;
                QuarantinedJobCount++;
            }

            return true;
        }

        /// <summary>
        /// Marks every unfinished job of one installation that may reach the named resource as retained. Teardown
        /// uses this after quarantining the resource, so the reason a resource is still held is data (P-048).
        /// </summary>
        public int RetainByQuarantineFor(PluginInstanceId instance, Id128 resourceId)
        {
            int retained = 0;
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                TrackedJob job = jobs[canonicalOrder[i]];
                if (job.Completed || !job.Instance.Equals(instance) || !References(job, resourceId))
                {
                    continue;
                }

                if (!job.RetainedByQuarantine)
                {
                    job.RetainedByQuarantine = true;
                    QuarantinedJobCount++;
                }

                retained++;
            }

            return retained;
        }

        /// <summary>True when some unfinished job may still reach this resource; it must not be released (P-047).</summary>
        public bool IsResourceFenced(Id128 resourceId)
        {
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                TrackedJob job = jobs[canonicalOrder[i]];
                if (!job.Completed && References(job, resourceId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Releases a tracked job's fence record after its resources were settled; the resource ids come back so the
        /// caller can retire exactly what the job held. A still-outstanding job is refused.
        /// </summary>
        public bool Release(Id128 jobId, out IReadOnlyList<Id128>? resourceIds)
        {
            resourceIds = null;
            if (!jobs.TryGetValue(jobId, out TrackedJob job) || !job.Completed)
            {
                return false;
            }

            resourceIds = job.ResourceIds;
            jobs.Remove(jobId);
            canonicalOrder.Remove(jobId);
            ReleasedCount++;
            return true;
        }

        private static bool References(TrackedJob job, Id128 resourceId)
        {
            for (int i = 0; i < job.ResourceIds.Count; i++)
            {
                if (job.ResourceIds[i].Equals(resourceId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(List<Id128> ids, Id128 candidate)
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

        private static int CompareIds(Id128 left, Id128 right) => left.CompareTo(right);
    }
}
