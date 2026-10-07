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

        [TestCase("items")]
        [TestCase("ManagedItems")]
        public void R11A_AlreadyListedAppendDoesNotUndoLaterEnrollment(string field)
        {
            FixtureItemDefinition first = _bed.CreateItem("First");
            FixtureItemDefinition second = _bed.CreateItem("Second");
            FixtureNpcDefinition npc = _bed.CreateNpc("Collector");
            var items = field == "items" ? npc.items : npc.ManagedItems;
            items.Add(first);
            EditorUtility.SetDirty(npc);
            AssetDatabase.SaveAssets();
            _bed.Runtime.Index.Rebuild();
            ChangeSet change = StudioTestBed.NewChangeSet("repeat enrollment", null,
                StudioTestBed.Op("append", "assign", _bed.Runtime.Resolver.BuildRef(npc),
                    new JObject { ["field"] = field, ["append"] = true, ["value"] = AssetDatabase.GetAssetPath(first) }));

            ApplyReport applied = _bed.Runtime.Engine.Apply(change);

            Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics));
            Assert.That(field == "items" ? npc.items : npc.ManagedItems, Is.EqualTo(new[] { first }));
            JObject outcome = JObject.Parse(applied.Entry.Outcomes![0].Detail!);
            Assert.That((bool?)outcome["alreadyListed"], Is.True);
            Assert.That((int?)outcome["index"], Is.EqualTo(0));
            Assert.That(applied.Entry.Outcomes[0].Undo, Is.Null);
            items.Add(second);
            EditorUtility.SetDirty(npc);
            HistoryResult undone = _bed.Runtime.History.Undo();
            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(field == "items" ? npc.items : npc.ManagedItems, Is.EqualTo(new[] { first, second }),
                "Undoing a recorded no-op must not truncate a later enrollment.");

            FixtureItemDefinition third = _bed.CreateItem("Third");
            AuthoringRef reference = _bed.Runtime.Resolver.BuildRef(npc)!;
            ApplyReport edited = _bed.Runtime.Engine.Apply(StudioTestBed.NewChangeSet("append and replace", null,
                StudioTestBed.Op("append", "assign", reference,
                    new JObject { ["field"] = field, ["append"] = true, ["value"] = AssetDatabase.GetAssetPath(third) }),
                StudioTestBed.Op("replace", "assign", reference,
                    new JObject { ["field"] = field, ["index"] = 0, ["value"] = AssetDatabase.GetAssetPath(second) }, "append")));
            Assert.That(edited.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", edited.Diagnostics));
            Assert.That(field == "items" ? npc.items : npc.ManagedItems, Is.EqualTo(new[] { second, second, third }),
                "Distinct append grows the list; index replacement is not deduplicated.");
            Assert.That(_bed.Runtime.History.Undo().Ok, Is.True);
            Assert.That(field == "items" ? npc.items : npc.ManagedItems, Is.EqualTo(new[] { first, second }));
        }

        [Test]
        public void R6_G_Request1_GenericDialogueUsesTrustedPreparation()
        {
            var operation = StudioTestBed.Op("graph", "create", null,
                new JObject { ["type"] = "dialogue.graph", ["name"] = "Graph", ["path"] = _bed.Folder + "/Graph.asset" });
            var change = StudioTestBed.NewChangeSet("dialogue", null, operation);
            var context = (EditContext)System.Activator.CreateInstance(typeof(EditContext),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                new object?[] { _bed.Runtime, change, operation, null, true, null }, null)!;
            var plan = (PreparedCreation?)typeof(PreparedCreation).GetMethod("Plan",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { context });
            Assert.That(plan, Is.Not.Null, "Generic creation must not bypass gameplay preparation.");
            var result = plan!.Apply!(context);
            Assert.That(result.Status, Is.EqualTo(OutcomeStatus.Refused), "No owning gameplay world exists in this engine fixture.");
            Assert.That(File.Exists(_bed.Folder + "/Graph.asset"), Is.False);
        }

        [Test]
        public void R6_G_Request1_AbsentCreationAdapterRefusesPrecisely()
        {
            var plan = (PreparedCreation)typeof(PreparedCreation).GetMethod("PrepareGameplay",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(null, new object?[] { null, new JObject(), null })!;
            var result = plan.Apply!(null!);
            Assert.That(result.Status, Is.EqualTo(OutcomeStatus.Refused));
            Assert.That(result.Code, Is.EqualTo(DiagnosticCodes.NotConfigured));
            Assert.That(result.Detail, Does.Contain("gameplay creation adapter is unavailable"));
            Assert.That(plan.Inverse, Is.Empty);
        }


        [Test]
        public void R7E_MissingPrefabCandidatePreservesCanonicalDiagnosticWithoutMutation()
        {
            using StudioRuntime runtime = DefinitionRuntime(out ScriptableObject definition);
            AuthoringRef reference = runtime.Resolver.BuildRef(definition)!;
            string before = EditorJsonUtility.ToJson(definition);
            bool dirty = EditorUtility.IsDirty(definition);
            int objects = Resources.FindObjectsOfTypeAll(definition.GetType()).Length;
            var expected = DefinitionDiagnostic(definition, "GP-ENT-006");
            ChangeSet change = StudioTestBed.NewChangeSet("invalid definition", null,
                StudioTestBed.Set("edit", reference, "defaultScaleMilli", 1200));

            StagedChangeSet staged = runtime.Engine.Stage(change, new StageOptions
            {
                Mode = ValidationMode.Candidate,
                ToolCatalogRevision = runtime.Registry.Catalog.Revision ?? runtime.Registry.Catalog.ComputeRevision(),
                Previews = false,
            });

            Assert.That(staged.Ok, Is.False);
            Assert.That(staged.AllDiagnostics, Has.Some.Matches<Diagnostic>(diagnostic =>
                diagnostic.Code == expected.Code && diagnostic.Message == expected.Message
                && diagnostic.Where?.Ref?.IdentityKey == SemanticIndexService.EdgeRef(reference).IdentityKey
                && (string?)diagnostic.Data?["subject"] == expected.Subject));
            Assert.That(runtime.Journal.Exists(change.Id), Is.False, "Invalid candidates cannot enter the journal.");
            Assert.That(EditorJsonUtility.ToJson(definition), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(definition), Is.EqualTo(dirty));
            Assert.That(Resources.FindObjectsOfTypeAll(definition.GetType()).Length, Is.EqualTo(objects), "Projection copies must be released.");
        }

        [TestCase("set")]
        [TestCase("assign")]
        public void R7E_ProposedPrefabRepairStagesWithoutWritingAsset(string tool)
        {
            using StudioRuntime runtime = DefinitionRuntime(out ScriptableObject definition);
            string prefabPath = DefinitionPrefab();
            string before = EditorJsonUtility.ToJson(definition);
            bool dirty = EditorUtility.IsDirty(definition);
            byte[] asset = File.ReadAllBytes(AssetDatabase.GetAssetPath(definition));
            ChangeSet change = StudioTestBed.NewChangeSet("repair", null,
                StudioTestBed.Op("repair", tool, runtime.Resolver.BuildRef(definition),
                    new JObject { ["field"] = "prefab", ["value"] = prefabPath }));

            StagedChangeSet staged = runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics));
            Assert.That(EditorJsonUtility.ToJson(definition), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(definition), Is.EqualTo(dirty));
            Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(definition)), Is.EqualTo(asset));
        }

        [Test]
        public void R7E_ProposedInvalidStateBlocksEveryContributorUnderBestEffort()
        {
            using StudioRuntime runtime = DefinitionRuntime(out ScriptableObject definition);
            SetDefinitionPrefab(definition, DefinitionPrefab());
            AuthoringRef reference = runtime.Resolver.BuildRef(definition)!;
            string before = EditorJsonUtility.ToJson(definition);
            var expectedCopy = UnityEngine.Object.Instantiate(definition);
            expectedCopy.name = definition.name;
            (string Code, string Message, string Subject) expected;
            try
            {
                using var serialized = new SerializedObject(expectedCopy);
                serialized.FindProperty("prefab").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                expected = DefinitionDiagnostic(expectedCopy, "GP-ENT-006");
            }
            finally { UnityEngine.Object.DestroyImmediate(expectedCopy); }
            ChangeSet change = StudioTestBed.NewChangeSet("invalid proposal", ApplyPolicy.BestEffort,
                StudioTestBed.Set("scale", reference, "defaultScaleMilli", 1500),
                StudioTestBed.Set("clear", reference, "prefab", JValue.CreateNull()));

            StagedChangeSet staged = runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.False);
            Assert.That(staged.AllDiagnostics, Has.Some.Matches<Diagnostic>(diagnostic =>
                diagnostic.Code == expected.Code && diagnostic.Message == expected.Message
                && diagnostic.Where?.Ref?.IdentityKey == SemanticIndexService.EdgeRef(reference).IdentityKey
                && (string?)diagnostic.Data?["subject"] == expected.Subject));
            ApplyReport refused = runtime.Engine.Apply(staged);
            Assert.That(refused.State, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(refused.Entry.Outcomes, Has.All.Matches<OperationOutcome>(outcome => outcome.Status == OutcomeStatus.Refused));
            Assert.That(EditorJsonUtility.ToJson(definition), Is.EqualTo(before));
        }

        [Test]
        public void R7E_MultipleOperationsValidateFinalDependencyOrderedState()
        {
            using StudioRuntime runtime = DefinitionRuntime(out ScriptableObject definition);
            string prefabPath = DefinitionPrefab();
            SetDefinitionPrefab(definition, prefabPath);
            AuthoringRef reference = runtime.Resolver.BuildRef(definition)!;
            string before = EditorJsonUtility.ToJson(definition);
            ChangeSet change = StudioTestBed.NewChangeSet("replace prefab", null,
                StudioTestBed.Op("repair", "assign", reference,
                    new JObject { ["field"] = "prefab", ["value"] = prefabPath }, "clear"),
                StudioTestBed.Set("clear", reference, "prefab", JValue.CreateNull()));

            StagedChangeSet staged = runtime.Engine.Stage(change);

            Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics));
            Assert.That(EditorJsonUtility.ToJson(definition), Is.EqualTo(before));
        }

        [Test]
        public void R7E_AssignCollectionValidatesAppendAndSubsequentIndexRepair()
        {
            using StudioRuntime runtime = DefinitionRuntime(out ScriptableObject definition);
            SetDefinitionPrefab(definition, DefinitionPrefab());
            System.Type? variantType = System.Type.GetType("GameCore.Gameplay.Entities.VariantDefinition, GameCore.Gameplay.Entities");
            Assert.That(variantType, Is.Not.Null);
            ScriptableObject variant = ScriptableObject.CreateInstance(variantType!);
            StudioTestBed.MintId(variant);
            string variantPath = _bed.Folder + "/Variant.asset";
            AssetDatabase.CreateAsset(variant, variantPath);
            AuthoringRef reference = runtime.Resolver.BuildRef(definition)!;
            string before = EditorJsonUtility.ToJson(definition);
            Operation append = StudioTestBed.Op("append", "assign", reference,
                new JObject { ["field"] = "variants", ["append"] = true });

            StagedChangeSet invalid = runtime.Engine.Stage(StudioTestBed.NewChangeSet("invalid variant", null, append));
            Assert.That(invalid.AllDiagnostics, Has.Some.Matches<Diagnostic>(diagnostic => diagnostic.Code == "GP-ID-001"));
            StagedChangeSet repaired = runtime.Engine.Stage(StudioTestBed.NewChangeSet("repair variant", null,
                StudioTestBed.Op("repair", "assign", reference,
                    new JObject { ["field"] = "variants", ["index"] = 0, ["value"] = variantPath }, "append"), append));

            Assert.That(repaired.Ok, Is.True, string.Join("; ", repaired.AllDiagnostics));
            Assert.That(EditorJsonUtility.ToJson(definition), Is.EqualTo(before));
        }

        [Test]
        public void R7E_HistoryCanUndoARepairBackToInvalidDefinition()
        {
            using StudioRuntime runtime = DefinitionRuntime(out ScriptableObject definition);
            ChangeSet repair = StudioTestBed.NewChangeSet("repair before undo", null,
                StudioTestBed.Op("repair", "assign", runtime.Resolver.BuildRef(definition),
                    new JObject { ["field"] = "prefab", ["value"] = DefinitionPrefab() }));
            ApplyReport applied = runtime.Engine.Apply(repair);
            Assert.That(applied.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied.Diagnostics));
            Assert.That(new SerializedObject(definition).FindProperty("prefab").objectReferenceValue, Is.Not.Null);

            HistoryResult undone = runtime.History.Undo();

            Assert.That(undone.Ok, Is.True, string.Join("; ", undone.Diagnostics));
            Assert.That(new SerializedObject(definition).FindProperty("prefab").objectReferenceValue, Is.Null,
                "History inverses bypass forward definition validation.");
        }

        private StudioRuntime DefinitionRuntime(out ScriptableObject definition)
        {
            System.Type? type = System.Type.GetType("GameCore.Gameplay.Entities.EntityDefinition, GameCore.Gameplay.Entities");
            Assert.That(type, Is.Not.Null, "The real gameplay definition is required by this regression.");
            definition = ScriptableObject.CreateInstance(type!);
            definition.name = "Definition";
            StudioTestBed.MintId(definition);
            AssetDatabase.CreateAsset(definition, _bed.Folder + "/Definition.asset");
            AssetDatabase.SaveAssets();
            StudioRuntime runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(StudioTestBed.ProjectRoot, _bed.StateRoot, "definition-validation"),
                Log = _bed.Log,
                TypeSource = () => new[] { type! },
                ToolMethodSource = () => System.Array.Empty<System.Reflection.MethodInfo>(),
                SearchFolders = new[] { _bed.Folder },
                LoadIndexCache = false,
            });
            runtime.Index.Rebuild();
            return runtime;
        }

        private string DefinitionPrefab()
        {
            string path = _bed.Folder + "/View.prefab";
            GameObject view = new GameObject("View");
            try { PrefabUtility.SaveAsPrefabAsset(view, path); }
            finally { UnityEngine.Object.DestroyImmediate(view); }
            return path;
        }

        private static void SetDefinitionPrefab(ScriptableObject definition, string path)
        {
            using var serialized = new SerializedObject(definition);
            serialized.FindProperty("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static (string Code, string Message, string Subject) DefinitionDiagnostic(ScriptableObject definition, string code)
        {
            System.Type? validator = System.Type.GetType("GameCore.Gameplay.Entities.Editor.EntityValidator, GameCore.Gameplay.Entities.Editor");
            Assert.That(validator, Is.Not.Null);
            var method = validator!.GetMethod("Validate", new[] { definition.GetType() })!;
            foreach (object diagnostic in (System.Collections.IEnumerable)method.Invoke(null, new object[] { definition })!)
            {
                System.Type type = diagnostic.GetType();
                if ((string)type.GetProperty("Code")!.GetValue(diagnostic)! == code)
                    return (code, (string)type.GetProperty("Message")!.GetValue(diagnostic)!, (string)type.GetProperty("SubjectId")!.GetValue(diagnostic)!);
            }
            Assert.Fail("Expected canonical diagnostic " + code);
            return default;
        }

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
