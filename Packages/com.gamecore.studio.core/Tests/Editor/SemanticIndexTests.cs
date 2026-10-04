// GameCore.Studio.Edit.Tests - semantic index: projection, references, impact, slices with truncation, cache round trip
// and incremental updates (docs/studio/03-authoring-contracts.md s3).
#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class SemanticIndexTests
    {
        private StudioTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new StudioTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        [Test]
        public void Rebuild_ProjectsNodesReferencesAndContainment()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            FixtureNpcDefinition ferryman = _bed.CreateNpc("Ferryman", "Mind the fog.", lantern);
            FixtureRegion harbor = _bed.SpawnRegion("Harbor");
            FixtureAuthoredEntity entity = _bed.SpawnEntity("FerrymanEntity", Vector3.zero, ferryman, harbor.transform);
            GameObject duckObject = new GameObject("Duck");
            FixtureDuckEntity duck = duckObject.AddComponent<FixtureDuckEntity>();
            StudioTestBed.MintId(duck);
            _bed.SaveScene();

            Stopwatch watch = Stopwatch.StartNew();
            _bed.Runtime.Index.Rebuild();
            StudioTestBed.Timing("index rebuild (6 objects)", watch);

            SemanticIndexService index = _bed.Runtime.Index;
            Assert.That(index.FindNode(_bed.Ref(lantern))?.Type, Is.EqualTo("fixture.item"));
            Assert.That(index.FindNode(_bed.Ref(ferryman))?.Type, Is.EqualTo("fixture.npc"));
            Assert.That(index.FindNode(_bed.Ref(entity))?.Type, Is.EqualTo("fixture.entity"));
            Assert.That(index.FindNode(_bed.Ref(duck))?.Type, Is.EqualTo("fixture.duck"), "a serialized authoringId field alone makes a component addressable");

            IReadOnlyList<IndexReference> toLantern = index.ReferencesTo(_bed.Ref(lantern));
            Assert.That(toLantern.Count, Is.EqualTo(1));
            Assert.That(toLantern[0].Field, Is.EqualTo("startingItem"));
            Assert.That(toLantern[0].FromType, Is.EqualTo("fixture.npc"));

            ImpactReport impact = index.ImpactOf(_bed.Ref(lantern));
            Assert.That(impact.Items, Has.Some.Matches<ImpactItem>(item => item.Ref.SameTarget(_bed.Ref(ferryman)) && item.Depth == 1));
            Assert.That(impact.Items, Has.Some.Matches<ImpactItem>(item => item.Ref.SameTarget(_bed.Ref(entity)) && item.Depth == 2));

            ImpactReport regionImpact = index.ImpactOf(_bed.Ref(harbor));
            Assert.That(regionImpact.Items, Has.Some.Matches<ImpactItem>(item => item.Relation == "contains" && item.Ref.SameTarget(_bed.Ref(entity))));
        }

        [Test]
        public void Slice_IsCutAtTheByteCapWithAnExplicitFlag()
        {
            List<FixtureItemDefinition> items = new List<FixtureItemDefinition>();
            for (int i = 0; i < 30; i++)
            {
                items.Add(_bed.CreateItem("Item" + i.ToString("00"), i));
            }

            FixtureNpcDefinition npc = _bed.CreateNpc("Keeper", "Hi", items[0]);
            _bed.Runtime.Index.Rebuild();
            AuthoringRef[] selection = { _bed.Ref(npc) };

            IndexSlice full = _bed.Runtime.Index.Slice(selection, 2, SemanticIndexService.DefaultByteCap, new[] { "fixture.item" });
            Assert.That(full.Truncated, Is.False);
            Assert.That(full.Index.Nodes.Count, Is.EqualTo(31));

            Stopwatch watch = Stopwatch.StartNew();
            IndexSlice cut = _bed.Runtime.Index.Slice(selection, 2, 2048, new[] { "fixture.item" });
            StudioTestBed.Timing("index slice (31 nodes, 2 KiB cap)", watch);
            Assert.That(cut.Truncated, Is.True);
            Assert.That(cut.OmittedNodes, Is.GreaterThan(0));
            Assert.That(cut.Bytes, Is.LessThanOrEqualTo(2048));
            Assert.That(cut.Index.FindNode(_bed.Ref(npc)), Is.Not.Null, "the selection itself is never cut");
        }

        [Test]
        public void Incremental_ChangeIsOneRevisionAndUpdatesTheStamp()
        {
            FixtureItemDefinition flask = _bed.CreateItem("OilFlask", 1f);
            SemanticIndexService index = _bed.Runtime.Index;
            index.Rebuild();
            long before = index.Revision;
            string? stampBefore = index.FindNode(_bed.Ref(flask))?.Ref.Stamp;

            index.Flush();
            Assert.That(index.Revision, Is.EqualTo(before), "a flush without changes keeps the revision");

            SerializedObject serialized = new SerializedObject(flask);
            serialized.FindProperty("weight").floatValue = 3.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            index.MarkObjectChanged(flask);
            Stopwatch watch = Stopwatch.StartNew();
            index.Flush();
            StudioTestBed.Timing("index incremental flush (1 asset)", watch);

            Assert.That(index.Revision, Is.EqualTo(before + 1));
            Assert.That(index.FindNode(_bed.Ref(flask))?.Ref.Stamp, Is.Not.EqualTo(stampBefore));

            FixtureItemDefinition added = _bed.CreateItem("Rope");
            index.MarkAssetsChanged(new[] { AssetDatabase.GetAssetPath(added) });
            Assert.That(index.FindNode(_bed.Ref(added)), Is.Not.Null);
            AuthoringRef addedRef = _bed.Ref(added, false);
            string path = AssetDatabase.GetAssetPath(added);
            AssetDatabase.DeleteAsset(path);
            index.MarkAssetsChanged(null, new[] { path });
            Assert.That(index.FindNode(addedRef), Is.Null);
        }

        [Test]
        public void Cache_RoundTripsAcrossRuntimes()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern");
            _bed.CreateNpc("Ferryman", "Hello", lantern);
            _bed.Runtime.Index.Rebuild();
            _bed.Runtime.Index.SaveCache();
            Assert.That(File.Exists(_bed.Runtime.Paths.IndexCachePath), Is.True, "Library/GameCoreStudio/index.json");
            long revision = _bed.Runtime.Index.Revision;
            AuthoringRef lanternRef = _bed.Ref(lantern);

            StudioRuntime reloaded = _bed.Reload();
            Assert.That(reloaded.Index.IsBuilt, Is.True, "the cache restores the index without a rebuild");
            Assert.That(reloaded.Index.FindNode(lanternRef), Is.Not.Null);
            Assert.That(reloaded.Index.ReferencesTo(lanternRef).Count, Is.EqualTo(1));
            Assert.That(reloaded.Index.Revision, Is.GreaterThanOrEqualTo(revision));
        }
    }
}
