// GameCore.Derivation tests — SADR-013 (studio): an installation's configuration-bound rule payloads.
//
// SADR-013 chose to bind configuration into derivation (option a): the composition-to-derivation translation resolves
// each bound rule's payload from the installation's effective configuration and hands it in as a `BoundRulePayload`.
// These tests pin what that means inside derivation, independent of Unity:
//
//   * the bound bytes are the contribution's payload, under the rule's unchanged contribution key (P-017);
//   * the full engine, the incremental engine and the reference oracle agree on a bound composition;
//   * a reconfiguration that moves only the bound value is a "changed" contribution - never a remove-and-add - and
//     the change set names the installation, so the incremental engine re-derives it instead of carrying stale rows;
//   * an installation without bindings keeps its previous snapshot hash exactly (no canonical-text drift);
//   * a binding cannot invent a rule the manifest does not declare, nor bind one rule twice.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class BoundRulePayloadTests
    {
        private static readonly RuleId FestivalRule =
            FixtureIds.Rule(CardComposition.FestivalScoring + CardComposition.SetBonusSuffix);

        [Test]
        public void ABoundPayloadIsTheContributedValueAndAllThreeEnginesAgree()
        {
            DerivationSnapshot bound = Snapshot(7, 1UL);
            FixtureValueSource values = CardComposition.ValueSource();

            DerivationResult full = DerivationAssert.Accepted(
                DerivationEngine.Derive(bound, values, DerivationOptions.Default, null));
            DerivationResult incremental = DerivationAssert.Accepted(
                IncrementalDerivationEngine.Derive(bound, values, DerivationOptions.Default, null, null).Result);
            DerivationResult oracle = DerivationAssert.Accepted(
                DerivationOracle.Derive(bound, values, DerivationOptions.Default, null));

            // Seat A sits under League A, where only the festival provider reaches it (07 s2.1).
            EffectiveSlot slot = DerivationAssert.SlotOf(full, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus);
            Assert.That(DerivationAssert.Int32Value(slot), Is.EqualTo(7), "the configured value replaces the manifest's 2");
            Assert.That(
                DerivationProjection.SemanticsText(incremental),
                Is.EqualTo(DerivationProjection.SemanticsText(full)),
                "the incremental engine must see the bound payload exactly as the full engine does");
            Assert.That(
                DerivationProjection.SemanticsText(oracle),
                Is.EqualTo(DerivationProjection.SemanticsText(full)),
                "the reference oracle must see the bound payload exactly as the full engine does");

            // League B's quiet provider is not bound: it still contributes its manifest payload.
            EffectiveSlot quiet = DerivationAssert.SlotOf(full, FixtureIds.Target(CardComposition.SeatC), CardComposition.SetBonus);
            Assert.That(DerivationAssert.Int32Value(quiet), Is.EqualTo(CardComposition.QuietBonus));
        }

        [Test]
        public void AReconfigureIsAChangedContributionUnderTheSameKey()
        {
            FixtureValueSource values = CardComposition.ValueSource();
            DerivationSnapshot before = Snapshot(7, 1UL);
            DerivationSnapshot after = Snapshot(11, 2UL);

            DerivationResult first = DerivationAssert.Accepted(
                IncrementalDerivationEngine.Derive(before, values, DerivationOptions.Default, null, null).Result);
            IncrementalDerivationOutcome outcome =
                IncrementalDerivationEngine.Derive(after, values, DerivationOptions.Default, first, null);
            DerivationResult second = DerivationAssert.Accepted(outcome.Result);
            DerivationResult oracle = DerivationAssert.Accepted(
                DerivationOracle.Derive(after, values, DerivationOptions.Default, first));

            EffectiveSlot slot = DerivationAssert.SlotOf(second, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus);
            Assert.That(DerivationAssert.Int32Value(slot), Is.EqualTo(11), "the incremental base must not carry the old value");
            Assert.That(
                DerivationProjection.SemanticsText(second),
                Is.EqualTo(DerivationProjection.SemanticsText(oracle)));

            Assert.That(second.Delta, Is.Not.Null);
            DerivationDelta delta = second.Delta!;
            Assert.That(delta.Added, Is.Empty, "a reconfigure adds no contribution (P-017)");
            Assert.That(delta.Removed, Is.Empty, "a reconfigure removes no contribution (P-017)");
            Assert.That(delta.Changed.Count, Is.GreaterThan(0), "the bound value moved, so contributions changed");
            for (int i = 0; i < delta.Changed.Count; i++)
            {
                Assert.That(delta.Changed[i].Rule, Is.EqualTo(FestivalRule), "only the bound rule's contributions change");
            }

            DerivationChangeSet changes = DerivationChangeSet.Diff(before, after);
            Assert.That(
                changes.ChangedInstalls,
                Does.Contain(FixtureIds.Instance(CardComposition.FestivalScoring)),
                "a moved bound payload is an install change, so its reach is re-derived");
        }

        [Test]
        public void AnInstallWithoutBindingsKeepsItsSnapshotHash()
        {
            DerivationSnapshot plain = CardComposition.Builder()
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), new AssemblyEpoch(1UL))
                .ToSnapshot();
            DerivationSnapshot rebuiltWithEmptyBindings = Rebind(plain, null);
            DerivationSnapshot bound = Snapshot(7, 1UL);

            Assert.That(
                rebuiltWithEmptyBindings.SnapshotHash.Equals(plain.SnapshotHash),
                Is.True,
                "an empty binding list must not change the canonical snapshot text");
            Assert.That(
                bound.SnapshotHash.Equals(plain.SnapshotHash),
                Is.False,
                "a bound payload is semantic input, so it is part of the snapshot hash (P-008)");
        }

        [Test]
        public void ABindingCannotInventOrDuplicateARule()
        {
            DerivationSnapshot plain = CardComposition.Builder()
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), new AssemblyEpoch(1UL))
                .ToSnapshot();
            DerivationInstall festival = Find(plain, CardComposition.FestivalScoring);

            var undeclared = new List<BoundRulePayload>
            {
                new BoundRulePayload(FixtureIds.Rule("cards.not-declared"), FixturePayload.Int32(1)),
            };
            Assert.Throws<ArgumentException>(() =>
                new DerivationInstall(festival.Record, festival.State, festival.Manifest, undeclared));

            var twice = new List<BoundRulePayload>
            {
                new BoundRulePayload(FestivalRule, FixturePayload.Int32(1)),
                new BoundRulePayload(FestivalRule, FixturePayload.Int32(2)),
            };
            Assert.Throws<ArgumentException>(() =>
                new DerivationInstall(festival.Record, festival.State, festival.Manifest, twice));

            var once = new List<BoundRulePayload> { new BoundRulePayload(FestivalRule, FixturePayload.Int32(5)) };
            var install = new DerivationInstall(festival.Record, festival.State, festival.Manifest, once);
            DerivationRule rule = festival.Manifest.DerivationRules[0];
            Assert.That(FixturePayload.TryReadInt32(install.PayloadOf(rule), out int value), Is.True);
            Assert.That(value, Is.EqualTo(5));
            Assert.That(FixturePayload.TryReadInt32(festival.PayloadOf(rule), out int manifestValue), Is.True);
            Assert.That(manifestValue, Is.EqualTo(CardComposition.FestivalBonus), "unbound installs keep the manifest payload");
        }

        /// <summary>The card composition with the festival rule bound to <paramref name="bonus"/>.</summary>
        private static DerivationSnapshot Snapshot(int bonus, ulong revision)
        {
            DerivationSnapshot plain = CardComposition.Builder()
                .Build(PropagationMode.Automatic, new CompositionRevision(revision), new AssemblyEpoch(revision))
                .ToSnapshot();
            return Rebind(plain, new List<BoundRulePayload>
            {
                new BoundRulePayload(FestivalRule, FixturePayload.Int32(bonus)),
            });
        }

        /// <summary>A copy of <paramref name="snapshot"/> whose festival installation carries <paramref name="bound"/>.</summary>
        private static DerivationSnapshot Rebind(DerivationSnapshot snapshot, IReadOnlyList<BoundRulePayload>? bound)
        {
            var installs = new List<DerivationInstall>(snapshot.Installs.Count);
            for (int i = 0; i < snapshot.Installs.Count; i++)
            {
                DerivationInstall install = snapshot.Installs[i];
                if (install.Instance.Equals(FixtureIds.Instance(CardComposition.FestivalScoring)))
                {
                    installs.Add(new DerivationInstall(install.Record, install.State, install.Manifest, bound));
                }
                else
                {
                    installs.Add(install);
                }
            }

            return new DerivationSnapshot(
                snapshot.World,
                snapshot.Revision,
                snapshot.Epoch,
                snapshot.Mode,
                snapshot.Scopes,
                installs,
                snapshot.Targets,
                snapshot.Contracts.Contracts,
                snapshot.RuleKeys,
                snapshot.Overrides);
        }

        private static DerivationInstall Find(DerivationSnapshot snapshot, string name)
        {
            for (int i = 0; i < snapshot.Installs.Count; i++)
            {
                if (snapshot.Installs[i].Instance.Equals(FixtureIds.Instance(name)))
                {
                    return snapshot.Installs[i];
                }
            }

            Assert.Fail("no installation " + name);
            return null!;
        }
    }
}
