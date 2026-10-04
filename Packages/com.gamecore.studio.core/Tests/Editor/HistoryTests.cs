// GameCore.Studio.Edit.Tests - journal undo/redo with retained artifacts (no regeneration), undo conflicts, the journal
// and redo stack surviving a runtime rebuild (domain reload), and crash recovery of Interrupted entries (SADR-009,
// 02 s5).
#nullable enable
using System.Diagnostics;
using System.IO;
using System.Text;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class HistoryTests
    {
        private StudioTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new StudioTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        [Test]
        public void UndoRedo_ReusesRetainedArtifactsAndNeverCallsTheGateway()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            _bed.Runtime.Index.Rebuild();
            byte[] note = Encoding.UTF8.GetBytes("The ferryman keeps a lantern lit.\n");
            string digest = _bed.Runtime.Artifacts.Put(note, null);
            string notePath = _bed.Folder + "/FerrymanNote.txt";
            string noteFile = Path.Combine(StudioTestBed.ProjectRoot, notePath);

            ChangeSet changeSet = new ChangeSet(
                IdDerivation.NewChangeSetId(),
                ChangeSet.SchemaId,
                new Intent("import a note and edit the lantern", IntentOrigin.Manual),
                new[]
                {
                    StudioTestBed.Op("op1", BuiltInToolIds.AssetImport, null, new JObject { ["path"] = notePath, ["artifact"] = new JObject { ["artifact"] = ContentStamp.Prefix + digest } }),
                    StudioTestBed.Set("op2", _bed.Ref(lantern), "weight", 6),
                },
                artifacts: new[] { new ArtifactRef(digest, "text/plain", note.LongLength, "FerrymanNote.txt") });
            ApplyReport applied = _bed.Runtime.Engine.Apply(changeSet);
            Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics));
            Assert.That(File.Exists(noteFile), Is.True);
            Assert.That(lantern.weight, Is.EqualTo(6f));

            Stopwatch watch = Stopwatch.StartNew();
            HistoryResult undone = _bed.Runtime.History.Undo();
            StudioTestBed.Timing("journal undo (import + set)", watch);
            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(undone.State, Is.EqualTo(ChangeSetState.Undone));
            Assert.That(File.Exists(noteFile), Is.False, "undo removed the imported file");
            Assert.That(lantern.weight, Is.EqualTo(2f));
            Assert.That(_bed.Runtime.History.NextRedo, Is.EqualTo(changeSet.Id));

            int readsBefore = _bed.Runtime.Artifacts.ReadCount;
            watch.Restart();
            HistoryResult redone = _bed.Runtime.History.Redo();
            StudioTestBed.Timing("journal redo (import + set)", watch);
            Assert.That(redone.Ok, Is.True, string.Join("; ", redone.Diagnostics));
            Assert.That(redone.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(File.Exists(noteFile), Is.True);
            Assert.That(File.ReadAllBytes(noteFile), Is.EqualTo(note), "redo wrote the retained bytes");
            Assert.That(_bed.Runtime.Artifacts.ReadCount, Is.GreaterThan(readsBefore), "redo read the retained artifact");
            Assert.That(lantern.weight, Is.EqualTo(6f));
            Assert.That(_bed.Gateway.Calls, Is.EqualTo(0), "nothing was regenerated");
            Assert.That(_bed.Runtime.Journal.Read(changeSet.Id)?.State, Is.EqualTo(ChangeSetState.Applied));
        }

        [Test]
        public void Redo_DoesNotRepeatAgentRequests()
        {
            FixtureNpcDefinition npc = _bed.CreateNpc("Ferryman");
            _bed.Runtime.Index.Rebuild();
            ChangeSet changeSet = StudioTestBed.NewChangeSet(
                "request a portrait",
                null,
                StudioTestBed.Op("op1", BuiltInToolIds.AssetGenerate, null, new JObject { ["kind"] = "portrait", ["prompt"] = "an old ferryman" }),
                StudioTestBed.Set("op2", _bed.Ref(npc), "greeting", "Fog again."));
            Assert.That(_bed.Runtime.Engine.Apply(changeSet).State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(_bed.Gateway.Calls, Is.EqualTo(1));

            Assert.That(_bed.Runtime.History.Undo().Ok, Is.True);
            HistoryResult redone = _bed.Runtime.History.Redo();
            Assert.That(redone.Ok, Is.True, string.Join("; ", redone.Diagnostics));
            Assert.That(_bed.Gateway.Calls, Is.EqualTo(1), "redo does not reopen the generation task");
            Assert.That(npc.greeting, Is.EqualTo("Fog again."));
            ChangeSet entry = _bed.Runtime.Journal.Read(changeSet.Id)!;
            Assert.That(entry.Outcomes![0].Status, Is.EqualTo(OutcomeStatus.Skipped));
        }

        [Test]
        public void Undo_RefusesWithConflictWhenTheObjectChangedSince()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            _bed.Runtime.Index.Rebuild();
            ChangeSet changeSet = StudioTestBed.NewChangeSet("weight", null, StudioTestBed.Set("op1", _bed.Ref(lantern), "weight", 5));
            Assert.That(_bed.Runtime.Engine.Apply(changeSet).State, Is.EqualTo(ChangeSetState.Applied));

            SerializedObject serialized = new SerializedObject(lantern);
            serialized.FindProperty("displayName").stringValue = "Later edit";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            HistoryResult refused = _bed.Runtime.History.Undo();
            Assert.That(refused.Ok, Is.False);
            Assert.That(refused.Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == DiagnosticCodes.Conflict && d.Data?["actual"] != null));
            Assert.That(lantern.weight, Is.EqualTo(5f));

            HistoryResult forced = _bed.Runtime.History.Undo(null, true);
            Assert.That(forced.Ok, Is.True);
            Assert.That(lantern.weight, Is.EqualTo(2f));
            Assert.That(lantern.displayName, Is.EqualTo("Later edit"), "undo restores only what the change set changed");
        }

        [Test]
        public void JournalAndRedoStack_SurviveARuntimeRebuild()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            _bed.Runtime.Index.Rebuild();
            ChangeSet first = StudioTestBed.NewChangeSet("first", null, StudioTestBed.Set("op1", _bed.Ref(lantern), "weight", 3));
            Assert.That(_bed.Runtime.Engine.Apply(first).State, Is.EqualTo(ChangeSetState.Applied));
            ChangeSet second = StudioTestBed.NewChangeSet("second", null, StudioTestBed.Set("op1", _bed.Ref(lantern), "weight", 4));
            Assert.That(_bed.Runtime.Engine.Apply(second).State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(_bed.Runtime.History.Undo().ChangeSetId, Is.EqualTo(second.Id));
            _bed.Runtime.Index.SaveCache();

            StudioRuntime reloaded = _bed.Reload();
            Assert.That(reloaded.Journal.List().Count, Is.EqualTo(2));
            Assert.That(reloaded.History.NextRedo, Is.EqualTo(second.Id), "the redo stack is durable");
            Assert.That(reloaded.History.NextUndo, Is.EqualTo(first.Id));
            HistoryResult redone = reloaded.History.Redo();
            Assert.That(redone.Ok, Is.True, string.Join("; ", redone.Diagnostics));
            Assert.That(lantern.weight, Is.EqualTo(4f));
            Assert.That(reloaded.History.Undo().ChangeSetId, Is.EqualTo(second.Id));
            Assert.That(reloaded.History.Undo().ChangeSetId, Is.EqualTo(first.Id));
            Assert.That(lantern.weight, Is.EqualTo(2f));
        }

        [Test]
        public void Crash_LeavesAnInterruptedEntryThatCanBeRolledBack()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            FixtureNpcDefinition npc = _bed.CreateNpc("Ferryman", "Hello");
            _bed.Runtime.Index.Rebuild();
            StudioRuntime crashing = _bed.Reload(new EngineOptions
            {
                FaultHook = (point, opId) =>
                {
                    if (point == EngineFaultPoint.BeforeOperation && opId == "op3")
                    {
                        throw new SimulatedCrashException("editor died before op3");
                    }
                },
            });
            ChangeSet changeSet = StudioTestBed.NewChangeSet(
                "crash",
                null,
                StudioTestBed.Set("op1", _bed.Ref(lantern), "weight", 8),
                StudioTestBed.Set("op2", _bed.Ref(npc), "greeting", "Crashing"),
                StudioTestBed.Set("op3", _bed.Ref(lantern), "displayName", "Never"));
            Assert.Throws<SimulatedCrashException>(() => crashing.Engine.Apply(changeSet));

            StudioRuntime recovered = _bed.Reload();
            Assert.That(recovered.History.Interrupted(), Has.Some.Matches<JournalRecord>(record => record.Id == changeSet.Id));
            ChangeSet entry = recovered.Journal.Read(changeSet.Id)!;
            Assert.That(entry.State, Is.EqualTo(ChangeSetState.Interrupted));
            Assert.That(entry.Outcomes!.Count, Is.EqualTo(2), "outcomes were checkpointed per operation");

            HistoryResult rolledBack = recovered.History.RollbackInterrupted(changeSet.Id);
            Assert.That(rolledBack.Ok, Is.True);
            Assert.That(rolledBack.State, Is.EqualTo(ChangeSetState.Failed));
            Assert.That(lantern.weight, Is.EqualTo(2f));
            Assert.That(npc.greeting, Is.EqualTo("Hello"));
            Assert.That(recovered.History.Interrupted(), Is.Empty);
        }

        [Test]
        public void Crash_InterruptedEntryCanBeResumed()
        {
            FixtureItemDefinition lantern = _bed.CreateItem("Lantern", 2f);
            _bed.Runtime.Index.Rebuild();
            StudioRuntime crashing = _bed.Reload(new EngineOptions
            {
                FaultHook = (point, opId) =>
                {
                    if (point == EngineFaultPoint.AfterOperation && opId == "op1")
                    {
                        throw new SimulatedCrashException("editor died after op1");
                    }
                },
            });
            ChangeSet changeSet = StudioTestBed.NewChangeSet(
                "resume",
                null,
                StudioTestBed.Set("op1", _bed.Ref(lantern), "weight", 8),
                new Operation("op2", BuiltInToolIdsExt.Set, _bed.Ref(lantern, false), new JObject { ["field"] = "displayName", ["value"] = "Resumed" }, null, Preconditions.None));
            Assert.Throws<SimulatedCrashException>(() => crashing.Engine.Apply(changeSet));

            StudioRuntime recovered = _bed.Reload();
            HistoryResult resumed = recovered.History.ResumeInterrupted(changeSet.Id);
            Assert.That(resumed.Ok, Is.True, string.Join("; ", resumed.Diagnostics));
            Assert.That(resumed.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(lantern.displayName, Is.EqualTo("Resumed"));
            Assert.That(recovered.Journal.Read(changeSet.Id)!.Outcomes!.Count, Is.EqualTo(2));
        }
    }
}
