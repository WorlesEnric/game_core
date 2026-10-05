// P1.4 dotnet tests: the pure logic rules (conditions, action sets, rules, explain ring, request rings, model set) and
// the narrative contract ids/codecs they are keyed by.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using NUnit.Framework;
using static GameCore.Rules.Gameplay.Tests.Narrative.NarrativeFixtures;

namespace GameCore.Rules.Gameplay.Tests.Narrative
{
    [TestFixture]
    public sealed class LogicRulesTests
    {
        private static readonly ConditionContext Player = new ConditionContext(0, 0);

        [Test]
        public void FactCondition_PassesAndFails_WithTheFailedConditionNamed()
        {
            var state = new StateSnapshot();
            ConditionSetModel gate = Fact(GateOpen, "gate_open");
            ConditionResult failed = ConditionRules.Evaluate(gate, state, null, Player);
            Assert.That(failed.Passed, Is.False);
            Assert.That(failed.FailedIndex, Is.EqualTo(0));
            Assert.That(failed.FailedCondition, Does.Contain("fact gate_open >= 1").And.Contain("read 0"));
            Assert.That(failed.Inputs, Has.Count.EqualTo(1));

            state.SetFact(GateOpen, 1);
            Assert.That(ConditionRules.Evaluate(gate, state, null, Player).Passed, Is.True);
            Assert.That(ConditionRules.Evaluate(null, state, null, Player).Passed, Is.True, "no conditions hold");
        }

        [Test]
        public void AllMode_StopsAtTheFirstFailure_AnyModeNeedsOne()
        {
            var conditions = new[]
            {
                new ConditionModel(ConditionKind.Fact, GateOpen, 0, CompareOp.Equal, 1, "fact gate_open"),
                new ConditionModel(ConditionKind.Fact, BellRung, 0, CompareOp.Equal, 1, "fact bell_rung"),
            };
            var state = new StateSnapshot().SetFact(GateOpen, 1);
            ConditionResult all = ConditionRules.Evaluate(new ConditionSetModel(1, "all", ConditionMode.All, conditions), state, null, Player);
            Assert.That(all.Passed, Is.False);
            Assert.That(all.FailedIndex, Is.EqualTo(1));
            Assert.That(ConditionRules.Evaluate(new ConditionSetModel(2, "any", ConditionMode.Any, conditions), state, null, Player).Passed, Is.True);

            ConditionResult none = ConditionRules.Evaluate(new ConditionSetModel(3, "any", ConditionMode.Any, conditions), new StateSnapshot(), null, Player);
            Assert.That(none.Passed, Is.False);
            Assert.That(none.FailedCondition, Does.StartWith("none of 2"));
        }

        [Test]
        public void NestedConditionSets_EvaluateThroughTheLookup_AndCyclesFailSafely()
        {
            ConditionSetModel inner = Fact(BellRung, "bell_rung");
            var outer = new ConditionSetModel(77, "outer", ConditionMode.All, new[]
            {
                new ConditionModel(ConditionKind.ConditionSet, inner.Key, 0, CompareOp.Equal, 1, "set bell_rung"),
            });
            var table = new ConditionSetTable().Add(inner).Add(outer);
            Assert.That(ConditionRules.Evaluate(outer, new StateSnapshot().SetFact(BellRung, 1), table, Player).Passed, Is.True);
            Assert.That(ConditionRules.Evaluate(outer, new StateSnapshot(), table, Player).Passed, Is.False);
            Assert.That(ConditionRules.IsAcyclic(outer, table), Is.True);

            var self = new ConditionSetModel(88, "self", ConditionMode.All, new[]
            {
                new ConditionModel(ConditionKind.ConditionSet, 88, 0, CompareOp.Equal, 1, "set self"),
            });
            table.Add(self);
            Assert.That(ConditionRules.IsAcyclic(self, table), Is.False);
            Assert.That(ConditionRules.Evaluate(self, new StateSnapshot(), table, Player).Passed, Is.False, "a cycle ends at the depth limit and fails");
        }

        [Test]
        public void ItemRegionQuestAndTimeConditions_ReadTheActorState()
        {
            var state = new StateSnapshot { ActorInventoryKey = 500, NowMs = 9000 };
            state.SetItem(500, OldCoin, 3).SetRegion(0, Belfry).SetQuest(QuestKey, QuestField.Stage, 2).SetObjectiveDone(QuestKey, 4, true);
            Assert.That(ConditionRules.Read(new ConditionModel(ConditionKind.ItemCount, OldCoin, 0, CompareOp.GreaterOrEqual, 3, string.Empty), state, null, Player), Is.EqualTo(3));
            Assert.That(ConditionRules.Read(new ConditionModel(ConditionKind.ItemCount, OldCoin, 501, CompareOp.GreaterOrEqual, 3, string.Empty), state, null, Player), Is.EqualTo(0));
            Assert.That(ConditionRules.Read(new ConditionModel(ConditionKind.Region, 0, 0, CompareOp.Equal, Belfry, string.Empty), state, null, Player), Is.EqualTo(Belfry));
            Assert.That(ConditionRules.Read(new ConditionModel(ConditionKind.QuestStage, QuestKey, 0, CompareOp.Equal, 2, string.Empty), state, null, Player), Is.EqualTo(2));
            Assert.That(ConditionRules.Read(new ConditionModel(ConditionKind.ObjectiveDone, QuestKey, 4, CompareOp.Equal, 1, string.Empty), state, null, Player), Is.EqualTo(1));
            Assert.That(ConditionRules.Read(new ConditionModel(ConditionKind.TimeMs, 0, 0, CompareOp.GreaterOrEqual, 0, string.Empty), state, null, Player), Is.EqualTo(9000));
            Assert.That(ConditionRules.Read(new ConditionModel(ConditionKind.Always, 0, 0, CompareOp.Equal, 1, string.Empty), state, null, Player), Is.EqualTo(1));
        }

        [Test]
        public void CompareOperators_ParseAndTest()
        {
            string[] symbols = { "==", "!=", "<", "<=", ">", ">=" };
            for (int i = 0; i < symbols.Length; i++)
            {
                Assert.That(Compare.TryParse(symbols[i], out CompareOp op), Is.True, symbols[i]);
                Assert.That(Compare.Symbol(op), Is.EqualTo(symbols[i]));
            }

            Assert.That(Compare.TryParse("=", out CompareOp equal) && equal == CompareOp.Equal, Is.True);
            Assert.That(Compare.TryParse("~", out CompareOp _), Is.False);
            Assert.That(Compare.Test(3, CompareOp.GreaterOrEqual, 3) && Compare.Test(2, CompareOp.Less, 3) && !Compare.Test(2, CompareOp.Equal, 3), Is.True);
        }

        [Test]
        public void FactOverlayState_OverridesFactsOnly()
        {
            var inner = new StateSnapshot().SetFact(GateOpen, 0).SetFact(BellRung, 1);
            var overlay = new FactOverlayState(inner, new Dictionary<int, int> { { GateOpen, 1 } });
            Assert.That(overlay.Fact(GateOpen), Is.EqualTo(1));
            Assert.That(overlay.Fact(BellRung), Is.EqualTo(1));
            Assert.That(inner.Fact(GateOpen), Is.EqualTo(0), "the inner state is untouched");
        }

        [Test]
        public void Flatten_ExpandsNestedSetsInOrder_AndReportsCycles()
        {
            ActionSetModel inner = SetFacts("inner", (BellRung, 1, "bell_rung"), (GateOpen, 1, "gate_open"));
            var outer = new ActionSetModel(10, "outer", new[]
            {
                ActionModel.SetFact(HeardRumour, 1, "fact heard_rumour"),
                new ActionModel(ActionKind.RunActionSet, inner.Key, 0, 0, string.Empty, string.Empty, "inner"),
                ActionModel.Grant(Lantern, 1, "lantern"),
            });
            var table = new ActionSetTable().Add(inner).Add(outer);
            var problems = new List<string>();
            ActionSetModel flat = ActionRules.Flatten(outer, table, problems);
            Assert.That(problems, Is.Empty);
            Assert.That(flat.Actions, Has.Count.EqualTo(4));
            Assert.That(flat.Actions[0].Key, Is.EqualTo(HeardRumour));
            Assert.That(flat.Actions[1].Key, Is.EqualTo(BellRung));
            Assert.That(flat.Actions[3].Kind, Is.EqualTo(ActionKind.Grant));
            Assert.That(flat.OnlyFacts, Is.False);
            Assert.That(inner.OnlyFacts, Is.True);

            var loop = new ActionSetModel(11, "loop", new[] { new ActionModel(ActionKind.RunActionSet, 11, 0, 0, string.Empty, string.Empty, "loop") });
            table.Add(loop);
            ActionSetModel none = ActionRules.Flatten(loop, table, problems);
            Assert.That(none.Actions, Is.Empty);
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("cycle"));
        }

        [Test]
        public void Flatten_CapsTheActionCount_AndAddFactSaturates()
        {
            var many = new List<ActionModel>();
            for (int i = 0; i < ActionRules.MaxActions + 5; i++)
            {
                many.Add(ActionModel.SetFact(HeardRumour, i, "fact heard_rumour"));
            }

            var problems = new List<string>();
            ActionSetModel flat = ActionRules.Flatten(new ActionSetModel(12, "many", many), new ActionSetTable(), problems);
            Assert.That(flat.Actions, Has.Count.EqualTo(ActionRules.MaxActions));
            Assert.That(problems, Has.Count.EqualTo(1));

            var add = new ActionModel(ActionKind.AddFact, HeardRumour, 0, 5, string.Empty, string.Empty, string.Empty);
            Assert.That(ActionRules.FactValueAfter(add, 2), Is.EqualTo(7));
            Assert.That(ActionRules.FactValueAfter(add, int.MaxValue - 1), Is.EqualTo(int.MaxValue));
            Assert.That(ActionRules.FactValueAfter(ActionModel.SetFact(HeardRumour, 3, string.Empty), 99), Is.EqualTo(3));
            Assert.That(add.Describe(), Is.EqualTo("add 5 to " + HeardRumour));
        }

        [Test]
        public void Triggers_MatchKindKeyAndValueFilters()
        {
            var anyFact = new TriggerModel(TriggerKind.FactSet, 0, TriggerModel.AnyValue, string.Empty);
            var bellOne = new TriggerModel(TriggerKind.FactSet, BellRung, 1, "bell_rung");
            Assert.That(RuleRules.Matches(anyFact, TriggerKind.FactSet, GateOpen, 7), Is.True);
            Assert.That(RuleRules.Matches(bellOne, TriggerKind.FactSet, BellRung, 1), Is.True);
            Assert.That(RuleRules.Matches(bellOne, TriggerKind.FactSet, BellRung, 0), Is.False);
            Assert.That(RuleRules.Matches(bellOne, TriggerKind.FactSet, GateOpen, 1), Is.False);
            Assert.That(RuleRules.Matches(bellOne, TriggerKind.ItemGranted, BellRung, 1), Is.False);
            Assert.That(RuleRules.Matches(new TriggerModel(TriggerKind.Manual, 0, TriggerModel.AnyValue, string.Empty), TriggerKind.Manual, 0, 0), Is.False);
            Assert.That(bellOne.Describe(), Is.EqualTo("FactSet bell_rung = 1"));
        }

        [Test]
        public void Decide_Once_FiresOnlyTheFirstTime()
        {
            var rule = new RuleModel(5, "once", new TriggerModel(TriggerKind.FactSet, 0, TriggerModel.AnyValue, string.Empty), null, null, true, 0, 0, 0);
            RuleDecision first = RuleRules.Decide(rule, new RuleState(0, 0, 0), 0, new StateSnapshot(), null, Player);
            Assert.That(first.Fire, Is.True);
            Assert.That(first.Next.Fired, Is.EqualTo(1));
            RuleDecision second = RuleRules.Decide(rule, first.Next, 0, new StateSnapshot(), null, Player);
            Assert.That(second.Fire, Is.False);
            Assert.That(second.Reason, Is.EqualTo(SkipReason.Once));
            Assert.That(second.Next.Counter, Is.EqualTo(2), "every evaluation counts");
            Assert.That(second.Conditions, Is.Null, "a gated rule reads no conditions");
        }

        [Test]
        public void Decide_CooldownAndMaxFires_GateTheRule()
        {
            var rule = new RuleModel(6, "cool", new TriggerModel(TriggerKind.FactSet, 0, TriggerModel.AnyValue, string.Empty), null, null, false, 1000, 2, 0);
            RuleDecision a = RuleRules.Decide(rule, new RuleState(0, 0, 0), 500, new StateSnapshot(), null, Player);
            Assert.That(a.Fire, Is.True);
            Assert.That(a.Next.CooldownUntilMs, Is.EqualTo(1500));
            RuleDecision b = RuleRules.Decide(rule, a.Next, 1499, new StateSnapshot(), null, Player);
            Assert.That(b.Reason, Is.EqualTo(SkipReason.Cooldown));
            RuleDecision c = RuleRules.Decide(rule, b.Next, 1500, new StateSnapshot(), null, Player);
            Assert.That(c.Fire, Is.True);
            RuleDecision d = RuleRules.Decide(rule, c.Next, 9000, new StateSnapshot(), null, Player);
            Assert.That(d.Reason, Is.EqualTo(SkipReason.MaxFires));
            Assert.That(RuleDecision.ReasonName(d.Reason), Is.EqualTo("maxFires"));
        }

        [Test]
        public void Decide_ConditionFailure_CountsButDoesNotFire()
        {
            var rule = new RuleModel(7, "gate", new TriggerModel(TriggerKind.FactSet, BellRung, 1, string.Empty), Fact(GateOpen, "gate_open"), null, false, 0, 0, 0);
            RuleDecision skipped = RuleRules.Decide(rule, new RuleState(0, 0, 3), 0, new StateSnapshot(), null, Player);
            Assert.That(skipped.Fire, Is.False);
            Assert.That(skipped.Reason, Is.EqualTo(SkipReason.Condition));
            Assert.That(skipped.Next.Counter, Is.EqualTo(4));
            Assert.That(skipped.Next.Fired, Is.EqualTo(0));
            Assert.That(skipped.Conditions!.FailedCondition, Does.Contain("gate_open"));
        }

        [Test]
        public void Select_OrdersByPriorityThenKey()
        {
            var trigger = new TriggerModel(TriggerKind.QuestCompleted, QuestKey, TriggerModel.AnyValue, string.Empty);
            var rules = new[]
            {
                new RuleModel(30, "c", trigger, null, null, false, 0, 0, 1),
                new RuleModel(20, "b", trigger, null, null, false, 0, 0, 0),
                new RuleModel(10, "a", trigger, null, null, false, 0, 0, 1),
                new RuleModel(40, "other", new TriggerModel(TriggerKind.QuestFailed, QuestKey, TriggerModel.AnyValue, string.Empty), null, null, false, 0, 0, 0),
            };
            List<RuleModel> selected = RuleRules.Select(rules, TriggerKind.QuestCompleted, QuestKey, 0);
            Assert.That(selected.ConvertAll(r => r.Name), Is.EqualTo(new[] { "b", "a", "c" }));
        }

        [Test]
        public void BoundedRing_KeepsTheNewestItems()
        {
            var ring = new BoundedRing<int>(256);
            for (int i = 0; i < 300; i++)
            {
                ring.Add(i);
            }

            Assert.That(ring.Count, Is.EqualTo(256));
            Assert.That(ring.Added, Is.EqualTo(300));
            Assert.That(ring.At(0), Is.EqualTo(299));
            Assert.That(ring.At(255), Is.EqualTo(44));
            Assert.That(ring.Recent(3), Is.EqualTo(new[] { 299, 298, 297 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => ring.At(256));
            ring.Clear();
            Assert.That(ring.Count, Is.EqualTo(0));
        }

        [Test]
        public void RequestRing_RemembersTheLastEightRequests()
        {
            var ring = new int[RequestRing.Size];
            int head = 0;
            for (int id = 1; id <= 10; id++)
            {
                RequestRing.Push(ring, head, id, out head);
            }

            Assert.That(RequestRing.Contains(ring, 10), Is.True);
            Assert.That(RequestRing.Contains(ring, 3), Is.True);
            Assert.That(RequestRing.Contains(ring, 2), Is.False, "the ring forgets the oldest");
            Assert.That(RequestRing.Contains(ring, 0), Is.False, "request id 0 is never deduplicated");
            Assert.That(head, Is.EqualTo(10 % RequestRing.Size));
            Assert.That(RequestRing.Size, Is.EqualTo(NarrativeKeys.RequestRingSize));
        }

        [Test]
        public void ModelSet_ResolvesRefs_ReportsCollisions_AndSizesSlots()
        {
            var set = new NarrativeModelSet();
            set.AddFact(new FactModel(GateOpen, "gate_open", 0, true));
            set.AddItem(new ItemModel(OldCoin, "Old Coin", 10, 5, 1, null, 0), "coin-id");
            set.AddItem(new ItemModel(OldCoin, "Duplicate", 1, 1, 1, null, 0), "dup-id");
            set.AddInventory(new InventoryModel(600, "Pack", 12, 0, 0, null, 0, true), "pack-id");
            set.AddInventory(new InventoryModel(601, "Chest", 4, 0, 0, null, 0, false), "chest-id");
            set.AddQuest(DrownedBell(), "quest-id");
            set.AddGraph(Maren(), "maren-id");
            set.Freeze();
            Assert.That(set.TryResolve("coin-id", out int coin) && coin == OldCoin, Is.True);
            Assert.That(set.TryResolve("gate_open", out int fact) && fact == GateOpen, Is.True);
            Assert.That(set.Problems, Has.Count.EqualTo(1));
            Assert.That(set.Problems[0], Does.Contain("collides"));
            Assert.That(set.MaxInventorySlots, Is.EqualTo(12));
            Assert.That(set.MaxObjectives, Is.EqualTo(8));
            Assert.That(set.MaxVisitedWords, Is.EqualTo(1));
            Assert.That(set.PlayerInventory!.Name, Is.EqualTo("Pack"));
            Assert.That(set.TryGetFactByName("gate_open", out FactModel? found) && found!.Key == GateOpen, Is.True);
            Assert.Throws<InvalidOperationException>(() => set.AddFact(new FactModel(BellRung, "bell_rung", 0, true)));
        }

        [Test]
        public void FactKeysAndSlots_AreStableDerivations()
        {
            Assert.That(NarrativeKeys.FactKey("bell_rung"), Is.EqualTo(NarrativeKeys.FactKey("bell_rung")));
            Assert.That(NarrativeKeys.FactKey("bell_rung"), Is.GreaterThan(0));
            Assert.That(NarrativeKeys.FactKey("bell_rung"), Is.Not.EqualTo(NarrativeKeys.FactKey("gate_open")));
            Assert.That(NarrativeKeys.IsValidFactName("maren_trusts_player"), Is.True);
            Assert.That(NarrativeKeys.IsValidFactName("Bell"), Is.False);
            Assert.That(NarrativeKeys.IsValidFactName("1bell"), Is.False);
            Assert.That(NarrativeKeys.IsValidFactName("bell-rung"), Is.False);
            Assert.Throws<ArgumentException>(() => NarrativeKeys.FactKey("Bad Name"));
            Assert.That(DialogueIds.Fact("bell_rung"), Is.EqualTo(SlotNames.Of("narrative", "fact.bell_rung")));
            Assert.That(InventoryIds.Item(3), Is.Not.EqualTo(InventoryIds.Count(3)));
            Assert.That(QuestIds.ObjectiveCount(2), Is.Not.EqualTo(QuestIds.ObjectiveDone(2)));
            Assert.That(NarrativeCatalogNames.Logic.Entries, Has.Count.EqualTo(4));
            Assert.That(NarrativeDiagnosticCodes.All, Is.Unique);
        }

        [Test]
        public void NarrativeEvent_RoundTripsFortyBytes()
        {
            var target = new TargetId(new Id128(0x0102030405060708UL, 0x1112131415161718UL));
            FrozenPayload payload = NarrativeEvent.Encode(target, 1, -2, 3, int.MaxValue, int.MinValue, 6);
            Assert.That(payload.Length, Is.EqualTo(NarrativeEvent.Length));
            Assert.That(NarrativeEvent.TryDecode(payload, out NarrativeEvent decoded), Is.True);
            Assert.That(decoded.Target, Is.EqualTo(target));
            Assert.That(new[] { decoded.A, decoded.B, decoded.C, decoded.D, decoded.E, decoded.F }, Is.EqualTo(new[] { 1, -2, 3, int.MaxValue, int.MinValue, 6 }));
            Assert.That(NarrativeEvent.TryDecode(NarrativeCommands.Ints(1, 2), out NarrativeEvent _), Is.False);
            Assert.That(NarrativeCommands.TryReadInts(NarrativeCommands.Grant(5, 6, 7).Bytes, 3, out int[] values), Is.True);
            Assert.That(values, Is.EqualTo(new[] { 5, 6, 7 }));
            Assert.That(NarrativeCommands.TryReadInts(NarrativeCommands.Grant(5, 6, 7).Bytes, 2, out int[] _), Is.False);
        }

        [Test]
        public void Seams_ConvertInteractionContexts_AndDefaultHeadless()
        {
            const string gate = "0b44e6c8-4c3c-483f-9202-57dc00024a20";
            var interaction = new InteractionContext(gate, 0, 77, 0, new EmptySlots());
            EvaluationContext converted = EvaluationContext.FromInteraction(interaction);
            Assert.That(converted.ActorKey, Is.EqualTo(77));
            Assert.That(converted.SubjectAuthoringId, Is.EqualTo(gate));
            Assert.That(converted.SubjectKey, Is.EqualTo(AuthoringIds.StableKey(gate)), "a missing target key is derived from the id");
            Assert.That(converted.Subject, Is.EqualTo(AuthoringIds.TargetIdFor(gate)));
            Assert.That(EvaluationContext.FromInteraction(null).SubjectKey, Is.EqualTo(0));
            Assert.That(EvaluationContext.FromInteraction(new InteractionContext("x", 5, 1, 0, new EmptySlots())).SubjectKey, Is.EqualTo(5));
            Assert.That(new NotConfiguredMediaGateway().RequestVoiceLine(new VoiceGenerationRequest("g", 1, "Maren", "hi", string.Empty)).Status,
                Is.EqualTo(MediaGenerationStatus.NotConfigured));
            var messages = new NullNarrativeMessageSink();
            messages.Show(new NarrativeMessage("The bell has rung.", "rule:return"));
            Assert.That(messages.Diagnostics[0], Does.Contain("no message sink"));
            var view = new NullDialogueView();
            Assert.That(view.Last.Active, Is.False);
            Assert.That(EvaluationContext.ForSubject("not-an-id").SubjectKey, Is.EqualTo(0));
        }

        private sealed class EmptySlots : ICommittedSlotReader
        {
            public bool TryRead(TargetId target, OwnerId owner, SlotId slot, out int value)
            {
                value = 0;
                return false;
            }
        }
    }
}
