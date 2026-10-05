// GameCore.Studio.Authoring - Studio logging with a redaction hook (docs/studio/02-architecture.md SADR-018).
// Every Studio component logs through IStudioLog; the redactor runs on every message before it reaches a sink, so
// keys such as etos app keys (etk_ prefix) never appear in Editor logs.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.Authoring
{
    public enum StudioLogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    /// <summary>One log record (already redacted).</summary>
    public sealed class StudioLogEntry
    {
        public StudioLogEntry(StudioLogLevel level, string category, string message, Diagnostic? diagnostic)
        {
            Level = level;
            Category = category;
            Message = message;
            Diagnostic = diagnostic;
        }

        public StudioLogLevel Level { get; }

        public string Category { get; }

        public string Message { get; }

        public Diagnostic? Diagnostic { get; }

        public override string ToString() =>
            "[GameCore.Studio/" + Category + "] " + Message + (Diagnostic == null ? string.Empty : " (" + Diagnostic + ")");
    }

    /// <summary>The Studio's log sink.</summary>
    public interface IStudioLog
    {
        void Write(StudioLogLevel level, string category, string message, Diagnostic? diagnostic = null);
    }

    /// <summary>Removes secrets from text before it is logged.</summary>
    public interface ILogRedactor
    {
        string Redact(string text);
    }

    /// <summary>
    /// Default redactor: etos keys (<c>etk_</c> + token), bearer tokens and <c>key=</c>/<c>token=</c> query values.
    /// </summary>
    public sealed class SecretRedactor : ILogRedactor
    {
        public const string Mask = "[redacted]";

        private static readonly Regex EtosKey = new Regex(@"(?:et[kpta]_|sk-)[A-Za-z0-9_\-]+", RegexOptions.CultureInvariant);

        private static readonly Regex Bearer = new Regex(@"(?i)(bearer\s+)[A-Za-z0-9._\-~+/=]+", RegexOptions.CultureInvariant);

        private static readonly Regex JsonSecret = new Regex(@"(?i)(""[^""\r\n]*(?:key|token|secret|password)[^""\r\n]*""\s*:\s*)(""(?:\\.|[^""\\])*""|true|false|null|-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);

        private static readonly Regex QueryValue = new Regex(@"(?i)\b(key|token|secret|password)=([^&\s""']+)", RegexOptions.CultureInvariant);

        public string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            string trimmed = text.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                try { return RedactJson(JToken.Parse(text)).ToString(Formatting.None); }
                catch (JsonException) { }
            }
            return RedactText(text);
        }

        private string RedactText(string text)
        {
            string result = EtosKey.Replace(text, Mask);
            result = Bearer.Replace(result, "$1" + Mask);
            result = QueryValue.Replace(result, "$1=" + Mask);
            result = JsonSecret.Replace(result, "$1\"" + Mask + "\"");
            return result;
        }
        /// <summary>Returns a sanitized deep copy, including nested JSON secret-key values.</summary>
        public JToken RedactJson(JToken value)
        {
            if (value is JObject obj)
            {
                JObject copy = new JObject();
                foreach (JProperty property in obj.Properties())
                    copy[RedactText(property.Name)] = Regex.IsMatch(property.Name, "key|token|secret|password", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                        ? new JValue(Mask) : RedactJson(property.Value);
                return copy;
            }
            if (value is JArray array)
            {
                JArray copy = new JArray();
                foreach (JToken item in array) copy.Add(RedactJson(item));
                return copy;
            }
            return value.Type == JTokenType.String ? new JValue(RedactText(value.Value<string>() ?? string.Empty)) : value.DeepClone();
        }
    }

    /// <summary>Line-buffered redaction before bytes reach a child-log or evidence sink, including split tokens.</summary>
    public sealed class RedactingTextWriter : TextWriter
    {
        private readonly TextWriter _sink;
        private readonly SecretRedactor _redactor = new SecretRedactor();
        private readonly StringBuilder _line = new StringBuilder();
        public RedactingTextWriter(TextWriter sink) { _sink = sink ?? throw new ArgumentNullException(nameof(sink)); }
        public override Encoding Encoding => _sink.Encoding;
        public override void Write(char value)
        {
            if (value == '\n') { _sink.WriteLine(_redactor.Redact(_line.ToString())); _line.Clear(); }
            else _line.Append(value);
        }
        public override void Write(string? value) { if (value != null) foreach (char character in value) Write(character); }
        public override void Flush() => _sink.Flush();
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (_line.Length > 0) _sink.Write(_redactor.Redact(_line.ToString())); _line.Clear(); _sink.Flush(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Base log: redacts, filters by level, keeps a bounded tail of recent entries.</summary>
    public abstract class StudioLogBase : IStudioLog
    {
        private readonly List<StudioLogEntry> _recent = new List<StudioLogEntry>();

        protected StudioLogBase(ILogRedactor? redactor, StudioLogLevel minimum, int capacity)
        {
            Redactor = redactor ?? new SecretRedactor();
            Minimum = minimum;
            Capacity = Math.Max(1, capacity);
        }

        public ILogRedactor Redactor { get; set; }

        public StudioLogLevel Minimum { get; set; }

        public int Capacity { get; }

        /// <summary>The most recent entries (oldest first), bounded by <see cref="Capacity"/>.</summary>
        public IReadOnlyList<StudioLogEntry> Recent => _recent;

        public void Write(StudioLogLevel level, string category, string message, Diagnostic? diagnostic = null)
        {
            if (level < Minimum)
            {
                return;
            }

            Diagnostic? redactedDiagnostic = diagnostic == null
                ? null
                : new Diagnostic(diagnostic.Code, Redactor.Redact(diagnostic.Message), diagnostic.Hint == null ? null : Redactor.Redact(diagnostic.Hint), diagnostic.Where, diagnostic.Data == null ? null : (JObject)new SecretRedactor().RedactJson(diagnostic.Data));
            StudioLogEntry entry = new StudioLogEntry(level, Redactor.Redact(category ?? "studio"), Redactor.Redact(message ?? string.Empty), redactedDiagnostic);
            _recent.Add(entry);
            if (_recent.Count > Capacity)
            {
                _recent.RemoveAt(0);
            }

            Emit(entry);
        }

        protected abstract void Emit(StudioLogEntry entry);
    }

    /// <summary>Writes to the Unity console.</summary>
    public sealed class UnityStudioLog : StudioLogBase
    {
        public UnityStudioLog(ILogRedactor? redactor = null, StudioLogLevel minimum = StudioLogLevel.Info, int capacity = 512)
            : base(redactor, minimum, capacity)
        {
        }

        protected override void Emit(StudioLogEntry entry)
        {
            switch (entry.Level)
            {
                case StudioLogLevel.Error:
                    UnityEngine.Debug.LogError(entry.ToString());
                    break;
                case StudioLogLevel.Warning:
                    UnityEngine.Debug.LogWarning(entry.ToString());
                    break;
                default:
                    UnityEngine.Debug.Log(entry.ToString());
                    break;
            }
        }
    }

    /// <summary>Keeps entries in memory only (tests, headless tools).</summary>
    public sealed class MemoryStudioLog : StudioLogBase
    {
        public MemoryStudioLog(ILogRedactor? redactor = null, StudioLogLevel minimum = StudioLogLevel.Debug, int capacity = 4096)
            : base(redactor, minimum, capacity)
        {
        }

        protected override void Emit(StudioLogEntry entry)
        {
        }
    }
}
