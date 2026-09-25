// GameCore.Rules.Narrative — the chapter provider's derivation plan (07 section 3.1, P-015, P-017, P-019, P-021).
//
// A chapter contributes bindings to compatible descendants without any per-instance import. What that means
// concretely is four things per binding: the capability it emits, the stratum the capability occupies, the rule
// identity that produces it, the recipe that selects the target, and the single canonical int32 value a derived
// row carries.
//
// This table is the package's declaration source: the gameplay package builds its manifests from it, the pure
// tests exercise it, and the canonical trace records it. It is content, not mechanism: it names no ECS type, no
// stage and no engine API, so it is the same data in a plain test, in the Editor and in the player.
//
// Two properties matter for the kernel and are therefore explicit here:
//
//   * every capability has ONE rule per chapter and a `Replace` policy, so an effective slot is supported by
//     exactly one contribution and its value is exactly one int32 — the shape a published binding row holds
//     (05 section 6, GC-008's derived-variant transfer);
//   * the stratum-1 choice binding declares the stratum-0 dialogue binding as its input, which is the only way a
//     rule may read another capability (P-021: strictly lower strata).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Narrative
{
    /// <summary>One binding a chapter contributes: capability, stratum, rule, selector recipe and canonical value.</summary>
    public sealed class NarrativeBindingPlan
    {
        public NarrativeBindingPlan(
            string capabilityName,
            int stratum,
            string ruleSuffix,
            string selectorRecipeName,
            string schemaName,
            string inputCapabilityName,
            string declaredPolicy)
        {
            if (string.IsNullOrEmpty(capabilityName))
            {
                throw new ArgumentException("a binding plan names its capability.", nameof(capabilityName));
            }

            CapabilityName = capabilityName;
            Stratum = stratum;
            RuleSuffix = ruleSuffix ?? string.Empty;
            SelectorRecipeName = selectorRecipeName ?? string.Empty;
            SchemaName = schemaName;
            InputCapabilityName = inputCapabilityName ?? string.Empty;
            DeclaredPolicy = declaredPolicy ?? string.Empty;
        }

        /// <summary>Stable name of the capability this binding emits.</summary>
        public string CapabilityName { get; }

        /// <summary>Declared stratum of the capability; a rule may read only strictly lower strata (P-021).</summary>
        public int Stratum { get; }

        /// <summary>Suffix of the chapter's rule name, in the derivation fixture's `<chapterTag><suffix>` shape.</summary>
        public string RuleSuffix { get; }

        /// <summary>Stable name of the recipe whose targets the rule selects.</summary>
        public string SelectorRecipeName { get; }

        /// <summary>Declared schema of the capability's single output slot.</summary>
        public string SchemaName { get; }

        /// <summary>Declared lower-stratum input capability; empty when the rule reads no capability.</summary>
        public string InputCapabilityName { get; }

        /// <summary>Declared composition policy of the capability's only slot; the plan never infers one (P-019).</summary>
        public string DeclaredPolicy { get; }

        /// <summary>True when the rule reads a lower-stratum capability of the same target.</summary>
        public bool HasInput => InputCapabilityName.Length != 0;

        /// <summary>The chapter's rule name: `<chapterTag><ruleSuffix>`.</summary>
        public string RuleName(string chapterTag) => chapterTag + RuleSuffix;

        public override string ToString()
            => CapabilityName + "@" + Stratum.ToString(CultureInfo.InvariantCulture) + "<-" + SelectorRecipeName;
    }

    /// <summary>The four bindings a chapter contributes, in canonical (capability name) order.</summary>
    public static class NarrativeDerivationPlan
    {
        /// <summary>Composition policy every narrative binding declares; one supporter per slot (GC-008's transfer).</summary>
        public const string ReplacePolicy = "Replace";

        /// <summary>
        /// The bindings, in canonical order. The dialogue binding is what a villager receives first; the choice
        /// binding is derived from it in the next stratum, so a target with no finalized dialogue binding never
        /// receives a choice surface (P-021).
        /// </summary>
        public static IReadOnlyList<NarrativeBindingPlan> Bindings { get; } = new[]
        {
            new NarrativeBindingPlan(
                NarrativeCompositionNames.ChoiceBindingCapability,
                NarrativeCompositionNames.ChoiceStratum,
                NarrativeCompositionNames.ChoiceRuleSuffix,
                NarrativeCompositionNames.VillagerRecipe,
                NarrativeCompositionNames.ChoiceBindingSchema,
                NarrativeCompositionNames.DialogueBindingCapability,
                ReplacePolicy),
            new NarrativeBindingPlan(
                NarrativeCompositionNames.DialogueBindingCapability,
                NarrativeCompositionNames.BindingStratum,
                NarrativeCompositionNames.DialogueRuleSuffix,
                NarrativeCompositionNames.VillagerRecipe,
                NarrativeCompositionNames.DialogueBindingSchema,
                string.Empty,
                ReplacePolicy),
            new NarrativeBindingPlan(
                NarrativeCompositionNames.EncounterBindingCapability,
                NarrativeCompositionNames.BindingStratum,
                NarrativeCompositionNames.HookBeginRuleSuffix,
                NarrativeCompositionNames.QuestEncounterRecipe,
                NarrativeCompositionNames.EncounterBindingSchema,
                string.Empty,
                ReplacePolicy),
            new NarrativeBindingPlan(
                NarrativeCompositionNames.GateBindingCapability,
                NarrativeCompositionNames.BindingStratum,
                NarrativeCompositionNames.GateRuleSuffix,
                NarrativeCompositionNames.QuestGateRecipe,
                NarrativeCompositionNames.GateBindingSchema,
                string.Empty,
                ReplacePolicy),
        };

        /// <summary>The bindings one chapter contributes, with that chapter's rule names resolved.</summary>
        public static IReadOnlyList<string> RuleNames(string chapterTag)
        {
            var names = new List<string>(Bindings.Count);
            for (int i = 0; i < Bindings.Count; i++)
            {
                names.Add(Bindings[i].RuleName(chapterTag));
            }

            return names;
        }

        /// <summary>Every canonical line of the plan, for evidence digests and the trace.</summary>
        public static IReadOnlyList<string> CanonicalLines(string chapterTag)
        {
            var lines = new List<string>(Bindings.Count);
            for (int i = 0; i < Bindings.Count; i++)
            {
                NarrativeBindingPlan plan = Bindings[i];
                lines.Add(
                    "binding=" + plan.CapabilityName
                    + ";stratum=" + plan.Stratum.ToString(CultureInfo.InvariantCulture)
                    + ";rule=" + plan.RuleName(chapterTag)
                    + ";recipe=" + plan.SelectorRecipeName
                    + ";schema=" + plan.SchemaName
                    + ";policy=" + plan.DeclaredPolicy
                    + ";input=" + (plan.HasInput ? plan.InputCapabilityName : "<none>"));
            }

            return lines;
        }

        /// <summary>The value one derived row carries for one chapter: the chapter's binding ordinal.</summary>
        public static bool TryValueOf(string chapterTag, out int value)
        {
            if (NarrativeChapters.TryGet(chapterTag, out ChapterDefinition? chapter) && chapter != null)
            {
                value = chapter.BindingOrdinal;
                return true;
            }

            value = 0;
            return false;
        }
    }
}
