// Protocol fixture execution tests (GC-002). Loads every committed case, executes it against the independent
// oracle, writes the result document, and asserts the properties the W0 gate depends on.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Contracts;
using GameCore.ProtocolFixtures;
using GameCore.ProtocolFixtures.Fixtures;
using GameCore.ProtocolFixtures.Oracle;
using NUnit.Framework;

namespace GameCore.ProtocolFixtures.Tests
{
    [TestFixture]
    public sealed class ProtocolFixtureTests
    {
        private static readonly string[] RequiredKinds =
        {
            "idCanonicalBytes",
            "stableIdOrder",
            "handleValidation",
            "worldCollision",
            "counterAdvance",
            "publicationAdvance",
            "versionSupport",
            "assemblyIndependence",
            "idHexParsing",
            "diagnosticCodeText",
            "envelopeCodec",
        };

        private static readonly string[] Gc002RequirementIds = { "P-001", "P-004", "P-005", "P-006", "P-008", "P-055", "P-057" };

        private static string root = string.Empty;
        private static string resultDocumentPath = string.Empty;
        private static List<FixtureFile> files = new List<FixtureFile>();
        private static List<FixtureCase> cases = new List<FixtureCase>();
        private static List<FixtureResult> results = new List<FixtureResult>();
        private static FixtureRunner runner = new FixtureRunner();

        [OneTimeSetUp]
        public void LoadAndExecuteAllCases()
        {
            root = RepoLayout.FindRoot();
            string caseDirectory = RepoLayout.Resolve(root, RepoLayout.FixtureCaseDirectory);
            resultDocumentPath = RepoLayout.Resolve(root, RepoLayout.ResultDocumentPath);

            files = new List<FixtureFile>(FixtureLoader.LoadDirectory(caseDirectory, root));
            cases = new List<FixtureCase>();
            results = new List<FixtureResult>();
            runner = new FixtureRunner();

            foreach (FixtureFile file in files)
            {
                foreach (FixtureCase fixtureCase in file.Cases)
                {
                    cases.Add(fixtureCase);
                    results.Add(runner.Run(fixtureCase));
                }
            }

            // The document is written before any assertion so a failing run still leaves its evidence behind.
            ResultDocument.Write(resultDocumentPath, ResultDocument.ExecutedStatus, results);
        }

        [Test]
        public void CaseDirectoryContainsFixtureFiles()
        {
            Assert.That(files.Count, Is.GreaterThanOrEqualTo(5), "Expected several case files under the fixture data directory.");
            Assert.That(cases.Count, Is.GreaterThanOrEqualTo(30), "The W0 fixture set must cover identity, handles, counters and versions.");
        }

        [Test]
        public void EveryCaseExecutesAndMatchesItsExpectation()
        {
            List<string> failures = new List<string>();
            foreach (FixtureResult result in results)
            {
                if (result.Outcome != FixtureOutcome.Pass)
                {
                    failures.Add(result.CaseId + " -> " + result.Outcome + ": " + result.Detail);
                }
            }

            Assert.That(
                failures,
                Is.Empty,
                "Fixture cases did not match their expectation (no case may be NotRun or Blocked in a delivered run):\n  " +
                string.Join("\n  ", failures.ToArray()));
        }

        [Test]
        public void OracleDistinguishesValidFromInvalidOutcomes()
        {
            int validCases = 0;
            int invalidCases = 0;
            foreach (FixtureCase fixtureCase in cases)
            {
                if (fixtureCase.Expectation == FixtureExpectation.Valid)
                {
                    validCases++;
                }
                else
                {
                    invalidCases++;
                    OracleVerdict verdict = runner.Evaluate(fixtureCase);
                    Assert.That(verdict.IsValid, Is.False, "Case '" + fixtureCase.CaseId + "' was accepted although it must be refused.");
                    Assert.That(
                        verdict.Code,
                        Is.EqualTo(fixtureCase.ExpectedCode),
                        "Case '" + fixtureCase.CaseId + "' produced a different refusal code; the fixture expectation and the oracle must agree.");
                }
            }

            Assert.That(validCases, Is.GreaterThan(10), "The fixture set must contain accepted inputs.");
            Assert.That(invalidCases, Is.GreaterThan(10), "The fixture set must contain refused inputs.");
        }

        [Test]
        public void ShuffledInsertionOrdersProduceIdenticalCanonicalOutput()
        {
            int checkedOrders = 0;
            foreach (FixtureCase fixtureCase in cases)
            {
                if (!string.Equals(fixtureCase.Kind, "stableIdOrder", StringComparison.Ordinal) ||
                    fixtureCase.Expectation != FixtureExpectation.Valid)
                {
                    continue;
                }

                IReadOnlyList<Id128> ids = ReadIds(fixtureCase);
                Id128[] canonical = CanonicalOrder.SortedCanonical(ids);

                for (int i = 0; i < ids.Count; i++)
                {
                    // Every rotation is a different insertion order with the same set of identities.
                    Id128[] rotated = new Id128[ids.Count];
                    for (int j = 0; j < ids.Count; j++)
                    {
                        rotated[j] = ids[(i + j) % ids.Count];
                    }

                    Id128[] sorted = CanonicalOrder.SortedCanonical(rotated);
                    Assert.That(
                        CanonicalOrder.Describe(sorted),
                        Is.EqualTo(CanonicalOrder.Describe(canonical)),
                        "Case '" + fixtureCase.CaseId + "' produced a different canonical order for rotation " + i);

                    Id128[] sortedAgain = CanonicalOrder.SortedCanonical(sorted);
                    Assert.That(
                        CanonicalOrder.Describe(sortedAgain),
                        Is.EqualTo(CanonicalOrder.Describe(canonical)),
                        "Canonical sorting must be idempotent for case '" + fixtureCase.CaseId + "'.");

                    checkedOrders++;
                }
            }

            Assert.That(checkedOrders, Is.GreaterThan(0), "No ordering case was exercised.");
        }

        [Test]
        public void SeamCodecAgreesWithOracleCanonicalBytes()
        {
            List<Id128> ids = new List<Id128>();
            for (ulong i = 0UL; i < 64UL; i++)
            {
                ids.Add(new Id128(i * 0x0123456789abcdefUL, ~i));
            }

            foreach (FixtureCase fixtureCase in cases)
            {
                if (string.Equals(fixtureCase.Kind, "stableIdOrder", StringComparison.Ordinal))
                {
                    foreach (Id128 id in ReadIds(fixtureCase))
                    {
                        ids.Add(id);
                    }
                }
            }

            for (int i = 0; i < ids.Count; i++)
            {
                Id128 id = ids[i];
                Assert.That(Id128Codec.ToHex(id), Is.EqualTo(CanonicalOrder.Hex(id)), "Seam hex and oracle hex must agree for " + id.ToString());
                Assert.That(
                    Id128Codec.ToBigEndianBytes(id),
                    Is.EqualTo(CanonicalOrder.Bytes(id)),
                    "Seam canonical bytes and oracle canonical bytes must agree for " + id.ToString());
                Assert.That(
                    Id128Codec.CompareBigEndian(id, ids[(i + 1) % ids.Count]),
                    Is.EqualTo(CanonicalOrder.Compare(id, ids[(i + 1) % ids.Count])),
                    "Seam comparison and oracle comparison must agree.");
            }
        }

        [Test]
        public void CounterOracleRefusesOverflowForEveryCounter()
        {
            foreach (CounterName counter in Enum.GetValues(typeof(CounterName)))
            {
                CounterAdvance atMaximum = CounterOracle.Advance(counter, ulong.MaxValue);
                Assert.That(atMaximum.Accepted, Is.False, counter + " must refuse further allocation at its maximum.");
                Assert.That(atMaximum.Next, Is.EqualTo(ulong.MaxValue), counter + " must not wrap.");
                Assert.That(atMaximum.Code, Is.EqualTo(CounterOracle.OverflowCode));

                CounterAdvance nearMaximum = CounterOracle.Advance(counter, ulong.MaxValue - 1UL);
                Assert.That(nearMaximum.Accepted, Is.True, counter + " must still advance one below its maximum.");
                Assert.That(nearMaximum.Next, Is.EqualTo(ulong.MaxValue));
            }
        }

        [Test]
        public void EveryDocumentedKindAndRequirementIsExercised()
        {
            foreach (string kind in RequiredKinds)
            {
                Assert.That(
                    runner.KindUseCounts.ContainsKey(kind),
                    Is.True,
                    "No fixture case exercises kind '" + kind + "'.");
            }

            List<string> declared = new List<string>();
            foreach (FixtureCase fixtureCase in cases)
            {
                foreach (string requirementId in fixtureCase.RequirementIds)
                {
                    if (!declared.Contains(requirementId))
                    {
                        declared.Add(requirementId);
                    }
                }
            }

            foreach (string requirementId in Gc002RequirementIds)
            {
                Assert.That(
                    declared.Contains(requirementId),
                    Is.True,
                    "No fixture case names requirement " + requirementId + " (GC-002 normative section).");
            }
        }

        [Test]
        public void ResultDocumentRoundTripsAndIsWrittenToTheDocumentedPath()
        {
            Assert.That(File.Exists(resultDocumentPath), Is.True, "The result document was not written: " + resultDocumentPath);
            Assert.That(
                resultDocumentPath,
                Is.EqualTo(Path.Combine(root, "artifacts", "protocol-fixtures", "results.json")),
                "The result document must be artifacts/protocol-fixtures/results.json.");

            ResultDocumentDto document = ResultDocument.Read(resultDocumentPath);
            Assert.That(document.SchemaVersion, Is.EqualTo(ResultDocument.SchemaVersion));
            Assert.That(document.Status, Is.EqualTo(ResultDocument.ExecutedStatus));
            Assert.That(document.GeneratedBy, Is.EqualTo(ResultDocument.GeneratedBy));
            Assert.That(document.Cases.Count, Is.EqualTo(cases.Count));
            Assert.That(document.Summary.CaseCount, Is.EqualTo(document.Cases.Count));
            Assert.That(
                document.Summary.Pass + document.Summary.Fail + document.Summary.NotRun + document.Summary.Blocked,
                Is.EqualTo(document.Summary.CaseCount),
                "Outcome counts must account for every row.");

            foreach (ResultCaseDto row in document.Cases)
            {
                Assert.That(row.CaseId, Is.Not.Empty);
                Assert.That(row.TestId, Is.Not.Empty);
                Assert.That(row.RequirementIds, Is.Not.Empty);
                Assert.That(row.Outcome, Is.Not.Empty);
            }

            string reserialized = ResultDocument.Serialize(ResultDocument.Build(ResultDocument.ExecutedStatus, results));
            Assert.That(reserialized, Is.EqualTo(File.ReadAllText(resultDocumentPath)), "Result documents must serialize deterministically.");
        }

        [Test]
        public void FixtureLoaderRejectsMalformedCasesAndRunnerBlocksUnknownKinds()
        {
            Assert.Throws<FixtureFormatException>(() => FixtureLoader.ParseCaseFromJson(
                "{\"caseId\":\"x\",\"title\":\"t\",\"requirementIds\":[\"P-005\"],\"testIds\":[\"TEST-002\"],\"expected\":{\"outcome\":\"valid\"},\"parameters\":{}}"));

            Assert.Throws<FixtureFormatException>(() => FixtureLoader.ParseCaseFromJson(
                "{\"caseId\":\"x\",\"title\":\"t\",\"requirementIds\":[\"Q-005\"],\"testIds\":[\"TEST-002\"],\"kind\":\"counterAdvance\",\"expected\":{\"outcome\":\"valid\"},\"parameters\":{}}"));

            Assert.Throws<FixtureFormatException>(() => FixtureLoader.ParseCaseFromJson(
                "{\"caseId\":\"x\",\"title\":\"t\",\"requirementIds\":[\"P-005\"],\"testIds\":[\"TEST-002\"],\"kind\":\"counterAdvance\",\"expected\":{\"outcome\":\"invalid\"},\"parameters\":{}}"));

            FixtureCase unknownKind = FixtureLoader.ParseCaseFromJson(
                "{\"caseId\":\"x\",\"title\":\"t\",\"requirementIds\":[\"P-005\"],\"testIds\":[\"TEST-002\"],\"kind\":\"notAKind\",\"expected\":{\"outcome\":\"valid\"},\"parameters\":{}}");
            FixtureResult blocked = new FixtureRunner().Run(unknownKind);
            Assert.That(blocked.Outcome, Is.EqualTo(FixtureOutcome.Blocked));
            Assert.That(blocked.Detail, Does.Contain("notAKind"));
        }

        [Test]
        public void CaseIdentifiersAreUniqueAcrossFiles()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (FixtureCase fixtureCase in cases)
            {
                Assert.That(
                    seen.Add(fixtureCase.CaseId),
                    Is.True,
                    "Duplicate case id '" + fixtureCase.CaseId + "' in " + fixtureCase.SourcePath);
            }
        }

        private static IReadOnlyList<Id128> ReadIds(FixtureCase fixtureCase)
        {
            List<Id128> ids = new List<Id128>();
            foreach (System.Text.Json.JsonElement element in fixtureCase.Parameters.GetProperty("ids").EnumerateArray())
            {
                string? text = element.GetString();
                if (text == null || !CanonicalOrder.TryParseHex(text, out Id128 id))
                {
                    throw new FixtureFormatException("Case '" + fixtureCase.CaseId + "' has an id that is not 32 hex characters.");
                }

                ids.Add(id);
            }

            return ids;
        }
    }
}
