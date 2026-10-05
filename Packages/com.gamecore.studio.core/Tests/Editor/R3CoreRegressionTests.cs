#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class R3CoreRegressionTests
    {
        private StudioTestBed _bed = null!;
        [SetUp] public void SetUp() => _bed = new StudioTestBed();
        [TearDown] public void TearDown() => _bed.Dispose();

        private static JObject Retained(string path) => JObject.Parse(File.ReadAllText(Path.Combine(
            StudioTestBed.ProjectRoot, "../../artifacts/studio/workflows/P3.2/runs", path)));

        [Test]
        public void D1_DefinitionAddedAfterCacheAppearsWithoutManualRebuild()
        {
            _bed.CreateNpc("Before");
            _bed.Runtime.Index.SaveCache();
            _bed.CreateNpc("After");
            AssetDatabase.SaveAssets();
            StudioRuntime restarted = _bed.CreateRuntime(loadIndexCache: true);
            Assert.That(restarted.Index.Snapshot().Nodes.Select(n => n.Name), Does.Contain("After"));
            Assert.That(restarted.Index.Slice(Array.Empty<AuthoringRef>(), includeTypes: new[] { "fixture.npc" }).Index.Nodes.Select(n => n.Name), Does.Contain("After"));
        }

        [Test]
        public void D8_SafeSpriteSettingsAcceptedAndUnsafeSettingsRefused()
        {
            Assert.That((string?)Retained("harness-20261005T130114Z/headless-h2-media.json")["assign"]!["diagnostics"]![0]!["message"],
                Does.Contain("unsupported setting spriteImportMode"));
            JObject settings = new JObject { ["textureType"] = "Sprite", ["spriteImportMode"] = "Single",
                ["spritePixelsPerUnit"] = 100, ["filterMode"] = "Bilinear", ["maxTextureSize"] = 2048 };
            string path = _bed.Folder + "/generated.png";
            Assert.That(MediaImportPolicy.Validate(_bed.Runtime.Paths, path, settings), Is.Null);
            settings["spriteImportMode"] = "Multiple";
            Assert.That(MediaImportPolicy.Validate(_bed.Runtime.Paths, path, settings), Is.Not.Null);
            settings["spriteImportMode"] = "Single";
            settings["spritePixelsPerUnit"] = -1;
            Assert.That(MediaImportPolicy.Validate(_bed.Runtime.Paths, path, settings), Is.Not.Null);
            settings["spritePixelsPerUnit"] = 100;
            settings["assetBundleName"] = "untrusted";
            Assert.That(MediaImportPolicy.Validate(_bed.Runtime.Paths, path, settings), Is.Not.Null);
        }

        private ChangeSet Ring(ApplyPolicy policy, out GameCore.Studio.Fixtures.FixtureAuthoredEntity first)
        {
            JObject json = Retained("batch2-20261005T104639Z/ring/candidate.json");
            Assert.That((string?)Retained("batch2-20261005T104639Z/ring/rebased.json")["diagnostics"]![0]!["code"], Is.EqualTo("Conflict"));
            Assert.That((string?)Retained("batch2-20261005T104639Z/ring/apply-best-effort-report.json")["state"], Is.EqualTo("Rejected"));
            var entities = Enumerable.Range(0, 4).Select(i => _bed.SpawnEntity("Crate" + i, new Vector3(i, 0, 0))).ToArray();
            _bed.SaveScene();
            first = entities[0];
            for (int i = 0; i < 4; i++)
            {
                AuthoringRef reference = _bed.Ref(entities[i]);
                json["baseVersions"]![i]!["ref"] = StudioJson.ToToken(reference);
                json["baseVersions"]![i]!["stamp"] = reference.Stamp;
                if (i < 3) json["operations"]![i]!["target"] = StudioJson.ToToken(reference);
            }
            json.Remove("selection");
            json["policy"] = policy.ToString();
            return StudioJson.Deserialize<ChangeSet>(json.ToString());
        }

        [Test]
        public void D20_RebaseRefreshesOnlySuccessfullyReplannedBaseVersions()
        {
            ChangeSet candidate = Ring(ApplyPolicy.BestEffort, out var first);
            first.transform.position += Vector3.right;
            StagedChangeSet rebased = _bed.Runtime.Engine.Rebase(_bed.Runtime.Engine.Stage(candidate), new[] { "op1" });
            Assert.That(rebased.ChangeSet.BaseVersions![0].Stamp, Is.EqualTo(_bed.Ref(first).Stamp));
            Assert.That(rebased.Ok, Is.True, string.Join(" | ", rebased.AllDiagnostics));
            Assert.That(_bed.Runtime.Engine.Apply(rebased).Outcomes.All(o => o.Status == OutcomeStatus.Applied), Is.True);
        }

        [Test]
        public void D20_BestEffortRefusesStaleTargetAndAppliesIndependentOperations()
        {
            ChangeSet candidate = Ring(ApplyPolicy.BestEffort, out var first);
            StagedChangeSet staged = _bed.Runtime.Engine.Stage(candidate);
            first.transform.position += Vector3.right;
            ApplyReport report = _bed.Runtime.Engine.Apply(staged);
            Assert.That(report.Outcomes.Select(o => o.Status), Is.EqualTo(new[] { OutcomeStatus.Refused, OutcomeStatus.Applied, OutcomeStatus.Applied }));
        }

        [Test]
        public void D21_CoreProvidesBoundedPromptReferenceResolver()
        {
            Assert.That(typeof(SemanticIndexService).GetMethod("ResolvePromptReferences"), Is.Not.Null);
        }
    }
}
