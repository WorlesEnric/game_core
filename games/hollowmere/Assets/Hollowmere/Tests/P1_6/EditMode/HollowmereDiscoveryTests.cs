// GameCore.Studio.Hollowmere.Tests - Studio discovers the P1.1 gameplay authoring surface through the mirror attributes
// declared in GameCore.Gameplay.Contracts (bound by attribute and interface name; this assembly references no gameplay
// package): the registry exports entity.place, and the index projects Hollowmere's entity.definition assets.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Hollowmere.Tests
{
    public sealed class HollowmereDiscoveryTests
    {
        private const string HollowmereFolder = "Assets/Hollowmere";

        private string _stateRoot = string.Empty;
        private StudioRuntime? _runtime;

        [SetUp]
        public void SetUp()
        {
            _stateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p16-hollowmere-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stateRoot);
            Stopwatch watch = Stopwatch.StartNew();
            _runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, _stateRoot, "p16-hollowmere"),
                Log = new MemoryStudioLog(),
                SearchFolders = new[] { HollowmereFolder },
                IndexScope = AuthoringSourceScope.Assets,
                LoadIndexCache = false,
            });
            UnityEngine.Debug.Log("[P1.6] studio runtime create (TypeCache discovery): " + watch.ElapsedMilliseconds + " ms");
        }

        [TearDown]
        public void TearDown()
        {
            _runtime?.Dispose();
            _runtime = null;
            if (Directory.Exists(_stateRoot))
            {
                Directory.Delete(_stateRoot, true);
            }
        }

        [Test]
        public void Registry_DiscoversTheGameplayEntityPlaceTool()
        {
            StudioRuntime runtime = _runtime!;
            Stopwatch watch = Stopwatch.StartNew();
            ToolCatalog catalog = runtime.Registry.Catalog;
            UnityEngine.Debug.Log("[P1.6] catalog build (" + catalog.Tools.Count + " tools): " + watch.ElapsedMilliseconds + " ms");

            Assert.That(runtime.Registry.Find("entity.place"), Is.Not.Null, string.Join("; ", runtime.Registry.Problems));
            ToolEntry? place = null;
            foreach (ToolEntry entry in catalog.Tools)
            {
                if (entry.Id == "entity.place")
                {
                    place = entry;
                }
            }

            Assert.That(place, Is.Not.Null);
            Assert.That(place!.Tier, Is.EqualTo(ToolTier.Compose), "mirror enum converted by member name");
            Assert.That(place.RuntimeApply, Is.EqualTo(RuntimeApply.Rebuild));
            Assert.That(place.TargetType, Is.EqualTo("entity.definition"), "the first mirror-[Authorable] parameter is the target");
            Assert.That(place.Args, Has.Some.Matches<ValueSpec>(arg => arg.Name == "position" && arg.Unit == "m" && arg.Required));
            Assert.That(place.Args, Has.Some.Matches<ValueSpec>(arg => arg.Name == "yaw" && !arg.Required));
            Assert.That(place.Prerequisites, Has.Some.Matches<Prerequisite>(p => p.Requires == "world.region"));
            Assert.That(catalog.ObjectTypes, Has.Some.Matches<ObjectTypeEntry>(type => type.TypeId == "entity.definition"));
            Assert.That(catalog.Revision, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Index_ProjectsHollowmereEntityDefinitions()
        {
            List<string> minted = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:EntityDefinition", new[] { HollowmereFolder }))
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                SerializedProperty? id = asset == null ? null : new SerializedObject(asset).FindProperty("authoringId");
                if (id != null && !string.IsNullOrEmpty(id.stringValue))
                {
                    minted.Add(id.stringValue);
                }
            }

            Assert.That(minted, Is.Not.Empty, "Hollowmere ships baked entity definitions");

            Stopwatch watch = Stopwatch.StartNew();
            _runtime!.Index.Rebuild();
            SemanticIndex snapshot = _runtime.Index.Snapshot();
            UnityEngine.Debug.Log("[P1.6] hollowmere index rebuild (" + snapshot.Nodes.Count + " nodes): " + watch.ElapsedMilliseconds + " ms");

            HashSet<string> indexed = new HashSet<string>(StringComparer.Ordinal);
            foreach (IndexNode node in snapshot.Nodes)
            {
                if (node.Type == "entity.definition" && node.Ref.AuthoringId != null)
                {
                    indexed.Add(node.Ref.AuthoringId);
                }
            }

            Assert.That(indexed, Is.EquivalentTo(minted), "every minted definition asset is one entity.definition node");
            Assert.That(snapshot.Nodes, Has.Some.Matches<IndexNode>(node => node.Type == "world.region"));
        }
    }
}
