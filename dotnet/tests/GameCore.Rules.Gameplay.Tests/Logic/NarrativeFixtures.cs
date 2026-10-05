// P1.4 dotnet tests: shared models of the Drowned Bell content, built directly (no Unity), for the logic, quest and
// dialogue rule tests. The keys are the same derivations the bake uses (NarrativeKeys.FactKey for facts).
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;

namespace GameCore.Rules.Gameplay.Tests.Narrative
{
    internal static class NarrativeFixtures
    {
        public static readonly int HeardRumour = NarrativeKeys.FactKey("heard_rumour");
        public static readonly int MarenTrusts = NarrativeKeys.FactKey("maren_trusts_player");
        public static readonly int OddPaid = NarrativeKeys.FactKey("odd_paid");
        public static readonly int OddPersuaded = NarrativeKeys.FactKey("odd_persuaded");
        public static readonly int GateOpen = NarrativeKeys.FactKey("gate_open");
        public static readonly int BellRung = NarrativeKeys.FactKey("bell_rung");

        public const int OldCoin = 1001;
        public const int Clapper = 1002;
        public const int Lantern = 1003;
        public const int GateKey = 1004;
        public const int Belfry = 2001;
        public const int Village = 2002;
        public const int MarenGraph = 3001;
        public const int QuestKey = 4001;

        public static ConditionSetModel Fact(int factKey, string name, int atLeast = 1) =>
            new ConditionSetModel(
                NarrativeKeys.NameKey("test.cond." + name + "." + atLeast),
                name,
                ConditionMode.All,
                new[] { new ConditionModel(ConditionKind.Fact, factKey, 0, CompareOp.GreaterOrEqual, atLeast, "fact " + name) });

        public static ActionSetModel SetFacts(string name, params (int Key, int Value, string Label)[] facts)
        {
            var actions = new List<ActionModel>();
            foreach ((int key, int value, string label) in facts)
            {
                actions.Add(ActionModel.SetFact(key, value, "fact " + label));
            }

            return new ActionSetModel(NarrativeKeys.NameKey("test.actions." + name), name, actions);
        }

        /// <summary>The Drowned Bell: rumour, gate (pay or persuade), belfry and bell, return to Maren.</summary>
        public static QuestModel DrownedBell()
        {
            var objectives = new List<ObjectiveModel>
            {
                new ObjectiveModel(0, 0, ObjectiveKind.Fact, HeardRumour, 1, 0, "Hear the rumour", "heard_rumour"),
                new ObjectiveModel(1, 1, ObjectiveKind.Fact, GateOpen, 1, 0, "Open the marsh gate", "gate_open"),
                new ObjectiveModel(2, 1, ObjectiveKind.Fact, OddPaid, 1, 1, "Pay Odd", "odd_paid"),
                new ObjectiveModel(3, 1, ObjectiveKind.Fact, OddPersuaded, 1, 2, "Persuade Odd", "odd_persuaded"),
                new ObjectiveModel(4, 2, ObjectiveKind.Collect, Clapper, 1, 0, "Find the clapper", "bell_clapper"),
                new ObjectiveModel(5, 2, ObjectiveKind.Reach, Belfry, 1, 0, "Reach the belfry", "Drowned Belfry"),
                new ObjectiveModel(6, 2, ObjectiveKind.Fact, BellRung, 1, 0, "Ring the bell", "bell_rung"),
                new ObjectiveModel(7, 3, ObjectiveKind.Talk, MarenGraph, 1, 0, "Return to Maren", "Maren"),
            };
            var stages = new List<StageModel>
            {
                new StageModel(0, "Hear the rumour", "Maren speaks of a bell under the marsh.", new[] { 0 }, -1),
                new StageModel(1, "Open the marsh gate", "Odd keeps the gate.", new[] { 1, 2, 3 }, -1),
                new StageModel(2, "Ring the drowned bell", "The belfry stands in the water.", new[] { 4, 5, 6 }, -1),
                new StageModel(3, "Return to Maren", "Tell Maren the bell has rung.", new[] { 7 }, -1),
            };
            var rewards = new List<RewardModel>
            {
                new RewardModel(RewardKind.Item, Lantern, 1, 0, "lantern"),
                new RewardModel(RewardKind.Fact, NarrativeKeys.FactKey("maren_grateful"), 1, 0, "maren_grateful"),
                new RewardModel(RewardKind.Item, OldCoin, 2, 2, "old_coin"),
            };
            return new QuestModel(QuestKey, "The Drowned Bell", stages, objectives, rewards, null, new[] { "pay Odd", "persuade Odd" });
        }

        /// <summary>A small Maren graph: branch on bell_rung; before the bell, a choice that sets heard_rumour.</summary>
        public static DialogueGraphModel Maren()
        {
            ConditionSetModel bell = Fact(BellRung, "bell_rung");
            ConditionSetModel trusts = Fact(MarenTrusts, "maren_trusts_player");
            var nodes = new List<DialogueNodeModel>
            {
                Node(0, DialogueNodeKind.Branch, string.Empty, bell, null, 6, 1),
                Line(1, "Maren", "The bell under the marsh has been silent for forty years.", 2),
                Choice(2, new DialogueOptionModel("I'll find it.", null, 3, false), new DialogueOptionModel("Do you trust me?", trusts, 5, false)),
                Node(3, DialogueNodeKind.Action, string.Empty, null, SetFacts("rumour", (HeardRumour, 1, "heard_rumour")), 4, -1),
                Line(4, "Maren", "Odd keeps the marsh gate. Mind him.", -1),
                Line(5, "Maren", "I do.", 4),
                Line(6, "Maren", "You rang it. I heard it from here.", 7),
                Node(7, DialogueNodeKind.End, string.Empty, null, null, -1, -1),
            };
            return new DialogueGraphModel(MarenGraph, "Maren", 0, nodes, NarrativeKeys.SpeakerNameKey("Maren"), "Maren");
        }

        public static DialogueNodeModel Line(int index, string speaker, string text, int next) =>
            new DialogueNodeModel(index, DialogueNodeKind.Line, NarrativeKeys.SpeakerNameKey(speaker), speaker, text, string.Empty, string.Empty, null, null, next, -1, null);

        public static DialogueNodeModel Choice(int index, params DialogueOptionModel[] options) =>
            new DialogueNodeModel(index, DialogueNodeKind.Choice, 0, string.Empty, string.Empty, string.Empty, string.Empty, null, null, -1, -1, options);

        public static DialogueNodeModel Node(int index, DialogueNodeKind kind, string speaker, ConditionSetModel? condition, ActionSetModel? actions, int next, int elseNext) =>
            new DialogueNodeModel(index, kind, 0, speaker, string.Empty, string.Empty, string.Empty, condition, actions, next, elseNext, null);
    }
}
