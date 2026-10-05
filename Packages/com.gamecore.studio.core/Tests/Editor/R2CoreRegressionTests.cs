#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using GameCore.Studio.Authoring;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class R2CoreRegressionTests
    {
        private StudioTestBed _bed = null!;
        [SetUp] public void SetUp() => _bed = new StudioTestBed();
        [TearDown] public void TearDown() => _bed.Dispose();

        private ChangeSet Import(string path, JObject? settings = null)
        {
            string digest = _bed.Runtime.Artifacts.Put(Encoding.UTF8.GetBytes("replacement"), null);
            return StudioRuntime.Single("import", IntentOrigin.Manual, new Operation("op1", BuiltInToolIds.AssetImport, null,
                new JObject { ["path"] = path, ["artifact"] = new JObject { ["artifact"] = "sha256:" + digest }, ["importer"] = settings }));
        }

        [TestCase("Editor/Evil.cs")]
        [TestCase("plugin.dll")]
        [TestCase("plugin.so")]
        [TestCase("plugin.asmdef")]
        [TestCase("plugin.asmref")]
        [TestCase("compiler.rsp")]
        [TestCase("thing.prefab")]
        [TestCase("thing.controller")]
        [TestCase("thing.mat")]
        [TestCase("Editor/data.txt")]
        public void R2_07_ExecutableAndHookPathsRefusedBeforeWrite(string suffix)
        {
            string path = _bed.Folder + "/" + suffix;
            ApplyReport report = _bed.Runtime.Engine.Apply(Import(path));
            Assert.That(report.Ok, Is.False);
            Assert.That(File.Exists(_bed.Runtime.Paths.Absolute(path)), Is.False);
            Assert.That(report.Outcomes[0].Detail, Does.Contain("media_"));
        }

        [Test]
        public void R2_08_SourcePathNeverReadAndAbsentFromCatalog()
        {
            Assert.That(_bed.Runtime.Registry.Catalog.FindTool(BuiltInToolIds.AssetImport)!.FindArg("source"), Is.Null);
            ChangeSet candidate = StudioRuntime.Single("source", IntentOrigin.Manual, new Operation("op1", BuiltInToolIds.AssetImport, null,
                new JObject { ["path"] = _bed.Folder + "/data.txt", ["source"] = "/unreadable/not-an-artifact" }));
            Assert.That(_bed.Runtime.Engine.Stage(candidate).Ok, Is.False);
        }

        [Test]
        public void R2_02_InvalidImporterLeavesExistingBytesAndMetaUnchanged()
        {
            string path = _bed.Folder + "/data.txt";
            File.WriteAllText(_bed.Runtime.Paths.Absolute(path), "original");
            AssetDatabase.ImportAsset(path);
            byte[] meta = File.ReadAllBytes(_bed.Runtime.Paths.Absolute(path) + ".meta");
            Assert.That(_bed.Runtime.Engine.Apply(Import(path, new JObject { ["arbitraryHook"] = true })).Ok, Is.False);
            Assert.That(File.ReadAllText(_bed.Runtime.Paths.Absolute(path)), Is.EqualTo("original"));
            Assert.That(File.ReadAllBytes(_bed.Runtime.Paths.Absolute(path) + ".meta"), Is.EqualTo(meta));
        }

        [Test]
        public void R2_03_CrashInsideFileWriteHasDurablePreimage()
        {
            string path = _bed.Folder + "/data.txt";
            File.WriteAllText(_bed.Runtime.Paths.Absolute(path), "original");
            AssetDatabase.ImportAsset(path);
            ChangeSet change = Import(path);
            _bed.Runtime.Engine.Options.FaultHook = (point, _) => { if (point == EngineFaultPoint.AfterFileWrite) throw new SimulatedCrashException("crash"); };
            Assert.Throws<SimulatedCrashException>(() => _bed.Runtime.Engine.Apply(change));
            StudioRuntime restarted = _bed.Reload();
            Assert.That(restarted.Journal.Read(change.Id)!.Outcomes![0].Undo, Is.Not.Null);
            Assert.That(restarted.History.ResumeInterrupted(change.Id).Ok, Is.False, "unknown completion must not replay");
            Assert.That(restarted.History.RollbackInterrupted(change.Id).Ok, Is.True);
            Assert.That(File.ReadAllText(restarted.Paths.Absolute(path)), Is.EqualTo("original"));
        }

        [Test]
        public void R2_04_FailedInverseRemainsInterruptedWithEvidence()
        {
            ChangeSet change = Import(_bed.Folder + "/data.txt");
            Operation inverse = new Operation("inverse", "missing.inverse", null);
            UndoPayload payload = new UndoPayload(new[] { inverse }, true, Array.Empty<StampWitness>(), null);
            _bed.Runtime.Journal.Write(change.WithState(ChangeSetState.Interrupted).WithOutcomes(new[] {
                new OperationOutcome("op1", OutcomeStatus.Applied, null, null, null, new OperationUndo(payload.ToJson())) }));
            HistoryResult result = _bed.Runtime.History.RollbackInterrupted(change.Id);
            Assert.That(result.Ok, Is.False);
            Assert.That(_bed.Runtime.Journal.Read(change.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Interrupted));
            Assert.That(_bed.Runtime.Journal.Read(change.Id)!.Outcomes![0].Undo, Is.Not.Null);
        }

        [Test]
        public void R2_05_ReadDependencyChangedAfterStageRefusesApply()
        {
            FixtureItemDefinition read = _bed.CreateItem("Read");
            FixtureItemDefinition write = _bed.CreateItem("Write");
            AuthoringRef readRef = _bed.Ref(read);
            ChangeSet change = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("read dependency", IntentOrigin.Manual),
                new[] { StudioTestBed.Set("op1", _bed.Ref(write), "weight", 2) }, baseVersions: new[] { new BaseVersion(readRef, readRef.Stamp!) });
            StagedChangeSet staged = _bed.Runtime.Engine.Stage(change);
            read.weight = 7;
            EditorUtility.SetDirty(read);
            Assert.That(_bed.Runtime.Engine.Apply(staged).Ok, Is.False);
            Assert.That(write.weight, Is.EqualTo(1));
        }

        [Test]
        public void R2_01_SaveAndReloadDuringDragPreserveRealTransform()
        {
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Ghost", Vector3.zero);
            _bed.SaveScene();
            GizmoMoveController gizmo = new GizmoMoveController(_bed.Runtime);
            gizmo.Begin(entity.gameObject);
            gizmo.DragTo(Vector3.one);
            Assert.That(gizmo.PreviewTransform!.position, Is.EqualTo(Vector3.one));
            _bed.SaveScene();
            _bed.Reload();
            Assert.That(entity.transform.position, Is.EqualTo(Vector3.zero));
            gizmo.Cancel();
            Assert.That(entity.transform.position, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void R2_06_RuntimeActionsDoNotWriteAuthoredDataAndCannotUndo()
        {
            _bed.Runtime.Engine.Options.PlayModeProbe = () => true;
            _bed.Runtime.Services.RegisterLiveTranslator(new FixtureLiveTranslator());
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Live", Vector3.zero);
            ChangeSet change = StudioTestBed.NewChangeSet("live", null, StudioTestBed.Set("op1", _bed.Ref(entity), "level", 8));
            ApplyReport result = _bed.Runtime.Engine.Apply(change);
            Assert.That(result.Ok, Is.True);
            Assert.That(entity.level, Is.EqualTo(1));
            Assert.That(result.Outcomes[0].Undo, Is.Null);
            Assert.That(_bed.Runtime.History.Undo(change.Id).Ok, Is.False);
        }

        [Test]
        public void R2_22_SharedRedactorMasksNestedJsonAndSplitChildOutput()
        {
            SecretRedactor redactor = new SecretRedactor();
            string raw = "etk_abc ett_abc etp_abc eta_abc sk-abc Bearer abc";
            Assert.That(redactor.Redact(raw), Does.Not.Contain("abc"));
            JObject json = new JObject { ["nested"] = new JObject { ["apiKey"] = "plain-value", ["message"] = raw } };
            Assert.That(redactor.Redact(json.ToString()), Does.Not.Contain("plain-value").And.Not.Contain("abc"));
            StringWriter sink = new StringWriter();
            using (RedactingTextWriter writer = new RedactingTextWriter(sink)) { writer.Write("ett_"); writer.Write("abc\n"); }
            Assert.That(sink.ToString(), Does.Not.Contain("abc"));
            _bed.Log.Write(StudioLogLevel.Error, "test", raw, new Diagnostic(DiagnosticCodes.Refused, raw, data: json));
            Assert.That(_bed.Log.Recent.Last().Diagnostic!.Data!.ToString(), Does.Not.Contain("plain-value"));
        }

        [GameCore.Studio.Model.AuthorOperation("r2.pure", ReadOnly = true)]
        public static int Pure([GameCore.Studio.Model.AuthorArg(Min = 1, Max = 5)] int value) => value;

        [Test]
        public void R2_33_ReadOnlyMetadataFlowsThroughRegistry()
        {
            _bed.Runtime.Registry.MethodSource = () => new[] { typeof(R2CoreRegressionTests).GetMethod(nameof(Pure))! };
            _bed.Runtime.Registry.Invalidate();
            Assert.That(_bed.Runtime.Registry.Find("r2.pure")!.ReadOnly, Is.True);
            Assert.That(_bed.Runtime.Registry.Catalog.FindTool("r2.pure")!.ReadOnly, Is.True);
            Assert.That(_bed.Runtime.Registry.Invoke("r2.pure", null, new JObject { ["value"] = 3 }).Status, Is.EqualTo(OutcomeStatus.Applied));
            Assert.That(_bed.Runtime.Registry.Invoke("r2.pure", null, new JObject()).Status, Is.EqualTo(OutcomeStatus.Refused));
        }

        [Test]
        public void R2_05_CatalogChangedAfterStageRefusesApply()
        {
            FixtureItemDefinition item = _bed.CreateItem("Catalog");
            StagedChangeSet staged = _bed.Runtime.Engine.Stage(StudioTestBed.NewChangeSet("catalog", null, StudioTestBed.Set("op1", _bed.Ref(item), "weight", 9)));
            _bed.Runtime.Registry.MethodSource = () => Array.Empty<System.Reflection.MethodInfo>();
            _bed.Runtime.Registry.Invalidate();
            ApplyReport report = _bed.Runtime.Engine.Apply(staged);
            Assert.That(report.Ok, Is.False);
            Assert.That(report.Diagnostics.Any(d => d.Code == DiagnosticCodes.StaleContext && d.Data?["expected"] != null), Is.True);
            Assert.That(item.weight, Is.EqualTo(1));
        }

        [Test]
        public void R2_04_ResumeDoesNotDropFailedDependency()
        {
            FixtureItemDefinition item = _bed.CreateItem("Dependency");
            ChangeSet change = StudioTestBed.NewChangeSet("dependencies", ApplyPolicy.BestEffort,
                StudioTestBed.Set("op1", _bed.Ref(item), "weight", 3),
                StudioTestBed.Op("op2", "set", _bed.Ref(item), new JObject { ["field"] = "weight", ["value"] = 8 }, "op1"));
            _bed.Runtime.Journal.Write(change.WithState(ChangeSetState.Interrupted).WithOutcomes(new[] { new OperationOutcome("op1", OutcomeStatus.Refused, DiagnosticCodes.Refused, "refused") }));
            _bed.Runtime.History.ResumeInterrupted(change.Id);
            Assert.That(item.weight, Is.EqualTo(1));
            Assert.That(_bed.Runtime.Journal.Read(change.Id)!.Outcomes!.Single(o => o.OpId == "op2").Status, Is.EqualTo(OutcomeStatus.Skipped));
        }

        [Test]
        public void R2_03_UndoCrashCheckpointsBeforeInverseMutation()
        {
            FixtureItemDefinition item = _bed.CreateItem("Undo");
            ChangeSet change = StudioTestBed.NewChangeSet("undo", null, StudioTestBed.Set("op1", _bed.Ref(item), "weight", 3));
            Assert.That(_bed.Runtime.Engine.Apply(change).Ok, Is.True);
            _bed.Runtime.Engine.Options.FaultHook = (point, _) => { if (point == EngineFaultPoint.AfterOperation) throw new SimulatedCrashException("undo crash"); };
            Assert.Throws<SimulatedCrashException>(() => _bed.Runtime.History.Undo(change.Id));
            StudioRuntime reloaded = _bed.Reload();
            Assert.That(reloaded.Journal.Read(change.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Interrupted));
            Assert.That(reloaded.History.ResumeInterrupted(change.Id).Ok, Is.True);
            Assert.That(item.weight, Is.EqualTo(1));
            Assert.That(reloaded.Journal.Read(change.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Undone));
        }

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

        [Test]
        public void R2_15_AllHistoryActionsUseTypedAdmissionHandler()
        {
            ChangeSet entry = StudioRuntime.Single("admit", IntentOrigin.Manual, new Operation("op1", "mechanism.admit", null));
            AdmissionHandler handler = new AdmissionHandler();
            _bed.Runtime.History.RegisterHandler(handler);
            _bed.Runtime.Journal.Write(entry.WithState(ChangeSetState.Applied));
            Assert.That(_bed.Runtime.History.Undo(entry.Id).Ok, Is.True);
            Assert.That(handler.Called, Is.EqualTo(HistoryAction.Undo));
            _bed.Runtime.Journal.Write(entry.WithState(ChangeSetState.Undone));
            _bed.Runtime.History.Redo(entry.Id);
            Assert.That(handler.Called, Is.EqualTo(HistoryAction.Redo));
            _bed.Runtime.Journal.Write(entry.WithState(ChangeSetState.Interrupted));
            _bed.Runtime.History.ResumeInterrupted(entry.Id);
            Assert.That(handler.Called, Is.EqualTo(HistoryAction.Resume));
            _bed.Runtime.History.RollbackInterrupted(entry.Id);
            Assert.That(handler.Called, Is.EqualTo(HistoryAction.Rollback));
        }

        [Test]
        public void R2_39_ProductionDiscoveryExcludesFixtures()
        {
            Assert.That(AuthoringTypeCache.ToolMethods().Any(method => method.DeclaringType == typeof(FixtureTools)), Is.False);
            Assert.That(AuthoringTypeCache.AuthorableTypes().Contains(typeof(FixtureItemDefinition)), Is.False);
        }

        [Test]
        public void R2_36_CoreReferencesInstalledBeforeAnyView()
        {
            Assert.That(_bed.Runtime.Index.Contributors.Contains(_bed.Runtime.References), Is.True);
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Speaker", Vector3.zero);
            FixtureDialogueDefinition graph = ScriptableObject.CreateInstance<FixtureDialogueDefinition>();
            StudioTestBed.MintId(graph);
            graph.lines.Add(new FixtureLine { speakerEntityId = entity.AuthoringId });
            AssetDatabase.CreateAsset(graph, _bed.Folder + "/Graph.asset");
            _bed.SaveScene();
            _bed.Runtime.Index.Rebuild();
            Assert.That(_bed.Runtime.Index.ReferencesTo(_bed.Ref(entity)).Any(reference => reference.Field == "lines[0].speakerEntityId"), Is.True);
            Assert.That(_bed.Runtime.Index.ImpactOf(_bed.Ref(entity)).Items.Any(item => item.Ref.SameTarget(_bed.Ref(graph))), Is.True);
            StudioRuntime reloaded = _bed.Reload();
            Assert.That(reloaded.Index.ReferencesTo(_bed.Ref(entity)).Any(reference => reference.Field == "lines[0].speakerEntityId"), Is.True);
        }
    }
}
