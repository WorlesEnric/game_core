// GameCore.Planning — inert resource acquisition and cleanup accounting (GC-008).
//
// Normative sources: 00 P-029 (allocate unpublished bindings, immutable blob/config tables, inert system
// instances and resource leases; registrations/subscriptions acquired here MUST remain gated and cannot emit
// gameplay commands until publication; a preparation failure releases staged leases in reverse dependency order
// and leaves the old assembly intact), P-048 (dispose each lease at most once, attempt every independent cleanup,
// aggregate failures, retain anything still reachable by unfinished work) and 05 s4 (`ResourceStaging`: staged
// acquisition handles, dependencies, readiness, retiring lease ids, scratch capacity — process-local, never part
// of the plan's stable hash).
//
// The gate is a narrow port, not a resource system: production wires it to `IManagedResourceFactory`/the
// composition host's staged resources (GC-004) and the fixture wires it to a deterministic fake. What this type
// owns is the *invariant* the protocol cares about — while a plan is unpublished, nothing it acquired can be
// observed or emit gameplay, and a failure before publication releases exactly what was acquired.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>
    /// Narrow acquisition port of one plan. A gate is control-plane only: it acquires a lease and reports failure
    /// as a code, never as a live gameplay effect (05 s5 `IManagedResourceFactory.Prepare`).
    /// </summary>
    public interface IPlanResourceGate
    {
        /// <summary>Acquires one lease behind a closed gate; the lease id is process-local (05 s4).</summary>
        bool TryAcquire(ResourceKey resource, ulong bytes, out Id128 leaseId, out DiagnosticCode code);

        /// <summary>Releases one lease at most once; a failure keeps the lease retained, it never frees unsafely.</summary>
        bool Release(Id128 leaseId, out DiagnosticCode code);
    }

    /// <summary>One staged, still-inert acquisition of a plan (P-029).</summary>
    public readonly struct InertLease
    {
        public readonly ResourceKey Resource;
        public readonly Id128 LeaseId;
        public readonly ulong Bytes;

        /// <summary>Acquisition ordinal of the plan; leases retire in reverse acquisition order (P-048).</summary>
        public readonly uint AcquisitionOrdinal;

        public readonly ResourceReadiness Readiness;

        public readonly IReadOnlyList<ResourceKey> Dependencies;

        public InertLease(
            ResourceKey resource,
            Id128 leaseId,
            ulong bytes,
            uint acquisitionOrdinal,
            ResourceReadiness readiness,
            IReadOnlyList<ResourceKey>? dependencies)
        {
            Resource = resource;
            LeaseId = leaseId;
            Bytes = bytes;
            AcquisitionOrdinal = acquisitionOrdinal;
            Readiness = readiness;
            Dependencies = ContractCollections.Freeze(dependencies);
        }

        public override string ToString() =>
            Resource.ToString() + (Readiness == ResourceReadiness.Ready ? ":ready" : ":inert")
            + "@" + AcquisitionOrdinal.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Aggregated result of releasing every remaining acquisition of a plan (P-048).</summary>
    public sealed class AcquisitionCleanup
    {
        public static readonly AcquisitionCleanup Empty =
            new AcquisitionCleanup(null, null, null, 0);

        public AcquisitionCleanup(
            IReadOnlyList<Id128>? released,
            IReadOnlyList<Id128>? failed,
            IReadOnlyList<Id128>? quarantined,
            int attempted)
        {
            Released = ContractCollections.Freeze(released);
            Failed = ContractCollections.Freeze(failed);
            Quarantined = ContractCollections.Freeze(quarantined);
            Attempted = attempted;
        }

        public IReadOnlyList<Id128> Released { get; }

        /// <summary>Leases whose release threw; they stay tracked instead of being reported as disposed.</summary>
        public IReadOnlyList<Id128> Failed { get; }

        /// <summary>Leases retained because unfinished work may still reach them; a timeout never frees these.</summary>
        public IReadOnlyList<Id128> Quarantined { get; }

        public int Attempted { get; }

        /// <summary>True when a published plan could not finish cleanup: `PublishedWithCleanupErrors` (05 s4).</summary>
        public bool HasCleanupErrors => Failed.Count != 0 || Quarantined.Count != 0;

        public override string ToString() =>
            "released=" + Released.Count.ToString(CultureInfo.InvariantCulture)
            + ";failed=" + Failed.Count.ToString(CultureInfo.InvariantCulture)
            + ";quarantined=" + Quarantined.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The staged acquisitions of one plan. Before publication every lease is inert, so a staged subscription or
    /// binding cannot submit gameplay through it (P-029); publication is the only moment the gates open, and a
    /// rejected or cancelled plan releases its leases in reverse acquisition order.
    /// </summary>
    public sealed class InertAcquisitionSet
    {
        private readonly IPlanResourceGate gate;
        private readonly List<InertLease> leases = new List<InertLease>();
        private readonly List<Id128> released = new List<Id128>();
        private readonly HashSet<Id128> releasedSet = new HashSet<Id128>();

        public InertAcquisitionSet(IPlanResourceGate gate, OperationId operation)
        {
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
            Operation = operation;
        }

        public OperationId Operation { get; }

        /// <summary>True once <see cref="OpenGates"/> ran at the publication boundary.</summary>
        public bool IsPublished { get; private set; }

        /// <summary>Acquisitions in acquisition order; every one is inert until publication (P-029).</summary>
        public IReadOnlyList<InertLease> Leases => leases;

        public int Count => leases.Count;

        public int ReleasedCount => released.Count;

        /// <summary>Failed acquisitions; a plan whose acquisition failed rejects with this count reported.</summary>
        public int FailedCount { get; private set; }

        public ulong StagedBytes { get; private set; }

        /// <summary>
        /// Whether the staged resources may emit gameplay. False for every unpublished plan: the gate is the
        /// whole point of staging, and a caller cannot bypass it because the leases carry no callable surface.
        /// </summary>
        public bool CanEmitGameplay => IsPublished;

        /// <summary>Acquires one inert lease; failure records the gate's own code and releases nothing (P-029).</summary>
        public bool TryAcquire(
            ResourceKey resource,
            ulong bytes,
            IReadOnlyList<ResourceKey>? dependencies,
            out DiagnosticCode code)
        {
            if (IsPublished)
            {
                // After publication the leases are already live: a late acquisition would not be staged work.
                code = DiagnosticCode.TooLate;
                return false;
            }

            if (!gate.TryAcquire(resource, bytes, out Id128 leaseId, out code))
            {
                FailedCount++;
                return false;
            }

            leases.Add(new InertLease(
                resource,
                leaseId,
                bytes,
                (uint)leases.Count,
                ResourceReadiness.Pending,
                dependencies));
            StagedBytes += bytes;
            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>Publication: this is the only transition that makes the staged gates usable (P-029, P-030).</summary>
        public int OpenGates()
        {
            if (IsPublished)
            {
                return 0;
            }

            IsPublished = true;
            int opened = 0;
            for (int i = 0; i < leases.Count; i++)
            {
                if (releasedSet.Contains(leases[i].LeaseId))
                {
                    continue;
                }

                leases[i] = new InertLease(
                    leases[i].Resource,
                    leases[i].LeaseId,
                    leases[i].Bytes,
                    leases[i].AcquisitionOrdinal,
                    ResourceReadiness.Ready,
                    leases[i].Dependencies);
                opened++;
            }

            return opened;
        }

        /// <summary>
        /// Releases every unreleased lease in reverse acquisition order, attempting all of them and aggregating
        /// failures (P-048). Idempotent per lease: a repeated release is never attempted twice.
        /// </summary>
        public AcquisitionCleanup ReleaseAll(IReadOnlyList<Id128>? quarantined = null)
        {
            var failed = new List<Id128>();
            int attempted = 0;
            for (int i = leases.Count - 1; i >= 0; i--)
            {
                Id128 leaseId = leases[i].LeaseId;
                if (releasedSet.Contains(leaseId))
                {
                    continue;
                }

                attempted++;
                if (gate.Release(leaseId, out DiagnosticCode code) && code == DiagnosticCode.None)
                {
                    releasedSet.Add(leaseId);
                    released.Add(leaseId);
                    continue;
                }

                failed.Add(leaseId);
            }

            var retained = new List<Id128>();
            if (quarantined != null)
            {
                for (int i = 0; i < quarantined.Count; i++)
                {
                    retained.Add(quarantined[i]);
                }
            }

            return new AcquisitionCleanup(released, failed, retained, attempted);
        }

        /// <summary>Projects the staged acquisitions onto the plan's contract-level `ResourceStaging` (05 s4).</summary>
        public ResourceStaging ToStaging(ulong scratchCapacityBytes, IReadOnlyList<Id128>? retiringLeaseIds)
        {
            var staged = new List<StagedLease>(leases.Count);
            for (int i = 0; i < leases.Count; i++)
            {
                InertLease lease = leases[i];
                staged.Add(new StagedLease(
                    lease.Resource,
                    lease.LeaseId,
                    lease.Readiness,
                    lease.Dependencies,
                    lease.AcquisitionOrdinal));
            }

            return new ResourceStaging(staged, retiringLeaseIds, scratchCapacityBytes);
        }

        /// <summary>Lease ids that are still held (neither released nor failed), for diagnostics (P-048).</summary>
        public IReadOnlyList<Id128> RetainedLeaseIds()
        {
            var retained = new List<Id128>();
            for (int i = 0; i < leases.Count; i++)
            {
                if (!releasedSet.Contains(leases[i].LeaseId))
                {
                    retained.Add(leases[i].LeaseId);
                }
            }

            return retained;
        }
    }
}
