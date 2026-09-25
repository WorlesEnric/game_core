// GameCore.Composition — managed resource gates, gated leases and the resource ledger (P-007, P-029, P-047,
// P-048).
//
// Three rules drive this file:
//  * A prepared resource is inert. Its gate is closed, so a callback that arrives before publication cannot
//    reach gameplay, and a gate that closes at retirement never reopens (P-029, P-047).
//  * A lease is disposed at most once, its disposer key was recorded at acquisition time, and independent
//    cleanup continues after one disposer throws (P-048).
//  * Resources still reachable by unfinished work are quarantined and retained; elapsed time never authorizes
//    freeing them (P-048). The ledger records that state instead of guessing.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>A managed-resource preparation failed; the caller turns this into a typed rejection (P-049).</summary>
    public sealed class ResourcePreparationException : Exception
    {
        public ResourcePreparationException(DiagnosticCode code, string message)
            : base(message)
        {
            Code = code;
        }

        /// <summary>Stable rejection code the preparation failure maps to (00 s9).</summary>
        public DiagnosticCode Code { get; }
    }

    /// <summary>
    /// Gate in front of a prepared managed callback or subscription. It starts closed, opens exactly once at
    /// publication, and closes irreversibly for its activation epoch (P-029, P-047).
    /// </summary>
    public sealed class ManagedResourceGate : IResourceGate
    {
        private bool closedIrreversibly;

        public ManagedResourceGate()
        {
        }

        public bool IsOpen { get; private set; }

        public int CloseCount { get; private set; }

        public int DispatchAttempts { get; private set; }

        public int DroppedDispatches { get; private set; }

        public void Close()
        {
            if (closedIrreversibly)
            {
                return;
            }

            closedIrreversibly = true;
            IsOpen = false;
            CloseCount++;
        }

        /// <summary>
        /// Opens the gate at publication. A gate that was already closed for an earlier activation epoch never
        /// reopens, so a late callback cannot reacquire authority (P-047).
        /// </summary>
        public bool OpenOnPublication()
        {
            if (closedIrreversibly)
            {
                return false;
            }

            IsOpen = true;
            return true;
        }

        /// <summary>Counts one dispatch attempt; a closed gate drops the work instead of delivering it (P-047).</summary>
        public bool TryDispatch()
        {
            DispatchAttempts++;
            if (!IsOpen)
            {
                DroppedDispatches++;
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Gated managed lease. The disposer key and the readiness state exist before the value is exposed, so a
    /// partially successful acquisition still has cleanup recorded immediately (P-007, P-048).
    /// </summary>
    public sealed class ManagedResourceLease : IManagedResourceLease
    {
        private readonly Action<Id128>? onDispose;
        private bool disposeAttempted;

        public ManagedResourceLease(
            ResourceKey resource,
            Id128 leaseId,
            AsyncWorkToken token,
            FactoryKey disposer,
            IResourceGate? gate,
            Action<Id128>? onDispose)
        {
            Resource = resource;
            LeaseId = leaseId;
            Token = token;
            Disposer = disposer;
            Gate = gate ?? new ManagedResourceGate();
            this.onDispose = onDispose;
        }

        public ResourceKey Resource { get; }

        public Id128 LeaseId { get; }

        public AsyncWorkToken Token { get; }

        /// <summary>Pending while the lease is staged behind its gate; a staged lease is not usable yet (P-029).</summary>
        public ResourceReadiness Readiness { get; private set; } = ResourceReadiness.Pending;

        public FactoryKey Disposer { get; }

        /// <summary>Inert until the owning activation publishes; consumed callbacks re-check it (P-047).</summary>
        public IResourceGate Gate { get; }

        public bool IsDisposed { get; private set; }

        public int DisposeCount { get; private set; }

        /// <summary>Marks the staged lease ready at publication, behind its now-open gate (P-030).</summary>
        public void MarkReady() => Readiness = IsDisposed ? ResourceReadiness.Retiring : ResourceReadiness.Ready;

        /// <summary>Records a staged acquisition failure; the lease stays tracked for cleanup (P-029, P-049).</summary>
        public void MarkFailed() => Readiness = ResourceReadiness.Failed;

        public void Dispose()
        {
            if (disposeAttempted)
            {
                // P-048: dispose each lease at most once; a repeat is not a second release.
                if (!IsDisposed)
                {
                    throw new InvalidOperationException("The previous release failed; the resource remains retained.");
                }

                return;
            }

            disposeAttempted = true;
            DisposeCount++;
            Readiness = ResourceReadiness.Retiring;
            Gate.Close();
            if (onDispose != null)
            {
                onDispose(LeaseId);
            }

            IsDisposed = true;
        }
    }

    /// <summary>Retirement/cleanup outcome of one resource or one teardown pass (P-048).</summary>
    public sealed class CleanupReport
    {
        public CleanupReport(
            IReadOnlyList<Id128>? retired,
            IReadOnlyList<Id128>? failed,
            IReadOnlyList<Id128>? quarantined)
        {
            Retired = ContractCollections.Freeze(retired);
            Failed = ContractCollections.Freeze(failed);
            Quarantined = ContractCollections.Freeze(quarantined);
        }

        public IReadOnlyList<Id128> Retired { get; }

        /// <summary>Releases that threw; their resources stay retained, never reported as disposed (P-048).</summary>
        public IReadOnlyList<Id128> Failed { get; }

        /// <summary>Still reachable by unfinished work; a later safe teardown must release them (P-048).</summary>
        public IReadOnlyList<Id128> Quarantined { get; }

        public bool HasCleanupErrors => Failed.Count != 0;

        public bool Blocked => Quarantined.Count != 0;

        public static CleanupReport Empty { get; } =
            new CleanupReport(Array.Empty<Id128>(), Array.Empty<Id128>(), Array.Empty<Id128>());
    }

    /// <summary>
    /// Tracked resource ledger of one composition host. Lease ids are process-local and never used as world
    /// identity (05 s4); every record keeps the owner, the acquisition ordinal and the dependency edge so
    /// retirement order is a property of the data (P-048).
    /// </summary>
    public sealed class ResourceLedger
    {
        private readonly Dictionary<Id128, LeaseEntry> leases = new Dictionary<Id128, LeaseEntry>();
        private readonly List<Id128> acquisitionOrder = new List<Id128>();
        private readonly Dictionary<Id128, WorldResourceRecord> records = new Dictionary<Id128, WorldResourceRecord>();

        public int LiveLeaseCount
        {
            get
            {
                int live = 0;
                for (int i = 0; i < acquisitionOrder.Count; i++)
                {
                    if (records[acquisitionOrder[i]].IsRetained)
                    {
                        live++;
                    }
                }

                return live;
            }
        }

        public int RetiredCount { get; private set; }

        public int QuarantinedCount { get; private set; }

        public int FailedReleaseCount { get; private set; }

        /// <summary>Retained resource ids in acquisition order; the inspectable ownership record (GC-004 DoD).</summary>
        public IReadOnlyList<Id128> RetainedResourceIds()
        {
            List<Id128> retained = new List<Id128>();
            for (int i = 0; i < acquisitionOrder.Count; i++)
            {
                if (records[acquisitionOrder[i]].IsRetained)
                {
                    retained.Add(acquisitionOrder[i]);
                }
            }

            return retained;
        }

        public IReadOnlyList<WorldResourceRecord> Records()
        {
            List<WorldResourceRecord> all = new List<WorldResourceRecord>(acquisitionOrder.Count);
            for (int i = 0; i < acquisitionOrder.Count; i++)
            {
                all.Add(records[acquisitionOrder[i]]);
            }

            return all;
        }

        /// <summary>
        /// One lease record by identity; used by teardown to report a retained reference's key, bytes and state
        /// instead of guessing them (P-048, 06 s6).
        /// </summary>
        public bool TryGetRecord(Id128 leaseId, out WorldResourceRecord record) =>
            records.TryGetValue(leaseId, out record);

        /// <summary>Registers a lease with its owner and acquisition ordinal, before anything can observe it.</summary>
        public bool Acquire(
            IManagedResourceLease lease,
            WorldResourceKind kind,
            OwnerId owner,
            PluginInstanceId instance,
            uint acquisitionOrdinal,
            Id128 dependsOn)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            if (records.ContainsKey(lease.LeaseId))
            {
                // One lease id identifies one acquisition for this process; a repeat is a programming error.
                return false;
            }

            WorldResourceRecord record = new WorldResourceRecord(
                lease.LeaseId,
                kind,
                lease.Resource,
                owner,
                instance,
                ResourceRetirementState.Acquired,
                dependsOn,
                acquisitionOrdinal,
                0UL);
            records.Add(lease.LeaseId, record);
            acquisitionOrder.Add(lease.LeaseId);
            leases.Add(lease.LeaseId, new LeaseEntry(lease, instance));
            return true;
        }

        /// <summary>Marks a lease ready at publication (P-030).</summary>
        public bool MarkReady(Id128 leaseId)
        {
            if (!records.TryGetValue(leaseId, out WorldResourceRecord record) ||
                record.State != ResourceRetirementState.Acquired)
            {
                return false;
            }

            records[leaseId] = WithState(record, ResourceRetirementState.Ready);
            if (leases.TryGetValue(leaseId, out LeaseEntry entry) && entry.Lease is ManagedResourceLease managed)
            {
                managed.MarkReady();
            }

            return true;
        }

        /// <summary>
        /// One retirement attempt. A quarantine cannot be retired by this path: P-048 keeps a resource that
        /// unfinished work may still reach. Dispose runs at most once per lease even when cleanup repeats.
        /// </summary>
        public bool Retire(Id128 leaseId)
        {
            if (!records.TryGetValue(leaseId, out WorldResourceRecord record))
            {
                return false;
            }

            if (record.State == ResourceRetirementState.Retired || record.State == ResourceRetirementState.Quarantined)
            {
                return false;
            }

            records[leaseId] = WithState(record, ResourceRetirementState.Retiring);
            if (!leases.TryGetValue(leaseId, out LeaseEntry entry))
            {
                records[leaseId] = WithState(record, ResourceRetirementState.Retired);
                RetiredCount++;
                return true;
            }

            try
            {
                entry.Lease.Dispose();
                records[leaseId] = WithState(records[leaseId], ResourceRetirementState.Retired);
                RetiredCount++;
                return true;
            }
            catch (Exception)
            {
                // A failed release stays retained and tracked, so it is quarantined rather than reported as a
                // disposal; independent cleanup still continues (P-048).
                records[leaseId] = WithState(records[leaseId], ResourceRetirementState.Quarantined);
                if (leases.TryGetValue(leaseId, out LeaseEntry failed) && failed.Lease is ManagedResourceLease managed)
                {
                    managed.MarkFailed();
                }

                QuarantinedCount++;
                FailedReleaseCount++;
                return false;
            }
        }

        /// <summary>Retains a resource whose users have not ended; it is never freed on a timeout (P-048).</summary>
        public bool Quarantine(Id128 leaseId)
        {
            if (!records.TryGetValue(leaseId, out WorldResourceRecord record) || !record.IsRetained)
            {
                return false;
            }

            records[leaseId] = WithState(record, ResourceRetirementState.Quarantined);
            QuarantinedCount++;
            return true;
        }

        /// <summary>Releases a quarantine after every user ended, allowing a later safe retirement (P-048).</summary>
        public bool ReleaseQuarantine(Id128 leaseId)
        {
            if (!records.TryGetValue(leaseId, out WorldResourceRecord record) ||
                record.State != ResourceRetirementState.Quarantined)
            {
                return false;
            }

            records[leaseId] = WithState(record, ResourceRetirementState.Retiring);
            return true;
        }

        /// <summary>
        /// Retires one instance's leases in reverse acquisition order, attempting every independent cleanup and
        /// aggregating failures (P-048). Leases whose jobs are still outstanding are quarantined instead.
        /// </summary>
        public CleanupReport RetireInstance(PluginInstanceId instance, IReadOnlyList<Id128>? outstandingJobResourceIds, ActivationStamp activation)
        {
            List<Id128> mine = new List<Id128>();
            for (int i = 0; i < acquisitionOrder.Count; i++)
            {
                Id128 id = acquisitionOrder[i];
                AsyncWorkToken token = leases[id].Lease.Token;
                if (records[id].Instance.Equals(instance) && records[id].IsRetained &&
                    token.InstallationGeneration.Equals(activation.Generation) && token.ActivationEpoch.Equals(activation.ActivationEpoch))
                {
                    mine.Add(acquisitionOrder[i]);
                }
            }

            mine.Sort((left, right) => records[right].AcquisitionOrdinal.CompareTo(records[left].AcquisitionOrdinal));

            List<Id128> retired = new List<Id128>();
            List<Id128> failed = new List<Id128>();
            List<Id128> quarantined = new List<Id128>();
            for (int i = 0; i < mine.Count; i++)
            {
                if (IsHeldByJob(mine[i], outstandingJobResourceIds))
                {
                    // Retained by quarantine: the release is deferred, not silently completed.
                    Quarantine(mine[i]);
                    quarantined.Add(mine[i]);
                    continue;
                }

                if (Retire(mine[i]))
                {
                    retired.Add(mine[i]);
                }
                else
                {
                    failed.Add(mine[i]);
                    if (records[mine[i]].State == ResourceRetirementState.Quarantined)
                    {
                        quarantined.Add(mine[i]);
                    }
                }
            }

            return new CleanupReport(retired, failed, quarantined);
        }

        /// <summary>How many leases of one instance are still retained; zero proves a complete teardown (P-048).</summary>
        public int RetainedCountFor(PluginInstanceId instance)
        {
            int count = 0;
            for (int i = 0; i < acquisitionOrder.Count; i++)
            {
                WorldResourceRecord record = records[acquisitionOrder[i]];
                if (record.Instance.Equals(instance) && record.IsRetained)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsHeldByJob(Id128 leaseId, IReadOnlyList<Id128>? outstanding)
        {
            if (outstanding == null)
            {
                return false;
            }

            for (int i = 0; i < outstanding.Count; i++)
            {
                if (outstanding[i].Equals(leaseId))
                {
                    return true;
                }
            }

            return false;
        }

        private static WorldResourceRecord WithState(WorldResourceRecord record, ResourceRetirementState state) =>
            new WorldResourceRecord(
                record.ResourceId,
                record.Kind,
                record.Key,
                record.Owner,
                record.Instance,
                state,
                record.DependsOn,
                record.AcquisitionOrdinal,
                record.Bytes);

        private readonly struct LeaseEntry
        {
            public readonly IManagedResourceLease Lease;
            public readonly PluginInstanceId Instance;

            public LeaseEntry(IManagedResourceLease lease, PluginInstanceId instance)
            {
                Lease = lease;
                Instance = instance;
            }
        }
    }

    /// <summary>
    /// Staged acquisitions of one unpublished operation. Everything prepared here stays inert: the gate is
    /// closed, the readiness is <see cref="ResourceReadiness.Pending"/> and nothing is exposed to gameplay, so
    /// the old published assembly keeps running unchanged (P-029).
    /// </summary>
    public sealed class ResourcePreparationSet
    {
        private readonly IManagedResourceFactory factory;
        private readonly ResourceLedger ledger;
        private readonly WorldId world;
        private readonly PluginInstanceId owner;
        private readonly List<PreparedEntry> entries = new List<PreparedEntry>();
        private uint nextOrdinal;
        private bool published;

        public ResourcePreparationSet(IManagedResourceFactory factory, ResourceLedger ledger, WorldId world, PluginInstanceId owner)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.world = world;
            this.owner = owner;
        }

        /// <summary>World these staged acquisitions belong to (P-004 identity scope).</summary>
        public WorldId World => world;

        /// <summary>Installation that owns every lease in this set (P-048 ownership).</summary>
        public PluginInstanceId Owner => owner;

        public int Count => entries.Count;

        /// <summary>Prepared leases that are still unpublished; the inspectable staged resource table (P-029).</summary>
        public IReadOnlyList<StagedLease> Staged()
        {
            List<StagedLease> staged = new List<StagedLease>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                PreparedEntry entry = entries[i];
                staged.Add(new StagedLease(
                    entry.Request.Resource,
                    entry.Lease.LeaseId,
                    entry.Lease.Readiness,
                    entry.Dependencies,
                    entry.Ordinal));
            }

            return staged;
        }

        /// <summary>
        /// Prepares one resource behind a closed gate. A failure is reported as a value: the caller rejects the
        /// operation and every earlier acquisition is released in reverse dependency order (P-029, P-049).
        /// </summary>
        public bool TryPrepare(
            ResourceKey resource,
            AsyncWorkToken token,
            FrozenPayload config,
            IReadOnlyList<ResourceKey>? dependencies,
            out Id128 leaseId,
            out DiagnosticCode code)
        {
            leaseId = Id128.Zero;
            code = DiagnosticCode.None;

            ManagedResourceRequest request = new ManagedResourceRequest(resource, token, config);
            IManagedResourceLease lease;
            try
            {
                lease = factory.Prepare(request);
            }
            catch (ResourcePreparationException failure)
            {
                code = failure.Code;
                return false;
            }
            catch (Exception)
            {
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            if (lease == null)
            {
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            uint ordinal = nextOrdinal;
            nextOrdinal++;
            if (!ledger.Acquire(lease, WorldResourceKind.ManagedLease, default(OwnerId), owner, ordinal, Id128.Zero))
            {
                code = DiagnosticCode.OwnershipConflict;
                return false;
            }

            leaseId = lease.LeaseId;
            entries.Add(new PreparedEntry(request, lease, ContractCollections.Freeze(dependencies), ordinal));
            entries.Sort(CompareEntries);
            return true;
        }

        /// <summary>
        /// Opens the gates and marks every staged lease ready; called only once publication has committed, so no
        /// callback can run before its activation exists (P-030, P-047).
        /// </summary>
        public int PublishReady()
        {
            if (published)
            {
                return 0;
            }

            published = true;
            int ready = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                PreparedEntry entry = entries[i];
                if (entry.Lease.Gate is ManagedResourceGate gate && !gate.OpenOnPublication())
                {
                    continue;
                }

                ledger.MarkReady(entry.Lease.LeaseId);
                ready++;
            }

            return ready;
        }

        /// <summary>
        /// Releases staged acquisitions in reverse dependency order after a failed or cancelled operation; the
        /// old assembly is untouched because nothing here was ever published (P-029).
        /// </summary>
        public CleanupReport ReleaseStaged()
        {
            List<PreparedEntry> ordered = new List<PreparedEntry>(entries);
            ordered.Sort(CompareReverseDependencyOrder);

            List<Id128> retired = new List<Id128>();
            List<Id128> failed = new List<Id128>();
            List<Id128> quarantined = new List<Id128>();
            for (int i = 0; i < ordered.Count; i++)
            {
                Id128 leaseId = ordered[i].Lease.LeaseId;
                if (ledger.Retire(leaseId))
                {
                    retired.Add(leaseId);
                }
                else
                {
                    failed.Add(leaseId);
                    quarantined.Add(leaseId);
                }
            }

            entries.Clear();
            return new CleanupReport(retired, failed, quarantined);
        }

        private static int CompareEntries(PreparedEntry left, PreparedEntry right) => left.Ordinal.CompareTo(right.Ordinal);

        /// <summary>
        /// Reverse acquisition order. Preparation records dependencies first (P-029 prepares in dependency
        /// order), so releasing the later ordinal first also releases dependents before their dependencies.
        /// </summary>
        private static int CompareReverseDependencyOrder(PreparedEntry left, PreparedEntry right) =>
            right.Ordinal.CompareTo(left.Ordinal);

        private readonly struct PreparedEntry
        {
            public readonly ManagedResourceRequest Request;
            public readonly IManagedResourceLease Lease;
            public readonly IReadOnlyList<ResourceKey> Dependencies;
            public readonly uint Ordinal;

            public PreparedEntry(
                ManagedResourceRequest request,
                IManagedResourceLease lease,
                IReadOnlyList<ResourceKey> dependencies,
                uint ordinal)
            {
                Request = request;
                Lease = lease;
                Dependencies = dependencies;
                Ordinal = ordinal;
            }
        }
    }
}
