// GC-027's fixture-agreement suite: the committed documents under `tests/GameCore.Recovery/Data` against the
// production tables they describe (P-049, P-053, P-055, TEST-016).
//
// The permitted-outcome matrix is the normative statement of GC-027's definition of done ("all fault points have a
// permitted observable result and no hidden external replay"), so this suite asserts the data and the production
// table `RecoveryFaultPoints.All` say the same thing, position by position, and that the fixture vocabulary the
// engine-free reader validates against is exactly the production vocabulary. The store-version fixture is driven
// through a real envelope: each case's declared `major.minor` is written into an envelope the production store
// framed itself, and the read must answer the case's own `expectedCode` and `expectedDetailContains`.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Execution.Recovery;
using GameCore.Recovery.Fixtures;
using NUnit.Framework;

namespace GameCore.Recovery.Fixtures.Tests
{
    /// <summary>The committed fixtures and the production recovery tables agree, case by case.</summary>
    [TestFixture]
    public sealed class RecoveryMatrixFixtureTests
    {
        /// <summary>The traceability tests GC-027 owns; a fixture case may name only these (09 sGC-027).</summary>
        private static readonly string[] OwningTestIds = { "TEST-002", "TEST-010", "TEST-014", "TEST-016", "TEST-017" };

        /// <summary>The protocol requirements GC-027 names as normative.</summary>
        private static readonly string[] NormativeRequirementIds =
        {
            "P-004", "P-005", "P-031", "P-032", "P-045", "P-047", "P-048",
            "P-049", "P-050", "P-053", "P-054", "P-055",
        };

        [Test]
        public void TheOrderedCaseIdsAreExactlyTheProductionFaultPointIds()
        {
            RecoveryMatrixDocument document = ReadMatrix();
            Assert.That(document.Format, Is.EqualTo(RecoveryFixtureFormat.RecoveryMatrix));
            Assert.That(document.Cases.Count, Is.EqualTo(RecoveryFaultPoints.Count),
                "the matrix carries one case per injection point.");

            for (int i = 0; i < RecoveryFaultPoints.Count; i++)
            {
                Assert.That(document.Cases[i].Id, Is.EqualTo(RecoveryFaultPoints.All[i].Id),
                    "case " + i + " must be the production table's point " + i + "; the order is the evidence order.");
            }
        }

        [Test]
        public void EveryCaseAgreesWithTheProductionFaultPointTable()
        {
            RecoveryMatrixDocument document = ReadMatrix();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (RecoveryMatrixCase matrixCase in document.Cases)
            {
                string context = "matrix case '" + matrixCase.Id + "'";
                Assert.That(seenIds.Add(matrixCase.Id), Is.True, context + ": case ids must be unique.");
                Assert.That(RecoveryFaultPoints.TryGet(matrixCase.Id, out RecoveryFaultPoint point), Is.True,
                    context + ": the id must name a production injection point.");
                Assert.That(point.Statement, Is.Not.Empty);

                Assert.That(matrixCase.Mechanism, Is.EqualTo(point.Mechanism.ToString()),
                    context + ": the mechanism must be the production table's.");
                Assert.That(matrixCase.PermittedOutcome, Is.EqualTo(point.Permitted.ToString()),
                    context + ": the permitted outcome must be the production table's.");
                Assert.That(matrixCase.DataLossClass, Is.EqualTo(point.DataLoss.ToString()),
                    context + ": the data-loss class must be the production table's.");

                Assert.That(matrixCase.BoundaryNames.Count, Is.EqualTo(point.BoundaryNames.Count),
                    context + ": the boundary names must be the production table's.");
                for (int i = 0; i < point.BoundaryNames.Count; i++)
                {
                    Assert.That(matrixCase.BoundaryNames[i], Is.EqualTo(point.BoundaryNames[i]),
                        context + ": boundary name " + i + " must be the production table's.");
                }

                Assert.That(matrixCase.Title, Is.Not.Empty, context + ": the case states the behaviour it pins.");
                Assert.That(matrixCase.Statement, Is.Not.Empty, context + ": the case states what must be observable.");
                Assert.That(matrixCase.RequirementIds, Is.Not.Empty, context + ": every case names its requirements.");
                Assert.That(matrixCase.TestIds, Is.Not.Empty, context + ": every case names its owning tests.");
                Assert.That(Contains(matrixCase.RequirementIds, "P-049"), Is.True,
                    context + ": every fault point proves P-049, the requirement GC-027 exists to close.");

                foreach (string requirementId in matrixCase.RequirementIds)
                {
                    Assert.That(requirementId, Is.Not.Empty);
                    Assert.That(Contains(NormativeRequirementIds, requirementId), Is.True,
                        context + ": '" + requirementId + "' is not one of GC-027's normative requirements.");
                }

                foreach (string testId in matrixCase.TestIds)
                {
                    Assert.That(testId, Is.Not.Empty);
                    Assert.That(Contains(OwningTestIds, testId), Is.True,
                        context + ": '" + testId + "' is not one of the tests GC-027 owns.");
                }
            }
        }

        [Test]
        public void EveryCasesBoundaryNamesAreDeclaredByItsInjectionMechanism()
        {
            RecoveryMatrixDocument document = ReadMatrix();
            var declared = new List<string>();
            int hooks = 0;
            int singleNamePoints = 0;
            int twoNamePoints = 0;
            string twoNamePointId = string.Empty;

            foreach (RecoveryMatrixCase matrixCase in document.Cases)
            {
                string context = "matrix case '" + matrixCase.Id + "'";
                Assert.That(RecoveryFaultPoints.TryGet(matrixCase.Id, out RecoveryFaultPoint point), Is.True, context);
                for (int i = 0; i < matrixCase.BoundaryNames.Count; i++)
                {
                    declared.Add(matrixCase.BoundaryNames[i]);
                }

                Assert.That(matrixCase.BoundaryNames, Is.Not.Empty,
                    context + ": an injection point that names no boundary cannot be armed or asserted.");

                // A latch names one `FaultBoundaryText` boundary per reach, a store read names the store's own read
                // (no latch is armed there at all), and a delivery hook names the pair either side of its delivery
                // step. The expected vocabulary per mechanism is derived from the production tables below rather
                // than re-encoded here.
                IReadOnlyList<string> allowed = RecoveryFixtureVocabulary.NamesFor(point.Mechanism.ToString());
                foreach (string boundaryName in matrixCase.BoundaryNames)
                {
                    Assert.That(Contains(allowed, boundaryName), Is.True,
                        context + ": '" + boundaryName + "' is not a boundary a " + point.Mechanism
                        + " injection point declares; the declared names are "
                        + RecoveryFixtureVocabulary.Describe(allowed) + ".");
                }

                if (point.Mechanism == RecoveryInjectionMechanism.DeliveryHook)
                {
                    hooks++;
                    Assert.That(matrixCase.BoundaryNames.Count, Is.EqualTo(2),
                        context + ": a delivery hook covers the pair of boundaries either side of its delivery step.");
                    Assert.That(matrixCase.BoundaryNames[0], Is.Not.EqualTo(matrixCase.BoundaryNames[1]),
                        context + ": the two boundaries of one hook must differ.");
                    foreach (string boundaryName in matrixCase.BoundaryNames)
                    {
                        Assert.That(Contains(DeliveryBoundaries.All, boundaryName), Is.True,
                            context + ": '" + boundaryName + "' is not a declared delivery boundary (P-045).");
                    }

                    continue;
                }

                Assert.That(Contains(DeliveryBoundaries.All, matrixCase.BoundaryNames[0]), Is.False,
                    context + ": a non-hook point must not name a delivery boundary.");

                if (matrixCase.BoundaryNames.Count == 1)
                {
                    singleNamePoints++;
                }
                else
                {
                    // Exactly one point is reached twice: the postwrite-apply boundary, after the staged world was
                    // written to and as the validated world is about to be published (P-030, P-031).
                    twoNamePoints++;
                    twoNamePointId = matrixCase.Id;
                }
            }

            Assert.That(hooks, Is.EqualTo(3), "the eight points contain exactly three delivery hooks.");
            Assert.That(twoNamePoints, Is.EqualTo(1), "exactly one latch point covers two reaches of its boundary.");
            Assert.That(twoNamePointId, Is.EqualTo(RecoveryFaultPoints.RestorePostwriteApply),
                "the point reached twice is the postwrite-apply boundary.");
            Assert.That(singleNamePoints, Is.EqualTo(RecoveryFaultPoints.Count - hooks - twoNamePoints));
            Assert.That(declared, Is.EqualTo(RecoveryFaultPoints.BoundaryNames()),
                "the matrix covers every boundary the production table declares, in the same order, duplicates included.");
        }

        [Test]
        public void TheFixtureVocabularyEqualsTheProductionVocabulary()
        {
            Assert.That(RecoveryFixtureVocabulary.Mechanisms, Is.EqualTo(Enum.GetNames(typeof(RecoveryInjectionMechanism))),
                "the fixture mechanism names must be the production enum's member names.");
            Assert.That(RecoveryFixtureVocabulary.PermittedOutcomes, Is.EqualTo(Enum.GetNames(typeof(RecoveryPermittedOutcome))),
                "the fixture permitted-outcome names must be the production enum's member names.");
            Assert.That(RecoveryFixtureVocabulary.DataLossClasses, Is.EqualTo(Enum.GetNames(typeof(RecoveryDataLossClass))),
                "the fixture data-loss names must be the production enum's member names.");

            var codes = new List<string> { DiagnosticCode.None.ToString() };
            foreach (DiagnosticCode code in DiagnosticCodeText.Values)
            {
                codes.Add(DiagnosticCodeText.Of(code));
            }

            Assert.That(RecoveryFixtureVocabulary.DiagnosticCodes, Is.EqualTo(codes),
                "the fixture diagnostic names must be the production code text, `None` included.");

            var boundaries = new List<string>(RecoveryFixtureVocabulary.LatchBoundaryNames);
            boundaries.AddRange(DeliveryBoundaries.All);
            boundaries.Add("store-read");


            // The vocabulary answers membership questions, so the contract is the *set* of declared names. Equal
            // counts plus both directions of membership is exact set equality.
            Assert.That(RecoveryFixtureVocabulary.BoundaryNames.Count, Is.EqualTo(boundaries.Count),
                "the fixture vocabulary carries every latch, delivery and store-read boundary exactly once.");
            foreach (string boundaryName in boundaries)
            {
                Assert.That(Contains(RecoveryFixtureVocabulary.BoundaryNames, boundaryName), Is.True,
                    "the fixture vocabulary must contain the declared boundary name '" + boundaryName + "'.");
            }

            foreach (string boundaryName in RecoveryFixtureVocabulary.BoundaryNames)
            {
                Assert.That(Contains(boundaries, boundaryName), Is.True,
                    "the fixture vocabulary must not declare a name no production table has: '" + boundaryName + "'.");
            }

            // Every latch boundary a production recovery point names must be in the latch list; the store-read name is
            // the one boundary that is not a latch at all, because a store refuses a read by itself.
            var latchNames = new List<string>();
            foreach (RecoveryFaultPoint point in RecoveryFaultPoints.All)
            {
                if (point.Mechanism != RecoveryInjectionMechanism.Latch)
                {
                    continue;
                }

                for (int i = 0; i < point.BoundaryNames.Count; i++)
                {
                    latchNames.Add(point.BoundaryNames[i]);
                    Assert.That(Contains(RecoveryFixtureVocabulary.LatchBoundaryNames, point.BoundaryNames[i]), Is.True,
                        "the latch name '" + point.BoundaryNames[i] + "' must be declared in the fixture's latch list.");
                }
            }

            Assert.That(latchNames.Count, Is.EqualTo(5), "the four latch points cover five reaches in total.");
            Assert.That(RecoveryFixtureVocabulary.LatchBoundaryNames.Count, Is.EqualTo(13),
                "`FaultBoundaryText.Names` declares thirteen boundaries, in `FaultBoundary`'s order.");
            Assert.That(RecoveryFixtureVocabulary.DeliveryBoundaryNames, Is.EqualTo(DeliveryBoundaries.All),
                "the fixture delivery names must be `DeliveryBoundaries.All` exactly, in its order.");
            Assert.That(RecoveryFixtureVocabulary.StoreReadBoundaryNames.Count, Is.EqualTo(1));
            Assert.That(RecoveryFixtureVocabulary.StoreReadBoundaryNames[0], Is.EqualTo("store-read"));
            Assert.That(Contains(RecoveryFixtureVocabulary.LatchBoundaryNames, "store-read"), Is.False,
                "the store-read boundary is a reader's refusal, not a latch `FaultBoundaryText` declares.");
            Assert.That(RecoveryFixtureVocabulary.NamesFor("Latch"), Is.EqualTo(RecoveryFixtureVocabulary.LatchBoundaryNames));
            Assert.That(RecoveryFixtureVocabulary.NamesFor("DeliveryHook"), Is.EqualTo(DeliveryBoundaries.All));
            Assert.That(RecoveryFixtureVocabulary.NamesFor("StoreRead"),
                Is.EqualTo(RecoveryFixtureVocabulary.StoreReadBoundaryNames));
            Assert.That(RecoveryFixtureVocabulary.Union(RecoveryFixtureVocabulary.LatchBoundaryNames, new[] { "a", "b" }).Count,
                Is.EqualTo(15), "a union concatenates in argument order, so a repeated name would show up as a count.");
            Assert.Throws<ArgumentNullException>(() => RecoveryFixtureVocabulary.Union(null!));
        }

        [Test]
        public void EveryStoreVersionCaseAgreesWithTheFormatAndWithARealEnvelope()
        {
            StoreVersionDocument document = ReadStoreVersions();
            Assert.That(document.Format, Is.EqualTo(RecoveryFixtureFormat.StoreVersions));

            byte[] envelope = PublishedEnvelope();
            int supported = 0;
            int futureMinor = 0;
            int futureMajor = 0;
            int majorZero = 0;
            var refusalBranches = new HashSet<string>(StringComparer.Ordinal);

            foreach (StoreVersionCase versionCase in document.Cases)
            {
                string context = "store-version case '" + versionCase.Id + "'";
                Assert.That(CheckpointStoreFormat.IsSupported(versionCase.Major, versionCase.Minor),
                    Is.EqualTo(versionCase.ExpectedSupported),
                    context + ": IsSupported must answer what the case declares.");
                Assert.That(versionCase.RequirementIds, Is.Not.Empty, context);
                Assert.That(versionCase.TestIds, Is.Not.Empty, context);

                byte[] patched = (byte[])envelope.Clone();
                patched[CheckpointStoreEnvelope.MajorOffset] = versionCase.Major;
                patched[CheckpointStoreEnvelope.MinorOffset] = versionCase.Minor;
                var store = new MemoryCheckpointStore("memory://gc027-store-version");
                Assert.That(store.TryOverwriteEnvelope(patched, out string overwriteDetail), Is.True, overwriteDetail);

                bool read = store.TryRead(out byte[]? readBack, out StoredCheckpoint stored, out DiagnosticCode code, out string detail);
                Assert.That(code.ToString(), Is.EqualTo(versionCase.ExpectedCode),
                    context + ": a read of this envelope must report the case's own code.");

                if (versionCase.ExpectedSupported)
                {
                    supported++;
                    Assert.That(read, Is.True, context + ": a supported version must be readable.");
                    Assert.That(detail, Is.Empty, context + ": a supported version produces no refusal detail.");
                    Assert.That(readBack, Is.Not.Null);
                    Assert.That(stored.IsStored, Is.True);
                    Assert.That(versionCase.ExpectedCode, Is.EqualTo(DiagnosticCode.None.ToString()));
                    Assert.That(versionCase.ExpectedDetailContains, Is.Empty,
                        context + ": the supported case names no refusal branch.");
                    Assert.That(versionCase.Major, Is.EqualTo(CheckpointStoreFormat.FormatMajor),
                        context + ": the one supported version is this build's own major.");
                    Assert.That(versionCase.Minor, Is.EqualTo(CheckpointStoreFormat.FormatMinor),
                        context + ": the supported case is the current minor, not a future one.");
                    continue;
                }

                Assert.That(read, Is.False, context + ": an unsupported version is refused before anything is read.");
                Assert.That(readBack, Is.Null, context + ": a refused read returns no document.");
                Assert.That(stored.IsStored, Is.False);
                Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
                Assert.That(versionCase.ExpectedDetailContains, Is.Not.Empty,
                    context + ": a refusal case must pin the branch that produced it (P-052).");
                Assert.That(detail, Does.Contain(versionCase.ExpectedDetailContains));
                Assert.That(versionCase.ExpectedDetailContains, Does.Contain("store format " + versionCase.VersionText),
                    context + ": the refusal names the version it read, so two refusals cannot be confused.");
                refusalBranches.Add(versionCase.ExpectedCode + "|" + versionCase.ExpectedDetailContains);

                if (versionCase.Major == CheckpointStoreFormat.FormatMajor)
                {
                    futureMinor++;
                }
                else if (versionCase.Major > CheckpointStoreFormat.FormatMajor)
                {
                    futureMajor++;
                }
                else
                {
                    majorZero++;
                }
            }

            Assert.That(supported, Is.EqualTo(1), "exactly one case is the version this build implements.");
            Assert.That(futureMinor, Is.GreaterThanOrEqualTo(1), "a future minor of the current major is covered.");
            Assert.That(futureMajor, Is.GreaterThanOrEqualTo(1), "a future major is covered.");
            Assert.That(majorZero, Is.GreaterThanOrEqualTo(1), "major zero is covered.");
            Assert.That(refusalBranches.Count, Is.EqualTo(document.Cases.Count - supported),
                "every refusal case pins a distinct (code, detail) branch.");
            Assert.That(HasVersion(document, 1, 1), Is.True, "the future minor 1.1 must be covered.");
            Assert.That(HasVersion(document, 2, 0), Is.True, "the future major 2.0 must be covered.");
            Assert.That(HasVersion(document, 0, 0), Is.True, "major zero must be covered.");
        }

        [Test]
        public void TheReaderRefusesAMalformedDocumentInsteadOfSubstitutingDefaults()
        {
            string valid = File.ReadAllText(RecoveryFixturePaths.RecoveryMatrixFile());
            Assert.That(RecoveryMatrixDocument.TryRead(valid, out RecoveryMatrixDocument? document, out string reason),
                Is.True, reason);
            Assert.That(document, Is.Not.Null);

            AssertRefused(valid, "\"gamecore.checkpoint-fixtures/recovery-matrix/1\"",
                "\"gamecore.checkpoint-fixtures/other/1\"", "declares format");
            AssertRefused(valid, "\"id\": \"checkpoint-publication\"", "\"id\": \"capture-copy\"", "more than once");
            AssertRefused(valid, "\"mechanism\": \"Latch\"", "\"mechanism\": \"Latched\"", "not one of");
            AssertRefused(valid, "\"permittedOutcome\": \"NoCheckpointProduced\"",
                "\"permittedOutcome\": \"Whatever\"", "not one of");
            AssertRefused(valid, "\"dataLossClass\": \"UncommittedAttemptWork\"",
                "\"dataLossClass\": \"SomeLoss\"", "not one of");
            AssertRefused(valid, "\"store-read\"", "\"store-write\"", "not one of");
            AssertRefused(valid, "\"boundaryNames\": [\n        \"store-read\"\n      ]", "\"boundaryNames\": []",
                "empty 'boundaryNames'");
            AssertRefused(valid, "\"statement\":", "\"statements\":", "must declare a 'statement' property");
            AssertRefused(valid, "{\n  \"format\"", "{\n  \"formats\"", "must declare a 'format' property");
            Assert.That(RecoveryMatrixDocument.TryRead("[1, 2]", out RecoveryMatrixDocument? arrayRoot, out string arrayReason),
                Is.False);
            Assert.That(arrayRoot, Is.Null);
            Assert.That(arrayReason, Does.Contain("root must be a JSON object"));

            Assert.That(RecoveryMatrixDocument.TryRead("{ not json", out RecoveryMatrixDocument? broken, out string jsonReason),
                Is.False);
            Assert.That(broken, Is.Null);
            Assert.That(jsonReason, Does.Contain("not valid JSON"));

            Assert.That(RecoveryMatrixDocument.TryReadFile("does/not/exist.json", out RecoveryMatrixDocument? missing, out string fileReason),
                Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(fileReason, Does.Contain("not at"));

            Assert.That(RecoveryMatrixDocument.Read(valid), Is.Not.Null, "the committed document reads.");
            Assert.Throws<RecoveryFixtureFormatException>(() => RecoveryMatrixDocument.Read("{ not json"));

            string validVersions = File.ReadAllText(RecoveryFixturePaths.StoreVersionsFile());
            Assert.That(StoreVersionDocument.TryRead(validVersions, out StoreVersionDocument? versions, out string versionReason),
                Is.True, versionReason);
            Assert.That(versions, Is.Not.Null);
            AssertRefusedVersions(validVersions, "\"gamecore.checkpoint-fixtures/store-versions/1\"",
                "\"gamecore.checkpoint-fixtures/store-versions/2\"", "declares format");
            AssertRefusedVersions(validVersions, "\"major\": 1,\n      \"minor\": 0", "\"major\": 300,\n      \"minor\": 0",
                "outside 0..255");
            AssertRefusedVersions(validVersions, "\"expectedSupported\": true", "\"expectedSupported\": 1", "must be a boolean");
            Assert.That(StoreVersionDocument.Read(validVersions), Is.Not.Null);
        }

        [Test]
        public void TheFixturePathsResolveTheCommittedDocuments()
        {
            Assert.That(RecoveryFixturePaths.RecoveryMatrixPath,
                Is.EqualTo("tests/GameCore.Recovery/Data/recovery-matrix.json"));
            Assert.That(RecoveryFixturePaths.StoreVersionsPath,
                Is.EqualTo("tests/GameCore.Recovery/Data/checkpoint-store-versions.json"));

            Assert.That(RecoveryFixturePaths.TryFindRoot(out string root), Is.True,
                "the suite must run from inside the repository, or GAMECORE_REPO_ROOT must name it.");
            Assert.That(File.Exists(RecoveryFixturePaths.Resolve(root, RecoveryFixturePaths.RootMarker)), Is.True,
                "the root is the directory holding the repository marker.");
            Assert.That(File.Exists(RecoveryFixturePaths.RecoveryMatrixFile()), Is.True);
            Assert.That(File.Exists(RecoveryFixturePaths.StoreVersionsFile()), Is.True);
            Assert.That(RecoveryFixturePaths.RecoveryMatrixFile(),
                Is.EqualTo(RecoveryFixturePaths.Resolve(RecoveryFixturePaths.FindRoot(), RecoveryFixturePaths.RecoveryMatrixPath)));

            Assert.Throws<ArgumentException>(() => RecoveryFixturePaths.Resolve(string.Empty, "x"));
            Assert.Throws<ArgumentException>(() => RecoveryFixturePaths.Resolve("root", string.Empty));
        }

#if GAMECORE_FAULT_INJECTION
        /// <summary>
        /// The latch name list the pure fixture assembly holds as text equals the production
        /// `GameCore.Unity.Runtime.Faults.FaultBoundaryText.Names`.
        ///
        /// This case exists only where `FaultBoundaryText` does, which is a compilation that carries the fault-latch
        /// marker symbol: the type lives in `Packages/com.gamecore.unity.runtime/Runtime/Faults`, an engine assembly
        /// that is compiled under `GAMECORE_FAULT_INJECTION` and is absent from the plain-dotnet build (which compiles
        /// only the engine-free `Runtime/Pure` and `Runtime/Observation` trees). `RecoveryFixtureVocabulary` therefore
        /// holds the thirteen names itself, and this is the one place that can prove the copy is exact - so the
        /// guarded case is the whole reason the copy is safe to keep.
        /// </summary>
        [Test]
        public void ALatchBoundaryNameListMatchesTheUnityFaultBoundaryText()
        {
            string[] names = GameCore.Unity.Runtime.Faults.FaultBoundaryText.Names;
            Assert.That(names.Length, Is.EqualTo(GameCore.Unity.Runtime.Faults.FaultBoundaryText.Count));
            Assert.That(RecoveryFixtureVocabulary.LatchBoundaryNames, Is.EqualTo(names),
                "the fixture's latch name list must be `FaultBoundaryText.Names` exactly, in `FaultBoundary`'s order.");

            foreach (RecoveryFaultPoint point in RecoveryFaultPoints.All)
            {
                if (point.Mechanism != RecoveryInjectionMechanism.Latch)
                {
                    continue;
                }

                for (int i = 0; i < point.BoundaryNames.Count; i++)
                {
                    Assert.That(Array.IndexOf(names, point.BoundaryNames[i]), Is.GreaterThanOrEqualTo(0),
                        "the latch boundary '" + point.BoundaryNames[i] + "' must be a `FaultBoundaryText` name.");
                }
            }
        }
#endif

        private static RecoveryMatrixDocument ReadMatrix()
        {
            string path = RecoveryFixturePaths.RecoveryMatrixFile();
            Assert.That(RecoveryMatrixDocument.TryReadFile(path, out RecoveryMatrixDocument? document, out string reason),
                Is.True, path + ": " + reason);
            Assert.That(document, Is.Not.Null);
            return document!;
        }

        private static StoreVersionDocument ReadStoreVersions()
        {
            string path = RecoveryFixturePaths.StoreVersionsFile();
            Assert.That(StoreVersionDocument.TryReadFile(path, out StoreVersionDocument? document, out string reason),
                Is.True, path + ": " + reason);
            Assert.That(document, Is.Not.Null);
            return document!;
        }

        private static byte[] PublishedEnvelope()
        {
            var store = new MemoryCheckpointStore("memory://gc027-source");
            var document = new byte[8];
            for (int i = 0; i < document.Length; i++)
            {
                document[i] = (byte)(i + 1);
            }

            Assert.That(store.TryPublish(document, out _, out DiagnosticCode code, out string detail), Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            byte[]? envelope = store.EnvelopeBytes();
            Assert.That(envelope, Is.Not.Null, "a published store exposes the envelope the version cases patch.");
            return envelope!;
        }

        private static bool HasVersion(StoreVersionDocument document, byte major, byte minor)
        {
            for (int i = 0; i < document.Cases.Count; i++)
            {
                if (document.Cases[i].Major == major && document.Cases[i].Minor == minor)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(IReadOnlyList<string> values, string candidate)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertRefused(string valid, string from, string to, string expectedReason)
        {
            string malformed = valid.Replace(from, to);
            Assert.That(malformed, Is.Not.EqualTo(valid),
                "the case must actually change the document; '" + from + "' was not found.");
            Assert.That(RecoveryMatrixDocument.TryRead(malformed, out RecoveryMatrixDocument? document, out string reason),
                Is.False, "a malformed matrix must be refused, never read with a substituted default (P-054).");
            Assert.That(document, Is.Null, "a refused read leaves no document behind.");
            Assert.That(reason, Does.Contain(expectedReason), "the refusal must name the defect: " + reason);
        }

        private static void AssertRefusedVersions(string valid, string from, string to, string expectedReason)
        {
            string malformed = valid.Replace(from, to);
            Assert.That(malformed, Is.Not.EqualTo(valid),
                "the case must actually change the document; '" + from + "' was not found.");
            Assert.That(StoreVersionDocument.TryRead(malformed, out StoreVersionDocument? document, out string reason),
                Is.False, "a malformed store-version fixture must be refused (P-054).");
            Assert.That(document, Is.Null);
            Assert.That(reason, Does.Contain(expectedReason), "the refusal must name the defect: " + reason);
        }
    }
}
