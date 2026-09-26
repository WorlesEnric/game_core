// GameCore.ReferenceConformance — the deterministic document the genre/assembly audit writes (P-060).
//
// `artifacts/gc-024/genre-audit.json` is GC-024's final genre audit. It is written by hand rather than by a
// serializer for the same reason every other GameCore recording is (04 s8 forbids reflection-driven construction):
// the field order is fixed, the ordering of every list is canonical, and no path, clock or machine value enters it,
// so the same tree produces the same bytes and a reviewer can diff two revisions directly.
//
// The document carries the declarations it read, the two directions of the rule it checked, the genre tokens it
// found in kernel sources, and the falsifiability counts — a scan that read nothing cannot look clean because
// `clean` requires every count to be positive.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.ReferenceConformance
{
    /// <summary>Writes and re-reads the genre/assembly audit document.</summary>
    public static class GenreAuditDocument
    {
        /// <summary>The document's format name; a reader refuses anything else (P-054).</summary>
        public const string FormatName = "gamecore.genre-audit/1";

        /// <summary>The artifact path the document belongs at, relative to the repository root.</summary>
        public const string ArtifactPath = "artifacts/gc-024/genre-audit.json";

        /// <summary>The task that owns this document.</summary>
        public const string TaskId = "GC-024";

        /// <summary>Serializes one audit report as the canonical document.</summary>
        public static string Write(GenreAuditReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var text = new StringBuilder();
            text.Append("{\n");
            text.Append("  \"format\": \"").Append(FormatName).Append("\",\n");
            text.Append("  \"task\": \"").Append(TaskId).Append("\",\n");
            text.Append("  \"requirement\": \"P-001, P-057, P-059; 04 s2 (assembly dependency direction)\",\n");
            text.Append("  \"rule\": \"no kernel assembly references a gameplay, rules, qualification or generated"
                + " assembly, no dotnet kernel project references one, no kernel source names a genre type; and every"
                + " gameplay/rules assembly references the kernel\",\n");

            text.Append("  \"repositoryRootGiven\": ").Append(ConformanceValue.Bool(report.RepositoryRoot.Length != 0))
                .Append(",\n");
            text.Append("  \"clean\": ").Append(ConformanceValue.Bool(report.Clean)).Append(",\n");

            text.Append("  \"counts\": {\n");
            text.Append("    \"kernelAssemblies\": ")
                .Append(report.CountOf(AssemblyClass.Kernel).ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"familyAssemblies\": ")
                .Append(report.CountOf(AssemblyClass.Family).ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"generatedAssemblies\": ")
                .Append(report.CountOf(AssemblyClass.Generated).ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"qualificationAssemblies\": ")
                .Append(report.CountOf(AssemblyClass.Qualification).ToString(CultureInfo.InvariantCulture))
                .Append(",\n");
            text.Append("    \"asmdefs\": ").Append(report.AsmdefCount.ToString(CultureInfo.InvariantCulture))
                .Append(",\n");
            text.Append("    \"projectFiles\": ")
                .Append(report.ProjectFileCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"kernelSourcesScanned\": ")
                .Append(report.KernelSourceCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"referenceAssertions\": ")
                .Append(report.ReferenceAssertionCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"violations\": ")
                .Append(report.Violations.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"kernelGenreTokens\": ")
                .Append(report.KernelTokens.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"missingPackages\": ")
                .Append(report.MissingPackages.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            text.Append("    \"familiesOnKernel\": ")
                .Append(report.FamiliesOnKernel().Count.ToString(CultureInfo.InvariantCulture)).Append("\n");
            text.Append("  },\n");

            text.Append("  \"kernelAssemblies\": ");
            WriteStrings(text, report.KernelAssemblies(), 2);
            text.Append(",\n");

            text.Append("  \"familyAssemblies\": ");
            WriteStrings(text, report.FamilyAssemblies(), 2);
            text.Append(",\n");

            text.Append("  \"familiesReferencingKernel\": ");
            WriteStrings(text, report.FamiliesOnKernel(), 2);
            text.Append(",\n");

            text.Append("  \"assemblies\": [\n");
            for (int i = 0; i < report.Assemblies.Count; i++)
            {
                AssemblyRecord record = report.Assemblies[i];
                text.Append("    {\n");
                text.Append("      \"name\": \"").Append(Escape(record.Name)).Append("\",\n");
                text.Append("      \"path\": \"").Append(Escape(record.Path)).Append("\",\n");
                text.Append("      \"kind\": \"")
                    .Append(record.Kind == ReferenceKind.Asmdef ? "asmdef" : "csproj").Append("\",\n");
                text.Append("      \"classification\": \"").Append(ClassToken(record.Classification)).Append("\",\n");
                text.Append("      \"packageRoot\": \"").Append(Escape(record.PackageRoot)).Append("\",\n");
                text.Append("      \"noEngineReferences\": ")
                    .Append(ConformanceValue.Bool(record.NoEngineReferences)).Append(",\n");
                text.Append("      \"references\": ");
                WriteStrings(text, record.References, 3);
                text.Append("\n    }");
                if (i != report.Assemblies.Count - 1)
                {
                    text.Append(',');
                }

                text.Append('\n');
            }

            text.Append("  ],\n");

            text.Append("  \"violations\": [\n");
            for (int i = 0; i < report.Violations.Count; i++)
            {
                AssemblyViolation violation = report.Violations[i];
                text.Append("    {\"assembly\": \"").Append(Escape(violation.Assembly))
                    .Append("\", \"reference\": \"").Append(Escape(violation.Reference))
                    .Append("\", \"rule\": \"").Append(Escape(violation.Rule))
                    .Append("\", \"path\": \"").Append(Escape(violation.Path)).Append("\"}");
                if (i != report.Violations.Count - 1)
                {
                    text.Append(',');
                }

                text.Append('\n');
            }

            text.Append("  ],\n");

            text.Append("  \"kernelGenreTokens\": [\n");
            for (int i = 0; i < report.KernelTokens.Count; i++)
            {
                GenreTokenFinding finding = report.KernelTokens[i];
                text.Append("    {\"path\": \"").Append(Escape(finding.Path))
                    .Append("\", \"line\": ").Append(finding.Line.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"token\": \"").Append(Escape(finding.Token)).Append("\"}");
                if (i != report.KernelTokens.Count - 1)
                {
                    text.Append(',');
                }

                text.Append('\n');
            }

            text.Append("  ],\n");

            text.Append("  \"missingPackages\": ");
            WriteStrings(text, report.MissingPackages, 2);
            text.Append(",\n");

            text.Append("  \"forbiddenKernelReferencePrefixes\": ");
            WriteStrings(text, AssemblyReferenceAudit.ForbiddenPrefixes, 2);
            text.Append(",\n");

            text.Append("  \"forbiddenKernelTokens\": ");
            WriteStrings(text, AssemblyReferenceAudit.ForbiddenKernelTokens, 2);
            text.Append(",\n");

            text.Append("  \"verdict\": \"").Append(Escape(report.Describe())).Append("\"\n");
            text.Append("}\n");
            return text.ToString();
        }

        /// <summary>
        /// The counts the document must carry, read back out of it. The player probe and the EditMode suite use this
        /// so an artifact cannot be claimed without its own counts agreeing (P-060).
        /// </summary>
        public static bool TryReadCounts(
            string? document,
            out bool clean,
            out int violations,
            out int kernelSources,
            out int referenceAssertions,
            out string detail)
        {
            clean = false;
            violations = 0;
            kernelSources = 0;
            referenceAssertions = 0;
            if (document == null)
            {
                detail = "no document was supplied";
                return false;
            }

            if (document.IndexOf("\"format\": \"" + FormatName + "\"", StringComparison.Ordinal) < 0)
            {
                detail = "the document does not declare format " + FormatName;
                return false;
            }

            if (!TryReadBool(document, "clean", out clean))
            {
                detail = "the document carries no boolean `clean`";
                return false;
            }

            if (!TryReadCount(document, "violations", out violations)
                || !TryReadCount(document, "kernelSourcesScanned", out kernelSources)
                || !TryReadCount(document, "referenceAssertions", out referenceAssertions))
            {
                detail = "the document's count block is incomplete";
                return false;
            }

            detail = "clean=" + ConformanceValue.Bool(clean)
                + "; violations=" + violations.ToString(CultureInfo.InvariantCulture)
                + "; kernelSources=" + kernelSources.ToString(CultureInfo.InvariantCulture)
                + "; referenceAssertions=" + referenceAssertions.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static void WriteStrings(StringBuilder text, IReadOnlyList<string> values, int indent)
        {
            if (values == null || values.Count == 0)
            {
                text.Append("[]");
                return;
            }

            string pad = new string(' ', indent * 2);
            string inner = new string(' ', (indent + 1) * 2);
            text.Append("[\n");
            for (int i = 0; i < values.Count; i++)
            {
                text.Append(inner).Append('"').Append(Escape(values[i])).Append('"');
                if (i != values.Count - 1)
                {
                    text.Append(',');
                }

                text.Append('\n');
            }

            text.Append(pad).Append(']');
        }

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

        private static string ClassToken(AssemblyClass classification)
        {
            switch (classification)
            {
                case AssemblyClass.Kernel:
                    return "kernel";
                case AssemblyClass.Family:
                    return "family";
                case AssemblyClass.Generated:
                    return "generated";
                default:
                    return "qualification";
            }
        }

        private static bool TryReadBool(string document, string key, out bool value)
        {
            value = false;
            string needle = "\"" + key + "\":";
            int index = document.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            string rest = document.Substring(index + needle.Length);
            if (rest.StartsWith("true", StringComparison.Ordinal))
            {
                value = true;
                return true;
            }

            if (rest.StartsWith("false", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static bool TryReadCount(string document, string key, out int value)
        {
            value = 0;
            string needle = "\"" + key + "\":";
            int index = document.IndexOf(needle, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            int start = index + needle.Length;
            int end = start;
            while (end < document.Length && document[end] >= '0' && document[end] <= '9')
            {
                end++;
            }

            if (end == start)
            {
                return false;
            }

            return int.TryParse(
                document.Substring(start, end - start),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
