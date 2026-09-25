#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The encounter domain's pure rules (07 s3.1/3.2, P-032): an encounter is idle, becomes active when the hook
    /// condition holds, completes once it stops holding, and its hooks are the chapter's declared ordered plan,
    /// read in order and bounded by it.
    /// </summary>
    [TestFixture]
    public sealed class EncounterRuleTests
    {
        [Test]
        public void TheDeclaredStatusValuesAreTheDocumentedOnes()
        {
            Assert.That(NarrativeEncounterStatus.Idle, Is.EqualTo(0));
            Assert.That(NarrativeEncounterStatus.Active, Is.EqualTo(1));
            Assert.That(NarrativeEncounterStatus.Completed, Is.EqualTo(2));
            Assert.That(NarrativeEncounterRules.HookCount, Is.EqualTo(2));
        }

        [Test]
        public void AnIdleEncounterWithoutTheConditionIsRefusedAndStaysIdle()
        {
            bool accepted = NarrativeEncounterRules.TryReactToCondition(
                NarrativeEncounterStatus.Idle,
                false,
                out int nextStatus,
                out string refusalCode);

            Assert.That(accepted, Is.False);
            Assert.That(nextStatus, Is.EqualTo(NarrativeEncounterStatus.Idle));
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.EncounterUnchanged));
            Assert.That(
                NarrativeEncounterRules.TryReactToCondition(NarrativeEncounterStatus.Idle, false, out int nextWithoutCode),
                Is.False);
            Assert.That(nextWithoutCode, Is.EqualTo(NarrativeEncounterStatus.Idle));
        }

        [Test]
        public void AnIdleEncounterBecomesActiveOnceTheConditionHolds()
        {
            bool accepted = NarrativeEncounterRules.TryReactToCondition(
                NarrativeEncounterStatus.Idle,
                true,
                out int nextStatus,
                out string refusalCode);

            Assert.That(accepted, Is.True);
            Assert.That(nextStatus, Is.EqualTo(NarrativeEncounterStatus.Active));
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.None));
        }

        [Test]
        public void AnActiveEncounterCompletesWhenTheConditionStopsHolding()
        {
            bool accepted = NarrativeEncounterRules.TryReactToCondition(
                NarrativeEncounterStatus.Active,
                false,
                out int nextStatus,
                out string refusalCode);

            Assert.That(accepted, Is.True);
            Assert.That(nextStatus, Is.EqualTo(NarrativeEncounterStatus.Completed));
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.None));
        }

        [Test]
        public void AnActiveEncounterWithTheConditionStillHoldingIsRefusedAndStaysActive()
        {
            bool accepted = NarrativeEncounterRules.TryReactToCondition(
                NarrativeEncounterStatus.Active,
                true,
                out int nextStatus,
                out string refusalCode);

            Assert.That(accepted, Is.False);
            Assert.That(nextStatus, Is.EqualTo(NarrativeEncounterStatus.Active), "a repeated observation advances nothing.");
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.EncounterUnchanged));
        }

        [Test]
        public void ACompletedEncounterNeverTransitionsAgain()
        {
            bool[] conditions = { false, true };
            for (int i = 0; i < conditions.Length; i++)
            {
                bool accepted = NarrativeEncounterRules.TryReactToCondition(
                    NarrativeEncounterStatus.Completed,
                    conditions[i],
                    out int nextStatus,
                    out string refusalCode);

                Assert.That(accepted, Is.False);
                Assert.That(nextStatus, Is.EqualTo(NarrativeEncounterStatus.Completed));
                Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.EncounterUnchanged));
            }
        }

        [Test]
        public void TheHookPlanIsReadInDeclaredOrderForEveryChapter()
        {
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];

                Assert.That(NarrativeEncounterRules.TryGetHook(chapter, 0, out string first), Is.True);
                Assert.That(first, Is.EqualTo(chapter.BeginHookDefinition));
                Assert.That(first, Is.EqualTo(chapter.EncounterHookPlan[0]));

                Assert.That(NarrativeEncounterRules.TryGetHook(chapter, 1, out string second), Is.True);
                Assert.That(second, Is.EqualTo(chapter.OfferHookDefinition));
                Assert.That(second, Is.EqualTo(chapter.EncounterHookPlan[1]));

                Assert.That(first, Is.Not.EqualTo(second));
            }
        }

        [Test]
        public void AHookIndexOutsideThePlanIsRefusedAndReportsNoName()
        {
            ChapterDefinition chapter = NarrativeTestSupport.ChapterOne;
            int[] outside = { -1, NarrativeEncounterRules.HookCount, 3, int.MaxValue };

            for (int i = 0; i < outside.Length; i++)
            {
                Assert.That(
                    NarrativeEncounterRules.TryGetHook(chapter, outside[i], out string hookDefinition),
                    Is.False,
                    "hook index " + outside[i] + " is outside the bounded plan (P-021).");
                Assert.That(hookDefinition, Is.Empty);
            }
        }

        [Test]
        public void EveryChapterPlanHasExactlyTheDeclaredHookCount()
        {
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];
                IReadOnlyList<string> plan = NarrativeEncounterRules.HookPlan(chapter);

                Assert.That(plan.Count, Is.EqualTo(NarrativeEncounterRules.HookCount));
                Assert.That(plan.Count, Is.EqualTo(chapter.EncounterHookPlan.Count));
                for (int h = 0; h < plan.Count; h++)
                {
                    Assert.That(plan[h], Is.EqualTo(chapter.EncounterHookPlan[h]));
                    Assert.That(NarrativeEncounterRules.TryGetHook(chapter, h, out string viaIndex), Is.True);
                    Assert.That(viaIndex, Is.EqualTo(plan[h]));
                }
            }
        }

        [Test]
        public void TheSessionIdentityIsTargetFactAndVersion()
        {
            string identity = NarrativeEncounterRules.SessionIdentity(
                NarrativeCompositionNames.EncounterOak,
                NarrativeFacts.BridgePermitFactKey,
                NarrativeFacts.InitialVersion + 1);

            Assert.That(identity, Is.EqualTo("encounter-oak/chapter1.bridgePermit/v2"));
            Assert.That(
                NarrativeEncounterRules.SessionIdentity(
                    NarrativeCompositionNames.EncounterOak,
                    NarrativeFacts.BridgePermitFactKey,
                    NarrativeFacts.InitialVersion + 1),
                Is.EqualTo(identity),
                "a duplicate observation of one transition names the same session (P-054).");
            Assert.That(
                NarrativeEncounterRules.SessionIdentity(
                    NarrativeCompositionNames.EncounterOak,
                    NarrativeFacts.BridgePermitFactKey,
                    NarrativeFacts.InitialVersion + 2),
                Is.Not.EqualTo(identity));
        }

        [TestCase(NarrativeEncounterStatus.Idle, "idle")]
        [TestCase(NarrativeEncounterStatus.Active, "active")]
        [TestCase(NarrativeEncounterStatus.Completed, "completed")]
        [TestCase(9, "idle")]
        public void TheCanonicalTextFormNamesTheStatus(int status, string expected)
        {
            Assert.That(NarrativeEncounterRules.Describe(status), Is.EqualTo(expected));
        }
    }
}
