// Hollowmere.P1_7b.EditMode.Tests - B7: the semantic index sees the cross-definition references of the Hollowmere
// content once they are typed (B1): NPC -> dialogue graph, interactable -> condition set, quest -> reward item,
// vendor -> stocked item, portal -> condition set; ImpactOf(lantern) lists the quest reward and the vendor stock.
//
// The Hollowmere assets still store P1.3/P1.4 string references (P3.1 owns them), so the test runs authoring.migrateRefs
// (apply) in memory first, links the gate and a portal to a temp Any-mode condition set ("the ferryman's favour OR the
// repaired punt") and stocks the lantern at Odd's stall; the fixture reverts every change and reloads the touched
// assets from disk, so nothing is written.
#nullable enable
using System.Collections.Generic;
using System.Linq;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Interaction.Editor;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Inventory.Editor;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
using GameCore.Gameplay.World.Editor;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_7b.EditMode.Tests
{
    public sealed class IndexEdgeTests
    {
        private HardeningTestBed? _bed;

        private HardeningTestBed Bed => _bed!;

        [SetUp]
        public void SetUp() => _bed = new HardeningTestBed("index");

        [TearDown]
        public void TearDown()
        {
            _bed?.Dispose();
            _bed = null;
        }

        [Test]
        public void IndexEdges_HollowmereContent_TypedReferencesBecomeEdges_AndImpactOfTheLanternListsRewardAndStock()
        {
            MigrationSummary migration = LogicTools.MigrateRefs(new[] { HardeningTestBed.HollowmereFolder }, true);
            Assert.That(migration.Applied, Is.True);
            TestContext.WriteLine("authoring.migrateRefs: " + migration.ChangedCount + " changed, " + migration.UnresolvedCount + " unresolved");
            foreach (var report in migration.Reports)
            {
                foreach (string line in report.Changed)
                {
                    TestContext.WriteLine("  [" + report.MigrationId + "] " + line);
                }

                foreach (string line in report.Unresolved)
                {
                    TestContext.WriteLine("  [" + report.MigrationId + "] UNRESOLVED " + line);
                }
            }

            NpcDefinition maren = HardeningTestBed.Load<NpcDefinition>("Maren");
            DialogueGraphDefinition marenGraph = HardeningTestBed.Load<DialogueGraphDefinition>("Maren");
            Assert.That(maren.Dialogue, Is.SameAs(marenGraph), "Maren's string graph id migrated to the typed dialogue reference");

            InteractableDefinition gate = HardeningTestBed.Load<InteractableDefinition>("CausewayGate");
            QuestDefinition quest = HardeningTestBed.Load<QuestDefinition>("DrownedBell");
            ItemDefinition lantern = HardeningTestBed.Load<ItemDefinition>("Lantern");
            VendorDefinition stall = HardeningTestBed.Load<VendorDefinition>("OddsStall");
            Assert.That(quest.Rewards.Any(r => r.target == lantern), Is.True, "the Drowned Bell rewards the lantern");

            List<IFactDefinition> facts = FactsOf(HardeningTestBed.Load<GameplayContentSet>("HollowmereContent"));
            Assert.That(facts.Count, Is.GreaterThanOrEqualTo(2), "the content declares facts");
            ConditionSetDefinition ferry = Bed.Create<ConditionSetDefinition>("FerryFavourOrPunt", set => set.Configure(ConditionMode.Any, new[]
            {
                ConditionEntry.Of(ConditionKind.Fact, (ScriptableObject)facts[0], CompareOp.NotEqual, 0),
                ConditionEntry.Of(ConditionKind.Fact, (ScriptableObject)facts[1], CompareOp.NotEqual, 0),
            }));
            Assert.That(ferry.Conditions[0].fact, Is.SameAs(facts[0]), "a fact condition stores its fact in the typed field");

            InteractionTools.LinkCondition(gate, ferry);
            PortalDefinition portal = AnyPortal();
            WorldTools.ConfigurePortal(portal, ferry);
            if (!stall.Stock.Any(line => line.item == lantern))
            {
                InventoryTools.SetStock(stall, lantern, 1, 5);
            }

            Bed.Runtime.Index.Rebuild();
            AssertReferences(maren, marenGraph, "dialogue");
            AssertReferences(gate, ferry, "condition");
            AssertReferences(quest, lantern, "rewards[");
            AssertReferences(stall, lantern, "stock[");
            AssertReferences(portal, ferry, "condition");

            ImpactReport impact = Bed.Runtime.Index.ImpactOf(Bed.Ref(lantern));
            List<string> impacted = impact.Items.Select(item => item.Ref.AuthoringId ?? string.Empty).ToList();
            Assert.That(impacted, Does.Contain(quest.AuthoringId), "ImpactOf(lantern) lists the quest reward");
            Assert.That(impacted, Does.Contain(stall.AuthoringId), "ImpactOf(lantern) lists the vendor stock");
            Assert.That(impact.Items.Where(i => i.Ref.AuthoringId == quest.AuthoringId).Select(i => i.Field), Has.Some.StartsWith("rewards["));
        }

        private void AssertReferences(UnityEngine.Object from, UnityEngine.Object to, string fieldPrefix)
        {
            IReadOnlyList<IndexReference> references = Bed.Runtime.Index.ReferencesTo(Bed.Ref(to));
            string fromId = Bed.Ref(from).AuthoringId ?? string.Empty;
            Assert.That(references.Where(r => r.From.AuthoringId == fromId).Select(r => r.Field), Has.Some.StartsWith(fieldPrefix),
                from.name + " -> " + to.name + " edge (" + fieldPrefix + "...) among: " + string.Join(", ", references.Select(r => r.ToString())));
            SemanticIndex snapshot = Bed.Runtime.Index.Snapshot();
            Assert.That(snapshot.Edges ?? new List<IndexEdge>(), Has.Some.Matches<IndexEdge>(e => e.Kind == EdgeKind.References && e.From.AuthoringId == fromId && e.To.AuthoringId == Bed.Ref(to).AuthoringId),
                "a references edge " + from.name + " -> " + to.name);
        }

        private static PortalDefinition AnyPortal()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(PortalDefinition), new[] { HardeningTestBed.HollowmereFolder });
            System.Array.Sort(guids, System.StringComparer.Ordinal);
            foreach (string guid in guids)
            {
                PortalDefinition? portal = AssetDatabase.LoadAssetAtPath<PortalDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (portal != null && portal.RegionA != null && portal.RegionB != null)
                {
                    return portal;
                }
            }

            Assert.Fail("Hollowmere has no portal connecting two regions");
            return null!;
        }

        private static List<IFactDefinition> FactsOf(GameplayContentSet set)
        {
            var facts = new List<IFactDefinition>();
            foreach (ScriptableObject definition in set.Definitions)
            {
                if (definition is IFactDefinition fact)
                {
                    facts.Add(fact);
                }
            }

            return facts;
        }
    }
}
