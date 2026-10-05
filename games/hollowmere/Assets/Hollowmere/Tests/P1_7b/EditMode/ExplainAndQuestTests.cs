// Hollowmere.P1_7b.EditMode.Tests - B7 acceptance rows:
//   W-PLUG-05/09  interaction.explain and logic.whyNot name the failed condition of the locked Causeway Gate and the
//                 inputs it read; logic.whyNot also lists what would change them
//   W-PLUG-07     quest.simulate: a failed quest closes its dependents (transitively); a prerequisite cycle is refused
//   W-PLUG-02     despawn -> respawn keeps the entity's variant and scale overrides (pure kernel API)
//   B4            the live catalog marks structural fields (prefab, variant set, kind, slot layout) and only those
#nullable enable
using System.Linq;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Interaction.Editor;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.Quest.Editor;
using GameCore.Rules.Gameplay.Entities;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Studio.Model;
using NUnit.Framework;

namespace Hollowmere.P1_7b.EditMode.Tests
{
    public sealed class ExplainAndQuestTests
    {
        private HardeningTestBed? _bed;

        private HardeningTestBed Bed => _bed!;

        [SetUp]
        public void SetUp() => _bed = new HardeningTestBed("explain");

        [TearDown]
        public void TearDown()
        {
            _bed?.Dispose();
            _bed = null;
        }

        [Test]
        public void WPlug05_InteractionExplain_NamesTheFailedConditionOfTheLockedCausewayGate()
        {
            InteractableDefinition gate = HardeningTestBed.Load<InteractableDefinition>("CausewayGate");
            Assert.That(gate.ConditionRef, Is.Not.Empty, "the gate is gated");

            ConditionExplanation locked = InteractionTools.Explain(gate);
            TestContext.WriteLine("initial: " + locked + " | read " + string.Join(", ", locked.Inputs));
            Assert.That(locked.Known, Is.True, locked.FailedCondition);
            Assert.That(locked.Passed, Is.False, "the gate starts locked");
            Assert.That(locked.FailedIndex, Is.EqualTo(0));
            Assert.That(locked.FailedCondition, Does.Contain("gate_open"));
            Assert.That(locked.Inputs, Is.Not.Empty, "the explanation lists the inputs it read");

            ConditionExplanation open = InteractionTools.Explain(gate, "fact.gate_open=1");
            Assert.That(open.Passed, Is.True, open.ToString());
        }

        [Test]
        public void WPlug09_LogicWhyNot_NamesTheFailedConditionAndWhatWouldChangeIt()
        {
            InteractableDefinition gate = HardeningTestBed.Load<InteractableDefinition>("CausewayGate");
            GameplayContentSet content = HardeningTestBed.Load<GameplayContentSet>("HollowmereContent");

            WhyNotReport why = LogicTools.WhyNot(content, gate);
            TestContext.WriteLine(why.ToString());
            Assert.That(why.Passed, Is.False);
            Assert.That(why.FailedCondition, Does.Contain("gate_open"));
            Assert.That(why.Condition.Inputs, Is.Not.Empty);
            Assert.That(why.Fixes, Is.Not.Empty, "a rule or action set that sets gate_open is named");

            WhyNotReport open = LogicTools.WhyNot(content, gate, "fact.gate_open=1");
            Assert.That(open.Passed, Is.True, open.ToString());
        }

        [Test]
        public void WPlug07_QuestSimulate_FailureClosesDependentsTransitively_AndCyclesAreRefused()
        {
            FactDefinition flag = Bed.Create<FactDefinition>("P17bQuestFlag", f => f.Configure("p17b_quest_flag", 0, true));
            QuestDefinition first = Bed.Create<QuestDefinition>("P17bFirst", q => Stage(q, flag));
            QuestDefinition second = Bed.Create<QuestDefinition>("P17bSecond", q => Stage(q, flag));
            QuestDefinition third = Bed.Create<QuestDefinition>("P17bThird", q => Stage(q, flag));
            QuestTools.SetPrerequisites(second, new[] { first });
            QuestTools.SetPrerequisites(third, new[] { second });

            Assert.That(QuestTools.ClosedBy(first), Is.EqualTo(new[] { second, third }));
            string failed = QuestTools.Simulate(first, "start; fail");
            TestContext.WriteLine(failed);
            Assert.That(QuestTools.SimulateResult(first, "start; fail").State.Status, Is.EqualTo(QuestRules.Failed));
            Assert.That(failed, Does.Contain(AuthoringHardeningCodes.QuestClosedByPrerequisite + ": closes " + second.Title));
            Assert.That(failed, Does.Contain(AuthoringHardeningCodes.QuestClosedByPrerequisite + ": closes " + third.Title));

            string completed = QuestTools.Simulate(first, "start; fact:p17b_quest_flag=1");
            Assert.That(completed, Does.Not.Contain(AuthoringHardeningCodes.QuestClosedByPrerequisite), "only a failure closes dependents");

            QuestInspection inspection = QuestTools.InspectRuntime(first);
            Assert.That(inspection.ClosesOnFailure, Is.EqualTo(new[] { second.Title, third.Title }));
            Assert.That(QuestTools.InspectRuntime(third).Prerequisites, Is.EqualTo(new[] { second.Title }));

            var refused = Assert.Throws<System.ArgumentException>(() => QuestTools.SetPrerequisites(first, new[] { third }));
            Assert.That(refused!.Message, Does.StartWith(AuthoringHardeningCodes.QuestPrerequisiteCycle));
            Assert.That(QuestValidator.Validate(first).Select(d => d.Code), Has.No.Member(AuthoringHardeningCodes.QuestPrerequisiteCycle));
        }

        [Test]
        public void WPlug02_DespawnRespawn_KeepsTheVariantAndScaleOverrides()
        {
            EntityState placed = EntityRules.Placed(variant: 2, scaleMilli: 1200, visible: true, alive: true);
            EntityTransition despawned = EntityRules.Despawn(placed);
            Assert.That(despawned.Accepted, Is.True);
            Assert.That(EntityRules.IsPresented(despawned.State), Is.False);
            EntityTransition respawned = EntityRules.Spawn(despawned.State);
            Assert.That(respawned.Accepted, Is.True);
            Assert.That(respawned.State.Variant, Is.EqualTo(2), "the variant override survives");
            Assert.That(respawned.State.ScaleMilli, Is.EqualTo(1200), "the scale override (1.2) survives");
            Assert.That(EntityRules.IsPresented(respawned.State), Is.True);
            Assert.That(EntityRules.Spawn(respawned.State).Refusal, Is.EqualTo(EntityRefusal.AlreadyAlive));
        }

        [Test]
        public void B4_TheCatalogMarksStructuralFieldsOnly()
        {
            ToolCatalog catalog = Bed.Runtime.Registry.Catalog;
            AssertStructural(catalog, "entity.definition", "prefab", true);
            AssertStructural(catalog, "entity.definition", "variants", true);
            AssertStructural(catalog, "entity.definition", "defaultScaleMilli", false);
            AssertStructural(catalog, "interaction.interactable", "kind", true);
            AssertStructural(catalog, "interaction.interactable", "range", false);
            AssertStructural(catalog, "inventory.inventory", "slotCount", true);
            AssertStructural(catalog, "world.region", "scenePath", true);
            AssertStructural(catalog, "world.region", "displayName", false);
        }

        private static void AssertStructural(ToolCatalog catalog, string typeId, string field, bool structural)
        {
            ObjectTypeEntry? type = catalog.ObjectTypes.FirstOrDefault(t => t.TypeId == typeId);
            Assert.That(type, Is.Not.Null, typeId);
            FieldSpec? spec = type!.Fields.FirstOrDefault(f => f.Name == field);
            Assert.That(spec, Is.Not.Null, typeId + "." + field);
            Assert.That(spec!.Structural == true, Is.EqualTo(structural), typeId + "." + field);
        }

        private static void Stage(QuestDefinition quest, FactDefinition flag)
        {
            quest.AddStage(new QuestStageEntry { title = "Raise the flag" });
            quest.AddObjective(new ObjectiveDefinition { stage = 0, kind = ObjectiveKind.Fact, target = flag, required = 1, text = "Raise the flag" });
        }
    }
}
