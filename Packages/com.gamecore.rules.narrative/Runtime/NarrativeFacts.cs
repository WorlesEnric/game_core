// GameCore.Rules.Narrative — durable quest facts as pure integer rules (07 section 3.2, P-032, P-044).
//
// `QuestFact[] { Key, Value, Version }` is the ledger's authoritative state. The protocol addresses authoritative
// state as `(TargetId, OwnerId, SlotId)` (P-032), so one declared fact is one state slot: the fact key IS the slot
// identity, the value is a bounded integer, and the fact version is the slot's own version counter. That mapping is
// why the narrative slice needs no second authoritative store and no kernel schema: the ledger owns slots, the
// kernel publishes them, and a committed fact is observable exactly like any other owned state.
//
// The two rules that make a fact transition meaningful are integer functions here, so they are testable without a
// world (TEST-013 uses "a narrative flag transition" beside the card transfer):
//
//   * a fact value is one of {0, 1} — a fact is a flag, not an unbounded counter, and an out-of-domain request is
//     refused before any write;
//   * a request that does not change the value is not a transition. The ledger still answers the duplicate
//     *command* from its request ledger (P-037), and the fact rule independently refuses to emit a second
//     transition, which is what REF-N02's "duplicate mutation emits no duplicate fact transition" checks.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Narrative
{
    /// <summary>The declared durable facts of the reference composition, with their pure value rules.</summary>
    public static class NarrativeFacts
    {
        /// <summary>A fact that does not hold.</summary>
        public const int False = 0;

        /// <summary>A fact that holds.</summary>
        public const int True = 1;

        /// <summary>The fact Chapter One's valid choice sets (07 section 3.2).</summary>
        public const string BridgePermitFactKey = "chapter1.bridgePermit";

        /// <summary>The fact Chapter Two's gate condition reads; its own key, never Chapter One's (07 section 3.3).</summary>
        public const string HarborPermitFactKey = "chapter2.harborPermit";

        /// <summary>Value every declared fact starts at before any accepted choice.</summary>
        public const int InitialValue = False;

        /// <summary>Version every declared fact slot starts at; a transition advances it by one.</summary>
        public const int InitialVersion = 1;

        /// <summary>Every declared fact key in canonical (ordinal) order: the ledger's slot declaration order.</summary>
        public static IReadOnlyList<string> DeclaredFactKeys { get; } = new[]
        {
            BridgePermitFactKey,
            HarborPermitFactKey,
        };

        /// <summary>Reads the ledger's stable slot ordinal of one fact key; a miss is refused, never guessed.</summary>
        public static bool TryGetFactOrdinal(string factKey, out int ordinal)
        {
            for (int i = 0; i < DeclaredFactKeys.Count; i++)
            {
                if (string.Equals(DeclaredFactKeys[i], factKey, StringComparison.Ordinal))
                {
                    ordinal = i;
                    return true;
                }
            }

            ordinal = -1;
            return false;
        }

        /// <summary>
        /// Stable, canonical slot tag of the bridge-permit fact. A fact key may contain uppercase characters
        /// (it is a content key), while a stable name may not, so the tag is the fact's addressable lowercase form.
        /// </summary>
        public const string BridgePermitSlotTag = "bridge-permit";

        /// <summary>Stable canonical slot tag of the harbor-permit fact.</summary>
        public const string HarborPermitSlotTag = "harbor-permit";

        /// <summary>The canonical slot tag of one declared fact key; a miss is refused, never guessed.</summary>
        public static bool TryGetFactSlotTag(string factKey, out string slotTag)
        {
            if (string.Equals(factKey, BridgePermitFactKey, StringComparison.Ordinal))
            {
                slotTag = BridgePermitSlotTag;
                return true;
            }

            if (string.Equals(factKey, HarborPermitFactKey, StringComparison.Ordinal))
            {
                slotTag = HarborPermitSlotTag;
                return true;
            }

            slotTag = string.Empty;
            return false;
        }

        /// <summary>The declared fact key of one ledger slot ordinal; a miss is refused (P-004).</summary>
        public static bool TryGetFactKeyByOrdinal(int ordinal, out string factKey)
        {
            if (ordinal >= 0 && ordinal < DeclaredFactKeys.Count)
            {
                factKey = DeclaredFactKeys[ordinal];
                return true;
            }

            factKey = string.Empty;
            return false;
        }

        public static bool IsDeclared(string factKey) => TryGetFactOrdinal(factKey, out int _);

        /// <summary>True when the integer is a legal fact value.</summary>
        public static bool IsValidValue(int value) => value == False || value == True;

        /// <summary>Advances one fact version by one transition.</summary>
        public static int NextVersion(int version) => version + 1;

        /// <summary>
        /// Applies one requested fact value to the current one. A request that changes the value yields the new
        /// value; an out-of-domain value and a request that changes nothing are both refused, so a duplicate
        /// mutation cannot emit a second transition or a second committed fact event (REF-N02).
        /// </summary>
        public static bool TryTransition(int currentValue, int requestedValue, out int nextValue)
            => TryTransition(currentValue, requestedValue, out nextValue, out string _);

        /// <summary>The same transition with its stable refusal code (P-052).</summary>
        public static bool TryTransition(
            int currentValue,
            int requestedValue,
            out int nextValue,
            out string refusalCode)
        {
            nextValue = currentValue;
            refusalCode = NarrativeRefusals.None;
            if (!IsValidValue(currentValue) || !IsValidValue(requestedValue))
            {
                refusalCode = NarrativeRefusals.FactValueOutOfDomain;
                return false;
            }

            if (currentValue == requestedValue)
            {
                refusalCode = NarrativeRefusals.FactUnchanged;
                return false;
            }

            nextValue = requestedValue;
            return true;
        }

        /// <summary>True when two fact values are the same fact state; used by the commitment digest.</summary>
        public static bool IsSameFact(int left, int right) => left == right;

        /// <summary>Canonical text form of one fact state, for evidence and diagnostics only.</summary>
        public static string Describe(string factKey, int value, int version)
            => factKey + "=" + (value == True ? "true" : "false")
                + "@v" + version.ToString(CultureInfo.InvariantCulture);
    }
}
