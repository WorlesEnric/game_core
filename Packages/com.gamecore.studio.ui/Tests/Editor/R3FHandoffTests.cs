#nullable enable
using System.IO;
using System.Linq;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI.Tests
{
    public sealed class R3FHandoffTests
    {
        private sealed class SingletonScopeTool : IStudioTool
        {
            private readonly IStudioTool inner;
            public SingletonScopeTool(IStudioTool tool)
            {
                inner = tool;
                var json = (JObject)StudioJson.ToToken(tool.Entry);
                json["id"] = "r3f.set";
                json["scopes"] = new JArray("Definition");
                Entry = StudioJson.Deserialize<ToolEntry>(json.ToString());
            }
            public ToolEntry Entry { get; }
            public bool Internal => false;
            public bool ReadOnly => false;
            public ToolStageResult Stage(EditContext context) => inner.Stage(context);
            public OperationResult Apply(EditContext context) => inner.Apply(context);
        }

        [Test]
        public void D21_RetainedRingPromptIncludesUnselectedWell()
        {
            using var bed = new UiTestBed();
            var selected = bed.SpawnEntity("Guard", Vector3.zero);
            var well = bed.SpawnEntity("Village Well", new Vector3(7, 0, 9));
            bed.SaveScene();
            bed.Runtime.Index.Rebuild();
            var retained = JObject.Parse(File.ReadAllText(Path.Combine(UiTestBed.ProjectRoot,
                "../../artifacts/studio/workflows/P3.2/runs/batch-20261005T100136Z/ring/request.json")));
            string text = (string)retained["intent"]!["text"]!;
            var selection = new SelectionSnapshot("sel_01ARZ3NDEKTSV4RRFFQ69G5FAV", SelectionMode.Edit, new[] { bed.Ref(selected) }, bed.Runtime.Index.Revision);
            var built = new AgentRequestBuilder(bed.Runtime).Build(text, selection);
            Assert.That(built.Request.ContextSlice.Nodes.Any(n => n.Ref.SameTarget(bed.Ref(well))), Is.True);
            Assert.That(built.Selection.Targets.Count, Is.EqualTo(1));
            var scene = JObject.Parse(Encoding.UTF8.GetString(built.Request.Attachments.Single(a => a.Name == AgentRequestBuilder.SceneContextName).Data));
            Assert.That(scene["objects"]!.Any(n => (string?)n["name"] == "Village Well"), Is.True);
            Assert.That(built.ContextBytes, Is.LessThanOrEqualTo(AgentRequestBuilder.SliceByteCap));
        }

        [Test]
        public void D21_PromptReferencesRespectCapsAndReportTruncation()
        {
            using var bed = new UiTestBed();
            for (int i = 0; i < 130; i++) bed.SpawnEntity("Village Well", new Vector3(i, 0, 0));
            bed.SaveScene();
            bed.Runtime.Index.Rebuild();
            var selection = new SelectionSnapshot("sel_01ARZ3NDEKTSV4RRFFQ69G5FAV", SelectionMode.Edit, System.Array.Empty<AuthoringRef>(), bed.Runtime.Index.Revision);
            var built = new AgentRequestBuilder(bed.Runtime) { ByteCap = 1024 }.Build("Ring around Village Well", selection);
            Assert.That(built.ContextTruncated, Is.True);
            Assert.That(built.ContextOmittedNodes, Is.GreaterThan(0));
            Assert.That(built.ContextBytes, Is.LessThanOrEqualTo(1024));
            Assert.That(built.Request.ContextSlice.Nodes.Count, Is.LessThanOrEqualTo(128));
            var attachment = built.Request.Attachments.Single(a => a.Name == AgentRequestBuilder.SceneContextName);
            Assert.That(attachment.Data.Length, Is.LessThanOrEqualTo(1024));
            Assert.That((bool)JObject.Parse(Encoding.UTF8.GetString(attachment.Data))["truncated"]!, Is.True);
        }

        [Test]
        public void D5_ScopeInferenceIsVisibleInformationAndDoesNotBlockApply()
        {
            using var bed = new UiTestBed();
            var npc = bed.CreateNpc("Smith");
            bed.Runtime.Index.Rebuild();
            bed.Runtime.Registry.Register(new SingletonScopeTool(bed.Runtime.Registry.Find("set")!), true);
            var change = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("greet", IntentOrigin.Agent),
                new[] { new Operation("greet", "r3f.set", bed.Ref(npc).WithScope(null), new JObject { ["field"] = "greeting", ["value"] = "Welcome" }) });
            var entry = bed.Context.Candidates.Add(change.Id, change, bed.CatalogRevision());
            var staged = bed.Context.Candidates.Preview(entry);
            Assert.That(staged.Inferences.Count, Is.EqualTo(1));
            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics));
            var panel = new CandidatePanelView(bed.Context);
            panel.Select(entry.Id);
            var evidence = panel.Q<VisualElement>("candidate-inferences");
            Assert.That(evidence, Is.Not.Null);
            Assert.That(evidence.Query<Label>().ToList().Any(l => l.text.Contains("ScopeInferred")), Is.True);
            Assert.That(evidence.Query<Label>(className: "gcs-diagnostic").ToList(), Is.Empty);
            Assert.That(bed.Context.Candidates.Apply(entry).Ok, Is.True);
        }
    }
}
