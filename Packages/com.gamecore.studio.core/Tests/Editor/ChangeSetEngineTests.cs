// GameCore.Studio.Edit.Tests - the edit engine: W-EDIT-02 (a 5-op change set with one stale op under AllOrNothing and
// BestEffort), AllOrNothing rollback when a tool throws (Undo group + asset-level inverses), conflict and rebase, and
// the journal location (docs/studio/03-authoring-contracts.md s6, s7).
#nullable enable
using System.Diagnostics;
using System.IO;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class ChangeSetEngineTests
    {
        private StudioTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new StudioTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        private sealed class FakeWorld
        {
            public FixtureItemDefinition Lantern = null!;
            public FixtureItemDefinition Flask = null!;
            public FixtureNpcDefinition Ferryman = null!;
            public FixtureNpcDefinition Keeper = null!;
            public FixtureAuthoredEntity Entity = null!;
        }

        private FakeWorld Build()
        {
            FakeWorld world = new FakeWorld
            {
                Lantern = _bed.CreateItem("Lantern", 2f),
                Flask = _bed.CreateItem("OilFlask", 1f),
            };
            world.Ferryman = _bed.CreateNpc("Ferryman", "Hello", world.Lantern);
            world.Keeper = _bed.CreateNpc("Keeper", "Welcome");
            world.Entity = _bed.SpawnEntity("FerrymanEntity", Vector3.zero, world.Ferryman);
            _bed.SaveScene();
            _bed.Runtime.Index.Rebuild();
            return world;
        }

        /// <summary>Five operations; op5's target changed after planning (stale stamp).</summary>
        private ChangeSet FiveOps(FakeWorld world, ApplyPolicy policy)
        {
            AuthoringRef flaskPlanned = _bed.Ref(world.Flask);
            SerializedObject serialized = new SerializedObject(world.Flask);
            serialized.FindProperty("displayName").stringValue = "Edited elsewhere";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return StudioTestBed.NewChangeSet(
                "W-EDIT-02",
                policy,
                StudioTestBed.Set("op1", _bed.Ref(world.Lantern), "weight", 4.5),
                StudioTestBed.Set("op2", _bed.Ref(world.Ferryman), "greeting", "Mind the fog."),
                StudioTestBed.Op("op3", "fixture.setGreeting", _bed.Ref(world.Keeper), new JObject { ["greeting"] = "Lanterns here." }),
                StudioTestBed.Set("op4", _bed.Ref(world.Entity), "level", 3),
                StudioTestBed.Set("op5", flaskPlanned, "displayName", "Lamp oil"));
        }

        [Test]
        public void WEdit02_AllOrNothing_RefusesTheStaleOpAndAppliesNothing()
        {
            FakeWorld world = Build();
            ChangeSet changeSet = FiveOps(world, ApplyPolicy.AllOrNothing);
            Stopwatch watch = Stopwatch.StartNew();
            ApplyReport report = _bed.Runtime.Engine.Apply(changeSet);
            StudioTestBed.Timing("W-EDIT-02 AllOrNothing apply (5 ops)", watch);

            Assert.That(report.State, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(report.Outcome("op5")!.Status, Is.EqualTo(OutcomeStatus.Refused));
            Assert.That(report.Outcome("op5")!.Code, Is.EqualTo(DiagnosticCodes.Conflict));
            foreach (string opId in new[] { "op1", "op2", "op3", "op4" })
            {
                Assert.That(report.Outcome(opId)!.Status, Is.EqualTo(OutcomeStatus.Skipped), opId);
            }

            Assert.That(report.Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == DiagnosticCodes.Conflict && d.Data != null && d.Data["expected"] != null && d.Data["actual"] != null));
            Assert.That(world.Lantern.weight, Is.EqualTo(2f));
            Assert.That(world.Ferryman.greeting, Is.EqualTo("Hello"));
            Assert.That(world.Keeper.greeting, Is.EqualTo("Welcome"));
            Assert.That(world.Entity.level, Is.EqualTo(1));
            Assert.That(_bed.Runtime.Journal.Read(changeSet.Id)?.State, Is.EqualTo(ChangeSetState.Rejected));
        }

        [Test]
        public void WEdit02_BestEffort_AppliesTheOthersAndRecordsPerOpOutcomes()
        {
            FakeWorld world = Build();
            ChangeSet changeSet = FiveOps(world, ApplyPolicy.BestEffort);
            Stopwatch watch = Stopwatch.StartNew();
            ApplyReport report = _bed.Runtime.Engine.Apply(changeSet);
            StudioTestBed.Timing("W-EDIT-02 BestEffort apply (5 ops)", watch);

            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(report.Outcome("op5")!.Status, Is.EqualTo(OutcomeStatus.Refused));
            Assert.That(report.Outcome("op5")!.Code, Is.EqualTo(DiagnosticCodes.Conflict));
            foreach (string opId in new[] { "op1", "op2", "op3", "op4" })
            {
                Assert.That(report.Outcome(opId)!.Status, Is.EqualTo(OutcomeStatus.Applied), opId);
                Assert.That(report.Outcome(opId)!.Undo, Is.Not.Null, opId + " records its inverse");
            }

            Assert.That(world.Lantern.weight, Is.EqualTo(4.5f));
            Assert.That(world.Ferryman.greeting, Is.EqualTo("Mind the fog."));
            Assert.That(world.Keeper.greeting, Is.EqualTo("Lanterns here."));
            Assert.That(world.Entity.level, Is.EqualTo(3));
            Assert.That(world.Flask.displayName, Is.EqualTo("Edited elsewhere"), "the stale op did not overwrite the concurrent edit");

            ChangeSet? journaled = _bed.Runtime.Journal.Read(changeSet.Id);
            Assert.That(journaled?.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(journaled!.Timestamps?.Applied, Is.Not.Null);
            Assert.That(journaled.Outcomes!.Count, Is.EqualTo(5));
        }

        [Test]
        public void AllOrNothing_RollsBackUndoAndAssetLevelChangesWhenAToolThrows()
        {
            FakeWorld world = Build();
            string createdPath = _bed.Folder + "/Rope.asset";
            ChangeSet changeSet = StudioTestBed.NewChangeSet(
                "rollback",
                ApplyPolicy.AllOrNothing,
                StudioTestBed.Set("op1", _bed.Ref(world.Lantern), "weight", 7),
                StudioTestBed.Op("op2", BuiltInToolIdsExt.Create, null, new JObject { ["type"] = "fixture.item", ["name"] = "Rope", ["path"] = createdPath }),
                StudioTestBed.Op("op3", "fixture.fail", _bed.Ref(world.Ferryman), new JObject { ["message"] = "boom" }));

            ApplyReport report = _bed.Runtime.Engine.Apply(changeSet);

            Assert.That(report.State, Is.EqualTo(ChangeSetState.Failed));
            Assert.That(report.RolledBack, Is.True);
            Assert.That(report.Outcome("op3")!.Status, Is.EqualTo(OutcomeStatus.Failed));
            Assert.That(report.Outcome("op1")!.Status, Is.EqualTo(OutcomeStatus.Skipped));
            Assert.That(report.Outcome("op2")!.Status, Is.EqualTo(OutcomeStatus.Skipped));
            Assert.That(world.Lantern.weight, Is.EqualTo(2f), "Undo.RevertAllDownToGroup restored the field");
            Assert.That(File.Exists(Path.Combine(StudioTestBed.ProjectRoot, createdPath)), Is.False, "the asset-level inverse deleted the created asset");
            Assert.That(_bed.Runtime.Journal.Read(changeSet.Id)?.State, Is.EqualTo(ChangeSetState.Failed));
        }

        [Test]
        public void Conflict_RebaseReplansAgainstTheCurrentStamp()
        {
            FakeWorld world = Build();
            AuthoringRef planned = _bed.Ref(world.Lantern);
            SerializedObject serialized = new SerializedObject(world.Lantern);
            serialized.FindProperty("displayName").stringValue = "Brass lantern";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            StagedChangeSet staged = _bed.Runtime.Engine.Stage(StudioTestBed.NewChangeSet("rebase", null, StudioTestBed.Set("op1", planned, "weight", 3)));
            Assert.That(staged.Conflicts.Count, Is.EqualTo(1));
            Assert.That(staged.Ok, Is.False);

            StagedChangeSet rebased = _bed.Runtime.Engine.Rebase(staged);
            Assert.That(rebased.Ok, Is.True, string.Join("; ", rebased.AllDiagnostics));
            ApplyReport report = _bed.Runtime.Engine.Apply(rebased);
            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(world.Lantern.weight, Is.EqualTo(3f));
            Assert.That(world.Lantern.displayName, Is.EqualTo("Brass lantern"));
        }

        [Test]
        public void Journal_EntryLivesUnderYearAndMonthOfItsId()
        {
            FakeWorld world = Build();
            ChangeSet changeSet = StudioTestBed.NewChangeSet("journal", null, StudioTestBed.Set("op1", _bed.Ref(world.Lantern), "weight", 5));
            _bed.Runtime.Engine.Apply(changeSet);
            string path = _bed.Runtime.Journal.PathOf(changeSet.Id);
            System.DateTime time = Journal.TimeOf(changeSet.Id);
            StringAssert.EndsWith(Path.Combine(time.Year.ToString("0000"), time.Month.ToString("00"), changeSet.Id + ".json"), path);
            Assert.That(File.Exists(path), Is.True);
            ChangeSet parsed = StudioJson.Deserialize<ChangeSet>(File.ReadAllText(path));
            Assert.That(parsed.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(new ChangeSetValidator(_bed.Runtime.Registry.Catalog).Validate(parsed), Is.Empty, "a journal entry is a valid change set");
        }

        [Test]
        public void Queue_SerializesAsyncApplies()
        {
            FakeWorld world = Build();
            System.Threading.Tasks.Task<ApplyReport> first = _bed.Runtime.Engine.ApplyAsync(StudioTestBed.NewChangeSet("a", null, StudioTestBed.Set("op1", _bed.Ref(world.Lantern), "weight", 5)));
            System.Threading.Tasks.Task<ApplyReport> second = _bed.Runtime.Engine.ApplyAsync(StudioTestBed.NewChangeSet("b", null, StudioTestBed.Set("op1", _bed.Ref(world.Ferryman), "greeting", "Hi")));
            Assert.That(_bed.Runtime.Queue.Pending, Is.EqualTo(2));
            Assert.That(_bed.Runtime.Queue.Drain(), Is.EqualTo(2));
            Assert.That(first.Result.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(second.Result.State, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(world.Lantern.weight, Is.EqualTo(5f));
            Assert.That(world.Ferryman.greeting, Is.EqualTo("Hi"));
        }
    }
}
