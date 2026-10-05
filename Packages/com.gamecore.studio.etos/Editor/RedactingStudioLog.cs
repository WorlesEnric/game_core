// GameCore.Studio.Etos - every etos line goes through the shared redaction (EtosRedaction: etk_/ett_/etp_/eta_ tokens,
// bearer values, tickets) before it reaches the Studio log, using the shared core policy (D9).
#nullable enable
using System;
using GameCore.Studio.Authoring;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;

namespace GameCore.Studio.Etos
{
    /// <summary>A log that redacts etos credentials, then forwards.</summary>
    public sealed class RedactingStudioLog : IStudioLog
    {
        private readonly IStudioLog _inner;

        public RedactingStudioLog(IStudioLog inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Write(StudioLogLevel level, string category, string message, Diagnostic? diagnostic = null)
        {
            Diagnostic? clean = diagnostic == null ? null : Redact(diagnostic);
            _inner.Write(level, EtosRedaction.Redact(category), EtosRedaction.Redact(message), clean);
        }

        /// <summary>The diagnostic with its message and hint redacted (structured data sanitized).</summary>
        public static Diagnostic Redact(Diagnostic diagnostic)
        {
            return new Diagnostic(EtosRedaction.Redact(diagnostic.Code), EtosRedaction.Redact(diagnostic.Message), diagnostic.Hint == null ? null : EtosRedaction.Redact(diagnostic.Hint), diagnostic.Where, diagnostic.Data == null ? null : (Newtonsoft.Json.Linq.JObject)EtosRedaction.RedactJson(diagnostic.Data));
        }
    }
}
