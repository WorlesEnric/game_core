// GameCore.Gameplay.Traversal — the traversal catalog's registered value source (GC-020).
//
// Normative sources: 04 s2's assembly table — `GameCore.Rules.<Name>` holds "game-specific value schemas and reusable
// pure rules" over `GameCore.Contracts` and small value types only, while `GameCore.Gameplay.<Name>` holds the domain
// schemas, declarations, Unity systems and stage/owner policies — plus P-009/P-028 (a generated catalog holds one
// keyed instance per registration, and an unregistered key stays a miss rather than a silent identity) and P-019 (a
// reducer is pure, bounded, versioned and supplied by the contract's own package).
//
// WHY THIS LIVES HERE AND NOT IN THE RULES PACKAGE. `IDerivationValueSource` is a derivation seam, and the derivation
// package is not a member of the rules layer's allowed references (04 s2). The traversal rules package therefore ships
// only the pure rules and the two registered seams — `TraversalAccelerationReducer` and `TraversalAlwaysPredicate` —
// and this file binds those instances to the derivation engine's interface. It is the same split the card package
// makes (`CardDerivationValueSource` lives in `GameCore.Gameplay.Cards`, while `CardSetBonusReducer` lives in
// `GameCore.Rules.Cards`), so the registered path and the direct path cannot drift (P-028).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Rules.Traversal;

namespace GameCore.Gameplay.Traversal
{
    /// <summary>
    /// The traversal catalog's registered reducer and static predicate: the acceleration int32 fold and the
    /// always-accepting target predicate of 07 s4.1. Both delegate to the rules package, so the registered path and
    /// the direct path cannot drift (P-019, P-028).
    /// </summary>
    public sealed class TraversalDerivationValueSource : IDerivationValueSource
    {
        private readonly TraversalAccelerationReducer reducer;
        private readonly TraversalAlwaysPredicate predicate;

        /// <summary>Builds the source over the two generated traversal registrations.</summary>
        public TraversalDerivationValueSource(TraversalAccelerationReducer reducer, TraversalAlwaysPredicate predicate)
        {
            this.reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
            this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <summary>
        /// The source over the traversal catalog's own keys: `TraversalVocabulary.AccelerationReducerKey` and
        /// `TraversalVocabulary.AlwaysPredicateKey`. A derivation that resolves any other key reports a miss, which
        /// validation turns into a catalog error rather than a silent identity (P-028).
        /// </summary>
        public static TraversalDerivationValueSource Default() =>
            new TraversalDerivationValueSource(
                new TraversalAccelerationReducer(TraversalVocabulary.AccelerationReducerKey),
                new TraversalAlwaysPredicate(TraversalVocabulary.AlwaysPredicateKey));

        /// <summary>The reducer instance this source resolves, so a scenario can assert the registered path ran.</summary>
        public TraversalAccelerationReducer Reducer => reducer;

        /// <summary>The predicate instance this source resolves.</summary>
        public TraversalAlwaysPredicate Predicate => predicate;

        /// <summary>Reductions this source resolved, so a scenario can assert the registered path ran.</summary>
        public int ReductionCount { get; private set; }

        /// <summary>Predicate evaluations this source resolved.</summary>
        public int PredicateCount { get; private set; }

        /// <inheritdoc />
        public bool IsReductionRegistered(FactoryKey reducerKey) => reducerKey.Equals(reducer.Key);

        /// <inheritdoc />
        public bool IsPredicateRegistered(FactoryKey predicateKey) => predicateKey.Equals(predicate.Key);

        /// <inheritdoc />
        public bool TryReduce(FactoryKey reducerKey, IReadOnlyList<FrozenPayload> inputs, out FrozenPayload? result)
        {
            result = null;
            if (!reducerKey.Equals(reducer.Key))
            {
                return false;
            }

            // The Additive fold runs over the canonical contribution order the engine supplies, which is the
            // precedence order of P-018; this method never re-sorts what it was given.
            var values = new List<int>(inputs.Count);
            for (int i = 0; i < inputs.Count; i++)
            {
                if (!TraversalPayloadCodec.TryReadInt32(inputs[i].Bytes, out int value))
                {
                    throw new ReducerFailureException(
                        reducerKey,
                        "contribution " + i.ToString(CultureInfo.InvariantCulture)
                        + " is not one canonical int32 scalar (05 s6)");
                }

                values.Add(value);
            }

            if (!reducer.TryReduce(values, out int effective, out string failure))
            {
                throw new ReducerFailureException(reducerKey, failure);
            }

            ReductionCount++;
            result = TraversalPayloadCodec.WriteAcceleration(effective);
            return true;
        }

        /// <inheritdoc />
        public bool TryEvaluate(FactoryKey predicateKey, DerivationPredicateContext context, out bool result)
        {
            result = false;
            if (!predicateKey.Equals(predicate.Key))
            {
                return false;
            }

            // Eligibility reads declared descriptors only: a target's tags are immutable assembly data, never live
            // mutable ECS state or wall time (P-015).
            var tags = new List<string>(context.Tags.Count);
            for (int i = 0; i < context.Tags.Count; i++)
            {
                tags.Add(context.Tags[i].ToString());
            }

            PredicateCount++;
            result = predicate.IsMatch(tags);
            return true;
        }
    }
}
