// GameCore.Studio.Edit.Tests - manual edits: W-EDIT-05 (a gizmo drag and a typed move produce identical journal
// entries), inspector commits as journaled set/assign change sets with inline validation, and the generated inspector.
#nullable enable
using GameCore.Studio.Authoring;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class ManualEditTests
    {
        private StudioTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new StudioTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        /// <summary>The journal entry without the parts that differ by construction (id, timestamps).</summary>
        private JObject Comparable(string changeSetId)
        {
            JObject entry = (JObject)StudioJson.ToToken(_bed.Runtime.Journal.Read(changeSetId)!);
            entry.Remove("id");
            entry.Remove("timestamps");
            return entry;
        }

        [Test]
        public void WEdit05_GizmoDragAndTypedMoveProduceIdenticalJournalEntries()
        {
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Ferryman", new Vector3(1f, 0f, 2f));
            _bed.SaveScene();
            _bed.Runtime.Index.Rebuild();
            Vector3 target = new Vector3(4f, 0f, -1.5f);

            GizmoMoveController gizmo = new GizmoMoveController(_bed.Runtime);
            gizmo.Begin(entity.gameObject);
            gizmo.DragTo(new Vector3(2f, 0f, 1f));
            gizmo.DragTo(new Vector3(3f, 0f, 0f));
            gizmo.DragTo(target);
            Assert.That(_bed.Runtime.Journal.List().Count, Is.EqualTo(0), "dragging writes nothing");
            ApplyReport? dragged = gizmo.End();
            Assert.That(dragged, Is.Not.Null);
            Assert.That(dragged!.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", dragged.Diagnostics));
            Assert.That(entity.transform.position, Is.EqualTo(target));
            Assert.That(_bed.Runtime.Journal.List().Count, Is.EqualTo(1), "one drag is one change set");

            Assert.That(_bed.Runtime.History.Undo().Ok, Is.True);
            Assert.That(entity.transform.position, Is.EqualTo(new Vector3(1f, 0f, 2f)));

            ChangeSet typed = MoveChangeSets.Build(_bed.Runtime, entity.gameObject, target)!;
            ApplyReport typedReport = _bed.Runtime.Engine.Apply(typed);
            Assert.That(typedReport.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(entity.transform.position, Is.EqualTo(target));

            JObject fromGizmo = Comparable(dragged.Entry.Id);
            fromGizmo["state"] = "Applied";
            JObject fromTyping = Comparable(typed.Id);
            Assert.That(JToken.DeepEquals(fromGizmo, fromTyping), Is.True, fromGizmo + "\n---\n" + fromTyping);
        }

        [Test]
        public void InspectorCommit_IsAJournaledSetAndInvalidValuesAreRejectedInline()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            _bed.Runtime.Index.Rebuild();
            ManualEditCommitter committer = new ManualEditCommitter(_bed.Runtime);
            AuthoringTypeInfo info = _bed.Runtime.Identity.Describe(lantern)!;
            AuthorMemberInfo weight = info.FindMember("weight")!;

            Assert.That(committer.Check(weight, new JValue(250.0)), Is.Not.Empty, "above the declared max of 100 kg");
            Assert.That(committer.Commit(lantern, weight, new JValue(250.0)), Is.Null);
            Assert.That(lantern.weight, Is.EqualTo(2f));

            ApplyReport? report = committer.Commit(lantern, weight, new JValue(3.25));
            Assert.That(report, Is.Not.Null);
            Assert.That(report!.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(lantern.weight, Is.EqualTo(3.25f));
            ChangeSet entry = _bed.Runtime.Journal.Read(report.Entry.Id)!;
            Assert.That(entry.Intent.Origin, Is.EqualTo(IntentOrigin.Manual));
            Assert.That(entry.Operations[0].Tool, Is.EqualTo(BuiltInToolIdsExt.Set));
        }

        [Test]
        public void InspectorCommit_ReferencesGoThroughAssign()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern");
            FixtureNpcDefinition npc = _bed.CreateNpc("Ferryman");
            _bed.Runtime.Index.Rebuild();
            ManualEditCommitter committer = new ManualEditCommitter(_bed.Runtime);
            AuthorMemberInfo item = _bed.Runtime.Identity.Describe(npc)!.FindMember("startingItem")!;

            ApplyReport? report = committer.Commit(npc, item, _bed.Runtime.Resolver.Codec.Refs.WriteRef(lantern));
            Assert.That(report?.State, Is.EqualTo(ChangeSetState.Applied), report == null ? "no report" : string.Join("; ", report.Diagnostics));
            Assert.That(npc.startingItem, Is.SameAs(lantern));
            Assert.That(report!.Entry.Operations[0].Tool, Is.EqualTo(BuiltInToolIdsExt.Assign));
        }

        [Test]
        public void InspectorBuilder_GeneratesOneRowPerAuthorableMember()
        {
            FixtureNpcDefinition npc = _bed.CreateNpc("Ferryman");
            AuthoringInspectorBuilder builder = new AuthoringInspectorBuilder(_bed.Runtime.Identity, _bed.Runtime.Resolver.Codec, new ManualEditCommitter(_bed.Runtime));
            VisualElement? root = builder.Build(npc);
            Assert.That(root, Is.Not.Null);
            Assert.That(root!.Q("field-greeting"), Is.Not.Null);
            Assert.That(root.Q("field-mood"), Is.Not.Null);
            Assert.That(root.Q("field-startingItem"), Is.Not.Null);
            Assert.That(root.Q("field-greeting").Q<TextField>(), Is.Not.Null);
            Assert.That(builder.Build(_bed.SpawnEntity("Plain", Vector3.zero).transform), Is.Null, "non-authorable objects get no generated inspector");
        }
    }
}
