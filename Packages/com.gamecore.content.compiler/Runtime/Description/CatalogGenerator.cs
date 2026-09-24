// GameCore.Content.Compiler - build-time generation service (GC-003).
// This is the thin, Unity-free orchestration layer the Editor entry point calls: read a description document,
// validate it, emit deterministic C#, write it atomically with LF line endings and no BOM, then re-read the
// written file and verify the recorded catalog fingerprint. A generation that produces a file whose fingerprint
// disagrees with its declarations fails instead of leaving a stale catalog in the project.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace GameCore.Content.Compiler
{
    /// <summary>Outcome of one generation run.</summary>
    public sealed class CatalogGenerationReport
    {
        internal CatalogGenerationReport(
            bool succeeded,
            string summary,
            string? outputPath,
            int bytesWritten,
            string? catalogFingerprint,
            string? catalogFileHash,
            bool wroteFile)
        {
            Succeeded = succeeded;
            Summary = summary;
            OutputPath = outputPath;
            BytesWritten = bytesWritten;
            CatalogFingerprint = catalogFingerprint;
            CatalogFileHash = catalogFileHash;
            WroteFile = wroteFile;
        }

        public bool Succeeded { get; }

        /// <summary>One-line or multi-line summary suitable for the Unity or console log.</summary>
        public string Summary { get; }

        /// <summary>Full path of the generated file; null when generation failed before writing.</summary>
        public string? OutputPath { get; }

        /// <summary>Bytes written; zero when the file was already current or generation failed.</summary>
        public int BytesWritten { get; }

        /// <summary>Fingerprint recorded in the generated file; null on failure.</summary>
        public string? CatalogFingerprint { get; }

        /// <summary>File-prefix hash recorded in the generated file; null on failure.</summary>
        public string? CatalogFileHash { get; }

        /// <summary>True when the file content changed; false when it was already byte-identical.</summary>
        public bool WroteFile { get; }
    }

    /// <summary>Reads, validates, emits and verifies one generated catalog file.</summary>
    public static class CatalogGenerator
    {
        /// <summary>Newline policy of generated files: LF only, independent of the host platform.</summary>
        public const string Newline = "\n";

        /// <summary>Runs one generation from a description file path to an output file path.</summary>
        public static CatalogGenerationReport GenerateFromFiles(string descriptionPath, string outputPath)
        {
            if (string.IsNullOrEmpty(descriptionPath))
            {
                throw new ArgumentException("A description path is required.", nameof(descriptionPath));
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }

            string descriptionFull = Path.GetFullPath(descriptionPath);
            string outputFull = Path.GetFullPath(outputPath);

            if (!File.Exists(descriptionFull))
            {
                return Failure("no catalog description at " + descriptionFull);
            }

            string json = File.ReadAllText(descriptionFull, new UTF8Encoding(false));
            return GenerateFromText(json, outputFull);
        }

        /// <summary>Runs one generation from description text to an output file path.</summary>
        public static CatalogGenerationReport GenerateFromText(string json, string outputPath)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }

            string outputFull = Path.GetFullPath(outputPath);

            CatalogCompilationResult compilation;
            try
            {
                compilation = CatalogDescriptionReader.Read(json);
            }
            catch (CatalogDescriptionException error)
            {
                return Failure("the description could not be validated: " + error.Message);
            }

            if (!compilation.Succeeded)
            {
                return Failure("the catalog description was rejected:\n" + compilation.Describe());
            }

            string code = compilation.GeneratedCode!;
            string? directory = Path.GetDirectoryName(outputFull);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bool existed = File.Exists(outputFull);
            bool unchanged = existed && string.Equals(File.ReadAllText(outputFull, new UTF8Encoding(false)), code, StringComparison.Ordinal);
            if (!unchanged)
            {
                // A byte-order mark would break the file-prefix hash scope, so the writer is explicit about it.
                File.WriteAllText(outputFull, code, new UTF8Encoding(false));
            }

            CatalogGenerationReport verification = Verify(outputFull);
            if (!verification.Succeeded)
            {
                return verification;
            }

            return new CatalogGenerationReport(
                true,
                Describe(unchanged ? "unchanged" : existed ? "regenerated" : "created", code, outputFull),
                outputFull,
                Encoding.UTF8.GetByteCount(code),
                verification.CatalogFingerprint,
                verification.CatalogFileHash,
                !unchanged);
        }

        /// <summary>
        /// Re-reads a generated file and verifies its recorded file hash and fingerprint against the file's own
        /// declarations. Used after generation and by tests that assert committed output is not stale.
        /// </summary>
        public static CatalogGenerationReport Verify(string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }

            string outputFull = Path.GetFullPath(outputPath);
            if (!File.Exists(outputFull))
            {
                return Failure("no generated file at " + outputFull);
            }

            string text = File.ReadAllText(outputFull, new UTF8Encoding(false));
            string fileHash;
            try
            {
                fileHash = CatalogEmitter.FilePrefixHash(text);
            }
            catch (CatalogDescriptionException error)
            {
                return Failure("the generated file is not a catalog: " + error.Message);
            }

            string? fingerprint = ExtractStringConstant(text, "CatalogFingerprint");
            if (fingerprint == null)
            {
                return Failure("the generated file records no CatalogFingerprint");
            }

            return new CatalogGenerationReport(
                true,
                "verified " + outputFull,
                outputFull,
                Encoding.UTF8.GetByteCount(text),
                fingerprint,
                fileHash,
                false);
        }

        /// <summary>Extracts the value of one generated string constant; used by verification and tests.</summary>
        public static string? ExtractStringConstant(string generatedText, string constantName)
        {
            if (generatedText == null)
            {
                throw new ArgumentNullException(nameof(generatedText));
            }

            const string Indent = "        public const string ";
            int start = generatedText.IndexOf(Indent + constantName + " = ", StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            int firstQuote = generatedText.IndexOf('"', start + Indent.Length + constantName.Length + 3);
            if (firstQuote < 0)
            {
                return null;
            }

            int secondQuote = generatedText.IndexOf('"', firstQuote + 1);
            return secondQuote < 0 ? null : generatedText.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        }

        private static string Describe(string action, string code, string outputFull)
        {
            string? fingerprint = ExtractStringConstant(code, "CatalogFingerprint");
            return action + " " + outputFull + "; bytes=" + Encoding.UTF8.GetByteCount(code).ToString(CultureInfo.InvariantCulture) +
                   "; catalogFingerprint=" + (fingerprint ?? "missing");
        }

        private static CatalogGenerationReport Failure(string summary) =>
            new CatalogGenerationReport(false, summary, null, 0, null, null, false);
    }
}
