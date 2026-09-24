// Independent pure oracle (GC-002). Executes one fixture case against the protocol oracles and compares the
// observed verdict with the case's stated expectation. A malformed case is Blocked, never silently skipped.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using GameCore.Contracts;
using GameCore.ProtocolFixtures.Oracle;

namespace GameCore.ProtocolFixtures.Fixtures
{
    /// <summary>Fixture executor for the kinds documented in Data/result-schema.json.</summary>
    public sealed class FixtureRunner
    {
        private readonly Dictionary<string, int> kindUseCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Observed case count per kind; used to assert every documented kind is exercised.</summary>
        public IReadOnlyDictionary<string, int> KindUseCounts => kindUseCounts;

        public FixtureResult Run(FixtureCase fixtureCase)
        {
            if (fixtureCase == null)
            {
                throw new ArgumentNullException(nameof(fixtureCase));
            }

            kindUseCounts.TryGetValue(fixtureCase.Kind, out int used);
            kindUseCounts[fixtureCase.Kind] = used + 1;

            OracleVerdict verdict;
            try
            {
                verdict = Evaluate(fixtureCase);
            }
            catch (FixtureFormatException error)
            {
                return new FixtureResult(
                    fixtureCase.CaseId,
                    fixtureCase.RequirementIds,
                    fixtureCase.PrimaryTestId,
                    FixtureOutcome.Blocked,
                    error.Message);
            }

            bool passed;
            string detail;
            if (fixtureCase.Expectation == FixtureExpectation.Valid)
            {
                passed = verdict.IsValid;
                detail = "expected valid; observed " + Describe(verdict);
            }
            else
            {
                passed = !verdict.IsValid && string.Equals(verdict.Code, fixtureCase.ExpectedCode, StringComparison.Ordinal);
                detail = "expected invalid(" + fixtureCase.ExpectedCode + "); observed " + Describe(verdict);
            }

            return new FixtureResult(
                fixtureCase.CaseId,
                fixtureCase.RequirementIds,
                fixtureCase.PrimaryTestId,
                passed ? FixtureOutcome.Pass : FixtureOutcome.Fail,
                detail);
        }

        public OracleVerdict Evaluate(FixtureCase fixtureCase)
        {
            if (fixtureCase == null)
            {
                throw new ArgumentNullException(nameof(fixtureCase));
            }

            JsonElement parameters = fixtureCase.Parameters;
            switch (fixtureCase.Kind)
            {
                case "idCanonicalBytes":
                    return EvaluateCanonicalBytes(parameters);
                case "stableIdOrder":
                    return EvaluateStableIdOrder(parameters);
                case "handleValidation":
                    return EvaluateHandleValidation(parameters);
                case "worldCollision":
                    return EvaluateWorldCollision(parameters);
                case "counterAdvance":
                    return EvaluateCounterAdvance(parameters);
                case "publicationAdvance":
                    return EvaluatePublicationAdvance(parameters);
                case "versionSupport":
                    return EvaluateVersionSupport(parameters);
                case "assemblyIndependence":
                    return EvaluateAssemblyIndependence(parameters);
                case "idHexParsing":
                    return EvaluateIdHexParsing(parameters);
                case "diagnosticCodeText":
                    return EvaluateDiagnosticCodeText(parameters);
                case "envelopeCodec":
                    return EvaluateEnvelopeCodec(parameters);
                default:
                    throw new FixtureFormatException(
                        "Case '" + fixtureCase.CaseId + "' uses unknown fixture kind '" + fixtureCase.Kind + "'.");
            }
        }

        private static string Describe(OracleVerdict verdict) =>
            verdict.IsValid ? "valid (" + verdict.Detail + ")" : verdict.Code + " (" + verdict.Detail + ")";

        private static OracleVerdict EvaluateCanonicalBytes(JsonElement parameters)
        {
            Id128 id = RequireId(parameters, "id");
            string expectedHex = RequireString(parameters, "expectedBytesHex");
            string actualHex = CanonicalOrder.Hex(id);

            // Self-consistency first: the canonical hex must round-trip to the input identity.
            if (!CanonicalOrder.TryParseHex(actualHex, out Id128 roundTrip) || !roundTrip.Equals(id))
            {
                return OracleVerdict.Invalid(
                    "HexRoundTripMismatch",
                    "canonical hex '" + actualHex + "' does not round-trip to the input id");
            }

            return string.Equals(actualHex, expectedHex, StringComparison.Ordinal)
                ? OracleVerdict.Valid("canonical bytes " + actualHex)
                : OracleVerdict.Invalid("CanonicalBytesMismatch", "expected " + expectedHex + ", observed " + actualHex);
        }

        private static OracleVerdict EvaluateStableIdOrder(JsonElement parameters)
        {
            IReadOnlyList<Id128> ids = RequireIdArray(parameters, "ids");
            IReadOnlyList<Id128> expected = RequireIdArray(parameters, "expectedCanonicalOrder");
            if (expected.Count != ids.Count)
            {
                return OracleVerdict.Invalid(
                    "OrderMismatch",
                    "expectedCanonicalOrder names " + expected.Count + " ids but the input has " + ids.Count);
            }

            List<int[]> orders = ReadPermutations(parameters, ids.Count);
            int compared = 0;
            foreach (int[] order in orders)
            {
                Id128[] permuted = ApplyPermutation(ids, order);
                Id128[] canonical = CanonicalOrder.SortedCanonical(permuted);
                if (!SameSequence(canonical, expected))
                {
                    return OracleVerdict.Invalid(
                        "PermutationOrderMismatch",
                        "insertion order " + DescribeOrder(order) + " produced " + CanonicalOrder.Describe(canonical));
                }

                Id128[] byBytes = CanonicalOrder.SortedByBytes(permuted);
                if (!SameSequence(byBytes, expected))
                {
                    return OracleVerdict.Invalid(
                        "ByteOrderMismatch",
                        "canonical byte comparison produced " + CanonicalOrder.Describe(byBytes) + " for order " + DescribeOrder(order));
                }

                compared++;
            }

            if (!CanonicalOrder.ComparisonPathsAgree(ids))
            {
                return OracleVerdict.Invalid("ComparisonPathsDisagree", "numeric and byte-lexicographic comparisons disagree");
            }

            return OracleVerdict.Valid(
                "canonical order " + CanonicalOrder.Describe(expected) + " stable across " + compared + " insertion orders");
        }

        private static OracleVerdict EvaluateHandleValidation(JsonElement parameters)
        {
            JsonElement handleElement = RequireObject(parameters, "handle");
            WorldId handleWorld = new WorldId(RequireId(handleElement, "world"));
            TargetHandle handle = new TargetHandle(
                handleWorld,
                RequireUint(handleElement, "slot"),
                RequireUlong(handleElement, "generation"));

            WorldId liveWorld = new WorldId(RequireId(parameters, "liveWorld"));
            LiveSlotRecord slot = new LiveSlotRecord(
                ParseLiveness(RequireString(parameters, "liveLiveness")),
                RequireUlong(parameters, "liveGeneration"),
                ParseCategory(RequireString(parameters, "liveCategory")));

            HandleValidationOutcome outcome = IdentityOracle.ValidateTarget(
                handle,
                liveWorld,
                RequireBool(parameters, "slotKnown"),
                slot,
                ParseCategory(RequireString(parameters, "expectedCategory")),
                OptionalUlong(parameters, "requiredActivationEpoch", 0UL),
                OptionalUlong(parameters, "providedActivationEpoch", 0UL));

            return outcome.IsValid
                ? OracleVerdict.Valid("dereference accepted")
                : OracleVerdict.Invalid(outcome.Code.ToString(), "dereference refused");
        }

        private static OracleVerdict EvaluateWorldCollision(JsonElement parameters)
        {
            Id128 worldA = RequireId(parameters, "worldA");
            Id128 worldB = RequireId(parameters, "worldB");
            TargetHandle handleA = new TargetHandle(
                new WorldId(worldA),
                RequireUint(parameters, "slotA"),
                RequireUlong(parameters, "generationA"));
            TargetHandle handleB = new TargetHandle(
                new WorldId(worldB),
                RequireUint(parameters, "slotB"),
                RequireUlong(parameters, "generationB"));

            bool expectCollide = RequireBool(parameters, "expectCollide");
            bool collide = IdentityOracle.HandlesCollide(handleA, handleB);
            if (collide != expectCollide)
            {
                return OracleVerdict.Invalid(
                    "CollisionMismatch",
                    "fixture expects collide=" + expectCollide + ", oracle observed collide=" + collide);
            }

            if (!worldA.Equals(worldB) && !IdentityOracle.RestoredWorldRejectsOldHandle(handleA, new WorldId(worldB)))
            {
                return OracleVerdict.Invalid(
                    "IncarnationNotSeparated",
                    "a handle from a different world incarnation was accepted after restore");
            }

            return OracleVerdict.Valid(
                "collide=" + collide + ", distinct world incarnations stay separated");
        }

        private static OracleVerdict EvaluateCounterAdvance(JsonElement parameters)
        {
            CounterName counter = ParseCounter(RequireString(parameters, "counter"));
            ulong current = RequireUlong(parameters, "current");
            CounterAdvance advance = CounterOracle.Advance(counter, current);

            if (!advance.Accepted)
            {
                if (advance.Wrapped || advance.Next != current)
                {
                    return OracleVerdict.Invalid(
                        "WrappedInsteadOfRejected",
                        counter + " at " + current + " produced " + advance.Next + " instead of refusing");
                }

                return OracleVerdict.Invalid(
                    CounterOracle.OverflowCode,
                    counter + " at " + current + " refused further allocation without wraparound");
            }

            return OracleVerdict.Valid(counter + " advanced to " + advance.Next);
        }

        private static OracleVerdict EvaluatePublicationAdvance(JsonElement parameters)
        {
            string phase = RequireString(parameters, "phase");
            PublicationState before = new PublicationState(
                RequireUlong(parameters, "revision"),
                RequireUlong(parameters, "epoch"),
                RequireUlong(parameters, "step"));

            PublicationAdvance advance;
            switch (phase)
            {
                case "firstPublication":
                    advance = CounterOracle.AfterFirstPublication(before);
                    break;
                case "publication":
                    advance = CounterOracle.AfterPublication(before);
                    break;
                case "noChange":
                    advance = CounterOracle.AfterNoChangeProposal(before);
                    break;
                case "committedStep":
                    advance = CounterOracle.AfterCommittedStep(before);
                    break;
                default:
                    throw new FixtureFormatException("Unknown publicationAdvance phase '" + phase + "'.");
            }

            if (!advance.Accepted)
            {
                return OracleVerdict.Invalid(advance.Code, phase + " refused at " + before);
            }

            ulong expectedRevision = RequireUlong(parameters, "expectRevision");
            ulong expectedEpoch = RequireUlong(parameters, "expectEpoch");
            ulong expectedStep = RequireUlong(parameters, "expectStep");
            PublicationState expected = new PublicationState(expectedRevision, expectedEpoch, expectedStep);
            if (advance.After.CompositionRevision != expectedRevision ||
                advance.After.AssemblyEpoch != expectedEpoch ||
                advance.After.LogicalStepId != expectedStep)
            {
                return OracleVerdict.Invalid(
                    "PublicationInvariantViolated",
                    phase + " produced " + advance.After + " but the fixture expects " + expected);
            }

            if ((phase == "firstPublication" || phase == "publication") && !advance.StepUnchanged)
            {
                return OracleVerdict.Invalid("StepChangedOnPublication", phase + " moved the logical step");
            }

            if (phase == "noChange" && CounterOracle.AnyCounterMoved(before, advance.After))
            {
                return OracleVerdict.Invalid("NoChangeMovedCounters", "a no-change proposal moved a version counter");
            }

            if (phase == "committedStep" &&
                (advance.After.CompositionRevision != before.CompositionRevision || advance.After.AssemblyEpoch != before.AssemblyEpoch))
            {
                return OracleVerdict.Invalid("CompositionMovedOnStep", "a committed step moved the revision or epoch");
            }

            return OracleVerdict.Valid(phase + " produced " + advance.After);
        }

        private static OracleVerdict EvaluateVersionSupport(JsonElement parameters)
        {
            HostProtocol host = new HostProtocol(
                RequireInt(parameters, "hostMajor"),
                RequireInt(parameters, "hostMinor"),
                OptionalIdArray(parameters, "knownFeatures"));
            ManifestProtocol manifest = new ManifestProtocol(
                RequireInt(parameters, "manifestMajor"),
                RequireInt(parameters, "minMinor"),
                RequireInt(parameters, "maxMinor"),
                OptionalIdArray(parameters, "requiredFeatures"));

            VersionDecision decision = VersionOracle.Evaluate(host, manifest);
            if (decision.Supported)
            {
                return OracleVerdict.Valid(
                    "manifest major " + manifest.Major + " minor window " + manifest.MinMinor + ".." + manifest.MaxMinor + " accepted");
            }

            if (!decision.IsRejectedWithoutFallback)
            {
                return OracleVerdict.Invalid("SilentFallback", "a mismatch was accepted without a diagnostic code");
            }

            return OracleVerdict.Invalid(decision.Code, decision.Verdict.ToString() + ": " + decision.Detail);
        }

        private static OracleVerdict EvaluateAssemblyIndependence(JsonElement parameters)
        {
            string assemblyName = RequireString(parameters, "assemblyName");
            IReadOnlyList<string> forbiddenPrefixes = OptionalStringArray(parameters, "forbiddenNamespacePrefixes");
            IReadOnlyList<string> forbiddenAssemblies = OptionalStringArray(parameters, "forbiddenAssemblyNames");
            IReadOnlyList<string> requiredReferences = OptionalStringArray(parameters, "requiredReferences");

            Assembly assembly;
            try
            {
                assembly = Assembly.Load(new AssemblyName(assemblyName));
            }
            catch (Exception error) when (error is FileNotFoundException || error is FileLoadException || error is BadImageFormatException)
            {
                return OracleVerdict.Invalid("AssemblyNotLoadable", error.Message);
            }

            List<string> references = new List<string>();
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string? name = reference.Name;
                if (name != null)
                {
                    references.Add(name);
                }
            }

            foreach (string forbidden in forbiddenAssemblies)
            {
                foreach (string reference in references)
                {
                    if (string.Equals(reference, forbidden, StringComparison.Ordinal))
                    {
                        return OracleVerdict.Invalid(
                            "GameplayOrEngineDependency",
                            assemblyName + " references forbidden assembly " + reference);
                    }
                }
            }

            foreach (string required in requiredReferences)
            {
                bool found = false;
                foreach (string reference in references)
                {
                    if (string.Equals(reference, required, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return OracleVerdict.Invalid(
                        "MissingSeamReference",
                        assemblyName + " does not reference required assembly " + required);
                }
            }

            foreach (Type type in assembly.GetExportedTypes())
            {
                string? ns = type.Namespace;
                if (ns == null)
                {
                    continue;
                }

                foreach (string prefix in forbiddenPrefixes)
                {
                    if (ns.Equals(prefix, StringComparison.Ordinal) || ns.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return OracleVerdict.Invalid(
                            "ForbiddenNamespace",
                            assemblyName + " exposes type " + type.FullName + " in forbidden namespace " + prefix);
                    }
                }
            }

            return OracleVerdict.Valid(
                assemblyName + " references " + references.Count + " assemblies, none of them engine or gameplay");
        }

        private static OracleVerdict EvaluateIdHexParsing(JsonElement parameters)
        {
            string canonical = RequireString(parameters, "canonical");
            if (!CanonicalOrder.TryParseHex(canonical, out Id128 canonicalId))
            {
                throw new FixtureFormatException("'canonical' must be 32 lowercase hex characters.");
            }

            if (!CanonicalOrder.TryParseHex(CanonicalOrder.Hex(canonicalId), out Id128 roundTrip) || !roundTrip.Equals(canonicalId))
            {
                return OracleVerdict.Invalid("HexRoundTripMismatch", "the oracle hex form does not round-trip");
            }

            if (!Id128Codec.TryParseHex(canonical, out Id128 seamId) || !seamId.Equals(canonicalId))
            {
                return OracleVerdict.Invalid("SeamRejectedCanonicalHex", "the seam rejected the canonical form " + canonical);
            }

            if (!string.Equals(Id128Codec.ToHex(canonicalId), canonical, StringComparison.Ordinal))
            {
                return OracleVerdict.Invalid("SeamHexFormDiffers", "the seam wrote " + Id128Codec.ToHex(canonicalId));
            }

            if (!parameters.TryGetProperty("candidates", out JsonElement candidates) || candidates.ValueKind != JsonValueKind.Array)
            {
                throw new FixtureFormatException("'candidates' must be an array of { text, accepted } objects.");
            }

            int index = 0;
            foreach (JsonElement candidate in candidates.EnumerateArray())
            {
                string? text = candidate.ValueKind == JsonValueKind.String ? candidate.GetString() : null;
                bool declaredAccepted = text != null;
                if (candidate.ValueKind == JsonValueKind.Object)
                {
                    text = RequireString(candidate, "text", allowEmpty: true);
                    declaredAccepted = RequireBool(candidate, "accepted");
                }

                bool oracleAccepted = CanonicalOrder.TryParseHex(text, out Id128 _);
                bool seamAccepted = Id128Codec.TryParseHex(text, out Id128 _);
                if (oracleAccepted != seamAccepted)
                {
                    return OracleVerdict.Invalid(
                        "ParserDisagreement",
                        "candidate " + index + " '" + text + "': oracle " + oracleAccepted + ", seam " + seamAccepted);
                }

                if (seamAccepted != declaredAccepted)
                {
                    return OracleVerdict.Invalid(
                        "ParserExpectationMismatch",
                        "candidate " + index + " '" + text + "' was declared accepted=" + declaredAccepted +
                        " but every parser reported accepted=" + seamAccepted);
                }

                // The strict form is the only accepted one: it must round-trip to exactly the same text.
                if (seamAccepted && (!Id128Codec.TryParseHex(text, out Id128 parsed) || !string.Equals(Id128Codec.ToHex(parsed), text, StringComparison.Ordinal)))
                {
                    return OracleVerdict.Invalid("NonCanonicalFormAccepted", "candidate " + index + " '" + text + "' is accepted but is not the canonical form");
                }

                index++;
            }

            return OracleVerdict.Valid("canonical form and " + index + " candidate forms judged identically by both parsers");
        }

        private static OracleVerdict EvaluateDiagnosticCodeText(JsonElement parameters)
        {
            if (!parameters.TryGetProperty("requiredLiterals", out JsonElement literals) || literals.ValueKind != JsonValueKind.Array)
            {
                throw new FixtureFormatException("'requiredLiterals' must be an array of code literals.");
            }

            HashSet<string> required = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement literal in literals.EnumerateArray())
            {
                string? text = literal.ValueKind == JsonValueKind.String ? literal.GetString() : null;
                if (text == null)
                {
                    throw new FixtureFormatException("'requiredLiterals' entries must be strings.");
                }

                required.Add(text);
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DiagnosticCode code in DiagnosticCodeText.Values)
            {
                string text = DiagnosticCodeText.Of(code);
                if (!seen.Add(text))
                {
                    return OracleVerdict.Invalid("DuplicateLiteral", "two enum values map to the literal " + text);
                }

                if (!DiagnosticCodeText.TryParse(text, out DiagnosticCode parsed) || parsed != code)
                {
                    return OracleVerdict.Invalid("LiteralRoundTripFailed", text + " did not parse back to " + code);
                }

                if (text != code.ToString())
                {
                    return OracleVerdict.Invalid("LiteralDiffersFromName", text + " differs from the enum name " + code);
                }

                required.Remove(text);
            }

            if (required.Count != 0)
            {
                List<string> missing = new List<string>();
                foreach (string text in required)
                {
                    missing.Add(text);
                }

                missing.Sort(StringComparer.Ordinal);
                return OracleVerdict.Invalid("RequiredLiteralMissing", "unmapped required literals: " + string.Join(", ", missing));
            }

            if (!DiagnosticCodeText.TryParse("NotARealCode", out DiagnosticCode _))
            {
                return OracleVerdict.Valid("every code literal round-trips; an unknown literal is rejected");
            }

            return OracleVerdict.Invalid("UnknownLiteralAccepted", "'NotARealCode' was accepted as a diagnostic code");
        }

        private static OracleVerdict EvaluateEnvelopeCodec(JsonElement parameters)
        {
            SchemaRef schema = new SchemaRef(
                new SchemaId(RequireId(parameters, "schemaId")),
                RequireUint(parameters, "schemaVersion"));
            string probe = RequireString(parameters, "probe");

            EnvelopeProbe result;
            switch (probe)
            {
                case "checksumRoundTrip":
                    result = EnvelopeOracle.ChecksumRoundTrip(schema, false);
                    break;
                case "checksumTamper":
                    result = EnvelopeOracle.ChecksumRoundTrip(schema, true);
                    break;
                case "fieldTypeRoundTrip":
                    result = EnvelopeOracle.FieldTypeRoundTrip(schema);
                    break;
                case "canonicalNaN":
                    result = EnvelopeOracle.CanonicalNaN(schema);
                    break;
                case "unknownRequiredFeature":
                    result = EnvelopeOracle.UnknownRequiredFeature(schema, false);
                    break;
                case "knownRequiredFeature":
                    result = EnvelopeOracle.UnknownRequiredFeature(schema, true);
                    break;
                case "writerFieldIdZero":
                    result = EnvelopeOracle.WriterFieldId(schema, 0);
                    break;
                case "writerFieldIdTooLarge":
                    result = EnvelopeOracle.WriterFieldId(schema, unchecked((int)0x80000000));
                    break;
                case "writerListHeaderOverLimit":
                    result = EnvelopeOracle.WriterListHeader(schema, 70000, 0);
                    break;
                case "writerListBytesOverLimit":
                    result = EnvelopeOracle.WriterListHeader(schema, 1, 600000);
                    break;
                case "readerBoolByte":
                case "readerFieldIdOutOfRange":
                case "readerListCountExceeded":
                case "readerListTooLarge":
                case "readerTruncatedField":
                    result = ReadRawEnvelopeProbe(probe, schema, parameters);
                    break;
                default:
                    throw new FixtureFormatException("Unknown envelopeCodec probe '" + probe + "'.");
            }

            return result.Accepted
                ? OracleVerdict.Valid(result.Detail)
                : OracleVerdict.Invalid(result.Code, result.Detail);
        }

        private static EnvelopeProbe ReadRawEnvelopeProbe(string probe, SchemaRef schema, JsonElement parameters)
        {
            byte[] header = RawEnvelopeBuilder.Header(schema);
            switch (probe)
            {
                case "readerBoolByte":
                    return EnvelopeOracle.ReadFirstField(
                        RawEnvelopeBuilder.Concat(header, RawEnvelopeBuilder.Field(1U, WireType.Bool, new[] { (byte)2 })),
                        schema);
                case "readerFieldIdOutOfRange":
                    return EnvelopeOracle.ReadFirstField(
                        RawEnvelopeBuilder.Concat(
                            header,
                            RawEnvelopeBuilder.Field(0x80000000U, WireType.UInt32, RawEnvelopeBuilder.UInt32Payload(1U))),
                        schema);
                case "readerListCountExceeded":
                    return EnvelopeOracle.ReadFirstField(
                        RawEnvelopeBuilder.Concat(
                            header,
                            RawEnvelopeBuilder.Field(
                                1U,
                                WireType.List,
                                RawEnvelopeBuilder.ListPayload((uint)RequireUint(parameters, "declaredCount"), 0U))),
                        schema);
                case "readerListTooLarge":
                    return EnvelopeOracle.ReadFirstField(
                        RawEnvelopeBuilder.Concat(
                            header,
                            RawEnvelopeBuilder.Field(
                                1U,
                                WireType.List,
                                RawEnvelopeBuilder.ListPayload(1U, (uint)RequireUint(parameters, "declaredBytes")))),
                        schema);
                case "readerTruncatedField":
                    return EnvelopeOracle.ReadFirstField(
                        RawEnvelopeBuilder.Concat(
                            header,
                            RawEnvelopeBuilder.Field(
                                1U,
                                WireType.Bytes,
                                RawEnvelopeBuilder.BytesPayload(4, 0xAB),
                                64)),
                        schema);
                default:
                    throw new FixtureFormatException("Unknown raw envelope probe '" + probe + "'.");
            }
        }

        private static List<int[]> ReadPermutations(JsonElement parameters, int idCount)
        {
            List<int[]> orders = new List<int[]>();
            if (parameters.TryGetProperty("permutations", out JsonElement permutations) && permutations.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in permutations.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Array)
                    {
                        throw new FixtureFormatException("'permutations' entries must be arrays of id indices.");
                    }

                    int[] order = new int[idCount];
                    int index = 0;
                    foreach (JsonElement item in entry.EnumerateArray())
                    {
                        if (index >= idCount || item.ValueKind != JsonValueKind.Number)
                        {
                            throw new FixtureFormatException("A permutation must name exactly " + idCount + " numeric indices.");
                        }

                        order[index] = item.GetInt32();
                        index++;
                    }

                    if (index != idCount)
                    {
                        throw new FixtureFormatException("A permutation must name exactly " + idCount + " numeric indices.");
                    }

                    orders.Add(order);
                }

                return orders;
            }

            if (idCount > 6)
            {
                throw new FixtureFormatException(
                    "Cases with more than six ids must list explicit 'permutations' (" + idCount + " ids were given).");
            }

            if (idCount <= 1)
            {
                orders.Add(new[] { 0 });
                return orders;
            }

            List<int[]> generated = new List<int[]>();
            AllPermutations(new int[idCount], 0, generated);
            return generated;
        }

        private static void AllPermutations(int[] buffer, int position, List<int[]> into)
        {
            if (position == buffer.Length)
            {
                int[] copy = new int[buffer.Length];
                Array.Copy(buffer, copy, buffer.Length);
                into.Add(copy);
                return;
            }

            for (int candidate = 0; candidate < buffer.Length; candidate++)
            {
                if (ContainsIndex(buffer, position, candidate))
                {
                    continue;
                }

                buffer[position] = candidate;
                AllPermutations(buffer, position + 1, into);
            }
        }

        private static bool ContainsIndex(int[] buffer, int length, int candidate)
        {
            for (int i = 0; i < length; i++)
            {
                if (buffer[i] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private static Id128[] ApplyPermutation(IReadOnlyList<Id128> ids, int[] order)
        {
            Id128[] permuted = new Id128[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                int source = order[i];
                if (source < 0 || source >= ids.Count)
                {
                    throw new FixtureFormatException("Permutation index " + source + " is outside the id array.");
                }

                permuted[i] = ids[source];
            }

            return permuted;
        }

        private static bool SameSequence(IReadOnlyList<Id128> left, IReadOnlyList<Id128> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string DescribeOrder(int[] order)
        {
            string[] parts = new string[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                parts[i] = order[i].ToString(CultureInfo.InvariantCulture);
            }

            return "[" + string.Join(",", parts) + "]";
        }

        private static SlotLiveness ParseLiveness(string text)
        {
            switch (text)
            {
                case "live":
                    return SlotLiveness.Live;
                case "retired":
                    return SlotLiveness.Retired;
                default:
                    throw new FixtureFormatException("Unknown liveLiveness '" + text + "'; expected 'live' or 'retired'.");
            }
        }

        private static HandleCategory ParseCategory(string text)
        {
            switch (text)
            {
                case "target":
                    return HandleCategory.Target;
                case "scope":
                    return HandleCategory.Scope;
                case "plugin":
                    return HandleCategory.Plugin;
                default:
                    throw new FixtureFormatException("Unknown handle category '" + text + "'; expected target, scope or plugin.");
            }
        }

        private static CounterName ParseCounter(string text)
        {
            if (Enum.TryParse(text, false, out CounterName parsed) && Enum.IsDefined(typeof(CounterName), parsed))
            {
                return parsed;
            }

            throw new FixtureFormatException("Unknown counter '" + text + "'.");
        }

        private static JsonElement RequireObject(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
            {
                throw new FixtureFormatException("'" + name + "' must be an object.");
            }

            return value;
        }

        private static string RequireString(JsonElement parameters, string name, bool allowEmpty = false)
        {
            if (!parameters.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                throw new FixtureFormatException("'" + name + "' must be " + (allowEmpty ? "a string." : "a non-empty string."));
            }

            string? text = value.GetString();
            if (text == null || (!allowEmpty && text.Length == 0))
            {
                throw new FixtureFormatException("'" + name + "' must be a non-empty string.");
            }

            return text;
        }

        private static bool RequireBool(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out JsonElement value) ||
                (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
            {
                throw new FixtureFormatException("'" + name + "' must be a boolean.");
            }

            return value.GetBoolean();
        }

        private static int RequireInt(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            {
                throw new FixtureFormatException("'" + name + "' must be a number.");
            }

            return value.GetInt32();
        }

        private static uint RequireUint(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            {
                throw new FixtureFormatException("'" + name + "' must be a number.");
            }

            if (!value.TryGetUInt32(out uint number))
            {
                throw new FixtureFormatException("'" + name + "' must be an unsigned 32-bit value.");
            }

            return number;
        }

        private static ulong RequireUlong(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out JsonElement value))
            {
                throw new FixtureFormatException("'" + name + "' is required.");
            }

            return ReadUlong(value, name);
        }

        private static ulong OptionalUlong(JsonElement parameters, string name, ulong fallback) =>
            parameters.TryGetProperty(name, out JsonElement value) ? ReadUlong(value, name) : fallback;

        private static ulong ReadUlong(JsonElement value, string name)
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out ulong numeric))
            {
                return numeric;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                if (text != null && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed))
                {
                    return parsed;
                }
            }

            throw new FixtureFormatException(
                "'" + name + "' must be an unsigned 64-bit value, written as a number or a decimal string.");
        }

        private static Id128 RequireId(JsonElement parameters, string name)
        {
            string text = RequireString(parameters, name);
            if (!CanonicalOrder.TryParseHex(text, out Id128 id))
            {
                throw new FixtureFormatException("'" + name + "' must be 32 hexadecimal characters.");
            }

            return id;
        }

        private static IReadOnlyList<Id128> RequireIdArray(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                throw new FixtureFormatException("'" + name + "' must be an array of ids.");
            }

            List<Id128> ids = new List<Id128>();
            foreach (JsonElement item in value.EnumerateArray())
            {
                string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (text == null || !CanonicalOrder.TryParseHex(text, out Id128 id))
                {
                    throw new FixtureFormatException("'" + name + "' entries must be 32 hexadecimal characters.");
                }

                ids.Add(id);
            }

            if (ids.Count == 0)
            {
                throw new FixtureFormatException("'" + name + "' must not be empty.");
            }

            return ids;
        }

        private static IReadOnlyList<string> OptionalStringArray(JsonElement parameters, string name)
        {
            if (!parameters.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            List<string> values = new List<string>();
            foreach (JsonElement item in value.EnumerateArray())
            {
                string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (text == null)
                {
                    throw new FixtureFormatException("'" + name + "' entries must be strings.");
                }

                values.Add(text);
            }

            return values;
        }

        private static IReadOnlyList<Id128> OptionalIdArray(JsonElement parameters, string name) =>
            parameters.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
                ? RequireIdArray(parameters, name)
                : Array.Empty<Id128>();
    }
}
