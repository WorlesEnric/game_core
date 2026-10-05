// GameCore.Studio.UI.Tests - the prompt bar and the task tray over P2.2's gateway contract: a request carries the
// selection snapshot, a depth-2 index slice under the 64 KB cap (truncation reported), the tool catalog revision, worker
// mode and attachment bytes; disabled reasons follow the provider status; a refusal keeps its code; voice transcripts
// land in the field and never submit; the task tray follows RequestViews, cancels, answers clarifications with the
// parent id and survives a simulated domain reload, recovering the candidate (fetched, or adopted from the gateway's own
// import).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Studio.UI.Tests
{
    public sealed class PromptAndTaskTests
    {
        private UiTestBed _bed = null!;

        [SetUp]
        public void SetUp() => _bed = new UiTestBed();

        [TearDown]
        public void TearDown() => _bed.Dispose();

        private static void Done(Task task)
        {
            Assert.That(task.IsCompleted, Is.True, "the test gateway answers synchronously");
            task.GetAwaiter().GetResult();
        }

        private static T Done<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True, "the test gateway answers synchronously");
            return task.Result;
        }

        [Test]
        public void Submit_CarriesSelectionSliceCatalogRevisionModeAndAttachments()
        {
            FixtureNpcDefinition smith = _bed.CreateNpc("Blacksmith");
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Smith", Vector3.zero, smith);
            _bed.SaveScene();
            _bed.Runtime.Index.Rebuild();
            _bed.Context.Selection.Set(new[] { _bed.Ref(entity) });
            string attachment = Path.Combine(_bed.StateRoot, "reference.png");
            File.WriteAllBytes(attachment, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            FrameContext frame = new FrameContext(new CameraPose(new double[] { 0, 3, -10 }, new double[] { 0, 0, 0, 1 }, 60, 1.7777), new[] { 1280, 720 });

            PromptBar bar = new PromptBar(_bed.Context, () => frame) { Text = "Make this blacksmith greet travellers" };
            bar.AddAttachment(attachment);
            PromptSubmission? handle = Done(bar.SubmitAsync());

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.Accepted, Is.True);
            Assert.That(_bed.Gateway.Submitted.Count, Is.EqualTo(1));
            AgentRequest request = _bed.Gateway.Submitted[0];
            PreparedRequest prepared = bar.LastRequest!;
            Assert.That(request.Intent, Is.EqualTo("Make this blacksmith greet travellers"));
            Assert.That(prepared.Intent.Origin, Is.EqualTo(IntentOrigin.Agent));
            Assert.That(request.Mode, Is.EqualTo("design"));
            Assert.That(request.ChangeSetId, Is.EqualTo(prepared.ChangeSetId));
            Assert.That(request.Selection.Mode, Is.EqualTo(SelectionMode.Edit));
            Assert.That(request.Selection.Targets.Count, Is.EqualTo(1));
            Assert.That(request.Selection.Targets[0].SameTarget(_bed.Ref(entity)), Is.True);
            Assert.That(request.Selection.Frame, Is.SameAs(frame));
            Assert.That(request.ToolCatalogRevision, Is.EqualTo(_bed.CatalogRevision()));
            Assert.That(prepared.ContextBytes, Is.LessThanOrEqualTo(AgentRequestBuilder.SliceByteCap));
            Assert.That(prepared.ContextTruncated, Is.False);
            Assert.That(request.ContextSlice.Nodes.Count, Is.GreaterThanOrEqualTo(2), "depth 2 reaches the referenced definition");
            Assert.That(request.Attachments.Count, Is.EqualTo(1));
            Assert.That(request.Attachments[0].MediaType, Is.EqualTo("image/png"));
            Assert.That(request.Attachments[0].Data, Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
            Assert.That(bar.Text, Is.Empty, "an accepted request clears the field");
            Assert.That(_bed.Context.Tasks.Find(prepared.ChangeSetId)!.requestId, Is.EqualTo(handle.RequestId));
        }

        [Test]
        public void Slice_IsCappedAndTruncationIsReported()
        {
            List<AuthoringRef> targets = new List<AuthoringRef>();
            for (int i = 0; i < 40; i++)
            {
                FixtureNpcDefinition npc = _bed.CreateNpc("Npc" + i, "Greeting number " + i + " with some padding text to make nodes larger");
                targets.Add(_bed.Ref(_bed.SpawnEntity("Villager" + i, new Vector3(i * 2f, 0f, 0f), npc)));
            }

            _bed.SaveScene();
            _bed.Runtime.Index.Rebuild();
            _bed.Context.Selection.Set(targets);
            SelectionSnapshot snapshot = _bed.Context.Selection.Capture(SelectionMode.Edit);
            int full = _bed.Runtime.Index.Slice(targets, AgentRequestBuilder.SliceDepth, int.MaxValue).Bytes;

            PreparedRequest uncapped = _bed.Context.Requests.Build("Make these villagers wave", snapshot);
            Assert.That(uncapped.ContextBytes, Is.LessThanOrEqualTo(AgentRequestBuilder.SliceByteCap));
            Assert.That(uncapped.ContextTruncated, Is.EqualTo(full > AgentRequestBuilder.SliceByteCap));

            _bed.Context.Requests.ByteCap = Math.Max(512, full / 4);
            PreparedRequest capped = _bed.Context.Requests.Build("Make these villagers wave", snapshot);
            Assert.That(capped.ContextTruncated, Is.True);
            Assert.That(capped.ContextBytes, Is.LessThanOrEqualTo(_bed.Context.Requests.ByteCap));
            Assert.That(capped.ContextOmittedNodes, Is.GreaterThan(0));
            Assert.That(capped.Request.ContextSlice.Nodes.Count, Is.LessThan(uncapped.Request.ContextSlice.Nodes.Count));
            Debug.Log("[P2.1] slice bytes full=" + full + " capped=" + capped.ContextBytes + " omitted=" + capped.ContextOmittedNodes);
        }

        [Test]
        public void DisabledReasons_AreSpecific()
        {
            ProviderStatus unreachable = new ProviderStatus(ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, false, false, null, new Diagnostic("transport", "connection refused"), DateTime.UtcNow);
            ProviderStatus wrongKey = new ProviderStatus(ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, true, false, null, new Diagnostic("unauthorized", "bad key", "Pair again."), DateTime.UtcNow);
            ProviderStatus starting = new ProviderStatus(ProviderState.Live, ProviderState.Live, ProviderState.Unknown, ProviderState.NotConfigured, ProviderState.Live, true, false, "0.5.0", new Diagnostic("agent_starting", "not connected"), DateTime.UtcNow);
            Assert.That(AgentRequestBuilder.DisabledReason(NullAgentGateway.Instance.Status, "do it", false), Does.StartWith("Not configured"));
            Assert.That(AgentRequestBuilder.DisabledReason(ProviderStatus.Unknown, "do it", false), Does.StartWith("Connecting"));
            Assert.That(AgentRequestBuilder.DisabledReason(unreachable, "do it", false), Does.StartWith("No node"));
            Assert.That(AgentRequestBuilder.DisabledReason(wrongKey, "do it", false), Does.StartWith("Refused by the node (unauthorized)"));
            Assert.That(AgentRequestBuilder.DisabledReason(starting, "do it", false), Does.Contain("not connected yet"));
            Assert.That(AgentRequestBuilder.DisabledReason(_bed.Gateway.Status, "   ", false), Is.Not.Null);
            Assert.That(AgentRequestBuilder.DisabledReason(_bed.Gateway.Status, "make this red", false), Does.Contain("select"));
            Assert.That(AgentRequestBuilder.DisabledReason(_bed.Gateway.Status, "add a lantern to the village", false), Is.Null);

            PromptBar bar = new PromptBar(new StudioUiContext(_bed.Runtime, () => NullAgentGateway.Instance, new SelectionModel(_bed.Runtime), new TaskLedger(new MemoryTaskRowStore()), false)) { Text = "add a lantern" };
            bar.Refresh();
            Assert.That(bar.DisabledReason, Does.StartWith("Not configured"));
            Assert.That(Done(bar.SubmitAsync()), Is.Null, "a disabled bar sends nothing");
        }

        [Test]
        public void Submit_RefusalKeepsTheCodeAndTheNullGatewayNeverFakesAnAnswer()
        {
            _bed.Gateway.RefuseWith = new Diagnostic("stale_context", "The tool catalog changed.", "Rebuild the request.");
            PreparedRequest request = _bed.Context.Requests.Build("Add a lantern to the village", _bed.Context.Selection.Capture(SelectionMode.Edit));
            PromptSubmission refused = Done(_bed.Context.Submit(request));
            Assert.That(refused.Accepted, Is.False);
            Assert.That(refused.Refusal!.Code, Is.EqualTo("stale_context"));
            TaskRow row = _bed.Context.Tasks.Find(request.ChangeSetId)!;
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Refused));
            Assert.That(row.Diagnostics()[0].Hint, Is.EqualTo("Rebuild the request."));

            Assert.That(NullAgentGateway.Instance.SubmitAsync(request.Request, default).IsFaulted, Is.True);
            Assert.That(NullAgentGateway.Instance.FetchCandidateAsync("cs_x", default).IsFaulted, Is.True);
            Assert.That(Done(NullAgentGateway.Instance.GenerateAsync(new OpRequest("generate.image"), default)).Refusal!.Code, Is.EqualTo("not_configured"));
            Assert.That(GatewayErrors.ToDiagnostic(new InvalidOperationException("socket closed")).Code, Is.EqualTo("transport"));
        }

        [Test]
        public void Voice_FinalTranscriptLandsInTheFieldAndIsNeverSubmitted()
        {
            ScriptedVoiceSession voice = _bed.Gateway.Voice;
            PromptBar bar = new PromptBar(_bed.Context);
            bar.ToggleVoice();
            Assert.That(voice.Starts, Is.EqualTo(1));
            Assert.That(bar.VoiceActive, Is.True);

            voice.Say(new TranscriptUpdate("item_1", 1, "add a lantern", false));
            _bed.Context.Tick();
            Assert.That(bar.PartialTranscript, Does.Contain("add a lantern"));
            Assert.That(bar.Text, Is.Empty, "partial text stays out of the field");
            voice.Say(new TranscriptUpdate("item_1", 2, "add a lantern by the well", true));
            _bed.Context.Tick();
            Assert.That(bar.Text, Is.EqualTo("add a lantern by the well"));
            Assert.That(_bed.Gateway.Submitted, Is.Empty, "a transcript never submits");
            bar.ToggleVoice();
            Assert.That(voice.Stops, Is.EqualTo(1));

            PromptSubmission? handle = Done(bar.SubmitAsync());
            Assert.That(handle!.Accepted, Is.True);
            Assert.That(bar.LastRequest!.Intent.Origin, Is.EqualTo(IntentOrigin.Voice));
            Assert.That(_bed.Gateway.Submitted[0].VoiceTranscriptId, Is.EqualTo("item_1"));

            voice.Fail(new Diagnostic("rate_limited", "Too many sessions."));
            _bed.Context.Tick();
            Assert.That(bar.PartialTranscript, Does.Contain("rate_limited"));
        }

        [Test]
        public void TaskTray_FollowsEventsCancelsAndAnswersClarifications()
        {
            PreparedRequest request = _bed.Context.Requests.Build("Add a lantern to the village", _bed.Context.Selection.Capture(SelectionMode.Edit));
            PromptSubmission handle = Done(_bed.Context.Submit(request));
            string id = handle.RequestId!;
            TaskRow row = _bed.Context.Tasks.Find(request.ChangeSetId)!;
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Queued));
            Assert.That(row.requestId, Is.EqualTo(id));
            Assert.That(id, Is.EqualTo(request.ChangeSetId), "P2.2: the request id is the change-set id");

            _bed.Gateway.Emit(TestAgentGateway.View(id, "running", request.Intent.Text, "gc-designer", "running", seq: 2, progress: "running: reading the slice"));
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Queued), "events are marshalled to the main thread");
            _bed.Context.Tick();
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Running));
            Assert.That(row.worker, Is.EqualTo("gc-designer"));
            Assert.That(row.etosStatus, Is.EqualTo("running"));
            Assert.That(row.progress, Does.Contain("reading the slice"));
            Assert.That(row.ElapsedText(DateTime.UtcNow), Is.Not.Empty);

            TaskTrayView tray = new TaskTrayView(_bed.Context);
            tray.Rebuild();
            Assert.That(tray.RenderedRows, Is.EqualTo(1));
            Done(tray.Cancel(row));
            Assert.That(_bed.Gateway.Cancelled, Is.EqualTo(new[] { id }));

            _bed.Gateway.Emit(TestAgentGateway.View(id, "needs_clarification", request.Intent.Text, outcome: new JObject { ["code"] = "needs_clarification", ["text"] = "Which lantern style?" }, seq: 3));
            _bed.Context.Tick();
            Assert.That(row.question, Is.EqualTo("Which lantern style?"));
            PromptSubmission? answer = Done(tray.Answer(row, "The iron one"));
            Assert.That(answer!.Accepted, Is.True);
            AgentRequest followUp = _bed.Gateway.Submitted[1];
            Assert.That(followUp.Parent, Is.EqualTo(request.ChangeSetId));
            Assert.That(followUp.Intent, Does.Contain("The iron one"));

            _bed.Gateway.Emit(TestAgentGateway.View(id, "failed", request.Intent.Text, outcome: new JObject { ["code"] = "task_failed", ["message"] = "worker crashed" }, seq: 4));
            _bed.Gateway.Emit(TestAgentGateway.View("cs_unknown_state", "teleported", "?", seq: 1));
            _bed.Context.Tick();
            Assert.That(row.State, Is.EqualTo(AgentRequestState.TaskFailed));
            Assert.That(row.Diagnostics(), Has.Some.Matches<Diagnostic>(d => d.Code == "task_failed"));
            Assert.That(_bed.Context.Tasks.Find("cs_unknown_state")!.State, Is.EqualTo(AgentRequestState.Unresolved), "an unknown state is never shown as success");
        }

        [Test]
        public void TaskTray_SurvivesDomainReloadAndRecoversTheCandidate()
        {
            FixtureNpcDefinition smith = _bed.CreateNpc("Blacksmith");
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Smith", Vector3.zero, smith);
            _bed.SaveScene();
            PreparedRequest request = _bed.Context.Requests.Build("Greet travellers warmly", _bed.Context.Selection.Capture(SelectionMode.Edit));
            PromptSubmission handle = Done(_bed.Context.Submit(request));
            Assert.That(_bed.Store.Rows.Count, Is.EqualTo(1));

            StudioUiContext reloaded = _bed.Reload();
            Assert.That(reloaded.Tasks.Rows.Count, Is.EqualTo(1), "rows persist across the reload");
            TaskRow row = reloaded.Tasks.Rows[0];
            Assert.That(row.changeSetId, Is.EqualTo(request.ChangeSetId));
            Assert.That(row.requestId, Is.EqualTo(handle.RequestId));
            Assert.That(row.intent, Is.EqualTo(request.Intent.Text));

            _bed.Gateway.LoadCandidate("candidate-greeting.json", new Dictionary<string, AuthoringRef> { ["npc"] = _bed.Ref(smith), ["entity"] = _bed.Ref(entity) }, _bed.CatalogRevision(), request.ChangeSetId);
            _bed.Gateway.LoadRequests("requests-after-reload.json", new Dictionary<string, string> { ["$request"] = request.ChangeSetId, ["$csid"] = request.ChangeSetId, ["$intent"] = request.Intent.Text });
            Task recovery = reloaded.RecoverAsync();
            Assert.That(recovery.IsCompleted, Is.True);
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Candidate), "the re-fetch brings the row up to date");
            Assert.That(row.worker, Is.EqualTo("gc-designer"));
            Assert.That(row.etosStatus, Is.EqualTo("done"));
            Assert.That(reloaded.Candidates.Entries.Count, Is.EqualTo(1), "the candidate is fetched after the reload");
            Assert.That(reloaded.Candidates.Entries[0].GatewayStaged, Is.False);
        }

        [Test]
        public void GatewayImportedCandidate_IsAdoptedNotStagedTwice()
        {
            FixtureNpcDefinition smith = _bed.CreateNpc("Blacksmith", "Hello");
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Smith", Vector3.zero, smith);
            _bed.SaveScene();
            _bed.Runtime.Index.Rebuild();
            _bed.Gateway.Options.AutoImport = true;
            PreparedRequest request = _bed.Context.Requests.Build("Greet travellers warmly", _bed.Context.Selection.Capture(SelectionMode.Edit));
            PromptSubmission handle = Done(_bed.Context.Submit(request));
            string id = handle.RequestId!;
            _bed.Gateway.LoadCandidate("candidate-greeting.json", new Dictionary<string, AuthoringRef> { ["npc"] = _bed.Ref(smith), ["entity"] = _bed.Ref(entity) }, _bed.CatalogRevision(), id);

            _bed.Gateway.EmitCandidate(id, id);
            _bed.Context.Tick();
            Assert.That(_bed.Context.Candidates.Entries, Is.Empty, "an AutoImport gateway imports its own candidate; the UI waits for it");

            StagedChangeSet imported = _bed.Gateway.Import(_bed.Runtime, id);
            Assert.That(imported.Ok, Is.True, string.Join("; ", imported.Diagnostics));
            Assert.That(GatewayExtras.ImportsItself(_bed.Gateway, id), Is.True);
            Assert.That(GatewayExtras.StagedBy(_bed.Gateway, id), Is.SameAs(imported), "the gateway's staged change set is visible to the UI");
            _bed.Gateway.Emit(TestAgentGateway.View(id, "candidate", request.Intent.Text, localState: "staged", hasCandidate: true, seq: 5));
            _bed.Context.Tick();
            CandidateEntry? entry = _bed.Context.Candidates.Find(id);
            Assert.That(entry, Is.Not.Null, "adopted when the gateway reports the candidate staged");
            Assert.That(entry!.GatewayStaged, Is.True);
            Assert.That(entry.Staged, Is.SameAs(imported), "the gateway's staged change set is adopted, not staged again");
            Assert.That(entry.Stage, Is.EqualTo(CandidateStage.Previewing));
            Assert.That(_bed.Context.Candidates.Preview(entry), Is.SameAs(imported));
            Assert.That(_bed.Context.Tasks.Find(id)!.localState, Is.EqualTo("staged"));

            _bed.Context.Candidates.Reject(entry, "Not now");
            Assert.That(imported.Consumed, Is.True);
            Assert.That(_bed.Runtime.Journal.Read(id)!.EffectiveState, Is.EqualTo(ChangeSetState.Rejected));
            Assert.That(_bed.Gateway.Rejected, Has.Count.EqualTo(1), "the gateway is told after the UI discarded the stage");
            Assert.That(smith.greeting, Is.EqualTo("Hello"));
        }
    }
}
