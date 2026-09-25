// GameCore.Composition.Diagnostics — bounded retention of compact provenance (GC-016).
//
// P-026: "Records may be interned/compacted but remain reconstructable for the retained epoch." Retention is the
// other half of that sentence: a world that runs for days cannot keep provenance for every epoch it ever published,
// so this store keeps a bounded number of epochs and says so when a reader asks for one it dropped.
//
// The memory discipline is deliberate and testable:
//
//   * one epoch's records are interned against a per-epoch evidence table, so evidence repeated across hundreds of
//     targets costs one entry;
//   * the entry table belongs to the epoch, so evicting an epoch releases exactly the memory that epoch introduced;
//   * a publish that would exceed the declared record or entry bound is *refused* before any mutation, because a
//     silently truncated provenance set would be a lie about what the world decided (P-052, TEST-007 discipline);
//   * a record whose evidence keys do not resolve is refused as `MissingDependency`, so a retained record can
//     always be reconstructed completely.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition.Diagnostics
{
    /// <summary>How one provenance publish ended.</summary>
    public enum ProvenancePublishOutcome
    {
        /// <summary>The epoch or staged operation was recorded for the first time.</summary>
        Published = 0,

        /// <summary>The same epoch or operation was already recorded; the newer set replaced it in place.</summary>
        Replaced = 1,

        /// <summary>Nothing was recorded; <see cref="ProvenancePublishReport.Code"/> names why.</summary>
        Refused = 2,
    }

    /// <summary>Result of one provenance publish: what happened, the stable code, and the retained sizes.</summary>
    public readonly struct ProvenancePublishReport
    {
        public ProvenancePublishReport(
            ProvenancePublishOutcome outcome,
            DiagnosticCode code,
            SnapshotToken token,
            OperationId operation,
            int recordCount,
            int entryCount)
        {
            Outcome = outcome;
            Code = code;
            Token = token;
            Operation = operation;
            RecordCount = recordCount;
            EntryCount = entryCount;
        }

        public ProvenancePublishOutcome Outcome { get; }

        public DiagnosticCode Code { get; }

        /// <summary>The published epoch token; default for a staged publish.</summary>
        public SnapshotToken Token { get; }

        /// <summary>The staged operation; default for a published epoch.</summary>
        public OperationId Operation { get; }

        /// <summary>Records retained for this token/operation, or zero when the publish was refused.</summary>
        public int RecordCount { get; }

        /// <summary>Evidence entries retained for it, or zero when the publish was refused.</summary>
        public int EntryCount { get; }

        public bool Succeeded => Outcome != ProvenancePublishOutcome.Refused;

        public override string ToString() =>
            "ProvenancePublish(" + Outcome.ToString() + ", " + DiagnosticCodeText.Of(Code)
            + ", records=" + RecordCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The retained provenance of one epoch (or one staged operation): its canonically ordered records, its interned
    /// evidence, the state dispositions of the same publication and the digest over the whole set.
    /// </summary>
    public sealed class ProvenanceEpoch
    {
        private readonly Dictionary<PairKey, List<CapabilityProvenance>> byPair =
            new Dictionary<PairKey, List<CapabilityProvenance>>();

        internal ProvenanceEpoch(
            SnapshotToken token,
            OperationId operation,
            bool staged,
            IReadOnlyList<CapabilityProvenance> records,
            IReadOnlyList<ProvenanceEntry> entries,
            IReadOnlyList<StateDisposition> dispositions)
        {
            Token = token;
            Operation = operation;
            IsStaged = staged;
            Records = records;
            Entries = entries;
            StateDispositions = dispositions;
            Digest = ProvenanceDigest.Of(records);

            for (int i = 0; i < records.Count; i++)
            {
                CapabilityProvenance record = records[i];
                var key = new PairKey(record.Target, record.Capability);
                if (!byPair.TryGetValue(key, out List<CapabilityProvenance>? list) || list == null)
                {
                    list = new List<CapabilityProvenance>();
                    byPair.Add(key, list);
                }

                list.Add(record);
            }
        }

        public SnapshotToken Token { get; }

        public OperationId Operation { get; }

        /// <summary>True when these records describe a still-unpublished plan, never world observation (05 s5).</summary>
        public bool IsStaged { get; }

        /// <summary>Every record of the epoch, canonically ordered.</summary>
        public IReadOnlyList<CapabilityProvenance> Records { get; }

        /// <summary>Interned evidence of this epoch, released with it.</summary>
        public IReadOnlyList<ProvenanceEntry> Entries { get; }

        public IReadOnlyList<StateDisposition> StateDispositions { get; }

        /// <summary>Canonical digest over <see cref="Records"/>, independent of how they were enumerated.</summary>
        public ContentHash Digest { get; }

        public int RecordCount => Records.Count;

        public int EntryCount => Entries.Count;

        public int DistinctPairCount => byPair.Count;

        /// <summary>The records of one (target, capability) pair, or false when the pair has none.</summary>
        public bool TryGetPair(TargetId target, CapabilityId capability, out IReadOnlyList<CapabilityProvenance>? records)
        {
            if (byPair.TryGetValue(new PairKey(target, capability), out List<CapabilityProvenance>? found) && found != null)
            {
                records = found;
                return true;
            }

            records = null;
            return false;
        }

        /// <summary>Resolves one interned evidence key within this epoch.</summary>
        public bool TryResolveEntry(Id128 key, out ProvenanceEntry? entry)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Key.Equals(key))
                {
                    entry = Entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public override string ToString() =>
            "ProvenanceEpoch(" + (IsStaged ? Operation.ToString() : Token.ToString())
            + ", records=" + RecordCount.ToString(CultureInfo.InvariantCulture)
            + ", entries=" + EntryCount.ToString(CultureInfo.InvariantCulture) + ")";

        /// <summary>Pair identity: stable ids only, never a string, so two runs index identically.</summary>
        internal readonly struct PairKey : IEquatable<PairKey>
        {
            public PairKey(TargetId target, CapabilityId capability)
            {
                Target = target;
                Capability = capability;
            }

            public TargetId Target { get; }

            public CapabilityId Capability { get; }

            public bool Equals(PairKey other) =>
                Target.Equals(other.Target) && Capability.Equals(other.Capability);

            public override bool Equals(object? obj) => obj is PairKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Target.GetHashCode() * 397) ^ Capability.GetHashCode();
                }
            }
        }
    }

    /// <summary>
    /// Bounded store of reconstructed-able provenance for the epochs and staged operations this world still retains.
    /// Publishing is a pure recording step: it never mutates world state, never publishes a plan and never refuses
    /// the operation it describes (P-051).
    /// </summary>
    public sealed class ProvenanceStore
    {
        private readonly Dictionary<SnapshotToken, ProvenanceEpoch> epochs =
            new Dictionary<SnapshotToken, ProvenanceEpoch>();
        private readonly Queue<SnapshotToken> epochOrder = new Queue<SnapshotToken>();
        private readonly Dictionary<OperationId, ProvenanceEpoch> staged =
            new Dictionary<OperationId, ProvenanceEpoch>();
        private readonly Queue<OperationId> stagedOrder = new Queue<OperationId>();

        public ProvenanceStore(WorldId world, int epochRetention, int maxRecordsPerEpoch, int maxEntriesPerEpoch, int stagedRetention)
        {
            World = world;
            if (epochRetention <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(epochRetention), "Epoch retention must be positive (P-026).");
            }

            if (maxRecordsPerEpoch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRecordsPerEpoch), "A record bound must be positive (P-026).");
            }

            if (maxEntriesPerEpoch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEntriesPerEpoch), "An evidence bound must be positive.");
            }

            if (stagedRetention <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stagedRetention), "Staged retention must be positive (P-026).");
            }

            EpochRetention = epochRetention;
            MaxRecordsPerEpoch = maxRecordsPerEpoch;
            MaxEntriesPerEpoch = maxEntriesPerEpoch;
            StagedRetention = stagedRetention;
        }

        public WorldId World { get; }

        /// <summary>Published epochs retained before the oldest is dropped.</summary>
        public int EpochRetention { get; }

        /// <summary>Records one epoch may carry; more is refused rather than truncated.</summary>
        public int MaxRecordsPerEpoch { get; }

        /// <summary>Evidence entries one epoch may intern; more is refused rather than truncated.</summary>
        public int MaxEntriesPerEpoch { get; }

        /// <summary>Staged operations retained before the oldest is dropped.</summary>
        public int StagedRetention { get; }

        public int EpochCount => epochs.Count;

        public int StagedCount => staged.Count;

        /// <summary>Epochs dropped by retention; a reader for one of them receives `CursorExpired`.</summary>
        public int DroppedEpochCount { get; private set; }

        public int DroppedStagedCount { get; private set; }

        public int PublishedEpochCount { get; private set; }

        public int PublishedStagedCount { get; private set; }

        /// <summary>Evidence keys interned onto an already retained entry of the same epoch.</summary>
        public int InternedEntryCount { get; private set; }

        /// <summary>Publishes refused before any mutation (bound, foreign world or unresolvable evidence).</summary>
        public int RefusedPublishCount { get; private set; }

        /// <summary>Reconstructions served.</summary>
        public int ReconstructionCount { get; private set; }

        /// <summary>Reconstruction requests for an epoch or operation outside retention.</summary>
        public int ExpiredReconstructionCount { get; private set; }

        /// <summary>Records the provenance of one published epoch image (P-026).</summary>
        public ProvenancePublishReport PublishEpoch(
            SnapshotToken token,
            IReadOnlyList<CapabilityProvenance>? records,
            IReadOnlyList<ProvenanceEntry>? entries,
            IReadOnlyList<StateDisposition>? dispositions)
        {
            if (!token.World.Session.Equals(World.Session))
            {
                RefusedPublishCount++;
                return Refused(default(SnapshotToken), default(OperationId), DiagnosticCode.StaleHandle);
            }

            return Publish(token, default(OperationId), false, records, entries, dispositions);
        }

        /// <summary>Records staged-plan provenance for one operation that has not published; reported distinctly.</summary>
        public ProvenancePublishReport PublishStaged(
            OperationId operation,
            IReadOnlyList<CapabilityProvenance>? records,
            IReadOnlyList<ProvenanceEntry>? entries,
            IReadOnlyList<StateDisposition>? dispositions)
        {
            if (!operation.World.Session.Equals(World.Session))
            {
                RefusedPublishCount++;
                return Refused(default(SnapshotToken), default(OperationId), DiagnosticCode.StaleHandle);
            }

            return Publish(default(SnapshotToken), operation, true, records, entries, dispositions);
        }

        /// <summary>The retained provenance of one published epoch, when it is still retained.</summary>
        public bool TryGetEpoch(SnapshotToken token, out ProvenanceEpoch? epoch) =>
            epochs.TryGetValue(token, out epoch);

        /// <summary>The retained provenance of one staged operation, when it is still retained.</summary>
        public bool TryGetStaged(OperationId operation, out ProvenanceEpoch? epoch) =>
            staged.TryGetValue(operation, out epoch);

        /// <summary>Drops one staged operation's provenance, e.g. when its plan is released (P-046).</summary>
        public bool DropStaged(OperationId operation)
        {
            if (!staged.Remove(operation))
            {
                return false;
            }

            return true;
        }

        public bool TryReconstruct(
            SnapshotToken token,
            TargetId target,
            CapabilityId capability,
            uint offset,
            uint maxRecords,
            out ProvenanceReconstruction? reconstruction,
            out DiagnosticCode code)
        {
            if (!TryGetEpoch(token, out ProvenanceEpoch? epoch) || epoch == null)
            {
                ExpiredReconstructionCount++;
                reconstruction = null;
                code = DiagnosticCode.CursorExpired;
                return false;
            }

            return Reconstruct(epoch, token, default(OperationId), target, capability, offset, maxRecords, out reconstruction, out code);
        }

        public bool TryReconstructStaged(
            OperationId operation,
            TargetId target,
            CapabilityId capability,
            uint offset,
            uint maxRecords,
            out ProvenanceReconstruction? reconstruction,
            out DiagnosticCode code)
        {
            if (!TryGetStaged(operation, out ProvenanceEpoch? epoch) || epoch == null)
            {
                ExpiredReconstructionCount++;
                reconstruction = null;
                code = DiagnosticCode.CursorExpired;
                return false;
            }

            return Reconstruct(epoch, default(SnapshotToken), operation, target, capability, offset, maxRecords, out reconstruction, out code);
        }

        /// <summary>Digest of one published pair's whole record set: the observable identity of that explanation.</summary>
        public ContentHash DigestOf(SnapshotToken token, TargetId target, CapabilityId capability)
        {
            if (!TryGetEpoch(token, out ProvenanceEpoch? epoch) || epoch == null)
            {
                return ContentHash.Empty;
            }

            return epoch.TryGetPair(target, capability, out IReadOnlyList<CapabilityProvenance>? records) && records != null
                ? ProvenanceDigest.Of(records)
                : ContentHash.Empty;
        }

        /// <summary>Digest of one staged pair's whole record set.</summary>
        public ContentHash DigestOfStaged(OperationId operation, TargetId target, CapabilityId capability)
        {
            if (!TryGetStaged(operation, out ProvenanceEpoch? epoch) || epoch == null)
            {
                return ContentHash.Empty;
            }

            return epoch.TryGetPair(target, capability, out IReadOnlyList<CapabilityProvenance>? records) && records != null
                ? ProvenanceDigest.Of(records)
                : ContentHash.Empty;
        }

        /// <summary>Distinct (target, capability) pairs the retained epoch has provenance for.</summary>
        public int PairCount(SnapshotToken token) =>
            TryGetEpoch(token, out ProvenanceEpoch? epoch) && epoch != null ? epoch.DistinctPairCount : 0;

        /// <summary>Every target the retained epoch has provenance for, ascending and duplicate-free.</summary>
        public IReadOnlyList<TargetId> Targets(SnapshotToken token)
        {
            if (!TryGetEpoch(token, out ProvenanceEpoch? epoch) || epoch == null)
            {
                return Array.Empty<TargetId>();
            }

            var targets = new List<TargetId>();
            for (int i = 0; i < epoch.Records.Count; i++)
            {
                TargetId candidate = epoch.Records[i].Target;
                bool known = false;
                for (int k = 0; k < targets.Count; k++)
                {
                    if (targets[k].Equals(candidate))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    targets.Add(candidate);
                }
            }

            targets.Sort(CompareTargets);
            return targets;
        }

        private ProvenancePublishReport Publish(
            SnapshotToken token,
            OperationId operation,
            bool isStaged,
            IReadOnlyList<CapabilityProvenance>? records,
            IReadOnlyList<ProvenanceEntry>? entries,
            IReadOnlyList<StateDisposition>? dispositions)
        {
            int recordCount = records == null ? 0 : records.Count;
            int entryCount = entries == null ? 0 : entries.Count;
            if (recordCount > MaxRecordsPerEpoch || entryCount > MaxEntriesPerEpoch)
            {
                // Refused before any mutation: a truncated provenance set would misreport what the world decided.
                RefusedPublishCount++;
                return Refused(token, operation, DiagnosticCode.BudgetExceeded);
            }

            var interned = new List<ProvenanceEntry>(entryCount);
            var internedKeys = new HashSet<Id128>();
            for (int i = 0; i < entryCount; i++)
            {
                ProvenanceEntry entry = entries![i];
                if (internedKeys.Add(entry.Key))
                {
                    interned.Add(entry);
                }
                else
                {
                    InternedEntryCount++;
                }
            }

            var ordered = new List<CapabilityProvenance>(recordCount);
            for (int i = 0; i < recordCount; i++)
            {
                CapabilityProvenance record = records![i];
                for (int k = 0; k < record.EvidenceKeys.Count; k++)
                {
                    if (!internedKeys.Contains(record.EvidenceKeys[k]))
                    {
                        // A record whose evidence cannot be resolved is never retained: reconstruction must be total.
                        RefusedPublishCount++;
                        return Refused(token, operation, DiagnosticCode.MissingDependency);
                    }
                }

                ordered.Add(record);
            }

            ordered.Sort(CompareRecords);
            var dispositionList = new List<StateDisposition>();
            if (dispositions != null)
            {
                for (int i = 0; i < dispositions.Count; i++)
                {
                    dispositionList.Add(dispositions[i]);
                }
            }

            var epoch = new ProvenanceEpoch(
                token,
                operation,
                isStaged,
                ordered,
                interned,
                dispositionList);

            ProvenancePublishOutcome outcome;
            if (isStaged)
            {
                outcome = staged.ContainsKey(operation)
                    ? ProvenancePublishOutcome.Replaced
                    : ProvenancePublishOutcome.Published;
                if (outcome == ProvenancePublishOutcome.Published)
                {
                    PublishedStagedCount++;
                    stagedOrder.Enqueue(operation);
                }

                staged[operation] = epoch;
                while (staged.Count > StagedRetention)
                {
                    staged.Remove(stagedOrder.Dequeue());
                    DroppedStagedCount++;
                }
            }
            else
            {
                outcome = epochs.ContainsKey(token)
                    ? ProvenancePublishOutcome.Replaced
                    : ProvenancePublishOutcome.Published;
                if (outcome == ProvenancePublishOutcome.Published)
                {
                    PublishedEpochCount++;
                    epochOrder.Enqueue(token);
                }

                epochs[token] = epoch;
                while (epochs.Count > EpochRetention)
                {
                    epochs.Remove(epochOrder.Dequeue());
                    DroppedEpochCount++;
                }
            }

            return new ProvenancePublishReport(
                outcome, DiagnosticCode.None, token, operation, ordered.Count, interned.Count);
        }

        private bool Reconstruct(
            ProvenanceEpoch epoch,
            SnapshotToken token,
            OperationId operation,
            TargetId target,
            CapabilityId capability,
            uint offset,
            uint maxRecords,
            out ProvenanceReconstruction? reconstruction,
            out DiagnosticCode code)
        {
            if (maxRecords == 0U)
            {
                reconstruction = null;
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            IReadOnlyList<CapabilityProvenance> all = epoch.TryGetPair(target, capability, out IReadOnlyList<CapabilityProvenance>? pair) && pair != null
                ? pair
                : Array.Empty<CapabilityProvenance>();

            var page = new List<CapabilityProvenance>();
            int start = offset > (uint)all.Count ? all.Count : (int)offset;
            for (int i = start; i < all.Count && page.Count < (int)maxRecords; i++)
            {
                page.Add(all[i]);
            }

            var evidence = new List<ProvenanceEntry>();
            for (int i = 0; i < page.Count; i++)
            {
                IReadOnlyList<Id128> keys = page[i].EvidenceKeys;
                for (int k = 0; k < keys.Count; k++)
                {
                    if (epoch.TryResolveEntry(keys[k], out ProvenanceEntry? entry) && entry != null)
                    {
                        bool known = false;
                        for (int e = 0; e < evidence.Count; e++)
                        {
                            if (evidence[e].Key.Equals(entry.Key))
                            {
                                known = true;
                                break;
                            }
                        }

                        if (!known)
                        {
                            evidence.Add(entry);
                        }
                    }
                }
            }

            reconstruction = new ProvenanceReconstruction(
                epoch.IsStaged ? ExplanationSource.StagedPlan : ExplanationSource.PublishedComposition,
                token,
                operation,
                target,
                capability,
                page,
                all,
                evidence,
                epoch.StateDispositions,
                offset,
                maxRecords,
                (ulong)all.Count,
                start + page.Count < all.Count,
                ProvenanceDigest.Of(all));
            ReconstructionCount++;
            code = DiagnosticCode.None;
            return true;
        }

        private static ProvenancePublishReport Refused(
            SnapshotToken token, OperationId operation, DiagnosticCode code) =>
            new ProvenancePublishReport(
                ProvenancePublishOutcome.Refused, code, token, operation, 0, 0);

        private static int CompareTargets(TargetId left, TargetId right) => left.Value.CompareTo(right.Value);

        /// <summary>
        /// Canonical record order: kind, then rule, then provider, then output slot, then the producer's stable
        /// record key. It depends on nothing about the producer's enumeration order (P-008).
        /// </summary>
        private static int CompareRecords(CapabilityProvenance left, CapabilityProvenance right)
        {
            int order = ((int)left.Kind).CompareTo((int)right.Kind);
            if (order != 0)
            {
                return order;
            }

            order = left.Rule.Value.CompareTo(right.Rule.Value);
            if (order != 0)
            {
                return order;
            }

            order = left.Provider.Value.CompareTo(right.Provider.Value);
            if (order != 0)
            {
                return order;
            }

            order = left.OutputSlot.CompareTo(right.OutputSlot);
            return order != 0 ? order : left.RecordKey.CompareTo(right.RecordKey);
        }

        public override string ToString() =>
            "ProvenanceStore(epochs=" + EpochCount.ToString(CultureInfo.InvariantCulture)
            + "/" + EpochRetention.ToString(CultureInfo.InvariantCulture)
            + ", staged=" + StagedCount.ToString(CultureInfo.InvariantCulture)
            + ", dropped=" + DroppedEpochCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
