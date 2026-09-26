// Production contract tests (GC-003). Unity-free, engine-free, deterministic.
//
// Coverage: catalog build/validation rules (P-009, P-028, P-055), canonical ordering (P-008), generated
// serializer binding and envelope validation (P-054, P-055), manifest validation diagnostics (P-009, P-012,
// P-021, P-032, P-039, P-040, P-043), key derivation (P-004) and the API-compatibility gate against the frozen
// W0 reference-seam snapshot.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.ApiSnapshot;
using GameCore.Contracts;
using GameCore.ProtocolFixtures;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    [TestFixture]
    public sealed class CatalogBuildTests
    {
        private static readonly Id128 PackageA = new Id128(0x1111111111111111UL, 0x2222222222222222UL);
        private static readonly Id128 FeatureA = new Id128(0x5555555555555555UL, 0x6666666666666666UL);
        private static readonly Id128 ImplA = new Id128(0x7777777777777777UL, 0x8888888888888888UL);
        private static readonly Id128 ImplB = new Id128(0x9999999999999999UL, 0xAAAAAAAAAAAAAAAAUL);
        private static readonly Id128 SchemaA = new Id128(0xBBBBBBBBBBBBBBBBUL, 0xCCCCCCCCCCCCCCCCUL);
        private static readonly SchemaRef SchemaRefA = new SchemaRef(new SchemaId(SchemaA), 1U);
        private static readonly SchemaRef UnsupportedSchemaRefA = new SchemaRef(new SchemaId(SchemaA), 2U);
        private static readonly Id128 UnknownSchema = new Id128(0xDDDDDDDDDDDDDDDDUL, 0xEEEEEEEEEEEEEEEEUL);
        private static readonly Id128 KeyA = new Id128(0x0101010101010101UL, 0x0202020202020202UL);
        private static readonly Id128 KeyB = new Id128(0x0303030303030303UL, 0x0404040404040404UL);
        private static readonly Id128 KeyC = new Id128(0x0505050505050505UL, 0x0606060606060606UL);

        private static FactoryRegistration Factory(Id128 key, FactoryKind kind, Id128 implementation)
            => new FactoryRegistration(new FactoryKey(key, 1U), kind, PackageA, implementation, 1U);

        private static SchemaRegistration SchemaRegistration(FactoryKey serializer, bool required)
            => new SchemaRegistration(SchemaRefA, PackageA, serializer, required);

        private static SchemaRegistration SchemaRegistrationAt(SchemaRef schema, FactoryKey serializer, bool required)
            => new SchemaRegistration(schema, PackageA, serializer, required);

        private static TestSerializer Serializer(FactoryKey key, GeneratedFieldSlot[] fields)
            => new TestSerializer(key, SchemaRefA, new[] { FeatureA }, fields);

        [Test]
        public void BuildAcceptsConsistentTablesAndReportsNoDiagnostics()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(
                new[]
                {
                    Factory(KeyA, FactoryKind.PluginFactory, ImplA),
                    Factory(KeyB, FactoryKind.Serializer, SchemaA),
                },
                new[] { SchemaRegistration(new FactoryKey(KeyB, 1U), true) },
                new[] { FeatureA },
                new[] { Serializer(new FactoryKey(KeyB, 1U), TestSerializer.DefaultFields) });

            Assert.That(result.Succeeded, Is.True, result.Describe());
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Catalog!.Factories.Count, Is.EqualTo(2));
            Assert.That(result.Catalog.Schemas.Count, Is.EqualTo(1));
            Assert.That(result.Catalog.Serializers.Count, Is.EqualTo(1));
            Assert.That(result.Catalog.SupportedFeatureIds.Count, Is.EqualTo(1));
            Assert.That(result.Catalog.SupportsFeature(FeatureA), Is.True);
            Assert.That(
                result.Catalog.SupportsFeature(new Id128(0x5555555555555555UL, 0x6666666666666667UL)),
                Is.False,
                "a feature the build does not declare must not be reported as supported");
        }

        [Test]
        public void EmptyCatalogIsLegalAndHasAStableFingerprint()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(null, null, null, null);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Catalog!.FactoryKeysInCanonicalOrder(), Is.Empty);
            Assert.That(result.Catalog.Fingerprint.IsEmpty, Is.False, "an empty catalog still has a content hash");
            Assert.That(
                result.Catalog.Fingerprint,
                Is.EqualTo(CatalogFingerprint.Compute(null, null, null)),
                "the fingerprint must be reproducible");
        }

        [Test]
        public void DuplicateFactoryKeyRejectsWithOwnershipConflict()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(
                new[]
                {
                    Factory(KeyA, FactoryKind.PluginFactory, ImplA),
                    Factory(KeyA, FactoryKind.SystemFactory, ImplB),
                },
                null,
                null,
                null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(CodesOf(result), Does.Contain(DiagnosticCode.OwnershipConflict));
            Assert.That(result.Describe(), Does.Contain("duplicate factory key"));
        }

        [Test]
        public void OneKeyIdentityAtTwoVersionsRejects()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(
                new[]
                {
                    new FactoryRegistration(new FactoryKey(KeyA, 1U), FactoryKind.PluginFactory, PackageA, ImplA, 1U),
                    new FactoryRegistration(new FactoryKey(KeyA, 2U), FactoryKind.PluginFactory, PackageA, ImplA, 2U),
                },
                null,
                null,
                null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(CodesOf(result), Does.Contain(DiagnosticCode.OwnershipConflict));
        }

        [Test]
        public void DefaultZeroKeyRejectsAsMissingIdentity()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(
                new[] { Factory(default(Id128), FactoryKind.PluginFactory, ImplA) },
                null,
                null,
                null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(CodesOf(result), Does.Contain(DiagnosticCode.MissingDependency));
        }

        [Test]
        public void SchemaWithoutRegisteredSerializerRejectsAsMissingDependency()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(
                new[] { Factory(KeyA, FactoryKind.PluginFactory, ImplA) },
                new[] { SchemaRegistration(new FactoryKey(KeyB, 1U), true) },
                null,
                null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(CodesOf(result), Does.Contain(DiagnosticCode.MissingDependency));
            Assert.That(result.Describe(), Does.Contain("missing precompiled serializer"));
        }

        [Test]
        public void SerializerServingAnotherSchemaRejects()
        {
            var otherSchema = new SchemaRef(new SchemaId(UnknownSchema), 1U);
            CatalogBuildResult result = ImmutableCatalog.Build(
                new[] { Factory(KeyB, FactoryKind.Serializer, UnknownSchema) },
                new[] { SchemaRegistration(new FactoryKey(KeyB, 1U), true) },
                null,
                new[] { new TestSerializer(new FactoryKey(KeyB, 1U), otherSchema, null, TestSerializer.DefaultFields) });

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Describe(), Does.Contain("serves"));
        }

        [Test]
        public void UnregisteredSerializerRejects()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(
                null,
                new[] { SchemaRegistration(new FactoryKey(KeyB, 1U), true) },
                null,
                null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(CodesOf(result), Does.Contain(DiagnosticCode.MissingDependency));
        }

        [Test]
        public void LookupMissReportsMissingDependencyAndNeverSubstitutes()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(
                new[]
                {
                    Factory(KeyA, FactoryKind.PluginFactory, ImplA),
                    Factory(KeyB, FactoryKind.Serializer, SchemaA),
                },
                new[] { SchemaRegistration(new FactoryKey(KeyB, 1U), true) },
                null,
                new[] { Serializer(new FactoryKey(KeyB, 1U), TestSerializer.DefaultFields) });
            Assert.That(result.Succeeded, Is.True, result.Describe());
            ImmutableCatalog catalog = result.Catalog!;

            CatalogLookup hit = catalog.Lookup(new FactoryKey(KeyA, 1U));
            Assert.That(hit.Found, Is.True);
            Assert.That(hit.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(hit.Factory!.Kind, Is.EqualTo(FactoryKind.PluginFactory));

            CatalogLookup mismatchVersion = catalog.Lookup(new FactoryKey(KeyA, 4U));
            Assert.That(mismatchVersion.Found, Is.False);
            Assert.That(mismatchVersion.Code, Is.EqualTo(DiagnosticCode.MissingDependency));

            CatalogLookup unknown = catalog.Lookup(new FactoryKey(KeyC, 1U));
            Assert.That(unknown.Found, Is.False);
            Assert.That(unknown.Code, Is.EqualTo(DiagnosticCode.MissingDependency));

            CatalogLookup defaultKey = catalog.Lookup(default(FactoryKey));
            Assert.That(defaultKey.Found, Is.False);
            Assert.That(defaultKey.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
        }

        [Test]
        public void SchemaLookupDistinguishesUnknownIdentityFromUnacceptedVersion()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(
                new[] { Factory(KeyB, FactoryKind.Serializer, SchemaA) },
                new[] { SchemaRegistration(new FactoryKey(KeyB, 1U), true) },
                null,
                new[] { Serializer(new FactoryKey(KeyB, 1U), TestSerializer.DefaultFields) });
            Assert.That(result.Succeeded, Is.True, result.Describe());
            ImmutableCatalog catalog = result.Catalog!;

            Assert.That(catalog.LookupSchema(SchemaRefA).Found, Is.True);

            CatalogLookup wrongVersion = catalog.LookupSchema(UnsupportedSchemaRefA);
            Assert.That(wrongVersion.Found, Is.False);
            Assert.That(wrongVersion.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));

            CatalogLookup unknownIdentity = catalog.LookupSchema(new SchemaRef(new SchemaId(UnknownSchema), 1U));
            Assert.That(unknownIdentity.Found, Is.False);
            Assert.That(unknownIdentity.Code, Is.EqualTo(DiagnosticCode.MissingDependency));

            Assert.That(catalog.TryGetSerializer(SchemaRefA, out ISchemaSerializer? bound), Is.True);
            Assert.That(bound!.Key, Is.EqualTo(new FactoryKey(KeyB, 1U)));
            Assert.That(catalog.TryGetSerializer(UnsupportedSchemaRefA, out ISchemaSerializer? _), Is.False);
        }

        [Test]
        public void CanonicalOrderIsIndependentOfRegistrationOrder()
        {
            FactoryRegistration[] forward =
            {
                Factory(KeyA, FactoryKind.PluginFactory, ImplA),
                Factory(KeyB, FactoryKind.PluginFactory, ImplB),
                Factory(KeyC, FactoryKind.PluginFactory, ImplA),
            };
            var reversed = new List<FactoryRegistration>(forward);
            reversed.Reverse();
            var shuffled = new List<FactoryRegistration> { forward[1], forward[2], forward[0] };

            CatalogBuildResult a = ImmutableCatalog.Build(forward, null, null, null);
            CatalogBuildResult b = ImmutableCatalog.Build(reversed, null, null, null);
            CatalogBuildResult c = ImmutableCatalog.Build(shuffled, null, null, null);

            Assert.That(a.Succeeded && b.Succeeded && c.Succeeded, Is.True);
            Assert.That(a.Catalog!.Fingerprint, Is.EqualTo(b.Catalog!.Fingerprint));
            Assert.That(a.Catalog.Fingerprint, Is.EqualTo(c.Catalog!.Fingerprint));
            Assert.That(
                Describe(a.Catalog.FactoryKeysInCanonicalOrder()),
                Is.EqualTo(Describe(new[] { new FactoryKey(KeyA, 1U), new FactoryKey(KeyB, 1U), new FactoryKey(KeyC, 1U) })));
        }

        [Test]
        public void UnsupportedFeatureDeclarationIsReportedAsMissingIdentity()
        {
            CatalogBuildResult result = ImmutableCatalog.Build(null, null, new[] { default(Id128) }, null);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(CodesOf(result), Does.Contain(DiagnosticCode.MissingDependency));
        }

        internal static List<DiagnosticCode> CodesOf(CatalogBuildResult result)
        {
            List<DiagnosticCode> codes = new List<DiagnosticCode>();
            for (int i = 0; i < result.Diagnostics.Count; i++)
            {
                codes.Add(result.Diagnostics[i].Code);
                Assert.That(result.Diagnostics[i].CodeText, Is.Not.Empty);
                Assert.That(result.Diagnostics[i].Phase, Is.EqualTo(OperationPhase.Validation));
            }

            return codes;
        }

        private static string Describe(IReadOnlyList<FactoryKey> keys)
        {
            string[] parts = new string[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                parts[i] = keys[i].RegistrationKey.ToString();
            }

            return string.Join(",", parts);
        }
    }

    /// <summary>
    /// Minimal serializer used by the catalog tests. It exercises the same base class generated serializers
    /// derive from, so the shared envelope rules are covered without compiling generated source.
    /// </summary>
    internal sealed class TestSerializer : GeneratedSerializerBase
    {
        internal static readonly GeneratedFieldSlot[] DefaultFields =
        {
            new GeneratedFieldSlot(1, WireType.UInt64, true),
            new GeneratedFieldSlot(2, WireType.Int32, false),
        };

        private readonly GeneratedFieldSlot[] fields;

        internal TestSerializer(FactoryKey key, SchemaRef schema, IReadOnlyList<Id128>? features, GeneratedFieldSlot[] fields)
            : base(key, schema, features)
        {
            this.fields = fields;
        }

        protected override IReadOnlyList<GeneratedFieldSlot> DeclaredFields => fields;

        /// <summary>Writes one canonical document, then appends the trailing checksum.</summary>
        internal byte[] Write(ulong required, int optional)
        {
            var writer = new EnvelopeWriter(new EnvelopeHeader(1, 0, Schema, KnownFeatureIds));
            writer.WriteUInt64Field(1, required);
            writer.WriteInt32Field(2, optional);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        /// <summary>Writes a document that omits the optional field.</summary>
        internal byte[] WriteRequiredOnly(ulong required)
        {
            var writer = new EnvelopeWriter(new EnvelopeHeader(1, 0, Schema, KnownFeatureIds));
            writer.WriteUInt64Field(1, required);
            writer.WriteChecksum();
            return writer.ToArray();
        }
    }

    [TestFixture]
    public sealed class GeneratedSerializerContractTests
    {
        private static readonly Id128 SerializerKey = new Id128(0x0A0A0A0A0A0A0A0AUL, 0x0B0B0B0B0B0B0B0BUL);
        private static readonly Id128 Feature = new Id128(0x0C0C0C0C0C0C0C0CUL, 0x0D0D0D0D0D0D0D0DUL);
        private static readonly SchemaRef Schema = new SchemaRef(new SchemaId(new Id128(0x0E0E0E0E0E0E0E0EUL, 0x0F0F0F0F0F0F0F0FUL)), 1U);
        private static readonly SchemaRef OtherSchema = new SchemaRef(
            new SchemaId(new Id128(0x0E0E0E0E0E0E0E0FUL, 0x0F0F0F0F0F0F0F0FUL)),
            1U);

        private static TestSerializer NewSerializer(IReadOnlyList<Id128>? features = null) =>
            new TestSerializer(new FactoryKey(SerializerKey, 1U), Schema, features ?? new[] { Feature }, TestSerializer.DefaultFields);

        [Test]
        public void ValidDocumentPassesValidationAndTamperingIsDetected()
        {
            TestSerializer serializer = NewSerializer();
            byte[] document = serializer.Write(0x0102030405060708UL, -7);

            Assert.That(serializer.TryValidate(document, out EnvelopeError error), Is.True, error.ToString());
            Assert.That(error, Is.EqualTo(EnvelopeError.None));

            byte[] tampered = (byte[])document.Clone();
            tampered[document.Length - 2] ^= 0x01;
            Assert.That(serializer.TryValidate(tampered, out EnvelopeError tamperedError), Is.False);
            Assert.That(tamperedError, Is.EqualTo(EnvelopeError.ChecksumMismatch));
        }

        [Test]
        public void MissingRequiredFieldRejectsAndMissingOptionalFieldIsAccepted()
        {
            TestSerializer serializer = NewSerializer();
            byte[] complete = serializer.Write(0x21UL, 5);
            byte[] requiredOnly = serializer.WriteRequiredOnly(0x21UL);

            Assert.That(serializer.TryValidate(complete, out EnvelopeError _), Is.True);
            Assert.That(serializer.TryValidate(requiredOnly, out EnvelopeError _), Is.True);

            var writer = new EnvelopeWriter(new EnvelopeHeader(1, 0, Schema, new[] { Feature }));
            writer.WriteInt32Field(2, 5);
            writer.WriteChecksum();
            byte[] missingRequired = writer.ToArray();

            Assert.That(serializer.TryValidate(missingRequired, out EnvelopeError error), Is.False);
            Assert.That(error, Is.EqualTo(EnvelopeError.MissingRequiredField));
        }

        [Test]
        public void DuplicateDeclaredFieldRejects()
        {
            TestSerializer serializer = NewSerializer();
            var writer = new EnvelopeWriter(new EnvelopeHeader(1, 0, Schema, new[] { Feature }));
            writer.WriteUInt64Field(1, 1UL);
            writer.WriteUInt64Field(1, 2UL);
            writer.WriteChecksum();

            Assert.That(serializer.TryValidate(writer.ToArray(), out EnvelopeError error), Is.False);
            Assert.That(error, Is.EqualTo(EnvelopeError.DuplicateField));
        }

        [Test]
        public void UnknownOptionalFieldIsSkippedAndDocumentStillValidates()
        {
            TestSerializer serializer = NewSerializer();
            var writer = new EnvelopeWriter(new EnvelopeHeader(1, 0, Schema, new[] { Feature }));
            writer.WriteUInt64Field(1, 9UL);
            writer.WriteUtf8Field(99, "extension");
            writer.WriteChecksum();

            Assert.That(serializer.TryValidate(writer.ToArray(), out EnvelopeError error), Is.True, error.ToString());
        }

        [Test]
        public void DocumentsForAnotherSchemaOrUnknownFeatureReject()
        {
            TestSerializer serializer = NewSerializer();

            var foreign = new EnvelopeWriter(new EnvelopeHeader(1, 0, OtherSchema, new[] { Feature }));
            foreign.WriteUInt64Field(1, 1UL);
            foreign.WriteChecksum();
            Assert.That(serializer.TryValidate(foreign.ToArray(), out EnvelopeError schemaError), Is.False);
            Assert.That(schemaError, Is.EqualTo(EnvelopeError.UnsupportedVersion));

            TestSerializer withoutFeature = NewSerializer(Array.Empty<Id128>());
            byte[] document = serializer.Write(1UL, 2);
            Assert.That(withoutFeature.TryValidate(document, out EnvelopeError featureError), Is.False);
            Assert.That(featureError, Is.EqualTo(EnvelopeError.UnknownRequiredFeature));
        }

        [Test]
        public void TruncatedAndTrailingBytesReject()
        {
            TestSerializer serializer = NewSerializer();
            byte[] document = serializer.Write(3UL, 4);

            byte[] truncated = new byte[document.Length - 1];
            Buffer.BlockCopy(document, 0, truncated, 0, truncated.Length);
            Assert.That(serializer.TryValidate(truncated, out EnvelopeError truncationError), Is.False);
            Assert.That(truncationError, Is.EqualTo(EnvelopeError.Truncated));

            byte[] extended = new byte[document.Length + 1];
            Buffer.BlockCopy(document, 0, extended, 0, document.Length);
            Assert.That(serializer.TryValidate(extended, out EnvelopeError trailingError), Is.False);
            Assert.That(trailingError, Is.EqualTo(EnvelopeError.FieldLengthMismatch));
        }

        [Test]
        public void DeclaredFieldBufferRecordsPresentFieldsForForwardReads()
        {
            TestSerializer serializer = NewSerializer();
            byte[] document = serializer.Write(0x42UL, 7);

            GeneratedEnvelopeReader.TryReadDeclaredFields(
                document,
                Schema,
                new[] { Feature },
                TestSerializer.DefaultFields,
                new GeneratedFieldBuffer(),
                out EnvelopeError error);
            Assert.That(error, Is.EqualTo(EnvelopeError.None));

            var reader = new EnvelopeReader(document);
            Assert.That(reader.TryReadHeader(new[] { Feature }, out EnvelopeHeader _), Is.True);
            Assert.That(reader.TryReadField(out EnvelopeField first), Is.True);
            Assert.That(first.FieldId, Is.EqualTo(1));
            Assert.That(reader.TryReadUInt64(first, out ulong value), Is.True);
            Assert.That(value, Is.EqualTo(0x42UL));

            HashSet<int> seen = new HashSet<int>();
            while (reader.TryReadField(out EnvelopeField field))
            {
                if (field.IsChecksum)
                {
                    Assert.That(reader.TryVerifyChecksum(field, out ulong _), Is.True);
                    break;
                }

                Assert.That(seen.Add(field.FieldId), Is.True, "a declared field may appear once");
                Assert.That(reader.Skip(field), Is.True);
            }

            Assert.That(seen, Is.EquivalentTo(new[] { 2 }));
        }
    }

    /// <summary>
    /// Pins the cancellation outcomes the W0 interface gate added for P-050. No test in this project would fail
    /// if a member were renumbered, so the numbers are asserted explicitly: a caller switches on them, and a
    /// silent renumber would change behaviour without changing a name.
    /// </summary>
    [TestFixture]
    public sealed class CancelOutcomeContractTests
    {
        [Test]
        public void CancelOutcomeCarriesTheIdempotencyConflictAndRejectedValues()
        {
            Assert.That((int)CancelOutcome.Cancelled, Is.EqualTo(0));
            Assert.That((int)CancelOutcome.TooLate, Is.EqualTo(1));
            Assert.That((int)CancelOutcome.Unknown, Is.EqualTo(2));
            Assert.That((int)CancelOutcome.ResultExpired, Is.EqualTo(3));

            // Added by the seam change on main; the original ledger row is kept on a conflict (P-050).
            Assert.That((int)CancelOutcome.IdempotencyConflict, Is.EqualTo(4));
            Assert.That((int)CancelOutcome.Rejected, Is.EqualTo(5));

            Assert.That(Enum.GetValues(typeof(CancelOutcome)).Length, Is.EqualTo(6));
            Assert.That(Enum.IsDefined(typeof(CancelOutcome), CancelOutcome.IdempotencyConflict), Is.True);
            Assert.That(Enum.IsDefined(typeof(CancelOutcome), CancelOutcome.Rejected), Is.True);
        }
    }

    [TestFixture]
    public sealed class IdentityDerivationTests
    {
        [Test]
        public void DerivationMatchesTheCommittedProbeKeyLiterals()
        {
            // These literals are the ones the GC-001 probe committed and the generated probe catalog embeds,
            // so this test fails if the derivation rule changes without the player being regenerated (P-004).
            Assert.That(
                StableNameKeyDerivation.Derive("gamecore.validation.plugin.fixture"),
                Is.EqualTo(new Id128(0x0284B6EC6D41B5AAUL, 0xD17744CF859C74B6UL)));
            Assert.That(
                StableNameKeyDerivation.Derive("gamecore.validation.handler.magnitude"),
                Is.EqualTo(new Id128(0x1C9A1F368FDAD8DDUL, 0x4E0CC4C0E9F7DA67UL)));
        }

        [Test]
        public void DerivationIsDeterministicAndRejectsNonCanonicalNames()
        {
            Assert.That(
                StableNameKeyDerivation.Derive("a.example.plugin"),
                Is.EqualTo(StableNameKeyDerivation.Derive("a.example.plugin")));

            Assert.Throws<ArgumentException>(() => StableNameKeyDerivation.Derive("A.Example.Plugin"));
            Assert.Throws<ArgumentException>(() => StableNameKeyDerivation.Derive(".leading"));
            Assert.Throws<ArgumentException>(() => StableNameKeyDerivation.Derive("trailing."));
            Assert.Throws<ArgumentException>(() => StableNameKeyDerivation.Derive("double..dot"));
            Assert.Throws<ArgumentException>(() => StableNameKeyDerivation.Derive("space here"));
            Assert.Throws<ArgumentNullException>(() => StableNameKeyDerivation.Derive(null!));
            Assert.That(StableNameKeyDerivation.IsCanonicalStableName("a_b-c.d1"), Is.True);
        }
    }

    [TestFixture]
    public sealed class ApiCompatibilityTests
    {
        private const string SurfaceNamespace = "GameCore.Contracts";

        /// <summary>
        /// The members GC-012 added to <c>StateSlotSpec</c> (P-032's manifest-supported reset): an additive
        /// constructor overload and the two read-only properties it assigns. Matched by shape rather than by a full
        /// signature, because the snapshot generator qualifies parameter types and renders default values in its own
        /// way. The declaring type is rendered with its short name, as the frozen listing shows
        /// ("ctor public StateSlotSpec(...)").
        /// </summary>
        private static bool IsStateSlotResetAddition(string member) =>
            (member.StartsWith("ctor public StateSlotSpec(", StringComparison.Ordinal)
                && member.Contains("System.Boolean resetSupported", StringComparison.Ordinal)
                && member.Contains("System.String? resetReason", StringComparison.Ordinal))
            || member == "property public System.Boolean ResetSupported { get; }"
            || member == "property public System.String ResetReason { get; }";

        private const string RegenerationHint =
            "The production listing is a superset of the frozen W0 snapshot. If a line was intentionally " +
            "removed or changed, the change must go through the W0 interface gate: regenerate the frozen " +
            "surface in tests/GameCore.ReferenceSeams and record the decision in artifacts/gc-003/HANDOFF.md.";

        [Test]
        public void ProductionSurfaceIsAStrictSupersetOfTheFrozenSeamSnapshot()
        {
            string root = RepoLayout.FindRoot();
            string snapshotPath = RepoLayout.Resolve(root, RepoLayout.ApiSnapshotPath);
            Assert.That(File.Exists(snapshotPath), Is.True, "Frozen API snapshot is missing: " + snapshotPath);

            string frozen = File.ReadAllText(snapshotPath);
            string production = ApiSnapshotGenerator.Generate(typeof(Id128).Assembly, SurfaceNamespace);
            ApiCompatibilityResult comparison = ApiSurfaceComparer.Compare(frozen, production);

            Assert.That(
                comparison.FrozenTypes,
                Is.GreaterThan(100),
                "The frozen snapshot must describe the real reference surface.");

            Assert.That(
                comparison.Compatible,
                Is.True,
                "The production contract surface removed or changed a frozen line.\n" +
                comparison.Describe() + "\n" + RegenerationHint);

            Assert.That(
                comparison.ProductionTypes,
                Is.GreaterThanOrEqualTo(comparison.FrozenTypes),
                "A superset cannot have fewer types than the frozen surface.");

            // Documented GC-003 catalog additions and GC-015 state-disposition additions are the only permitted drift.
            // GC-018 adds the checkpoint surface on top: the envelope reader/writer stay frozen, but the record
            // value types the checkpoint catalog serializes, their codec set, document, identity table, migration
            // plan and the CanonicalId32 four-word collation are new exported types (P-053, P-054, 05 s6) in
            // artifacts/gc-018/HANDOFF.md; additions outside this set remain refused.
            var addedTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                "type class GameCore.Contracts.BoundRegistration<TImplementation>",
                "type class GameCore.Contracts.CatalogBuildResult",
                "type class GameCore.Contracts.CatalogFingerprint [static]",
                "type class GameCore.Contracts.GeneratedEnvelopeReader [static]",
                "type class GameCore.Contracts.GeneratedFieldBuffer",
                "type class GameCore.Contracts.GeneratedSerializerBase : GameCore.Contracts.ISchemaSerializer",
                "type class GameCore.Contracts.ImmutableCatalog : GameCore.Contracts.ICatalog",
                "type class GameCore.Contracts.ManifestValidationReport",
                "type class GameCore.Contracts.ManifestValidator [static]",
                "type class GameCore.Contracts.StableNameKeyDerivation [static]",
                "type interface GameCore.Contracts.ISchemaSerializer",
                "type struct GameCore.Contracts.GeneratedFieldSlot",
                "type class GameCore.Contracts.CanonicalId32 [static]",
                "type class GameCore.Contracts.CheckpointCodecSet",
                "type class GameCore.Contracts.CheckpointDocument",
                "type class GameCore.Contracts.CheckpointErrors [static]",
                "type class GameCore.Contracts.CheckpointFormat [static]",
                "type class GameCore.Contracts.CheckpointIdentityTable",
                "type class GameCore.Contracts.CheckpointMigrationRegistry",
                "type class GameCore.Contracts.CheckpointSerializer",
                "type class GameCore.Contracts.MigrationPlan",
                "type enum GameCore.Contracts.CheckpointQueuePolicy : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.OutboxDeliveryState : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.OutboxDurability : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.OutboxRowKind : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.CheckpointRecordKind : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.ClockRowKind : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.ClockWakeState : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.CursorRowKind : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.GrantKind : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.IdentityCategory : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.MigrationPlanOutcome : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.ReferenceResolution : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type interface GameCore.Contracts.ICheckpointRecordCodec",
                "type interface GameCore.Contracts.ICheckpointRecordCodec<TValue> : GameCore.Contracts.ICheckpointRecordCodec",
                "type interface GameCore.Contracts.ISchemaMigrationStep",
                "type struct GameCore.Contracts.CheckpointCounts",
                "type struct GameCore.Contracts.ClockRecordValue",
                "type struct GameCore.Contracts.CommandRecordValue",
                "type struct GameCore.Contracts.CursorRecordValue",
                "type struct GameCore.Contracts.GrantRecordValue",
                "type struct GameCore.Contracts.HeaderRecordValue",
                "type struct GameCore.Contracts.InstallRecordValue",
                "type struct GameCore.Contracts.MessageRecordValue",
                "type struct GameCore.Contracts.OutboxRecordValue",
                "type struct GameCore.Contracts.RngRecordValue",
                "type struct GameCore.Contracts.ScopeRecordValue",
                "type struct GameCore.Contracts.SelectionRecordValue",
                "type struct GameCore.Contracts.SlotRecordValue",
                "type struct GameCore.Contracts.TargetRecordValue",
                // GC-023 adds the fixed compact telemetry schema on top: the counter id enum, its aggregation
                // policy, the retention policy, the owner seam, the counting helpers, the byte accounting and the
                // containers (counter set, section, frame, retained trace, keyed duration series). Additions are the
                // only permitted drift, and every one of them is recorded in artifacts/gc-023/HANDOFF.md.
                "type class GameCore.Contracts.TelemetryBytes [static]",
                "type class GameCore.Contracts.TelemetryCounting [static]",
                "type class GameCore.Contracts.TelemetryCounterSet",
                "type class GameCore.Contracts.TelemetryDurations [static]",
                "type class GameCore.Contracts.TelemetryFrame",
                "type class GameCore.Contracts.TelemetrySchema [static]",
                "type class GameCore.Contracts.TelemetrySection",
                "type class GameCore.Contracts.TelemetrySeries",
                "type class GameCore.Contracts.TelemetryTrace",
                "type enum GameCore.Contracts.TelemetryAggregation : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type enum GameCore.Contracts.TelemetryCounter : System.IComparable, System.IConvertible, System.IFormattable, System.ISpanFormattable",
                "type interface GameCore.Contracts.ITelemetryOwner",
                "type struct GameCore.Contracts.TelemetryRetention",
                "type struct GameCore.Contracts.TelemetrySeriesEntry",
            };
            foreach (string addition in comparison.AddedLines)
            {
                if (addition.StartsWith("type header: ", StringComparison.Ordinal))
                {
                    Assert.That(addedTypes, Does.Contain(addition.Substring("type header: ".Length)), addition);
                    continue;
                }

                int separator = addition.IndexOf(" :: ", StringComparison.Ordinal);
                string header = addition.Substring(0, separator);
                string member = addition.Substring(separator + " :: ".Length);
                // GC-012 records one more documented addition: P-032 requires a `Reset` to be
                // "manifest-supported", which the manifest's StateSlotSpec had no field to express; the two new
                // members are appended optional parameters plus their read-only properties, in
                // artifacts/gc-012/HANDOFF.md section 6.
                bool allowed = addedTypes.Contains(header)
                    || (header == "type class GameCore.Contracts.EnvelopeReader"
                        && member == "method public System.Boolean TrySeekTo(System.Int32 offset)")
                    || (header.StartsWith("type enum GameCore.Contracts.EnvelopeError :", StringComparison.Ordinal)
                        && (member == "enumvalue public MissingRequiredField = 16"
                            || member == "enumvalue public DuplicateField = 17"))
                    || (header.StartsWith("type enum GameCore.Contracts.FactoryKind :", StringComparison.Ordinal)
                        && member == "enumvalue public Handler = 10")
                    || (header.StartsWith("type class GameCore.Contracts.StateSlotSpec", StringComparison.Ordinal)
                        && IsStateSlotResetAddition(member))
                    || (header == "type struct GameCore.Contracts.StateDisposition"
                        && (member == "field public readonly GameCore.Contracts.OwnerId DestinationOwner"
                            || member == "field public readonly System.UInt32 ToVersion"
                            || member == "ctor public StateDisposition(GameCore.Contracts.StateSlotKey slot, GameCore.Contracts.StateDispositionKind kind, GameCore.Contracts.TargetId transferTo, GameCore.Contracts.FactoryKey migrationKey, GameCore.Contracts.OwnerId destinationOwner)"
                            || member == "ctor public StateDisposition(GameCore.Contracts.StateSlotKey slot, GameCore.Contracts.StateDispositionKind kind, GameCore.Contracts.TargetId transferTo, GameCore.Contracts.FactoryKey migrationKey, GameCore.Contracts.OwnerId destinationOwner, System.UInt32 toVersion)"))
                    || (header.StartsWith("type enum GameCore.Contracts.StateDispositionKind :", StringComparison.Ordinal)
                        && (member == "enumvalue public RetainDormant = 4"
                            || member == "enumvalue public Reset = 5"));
                Assert.That(allowed, Is.True, "Undocumented production API addition: " + addition);
            }

            TestContext.Out.WriteLine(comparison.Describe());
        }

        [Test]
        public void ProductionListingIsDeterministicAndEngineFree()
        {
            string first = ApiSnapshotGenerator.Generate(typeof(Id128).Assembly, SurfaceNamespace);
            string second = ApiSnapshotGenerator.Generate(typeof(Id128).Assembly, SurfaceNamespace);
            Assert.That(second, Is.EqualTo(first), "Snapshot generation must be deterministic for one assembly.");
            Assert.That(first, Does.Contain("type struct GameCore.Contracts.Id128"));

            foreach (string line in ApiSnapshotDiff.Normalize(first).Split('\n'))
            {
                Assert.That(line, Does.Not.Contain("UnityEngine"), "The contract surface must not expose engine types.");
                Assert.That(line, Does.Not.Contain("Unity.Entities"), "The contract surface must not expose ECS types.");
            }
        }

        [Test]
        public void ComparerReportsRemovedAndChangedLinesAndAcceptsAdditions()
        {
            const string frozen =
                "# header\ntype struct A.B\n  field public System.Int32 Value\n  method public System.Boolean Equals(A.B other)\n";
            const string production =
                "# header\ntype struct A.B\n  field public System.Int32 Value\n  method public System.Boolean Equals(A.B other)\n  method public System.String ToString()\ntype class A.C\n";

            ApiCompatibilityResult superset = ApiSurfaceComparer.Compare(frozen, production);
            Assert.That(superset.Compatible, Is.True);
            Assert.That(superset.AddedLines.Count, Is.EqualTo(2));

            const string reduced =
                "# header\ntype struct A.B\n  field public System.Int32 Value\n";

            ApiCompatibilityResult removed = ApiSurfaceComparer.Compare(frozen, reduced);
            Assert.That(removed.Compatible, Is.False);
            Assert.That(removed.RemovedLines.Count, Is.EqualTo(1));
            Assert.That(removed.Describe(), Does.Contain("Equals"));
        }
    }
}
