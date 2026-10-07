// W-ETOS-04: drive pre-existing gateway/runtime APIs and inspect authenticated HTTP publication.
#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameCore.Studio.Etos.Tests
{
    public sealed class IndexPublicationTests
    {
        private string _folder = null!;
        private string _state = null!;
        private StudioRuntime _runtime = null!;
        private FakeCompanion _server = null!;
        private CompanionClient _client = null!;
        private MainThreadQueue _queue = null!;
        private EtosAgentGateway _gateway = null!;
        private FixtureDialogueDefinition _definition = null!;
        private string _authoringId = null!;

        [SetUp]
        public void SetUp()
        {
            string leaf = "EtosIndex" + Guid.NewGuid().ToString("N");
            _folder = "Assets/" + leaf;
            AssetDatabase.CreateFolder("Assets", leaf);
            _state = Path.Combine(Path.GetTempPath(), leaf);
            Directory.CreateDirectory(_state);
            _definition = ScriptableObject.CreateInstance<FixtureDialogueDefinition>();
            _definition.lines.Add(new FixtureLine { text = "Before" });
            _authoringId = Guid.NewGuid().ToString("D");
            var serialized = new SerializedObject(_definition);
            serialized.FindProperty("authoringId").stringValue = _authoringId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(_definition, _folder + "/Dialogue.asset");
            var log = new MemoryStudioLog();
            _runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, _state, "publication-test"),
                Log = log,
                TypeSource = () => new[] { typeof(FixtureDialogueDefinition) },
                ToolMethodSource = () => Array.Empty<System.Reflection.MethodInfo>(),
                SearchFolders = new[] { _folder },
                IndexScope = AuthoringSourceScope.Assets,
                LoadIndexCache = false,
            });
            _server = new FakeCompanion();
            _server.Start();
            _client = new CompanionClient(new EtosClientOptions { NodeUrl = _server.NodeUrl, ProjectId = new string('a', 64) },
                new EtosCredentials(FakeCompanion.AppKey, _server.NodeUrl, "fake"));
            _queue = new MainThreadQueue(log);
            _gateway = new EtosAgentGateway(_client, _runtime, _queue, new MemoryCursorStore(), log: log);
        }

        [TearDown]
        public void TearDown()
        {
            _gateway.Dispose();
            _client.Dispose();
            _server.Dispose();
            _runtime.Dispose();
            AssetDatabase.DeleteAsset(_folder);
            if (Directory.Exists(_state)) Directory.Delete(_state, true);
        }

        private JObject[] Deltas() => _server.Calls.Where(call => call.Path.EndsWith("/v1/index/delta", StringComparison.Ordinal))
            .Select(call => JObject.Parse(call.Body)).ToArray();

        private string? Text(JObject delta) => (string?)((JArray)delta["nodes"]!).OfType<JObject>()
            .FirstOrDefault(node => (string?)node["ref"]?["authoringId"] == _authoringId)?["fields"]?["lines"]?["value"]?[0]?["text"];

        private IEnumerator Until(Func<bool> condition)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                _queue.Pump();
                _gateway.Tick();
                Assert.That(DateTime.UtcNow, Is.LessThan(end), "lifecycle delta was not published");
                yield return null;
            }
            _queue.Pump();
            _gateway.Tick();
        }

        [UnityTest]
        public IEnumerator W_ETOS_04_OpenApplyUndoRedoAndRebuildPublishCommittedValues()
        {
            _gateway.Start();
            yield return Until(() => Deltas().Any(delta => Text(delta) == "Before"));
            Assert.That(Deltas()[0]["baseRevision"], Is.Null, "session open is a full snapshot");
            Assert.That(_server.Calls.Where(call => call.Path.EndsWith("/v1/index/delta", StringComparison.Ordinal))
                .All(call => call.Authorized && call.ProjectId == new string('a', 64)), Is.True);

            AuthoringRef target = _runtime.Resolver.BuildRef(_definition, AuthorScope.Definition)!;
            ChangeSet edit = StudioRuntime.Single("edit the line", IntentOrigin.Manual,
                new Operation("op1", "set", target, new JObject
                {
                    ["field"] = "lines",
                    ["value"] = new JArray(new JObject { ["text"] = "After" }),
                }));
            ApplyReport applied = _runtime.Engine.Apply(edit);
            Assert.That(applied.Ok, Is.True, string.Join("; ", applied.Diagnostics));
            yield return Until(() => Deltas().Any(delta => Text(delta) == "After"));
            int beforeUndo = Deltas().Length;
            Assert.That(_runtime.History.Undo(edit.Id).Ok, Is.True);
            yield return Until(() => Deltas().Skip(beforeUndo).Any(delta => Text(delta) == "Before"));
            int beforeRedo = Deltas().Length;
            Assert.That(_runtime.History.Redo(edit.Id).Ok, Is.True);
            yield return Until(() => Deltas().Skip(beforeRedo).Any(delta => Text(delta) == "After"));

            _definition.lines[0].text = "Rebuilt";
            _runtime.Index.Rebuild();
            yield return Until(() => Deltas().Any(delta => Text(delta) == "Rebuilt"));
            JObject[] deltas = Deltas();
            for (int i = 1; i < deltas.Length; i++)
                Assert.That((long?)deltas[i]["baseRevision"], Is.EqualTo((long?)deltas[i - 1]["revision"]), "each delta follows the acknowledged predecessor");
        }

        [UnityTest]
        public IEnumerator W_ETOS_04_RemovalAndSessionReplacementDoNotLeaveSubscriptions()
        {
            _gateway.Start();
            yield return Until(() => Deltas().Any(delta => Text(delta) == "Before"));
            _gateway.Dispose();
            _definition.lines[0].text = "Reopened";
            _runtime.Index.Rebuild();
            var replacement = new EtosAgentGateway(_client, _runtime, _queue, new MemoryCursorStore());
            _gateway = replacement;
            int beforeOpen = Deltas().Length;
            replacement.Start();
            yield return Until(() => Deltas().Skip(beforeOpen).Any(delta => Text(delta) == "Reopened"));
            Assert.That(Deltas()[beforeOpen]["baseRevision"], Is.Null);
            string path = AssetDatabase.GetAssetPath(_definition);
            AssetDatabase.DeleteAsset(path);
            _runtime.Index.Rebuild();
            yield return Until(() => Deltas().Any(delta => ((JArray)delta["removals"]!).OfType<JObject>()
                .Any(reference => (string?)reference["authoringId"] == _authoringId)));
            replacement.Dispose();
            int stopped = Deltas().Length;
            _runtime.Index.Rebuild();
            DateTime end = DateTime.UtcNow.AddSeconds(1.2);
            while (DateTime.UtcNow < end)
            {
                _queue.Pump();
                yield return null;
            }
            Assert.That(Deltas().Length, Is.EqualTo(stopped), "disposed sessions do not publish later revisions");
        }
    }
}
