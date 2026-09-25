// GameCore.Composition.Diagnostics — the bounded diagnostic registry and the composition diagnostic feed (GC-016).
//
// P-052 requires a rejection to "explain the smallest known conflicting set or a bounded summary with retrieval
// key", and P-026 requires `CompositionPublished`/`CompositionRejected` to carry "operation ID, old/new revision,
// plan hash, counts, and diagnostic keys". A key has to resolve somewhere bounded, so this file owns:
//
//   * `DiagnosticRegistry`: interned, bounded payload storage. Structurally identical diagnostics share one key and
//     one row, so recording the same rejection 10,000 times costs one row; the registry keeps a bounded number of
//     rows and counts what it dropped (TEST-023).
//   * `CompositionDiagnosticFeed`: the observer that turns the published/rejected composition events into those
//     records under one retrieval key, preserving the identity, revision/epoch pair, plan hash, counts and codes.
//
// Neither type decides anything about a publication: recording is observation, never publication (P-029, P-051).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition.Diagnostics
{
    /// <summary>
    /// Bounded, interned storage of structured diagnostic payloads, indexed by retrieval key. Registering the same
    /// fact twice returns the same key and reuses the stored payload, so a caller can both report a diagnostic and
    /// keep it retrievable later without duplicating it.
    /// </summary>
    public sealed class DiagnosticRegistry
    {
        private readonly Dictionary<Id128, DiagnosticEnvelope> byKey = new Dictionary<Id128, DiagnosticEnvelope>();
        private readonly Queue<Id128> registrationOrder = new Queue<Id128>();

        public DiagnosticRegistry(int retention)
        {
            if (retention <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retention), "A diagnostic registry is bounded and positive.");
            }

            Retention = retention;
        }

        /// <summary>Distinct diagnostic facts retained before the oldest is dropped (counted, never silent).</summary>
        public int Retention { get; }

        /// <summary>Distinct facts retained right now.</summary>
        public int Count => byKey.Count;

        /// <summary>Registration calls served.</summary>
        public int RegisteredCount { get; private set; }

        /// <summary>Registrations that resolved to an already retained fact.</summary>
        public int InternedCount { get; private set; }

        /// <summary>Rows dropped because retention is bounded.</summary>
        public int DroppedCount { get; private set; }

        /// <summary>Registers one payload and returns its retrieval key.</summary>
        public DiagnosticKey Register(DiagnosticEnvelope envelope)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            RegisteredCount++;
            if (byKey.ContainsKey(envelope.Key.Value))
            {
                InternedCount++;
                return envelope.Key;
            }

            byKey.Add(envelope.Key.Value, envelope);
            registrationOrder.Enqueue(envelope.Key.Value);
            while (byKey.Count > Retention)
            {
                byKey.Remove(registrationOrder.Dequeue());
                DroppedCount++;
            }

            return envelope.Key;
        }

        /// <summary>Wraps one contract diagnostic, keys it by its typed hash and registers it.</summary>
        public DiagnosticKey Register(Diagnostic diagnostic)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            DiagnosticKey key = DiagnosticKey.FromCanonical(
                DiagnosticEnvelope.From(diagnostic, DiagnosticKey.None).CanonicalHash());
            return Register(DiagnosticEnvelope.From(diagnostic, key));
        }

        /// <summary>True when the key still resolves; a dropped key reports false instead of a stale payload.</summary>
        public bool TryGet(DiagnosticKey key, out DiagnosticEnvelope? envelope) =>
            byKey.TryGetValue(key.Value, out envelope);

        /// <summary>Every retained payload in the order it was first registered.</summary>
        public IReadOnlyList<DiagnosticEnvelope> Entries()
        {
            var entries = new List<DiagnosticEnvelope>(registrationOrder.Count);
            foreach (Id128 key in registrationOrder)
            {
                if (byKey.TryGetValue(key, out DiagnosticEnvelope? envelope) && envelope != null)
                {
                    entries.Add(envelope);
                }
            }

            return entries;
        }

        /// <summary>The canonically ordered retained payloads, which is what a report prints (P-052).</summary>
        public IReadOnlyList<DiagnosticEnvelope> OrderedEntries() => DiagnosticPrecedence.Order(Entries());

        public override string ToString() =>
            "DiagnosticRegistry(" + Count.ToString(CultureInfo.InvariantCulture)
            + "/" + Retention.ToString(CultureInfo.InvariantCulture)
            + ", interned=" + InternedCount.ToString(CultureInfo.InvariantCulture)
            + ", dropped=" + DroppedCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Structured record of one successful composition publication (P-026): the operation, the old/new revision and
    /// epoch pair, the plan hash, the affected counts and the retrieval key of the publication's own diagnostics.
    /// </summary>
    public sealed class PublicationDiagnostic
    {
        public PublicationDiagnostic(
            DiagnosticKey key,
            OperationId operation,
            CompositionRevision oldRevision,
            CompositionRevision newRevision,
            AssemblyEpoch oldEpoch,
            AssemblyEpoch newEpoch,
            ContentHash planHash,
            AffectedCounts counts,
            IReadOnlyList<DiagnosticKey>? diagnosticKeys)
        {
            Key = key;
            Operation = operation;
            OldRevision = oldRevision;
            NewRevision = newRevision;
            OldEpoch = oldEpoch;
            NewEpoch = newEpoch;
            PlanHash = planHash;
            Counts = counts ?? new AffectedCounts(0, 0, 0, 0, 0);
            DiagnosticKeys = ContractCollections.Freeze(diagnosticKeys);
        }

        /// <summary>Retrieval key of this publication record; never <see cref="DiagnosticKey.None"/>.</summary>
        public DiagnosticKey Key { get; }

        public OperationId Operation { get; }

        public CompositionRevision OldRevision { get; }

        public CompositionRevision NewRevision { get; }

        public AssemblyEpoch OldEpoch { get; }

        public AssemblyEpoch NewEpoch { get; }

        public ContentHash PlanHash { get; }

        public AffectedCounts Counts { get; }

        /// <summary>Keys of the step/publish diagnostics recorded with this publication, in canonical order.</summary>
        public IReadOnlyList<DiagnosticKey> DiagnosticKeys { get; }

        public override string ToString() =>
            "Published(" + Operation.ToString() + ", " + OldRevision.ToString() + "->" + NewRevision.ToString() + ")";
    }

    /// <summary>
    /// Structured record of one rejected composition proposal (P-026): the operation, the plan hash, the stable code
    /// of the smallest known conflicting set and the retrieval keys of its diagnostics, so an operator can resolve
    /// the same facts the lane used instead of re-reading a log line.
    /// </summary>
    public sealed class RejectionDiagnostic
    {
        public RejectionDiagnostic(
            DiagnosticKey key,
            OperationId operation,
            ContentHash planHash,
            DiagnosticCode code,
            IReadOnlyList<DiagnosticKey>? diagnosticKeys,
            IReadOnlyList<DiagnosticCode>? diagnosticCodes)
        {
            Key = key;
            Operation = operation;
            PlanHash = planHash;
            Code = code;
            DiagnosticKeys = ContractCollections.Freeze(diagnosticKeys);
            DiagnosticCodes = ContractCollections.Freeze(diagnosticCodes);
        }

        public DiagnosticKey Key { get; }

        public OperationId Operation { get; }

        public ContentHash PlanHash { get; }

        /// <summary>The rejection's own stable code, or `None` when only the plan carried the refusal.</summary>
        public DiagnosticCode Code { get; }

        public string CodeText => DiagnosticCodeText.Of(Code);

        public IReadOnlyList<DiagnosticKey> DiagnosticKeys { get; }

        public IReadOnlyList<DiagnosticCode> DiagnosticCodes { get; }

        public override string ToString() =>
            "Rejected(" + Operation.ToString() + ", " + CodeText + ")";
    }

    /// <summary>
    /// Observer of composition publication and rejection events that records their structured payloads. It is the
    /// production consumer of `ICompositionObserver` (P-029, P-045): registering a payload never changes precedence,
    /// never mutates world state and never refuses an event.
    /// </summary>
    public sealed class CompositionDiagnosticFeed : ICompositionObserver
    {
        private readonly DiagnosticRegistry registry;
        private readonly Dictionary<Id128, PublicationDiagnostic> publications =
            new Dictionary<Id128, PublicationDiagnostic>();
        private readonly Dictionary<Id128, RejectionDiagnostic> rejections =
            new Dictionary<Id128, RejectionDiagnostic>();
        private readonly Queue<Id128> recordOrder = new Queue<Id128>();

        public CompositionDiagnosticFeed(DiagnosticRegistry registry, int retention)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            if (retention <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retention), "A composition feed is bounded and positive.");
            }

            Retention = retention;
        }

        public DiagnosticRegistry Registry => registry;

        /// <summary>Records retained before the oldest publication/rejection record is dropped.</summary>
        public int Retention { get; }

        public int PublicationCount { get; private set; }

        public int RejectionCount { get; private set; }

        public int DroppedRecordCount { get; private set; }

        public int RecordCount => publications.Count + rejections.Count;

        /// <summary>Records one publication and returns its structured record.</summary>
        public PublicationDiagnostic RecordPublished(CompositionPublishedEvent published)
        {
            if (published == null)
            {
                throw new ArgumentNullException(nameof(published));
            }

            DiagnosticKey key = DiagnosticKey.FromCanonical(PublicationHash(published));
            var record = new PublicationDiagnostic(
                key,
                published.Operation,
                published.OldRevision,
                published.NewRevision,
                published.OldEpoch,
                published.NewEpoch,
                published.PlanHash,
                published.Counts,
                null);

            PublicationCount++;
            Remember(key.Value, record, null);
            return record;
        }

        /// <summary>Records one rejection, interns every diagnostic it carried and returns its structured record.</summary>
        public RejectionDiagnostic RecordRejected(CompositionRejectedEvent rejected)
        {
            if (rejected == null)
            {
                throw new ArgumentNullException(nameof(rejected));
            }

            var keys = new List<DiagnosticKey>(rejected.Diagnostics.Count);
            var codes = new List<DiagnosticCode>(rejected.Diagnostics.Count);
            for (int i = 0; i < rejected.Diagnostics.Count; i++)
            {
                Diagnostic diagnostic = rejected.Diagnostics[i];
                keys.Add(registry.Register(diagnostic));
                codes.Add(diagnostic.Code);
            }

            DiagnosticCode code = codes.Count > 0 ? codes[0] : DiagnosticCode.None;
            DiagnosticKey key = DiagnosticKey.FromCanonical(RejectionHash(rejected, code));
            var record = new RejectionDiagnostic(
                key, rejected.Operation, rejected.PlanHash, code, keys, codes);

            RejectionCount++;
            Remember(key.Value, null, record);
            return record;
        }

        public void OnCompositionPublished(CompositionPublishedEvent published) => RecordPublished(published);

        public void OnCompositionRejected(CompositionRejectedEvent rejected) => RecordRejected(rejected);

        public bool TryGetPublication(DiagnosticKey key, out PublicationDiagnostic? publication) =>
            publications.TryGetValue(key.Value, out publication);

        public bool TryGetRejection(DiagnosticKey key, out RejectionDiagnostic? rejection) =>
            rejections.TryGetValue(key.Value, out rejection);

        /// <summary>Publication records in the order they were observed.</summary>
        public IReadOnlyList<PublicationDiagnostic> Publications()
        {
            var records = new List<PublicationDiagnostic>(publications.Count);
            foreach (Id128 key in recordOrder)
            {
                if (publications.TryGetValue(key, out PublicationDiagnostic? record) && record != null)
                {
                    records.Add(record);
                }
            }

            return records;
        }

        /// <summary>Rejection records in the order they were observed.</summary>
        public IReadOnlyList<RejectionDiagnostic> Rejections()
        {
            var records = new List<RejectionDiagnostic>(rejections.Count);
            foreach (Id128 key in recordOrder)
            {
                if (rejections.TryGetValue(key, out RejectionDiagnostic? record) && record != null)
                {
                    records.Add(record);
                }
            }

            return records;
        }

        /// <summary>Canonical hash of one publication record's typed fields: the record's own identity.</summary>
        private static ContentHash PublicationHash(CompositionPublishedEvent published)
        {
            var bytes = new List<byte>(160);
            DiagnosticDocument.AppendId128(bytes, published.Operation.World.Session);
            DiagnosticDocument.AppendId128(bytes, published.Operation.IssuerId);
            DiagnosticDocument.AppendUInt64(bytes, published.Operation.IssuerSequence);
            DiagnosticDocument.AppendUInt64(bytes, published.OldRevision.Value);
            DiagnosticDocument.AppendUInt64(bytes, published.NewRevision.Value);
            DiagnosticDocument.AppendUInt64(bytes, published.OldEpoch.Value);
            DiagnosticDocument.AppendUInt64(bytes, published.NewEpoch.Value);
            DiagnosticDocument.AppendHash(bytes, published.PlanHash);
            DiagnosticDocument.AppendUInt32(bytes, (uint)published.Counts.Targets);
            DiagnosticDocument.AppendUInt32(bytes, (uint)published.Counts.Installs);
            DiagnosticDocument.AppendUInt32(bytes, (uint)published.Counts.ContributionsAdded);
            DiagnosticDocument.AppendUInt32(bytes, (uint)published.Counts.ContributionsRetracted);
            DiagnosticDocument.AppendUInt32(bytes, (uint)published.Counts.Stages);
            return ContentHash.Compute(bytes.ToArray());
        }

        /// <summary>Canonical hash of one rejection record: identity, plan hash and the rejection's own code.</summary>
        private static ContentHash RejectionHash(CompositionRejectedEvent rejected, DiagnosticCode code)
        {
            var bytes = new List<byte>(96);
            DiagnosticDocument.AppendId128(bytes, rejected.Operation.World.Session);
            DiagnosticDocument.AppendId128(bytes, rejected.Operation.IssuerId);
            DiagnosticDocument.AppendUInt64(bytes, rejected.Operation.IssuerSequence);
            DiagnosticDocument.AppendHash(bytes, rejected.PlanHash);
            DiagnosticDocument.AppendUInt32(bytes, (uint)code);
            DiagnosticDocument.AppendUInt32(bytes, (uint)rejected.Diagnostics.Count);
            return ContentHash.Compute(bytes.ToArray());
        }

        private void Remember(Id128 key, PublicationDiagnostic? publication, RejectionDiagnostic? rejection)
        {
            if (publication != null && !publications.ContainsKey(key))
            {
                publications.Add(key, publication);
                recordOrder.Enqueue(key);
            }
            else if (rejection != null && !rejections.ContainsKey(key))
            {
                rejections.Add(key, rejection);
                recordOrder.Enqueue(key);
            }

            while (recordOrder.Count > Retention)
            {
                Id128 oldest = recordOrder.Dequeue();
                if (publications.Remove(oldest) || rejections.Remove(oldest))
                {
                    DroppedRecordCount++;
                }
            }
        }

        public override string ToString() =>
            "CompositionDiagnosticFeed(publications=" + PublicationCount.ToString(CultureInfo.InvariantCulture)
            + ", rejections=" + RejectionCount.ToString(CultureInfo.InvariantCulture)
            + ", records=" + RecordCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
