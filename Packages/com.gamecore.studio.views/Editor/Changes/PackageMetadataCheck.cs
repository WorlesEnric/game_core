#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Views
{
    /// <summary>The actual repository checker result, separate from the informational package graph.</summary>
    public sealed class PackageMetadataCheck
    {
        private PackageMetadataCheck(bool assessed, IReadOnlyList<string> problems, string detail)
        {
            Assessed = assessed;
            Problems = problems;
            Detail = detail;
        }

        public bool Assessed { get; }
        public IReadOnlyList<string> Problems { get; }
        public string Detail { get; }
        public bool Passed => Assessed && Problems.Count == 0;

        public static PackageMetadataCheck Parse(string json, int exitCode)
        {
            JObject report = JObject.Parse(json);
            if (!(report["problems"] is JArray rows))
                return Unavailable("Checker report has no problems array.");
            List<string> problems = new List<string>();
            foreach (JToken row in rows)
            {
                if (row.Type != JTokenType.String) return Unavailable("Checker problem must be a string.");
                problems.Add(new SecretRedactor().Redact(row.Value<string>() ?? string.Empty));
            }
            if ((exitCode == 0) != (problems.Count == 0) || (exitCode != 0 && exitCode != 1))
                return Unavailable("Checker exit status disagrees with its report.");
            return new PackageMetadataCheck(true, problems, "tools/check_package_metadata.py: " + (problems.Count == 0 ? "passed" : problems.Count + " problems"));
        }

        public static Task<PackageMetadataCheck> RunAsync(string projectDirectory)
        {
            return Task.Run(() => Run(projectDirectory));
        }

        private static PackageMetadataCheck Run(string projectDirectory)
        {
            string temporary = Path.Combine(Path.GetTempPath(), "studio-metadata-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                DirectoryInfo? directory = new DirectoryInfo(projectDirectory);
                while (directory != null && !File.Exists(Path.Combine(directory.FullName, "tools", "check_package_metadata.py"))) directory = directory.Parent;
                if (directory == null) return Unavailable("Repository checker was not found; dependencies are informational only.");
                ProcessStartInfo start = new ProcessStartInfo("python3")
                {
                    WorkingDirectory = directory.FullName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                start.ArgumentList.Add(Path.Combine(directory.FullName, "tools", "check_package_metadata.py"));
                start.ArgumentList.Add("--json");
                start.ArgumentList.Add(temporary);
                using Process process = new Process { StartInfo = start };
                // Do not retain raw child logs. This checker writes the structured result we consume separately.
                using RedactingTextWriter sink = new RedactingTextWriter(TextWriter.Null);
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sink) sink.WriteLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sink) sink.WriteLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                // Repository traversal can exceed 30 seconds during a cold import on a busy build host.
                // This runs off the UI thread; retain a bounded deadline and fail closed on timeout.
                if (!process.WaitForExit(120000))
                {
                    process.Kill();
                    process.WaitForExit();
                    return Unavailable("Repository checker timed out.");
                }
                process.WaitForExit();
                return File.Exists(temporary) ? Parse(File.ReadAllText(temporary), process.ExitCode) : Unavailable("Checker produced no JSON report.");
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception || error is InvalidOperationException || error is Newtonsoft.Json.JsonException)
            {
                return Unavailable(new SecretRedactor().Redact(error.Message));
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static PackageMetadataCheck Unavailable(string detail) => new PackageMetadataCheck(false, Array.Empty<string>(), "Not assessed: " + detail);
    }
}
