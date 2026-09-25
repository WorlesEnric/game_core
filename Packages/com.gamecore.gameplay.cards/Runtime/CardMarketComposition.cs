// GameCore.Gameplay.Cards — the card market's derivation value source and its composition builder (GC-011).
//
// Normative sources: 07 s2.1 (the market composition tree, `FestivalScoring` +2 and `QuietScoring` +1, the nested
// festival +3, the isolated practice seat), P-013 (Automatic propagation to eligible existing and future
// descendants with no per-instance import), P-015 (eligibility is a reusable descriptor), P-016 (an isolation
// boundary blocks outside rules in either mode) and P-019 (the registered reducer is the contract's own).
//
// The composition itself is the GC-006 reusable card fixture's tree and stable names, reached through
// `GameCore.Rules.Cards.CardVocabulary`: this package mounts the market through the real control lane and derives
// it with the real engine, so the tree, the providers and the capability identities are the same literals the
// committed card catalog registers. The value source below is the registered reducer and predicate of that
// catalog, so a derivation over the mounted market resolves `cards.reducer.int32-sum` and
// `cards.predicate.always` exactly as the generated registrations declare them (P-009).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Rules.Cards;

namespace GameCore.Gameplay.Cards
{
    /// <summary>
    /// The card market's registered reducer and static predicate: the `cards.set-bonus` Int32 fold and the
    /// always-accepting target predicate (07 s2.1). Both delegate to the rules package, so the registered path and
    /// the direct path cannot drift (P-019, P-028).
    /// </summary>
    public sealed class CardDerivationValueSource : IDerivationValueSource
    {
        private readonly CardSetBonusReducer reducer;
        private readonly CardAlwaysPredicate predicate;

        /// <summary>Builds the source over the two generated card registrations.</summary>
        public CardDerivationValueSource(CardSetBonusReducer reducer, CardAlwaysPredicate predicate)
        {
            this.reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
            this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <summary>
        /// The source over the card catalog's own keys: `CardVocabulary.BonusReducerKey` and
        /// `CardVocabulary.AlwaysPredicateKey`. A derivation that resolves any other key reports a miss, which
        /// validation turns into a catalog error rather than a silent identity (P-028).
        /// </summary>
        public static CardDerivationValueSource Default() =>
            new CardDerivationValueSource(
                new CardSetBonusReducer(CardVocabulary.BonusReducerKey),
                new CardAlwaysPredicate(CardVocabulary.AlwaysPredicateKey));

        /// <summary>Reductions this source resolved, so a scenario can assert the registered path ran.</summary>
        public int ReductionCount { get; private set; }

        /// <summary>Predicate evaluations this source resolved.</summary>
        public int EvaluationCount { get; private set; }

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
                if (!CardTableDeclarations.TryReadInt32(inputs[i].Bytes, out int value))
                {
                    throw new ReducerFailureException(
                        reducerKey,
                        "contribution " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " is not one canonical int32 scalar (05 s6)");
                }

                values.Add(value);
            }

            if (!reducer.TryReduce(values, out int effectiveBonus, out string failure))
            {
                throw new ReducerFailureException(reducerKey, failure);
            }

            ReductionCount++;
            result = CardTableDeclarations.WriteInt32(effectiveBonus);
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

            EvaluationCount++;
            result = predicate.IsMatch(tags);
            return true;
        }
    }

    /// <summary>
    /// The mount payloads of the card market: a scope creation and an installation mount, in the exact shape the
    /// control lane's applier validates (GC-004). A mount's declared configuration hash is the canonical hash of
    /// the effective configuration (schema defaults over the local patch), which is what
    /// `CompositionEditApplier.ValidateConfigDocument` recomputes (P-020).
    /// </summary>
    public static class CardTablePayloads
    {
        /// <summary>
        /// O-02: create one scope under an existing parent. The parent must already exist in the state the edit is
        /// planned against, so a nested tree is created top-down (P-010).
        /// </summary>
        public static CompositionEditPayload ScopeCreate(
            ScopeId scope,
            ScopeId parent,
            bool isolateSetBonus)
        {
            IsolationSet capabilityIsolation = isolateSetBonus
                ? new IsolationSet(false, new[] { CardVocabulary.SetBonusCapability.Value })
                : new IsolationSet(false, null);

            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                null,
                capabilityIsolation,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>
        /// O-03: mount one plugin instance at one scope. The declared configuration hash is the canonical hash of
        /// the effective configuration, exactly as the applier recomputes it (P-020).
        /// </summary>
        public static CompositionEditPayload Mount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ConfigDocument effective = EffectiveConfiguration(manifest, instance);
            return new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                DefinitionRevision.First,
                ConfigDocumentCodec.HashOf(effective),
                ConfigDocument.Empty,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>
        /// O-08: set the world propagation mode. The scope must be default or the world root, because the mode is
        /// one world-level setting (P-013, P-014).
        /// </summary>
        public static CompositionEditPayload ModeSet(PropagationMode mode)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ModeSet,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                mode);
        }

        /// <summary>
        /// O-02: move a scope subtree under a new parent (07 s2.4's "Reparent `SeatA` from `LeagueA` to
        /// `LeagueB`"). Membership, contributions and bindings publish together (P-025).
        /// </summary>
        public static CompositionEditPayload ScopeReparent(ScopeId scope, ScopeId newParent)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeReparent,
                scope,
                newParent,
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>O-07: unmount one installation (07 s2.4's "Unmount `FestivalScoring`").</summary>
        public static CompositionEditPayload Unmount(PluginInstanceId instance)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.InstallUnmount,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>The effective configuration a mount publishes: schema defaults, then the local declaration.</summary>
        private static ConfigDocument EffectiveConfiguration(PluginManifest manifest, PluginInstanceId instance)
        {
            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(
                    ConfigLayerOrigin.SchemaDefaults,
                    manifest.ConfigSchema.Id.Value,
                    ConfigDocument.Empty),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, instance.Value, ConfigDocument.Empty),
            });

            return composed.Value;
        }
    }

    /// <summary>
    /// The card market's scope tree, install identities and manifest declarations: the mount plan a scenario
    /// applies through the real control lane.
    /// </summary>
    public static class CardMarketComposition
    {
        /// <summary>The world root scope: the match. Every card scope is created under it (P-010).</summary>
        public static ScopeId MatchScope => CardIdentity.Scope(CardVocabulary.Match);

        /// <summary>Create-scope payloads for the whole market tree, parents before children (P-010).</summary>
        public static IReadOnlyList<CompositionEditPayload> ScopeCreates()
        {
            ScopeId match = CardIdentity.Scope(CardVocabulary.Match);
            return new List<CompositionEditPayload>
            {
                CardTablePayloads.ScopeCreate(CardIdentity.Scope(CardVocabulary.TableArea), match, false),
                CardTablePayloads.ScopeCreate(CardIdentity.Scope(CardVocabulary.LeagueA), match, false),
                CardTablePayloads.ScopeCreate(CardIdentity.Scope(CardVocabulary.LeagueB), match, false),
                CardTablePayloads.ScopeCreate(CardIdentity.Scope(CardVocabulary.Spectators), match, false),
                CardTablePayloads.ScopeCreate(
                    CardIdentity.Scope(CardVocabulary.SeatAScope),
                    CardIdentity.Scope(CardVocabulary.LeagueA),
                    false),
                CardTablePayloads.ScopeCreate(
                    CardIdentity.Scope(CardVocabulary.SeatBScope),
                    CardIdentity.Scope(CardVocabulary.LeagueA),
                    false),
                CardTablePayloads.ScopeCreate(
                    CardIdentity.Scope(CardVocabulary.SeatCScope),
                    CardIdentity.Scope(CardVocabulary.LeagueB),
                    false),
                // The practice seat is eligible and beneath the festival provider, but its isolation boundary
                // blocks the contribution in either mode (P-016, 07 s2.1).
                CardTablePayloads.ScopeCreate(
                    CardIdentity.Scope(CardVocabulary.Practice),
                    CardIdentity.Scope(CardVocabulary.LeagueA),
                    true),
            };
        }

        /// <summary>The market table target's scope.</summary>
        public static ScopeId TableScope => CardIdentity.Scope(CardVocabulary.TableArea);

        /// <summary>The scoreboard target's scope: it takes no card rule, so it stays on its base recipe (P-015).</summary>
        public static ScopeId SpectatorScope => CardIdentity.Scope(CardVocabulary.Spectators);

        /// <summary>The practice target's scope: beneath the isolation boundary.</summary>
        public static ScopeId PracticeScope => CardIdentity.Scope(CardVocabulary.Practice);
    }
}
