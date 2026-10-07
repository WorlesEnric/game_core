#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

namespace GameCore.Studio.UI.Tests
{
    public sealed class R2UiRegressionTests
    {
        private UiTestBed _bed = null!;
        [SetUp] public void SetUp() => _bed = new UiTestBed();
        [TearDown] public void TearDown() => _bed.Dispose();

        private CandidateEntry Candidate()
        {
            byte[] proposal = Encoding.UTF8.GetBytes("{}");
            string digest = ContentStamp.Sha256Hex(proposal);
            _bed.Runtime.Artifacts.Put(proposal, new ArtifactRef(digest, "application/json", proposal.Length, "proposal.json"));
            ChangeSet cs = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId,
                new Intent("Stage this mechanism", IntentOrigin.Agent), new[] { new Operation("op1", BuiltInToolIds.MechanismPropose, null,
                new JObject { ["package"] = new JObject { ["artifact"] = ContentStamp.Prefix + new string('a', 64) },
                    ["proposal"] = new JObject { ["artifact"] = ContentStamp.Prefix + digest }, ["stageInputs"] = new JArray("Assets/data.json") }) });
            _bed.Runtime.Journal.Write(cs.WithState(ChangeSetState.Candidate));
            return _bed.Context.Candidates.Add("request", cs);
        }

        private StageService ConfigureStage(string confinement = "docker", bool optedIn = false)
        {
            StageService service = new StageService { Confinement = confinement };
            AdmissionOptions options = StageAdmission.Of(_bed.Runtime).Options;
            options.ProjectId = new string('b', 64);
            options.SourceRevision = () => "source-revision";
            options.CatalogRevision = _bed.CatalogRevision;
            options.StageService = service;
            options.AllowHostConfinement = optedIn;
            return service;
        }

        [Test]
        public void R2_13_TypedRequestFetchesVerifiedVerdictAndDisplaysBinding()
        {
            CandidateEntry entry = Candidate();
            StageService service = ConfigureStage();
            Assert.That(_bed.Context.Candidates.RequestStage(entry).GetAwaiter().GetResult(), Is.Null);
            Assert.That(service.Request, Is.Not.Null);
            Assert.That(service.Request!.ChangeSetId, Is.EqualTo(entry.Id));
            Assert.That(service.Request.SourceProject, Is.EqualTo(_bed.Runtime.Paths.ProjectRoot));
            Assert.That(service.Request.ProjectId, Is.EqualTo(new string('b', 64)));
            Assert.That(service.Request.SourceRevision, Is.EqualTo("source-revision"));
            Assert.That(service.Request.CatalogRevision, Is.EqualTo(_bed.CatalogRevision()));
            Assert.That(service.Request.StageInputs, Is.EqualTo(new[] { "Assets/data.json" }));
            Assert.That(service.Verifications, Is.EqualTo(1));
            CandidatePanelView panel = new CandidatePanelView(_bed.Context);
            panel.Select(entry.Id);
            Assert.That(panel.Q<Button>("candidate-admit").enabledSelf, Is.True);
            string text = string.Join("\n", panel.Query<Label>().ToList().Select(label => label.text));
            Assert.That(text, Does.Contain("test-job").And.Contain("docker").And.Contain("coldCache: True").And.Contain("unity-editmode: pass").And.Contain("Forbidden hits: 0"));
            Assert.That(panel.Q<Button>("candidate-record-verdict"), Is.Null);
            CandidateCoordinator recovered = new CandidateCoordinator(_bed.Runtime, () => _bed.Gateway);
            CandidateEntry recoveredEntry = recovered.Add("request", entry.ChangeSet);
            Assert.That(typeof(CandidateEntry).GetProperty("StageJobId")!.GetValue(recoveredEntry), Is.EqualTo("test-job"));
            Assert.That(typeof(CandidateEntry).GetProperty("VerifiedVerdict")!.GetValue(recoveredEntry), Is.Null, "session bytes do not restore authorization");
            var refresh = (Task<Diagnostic?>)typeof(CandidateCoordinator).GetMethod("RefreshStage")!.Invoke(recovered, new object[] { recoveredEntry });
            Assert.That(refresh.GetAwaiter().GetResult(), Is.Null);
            Assert.That(service.Verifications, Is.EqualTo(2));
        }

        [Test]
        public void R9_B_AlreadyRecoveredCandidateRefreshesLateJobBindingWithoutRestaging()
        {
            CandidateEntry entry = Candidate();
            StageService service = ConfigureStage();
            CandidateCoordinator recovered = new CandidateCoordinator(_bed.Runtime, () => _bed.Gateway);
            CandidateEntry early = recovered.Add(entry.Id, entry.ChangeSet);
            Assert.That(early.StageJobId, Is.Null);
            Assert.That(_bed.Context.Candidates.RequestStage(entry).GetAwaiter().GetResult(), Is.Null);
            Assert.That(recovered.Add(entry.Id, entry.ChangeSet), Is.SameAs(early));
            Assert.That(recovered.RefreshStage(early).GetAwaiter().GetResult(), Is.Null);
            Assert.That(recovered.CanAdmit(early), Is.True);
            Assert.That(service.Submissions, Is.EqualTo(1), "Recovery fetches the original signed job; it never submits another stage.");
        }

        [Test]
        public void R9_B_RefreshWithoutAddRecoversLateBindingButRefusesChangedSource()
        {
            CandidateEntry entry = Candidate();
            StageService service = ConfigureStage();
            CandidateCoordinator recovered = new CandidateCoordinator(_bed.Runtime, () => _bed.Gateway);
            CandidateEntry early = recovered.Add(entry.Id, entry.ChangeSet);
            Assert.That(_bed.Context.Candidates.RequestStage(entry).GetAwaiter().GetResult(), Is.Null);
            Assert.That(recovered.RefreshStage(early).GetAwaiter().GetResult(), Is.Null);
            StageAdmission.Of(_bed.Runtime).Options.SourceRevision = () => "changed-source";
            Assert.That(recovered.CanAdmit(early), Is.False);
            Assert.That(recovered.RefreshStage(early).GetAwaiter().GetResult()!.Message, Does.Contain("stage_context_changed"));
            Assert.That(recovered.CanAdmit(early), Is.False);
            Assert.That(service.Submissions, Is.EqualTo(1));
        }

        [Test]
        public void R9_B_ChangedCandidateCannotUsePreviouslyVerifiedJob()
        {
            CandidateEntry entry = Candidate();
            ConfigureStage();
            Assert.That(_bed.Context.Candidates.RequestStage(entry).GetAwaiter().GetResult(), Is.Null);
            entry.ChangeSet.Operations[0].Args!["package"] = new JObject { ["artifact"] = ContentStamp.Prefix + new string('e', 64) };
            Assert.That(_bed.Context.Candidates.CanAdmit(entry), Is.False);
            Assert.That(_bed.Context.Candidates.RefreshStage(entry).GetAwaiter().GetResult()!.Message, Does.Contain("stage_context_changed"));
        }

        [TestCase("host", false, false)]
        [TestCase("host", true, true)]
        [TestCase("docker", false, true)]
        public void R2_13_HostAdmissionRequiresTrustedOperatorOptIn(string confinement, bool optIn, bool allowed)
        {
            CandidateEntry entry = Candidate();
            ConfigureStage(confinement, optIn);
            Diagnostic? result = _bed.Context.Candidates.RequestStage(entry).GetAwaiter().GetResult();
            Assert.That(result == null, Is.EqualTo(allowed));
            CandidatePanelView panel = new CandidatePanelView(_bed.Context);
            panel.Select(entry.Id);
            Assert.That(panel.Q<Button>("candidate-admit").enabledSelf, Is.EqualTo(allowed));
            if (confinement == "host" && allowed)
                Assert.That(string.Join("\n", panel.Query<Label>().ToList().Select(label => label.text)), Does.Contain("Warning: host confinement"));
        }

        [TestCase("verify")]
        [TestCase("steps")]
        [TestCase("forbidden")]
        public void R2_13_UnverifiedOrIncompleteVerdictNeverEnablesAdmit(string defect)
        {
            CandidateEntry entry = Candidate();
            StageService service = ConfigureStage();
            service.Defect = defect;
            Assert.That(_bed.Context.Candidates.RequestStage(entry).GetAwaiter().GetResult(), Is.Not.Null);
            CandidateStaging.MarkVerdict(_bed.Runtime, entry.Id, ScenarioStatus.Pass, "candidate-authored pass");
            CandidatePanelView panel = new CandidatePanelView(_bed.Context);
            panel.Select(entry.Id);
            Assert.That(panel.Q<Button>("candidate-admit").enabledSelf, Is.False);
            Assert.Throws<InvalidOperationException>(() => _bed.Context.Candidates.Admit(entry, false));
        }

        [Test]
        public void R2_15_AllPanelHistoryActionsUseGenericDispatch()
        {
            ChangeSet entry = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("admission", IntentOrigin.Manual),
                new[] { new Operation("op1", MechanismAdmission.AdmitTool) });
            HistoryHandler handler = new HistoryHandler();
            _bed.Runtime.History.RegisterHandler(handler);
            HistoryPanelView panel = new HistoryPanelView(_bed.Context);
            _bed.Runtime.Journal.Write(entry.WithState(ChangeSetState.Applied));
            panel.Undo(entry.Id);
            _bed.Runtime.Journal.Write(entry.WithState(ChangeSetState.Undone));
            panel.Redo(entry.Id);
            _bed.Runtime.Journal.Write(entry.WithState(ChangeSetState.Interrupted));
            MethodInfo recover = typeof(HistoryPanelView).GetMethod("Recover", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            recover.Invoke(panel, new object[] { entry.Id, true });
            recover.Invoke(panel, new object[] { entry.Id, false });
            Assert.That(handler.Actions, Is.EqualTo(new[] { HistoryAction.Undo, HistoryAction.Redo, HistoryAction.Resume, HistoryAction.Rollback }));
        }

        [Test]
        public void R2_24_ScenePackingStopsAtUtf8ByteCapAndReportsTruncation()
        {
            var entity = _bed.SpawnEntity("etk_sensitive_123456789", Vector3.zero);
            _bed.SaveScene();
            var targets = Enumerable.Repeat(_bed.Ref(entity), 2000).ToArray();
            var redacted = _bed.Context.Requests.SceneContext(new[] { targets[0] })!;
            Assert.That((string?)JObject.Parse(Encoding.UTF8.GetString(redacted.Data))["objects"]![0]!["name"], Is.EqualTo("[redacted]"));
            _bed.Context.Requests.ByteCap = 512;
            var scene = _bed.Context.Requests.SceneContext(targets)!;
            Assert.That(scene.Data.Length, Is.LessThanOrEqualTo(512));
            string json = Encoding.UTF8.GetString(scene.Data);
            Assert.That(JObject.Parse(json)["truncated"]!.Value<bool>(), Is.True);
            Assert.That(json, Does.Not.Contain("etk_sensitive"));
            var selection = new SelectionSnapshot("sel_" + IdDerivation.NewChangeSetId().Substring(3), GameCore.Studio.Model.SelectionMode.Edit, new[] { targets[0] }, 0);
            PreparedRequest request = _bed.Context.Requests.Build("inspect", selection);
            Assert.That(Encoding.UTF8.GetByteCount(StudioJson.Serialize(request.Request.ContextSlice, false)), Is.LessThanOrEqualTo(512));
            Assert.That(request.ContextTruncated, Is.True);
        }

        [Test]
        public void R2_24_AttachmentCountAndAggregateBytesAreBoundedBeforeRead()
        {
            string path = Path.Combine(_bed.StateRoot, "large.bin");
            using (FileStream file = File.Create(path)) file.SetLength(9 * 1024 * 1024);
            var selection = new SelectionSnapshot("sel_" + IdDerivation.NewChangeSetId().Substring(3), GameCore.Studio.Model.SelectionMode.Edit, Array.Empty<AuthoringRef>(), 0);
            PromptAttachment fileRef = AgentRequestBuilder.AttachmentFor(path);
            Assert.Throws<ArgumentException>(() => _bed.Context.Requests.Build("inspect", selection, attachments: new[] { fileRef, fileRef }));
            Assert.Throws<ArgumentException>(() => _bed.Context.Requests.Build("inspect", selection, attachments: Enumerable.Repeat(fileRef, 9).ToArray()));
        }

        [Test]
        public void R2_29_OnlyImageFocusOwnsGameInputAndLossResetsHeldKeys()
        {
            MethodInfo? owns = typeof(StudioViewportWindow).GetMethod("OwnsGameInput");
            Assert.That(owns, Is.Not.Null, "input ownership must be element-scoped");
            Image image = new Image();
            foreach (Focusable focused in new Focusable[] { new TextField(), new Button(), new VisualElement(), image })
            foreach (ViewportMode mode in new[] { ViewportMode.Play, ViewportMode.Select, ViewportMode.Inspect })
            foreach (bool playing in new[] { true, false })
            foreach (bool windowFocused in new[] { true, false })
                Assert.That(owns!.Invoke(null, new object[] { mode, playing, windowFocused, image, focused }),
                    Is.EqualTo(mode == ViewportMode.Play && playing && windowFocused && focused == image));
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            using PlayInputRouting routing = new PlayInputRouting();
            try
            {
                routing.Update(true);
                InputState.Change(keyboard, new KeyboardState(Key.W));
                Assert.That(keyboard.wKey.isPressed, Is.True);
                routing.Update(false);
                Assert.That(keyboard.wKey.isPressed, Is.False, "focus loss cancels held gameplay state synchronously");
                typeof(PlayInputRouting).GetMethod("Guard")!.Invoke(routing, new object[] { (Func<bool>)(() => true) });
                // Allocate through reflection to keep Unity.Collections out of this package's public dependency set.
                MethodInfo from = typeof(StateEvent).GetMethods().Single(method => method.Name == "From" && method.GetParameters().Length == 3);
                object[] arguments = { keyboard, default(InputEventPtr), from.GetParameters()[2].DefaultValue };
                using (IDisposable buffer = (IDisposable)from.Invoke(null, arguments)!)
                {
                    InputEventPtr input = (InputEventPtr)arguments[1];
                    typeof(PlayInputRouting).GetMethod("OnEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(routing, new object[] { input, keyboard });
                    Assert.That(input.handled, Is.True, "text/control ownership suppresses game events even with permissive global input settings");
                }
            }
            finally { InputSystem.RemoveDevice(keyboard); }
        }

        [UnityTest]
        public IEnumerator R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers()
        {
            if (!ViewportRenderer.CanRender) Assert.Ignore("R2-29: window event delivery requires a graphical Editor.");
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            try
            {
                window.UseContext(_bed.Context); window.Show(); window.EnsureGui(); window.Focus();
                yield return null;
                VisualElement root = window.rootVisualElement;
                VisualElement image = root.Q("viewport-image");
                TextField prompt = root.Q<TextField>("prompt-input");
                VisualElement button = root.Q<Button>("mode-select");
                int downs = 0, ups = 0, imageDowns = 0, imageUps = 0, promptBubbleDowns = 0;
                image.RegisterCallback<KeyDownEvent>(_ => imageDowns++);
                image.RegisterCallback<KeyUpEvent>(_ => imageUps++);
                prompt.RegisterCallback<KeyDownEvent>(_ => promptBubbleDowns++);
                // Observe delivery before TextInput consumes Tab in its own default/callback handling.
                // A bubbling callback on TextField is not an event-delivery witness.
                root.RegisterCallback<KeyDownEvent>(_ => downs++, TrickleDown.TrickleDown);
                root.RegisterCallback<KeyUpEvent>(_ => ups++, TrickleDown.TrickleDown);
                foreach (VisualElement control in new[] { (VisualElement)prompt, button, image })
                {
                    window.SetMode(ViewportMode.Select);
                    control.Focus();
                    yield return null;
                    VisualElement? focused = root.focusController.focusedElement as VisualElement;
                    Assert.That(focused != null && (focused == control || control.Contains(focused)), Is.True,
                        "focus must settle on " + control.name + " before dispatch");
                    int beforeDowns = downs, beforeUps = ups, beforeImageDowns = imageDowns, beforeImageUps = imageUps;
                    using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.W })) focused!.SendEvent(down);
                    using (KeyUpEvent up = KeyUpEvent.GetPooled(new Event { type = EventType.KeyUp, keyCode = KeyCode.W })) focused!.SendEvent(up);
                    Assert.That(downs - beforeDowns, Is.EqualTo(1), "P42-UI-01: trickle-down delivery to " + control.name);
                    Assert.That(ups - beforeUps, Is.EqualTo(1), "key-up delivery to " + control.name);
                    Assert.That(imageDowns - beforeImageDowns, Is.EqualTo(control == image ? 1 : 0), "control keys must never enter the image event path");
                    Assert.That(imageUps - beforeImageUps, Is.EqualTo(control == image ? 1 : 0), "control key-ups must never enter the image event path");
                    // Tab can move Toolkit focus. Test its viewport shortcut separately from the
                    // non-navigation down/up pair, whose focus must stay on the same control.
                    using (KeyDownEvent tab = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.Tab })) focused!.SendEvent(tab);
                    Assert.That(window.Mode, Is.EqualTo(control == image ? ViewportMode.Inspect : ViewportMode.Select),
                        "only viewport focus may invoke viewport shortcuts");
                    Debug.Log("[R4-B key delivery] " + control.name + " down=1 up=1 imageDowns=" + (imageDowns - beforeImageDowns)
                        + " imageUps=" + (imageUps - beforeImageUps) + " promptBubbleDowns=" + promptBubbleDowns + " mode=" + window.Mode
                        + " focusAfterTab=" + (root.focusController.focusedElement as VisualElement)?.name);
                }
                // Text input itself must still work without entering viewport handlers.
                prompt.Focus();
                yield return null;
                using (KeyDownEvent down = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.W, character = 'w' }))
                    ((VisualElement)root.focusController.focusedElement).SendEvent(down);
                Assert.That(prompt.value, Does.Contain("w"));
                image.Focus();
                yield return null;
                window.Routing.Update(true);
                bool releasedDuringFocusOut = false;
                image.RegisterCallback<FocusOutEvent>(_ => releasedDuringFocusOut = !window.Routing.Active);
                prompt.Focus();
                yield return null;
                Assert.That(releasedDuringFocusOut, Is.True, "routing must release synchronously during image FocusOut");
                Assert.That(window.Routing.Active, Is.False, "image FocusOut releases input");
            }
            finally { window.Close(); }
        }

        [Test]
        public void R2_30_ReopenedViewportAndLayoutEnforceMinima()
        {
            for (int i = 0; i < 2; i++)
            {
                StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
                try
                {
                    window.UseContext(_bed.Context); window.EnsureGui();
                    Assert.That(window.minSize.x, Is.GreaterThanOrEqualTo(640));
                    Assert.That(window.rootVisualElement.Q("viewport-area").style.minHeight.value.value, Is.GreaterThanOrEqualTo(360));
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
            }
            MethodInfo? minimum = typeof(StudioMenu).GetMethod("EnforceMinimum");
            Assert.That(minimum, Is.Not.Null);
            Rect bounds = (Rect)minimum!.Invoke(null, new object[] { new Rect(0, 0, 800, 600) });
            Assert.That(bounds.size, Is.EqualTo(new Vector2(1280, 720)));
            MethodInfo? layoutMethod = typeof(StudioMenu).GetMethod("Layout");
            Assert.That(layoutMethod, Is.Not.Null);
            Rect[] layout = (Rect[])layoutMethod!.Invoke(null, new object[] { bounds });
            Assert.That(layout[0].width, Is.GreaterThanOrEqualTo(640));
            Assert.That(layout[0].height, Is.GreaterThanOrEqualTo(480));
            Assert.That(layout[3].width, Is.GreaterThanOrEqualTo(420));
            Assert.That(layout[3].height, Is.GreaterThanOrEqualTo(260));
            for (int i = 0; i < layout.Length; i++)
            {
                Assert.That(layout[i].xMax, Is.LessThanOrEqualTo(bounds.xMax + .01));
                Assert.That(layout[i].yMax, Is.LessThanOrEqualTo(bounds.yMax + .01));
                for (int j = i + 1; j < layout.Length; j++) Assert.That(layout[i].Overlaps(layout[j]), Is.False);
            }
        }

        [Test]
        public void R2_01_GizmoProjectsPreviewWhileRealTargetRemainsUnchanged()
        {
            var entity = _bed.SpawnEntity("moving", Vector3.zero);
            _bed.SaveScene();
            ViewportMoveGizmo gizmo = new ViewportMoveGizmo(() => _bed.Runtime);
            Vector3 projected = Vector3.zero;
            Func<Vector3, Vector2?> project = p => { projected = p; return new Vector2(p.x * 100, p.y * 100); };
            gizmo.Refresh(entity.gameObject, project);
            Assert.That(gizmo.BeginDrag(0, Vector2.zero, 1), Is.True);
            try
            {
                gizmo.DragTo(new Vector2(200, 0));
                Vector3 first = Vector3.zero;
                bool captured = false;
                gizmo.Refresh(entity.gameObject, p => { if (!captured) { first = p; captured = true; } return project(p); });
                Assert.That(first.x, Is.EqualTo(2).Within(.001));
                Assert.That(entity.transform.position, Is.EqualTo(Vector3.zero));
            }
            finally { gizmo.Cancel(); }
        }

        [Test]
        public void R2_22_40_PanelSinksRedactSecretsAndRejectReasonIsLocal()
        {
            string secret = "etp_sensitive_123456789";
            ChangeSet entry = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent(secret, IntentOrigin.Agent),
                new[] { new Operation("op1", BuiltInToolIds.InspectDescribe, null, new JObject { ["apiToken"] = "private-value" }) });
            _bed.Context.Candidates.Add("request", entry);
            CandidatePanelView panel = new CandidatePanelView(_bed.Context); panel.Select(entry.Id);
            Assert.That(string.Join("\n", panel.Query<Label>().ToList().Select(label => label.text + label.tooltip)), Does.Not.Contain(secret).And.Not.Contain("private-value"));
            Assert.That(panel.Q<TextField>("candidate-reject-reason").tooltip, Does.Contain("locally").And.Not.Contain("sent back"));
            _bed.Runtime.Journal.Write(entry);
            HistoryPanelView history = new HistoryPanelView(_bed.Context); history.Select(entry.Id);
            Assert.That(history.Q<TextField>("history-json").value, Does.Not.Contain(secret).And.Not.Contain("private-value"));
        }

        private sealed class HistoryHandler : IHistoryEntryHandler
        {
            public HistoryEntryKind Kind => HistoryEntryKind.Admission;
            public readonly List<HistoryAction> Actions = new List<HistoryAction>();
            public HistoryResult Handle(ChangeSet entry, HistoryAction action, bool force)
            {
                Actions.Add(action);
                return new HistoryResult(entry.Id, false, ChangeSetState.Interrupted, Array.Empty<Diagnostic>(), null);
            }
        }

        private sealed class StageService : IStageService
        {
            public StageCandidateRequest? Request;
            public string Confinement = "docker";
            public string Defect = "";
            public int Verifications;
            public int Submissions;
            public Task<string> RequestStage(StageCandidateRequest request) { Submissions++; Request = request; return Task.FromResult("test-job"); }
            public Task<SignedVerdict> GetVerdict(string jobId)
            {
                var steps = new JArray();
                foreach (string id in new[] { "scan", "checkers", "dotnet", "unity-editmode", "playmode-smoke", "determinism", "budget" })
                    steps.Add(new JObject { ["id"] = id, ["status"] = Defect == "steps" && id == "dotnet" ? "fail" : "pass", ["durationMs"] = 1 });
                var verdict = new JObject
                {
                    ["schema"] = StageVerdict.SchemaId, ["changeSetId"] = Request!.ChangeSetId, ["jobId"] = jobId,
                    ["projectId"] = Request.ProjectId, ["sourceRevision"] = Request.SourceRevision, ["catalogRevision"] = Request.CatalogRevision,
                    ["confinement"] = Confinement, ["coldCache"] = true, ["pass"] = true, ["partial"] = false,
                    ["durationMs"] = 400000, ["budgetMs"] = 360000, ["steps"] = steps,
                    ["forbiddenHits"] = Defect == "forbidden" ? new JArray("hook") : new JArray(),
                    ["catalogDelta"] = new JObject
                    {
                        ["world"] = new string('c', 64),
                        ["mechanisms"] = new JArray(new JObject { ["package"] = "com.example.ui-fixture", ["catalogType"] = "Example.UiFixture.Catalog", ["fingerprint"] = new string('d', 64) }),
                        ["predicted"] = CatalogSet.Combine(new string('c', 64), new[] { new string('d', 64) }),
                    },
                    ["artifacts"] = new JArray(new JObject { ["role"] = "package", ["sha256"] = Request.PackageDigest }, new JObject { ["role"] = "proposal", ["sha256"] = Request.ProposalDigest }),
                };
                return Task.FromResult(new SignedVerdict(jobId, "companion-signature", verdict));
            }
            public Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request)
            {
                Verifications++;
                Assert.That(JToken.DeepEquals(JObject.FromObject(request.Expected), JObject.FromObject(Request!)), Is.True);
                return Task.FromResult(new StageVerification(Defect != "verify", jobId));
            }
        }
    }
}
