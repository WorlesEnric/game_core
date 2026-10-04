// GameCore.Gameplay.Contracts.Narrative - the seams between the narrative plugins (P1.4) and the packages around them.
//
//   IConversationStarter   implemented by the dialogue package; the NPC package (P1.3) starts a conversation with it
//   IConditionEvaluator    implemented by the logic package; interaction (P1.3) and dialogue evaluate condition refs
//   IActionRunner          implemented by the logic package; interaction (P1.3) runs action refs after a committed use
//   INarrativeFeedbackSink audio/VFX cues of logic actions (playAudio) - P1.5 renders them
//   INarrativeMessageSink  messages of logic actions (showMessage) - P1.5 shows them; the null sink records a diagnostic
//   IVoiceLinePlayer       voice-line requests of the dialogue presenter - P1.5 plays them
//   IExplainSource         the logic package's explain trace (last evaluations and why they failed) for Studio
//   IMediaGenerationGateway  dialogue.generateVoice delegates to it; the default answers NotConfigured until P2.2
//
// The first three keep the signatures of the P1.4 brief. P1.3 declares interfaces with the same simple names in
// GameCore.Gameplay.Contracts; these live in the Narrative namespace so both compile side by side, and the narrative
// packages always name them through a namespace alias. Every seam has a null object that is the default.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts.Narrative
{
    /// <summary>What a condition or action is evaluated about: the actor (usually the player) and the subject.</summary>
    public readonly struct EvaluationContext
    {
        public EvaluationContext(int actorKey, int subjectKey, string subjectAuthoringId, TargetId actor, TargetId subject)
        {
            ActorKey = actorKey;
            SubjectKey = subjectKey;
            SubjectAuthoringId = subjectAuthoringId ?? string.Empty;
            Actor = actor;
            Subject = subject;
        }

        /// <summary>Stable key of the actor's entity (AuthoringIds.StableKey), or 0 for "the player".</summary>
        public int ActorKey { get; }

        /// <summary>Stable key of the subject (interactable, NPC, world item), or 0.</summary>
        public int SubjectKey { get; }

        /// <summary>Authoring id of the subject, when known.</summary>
        public string SubjectAuthoringId { get; }

        public TargetId Actor { get; }

        public TargetId Subject { get; }

        /// <summary>No actor and no subject: conditions about the player and world state only.</summary>
        public static EvaluationContext None => new EvaluationContext(0, 0, string.Empty, default(TargetId), default(TargetId));

        /// <summary>A context about one subject by authoring id (its key is derived), with the player as actor.</summary>
        public static EvaluationContext ForSubject(string subjectAuthoringId)
        {
            if (!AuthoringIds.IsValid(subjectAuthoringId))
            {
                return None;
            }

            return new EvaluationContext(
                0,
                AuthoringIds.StableKey(subjectAuthoringId),
                subjectAuthoringId,
                default(TargetId),
                AuthoringIds.TargetIdFor(subjectAuthoringId));
        }
    }

    /// <summary>Starts a conversation with an NPC. Implemented by the dialogue package.</summary>
    public interface IConversationStarter
    {
        /// <param name="npcAuthoringId">Authoring id of the NPC's placed entity (may be empty for a scripted speaker).</param>
        /// <param name="dialogueGraphRef">Authoring id (or asset name) of the dialogue graph.</param>
        bool TryStart(string npcAuthoringId, string dialogueGraphRef);
    }

    /// <summary>The default conversation starter: no dialogue system; nothing starts.</summary>
    public sealed class NullConversationStarter : IConversationStarter
    {
        public int Requests { get; private set; }

        public bool TryStart(string npcAuthoringId, string dialogueGraphRef)
        {
            Requests++;
            return false;
        }
    }

    /// <summary>
    /// Evaluates a condition reference over committed state: a ConditionSetDefinition (authoring id or name), or a fact
    /// shorthand <c>narrative.fact.&lt;name&gt;</c> (true when non-zero) / <c>narrative.fact.&lt;name&gt;&gt;=N</c>.
    /// </summary>
    public interface IConditionEvaluator
    {
        /// <summary>True when the conditions hold; otherwise false with the first failed condition described.</summary>
        bool Evaluate(string conditionSetRef, in EvaluationContext ctx, out string failedCondition);
    }

    /// <summary>The default evaluator: an empty reference holds, any other reference fails ("no logic system").</summary>
    public sealed class NullConditionEvaluator : IConditionEvaluator
    {
        public int Evaluations { get; private set; }

        public bool Evaluate(string conditionSetRef, in EvaluationContext ctx, out string failedCondition)
        {
            Evaluations++;
            if (string.IsNullOrEmpty(conditionSetRef))
            {
                failedCondition = string.Empty;
                return true;
            }

            failedCondition = "no logic system is installed to evaluate '" + conditionSetRef + "'";
            return false;
        }
    }

    /// <summary>Runs an action reference (an ActionSetDefinition, or a built-in action such as <c>inventory.pickup</c>).</summary>
    public interface IActionRunner
    {
        /// <summary>
        /// True when the run was handed to the world (a command was admitted); the actions take effect in later steps,
        /// exactly once, through the logic plugin's outbox.
        /// </summary>
        bool TryRun(string actionSetRef, in EvaluationContext ctx);
    }

    /// <summary>The default action runner: runs nothing.</summary>
    public sealed class NullActionRunner : IActionRunner
    {
        public int Runs { get; private set; }

        public bool TryRun(string actionSetRef, in EvaluationContext ctx)
        {
            Runs++;
            return false;
        }
    }

    /// <summary>One audio/VFX cue raised by a logic action (presentation only).</summary>
    public sealed class NarrativeFeedbackCue
    {
        public NarrativeFeedbackCue(string cue, string source, int subjectKey)
        {
            Cue = cue ?? string.Empty;
            Source = source ?? string.Empty;
            SubjectKey = subjectKey;
        }

        /// <summary>The cue id, e.g. <c>ambience.belfry.calm</c>.</summary>
        public string Cue { get; }

        /// <summary>What raised it, e.g. <c>rule:echo_freed_ambience</c>.</summary>
        public string Source { get; }

        public int SubjectKey { get; }
    }

    /// <summary>Receives the cues of playAudio actions (P1.5).</summary>
    public interface INarrativeFeedbackSink
    {
        void OnFeedback(NarrativeFeedbackCue cue);
    }

    /// <summary>The default feedback sink: keeps the cues it saw.</summary>
    public sealed class NullNarrativeFeedbackSink : INarrativeFeedbackSink
    {
        private readonly List<NarrativeFeedbackCue> cues = new List<NarrativeFeedbackCue>();

        public IReadOnlyList<NarrativeFeedbackCue> Cues => cues;

        public void OnFeedback(NarrativeFeedbackCue cue)
        {
            if (cue != null && cues.Count < 256)
            {
                cues.Add(cue);
            }
        }
    }

    /// <summary>One message raised by a showMessage action.</summary>
    public sealed class NarrativeMessage
    {
        public NarrativeMessage(string text, string source)
        {
            Text = text ?? string.Empty;
            Source = source ?? string.Empty;
        }

        public string Text { get; }

        public string Source { get; }
    }

    /// <summary>Shows the messages of showMessage actions (P1.5).</summary>
    public interface INarrativeMessageSink
    {
        void Show(NarrativeMessage message);
    }

    /// <summary>The default message sink: records each message as a diagnostic line (no UI is installed).</summary>
    public sealed class NullNarrativeMessageSink : INarrativeMessageSink
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public void Show(NarrativeMessage message)
        {
            if (message != null && diagnostics.Count < 256)
            {
                diagnostics.Add("no message sink: [" + message.Source + "] " + message.Text);
            }
        }
    }

    /// <summary>A voice line the dialogue presenter wants played.</summary>
    public sealed class VoiceLineRequest
    {
        public VoiceLineRequest(int graphKey, int node, string speaker, string text, string clipRef, object? clip)
        {
            GraphKey = graphKey;
            Node = node;
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
            ClipRef = clipRef ?? string.Empty;
            Clip = clip;
        }

        public int GraphKey { get; }

        public int Node { get; }

        public string Speaker { get; }

        public string Text { get; }

        /// <summary>Asset reference of the clip (asset GUID), empty when the line has none.</summary>
        public string ClipRef { get; }

        /// <summary>The clip asset itself (an AudioClip in Unity), or null.</summary>
        public object? Clip { get; }
    }

    /// <summary>Plays voice lines (P1.5); a new request interrupts the previous one.</summary>
    public interface IVoiceLinePlayer
    {
        void Play(VoiceLineRequest request);

        void Stop(string reason);
    }

    /// <summary>The default voice player: keeps the requests it saw.</summary>
    public sealed class NullVoiceLinePlayer : IVoiceLinePlayer
    {
        private readonly List<VoiceLineRequest> requests = new List<VoiceLineRequest>();

        public IReadOnlyList<VoiceLineRequest> Requests => requests;

        public int Stops { get; private set; }

        public void Play(VoiceLineRequest request)
        {
            if (request != null && requests.Count < 256)
            {
                requests.Add(request);
            }
        }

        public void Stop(string reason) => Stops++;
    }

    /// <summary>One logic evaluation as the explain trace recorded it.</summary>
    public sealed class ExplainRecord
    {
        public ExplainRecord(
            string ruleRef,
            string ruleName,
            long step,
            bool fired,
            string reason,
            int failedConditionIndex,
            string failedCondition,
            IReadOnlyList<string> inputs)
        {
            RuleRef = ruleRef ?? string.Empty;
            RuleName = ruleName ?? string.Empty;
            Step = step;
            Fired = fired;
            Reason = reason ?? string.Empty;
            FailedConditionIndex = failedConditionIndex;
            FailedCondition = failedCondition ?? string.Empty;
            Inputs = inputs ?? System.Array.Empty<string>();
        }

        /// <summary>Authoring id of the rule (or of the condition set for a direct evaluation).</summary>
        public string RuleRef { get; }

        public string RuleName { get; }

        /// <summary>Logical step of the evaluation (0 for a host-side evaluation).</summary>
        public long Step { get; }

        public bool Fired { get; }

        /// <summary><c>fired</c>, <c>condition</c>, <c>cooldown</c>, <c>once</c>, <c>maxFires</c> or <c>evaluated</c>.</summary>
        public string Reason { get; }

        /// <summary>Index of the first failed condition, or -1.</summary>
        public int FailedConditionIndex { get; }

        public string FailedCondition { get; }

        /// <summary>Every input the evaluation read, e.g. <c>fact gate_open = 0 (needs &gt;= 1)</c>.</summary>
        public IReadOnlyList<string> Inputs { get; }

        public override string ToString() =>
            RuleName + (Fired ? " fired" : " skipped (" + Reason + (FailedCondition.Length > 0 ? ": " + FailedCondition : string.Empty) + ")");
    }

    /// <summary>The last evaluations of the logic plugin and why they did or did not fire (Studio's inspect.explain).</summary>
    public interface IExplainSource
    {
        int Count { get; }

        /// <summary>Up to <paramref name="max"/> records, newest first.</summary>
        IReadOnlyList<ExplainRecord> Recent(int max);

        /// <summary>The newest record of one rule (authoring id or name).</summary>
        bool TryExplain(string ruleRef, out ExplainRecord? record);
    }

    /// <summary>The outcome of a media generation request.</summary>
    public enum MediaGenerationStatus
    {
        /// <summary>No generation gateway is configured (the default until the Studio etos client, P2.2).</summary>
        NotConfigured = 0,

        /// <summary>The request was handed to the gateway; the artifact arrives later as a candidate.</summary>
        Requested = 1,

        /// <summary>The gateway refused the request.</summary>
        Refused = 2,
    }

    /// <summary>A request to generate one voice line.</summary>
    public sealed class VoiceGenerationRequest
    {
        public VoiceGenerationRequest(string graphAuthoringId, int node, string speaker, string text, string voice)
        {
            GraphAuthoringId = graphAuthoringId ?? string.Empty;
            Node = node;
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
            Voice = voice ?? string.Empty;
        }

        public string GraphAuthoringId { get; }

        public int Node { get; }

        public string Speaker { get; }

        public string Text { get; }

        /// <summary>Voice id or description for the TTS provider; empty for the provider default.</summary>
        public string Voice { get; }
    }

    /// <summary>The answer of a media generation gateway.</summary>
    public sealed class MediaGenerationResult
    {
        public MediaGenerationResult(MediaGenerationStatus status, string requestId, string detail)
        {
            Status = status;
            RequestId = requestId ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public MediaGenerationStatus Status { get; }

        public string RequestId { get; }

        public string Detail { get; }
    }

    /// <summary>Generates media through Studio's agent tier (P2.2 implements it over the etos companion).</summary>
    public interface IMediaGenerationGateway
    {
        MediaGenerationResult RequestVoiceLine(VoiceGenerationRequest request);
    }

    /// <summary>The default gateway: every request answers NotConfigured.</summary>
    public sealed class NotConfiguredMediaGateway : IMediaGenerationGateway
    {
        public int Requests { get; private set; }

        public MediaGenerationResult RequestVoiceLine(VoiceGenerationRequest request)
        {
            Requests++;
            return new MediaGenerationResult(
                MediaGenerationStatus.NotConfigured,
                string.Empty,
                "no media generation gateway is configured (the Studio etos client, P2.2, provides one)");
        }
    }
}
