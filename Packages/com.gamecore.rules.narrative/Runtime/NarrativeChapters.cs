// GameCore.Rules.Narrative — chapter content of the narrative reference composition (07 section 3).
//
// Normative sources: docs/game-core/07-reference-compositions.md section 3.1 (reusable eligibility: a chapter
// contributes a dialogue binding, a gate condition binding and an encounter hook binding to compatible
// descendants) and section 3.2 (a valid choice sets `chapter1.bridgePermit = true`; the gate owner evaluates the
// last accepted condition).
//
// This file owns CONTENT, not identity. Every stable identity (scope, recipe, target, capability, rule) is the
// derivation fixture's own vocabulary (`GameCore.Derivation.Fixtures.NarrativeComposition`), which GC-006 already
// froze and which the Wave 2 gate reuses, so a chapter rule name can never drift between the pure rules, the
// gameplay package and the composition fixture. What lives here is exactly the data a chapter contributes:
//
//   * its binding ordinal: the single canonical int32 a derived binding row carries (05 section 6 slot values are
//     one big-endian int32, so a chapter's contribution is identified by its ordinal and its provenance), and
//   * the stable names of the immutable definitions it binds (graph, condition, hooks, choice surface).
//
// The definition names are `<chapterTag><suffix>`, the same shape GC-006's fixture payloads use, so a per-chapter
// rule payload and a chapter descriptor name the same content.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Narrative
{
    /// <summary>
    /// One chapter's reusable content: what it binds, and which durable fact its gate condition reads. Immutable
    /// and engine-free, so the same descriptor serves derivation, gameplay and a pure test.
    /// </summary>
    public sealed class ChapterDefinition
    {
        public ChapterDefinition(
            string chapterTag,
            int bindingOrdinal,
            int openingNodeOrdinal,
            string gateConditionFactKey)
        {
            if (string.IsNullOrEmpty(chapterTag))
            {
                throw new ArgumentException("a chapter needs a stable tag (P-004).", nameof(chapterTag));
            }

            if (bindingOrdinal <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bindingOrdinal),
                    "a chapter's binding ordinal is positive; a derived binding row carries it as its slot value.");
            }

            if (openingNodeOrdinal <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(openingNodeOrdinal),
                    "a chapter opens a conversation at a positive node ordinal.");
            }

            if (string.IsNullOrEmpty(gateConditionFactKey))
            {
                throw new ArgumentException(
                    "a chapter's gate condition reads a declared durable fact key.", nameof(gateConditionFactKey));
            }

            ChapterTag = chapterTag;
            BindingOrdinal = bindingOrdinal;
            OpeningNodeOrdinal = openingNodeOrdinal;
            GateConditionFactKey = gateConditionFactKey;
        }

        /// <summary>The chapter's stable tag, e.g. `chapter-one`; the suffix of every definition name it binds.</summary>
        public string ChapterTag { get; }

        /// <summary>
        /// The canonical int32 slot value every derived binding of this chapter carries. One number identifies the
        /// selected chapter content without inventing a second payload encoding for a binding row (05 section 6).
        /// </summary>
        public int BindingOrdinal { get; }

        /// <summary>Dialogue node ordinal a conversation opens at under this chapter's graph.</summary>
        public int OpeningNodeOrdinal { get; }

        /// <summary>Durable fact key this chapter's gate condition binding reads (07 section 3.1).</summary>
        public string GateConditionFactKey { get; }

        /// <summary>Stable name of the chapter's dialogue graph definition.</summary>
        public string DialogueGraphDefinition => ChapterTag + NarrativeDefinitionSuffixes.DialogueGraph;

        /// <summary>Stable name of the chapter's gate condition definition.</summary>
        public string GateConditionDefinition => ChapterTag + NarrativeDefinitionSuffixes.GateCondition;

        /// <summary>Stable name of the chapter's choice-surface definition (the stratum-1 capability payload).</summary>
        public string ChoiceSurfaceDefinition => ChapterTag + NarrativeDefinitionSuffixes.ChoiceSurface;

        /// <summary>Stable name of the chapter's first encounter hook definition.</summary>
        public string BeginHookDefinition => ChapterTag + NarrativeDefinitionSuffixes.BeginHook;

        /// <summary>Stable name of the chapter's second encounter hook definition.</summary>
        public string OfferHookDefinition => ChapterTag + NarrativeDefinitionSuffixes.OfferHook;

        /// <summary>The chapter's ordered encounter hook plan: `BeginScene` precedes `OfferChoice` (07 section 3.1).</summary>
        public IReadOnlyList<string> EncounterHookPlan
        {
            get { return new[] { BeginHookDefinition, OfferHookDefinition }; }
        }

        public override string ToString()
            => "chapter(" + ChapterTag + "#" + BindingOrdinal.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The `<chapterTag><suffix>` shape of a chapter's immutable definition names.</summary>
    public static class NarrativeDefinitionSuffixes
    {
        public const string DialogueGraph = ".dialogue-graph";
        public const string GateCondition = ".gate-condition";
        public const string ChoiceSurface = ".choice-surface";
        public const string BeginHook = ".hook.begin";
        public const string OfferHook = ".hook.offer";
    }

    /// <summary>
    /// The chapters of the reference composition, in canonical (ascending ordinal) order. Lookup is by exact tag;
    /// an unknown tag is a miss a caller must handle, never a guessed default chapter (P-015's `Ineligible` rule).
    /// </summary>
    public static class NarrativeChapters
    {
        public const string ChapterOneTag = "chapter-one";
        public const string ChapterTwoTag = "chapter-two";

        /// <summary>
        /// Chapter One binds the permit condition of the story world's first gate; Chapter Two is a sibling branch
        /// with its own content, so a target under one chapter never receives the other's binding (07 section 3.1).
        /// </summary>
        public static IReadOnlyList<ChapterDefinition> All { get; } = new[]
        {
            new ChapterDefinition(ChapterOneTag, 1, 1, NarrativeFacts.BridgePermitFactKey),
            new ChapterDefinition(ChapterTwoTag, 2, 1, NarrativeFacts.HarborPermitFactKey),
        };

        /// <summary>True when the ordinal is one a declared chapter can carry.</summary>
        public static bool IsDeclaredOrdinal(int ordinal)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].BindingOrdinal == ordinal)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGet(string chapterTag, out ChapterDefinition? definition)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].ChapterTag, chapterTag, StringComparison.Ordinal))
                {
                    definition = All[i];
                    return true;
                }
            }

            definition = null;
            return false;
        }

        /// <summary>The chapter a tag names; an unknown tag throws, because a caller cannot invent content.</summary>
        public static ChapterDefinition Get(string chapterTag)
        {
            if (!TryGet(chapterTag, out ChapterDefinition? definition) || definition == null)
            {
                throw new ArgumentException("no chapter is declared for tag '" + chapterTag + "'.", nameof(chapterTag));
            }

            return definition;
        }

        /// <summary>
        /// The chapter a derived binding row's int32 value names, or a miss. This is the inverse of
        /// <see cref="ChapterDefinition.BindingOrdinal"/> and is what a gameplay stage uses to resolve a binding
        /// without a second identity table.
        /// </summary>
        public static bool TryGetByOrdinal(int bindingOrdinal, out ChapterDefinition? definition)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].BindingOrdinal == bindingOrdinal)
                {
                    definition = All[i];
                    return true;
                }
            }

            definition = null;
            return false;
        }

        /// <summary>Canonical chapter tags, ascending. Used by the composition and by evidence digests.</summary>
        public static IReadOnlyList<string> Tags
        {
            get
            {
                var tags = new List<string>(All.Count);
                for (int i = 0; i < All.Count; i++)
                {
                    tags.Add(All[i].ChapterTag);
                }

                return tags;
            }
        }
    }
}
