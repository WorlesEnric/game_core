// GameCore.Derivation — canonical projections for differential comparison (GC-006).
//
// TEST-008 and 02 s4 require the runtime derivation and the full-recompute oracle to be compared on *effective
// values, supports, recipe hashes and explanation reason sets*, not on component counts. This file is the single
// projection both sides are reduced to before comparison, so the assertion cannot accidentally compare two
// different things.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Canonical text forms of a derivation result for equality and subset assertions.</summary>
    public static class DerivationProjection
    {
        /// <summary>
        /// Effective semantics only: per target, its effective capability set, every slot's value hash, policy,
        /// support keys, shadowed keys and its recipe hash. Provider identities are included in the support keys,
        /// because "the surviving support" is exactly what a retraction must preserve (P-017).
        /// </summary>
        public static string SemanticsText(DerivationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            StringBuilder text = new StringBuilder();
            text.Append("accepted=").Append(result.Accepted ? "1" : "0").Append('\n');
            text.Append("mode=").Append(result.Snapshot.Mode.ToString()).Append('\n');
            IReadOnlyList<TargetAssembly> assemblies = result.Assemblies;
            for (int a = 0; a < assemblies.Count; a++)
            {
                TargetAssembly assembly = assemblies[a];
                text.Append("target=").Append(assembly.Target.ToString())
                    .Append(";scope=").Append(assembly.Scope.ToString())
                    .Append(";recipe=").Append(assembly.RecipeHash.ToHex()).Append('\n');

                text.Append("  capabilities=");
                for (int c = 0; c < assembly.EffectiveCapabilities.Count; c++)
                {
                    if (c > 0)
                    {
                        text.Append(',');
                    }

                    text.Append(assembly.EffectiveCapabilities[c].ToString());
                }

                text.Append('\n');

                for (int s = 0; s < assembly.Slots.Count; s++)
                {
                    EffectiveSlot slot = assembly.Slots[s];
                    text.Append("  slot=").Append(slot.Capability.ToString())
                        .Append('#').Append(slot.Slot)
                        .Append('/').Append(slot.Version)
                        .Append('/').Append(slot.Policy.ToString())
                        .Append(";hash=").Append(slot.Hash.ToHex())
                        .Append(";values=");
                    for (int v = 0; v < slot.Values.Count; v++)
                    {
                        if (v > 0)
                        {
                            text.Append('|');
                        }

                        PayloadCodec.AppendCanonical(text, slot.Values[v]);
                    }

                    text.Append(";support=");
                    AppendKeys(text, KeysOf(slot.Support));
                    text.Append(";shadowed=");
                    AppendKeys(text, KeysOf(slot.Shadowed));
                    text.Append('\n');
                }
            }

            return text.ToString();
        }

        /// <summary>Content hash of <see cref="SemanticsText"/>: the one-line equality a test asserts.</summary>
        public static ContentHash SemanticsHash(DerivationResult result) =>
            ContentHash.Compute(Encoding.UTF8.GetBytes(SemanticsText(result)));

        /// <summary>
        /// Every candidate decision in canonical text form. Two derivations with equal decision sets agree about
        /// who emitted, who lost, who was excluded and why (P-026).
        /// </summary>
        public static IReadOnlyList<string> DecisionKeys(DerivationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            List<string> keys = new List<string>(result.Decisions.Count);
            for (int i = 0; i < result.Decisions.Count; i++)
            {
                CandidateDecision decision = result.Decisions[i];
                StringBuilder line = new StringBuilder();
                line.Append("decision=").Append(decision.Target.ToString())
                    .Append('/').Append(decision.Capability.ToString())
                    .Append('/').Append(decision.Rule.ToString())
                    .Append('@').Append(decision.Provider.ToString())
                    .Append('#').Append(decision.OutputSlot)
                    .Append(";status=").Append(decision.Status.ToString())
                    .Append(";reason=").Append(decision.Reason.ToString())
                    .Append(";gate=").Append(decision.ModeGate.ToString())
                    .Append(";stratum=").Append(decision.Stratum)
                    .Append(";depth=").Append(decision.ProviderDepth);
                keys.Add(line.ToString());
            }

            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        /// <summary>
        /// The decisions of <paramref name="subset"/> that <paramref name="superset"/> does not contain. An empty
        /// result means the indexed engine invented nothing the index-free oracle cannot reproduce.
        /// </summary>
        public static IReadOnlyList<string> MissingDecisions(
            DerivationResult subset,
            DerivationResult superset)
        {
            if (subset == null)
            {
                throw new ArgumentNullException(nameof(subset));
            }

            if (superset == null)
            {
                throw new ArgumentNullException(nameof(superset));
            }

            HashSet<string> known = new HashSet<string>(DecisionKeys(superset));
            List<string> missing = new List<string>();
            IReadOnlyList<string> candidate = DecisionKeys(subset);
            for (int i = 0; i < candidate.Count; i++)
            {
                if (!known.Contains(candidate[i]))
                {
                    missing.Add(candidate[i]);
                }
            }

            return missing;
        }

        /// <summary>Canonical text of the counters that a cost report must expose (P-022, P-023).</summary>
        public static string CounterText(DerivationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            CostCounters counters = result.Counters;
            StringBuilder text = new StringBuilder();
            text.Append("within=").Append(counters.WithinBudget ? "1" : "0")
                .Append(";dimension=").Append(counters.ExceededDimension.ToString())
                .Append(";exceeded=").Append(counters.ExceededCount)
                .Append('/').Append(counters.ExceededLimit)
                .Append(";causes=").Append(counters.TopFanOutCauses.Count)
                .Append(";").Append(counters.Describe());
            return text.ToString();
        }

        private static List<ContributionKey> KeysOf(IReadOnlyList<CapabilityContribution> contributions)
        {
            List<ContributionKey> keys = new List<ContributionKey>(contributions.Count);
            for (int i = 0; i < contributions.Count; i++)
            {
                keys.Add(contributions[i].Key);
            }

            return keys;
        }

        private static void AppendKeys(StringBuilder text, IReadOnlyList<ContributionKey> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0)
                {
                    text.Append('|');
                }

                text.Append(keys[i].Provider.ToString())
                    .Append('/').Append(keys[i].Rule.ToString())
                    .Append('#').Append(keys[i].OutputSlot);
            }
        }
    }
}
