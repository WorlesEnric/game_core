// Independent pure oracle (GC-002). Fixture case/result model shared by the loader, runner and tests.
// The result format is documented in tests/GameCore.ProtocolFixtures/README.md and Data/result-schema.json.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace GameCore.ProtocolFixtures.Fixtures
{
    /// <summary>What a case claims about the input (05 s2, TEST-002).</summary>
    public enum FixtureExpectation
    {
        Valid = 0,
        Invalid = 1,
    }

    /// <summary>Outcome vocabulary of the result document.</summary>
    public enum FixtureOutcome
    {
        Pass = 0,
        Fail = 1,
        NotRun = 2,
        Blocked = 3,
    }

    /// <summary>Raised when fixture data does not match the documented case format.</summary>
    public sealed class FixtureFormatException : Exception
    {
        public FixtureFormatException(string message)
            : base(message)
        {
        }
    }

    /// <summary>One fixture case parsed from canonical JSON.</summary>
    public sealed class FixtureCase
    {
        public FixtureCase(
            string caseId,
            string title,
            IReadOnlyList<string> requirementIds,
            IReadOnlyList<string> testIds,
            string kind,
            FixtureExpectation expectation,
            string expectedCode,
            JsonElement parameters,
            string sourcePath)
        {
            CaseId = caseId;
            Title = title;
            RequirementIds = requirementIds;
            TestIds = testIds;
            Kind = kind;
            Expectation = expectation;
            ExpectedCode = expectedCode;
            Parameters = parameters;
            SourcePath = sourcePath;
        }

        public string CaseId { get; }

        public string Title { get; }

        public IReadOnlyList<string> RequirementIds { get; }

        public IReadOnlyList<string> TestIds { get; }

        public string Kind { get; }

        public FixtureExpectation Expectation { get; }

        /// <summary>Required rejection code when <see cref="Expectation"/> is Invalid.</summary>
        public string ExpectedCode { get; }

        public JsonElement Parameters { get; }

        public string SourcePath { get; }

        public string PrimaryTestId => TestIds.Count != 0 ? TestIds[0] : string.Empty;
    }

    /// <summary>One case file plus the repository-relative path it came from.</summary>
    public sealed class FixtureFile
    {
        public FixtureFile(string path, IReadOnlyList<FixtureCase> cases)
        {
            Path = path;
            Cases = cases;
        }

        public string Path { get; }

        public IReadOnlyList<FixtureCase> Cases { get; }
    }

    /// <summary>What the oracle observed for one case.</summary>
    public readonly struct OracleVerdict
    {
        public OracleVerdict(bool valid, string code, string detail)
        {
            Valid = valid;
            Code = code ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public static OracleVerdict Valid(string detail) => new OracleVerdict(true, string.Empty, detail);

        public static OracleVerdict Invalid(string code, string detail) => new OracleVerdict(false, code, detail);

        public bool Valid { get; }

        /// <summary>Stable rejection code; empty when the input is valid.</summary>
        public string Code { get; }

        public string Detail { get; }
    }

    /// <summary>Result of one executed case, in the documented result format.</summary>
    public sealed class FixtureResult
    {
        public FixtureResult(
            string caseId,
            IReadOnlyList<string> requirementIds,
            string testId,
            FixtureOutcome outcome,
            string detail)
        {
            CaseId = caseId;
            RequirementIds = requirementIds;
            TestId = testId;
            Outcome = outcome;
            Detail = detail;
        }

        public string CaseId { get; }

        public IReadOnlyList<string> RequirementIds { get; }

        public string TestId { get; }

        public FixtureOutcome Outcome { get; }

        public string Detail { get; }
    }
}
