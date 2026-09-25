#nullable enable
using System.Collections.Generic;
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The dialogue stage's choice validation (07 s3.2, P-042): a choice is legal only at the node the conversation
    /// sits on and only while the conversation is idle or active, accepting the permit choice requests exactly one
    /// durable fact mutation, declining requests none, and every refusal carries a stable code and no mutation.
    /// </summary>
    [TestFixture]
    public sealed class DialogueChoiceTests
    {
        private static NarrativeChoice NodeOneChoice(int choiceOrdinal)
            => new NarrativeChoice(NarrativeTestSupport.ChapterOne.OpeningNodeOrdinal, choiceOrdinal);

        [Test]
        public void TheDeclaredChoiceAndResultNodeNumbersAreTheDocumentedOnes()
        {
            Assert.That(NarrativeDialogueRules.PermitChoice, Is.EqualTo(1));
            Assert.That(NarrativeDialogueRules.DeclineChoice, Is.EqualTo(2));
            Assert.That(NarrativeDialogueRules.PermitResultNode, Is.EqualTo(2));
            Assert.That(NarrativeDialogueRules.DeclineResultNode, Is.EqualTo(1));
            Assert.That(NarrativeDialogueRules.PermitResultNode, Is.Not.EqualTo(NarrativeDialogueRules.DeclineResultNode));
            Assert.That(NarrativeChoice.EncodedLength, Is.EqualTo(8), "two big-endian int32 scalars (05 s6).");
            Assert.That(
                NarrativeTestSupport.ChapterOne.OpeningNodeOrdinal,
                Is.EqualTo(NarrativeDialogueRules.DeclineResultNode),
                "declining leaves the conversation on the chapter's opening node.");
        }

        [Test]
        public void OnlyThePermitAndDeclineChoicesAreDeclared()
        {
            Assert.That(NarrativeDialogueRules.IsDeclaredChoice(NarrativeDialogueRules.PermitChoice), Is.True);
            Assert.That(NarrativeDialogueRules.IsDeclaredChoice(NarrativeDialogueRules.DeclineChoice), Is.True);
            Assert.That(NarrativeDialogueRules.IsDeclaredChoice(0), Is.False);
            Assert.That(NarrativeDialogueRules.IsDeclaredChoice(3), Is.False);
            Assert.That(NarrativeDialogueRules.IsDeclaredChoice(-1), Is.False);
            Assert.That(NarrativeDialogueRules.IsDeclaredChoice(int.MaxValue), Is.False);
        }

        [Test]
        public void AChoiceComparesByItsTwoOrdinals()
        {
            Assert.That(new NarrativeChoice(1, 2), Is.EqualTo(new NarrativeChoice(1, 2)));
            Assert.That(new NarrativeChoice(1, 2), Is.Not.EqualTo(new NarrativeChoice(1, 1)));
            Assert.That(new NarrativeChoice(1, 2), Is.Not.EqualTo(new NarrativeChoice(2, 2)));
            Assert.That(new NarrativeChoice(1, 2).Equals(null), Is.False);
            Assert.That(new NarrativeChoice(1, 2).GetHashCode(), Is.EqualTo(new NarrativeChoice(1, 2).GetHashCode()));
            Assert.That(new NarrativeChoice(1, 2).ToString(), Is.EqualTo("choice(node=1, choice=2)"));
        }

        [Test]
        public void ThePermitChoiceIsAcceptedAndRequestsExactlyOneMutationOfTheChaptersGateConditionFact()
        {
            ChapterDefinition chapter = NarrativeTestSupport.ChapterOne;
            ChoiceValidation validation = NarrativeDialogueRules.Validate(
                chapter,
                NodeOneChoice(NarrativeDialogueRules.PermitChoice),
                chapter.OpeningNodeOrdinal,
                NarrativeConversationStatus.Idle);

            Assert.That(validation.Accepted, Is.True);
            Assert.That(validation.RequestsFactMutation, Is.True);
            Assert.That(validation.FactKey, Is.EqualTo(chapter.GateConditionFactKey));
            Assert.That(validation.FactKey, Is.EqualTo(NarrativeFacts.BridgePermitFactKey));
            Assert.That(NarrativeFacts.IsDeclared(validation.FactKey), Is.True);
            Assert.That(validation.FactValue, Is.EqualTo(NarrativeFacts.True));
            Assert.That(validation.ResultingNodeOrdinal, Is.EqualTo(NarrativeDialogueRules.PermitResultNode));
            Assert.That(validation.ResultingStatus, Is.EqualTo(NarrativeConversationStatus.Active));
            Assert.That(validation.RefusalCode, Is.EqualTo(NarrativeRefusals.None));
            Assert.That(validation.Reason, Is.Empty, "an accepted choice carries no refusal prose.");

            Assert.That(
                NarrativeFacts.TryTransition(NarrativeFacts.InitialValue, validation.FactValue, out int next),
                Is.True,
                "the requested mutation is the one legal transition from the declared initiation.");
            Assert.That(next, Is.EqualTo(NarrativeFacts.True));
        }

        [Test]
        public void TheDeclineChoiceIsAcceptedAndRequestsNoMutation()
        {
            for (int i = 0; i < NarrativeChapters.All.Count; i++)
            {
                ChapterDefinition chapter = NarrativeChapters.All[i];
                ChoiceValidation validation = NarrativeDialogueRules.Validate(
                    chapter,
                    new NarrativeChoice(chapter.OpeningNodeOrdinal, NarrativeDialogueRules.DeclineChoice),
                    chapter.OpeningNodeOrdinal,
                    NarrativeConversationStatus.Idle);

                Assert.That(validation.Accepted, Is.True);
                Assert.That(validation.ResultingNodeOrdinal, Is.EqualTo(NarrativeDialogueRules.DeclineResultNode));
                Assert.That(validation.ResultingStatus, Is.EqualTo(NarrativeConversationStatus.Active));
                Assert.That(validation.RefusalCode, Is.EqualTo(NarrativeRefusals.None));
                Assert.That(validation.FactKey, Is.Empty, "declining mutates no fact (P-044).");
                Assert.That(validation.RequestsFactMutation, Is.False);
            }
        }

        [Test]
        public void AChoiceIsStillAcceptedWhileTheConversationIsActive()
        {
            ChapterDefinition chapter = NarrativeTestSupport.ChapterOne;
            ChoiceValidation validation = NarrativeDialogueRules.Validate(
                chapter,
                NodeOneChoice(NarrativeDialogueRules.PermitChoice),
                chapter.OpeningNodeOrdinal,
                NarrativeConversationStatus.Active);

            Assert.That(validation.Accepted, Is.True);
            Assert.That(validation.ResultingStatus, Is.EqualTo(NarrativeConversationStatus.Active));
            Assert.That(validation.RefusalCode, Is.EqualTo(NarrativeRefusals.None));
        }

        [Test]
        public void AChoiceAnsweredAtAnotherNodeIsRefusedWithNodeMismatch()
        {
            ChapterDefinition chapter = NarrativeTestSupport.ChapterOne;
            ChoiceValidation validation = NarrativeDialogueRules.Validate(
                chapter,
                new NarrativeChoice(chapter.OpeningNodeOrdinal + 1, NarrativeDialogueRules.PermitChoice),
                chapter.OpeningNodeOrdinal,
                NarrativeConversationStatus.Idle);

            Assert.That(validation.Accepted, Is.False);
            Assert.That(validation.RefusalCode, Is.EqualTo(NarrativeRefusals.NodeMismatch));
            Assert.That(validation.RequestsFactMutation, Is.False);
            Assert.That(validation.Reason, Is.Not.Empty);
        }

        [Test]
        public void AnUndeclaredChoiceIsRefusedWithUndeclaredChoice()
        {
            int[] undeclared = { 0, 3, -1, int.MaxValue };
            for (int i = 0; i < undeclared.Length; i++)
            {
                ChapterDefinition chapter = NarrativeTestSupport.ChapterOne;
                ChoiceValidation validation = NarrativeDialogueRules.Validate(
                    chapter,
                    NodeOneChoice(undeclared[i]),
                    chapter.OpeningNodeOrdinal,
                    NarrativeConversationStatus.Idle);

                Assert.That(validation.Accepted, Is.False, "choice " + undeclared[i] + " is not declared.");
                Assert.That(validation.RefusalCode, Is.EqualTo(NarrativeRefusals.UndeclaredChoice));
                Assert.That(validation.RequestsFactMutation, Is.False);
            }
        }

        [Test]
        public void AConversationThatIsNeitherIdleNorActiveRefusesEveryChoice()
        {
            int[] inProgress = { NarrativeConversationStatus.Requested, NarrativeConversationStatus.Closed, 7 };
            int[] choices = { NarrativeDialogueRules.PermitChoice, NarrativeDialogueRules.DeclineChoice };

            for (int i = 0; i < inProgress.Length; i++)
            {
                for (int c = 0; c < choices.Length; c++)
                {
                    ChapterDefinition chapter = NarrativeTestSupport.ChapterOne;
                    ChoiceValidation validation = NarrativeDialogueRules.Validate(
                        chapter,
                        NodeOneChoice(choices[c]),
                        chapter.OpeningNodeOrdinal,
                        inProgress[i]);

                    Assert.That(validation.Accepted, Is.False, "status " + inProgress[i] + " accepts no choice.");
                    Assert.That(validation.RefusalCode, Is.EqualTo(NarrativeRefusals.ConversationNotIdle));
                    Assert.That(validation.RequestsFactMutation, Is.False);
                }
            }
        }

        [Test]
        public void EveryRefusedValidationReportsNoMutationAndTheUnsetValue()
        {
            ChapterDefinition chapter = NarrativeTestSupport.ChapterOne;
            var refusals = new List<ChoiceValidation>
            {
                NarrativeDialogueRules.Validate(
                    chapter,
                    new NarrativeChoice(chapter.OpeningNodeOrdinal + 1, NarrativeDialogueRules.PermitChoice),
                    chapter.OpeningNodeOrdinal,
                    NarrativeConversationStatus.Idle),
                NarrativeDialogueRules.Validate(
                    chapter,
                    NodeOneChoice(9),
                    chapter.OpeningNodeOrdinal,
                    NarrativeConversationStatus.Idle),
                NarrativeDialogueRules.Validate(
                    chapter,
                    NodeOneChoice(NarrativeDialogueRules.PermitChoice),
                    chapter.OpeningNodeOrdinal,
                    NarrativeConversationStatus.Closed),
            };

            Assert.That(refusals.Count, Is.EqualTo(3));
            for (int i = 0; i < refusals.Count; i++)
            {
                ChoiceValidation refusal = refusals[i];
                Assert.That(refusal.Accepted, Is.False);
                Assert.That(refusal.RefusalCode, Is.Not.EqualTo(NarrativeRefusals.None));
                Assert.That(refusal.RequestsFactMutation, Is.False);
                Assert.That(refusal.FactKey, Is.Empty);
                Assert.That(refusal.FactValue, Is.EqualTo(NarrativeFacts.False));
                Assert.That(refusal.Reason, Is.Not.Empty);
            }

            Assert.That(
                refusals[0].RefusalCode,
                Is.Not.EqualTo(refusals[1].RefusalCode),
                "each declared refusal kind keeps its own stable code (P-052).");
        }
    }
}
