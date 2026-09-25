// GameCore.Composition.Diagnostics — structured diagnostic payloads (GC-016).
//
// P-052 requires "a stable code, world/operation/plan identity, phase, involved rule/stage/schema ids, provenance
// pointers, counts/budgets, and retry classification" and then says the load-bearing part out loud: "Logging never
// changes precedence or world state." A diagnostic that is only a formatted string cannot satisfy that, because a
// later culture, a reordered key list or a reworded summary would change how two diagnostics order.
//
// This file therefore splits the two roles:
//
//   * `DiagnosticEnvelope` is the payload: typed, immutable, with interns and retrieval keys;
//   * `DiagnosticPrecedence` is the only ordering: a total order over typed fields, defined without reading
//     `Summary`, `CodeText` or anything else that exists to be printed.
//
// `CanonicalHash` is what interning keys are made of, and it deliberately excludes the summary: two diagnostics that
// differ only in wording are the same fact, so they intern to one key and one retrieval row.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition.Diagnostics
{
    /// <summary>
    /// The retrieval key of one structured diagnostic: the first 128 bits of its canonical typed hash. It is a
    /// stable identity for a fact, not for a log line, so a rejection's key can be stored in a plan, an event or a
    /// checkpoint and still resolve to the same payload.
    /// </summary>
    public readonly struct DiagnosticKey : IEquatable<DiagnosticKey>
    {
        public DiagnosticKey(Id128 value)
        {
            Value = value;
        }

        public Id128 Value { get; }

        /// <summary>No diagnostic: a publication or a successful step carries no rejection key.</summary>
        public static DiagnosticKey None => new DiagnosticKey(Id128.Zero);

        public bool IsNone => Value.IsDefault;

        /// <summary>The key of a canonical diagnostic hash (its first 16 bytes, big-endian).</summary>
        public static DiagnosticKey FromCanonical(ContentHash canonical) =>
            new DiagnosticKey(Id128Codec.ReadBigEndian(canonical.ToArray(), 0));

        public bool Equals(DiagnosticKey other) => Value.Equals(other.Value);

        public override bool Equals(object? obj) => obj is DiagnosticKey other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(DiagnosticKey left, DiagnosticKey right) => left.Equals(right);

        public static bool operator !=(DiagnosticKey left, DiagnosticKey right) => !left.Equals(right);

        public override string ToString() => "DiagnosticKey(" + Value.ToString() + ")";
    }

    /// <summary>
    /// One structured diagnostic payload. Every field is typed evidence copied from the <see cref="Diagnostic"/>
    /// that produced it, plus the interned retrieval key. Nothing here is mutable and nothing here is derived from
    /// a formatted string.
    /// </summary>
    public sealed class DiagnosticEnvelope
    {
        public DiagnosticEnvelope(
            DiagnosticKey key,
            DiagnosticCode code,
            OperationPhase phase,
            OperationId operation,
            ContentHash planHash,
            IReadOnlyList<Id128>? involvedIds,
            IReadOnlyList<FactoryKey>? involvedKeys,
            long count,
            long budgetLimit,
            RetryClassification retry,
            string summary)
        {
            Key = key;
            Code = code;
            Phase = phase;
            Operation = operation;
            PlanHash = planHash ?? ContentHash.Empty;
            InvolvedIds = ContractCollections.Freeze(involvedIds);
            InvolvedKeys = ContractCollections.Freeze(involvedKeys);
            Count = count;
            BudgetLimit = budgetLimit;
            Retry = retry;
            Summary = summary ?? string.Empty;
            CodeText = DiagnosticCodeText.Of(code);
        }

        /// <summary>Retrieval key: the first 128 bits of <see cref="CanonicalHash"/>, ignoring the summary.</summary>
        public DiagnosticKey Key { get; }

        public DiagnosticCode Code { get; }

        /// <summary>The normative literal of <see cref="Code"/>, for human diagnostics only.</summary>
        public string CodeText { get; }

        public OperationPhase Phase { get; }

        public OperationId Operation { get; }

        public ContentHash PlanHash { get; }

        /// <summary>Involved rule/stage/schema/content ids: the smallest conflicting set the producer knows.</summary>
        public IReadOnlyList<Id128> InvolvedIds { get; }

        public IReadOnlyList<FactoryKey> InvolvedKeys { get; }

        /// <summary>Observed count (affected entities, budget used, conflicting candidates).</summary>
        public long Count { get; }

        /// <summary>Declared budget when <see cref="Count"/> is a usage; zero when there is none.</summary>
        public long BudgetLimit { get; }

        public RetryClassification Retry { get; }

        /// <summary>Free text for a human reader. Never an input to ordering or identity (P-052).</summary>
        public string Summary { get; }

        /// <summary>Wraps one contract diagnostic as a payload, stamped with its retrieval key.</summary>
        public static DiagnosticEnvelope From(Diagnostic diagnostic, DiagnosticKey key)
        {
            if (diagnostic == null)
            {
                throw new ArgumentNullException(nameof(diagnostic));
            }

            return new DiagnosticEnvelope(
                key,
                diagnostic.Code,
                diagnostic.Phase,
                diagnostic.Operation,
                diagnostic.PlanHash,
                diagnostic.InvolvedIds,
                diagnostic.InvolvedKeys,
                diagnostic.Count,
                diagnostic.BudgetLimit,
                diagnostic.Retry,
                diagnostic.Summary);
        }

        /// <summary>
        /// Canonical hash over the typed fields only: code, phase, operation, plan hash, involved ids and keys,
        /// counts, budget and retry class. The summary is excluded on purpose, so rewording a diagnostic can never
        /// change its identity or its precedence (P-052).
        /// </summary>
        public ContentHash CanonicalHash()
        {
            var bytes = new List<byte>(128);
            DiagnosticDocument.AppendUInt32(bytes, (uint)Code);
            DiagnosticDocument.AppendUInt32(bytes, (uint)Phase);
            DiagnosticDocument.AppendUInt32(bytes, (uint)Retry);
            DiagnosticDocument.AppendId128(bytes, Operation.World.Session);
            DiagnosticDocument.AppendId128(bytes, Operation.IssuerId);
            DiagnosticDocument.AppendUInt64(bytes, Operation.IssuerSequence);
            DiagnosticDocument.AppendHash(bytes, PlanHash);
            DiagnosticDocument.AppendUInt32(bytes, (uint)InvolvedIds.Count);
            for (int i = 0; i < InvolvedIds.Count; i++)
            {
                DiagnosticDocument.AppendId128(bytes, InvolvedIds[i]);
            }

            DiagnosticDocument.AppendUInt32(bytes, (uint)InvolvedKeys.Count);
            for (int i = 0; i < InvolvedKeys.Count; i++)
            {
                DiagnosticDocument.AppendId128(bytes, InvolvedKeys[i].RegistrationKey);
                DiagnosticDocument.AppendUInt32(bytes, InvolvedKeys[i].KeyVersion);
            }

            DiagnosticDocument.AppendInt64(bytes, Count);
            DiagnosticDocument.AppendInt64(bytes, BudgetLimit);
            return ContentHash.Compute(bytes.ToArray());
        }

        /// <summary>The log line for a human. It is never read back: ordering uses <see cref="DiagnosticPrecedence"/>.</summary>
        public string Format() =>
            "[" + Phase.ToString() + "/" + CodeText + "] "
            + Operation.ToString()
            + " plan=" + (PlanHash.IsEmpty ? "none" : PlanHash.ToHex())
            + " count=" + Count.ToString(CultureInfo.InvariantCulture)
            + (BudgetLimit == 0L ? string.Empty : "/" + BudgetLimit.ToString(CultureInfo.InvariantCulture))
            + " retry=" + Retry.ToString()
            + " ids=" + InvolvedIds.Count.ToString(CultureInfo.InvariantCulture)
            + " keys=" + InvolvedKeys.Count.ToString(CultureInfo.InvariantCulture)
            + (Summary.Length == 0 ? string.Empty : " " + Summary);

        public override string ToString() =>
            "Diagnostic(" + CodeText + ", " + Phase.ToString() + ", " + Key.ToString() + ")";
    }

    /// <summary>
    /// The one total order over structured diagnostics. It reads typed fields only, so no wording, culture or
    /// formatting decision can move a diagnostic in the order (P-052: "Logging never changes precedence").
    /// </summary>
    public static class DiagnosticPrecedence
    {
        /// <summary>
        /// Total order: phase, then code, then the operation identity, then the plan hash, then the involved ids and
        /// keys, then counts and retry class. Negative counts are impossible; the ordering is still total, because
        /// every compared field is fully ordered and the trailing budget comparison closes the tie.
        /// </summary>
        public static int Compare(DiagnosticEnvelope left, DiagnosticEnvelope right)
        {
            if (left == null)
            {
                throw new ArgumentNullException(nameof(left));
            }

            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            int order = ((int)left.Phase).CompareTo((int)right.Phase);
            if (order != 0)
            {
                return order;
            }

            order = ((int)left.Code).CompareTo((int)right.Code);
            if (order != 0)
            {
                return order;
            }

            order = CompareOperations(left.Operation, right.Operation);
            if (order != 0)
            {
                return order;
            }

            order = DiagnosticDocument.CompareHashes(left.PlanHash, right.PlanHash);
            if (order != 0)
            {
                return order;
            }

            order = CompareIdLists(left.InvolvedIds, right.InvolvedIds);
            if (order != 0)
            {
                return order;
            }

            order = CompareKeyLists(left.InvolvedKeys, right.InvolvedKeys);
            if (order != 0)
            {
                return order;
            }

            order = left.Count.CompareTo(right.Count);
            if (order != 0)
            {
                return order;
            }

            order = left.BudgetLimit.CompareTo(right.BudgetLimit);
            return order != 0 ? order : ((int)left.Retry).CompareTo((int)right.Retry);
        }

        /// <summary>True when two diagnostics are indistinguishable to precedence, i.e. the same ordered position.</summary>
        public static bool SamePosition(DiagnosticEnvelope left, DiagnosticEnvelope right) => Compare(left, right) == 0;

        /// <summary>
        /// A canonically ordered copy of one diagnostic list. The input order is never preserved on a tie, because
        /// the comparison is total; the result is therefore identical for any permutation of the same diagnostics.
        /// </summary>
        public static IReadOnlyList<DiagnosticEnvelope> Order(IReadOnlyList<DiagnosticEnvelope>? diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                return Array.Empty<DiagnosticEnvelope>();
            }

            var ordered = new List<DiagnosticEnvelope>(diagnostics);
            ordered.Sort(Compare);
            return ordered;
        }

        private static int CompareOperations(OperationId left, OperationId right)
        {
            int order = left.World.Session.CompareTo(right.World.Session);
            if (order != 0)
            {
                return order;
            }

            order = left.IssuerId.CompareTo(right.IssuerId);
            return order != 0 ? order : left.IssuerSequence.CompareTo(right.IssuerSequence);
        }

        private static int CompareIdLists(IReadOnlyList<Id128> left, IReadOnlyList<Id128> right)
        {
            int shared = left.Count < right.Count ? left.Count : right.Count;
            for (int i = 0; i < shared; i++)
            {
                int order = left[i].CompareTo(right[i]);
                if (order != 0)
                {
                    return order;
                }
            }

            return left.Count.CompareTo(right.Count);
        }

        private static int CompareKeyLists(IReadOnlyList<FactoryKey> left, IReadOnlyList<FactoryKey> right)
        {
            int shared = left.Count < right.Count ? left.Count : right.Count;
            for (int i = 0; i < shared; i++)
            {
                int order = left[i].RegistrationKey.CompareTo(right[i].RegistrationKey);
                if (order != 0)
                {
                    return order;
                }

                order = left[i].KeyVersion.CompareTo(right[i].KeyVersion);
                if (order != 0)
                {
                    return order;
                }
            }

            return left.Count.CompareTo(right.Count);
        }
    }

    /// <summary>Canonical big-endian field encoding used by diagnostic and provenance hashing (05 s6).</summary>
    internal static class DiagnosticDocument
    {
        public static void AppendUInt32(List<byte> destination, uint value)
        {
            destination.Add((byte)(value >> 24));
            destination.Add((byte)(value >> 16));
            destination.Add((byte)(value >> 8));
            destination.Add((byte)value);
        }

        public static void AppendUInt64(List<byte> destination, ulong value)
        {
            for (int shift = 56; shift >= 0; shift -= 8)
            {
                destination.Add((byte)(value >> shift));
            }
        }

        public static void AppendInt64(List<byte> destination, long value) =>
            AppendUInt64(destination, unchecked((ulong)value));

        public static void AppendId128(List<byte> destination, Id128 value)
        {
            AppendUInt64(destination, value.High);
            AppendUInt64(destination, value.Low);
        }

        public static void AppendHash(List<byte> destination, ContentHash hash)
        {
            byte[] bytes = hash.ToArray();
            for (int i = 0; i < bytes.Length; i++)
            {
                destination.Add(bytes[i]);
            }
        }

        /// <summary>Byte-wise comparison of two canonical hashes; never a textual comparison.</summary>
        public static int CompareHashes(ContentHash left, ContentHash right)
        {
            byte[] a = left.ToArray();
            byte[] b = right.ToArray();
            int shared = a.Length < b.Length ? a.Length : b.Length;
            for (int i = 0; i < shared; i++)
            {
                if (a[i] != b[i])
                {
                    return a[i] < b[i] ? -1 : 1;
                }
            }

            return a.Length.CompareTo(b.Length);
        }
    }
}
