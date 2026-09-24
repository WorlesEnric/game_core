// Content compiler tests (GC-003). Unity-free, engine-free, deterministic.
//
// Coverage: duplicate/unknown/unsupported rejection with specific diagnostics (P-009, P-055), byte
// reproducibility and declaration-order independence (P-008), fingerprint stability (P-028, P-053), generated
// serializer emission (P-054), and the committed probe catalog's agreement with a fresh generation from its
// own description document.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Contracts;
using GameCore.ProtocolFixtures;
using NUnit.Framework;

namespace GameCore.Content.Compiler.Tests
{
    [TestFixture]
    public sealed class CatalogDescriptionRejectionTests
    {
        private static CatalogCompilationResult Read(string json) => CatalogDescriptionReader.Read(json);

        private static void AssertRejected(CatalogCompilationResult result, CatalogDiagnosticCode code, string pathFragment)
        {
            Assert.That(result.Succeeded, Is.False, "the description must be rejected");
            Assert.That(result.GeneratedCode, Is.Null);

            bool found = false;
            foreach (CatalogDiagnostic diagnostic in result.Diagnostics)
            {
                if (diagnostic.Code == code && diagnostic.Path.Contains(pathFragment))
                {
                    found = true;
                    break;
                }
            }

            Assert.That(
                found,
                Is.True,
                "expected " + code + " at a path containing '" + pathFragment + "', but got:\n" + result.Describe());
        }

        [Test]
        public void DuplicateStableNameRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.DuplicateStableName);
            AssertRejected(result, CatalogDiagnosticCode.DuplicateStableName, "stableName");
            Assert.That(result.Describe(), Does.Contain("maps to exactly one derived key"));
        }

        [Test]
        public void UnknownMemberRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.UnknownMember);
            AssertRejected(result, CatalogDiagnosticCode.UnknownMember, "unexpectedMember");
            Assert.That(result.Describe(), Does.Contain("unknown member"));
        }

        [Test]
        public void MissingRequiredMemberRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.MissingClassName);
            AssertRejected(result, CatalogDiagnosticCode.MissingMember, "className");
        }

        [Test]
        public void InvalidIdentityHexRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.UppercaseIdentity);
            AssertRejected(result, CatalogDiagnosticCode.InvalidIdentityHex, "implementationId");
            Assert.That(result.Describe(), Does.Contain("32 lowercase hexadecimal"));
        }

        [Test]
        public void AllZeroImplementationIdentityRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.ZeroImplementationIdentity);
            AssertRejected(result, CatalogDiagnosticCode.InvalidIdentityHex, "implementationId");
        }

        [Test]
        public void ReservedFieldIdRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.ReservedFieldId);
            AssertRejected(result, CatalogDiagnosticCode.ReservedFieldId, "fields[0].id");
            Assert.That(result.Describe(), Does.Contain("reserved"));
        }

        [Test]
        public void DuplicateFieldIdRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.DuplicateFieldId);
            AssertRejected(result, CatalogDiagnosticCode.DuplicateFieldId, "fields[1].id");
        }

        [Test]
        public void UnsupportedWireTypeRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.UnsupportedWireType);
            AssertRejected(result, CatalogDiagnosticCode.UnsupportedWireType, "wireType");
            Assert.That(result.Describe(), Does.Contain("supported types are"));
        }

        [Test]
        public void UnknownFactoryKindRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.UnknownFactoryKind);
            AssertRejected(result, CatalogDiagnosticCode.InvalidValue, "groups[0].kind");
            Assert.That(result.Describe(), Does.Contain("accepted kinds are"));
        }

        [Test]
        public void OptionalBytesFieldIsAcceptedAndEmittedAsNullable()
        {
            CatalogCompilationResult result = Read(Descriptions.OptionalBytesField);
            Assert.That(result.Succeeded, Is.True, result.Describe());
            Assert.That(result.GeneratedCode!, Does.Contain("public readonly byte[]? Payload;"));
            Assert.That(result.GeneratedCode!, Does.Contain("new GeneratedFieldSlot(1, WireType.Bytes, false)"));
        }

        [Test]
        public void ZeroVersionRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.ZeroSchemaVersion);
            AssertRejected(result, CatalogDiagnosticCode.InvalidValue, "schemaVersion");
            Assert.That(result.Describe(), Does.Contain("positive version"));
        }

        [Test]
        public void UnsupportedProtocolVersionRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.ProtocolMajorTwo);
            AssertRejected(result, CatalogDiagnosticCode.InvalidProtocolVersion, "protocolVersion");
            Assert.That(result.Describe(), Does.Contain("major change is incompatible"));
        }

        [Test]
        public void UnimplementedMinorRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.ProtocolMinorNine);
            AssertRejected(result, CatalogDiagnosticCode.InvalidProtocolVersion, "protocolVersion");
        }

        [Test]
        public void WrongDescriptionFormatRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.WrongFormat);
            AssertRejected(result, CatalogDiagnosticCode.UnsupportedFormat, "descriptionFormat");
            Assert.That(result.Describe(), Does.Contain(CatalogDescriptionReader.FormatId));
        }

        [Test]
        public void InvalidCodeFragmentRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.InvalidCodeFragment);
            AssertRejected(result, CatalogDiagnosticCode.InvalidCodeFragment, "closedGenericRootStatements");
            Assert.That(result.Describe(), Does.Contain("cannot contain a comment"));
        }

        [Test]
        public void CodeFragmentWithStatementTerminatorRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.TerminatedCodeFragment);
            AssertRejected(result, CatalogDiagnosticCode.InvalidCodeFragment, "closedGenericRootStatements");
        }

        [Test]
        public void GroupMemberCollidingWithAReservedGeneratedMemberRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.ReservedMemberCollision);
            AssertRejected(result, CatalogDiagnosticCode.DuplicateMemberName, "groups[0]");
            Assert.That(result.Describe(), Does.Contain("Serializers"));
        }

        [Test]
        public void SchemaMemberCollidingWithAReservedGeneratedMemberRejects()
        {
            CatalogCompilationResult result = Read(Descriptions.ReservedSchemaMemberCollision);
            AssertRejected(result, CatalogDiagnosticCode.DuplicateMemberName, "schemas[0]");
        }

        [Test]
        public void MalformedJsonIsReportedAsInvalidDocument()
        {
            CatalogCompilationResult result = Read("{ \"descriptionFormat\": ");
            AssertRejected(result, CatalogDiagnosticCode.InvalidDocument, string.Empty);
        }

        [Test]
        public void DuplicateJsonMemberRejectsAsInvalidDocument()
        {
            CatalogCompilationResult result = Read(Descriptions.DuplicateJsonMember);
            AssertRejected(result, CatalogDiagnosticCode.InvalidDocument, string.Empty);
            Assert.That(result.Describe(), Does.Contain("duplicate object member name"));
        }

        [Test]
        public void DescriptionThatWouldBuildAnInvalidCatalogRejects()
        {
            // The generator runs the production catalog rules over the declarations it is about to emit, so a
            // description that would only fail later, at runtime, rejects here instead (P-009).
            CatalogCompilationResult result = Read(Descriptions.DuplicateFeatureId);
            AssertRejected(result, CatalogDiagnosticCode.InvalidCatalog, string.Empty);
            Assert.That(result.Describe(), Does.Contain("duplicate supported feature id"));
        }

        [Test]
        public void InvalidDocumentRootRejects()
        {
            CatalogCompilationResult result = Read("[]");
            AssertRejected(result, CatalogDiagnosticCode.InvalidDocument, string.Empty);
            Assert.That(result.Describe(), Does.Contain("root must be an object"));
        }

        [Test]
        public void EveryRejectionReportsAPathOrAnExplicitDocumentScope()
        {
            string[] samples =
            {
                Descriptions.DuplicateStableName,
                Descriptions.UnknownMember,
                Descriptions.MissingClassName,
                Descriptions.UnsupportedWireType,
                Descriptions.UnknownFactoryKind,
                Descriptions.ProtocolMajorTwo,
            };

            foreach (string sample in samples)
            {
                CatalogCompilationResult result = Read(sample);
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.Diagnostics, Is.Not.Empty);
                foreach (CatalogDiagnostic diagnostic in result.Diagnostics)
                {
                    Assert.That(diagnostic.Message, Is.Not.Empty, "every diagnostic states a reason");
                    Assert.That(diagnostic.ToString(), Does.Contain(diagnostic.Code.ToString()));
                }
            }
        }
    }

    [TestFixture]
    public sealed class CatalogEmissionTests
    {
        [Test]
        public void ValidDescriptionEmitsDeterministicBytes()
        {
            CatalogCompilationResult first = CatalogDescriptionReader.Read(Descriptions.Valid);
            CatalogCompilationResult second = CatalogDescriptionReader.Read(Descriptions.Valid);

            Assert.That(first.Succeeded, Is.True, first.Describe());
            Assert.That(first.GeneratedCode, Is.EqualTo(second.GeneratedCode));
            Assert.That(first.GeneratedCode!, Does.Not.Contain("\r"), "generated files use LF line endings only");
            Assert.That(first.GeneratedCode!, Does.Contain("namespace Test.Generated"));
            Assert.That(first.GeneratedCode!, Does.Contain("public const string CatalogFileHash = \""));
        }

        [Test]
        public void DeclarationOrderDoesNotChangeGeneratedBytes()
        {
            CatalogCompilationResult canonical = CatalogDescriptionReader.Read(Descriptions.Valid);
            CatalogCompilationResult permuted = CatalogDescriptionReader.Read(Descriptions.ValidPermutedDeclarationOrder);

            Assert.That(canonical.Succeeded && permuted.Succeeded, Is.True);
            Assert.That(
                permuted.GeneratedCode,
                Is.EqualTo(canonical.GeneratedCode),
                "field, entry, group and schema declaration order must not reach the generated file (P-008)");
        }

        [Test]
        public void RepeatedEmissionIsByteIdentical()
        {
            const int Attempts = 5;
            string? expected = null;
            for (int i = 0; i < Attempts; i++)
            {
                CatalogCompilationResult result = CatalogDescriptionReader.Read(Descriptions.Valid);
                Assert.That(result.Succeeded, Is.True, result.Describe());
                expected ??= result.GeneratedCode;
                Assert.That(result.GeneratedCode, Is.EqualTo(expected), "generation must be byte-reproducible");
            }
        }

        [Test]
        public void FingerprintLiteralMatchesTheDeclarations()
        {
            CatalogCompilationResult result = CatalogDescriptionReader.Read(Descriptions.Valid);
            Assert.That(result.Succeeded, Is.True, result.Describe());

            string recorded = CatalogGenerator.ExtractStringConstant(result.GeneratedCode!, "CatalogFingerprint")!;
            Assert.That(recorded, Has.Length.EqualTo(64));
            Assert.That(recorded, Is.EqualTo(TestDescriptions.Fingerprint(Descriptions.Valid)));

            string permuted = CatalogGenerator.ExtractStringConstant(
                CatalogDescriptionReader.Read(Descriptions.ValidPermutedDeclarationOrder).GeneratedCode!,
                "CatalogFingerprint")!;
            Assert.That(permuted, Is.EqualTo(recorded), "the fingerprint is independent of declaration order");
        }

        [Test]
        public void FileHashCoversTheGeneratedPrefixAndDetectsEdits()
        {
            CatalogCompilationResult result = CatalogDescriptionReader.Read(Descriptions.Valid);
            string text = result.GeneratedCode!;
            string declared = CatalogGenerator.ExtractStringConstant(text, "CatalogFileHash")!;

            Assert.That(CatalogEmitter.FilePrefixHash(text), Is.EqualTo(declared));

            string edited = text.Replace("CatalogFileHashScope", "CatalogFileHashScopeX");
            Assert.That(CatalogEmitter.FilePrefixHash(edited), Does.StartWith("mismatch:"));
        }

        [Test]
        public void GenerationReportVerifiesWhatItWrote()
        {
            string directory = Path.Combine(Path.GetTempPath(), "gc003-compiler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, "Generated.g.cs");
            try
            {
                CatalogGenerationReport created = CatalogGenerator.GenerateFromText(Descriptions.Valid, output);
                Assert.That(created.Succeeded, Is.True, created.Summary);
                Assert.That(created.WroteFile, Is.True);
                Assert.That(File.Exists(output), Is.True);
                Assert.That(created.CatalogFileHash, Is.Not.Null);
                Assert.That(created.CatalogFingerprint, Is.Not.Null);

                CatalogGenerationReport revisited = CatalogGenerator.GenerateFromText(Descriptions.Valid, output);
                Assert.That(revisited.Succeeded, Is.True, revisited.Summary);
                Assert.That(revisited.WroteFile, Is.False, "regenerating identical content must not rewrite the file");

                File.WriteAllText(output, File.ReadAllText(output).Replace("public static class", "public static class "));
                CatalogGenerationReport tampered = CatalogGenerator.Verify(output);
                Assert.That(tampered.Succeeded, Is.False, "a hand-edited generated file must fail verification");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void GeneratedSerializerDeclaresRequiredFieldsAndEnvelopeCalls()
        {
            CatalogCompilationResult result = CatalogDescriptionReader.Read(Descriptions.Valid);
            string text = result.GeneratedCode!;

            Assert.That(text, Does.Contain("new GeneratedFieldSlot(1, WireType.UInt64, true)"));
            Assert.That(text, Does.Contain("writer.WriteUInt64Field(1, value.High)"));
            Assert.That(text, Does.Contain("reader.TryReadUInt64(field0, out field0Value)"));
            Assert.That(text, Does.Contain("writer.WriteChecksum()"));
            Assert.That(text, Does.Contain(": GeneratedSerializerBase"));
            Assert.That(text, Does.Contain("public byte[] Serialize("));
            Assert.That(text, Does.Contain("out EnvelopeError error"));
        }

        [Test]
        public void EmittedFieldCasesCloseWithBreakAndBraceAndKeepBracesBalanced()
        {
            CatalogCompilationResult result = CatalogDescriptionReader.Read(Descriptions.Valid, out CatalogDescription? description);
            Assert.That(result.Succeeded, Is.True, result.Describe());
            string text = result.GeneratedCode!;

            int declaredFields = 0;
            foreach (CatalogSchemaDeclaration schema in description!.Schemas)
            {
                declaredFields += schema.Fields.Count;
            }

            Assert.That(declaredFields, Is.GreaterThan(0));
            Assert.That(
                CountOf(text, "                            break;\n"),
                Is.GreaterThanOrEqualTo(declaredFields),
                "every field case must terminate with a break so no case falls through (CS0163)");

            int open = CountOf(text, "{");
            int close = CountOf(text, "}");
            Assert.That(close, Is.EqualTo(open), "the generated file must have balanced braces (CS1513/CS1022)");
        }

        private static int CountOf(string text, string needle)
        {
            int count = 0;
            int index = text.IndexOf(needle, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
            }

            return count;
        }

        [Test]
        public void GeneratedClosedGenericRootsAndAttributesAreEmittedWithTerminators()
        {
            CatalogCompilationResult result = CatalogDescriptionReader.Read(Descriptions.Valid);
            string text = result.GeneratedCode!;

            Assert.That(text, Does.Contain("[assembly: RegisterGenericJobType(typeof(Test.Job<Test.Value>))]"));
            Assert.That(text, Does.Contain("Test.Roots.Track(default(Test.Job<Test.Value>));"));
            Assert.That(text, Does.Contain("public const bool HasClosedGenericRoots = true;"));
            Assert.That(text, Does.Contain("public static void RootClosedGenericInstantiations()"));
        }

        [Test]
        public void UsedCatalogDescriptionHasNoClosedGenericRootsWhenNoneAreDeclared()
        {
            CatalogCompilationResult result = CatalogDescriptionReader.Read(Descriptions.NoCodeDirectives);
            Assert.That(result.Succeeded, Is.True, result.Describe());
            Assert.That(result.GeneratedCode!, Does.Contain("public const bool HasClosedGenericRoots = false;"));
            Assert.That(result.GeneratedCode!, Does.Not.Contain("RootClosedGenericInstantiations()"));
        }

        [Test]
        public void GeneratorArgumentsParseTheDocumentedCommandLine()
        {
            CatalogGeneratorArguments valid = CatalogGeneratorArguments.Parse(new[]
            {
                "-batchmode", "-catalogDescription", "in.json", "-catalogOutput", "out.g.cs",
            });
            Assert.That(valid.IsValid, Is.True);
            Assert.That(valid.DescriptionPath, Is.EqualTo("in.json"));
            Assert.That(valid.OutputPath, Is.EqualTo("out.g.cs"));

            CatalogGeneratorArguments missing = CatalogGeneratorArguments.Parse(new[] { "-batchmode" });
            Assert.That(missing.IsValid, Is.False);
            Assert.That(missing.Errors.Count, Is.EqualTo(2));

            CatalogGeneratorArguments truncated = CatalogGeneratorArguments.Parse(new[] { "-catalogOutput" });
            Assert.That(truncated.Errors, Does.Contain("-catalogOutput requires a value."));

            Assert.That(CatalogGeneratorArguments.Parse(new[] { "-catalogHelp" }).PrintHelp, Is.True);
        }
    }

    /// <summary>
    /// The committed probe catalog must be exactly what the compiler generates from the committed description
    /// document: one owner, one source of truth, byte-reproducible output (09 GC-003 definition of done).
    /// </summary>
    [TestFixture]
    public sealed class CommittedProbeCatalogTests
    {
        private const string DescriptionPath = "unity/GameCore.Validation/Catalogs/ProbeCatalog.catalog.json";
        private const string GeneratedPath = "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs";

        [Test]
        public void CommittedGeneratedCatalogMatchesAFreshGeneration()
        {
            string root = RepoLayout.FindRoot();
            string descriptionPath = RepoLayout.Resolve(root, DescriptionPath);
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            Assert.That(File.Exists(descriptionPath), Is.True, "missing description document: " + DescriptionPath);
            Assert.That(File.Exists(generatedPath), Is.True, "missing generated catalog: " + GeneratedPath);

            CatalogCompilationResult compilation = CatalogDescriptionReader.Read(File.ReadAllText(descriptionPath));
            Assert.That(compilation.Succeeded, Is.True, compilation.Describe());

            string committed = File.ReadAllText(generatedPath);
            Assert.That(
                committed,
                Is.EqualTo(compilation.GeneratedCode),
                "The committed probe catalog differs from a fresh generation of " + DescriptionPath +
                ". Run the generator on the build host and commit its output:\n" +
                "  <UNITY> -batchmode -nographics -quit -projectPath unity/GameCore.Validation " +
                "-executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog " +
                "-logFile artifacts/gc-003/codegen.log");
        }

        [Test]
        public void CommittedGeneratedCatalogPassesItsOwnVerification()
        {
            string root = RepoLayout.FindRoot();
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            CatalogGenerationReport report = CatalogGenerator.Verify(generatedPath);
            Assert.That(report.Succeeded, Is.True, report.Summary);
            Assert.That(report.CatalogFileHash, Is.Not.Null);
            Assert.That(report.CatalogFingerprint, Has.Length.EqualTo(64));
        }

        [Test]
        public void CommittedProbeCatalogFingerprintMatchesItsDeclarations()
        {
            string root = RepoLayout.FindRoot();
            string descriptionPath = RepoLayout.Resolve(root, DescriptionPath);
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            CatalogCompilationResult compilation = CatalogDescriptionReader.Read(File.ReadAllText(descriptionPath));
            Assert.That(compilation.Succeeded, Is.True, compilation.Describe());

            string recorded = CatalogGenerator.ExtractStringConstant(
                File.ReadAllText(generatedPath),
                "CatalogFingerprint")!;
            Assert.That(
                recorded,
                Is.EqualTo(TestDescriptions.Fingerprint(File.ReadAllText(descriptionPath))),
                "the fingerprint embedded in the committed catalog must match its declarations (P-028)");
        }
    }

    /// <summary>Description documents used by the tests; each one is a documented input-form example.</summary>
    internal static class Descriptions
    {
        internal const string Valid = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""supportedFeatureIds"": [""11111111111111112222222222222222""],
  ""schemas"": [
    {
      ""stableName"": ""test.schema.record"",
      ""valueTypeName"": ""TestRecord"",
      ""serializerTypeName"": ""TestRecordSerializer"",
      ""serializerKeyName"": ""TestRecordSerializerKey"",
      ""schemaId"": ""33333333333333334444444444444444"",
      ""schemaVersion"": 1,
      ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"",
      ""required"": true,
      ""fields"": [
        { ""id"": 1, ""name"": ""High"", ""wireType"": ""UInt64"", ""required"": true },
        { ""id"": 2, ""name"": ""Label"", ""wireType"": ""Utf8"", ""required"": false }
      ]
    }
  ],
  ""groups"": [
    {
      ""tableName"": ""PluginRegistrations"",
      ""keysName"": ""PluginKeys"",
      ""lookupMethodName"": ""TryGetPluginFactory"",
      ""interfaceType"": ""Test.IPluginFactory"",
      ""kind"": ""PluginFactory"",
      ""entries"": [
        {
          ""stableName"": ""test.plugin.alpha"",
          ""keyName"": ""AlphaKey"",
          ""keyVersion"": 1,
          ""ownerPackageId"": ""55555555555555556666666666666666"",
          ""implementationId"": ""77777777777777778888888888888888"",
          ""implementationExpression"": ""new Test.AlphaFactory()""
        },
        {
          ""stableName"": ""test.plugin.beta"",
          ""keyName"": ""BetaKey"",
          ""keyVersion"": 2,
          ""ownerPackageId"": ""55555555555555556666666666666666"",
          ""implementationId"": ""9999999999999999aaaaaaaaaaaaaaaa"",
          ""implementationExpression"": ""new Test.BetaFactory()""
        }
      ]
    },
    {
      ""tableName"": ""HandlerRegistrations"",
      ""keysName"": ""HandlerKeys"",
      ""lookupMethodName"": ""TryGetHandler"",
      ""interfaceType"": ""Test.IHandler<Test.Value, int>"",
      ""kind"": ""Handler"",
      ""entries"": [
        {
          ""stableName"": ""test.handler.gamma"",
          ""keyName"": ""GammaKey"",
          ""keyVersion"": 1,
          ""ownerPackageId"": ""55555555555555556666666666666666"",
          ""implementationId"": ""bbbbbbbbbbbbbbbbcccccccccccccccc"",
          ""implementationExpression"": ""new Test.GammaHandler(GammaKey)""
        }
      ]
    }
  ],
  ""code"": {
    ""usingDirectives"": [""Test""],
    ""assemblyAttributes"": [""RegisterGenericJobType(typeof(Test.Job<Test.Value>))""],
    ""closedGenericRootStatements"": [""Test.Roots.Track(default(Test.Job<Test.Value>))""]
  }
}";

        /// <summary>The same declarations with every array reversed, including the schema field list.</summary>
        internal const string ValidPermutedDeclarationOrder = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""supportedFeatureIds"": [""11111111111111112222222222222222""],
  ""schemas"": [
    {
      ""stableName"": ""test.schema.record"",
      ""valueTypeName"": ""TestRecord"",
      ""serializerTypeName"": ""TestRecordSerializer"",
      ""serializerKeyName"": ""TestRecordSerializerKey"",
      ""schemaId"": ""33333333333333334444444444444444"",
      ""schemaVersion"": 1,
      ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"",
      ""required"": true,
      ""fields"": [
        { ""id"": 2, ""name"": ""Label"", ""wireType"": ""Utf8"", ""required"": false },
        { ""id"": 1, ""name"": ""High"", ""wireType"": ""UInt64"", ""required"": true }
      ]
    }
  ],
  ""groups"": [
    {
      ""tableName"": ""HandlerRegistrations"",
      ""keysName"": ""HandlerKeys"",
      ""lookupMethodName"": ""TryGetHandler"",
      ""interfaceType"": ""Test.IHandler<Test.Value, int>"",
      ""kind"": ""Handler"",
      ""entries"": [
        {
          ""stableName"": ""test.handler.gamma"",
          ""keyName"": ""GammaKey"",
          ""keyVersion"": 1,
          ""ownerPackageId"": ""55555555555555556666666666666666"",
          ""implementationId"": ""bbbbbbbbbbbbbbbbcccccccccccccccc"",
          ""implementationExpression"": ""new Test.GammaHandler(GammaKey)""
        }
      ]
    },
    {
      ""tableName"": ""PluginRegistrations"",
      ""keysName"": ""PluginKeys"",
      ""lookupMethodName"": ""TryGetPluginFactory"",
      ""interfaceType"": ""Test.IPluginFactory"",
      ""kind"": ""PluginFactory"",
      ""entries"": [
        {
          ""stableName"": ""test.plugin.beta"",
          ""keyName"": ""BetaKey"",
          ""keyVersion"": 2,
          ""ownerPackageId"": ""55555555555555556666666666666666"",
          ""implementationId"": ""9999999999999999aaaaaaaaaaaaaaaa"",
          ""implementationExpression"": ""new Test.BetaFactory()""
        },
        {
          ""stableName"": ""test.plugin.alpha"",
          ""keyName"": ""AlphaKey"",
          ""keyVersion"": 1,
          ""ownerPackageId"": ""55555555555555556666666666666666"",
          ""implementationId"": ""77777777777777778888888888888888"",
          ""implementationExpression"": ""new Test.AlphaFactory()""
        }
      ]
    }
  ],
  ""code"": {
    ""usingDirectives"": [""Test""],
    ""assemblyAttributes"": [""RegisterGenericJobType(typeof(Test.Job<Test.Value>))""],
    ""closedGenericRootStatements"": [""Test.Roots.Track(default(Test.Job<Test.Value>))""]
  }
}";

        internal const string NoCodeDirectives = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""EmptyCatalog"",
  ""fileName"": ""EmptyCatalog.g.cs"",
  ""schemas"": [],
  ""groups"": []
}";

        internal const string DuplicateStableName = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""groups"": [
    {
      ""tableName"": ""A"", ""keysName"": ""AKeys"", ""lookupMethodName"": ""TryA"",
      ""interfaceType"": ""Test.IFactory"", ""kind"": ""PluginFactory"",
      ""entries"": [
        { ""stableName"": ""test.dup"", ""keyName"": ""AKey"", ""keyVersion"": 1,
          ""ownerPackageId"": ""00000000000000000000000000000000"",
          ""implementationId"": ""11111111111111112222222222222222"",
          ""implementationExpression"": ""new Test.A()"" },
        { ""stableName"": ""test.dup"", ""keyName"": ""BKey"", ""keyVersion"": 1,
          ""ownerPackageId"": ""00000000000000000000000000000000"",
          ""implementationId"": ""33333333333333334444444444444444"",
          ""implementationExpression"": ""new Test.B()"" }
      ]
    }
  ]
}";

        internal const string DuplicateKey = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""schemas"": [
    { ""stableName"": ""test.schema.one"", ""valueTypeName"": ""One"", ""serializerTypeName"": ""OneSerializer"",
      ""serializerKeyName"": ""OneSerializerKey"", ""schemaId"": ""11111111111111112222222222222222"",
      ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true, ""fields"": [] },
    { ""stableName"": ""test.schema.two"", ""valueTypeName"": ""Two"", ""serializerTypeName"": ""TwoSerializer"",
      ""serializerKeyName"": ""TwoSerializerKey"", ""schemaId"": ""33333333333333334444444444444444"",
      ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true, ""fields"": [] },
    { ""stableName"": ""test.schema.one"", ""valueTypeName"": ""Three"", ""serializerTypeName"": ""ThreeSerializer"",
      ""serializerKeyName"": ""ThreeSerializerKey"", ""schemaId"": ""55555555555555556666666666666666"",
      ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true, ""fields"": [] }
  ],
  ""groups"": []
}";

        internal const string UnknownMember = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""unexpectedMember"": true
}";

        internal const string MissingClassName = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""fileName"": ""TestCatalog.g.cs""
}";

        internal const string UppercaseIdentity = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""groups"": [
    { ""tableName"": ""A"", ""keysName"": ""AKeys"", ""lookupMethodName"": ""TryA"",
      ""interfaceType"": ""Test.IFactory"", ""kind"": ""PluginFactory"",
      ""entries"": [
        { ""stableName"": ""test.alpha"", ""keyName"": ""AKey"", ""keyVersion"": 1,
          ""ownerPackageId"": ""00000000000000000000000000000000"",
          ""implementationId"": ""7777777777777777888888888888888A"",
          ""implementationExpression"": ""new Test.A()"" }
      ] }
  ]
}";

        internal const string ZeroImplementationIdentity = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""groups"": [
    { ""tableName"": ""A"", ""keysName"": ""AKeys"", ""lookupMethodName"": ""TryA"",
      ""interfaceType"": ""Test.IFactory"", ""kind"": ""PluginFactory"",
      ""entries"": [
        { ""stableName"": ""test.alpha"", ""keyName"": ""AKey"", ""keyVersion"": 1,
          ""ownerPackageId"": ""00000000000000000000000000000000"",
          ""implementationId"": ""00000000000000000000000000000000"",
          ""implementationExpression"": ""new Test.A()"" }
      ] }
  ]
}";

        internal const string ReservedFieldId = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""schemas"": [
    { ""stableName"": ""test.schema.record"", ""valueTypeName"": ""Record"",
      ""serializerTypeName"": ""RecordSerializer"", ""serializerKeyName"": ""RecordSerializerKey"",
      ""schemaId"": ""11111111111111112222222222222222"", ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true,
      ""fields"": [ { ""id"": 0, ""name"": ""Flags"", ""wireType"": ""UInt32"", ""required"": true } ] }
  ],
  ""groups"": []
}";

        internal const string DuplicateFieldId = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""schemas"": [
    { ""stableName"": ""test.schema.record"", ""valueTypeName"": ""Record"",
      ""serializerTypeName"": ""RecordSerializer"", ""serializerKeyName"": ""RecordSerializerKey"",
      ""schemaId"": ""11111111111111112222222222222222"", ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true,
      ""fields"": [
        { ""id"": 1, ""name"": ""High"", ""wireType"": ""UInt64"", ""required"": true },
        { ""id"": 1, ""name"": ""Low"", ""wireType"": ""UInt64"", ""required"": true }
      ] }
  ],
  ""groups"": []
}";

        internal const string UnsupportedWireType = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""schemas"": [
    { ""stableName"": ""test.schema.record"", ""valueTypeName"": ""Record"",
      ""serializerTypeName"": ""RecordSerializer"", ""serializerKeyName"": ""RecordSerializerKey"",
      ""schemaId"": ""11111111111111112222222222222222"", ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true,
      ""fields"": [ { ""id"": 1, ""name"": ""Payload"", ""wireType"": ""Decimal"", ""required"": true } ] }
  ],
  ""groups"": []
}";

        internal const string UnknownFactoryKind = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""groups"": [
    { ""tableName"": ""A"", ""keysName"": ""AKeys"", ""lookupMethodName"": ""TryA"",
      ""interfaceType"": ""Test.IFactory"", ""kind"": ""WidgetFactory"", ""entries"": [] }
  ]
}";

        internal const string OptionalBytesField = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""schemas"": [
    { ""stableName"": ""test.schema.record"", ""valueTypeName"": ""Record"",
      ""serializerTypeName"": ""RecordSerializer"", ""serializerKeyName"": ""RecordSerializerKey"",
      ""schemaId"": ""11111111111111112222222222222222"", ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true,
      ""fields"": [ { ""id"": 1, ""name"": ""Payload"", ""wireType"": ""Bytes"", ""required"": false } ] }
  ],
  ""groups"": []
}";

        internal const string ZeroSchemaVersion = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""schemas"": [
    { ""stableName"": ""test.schema.record"", ""valueTypeName"": ""Record"",
      ""serializerTypeName"": ""RecordSerializer"", ""serializerKeyName"": ""RecordSerializerKey"",
      ""schemaId"": ""11111111111111112222222222222222"", ""schemaVersion"": 0, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true, ""fields"": [] }
  ],
  ""groups"": []
}";

        internal const string ProtocolMajorTwo = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""2.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs""
}";

        internal const string ProtocolMinorNine = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.9"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs""
}";

        internal const string WrongFormat = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/9"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs""
}";

        internal const string InvalidCodeFragment = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""code"": { ""closedGenericRootStatements"": [""Test.Roots.Track(default(Test.Job<V>)) // note""] }
}";

        internal const string TerminatedCodeFragment = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""code"": { ""closedGenericRootStatements"": [""Test.Roots.Track(default(Test.Job<V>));""] }
}";

        internal const string ReservedMemberCollision = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""groups"": [
    { ""tableName"": ""Serializers"", ""keysName"": ""AKeys"", ""lookupMethodName"": ""TryA"",
      ""interfaceType"": ""Test.IFactory"", ""kind"": ""PluginFactory"", ""entries"": [] }
  ]
}";

        internal const string ReservedSchemaMemberCollision = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""schemas"": [
    { ""stableName"": ""test.schema.record"", ""valueTypeName"": ""SchemaRegistrations"",
      ""serializerTypeName"": ""RecordSerializer"", ""serializerKeyName"": ""RecordSerializerKey"",
      ""schemaId"": ""11111111111111112222222222222222"", ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true, ""fields"": [] }
  ],
  ""groups"": []
}";

        internal const string DuplicateJsonMember = @"{ ""descriptionFormat"": ""a"", ""descriptionFormat"": ""b"" }";

        internal const string DuplicateFeatureId = @"{
  ""descriptionFormat"": ""gamecore.catalog-description/1"",
  ""protocolVersion"": ""1.0"",
  ""namespace"": ""Test.Generated"",
  ""className"": ""TestCatalog"",
  ""fileName"": ""TestCatalog.g.cs"",
  ""supportedFeatureIds"": [""11111111111111112222222222222222"", ""11111111111111112222222222222222""],
  ""schemas"": [
    { ""stableName"": ""test.schema.record"", ""valueTypeName"": ""Record"",
      ""serializerTypeName"": ""RecordSerializer"", ""serializerKeyName"": ""RecordSerializerKey"",
      ""schemaId"": ""33333333333333334444444444444444"", ""schemaVersion"": 1, ""serializerKeyVersion"": 1,
      ""ownerPackageId"": ""00000000000000000000000000000000"", ""required"": true, ""fields"": [] }
  ],
  ""groups"": []
}";
    }

    /// <summary>
    /// Recomputes the catalog fingerprint from a description document's validated declarations without going
    /// through the emitter, so the value embedded in generated output is checked against an independent
    /// construction of the same registrations (P-028, P-053).
    /// </summary>
    internal static class TestDescriptions
    {
        internal static string Fingerprint(string json)
        {
            CatalogCompilationResult compilation = CatalogDescriptionReader.Read(json, out CatalogDescription? description);
            Assert.That(compilation.Succeeded, Is.True, compilation.Describe());
            Assert.That(description, Is.Not.Null);

            List<FactoryRegistration> factories = new List<FactoryRegistration>();
            List<SchemaRegistration> schemas = new List<SchemaRegistration>();

            foreach (CatalogRegistrationGroup group in description!.Groups)
            {
                FactoryKind kind = CatalogFactoryKinds.Parse(group.Kind);
                foreach (CatalogRegistrationEntry entry in group.Entries)
                {
                    factories.Add(new FactoryRegistration(
                        new FactoryKey(StableNameKeyDerivation.Derive(entry.StableName), entry.KeyVersion),
                        kind,
                        Identity(entry.OwnerPackageIdHex),
                        Identity(entry.ImplementationIdHex),
                        entry.KeyVersion));
                }
            }

            foreach (CatalogSchemaDeclaration schema in description.Schemas)
            {
                Id128 schemaId = Identity(schema.SchemaIdHex);
                FactoryKey serializerKey = new FactoryKey(
                    StableNameKeyDerivation.Derive(schema.StableName),
                    schema.SerializerKeyVersion);

                factories.Add(new FactoryRegistration(
                    serializerKey,
                    FactoryKind.Serializer,
                    Identity(schema.OwnerPackageIdHex),
                    schemaId,
                    schema.SchemaVersion));

                schemas.Add(new SchemaRegistration(
                    new SchemaRef(new SchemaId(schemaId), schema.SchemaVersion),
                    Identity(schema.OwnerPackageIdHex),
                    serializerKey,
                    schema.IsRequired));
            }

            List<Id128> features = new List<Id128>();
            foreach (string feature in description.SupportedFeatureHexIds)
            {
                features.Add(Identity(feature));
            }

            return CatalogFingerprint.Compute(factories, schemas, features).ToHex();
        }

        private static Id128 Identity(string hex)
        {
            Assert.That(Id128Codec.TryParseHex(hex, out Id128 value), Is.True, "'" + hex + "' is not canonical hex");
            return value;
        }
    }
}
