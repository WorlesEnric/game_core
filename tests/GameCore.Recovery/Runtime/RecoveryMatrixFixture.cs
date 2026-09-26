// GameCore.Recovery.Fixtures - GC-027's versioned recovery fixture model (engine-free, data-driven).
//
// Normative sources: docs/game-core/00-core-protocols.md P-049 ("Pre-mutation failure leaves the old published world
// usable ... Recovery creates a new `WorldId` from a verified checkpoint or initial catalog ... Retryable transient
// resource failures use host-configured bounded attempts with new operation IDs"), P-053 (a checkpoint is taken at a
// committed boundary; restore validates everything into a new unexposed world before publication), P-045 ("use
// explicit external idempotency keys, and persist an outbox when delivery must survive crashes") and P-054 (canonical
// byte order, explicit integer versions, length bounds, unknown required fields refused rather than approximated);
// 06 s7 (temporary file, checksum, atomic replacement); 09-implementation-guide.md GC-027 ("versioned checkpoint
// fixtures, crash/restart transcripts and outbox consistency reports").
//
// WHAT THIS ASSEMBLY IS
//
// The data half of GC-027: the committed fixture documents under `tests/GameCore.Recovery/Data`, their model, and the
// one reader that decides whether a document is the fixture this build understands. It contains no game logic and no
// assertion: the NUnit suite under `tests/GameCore.Recovery/Tests` drives the *production* recovery types
// (GameCore.Execution.Recovery, GameCore.Execution.Delivery) with these documents and asserts that the documents
// agree with the production tables.
//
// WHY THE READER KNOWS NAMES INSTEAD OF ENUM VALUES
//
// This assembly is compiled by Unity with `"references": []` (see Runtime/GameCore.Recovery.Fixtures.asmdef), so it
// cannot see GameCore.Unity.Runtime - where the production recovery enums live - at all. The fixture vocabulary is
// therefore carried as *text* (`RecoveryFixtureVocabulary`) and the only place that can compare text with the
// production enums is the Tests assembly, which references this assembly and GameCore.Unity.Runtime. Those tests
// assert the vocabulary tables equal the production enum member names exactly, so a renamed or added member fails the
// suite instead of silently outdating a fixture document.
//
// THE READER REFUSES, NEVER GUESSES
//
// `TryRead` returns false and one reason for: the wrong (or a missing) `format` id, a duplicate case id, a missing or
// mistyped property, an empty `cases` array, a `mechanism`/`permittedOutcome`/`dataLossClass`/`expectedCode` name
// outside the documented vocabulary, an empty or absent `boundaryNames` array, and a boundary name the production
// tables never declare. It never fills a missing property with a default and never reports success for a document it
// only partly understood (P-054).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GameCore.Recovery.Fixtures
{
    /// <summary>The two `format` ids this fixture package owns; a document that declares another one is not ours.</summary>
    public static class RecoveryFixtureFormat
    {
        /// <summary>Format id of `Data/recovery-matrix.json` (the permitted-outcome matrix).</summary>
        public const string RecoveryMatrix = "gamecore.checkpoint-fixtures/recovery-matrix/1";

        /// <summary>Format id of `Data/checkpoint-store-versions.json` (the store-envelope version cases).</summary>
        public const string StoreVersions = "gamecore.checkpoint-fixtures/store-versions/1";

        /// <summary>One-line description of both formats, for a report or an evidence line.</summary>
        public static string Describe() =>
            "recovery fixtures: " + RecoveryMatrix + ", " + StoreVersions;
    }

    /// <summary>Raised by a `Read` call when a fixture document is not the format this build reads.</summary>
    public sealed class RecoveryFixtureFormatException : Exception
    {
        public RecoveryFixtureFormatException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// The member and boundary names a committed fixture document may use, as text. The Tests assembly asserts every
    /// table below against the production enums and constants it can see, which is what keeps this text in step with
    /// the production vocabulary across the Unity/dotnet assembly split (see the file header).
    /// </summary>
    public static class RecoveryFixtureVocabulary
    {
        /// <summary>`RecoveryInjectionMechanism` member names, in the production enum's declaration order.</summary>
        public static IReadOnlyList<string> Mechanisms { get; } = Array.AsReadOnly(new[]
        {
            "Latch",
            "DeliveryHook",
            "StoreRead",
        });

        /// <summary>`RecoveryPermittedOutcome` member names, in the production enum's declaration order.</summary>
        public static IReadOnlyList<string> PermittedOutcomes { get; } = Array.AsReadOnly(new[]
        {
            "NoCheckpointProduced",
            "PreviousDocumentIntact",
            "DestinationNeverBuilt",
            "DestinationNeverExposed",
            "ObligationDurableBeforeDelivery",
            "RedeliveryReusesIdempotencyKey",
            "AcknowledgementRecordedOrRedeliveredOnce",
            "NewSessionFromStoreOrNoWorld",
        });

        /// <summary>`RecoveryDataLossClass` member names, in the production enum's declaration order.</summary>
        public static IReadOnlyList<string> DataLossClasses { get; } = Array.AsReadOnly(new[]
        {
            "None",
            "UncommittedAttemptWork",
            "UnpersistedObligation",
            "UncommittedSinceCheckpoint",
        });

        /// <summary>`DiagnosticCode` member names plus `None`, in the production enum's declaration order.</summary>
        public static IReadOnlyList<string> DiagnosticCodes { get; } = Array.AsReadOnly(new[]
        {
            "None",
            "StaleHandle",
            "StalePlan",
            "MissingDependency",
            "ServiceConflict",
            "CapabilityConflict",
            "AmbiguousOrder",
            "Cycle",
            "Ineligible",
            "UnsupportedVersion",
            "OwnershipConflict",
            "BudgetExceeded",
            "MigrationRequired",
            "ResourceUnavailable",
            "Cancelled",
            "TooLate",
            "IdempotencyConflict",
            "ResultExpired",
            "ApplyFault",
            "TeardownBlocked",
            "CursorExpired",
            "SnapshotBackpressure",
        });

        /// <summary>
        /// The thirteen `FaultBoundaryText.Names` values a latch injection point may name, in the order
        /// `FaultBoundary` declares them.
        ///
        /// They are held here as text rather than read from `FaultBoundaryText`, and that is a deliberate split, not
        /// an oversight: `GameCore.Unity.Runtime.Faults` is compiled only when the fault-injection marker package is
        /// present (`GAMECORE_FAULT_INJECTION`, see `FaultBoundaries.cs`), so the pure fixture assembly - which is
        /// compiled by plain dotnet and by Unity with no references at all - cannot see that type. The Unity-side
        /// suite, which compiles in a project that carries the marker, is the half that asserts this list equals
        /// `FaultBoundaryText.Names` (see `RecoveryMatrixFixtureTests.ALatchBoundaryNameListMatchesTheUnityFaultBoundaryText`).
        /// Everything the pure half can check without that type it does check: that every latch name a production
        /// recovery point declares is present in this list.
        /// </summary>
        public static IReadOnlyList<string> LatchBoundaryNames { get; } = Array.AsReadOnly(new[]
        {
            "validation",
            "acquisition",
            "fence",
            "migration",
            "first-live-write",
            "structural-playback",
            "gate-installation",
            "cleanup",
            "checkpoint-capture-copy",
            "checkpoint-publication",
            "restore-reference-repair",
            "restore-apply",
            "recovery-publication",
        });

        /// <summary>
        /// The boundary a `StoreRead` injection point names: the store's own read, which is a real refusal a reader
        /// reports rather than a latch. It is deliberately not one of `FaultBoundaryText.Names`, because nothing in
        /// production arms a latch there - `ICheckpointStore.TryRead` refuses a corrupt or absent document by itself.
        /// </summary>
        public static IReadOnlyList<string> StoreReadBoundaryNames { get; } = Array.AsReadOnly(new[]
        {
            "store-read",
        });

        /// <summary>The six `DeliveryBoundaries.All` names a `DeliveryHook` injection point may name, in their order.</summary>
        public static IReadOnlyList<string> DeliveryBoundaryNames { get; } = Array.AsReadOnly(new[]
        {
            "before-append",
            "after-append",
            "before-delivery",
            "after-delivery",
            "before-acknowledge",
            "after-acknowledge",
        });

        /// <summary>
        /// Every boundary name a fixture document may declare: the latch names, then the store-read name, then the
        /// delivery names. This is the set `RecoveryMatrixDocument.TryRead` validates `boundaryNames` against, and the
        /// order is only the order a refusal reason lists them in.
        /// </summary>
        public static IReadOnlyList<string> BoundaryNames { get; } = Union(
            LatchBoundaryNames, StoreReadBoundaryNames, DeliveryBoundaryNames);

        /// <summary>The concatenation of several name lists, as one read-only list.</summary>
        public static IReadOnlyList<string> Union(params IReadOnlyList<string>[] lists)
        {
            if (lists == null)
            {
                throw new ArgumentNullException(nameof(lists));
            }

            var names = new List<string>();
            for (int i = 0; i < lists.Length; i++)
            {
                IReadOnlyList<string> list = lists[i];
                if (list == null)
                {
                    throw new ArgumentNullException(nameof(lists));
                }

                for (int n = 0; n < list.Count; n++)
                {
                    names.Add(list[n]);
                }
            }

            return names;
        }

        /// <summary>
        /// The boundary names one injection mechanism may declare: the delivery boundaries for a hook, the
        /// store-read name for a store read, and the latch names for a latch. A name outside its mechanism's list
        /// would mean a scenario armed something the production table does not declare.
        /// </summary>
        public static IReadOnlyList<string> NamesFor(string mechanism)
        {
            if (string.Equals(mechanism, "DeliveryHook", StringComparison.Ordinal))
            {
                return DeliveryBoundaryNames;
            }

            if (string.Equals(mechanism, "StoreRead", StringComparison.Ordinal))
            {
                return StoreReadBoundaryNames;
            }

            return LatchBoundaryNames;
        }

        /// <summary>True when <paramref name="name"/> is one of <paramref name="vocabulary"/>, compared ordinally.</summary>
        public static bool Contains(IReadOnlyList<string> vocabulary, string name)
        {
            if (vocabulary == null)
            {
                throw new ArgumentNullException(nameof(vocabulary));
            }

            for (int i = 0; i < vocabulary.Count; i++)
            {
                if (string.Equals(vocabulary[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The names as one comma-separated line, for a refusal reason or a test message.</summary>
        public static string Describe(IReadOnlyList<string> vocabulary)
        {
            if (vocabulary == null)
            {
                throw new ArgumentNullException(nameof(vocabulary));
            }

            return string.Join(", ", vocabulary);
        }
    }

    /// <summary>
    /// One case of the permitted-outcome matrix: the injection point, how a fault is injected there, which production
    /// boundaries it covers, the observable result the protocol permits and the data-loss class that result implies
    /// (P-049, TEST-016).
    /// </summary>
    public sealed class RecoveryMatrixCase
    {
        public RecoveryMatrixCase(
            string id,
            string title,
            IReadOnlyList<string> requirementIds,
            IReadOnlyList<string> testIds,
            string mechanism,
            IReadOnlyList<string> boundaryNames,
            string permittedOutcome,
            string dataLossClass,
            string statement)
        {
            Id = id;
            Title = title;
            RequirementIds = requirementIds;
            TestIds = testIds;
            Mechanism = mechanism;
            BoundaryNames = boundaryNames;
            PermittedOutcome = permittedOutcome;
            DataLossClass = dataLossClass;
            Statement = statement;
        }

        /// <summary>Stable id of the injection point; equal to a `RecoveryFaultPoints` id (P-052).</summary>
        public string Id { get; }

        /// <summary>One sentence stating the behaviour this case pins.</summary>
        public string Title { get; }

        /// <summary>Protocol requirements the case exercises; non-empty.</summary>
        public IReadOnlyList<string> RequirementIds { get; }

        /// <summary>Traceability tests that own the case; non-empty.</summary>
        public IReadOnlyList<string> TestIds { get; }

        /// <summary>`RecoveryInjectionMechanism` member name, as text (see `RecoveryFixtureVocabulary`).</summary>
        public string Mechanism { get; }

        /// <summary>The production boundary names this point covers; one for a latch, two for a delivery hook.</summary>
        public IReadOnlyList<string> BoundaryNames { get; }

        /// <summary>`RecoveryPermittedOutcome` member name the scenario must observe.</summary>
        public string PermittedOutcome { get; }

        /// <summary>`RecoveryDataLossClass` member name the point implies.</summary>
        public string DataLossClass { get; }

        /// <summary>One sentence stating what must be observable after the fault fired.</summary>
        public string Statement { get; }

        public override string ToString() =>
            "matrix(" + Id + "," + Mechanism + ":" + string.Join("+", BoundaryNames) + "," + PermittedOutcome + ")";
    }

    /// <summary>
    /// The permitted-outcome matrix: one case per injection point, in `RecoveryFaultPoints.All` order. The order is
    /// part of the contract because the Tests assert the case ids equal the production table's ids position by
    /// position, which is what makes a case that was dropped or reordered visible (P-052).
    /// </summary>
    public sealed class RecoveryMatrixDocument
    {
        private RecoveryMatrixDocument(string format, IReadOnlyList<RecoveryMatrixCase> cases)
        {
            Format = format;
            Cases = cases;
        }

        /// <summary>The `format` id the document declared; always `RecoveryFixtureFormat.RecoveryMatrix`.</summary>
        public string Format { get; }

        /// <summary>The cases, in document order; non-empty.</summary>
        public IReadOnlyList<RecoveryMatrixCase> Cases { get; }

        /// <summary>
        /// Parses one matrix document from text. False and a reason for every documented refusal (see the file
        /// header); true only when the whole document was understood.
        /// </summary>
        public static bool TryRead(string? json, out RecoveryMatrixDocument? document, out string reason)
        {
            document = null;
            if (json == null)
            {
                reason = "the recovery matrix text is null; there is nothing to read.";
                return false;
            }

            if (!RecoveryJsonValue.TryParse(json, out RecoveryJsonValue root, out string parseFailure))
            {
                reason = "the recovery matrix is not valid JSON: " + parseFailure;
                return false;
            }

            {
                if (root.ValueKind != RecoveryJsonKind.Object)
                {
                    reason = "the recovery matrix root must be a JSON object.";
                    return false;
                }

                if (!RecoveryFixtureJson.TryString(root, "format", "the recovery matrix", out string format, out reason))
                {
                    return false;
                }

                if (!string.Equals(format, RecoveryFixtureFormat.RecoveryMatrix, StringComparison.Ordinal))
                {
                    reason = "the recovery matrix declares format '" + format + "' and this build reads '"
                        + RecoveryFixtureFormat.RecoveryMatrix + "'.";
                    return false;
                }

                if (!RecoveryFixtureJson.TryArray(root, "cases", "the recovery matrix", out RecoveryJsonValue cases, out reason))
                {
                    return false;
                }

                if (cases.GetArrayLength() == 0)
                {
                    reason = "the recovery matrix carries no cases; a fixture document with no case states nothing.";
                    return false;
                }

                var parsedCases = new List<RecoveryMatrixCase>(cases.GetArrayLength());
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (RecoveryJsonValue element in cases.EnumerateArray())
                {
                    if (!TryParseCase(element, out RecoveryMatrixCase? parsedCase, out reason))
                    {
                        return false;
                    }

                    if (!seenIds.Add(parsedCase!.Id))
                    {
                        reason = "the recovery matrix names case id '" + parsedCase!.Id
                            + "' more than once; two cases for one injection point cannot both be authoritative.";
                        return false;
                    }

                    parsedCases.Add(parsedCase!);
                }

                document = new RecoveryMatrixDocument(format, parsedCases);
                return true;
            }
        }

        /// <summary>The document, or `RecoveryFixtureFormatException` carrying the refusal reason.</summary>
        public static RecoveryMatrixDocument Read(string? json)
        {
            if (TryRead(json, out RecoveryMatrixDocument? document, out string reason) && document != null)
            {
                return document;
            }

            throw new RecoveryFixtureFormatException(reason);
        }

        /// <summary>Reads a committed matrix document from a file path.</summary>
        public static bool TryReadFile(string path, out RecoveryMatrixDocument? document, out string reason)
        {
            document = null;
            if (string.IsNullOrEmpty(path))
            {
                reason = "no fixture path was supplied.";
                return false;
            }

            if (!File.Exists(path))
            {
                reason = "the recovery matrix is not at " + path + "; the committed fixture is the only source.";
                return false;
            }

            return TryRead(File.ReadAllText(path), out document, out reason);
        }

        /// <summary>The document at <paramref name="path"/>, or `RecoveryFixtureFormatException`.</summary>
        public static RecoveryMatrixDocument ReadFile(string path)
        {
            if (TryReadFile(path, out RecoveryMatrixDocument? document, out string reason) && document != null)
            {
                return document;
            }

            throw new RecoveryFixtureFormatException(reason);
        }

        private static bool TryParseCase(RecoveryJsonValue element, out RecoveryMatrixCase? parsedCase, out string reason)
        {
            parsedCase = null;
            if (element.ValueKind != RecoveryJsonKind.Object)
            {
                reason = "a recovery-matrix case must be a JSON object.";
                return false;
            }

            if (!RecoveryFixtureJson.TryString(element, "id", "a recovery-matrix case", out string id, out reason))
            {
                return false;
            }

            string context = "recovery-matrix case '" + id + "'";
            if (!RecoveryFixtureJson.TryString(element, "title", context, out string title, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryStringArray(element, "requirementIds", context, out IReadOnlyList<string> requirementIds, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryStringArray(element, "testIds", context, out IReadOnlyList<string> testIds, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryVocabularyMember(
                element, "mechanism", context, RecoveryFixtureVocabulary.Mechanisms, out string mechanism, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryVocabularyArray(
                element, "boundaryNames", context, RecoveryFixtureVocabulary.BoundaryNames, out IReadOnlyList<string> boundaryNames, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryVocabularyMember(
                element, "permittedOutcome", context, RecoveryFixtureVocabulary.PermittedOutcomes, out string permittedOutcome, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryVocabularyMember(
                element, "dataLossClass", context, RecoveryFixtureVocabulary.DataLossClasses, out string dataLossClass, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryString(element, "statement", context, out string statement, out reason))
            {
                return false;
            }

            parsedCase = new RecoveryMatrixCase(
                id, title, requirementIds, testIds, mechanism, boundaryNames, permittedOutcome, dataLossClass, statement);
            return true;
        }
    }

    /// <summary>
    /// One case of the store-envelope version fixture: which `major.minor` a stored envelope declares, whether this
    /// build's reader supports it, and exactly what a read must answer (P-055, P-054).
    /// </summary>
    public sealed class StoreVersionCase
    {
        public StoreVersionCase(
            string id,
            string title,
            IReadOnlyList<string> requirementIds,
            IReadOnlyList<string> testIds,
            byte major,
            byte minor,
            bool expectedSupported,
            string expectedCode,
            string expectedDetailContains)
        {
            Id = id;
            Title = title;
            RequirementIds = requirementIds;
            TestIds = testIds;
            Major = major;
            Minor = minor;
            ExpectedSupported = expectedSupported;
            ExpectedCode = expectedCode;
            ExpectedDetailContains = expectedDetailContains;
        }

        /// <summary>Stable case id, unique in the document.</summary>
        public string Id { get; }

        /// <summary>One sentence stating the version interpretation the case pins.</summary>
        public string Title { get; }

        /// <summary>Protocol requirements the case exercises; non-empty.</summary>
        public IReadOnlyList<string> RequirementIds { get; }

        /// <summary>Traceability tests that own the case; non-empty.</summary>
        public IReadOnlyList<string> TestIds { get; }

        /// <summary>Format major the stored envelope declares.</summary>
        public byte Major { get; }

        /// <summary>Format minor the stored envelope declares.</summary>
        public byte Minor { get; }

        /// <summary>What `CheckpointStoreFormat.IsSupported(Major, Minor)` must answer.</summary>
        public bool ExpectedSupported { get; }

        /// <summary>`DiagnosticCode` member name a read of such an envelope reports; `None` means it is readable.</summary>
        public string ExpectedCode { get; }

        /// <summary>
        /// Substring the refusal detail must carry, so two branches that report the same code cannot be confused
        /// (P-052). Empty for the supported case, which has no refusal detail at all.
        /// </summary>
        public string ExpectedDetailContains { get; }

        /// <summary>Canonical `major.minor` text, which is what a refusal detail names.</summary>
        public string VersionText =>
            Major.ToString(CultureInfo.InvariantCulture) + "." + Minor.ToString(CultureInfo.InvariantCulture);

        public override string ToString() => "storeVersion(" + Id + "," + VersionText + ")";
    }

    /// <summary>
    /// The store-envelope version fixture: one case per version the reader must interpret, so "a future minor is
    /// refused" and "another major is a different layout" are data rather than prose (P-055).
    /// </summary>
    public sealed class StoreVersionDocument
    {
        private StoreVersionDocument(string format, IReadOnlyList<StoreVersionCase> cases)
        {
            Format = format;
            Cases = cases;
        }

        /// <summary>The `format` id the document declared; always `RecoveryFixtureFormat.StoreVersions`.</summary>
        public string Format { get; }

        /// <summary>The cases, in document order; non-empty.</summary>
        public IReadOnlyList<StoreVersionCase> Cases { get; }

        /// <summary>Parses one store-version document; false and a reason for every documented refusal.</summary>
        public static bool TryRead(string? json, out StoreVersionDocument? document, out string reason)
        {
            document = null;
            if (json == null)
            {
                reason = "the store-version fixture text is null; there is nothing to read.";
                return false;
            }

            if (!RecoveryJsonValue.TryParse(json, out RecoveryJsonValue root, out string parseFailure))
            {
                reason = "the store-version fixture is not valid JSON: " + parseFailure;
                return false;
            }

            {
                if (root.ValueKind != RecoveryJsonKind.Object)
                {
                    reason = "the store-version fixture root must be a JSON object.";
                    return false;
                }

                if (!RecoveryFixtureJson.TryString(root, "format", "the store-version fixture", out string format, out reason))
                {
                    return false;
                }

                if (!string.Equals(format, RecoveryFixtureFormat.StoreVersions, StringComparison.Ordinal))
                {
                    reason = "the store-version fixture declares format '" + format + "' and this build reads '"
                        + RecoveryFixtureFormat.StoreVersions + "'.";
                    return false;
                }

                if (!RecoveryFixtureJson.TryArray(root, "cases", "the store-version fixture", out RecoveryJsonValue cases, out reason))
                {
                    return false;
                }

                if (cases.GetArrayLength() == 0)
                {
                    reason = "the store-version fixture carries no cases; a fixture document with no case states nothing.";
                    return false;
                }

                var parsedCases = new List<StoreVersionCase>(cases.GetArrayLength());
                var seenIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (RecoveryJsonValue element in cases.EnumerateArray())
                {
                    if (!TryParseCase(element, out StoreVersionCase? parsedCase, out reason))
                    {
                        return false;
                    }

                    if (!seenIds.Add(parsedCase!.Id))
                    {
                        reason = "the store-version fixture names case id '" + parsedCase!.Id
                            + "' more than once; two cases for one version cannot both be authoritative.";
                        return false;
                    }

                    parsedCases.Add(parsedCase!);
                }

                document = new StoreVersionDocument(format, parsedCases);
                return true;
            }
        }

        /// <summary>The document, or `RecoveryFixtureFormatException` carrying the refusal reason.</summary>
        public static StoreVersionDocument Read(string? json)
        {
            if (TryRead(json, out StoreVersionDocument? document, out string reason) && document != null)
            {
                return document;
            }

            throw new RecoveryFixtureFormatException(reason);
        }

        /// <summary>Reads the committed store-version document from a file path.</summary>
        public static bool TryReadFile(string path, out StoreVersionDocument? document, out string reason)
        {
            document = null;
            if (string.IsNullOrEmpty(path))
            {
                reason = "no fixture path was supplied.";
                return false;
            }

            if (!File.Exists(path))
            {
                reason = "the store-version fixture is not at " + path + "; the committed fixture is the only source.";
                return false;
            }

            return TryRead(File.ReadAllText(path), out document, out reason);
        }

        /// <summary>The document at <paramref name="path"/>, or `RecoveryFixtureFormatException`.</summary>
        public static StoreVersionDocument ReadFile(string path)
        {
            if (TryReadFile(path, out StoreVersionDocument? document, out string reason) && document != null)
            {
                return document;
            }

            throw new RecoveryFixtureFormatException(reason);
        }

        private static bool TryParseCase(RecoveryJsonValue element, out StoreVersionCase? parsedCase, out string reason)
        {
            parsedCase = null;
            if (element.ValueKind != RecoveryJsonKind.Object)
            {
                reason = "a store-version case must be a JSON object.";
                return false;
            }

            if (!RecoveryFixtureJson.TryString(element, "id", "a store-version case", out string id, out reason))
            {
                return false;
            }

            string context = "store-version case '" + id + "'";
            if (!RecoveryFixtureJson.TryString(element, "title", context, out string title, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryStringArray(element, "requirementIds", context, out IReadOnlyList<string> requirementIds, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryStringArray(element, "testIds", context, out IReadOnlyList<string> testIds, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryByte(element, "major", context, out byte major, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryByte(element, "minor", context, out byte minor, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryBool(element, "expectedSupported", context, out bool expectedSupported, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryVocabularyMember(
                element, "expectedCode", context, RecoveryFixtureVocabulary.DiagnosticCodes, out string expectedCode, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureJson.TryText(element, "expectedDetailContains", context, allowEmpty: true, out string expectedDetailContains, out reason))
            {
                return false;
            }

            parsedCase = new StoreVersionCase(
                id, title, requirementIds, testIds, major, minor, expectedSupported, expectedCode, expectedDetailContains);
            return true;
        }
    }

    /// <summary>
    /// Repository-relative fixture paths and the one rule that locates the repository root.
    ///
    /// The rule - honour `GAMECORE_REPO_ROOT`, else search upwards for `docs/game-core/traceability.json` - is the same
    /// rule `GameCore.ProtocolFixtures.RepoLayout` implements, and it is reimplemented here instead of referenced on
    /// purpose: `GameCore.ProtocolFixtures` is an independent oracle assembly that this package neither ships with nor
    /// may depend on (this assembly is compiled by Unity with `"references": []`, and making the recovery fixtures'
    /// data location depend on an unrelated oracle assembly's release would be a second, undeclared coupling). The
    /// rule is a dozen lines, so the duplication is cheaper than that dependency. Unlike `RepoLayout`, failure is a
    /// refusal with a reason rather than an exception in `TryFindRoot`; `FindRoot` keeps the throwing form so a test
    /// that needs the path fails with the searched locations listed.
    /// </summary>
    public static class RecoveryFixturePaths
    {
        /// <summary>Environment override, for a build host with an out-of-tree working directory.</summary>
        public const string RootEnvironmentVariable = "GAMECORE_REPO_ROOT";

        /// <summary>Marker file that identifies the repository root (the same marker every other suite uses).</summary>
        public const string RootMarker = "docs/game-core/traceability.json";

        /// <summary>Repository-relative directory of this fixture package.</summary>
        public const string PackageDirectory = "tests/GameCore.Recovery";

        /// <summary>Repository-relative directory holding the committed fixture documents.</summary>
        public const string DataDirectory = "tests/GameCore.Recovery/Data";

        /// <summary>Repository-relative path of the permitted-outcome matrix document.</summary>
        public const string RecoveryMatrixPath = "tests/GameCore.Recovery/Data/recovery-matrix.json";

        /// <summary>Repository-relative path of the store-envelope version document.</summary>
        public const string StoreVersionsPath = "tests/GameCore.Recovery/Data/checkpoint-store-versions.json";

        /// <summary>Absolute path of `Data/recovery-matrix.json` in this checkout.</summary>
        public static string RecoveryMatrixFile() => Resolve(FindRoot(), RecoveryMatrixPath);

        /// <summary>Absolute path of `Data/checkpoint-store-versions.json` in this checkout.</summary>
        public static string StoreVersionsFile() => Resolve(FindRoot(), StoreVersionsPath);

        /// <summary>Finds the repository root, or throws with the searched locations listed.</summary>
        public static string FindRoot()
        {
            if (TryFindRoot(out string root))
            {
                return root;
            }

            throw new InvalidOperationException(
                "Repository root not found. Set " + RootEnvironmentVariable
                + " or run the tests from inside the repository. Searched upwards from: "
                + AppContext.BaseDirectory + ", " + Directory.GetCurrentDirectory()
                + " for marker '" + RootMarker + "'.");
        }

        /// <summary>False and an empty root when no directory on either search path is the repository root.</summary>
        public static bool TryFindRoot(out string root)
        {
            root = string.Empty;
            string? fromEnvironment = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment) && IsRoot(fromEnvironment!))
            {
                root = Path.GetFullPath(fromEnvironment!);
                return true;
            }

            if (SearchUpwards(AppContext.BaseDirectory, out root))
            {
                return true;
            }

            return SearchUpwards(Directory.GetCurrentDirectory(), out root);
        }

        /// <summary>Resolves a repository-relative path with this platform's separator.</summary>
        public static string Resolve(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(root))
            {
                throw new ArgumentException("A repository root is required.", nameof(root));
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("A relative path is required.", nameof(relativePath));
            }

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool SearchUpwards(string startDirectory, out string root)
        {
            root = string.Empty;
            if (string.IsNullOrEmpty(startDirectory))
            {
                return false;
            }

            DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current != null)
            {
                if (IsRoot(current.FullName))
                {
                    root = current.FullName;
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }

        private static bool IsRoot(string directory) => File.Exists(Resolve(directory, RootMarker));
    }

    /// <summary>
    /// The strict JSON helpers every fixture reader uses: a property is present and has the documented kind, or the
    /// document is refused with the context and the property named. Nothing here substitutes a default (P-054).
    /// </summary>
    internal static class RecoveryFixtureJson
    {
        /// <summary>A required non-empty string property; used for every name, id and statement in a fixture.</summary>
        internal static bool TryString(RecoveryJsonValue parent, string name, string context, out string value, out string reason) =>
            TryText(parent, name, context, allowEmpty: false, out value, out reason);

        /// <summary>
        /// A required string property, with or without text. `allowEmpty` exists for the one property whose empty
        /// value is itself meaningful (`expectedDetailContains` on the supported store-version case names no refusal
        /// branch), so an empty value is read rather than refused.
        /// </summary>
        internal static bool TryText(
            RecoveryJsonValue parent,
            string name,
            string context,
            bool allowEmpty,
            out string value,
            out string reason)
        {
            value = string.Empty;
            if (parent.ValueKind != RecoveryJsonKind.Object
                || !parent.TryGetProperty(name, out RecoveryJsonValue element))
            {
                reason = context + " must declare a '" + name + "' property.";
                return false;
            }

            if (element.ValueKind != RecoveryJsonKind.String)
            {
                reason = context + " declares '" + name + "' as " + element.ValueKind + " and it must be a string.";
                return false;
            }

            string? text = element.GetString();
            if (text == null || (!allowEmpty && text.Length == 0))
            {
                reason = context + " declares an empty '" + name + "'; a name that carries no text states nothing.";
                return false;
            }

            value = text ?? string.Empty;
            reason = string.Empty;
            return true;
        }

        internal static bool TryArray(RecoveryJsonValue parent, string name, string context, out RecoveryJsonValue value, out string reason)
        {
            value = default(RecoveryJsonValue);
            if (parent.ValueKind != RecoveryJsonKind.Object
                || !parent.TryGetProperty(name, out RecoveryJsonValue element))
            {
                reason = context + " must declare a '" + name + "' array.";
                return false;
            }

            if (element.ValueKind != RecoveryJsonKind.Array)
            {
                reason = context + " declares '" + name + "' as " + element.ValueKind + " and it must be an array.";
                return false;
            }

            value = element;
            reason = string.Empty;
            return true;
        }

        internal static bool TryStringArray(RecoveryJsonValue parent, string name, string context, out IReadOnlyList<string> value, out string reason)
        {
            value = Array.Empty<string>();
            if (!TryArray(parent, name, context, out RecoveryJsonValue array, out reason))
            {
                return false;
            }

            if (array.GetArrayLength() == 0)
            {
                reason = context + " declares an empty '" + name + "' array.";
                return false;
            }

            var entries = new List<string>(array.GetArrayLength());
            int index = 0;
            foreach (RecoveryJsonValue element in array.EnumerateArray())
            {
                if (element.ValueKind != RecoveryJsonKind.String)
                {
                    reason = context + " declares '" + name + "[" + index.ToString(CultureInfo.InvariantCulture)
                        + "]' as " + element.ValueKind + " and it must be a string.";
                    return false;
                }

                string? text = element.GetString();
                if (string.IsNullOrEmpty(text))
                {
                    reason = context + " declares an empty '" + name + "[" + index.ToString(CultureInfo.InvariantCulture)
                        + "]' entry; every entry must name something.";
                    return false;
                }

                entries.Add(text!);
                index++;
            }

            value = entries;
            reason = string.Empty;
            return true;
        }

        internal static bool TryVocabularyMember(
            RecoveryJsonValue parent,
            string name,
            string context,
            IReadOnlyList<string> vocabulary,
            out string value,
            out string reason)
        {
            value = string.Empty;
            if (!TryString(parent, name, context, out string text, out reason))
            {
                return false;
            }

            if (!RecoveryFixtureVocabulary.Contains(vocabulary, text))
            {
                reason = context + " declares '" + name + "' as '" + text + "', which is not one of: "
                    + RecoveryFixtureVocabulary.Describe(vocabulary) + ".";
                return false;
            }

            value = text;
            reason = string.Empty;
            return true;
        }

        internal static bool TryVocabularyArray(
            RecoveryJsonValue parent,
            string name,
            string context,
            IReadOnlyList<string> vocabulary,
            out IReadOnlyList<string> value,
            out string reason)
        {
            value = Array.Empty<string>();
            if (!TryStringArray(parent, name, context, out IReadOnlyList<string> entries, out reason))
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (!RecoveryFixtureVocabulary.Contains(vocabulary, entries[i]))
                {
                    reason = context + " declares '" + name + "[" + i.ToString(CultureInfo.InvariantCulture) + "]' as '"
                        + entries[i] + "', which is not one of: " + RecoveryFixtureVocabulary.Describe(vocabulary) + ".";
                    return false;
                }
            }

            value = entries;
            reason = string.Empty;
            return true;
        }

        internal static bool TryByte(RecoveryJsonValue parent, string name, string context, out byte value, out string reason)
        {
            value = 0;
            if (parent.ValueKind != RecoveryJsonKind.Object
                || !parent.TryGetProperty(name, out RecoveryJsonValue element))
            {
                reason = context + " must declare a '" + name + "' property.";
                return false;
            }

            if (element.ValueKind != RecoveryJsonKind.Number || !element.TryGetInt32(out int number))
            {
                reason = context + " declares '" + name + "' as " + element.ValueKind
                    + " and it must be an integer in 0..255.";
                return false;
            }

            if (number < 0 || number > 255)
            {
                reason = context + " declares '" + name + "' as " + number.ToString(CultureInfo.InvariantCulture)
                    + ", which is outside 0..255 and is therefore not a format version byte (P-054).";
                return false;
            }

            value = (byte)number;
            reason = string.Empty;
            return true;
        }

        internal static bool TryBool(RecoveryJsonValue parent, string name, string context, out bool value, out string reason)
        {
            value = false;
            if (parent.ValueKind != RecoveryJsonKind.Object
                || !parent.TryGetProperty(name, out RecoveryJsonValue element))
            {
                reason = context + " must declare a '" + name + "' property.";
                return false;
            }

            if (element.ValueKind != RecoveryJsonKind.True && element.ValueKind != RecoveryJsonKind.False)
            {
                reason = context + " declares '" + name + "' as " + element.ValueKind + " and it must be a boolean.";
                return false;
            }

            value = element.GetBoolean();
            reason = string.Empty;
            return true;
        }
    }
}
