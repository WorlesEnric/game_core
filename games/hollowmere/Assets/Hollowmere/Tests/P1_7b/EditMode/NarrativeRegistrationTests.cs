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
using UnityEngine;

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
        }

        [Test]
        public void EveryNarrativeType_CreatesAndRegistersOnTheContentSet_ThroughTheEngine()
        {
            string[] worlds = AssetDatabase.FindAssets("t:" + nameof(WorldDefinition), new[] { HardeningTestBed.HollowmereFolder });
            Assert.That(worlds.Length, Is.EqualTo(1));
            WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(AssetDatabase.GUIDToAssetPath(worlds[0]));
            GameplayContentSet content = Bed.Create<GameplayContentSet>("P17bRegistry", c => c.Configure(world, new ScriptableObject[0]));
            Bed.Runtime.Index.Rebuild();

            var registered = new List<ScriptableObject>();
            foreach (string kind in NarrativeKinds.All)
            {
                string path = HardeningTestBed.TempFolder + "/P17b_" + kind.Replace('.', '_') + ".asset";
                Bed.Apply("create", null, new JObject { ["type"] = kind, ["name"] = "P17b " + kind, ["path"] = path });
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
