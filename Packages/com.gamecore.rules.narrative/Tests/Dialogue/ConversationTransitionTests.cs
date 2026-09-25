#nullable enable
using NUnit.Framework;

namespace GameCore.Rules.Narrative.Tests
{
    /// <summary>
    /// The declared conversation status transition table (P-032, P-052): admitting a choice moves an idle
    /// conversation to `Requested`, an admitted choice activates it, and an in-progress conversation closes. Every
    /// other pair is refused with a stable code and leaves the status untouched, and the named wrappers are exactly
    /// that table.
    /// </summary>
    [TestFixture]
    public sealed class ConversationTransitionTests
    {
        private static readonly int[] AllStatuses =
        {
            NarrativeConversationStatus.Idle,
            NarrativeConversationStatus.Requested,
            NarrativeConversationStatus.Active,
            NarrativeConversationStatus.Closed,
        };

        [Test]
        public void TheDeclaredStatusValuesAreTheDocumentedOnes()
        {
            Assert.That(NarrativeConversationStatus.Idle, Is.EqualTo(0));
            Assert.That(NarrativeConversationStatus.Requested, Is.EqualTo(1));
            Assert.That(NarrativeConversationStatus.Active, Is.EqualTo(2));
            Assert.That(NarrativeConversationStatus.Closed, Is.EqualTo(3));
            Assert.That(AllStatuses.Length, Is.EqualTo(4));
        }

        [TestCase(NarrativeConversationStatus.Idle, NarrativeConversationStatus.Requested, NarrativeConversationStatus.Requested)]
        [TestCase(NarrativeConversationStatus.Requested, NarrativeConversationStatus.Active, NarrativeConversationStatus.Active)]
        [TestCase(NarrativeConversationStatus.Requested, NarrativeConversationStatus.Closed, NarrativeConversationStatus.Closed)]
        [TestCase(NarrativeConversationStatus.Active, NarrativeConversationStatus.Closed, NarrativeConversationStatus.Closed)]
        public void ADeclaredTransitionIsAcceptedWithItsExactResultingStatus(int current, int requested, int expected)
        {
            bool accepted = NarrativeDialogueRules.TryTransition(current, requested, out int next, out string refusalCode);

            Assert.That(accepted, Is.True);
            Assert.That(next, Is.EqualTo(expected));
            Assert.That(next, Is.EqualTo(requested), "an accepted transition lands on the requested status.");
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.None));
        }

        [TestCase(NarrativeConversationStatus.Idle, NarrativeConversationStatus.Idle)]
        [TestCase(NarrativeConversationStatus.Idle, NarrativeConversationStatus.Active)]
        [TestCase(NarrativeConversationStatus.Idle, NarrativeConversationStatus.Closed)]
        [TestCase(NarrativeConversationStatus.Requested, NarrativeConversationStatus.Idle)]
        [TestCase(NarrativeConversationStatus.Requested, NarrativeConversationStatus.Requested)]
        [TestCase(NarrativeConversationStatus.Active, NarrativeConversationStatus.Idle)]
        [TestCase(NarrativeConversationStatus.Active, NarrativeConversationStatus.Requested)]
        [TestCase(NarrativeConversationStatus.Active, NarrativeConversationStatus.Active)]
        [TestCase(NarrativeConversationStatus.Closed, NarrativeConversationStatus.Idle)]
        [TestCase(NarrativeConversationStatus.Closed, NarrativeConversationStatus.Requested)]
        [TestCase(NarrativeConversationStatus.Closed, NarrativeConversationStatus.Active)]
        [TestCase(NarrativeConversationStatus.Closed, NarrativeConversationStatus.Closed)]
        [TestCase(NarrativeConversationStatus.Idle, 4)]
        [TestCase(NarrativeConversationStatus.Requested, -1)]
        public void AnIllegalTransitionIsRefusedWithItsStableCodeAndLeavesTheStatusUnchanged(int current, int requested)
        {
            bool accepted = NarrativeDialogueRules.TryTransition(current, requested, out int next, out string refusalCode);

            Assert.That(accepted, Is.False);
            Assert.That(refusalCode, Is.EqualTo(NarrativeRefusals.ConversationTransitionIllegal));
            Assert.That(next, Is.EqualTo(current), "a refused transition writes no status (P-052).");
        }

        [Test]
        public void TheWholeStatusMatrixMatchesTheDeclaredTableExactly()
        {
            int accepted = 0;
            for (int current = 0; current < AllStatuses.Length; current++)
            {
                for (int requested = 0; requested < AllStatuses.Length; requested++)
                {
                    int from = AllStatuses[current];
                    int to = AllStatuses[requested];
                    bool expected =
                        (from == NarrativeConversationStatus.Idle && to == NarrativeConversationStatus.Requested)
                        || (from == NarrativeConversationStatus.Requested && to == NarrativeConversationStatus.Active)
                        || (to == NarrativeConversationStatus.Closed
                            && (from == NarrativeConversationStatus.Requested || from == NarrativeConversationStatus.Active));

                    bool actual = NarrativeDialogueRules.TryTransition(from, to, out int next, out string refusalCode);
                    Assert.That(actual, Is.EqualTo(expected), "transition " + from + " -> " + to);
                    Assert.That(next, Is.EqualTo(expected ? to : from));
                    Assert.That(
                        refusalCode,
                        Is.EqualTo(expected ? NarrativeRefusals.None : NarrativeRefusals.ConversationTransitionIllegal));

                    if (actual)
                    {
                        accepted++;
                    }
                }
            }

            Assert.That(accepted, Is.EqualTo(4), "four of the sixteen status pairs are declared transitions.");
        }

        [Test]
        public void TheNamedWrappersAgreeWithTheDeclaredTableForEveryStatus()
        {
            for (int i = 0; i < AllStatuses.Length; i++)
            {
                int status = AllStatuses[i];

                bool expectedRequest = NarrativeDialogueRules.TryTransition(
                    status,
                    NarrativeConversationStatus.Requested,
                    out int requestedNext,
                    out string requestRefusal);
                bool actualRequest = NarrativeDialogueRules.TryRequest(status, out int requestWrapperNext);
                Assert.That(actualRequest, Is.EqualTo(expectedRequest), "TryRequest(" + status + ")");
                Assert.That(requestWrapperNext, Is.EqualTo(requestedNext));
                Assert.That(
                    requestRefusal,
                    Is.EqualTo(expectedRequest ? NarrativeRefusals.None : NarrativeRefusals.ConversationTransitionIllegal));

                bool expectedActivate = NarrativeDialogueRules.TryTransition(
                    status,
                    NarrativeConversationStatus.Active,
                    out int activateNext,
                    out string activateRefusal);
                bool actualActivate = NarrativeDialogueRules.TryActivate(status, out int activateWrapperNext);
                Assert.That(actualActivate, Is.EqualTo(expectedActivate), "TryActivate(" + status + ")");
                Assert.That(activateWrapperNext, Is.EqualTo(activateNext));
                Assert.That(
                    activateRefusal,
                    Is.EqualTo(expectedActivate ? NarrativeRefusals.None : NarrativeRefusals.ConversationTransitionIllegal));

                bool expectedClose = NarrativeDialogueRules.TryTransition(
                    status,
                    NarrativeConversationStatus.Closed,
                    out int closeNext,
                    out string closeRefusal);
                bool actualClose = NarrativeDialogueRules.TryClose(status, out int closeWrapperNext);
                Assert.That(actualClose, Is.EqualTo(expectedClose), "TryClose(" + status + ")");
                Assert.That(closeWrapperNext, Is.EqualTo(closeNext));
                Assert.That(
                    closeRefusal,
                    Is.EqualTo(expectedClose ? NarrativeRefusals.None : NarrativeRefusals.ConversationTransitionIllegal));
            }
        }

        [Test]
        public void TheDeclaredTransitionsReachEveryStatusIncludingTheClosedDisposition()
        {
            Assert.That(NarrativeDialogueRules.TryRequest(NarrativeConversationStatus.Idle, out int requested), Is.True);
            Assert.That(requested, Is.EqualTo(NarrativeConversationStatus.Requested));
            Assert.That(NarrativeDialogueRules.TryActivate(requested, out int active), Is.True);
            Assert.That(active, Is.EqualTo(NarrativeConversationStatus.Active));
            Assert.That(NarrativeDialogueRules.TryClose(active, out int closed), Is.True);
            Assert.That(closed, Is.EqualTo(NarrativeConversationStatus.Closed));
            Assert.That(
                NarrativeDialogueRules.TryClose(closed, out int stillClosed),
                Is.False,
                "closing a closed conversation is not a transition.");
            Assert.That(stillClosed, Is.EqualTo(NarrativeConversationStatus.Closed));

            Assert.That(NarrativeDialogueRules.TryClose(requested, out int closedEarly), Is.True);
            Assert.That(closedEarly, Is.EqualTo(NarrativeConversationStatus.Closed));
            Assert.That(
                NarrativeDialogueRules.TryRequest(closedEarly, out int reopened),
                Is.False,
                "a live conversation is never silently reopened.");
            Assert.That(reopened, Is.EqualTo(NarrativeConversationStatus.Closed));
        }
    }
}
