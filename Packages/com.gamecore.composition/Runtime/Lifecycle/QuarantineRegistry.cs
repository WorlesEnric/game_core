// GameCore.Composition — the bounded quarantine registry (P-048, 06 s6).
//
// 06 s6 states the rule this file implements: "A published removal can leave a Retiring instance in a quarantine
// registry without leaving it in the active installation graph. This registry is bounded and observable;
// exhaustion rejects further resource acquisition or faults/stops according to host policy rather than dropping
// the references."
//
// So quarantine is the one place where the protocol accepts *not* being able to clean up immediately, and it
// demands the accounting that goes with it: every quarantined reference stays on the books, the book has a limit,
// and hitting the limit is a refusal rather than silence. Three consequences are visible here:
//
//   * a quarantined reference is never silently dropped — removing one requires an explicit release, which only
//     succeeds once the work that held it is gone;
//   * admission of *new* quarantines is refused at the ceiling (`TeardownBlocked`), and the caller decides whether
//     that refuses acquisition or faults/stops;
//   * the registry is inspectable: what is retained, for which instance, holding how many bytes, and since which
//     operation.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>One retained reference: what is held, for whom, from which published removal (P-048).</summary>
    public sealed class QuarantinedResource
    {
        public QuarantinedResource(
            Id128 resourceId,
            ResourceKey key,
            PluginInstanceId instance,
            OperationId operation,
            ResourceRetirementState state,
            ulong bytes,
            string reason)
        {
            ResourceId = resourceId;
            Key = key;
            Instance = instance;
            Operation = operation;
            State = state;
            Bytes = bytes;
            Reason = reason ?? string.Empty;
        }

        /// <summary>Process-local resource identity; never a persisted identity (05 s4).</summary>
        public Id128 ResourceId { get; }

        public ResourceKey Key { get; }

        /// <summary>The installation whose work kept the reference alive.</summary>
        public PluginInstanceId Instance { get; }

        /// <summary>The operation whose publication left the reference retained (P-050).</summary>
        public OperationId Operation { get; }

        public ResourceRetirementState State { get; }

        public ulong Bytes { get; }

        /// <summary>Why it is still held: the observed fence, never an elapsed duration (P-048).</summary>
        public string Reason { get; }

        public override string ToString() =>
            "quarantine(" + ResourceId.ToString() + ", " + Instance.ToString() + ", bytes="
            + Bytes.ToString(CultureInfo.InvariantCulture) + ", " + Reason + ")";
    }

    /// <summary>Result of one quarantine admission attempt; refusal carries the reason a caller must act on.</summary>
    public readonly struct QuarantineAdmission
    {
        public readonly bool Admitted;
        public readonly DiagnosticCode Code;
        public readonly string Detail;

        public QuarantineAdmission(bool admitted, DiagnosticCode code, string detail)
        {
            Admitted = admitted;
            Code = code;
            Detail = detail ?? string.Empty;
        }
    }

    /// <summary>
    /// Bounded, observable registry of resources whose users have not ended. A duplicate admission for one
    /// reference is refused, so the retained set is a set and never a count of attempts.
    /// </summary>
    public sealed class QuarantineRegistry
    {
        private readonly Dictionary<Id128, QuarantinedResource> entries = new Dictionary<Id128, QuarantinedResource>();
        private readonly List<Id128> canonicalOrder = new List<Id128>();

        public QuarantineRegistry(int maxEntries, ulong maxBytes)
        {
            if (maxEntries <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEntries), "A quarantine registry is bounded by a positive entry count (06 s6).");
            }

            MaxEntries = maxEntries;
            MaxBytes = maxBytes;
        }

        public int MaxEntries { get; }

        public ulong MaxBytes { get; }

        public int Count => entries.Count;

        public ulong Bytes { get; private set; }

        /// <summary>Admissions refused because the registry was at its configured ceiling (06 s6).</summary>
        public int ExhaustionCount { get; private set; }

        /// <summary>Admissions refused because the reference was already retained; a set, not a counter.</summary>
        public int DuplicateCount { get; private set; }

        public int ReleasedCount { get; private set; }

        public int AdmittedCount { get; private set; }

        /// <summary>True when the registry cannot accept another retained reference.</summary>
        public bool IsExhausted => Count >= MaxEntries || (MaxBytes != 0UL && Bytes >= MaxBytes);

        public DiagnosticCode LastRefusalCode { get; private set; } = DiagnosticCode.None;

        /// <summary>Retained references in canonical resource order; the inspectable quarantine ledger.</summary>
        public IReadOnlyList<QuarantinedResource> Entries()
        {
            List<QuarantinedResource> all = new List<QuarantinedResource>(canonicalOrder.Count);
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                all.Add(entries[canonicalOrder[i]]);
            }

            return all;
        }

        /// <summary>Retained references of one installation, in canonical resource order.</summary>
        public IReadOnlyList<QuarantinedResource> EntriesFor(PluginInstanceId instance)
        {
            List<QuarantinedResource> mine = new List<QuarantinedResource>();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                QuarantinedResource entry = entries[canonicalOrder[i]];
                if (entry.Instance.Equals(instance))
                {
                    mine.Add(entry);
                }
            }

            return mine;
        }

        public bool Contains(Id128 resourceId) => entries.ContainsKey(resourceId);

        public bool TryGet(Id128 resourceId, out QuarantinedResource? entry)
        {
            if (entries.TryGetValue(resourceId, out QuarantinedResource found))
            {
                entry = found;
                return true;
            }

            entry = null;
            return false;
        }

        /// <summary>
        /// Admits one retained reference. At the ceiling the admission is refused with `TeardownBlocked`: the host
        /// then refuses acquisition or stops, and the existing references stay on the books (06 s6).
        /// </summary>
        public QuarantineAdmission Admit(
            Id128 resourceId,
            ResourceKey key,
            PluginInstanceId instance,
            OperationId operation,
            ResourceRetirementState state,
            ulong bytes,
            string reason)
        {
            if (entries.ContainsKey(resourceId))
            {
                DuplicateCount++;
                LastRefusalCode = DiagnosticCode.OwnershipConflict;
                return new QuarantineAdmission(false, DiagnosticCode.OwnershipConflict, "the reference is already quarantined; a quarantine is a set, not a counter");
            }

            if (IsExhausted)
            {
                ExhaustionCount++;
                LastRefusalCode = DiagnosticCode.TeardownBlocked;
                return new QuarantineAdmission(false, DiagnosticCode.TeardownBlocked, "the bounded quarantine registry is exhausted; further acquisition is refused rather than dropping references (06 s6)");
            }

            QuarantinedResource entry = new QuarantinedResource(resourceId, key, instance, operation, state, bytes, reason);
            entries.Add(resourceId, entry);
            canonicalOrder.Add(resourceId);
            canonicalOrder.Sort(CompareIds);
            Bytes += bytes;
            AdmittedCount++;
            return new QuarantineAdmission(true, DiagnosticCode.None, string.Empty);
        }

        /// <summary>
        /// Releases one retained reference after every user ended. This is the only way a reference leaves the
        /// registry, so its release is always explicit rather than implied by elapsed time (P-048).
        /// </summary>
        public bool Release(Id128 resourceId)
        {
            if (!entries.TryGetValue(resourceId, out QuarantinedResource entry))
            {
                return false;
            }

            entries.Remove(resourceId);
            canonicalOrder.Remove(resourceId);
            Bytes = Bytes >= entry.Bytes ? Bytes - entry.Bytes : 0UL;
            ReleasedCount++;
            return true;
        }

        /// <summary>Releases every reference of one installation; returns how many were released.</summary>
        public int ReleaseInstance(PluginInstanceId instance)
        {
            List<Id128> mine = new List<Id128>();
            for (int i = 0; i < canonicalOrder.Count; i++)
            {
                if (entries[canonicalOrder[i]].Instance.Equals(instance))
                {
                    mine.Add(canonicalOrder[i]);
                }
            }

            int released = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                if (Release(mine[i]))
                {
                    released++;
                }
            }

            return released;
        }

        private static int CompareIds(Id128 left, Id128 right) => left.CompareTo(right);
    }
}
