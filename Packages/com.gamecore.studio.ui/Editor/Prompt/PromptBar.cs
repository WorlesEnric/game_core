// GameCore.Studio.UI - the prompt bar docked at the bottom of the viewport (SR-1.5, SR-4.8, 04 s2/s5): a multi-line
// intent field (Ctrl/Cmd+Enter submits), a voice button (hold = push-to-talk, click = toggle) whose transcript revisions
// are shown inline while only the final transcript is dropped into the field for confirmation (it never submits by
// itself), provider status chips (image/tts/voice/3d/describe), attachments by drag and drop, and a disabled state
// that says why (no node, no key, no selection for a selection-scoped intent). The voice session is P2.2's
// (IAgentGateway.CreateVoiceSession: microphone capture and the realtime WebSocket belong to the etos package).
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SelectionMode = GameCore.Studio.Model.SelectionMode;

namespace GameCore.Studio.UI
{
    /// <summary>The prompt bar.</summary>
    public sealed class PromptBar : VisualElement
    {
        /// <summary>A press on the voice button longer than this is push-to-talk (released = stop).</summary>
        public const long PushToTalkMilliseconds = 350;

        private readonly StudioUiContext _context;
        private readonly Func<FrameContext?>? _captureFrame;
        private readonly TextField _input;
        private readonly Button _send;
        private readonly DropdownField _worker;
        private readonly Button _mic;
        private readonly Label _transcript;
        private readonly Label _reason;
        private readonly VisualElement _chips;
        private readonly VisualElement _attachmentsRow;
        private readonly VisualElement _level;
        private readonly List<PromptAttachment> _attachments = new List<PromptAttachment>();
        private IVoiceSession? _voice;
        private IAgentGateway? _voiceGateway;
        private bool _voiceActive;
        private long _micPressedAt;
        private bool _voiceToggled;
        private string? _voiceTranscriptId;
        private bool _submitting;
        private bool _attached;

        public PromptBar(StudioUiContext context, Func<FrameContext?>? captureFrame = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _captureFrame = captureFrame;
            name = "prompt-bar";
            AddToClassList("gcs-prompt");

            _transcript = new Label { name = "voice-transcript" };
            _transcript.AddToClassList("gcs-prompt__transcript");
            _transcript.style.display = DisplayStyle.None;
            Add(_transcript);

            VisualElement row = new VisualElement();
            row.AddToClassList("gcs-row");
            Add(row);

            _mic = new Button { name = "voice-button", text = "Mic", tooltip = "Voice: hold to talk, or click to toggle. The final transcript is placed in the field; nothing is sent until you press Send." };
            _mic.AddToClassList("gcs-prompt__mic");
            _mic.RegisterCallback<PointerDownEvent>(OnMicDown, TrickleDown.TrickleDown);
            _mic.RegisterCallback<PointerUpEvent>(OnMicUp, TrickleDown.TrickleDown);
            _mic.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Space || evt.keyCode == KeyCode.Return)
                {
                    ToggleVoice();
                    evt.StopPropagation();
                }
            });
            row.Add(_mic);

            _level = new VisualElement { name = "voice-level" };
            _level.AddToClassList("gcs-prompt__level");
            row.Add(_level);

            _input = new TextField { name = "prompt-input", multiline = true };
            _input.AddToClassList("gcs-prompt__input");
            _input.tooltip = "Describe the change. Ctrl+Enter (Cmd+Enter) sends it with the current selection.";
            _input.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            _input.RegisterValueChangedCallback(_ => Refresh());
            row.Add(_input);

            _send = new Button(() => _ = SubmitAsync()) { name = "prompt-send", text = "Send", tooltip = "Send (Ctrl+Enter)" };
            _send.AddToClassList("gcs-prompt__send");
            row.Add(_send);

            VisualElement footer = new VisualElement();
            footer.AddToClassList("gcs-wrap-row");
            Add(footer);
            _chips = new VisualElement { name = "provider-chips" };
            _chips.AddToClassList("gcs-row");
            _chips.AddToClassList("gcs-prompt__chips");
            footer.Add(_chips);
            _worker = new DropdownField("Worker") { name = "prompt-worker", tooltip = "Worker advertised by the companion for this request." };
            _worker.RegisterValueChangedCallback(change => SelectedWorker = change.newValue);
            footer.Add(_worker);
            _attachmentsRow = new VisualElement { name = "attachments" };
            _attachmentsRow.AddToClassList("gcs-row");
            footer.Add(_attachmentsRow);
            _reason = new Label { name = "prompt-reason" };
            _reason.AddToClassList("gcs-prompt__reason");
            Add(_reason);

            RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            RegisterCallback<DragPerformEvent>(OnDragPerform);
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
            Refresh();
        }

        /// <summary>The intent text.</summary>
        public string Text
        {
            get => _input.value ?? string.Empty;
            set
            {
                _input.value = StudioStyles.Safe(value);
                Refresh();
            }
        }

        /// <summary>The advertised worker for this prompt; also used by creator automation without a graphics panel.</summary>
        public string SelectedWorker
        {
            get => _worker.value;
            set
            {
                if (!_worker.choices.Contains(value)) throw new ArgumentException("The companion did not advertise worker " + StudioStyles.Safe(value) + ".", nameof(value));
                StudioWorkerSettings.instance.Worker = value;
                _worker.SetValueWithoutNotify(value);
            }
        }

        public IReadOnlyList<PromptAttachment> Attachments => _attachments;

        /// <summary>Why Send is disabled, or null.</summary>
        public string? DisabledReason { get; private set; }

        /// <summary>The last request built (sent or refused).</summary>
        public PreparedRequest? LastRequest { get; private set; }

        public PromptSubmission? LastHandle { get; private set; }

        /// <summary>The partial transcript currently shown (empty when none).</summary>
        public string PartialTranscript => _transcript.text ?? string.Empty;

        public bool VoiceActive => _voice != null && _voiceActive;

        /// <summary>Places text in the field (Agent-tier tools, clarifications) and focuses it.</summary>
        public void Prefill(string text)
        {
            Text = text;
            _voiceTranscriptId = null;
            _input.Focus();
        }

        public void AddAttachment(string path)
        {
            PromptAttachment attachment = AgentRequestBuilder.AttachmentFor(path);
            if (_attachments.Exists(existing => existing.Path == attachment.Path))
            {
                return;
            }

            if (_attachments.Count >= AgentRequestBuilder.MaxAttachments)
            {
                _reason.text = "At most eight attachments are allowed.";
                return;
            }
            _attachments.Add(attachment);
            RebuildAttachments();
        }

        /// <summary>Recomputes chips, the disabled reason and the voice button.</summary>
        public void Refresh()
        {
            IAgentGateway gateway = _context.Gateway;
            ProviderStatus status = gateway.Status;
            IReadOnlyList<string> advertised = GatewayExtras.Workers(gateway);
            List<string> workers = new List<string>();
            foreach (string worker in advertised)
                if (!string.IsNullOrWhiteSpace(worker) && !workers.Contains(worker)) workers.Add(worker);
            string preferred = StudioWorkerSettings.instance.Worker;
            string selected = workers.Contains(preferred) ? preferred : workers.Contains("gc-designer") ? "gc-designer" : workers.Count > 0 ? workers[0] : "gc-designer";
            _worker.choices = workers;
            _worker.SetValueWithoutNotify(selected);
            _worker.SetEnabled(workers.Count > 0 && !_submitting);
            _chips.Clear();
            foreach (string provider in ProviderNames.All)
            {
                _chips.Add(StudioStyles.ProviderChip(provider, status.For(provider)));
            }

            DisabledReason = _submitting ? "Sending..." : AgentRequestBuilder.DisabledReason(status, Text, !_context.Selection.IsEmpty);
            _send.SetEnabled(DisabledReason == null);
            _reason.text = StudioStyles.Safe(DisabledReason ?? (LastHandle != null ? LastHandleText(LastHandle) : string.Empty));
            if (LastRequest?.ContextTruncated == true) _reason.text += " Context truncated to byte/object limits (" + LastRequest.ContextOmittedNodes + " index nodes omitted).";
            _send.tooltip = StudioStyles.Safe(DisabledReason ?? "Send (Ctrl+Enter)");

            bool voiceAvailable = status.Voice != ProviderState.NotConfigured && status.Voice != ProviderState.Blocked;
            _mic.SetEnabled(voiceAvailable || VoiceActive);
            _mic.tooltip = StudioStyles.Safe(voiceAvailable
                ? "Voice: hold to talk, or click to toggle. The final transcript is placed in the field; nothing is sent until you press Send."
                : "Voice unavailable: the voice provider is " + ProviderNames.Wire(status.Voice) + ".");
            _mic.EnableInClassList("gcs-prompt__mic--active", VoiceActive);
        }

        /// <summary>Builds and submits the request (no-op with a reason when disabled).</summary>
        public async Task<PromptSubmission?> SubmitAsync()
        {
            Refresh();
            if (DisabledReason != null)
            {
                return null;
            }

            SelectionMode mode = EditorApplication.isPlaying ? SelectionMode.Play : SelectionMode.Edit;
            FrameContext? frame = _captureFrame?.Invoke();
            string? world = mode == SelectionMode.Play ? GameCore.Unity.App.GameApplication.Current?.World.ToString() : null;
            SelectionSnapshot selection = _context.Selection.Capture(mode, frame, world);
            IntentOrigin origin = _voiceTranscriptId != null ? IntentOrigin.Voice : IntentOrigin.Agent;
            PreparedRequest request;
            try
            {
                request = _context.Requests.Build(Text, selection, origin, _voiceTranscriptId, new List<PromptAttachment>(_attachments), worker: _worker.value);
            }
            catch (Exception error) when (error is System.IO.IOException || error is ArgumentException || error is UnauthorizedAccessException)
            {
                _reason.text = StudioStyles.Safe("Not sent: " + error.Message);
                return null;
            }

            LastRequest = request;
            _submitting = true;
            Refresh();
            PromptSubmission handle;
            try
            {
                handle = await _context.Submit(request);
            }
            finally
            {
                _submitting = false;
            }

            LastHandle = handle;
            if (handle.Accepted)
            {
                _input.value = string.Empty;
                _attachments.Clear();
                _voiceTranscriptId = null;
                RebuildAttachments();
            }

            Refresh();
            return handle;
        }

        /// <summary>Starts or stops the voice session (toggle mode).</summary>
        public void ToggleVoice()
        {
            if (VoiceActive)
            {
                StopVoice();
            }
            else
            {
                StartVoice();
            }
        }

        /// <summary>Handles one transcript revision (main thread): partial text shown inline, final text placed in the field.</summary>
        public void OnTranscript(TranscriptUpdate transcript)
        {
            if (transcript.Role != "user")
            {
                return;
            }

            if (!transcript.Final)
            {
                _transcript.text = StudioStyles.Safe("listening (rev " + transcript.Revision + "): " + transcript.Text);
                _transcript.style.display = DisplayStyle.Flex;
                return;
            }

            string current = Text.Trim();
            Text = current.Length == 0 ? transcript.Text.Trim() : current + " " + transcript.Text.Trim();
            _voiceTranscriptId = transcript.ItemId;
            _transcript.text = "Final transcript placed in the field; review it and press Send (Ctrl+Enter). Nothing was sent.";
            _transcript.style.display = DisplayStyle.Flex;
        }

        private void StartVoice()
        {
            IAgentGateway gateway = _context.Gateway;
            if (_voice == null || !ReferenceEquals(gateway, _voiceGateway))
            {
                DetachVoice();
                _voice = gateway.CreateVoiceSession();
                _voiceGateway = gateway;
                _voice.Transcript += OnVoiceTranscript;
                _voice.Level += OnVoiceLevel;
                _voice.Error += OnVoiceFailed;
            }

            _voiceActive = true;
            _transcript.text = "listening...";
            _transcript.style.display = DisplayStyle.Flex;
            Observe(_voice.StartAsync());
            Refresh();
        }

        private void StopVoice()
        {
            if (_voice != null && _voiceActive)
            {
                Observe(_voice.StopAsync());
            }

            _voiceActive = false;
            _voiceToggled = false;
            Refresh();
        }

        /// <summary>Surfaces a failed voice start/stop as the session's error (code preserved).</summary>
        private void Observe(Task task)
        {
            task.ContinueWith(done => OnVoiceFailed(GatewayErrors.ToDiagnostic(done.Exception!)), TaskContinuationOptions.OnlyOnFaulted);
        }

        private void OnMicDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            _micPressedAt = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            if (VoiceActive && _voiceToggled)
            {
                StopVoice();
                _micPressedAt = 0;
            }
            else if (!VoiceActive)
            {
                StartVoice();
            }

            evt.StopPropagation();
        }

        private void OnMicUp(PointerUpEvent evt)
        {
            if (evt.button != 0 || _micPressedAt == 0)
            {
                return;
            }

            long held = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond) - _micPressedAt;
            _micPressedAt = 0;
            if (held >= PushToTalkMilliseconds)
            {
                StopVoice();
            }
            else
            {
                _voiceToggled = true;
            }

            evt.StopPropagation();
        }

        private void OnVoiceTranscript(TranscriptUpdate transcript) => _context.Dispatcher.Post(() => OnTranscript(transcript));

        private void OnVoiceLevel(float level) => _context.Dispatcher.Post(() => _level.style.width = Mathf.Clamp01(level) * 40f);

        private void OnVoiceFailed(Diagnostic diagnostic) => _context.Dispatcher.Post(() =>
        {
            _transcript.text = StudioStyles.Safe("Voice: " + diagnostic.Code + ": " + diagnostic.Message);
            _transcript.style.display = DisplayStyle.Flex;
            _voiceActive = false;
            _voiceToggled = false;
            Refresh();
        });

        private void OnInputKeyDown(KeyDownEvent evt)
        {
            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && (evt.ctrlKey || evt.commandKey))
            {
                _ = SubmitAsync();
                evt.StopPropagation();
            }
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if (DragAndDrop.paths == null)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            foreach (string path in DragAndDrop.paths)
            {
                AddAttachment(System.IO.Path.GetFullPath(path));
            }
        }

        private void RebuildAttachments()
        {
            _attachmentsRow.Clear();
            foreach (PromptAttachment attachment in _attachments)
            {
                PromptAttachment captured = attachment;
                Button chip = new Button(() =>
                {
                    _attachments.Remove(captured);
                    RebuildAttachments();
                })
                { text = StudioStyles.Safe(attachment.Name + " x"), tooltip = StudioStyles.Safe(attachment.MediaType + ", " + attachment.Bytes + " bytes (click to remove)") };
                chip.AddToClassList("gcs-chip");
                _attachmentsRow.Add(chip);
            }
        }

        private static string LastHandleText(PromptSubmission handle)
        {
            if (handle.Refusal != null)
            {
                return "Refused: " + handle.Refusal.Code + ": " + handle.Refusal.Message;
            }

            return "Sent " + handle.ChangeSetId + "; follow it in the task tray.";
        }

        private void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _context.StatusChanged += OnStatusChanged;
            _context.Selection.Changed += Refresh;
            Refresh();
        }

        private void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _context.StatusChanged -= OnStatusChanged;
            _context.Selection.Changed -= Refresh;
            DetachVoice();
        }

        private void OnStatusChanged(ProviderStatus status) => Refresh();

        private void DetachVoice()
        {
            if (_voice != null)
            {
                if (_voiceActive)
                {
                    Observe(_voice.StopAsync());
                }

                _voice.Transcript -= OnVoiceTranscript;
                _voice.Level -= OnVoiceLevel;
                _voice.Error -= OnVoiceFailed;
                _voice = null;
                _voiceGateway = null;
                _voiceActive = false;
            }
        }
    }
}
