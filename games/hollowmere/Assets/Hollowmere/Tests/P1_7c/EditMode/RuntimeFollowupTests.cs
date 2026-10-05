#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Entities;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_7c.EditMode.Tests
{
    public sealed class RuntimeFollowupTests
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private NarrativeWorld? world;
        private QuestModule quests = null!;
        private LogicModule logic = null!;
        private QuestDefinition quest = null!;
        private RuleDefinition rule = null!;
        private ActionSetDefinition actions = null!;
        private long frame = 800000;
        private bool pumpEnabled;

        private T Asset<T>() where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            owned.Add(value);
            if (value is NarrativeDefinitionAsset definition) definition.SetAuthoringId(Guid.NewGuid().ToString());
            value.name = typeof(T).Name + owned.Count;
            return value;
        }

        [SetUp]
        public void SetUp()
        {
            pumpEnabled = GameCoreApplicationPump.IsEnabled;
            GameCoreApplicationPump.IsEnabled = true;
            GameCoreThreading.CaptureMainThread();
        }

        [TearDown]
        public void TearDown()
        {
            world?.Shutdown();
            world = null;
            foreach (UnityEngine.Object asset in owned) UnityEngine.Object.DestroyImmediate(asset);
            owned.Clear();
            GameCoreApplicationPump.IsEnabled = pumpEnabled;
        }

        private void Boot(int matchingRules = 0)
        {
            RegionManifest manifest = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<RegionManifest>("Assets/Hollowmere/World/Hollowmere.manifest.asset"));
            owned.Add(manifest);
            Assert.That(manifest, Is.Not.Null);
            quest = Asset<QuestDefinition>();
            quest.AddStage(new QuestStageEntry { title = "Count two signals" });
            quest.AddObjective(new ObjectiveDefinition { kind = ObjectiveKind.Interact, targetEntityId = manifest.Entities[0].authoringId, required = 2 });
            actions = Asset<ActionSetDefinition>();
            actions.Configure(new[] { new ActionEntry { kind = ActionKind.ShowMessage, text = "terminal" } });
            quest.SetConsequences(actions, actions);
            rule = Asset<RuleDefinition>();
            rule.ConfigureTrigger(TriggerKind.Manual, null, "", true, 0);
            rule.ConfigureLimits(false, 0, 1, 0);
            var assets = new List<NarrativeDefinitionAsset> { quest, actions, rule };
            for (int i = 0; i < matchingRules; i++)
            {
                RuleDefinition triggered = Asset<RuleDefinition>();
                triggered.ConfigureTrigger(TriggerKind.QuestStarted, quest, "", true, 0);
                assets.Add(triggered);
            }
            GameplayContentManifest content = Asset<GameplayContentManifest>();
            content.Assign(manifest.WorldId, "p1.7c", assets.Select(asset => new ContentEntry
            {
                kind = asset.NarrativeKind, authoringId = asset.AuthoringId, name = asset.name,
                key = NarrativeRefs.KeyOf(asset), asset = asset,
            }), Array.Empty<FactEntry>());
            quests = new QuestModule();
            logic = new LogicModule();
            world = NarrativeComposer.Boot(manifest, content, new INarrativeModule[] { logic, quests },
                new GameApplicationBootOptions { InstallPlayerLoop = false, AssignDefaultWorld = false, PumpAssertions = false, FrameClock = () => frame },
                new WorldBuildOptions { MaxEventsPerStep = 512 }, false);
            world.World.UseSceneLoader(new ImmediateSceneLoader());
            Assert.That(world.Root.Start().Outcome, Is.EqualTo(Outcome.Published));
        }

        private void Pump(int count = 12)
        {
            for (int i = 0; i < count; i++) { frame++; GameCoreApplicationPump.PumpFrame(); }
        }

        private TargetId QuestTarget => world!.Runtime.Index.TargetOf(NarrativeTargetKind.Quest, NarrativeRefs.KeyOf(quest));
        private void StartQuest()
        {
            Assert.That(world!.Runtime.Submitter.Submit(QuestIds.StartRoute, QuestTarget, QuestIds.StartCommand, NarrativeCommands.QuestStart(1)).Admitted, Is.True);
            Pump();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void P17c_01_QuestTerminalActions_UseOutboxExactlyOnce(bool fail)
        {
            Boot();
            var sink = new Messages();
            world!.Runtime.UseMessages(sink);
            StartQuest();
            RouteId route = fail ? QuestIds.FailRoute : QuestIds.CompleteRoute;
            SchemaRef schema = fail ? QuestIds.FailCommand : QuestIds.CompleteCommand;
            world.Runtime.Submitter.Submit(route, QuestTarget, schema, NarrativeCommands.QuestComplete(2));
            Pump(1);
            var rows = world.Delivery.Owner.Outbox.ToRecords();
            Assert.That(world.Delivery.Owner.Outbox.OpenCount, Is.EqualTo(1), "terminal action owed in the committing step");
            Pump();
            Assert.That(sink.Count, Is.EqualTo(1));
            world.Runtime.Submitter.Submit(route, QuestTarget, schema, NarrativeCommands.QuestComplete(3));
            Pump();
            Assert.That(sink.Count, Is.EqualTo(1));
            Assert.That(rows.Count, Is.GreaterThan(0));
            Assert.That(world.Delivery.Reinstate(rows, out string replay), Is.True, replay);
            Pump();
            Assert.That(sink.Count, Is.EqualTo(1), "terminal retention rejects a stale in-flight replay");
        }

        [Test]
        public void P17c_02_NpcAppearanceUsesVariantPrefabAcrossResidency()
        {
            RegionManifest manifest = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<RegionManifest>("Assets/Hollowmere/World/Hollowmere.manifest.asset"));
            owned.Add(manifest);
            EntityDefinition entity = UnityEngine.Object.Instantiate(manifest.Definitions.First(d => d.definition != null).definition!);
            owned.Add(entity);
            var prefab = new GameObject("appearance-prefab");
            owned.Add(prefab);
            new GameObject("appearance-marker").transform.SetParent(prefab.transform);
            VariantDefinition variant = Asset<VariantDefinition>();
            variant.Configure(prefab, Color.white);
            entity.SetVariants(new[] { variant });
            NpcDefinition npc = Asset<NpcDefinition>();
            npc.Configure(entity, "Test NPC", 1f, "", "");
            npc.SetAppearance(variant);
            NpcRoster roster = Asset<NpcRoster>();
            roster.Add(npc);
            var extension = new NpcWorldExtension(roster);
            extension.Validate(manifest);
            Assert.That(extension.Records, Is.Not.Empty);
            NpcRecord record = extension.Records[0];
            var root = new GameObject("test-views");
            owned.Add(root);
            var views = new PrefabViewBinder(root.transform, new Dictionary<int, string> { { 1, "resident" } }, new Dictionary<string, string>());
            // Exercise prefab construction in nographics without changing production's headless policy.
            typeof(PrefabViewBinder).GetField("<IsActive>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(views, true);
            views.Add(new EntityViewSpec(record.Target, record.AuthoringId, "NPC", entity, null));
            Type? binder = typeof(NpcModule).Assembly.GetType("GameCore.Gameplay.Npc.NpcAppearanceBinder");
            Assert.That(binder, Is.Not.Null);
            Activator.CreateInstance(binder!, views, extension);
            var slots = new ViewSlots();
            try
            {
                views.Present(slots);
                Assert.That(views.ViewCount, Is.Zero);
                views.OnResidencyChanged("resident", RegionResidency.Resident);
                views.Present(slots);
                Assert.That(views.TryGetView(record.Target, out GameObject? view), Is.True);
                Assert.That(view!.transform.Find("appearance-marker"), Is.Not.Null);
                views.OnResidencyChanged("resident", RegionResidency.Unloaded);
                Assert.That(views.ViewCount, Is.Zero);
                npc.SetAppearance(null);
                npc.SetAppearance(variant);
                views.OnResidencyChanged("resident", RegionResidency.Resident);
                views.Present(slots);
                Assert.That(views.TryGetView(record.Target, out view), Is.True);
                Assert.That(view!.transform.Find("appearance-marker"), Is.Not.Null);
            }
            finally { views.Clear(); }
        }

        private sealed class ViewSlots : ICommittedSlotReader
        {
            public bool TryRead(TargetId target, OwnerId owner, SlotId slot, out int value)
            {
                value = slot.Equals(GameplaySlots.Alive) || slot.Equals(GameplaySlots.Visible) || slot.Equals(GameplaySlots.Region) ? 1
                    : slot.Equals(GameplaySlots.ScaleMilli) ? 1000 : 0;
                return true;
            }
        }

        [Test]
        public void P17c_03_RespawnPreservesHiddenOverride()
        {
            EntityState state = new EntityState(1, 2, 1700, 0);
            MethodInfo? spawn = typeof(EntityRules).GetMethod("Spawn", new[] { typeof(EntityState), typeof(bool?) });
            Assert.That(spawn, Is.Not.Null);
            EntityTransition respawn = (EntityTransition)spawn!.Invoke(null, new object?[] { EntityRules.Despawn(state).State, null });
            Assert.That(respawn.State.Visible, Is.Zero);
            Assert.That(respawn.State.Variant, Is.EqualTo(2));
            Assert.That(respawn.State.ScaleMilli, Is.EqualTo(1700));
        }

        [Test]
        public void P17c_03_RuntimeSpawnUsesDefinitionVisibility()
        {
            Boot();
            var definition = world!.World.Manifest.Definitions.First(d => d.definition != null);
            EntityDefinition copy = UnityEngine.Object.Instantiate(definition.definition!);
            owned.Add(copy);
            bool before = definition.definition!.StartsVisible;
            // A definition default is consulted by the spawner; this temporary property change is restored in finally.
            EntityDefinition original = definition.definition;
            try
            {
                definition.definition = copy;
                copy.Configure(copy.Prefab, 1234, false, true);
                Assert.That(world.World.Spawner.TrySpawn(definition.authoringId, world.World.Manifest.Regions[0].authoringId,
                    0, 0, 0, 0, out TargetId spawned, out string detail), Is.True, detail);
                Assert.That(world.World.Slots.ReadOrDefault(spawned, GameplaySlots.EntityOwner, GameplaySlots.Visible, -1), Is.Zero);
            }
            finally { definition.definition = original; }
            Assert.That(original.StartsVisible, Is.EqualTo(before));
        }

        [Test]
        public void P17c_04_QuestQueryRegisteredAndReadsCommittedCopy()
        {
            Boot();
            StartQuest();
            Type? contract = typeof(QuestModule).Assembly.GetType("GameCore.Gameplay.Quest.IQuestRuntimeQuery");
            Assert.That(contract, Is.Not.Null, "runtime query contract");
            MethodInfo? registry = typeof(NarrativeRuntime).GetMethod("TryQuery");
            Assert.That(registry, Is.Not.Null);
            object?[] queryArgs = { null };
            Assert.That(registry!.MakeGenericMethod(contract!).Invoke(world!.Runtime, queryArgs), Is.True);
            object?[] readArgs = { NarrativeRefs.KeyOf(quest), null };
            Assert.That(contract!.GetMethod("TryRead")!.Invoke(queryArgs[0], readArgs), Is.True);
            var state = (QuestState)readArgs[1]!;
            Assert.That(state.Status, Is.EqualTo(QuestRules.Active));
            Assert.That(state.Counts, Is.EqualTo(new[] { 0 }));
            state.Counts[0] = 99;
            Assert.That(world.Runtime.Slots.ReadOrDefault(QuestTarget, QuestIds.Owner, QuestIds.ObjectiveCount(0), -1), Is.Zero);
        }

        [Test]
        public void P17c_05_RuleLiveConfigPreservesCountersAndChangesLimit()
        {
            Boot();
            TargetId target = world!.Runtime.Index.TargetOf(NarrativeTargetKind.Rule, NarrativeRefs.KeyOf(rule));
            void Evaluate() { world.Runtime.Submitter.Submit(LogicIds.EvaluateRoute, target, LogicIds.EvaluateCommand, NarrativeCommands.Evaluate(0, 0, 0, 0, 0)); Pump(); }
            Evaluate();
            Evaluate();
            Assert.That(world.Runtime.State.RuleFired(NarrativeRefs.KeyOf(rule)), Is.EqualTo(1));
            MethodInfo? retune = typeof(LogicModule).GetMethod("Retune");
            Assert.That(retune, Is.Not.Null, "SADR-013 live tuning entry point");
            object?[] args = { NarrativeRefs.KeyOf(rule), 1000, 3, null };
            Assert.That(retune!.Invoke(logic, args), Is.True, args[3] as string);
            Assert.That(world.Runtime.State.RuleFired(NarrativeRefs.KeyOf(rule)), Is.EqualTo(1));
            Evaluate();
            Assert.That(world.Runtime.State.RuleFired(NarrativeRefs.KeyOf(rule)), Is.EqualTo(2));
            Evaluate();
            Assert.That(world.Runtime.State.RuleFired(NarrativeRefs.KeyOf(rule)), Is.EqualTo(2), "new cooldown blocks the next evaluation");
            Assert.That(world.Runtime.Slots.ReadOrDefault(target, LogicIds.Owner, LogicIds.Counter, 0), Is.EqualTo(4));
        }

        [Test]
        public void P17c_07_TwoSignalsInOneStepBothCount()
        {
            Boot();
            StartQuest();
            int subject = NarrativeRefs.EntityKey(world!.World.Manifest.Entities[0].authoringId);
            FrozenPayload signal = NarrativeEvent.Encode(world.Runtime.Index.HubTarget, NarrativeRefs.KeyOf(actions), 0, subject, 0, 0, 0);
            world.Delivery.OnCommitted(LogicIds.ActionsRunEvent, signal, default(OperationId));
            world.Delivery.OnCommitted(LogicIds.ActionsRunEvent, signal, default(OperationId));
            Pump();
            Assert.That(world.Runtime.Slots.ReadOrDefault(QuestTarget, QuestIds.Owner, QuestIds.ObjectiveCount(0), 0), Is.EqualTo(2));
            Assert.That(world.Runtime.Slots.ReadOrDefault(QuestTarget, QuestIds.Owner, QuestIds.Status, 0), Is.EqualTo(QuestRules.Completed));
        }

        [Test]
        public void P17c_08_ExactCapacity_All256ObligationsFit_NoDrops()
        {
            Boot();
            FrozenPayload cue = NarrativeEvent.Encode(world!.Runtime.Index.HubTarget, NarrativeRefs.KeyOf(actions), 0, 0, 0, 0, 0);
            for (int i = 0; i < NarrativeDelivery.Capacity; i++)
            {
                Assert.That(world.Delivery.HasRoom(1), Is.True, "room for row " + i);
                world.Delivery.OnCommitted(LogicIds.ActionDueEvent, cue, default(OperationId));
            }
            Assert.That(world.Delivery.HasRoom(1), Is.False);
            Assert.That(world.Delivery.Owner.Outbox.OpenCount, Is.EqualTo(256));
            Assert.That(world.Delivery.Dropped, Is.Zero);
        }

        [Test]
        public void P17c_08_ManifestFanoutRefusesBeforeQuestMutation()
        {
            Boot(8);
            FrozenPayload cue = NarrativeEvent.Encode(world!.Runtime.Index.HubTarget, NarrativeRefs.KeyOf(actions), 0, 0, 0, 0, 0);
            for (int i = 0; i < 249; i++) world.Delivery.OnCommitted(LogicIds.ActionDueEvent, cue, default(OperationId));
            // Ask the actual event batch's budget with only seven rows left; eight matching rules require eight.
            MethodInfo? budget = typeof(NarrativeDelivery).GetMethod("HasRoomFor");
            Assert.That(budget, Is.Not.Null);
            Assert.That(budget!.Invoke(world.Delivery, new object[] { new[] { QuestIds.StartedEvent },
                new[] { NarrativeEvent.Encode(QuestTarget, NarrativeRefs.KeyOf(quest), 0, 0, 0, 0, 0) }, 0 }), Is.False);
            Assert.That(world.Delivery.Owner.Outbox.OpenCount, Is.EqualTo(249));
            Assert.That(world.Delivery.Dropped, Is.Zero);
        }

        private sealed class Messages : INarrativeMessageSink
        {
            public int Count;
            public void Show(NarrativeMessage message) { Count++; }
        }
    }
}
