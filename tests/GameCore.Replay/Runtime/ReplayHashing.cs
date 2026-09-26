// GameCore.Replay — canonical replay hashing (GC-023, TEST-022).
//
// TEST-022 fixes what a replay comparison may and may not read:
//
//   * "Hash canonical stable identities and schema fields, excluding timestamps, padding, diagnostic counters and
//     native layout." Every hash in this file is built from canonical text over stable ids, schema references,
//     integer versions, effective values and committed-event identities — and from nothing else. `ExcludedFields`
//     below is the normative list, and `ReplayStateHash.HashExclusionsAreHonoured` in the test suite is the check
//     that a released `WorldId`, a native entity index or a counter value cannot change any hash here.
//   * "Map fresh runtime WorldId values to a fixture world ordinal only in comparison output, including embedded
//     event/request identities; separately assert that actual recreated worlds have different session IDs and
//     reject stale handles." `WorldOrdinalMap` is that mapping and it is deliberately a *comparison* structure: it
//     is never consulted by the engine, the ledger or the observation store, so deduplication, high-water marks
//     and handle validation keep using the real 128-bit identities.
//   * The normalized comparison must not change runtime deduplication: `NormalizedText` only rewrites the text a
//     comparison prints, and `WorldOrdinalMap` holds no state the runtime can read.
//
// "Diagnostic counters" are excluded on purpose: a trace may carry them (a cost trace is GC-023's other half), but
// two runs of the same admitted input must compare equal even if one was instrumented and the other was not.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Derivation;

namespace GameCore.Replay
{
    /// <summary>
    /// The fields a canonical replay hash must not read (TEST-022). Declared as data so a test can enumerate the
    /// exclusion list instead of trusting a comment.
    /// </summary>
    public static class ExcludedFields
    {
        /// <summary>Host or plugin wall-clock readings; the kernel never reads a clock (P-008).</summary>
        public const string Timestamps = "timestamps";

        /// <summary>Struct padding and any other unspecified layout bytes.</summary>
        public const string Padding = "padding";

        /// <summary>Diagnostic and telemetry counters: two runs compare equal instrumented or not.</summary>
        public const string DiagnosticCounters = "diagnostic-counters";

        /// <summary>Native layout: entity indices, chunk order, `Entity.Version`, native addresses.</summary>
        public const string NativeLayout = "native-layout";

        /// <summary>Worker index, worker count and completion order of independent producers.</summary>
        public const string WorkerScheduling = "worker-scheduling";

        /// <summary>Dictionary/insertion enumeration order of a registration-time structure.</summary>
        public const string EnumerationOrder = "enumeration-order";

        /// <summary>Every excluded field, in the order this file documents them.</summary>
        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        {
            Timestamps,
            Padding,
            DiagnosticCounters,
            NativeLayout,
            WorkerScheduling,
            EnumerationOrder,
        });
    }

    /// <summary>
    /// Maps the fresh session identity of one run to the fixture ordinal a comparison prints. It is a comparison
    /// structure only: nothing in the kernel holds a reference to it, and `RuntimeIdentityIsUntouched` in the suite
    /// asserts that the real identity still deduplicates and still rejects a stale handle.
    /// </summary>
    public sealed class WorldOrdinalMap
    {
        private readonly Dictionary<Id128, int> ordinals = new Dictionary<Id128, int>();

        /// <summary>Registers one world's session identity under a fixture ordinal; the first registration wins.</summary>
        public int Register(WorldId world, int ordinal)
        {
            if (!ordinals.ContainsKey(world.Session))
            {
                ordinals.Add(world.Session, ordinal);
            }

            return ordinals[world.Session];
        }

        /// <summary>The ordinal of a world, or <c>-1</c> when the identity was never registered.</summary>
        public int OrdinalOf(WorldId world) =>
            ordinals.TryGetValue(world.Session, out int ordinal) ? ordinal : -1;

        /// <summary>Worlds registered so far, so a test can prove two runs of one fixture both mapped.</summary>
        public int Count => ordinals.Count;

        /// <summary>
        /// Rewrites every canonical identity mention of a registered world to <c>world#&lt;ordinal&gt;</c>. This is
        /// the only place a comparison may replace a runtime identity, and it changes text, never runtime state.
        /// </summary>
        public string Normalize(string canonicalText)
        {
            if (canonicalText == null)
            {
                throw new ArgumentNullException(nameof(canonicalText));
            }

            string normalized = canonicalText;
            foreach (KeyValuePair<Id128, int> pair in ordinals)
            {
                normalized = normalized.Replace(
                    pair.Key.ToString(),
                    "world#" + pair.Value.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
            }

            return normalized;
        }
    }

    /// <summary>
    /// Canonical text and hashes of one logical step's observable result: effective state, decisions, capability
    /// provenance and committed-event identities. Every method takes kernel values and reads only the fields
    /// <see cref="ExcludedFields"/> allows.
    /// </summary>
    public static class ReplayStateHash
    {
        /// <summary>
        /// Canonical text of the effective state of one accepted derivation: per target, its scope, its base recipe,
        /// and every effective slot (capability, version, slot, schema, policy, per-value canonical payload) in
        /// canonical (capability, version, slot) order. Support and shadowed identities are included, because
        /// "retracts exactly its support" (P-033) is part of the observable state a replay must reproduce.
        /// </summary>
        public static string StateText(DerivationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            StringBuilder text = new StringBuilder();
            text.Append("accepted=").Append(result.Accepted ? "1" : "0").Append('\n');
            text.Append("rejection=").Append(((int)result.Rejection).ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int a = 0; a < result.Assemblies.Count; a++)
            {
                TargetAssembly assembly = result.Assemblies[a];
                text.Append("target=").Append(assembly.Target.ToString()).Append('\n');
                text.Append("  scope=").Append(assembly.Scope.ToString()).Append('\n');
                text.Append("  recipe=").Append(assembly.BaseRecipe.ToString()).Append('\n');
                for (int s = 0; s < assembly.Slots.Count; s++)
                {
                    EffectiveSlot slot = assembly.Slots[s];
                    text.Append("  slot=").Append(slot.Capability.ToString())
                        .Append('/').Append(slot.Version.ToString(CultureInfo.InvariantCulture))
                        .Append('/').Append(slot.Slot.ToString(CultureInfo.InvariantCulture))
                        .Append('/').Append(slot.Schema.ToString())
                        .Append('/').Append(slot.Policy.ToString())
                        .Append('\n');
                    for (int v = 0; v < slot.Values.Count; v++)
                    {
                        text.Append("    value=");
                        PayloadCodec.AppendCanonical(text, slot.Values[v]);
                        text.Append('\n');
                    }

                    AppendSupports(text, "    support=", slot.Support);
                    AppendSupports(text, "    shadowed=", slot.Shadowed);
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// Canonical text of every candidate decision in canonical order: the target, the rule, the declared
        /// priority and the decision status/reason. A replay that admitted the same input must decide identically
        /// even when independent producers completed in another order (P-008).
        /// </summary>
        public static string DecisionText(DerivationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            StringBuilder text = new StringBuilder();
            for (int i = 0; i < result.Decisions.Count; i++)
            {
                CandidateDecision decision = result.Decisions[i];
                text.Append("decision=").Append(decision.Target.ToString())
                    .Append('/').Append(decision.Capability.ToString())
                    .Append('/').Append(decision.Rule.ToString())
                    .Append('/').Append(decision.Provider.ToString())
                    .Append('/').Append(decision.OutputSlot.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(((int)decision.Status).ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(decision.Reason.ToString())
                    .Append('/').Append(decision.Stratum.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            return text.ToString();
        }

        /// <summary>Runtime explanations are hashed in replays but omitted from oracle comparison: the oracle
        /// does not produce explanation records. Decisions, effective supports and provenance remain compared.</summary>
        public static string ExplanationText(DerivationResult result)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < result.Explanations.Count; i++)
            {
                DerivationExplanation explanation = result.Explanations[i];
                text.Append("explanation=").Append(explanation.Target.ToString())
                    .Append('/').Append(explanation.Capability.ToString())
                    .Append('\n');
                AppendSupports(text, "  winners=", explanation.Winners);
                AppendSupports(text, "  shadowed=", explanation.Shadowed);
            }

            return text.ToString();
        }

        /// <summary>Canonical text of capability provenance: every active contribution and its support set.</summary>
        public static string ProvenanceText(DerivationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            StringBuilder text = new StringBuilder();
            for (int i = 0; i < result.Contributions.Count; i++)
            {
                CapabilityContribution contribution = result.Contributions[i];
                text.Append("contribution=").Append(contribution.Key.Provider.ToString())
                    .Append('/').Append(contribution.Key.Rule.ToString())
                    .Append('/').Append(contribution.Key.Target.ToString())
                    .Append('/').Append(contribution.Key.Capability.ToString())
                    .Append('/').Append(contribution.Key.OutputSlot.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(contribution.Disposition.ToString())
                    .Append('\n');
            }

            return text.ToString();
        }

        /// <summary>Hash of one canonical text, so a comparison is one 32-byte value (05 s2).</summary>
        public static ContentHash HashOf(string canonicalText)
        {
            if (canonicalText == null)
            {
                throw new ArgumentNullException(nameof(canonicalText));
            }

            return ContentHash.Compute(Encoding.UTF8.GetBytes(canonicalText));
        }

        /// <summary>Canonical text of one committed event identity: target, schema, step, epoch and value.</summary>
        public static string EventIdentityText(
            TargetId target,
            SchemaRef schema,
            LogicalStepId step,
            AssemblyEpoch epoch,
            int value) =>
            "event=" + target.ToString()
            + '/' + schema.ToString()
            + "/step=" + step.Value.ToString(CultureInfo.InvariantCulture)
            + "/epoch=" + epoch.Value.ToString(CultureInfo.InvariantCulture)
            + "/value=" + value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Hash of a whole event sequence, in the order the sequence was committed.</summary>
        public static ContentHash EventHash(IReadOnlyList<string>? eventIdentities)
        {
            StringBuilder text = new StringBuilder();
            if (eventIdentities != null)
            {
                for (int i = 0; i < eventIdentities.Count; i++)
                {
                    text.Append(eventIdentities[i]).Append('\n');
                }
            }

            return HashOf(text.ToString());
        }

        /// <summary>Hash of a whole per-target integer state table, in canonical target order.</summary>
        public static ContentHash IntegerStateHash(IReadOnlyList<ReplayTargetState>? states)
        {
            StringBuilder text = new StringBuilder();
            if (states != null)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    text.Append("state=").Append(states[i].Target.ToString())
                        .Append('/').Append(states[i].Value.ToString(CultureInfo.InvariantCulture))
                        .Append('/').Append(states[i].Applied.ToString(CultureInfo.InvariantCulture))
                        .Append('\n');
                }
            }

            return HashOf(text.ToString());
        }

        /// <summary>
        /// A hash chain over per-step hashes: the accumulator starts as 32 zero bytes and
        /// <c>link[i] = H(link[i-1] || step[i])</c>. Any divergence at any step changes every later link, which is
        /// what makes one chain value serve as the whole ordered comparison (TEST-022). One definition is shared by
        /// every runner - the modeled fixture replay and the real-Unity-jobs variant - so two runners cannot
        /// disagree about what "the same replay" means.
        /// </summary>
        public static ContentHash Chain(IReadOnlyList<ContentHash>? perStep)
        {
            byte[] accumulator = new byte[ContentHash.SizeInBytes];
            for (int i = 0; i < (perStep?.Count ?? 0); i++)
            {
                byte[] step = perStep![i].ToArray();
                byte[] link = new byte[accumulator.Length + step.Length];
                Array.Copy(accumulator, 0, link, 0, accumulator.Length);
                Array.Copy(step, 0, link, accumulator.Length, step.Length);
                accumulator = ContentHash.Compute(link).ToArray();
            }

            return new ContentHash(accumulator);
        }

        /// <summary>
        /// The full canonical projection of one derivation a differential comparison uses: the effective state, then
        /// every decision and provenance line sorted ordinally. Sorting is what makes the projection a statement
        /// about *what* was decided rather than about the order the two traversals happened to walk in (TEST-008
        /// compares the effective assembly and provenance, not a traversal order).
        /// </summary>
        public static string ProjectionText(DerivationResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            var builder = new StringBuilder();
            builder.Append(StateText(result));
            AppendSorted(builder, "decision-line", DecisionText(result));
            AppendSorted(builder, "provenance-line", ProvenanceText(result));
            return builder.ToString();
        }

        private static void AppendSorted(StringBuilder text, string prefix, string canonicalLines)
        {
            if (canonicalLines.Length == 0)
            {
                return;
            }

            List<string> lines = new List<string>();
            int start = 0;
            for (int i = 0; i <= canonicalLines.Length; i++)
            {
                if (i == canonicalLines.Length || canonicalLines[i] == '\n')
                {
                    if (i > start)
                    {
                        lines.Add(canonicalLines.Substring(start, i - start));
                    }

                    start = i + 1;
                }
            }

            lines.Sort(StringComparer.Ordinal);
            for (int i = 0; i < lines.Count; i++)
            {
                text.Append(prefix).Append('=').Append(lines[i]).Append('\n');
            }
        }

        private static void AppendSupports(StringBuilder text, string prefix, IReadOnlyList<CapabilityContribution>? supports)
        {
            if (supports == null)
            {
                return;
            }

            for (int i = 0; i < supports.Count; i++)
            {
                text.Append(prefix).Append(supports[i].Key.Provider.ToString())
                    .Append('/').Append(supports[i].Key.Rule.ToString())
                    .Append('/').Append(supports[i].Key.Target.ToString())
                    .Append('/').Append(supports[i].Key.Capability.ToString())
                    .Append('/').Append(supports[i].Key.OutputSlot.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

    }

    /// <summary>One target's integer rule state after a step: the value and how many increments produced it.</summary>
    public readonly struct ReplayTargetState
    {
        public ReplayTargetState(TargetId target, int value, int applied)
        {
            Target = target;
            Value = value;
            Applied = applied;
        }

        public TargetId Target { get; }

        /// <summary>The fixture's integer state for this target (never a floating-point value).</summary>
        public int Value { get; }

        /// <summary>Slot contributions the fixture applied to reach it, so a silent skip is visible.</summary>
        public int Applied { get; }

        public override string ToString() =>
            Target.ToString() + "=" + Value.ToString(CultureInfo.InvariantCulture)
            + "(" + Applied.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
