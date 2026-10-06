#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Entities;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views;
using Hollowmere.P3_2.Workflows;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hollowmere.R7_B.Promotion
{
    public sealed class RuntimeMoveAcceptance
    {
        private const string Village = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        private const string BackupKey = "R7B.Promotion.Backup";
        private const string StateKey = "R7B.Promotion.State";

        [UnityTest]
        public IEnumerator W_EDIT_06_RealRuntimeMoveCreatorPromotionExitPlayAndInverse()
        {
            string project = Directory.GetParent(Application.dataPath)!.FullName;
            string state = Path.Combine(Path.GetTempPath(), "r7b-promotion-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(state);
            SessionState.SetString(StateKey, state);
            string backup = Path.Combine(state, "village.before");
            File.Copy(Village, backup);
            SessionState.SetString(BackupKey, backup);
            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            AuthoredEntity original = Entities().Single(e => e.name == "Well");
            string id = original.AuthoringId;
            Vector3 before = original.transform.position;
            Vector3 destination = before + new Vector3(1, 0, 0);
            Quaternion beforeRotation = original.transform.rotation;
            using (StudioRuntime runtime = Runtime(project, state))
            {
                runtime.Engine.RuntimeMoves.CaptureAuthoredState();
            }

            yield return new EnterPlayMode();
            AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Hollowmere/Boot/Boot.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            yield return WorkflowPlayChecks.StartGame();
            string runtimeId;
            string promotedId;
            using (StudioRuntime runtime = Runtime(project, state))
            {
                var bridge = new ReflectionGameplayBridge();
                using (var context = new StudioViewContext(runtime, new ListSelectionBridge(), bridge, false))
                {
                    for (int frame = 0; frame < 300 && (!runtime.Live.IsAvailable || !bridge.IsAvailable || !Entities().Any(e => e.AuthoringId == id)); frame++) yield return null;
                    Assert.That(runtime.Live.IsAvailable && bridge.IsAvailable, Is.True, bridge.Describe);
                    AuthoredEntity live = Entities().Single(e => e.AuthoringId == id);
                    AuthoringRef target = runtime.Resolver.BuildRef(live, AuthorScope.Instance, true)!;
                    var action = StudioRuntime.Single("W-EDIT-06 runtime move", IntentOrigin.Manual,
                        new Operation("runtime", RuntimeMovePromotion.ToolId, target,
                            new JObject { ["position"] = Vector(destination), ["yaw"] = 0 }));
                    ApplyReport moved = runtime.Engine.Apply(action);
                    runtimeId = moved.Entry.Id;
                    Assert.That(moved.State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", moved.Diagnostics));
                    Assert.That(moved.Outcome("runtime")!.Undo, Is.Null);
                    Assert.That(runtime.History.Undo(runtimeId).Ok, Is.False);
                    string operationId = moved.Outcome("runtime")!.GameCoreOps![0];
                    for (int frame = 0; frame < 300 && !bridge.IsAt(target, destination, 0, operationId); frame++) yield return null;
                    Assert.That(bridge.IsAt(target, destination, 0, operationId), Is.True, "real committed world.place pose");
                    Assert.That(live.transform.position, Is.EqualTo(before), "runtime command must not mutate authored proxy");
                    var view = new ChangesView(context);
                    view.ShowTab(ChangesView.JournalTab);
                    ListView timeline = view.Query<ListView>().ToList().Single(list => list.itemsSource.Cast<object>().Any(item => item is TimelineEntry));
                    int index = timeline.itemsSource.Cast<object>().Select((item, i) => new { item, i })
                        .Single(pair => pair.item is TimelineEntry entry && entry.Id == runtimeId).i;
                    timeline.SetSelection(index);
                    Button command = view.Q<Button>("apply-to-authored");
                    Assert.That(command, Is.Not.Null, "the selected runtime History entry exposes the creator command");
                    // Invoke the same public command handler wired to the actual button, not the engine seam.
                    Assert.That(view.ApplyToAuthored(runtimeId), Is.True);
                    Assert.That(context.Edits.ApplyToAuthored(runtimeId, out ChangeSet? promoted, out _), Is.True);
                    promotedId = promoted!.Id;
                    Assert.That(promotedId, Is.Not.EqualTo(runtimeId));
                    Assert.That(runtime.Journal.Read(promotedId)!.EffectiveState, Is.EqualTo(ChangeSetState.Candidate));
                    Assert.That(live.transform.position, Is.EqualTo(before));
                }
            }
            yield return new ExitPlayMode();
            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            using (StudioRuntime runtime = Runtime(project, state))
            {
                var applied = runtime.Engine.RuntimeMoves.ApplyPending();
                Assert.That(applied.Count, Is.EqualTo(1));
                Assert.That(applied[0].State, Is.EqualTo(ChangeSetState.Applied), string.Join("; ", applied[0].Diagnostics));
                Assert.That(applied[0].Outcome("move")!.Undo, Is.Not.Null);
                // The engine saves the promoted scene. Reopening must retain it without another Apply.
                EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
                AuthoredEntity persisted = Entities().Single(e => e.AuthoringId == id);
                Assert.That(persisted.transform.position, Is.EqualTo(destination));
                Assert.That(Quaternion.Angle(persisted.transform.rotation, Quaternion.identity), Is.LessThan(0.01f));
                Assert.That(runtime.Engine.RuntimeMoves.ApplyPending(), Is.Empty, "duplicate creator clicks produce only one authored application");
                Assert.That(runtime.History.Undo(promotedId).Ok, Is.True);
                Assert.That(persisted.transform.position, Is.EqualTo(before));
                Assert.That(Quaternion.Angle(persisted.transform.rotation, beforeRotation), Is.LessThan(0.01f));
                Assert.That(runtime.History.Undo(runtimeId).Ok, Is.False, "promotion never changes runtime nonundoability");
                EditorSceneManager.SaveScene(persisted.gameObject.scene);
                EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
                Assert.That(Entities().Single(e => e.AuthoringId == id).transform.position, Is.EqualTo(before));
                string evidence = Path.GetFullPath(Path.Combine(project, "../../artifacts/studio/verification/W-EDIT-06/r7-b"));
                Directory.CreateDirectory(evidence);
                File.WriteAllText(Path.Combine(evidence, "promotion.json"), new JObject
                {
                    ["row"] = "W-EDIT-06", ["runtimeChangeSetId"] = runtimeId, ["authoredChangeSetId"] = promotedId,
                    ["authoringId"] = id, ["before"] = Vector(before), ["persistedAfterReopen"] = Vector(destination),
                    ["inverseAfterReopen"] = Vector(before), ["creatorCommand"] = commandName,
                    ["runtimeNonUndoable"] = true, ["duplicateIdempotent"] = true, ["result"] = "pass"
                }.ToString());
                File.WriteAllText(Path.Combine(evidence, "runtime.json"), StudioJson.Serialize(runtime.Journal.Read(runtimeId)!));
                File.WriteAllText(Path.Combine(evidence, "authored-undone.json"), StudioJson.Serialize(runtime.Journal.Read(promotedId)!));
            }
        }
        private const string commandName = "Changes / Journal / Apply to authored";
        private static StudioRuntime Runtime(string project, string state) => StudioRuntime.Create(new StudioRuntimeOptions
        {
            Paths = new StudioPaths(project, state, "r7-b-promotion"), LoadIndexCache = false,
        });
        private static AuthoredEntity[] Entities() => UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        private static JArray Vector(Vector3 value) => new JArray(value.x, value.y, value.z);

        [UnityTearDown]
        public IEnumerator Restore()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string backup = SessionState.GetString(BackupKey, string.Empty);
            if (File.Exists(backup))
            {
                File.Copy(backup, Village, true);
                AssetDatabase.ImportAsset(Village, ImportAssetOptions.ForceSynchronousImport);
            }
            string state = SessionState.GetString(StateKey, string.Empty);
            if (Directory.Exists(state)) Directory.Delete(state, true);
            SessionState.EraseString(BackupKey);
            SessionState.EraseString(StateKey);
        }
    }
}
