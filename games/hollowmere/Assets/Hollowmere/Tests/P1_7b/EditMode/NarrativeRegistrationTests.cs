// Hollowmere.P1_7b.EditMode.Tests - every narrative definition type can be created and registered on a content set
// through the generic Studio route: `create <type>` then `assign GameplayContentSet.definitions` (append), both as
// journaled change sets through ChangeSetEngine. The definitions reference accepts category narrative.definition,
// which every NarrativeDefinitionAsset provides as a capability (P3.1's blocker).
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.World;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.P1_7b.EditMode.Tests
{
    public sealed class NarrativeRegistrationTests
    {
        private HardeningTestBed? _bed;

        private HardeningTestBed Bed => _bed!;

        [SetUp]
        public void SetUp()
        {
            _bed = new HardeningTestBed("register");
            _bed.Runtime.Index.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            _bed?.Dispose();
            _bed = null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void EveryNarrativeType_CreatesAndRegistersOnTheContentSet_ThroughTheEngine()
        {
            string[] worlds = AssetDatabase.FindAssets("t:" + nameof(WorldDefinition), new[] { HardeningTestBed.HollowmereFolder });
            Assert.That(worlds.Length, Is.EqualTo(1));
            string scenePath = HardeningTestBed.TempFolder + "/P17bRegistry.unity";
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RegionDefinition region = Bed.Create<RegionDefinition>("P17bRegistryRegion", r =>
            {
                r.Configure("Registry", scenePath);
                r.SetBounds(Vector3.zero, new Vector3(60f, 20f, 60f));
            });
            WorldDefinition world = Bed.Create<WorldDefinition>("P17bRegistryWorld", w =>
            {
                w.AddRegion(region);
                w.SetStartRegion(region);
            });
            GameplayContentSet content = Bed.Create<GameplayContentSet>("P17bRegistry", c => c.Configure(world, new ScriptableObject[0]));
            AuthoredRegion marker = new GameObject("Registry Region").AddComponent<AuthoredRegion>();
            marker.Configure(region, null);
            marker.Bounds.Configure(region.Bounds.center, region.Bounds.size);
            Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
            Bed.Runtime.Index.Rebuild();

            var registered = new List<ScriptableObject>();
            foreach (string kind in NarrativeKinds.All)
            {
                string path = HardeningTestBed.TempFolder + "/p17b_" + kind.Replace('.', '_').ToLowerInvariant() + ".asset";
                var fields = new JObject();
                string itemPath = HardeningTestBed.TempFolder + "/p17b_inventory_item.asset";
                if (kind == NarrativeKinds.Vendor)
                    fields["stock"] = new JArray(new JObject { ["item"] = itemPath, ["stock"] = 1, ["buyPrice"] = 1, ["sellPrice"] = 0 });
                else if (kind == NarrativeKinds.LootTable)
                    fields["entries"] = new JArray(new JObject { ["item"] = itemPath, ["weight"] = 1, ["min"] = 1, ["max"] = 1 });
                else if (kind == NarrativeKinds.WorldItem)
                {
                    fields["item"] = itemPath;
                    fields["region"] = AssetDatabase.GetAssetPath(region);
                    fields["count"] = 1;
                }
                else if (kind == NarrativeKinds.Quest)
                {
                    fields["stages"] = new JArray(new JObject { ["title"] = "Raise the flag" });
                    fields["objectives"] = new JArray(new JObject
                    {
                        ["kind"] = "Fact", ["stage"] = 0, ["required"] = 1,
                        ["fact"] = HardeningTestBed.TempFolder + "/p17b_narrative_fact.asset",
                    });
                }
                else if (kind == NarrativeKinds.Graph)
                {
                    fields["entry"] = 0;
                    fields["nodes"] = new JArray(new JObject { ["kind"] = "Line", ["text"] = "Hello." });
                }
                Bed.Apply("create", null, new JObject { ["type"] = kind, ["name"] = "P17b " + kind, ["path"] = path, ["fields"] = fields });
                var created = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                Assert.That(created, Is.Not.Null, "create " + kind + " wrote " + path);
                Assert.That(created, Is.InstanceOf<INarrativeDefinition>(), kind);
                Assert.That(((INarrativeDefinition)created).NarrativeKind, Is.EqualTo(kind));

                Bed.Apply("assign", content, new JObject { ["field"] = "definitions", ["value"] = path, ["append"] = true });
                registered.Add(created);
                Assert.That(content.Definitions, Is.EqualTo(registered), "assign definitions appended " + kind);
            }

            Assert.That(registered.Count, Is.EqualTo(NarrativeKinds.All.Count));
        }
    }
}
