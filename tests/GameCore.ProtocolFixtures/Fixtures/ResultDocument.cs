// Independent pure oracle (GC-002). Result document writer for artifacts/protocol-fixtures/results.json.
// Field names and outcomes are documented in tests/GameCore.ProtocolFixtures/README.md and
// Data/result-schema.json; the same DTOs are used by the tests to read the document back.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameCore.ProtocolFixtures.Fixtures
{
    /// <summary>One case row of the result document.</summary>
    public sealed class ResultCaseDto
    {
        public string CaseId { get; set; } = string.Empty;

        public List<string> RequirementIds { get; set; } = new List<string>();

        public string TestId { get; set; } = string.Empty;

        public string Outcome { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>Outcome counts of the result document.</summary>
    public sealed class ResultSummaryDto
    {
        public int CaseCount { get; set; }

        public int Pass { get; set; }

        public int Fail { get; set; }

        public int NotRun { get; set; }

        public int Blocked { get; set; }
    }

    /// <summary>Root of the result document.</summary>
    public sealed class ResultDocumentDto
    {
        public int SchemaVersion { get; set; }

        /// <summary>"NotRun (pending orchestrator build host)" until a build host executes the cases.</summary>
        public string Status { get; set; } = string.Empty;

        public string GeneratedBy { get; set; } = string.Empty;

        public List<ResultCaseDto> Cases { get; set; } = new List<ResultCaseDto>();

        public ResultSummaryDto Summary { get; set; } = new ResultSummaryDto();
    }

    /// <summary>Serializes and writes the fixture result document.</summary>
    public static class ResultDocument
    {
        public const int SchemaVersion = 1;

        public const string PendingStatus = "NotRun (pending orchestrator build host)";

        public const string ExecutedStatus = "Executed";

        public const string GeneratedBy = "dotnet/tests/GameCore.ProtocolFixtures.Tests";

        public static JsonSerializerOptions Options { get; } = CreateOptions();

        public static ResultDocumentDto Build(string status, IReadOnlyList<FixtureResult> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            ResultDocumentDto document = new ResultDocumentDto
            {
                SchemaVersion = SchemaVersion,
                Status = status,
                GeneratedBy = GeneratedBy,
            };

            ResultSummaryDto summary = document.Summary;
            foreach (FixtureResult result in results)
            {
                document.Cases.Add(new ResultCaseDto
                {
                    CaseId = result.CaseId,
                    RequirementIds = new List<string>(result.RequirementIds),
                    TestId = result.TestId,
                    Outcome = result.Outcome.ToString(),
                    Detail = result.Detail,
                });

                summary.CaseCount++;
                switch (result.Outcome)
                {
                    case FixtureOutcome.Pass:
                        summary.Pass++;
                        break;
                    case FixtureOutcome.Fail:
                        summary.Fail++;
                        break;
                    case FixtureOutcome.NotRun:
                        summary.NotRun++;
                        break;
                    default:
                        summary.Blocked++;
                        break;
                }
            }

            return document;
        }

        public static string Serialize(ResultDocumentDto document) =>
            JsonSerializer.Serialize(document, Options) + "\n";

        /// <summary>Writes the document, creating the artifact directory when missing.</summary>
        public static void Write(string path, string status, IReadOnlyList<FixtureResult> results)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A result document path is required.", nameof(path));
            }

            string full = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(full, Serialize(Build(status, results)));
        }

        public static ResultDocumentDto Read(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Result document not found.", path);
            }

            ResultDocumentDto? document = JsonSerializer.Deserialize<ResultDocumentDto>(File.ReadAllText(path), Options);
            if (document == null)
            {
                throw new InvalidOperationException("Result document deserialized to null: " + path);
            }

            return document;
        }

        private static JsonSerializerOptions CreateOptions() =>
            new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
    }
}
