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
//     addressable from the seed and the step alone, and the printed message names both.
//   * **Bounded.** The world size is capped (`MaxTargets`, `MaxInstalls`), so the candidate population of one
//     derivation stays small and 500 operations stay linear. Without the cap the brute-force reference evaluator
//     would cost O(steps^2) per seed and the sweep would measure the oracle rather than the engine. The cap
//     changes nothing about the operation vocabulary; every kind of operation still occurs many times per seed.
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

    /// <summary>How one provider's rule payload is reconfigured, so a reconfigure keeps the rule's shape.</summary>
    public enum PayloadKind
    {
        Tag = 0,
        Int32 = 1,
    }

    /// <summary>One seeded operation sequence over one reference fixture (TEST-008).</summary>
    public sealed class OperationSequence
    {
        /// <summary>Targets a sequence keeps alive at once; see the file header for why the world is bounded.</summary>
        public const int MaxTargets = 8;

        /// <summary>Installations a sequence keeps mounted at once.</summary>
        public const int MaxInstalls = 3;

        /// <summary>Operation kinds the sequence can apply; the fixpoint of TEST-008's vocabulary plus overrides.</summary>
        public const int OperationKinds = 13;

        private readonly uint seed;
        private readonly FixtureBuilder builder;
        private readonly List<string> scopeNames = new List<string>();
        private readonly List<string> targetNames = new List<string>();
        private readonly List<string> providerNames = new List<string>();
        private readonly Dictionary<string, string> providerTags = new Dictionary<string, string>();
        private readonly Dictionary<string, string> providerRuleNames = new Dictionary<string, string>();
        private readonly Dictionary<string, string> providerScopes = new Dictionary<string, string>();
        private readonly Dictionary<string, PayloadKind> providerPayloads = new Dictionary<string, PayloadKind>();
        private readonly string rootScope;
        private int providerCounter;
        private int spawnCounter;
        private int replaceCounter;
        private bool scopeMoved;
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
                rootScope = NarrativeComposition.StoryWorld;
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
                RegisterProvider(
                    NarrativeComposition.ChapterOneInstall,
                    NarrativeComposition.ChapterOne,
                    "chapter-one",
                    NarrativeComposition.DialogueRule("chapter-one"),
                    PayloadKind.Tag);
                RegisterProvider(
                    NarrativeComposition.ChapterTwoInstall,
                    NarrativeComposition.ChapterTwo,
                    "chapter-two",
                    NarrativeComposition.DialogueRule("chapter-two"),
                    PayloadKind.Tag);
            }
            else
            {
                builder = CardComposition.Builder();
                Values = CardComposition.ValueSource();
                rootScope = CardComposition.Match;
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
                RegisterProvider(
                    CardComposition.FestivalScoring,
                    CardComposition.LeagueA,
                    "festival-scoring",
                    CardComposition.FestivalScoring + CardComposition.SetBonusSuffix,
                    PayloadKind.Int32);
                RegisterProvider(
                    CardComposition.QuietScoring,
                    CardComposition.LeagueB,
                    "quiet-scoring",
                    CardComposition.QuietScoring + CardComposition.SetBonusSuffix,
                    PayloadKind.Int32);
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
            int kind = random.Next(OperationKinds);
            string applied;
            switch (kind)
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
                    applied = ExcludeCapability(random);
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
                    applied = AddGrantOrOverride(random);
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

            string name = "seq.s" + seed.ToString(CultureInfo.InvariantCulture) + ".spawn" + spawnCounter++;
            string scope = scopeNames[random.Next(scopeNames.Count)];
            string recipe = Family == SequenceFamily.Narrative
                ? (random.NextBool()
                    ? NarrativeComposition.VillagerRecipe
                    : NarrativeComposition.QuestGateRecipe)
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
            int index = Family == SequenceFamily.Narrative ? random.Next(2) : random.Next(3);
            InstallationState state = random.NextBool() ? InstallationState.Active : InstallationState.WaitingForDependencies;
            if (Family == SequenceFamily.Narrative)
            {
                IReadOnlyList<DerivationRule> rules = NarrativeComposition.Rules(tag);
                builder.Install(name, scope, random.NextInclusive(-1, 1), rules, state);
                NarrativeComposition.RegisterHookKeys(builder, tag);
                RegisterProvider(name, scope, tag, NarrativeComposition.DialogueRule(tag), PayloadKind.Tag);
            }
            else if (index == 0)
            {
                builder.Install(
                    name,
                    scope,
                    random.NextInclusive(-1, 1),
                    CardComposition.ScoringRules(name, random.NextInclusive(1, 3)),
                    state);
                RegisterProvider(name, scope, tag, name + CardComposition.SetBonusSuffix, PayloadKind.Int32);
            }
            else if (index == 1)
            {
                builder.Install(
                    name,
                    scope,
                    random.NextInclusive(-1, 1),
                    CardComposition.DrawPolicyRules(name),
                    state);
                RegisterProvider(name, scope, tag, name + CardComposition.DrawPolicySuffix, PayloadKind.Tag);
            }
            else
            {
                builder.Install(
                    name,
                    scope,
                    random.NextInclusive(-1, 1),
                    CardComposition.MarketRules(),
                    state);
                RegisterProvider(name, scope, tag, CardComposition.MarketTargetRule, PayloadKind.Tag);
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
            builder.RemoveInstall(name);
            providerNames.Remove(name);
            return "unmount(" + name + ")";
        }

        private string ReconfigurePayload(Lcg random)
        {
            if (providerNames.Count == 0)
            {
                return "reconfigure-skipped(empty)";
            }

            string name = providerNames[random.Next(providerNames.Count)];
            string ruleName = providerRuleNames[name];
            PayloadKind kind = providerPayloads[name];
            FrozenPayload payload = kind == PayloadKind.Int32
                ? FixturePayload.Int32(random.NextInclusive(1, 9))
                : FixturePayload.Tag(name + ".payload-" + random.NextInclusive(0, 9).ToString(CultureInfo.InvariantCulture));
            builder.ReplaceRulePayload(name, ruleName, payload);
            return "reconfigure(" + name + "/" + ruleName + ")";
        }

        private string SwitchMode()
        {
            mode = mode == PropagationMode.Automatic ? PropagationMode.Conservative : PropagationMode.Automatic;
            return "switchMode(" + mode.ToString() + ")";
        }

        private string ExcludeCapability(Lcg random)
        {
            string scope = scopeNames[random.Next(scopeNames.Count)];
            string capability = Family == SequenceFamily.Narrative
                ? NarrativeComposition.ConversationBinding
                : CardComposition.SetBonus;
            builder.AddScopeExclusion(
                scope,
                new ExclusionRule(
                    ExclusionTargetKind.Capability,
                    FixtureIds.Capability(capability).Value,
                    FixtureIds.Scope(scope),
                    default(TargetId),
                    random.NextBool()));
            return "exclude(" + capability + "@" + scope + ")";
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
            // The reference compositions' documented move: 07 s3.4's "Reparent Village under ChapterTwo" (and back
            // for the cards' "Reparent SeatA from LeagueA to LeagueB").
            if (Family == SequenceFamily.Narrative)
            {
                scopeMoved = !scopeMoved;
                string parent = scopeMoved ? NarrativeComposition.ChapterTwo : NarrativeComposition.ChapterOne;
                builder.MoveScope(NarrativeComposition.Village, parent);
                return "reparentScope(village@" + parent + ")";
            }

            scopeMoved = !scopeMoved;
            string cardParent = scopeMoved ? CardComposition.LeagueB : CardComposition.LeagueA;
            builder.MoveScope(CardComposition.SeatAScope, cardParent);
            return "reparentScope(seat-a-scope@" + cardParent + ")";
        }

        private string PatchDescriptor(Lcg random)
        {
            string name = targetNames[random.Next(targetNames.Count)];
            string capability = Family == SequenceFamily.Narrative
                ? NarrativeComposition.RewardBinding
                : CardComposition.MarketTarget;
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
            string tag = providerTags[old];
            PayloadKind kind = providerPayloads[old];
            builder.RemoveInstall(old);
            providerNames.Remove(old);

            replaceCounter++;
            string name = "seq.s" + seed.ToString(CultureInfo.InvariantCulture) + ".rp" + replaceCounter.ToString(CultureInfo.InvariantCulture);
            InstallationState state = InstallationState.Active;
            if (Family == SequenceFamily.Narrative)
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), NarrativeComposition.Rules(tag), state);
                NarrativeComposition.RegisterHookKeys(builder, tag);
                RegisterProvider(name, scope, tag, NarrativeComposition.DialogueRule(tag), PayloadKind.Tag);
            }
            else if (kind == PayloadKind.Int32)
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), CardComposition.ScoringRules(name, random.NextInclusive(1, 3)), state);
                RegisterProvider(name, scope, tag, name + CardComposition.SetBonusSuffix, PayloadKind.Int32);
            }
            else
            {
                builder.Install(name, scope, random.NextInclusive(-1, 1), CardComposition.DrawPolicyRules(name), state);
                RegisterProvider(name, scope, tag, name + CardComposition.DrawPolicySuffix, PayloadKind.Tag);
            }

            return "replaceProvider(" + old + "->" + name + "@" + scope + ")";
        }

        private string AddGrantOrOverride(Lcg random)
        {
            if (Family == SequenceFamily.Narrative)
            {
                // A Conservative import: an ancestor scope explicitly imports a chapter capability (P-013).
                string scope = random.NextBool()
                    ? NarrativeComposition.Village
                    : NarrativeComposition.Grove;
                string capability = random.NextBool()
                    ? NarrativeComposition.ConversationBinding
                    : NarrativeComposition.GateConditionBinding;
                string provider = ChapterProvider(random);
                builder.AddScopeImport(scope, capability, provider);
                return "addImport(" + capability + "@" + scope + "<-" + provider + ")";
            }

            // A versioned selection override: legal only for Replace/Exclusive slots (P-018).
            string target = targetNames[random.Next(targetNames.Count)];
            builder.Select(CardComposition.SetBonus, providerNames[random.Next(providerNames.Count)], atTarget: target);
            return "select(set-bonus@" + target + ")";
        }

        /// <summary>A mounted (or declared) chapter provider name, so an import names a real installation.</summary>
        private string ChapterProvider(Lcg random)
        {
            List<string> candidates = new List<string>();
            if (Family == SequenceFamily.Narrative)
            {
                candidates.Add(providerNames.Count > 0 ? providerNames[0] : NarrativeComposition.ChapterOneInstall);
                candidates.Add(providerNames.Count > 1 ? providerNames[1] : NarrativeComposition.ChapterTwoInstall);
            }

            return candidates[random.Next(candidates.Count)];
        }

        private void RegisterProvider(
            string name,
            string scope,
            string tag,
            string ruleName,
            PayloadKind payload)
        {
            providerNames.Add(name);
            providerTags[name] = tag;
            providerRuleNames[name] = ruleName;
            providerScopes[name] = scope;
            providerPayloads[name] = payload;
        }
    }
}
