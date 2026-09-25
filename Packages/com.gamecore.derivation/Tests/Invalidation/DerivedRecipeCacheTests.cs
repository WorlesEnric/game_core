// GameCore.Derivation tests — the derived-recipe variant cache and its staleness rules (GC-013, P-024).
//
// P-024: "Cache derived variants by recipe revision, scope inheritance fingerprint, mode, and catalog hash. On
// spawn publication, validate against the current composition revision; a stale variant is recomputed or rejected
// `StalePlan`, never published half-assembled."
//
// These tests pin the three properties that make the cache safe: an unchanged inheritance is a hit, any edit that
// can change what derives on a target changes its fingerprint (so the entry is recomputed, never reused), and an
// edit outside the target's path leaves its variant reusable.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class DerivedRecipeCacheTests
    {
        private const string HolidayScoring = "cards.holiday-scoring";
        private const string ExtraSeat = "seat-z";

        [Test]
        public void AnUnchangedInheritanceIsAHitAndAChangedOneIsAStaleRecomputation()
        {
            DerivationSnapshot snapshot = CardSnapshot();
            DerivationIndexSet indexes = DerivationIndexSet.Build(snapshot);
            ContentHash catalogHash = CapabilityCatalogHash.Compute(snapshot);
            DerivedRecipeCache cache = new DerivedRecipeCache(catalogHash);

            IReadOnlyList<IndexedRule> first = cache.Resolve(
                snapshot, indexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash fingerprint);
            IReadOnlyList<IndexedRule> second = cache.Resolve(
                snapshot, indexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash again);

            Assert.That(first.Count, Is.GreaterThan(0), "The league A festival reaches the seat.");
            Assert.That(second.Count, Is.EqualTo(first.Count));
            Assert.That(fingerprint.Equals(again), Is.True, "The same inputs give the same fingerprint (P-008).");
            Assert.That(cache.Hits, Is.EqualTo(1));
            Assert.That(cache.Misses, Is.EqualTo(1));
            Assert.That(cache.StaleRecomputations, Is.EqualTo(0));

            // The documented move of 07 s2.4 changes the inheritance path, so the entry must be recomputed.
            FixtureBuilder builder = CardComposition.Builder();
            builder.MoveScope(CardComposition.SeatAScope, CardComposition.LeagueB);
            DerivationSnapshot moved = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();
            DerivationIndexSet movedIndexes = DerivationIndexSet.Build(moved);

            IReadOnlyList<IndexedRule> afterMove = cache.Resolve(
                moved, movedIndexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash movedFingerprint);

            Assert.That(movedFingerprint.Equals(fingerprint), Is.False, "A move changes the fingerprint (P-025).");
            Assert.That(cache.StaleRecomputations, Is.EqualTo(1), "The stale variant is recomputed, never reused.");
            Assert.That(cache.Hits, Is.EqualTo(1), "A stale lookup is not a hit.");
            Assert.That(
                FindRule(afterMove, CardComposition.QuietScoring + CardComposition.SetBonusSuffix),
                Is.True,
                "The recomputed variant resolves the league B provider.");
            Assert.That(
                FindRule(afterMove, CardComposition.FestivalScoring + CardComposition.SetBonusSuffix),
                Is.False,
                "The league A provider no longer reaches the seat.");
        }

        [Test]
        public void AnEditOutsideTheTargetPathLeavesTheVariantReusable()
        {
            DerivationSnapshot snapshot = CardSnapshot();
            DerivationIndexSet indexes = DerivationIndexSet.Build(snapshot);
            ContentHash catalogHash = CapabilityCatalogHash.Compute(snapshot);
            DerivedRecipeCache cache = new DerivedRecipeCache(catalogHash);

            cache.Resolve(snapshot, indexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash fingerprint);

            // A provider mounted under league B cannot reach a league A seat, so the seat's inheritance is
            // unchanged and its variant is still current (P-023's locality, seen through the cache).
            FixtureBuilder builder = CardComposition.Builder();
            builder.Install(
                HolidayScoring,
                CardComposition.LeagueB,
                0,
                CardComposition.ScoringRules(HolidayScoring, 4),
                state: InstallationState.Active);
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();
            DerivationIndexSet afterIndexes = DerivationIndexSet.Build(after);

            IReadOnlyList<IndexedRule> second = cache.Resolve(
                after, afterIndexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash again);

            Assert.That(again.Equals(fingerprint), Is.True);
            Assert.That(cache.Hits, Is.EqualTo(1));
            Assert.That(cache.StaleRecomputations, Is.EqualTo(0));
            Assert.That(second.Count, Is.EqualTo(1), "The league A festival is the only provider on the seat's path.");
            Assert.That(
                FindRule(second, CardComposition.FestivalScoring + CardComposition.SetBonusSuffix),
                Is.True);
        }

        [Test]
        public void APayloadReconfigurationStalesTheVariant()
        {
            DerivationSnapshot snapshot = CardSnapshot();
            DerivationIndexSet indexes = DerivationIndexSet.Build(snapshot);
            DerivedRecipeCache cache = new DerivedRecipeCache(CapabilityCatalogHash.Compute(snapshot));
            cache.Resolve(snapshot, indexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash fingerprint);

            FixtureBuilder builder = CardComposition.Builder();
            builder.ReplaceRulePayload(
                CardComposition.FestivalScoring,
                CardComposition.FestivalScoring + CardComposition.SetBonusSuffix,
                FixturePayload.Int32(7));
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();
            DerivationIndexSet afterIndexes = DerivationIndexSet.Build(after);

            cache.Resolve(after, afterIndexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash again);

            Assert.That(again.Equals(fingerprint), Is.False, "A rule payload is part of the fingerprint (P-020).");
            Assert.That(cache.StaleRecomputations, Is.EqualTo(1));
        }

        [Test]
        public void AModeSwitchAndACatalogChangeAlsoChangeTheFingerprint()
        {
            DerivationSnapshot automatic = CardSnapshot();
            DerivationIndexSet automaticIndexes = DerivationIndexSet.Build(automatic);
            ContentHash catalogHash = CapabilityCatalogHash.Compute(automatic);
            DerivedRecipeCache cache = new DerivedRecipeCache(catalogHash);
            cache.Resolve(
                automatic, automaticIndexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash automaticPrint);

            DerivationSnapshot conservative = CardComposition.Builder()
                .Build(PropagationMode.Conservative, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();
            DerivationIndexSet conservativeIndexes = DerivationIndexSet.Build(conservative);
            cache.Resolve(
                conservative, conservativeIndexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash conservativePrint);

            Assert.That(conservativePrint.Equals(automaticPrint), Is.False, "The mode is part of the fingerprint (P-013).");

            // A different catalog hash is a different cache: the same snapshot under another catalog must not be
            // answered from the first catalog's entry.
            DerivedRecipeCache otherCatalog = new DerivedRecipeCache(new ContentHash(new byte[ContentHash.SizeInBytes]));
            otherCatalog.Resolve(
                automatic, automaticIndexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash otherPrint);
            Assert.That(otherPrint.Equals(automaticPrint), Is.False, "The catalog hash is part of the fingerprint (P-024).");
        }

        [Test]
        public void IsCurrentRejectsAVariantResolvedUnderAnotherFingerprint()
        {
            DerivationSnapshot snapshot = CardSnapshot();
            DerivationIndexSet indexes = DerivationIndexSet.Build(snapshot);
            ContentHash catalogHash = CapabilityCatalogHash.Compute(snapshot);
            DerivedRecipeCache cache = new DerivedRecipeCache(catalogHash);

            IReadOnlyList<IndexedRule> rules = cache.Resolve(
                snapshot, indexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash fingerprint);
            DerivedRecipeVariant variant = new DerivedRecipeVariant(
                fingerprint, FixtureIds.Recipe(CardComposition.CardSeatRecipe + ".definition", CardComposition.CardSeatRecipe),
                FixtureIds.Scope(CardComposition.SeatAScope),
                rules);

            Assert.That(DerivedRecipeCache.IsCurrent(variant, fingerprint), Is.True);

            FixtureBuilder builder = CardComposition.Builder();
            builder.MoveScope(CardComposition.SeatAScope, CardComposition.LeagueB);
            DerivationSnapshot moved = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();
            DerivationIndexSet movedIndexes = DerivationIndexSet.Build(moved);
            cache.Resolve(moved, movedIndexes, FixtureIds.Target(CardComposition.SeatA), out ContentHash movedPrint);

            Assert.That(
                DerivedRecipeCache.IsCurrent(variant, movedPrint),
                Is.False,
                "A variant prepared under the old inheritance is stale and is never published (P-024).");
        }

        [Test]
        public void TheCacheIsBoundedAndItsEvictionIsObservable()
        {
            DerivationSnapshot snapshot = CardSnapshot();
            DerivationIndexSet indexes = DerivationIndexSet.Build(snapshot);
            DerivedRecipeCache cache = new DerivedRecipeCache(CapabilityCatalogHash.Compute(snapshot), capacity: 2);

            cache.Resolve(snapshot, indexes, FixtureIds.Target(CardComposition.SeatA), out _);
            cache.Resolve(snapshot, indexes, FixtureIds.Target(CardComposition.SeatB), out _);
            Assert.That(cache.Count, Is.EqualTo(2));
            Assert.That(cache.Evictions, Is.EqualTo(0));

            cache.Resolve(snapshot, indexes, FixtureIds.Target(CardComposition.SeatC), out _);

            Assert.That(cache.Evictions, Is.EqualTo(2), "The cache is bounded, and the eviction is counted.");
            Assert.That(cache.Count, Is.EqualTo(1));
            Assert.That(cache.Describe(), Does.Contain("capacity=2"));

            cache.Clear();
            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(cache.Hits, Is.EqualTo(0), "Clearing the cache does not erase the run's evidence.");
        }

        [Test]
        public void ASpawnUnderANewScopeResolvesTheSameInheritanceAsItsSiblings()
        {
            // P-024's "fast repeated spawning": a target created later under the same inheritance resolves the same
            // reaching rules without walking the ancestor chain again.
            FixtureBuilder builder = CardComposition.Builder();
            builder.Target(ExtraSeat, CardComposition.SeatAScope, CardComposition.CardSeatRecipe);
            DerivationSnapshot snapshot = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();
            DerivationIndexSet indexes = DerivationIndexSet.Build(snapshot);
            DerivedRecipeCache cache = new DerivedRecipeCache(CapabilityCatalogHash.Compute(snapshot));

            IReadOnlyList<IndexedRule> existing = cache.Resolve(
                snapshot, indexes, FixtureIds.Target(CardComposition.SeatA), out _);
            IReadOnlyList<IndexedRule> spawned = cache.Resolve(
                snapshot, indexes, FixtureIds.Target(ExtraSeat), out _);

            Assert.That(cache.Hits, Is.EqualTo(1), "The sibling's entry is reused, not recomputed.");
            Assert.That(spawned.Count, Is.EqualTo(existing.Count));
            for (int i = 0; i < spawned.Count; i++)
            {
                Assert.That(spawned[i].Rule.RuleId.Equals(existing[i].Rule.RuleId), Is.True);
            }
        }

        private static bool FindRule(IReadOnlyList<IndexedRule> rules, string ruleName)
        {
            RuleId wanted = FixtureIds.Rule(ruleName);
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Rule.RuleId.Equals(wanted))
                {
                    return true;
                }
            }

            return false;
        }

        private static DerivationSnapshot CardSnapshot() =>
            CardComposition.Builder()
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();
    }
}
