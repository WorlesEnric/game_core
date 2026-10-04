// GameCore.Studio.Authoring - Studio logging with a redaction hook (docs/studio/02-architecture.md SADR-018).
// Every Studio component logs through IStudioLog; the redactor runs on every message before it reaches a sink, so
// keys such as etos app keys (etk_ prefix) never appear in Editor logs.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

        private static readonly Regex EtosKey = new Regex(@"etk_[A-Za-z0-9_\-]+", RegexOptions.CultureInvariant);

        private static readonly Regex Bearer = new Regex(@"(?i)(bearer\s+)[A-Za-z0-9._\-~+/=]+", RegexOptions.CultureInvariant);

        private static readonly Regex QueryValue = new Regex(@"(?i)\b(key|token|secret|password)=([^&\s""']+)", RegexOptions.CultureInvariant);

        public string Redact(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            string result = EtosKey.Replace(text, "etk_" + Mask);
            result = Bearer.Replace(result, "$1" + Mask);
            result = QueryValue.Replace(result, "$1=" + Mask);
            return result;
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
                : new Diagnostic(diagnostic.Code, Redactor.Redact(diagnostic.Message), diagnostic.Hint == null ? null : Redactor.Redact(diagnostic.Hint), diagnostic.Where);
            StudioLogEntry entry = new StudioLogEntry(level, category ?? "studio", Redactor.Redact(message ?? string.Empty), redactedDiagnostic);
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
