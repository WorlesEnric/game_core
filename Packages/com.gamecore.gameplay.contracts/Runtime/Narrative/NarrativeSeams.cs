// GameCore.Gameplay.Contracts.Narrative - the narrative plugins' own seams (P1.4); the interaction seams are P1.3's.
//
// P1.3's contracts (GameCore.Gameplay.Contracts) declare IConversationStarter, IConditionEvaluator, IActionRunner and
// IFeedbackSink; the dialogue package implements the first, the logic package the next two, and playAudio actions go to
// an IFeedbackSink. What only the narrative plugins need lives here:
//
//   EvaluationContext      the evaluator's and runner's richer context (actor, subject key/id/target);
//                          FromInteraction converts P1.3's InteractionContext
//   INarrativeMessageSink  messages of logic actions (showMessage) - P1.5 shows them; the null sink records them
//   IVoiceLinePlayer       voice-line requests of the dialogue presenter - P1.5 plays them
//   IExplainSource         the logic package's explain trace (last evaluations and why they failed) for Studio
//   IMediaGenerationGateway  dialogue.generateVoice delegates to it; the default answers NotConfigured until P2.2
//
// Every seam has a null object that is the default.
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

        /// <summary>The context of one of P1.3's interactions: the interactable as subject, its actor key as actor.</summary>
        public static EvaluationContext FromInteraction(InteractionContext? context)
        {
            if (context == null)
            {
                return None;
            }

            string id = context.TargetAuthoringId;
            bool valid = AuthoringIds.IsValid(id);
            int key = context.TargetKey != 0 ? context.TargetKey : (valid ? AuthoringIds.StableKey(id) : 0);
            return new EvaluationContext(context.ActorKey, key, id, default(TargetId), valid ? AuthoringIds.TargetIdFor(id) : default(TargetId));
        }

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
