#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.R3_A
{
    public sealed class RetainedWorkflowTests
    {
        private StudioRuntime _runtime = null!;
        private string _folder = string.Empty;
        private string _state = string.Empty;
        private static string Project => Directory.GetParent(Application.dataPath)!.FullName;
        private static ChangeSet Candidate(string path) => StudioJson.Deserialize<ChangeSet>(File.ReadAllText(
            Path.Combine(Project, "../../artifacts/studio/workflows/P3.2/runs", path, "candidate.json")));

        [SetUp]
        public void SetUp()
        {
            string leaf = "R3_A_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", leaf);
            _folder = "Assets/" + leaf;
            _state = Path.Combine(Path.GetTempPath(), leaf);
            _runtime = StudioRuntime.Create(new StudioRuntimeOptions {
                Paths = new StudioPaths(Project, _state, "r3-a"), Log = new MemoryStudioLog(),
                SearchFolders = new[] { _folder }, IndexScope = AuthoringSourceScope.Assets, LoadIndexCache = false });
        }

        [TearDown]
        public void TearDown()
        {
            _runtime.Dispose();
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(_folder);
            if (Directory.Exists(_state)) Directory.Delete(_state, true);
        }

        [Test]
        public void D4_ProductionCatalogRefusesRetainedTintBeforeApply()
        {
            ChangeSet candidate = Candidate("robe-20261005T055032Z/robe");
            StagedChangeSet staged = _runtime.Engine.Stage(candidate, new StageOptions {
                Mode = ValidationMode.Candidate, ToolCatalogRevision = _runtime.Registry.Catalog.Revision });
            Assert.That(staged.AllDiagnostics.Any(d => d.Message.Contains("GP-ENT-004") && d.Message.Contains("#rrggbb")), Is.True);
            Assert.That(_runtime.Journal.Exists(candidate.Id), Is.False);
        }

        [Test]
        public void D5_ProductionQuestSingletonInfersButRobeRemainsAmbiguous()
        {
            var validator = new ChangeSetValidator(_runtime.Registry.Catalog);
            Assert.That(validator.Validate(Candidate("narrative-20261005T072920Z/quest"))
                .Any(d => d.Code == DiagnosticCodes.ScopeNotAllowed), Is.False);
            Assert.That(validator.Validate(Candidate("robe2-20261005T070105Z/robe"))
                .Any(d => d.Code == DiagnosticCodes.ScopeNotAllowed), Is.True);
        }

        [Test]
        public void D8_GeneratedImageBindsAsSpriteAndUndoRestoresItemAndFile()
        {
            string itemPath = _folder + "/Item.asset";
            Assert.That(AssetDatabase.CopyAsset("Assets/Hollowmere/Items/Lantern.asset", itemPath), Is.True);
            UnityEngine.Object item = AssetDatabase.LoadMainAssetAtPath(itemPath);
            SerializedObject serialized = new SerializedObject(item);
            serialized.FindProperty("authoringId").stringValue = Guid.NewGuid().ToString("D");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            UnityEngine.Object? previous = serialized.FindProperty("icon").objectReferenceValue;
            Texture2D texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            byte[] bytes;
            try { texture.SetPixels(Enumerable.Repeat(Color.green, 16).ToArray()); texture.Apply(); bytes = texture.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            string digest = _runtime.Artifacts.Put(bytes, null);
            string path = _folder + "/sprite.png";
            var candidate = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("bind generated icon", IntentOrigin.Manual),
                new[] { new Operation("op1", "bind", _runtime.Resolver.BuildRef(item), new JObject {
                    ["field"] = "icon", ["path"] = path, ["artifact"] = new JObject { ["artifact"] = "sha256:" + digest },
                    ["importer"] = new JObject { ["textureType"] = "Sprite", ["spriteImportMode"] = "Single",
                        ["spritePixelsPerUnit"] = 100, ["filterMode"] = "Point", ["maxTextureSize"] = 1024 } }) },
                artifacts: new[] { new ArtifactRef(digest, "image/png", bytes.Length, "sprite.png") });
            ApplyReport report = _runtime.Engine.Apply(candidate);
            Assert.That(report.Ok, Is.True, string.Join(" | ", report.Diagnostics));
            serialized.Update();
            Assert.That(serialized.FindProperty("icon").objectReferenceValue, Is.TypeOf<Sprite>());
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.spriteImportMode, Is.EqualTo((int)SpriteImportMode.Single));
            Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(100));
            Assert.That(_runtime.History.Undo(candidate.Id).Ok, Is.True);
            serialized.Update();
            Assert.That(serialized.FindProperty("icon").objectReferenceValue, Is.EqualTo(previous));
            Assert.That(File.Exists(Path.Combine(Project, path)), Is.False);
        }
    }
}
