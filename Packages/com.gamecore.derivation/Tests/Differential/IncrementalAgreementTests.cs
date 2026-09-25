// GameCore.Derivation tests — incremental versus reference evaluator over 50 seeds x 500 operations (GC-013).
//
// TEST-008: "Generate 50 fixed seeds, each with 500 operations chosen from mount, unmount, reconfigure, spawn,
// retire, reparent, exclusion change, provider replacement and mode switch. After each accepted publication,
// compare canonical effective capabilities and provenance against the reference evaluator. Keep failed seeds and
// the reduced counterexample."
//
// This is the gate for the incremental engine (GC-013) and it compares three things after every step:
//
//   1. **Acceptance and rejection kind** — an incremental path must not accept what the oracle rejects.
//   2. **The semantic projection** — effective capability sets, per-slot values, support identities, shadowed
//      identities and recipe hashes (`DerivationProjection.SemanticsText`).
//   3. **The decision set and the delta** — the engine may invent no decision the oracle cannot reproduce, and the
//      contribution delta must be identical, which is what "retracts exactly its support" means in practice.
//
// Every assertion carries "seed N, step M (operation ...)" so a failure is addressable without a re-run, and the
// failing seed and operation are printed in the assertion message (TEST-008's "keep failed seeds").
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class IncrementalAgreementTests
    {
        /// <summary>Fixed seeds of the sweep; the count is part of the witness (TEST-008).</summary>
        private const int SeedCount = 50;

        /// <summary>Operations per seed, as TEST-008 requires.</summary>
        private const int StepsPerSeed = 500;

        [Test]
        public void TheIncrementalEngineMatchesTheOracleOnEverySeedAndStep()
        {
            for (uint seed = 1U; seed <= SeedCount; seed++)
            {
                RunSeed(seed, SequenceFamily.Narrative);
            }
        }

        [Test]
        public void TheIncrementalEngineMatchesTheOracleOnTheCardVocabularyToo()
        {
            for (uint seed = 5000U; seed < 5000U + SeedCount; seed++)
            {
                RunSeed(seed, SequenceFamily.Cards);
            }
        }

        [Test]
        public void TheIncrementalEngineAlsoAgreesWithAFullRecomputationOnExplanations()
        {
            // The oracle produces no explanations (GC-006), so provenance parity in the P-026 sense is asserted
            // against a full recomputation for a sample of steps.
            DerivationOptions options = DerivationOptions.Default;
            const uint seed = 7U;
            OperationSequence sequence = new OperationSequence(seed, SequenceFamily.Narrative);
            DerivationResult? previous = null;
            DerivationResult? previousIncremental = null;
            for (int step = 0; step < StepsPerSeed; step++)
            {
                string operation = sequence.ApplyNextOperation(step);
                DerivationSnapshot snapshot = sequence.Snapshot();
                DerivationResult full = DerivationEngine.Derive(snapshot, sequence.Values, options, previous);
                IncrementalDerivationOutcome incremental = IncrementalDerivationEngine.Derive(
                    snapshot, sequence.Values, options, previousIncremental, null);
                string where = Where(seed, step, operation, sequence);

                Assert.That(incremental.Result.Accepted, Is.EqualTo(full.Accepted), where + ": acceptance differs.");
                if (full.Accepted && step % 25 == 0)
                {
                    Assert.That(
                        DerivationProjection.SemanticsText(incremental.Result),
                        Is.EqualTo(DerivationProjection.SemanticsText(full)),
                        where + ": the incremental result differs from a full recomputation.");
                    Assert.That(
                        ExplanationText(incremental.Result),
                        Is.EqualTo(ExplanationText(full)),
                        where + ": carried provenance differs from a recomputed one (P-026).");
                }

                previous = full.Accepted ? full : previous;
                previousIncremental = incremental.Result.Accepted ? incremental.Result : previousIncremental;
            }
        }

        [Test]
        public void AModeSwitchReportsAWholeWorldInvalidation()
        {
            OperationSequence sequence = new OperationSequence(3U, SequenceFamily.Narrative);
            DerivationResult? previous = null;
            bool sawSwitch = false;
            for (int step = 0; step < 40 && !sawSwitch; step++)
            {
                PropagationMode before = sequence.Mode;
                string operation = sequence.ApplyNextOperation(step);
                DerivationSnapshot snapshot = sequence.Snapshot();
                IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                    snapshot, sequence.Values, DerivationOptions.Default, previous, null);
                if (snapshot.Mode != before && previous != null && previous.Accepted)
                {
                    sawSwitch = true;
                    Assert.That(outcome.Invalidation.WholeWorld, Is.True, "A mode switch invalidates the world (P-014).");
                    Assert.That(outcome.UsedFullRecompute, Is.True, "The cost of a mode switch is reported, not hidden.");
                    Assert.That(
                        outcome.Invalidation.Counters.Reasons,
                        Does.Contain(InvalidationReasons.ModeChanged));
                    Assert.That(outcome.Result.Accepted, Is.True, operation + ": " + outcome.Describe());
                }

                if (outcome.Result.Accepted)
                {
                    previous = outcome.Result;
                }
            }

            Assert.That(sawSwitch, Is.True, "the sweep must contain a mode switch");
        }

        private static void RunSeed(uint seed, SequenceFamily family)
        {
            DerivationOptions options = DerivationOptions.Default;
            OperationSequence sequence = new OperationSequence(seed, family);
            DerivationResult? published = null;
            DerivationResult? incrementalBase = null;
            for (int step = 0; step < StepsPerSeed; step++)
            {
                string operation = sequence.ApplyNextOperation(step);
                DerivationSnapshot snapshot = sequence.Snapshot();
                string where = Where(seed, step, operation, sequence);

                DerivationResult oracle = DerivationOracle.Derive(
                    snapshot, sequence.Values, options, published);
                IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                    snapshot, sequence.Values, options, incrementalBase, null);
                DerivationResult incremental = outcome.Result;

                Assert.That(incremental.Accepted, Is.EqualTo(oracle.Accepted), where + ": acceptance differs.");
                Assert.That(incremental.Rejection, Is.EqualTo(oracle.Rejection), where + ": rejection kind differs.");
                Assert.That(incremental.DiagnosticCode, Is.EqualTo(oracle.DiagnosticCode), where + ": diagnostic differs.");

                if (incremental.Accepted)
                {
                    Assert.That(
                        DerivationProjection.SemanticsText(incremental),
                        Is.EqualTo(DerivationProjection.SemanticsText(oracle)),
                        where + ": effective values, supports or recipe hashes differ (P-023).");
                    AssertDecisionSetsEqual(incremental, oracle, where);
                    Assert.That(
                        incremental.Delta == null,
                        Is.EqualTo(oracle.Delta == null),
                        where + ": delta presence differs.");
                    if (incremental.Delta != null && oracle.Delta != null)
                    {
                        Assert.That(
                            DeltaText(oracle.Delta),
                            Is.EqualTo(DeltaText(incremental.Delta)),
                            where + ": the contribution delta differs (P-017).");
                    }

                    Assert.That(
                        outcome.Counters.CarriedTargets + outcome.Counters.DirtyTargets,
                        Is.EqualTo(snapshot.Targets.Count),
                        where + ": every target is either carried or dirty.");
                }

                if (oracle.Accepted)
                {
                    published = oracle;
                }

                if (incremental.Accepted)
                {
                    incrementalBase = incremental;
                }
            }
        }

        /// <summary>
        /// The two decision sets must be equal in both directions: the incremental engine may neither invent a
        /// decision the oracle cannot reproduce nor miss one it makes (P-026). Both key lists are built once and
        /// compared through one count check plus a set lookup, because the sweep runs this 25,000 times.
        /// </summary>
        private static void AssertDecisionSetsEqual(DerivationResult incremental, DerivationResult oracle, string where)
        {
            List<string> runtime = new List<string>(DerivationProjection.DecisionKeys(incremental));
            List<string> reference = new List<string>(DerivationProjection.DecisionKeys(oracle));
            Assert.That(runtime.Count, Is.EqualTo(reference.Count), where + ": decision count differs (P-026).");
            HashSet<string> known = new HashSet<string>(reference);
            for (int i = 0; i < runtime.Count; i++)
            {
                if (!known.Contains(runtime[i]))
                {
                    Assert.Fail(where + ": the incremental engine produced a decision the oracle cannot reproduce: "
                        + runtime[i]);
                }
            }
        }

        private static string Where(uint seed, int step, string operation, OperationSequence sequence) =>
            "seed " + seed.ToString(CultureInfo.InvariantCulture)
            + ", step " + step.ToString(CultureInfo.InvariantCulture)
            + " (" + operation + "; " + sequence.Describe() + ")";

        /// <summary>Canonical text of every explanation, so provenance equality is one assertion (P-026).</summary>
        private static string ExplanationText(DerivationResult result)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < result.Explanations.Count; i++)
            {
                DerivationExplanation explanation = result.Explanations[i];
                lines.Add(
                    explanation.Target.ToString() + "/" + explanation.Capability.ToString()
                    + ";" + explanation.Mode.ToString()
                    + ";stratum=" + explanation.Stratum.ToString(CultureInfo.InvariantCulture)
                    + ";recipe=" + explanation.RecipeHash.ToHex()
                    + ";slot=" + explanation.SlotHash.ToHex()
                    + ";scope=" + explanation.ScopePath.Count.ToString(CultureInfo.InvariantCulture)
                    + ";winners=" + explanation.Winners.Count.ToString(CultureInfo.InvariantCulture)
                    + ";shadowed=" + explanation.Shadowed.Count.ToString(CultureInfo.InvariantCulture)
                    + ";decisions=" + explanation.Decisions.Count.ToString(CultureInfo.InvariantCulture)
                    + ";capabilities=" + explanation.EffectiveCapabilities.Count.ToString(CultureInfo.InvariantCulture));
                for (int d = 0; d < explanation.Decisions.Count; d++)
                {
                    CandidateDecision decision = explanation.Decisions[d];
                    lines.Add(
                        "  " + decision.Provider.ToString() + "/" + decision.Rule.ToString()
                        + "#" + decision.OutputSlot.ToString(CultureInfo.InvariantCulture)
                        + ";" + decision.Status.ToString()
                        + ";" + decision.Reason.ToString()
                        + ";" + decision.ModeGate.ToString()
                        + ";depth=" + decision.ProviderDepth.ToString(CultureInfo.InvariantCulture));
                }
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines.ToArray());
        }

        private static string DeltaText(DerivationDelta delta)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < delta.Added.Count; i++)
            {
                lines.Add("+ " + delta.Added[i].ToString());
            }

            for (int i = 0; i < delta.Removed.Count; i++)
            {
                lines.Add("- " + delta.Removed[i].ToString());
            }

            for (int i = 0; i < delta.Changed.Count; i++)
            {
                lines.Add("~ " + delta.Changed[i].ToString());
            }

            for (int i = 0; i < delta.Slots.Count; i++)
            {
                EffectiveSlotChange change = delta.Slots[i];
                lines.Add(
                    "= " + change.Target.ToString() + "/" + change.Capability.ToString()
                    + "#" + change.Slot.ToString(CultureInfo.InvariantCulture)
                    + (change.Added ? " added" : change.Removed ? " removed" : change.Changed ? " changed" : " support")
                    + " lost=" + change.LostSupport.Count.ToString(CultureInfo.InvariantCulture)
                    + " support=" + change.Support.Count.ToString(CultureInfo.InvariantCulture));
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines.ToArray());
        }
    }
}
