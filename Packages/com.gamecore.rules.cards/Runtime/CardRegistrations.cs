// GameCore.Rules.Cards — the reducer and predicate registration seam of the card market (GC-011).
//
// A generated catalog holds one keyed instance per registration and looks it up by FactoryKey; nothing is
// discovered by reflection and an unregistered key stays a miss (P-009, 05 s3). The instances therefore carry
// their own generated key and their stable name, and delegate to the pure rules functions so the registered path
// and the direct path cannot drift (P-028).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Rules.Cards
{
    /// <summary>The Int32 reducer seam a generated catalog binds for the `cards.set-bonus` slot (P-019).</summary>
    public interface ICardBonusReducer
    {
        /// <summary>The generated registration key this reducer is bound under.</summary>
        FactoryKey Key { get; }

        /// <summary>The registration's stable name, for diagnostics and fingerprints.</summary>
        string StableName { get; }

        /// <summary>
        /// Folds the contributions into one effective bonus. False reports a bounded failure (an overflow) with a
        /// non-empty <paramref name="failure"/> instead of a wrapped value.
        /// </summary>
        bool TryReduce(IReadOnlyList<int>? contributions, out int effectiveBonus, out string failure);
    }

    /// <summary>The registered `cards.reducer.int32-sum`: the arithmetic of <see cref="CardSetRules.TryReduceBonus"/>.</summary>
    public sealed class CardSetBonusReducer : ICardBonusReducer
    {
        /// <summary>Binds the reducer to its generated registration key.</summary>
        public CardSetBonusReducer(FactoryKey key)
        {
            Key = key;
        }

        /// <summary>The generated registration key the catalog holds this instance under.</summary>
        public FactoryKey Key { get; }

        /// <summary>Always <see cref="CardVocabulary.BonusReducer"/>; the fixture registers one Int32 sum (07 s2.1).</summary>
        public string StableName => CardVocabulary.BonusReducer;

        /// <summary>
        /// Delegates to <see cref="CardSetRules.TryReduceBonus"/>: ascending-index fold, overflow-checked.
        /// <paramref name="failure"/> is empty on success.
        /// </summary>
        public bool TryReduce(IReadOnlyList<int>? contributions, out int effectiveBonus, out string failure)
        {
            if (CardSetRules.TryReduceBonus(contributions, out effectiveBonus))
            {
                failure = string.Empty;
                return true;
            }

            failure = CardVocabulary.BonusReducer + ": the int32 fold overflowed";
            return false;
        }
    }

    /// <summary>The target-selector predicate seam a generated catalog binds for every card rule (P-009).</summary>
    public interface ICardTargetPredicate
    {
        /// <summary>The generated registration key this predicate is bound under.</summary>
        FactoryKey Key { get; }

        /// <summary>The registration's stable name, for diagnostics and fingerprints.</summary>
        string StableName { get; }

        /// <summary>True when the target's tags satisfy this predicate.</summary>
        bool IsMatch(IReadOnlyList<string>? targetTags);
    }

    /// <summary>The registered `cards.predicate.always`: the always-accepting predicate of the card fixture.</summary>
    public sealed class CardAlwaysPredicate : ICardTargetPredicate
    {
        /// <summary>Binds the predicate to its generated registration key.</summary>
        public CardAlwaysPredicate(FactoryKey key)
        {
            Key = key;
        }

        /// <summary>The generated registration key the catalog holds this instance under.</summary>
        public FactoryKey Key { get; }

        /// <summary>Always <see cref="CardVocabulary.AlwaysPredicate"/>.</summary>
        public string StableName => CardVocabulary.AlwaysPredicate;

        /// <summary>True for every target; a null tag list is accepted too, so no seat is skipped by accident.</summary>
        public bool IsMatch(IReadOnlyList<string>? targetTags) => true;
    }
}
