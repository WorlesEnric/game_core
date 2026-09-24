// Test-only reference seam for the shared GameCore.Contracts surface (TestOnlyMarker.cs).
// Canonical map-key ordering from docs/game-core/05-contracts-and-data-model.md s6: dictionary entries serialize
// by canonical key order, never by hash-table or enumeration order (P-008). Ordering is deterministic even when
// duplicate keys are present, and duplicate keys are reported so the caller can reject the document instead of
// silently choosing one entry.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Deterministic canonical ordering for serialized map entries.</summary>
    public static class CanonicalMapOrder
    {
        /// <summary>Orders entries by canonical big-endian identity bytes (P-004, P-008).</summary>
        public static IReadOnlyList<KeyValuePair<Id128, TValue>> ByIdKey<TValue>(
            IEnumerable<KeyValuePair<Id128, TValue>> entries,
            out bool hasDuplicateKeys) =>
            Order<Id128, TValue>(entries, CompareId, out hasDuplicateKeys);

        /// <summary>Orders entries by unsigned integer key; a numeric key is already canonical.</summary>
        public static IReadOnlyList<KeyValuePair<ulong, TValue>> ByUnsignedKey<TValue>(
            IEnumerable<KeyValuePair<ulong, TValue>> entries,
            out bool hasDuplicateKeys) =>
            Order<ulong, TValue>(entries, CompareUInt64, out hasDuplicateKeys);

        /// <summary>
        /// Orders entries by ordinal identifier. Comparison is by Unicode code point, which is the same order as
        /// the canonical UTF-8 bytes the serializer writes, so the wire order cannot disagree with the sorted
        /// order on any platform.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, TValue>> ByOrdinalStringKey<TValue>(
            IEnumerable<KeyValuePair<string, TValue>> entries,
            out bool hasDuplicateKeys) =>
            Order<string, TValue>(entries, CompareOrdinalUtf8, out hasDuplicateKeys);

        /// <summary>Canonical identity comparison: high word then low word.</summary>
        public static int CompareId(Id128 left, Id128 right) => left.CompareTo(right);

        public static int CompareUInt64(ulong left, ulong right) => left.CompareTo(right);

        /// <summary>Code-point ordinal comparison, equal to byte order of the canonical UTF-8 encoding.</summary>
        public static int CompareOrdinalUtf8(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            int leftIndex = 0;
            int rightIndex = 0;
            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                int leftCodePoint = NextCodePoint(left, ref leftIndex);
                int rightCodePoint = NextCodePoint(right, ref rightIndex);
                if (leftCodePoint != rightCodePoint)
                {
                    return leftCodePoint < rightCodePoint ? -1 : 1;
                }
            }

            if (leftIndex < left.Length)
            {
                return 1;
            }

            return rightIndex < right.Length ? -1 : 0;
        }

        private static IReadOnlyList<KeyValuePair<TKey, TValue>> Order<TKey, TValue>(
            IEnumerable<KeyValuePair<TKey, TValue>> entries,
            Comparison<TKey> comparison,
            out bool hasDuplicateKeys)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            List<KeyValuePair<TKey, TValue>> list = new List<KeyValuePair<TKey, TValue>>();
            foreach (KeyValuePair<TKey, TValue> entry in entries)
            {
                list.Add(entry);
            }

            if (list.Count == 0)
            {
                hasDuplicateKeys = false;
                return Array.Empty<KeyValuePair<TKey, TValue>>();
            }

            int[] order = new int[list.Count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            // A local flag: an out parameter cannot be assigned inside the comparison lambda.
            bool duplicates = false;
            Array.Sort(order, (left, right) =>
            {
                int byKey = comparison(list[left].Key, list[right].Key);
                if (byKey != 0)
                {
                    return byKey;
                }

                duplicates = true;

                // Original position breaks the tie so duplicate keys never order by sort implementation detail.
                return left.CompareTo(right);
            });

            hasDuplicateKeys = duplicates;

            KeyValuePair<TKey, TValue>[] ordered = new KeyValuePair<TKey, TValue>[list.Count];
            for (int i = 0; i < ordered.Length; i++)
            {
                ordered[i] = list[order[i]];
            }

            return Array.AsReadOnly(ordered);
        }

        private static int NextCodePoint(string text, ref int index)
        {
            char current = text[index];
            index++;
            if (!char.IsHighSurrogate(current) || index >= text.Length || !char.IsLowSurrogate(text[index]))
            {
                return current;
            }

            int codePoint = char.ConvertToUtf32(current, text[index]);
            index++;
            return codePoint;
        }
    }
}
