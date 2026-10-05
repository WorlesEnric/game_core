// GameCore.Studio.UI.Tests - candidates replayed from JSON fixtures drive the edit engine and the journal: receive
// (artifacts fetched and retained), Preview (staged in Candidate mode, ghosts for moves), Compare (property diff), Apply
// (journal Applied, measured), Reject (journal Rejected, the gateway told), a tampered artifact makes the candidate
// invalid, a stale catalog revision is reported as StaleContext. A mechanism candidate shows the stage verdict from the
// journal and admits only through StageAdmission with a passing verdict. History panel undo/redo (an admission's undo
// goes through StageAdmission.Undo); the viewport move gizmo produces the same journal entry as a typed move
// (W-EDIT-05).
#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI.Tests
{
    public sealed class CandidateHistoryGizmoTests
    {
        private UiTestBed _bed = null!;
        private FixtureNpcDefinition _smith = null!;
        private FixtureAuthoredEntity _guard = null!;

        [SetUp]
        public void SetUp()
        {
            _bed = new UiTestBed();
            _smith = _bed.CreateNpc("Blacksmith", "Hello");
            _guard = _bed.SpawnEntity("Guard", new Vector3(1f, 0f, 2f), _smith);
            _bed.SaveScene();
            _bed.Runtime.Index.Rebuild();
        }

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

        private Dictionary<string, AuthoringRef> Refs() => new Dictionary<string, AuthoringRef> { ["npc"] = _bed.Ref(_smith), ["entity"] = _bed.Ref(_guard) };

        private CandidateEntry Receive(string fixture, string? catalog = null)
        {
            TestCandidate candidate = _bed.Gateway.LoadCandidate(fixture, Refs(), catalog ?? _bed.CatalogRevision());
            Task<CandidateEntry> task = _bed.Context.ReceiveCandidate(candidate.RequestId, candidate.ChangeSet.Id, candidate.ToolCatalogRevision);
            Assert.That(task.IsCompleted, Is.True);
            return task.Result;
        }

        [Test]
        public void Candidate_PreviewCompareApply_DrivesEngineAndJournal()
        {
            CandidateEntry entry = Receive("candidate-greeting.json");
            Assert.That(entry.Stage, Is.EqualTo(CandidateStage.Received), string.Join("; ", entry.Problems));
            Assert.That(entry.Badges, Does.Contain(CandidateRequirements.EditTime));

            IReadOnlyList<PropertyDiffRow> diff = CandidateCompare.PropertyDiff(_bed.Runtime, entry.ChangeSet);
            Assert.That(diff.Count, Is.EqualTo(2));
            Assert.That(diff[0].Field, Is.EqualTo("greeting"));
            Assert.That(diff[0].Current, Does.Contain("Hello"));
            Assert.That(diff[0].Proposed, Does.Contain("Welcome, traveller!"));
            Assert.That(diff[0].Changed, Is.True);

            StagedChangeSet staged = _bed.Context.Candidates.Preview(entry);
            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.Diagnostics));
            Assert.That(entry.Stage, Is.EqualTo(CandidateStage.Previewing));
            Assert.That(_bed.Runtime.Journal.Read(entry.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Candidate));
            Assert.That(_smith.greeting, Is.EqualTo("Hello"), "staging changes nothing");

            ApplyReport report = _bed.Context.Candidates.Apply(entry);
            Assert.That(report.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", report.Diagnostics));
            Assert.That(entry.Stage, Is.EqualTo(CandidateStage.Applied));
            Assert.That(entry.ApplyMilliseconds, Is.Not.Null);
            Assert.That(_smith.greeting, Is.EqualTo("Welcome, traveller!"));
            Assert.That(_guard.level, Is.EqualTo(4));
            ChangeSet journaled = _bed.Runtime.Journal.Read(entry.Id)!;
            Assert.That(journaled.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(journaled.Intent.Origin, Is.EqualTo(IntentOrigin.Agent));
            UiTestBed.Timing("candidate apply", entry.ApplyMilliseconds!.Value);

            HistoryPanelView history = new HistoryPanelView(_bed.Context);
            Assert.That(history.Shown.Count, Is.EqualTo(1));
            HistoryResult undo = history.Undo(null);
            Assert.That(undo.Ok, Is.True, string.Join("; ", undo.Diagnostics));
            Assert.That(_smith.greeting, Is.EqualTo("Hello"));
            Assert.That(_guard.level, Is.EqualTo(1));
            Assert.That(history.Shown[0].State, Is.EqualTo(ChangeSetState.Undone));
            HistoryResult redo = history.Redo(null);
            Assert.That(redo.Ok, Is.True, string.Join("; ", redo.Diagnostics));
            Assert.That(_smith.greeting, Is.EqualTo("Welcome, traveller!"));
            Assert.That(history.StatusText, Does.StartWith("Redo done"));
        }

        [Test]
        public void Candidate_RejectJournalsAndSendsTheReason()
        {
            CandidateEntry entry = Receive("candidate-greeting.json");
            _bed.Context.Candidates.Preview(entry);
            _bed.Context.Candidates.Reject(entry, "Too informal for a blacksmith");
            Assert.That(entry.Stage, Is.EqualTo(CandidateStage.Rejected));
            Assert.That(_bed.Gateway.Rejected.Count, Is.EqualTo(1));
            Assert.That(_bed.Gateway.Rejected[0].Key, Is.EqualTo(entry.RequestId));
            Assert.That(_bed.Gateway.Rejected[0].Value, Is.EqualTo("Too informal for a blacksmith"));
            Assert.That(_bed.Runtime.Journal.Read(entry.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(_smith.greeting, Is.EqualTo("Hello"));
            Assert.That(_bed.Runtime.Staging.GhostsOf(entry.Id), Is.Empty);
        }

        [Test]
        public void Candidate_ArtifactsAreRetainedOnReceiptAndUnusedOnesAreReported()
        {
            CandidateEntry entry = Receive("candidate-artifact.json");
            Assert.That(entry.Stage, Is.EqualTo(CandidateStage.Received), string.Join("; ", entry.Problems));
            Assert.That(_bed.Runtime.Artifacts.Has("a3f445e41d80ac97e6094f18a53e3e401aa6b0cad99b7eeade490aed66620eeb"), Is.True, "artifacts are fetched, verified and retained before review");
            Assert.That(_bed.Gateway.ArtifactFetches, Is.EqualTo(1));
            StagedChangeSet staged = _bed.Context.Candidates.Preview(entry);
            Assert.That(staged.Ok, Is.False);
            Assert.That(staged.Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == DiagnosticCodes.CandidateInvalid && d.Message.Contains("no operation uses it")));
            Assert.That(_bed.Context.Candidates.Apply(entry).Ok, Is.False, "an invalid candidate is never applied");
            Assert.That(_smith.greeting, Is.EqualTo("Hello"));
        }

        [Test]
        public void Candidate_TamperedArtifactIsInvalidAndNotImported()
        {
            CandidateEntry entry = Receive("candidate-tampered.json");
            Assert.That(entry.Stage, Is.EqualTo(CandidateStage.Invalid));
            Assert.That(entry.Problems.Count, Is.GreaterThan(0));
            Assert.That(entry.Problems[0].Code, Is.EqualTo(DiagnosticCodes.CandidateInvalid));
            Assert.That(_bed.Runtime.Artifacts.Has("a3f445e41d80ac97e6094f18a53e3e401aa6b0cad99b7eeade490aed66620eeb"), Is.False);
            Assert.That(entry.IsOpen, Is.False, "an invalid candidate cannot be applied");
        }

        [Test]
        public void Candidate_StaleCatalogRevisionIsReported()
        {
            CandidateEntry entry = Receive("candidate-greeting.json", "sha256:" + new string('0', 64));
            StagedChangeSet staged = _bed.Context.Candidates.Preview(entry);
            Assert.That(staged.Diagnostics, Has.Some.Matches<Diagnostic>(d => d.Code == DiagnosticCodes.StaleContext));
        }

        [Test]
        public void Candidate_MovePreviewShowsAGhostAndToggleKeepsTheSceneUntouched()
        {
            ChangeSet typed = MoveChangeSets.Build(_bed.Runtime, _guard.gameObject, new Vector3(5f, 0f, 2f))!;
            ChangeSet agent = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Move the guard to the gate", IntentOrigin.Agent), typed.Operations,
                requirements: CandidateRequirements.Implied(_bed.Runtime, typed));
            Assert.That(CandidateRequirements.Badges(agent), Does.Contain(CandidateRequirements.RequiresPlayStop), "a move needs a world rebuild");
            CandidateEntry entry = _bed.Context.Candidates.Add("req_move", agent, _bed.CatalogRevision());
            StagedChangeSet staged = _bed.Context.Candidates.Preview(entry);
            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.Diagnostics));
            Assert.That(_bed.Runtime.Staging.GhostsOf(entry.Id).Count, Is.GreaterThanOrEqualTo(1), "a move previews as a ghost");
            Assert.That(_guard.transform.position, Is.EqualTo(new Vector3(1f, 0f, 2f)));
            _bed.Context.Candidates.ShowAfter(entry, false);
            Assert.That(entry.ShowingAfter, Is.False);
            _bed.Context.Candidates.ShowAfter(entry, true);
            ApplyReport report = _bed.Context.Candidates.Apply(entry);
            Assert.That(report.Ok, Is.True, string.Join("; ", report.Diagnostics));
            Assert.That(_guard.transform.position, Is.EqualTo(new Vector3(5f, 0f, 2f)));
            Assert.That(_bed.Runtime.Staging.GhostsOf(entry.Id), Is.Empty);
        }

        [Test]
        public void MechanismCandidate_ShowsTheVerdictFromTheJournalAndAdmitsOnlyWithAPass()
        {
            byte[] package = System.Text.Encoding.UTF8.GetBytes("not a real archive");
            string digest = ContentStamp.Sha256Hex(package);
            _bed.Runtime.Artifacts.Put(package, new ArtifactRef(digest, "application/gzip", package.LongLength, "package.tgz", null, "package"));
            Operation propose = new Operation("op1", BuiltInToolIds.MechanismPropose, null, new JObject
            {
                ["description"] = "A pressure plate",
                ["package"] = new JObject { ["artifact"] = ContentStamp.Prefix + digest },
                ["stageInputs"] = new JArray("Assets/Hollowmere/World"),
            });
            ChangeSet changeSet = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Add a pressure plate", IntentOrigin.Agent), new[] { propose },
                artifacts: new[] { new ArtifactRef(digest, "application/gzip", package.LongLength, "package.tgz", null, "package") },
                requirements: new Requirements(RuntimeApply.Compile, false, true, false));
            CandidateEntry entry = _bed.Context.Candidates.Add("req_mechanism", changeSet, _bed.CatalogRevision());
            Assert.That(CandidateRequirements.NeedsStageVerdict(entry.ChangeSet), Is.True);
            Assert.That(CandidateStaging.PackageRef(entry.ChangeSet), Is.EqualTo(ContentStamp.Prefix + digest));
            Assert.That(_bed.Context.Candidates.StageStateOf(entry).Label, Is.EqualTo("not staged"));
            Assert.That(() => _bed.Context.Candidates.Apply(entry), Throws.InvalidOperationException, "a mechanism never applies directly");
            Assert.That(() => _bed.Context.Candidates.Admit(entry, false), Throws.InvalidOperationException, "Admit needs a passing verdict");
            Assert.That(_bed.Context.Candidates.CanRequestStage, Is.False, "the test gateway has no staging lane");

            _bed.Runtime.Journal.Write(changeSet.WithState(ChangeSetState.Candidate));
            StageAdmission.Of(_bed.Runtime).MarkStagePending(entry.Id, "slot-7");
            StageState pending = _bed.Context.Candidates.StageStateOf(entry);
            Assert.That(pending.Label, Is.EqualTo("verdict pending"));
            Assert.That(pending.Verdict!.Detail, Does.Contain("slot-7"));
            CandidateStaging.MarkVerdict(_bed.Runtime, entry.Id, ScenarioStatus.Fail, "unity-editmode failed");
            Assert.That(_bed.Context.Candidates.StageStateOf(entry).Label, Is.EqualTo("verdict fail"));
            Assert.That(_bed.Context.Candidates.StageStateOf(entry).VerdictPassed, Is.False);

            CandidatePanelView panel = new CandidatePanelView(_bed.Context);
            panel.Select(entry.Id);
            Assert.That(panel.Q<VisualElement>("candidate-stage-state"), Is.Not.Null);
            Assert.That(panel.Q<Button>("candidate-admit").enabledSelf, Is.False);
            Assert.That(panel.Q<Button>("candidate-apply").enabledSelf, Is.False);
        }

        [Test]
        public void History_UndoOfAnAdmissionGoesThroughStageAdmission()
        {
            Operation admit = new Operation("op1", MechanismAdmission.AdmitTool, null, new JObject { ["package"] = new JObject { ["artifact"] = ContentStamp.Prefix + new string('a', 64) } }, null, null, RuntimeApply.Compile);
            ChangeSet admitted = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Admit a pressure plate", IntentOrigin.Agent), new[] { admit });
            _bed.Runtime.Journal.Write(admitted.WithState(ChangeSetState.Applied));
            StageAdmission.Configure(_bed.Runtime, new AdmissionOptions { Compiler = new NoCompile(), Catalog = new FixedCatalog(), PlayModeProbe = () => false });
            HistoryPanelView history = new HistoryPanelView(_bed.Context);
            HistoryResult result = history.Undo(admitted.Id);
            Assert.That(result.Ok, Is.False, "the fabricated admission has no trusted durable record");
            Assert.That(history.StatusText, Does.Contain("Undo refused"));
            Assert.That(_bed.Runtime.Journal.Read(admitted.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
        }

        /// <summary>A compiler that never compiles (the admission seam; no recompile inside a test).</summary>
        private sealed class NoCompile : IAdmissionCompiler
        {
            public void Compile(string reason, System.Action<AdmissionCompileResult> done) => done(new AdmissionCompileResult(true, false, "not compiled in tests"));
        }

        /// <summary>A catalog with a fixed world fingerprint and no mechanisms.</summary>
        private sealed class FixedCatalog : IAdmissionCatalog
        {
            public string? WorldFingerprint(out string? problem)
            {
                problem = null;
                return "sha256:" + new string('1', 64);
            }

            public string? MechanismFingerprint(string catalogType, out string? problem)
            {
                problem = null;
                return null;
            }
        }

        [Test]
        public void History_FilterByTargetAndUndoRedoManualEdits()
        {
            FixtureNpcDefinition other = _bed.CreateNpc("Ferryman", "Ahoy");
            _bed.Runtime.Index.Rebuild();
            ApplyReport first = _bed.Runtime.Engine.Apply(new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Smith greeting", IntentOrigin.Manual),
                new[] { new Operation("op1", BuiltInToolIdsExt.Set, _bed.Ref(_smith), new JObject { ["field"] = "greeting", ["value"] = "Morning" }) }));
            ApplyReport second = _bed.Runtime.Engine.Apply(new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Ferryman greeting", IntentOrigin.Manual),
                new[] { new Operation("op1", BuiltInToolIdsExt.Set, _bed.Ref(other), new JObject { ["field"] = "greeting", ["value"] = "Ahoy there" }) }));
            Assert.That(first.Ok && second.Ok, Is.True);

            HistoryPanelView history = new HistoryPanelView(_bed.Context);
            Assert.That(history.Shown.Count, Is.EqualTo(2));
            Assert.That(history.Shown[0].Id, Is.EqualTo(second.Entry.Id), "newest first");
            history.Filter = "Ferryman";
            Assert.That(history.Shown.Count, Is.EqualTo(1));
            Assert.That(history.Shown[0].Id, Is.EqualTo(second.Entry.Id));
            history.Filter = string.Empty;

            Assert.That(history.Undo(null).Ok, Is.True);
            Assert.That(other.greeting, Is.EqualTo("Ahoy"));
            Assert.That(history.Undo(null).Ok, Is.True);
            Assert.That(_smith.greeting, Is.EqualTo("Hello"));
            Assert.That(history.Undo(null).Ok, Is.False, "nothing left to undo");
            Assert.That(history.Redo(null).Ok, Is.True);
            Assert.That(_smith.greeting, Is.EqualTo("Morning"));
            Assert.That(history.Shown[1].Origin, Is.EqualTo("manual"));
        }

        [Test]
        public void WEdit05_ViewportGizmoDragEqualsTypedMove()
        {
            Vector3 start = _guard.transform.position;
            ViewportMoveGizmo gizmo = new ViewportMoveGizmo(() => _bed.Runtime);
            System.Func<Vector3, Vector2?> project = world => new Vector2(400f + (world.x * 50f), 300f - (world.y * 50f) + (world.z * 10f));
            gizmo.Refresh(_guard.gameObject, project);
            Vector2 origin = project(start)!.Value;
            Assert.That(gizmo.HitTest(origin + new Vector2(30f, 0f)), Is.EqualTo(0), "the x handle");
            Assert.That(gizmo.BeginDrag(0, origin, 1), Is.True);
            gizmo.DragTo(origin + new Vector2(50f, 0f));
            gizmo.DragTo(origin + new Vector2(100f, 0f));
            gizmo.DragTo(origin + new Vector2(150f, 0f));
            Assert.That(_bed.Runtime.Journal.List().Count, Is.EqualTo(0), "dragging writes nothing");
            ApplyReport? dragged = gizmo.EndDrag();
            Assert.That(dragged, Is.Not.Null);
            Assert.That(dragged!.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", dragged.Diagnostics));
            Vector3 target = start + new Vector3(3f, 0f, 0f);
            Assert.That(Vector3.Distance(_guard.transform.position, target), Is.LessThan(0.0001f));
            Assert.That(_bed.Runtime.Journal.List().Count, Is.EqualTo(1), "one drag is one change set");

            Assert.That(_bed.Runtime.History.Undo().Ok, Is.True);
            ChangeSet typed = MoveChangeSets.Build(_bed.Runtime, _guard.gameObject, _guard.transform.position + new Vector3(3f, 0f, 0f))!;
            ApplyReport typedReport = _bed.Runtime.Engine.Apply(typed);
            Assert.That(typedReport.State, Is.EqualTo(ChangeSetState.Applied));

            JObject fromGizmo = Comparable(dragged.Entry.Id);
            fromGizmo["state"] = "Applied";
            JObject fromTyping = Comparable(typed.Id);
            Assert.That(JToken.DeepEquals(fromGizmo, fromTyping), Is.True, fromGizmo + "\n---\n" + fromTyping);
        }
    }
}
