// GameCore.Studio.Edit - diagnostic helpers (docs/studio/03-authoring-contracts.md s9).
// Every refusal is a GameCore.Studio.Model.Diagnostic with a code registered in DiagnosticCodes. A plugin validator
// that raises an unregistered code is reported as ValidationFailed with the original code kept in the message.
#nullable enable
using GameCore.Studio.Model;
using GameCore.Studio.Authoring;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    public static class StudioDiagnostics
    {
        /// <summary>The code itself when registered, else <see cref="DiagnosticCodes.ValidationFailed"/>.</summary>
        public static string Registered(string? code)
        {
            return DiagnosticCodes.IsRegistered(code) ? code! : DiagnosticCodes.ValidationFailed;
        }

        /// <summary>A diagnostic with a registered code (an unregistered one is wrapped, 03 s9).</summary>
        public static Diagnostic Normalize(Diagnostic diagnostic)
        {
            if (DiagnosticCodes.IsRegistered(diagnostic.Code))
            {
                SecretRedactor redactor = new SecretRedactor();
                return new Diagnostic(diagnostic.Code, redactor.Redact(diagnostic.Message), diagnostic.Hint == null ? null : redactor.Redact(diagnostic.Hint),
                    diagnostic.Where, diagnostic.Data == null ? null : (JObject)redactor.RedactJson(diagnostic.Data));
            }

            return new Diagnostic(DiagnosticCodes.ValidationFailed, "[" + diagnostic.Code + "] " + diagnostic.Message, diagnostic.Hint, diagnostic.Where);
        }

        public static Diagnostic Op(string code, string opId, string message, string? hint = null) =>
            Normalize(Diagnostic.AtOperation(Registered(code), opId, message, hint));

        public static Diagnostic At(string code, AuthoringRef where, string message, string? hint = null) =>
            Normalize(Diagnostic.AtRef(Registered(code), where, message, hint));

        public static Diagnostic General(string code, string message, string? hint = null) =>
            Normalize(new Diagnostic(Registered(code), message, hint));
    }
}
