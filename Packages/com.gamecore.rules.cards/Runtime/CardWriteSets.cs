// GameCore.Rules.Cards — the bounded, canonically ordered write set of one settled command (GC-011).
//
// 07 s10.4 makes commit one owner's atomic act: "rechecks table version, reserves output capacity, then writes all
// accepted changes before observation is allowed". The set is therefore (a) bounded — a larger effect rejects
// instead of being truncated — and (b) canonically ordered, so the same logical settlement always commits in the
// same order regardless of how the rules were written or in which order candidates arrived (P-008).
//
// A caller can never fabricate a set: the only constructors are internal to this assembly, `Empty` is the
// canonical empty set, and every rejection returns it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Rules.Cards
{
    /// <summary>An immutable, bounded, canonically ordered set of the writes one command commits.</summary>
    public sealed class CardWriteSet
    {
        /// <summary>The empty set: what every rejected settlement returns, so a rejection writes nothing.</summary>
        public static readonly CardWriteSet Empty =
            new CardWriteSet(Array.Empty<CardWrite>(), CardSetRules.MaxWriteEntries);

        private readonly CardWrite[] _writes;

        private CardWriteSet(CardWrite[] writes, int maxEntries)
        {
            _writes = writes;
            MaxEntries = maxEntries;
        }

        /// <summary>How many writes the set holds; 0 for <see cref="Empty"/>.</summary>
        public int Count => _writes.Length;

        /// <summary>The hard bound on set size; a settlement whose effect exceeds it rejects (07 s10.4).</summary>
        public int MaxEntries { get; }

        /// <summary>
        /// Builds the canonical set from the emitted writes: a copy, insertion-sorted by
        /// (<see cref="CardWriteKind"/>, card, seat, amount). False reports <paramref name="set"/> as
        /// <see cref="Empty"/> because the effect exceeded <paramref name="maxEntries"/>.
        /// </summary>
        internal static bool TryCreate(IReadOnlyList<CardWrite>? writes, int maxEntries, out CardWriteSet set)
        {
            IReadOnlyList<CardWrite> source = writes ?? Array.Empty<CardWrite>();
            int count = source.Count;
            if (count > maxEntries)
            {
                set = Empty;
                return false;
            }

            if (count == 0)
            {
                set = Empty;
                return true;
            }

            CardWrite[] canonical = new CardWrite[count];
            for (int i = 0; i < count; i++)
            {
                canonical[i] = source[i];
            }

            SortCanonical(canonical);
            set = new CardWriteSet(canonical, maxEntries);
            return true;
        }

        /// <summary>The write at one index in canonical order; the index must be inside [0, <see cref="Count"/>).</summary>
        public CardWrite Write(int index)
        {
            if ((uint)index >= (uint)_writes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _writes[index];
        }

        /// <summary>True when any card write of the set names this card, in either direction.</summary>
        public bool ContainsCard(CardId card)
        {
            for (int i = 0; i < _writes.Length; i++)
            {
                CardWrite write = _writes[i];
                if (write.Card.Equals(card))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>One-line deterministic diagnostic form: `writes=N[Kind(seat=,card=,amount=);...]`.</summary>
        public string Describe()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("writes=");
            builder.Append(Count.ToString(CultureInfo.InvariantCulture));
            builder.Append('[');
            for (int i = 0; i < _writes.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(';');
                }

                builder.Append(_writes[i].ToString());
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static void SortCanonical(CardWrite[] writes)
        {
            for (int i = 1; i < writes.Length; i++)
            {
                CardWrite current = writes[i];
                int j = i - 1;
                while (j >= 0 && CompareCanonical(writes[j], current) > 0)
                {
                    writes[j + 1] = writes[j];
                    j--;
                }

                writes[j + 1] = current;
            }
        }

        /// <summary>
        /// The canonical order: kind first (so removals precede additions, a score and the table advance), then
        /// card value ascending, then seat, then amount.
        /// </summary>
        private static int CompareCanonical(CardWrite left, CardWrite right)
        {
            int byKind = ((int)left.Kind).CompareTo((int)right.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            int byCard = left.Card.CompareTo(right.Card);
            if (byCard != 0)
            {
                return byCard;
            }

            int bySeat = left.Seat.CompareTo(right.Seat);
            if (bySeat != 0)
            {
                return bySeat;
            }

            return left.Amount.CompareTo(right.Amount);
        }
    }
}
