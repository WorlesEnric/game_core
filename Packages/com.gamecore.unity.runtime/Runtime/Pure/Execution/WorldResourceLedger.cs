#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution
{
    /// <summary>Aggregate outcome of one teardown pass over the resource ledger (P-048).</summary>
    public readonly struct RetirementOutcome
    {
        public readonly int RetiredCount;
        public readonly int FailedCount;
        public readonly int QuarantinedCount;

        public RetirementOutcome(int retiredCount, int failedCount, int quarantinedCount)
        {
            RetiredCount = retiredCount;
            FailedCount = failedCount;
            QuarantinedCount = quarantinedCount;
        }

        /// <summary>True when every owned resource reached <see cref="ResourceRetirementState.Retired"/>.</summary>
        public bool AllRetired => FailedCount == 0 && QuarantinedCount == 0;

        public override string ToString()
        {
            return "retired=" + RetiredCount.ToString(CultureInfo.InvariantCulture)
                + " failed=" + FailedCount.ToString(CultureInfo.InvariantCulture)
                + " quarantined=" + QuarantinedCount.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// World/job resource ledger (03 s1, P-041, P-048). It records what a world acquired, in which order, with
    /// which dependency, and whether it was retired or retained behind quarantine. Lease identifiers are
    /// process-local and never serialized as world identity (05 s4).
    /// </summary>
    public sealed class WorldResourceLedger : ITelemetryOwner
    {
        string ITelemetryOwner.TelemetryOwner => "gamecore.execution.resources";

        /// <summary>Category salt of resource identifiers; distinct from the job-id category.</summary>
        public const ulong ResourceIdSalt = 0x7265736F75726365UL;

        /// <summary>Category salt of job identifiers.</summary>
        public const ulong JobIdSalt = 0x6A6F6273UL;

        private readonly Dictionary<Id128, WorldResourceRecord> resources =
            new Dictionary<Id128, WorldResourceRecord>();

        private readonly List<Id128> acquisitionOrder = new List<Id128>();

        private readonly Dictionary<Id128, JobLedgerRecord> jobs = new Dictionary<Id128, JobLedgerRecord>();

        private readonly List<Id128> jobOrder = new List<Id128>();

        private readonly IdSequence resourceIds = new IdSequence(ResourceIdSalt);

        private readonly IdSequence jobIds = new IdSequence(JobIdSalt);

        private readonly List<Id128> retirementScratch = new List<Id128>();

        public WorldResourceLedger(WorldId world)
        {
            World = world;
        }

        public WorldId World { get; }

        public int AcquireCount { get; private set; }

        public int RetireCount { get; private set; }

        public int QuarantineCount { get; private set; }

        /// <summary>Disposal attempts, including the failing ones, so "try every independent cleanup" is visible.</summary>
        public int DisposalAttemptCount { get; private set; }

        public int ResourceCount => resources.Count;

        public int JobCount => jobs.Count;

        /// <summary>
        /// Writes the world resource ledger through the fixed compact schema (GC-023, TEST-023): live leases, the
        /// bytes those leases hold, quarantine entries/bytes, and the tracked jobs still outstanding. The ledger is
        /// the one owner that sees every native resource, so it is where the memory split is decided.
        /// </summary>
        public void WriteTelemetry(TelemetryCounterSet into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            // The count and the bytes describe the same set - resources a live lease still holds - so quarantine is
            // reported only as quarantine and the four-way split stays a partition (TEST-023).
            int leaseHeld = 0;
            ulong leaseBytes = 0UL;
            for (int i = 0; i < acquisitionOrder.Count; i++)
            {
                WorldResourceRecord record = resources[acquisitionOrder[i]];
                if (IsLeaseHeld(record))
                {
                    leaseHeld++;
                    leaseBytes += record.Bytes;
                }
            }

            into.ObserveMax(TelemetryCounter.LiveLeases, leaseHeld);
            into.ObserveMax(TelemetryCounter.LeaseBytes, (long)leaseBytes);
            into.ObserveMax(TelemetryCounter.QuarantineEntries, QuarantineCount + QuarantinedJobCount);
            into.ObserveMax(TelemetryCounter.QuarantineBytes, (long)QuarantinedBytes);
            into.ObserveMax(TelemetryCounter.OutstandingCallbacks, OutstandingJobCount);
        }

        /// <summary>Acquires one resource and returns its ledger identity (P-048).</summary>
        public Id128 Acquire(
            WorldResourceKind kind,
            ResourceKey key,
            OwnerId owner,
            PluginInstanceId instance,
            Id128 dependsOn,
            ulong bytes)
        {
            Id128 id = resourceIds.Next();
            var record = new WorldResourceRecord(
                id,
                kind,
                key,
                owner,
                instance,
                ResourceRetirementState.Acquired,
                dependsOn,
                (uint)acquisitionOrder.Count,
                bytes);

            resources[id] = record;
            acquisitionOrder.Add(id);
            AcquireCount++;
            return id;
        }

        public bool TryGetResource(Id128 resourceId, out WorldResourceRecord record)
            => resources.TryGetValue(resourceId, out record);

        /// <summary>Marks a staged resource ready at publication; readiness does not change its identity (P-029).</summary>
        public bool MarkReady(Id128 resourceId)
        {
            if (!resources.TryGetValue(resourceId, out WorldResourceRecord record))
            {
                return false;
            }

            if (record.State != ResourceRetirementState.Acquired)
            {
                return false;
            }

            resources[resourceId] = WithResourceState(record, ResourceRetirementState.Ready);
            return true;
        }

        /// <summary>
        /// Retires one resource. A resource is disposed exactly once: an already retired or quarantined record is
        /// refused rather than released twice (P-048).
        /// </summary>
        public bool Retire(Id128 resourceId)
        {
            if (!resources.TryGetValue(resourceId, out WorldResourceRecord record))
            {
                return false;
            }

            if (record.State == ResourceRetirementState.Retired || record.State == ResourceRetirementState.Quarantined)
            {
                return false;
            }

            DisposalAttemptCount++;
            RetireCount++;
            resources[resourceId] = WithResourceState(record, ResourceRetirementState.Retired);
            return true;
        }

        /// <summary>Retains a resource whose users have not ended; a timeout never authorizes freeing it (P-048).</summary>
        public bool Quarantine(Id128 resourceId)
        {
            if (!resources.TryGetValue(resourceId, out WorldResourceRecord record))
            {
                return false;
            }

            if (record.State == ResourceRetirementState.Retired)
            {
                return false;
            }

            if (record.State != ResourceRetirementState.Quarantined)
            {
                DisposalAttemptCount++;
            }

            QuarantineCount++;
            resources[resourceId] = WithResourceState(record, ResourceRetirementState.Quarantined);
            return true;
        }

        /// <summary>Releases quarantine once every user has ended, allowing a later retirement (P-048).</summary>
        public bool ReleaseQuarantine(Id128 resourceId)
        {
            if (!resources.TryGetValue(resourceId, out WorldResourceRecord record))
            {
                return false;
            }

            if (record.State != ResourceRetirementState.Quarantined)
            {
                return false;
            }

            resources[resourceId] = WithResourceState(record, ResourceRetirementState.Retiring);
            return true;
        }

        /// <summary>Records one scheduled job. The host keeps the native handle beside this ledger record.</summary>
        public Id128 RecordJob(StageId stage, FactoryKey systemKey, AssemblyEpoch epoch, LogicalStepId step)
        {
            Id128 id = jobIds.Next();
            if (!jobs.ContainsKey(id))
            {
                jobOrder.Add(id);
            }

            jobs[id] = new JobLedgerRecord(id, stage, systemKey, epoch, step, false, false);
            return id;
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
        /// Marks an unfinished job as retained by quarantine: its buffers stay valid until the job completes
        /// (P-047, P-048). Calling this again is harmless.
        /// </summary>
        public bool RetainJobByQuarantine(Id128 jobId)
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
                record.Completed,
                true);
            return true;
        }

        public bool TryGetJob(Id128 jobId, out JobLedgerRecord record) => jobs.TryGetValue(jobId, out record);

        public bool IsJobCompleted(Id128 jobId)
            => jobs.TryGetValue(jobId, out JobLedgerRecord record) && record.Completed;

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

        public int QuarantinedJobCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < jobOrder.Count; i++)
                {
                    if (jobs.TryGetValue(jobOrder[i], out JobLedgerRecord record) && record.RetainedByQuarantine)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int RetainedResourceCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < acquisitionOrder.Count; i++)
                {
                    if (resources[acquisitionOrder[i]].IsRetained)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Bytes held by resources that are still retained (acquired or ready). TEST-023 requires leases, retained
        /// events and quarantine to be reported separately, so this is neither the quarantine total nor the event
        /// store's retained bytes (GC-023).
        /// </summary>
        public ulong RetainedResourceBytes
        {
            get
            {
                ulong bytes = 0UL;
                for (int i = 0; i < acquisitionOrder.Count; i++)
                {
                    WorldResourceRecord record = resources[acquisitionOrder[i]];
                    if (IsLeaseHeld(record))
                    {
                        bytes += record.Bytes;
                    }
                }

                return bytes;
            }
        }

        /// <summary>
        /// True while a record is held by a live lease. Quarantined records are excluded on purpose: TEST-023
        /// requires leases and quarantine to be reported separately, so the lease total must not already contain the
        /// quarantine total.
        /// </summary>
        private static bool IsLeaseHeld(WorldResourceRecord record) =>
            record.State == ResourceRetirementState.Acquired
            || record.State == ResourceRetirementState.Ready
            || record.State == ResourceRetirementState.Retiring;


        public ulong QuarantinedBytes
        {
            get
            {
                ulong bytes = 0UL;
                for (int i = 0; i < acquisitionOrder.Count; i++)
                {
                    WorldResourceRecord record = resources[acquisitionOrder[i]];
                    if (record.State == ResourceRetirementState.Quarantined)
                    {
                        bytes += record.Bytes;
                    }
                }

                return bytes;
            }
        }

        /// <summary>
        /// Retires every non-quarantined resource so that a dependent is released before the resource it depends
        /// on, attempting all independent cleanup and continuing past a failure (P-048). A resource named by
        /// <paramref name="failingResourceId"/> is quarantined and its dependents are still attempted, which is the
        /// injected-disposer-failure case.
        /// </summary>
        public RetirementOutcome RetireAll(Id128 failingResourceId)
        {
            retirementScratch.Clear();
            for (int i = 0; i < acquisitionOrder.Count; i++)
            {
                Id128 id = acquisitionOrder[i];
                WorldResourceRecord record = resources[id];
                if (record.State == ResourceRetirementState.Retired || record.State == ResourceRetirementState.Quarantined)
                {
                    continue;
                }

                retirementScratch.Add(id);
            }

            int retired = 0;
            int failed = 0;
            while (retirementScratch.Count > 0)
            {
                int next = SelectNextRetirement();
                Id128 id = retirementScratch[next];
                retirementScratch.RemoveAt(next);

                if (id.Equals(failingResourceId))
                {
                    Quarantine(id);
                    failed++;
                    continue;
                }

                if (Retire(id))
                {
                    retired++;
                }
            }

            return new RetirementOutcome(retired, failed, RetainedResourceCount);
        }

        /// <summary>Immutable snapshot of the ledger at one epoch (03 s1, P-048).</summary>
        public WorldResourceLedgerSnapshot Snapshot(AssemblyEpoch epoch)
        {
            var ordered = new List<WorldResourceRecord>(resources.Count);
            for (int i = 0; i < acquisitionOrder.Count; i++)
            {
                ordered.Add(resources[acquisitionOrder[i]]);
            }

            ordered.Sort(CompareResources);

            var jobRecords = new List<JobLedgerRecord>(jobOrder.Count);
            for (int i = 0; i < jobOrder.Count; i++)
            {
                jobRecords.Add(jobs[jobOrder[i]]);
            }

            return new WorldResourceLedgerSnapshot(World, epoch, ordered, jobRecords, QuarantinedBytes);
        }

        /// <summary>
        /// Picks the next resource whose dependents are all settled, so retirement flows from dependents toward
        /// their dependencies. A dependency cycle (which composition validation forbids) falls back to reverse
        /// acquisition order instead of blocking the pass, because cleanup must still be attempted.
        /// </summary>
        private int SelectNextRetirement()
        {
            for (int i = 0; i < retirementScratch.Count; i++)
            {
                Id128 candidate = retirementScratch[i];
                bool hasPendingDependent = false;
                for (int j = 0; j < retirementScratch.Count; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    if (resources[retirementScratch[j]].DependsOn.Equals(candidate))
                    {
                        hasPendingDependent = true;
                        break;
                    }
                }

                if (!hasPendingDependent)
                {
                    return i;
                }
            }

            return retirementScratch.Count - 1;
        }

        private static WorldResourceRecord WithResourceState(WorldResourceRecord record, ResourceRetirementState state)
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

        private static int CompareResources(WorldResourceRecord left, WorldResourceRecord right)
            => left.ResourceId.CompareTo(right.ResourceId);
    }
}
