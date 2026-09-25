// GameCore.Planning — canonical hashing of planning inputs (GC-008).
//
// Normative sources: 00 P-008 (comparisons use canonical big-endian stable-ID bytes, ordinal identifiers and
// explicit numeric keys; registration timing, dictionary enumeration and worker index never decide precedence),
// P-028 and 05 s4 (plan hashing uses semantic inputs, canonical ids, revisions and generated handler keys,
// excluding timestamps and object addresses; a plan hash is content, never a pointer or a clock).
//
// Every hash in this assembly is a SHA-256 over canonical UTF-8 text built from stable ids, versions and sorted
// collections. The text is deliberately inspectable: a failing hash comparison can be diffed, which is what makes
// "the same snapshot and input give the same hash" (O-09) a checkable statement rather than a promise.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>Canonical text building and hashing shared by the descriptor, the target binding table and the planner.</summary>
    public static class PlanHashing
    {
        /// <summary>Stable hexadecimal form of a 128-bit identity, never a platform-formatted value (P-054).</summary>
        public static string IdText(Id128 value) => Id128Codec.ToHex(value);

        /// <summary>Stable hexadecimal form of a content hash.</summary>
        public static string HashText(ContentHash value) => value.ToHex();

        /// <summary>Ordinal integer text; every numeric field of a canonical hash uses this form.</summary>
        public static string NumberText(long value) => value.ToString(CultureInfo.InvariantCulture);

        public static string UnsignedText(ulong value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Hashes canonical text into a content hash; the only hashing entry point of this assembly.</summary>
        public static ContentHash Of(string canonicalText)
        {
            if (canonicalText == null)
            {
                throw new ArgumentNullException(nameof(canonicalText));
            }

            return ContentHash.Compute(Encoding.UTF8.GetBytes(canonicalText));
        }

        /// <summary>Joins canonical parts with a separator that cannot appear inside an id text.</summary>
        public static string Join(string separator, IReadOnlyList<string> parts)
        {
            if (parts == null)
            {
                throw new ArgumentNullException(nameof(parts));
            }

            var builder = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(separator);
                }

                builder.Append(parts[i]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Sorts identities into canonical big-endian order (P-008). The comparer is the contract's own comparer, so
        /// planning can never disagree with the protocol about what "canonical order" means.
        /// </summary>
        public static void SortCanonical(List<Id128> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            values.Sort(Id128Codec.CanonicalComparer);
        }

        /// <summary>Sorted copy of the given identities in canonical big-endian order (P-008).</summary>
        public static IReadOnlyList<Id128> CanonicalCopy(IReadOnlyList<Id128>? values)
        {
            var copy = new List<Id128>(values != null ? values.Count : 0);
            if (values != null)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    copy.Add(values[i]);
                }
            }

            SortCanonical(copy);
            return copy;
        }
    }
}
