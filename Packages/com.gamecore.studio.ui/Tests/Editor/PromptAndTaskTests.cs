// GameCore.Studio.UI.Tests - the prompt bar and the task tray: a request carries the selection snapshot, a depth-2 index
// slice under the 64 KB cap (truncation reported), the tool catalog revision, mode and attachments; disabled reasons;
// voice transcripts land in the field and never submit; the task tray follows gateway events, cancels, answers
// clarifications with the parent id and survives a simulated domain reload with a re-fetch that recovers the candidate.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Fixtures;
using GameCore.Studio.Model;
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
            RequestHandle? handle = Done(bar.SubmitAsync());

            Assert.That(handle, Is.Not.Null);
            Assert.That(handle!.Accepted, Is.True);
            Assert.That(_bed.Gateway.Submitted.Count, Is.EqualTo(1));
            AgentRequest request = _bed.Gateway.Submitted[0];
            Assert.That(request.Intent.Text, Is.EqualTo("Make this blacksmith greet travellers"));
            Assert.That(request.Intent.Origin, Is.EqualTo(IntentOrigin.Agent));
            Assert.That(request.Mode, Is.EqualTo(AgentRequestMode.Edit));
            Assert.That(request.Selection.Targets.Count, Is.EqualTo(1));
            Assert.That(request.Selection.Targets[0].SameTarget(_bed.Ref(entity)), Is.True);
            Assert.That(request.Selection.Frame, Is.SameAs(frame));
            Assert.That(request.ToolCatalogRevision, Is.EqualTo(_bed.CatalogRevision()));
            Assert.That(request.ContextBytes, Is.LessThanOrEqualTo(AgentRequestBuilder.SliceByteCap));
            Assert.That(request.ContextTruncated, Is.False);
            Assert.That(((Newtonsoft.Json.Linq.JArray)request.ContextSlice["nodes"]!).Count, Is.GreaterThanOrEqualTo(2), "depth 2 reaches the referenced definition");
            Assert.That(request.Attachments.Count, Is.EqualTo(1));
            Assert.That(request.Attachments[0].MediaType, Is.EqualTo("image/png"));
            Assert.That(request.Attachments[0].Bytes, Is.EqualTo(4));
            Assert.That(request.ToJson()["toolCatalogRevision"]?.ToString(), Is.EqualTo(request.ToolCatalogRevision));
            Assert.That(bar.Text, Is.Empty, "an accepted request clears the field");
            Assert.That(_bed.Context.Tasks.Find(request.ChangeSetId)!.requestId, Is.EqualTo(handle.RequestId));
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

            AgentRequest uncapped = _bed.Context.Requests.Build("Make these villagers wave", snapshot, AgentRequestMode.Edit);
            Assert.That(uncapped.ContextBytes, Is.LessThanOrEqualTo(AgentRequestBuilder.SliceByteCap));
            Assert.That(uncapped.ContextTruncated, Is.EqualTo(full > AgentRequestBuilder.SliceByteCap));

            _bed.Context.Requests.ByteCap = Math.Max(512, full / 4);
            AgentRequest capped = _bed.Context.Requests.Build("Make these villagers wave", snapshot, AgentRequestMode.Edit);
            Assert.That(capped.ContextTruncated, Is.True);
            Assert.That(capped.ContextBytes, Is.LessThanOrEqualTo(_bed.Context.Requests.ByteCap));
            Assert.That(capped.ContextOmittedNodes, Is.GreaterThan(0));
            Assert.That((bool?)capped.ToJson()["contextTruncated"], Is.True);
            Debug.Log("[P2.1] slice bytes full=" + full + " capped=" + capped.ContextBytes + " omitted=" + capped.ContextOmittedNodes);
        }

        [Test]
        public void DisabledReasons_AreSpecific()
        {
            Assert.That(AgentRequestBuilder.DisabledReason(ProviderStatus.NotConfigured("no gateway"), "do it", false), Does.StartWith("Not configured"));
            Assert.That(AgentRequestBuilder.DisabledReason(new ProviderStatus(GatewayConnection.Disconnected, null, "refused"), "do it", false), Does.StartWith("No node"));
            Assert.That(AgentRequestBuilder.DisabledReason(_bed.Gateway.Status, "   ", false), Is.Not.Null);
            Assert.That(AgentRequestBuilder.DisabledReason(_bed.Gateway.Status, "make this red", false), Does.Contain("select"));
            Assert.That(AgentRequestBuilder.DisabledReason(_bed.Gateway.Status, "add a lantern to the village", false), Is.Null);

            PromptBar bar = new PromptBar(new StudioUiContext(_bed.Runtime, () => NullStudioAgentGateway.Instance, new SelectionModel(_bed.Runtime), new TaskLedger(new MemoryTaskRowStore()), false)) { Text = "add a lantern" };
            bar.Refresh();
            Assert.That(bar.DisabledReason, Does.StartWith("Not configured"));
            Assert.That(Done(bar.SubmitAsync()), Is.Null, "a disabled bar sends nothing");
        }

        [Test]
        public void Voice_FinalTranscriptLandsInTheFieldAndIsNeverSubmitted()
        {
            ScriptedVoiceSession voice = new ScriptedVoiceSession();
            _bed.Gateway.Voice = voice;
            PromptBar bar = new PromptBar(_bed.Context);
            bar.ToggleVoice();
            Assert.That(voice.Starts, Is.EqualTo(1));
            Assert.That(bar.VoiceActive, Is.True);

            voice.Say(new VoiceTranscript("utt_1", "add a lantern", 1, false));
            _bed.Context.Tick();
            Assert.That(bar.PartialTranscript, Does.Contain("add a lantern"));
            Assert.That(bar.Text, Is.Empty, "partial text stays out of the field");
            voice.Say(new VoiceTranscript("utt_1", "add a lantern by the well", 2, true));
            _bed.Context.Tick();
            Assert.That(bar.Text, Is.EqualTo("add a lantern by the well"));
            Assert.That(_bed.Gateway.Submitted, Is.Empty, "a transcript never submits");
            bar.ToggleVoice();
            Assert.That(voice.Stops, Is.EqualTo(1));

            RequestHandle? handle = Done(bar.SubmitAsync());
            Assert.That(handle!.Accepted, Is.True);
            Assert.That(_bed.Gateway.Submitted[0].Intent.Origin, Is.EqualTo(IntentOrigin.Voice));
            Assert.That(_bed.Gateway.Submitted[0].Intent.VoiceTranscriptId, Is.EqualTo("utt_1"));
        }

        [Test]
        public void TaskTray_FollowsEventsCancelsAndAnswersClarifications()
        {
            AgentRequest request = _bed.Context.Requests.Build("Add a lantern to the village", _bed.Context.Selection.Capture(SelectionMode.Edit), AgentRequestMode.Edit);
            RequestHandle handle = Done(_bed.Context.Submit(request));
            TaskRow row = _bed.Context.Tasks.Find(request.ChangeSetId)!;
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Queued));
            Assert.That(row.requestId, Is.EqualTo(handle.RequestId));

            DateTime created = DateTime.UtcNow.AddSeconds(-5);
            _bed.Gateway.Emit(AgentEvent.RequestUpdated(new AgentRequestInfo(handle.RequestId!, request.ChangeSetId, AgentRequestState.Running, request.Intent.Text, created, DateTime.UtcNow, "studio-agent", costUsd: 0.01, sequence: 2, etosStatus: "running")));
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Queued), "events are marshalled to the main thread");
            _bed.Context.Tick();
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Running));
            Assert.That(row.worker, Is.EqualTo("studio-agent"));
            Assert.That(row.ElapsedText(DateTime.UtcNow), Is.Not.Empty);

            TaskTrayView tray = new TaskTrayView(_bed.Context);
            tray.Rebuild();
            Assert.That(tray.RenderedRows, Is.EqualTo(1));
            Done(tray.Cancel(row));
            Assert.That(_bed.Gateway.Cancelled, Is.EqualTo(new[] { handle.RequestId }));

            _bed.Gateway.Emit(AgentEvent.RequestUpdated(new AgentRequestInfo(handle.RequestId!, request.ChangeSetId, AgentRequestState.NeedsClarification, request.Intent.Text, created, DateTime.UtcNow, question: "Which lantern style?", sequence: 3)));
            _bed.Context.Tick();
            Assert.That(row.question, Is.EqualTo("Which lantern style?"));
            RequestHandle? answer = Done(tray.Answer(row, "The iron one"));
            Assert.That(answer!.Accepted, Is.True);
            AgentRequest followUp = _bed.Gateway.Submitted[1];
            Assert.That(followUp.Parent, Is.EqualTo(request.ChangeSetId));
            Assert.That(followUp.Intent.Text, Does.Contain("The iron one"));
        }

        [Test]
        public void TaskTray_SurvivesDomainReloadAndRecoversTheCandidate()
        {
            FixtureNpcDefinition smith = _bed.CreateNpc("Blacksmith");
            FixtureAuthoredEntity entity = _bed.SpawnEntity("Smith", Vector3.zero, smith);
            _bed.SaveScene();
            AgentRequest request = _bed.Context.Requests.Build("Greet travellers warmly", _bed.Context.Selection.Capture(SelectionMode.Edit), AgentRequestMode.Edit);
            RequestHandle handle = Done(_bed.Context.Submit(request));
            Assert.That(_bed.Store.Rows.Count, Is.EqualTo(1));

            StudioUiContext reloaded = _bed.Reload();
            Assert.That(reloaded.Tasks.Rows.Count, Is.EqualTo(1), "rows persist across the reload");
            TaskRow row = reloaded.Tasks.Rows[0];
            Assert.That(row.changeSetId, Is.EqualTo(request.ChangeSetId));
            Assert.That(row.requestId, Is.EqualTo(handle.RequestId));
            Assert.That(row.intent, Is.EqualTo(request.Intent.Text));

            _bed.Gateway.LoadCandidate("candidate-greeting.json", new Dictionary<string, AuthoringRef> { ["npc"] = _bed.Ref(smith), ["entity"] = _bed.Ref(entity) }, _bed.CatalogRevision());
            _bed.Gateway.LoadRequests("requests-after-reload.json", new Dictionary<string, string> { ["$request"] = "req_fixture_greeting", ["$csid"] = request.ChangeSetId, ["$intent"] = request.Intent.Text });
            row.requestId = "req_fixture_greeting";
            Task recovery = reloaded.RecoverAsync();
            Assert.That(recovery.IsCompleted, Is.True);
            Assert.That(row.State, Is.EqualTo(AgentRequestState.Candidate), "the re-fetch brings the row up to date");
            Assert.That(row.worker, Is.EqualTo("studio-agent"));
            Assert.That(row.hasCost, Is.True);
            Assert.That(reloaded.Candidates.Entries.Count, Is.EqualTo(1), "the candidate is fetched after the reload");
        }
    }
}
