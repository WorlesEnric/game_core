// GameCore.Studio.UI - the prompt bar docked at the bottom of the viewport (SR-1.5, SR-4.8, 04 s2/s5): a multi-line
// intent field (Ctrl/Cmd+Enter submits), a voice button (hold = push-to-talk, click = toggle) whose transcript revisions
// are shown inline while only the final transcript is dropped into the field for confirmation (it never submits by
// itself), provider status chips (image/tts/voice/3d/describe), attachments by drag and drop, and a disabled state
// that says why (no node, no key, no selection for a selection-scoped intent).
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

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
        private readonly Button _mic;
        private readonly Label _transcript;
        private readonly Label _reason;
        private readonly VisualElement _chips;
        private readonly VisualElement _attachmentsRow;
        private readonly VisualElement _level;
        private readonly List<AgentAttachment> _attachments = new List<AgentAttachment>();
        private IVoiceSession? _voice;
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
            footer.AddToClassList("gcs-row");
            Add(footer);
            _chips = new VisualElement { name = "provider-chips" };
            _chips.AddToClassList("gcs-row");
            footer.Add(_chips);
            _attachmentsRow = new VisualElement { name = "attachments" };
            _attachmentsRow.AddToClassList("gcs-row");
            footer.Add(_attachmentsRow);
            _reason = new Label { name = "prompt-reason" };
            _reason.AddToClassList("gcs-prompt__reason");
            footer.Add(_reason);

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
                _input.value = value ?? string.Empty;
                Refresh();
            }
        }

        public IReadOnlyList<AgentAttachment> Attachments => _attachments;

        /// <summary>Why Send is disabled, or null.</summary>
        public string? DisabledReason { get; private set; }

        /// <summary>The last request built (sent or refused).</summary>
        public AgentRequest? LastRequest { get; private set; }

        public RequestHandle? LastHandle { get; private set; }

        /// <summary>The partial transcript currently shown (empty when none).</summary>
        public string PartialTranscript => _transcript.text ?? string.Empty;

        public bool VoiceActive => _voice != null && _voice.IsActive;

        /// <summary>Places text in the field (Agent-tier tools, clarifications) and focuses it.</summary>
        public void Prefill(string text)
        {
            Text = text;
            _voiceTranscriptId = null;
            _input.Focus();
        }

        public void AddAttachment(string path)
        {
            AgentAttachment attachment = AgentRequestBuilder.AttachmentFor(path);
            if (_attachments.Exists(existing => existing.Path == attachment.Path))
            {
                return;
            }

            _attachments.Add(attachment);
            RebuildAttachments();
        }

        /// <summary>Recomputes chips, the disabled reason and the voice button.</summary>
        public void Refresh()
        {
            IStudioAgentGateway gateway = _context.Gateway;
            ProviderStatus status = gateway.Status;
            _chips.Clear();
            foreach (string provider in ProviderNames.All)
            {
                _chips.Add(StudioStyles.ProviderChip(provider, status.Of(provider)));
            }

            DisabledReason = _submitting ? "Sending..." : AgentRequestBuilder.DisabledReason(status, Text, !_context.Selection.IsEmpty);
            _send.SetEnabled(DisabledReason == null);
            _reason.text = DisabledReason ?? (LastHandle != null ? LastHandleText(LastHandle) : string.Empty);
            _send.tooltip = DisabledReason ?? "Send (Ctrl+Enter)";

            IVoiceSession? voice = gateway.Voice;
            bool voiceAvailable = voice != null && status.Of(ProviderNames.Voice) != ProviderAvailability.NotConfigured && status.Of(ProviderNames.Voice) != ProviderAvailability.Blocked;
            _mic.SetEnabled(voiceAvailable || VoiceActive);
            _mic.tooltip = voiceAvailable
                ? "Voice: hold to talk, or click to toggle. The final transcript is placed in the field; nothing is sent until you press Send."
                : "Voice unavailable: " + (voice == null ? "the gateway has no voice session" : "voice provider is " + ProviderNames.Wire(status.Of(ProviderNames.Voice))) + ".";
            _mic.EnableInClassList("gcs-prompt__mic--active", VoiceActive);
        }

        /// <summary>Builds and submits the request (no-op with a reason when disabled).</summary>
        public async Task<RequestHandle?> SubmitAsync()
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
            AgentRequest request = _context.Requests.Build(Text, selection, mode == SelectionMode.Play ? AgentRequestMode.Play : AgentRequestMode.Edit, origin, _voiceTranscriptId, new List<AgentAttachment>(_attachments));
            LastRequest = request;
            _submitting = true;
            Refresh();
            RequestHandle handle;
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
        public void OnTranscript(VoiceTranscript transcript)
        {
            if (!transcript.IsFinal)
            {
                _transcript.text = "listening (rev " + transcript.Revision + "): " + transcript.Text;
                _transcript.style.display = DisplayStyle.Flex;
                return;
            }

            string current = Text.Trim();
            Text = current.Length == 0 ? transcript.Text.Trim() : current + " " + transcript.Text.Trim();
            _voiceTranscriptId = transcript.UtteranceId;
            _transcript.text = "Final transcript placed in the field; review it and press Send (Ctrl+Enter). Nothing was sent.";
            _transcript.style.display = DisplayStyle.Flex;
        }

        private void StartVoice()
        {
            IVoiceSession? voice = _context.Gateway.Voice;
            if (voice == null)
            {
                Refresh();
                return;
            }

            if (!ReferenceEquals(voice, _voice))
            {
                DetachVoice();
                _voice = voice;
                _voice.Transcript += OnVoiceTranscript;
                _voice.Level += OnVoiceLevel;
                _voice.Failed += OnVoiceFailed;
            }

            _voice.Start();
            _transcript.text = "listening...";
            _transcript.style.display = DisplayStyle.Flex;
            Refresh();
        }

        private void StopVoice()
        {
            _voice?.Stop();
            _voiceToggled = false;
            Refresh();
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

        private void OnVoiceTranscript(VoiceTranscript transcript) => _context.Dispatcher.Post(() => OnTranscript(transcript));

        private void OnTranscriptEvent(VoiceTranscript transcript)
        {
            if (_voice == null)
            {
                OnTranscript(transcript);
            }
        }

        private void OnVoiceLevel(float level) => _context.Dispatcher.Post(() => _level.style.width = Mathf.Clamp01(level) * 40f);

        private void OnVoiceFailed(Diagnostic diagnostic) => _context.Dispatcher.Post(() =>
        {
            _transcript.text = "Voice: " + diagnostic.Code + ": " + diagnostic.Message;
            _transcript.style.display = DisplayStyle.Flex;
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
            foreach (AgentAttachment attachment in _attachments)
            {
                AgentAttachment captured = attachment;
                Button chip = new Button(() =>
                {
                    _attachments.Remove(captured);
                    RebuildAttachments();
                })
                { text = attachment.Name + " x", tooltip = attachment.MediaType + ", " + attachment.Bytes + " bytes (click to remove)" };
                chip.AddToClassList("gcs-chip");
                _attachmentsRow.Add(chip);
            }
        }

        private static string LastHandleText(RequestHandle handle)
        {
            if (handle.Refusal != null)
            {
                return "Refused: " + handle.Refusal.Code + ": " + handle.Refusal.Message;
            }

            return "Sent " + handle.ChangeSetId + " (" + AgentRequestStates.Wire(handle.State) + ")";
        }

        private void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _context.VoiceTranscriptReceived += OnTranscriptEvent;
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
            _context.VoiceTranscriptReceived -= OnTranscriptEvent;
            _context.StatusChanged -= OnStatusChanged;
            _context.Selection.Changed -= Refresh;
            DetachVoice();
        }

        private void OnStatusChanged(ProviderStatus status) => Refresh();

        private void DetachVoice()
        {
            if (_voice != null)
            {
                if (_voice.IsActive)
                {
                    _voice.Stop();
                }

                _voice.Transcript -= OnVoiceTranscript;
                _voice.Level -= OnVoiceLevel;
                _voice.Failed -= OnVoiceFailed;
                _voice = null;
            }
        }
    }
}
