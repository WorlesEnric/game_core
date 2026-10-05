#nullable enable
using System;
using System.Linq;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Studio.Views.Hollowmere.Tests
{
    public sealed class R2ViewsIntegrationTests : HollowmereViewsFixture
    {
        private sealed class AdmissionHandler : IHistoryEntryHandler
        {
            public HistoryEntryKind Kind => HistoryEntryKind.Admission;
            public HistoryAction? Called { get; private set; }
            public HistoryResult Handle(ChangeSet entry, HistoryAction action, bool force)
            {
                Called = action;
                return new HistoryResult(entry.Id, true, entry.EffectiveState, Array.Empty<Diagnostic>(), null);
            }
        }
        private sealed class ReclassifiedTool : IStudioTool
        {
            public ReclassifiedTool(ToolEntry entry) { Entry = entry; }
            public ToolEntry Entry { get; }
            public bool Internal => false;
            public bool ReadOnly => false;
            public ToolStageResult Stage(EditContext context) => new ToolStageResult();
            public OperationResult Apply(EditContext context) => throw new InvalidOperationException("must never invoke");
        }

        [Test]
        public void R2_15_ViewsUndoAndRedoUseAdmissionHistoryHandler()
        {
            ChangeSet entry = StudioRuntime.Single("admit", IntentOrigin.Manual, new Operation("op1", "mechanism.admit", null));
            AdmissionHandler handler = new AdmissionHandler();
            Runtime.History.RegisterHandler(handler);
            Runtime.Journal.Write(entry.WithState(ChangeSetState.Applied));
            Assert.That(Context.Edits.Undo(entry.Id).Ok, Is.True);
            Assert.That(handler.Called, Is.EqualTo(HistoryAction.Undo));
            Runtime.Journal.Write(entry.WithState(ChangeSetState.Undone));
            Assert.That(Context.Edits.Redo(entry.Id).Ok, Is.True);
            Assert.That(handler.Called, Is.EqualTo(HistoryAction.Redo));
        }

        [Test]
        public void R2_33_ViewsRespectRegistryAuthorityWhenPureToolIsReclassified()
        {
            AuthoringRef graph = NodeAt(Root + "/Dialogue/Graphs/Maren.asset").Ref;
            Assert.That(Context.Tools.Invoke("dialogue.preview", graph, new JObject()).Ok, Is.True);
            Runtime.Registry.Register(new ReclassifiedTool(Runtime.Registry.Find("dialogue.preview")!.Entry), replace: true);
            ToolInvocation result = Context.Tools.Invoke("dialogue.preview", graph, new JObject());
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCodes.Refused));
        }

        [Test]
        public void R2_34_SpawnTargetsDefinitionAndPortalCarriesExplicitRegion()
        {
            WorldDocument world = WorldDocument.Load(Context, Context.Graph())!;
            WorldRegion region = First(world.Regions, item => item.ScenePath == Thornwick);
            Operation spawn = WorldEdits.SetSpawnPoint(Runtime, region, region.Spawn!.Value, 20f);
            Assert.That(spawn.Target!.SameTarget(region.Ref), Is.True);
            Assert.That(spawn.Target.Kind, Is.EqualTo(AuthoringKind.Definition));
            Assert.That(Context.Edits.ApplyOne("set spawn", spawn).Ok, Is.True);
            WorldPortal portal = First(world.Portals, item => item.RegionA == region.Key || item.RegionB == region.Key);
            Operation add = WorldEdits.AddPortal(portal, region, region.Spawn.Value);
            AuthoringRef bound = StudioJson.Deserialize<AuthoringRef>(add.Args!["region"]!.ToString());
            Assert.That(bound.SameTarget(region.Ref), Is.True);
        }

        [Test]
        public void R2_34_SecondOperationRefusalRollsBackConnectionInOneChangeSet()
        {
            WorldDocument world = WorldDocument.Load(Context, Context.Graph())!;
            ChangeSet connect = WorldEdits.ConnectRegions(Runtime, world, world.Regions[0], world.Regions[1]);
            Operation fail = ViewEdits.Op("op2", WorldEdits.ConnectRegionsTool, world.Ref, new JObject
            {
                ["regionA"] = StudioJson.ToToken(SemanticIndexService.EdgeRef(world.Regions[0].Ref)),
                ["regionB"] = StudioJson.ToToken(SemanticIndexService.EdgeRef(world.Regions[0].Ref)),
            }, new[] { "op1" });
            int before = Runtime.Journal.List().Count;
            ChangeSet batch = ViewEdits.Build("connect then refuse", connect.Operations.Concat(new[] { fail }).ToArray());
            ApplyReport report = Context.Edits.Apply(batch);
            Assert.That(report.Ok, Is.False);
            Assert.That(Runtime.Journal.List().Count, Is.EqualTo(before + 1));
            Runtime.Index.Rebuild();
            Assert.That(WorldDocument.Load(Context, Context.Graph())!.Portals.Count, Is.EqualTo(world.Portals.Count));
        }
    }
}
