// GameCore.Studio.UI - how the Studio UI reads P2.2's agent gateway (GameCore.Studio.Authoring.Agent.IAgentGateway,
// docs/studio/04-etos-integration.md s2/s3): the tray's request states (the companion's RequestView states, never
// invented, plus the local Refused of a submission that was refused before a request existed), the connection summary
// the prompt bar derives from ProviderStatus, provider names, and the mapping of gateway exceptions to diagnostics with
// the etos code preserved.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.UI
{
    /// <summary>Request states shown in the task tray.</summary>
    public enum AgentRequestState
    {
        /// <summary>Companion <c>requested</c>: accepted, the etos task is queued or starting.</summary>
        Queued,
        Running,
        /// <summary>etos <c>waiting</c>; the row's waiting reason says why (budget, input, ...).</summary>
        Waiting,
        Candidate,
        /// <summary>The worker's change set failed validation; the companion may re-ask once.</summary>
        CandidateInvalid,
        NeedsClarification,
        /// <summary>Companion <c>failed</c>.</summary>
        TaskFailed,
        Cancelled,
        /// <summary>etos <c>unknown</c> / <c>outcome_unknown</c>; never a success.</summary>
        Unresolved,
        /// <summary>Local: the submission was refused (not configured, stale context, transport); no request exists.</summary>
        Refused,
    }

    /// <summary>Spellings and lifecycle predicates of <see cref="AgentRequestState"/>.</summary>
    public static class AgentRequestStates
    {
        public static string Wire(AgentRequestState state)
        {
            switch (state)
            {
                case AgentRequestState.Queued:
                    return "queued";
                case AgentRequestState.Running:
                    return "running";
                case AgentRequestState.Waiting:
                    return "waiting";
                case AgentRequestState.Candidate:
                    return "candidate";
                case AgentRequestState.CandidateInvalid:
                    return "candidate_invalid";
                case AgentRequestState.NeedsClarification:
                    return "needs_clarification";
                case AgentRequestState.TaskFailed:
                    return "task_failed";
                case AgentRequestState.Cancelled:
                    return "cancelled";
                case AgentRequestState.Unresolved:
                    return "unresolved";
                default:
                    return "refused";
            }
        }

        /// <summary>
        /// Parses the tray spelling or the companion RequestView spelling (<c>requested</c> = Queued, <c>failed</c> =
        /// TaskFailed); null for anything else (callers must not guess a state).
        /// </summary>
        public static AgentRequestState? Parse(string? wire)
        {
            switch (wire)
            {
                case "queued":
                case "requested":
                    return AgentRequestState.Queued;
                case "running":
                    return AgentRequestState.Running;
                case "waiting":
                    return AgentRequestState.Waiting;
                case "candidate":
                    return AgentRequestState.Candidate;
                case "candidate_invalid":
                    return AgentRequestState.CandidateInvalid;
                case "needs_clarification":
                    return AgentRequestState.NeedsClarification;
                case "task_failed":
                case "failed":
                    return AgentRequestState.TaskFailed;
                case "cancelled":
                    return AgentRequestState.Cancelled;
                case "unresolved":
                    return AgentRequestState.Unresolved;
                case "refused":
                    return AgentRequestState.Refused;
                default:
                    return null;
            }
        }

        /// <summary>True when the request can still change (cancel is meaningful).</summary>
        public static bool IsOpen(AgentRequestState state) =>
            state == AgentRequestState.Queued || state == AgentRequestState.Running || state == AgentRequestState.Waiting;
    }

    /// <summary>A request as the tray shows it, mapped from P2.2's <see cref="RequestView"/>.</summary>
    public sealed class AgentRequestInfo
    {
        public AgentRequestInfo(
            string requestId,
            string changeSetId,
            AgentRequestState state,
            string intentText,
            DateTime createdUtc,
            DateTime updatedUtc,
            string? worker = null,
            string? waitingReason = null,
            IReadOnlyList<string>? taskIds = null,
            IReadOnlyList<Diagnostic>? diagnostics = null,
            string? question = null,
            long sequence = 0,
            string? etosStatus = null,
            string? localState = null,
            string? progress = null)
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            ChangeSetId = changeSetId ?? throw new ArgumentNullException(nameof(changeSetId));
            State = state;
            IntentText = intentText ?? string.Empty;
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
            Worker = worker;
            WaitingReason = waitingReason;
            TaskIds = taskIds ?? Array.Empty<string>();
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
            Question = question;
            Sequence = sequence;
            EtosStatus = etosStatus;
            LocalState = localState;
            Progress = progress;
        }

        public string RequestId { get; }

        public string ChangeSetId { get; }

        public AgentRequestState State { get; }

        public string IntentText { get; }

        public DateTime CreatedUtc { get; }

        public DateTime UpdatedUtc { get; }

        public string? Worker { get; }

        public string? WaitingReason { get; }

        public IReadOnlyList<string> TaskIds { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public string? Question { get; }

        public long Sequence { get; }

        /// <summary>The etos task status, verbatim.</summary>
        public string? EtosStatus { get; }

        /// <summary>The Studio-side import state the gateway reports (importing, staged, stage_refused, import_failed, ...).</summary>
        public string? LocalState { get; }

        public string? Progress { get; }

        /// <summary>
        /// Maps a gateway view. A local failure before a request existed (<c>submit_failed</c>) is Refused; a state the
        /// UI does not know is shown as Unresolved (never as success).
        /// </summary>
        public static AgentRequestInfo From(RequestView view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            AgentRequestState state = view.LocalState == "submit_failed" ? AgentRequestState.Refused : AgentRequestStates.Parse(view.State) ?? AgentRequestState.Unresolved;
            JObject? outcome = view.Outcome;
            List<Diagnostic> diagnostics = new List<Diagnostic>(view.Diagnostics);
            string? code = (string?)outcome?["code"];
            string? message = (string?)outcome?["message"];
            if (code != null && code != "candidate" && code != "waiting" && code != "needs_clarification" && message != null && !diagnostics.Exists(d => d.Code == code))
            {
                diagnostics.Add(new Diagnostic(code, message, (string?)outcome?["hint"]));
            }

            string? question = state == AgentRequestState.NeedsClarification ? (string?)outcome?["text"] ?? message ?? QuestionOf(view.Progress) : null;
            string? waiting = state == AgentRequestState.Waiting ? message ?? view.Progress : null;
            List<string> tasks = new List<string>(view.Tasks);
            if (view.TaskId != null && !tasks.Contains(view.TaskId))
            {
                tasks.Add(view.TaskId);
            }

            DateTime now = DateTime.UtcNow;
            return new AgentRequestInfo(
                view.RequestId,
                view.ChangeSetId,
                state,
                view.Intent ?? string.Empty,
                Time(view.CreatedAt, now),
                Time(view.UpdatedAt, now),
                view.Worker,
                waiting,
                tasks,
                diagnostics,
                question,
                view.Seq,
                view.TaskStatus,
                view.LocalState,
                view.Progress);
        }

        /// <summary>Companion times are Unix milliseconds (seconds are accepted too); 0 means unknown (now).</summary>
        public static DateTime Time(long unix, DateTime fallback)
        {
            if (unix <= 0)
            {
                return fallback;
            }

            try
            {
                return unix > 100_000_000_000L ? DateTimeOffset.FromUnixTimeMilliseconds(unix).UtcDateTime : DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return fallback;
            }
        }

        private static string? QuestionOf(string? progress)
        {
            const string Prefix = "question: ";
            return progress != null && progress.StartsWith(Prefix, StringComparison.Ordinal) ? progress.Substring(Prefix.Length) : null;
        }
    }

    /// <summary>The connection summary the prompt bar and settings show.</summary>
    public enum GatewayConnection
    {
        /// <summary>No gateway is registered, or no key/companion is configured.</summary>
        NotConfigured,
        /// <summary>Not checked yet.</summary>
        Connecting,
        /// <summary>The companion does not answer (transport).</summary>
        Disconnected,
        /// <summary>The node answered and refused (key, grant).</summary>
        Refused,
        /// <summary>The node answered; the companion's agent is not connected yet.</summary>
        AgentStarting,
        Connected,
    }

    /// <summary>Provider names and connection derivation over P2.2's <see cref="ProviderStatus"/>.</summary>
    public static class ProviderNames
    {
        public const string Image = "image";
        public const string Tts = "tts";
        public const string Voice = "voice";
        public const string ThreeD = "3d";
        public const string Describe = "describe";

        public static readonly IReadOnlyList<string> All = new[] { Image, Tts, Voice, ThreeD, Describe };

        /// <summary>Codes that mean the node answered and refused this app.</summary>
        private static readonly IReadOnlyList<string> RefusalCodes = new[] { "unauthorized", "forbidden", "agent_unknown", "blocked", "invalid_key", "app_unknown" };

        public static string Wire(ProviderState state)
        {
            switch (state)
            {
                case ProviderState.Live:
                    return "live";
                case ProviderState.NotConfigured:
                    return "not_configured";
                case ProviderState.Blocked:
                    return "blocked";
                default:
                    return "unknown";
            }
        }

        public static GatewayConnection ConnectionOf(ProviderStatus status)
        {
            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }

            string? code = status.Problem?.Code;
            if (status.AgentReady)
            {
                return GatewayConnection.Connected;
            }

            if (code == "not_configured")
            {
                return GatewayConnection.NotConfigured;
            }

            if (!status.NodeReachable)
            {
                return status.CheckedAtUtc == null && status.Problem == null ? GatewayConnection.Connecting : GatewayConnection.Disconnected;
            }

            foreach (string refusal in RefusalCodes)
            {
                if (refusal == code)
                {
                    return GatewayConnection.Refused;
                }
            }

            return GatewayConnection.AgentStarting;
        }
    }

    /// <summary>Raised by gateways that refuse with a Studio diagnostic (the null gateway, the test gateway).</summary>
    public sealed class StudioGatewayException : Exception
    {
        public StudioGatewayException(Diagnostic diagnostic)
            : base(diagnostic?.Code + ": " + diagnostic?.Message)
        {
            Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        }

        public Diagnostic Diagnostic { get; }
    }

    /// <summary>Gateway exceptions as diagnostics, etos codes preserved.</summary>
    public static class GatewayErrors
    {
        /// <summary>
        /// The diagnostic of a failed gateway call: a <see cref="StudioGatewayException"/>'s own; for P2.2's etos
        /// exceptions (this package does not reference com.gamecore.studio.etos) the public <c>Code</c>,
        /// <c>Error.Message</c> and <c>Error.Hint</c>; anything else is <c>transport</c>.
        /// </summary>
        public static Diagnostic ToDiagnostic(Exception error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            if (error is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
            {
                error = aggregate.InnerExceptions[0];
            }

            if (error is StudioGatewayException studio)
            {
                return studio.Diagnostic;
            }

            PropertyInfo? codeProperty = error.GetType().GetProperty("Code", BindingFlags.Public | BindingFlags.Instance);
            if (codeProperty != null && codeProperty.PropertyType == typeof(string) && codeProperty.GetValue(error) is string code && code.Length > 0)
            {
                object? inner = error.GetType().GetProperty("Error", BindingFlags.Public | BindingFlags.Instance)?.GetValue(error);
                string? hint = inner?.GetType().GetProperty("Hint", BindingFlags.Public | BindingFlags.Instance)?.GetValue(inner) as string;
                string? message = inner?.GetType().GetProperty("Message", BindingFlags.Public | BindingFlags.Instance)?.GetValue(inner) as string;
                return new Diagnostic(code, message ?? error.Message, hint);
            }

            return new Diagnostic("transport", error.Message);
        }
    }
}
