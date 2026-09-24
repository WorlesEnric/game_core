#nullable enable
using System.Collections.Generic;
using System.Text;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One probe step outcome. Status is <c>Pass</c>, <c>Fail</c> or <c>ExpectedNegative</c>.</summary>
    public sealed class ProbeOutcome
    {
        private ProbeOutcome(string name, string status, string detail)
        {
            Name = name;
            Status = status;
            Detail = detail;
        }

        public string Name { get; }

        public string Status { get; }

        public string Detail { get; }

        public static ProbeOutcome Pass(string name, string detail) => new ProbeOutcome(name, "Pass", detail);

        public static ProbeOutcome Fail(string name, string detail) => new ProbeOutcome(name, "Fail", detail);

        public static ProbeOutcome ExpectedNegative(string name, string detail)
            => new ProbeOutcome(name, "ExpectedNegative", detail);
    }

    /// <summary>
    /// Structured probe result written to the path given by <c>-probeResult</c>. Field order is fixed so the
    /// artifact is diffable between runs.
    /// </summary>
    public sealed class ProbeReport
    {
        private readonly List<ProbeOutcome> outcomes = new List<ProbeOutcome>();

        public ProbeReport(string mode, string declaredUnityVersion, string declaredTarget)
        {
            Mode = mode;
            DeclaredUnityVersion = declaredUnityVersion;
            DeclaredTarget = declaredTarget;
        }

        public string Mode { get; }

        public string DeclaredUnityVersion { get; }

        public string DeclaredTarget { get; }

        public string Result { get; private set; } = "Fail";

        public int ExitCode { get; private set; } = 1;

        public string FailureReason { get; private set; } = string.Empty;

        public IReadOnlyList<ProbeOutcome> Outcomes => outcomes;

        public void Add(ProbeOutcome outcome) => outcomes.Add(outcome);

        public bool HasStatus(string status)
        {
            for (int i = 0; i < outcomes.Count; i++)
            {
                if (outcomes[i].Status == status)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Positive mode: every step must pass; exit code 0 is reserved for that case.</summary>
        public void CompletePositive()
        {
            if (outcomes.Count == 0)
            {
                Set("Fail", 1, "no probe steps executed");
                return;
            }

            if (HasStatus("Fail") || HasStatus("ExpectedNegative"))
            {
                Set("Fail", 1, "at least one probe step did not pass");
                return;
            }

            Set("Pass", 0, string.Empty);
        }

        /// <summary>
        /// Negative mode: the absent registration must be reported as <c>ExpectedNegative</c> and nothing may
        /// fail. Success uses exit code 3, distinct from both the positive success (0) and any failure (1).
        /// </summary>
        public void CompleteNegative()
        {
            if (HasStatus("Fail"))
            {
                Set("Fail", 1, "a registration absent from the generated catalog was resolved or a control step failed");
                return;
            }

            if (!HasStatus("ExpectedNegative"))
            {
                Set("Fail", 1, "the missing-registration step produced no explicit negative result");
                return;
            }

            Set("ExpectedNegative", 3, string.Empty);
        }

        public void FailUnexpectedly(string reason) => Set("Fail", 1, reason);

        private void Set(string result, int exitCode, string failureReason)
        {
            Result = result;
            ExitCode = exitCode;
            FailureReason = failureReason;
        }

        /// <summary>Serializes the report. Deterministic ordering; no timestamps or machine paths are embedded.</summary>
        public string ToJson()
        {
            var builder = new StringBuilder();
            builder.Append("{\n");
            AppendString(builder, 1, "task", "GC-001");
            AppendString(builder, 1, "probe", "GameCore.Validation.ProbeHost.ProbeRunner");
            AppendString(builder, 1, "mode", Mode);
            AppendString(builder, 1, "result", Result);
            AppendInt(builder, 1, "exitCode", ExitCode);
            AppendString(builder, 1, "declaredUnityVersion", DeclaredUnityVersion);
            AppendString(builder, 1, "unityVersion", ProbeEnvironment.UnityVersion);
            AppendString(builder, 1, "declaredTarget", DeclaredTarget);
            AppendString(builder, 1, "platform", ProbeEnvironment.Platform);
            AppendString(builder, 1, "architecture", ProbeEnvironment.Architecture);
            AppendString(builder, 1, "processorType", ProbeEnvironment.ProcessorType);
            AppendString(builder, 1, "scriptingBackend", ProbeEnvironment.ScriptingBackend);
            AppendBool(builder, 1, "isIl2Cpp", ProbeEnvironment.IsIl2Cpp);
            AppendBool(builder, 1, "is64BitProcess", ProbeEnvironment.Is64BitProcess);
            AppendString(builder, 1, "managedStrippingLevel", ProbeEnvironment.ManagedStrippingLevel);
            AppendString(builder, 1, "managedStrippingLevelSource", ProbeEnvironment.ManagedStrippingLevelSource);
            AppendBool(builder, 1, "burstCompilerEnabled", ProbeEnvironment.BurstCompilerEnabled);
            AppendString(builder, 1, "fixturePluginPreservation", ProbeEnvironment.FixturePluginPreservation);
            AppendString(builder, 1, "catalogGeneratedFile", ProbeEnvironment.CatalogGeneratedFile);
            AppendString(builder, 1, "catalogFileHash", ProbeEnvironment.CatalogFileHash);
            AppendString(builder, 1, "catalogFileHashAlgorithm", ProbeEnvironment.CatalogFileHashAlgorithm);
            AppendString(builder, 1, "catalogFileHashScope", ProbeEnvironment.CatalogFileHashScope);
            AppendString(builder, 1, "failureReason", FailureReason);
            builder.Append("  \"probes\": [");
            for (int i = 0; i < outcomes.Count; i++)
            {
                builder.Append(i == 0 ? "\n" : ",\n");
                builder.Append("    {\n");
                AppendString(builder, 3, "name", outcomes[i].Name);
                AppendString(builder, 3, "status", outcomes[i].Status);
                AppendString(builder, 3, "detail", outcomes[i].Detail);
                builder.Append("    }");
            }

            builder.Append(outcomes.Count == 0 ? "]\n" : "\n  ]\n");
            builder.Append("}\n");
            return builder.ToString();
        }

        private static void AppendString(StringBuilder builder, int depth, string name, string value)
        {
            builder.Append(Indent(depth)).Append('"').Append(name).Append("\": \"").Append(Escape(value)).Append("\",\n");
        }

        private static void AppendInt(StringBuilder builder, int depth, string name, int value)
        {
            builder.Append(Indent(depth)).Append('"').Append(name).Append("\": ").Append(value).Append(",\n");
        }

        private static void AppendBool(StringBuilder builder, int depth, string name, bool value)
        {
            builder.Append(Indent(depth))
                .Append('"')
                .Append(name)
                .Append("\": ")
                .Append(value ? "true" : "false")
                .Append(",\n");
        }

        private static string Indent(int depth) => new string(' ', depth * 2);

        private static string Escape(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
