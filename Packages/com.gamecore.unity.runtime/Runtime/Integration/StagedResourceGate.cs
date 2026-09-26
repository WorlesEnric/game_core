// GameCore.Unity.Runtime — W2 integration seam: staged (inert) plan resource leases.
//
// GC-008's `InertAcquisitionSet` stages leases that must not emit gameplay before publication (P-029) and releases
// them in reverse order with aggregated failures (P-048). It takes an `IPlanResourceGate`, which is the host's
// authority: who may acquire, and how many bytes are still available. No gate implementation existed in production
// code, so every planner caller had to invent one; this is the boring, bounded default.
//
// The gate owns no native memory and reaches no engine API. It hands out deterministic lease identities, refuses
// past a declared byte ceiling, and reports a release it cannot honour instead of throwing from a cleanup path.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Unity.Runtime.Faults;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>One staged lease the gate handed out, kept so a release can be checked against it.</summary>
    public readonly struct StagedResourceLease
    {
        public readonly ResourceKey Resource;
        public readonly Id128 LeaseId;
        public readonly ulong Bytes;

        public StagedResourceLease(ResourceKey resource, Id128 leaseId, ulong bytes)
        {
            Resource = resource;
            LeaseId = leaseId;
            Bytes = bytes;
        }

        public override string ToString() => Resource.ToString() + "=" + Bytes.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Bounded in-memory <see cref="IPlanResourceGate"/>: deterministic lease identities, a hard byte ceiling and a
    /// counted refusal. A lease is released at most once; a second release is reported, never silently accepted.
    /// </summary>
    public sealed class StagedResourceGate : IPlanResourceGate
    {
        private readonly Dictionary<Id128, StagedResourceLease> leases = new Dictionary<Id128, StagedResourceLease>();
        private readonly ulong byteCeiling;
        private readonly Id128 category;
#if GAMECORE_FAULT_INJECTION
        private readonly AssemblyFaultInjection? faults;
#endif

        private ulong nextLease;

        public StagedResourceGate(ulong byteCeiling, Id128 category)
        {
            this.byteCeiling = byteCeiling;
            this.category = category;
#if GAMECORE_FAULT_INJECTION
            this.faults = null;
#endif
        }

#if GAMECORE_FAULT_INJECTION
        /// <summary>
        /// The gate of one world when a fault latch is available (GC-017). The latch is the world's
        /// (<see cref="UnityWorldHost.Faults"/>), so the acquisition and cleanup boundaries of TEST-016 rows 2 and
        /// 8 are reachable through the same instance the publisher and the driver use. This constructor does not
        /// exist in a shipping compilation, and neither does the latch it takes.
        /// </summary>
        public StagedResourceGate(ulong byteCeiling, Id128 category, AssemblyFaultInjection? faults)
        {
            this.byteCeiling = byteCeiling;
            this.category = category;
            this.faults = faults;
        }
#endif

        /// <summary>Leases handed out so far; the acquisition counter of the gate (P-029).</summary>
        public int AcquiredCount { get; private set; }

        /// <summary>Leases released successfully.</summary>
        public int ReleasedCount { get; private set; }

        /// <summary>Acquisition attempts refused because the byte ceiling was reached (P-022, P-029).</summary>
        public int BudgetExceededCount { get; private set; }

#if GAMECORE_FAULT_INJECTION
        /// <summary>
        /// Acquisition attempts refused by an injected fault, kept apart from <see cref="BudgetExceededCount"/> so a
        /// budget reading never absorbs a fault refusal (GC-017, TEST-016 row 2).
        /// </summary>
        public int InjectionRefusalCount { get; private set; }

        /// <summary>Releases refused by an injected fault; the lease stays owned here (GC-017, row 8).</summary>
        public int InjectionReleaseRefusalCount { get; private set; }
#endif

        /// <summary>Releases for an identity this gate never handed out; a caller defect, reported not thrown.</summary>
        public int UnknownReleaseCount { get; private set; }

        /// <summary>Bytes currently held by live leases.</summary>
        public ulong StagedBytes { get; private set; }

        /// <summary>Live leases in acquisition order, for diagnostics.</summary>
        public int LiveLeaseCount => leases.Count;

        /// <inheritdoc />
        public bool TryAcquire(ResourceKey resource, ulong bytes, out Id128 leaseId, out DiagnosticCode code)
        {
#if GAMECORE_FAULT_INJECTION
            // Acquisition boundary (GC-017, TEST-016 row 2): the refusal is a value, not a throw, so the plan's
            // staged set records a failed acquisition and the caller refuses the plan without touching live state.
            if (FaultReach.Refuse(faults, FaultBoundary.Acquisition, "injected acquisition fault: the lease is refused"))
            {
                InjectionRefusalCount++;
                leaseId = default(Id128);
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }
#endif

            if (StagedBytes + bytes > byteCeiling)
            {
                BudgetExceededCount++;
                leaseId = default(Id128);
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            nextLease++;
            leaseId = new Id128(category.High, category.Low ^ nextLease);
            leases.Add(leaseId, new StagedResourceLease(resource, leaseId, bytes));
            StagedBytes += bytes;
            AcquiredCount++;
            code = DiagnosticCode.None;
            return true;
        }

        /// <inheritdoc />
        public bool Release(Id128 leaseId, out DiagnosticCode code)
        {
#if GAMECORE_FAULT_INJECTION
            // Cleanup boundary (GC-017, TEST-016 row 8): a refused release is reported to the caller's aggregation
            // (P-048) and the lease stays owned here, so "other safe cleanup proceeds" remains observable while the
            // failed release is never reported as a successful disposal.
            if (FaultReach.Refuse(faults, FaultBoundary.Cleanup, "injected cleanup fault: the release is refused"))
            {
                InjectionReleaseRefusalCount++;
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }
#endif

            if (leaseId.IsDefault || !leases.TryGetValue(leaseId, out StagedResourceLease lease))
            {
                UnknownReleaseCount++;
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            leases.Remove(leaseId);
            StagedBytes = StagedBytes > lease.Bytes ? StagedBytes - lease.Bytes : 0UL;
            ReleasedCount++;
            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>Live leases in acquisition order, so a test can assert what was actually staged (P-029).</summary>
        public IReadOnlyList<StagedResourceLease> LiveLeases()
        {
            var ordered = new List<StagedResourceLease>(leases.Values);
            ordered.Sort((left, right) => left.LeaseId.CompareTo(right.LeaseId));
            return ordered;
        }

        public override string ToString()
            => "stagedGate(acquired=" + AcquiredCount.ToString(CultureInfo.InvariantCulture)
                + ", released=" + ReleasedCount.ToString(CultureInfo.InvariantCulture)
                + ", stagedBytes=" + StagedBytes.ToString(CultureInfo.InvariantCulture)
                + ", ceiling=" + byteCeiling.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
