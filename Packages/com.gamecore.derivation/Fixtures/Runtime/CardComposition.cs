// GameCore.Derivation fixtures — the card-market composition of 07 s2 (GC-006).
//
// Reusable card descriptors so GC-011 can run the card slice without re-declaring the market. Structure (07 s2.1):
//
//   match                  [card-table-runtime]
//   ├── table-area         table-1 : MarketTableRecipe
//   ├── league-a           [festival-scoring]
//   │   ├── seat-a-scope   seat-a : CardSeatRecipe        (a nested festival can also mount here: +3)
//   │   ├── seat-b-scope   seat-b : CardSeatRecipe
//   │   └── practice       [CapabilityIsolation: cards.set-bonus]  practice-seat : CardSeatRecipe
//   ├── league-b           [quiet-scoring]
//   │   └── seat-c-scope   seat-c : CardSeatRecipe
//   └── spectators         scoreboard : ScoreboardViewRecipe
//
// `cards.set-bonus` is `Additive` with a registered Int32 reducer: one provenance record per source, and reducing
// the effective integer bonus never assumes associativity beyond the registered reducer (P-019). The Practice seat
// is eligible and beneath the festival provider, but its isolation boundary blocks the contribution in either
// mode: a full target opt-in does not bypass a boundary (P-016). `cards.draw-policy` is `Exclusive`, which is what
// 07 s2.4 uses to show that a mode switch can expose an unresolved conflict.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation.Fixtures
{
    /// <summary>The card-market fixture of 07 s2, with its documented stable names and reducer registration.</summary>
    public static class CardComposition
    {
        // Scopes (07 s2.1)
        public const string Match = "cards.match";
        public const string TableArea = "cards.table-area";
        public const string LeagueA = "cards.league-a";
        public const string LeagueB = "cards.league-b";
        public const string Spectators = "cards.spectators";
        public const string SeatAScope = "cards.seat-a-scope";
        public const string SeatBScope = "cards.seat-b-scope";
        public const string SeatCScope = "cards.seat-c-scope";
        public const string Practice = "cards.practice";

        // Installations
        public const string FestivalScoring = "cards.festival-scoring";
        public const string NestedFestival = "cards.nested-festival";
        public const string QuietScoring = "cards.quiet-scoring";
        public const string DrawPolicyOne = "cards.draw-policy-one";
        public const string DrawPolicyTwo = "cards.draw-policy-two";

        // Target descriptors (reusable recipes)
        public const string CardSeatRecipe = "cards.card-seat-recipe";
        public const string MarketTableRecipe = "cards.market-table-recipe";
        public const string ScoreboardViewRecipe = "cards.scoreboard-view-recipe";

        // Targets
        public const string SeatA = "seat-a";
        public const string SeatB = "seat-b";
        public const string SeatC = "seat-c";
        public const string SeatD = "seat-d";
        public const string PracticeSeat = "practice-seat";
        public const string Scoreboard = "scoreboard";
        public const string TableOne = "table-1";

        // Derived capabilities
        public const string SetBonus = "cards.set-bonus";
        public const string DrawPolicy = "cards.draw-policy";
        public const string MarketTarget = "cards.market-target";

        // Payload schemas carried by those slots (07 s2.2 rows)
        public const string EffectiveSetBonusSchema = "cards.effective-set-bonus";
        public const string DrawPolicySchema = "cards.draw-policy-binding";
        public const string MarketBindingSchema = "cards.market-binding";

        // Rules
        public const string SetBonusSuffix = ".set-bonus";
        public const string DrawPolicySuffix = ".draw-policy";
        public const string MarketTargetRule = "cards.market-target-binding";

        // Generated registrations: the registered Int32 reducer and the always-accepting predicate
        public const string BonusReducer = "cards.reducer.int32-sum";
        public const string AlwaysPredicate = "cards.predicate.always";

        /// <summary>`cards.set-bonus` occupies stratum 0; nothing in this fixture depends on it (P-021).</summary>
        public const int BonusStratum = 0;

        /// <summary>The world id every card fixture run uses (P-004).</summary>
        public static WorldId DefaultWorld { get; } = new WorldId(FixtureIds.Id("gamecore.world.cards"));

        /// <summary>The documented bonus values of 07 s2.1 and REF-C04.</summary>
        public const int FestivalBonus = 2;
        public const int NestedFestivalBonus = 3;
        public const int QuietBonus = 1;

        /// <summary>
        /// Builds the composition. <paramref name="mountNestedFestival"/> adds the nested `+3` provider under
        /// League A; <paramref name="mountDrawPolicyConflict"/> mounts two applicable `Exclusive` policies, which
        /// is the conflict REF-C06 expects the whole plan to reject.
        /// </summary>
        public static FixtureBuilder Builder(
            bool mountNestedFestival = false,
            bool mountDrawPolicyConflict = false)
        {
            FixtureBuilder builder = new FixtureBuilder(DefaultWorld)
                .Scope(Match, null)
                .Scope(TableArea, Match)
                .Scope(LeagueA, Match)
                .Scope(LeagueB, Match)
                .Scope(Spectators, Match)
                .Scope(SeatAScope, LeagueA)
                .Scope(SeatBScope, LeagueA)
                .Scope(SeatCScope, LeagueB)
                .Scope(Practice, LeagueA, isolatedCapabilities: new[] { SetBonus });

            builder
                .Contract(SetBonus, BonusStratum, new[]
                {
                    new FixtureSlot(
                        EffectiveSetBonusSchema,
                        CompositionPolicy.Additive,
                        reducer: FixtureIds.Key(BonusReducer)),
                })
                .Contract(DrawPolicy, BonusStratum, new[]
                {
                    new FixtureSlot(DrawPolicySchema, CompositionPolicy.Exclusive),
                })
                .Contract(MarketTarget, BonusStratum, new[]
                {
                    new FixtureSlot(MarketBindingSchema, CompositionPolicy.Replace),
                });

            builder
                .Target(TableOne, TableArea, MarketTableRecipe)
                .Target(SeatA, SeatAScope, CardSeatRecipe)
                .Target(SeatB, SeatBScope, CardSeatRecipe)
                .Target(SeatC, SeatCScope, CardSeatRecipe)
                .Target(PracticeSeat, Practice, CardSeatRecipe)
                .Target(Scoreboard, Spectators, ScoreboardViewRecipe);

            builder.Install(
                FestivalScoring,
                LeagueA,
                0,
                ScoringRules(FestivalScoring, FestivalBonus),
                state: InstallationState.Active);

            builder.Install(
                QuietScoring,
                LeagueB,
                0,
                ScoringRules(QuietScoring, QuietBonus),
                state: InstallationState.Active);

            if (mountNestedFestival)
            {
                builder.Install(
                    NestedFestival,
                    SeatAScope,
                    0,
                    ScoringRules(NestedFestival, NestedFestivalBonus),
                    state: InstallationState.Active);
            }

            if (mountDrawPolicyConflict)
            {
                // One policy per league scope would never share a target; 07 s2.4's conflict needs two providers
                // whose reach overlaps, so the second is mounted at the match root and reaches both leagues.
                builder.Install(DrawPolicyOne, LeagueA, 0, DrawPolicyRules(DrawPolicyOne), state: InstallationState.Active);
                builder.Install(DrawPolicyTwo, Match, 0, DrawPolicyRules(DrawPolicyTwo), state: InstallationState.Active);
            }

            builder.Install(MarketTargetRule, TableArea, 0, MarketRules(), state: InstallationState.Active);
            return builder;
        }

        /// <summary>The value source this fixture registers: the Int32 sum reducer and one always-accepting predicate.</summary>
        public static FixtureValueSource ValueSource() =>
            new FixtureValueSource()
                .RegisterInt32Sum(BonusReducer)
                .RegisterAlwaysPredicate(AlwaysPredicate);

        /// <summary>One scoring provider's rule set: a single Additive slot over every compatible seat (07 s2.1).</summary>
        public static IReadOnlyList<DerivationRule> ScoringRules(string installName, int bonus) =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    installName + SetBonusSuffix,
                    SetBonus,
                    BonusStratum,
                    1U,
                    FixtureBuilder.Selector(CardSeatRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Additive,
                    FixturePayload.Int32(bonus)),
            };

        /// <summary>One `Exclusive` draw-policy provider (07 s2.4).</summary>
        public static IReadOnlyList<DerivationRule> DrawPolicyRules(string installName) =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    installName + DrawPolicySuffix,
                    DrawPolicy,
                    BonusStratum,
                    1U,
                    FixtureBuilder.Selector(CardSeatRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Exclusive,
                    FixturePayload.Tag(installName + ".policy")),
            };

        /// <summary>The market binding rule: the only rule whose selector is the market table recipe (07 s2.1).</summary>
        public static IReadOnlyList<DerivationRule> MarketRules() =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    MarketTargetRule,
                    MarketTarget,
                    BonusStratum,
                    1U,
                    FixtureBuilder.Selector(MarketTableRecipe),
                    FixtureIds.Key(AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("cards.market-binding")),
            };

        /// <summary>
        /// The seat target descriptor of a later-created seat (07 s2.4 "Spawn seat D below LeagueA"), so a future
        /// target can be added to the same fixture without re-declaring the recipe.
        /// </summary>
        public static FixtureBuilder AddSeat(FixtureBuilder builder, string seatName, string scopeName) =>
            builder.Target(seatName, scopeName, CardSeatRecipe);
    }
}
