#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Dialogue.Editor;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Entities.Editor;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Ui;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hollowmere.R3_D
{
    public sealed class GameplayBindingTests
    {
        private const string Folder = "Assets/Hollowmere/Tests/R3_D/Scratch";
        private StudioRuntime runtime = null!;
        private string state = string.Empty;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.CreateFolder("Assets/Hollowmere/Tests/R3_D", "Scratch");
            state = Path.Combine(Path.GetTempPath(), "r3-d-" + Guid.NewGuid().ToString("N"));
            runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Path.GetDirectoryName(Application.dataPath)!, state, "r3-d"),
                SearchFolders = new[] { Folder },
                TypeSource = () => new[] { typeof(EntityDefinition), typeof(ConditionSetDefinition), typeof(FactDefinition), typeof(DialogueGraphDefinition), typeof(GameplayContentSet), typeof(UiDocumentDefinition) },
                ToolMethodSource = () => typeof(EntityTools).GetMethods().Concat(typeof(DialogueTools).GetMethods()).Concat(typeof(GameCore.Gameplay.Ui.Editor.UiTools).GetMethods())
                    .Where(m => GameCore.Studio.Authoring.AuthoringMetadata.Operation(m) != null),
                LoadIndexCache = false,
            });
        }

        [TearDown]
        public void TearDown()
        {
            runtime.Dispose();
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(Folder);
            if (Directory.Exists(state)) Directory.Delete(state, true);
        }

        private T Asset<T>(string name) where T : ScriptableObject
        {
            T value = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(value, Folder + "/" + name + ".asset");
            return value;
        }

        private EntityDefinition Definition()
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, Folder + "/Material.mat");
            primitive.GetComponent<Renderer>().sharedMaterials = new[] { material, material };
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(primitive, Folder + "/Robe.prefab");
            UnityEngine.Object.DestroyImmediate(primitive);
            EntityDefinition definition = Asset<EntityDefinition>("MarenEntity");
            definition.Configure(prefab, 1000, true, true);
            definition.EnsureAuthoringId();
            return definition;
        }

        private Texture2D ImportedTexture()
        {
            var source = new Texture2D(1, 1);
            source.SetPixel(0, 0, Color.green);
            source.Apply();
            byte[] bytes = source.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(source);
            string digest = runtime.Artifacts.Put(bytes, null);
            string path = Folder + "/green-robe.png";
            var change = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId,
                new Intent("D9 retained generated texture import", IntentOrigin.Manual),
                new[] { new Operation("import", BuiltInToolIds.AssetImport, null, new JObject
                {
                    ["path"] = path, ["artifact"] = new JObject { ["artifact"] = "sha256:" + digest },
                }) }, artifacts: new[] { new ArtifactRef(digest, "image/png", bytes.Length, "green-robe.png") });
            var report = runtime.Engine.Apply(change);
            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", report.Diagnostics));
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private ChangeSet Change(string tool, UnityEngine.Object target, JObject args) => new ChangeSet(
            IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent(tool, IntentOrigin.Manual),
            new[] { new Operation("op1", tool, runtime.Resolver.BuildRef(target, AuthorScope.Definition), args) },
            requirements: new Requirements(RuntimeApply.Rebuild, true, false, false));

        [Test]
        public void D9_D10_CatalogExportHasTypedReferencesAndSources()
        {
            ToolCatalog catalog = runtime.Registry.BuildCatalog();
            Assert.That(catalog.FindTool("entity.setMaterialTexture")!.FindArg("texture")!.Type, Is.EqualTo("ref"));
            Assert.That(catalog.FindTool("dialogue.setFactCondition")!.FindArg("fact")!.Category, Is.EqualTo(NarrativeKinds.Fact));
            Assert.That(catalog.FindTool("ui.bind")!.FindArg("source")!.Doc, Does.Contain("hud.QuestStageTitle"));
            var sample = new ToolCatalog(catalog.ObjectTypes, catalog.Tools.Where(t => t.Id == "entity.setMaterialTexture"
                || t.Id == "dialogue.setFactCondition" || t.Id == "dialogue.setFact" || t.Id == "dialogue.linkCondition"
                || t.Id == "dialogue.generateVoice" || t.Id == "ui.bind").ToArray()).WithRevision();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../../Packages/com.gamecore.gameplay.entities/Documentation~/r3-d-tool-catalog.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, StudioJson.Serialize(sample) + "\n");
        }

        [Test]
        public void D9_ImportedTextureTypedChangeSetUndoRedoAndBakeImpact()
        {
            EntityDefinition definition = Definition();
            Texture2D texture = ImportedTexture();
            runtime.Index.Rebuild();
            string content = DefinitionCanonicalizer.ContentStamp(definition);
            string recipe = DefinitionCanonicalizer.StructuralStamp(definition);
            var change = Change("entity.setMaterialTexture", definition, new JObject
            {
                ["texture"] = StudioJson.ToToken(runtime.Resolver.BuildRef(texture)!), ["renderer"] = 0,
                ["slot"] = 1, ["property"] = "_BaseMap",
            });
            var report = runtime.Engine.Apply(change);
            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", report.Diagnostics));
            Assert.That(definition.MaterialTextures.Single().texture, Is.SameAs(texture));
            Assert.That(DefinitionCanonicalizer.ContentStamp(definition), Is.Not.EqualTo(content));
            Assert.That(DefinitionCanonicalizer.StructuralStamp(definition), Is.EqualTo(recipe));
            Assert.That(runtime.History.Undo().Ok, Is.True);
            Assert.That(definition.MaterialTextures, Is.Empty);
            Assert.That(DefinitionCanonicalizer.ContentStamp(definition), Is.EqualTo(content));
            Assert.That(runtime.History.Redo().Ok, Is.True);
            Assert.That(definition.MaterialTextures.Single().texture, Is.SameAs(texture));
            AssetDatabase.SaveAssets();
            Assert.That(EntityValidator.Validate(definition), Is.Empty);
        }

        [Test]
        public void D9_MaterialSlotPresentationDoesNotMutateSharedMaterialAndReappliesOnSpawn()
        {
            EntityDefinition definition = Definition();
            Texture2D texture = ImportedTexture();
            EntityTools.SetMaterialTexture(definition, texture, 0, 1);
            for (int spawn = 0; spawn < 2; spawn++)
            {
                GameObject view = UnityEngine.Object.Instantiate(definition.Prefab!);
                try
                {
                    Renderer renderer = view.GetComponent<Renderer>();
                    Texture before = renderer.sharedMaterials[1].GetTexture("_BaseMap");
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_BaseColor", Color.green);
                    renderer.SetPropertyBlock(block);
                    MaterialTextureBinding.Apply(view, definition.MaterialTextures);
                    renderer.GetPropertyBlock(block, 1);
                    Assert.That(block.GetTexture("_BaseMap"), Is.SameAs(texture));
                    Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.green));
                    renderer.GetPropertyBlock(block, 0);
                    Assert.That(block.GetTexture("_BaseMap"), Is.Null);
                    Assert.That(renderer.sharedMaterials[1].GetTexture("_BaseMap"), Is.SameAs(before));
                }
                finally { UnityEngine.Object.DestroyImmediate(view); }
            }
        }

        [TestCase(-1, 0, "_BaseMap")]
        [TestCase(2, 0, "_BaseMap")]
        [TestCase(0, 2, "_BaseMap")]
        [TestCase(0, 0, "_BaseColor")]
        [TestCase(0, 0, "missing")]
        public void D9_InvalidBindingRefusesWithoutMutation(int renderer, int slot, string property)
        {
            EntityDefinition definition = Definition();
            var error = Assert.Throws<ArgumentException>(() => EntityTools.SetMaterialTexture(definition, null, renderer, slot, property));
            Assert.That(error!.Message, Does.StartWith(MaterialTextureBinding.InvalidCode));
            Assert.That(definition.MaterialTextures, Is.Empty);
        }

        [Test]
        public void D10a_TypedFactConditionAppliesThroughEngineAndUndoes()
        {
            var fact = Asset<FactDefinition>("shrine_lit");
            fact.Configure("shrine_lit", 0, true);
            fact.EnsureAuthoringId();
            var condition = Asset<ConditionSetDefinition>("ShrineLit");
            condition.EnsureAuthoringId();
            runtime.Index.Rebuild();
            var change = Change("dialogue.setFactCondition", condition, new JObject
            { ["fact"] = StudioJson.ToToken(runtime.Resolver.BuildRef(fact)!), ["comparison"] = "Equal", ["value"] = 1 });
            var report = runtime.Engine.Apply(change);
            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", report.Diagnostics));
            Assert.That(condition.Conditions.Single().fact, Is.SameAs(fact));
            Assert.That(runtime.History.Undo().Ok, Is.True);
            Assert.That(condition.Conditions, Is.Empty);
            Assert.That(runtime.History.Redo().Ok, Is.True);
            Assert.That(condition.Conditions.Single().fact, Is.SameAs(fact));
        }

        [Test]
        public void D10a_CandidateAssignedFactIdIsStableAndDuplicateIdsRefuse()
        {
            const string id = "12345678-1234-4234-8234-123456789abc";
            var content = Asset<GameplayContentSet>("Content");
            FactDefinition fact = DialogueTools.SetFact(content, "shrine_lit", 0, true, id);
            Assert.That(fact.AuthoringId, Is.EqualTo(id));
            var condition = Asset<ConditionSetDefinition>("ShrineLit");
            DialogueTools.SetFactCondition(condition, fact);
            Assert.That(condition.Conditions.Single().fact, Is.SameAs(fact));
            Assert.That(DialogueTools.SetFact(content, "shrine_lit", 1, true, id), Is.SameAs(fact));
            Assert.Throws<ArgumentException>(() => DialogueTools.SetFact(content, "other_fact", 0, true, id));
            Assert.Throws<ArgumentException>(() => DialogueTools.SetFact(content, "shrine_lit", 0, true, Guid.NewGuid().ToString()));
            Assert.Throws<ArgumentException>(() => DialogueTools.SetFact(content, "invalid_id", 0, true, "bad-id"));
            Assert.That(content.Definitions.Count, Is.EqualTo(1));
            Assert.That(fact.AuthoringId, Is.EqualTo(id));
        }

        [Test]
        public void D9_ClearIsUndoableAndUnsavedTextureRefuses()
        {
            EntityDefinition definition = Definition();
            Texture2D texture = ImportedTexture();
            EntityTools.SetMaterialTexture(definition, texture);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            EntityTools.SetMaterialTexture(definition, null);
            Undo.FlushUndoRecordObjects();
            Assert.That(definition.MaterialTextures, Is.Empty);
            Undo.PerformUndo();
            Assert.That(definition.MaterialTextures.Single().texture, Is.SameAs(texture));
            var transient = new Texture2D(1, 1);
            try
            {
                Assert.Throws<ArgumentException>(() => EntityTools.SetMaterialTexture(definition, transient));
                Assert.That(definition.MaterialTextures.Single().texture, Is.SameAs(texture));
            }
            finally { UnityEngine.Object.DestroyImmediate(transient); }
        }

        [Test]
        public void D10b_StageTitleTracksFirstActiveQuestAndClearsOnCompletion()
        {
            var ui = new UiRuntime(null);
            QuestView Quest(int key, int status, string stage) => new QuestView(key, "Quest", status, 0, stage,
                string.Empty, 0, new[] { new ObjectiveView(0, "Gather oil", 0, 2, false, 0) });
            ui.Show(new JournalViewModel(new[] { Quest(1, 2, "Old"), Quest(2, 1, "Light the shrine"), Quest(3, 1, "Later") }, 0));
            Assert.That(ui.Models.Hud.QuestStageTitle, Is.EqualTo("Light the shrine"));
            Assert.That(ui.Models.Hud.ObjectiveText, Does.StartWith("Gather oil"));
            var tree = new VisualElement();
            tree.Add(new Label { name = "Objective" });
            Assert.That(BindingHost.Check(tree, new[] { new UiBindingEntry("Objective", "text", "vm:hud.QuestStageTitle", "") }, ui.Models).Ok, Is.True);
            ui.Show(new JournalViewModel(new[] { Quest(2, 1, "Ring the bell") }, 0));
            Assert.That(ui.Models.Hud.QuestStageTitle, Is.EqualTo("Ring the bell"));
            ui.Show(new JournalViewModel(new[] { Quest(2, 2, "Finished") }, 0));
            Assert.That(ui.Models.Hud.QuestStageTitle, Is.Empty);
            ui.Show(new JournalViewModel(Array.Empty<QuestView>(), 0));
            Assert.That(ui.Models.Hud.QuestStageTitle, Is.Empty);
        }
    }
}
