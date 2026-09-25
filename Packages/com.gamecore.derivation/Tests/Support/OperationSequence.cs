// GameCore.Derivation tests — the seeded operation sequence both differential sweeps drive (GC-013, TEST-008).
//
// TEST-008: "Generate 50 fixed seeds, each with 500 operations chosen from mount, unmount, reconfigure, spawn,
// retire, reparent, exclusion change, provider replacement and mode switch. After each accepted publication,
// compare canonical effective capabilities and provenance against the reference evaluator."
//
// The sequence owner below produces that vocabulary over the two reference fixtures of 07. It is shared by the
// runtime-versus-oracle sweep (GC-006's promise, extended to 500 operations) and the incremental-versus-oracle
// sweep (GC-013), so both speak exactly the same operations.
//
// Two properties make the sweep usable as evidence rather than just long:
//
//   * **Reproducible.** Each step derives its operation from `Lcg(seed * 7919 + step + 1)`, so a failing step is
//     addressable from the seed and the step alone.
//   * **Bounded.** The world size is capped (`MaxTargets`, `MaxInstalls`), so the candidate population of one
//     derivation stays small and 500 operations stay linear. Without the cap the brute-force reference evaluator
//     would cost O(steps^2) per seed and the sweep would measure the oracle rather than the engine. The cap does
//     not shrink the vocabulary: every documented operation kind still occurs many times per seed.
//
// The random providers deliberately stay inside the accepted vocabulary: a card provider outputs either the
// `Additive` set-bonus slot or the `Replace` market binding, never the `Exclusive` draw policy, because two
// randomly overlapping `Exclusive` providers would reject every following step and the sweep would stop
// exercising the accepted path. The `Exclusive` conflict itself is covered by its own tests (GC-006's REF-C06 and
// the GC-013 `ReferenceMoveAndModeTests`).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;

namespace GameCore.Derivation.Tests
{
    /// <summary>Which reference composition a sequence drives (07 s2 cards or 07 s3 narrative).</summary>
    public enum SequenceFamily
    {
        Narrative = 0,
        Cards = 1,
    }

    /// <summary>What kind of provider one mounted installation is, so a reconfigure keeps its rule's shape.</summary>
    public enum ProviderKind
    {
        /// <summary>A chapter provider: the six-rule chain with a tag payload and hook ordering keys.</summary>
        Chapter = 0,

        /// <summary>A card scoring provider: one `Additive` set-bonus slot with an Int32 payload.</summary>
        Scoring = 1,

        /// <summary>A card market provider: the `Replace` market binding with a tag payload.</summary>
        Market = 2,
    }

    /// <summary>One seeded operation sequence over one reference fixture (TEST-008).</summary>
    public sealed class OperationSequence
    {
        /// <summary>Targets a sequence keeps alive at once; see the file header for why the world is bounded.</summary>
        public const int MaxTargets = 8;

        /// <summary>Installations a sequence keeps mounted at once.</summary>
        public const int MaxInstalls = 5;

        /// <summary>Operation kinds the sequence can apply; TEST-008's vocabulary.</summary>
        public const int OperationKinds = 13;

        private readonly uint seed;
        private readonly FixtureBuilder builder;
        private readonly List<string> scopeNames = new List<string>();
        private readonly List<string> targetNames = new List<string>();
        private readonly List<string> providerNames = new List<string>();
        private readonly Dictionary<string, string> providerRules = new Dictionary<string, string>();
        private readonly Dictionary<string, string> providerScopes = new Dictionary<string, string>();
        private readonly Dictionary<string, ProviderKind> providerKinds = new Dictionary<string, ProviderKind>();
        private readonly List<string> capabilityNames = new List<string>();
        private readonly string scopeForExclusion;
        private int providerCounter;
        private int spawnCounter;
        private int replaceCounter;
        private bool scopeMoved;
        private bool exclusionApplied;
        private bool isolationApplied;
        private PropagationMode mode = PropagationMode.Automatic;
        private CompositionRevision revision = CompositionRevision.First;
        private AssemblyEpoch epoch = AssemblyEpoch.First;

        public OperationSequence(uint seed, SequenceFamily family)
        {
            this.seed = seed;
            Family = family;
            if (family == SequenceFamily.Narrative)
            {
                builder = NarrativeComposition.Builder();
                Values = NarrativeComposition.ValueSource();
                scopeNames.Add(NarrativeComposition.Village);
                scopeNames.Add(NarrativeComposition.Grove);
                scopeNames.Add(NarrativeComposition.Museum);
                scopeNames.Add(NarrativeComposition.Harbor);
                scopeNames.Add(NarrativeComposition.ChapterOne);
                scopeNames.Add(NarrativeComposition.ChapterTwo);
                targetNames.Add(NarrativeComposition.Mara);
                targetNames.Add(NarrativeComposition.GateEast);
                targetNames.Add(NarrativeComposition.EncounterOak);
                targetNames.Add(NarrativeComposition.Display);
                targetNames.Add(NarrativeComposition.Sailor);
                capabilityNames.Add(NarrativeComposition.ConversationBinding);
                capabilityNames.Add(NarrativeComposition.GateConditionBinding);
                scopeForExclusion = NarrativeComposition.Village;
                RegisterProvider(
                    NarrativeComposition.ChapterOneInstall,
                    NarrativeComposition.ChapterOne,
                    "chapter-one",
                    NarrativeComposition.DialogueRule("chapter-one"),
                    ProviderKind.Chapter);
                RegisterProvider(
                    NarrativeComposition.ChapterTwoInstall,
                    NarrativeComposition.ChapterTwo,
                    "chapter-two",
                    NarrativeComposition.DialogueRule("chapter-two"),
                    ProviderKind.Chapter);
            }
            else
            {
                builder = CardComposition.Builder();
                Values = CardComposition.ValueSource();
                scopeNames.Add(CardComposition.TableArea);
                scopeNames.Add(CardComposition.LeagueA);
                scopeNames.Add(CardComposition.LeagueB);
                scopeNames.Add(CardComposition.Spectators);
                scopeNames.Add(CardComposition.SeatAScope);
                scopeNames.Add(CardComposition.SeatBScope);
                scopeNames.Add(CardComposition.SeatCScope);
                scopeNames.Add(CardComposition.Practice);
                targetNames.Add(CardComposition.SeatA);
                targetNames.Add(CardComposition.SeatB);
                targetNames.Add(CardComposition.SeatC);
                targetNames.Add(CardComposition.PracticeSeat);
                targetNames.Add(CardComposition.Scoreboard);
                targetNames.Add(CardComposition.TableOne);
                capabilityNames.Add(CardComposition.SetBonus);
                capabilityNames.Add(CardComposition.MarketTarget);
                scopeForExclusion = CardComposition.LeagueA;
                RegisterProvider(
                    CardComposition.FestivalScoring,
                    CardComposition.LeagueA,
                    "festival-scoring",
                    CardComposition.FestivalScoring + CardComposition.SetBonusSuffix,
                    ProviderKind.Scoring);
                RegisterProvider(
                    CardComposition.QuietScoring,
                    CardComposition.LeagueB,
                    "quiet-scoring",
                    CardComposition.QuietScoring + CardComposition.SetBonusSuffix,
                    ProviderKind.Scoring);
            }
        }

        public SequenceFamily Family { get; }

        public FixtureValueSource Values { get; }

        /// <summary>The committed propagation mode of the current step (P-013).</summary>
        public PropagationMode Mode => mode;

        /// <summary>The revision the next snapshot is built at (P-006).</summary>
        public CompositionRevision Revision => revision;

        /// <summary>The assembly epoch the next snapshot is built at (P-006).</summary>
        public AssemblyEpoch Epoch => epoch;

        /// <summary>The current composition as an immutable derivation input.</summary>
        public DerivationSnapshot Snapshot() => builder.Build(mode, revision, epoch).ToSnapshot();

        /// <summary>
        /// Applies one seeded operation and advances the revision and epoch, because every operation is an
        /// ordinary composition step and the two counters move together (P-006). The returned text names the
        /// operation, so a failing seed prints what it did.
        /// </summary>
        public string ApplyNextOperation(int step)
        {
            Lcg random = new Lcg(unchecked((seed * 7919U) + (uint)step + 1U));
            string applied;
            switch (random.Next(OperationKinds))
            {
                case 0:
                    applied = SpawnTarget(random);
                    break;
                case 1:
                    applied = RetireTarget(random);
                    break;
                case 2:
                    applied = MountProvider(random);
                    break;
                case 3:
                    applied = UnmountProvider(random);
                    break;
                case 4:
                    applied = ReconfigurePayload(random);
                    break;
                case 5:
                    applied = SwitchMode();
                    break;
                case 6:
                    applied = ChangeExclusion(random);
                    break;
                case 7:
                    applied = ChangeLifecycle(random);
                    break;
                case 8:
                    applied = MoveTarget(random);
                    break;
                case 9:
                    applied = ReparentScope();
                    break;
                case 10:
                    applied = PatchDescriptor(random);
                    break;
                case 11:
                    applied = ReplaceProvider(random);
                    break;
                default:
                    applied = AddScopeGrant(random);
                    break;
            }

            revision = new CompositionRevision(revision.Value + 1UL);
            epoch = new AssemblyEpoch(epoch.Value + 1UL);
            return applied;
        }

        /// <summary>A human-readable description of the sequence's current shape, for a failing assertion.</summary>
        public string Describe() =>
            "family=" + Family.ToString()
            + ";targets=" + targetNames.Count.ToString(CultureInfo.InvariantCulture)
            + ";installs=" + providerNames.Count.ToString(CultureInfo.InvariantCulture)
            + ";mode=" + mode.ToString();

        private string SpawnTarget(Lcg random)
        {
            if (targetNames.Count >= MaxTargets)
            {
                return "spawn-skipped(bound)";
            }

            string name = "seq.s" + seed.ToString(CultureInfo.InvariantCulture) + ".spawn" + spawnCounter.ToString(CultureInfo.InvariantCulture);
            spawnCounter++;
            string scope = scopeNames[random.Next(scopeNames.Count)];
            string recipe = Family == SequenceFamily.Narrative
                ? (random.NextBool() ? NarrativeComposition.VillagerRecipe : NarrativeComposition.QuestGateRecipe)
                : CardComposition.CardSeatRecipe;
            builder.Target(name, scope, recipe);
            targetNames.Add(name);
            return "spawn(" + name + "@" + scope + ")";
        }

        private string RetireTarget(Lcg random)
        {
            if (targetNames.Count <= 2)
            {
                return "retire-skipped(floor)";
            }

            string name = targetNames[random.Next(targetNames.Count)];
            builder.RemoveTarget(name);
            targetNames.Remove(name);
            return "retire(" + name + ")";
        }

        private string MountProvider(Lcg random)
        {
            if (providerNames.Count >= MaxInstalls)
            {
                return "mount-skipped(bound)";
            }

            providerCounter++;
            string name = "seq.s" + seed.ToString(CultureInfo.InvariantCulture) + ".p" + providerCounter.ToString(CultureInfo.InvariantCulture);
            string tag = "seq-" + seed.ToString(CultureInfo.InvariantCulture) + "-" + providerCounter.ToString(CultureInfo.InvariantCulture);
            string scope = scopeNames[random.Next(scopeNames.Count)];
            InstallationState state = random.NextBool()
                ? InstallationState.Active
                : InstallationState.WaitingForDependencies;

            if (Family == SequenceFamily.Narrative)
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), ChapterRules(tag), state);
                RegisterProvider(name, scope, tag, NarrativeComposition.DialogueRule(tag), ProviderKind.Chapter);
            }
            else if (random.NextBool())
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), CardComposition.ScoringRules(name, random.NextInclusive(1, 3)), state);
                RegisterProvider(name, scope, tag, name + CardComposition.SetBonusSuffix, ProviderKind.Scoring);
            }
            else
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), CardComposition.MarketRules(), state);
                RegisterProvider(name, scope, tag, CardComposition.MarketTargetRule, ProviderKind.Market);
            }

            return "mount(" + name + "@" + scope + "," + state.ToString() + ")";
        }

        private string UnmountProvider(Lcg random)
        {
            if (providerNames.Count <= 1)
            {
                return "unmount-skipped(floor)";
            }

            string name = providerNames[random.Next(providerNames.Count)];
            RemoveProvider(name);
            return "unmount(" + name + ")";
        }

        private string ReconfigurePayload(Lcg random)
        {
            if (providerNames.Count == 0)
            {
                return "reconfigure-skipped(empty)";
            }

            string name = providerNames[random.Next(providerNames.Count)];
            FrozenPayload payload = providerKinds[name] == ProviderKind.Scoring
                ? FixturePayload.Int32(random.NextInclusive(1, 9))
                : FixturePayload.Tag(name + ".payload-" + random.NextInclusive(0, 9).ToString(CultureInfo.InvariantCulture));
            builder.ReplaceRulePayload(name, providerRules[name], payload);
            return "reconfigure(" + name + "/" + providerRules[name] + ")";
        }

        private string SwitchMode()
        {
            mode = mode == PropagationMode.Automatic ? PropagationMode.Conservative : PropagationMode.Automatic;
            return "switchMode(" + mode.ToString() + ")";
        }

        /// <summary>
        /// An exclusion change on one scope: the documented vocabulary of TEST-008 and 02 s7. The edit toggles
        /// between adding and clearing an exclusion, so both directions of the change occur (P-016).
        /// </summary>
        private string ChangeExclusion(Lcg random)
        {
            string capability = capabilityNames[random.Next(capabilityNames.Count)];
            if (exclusionApplied)
            {
                builder.ReplaceScopeExclusions(scopeForExclusion, null);
                exclusionApplied = false;
                return "clearExclusions(" + scopeForExclusion + ")";
            }

            builder.AddScopeExclusion(
                scopeForExclusion,
                new ExclusionRule(
                    ExclusionTargetKind.Capability,
                    FixtureIds.Capability(capability).Value,
                    FixtureIds.Scope(scopeForExclusion),
                    default(TargetId),
                    random.NextBool()));
            exclusionApplied = true;
            return "exclude(" + capability + "@" + scopeForExclusion + ")";
        }

        private string ChangeLifecycle(Lcg random)
        {
            if (providerNames.Count == 0)
            {
                return "lifecycle-skipped(empty)";
            }

            string name = providerNames[random.Next(providerNames.Count)];
            InstallationState state = random.NextBool()
                ? InstallationState.Active
                : InstallationState.WaitingForDependencies;
            builder.ReplaceInstallState(name, state);
            return "lifecycle(" + name + "," + state.ToString() + ")";
        }

        private string MoveTarget(Lcg random)
        {
            string name = targetNames[random.Next(targetNames.Count)];
            string scope = scopeNames[random.Next(scopeNames.Count)];
            builder.MoveTarget(name, scope);
            return "moveTarget(" + name + "@" + scope + ")";
        }

        private string ReparentScope()
        {
            // The reference compositions' documented move: 07 s3.4's "Reparent Village under ChapterTwo" and
            // 07 s2.4's "Reparent SeatA from LeagueA to LeagueB". Each call toggles, so both directions occur.
            scopeMoved = !scopeMoved;
            if (Family == SequenceFamily.Narrative)
            {
                string parent = scopeMoved ? NarrativeComposition.ChapterTwo : NarrativeComposition.ChapterOne;
                builder.MoveScope(NarrativeComposition.Village, parent);
                return "reparentScope(village@" + parent + ")";
            }

            string cardParent = scopeMoved ? CardComposition.LeagueB : CardComposition.LeagueA;
            builder.MoveScope(CardComposition.SeatAScope, cardParent);
            return "reparentScope(seat-a-scope@" + cardParent + ")";
        }

        private string PatchDescriptor(Lcg random)
        {
            string name = targetNames[random.Next(targetNames.Count)];
            string capability = capabilityNames[random.Next(capabilityNames.Count)];
            builder.AddTargetCapability(name, capability);
            return "patchDescriptor(" + name + "+" + capability + ")";
        }

        private string ReplaceProvider(Lcg random)
        {
            if (providerNames.Count == 0)
            {
                return "replace-skipped(empty)";
            }

            string old = providerNames[random.Next(providerNames.Count)];
            string scope = providerScopes[old];
            string tag = providerRules[old];
            ProviderKind kind = providerKinds[old];
            RemoveProvider(old);

            replaceCounter++;
            string name = "seq.s" + seed.ToString(CultureInfo.InvariantCulture) + ".rp" + replaceCounter.ToString(CultureInfo.InvariantCulture);
            if (kind == ProviderKind.Chapter)
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), ChapterRules(tag), InstallationState.Active);
                RegisterProvider(name, scope, tag, NarrativeComposition.DialogueRule(tag), ProviderKind.Chapter);
            }
            else if (kind == ProviderKind.Scoring)
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), CardComposition.ScoringRules(name, random.NextInclusive(1, 3)), InstallationState.Active);
                RegisterProvider(name, scope, tag, name + CardComposition.SetBonusSuffix, ProviderKind.Scoring);
            }
            else
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), CardComposition.MarketRules(), InstallationState.Active);
                RegisterProvider(name, scope, tag, CardComposition.MarketTargetRule, ProviderKind.Market);
            }

            return "replaceProvider(" + old + "->" + name + "@" + scope + ")";
        }

        private string AddScopeGrant(Lcg random)
        {
            // A Conservative import or boundary edit on one scope: data that only changes eligibility in
            // Conservative, so the sweep exercises both the gated and the ungated path (P-013, P-016).
            string scope = scopeNames[random.Next(scopeNames.Count)];
            if (Family == SequenceFamily.Narrative)
            {
                string capability = capabilityNames[random.Next(capabilityNames.Count)];
                string provider = providerNames[random.Next(providerNames.Count)];
                builder.AddScopeImport(scope, capability, provider);
                return "addImport(" + capability + "@" + scope + "<-" + provider + ")";
            }

            if (isolationApplied)
            {
                builder.ReplaceScopeIsolation(scope, new IsolationSet(false, null));
                isolationApplied = false;
                return "clearIsolation(" + scope + ")";
            }

            string isolated = capabilityNames[random.Next(capabilityNames.Count)];
            builder.AddScopeIsolation(scope, isolated);
            isolationApplied = true;
            return "isolate(" + isolated + "@" + scope + ")";
        }

        /// <summary>
        /// The two stratum-0 rules of one randomly mounted chapter: the dialogue binding and the gate condition.
        /// The reference fixture's own two chapters declare the full six-rule chain (including the `Ordered` hook
        /// slots and the strata-1/2 chain), so the sweep exercises those; a random provider declares the subset to
        /// bound the rule count the deliberately slow reference evaluator has to walk 25,000 times, which is what
        /// keeps 500 operations per seed practical without changing the operation vocabulary.
        /// </summary>
        private static IReadOnlyList<DerivationRule> ChapterRules(string chapterTag) =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    NarrativeComposition.DialogueRule(chapterTag),
                    NarrativeComposition.ConversationBinding,
                    NarrativeComposition.BindingStratum,
                    1U,
                    FixtureBuilder.Selector(NarrativeComposition.VillagerRecipe),
                    FixtureIds.Key(NarrativeComposition.AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag(chapterTag + ".dialogue-graph")),
                FixtureBuilder.Rule(
                    NarrativeComposition.GateRule(chapterTag),
                    NarrativeComposition.GateConditionBinding,
                    NarrativeComposition.BindingStratum,
                    1U,
                    FixtureBuilder.Selector(NarrativeComposition.QuestGateRecipe),
                    FixtureIds.Key(NarrativeComposition.AlwaysPredicate),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag(chapterTag + ".gate-condition")),
            };

        private void RemoveProvider(string name)
        {
            builder.RemoveInstall(name);
            providerNames.Remove(name);
            providerRules.Remove(name);
            providerScopes.Remove(name);
            providerKinds.Remove(name);
        }

        private void RegisterProvider(
            string name,
            string scope,
            string tag,
            string ruleName,
            ProviderKind kind)
        {
            providerNames.Add(name);
            providerRules[name] = ruleName;
            providerScopes[name] = scope;
            providerKinds[name] = kind;
        }
    }
}
