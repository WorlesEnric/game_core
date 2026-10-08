#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Dialogue.Editor;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.World;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.R3_F
{
    public sealed class FactHandoffTests
    {
        private const string Folder = "Assets/Hollowmere/Tests/R3_F/Scratch";
        private StudioRuntime runtime = null!;
        private string state = string.Empty;
        private GameplayContentSet content = null!;
        private ConditionSetDefinition condition = null!;
        private string factId = string.Empty;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.CreateFolder("Assets/Hollowmere/Tests/R3_F", "Scratch");
            state = Path.Combine(Path.GetTempPath(), "r3-f-" + Guid.NewGuid().ToString("N"));
            runtime = StudioRuntime.Create(new StudioRuntimeOptions {
                Paths = new StudioPaths(Path.GetDirectoryName(Application.dataPath)!, state, "r3-f"),
                SearchFolders = new[] { Folder }, LoadIndexCache = false });
            var world = ScriptableObject.CreateInstance<WorldDefinition>();
            world.EnsureAuthoringId();
            AssetDatabase.CreateAsset(world, Folder + "/World.asset");
            content = ScriptableObject.CreateInstance<GameplayContentSet>();
            content.Configure(world, Array.Empty<ScriptableObject>());
            AssetDatabase.CreateAsset(content, Folder + "/Content.asset");
            condition = ScriptableObject.CreateInstance<ConditionSetDefinition>();
            condition.EnsureAuthoringId();
            AssetDatabase.CreateAsset(condition, Folder + "/Condition.asset");
            factId = Guid.NewGuid().ToString("D");
            runtime.Index.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            runtime.Dispose();
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(Folder);
            if (Directory.Exists(state)) Directory.Delete(state, true);
        }

        private ChangeSet Change(bool forward = false, bool badProducer = false)
        {
            var producer = new Operation("fact", "dialogue.setFact", runtime.Resolver.BuildRef(content),
                new JObject { ["factName"] = badProducer ? "INVALID NAME" : "shrine_lit", ["authoringId"] = factId });
            var consumer = new Operation("condition", "dialogue.setFactCondition", runtime.Resolver.BuildRef(condition),
                new JObject { ["fact"] = StudioJson.ToToken(new AuthoringRef(AuthoringKind.Definition, authoringId: factId)), ["value"] = 1 });
            return new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("D10a shrine fact", IntentOrigin.Agent),
                forward ? new[] { consumer, producer } : new[] { producer, consumer },
                requirements: new Requirements(RuntimeApply.Compile, true, true, false),
                policy: badProducer ? ApplyPolicy.BestEffort : ApplyPolicy.AllOrNothing);
        }

        [Test]
        public void D4_D5_RetainedRobeSharesCompanionVerdictFixture()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../"));
            var fixture = JObject.Parse(File.ReadAllText(Path.Combine(root, "studio/agent/tests/fixtures/r3_f/robe-verdicts.json")));
            foreach (JObject sample in fixture["cases"]!)
            {
                var candidate = StudioJson.Deserialize<ChangeSet>(File.ReadAllText(Path.Combine(root,
                    "artifacts/studio/workflows/P3.2/runs", (string)sample["path"]!)));
                var op = candidate.Operations.Single(o => o.Tool == "entity.applyOverride");
                var isolated = new ChangeSet(candidate.Id, candidate.Schema, candidate.Intent, new[] { op });
                var diagnostics = new ChangeSetValidator(runtime.Registry.Catalog).Validate(isolated);
                Assert.That(diagnostics.Any(d => d.Code == (string)sample["code"]!
                    && (sample["contract"] == null || (string?)d.Data?["contract"] == (string?)sample["contract"])), Is.True);
            }
        }

        [Test]
        public void D10a_EarlierCandidateFactStagesAppliesAndReplaysThroughEngine()
        {
            var change = Change();
            var staged = runtime.Engine.Stage(change);
            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics));
            Assert.That(content.Definitions, Is.Empty, "Stage must not create assets");
            var report = runtime.Engine.Apply(staged);
            Assert.That(report.Ok, Is.True, string.Join("; ", report.Diagnostics));
            Assert.That(((FactDefinition)condition.Conditions.Single().fact!).AuthoringId, Is.EqualTo(factId));
            Assert.That(runtime.History.Undo(change.Id).Ok, Is.True);
            Assert.That(condition.Conditions, Is.Empty);
            Assert.That(runtime.History.Redo(change.Id).Ok, Is.True);
            Assert.That(((FactDefinition)condition.Conditions.Single().fact!).AuthoringId, Is.EqualTo(factId));
        }

        [Test]
        public void D10a_ForwardFactReferenceRefusesBeforeWritesWithWitness()
        {
            var staged = runtime.Engine.Stage(Change(true));
            var diagnostic = staged.AllDiagnostics.SingleOrDefault(d => (string?)d.Data?["reason"] == "candidate_fact_forward_reference");
            Assert.That(diagnostic, Is.Not.Null, string.Join("; ", staged.AllDiagnostics));
            Assert.That(diagnostic!.Code, Is.EqualTo(DiagnosticCodes.InvalidArgs));
            Assert.That((string?)diagnostic.Data!["producer"], Is.EqualTo("fact"));
            Assert.That(runtime.Engine.Apply(staged).Ok, Is.False);
            Assert.That(content.Definitions, Is.Empty);
            Assert.That(condition.Conditions, Is.Empty);
        }

        [TestCase("unknown")]
        [TestCase("wrong-kind")]
        [TestCase("duplicate")]
        [TestCase("cycle")]
        public void D10a_InvalidFactReferencesRefuseBeforeWrites(string mutation)
        {
            var json = (JObject)StudioJson.ToToken(Change());
            var operations = (JArray)json["operations"]!;
            if (mutation == "unknown") operations[1]!["args"]!["fact"]!["authoringId"] = Guid.NewGuid().ToString("D");
            if (mutation == "wrong-kind") operations[1]!["args"]!["fact"]!["kind"] = "Entity";
            if (mutation == "duplicate")
            {
                var duplicate = operations[0]!.DeepClone();
                duplicate["opId"] = "duplicate";
                operations.Insert(1, duplicate);
            }
            if (mutation == "cycle")
            {
                operations[0]!["dependsOn"] = new JArray("condition");
                operations[1]!["dependsOn"] = new JArray("fact");
            }
            var staged = runtime.Engine.Stage(StudioJson.Deserialize<ChangeSet>(json.ToString()));
            Assert.That(staged.Ok, Is.False);
            Assert.That(runtime.Engine.Apply(staged).Ok, Is.False);
            Assert.That(content.Definitions, Is.Empty);
            Assert.That(condition.Conditions, Is.Empty);
        }

        [Test]
        public void D10a_FailedFactProducerNeverAppliesDependentCondition()
        {
            var report = runtime.Engine.Apply(Change(badProducer: true));
            Assert.That(report.Outcomes.Single(o => o.OpId == "fact").Status, Is.EqualTo(OutcomeStatus.Failed));
            Assert.That(report.Entry.Outcomes!.Single(o => o.OpId == "condition").Status, Is.EqualTo(OutcomeStatus.Skipped));
            Assert.That(condition.Conditions, Is.Empty);
        }
    }
}
