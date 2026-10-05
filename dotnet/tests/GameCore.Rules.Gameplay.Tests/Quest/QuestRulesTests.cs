// P1.4 dotnet tests: the pure quest rules (stages, branches, latching objectives, rewards exactly once), the tracker's
// objective updates and quest.simulate along both Drowned Bell branches.
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using NUnit.Framework;
using static GameCore.Rules.Gameplay.Tests.Narrative.NarrativeFixtures;

namespace GameCore.Rules.Gameplay.Tests.Narrative
{
    [TestFixture]
    public sealed class QuestRulesTests
    {
        private static QuestState Started(QuestModel quest, List<QuestEvent> events)
        {
            var state = new QuestState(quest.Objectives.Count);
            Assert.That(QuestRules.Start(quest, state, events), Is.EqualTo(QuestRefusal.None));
            return state;
        }

        [Test]
        public void Start_EntersTheFirstStage()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            Assert.That(state.Status, Is.EqualTo(QuestRules.Active));
            Assert.That(state.Stage, Is.EqualTo(0));
            Assert.That(events.ConvertAll(e => e.Kind), Is.EqualTo(new[] { QuestEventKind.Started, QuestEventKind.StageEntered }));
            Assert.That(QuestRules.Start(quest, state, events), Is.EqualTo(QuestRefusal.AlreadyStarted));
        }

        [Test]
        public void SetObjective_OutsideTheCurrentStage_IsRefused()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            Assert.That(QuestRules.SetObjective(quest, state, 4, 1, events), Is.EqualTo(QuestRefusal.StageOutOfRange));
            Assert.That(QuestRules.SetObjective(quest, state, 99, 1, events), Is.EqualTo(QuestRefusal.ObjectiveOutOfRange));
            var inactive = new QuestState(quest.Objectives.Count);
            Assert.That(QuestRules.SetObjective(quest, inactive, 0, 1, events), Is.EqualTo(QuestRefusal.NotActive));
        }

        [Test]
        public void CompletingTheCommonObjectives_AdvancesTheStage()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            events.Clear();
            Assert.That(QuestRules.SetObjective(quest, state, 0, 1, events), Is.EqualTo(QuestRefusal.None));
            Assert.That(state.Stage, Is.EqualTo(1));
            Assert.That(events.ConvertAll(e => e.Kind), Is.EqualTo(new[] { QuestEventKind.ObjectiveUpdated, QuestEventKind.StageEntered }));
            Assert.That(events[1].A, Is.EqualTo(1));
            Assert.That(events[1].B, Is.EqualTo(0), "previous stage");
        }

        [Test]
        public void Done_Latches_EvenWhenTheCountDrops()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            QuestRules.Advance(quest, state, 2, events);
            QuestRules.SetObjective(quest, state, 4, 1, events);
            Assert.That(state.Done[4], Is.EqualTo(1));
            QuestRules.SetObjective(quest, state, 4, 0, events);
            Assert.That(state.Counts[4], Is.EqualTo(0));
            Assert.That(state.Done[4], Is.EqualTo(1), "done stays done");
            Assert.That(state.Stage, Is.EqualTo(2), "the other objectives are still open");
        }

        [Test]
        public void TheGateStage_NeedsTheGateAndOneBranch_AndRecordsIt()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            QuestRules.SetObjective(quest, state, 0, 1, events);
            QuestRules.SetObjective(quest, state, 1, 1, events);
            Assert.That(state.Stage, Is.EqualTo(1), "the gate alone is not enough");
            QuestRules.SetObjective(quest, state, 3, 1, events);
            Assert.That(state.Stage, Is.EqualTo(2));
            Assert.That(state.Branch, Is.EqualTo(2), "persuaded");
            Assert.That(QuestRules.StageSatisfied(quest, state, 2, out int _), Is.False);
        }

        [Test]
        public void Completion_GrantsEachRewardOnce_FilteredByBranch()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            state.Branch = 1;
            events.Clear();
            Assert.That(QuestRules.Advance(quest, state, 3, events), Is.EqualTo(QuestRefusal.None));
            Assert.That(QuestRules.SetObjective(quest, state, 7, 1, events), Is.EqualTo(QuestRefusal.None));
            Assert.That(state.Status, Is.EqualTo(QuestRules.Completed));
            List<QuestEvent> rewards = events.FindAll(e => e.Kind == QuestEventKind.RewardGranted);
            Assert.That(rewards, Has.Count.EqualTo(2), "the persuade-only coins are not granted on the pay branch");
            Assert.That(rewards[0].C, Is.EqualTo(Lantern));
            Assert.That(rewards[1].B, Is.EqualTo((int)RewardKind.Fact));
            Assert.That(QuestRules.Complete(quest, state, events), Is.EqualTo(QuestRefusal.NotActive), "a completed quest grants nothing again");
        }

        [Test]
        public void Advance_Next_AndPastTheLastStage_Completes()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            QuestRules.Advance(quest, state, -1, events);
            Assert.That(state.Stage, Is.EqualTo(1));
            Assert.That(QuestRules.Advance(quest, state, 9, events), Is.EqualTo(QuestRefusal.StageOutOfRange));
            QuestRules.Advance(quest, state, 3, events);
            QuestRules.Advance(quest, state, -1, events);
            Assert.That(state.Status, Is.EqualTo(QuestRules.Completed));
        }

        [Test]
        public void Fail_EndsTheQuest()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            Assert.That(QuestRules.Fail(quest, state, events), Is.EqualTo(QuestRefusal.None));
            Assert.That(state.Status, Is.EqualTo(QuestRules.Failed));
            Assert.That(events[events.Count - 1].Kind, Is.EqualTo(QuestEventKind.Failed));
            Assert.That(QuestRules.SetObjective(quest, state, 0, 1, events), Is.EqualTo(QuestRefusal.NotActive));
            Assert.That(QuestRules.Fail(quest, state, events), Is.EqualTo(QuestRefusal.NotActive));
        }

        [Test]
        public void Tracking_ReadsLevelObjectivesFromState()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            QuestRules.Advance(quest, state, 2, events);
            var world = new StateSnapshot { ActorInventoryKey = 9 };
            world.SetItem(9, Clapper, 1).SetRegion(0, Belfry);
            List<ObjectiveUpdate> updates = QuestTracking.Pending(quest, state, world, 0, new ObjectiveSignal[0]);
            Assert.That(updates.ConvertAll(u => u.Objective), Is.EqualTo(new[] { 4, 5 }));
            Assert.That(updates.ConvertAll(u => u.Count), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(QuestTracking.LevelCount(quest.Objectives[6], world, 0), Is.EqualTo(0));
        }

        [Test]
        public void Tracking_CountsEdgeSignals_ForTalkObjectives()
        {
            QuestModel quest = DrownedBell();
            var events = new List<QuestEvent>();
            QuestState state = Started(quest, events);
            QuestRules.Advance(quest, state, 3, events);
            var none = QuestTracking.Pending(quest, state, new StateSnapshot(), 0, new ObjectiveSignal[0]);
            Assert.That(none, Is.Empty, "talk objectives do not read state");
            var talked = QuestTracking.Pending(quest, state, new StateSnapshot(), 0, new[] { new ObjectiveSignal(ObjectiveKind.Talk, MarenGraph) });
            Assert.That(talked, Has.Count.EqualTo(1));
            Assert.That(talked[0].Count, Is.EqualTo(1));
            var other = QuestTracking.Pending(quest, state, new StateSnapshot(), 0, new[] { new ObjectiveSignal(ObjectiveKind.Talk, 12345) });
            Assert.That(other, Is.Empty);
        }

        [Test]
        public void Simulate_CompletesThePayBranch()
        {
            SimulationResult result = QuestSimulator.Simulate(DrownedBell(), new[]
            {
                new SimulationStep(SimulationStepKind.Fact, HeardRumour, 1, "heard_rumour"),
                new SimulationStep(SimulationStepKind.Fact, OddPaid, 1, "odd_paid"),
                new SimulationStep(SimulationStepKind.Fact, GateOpen, 1, "gate_open"),
                new SimulationStep(SimulationStepKind.Collect, Clapper, 1, "bell_clapper"),
                new SimulationStep(SimulationStepKind.Reach, Belfry, 0, "Drowned Belfry"),
                new SimulationStep(SimulationStepKind.Fact, BellRung, 1, "bell_rung"),
                new SimulationStep(SimulationStepKind.Talk, MarenGraph, 0, "Maren"),
            }, null);
            Assert.That(result.Completed, Is.True, result.Text);
            Assert.That(result.State.Branch, Is.EqualTo(1));
            Assert.That(result.Rewards, Has.Count.EqualTo(2));
            Assert.That(result.Text, Does.Contain("QuestCompleted The Drowned Bell via pay Odd"));
        }

        [Test]
        public void Simulate_CompletesThePersuadeBranch_WithItsExtraReward()
        {
            SimulationResult result = QuestSimulator.Simulate(DrownedBell(), new[]
            {
                new SimulationStep(SimulationStepKind.Fact, HeardRumour, 1, "heard_rumour"),
                new SimulationStep(SimulationStepKind.Fact, OddPersuaded, 1, "odd_persuaded"),
                new SimulationStep(SimulationStepKind.Fact, GateOpen, 1, "gate_open"),
                new SimulationStep(SimulationStepKind.Reach, Belfry, 0, "Drowned Belfry"),
                new SimulationStep(SimulationStepKind.Collect, Clapper, 1, "bell_clapper"),
                new SimulationStep(SimulationStepKind.Fact, BellRung, 1, "bell_rung"),
                new SimulationStep(SimulationStepKind.Talk, MarenGraph, 0, "Maren"),
            }, null);
            Assert.That(result.Completed, Is.True, result.Text);
            Assert.That(result.State.Branch, Is.EqualTo(2));
            Assert.That(result.Rewards, Has.Count.EqualTo(3));
        }

        [Test]
        public void Simulate_AnIncompletePath_StaysActive_AndOrderMatters()
        {
            SimulationResult result = QuestSimulator.Simulate(DrownedBell(), new[]
            {
                new SimulationStep(SimulationStepKind.Talk, MarenGraph, 0, "Maren"),
                new SimulationStep(SimulationStepKind.Fact, HeardRumour, 1, "heard_rumour"),
                new SimulationStep(SimulationStepKind.Fact, GateOpen, 1, "gate_open"),
            }, null);
            Assert.That(result.Completed, Is.False);
            Assert.That(result.State.Status, Is.EqualTo(QuestRules.Active));
            Assert.That(result.State.Stage, Is.EqualTo(1), "a talk before stage 3 does not count");
            Assert.That(result.Text, Does.EndWith("= active at stage 1"));
        }

        [Test]
        public void Outline_ListsStagesObjectivesAndRewards()
        {
            string outline = QuestSimulator.Outline(DrownedBell());
            Assert.That(outline, Does.StartWith("The Drowned Bell"));
            Assert.That(outline, Does.Contain("stage 1: Open the marsh gate"));
            Assert.That(outline, Does.Contain("Fact odd_persuaded [branch 2]"));
            Assert.That(outline, Does.Contain("reward: item lantern x1"));
            Assert.That(QuestSimulator.StatusName(QuestIds.Completed), Is.EqualTo("completed"));
        }
    }
}
