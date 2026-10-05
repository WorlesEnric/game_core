// P1.4 dotnet tests: the pure dialogue walker (lines, choices, branches, action nodes, ends), graph validation and
// dialogue.preview, which must differ for Maren before and after the bell.
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Logic;
using NUnit.Framework;
using static GameCore.Rules.Gameplay.Tests.Narrative.NarrativeFixtures;

namespace GameCore.Rules.Gameplay.Tests.Narrative
{
    [TestFixture]
    public sealed class DialogueRulesTests
    {
        private static FactTableHost Host(StateSnapshot? state = null) =>
            new FactTableHost(state ?? new StateSnapshot(), null, null, new ConditionContext(0, 0));

        [Test]
        public void Start_ResolvesTheBranch_ToTheFirstLine()
        {
            DialogueStop stop = DialogueRules.Start(Maren(), Host());
            Assert.That(stop.HasEnded, Is.False);
            Assert.That(stop.Kind, Is.EqualTo(DialogueNodeKind.Line));
            Assert.That(stop.Node, Is.EqualTo(1));
            Assert.That(stop.Visited, Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void Branch_FollowsTheFact()
        {
            DialogueStop stop = DialogueRules.Start(Maren(), Host(new StateSnapshot().SetFact(BellRung, 1)));
            Assert.That(stop.Node, Is.EqualTo(6));
            Assert.That(stop.Trace[0], Does.Contain("-> yes"));
        }

        [Test]
        public void Advance_MovesToTheNextNode_AndIsRefusedAtAChoice()
        {
            DialogueGraphModel maren = Maren();
            FactTableHost host = Host();
            Assert.That(DialogueRules.Advance(maren, 1, host, out DialogueStop? stop), Is.EqualTo(DialogueRefusal.None));
            Assert.That(stop!.Kind, Is.EqualTo(DialogueNodeKind.Choice));
            Assert.That(stop.Node, Is.EqualTo(2));
            Assert.That(DialogueRules.Advance(maren, 2, host, out DialogueStop? _), Is.EqualTo(DialogueRefusal.AdvanceAtChoice));
            Assert.That(DialogueRules.Advance(maren, 99, host, out DialogueStop? _), Is.EqualTo(DialogueRefusal.BadNode));
        }

        [Test]
        public void ChoiceMask_ReflectsOptionConditions()
        {
            DialogueGraphModel maren = Maren();
            Assert.That(DialogueRules.ChoiceMask(maren.Nodes[2], Host()), Is.EqualTo(0b01));
            Assert.That(DialogueRules.ChoiceMask(maren.Nodes[2], Host(new StateSnapshot().SetFact(MarenTrusts, 1))), Is.EqualTo(0b11));
        }

        [Test]
        public void Choose_AnUnavailableOption_IsRefused()
        {
            DialogueGraphModel maren = Maren();
            Assert.That(DialogueRules.Choose(maren, 2, 1, Host(), out DialogueStop? _), Is.EqualTo(DialogueRefusal.ChoiceUnavailable));
            Assert.That(DialogueRules.Choose(maren, 2, 5, Host(), out DialogueStop? _), Is.EqualTo(DialogueRefusal.ChoiceUnavailable));
            Assert.That(DialogueRules.Choose(maren, 1, 0, Host(), out DialogueStop? _), Is.EqualTo(DialogueRefusal.NotAtChoice));
        }

        [Test]
        public void ActionNode_SetsFactsThroughTheHost_AndIsRecorded()
        {
            DialogueGraphModel maren = Maren();
            FactTableHost host = Host();
            Assert.That(DialogueRules.Choose(maren, 2, 0, host, out DialogueStop? stop), Is.EqualTo(DialogueRefusal.None));
            Assert.That(stop!.ActionNodes, Is.EqualTo(new[] { 3 }));
            Assert.That(stop.Node, Is.EqualTo(4));
            Assert.That(host.Fact(HeardRumour), Is.EqualTo(1));
            Assert.That(host.Overrides.ContainsKey(HeardRumour), Is.True);
        }

        [Test]
        public void LineWithNoSuccessor_EndsOnAdvance()
        {
            DialogueGraphModel maren = Maren();
            Assert.That(DialogueRules.Advance(maren, 4, Host(), out DialogueStop? stop), Is.EqualTo(DialogueRefusal.None));
            Assert.That(stop!.HasEnded, Is.True);
            Assert.That(stop.Ended, Is.EqualTo(DialogueEndReason.EndNode));
        }

        [Test]
        public void EndNode_EndsTheConversation()
        {
            DialogueGraphModel maren = Maren();
            Assert.That(DialogueRules.Advance(maren, 6, Host(), out DialogueStop? stop), Is.EqualTo(DialogueRefusal.None));
            Assert.That(stop!.Ended, Is.EqualTo(DialogueEndReason.EndNode));
            Assert.That(stop.Node, Is.EqualTo(7));
        }

        [Test]
        public void AChoiceWithNoAvailableOption_IsADeadEnd()
        {
            var graph = new DialogueGraphModel(5, "locked", 0, new[]
            {
                Choice(0, new DialogueOptionModel("Only if trusted", Fact(MarenTrusts, "maren_trusts_player"), -1, false)),
            }, 0, "Odd");
            DialogueStop stop = DialogueRules.Start(graph, Host());
            Assert.That(stop.HasEnded, Is.True);
            Assert.That(stop.Ended, Is.EqualTo(DialogueEndReason.DeadEnd));
        }

        [Test]
        public void ABranchCycle_EndsAsADeadEnd()
        {
            var graph = new DialogueGraphModel(6, "loop", 0, new[]
            {
                Node(0, DialogueNodeKind.Branch, string.Empty, null, null, 1, 1),
                Node(1, DialogueNodeKind.Branch, string.Empty, null, null, 0, 0),
            }, 0, string.Empty);
            DialogueStop stop = DialogueRules.Start(graph, Host());
            Assert.That(stop.Ended, Is.EqualTo(DialogueEndReason.DeadEnd));
            Assert.That(stop.Visited, Has.Count.EqualTo(DialogueRules.MaxAutoSteps));
        }

        [Test]
        public void Validate_FindsDanglingEdgesAndUnreachableNodes()
        {
            var graph = new DialogueGraphModel(7, "broken", 0, new[]
            {
                Line(0, "Pip", "Hello", 5),
                Line(1, "Pip", "Never said", -1),
                Choice(2),
            }, 0, "Pip");
            List<string> problems = DialogueRules.Validate(graph);
            Assert.That(problems, Has.Some.StartsWith("dangling: node 0"));
            Assert.That(problems, Has.Some.EqualTo("unreachable: node 1"));
            Assert.That(problems, Has.Some.Contains("a choice needs"));
            Assert.That(DialogueRules.Validate(Maren()), Is.Empty);
            Assert.That(DialogueRules.Validate(new DialogueGraphModel(8, "empty", 0, new DialogueNodeModel[0], 0, string.Empty))[0], Does.StartWith("empty"));
        }

        [Test]
        public void Preview_OfMaren_DiffersBeforeAndAfterTheBell()
        {
            DialogueGraphModel maren = Maren();
            string before = DialoguePreview.Preview(maren, new StateSnapshot(), null, null);
            string after = DialoguePreview.Preview(maren, new StateSnapshot(), null, new Dictionary<int, int> { { BellRung, 1 } });
            Assert.That(before, Is.Not.EqualTo(after));
            Assert.That(before, Does.Contain("forty years"));
            Assert.That(before, Does.Not.Contain("You rang it"));
            Assert.That(after, Does.Contain("You rang it"));
            Assert.That(after, Does.Not.Contain("forty years"));
        }

        [Test]
        public void Preview_FollowsEveryAvailableOption_AndMarksUnavailableOnes()
        {
            DialogueGraphModel maren = Maren();
            string locked = DialoguePreview.Preview(maren, new StateSnapshot(), null, null);
            Assert.That(locked, Does.Contain("1x) \"Do you trust me?\""));
            Assert.That(locked, Does.Contain("choose 0 \"I'll find it.\""));
            Assert.That(locked, Does.Not.Contain("choose 1"));
            Assert.That(locked, Does.Contain("set fact heard_rumour = 1"));

            string trusted = DialoguePreview.Preview(maren, new StateSnapshot().SetFact(MarenTrusts, 1), null, null);
            Assert.That(trusted, Does.Contain("choose 1 \"Do you trust me?\""));
            Assert.That(trusted, Does.Contain("\"I do.\""));
        }

        [Test]
        public void VisitedWords_CoverEveryNode()
        {
            var nodes = new List<DialogueNodeModel>();
            for (int i = 0; i < 33; i++)
            {
                nodes.Add(Line(i, "Hale", "line " + i, i + 1 < 33 ? i + 1 : -1));
            }

            var graph = new DialogueGraphModel(9, "long", 0, nodes, 0, "Hale");
            Assert.That(graph.VisitedWords, Is.EqualTo(2));
            Assert.That(Maren().VisitedWords, Is.EqualTo(1));
            Assert.That(DialogueRules.Reachable(graph), Has.Count.EqualTo(33));
        }
    }
}
