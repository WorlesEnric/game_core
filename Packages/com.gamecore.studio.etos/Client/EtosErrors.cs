// GameCore.Studio.Etos.Client - etos-shaped errors (04 s2): {code, message, hint?, diagnostics?} with the HTTP status.
// Codes pass through unchanged from the node and the companion (not_configured, budget_exhausted, agent_starting,
// stale_context, candidate_invalid, forbidden, too_large, ...). The client adds only transport-level codes for things
// it observes itself (transport, protocol, timeout) and the artifact verification rules it enforces before handing
// bytes over (artifact_digest_mismatch, artifact_size_mismatch), named like the companion's own rules.
#nullable enable
using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Error codes the client names (all others are passed through verbatim).</summary>
    public static class EtosCodes
    {
        public const string NotConfigured = "not_configured";
        public const string BudgetExhausted = "budget_exhausted";
        public const string AgentStarting = "agent_starting";
        public const string AgentUnknown = "agent_unknown";
        public const string StaleContext = "stale_context";
        public const string CandidateInvalid = "candidate_invalid";
        public const string LedgerConflict = "ledger_conflict";
        public const string StageFailed = "stage_failed";
        public const string Forbidden = "forbidden";
        public const string Unauthorized = "unauthorized";
        public const string NotAllowed = "not_allowed";
        public const string TooLarge = "too_large";
        public const string NotFound = "not_found";
        public const string BadRequest = "bad_request";
        public const string Internal = "internal";
        public const string OutcomeUnknown = "outcome_unknown";
        public const string RateLimited = "rate_limited";

        /// <summary>The node could not be reached, or the connection broke (client-side).</summary>
        public const string Transport = "transport";

        /// <summary>An answer did not have the documented shape (client-side or companion).</summary>
        public const string Protocol = "protocol";

        /// <summary>The client's own deadline expired (client-side).</summary>
        public const string Timeout = "timeout";

        /// <summary>The client has no key file, or it cannot be read (client-side).</summary>
        public const string NoCredentials = "not_configured";

        /// <summary>Downloaded bytes do not hash to the requested sha256 (client-side, before they are handed over).</summary>
        public const string ArtifactDigestMismatch = "artifact_digest_mismatch";

        /// <summary>Downloaded bytes do not have the declared size (client-side, before they are handed over).</summary>
        public const string ArtifactSizeMismatch = "artifact_size_mismatch";

        /// <summary>True for codes that mean a provider or capability is blocked (grant, budget, credential).</summary>
        public static bool IsBlocked(string? code)
        {
            return code == Forbidden || code == "not_granted" || code == NotAllowed || code == BudgetExhausted || code == Unauthorized;
        }
    }

    /// <summary>One etos-shaped error.</summary>
    public sealed class EtosError
    {
        public EtosError(int status, string code, string message, string? hint = null, JArray? diagnostics = null)
        {
            Status = status;
            Code = string.IsNullOrEmpty(code) ? EtosCodes.Protocol : code;
            Message = EtosRedaction.Redact(message ?? string.Empty);
            Hint = hint == null ? null : EtosRedaction.Redact(hint);
            Diagnostics = diagnostics;
        }

        /// <summary>HTTP status (0 when no HTTP answer was received).</summary>
        public int Status { get; }

        public string Code { get; }

        /// <summary>Redacted message.</summary>
        public string Message { get; }

        /// <summary>Redacted hint.</summary>
        public string? Hint { get; }

        /// <summary>03 s9 diagnostics the companion itemised (contract-check findings), verbatim.</summary>
        public JArray? Diagnostics { get; }

        /// <summary>Reads an error body; anything that is not one becomes a code derived from the status.</summary>
        public static EtosError FromBody(int status, string? body)
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    JToken token = JToken.Parse(body!);
                    if (token is JObject obj)
                    {
                        JObject inner = obj["error"] as JObject ?? obj;
                        string? code = Json.Str(inner, "code");
                        if (!string.IsNullOrEmpty(code))
                        {
                            return new EtosError(status, code!, Json.Str(inner, "message") ?? string.Empty, Json.Str(inner, "hint"), inner["diagnostics"] as JArray);
                        }
                    }
                }
                catch (Newtonsoft.Json.JsonException)
                {
                }
            }

            string text = body == null ? string.Empty : body.Trim();
            if (text.Length > 512)
            {
                text = text.Substring(0, 512);
            }

            return new EtosError(status, CodeForStatus(status), text.Length == 0 ? "HTTP " + status.ToString(CultureInfo.InvariantCulture) : text);
        }

        /// <summary>The code used when an answer carries no etos body.</summary>
        public static string CodeForStatus(int status)
        {
            switch (status)
            {
                case 400:
                    return EtosCodes.BadRequest;
                case 401:
                    return EtosCodes.Unauthorized;
                case 403:
                    return EtosCodes.Forbidden;
                case 404:
                    return EtosCodes.NotFound;
                case 409:
                    return EtosCodes.LedgerConflict;
                case 413:
                    return EtosCodes.TooLarge;
                case 429:
                    return EtosCodes.RateLimited;
                case 502:
                case 504:
                    return EtosCodes.Transport;
                default:
                    return status >= 500 ? EtosCodes.Internal : EtosCodes.Protocol;
            }
        }

        public override string ToString()
        {
            return Code + ": " + Message + (Hint == null ? string.Empty : " (" + Hint + ")") + (Status == 0 ? string.Empty : " [HTTP " + Status.ToString(CultureInfo.InvariantCulture) + "]");
        }
    }

    /// <summary>A refused or failed client call; <see cref="Error"/> keeps the etos code.</summary>
    public sealed class EtosException : Exception
    {
        public EtosException(EtosError error, Exception? inner = null)
            : base(error.ToString(), inner)
        {
            Error = error;
        }

        public EtosError Error { get; }

        public string Code => Error.Code;

        public static EtosException Transport(string message, Exception? inner = null)
        {
            return new EtosException(new EtosError(0, EtosCodes.Transport, message + (inner == null ? string.Empty : ": " + inner.Message), "Is etosd running and the node URL right? (systemctl --user status etosd)"), inner);
        }

        public static EtosException Protocol(string message, Exception? inner = null)
        {
            return new EtosException(new EtosError(0, EtosCodes.Protocol, message), inner);
        }
    }
}
