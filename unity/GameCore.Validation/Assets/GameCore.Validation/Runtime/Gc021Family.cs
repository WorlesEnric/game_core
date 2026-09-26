// GameCore.Validation.ProbeHost — the GC-021 family half of the durable-delivery proof.
//
// The gate sentence this file serves, from `docs/game-core/09-implementation-guide.md` (GC-021) and the task's own
// probe sentence, is: "irreversible output adapters consume only committed events, use explicit external idempotency
// keys, and persist an outbox when delivery must survive crashes" (P-045) — driven over a real narrative world and a
// real card world, with a deterministic crash point, a capacity refusal that is never a silent drop, a
// checkpoint/reinstate round trip that carries the outbox and its cursor, and the narrative-to-card reward bridge
// that turns one accepted choice into one durable, idempotent card mutation (P-050, P-053, 07 s5).
//
// WHY THERE IS NO `IGc021Family`
//
// GC-018 needed a new interface because a checkpoint round trip needs facts no earlier scenario declared: the queued
// command, the dormant slot, the persistent clock, the boundary enrichment. GC-021 needs *nothing* a genre has to
// declare that `IGc018Family` (and therefore the existing family hosts) does not already declare:
//
//   * the world the seam runs over is the family's own world, created from `IGc013Family.CreateRequest` and its
//     generated registration, seeded by `IGc013Family.SeedTargets` and driven by its own runtime module — which is
//     exactly what `Gc018Scenario` already does;
//   * the one committed event that becomes a delivery obligation is the family's own declared command, which
//     `IGc018Family.QueuedCommand` builds on the family's own route, target and payload schema;
//   * the reward content is not a genre fact at all: it is the bridge's declared content policy, and the only values
//     it names are the two gameplay packages' own declared identities and ordinals.
//
// So this file declares no interface. It holds the shared value types of the run (`Gc021Step`,
// `Gc021ScenarioResult`), the two pieces of *reading* GC-021 does off a family (the narrative dialogue node its own
// live conversation sits on — `Gc021Choice` — and the reward content table — `Gc021RewardContent`), and the four
// catalog runners that build the family objects exactly the way `Gc018NarrativeHost` / `Gc018CardsHost` build theirs.
// A genre that declared nothing extra cannot drift from a genre that declares a lot, which is what keeps "both
// families" a comparison of two real runs rather than of two hand-written expectations (P-001).
//
// THE ONE PLACE THE CONTENT DEPARTS FROM `RewardCatalog.Default()`, AND WHY
//
// `RewardCatalog.Default()` declares node 1 granting card `CardId(1)` from seat B to seat A. Neither the node nor the
// card is right for these two family fixtures, and the landed `run_gc021_probe.sh` requires both to be right:
//
//   * node — the narrative family's own `SeedTargets` writes `MutableValue` (7) into the addressed villager's
//     conversation node slot, and `NarrativeDialogueRules.Validate` accepts a choice only at the node the
//     conversation really sits on, so the accepted choice lands on `PermitResultNode` (2), not on 1. The scenario
//     reads the node out of the committed choice payload and declares the content over *that* node, which is what
//     "the reward content covers the observed node" means;
//   * card — `CardTableKeys.SeatCard(seat, index)` is `(1 + index) + 100 * (seat + 1)`, so no seat in either family
//     fixture holds `CardId(1)`, and `CardRewardDestination` terminally refuses a reward whose card its holding seat
//     does not stock. `Gc021RewardContent.ForNode` therefore names `SeatCard(SeatBOrdinal, 0)` — a card the card
//     fixture's own `SeedSeat` really deals to seat B — because "one reward reached the recipient" has to be a real
//     mutation rather than a declared intention (P-034).
//
// Everything else about the content is the package's own: the seats are `CardTableKeys.SeatAOrdinal` /
// `CardTableKeys.SeatBOrdinal`, the card is the card package's arithmetic, the revision is content versioning (P-054),
// and the payload is `RewardPayloadCodec`'s canonical encoding, never a second one.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Integration.RewardOutbox;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Narrative;
using GameCore.Validation.Generated;
using GameCore.Validation.GeneratedCards;
using GameCore.Validation.Probe;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// Which genre half of Part B one run executes. It selects the *reading* the scenario does off the family (the
    /// dialogue node its live conversation sits on, or the card table's declared seats), never a different sequence:
    /// Part A and the eleven named observations are identical for both genres (P-001).
    /// </summary>
    public enum Gc021Genre
    {
        /// <summary>The narrative family: its own accepted choice is the committed event a reward comes from.</summary>
        Narrative = 0,

        /// <summary>The card family: its own committed table result is the committed event a reward comes from.</summary>
        Cards = 1,
    }

    /// <summary>One named GC-021 observation: what was checked and the values it was computed from.</summary>
    public sealed class Gc021Step
    {
        public Gc021Step(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Full result of one GC-021 run: the named observations plus one digest over them, computed with the same digest
    /// function the narrative trace uses. A run that records a different set of observations, or a failing one, cannot
    /// report the expected digest.
    /// </summary>
    public sealed class Gc021ScenarioResult
    {
        public Gc021ScenarioResult(string label, IReadOnlyList<Gc021Step> steps)
        {
            Label = label;
            Steps = steps;
            var lines = new List<string>(steps.Count);
            bool allPassed = steps.Count > 0;
            for (int i = 0; i < steps.Count; i++)
            {
                lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
                allPassed &= steps[i].Passed;
            }

            AllPassed = allPassed;
            Digest = NarrativeDigest.OfLines(lines);
        }

        /// <summary>The family label every observation name of this run is qualified with.</summary>
        public string Label { get; }

        /// <summary>The named observations, in execution order, qualified with <see cref="Label"/>.</summary>
        public IReadOnlyList<Gc021Step> Steps { get; }

        /// <summary>Canonical digest over this run's observation names and pass flags.</summary>
        public string Digest { get; }

        public bool AllPassed { get; }

        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].ToString());
                }
            }

            return "digest=" + Digest
                + "; label=" + Label
                + "; steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));
        }
    }

    /// <summary>
    /// The reward content one GC-021 run declares: one definition, at one node, granting one card the card fixture
    /// really stocks, from one declared seat to another. Immutable, so the content a probe reports is the content the
    /// bridge ran with (P-054).
    /// </summary>
    public sealed class Gc021RewardContent
    {
        /// <summary>The node ordinal of a world that carries no narrative choice (the card family's world).</summary>
        public const int NoObservedNode = -1;

        /// <summary>
        /// Stable node ordinal the card family's content names. A card world commits no narrative choice, so there is
        /// no observed node to cover; the content keeps the documented default node instead of inventing one, and the
        /// step that reports coverage names that basis in its detail.
        /// </summary>
        public const int CardsDeclaredNode = 1;

        private readonly int nodeOrdinal;
        private readonly CardId card;
        private readonly uint recipientSeat;
        private readonly uint holdingSeat;
        private readonly uint revision;

        private Gc021RewardContent(int nodeOrdinal, CardId card, uint recipientSeat, uint holdingSeat, uint revision)
        {
            this.nodeOrdinal = nodeOrdinal;
            this.card = card;
            this.recipientSeat = recipientSeat;
            this.holdingSeat = holdingSeat;
            this.revision = revision;
        }

        /// <summary>The node the accepted choice landed on: the reward's key, as the committed event records it.</summary>
        public int NodeOrdinal => nodeOrdinal;

        /// <summary>The granted card: the card package's own arithmetic over the holding seat's ordinal.</summary>
        public CardId Card => card;

        /// <summary>Seat the card is granted to.</summary>
        public uint RecipientSeat => recipientSeat;

        /// <summary>Seat the reward card is drawn from, where the card fixture really stocks it.</summary>
        public uint HoldingSeat => holdingSeat;

        /// <summary>Immutable definition revision; part of the reward's identity (07 s5, P-054).</summary>
        public uint Revision => revision;

        /// <summary>
        /// The content the two families' fixtures can really satisfy: the reward card is the one the card fixture's
        /// own `SeedSeat` deals to seat B, and it is granted to seat A, which does not hold it. Nothing here is a card
        /// concept this package invented — both seats and the card identity are `CardTableKeys`' own declared values
        /// (P-001, P-034).
        /// </summary>
        public static Gc021RewardContent ForNode(int nodeOrdinal)
            => new Gc021RewardContent(
                nodeOrdinal,
                CardTableKeys.SeatCard(CardTableKeys.SeatBOrdinal, 0),
                CardTableKeys.SeatAOrdinal,
                CardTableKeys.SeatBOrdinal,
                1U);

        /// <summary>One immutable definition of this content, in the shape the catalog and the payload both use.</summary>
        public RewardDefinition Definition()
            => new RewardDefinition(nodeOrdinal, card, recipientSeat, holdingSeat, revision);

        /// <summary>
        /// The content table the bridge runs with. <paramref name="requiresDurability"/> is the content's own policy
        /// and the scenario states it, so "the reward obligation is durable" is a claim about what was declared
        /// rather than about what happened to be configured (P-045).
        /// </summary>
        public RewardCatalog ToCatalog(bool requiresDurability, out string detail)
        {
            var catalog = new RewardCatalog(requiresDurability);
            if (!catalog.TryDeclare(Definition(), out detail))
            {
                return catalog;
            }

            detail = string.Empty;
            return catalog;
        }

        /// <summary>The declared content, in the detail vocabulary the observations report it in.</summary>
        public string Describe()
            => "node=" + nodeOrdinal.ToString(CultureInfo.InvariantCulture)
                + "; card=" + card.Value.ToString(CultureInfo.InvariantCulture)
                + "; recipientSeat=" + recipientSeat.ToString(CultureInfo.InvariantCulture)
                + "; holdingSeat=" + holdingSeat.ToString(CultureInfo.InvariantCulture)
                + "; revision=" + revision.ToString(CultureInfo.InvariantCulture);

        public override string ToString() => "gc021RewardContent(" + Describe() + ")";
    }

    /// <summary>
    /// The narrative half's declared choice: the node the family's *own* live conversation sits on, and the choice
    /// ordinal the reference graph accepts there.
    ///
    /// The node is read off the family, never written here: `IGc013Family.MutableValue` is the value
    /// `Gc013NarrativeHost.NarrativeFamily.SeedTargets` writes into the addressed villager's conversation node slot,
    /// and `NarrativeDialogueRules.Validate` refuses a choice that names any other node (07 s3.2). A scenario that
    /// picked its own node would be asserting a refusal, not an accepted choice.
    /// </summary>
    public sealed class Gc021Choice
    {
        private Gc021Choice(int nodeOrdinal, int choiceOrdinal, TargetId target)
        {
            NodeOrdinal = nodeOrdinal;
            ChoiceOrdinal = choiceOrdinal;
            Target = target;
        }

        /// <summary>Dialogue node ordinal the family's live conversation sits on when the choice is submitted.</summary>
        public int NodeOrdinal { get; }

        /// <summary>The declared choice that is accepted at that node and requests the chapter's gate fact.</summary>
        public int ChoiceOrdinal { get; }

        /// <summary>The villager the family's own choice route addresses.</summary>
        public TargetId Target { get; }

        /// <summary>
        /// The choice this family's world accepts: its own seeded conversation node plus the reference graph's
        /// permit choice, on the family's own declared route, target and payload schema (P-037, P-042).
        /// </summary>
        public static Gc021Choice Of(IGc013Family family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Gc021Choice(
                family.MutableValue,
                NarrativeDialogueRules.PermitChoice,
                NarrativeKeys.Mara);
        }

        /// <summary>
        /// The command envelope, built with the narrative package's own choice codec so the bytes the world admits are
        /// the bytes its reader decodes (P-054, 05 s6).
        /// </summary>
        public CommandEnvelope Command(OperationId operation)
            => new CommandEnvelope(
                operation,
                NarrativeKeys.ChoiceRoute,
                Target,
                NarrativeKeys.ChoiceCommandSchema,
                null,
                new FrozenPayload(NarrativePayloadCodec.EncodeChoice(new NarrativeChoice(NodeOrdinal, ChoiceOrdinal))));

        public override string ToString()
            => "gc021Choice(node=" + NodeOrdinal.ToString(CultureInfo.InvariantCulture)
                + ", choice=" + ChoiceOrdinal.ToString(CultureInfo.InvariantCulture)
                + ", target=" + Target.ToString() + ")";
    }

    /// <summary>
    /// The four catalog runners of the GC-021 mode: each family over its committed generated catalog and over its
    /// hand-written generated-style catalog, through the same scenario, so the two runs record the same named
    /// observations and must report the same digest.
    ///
    /// They are built exactly as `Gc013NarrativeHost`'s and `Gc013CardsHost`'s own GC-018 runners build theirs — the
    /// same `BuildVerifiedCatalog` call, the same `Declarations(...)` set, the same family constructor, the same
    /// fingerprint literal check — because the GC-021 proof must run the family the earlier gates ran and not a family
    /// assembled for this task (P-001, P-028).
    /// </summary>
    public static class Gc021CatalogRuns
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the sibling scenarios use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>Runs the GC-021 sequence for the narrative family over the committed generated catalog.</summary>
        public static Gc021ScenarioResult RunNarrativeGeneratedCatalog()
        {
            CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated catalog was rejected by the production catalog rules: " + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            if (!ContentHash.TryParseHex(ProbeCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return Gc021Scenario.Run(
                new Gc013NarrativeHost.NarrativeFamily(
                    catalog,
                    Gc013NarrativeHost.Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                    ProbeCatalog.CatalogFingerprint),
                Gc021Genre.Narrative);
        }

        /// <summary>Runs the GC-021 sequence for the narrative family over the hand-written fixture catalog.</summary>
        public static Gc021ScenarioResult RunNarrativeFixtureCatalog()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style narrative catalog was rejected: " + build.Describe());
            }

            return Gc021Scenario.Run(
                new Gc013NarrativeHost.NarrativeFamily(
                    build.Catalog,
                    Gc013NarrativeHost.Declarations(
                        NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                    NarrativeScenarioCatalog.Fingerprint().ToHex()),
                Gc021Genre.Narrative);
        }

        /// <summary>Runs the GC-021 sequence for the card family over the committed generated catalog.</summary>
        public static Gc021ScenarioResult RunCardsGeneratedCatalog()
        {
            CatalogBuildResult build = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated card catalog was rejected by the production catalog rules: "
                    + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            if (!ContentHash.TryParseHex(CardCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return Gc021Scenario.Run(
                new Gc013CardsHost.CardFamily(
                    catalog, Gc013CardsHost.Declarations(), CardCatalog.CatalogFingerprint),
                Gc021Genre.Cards);
        }

        /// <summary>Runs the GC-021 sequence for the card family over the hand-written fixture catalog.</summary>
        public static Gc021ScenarioResult RunCardsFixtureCatalog()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return Gc021Scenario.Run(
                new Gc013CardsHost.CardFamily(
                    build.Catalog, Gc013CardsHost.Declarations(), CardCatalogTable.Fingerprint().ToHex()),
                Gc021Genre.Cards);
        }

        /// <summary>
        /// Runs the narrative family over both catalogs and returns the combined observations: the generated-catalog
        /// steps keep their names, and the fixture-catalog steps are prefixed with <see cref="FixtureRunPrefix"/> so
        /// no two collide.
        /// </summary>
        public static IReadOnlyList<Gc021Step> RunBothNarrative(
            out Gc021ScenarioResult generated,
            out Gc021ScenarioResult fixture)
        {
            generated = RunNarrativeGeneratedCatalog();
            fixture = RunNarrativeFixtureCatalog();
            return Combine(generated, fixture);
        }

        /// <summary>Runs the card family over both catalogs, with the same name qualification.</summary>
        public static IReadOnlyList<Gc021Step> RunBothCards(
            out Gc021ScenarioResult generated,
            out Gc021ScenarioResult fixture)
        {
            generated = RunCardsGeneratedCatalog();
            fixture = RunCardsFixtureCatalog();
            return Combine(generated, fixture);
        }

        private static IReadOnlyList<Gc021Step> Combine(Gc021ScenarioResult generated, Gc021ScenarioResult fixture)
        {
            var combined = new List<Gc021Step>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                Gc021Step step = fixture.Steps[i];
                combined.Add(new Gc021Step(FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }
    }
}
