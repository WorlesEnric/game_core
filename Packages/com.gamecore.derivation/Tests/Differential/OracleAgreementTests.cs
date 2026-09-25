// GameCore.Derivation tests — runtime versus full-recompute oracle (P-023, TEST-008).
//
// P-023: "An independent full recomputation oracle MUST match the incremental effective assembly/provenance for
// randomized operation sequences." TEST-008 asks for 50 fixed seeds of a long operation sequence, comparing the
// canonical effective capabilities and provenance after every accepted publication.
//
// What is compared after every step: acceptance, rejection kind, the semantic projection (effective capability
// sets, slot values, support identities, recipe hashes) and the decision set (the engine may not invent a decision
// the index-free oracle cannot reproduce). A failing seed prints the seed and step.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class OracleAgreementTests
    {
        /// <summary>Fixed seeds of the sequence sweep; the count is part of the witness.</summary>
        private const int SeedCount = 50;

        /// <summary>Operations per seed. Small enough for a fast unit run, long enough to cross every code path.</summary>
        private const int StepsPerSeed = 40;

        [Test]
        public void TheRuntimeAndTheOracleAgreeOnEverySeedAndStep()
        {
            for (uint seed = 1U; seed <= SeedCount; seed++)
            {
                SequenceState sequence = new SequenceState(seed);
                for (int step = 0; step < StepsPerSeed; step++)
                {
                    sequence.ApplyNextOperation(step);
                    Compare(seed, step, sequence);
                }
            }
        }

        [Test]
        public void TheRuntimeAndTheOracleAgreeOnRandomCompositionsWithoutAnyOperationSequence()
        {
            IReadOnlyList<uint> seeds = RandomComposition.Seeds(SeedCount, 1000U);
            for (int i = 0; i < seeds.Count; i++)
            {
                FixtureComposition parts = RandomComposition.Build(seeds[i]);
                DerivationSnapshot snapshot = parts.ToSnapshot();
                FixtureValueSource values = RandomComposition.ValueSource();

                DerivationResult runtime = DerivationEngine.Derive(snapshot, values, DerivationOptions.Default, null);
                DerivationResult oracle = DerivationOracle.Derive(snapshot, values, DerivationOptions.Default, null);

                AssertAgreement(seeds[i], -1, runtime, oracle);
            }
        }

        [Test]
        public void TheOracleAndTheEngineAgreeOnTheReferenceCompositions()
        {
            FixtureValueSource narrative = NarrativeComposition.ValueSource();
            FixtureBuilder narrativeBuilder = NarrativeComposition.Builder();

            foreach (PropagationMode mode in new[] { PropagationMode.Automatic, PropagationMode.Conservative })
            {
                DerivationSnapshot snapshot = narrativeBuilder
                    .Build(mode, new CompositionRevision(1UL), AssemblyEpoch.First)
                    .ToSnapshot();

                AssertAgreement(
                    0U,
                    0,
                    DerivationEngine.Derive(snapshot, narrative, DerivationOptions.Default, null),
                    DerivationOracle.Derive(snapshot, narrative, DerivationOptions.Default, null));
            }

            FixtureValueSource cards = CardComposition.ValueSource();
            FixtureBuilder cardsBuilder = CardComposition.Builder(mountNestedFestival: true);
            DerivationSnapshot cardSnapshot = cardsBuilder
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();

            AssertAgreement(
                0U,
                0,
                DerivationEngine.Derive(cardSnapshot, cards, DerivationOptions.Default, null),
                DerivationOracle.Derive(cardSnapshot, cards, DerivationOptions.Default, null));
        }

        [Test]
        public void ADeltaComputedFromEitherEngineIsTheSame()
        {
            FixtureBuilder builder = NarrativeComposition.Builder();
            FixtureValueSource values = NarrativeComposition.ValueSource();
            DerivationSnapshot before = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();

            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, values, DerivationOptions.Default, null));

            builder.RemoveInstall(NarrativeComposition.ChapterOneInstall);
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            DerivationResult runtime = DerivationEngine.Derive(after, values, DerivationOptions.Default, published);
            DerivationResult oracle = DerivationOracle.Derive(after, values, DerivationOptions.Default, published);

            AssertAgreement(0U, 0, runtime, oracle);
            Assert.That(runtime.Delta, Is.Not.Null);
            Assert.That(oracle.Delta, Is.Not.Null);
            Assert.That(
                DeltaText(oracle.Delta!),
                Is.EqualTo(DeltaText(runtime.Delta!)),
                "Both engines must derive the same retraction delta (TEST-008).");
            Assert.That(runtime.Delta!.Removed.Count, Is.GreaterThan(0), "Unmounting the chapter retracts its keys.");
        }

        private static void Compare(uint seed, int step, SequenceState sequence)
        {
            DerivationSnapshot snapshot = sequence.Snapshot();
            DerivationResult runtime = DerivationEngine.Derive(
                snapshot, sequence.Values, DerivationOptions.Default, sequence.Previous);
            DerivationResult oracle = DerivationOracle.Derive(
                snapshot, sequence.Values, DerivationOptions.Default, sequence.Previous);

            AssertAgreement(seed, step, runtime, oracle);
            if (runtime.Accepted)
            {
                sequence.Previous = runtime;
            }
        }

        private static void AssertAgreement(uint seed, int step, DerivationResult runtime, DerivationResult oracle)
        {
            string where = "seed " + seed + ", step " + step;

            Assert.That(runtime.Accepted, Is.EqualTo(oracle.Accepted), where + ": acceptance differs.");
            Assert.That(runtime.Rejection, Is.EqualTo(oracle.Rejection), where + ": rejection kind differs.");
            Assert.That(
                runtime.DiagnosticCode,
                Is.EqualTo(oracle.DiagnosticCode),
                where + ": diagnostic code differs.");

            if (!runtime.Accepted)
            {
                return;
            }

            Assert.That(
                DerivationProjection.SemanticsText(oracle),
                Is.EqualTo(DerivationProjection.SemanticsText(runtime)),
                where + ": effective values, supports or recipe hashes differ.");
            Assert.That(
                DerivationProjection.MissingDecisions(runtime, oracle),
                Is.Empty,
                where + ": the engine produced a decision the oracle cannot reproduce.");

            DerivationDelta? runtimeDelta = runtime.Delta;
            DerivationDelta? oracleDelta = oracle.Delta;
            if (runtimeDelta == null || oracleDelta == null)
            {
                Assert.That(runtimeDelta == null, Is.EqualTo(oracleDelta == null), where + ": delta presence differs.");
                return;
            }

            Assert.That(DeltaText(oracleDelta), Is.EqualTo(DeltaText(runtimeDelta)), where + ": delta differs.");
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
                    "= " + change.Target.ToString() + "/" + change.Capability.ToString() + "#" + change.Slot
                    + (change.Added ? " added" : change.Removed ? " removed" : change.Changed ? " changed" : " support")
                    + " lost=" + change.LostSupport.Count
                    + " support=" + change.Support.Count);
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        /// <summary>
        /// One scripted operation sequence over the narrative fixture. Each operation is chosen from the seed and
        /// step through the same LCG, so the whole sequence is reproducible and a failing step is addressable.
        /// </summary>
        private sealed class SequenceState
        {
            private readonly uint seed;
            private readonly FixtureBuilder builder;
            private readonly List<string> scopeNames = new List<string>();
            private readonly List<string> targetNames = new List<string>();
            private readonly List<string> providerNames = new List<string>();
            private readonly Dictionary<string, string> providerTags = new Dictionary<string, string>();
            private int providerCounter;
            private PropagationMode mode = PropagationMode.Automatic;
            private CompositionRevision revision = CompositionRevision.First;
            private AssemblyEpoch epoch = AssemblyEpoch.First;
            private int spawnCounter;

            public SequenceState(uint seed)
            {
                this.seed = seed;
                builder = NarrativeComposition.Builder();
                Values = NarrativeComposition.ValueSource();

                scopeNames.Add(NarrativeComposition.Village);
                scopeNames.Add(NarrativeComposition.Grove);
                scopeNames.Add(NarrativeComposition.Museum);
                scopeNames.Add(NarrativeComposition.Harbor);
                scopeNames.Add(NarrativeComposition.ChapterOne);
                scopeNames.Add(NarrativeComposition.StoryWorld);

                targetNames.Add(NarrativeComposition.Mara);
                targetNames.Add(NarrativeComposition.GateEast);
                targetNames.Add(NarrativeComposition.EncounterOak);
                targetNames.Add(NarrativeComposition.Display);
                targetNames.Add(NarrativeComposition.Sailor);

                providerNames.Add(NarrativeComposition.ChapterOneInstall);
                providerNames.Add(NarrativeComposition.ChapterTwoInstall);
                providerTags.Add(NarrativeComposition.ChapterOneInstall, "chapter-one");
                providerTags.Add(NarrativeComposition.ChapterTwoInstall, "chapter-two");
            }

            public FixtureValueSource Values { get; }

            public DerivationResult? Previous { get; set; }

            public DerivationSnapshot Snapshot() =>
                builder.Build(mode, revision, epoch).ToSnapshot();

            /// <summary>Applies one seeded operation. Mounts are only used for already-declared capabilities.</summary>
            public void ApplyNextOperation(int step)
            {
                Lcg random = new Lcg(unchecked((seed * 7919U) + (uint)step + 1U));
                switch (random.Next(10))
                {
                    case 0:
                        Spawn(random);
                        break;
                    case 1:
                        Retire(random);
                        break;
                    case 2:
                        MountProvider(random);
                        break;
                    case 3:
                        UnmountProvider(random);
                        break;
                    case 4:
                        Reconfigure(random);
                        break;
                    case 5:
                        SwitchMode();
                        break;
                    case 6:
                        ExcludeCapability(random);
                        break;
                    case 7:
                        ChangeLifecycle(random);
                        break;
                    case 8:
                        MoveTarget(random);
                        break;
                    default:
                        AddImport(random);
                        break;
                }

                // Every operation is an ordinary composition step: the version domains advance together (P-006).
                revision = new CompositionRevision(revision.Value + 1UL);
                epoch = new AssemblyEpoch(epoch.Value + 1UL);
            }

            private void Spawn(Lcg random)
            {
                string name = "seq.s" + seed + ".spawn" + spawnCounter++;
                string scope = scopeNames[random.Next(scopeNames.Count)];
                string recipe = random.NextBool()
                    ? NarrativeComposition.VillagerRecipe
                    : NarrativeComposition.QuestGateRecipe;
                builder.Target(name, scope, recipe);
                targetNames.Add(name);
            }

            private void Retire(Lcg random)
            {
                if (targetNames.Count <= 1)
                {
                    return;
                }

                string name = targetNames[random.Next(targetNames.Count)];
                builder.RemoveTarget(name);
                targetNames.Remove(name);
            }

            private void MountProvider(Lcg random)
            {
                providerCounter++;
                string name = "seq.s" + seed + ".provider" + providerCounter;
                string tag = "chapter-seq" + providerCounter;
                string scope = scopeNames[random.Next(scopeNames.Count)];
                builder.Install(
                    name,
                    scope,
                    random.NextInclusive(-1, 1),
                    NarrativeComposition.Rules(tag),
                    state: InstallationState.Active);
                NarrativeComposition.RegisterHookKeys(builder, tag);
                providerNames.Add(name);
                providerTags.Add(name, tag);
            }

            private void UnmountProvider(Lcg random)
            {
                if (providerNames.Count <= 1)
                {
                    return;
                }

                string name = providerNames[random.Next(providerNames.Count)];
                builder.RemoveInstall(name);
                providerNames.Remove(name);
            }

            private void Reconfigure(Lcg random)
            {
                string provider = providerNames[random.Next(providerNames.Count)];
                string tag = providerTags[provider];
                builder.ReplaceRulePayload(
                    provider,
                    NarrativeComposition.DialogueRule(tag),
                    FixturePayload.Tag(tag + ".reconfigured-" + random.NextInclusive(0, 9)));
            }

            private void SwitchMode() => mode = mode == PropagationMode.Automatic
                ? PropagationMode.Conservative
                : PropagationMode.Automatic;

            private void ExcludeCapability(Lcg random)
            {
                string scope = scopeNames[random.Next(scopeNames.Count)];
                builder.AddScopeExclusion(
                    scope,
                    new ExclusionRule(
                        ExclusionTargetKind.Capability,
                        FixtureIds.Capability(NarrativeComposition.ConversationBinding).Value,
                        FixtureIds.Scope(scope),
                        default(TargetId),
                        random.NextBool()));
            }

            private void ChangeLifecycle(Lcg random)
            {
                string provider = providerNames[random.Next(providerNames.Count)];
                builder.ReplaceInstallState(
                    provider,
                    random.NextBool() ? InstallationState.Active : InstallationState.WaitingForDependencies);
            }

            private void MoveTarget(Lcg random)
            {
                string target = targetNames[random.Next(targetNames.Count)];
                string scope = scopeNames[random.Next(scopeNames.Count)];
                builder.MoveTarget(target, scope);
            }

            private void AddImport(Lcg random)
            {
                string scope = scopeNames[random.Next(scopeNames.Count)];
                builder.AddScopeImport(
                    scope,
                    NarrativeComposition.ConversationBinding,
                    random.NextBool() ? NarrativeComposition.ChapterOneInstall : NarrativeComposition.ChapterTwoInstall);
            }


        }
    }
}
