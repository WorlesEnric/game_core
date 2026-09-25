#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// Chapter content of the reference composition (07 s3.1): the declared chapters, their binding ordininals, the
    /// durable fact each gate reads, and the immutable definition names each chapter contributes. The chapter table
    /// is looked up by exact tag and by ordinal, and an unknown one misses rather than guessing a default (P-015).
    /// </summary>
    [TestFixture]
    public sealed class ChapterContentTests
    {
        [Test]
        public void TwoChaptersAreDeclaredWithAscendingDistinctOrdininals()
        {
            Assert.That(NarrativeChapters.All.Count, Is.EqualTo(2));
            Assert.That(
                NarrativeChapters.Tags,
                Is.EqualTo(new[] { NarrativeChapters.ChapterOneTag, NarrativeChapters.ChapterTwoTag }));

            int previous = 0;
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];
                Assert.That(
                    chapter.BindingOrdinal,
                    Is.GreaterThan(previous),
                    "the chapter table is in canonical ascending ordinal order (05 s6 slot value).");
                Assert.That(NarrativeChapters.IsDeclaredOrdinal(chapter.BindingOrdinal), Is.True);
                previous = chapter.BindingOrdinal;
            }
        }

        [Test]
        public void EveryChapterGateConditionReadsADeclaredFactKey()
        {
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];
                Assert.That(
                    NarrativeFacts.IsDeclared(chapter.GateConditionFactKey),
                    Is.True,
                    "chapter '" + chapter.ChapterTag + "' reads an undeclared fact key (07 s3.1).");
                Assert.That(
                    NarrativeFacts.TryGetFactOrdinal(chapter.GateConditionFactKey, out int ordinal),
                    Is.True);
                Assert.That(ordinal, Is.GreaterThanOrEqualTo(0));
            }

            Assert.That(
                NarrativeTestSupport.ChapterOne.GateConditionFactKey,
                Is.EqualTo(NarrativeFacts.BridgePermitFactKey));
            Assert.That(
                NarrativeTestSupport.ChapterTwo.GateConditionFactKey,
                Is.EqualTo(NarrativeFacts.HarborPermitFactKey));
            Assert.That(
                NarrativeTestSupport.ChapterOne.GateConditionFactKey,
                Is.Not.EqualTo(NarrativeTestSupport.ChapterTwo.GateConditionFactKey),
                "Chapter Two reads its own fact, never Chapter One's (07 s3.3).");
        }

        [Test]
        public void EveryChapterDeclaresTwoEncounterHooksInPlanOrder()
        {
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];
                IReadOnlyList<string> plan = chapter.EncounterHookPlan;

                Assert.That(plan.Count, Is.EqualTo(NarrativeEncounterRules.HookCount));
                Assert.That(plan.Count, Is.EqualTo(2));
                Assert.That(plan[0], Is.EqualTo(chapter.BeginHookDefinition));
                Assert.That(plan[1], Is.EqualTo(chapter.OfferHookDefinition));
                Assert.That(plan[0], Is.Not.EqualTo(plan[1]));
                Assert.That(
                    plan[0],
                    Is.EqualTo(chapter.ChapterTag + NarrativeDefinitionSuffixes.BeginHook),
                    "the begin hook precedes the offer hook (07 s3.1).");
            }
        }

        [Test]
        public void EveryChapterDefinitionNameIsItsTagPlusTheDeclaredSuffix()
        {
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];
                string tag = chapter.ChapterTag;

                Assert.That(chapter.DialogueGraphDefinition, Is.EqualTo(tag + NarrativeDefinitionSuffixes.DialogueGraph));
                Assert.That(chapter.GateConditionDefinition, Is.EqualTo(tag + NarrativeDefinitionSuffixes.GateCondition));
                Assert.That(chapter.ChoiceSurfaceDefinition, Is.EqualTo(tag + NarrativeDefinitionSuffixes.ChoiceSurface));
                Assert.That(chapter.BeginHookDefinition, Is.EqualTo(tag + NarrativeDefinitionSuffixes.BeginHook));
                Assert.That(chapter.OfferHookDefinition, Is.EqualTo(tag + NarrativeDefinitionSuffixes.OfferHook));
                Assert.That(chapter.OpeningNodeOrdinal, Is.GreaterThan(0));
                Assert.That(chapter.ToString(), Is.EqualTo("chapter(" + tag + "#" + chapter.BindingOrdinal + ")"));
            }
        }

        [Test]
        public void TryGetByOrdinalIsTheInverseOfTheBindingOrdinal()
        {
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];

                Assert.That(
                    NarrativeChapters.TryGetByOrdinal(chapter.BindingOrdinal, out ChapterDefinition? byOrdinal),
                    Is.True);
                Assert.That(byOrdinal, Is.Not.Null);
                Assert.That(byOrdinal!.ChapterTag, Is.EqualTo(chapter.ChapterTag));
                Assert.That(byOrdinal.BindingOrdinal, Is.EqualTo(chapter.BindingOrdinal));
                Assert.That(
                    NarrativeChapters.TryGet(chapter.ChapterTag, out ChapterDefinition? byTag),
                    Is.True);
                Assert.That(byTag, Is.Not.Null);
                Assert.That(byTag!.BindingOrdinal, Is.EqualTo(chapter.BindingOrdinal));
            }

            Assert.That(NarrativeChapters.TryGetByOrdinal(0, out ChapterDefinition? zero), Is.False);
            Assert.That(zero, Is.Null);
            Assert.That(NarrativeChapters.TryGetByOrdinal(99, out ChapterDefinition? high), Is.False);
            Assert.That(high, Is.Null);
            Assert.That(
                NarrativeChapters.TryGetByOrdinal(NarrativeChapters.All[NarrativeChapters.All.Count - 1].BindingOrdinal + 1, out ChapterDefinition? past),
                Is.False);
            Assert.That(past, Is.Null);
        }

        [Test]
        public void AnUnknownTagOrOrdinalMissesAndGetThrows()
        {
            Assert.That(NarrativeChapters.TryGet("chapter-three", out ChapterDefinition? missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(NarrativeChapters.TryGet(string.Empty, out ChapterDefinition? empty), Is.False);
            Assert.That(empty, Is.Null);

            Assert.That(NarrativeChapters.IsDeclaredOrdinal(0), Is.False);
            Assert.That(NarrativeChapters.IsDeclaredOrdinal(-1), Is.False);
            Assert.That(NarrativeChapters.IsDeclaredOrdinal(99), Is.False);

            Assert.Throws<ArgumentException>(() => NarrativeChapters.Get("chapter-three"));
            Assert.Throws<ArgumentException>(() => NarrativeChapters.Get(string.Empty));
        }
    }
}
