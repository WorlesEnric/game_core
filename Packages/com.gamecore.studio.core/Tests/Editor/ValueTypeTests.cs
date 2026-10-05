// GameCore.Studio.Edit.Tests - the authoringId value type (catalog, validation, create, index) and set/undo on list
// members of [Serializable] class elements (before-values captured through SerializedProperty), the two defects
// P2.3 reported against studio.core.
#nullable enable
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class ValueTypeTests
    {
        private const string SpeakerId = "4f6c1c9e-2b1a-4d3e-9a7b-0c1d2e3f4a5b";

        private StudioTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new StudioTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        [Test]
        public void AuthoringIdValueType_IsCatalogedValidatedCreatableAndIndexed()
        {
            ObjectTypeEntry? dialogueType = null;
            foreach (ObjectTypeEntry entry in _bed.Runtime.Registry.Catalog.ObjectTypes)
            {
                if (entry.TypeId == "fixture.dialogue")
                {
                    dialogueType = entry;
                }
            }

            Assert.That(dialogueType, Is.Not.Null, string.Join("; ", _bed.Runtime.Registry.Problems));
            Assert.That(dialogueType!.Fields, Has.Some.Matches<ValueSpec>(field => field.Name == "speakerEntityId" && field.Type == ValueTypes.AuthoringId));
            Assert.That(ValueTypes.IsKnown(ValueTypes.AuthoringId + ValueTypes.ArraySuffix), Is.True);

            ChangeSet create = StudioTestBed.NewChangeSet(
                "create a dialogue",
                null,
                StudioTestBed.Op("op1", BuiltInToolIdsExt.Create, null, new JObject
                {
                    ["type"] = "fixture.dialogue",
                    ["name"] = "Intro",
                    ["path"] = _bed.Folder,
                    ["fields"] = new JObject { ["speakerEntityId"] = SpeakerId },
                }));
            ApplyReport created = _bed.Runtime.Engine.Apply(create);
            Assert.That(created.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", created.Diagnostics));
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(FixtureDialogueDefinition), new[] { _bed.Folder });
            Assert.That(guids.Length, Is.EqualTo(1));
            FixtureDialogueDefinition dialogue = AssetDatabase.LoadAssetAtPath<FixtureDialogueDefinition>(AssetDatabase.GUIDToAssetPath(guids[0]));
            Assert.That(dialogue.speakerEntityId, Is.EqualTo(SpeakerId));

            _bed.Runtime.Index.Rebuild();
            IndexNode? node = _bed.Runtime.Index.FindNode(_bed.Ref(dialogue));
            Assert.That(node?.Type, Is.EqualTo("fixture.dialogue"));
            Assert.That(node!.Fields!["speakerEntityId"].Type, Is.EqualTo(ValueTypes.AuthoringId));
            Assert.That(node.Fields["speakerEntityId"].Value?.ToString(), Is.EqualTo(SpeakerId));

            foreach (string bad in new[] { "Not-A-Guid", SpeakerId.ToUpperInvariant(), "4f6c1c9e2b1a4d3e9a7b0c1d2e3f4a5b", "00000000-0000-0000-0000-000000000000" })
            {
                ApplyReport refused = _bed.Runtime.Engine.Apply(StudioTestBed.NewChangeSet("bad id", null, StudioTestBed.Set("op1", _bed.Ref(dialogue), "speakerEntityId", bad)));
                Assert.That(refused.State, Is.EqualTo(ChangeSetState.Rejected), bad);
                Assert.That(refused.Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == DiagnosticCodes.InvalidArgs && d.Message.Contains("authoring id")), bad);
                Assert.That(dialogue.speakerEntityId, Is.EqualTo(SpeakerId));
            }

            ApplyReport cleared = _bed.Runtime.Engine.Apply(StudioTestBed.NewChangeSet("clear speaker", null, StudioTestBed.Set("op1", _bed.Ref(dialogue), "speakerEntityId", string.Empty)));
            Assert.That(cleared.State, Is.EqualTo(ChangeSetState.Applied), "empty means none for an optional authoringId field: " + string.Join("; ", cleared.Diagnostics));
            Assert.That(dialogue.speakerEntityId, Is.Empty);

            HistoryResult undone = _bed.Runtime.History.Undo();
            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(dialogue.speakerEntityId, Is.EqualTo(SpeakerId));
        }

        [Test]
        public void SetOnListMembers_UndoRestoresTheExactElements()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            FixtureItemDefinition rope = _bed.CreateItem("Rope", 1f);
            FixtureDialogueDefinition dialogue = ScriptableObject.CreateInstance<FixtureDialogueDefinition>();
            dialogue.lines = new List<FixtureLine>
            {
                new FixtureLine { text = "Mind the fog.", mood = FixtureMood.Calm, item = lantern, speakerEntityId = SpeakerId, Weight = 3 },
                new FixtureLine { text = "The ferry leaves at dusk.", mood = FixtureMood.Grim, item = null, speakerEntityId = string.Empty, Weight = 1 },
                new FixtureLine { text = "Take the rope.", mood = FixtureMood.Cheerful, item = rope, speakerEntityId = string.Empty, Weight = 7 },
            };
            StudioTestBed.MintId(dialogue);
            AssetDatabase.CreateAsset(dialogue, _bed.Folder + "/Ferry.asset");
            _bed.Runtime.Index.Rebuild();

            AuthorMemberInfo member = _bed.Runtime.Identity.Describe(dialogue)!.FindMember("lines")!;
            JToken read = _bed.Runtime.Resolver.Codec.ReadMember(dialogue, member);
            Assert.That(read, Is.TypeOf<JArray>());
            JArray lines = (JArray)read;
            Assert.That(lines.Count, Is.EqualTo(3));
            Assert.That(lines[0], Is.TypeOf<JObject>(), "elements are captured as objects, not type names");
            Assert.That(lines[1]["text"]?.ToString(), Is.EqualTo("The ferry leaves at dusk."));

            JToken indexed = _bed.Runtime.Index.FindNode(_bed.Ref(dialogue))!.Fields!["lines"].Value!;
            Assert.That(indexed[1]?["text"]?.ToString(), Is.EqualTo("The ferry leaves at dusk."), "index values of [Serializable] elements are objects too");

            JArray edited = (JArray)lines.DeepClone();
            edited[1]!["text"] = "The ferry is gone.";
            edited[1]!["mood"] = "Cheerful";
            ChangeSet changeSet = StudioTestBed.NewChangeSet("edit line 2", null, StudioTestBed.Set("op1", _bed.Ref(dialogue), "lines", edited));
            ApplyReport applied = _bed.Runtime.Engine.Apply(changeSet);
            Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics));
            Assert.That(dialogue.lines[1].text, Is.EqualTo("The ferry is gone."));
            Assert.That(dialogue.lines[1].mood, Is.EqualTo(FixtureMood.Cheerful));
            Assert.That(dialogue.lines[0].item, Is.SameAs(lantern), "untouched elements keep their references");

            JObject inverse = (JObject)applied.Outcome("op1")!.Undo!.Inverse!;
            Assert.That(inverse.ToString(), Does.Not.Contain(typeof(FixtureLine).FullName!), "the inverse holds values, not type names");

            HistoryResult undone = _bed.Runtime.History.Undo();
            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(dialogue.lines.Count, Is.EqualTo(3));
            Assert.That(dialogue.lines[0].text, Is.EqualTo("Mind the fog."));
            Assert.That(dialogue.lines[0].mood, Is.EqualTo(FixtureMood.Calm));
            Assert.That(dialogue.lines[0].item, Is.SameAs(lantern));
            Assert.That(dialogue.lines[0].speakerEntityId, Is.EqualTo(SpeakerId));
            Assert.That(dialogue.lines[0].Weight, Is.EqualTo(3));
            Assert.That(dialogue.lines[1].text, Is.EqualTo("The ferry leaves at dusk."));
            Assert.That(dialogue.lines[1].mood, Is.EqualTo(FixtureMood.Grim));
            Assert.That(dialogue.lines[1].item, Is.Null);
            Assert.That(dialogue.lines[2].item, Is.SameAs(rope));
            Assert.That(dialogue.lines[2].Weight, Is.EqualTo(7));
            Assert.That(JToken.DeepEquals(_bed.Runtime.Resolver.Codec.ReadMember(dialogue, member), lines), Is.True, "undo restores the exact captured value");

            HistoryResult redone = _bed.Runtime.History.Redo();
            Assert.That(redone.Ok, Is.True, string.Join("; ", redone.Diagnostics));
            Assert.That(dialogue.lines[1].text, Is.EqualTo("The ferry is gone."));
        }
    }
}
