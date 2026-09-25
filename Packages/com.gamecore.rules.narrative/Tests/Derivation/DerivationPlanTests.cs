#nullable enable
using System.Collections.Generic;
using System.Globalization;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The chapter provider's derivation plan (07 s3.1, P-015, P-017, P-019, P-021): four bindings per chapter, one
    /// canonical int32 value each, a `Replace` policy on every slot, and one stratum-1 choice binding that reads the
    /// stratum-0 dialogue binding of the same target.
    /// </summary>
    [TestFixture]
    public sealed class DerivationPlanTests
    {
        private static readonly string[] ExpectedCapabilityOrder =
        {
            NarrativeCompositionNames.ChoiceBindingCapability,
            NarrativeCompositionNames.DialogueBindingCapability,
            NarrativeCompositionNames.EncounterBindingCapability,
            NarrativeCompositionNames.GateBindingCapability,
        };

        private static readonly string[] RequiredLineTokens =
        {
            "binding=",
            "stratum=",
            "rule=",
            "recipe=",
            "schema=",
            "policy=",
            "input=",
        };

        [Test]
        public void ThePlanDeclaresFourBindingsInCanonicalCapabilityNameOrder()
        {
            Assert.That(NarrativeDerivationPlan.Bindings.Count, Is.EqualTo(4));
            Assert.That(ExpectedCapabilityOrder.Length, Is.EqualTo(NarrativeDerivationPlan.Bindings.Count));

            for (int i = 0; i < NarrativeDerivationPlan.Bindings.Count; i++)
            {
                Assert.That(
                    NarrativeDerivationPlan.Bindings[i].CapabilityName,
                    Is.EqualTo(ExpectedCapabilityOrder[i]),
                    "binding " + i + " is out of declared order.");
            }

            for (int i = 1; i < NarrativeDerivationPlan.Bindings.Count; i++)
            {
                Assert.That(
                    string.CompareOrdinal(
                        NarrativeDerivationPlan.Bindings[i - 1].CapabilityName,
                        NarrativeDerivationPlan.Bindings[i].CapabilityName),
                    Is.LessThan(0),
                    "the canonical order is also ascending by capability name.");
            }
        }

        [Test]
        public void EveryBindingDeclaresOneOfTheTwoDeclaredStrataAReplacePolicyAndItsSlotNames()
        {
            for (int i = 0; i < NarrativeDerivationPlan.Bindings.Count; i++)
            {
                NarrativeBindingPlan binding = NarrativeDerivationPlan.Bindings[i];
                bool declaredStratum = binding.Stratum == NarrativeCompositionNames.BindingStratum
                    || binding.Stratum == NarrativeCompositionNames.ChoiceStratum;

                Assert.That(declaredStratum, Is.True, "binding '" + binding.CapabilityName + "' declares stratum " + binding.Stratum);
                Assert.That(binding.DeclaredPolicy, Is.EqualTo(NarrativeDerivationPlan.ReplacePolicy));
                Assert.That(binding.DeclaredPolicy, Is.EqualTo("Replace"), "one supporter per effective slot (GC-008).");
                Assert.That(binding.CapabilityName, Is.Not.Empty);
                Assert.That(binding.SchemaName, Is.Not.Empty);
                Assert.That(binding.SelectorRecipeName, Is.Not.Empty);
                Assert.That(binding.RuleSuffix, Is.Not.Empty);
                Assert.That(binding.RuleSuffix.StartsWith("."), Is.True, "a rule suffix completes `<chapterTag><suffix>`.");
            }
        }

        [Test]
        public void EachBindingDeclaresTheRuleRecipeAndSchemaTheChapterVocabularyNames()
        {
            AssertBinding(
                NarrativeCompositionNames.DialogueBindingCapability,
                NarrativeCompositionNames.DialogueRuleSuffix,
                NarrativeCompositionNames.VillagerRecipe,
                NarrativeCompositionNames.DialogueBindingSchema);
            AssertBinding(
                NarrativeCompositionNames.GateBindingCapability,
                NarrativeCompositionNames.GateRuleSuffix,
                NarrativeCompositionNames.QuestGateRecipe,
                NarrativeCompositionNames.GateBindingSchema);
            AssertBinding(
                NarrativeCompositionNames.EncounterBindingCapability,
                NarrativeCompositionNames.HookBeginRuleSuffix,
                NarrativeCompositionNames.QuestEncounterRecipe,
                NarrativeCompositionNames.EncounterBindingSchema);
            AssertBinding(
                NarrativeCompositionNames.ChoiceBindingCapability,
                NarrativeCompositionNames.ChoiceRuleSuffix,
                NarrativeCompositionNames.VillagerRecipe,
                NarrativeCompositionNames.ChoiceBindingSchema);
        }

        [Test]
        public void TheChoiceBindingReadsTheDialogueBindingFromAStrictlyLowerStratum()
        {
            NarrativeBindingPlan choice = Binding(NarrativeCompositionNames.ChoiceBindingCapability);
            NarrativeBindingPlan dialogue = Binding(NarrativeCompositionNames.DialogueBindingCapability);

            Assert.That(choice.InputCapabilityName, Is.EqualTo(dialogue.CapabilityName));
            Assert.That(choice.HasInput, Is.True);
            Assert.That(dialogue.HasInput, Is.False);
            Assert.That(choice.Stratum, Is.EqualTo(NarrativeCompositionNames.ChoiceStratum));
            Assert.That(dialogue.Stratum, Is.EqualTo(NarrativeCompositionNames.BindingStratum));
            Assert.That(
                dialogue.Stratum,
                Is.LessThan(choice.Stratum),
                "a rule reads only strictly lower strata (P-021).");

            for (int i = 0; i < NarrativeDerivationPlan.Bindings.Count; i++)
            {
                NarrativeBindingPlan binding = NarrativeDerivationPlan.Bindings[i];
                if (binding.CapabilityName == choice.CapabilityName)
                {
                    continue;
                }

                Assert.That(binding.HasInput, Is.False, "binding '" + binding.CapabilityName + "' reads no capability.");
                Assert.That(binding.InputCapabilityName, Is.Empty);
            }
        }

        [Test]
        public void ThePlanResolvesFourRuleNamesPerChapterInBindingOrder()
        {
            Assert.That(NarrativeChapters.Tags.Count, Is.EqualTo(2));

            for (int c = 0; c < NarrativeChapters.Tags.Count; c++)
            {
                string tag = NarrativeChapters.Tags[c];
                IReadOnlyList<string> ruleNames = NarrativeDerivationPlan.RuleNames(tag);

                Assert.That(ruleNames.Count, Is.EqualTo(NarrativeDerivationPlan.Bindings.Count));
                Assert.That(ruleNames, Is.Unique);
                for (int i = 0; i < ruleNames.Count; i++)
                {
                    Assert.That(ruleNames[i], Is.EqualTo(NarrativeDerivationPlan.Bindings[i].RuleName(tag)));
                    Assert.That(ruleNames[i], Is.EqualTo(tag + NarrativeDerivationPlan.Bindings[i].RuleSuffix));
                    Assert.That(
                        NarrativeComposition.RuleNames(tag),
                        Does.Contain(ruleNames[i]),
                        "the derivation fixture declares the same rule name for this chapter.");
                }
            }
        }

        [Test]
        public void TheValueOfADerivedRowIsTheChaptersBindingOrdinal()
        {
            Assert.That(NarrativeDerivationPlan.TryValueOf(NarrativeChapters.ChapterOneTag, out int chapterOne), Is.True);
            Assert.That(NarrativeDerivationPlan.TryValueOf(NarrativeChapters.ChapterTwoTag, out int chapterTwo), Is.True);

            Assert.That(chapterOne, Is.EqualTo(1));
            Assert.That(chapterTwo, Is.EqualTo(2));
            Assert.That(chapterOne, Is.LessThan(chapterTwo));
            Assert.That(chapterOne, Is.EqualTo(NarrativeTestSupport.ChapterOne.BindingOrdinal));
            Assert.That(chapterTwo, Is.EqualTo(NarrativeTestSupport.ChapterTwo.BindingOrdinal));

            Assert.That(NarrativeDerivationPlan.TryValueOf("chapter-three", out int undeclared), Is.False);
            Assert.That(undeclared, Is.EqualTo(0), "an undeclared chapter yields no value, never a guessed ordinal.");
        }

        [Test]
        public void EveryCanonicalLineCarriesTheWholeBindingRow()
        {
            for (int c = 0; c < NarrativeChapters.Tags.Count; c++)
            {
                string tag = NarrativeChapters.Tags[c];
                IReadOnlyList<string> lines = NarrativeDerivationPlan.CanonicalLines(tag);

                Assert.That(lines.Count, Is.EqualTo(4));
                Assert.That(lines, Is.Unique);
                for (int i = 0; i < lines.Count; i++)
                {
                    for (int t = 0; t < RequiredLineTokens.Length; t++)
                    {
                        Assert.That(lines[i], Does.Contain(RequiredLineTokens[t]), "line " + i + " misses " + RequiredLineTokens[t]);
                    }

                    Assert.That(lines[i], Does.Contain("binding=" + NarrativeDerivationPlan.Bindings[i].CapabilityName));
                    Assert.That(lines[i], Does.Contain("rule=" + NarrativeDerivationPlan.Bindings[i].RuleName(tag)));
                    Assert.That(lines[i], Does.Contain("recipe=" + NarrativeDerivationPlan.Bindings[i].SelectorRecipeName));
                    Assert.That(lines[i], Does.Contain("schema=" + NarrativeDerivationPlan.Bindings[i].SchemaName));
                    Assert.That(lines[i], Does.Contain("policy=" + NarrativeDerivationPlan.Bindings[i].DeclaredPolicy));
                }

                Assert.That(lines[0], Does.Contain("binding=" + NarrativeCompositionNames.ChoiceBindingCapability));
                Assert.That(
                    lines[0],
                    Does.Contain("stratum=" + NarrativeCompositionNames.ChoiceStratum.ToString(CultureInfo.InvariantCulture)));
                Assert.That(
                    lines[0],
                    Does.Contain("input=" + NarrativeCompositionNames.DialogueBindingCapability),
                    "the choice row names the dialogue binding it reads.");
                Assert.That(
                    lines[0],
                    Does.Contain("rule=" + NarrativeCompositionNames.ChoiceRule(tag)));
                Assert.That(lines[1], Does.Contain("input=<none>"), "a binding with no input declares that explicitly.");
            }

            Assert.That(
                NarrativeDerivationPlan.CanonicalLines(NarrativeChapters.ChapterOneTag),
                Is.EqualTo(NarrativeDerivationPlan.CanonicalLines(NarrativeChapters.ChapterOneTag)),
                "the canonical lines are a pure function of the declarations (P-008).");
            Assert.That(
                NarrativeDerivationPlan.CanonicalLines(NarrativeChapters.ChapterOneTag),
                Is.Not.EqualTo(NarrativeDerivationPlan.CanonicalLines(NarrativeChapters.ChapterTwoTag)),
                "each chapter resolves its own rule names.");
        }

        private static void AssertBinding(
            string capabilityName,
            string ruleSuffix,
            string selectorRecipeName,
            string schemaName)
        {
            NarrativeBindingPlan binding = Binding(capabilityName);

            Assert.That(binding.RuleSuffix, Is.EqualTo(ruleSuffix));
            Assert.That(binding.SelectorRecipeName, Is.EqualTo(selectorRecipeName));
            Assert.That(binding.SchemaName, Is.EqualTo(schemaName));

            for (int c = 0; c < NarrativeChapters.Tags.Count; c++)
            {
                string tag = NarrativeChapters.Tags[c];
                Assert.That(binding.RuleName(tag), Is.EqualTo(tag + ruleSuffix));
            }
        }

        private static NarrativeBindingPlan Binding(string capabilityName)
        {
            for (int i = 0; i < NarrativeDerivationPlan.Bindings.Count; i++)
            {
                NarrativeBindingPlan binding = NarrativeDerivationPlan.Bindings[i];
                if (binding.CapabilityName == capabilityName)
                {
                    return binding;
                }
            }

            throw new AssertionException("the plan declares no binding for capability '" + capabilityName + "'.");
        }
    }
}
