// GC-018 checkpoint catalog tests. Unity-free, engine-free, deterministic.
//
// The committed checkpoint catalog must be exactly what GameCore.Content.Compiler generates from the committed
// description document, its recorded file hash and fingerprint must verify, and every derived key literal in it
// must equal StableNameKeyDerivation.Derive of a canonical stable name (P-004, P-009, P-028).
#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using GameCore.Contracts;
using GameCore.ProtocolFixtures;
using NUnit.Framework;

namespace GameCore.Content.Compiler.Tests
{
    /// <summary>
    /// The committed GC-018 checkpoint catalog is generated content: one description document is the only source
    /// of truth, and a fresh generation must reproduce the committed file byte for byte. Unlike the probe and card
    /// catalogs it declares schemas only, so it is also the case that pins zero registration groups.
    /// </summary>
    [TestFixture]
    public sealed class CheckpointCatalogTests
    {
        private const string DescriptionPath = "unity/GameCore.Validation/Catalogs/CheckpointCatalog.catalog.json";

        private const string GeneratedPath =
            "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs";

        /// <summary>Schema count the committed description declares; changing it is a deliberate re-baseline.</summary>
        private const int DeclaredSchemaCount = 13;

        [Test]
        public void CommittedCheckpointCatalogMatchesAFreshGeneration()
        {
            string root = RepoLayout.FindRoot();
            string descriptionPath = RepoLayout.Resolve(root, DescriptionPath);
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            Assert.That(File.Exists(descriptionPath), Is.True, "missing description document: " + DescriptionPath);
            Assert.That(File.Exists(generatedPath), Is.True, "missing generated catalog: " + GeneratedPath);

            string directory = Path.Combine(Path.GetTempPath(), "gc018-checkpoint-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string regeneratedPath = Path.Combine(directory, "CheckpointCatalog.g.cs");
            try
            {
                CatalogGenerationReport report = CatalogGenerator.GenerateFromText(
                    File.ReadAllText(descriptionPath),
                    regeneratedPath);
                Assert.That(report.Succeeded, Is.True, report.Summary);
                Assert.That(
                    File.ReadAllBytes(generatedPath),
                    Is.EqualTo(File.ReadAllBytes(regeneratedPath)),
                    "The committed checkpoint catalog differs from a fresh generation of " + DescriptionPath +
                    ". Run the generator on the build host and commit its output:\n" +
                    "  <UNITY> -batchmode -nographics -quit -projectPath unity/GameCore.Validation " +
                    "-executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog " +
                    "-logFile artifacts/gc-018/codegen.log");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void CommittedCheckpointCatalogPassesItsOwnVerification()
        {
            string root = RepoLayout.FindRoot();
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            CatalogGenerationReport report = CatalogGenerator.Verify(generatedPath);
            Assert.That(report.Succeeded, Is.True, report.Summary);
            Assert.That(report.CatalogFileHash, Is.Not.Null);
            Assert.That(report.CatalogFingerprint, Has.Length.EqualTo(64));
        }

        [Test]
        public void CommittedCheckpointCatalogFingerprintMatchesItsDeclarations()
        {
            string root = RepoLayout.FindRoot();
            string descriptionPath = RepoLayout.Resolve(root, DescriptionPath);
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            CatalogDescription description = ReadDescription(descriptionPath);
            string recorded = CatalogGenerator.ExtractStringConstant(
                File.ReadAllText(generatedPath),
                "CatalogFingerprint")!;
            Assert.That(
                recorded,
                Is.EqualTo(CatalogEmitter.ComputeFingerprint(description)),
                "the fingerprint embedded in the committed checkpoint catalog must match its declarations (P-028)");
        }

        [Test]
        public void CommittedCheckpointCatalogKeyLiteralsMatchTheirDerivation()
        {
            string root = RepoLayout.FindRoot();
            string descriptionPath = RepoLayout.Resolve(root, DescriptionPath);
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            CatalogDescription description = ReadDescription(descriptionPath);
            string generated = File.ReadAllText(generatedPath);

            foreach (CatalogSchemaDeclaration schema in description.Schemas)
            {
                Id128 expected = StableNameKeyDerivation.Derive(schema.StableName);
                Match literal = Regex.Match(
                    generated,
                    "public static readonly FactoryKey " + Regex.Escape(schema.SerializerKeyName)
                    + " = new FactoryKey\\(new Id128\\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\\), (\\d+)U\\);");
                Assert.That(
                    literal.Success,
                    Is.True,
                    "the committed checkpoint catalog declares no key literal for " + schema.SerializerKeyName);

                Assert.That(
                    ParseUInt64Hex(literal.Groups[1].Value),
                    Is.EqualTo(expected.High),
                    schema.SerializerKeyName + ": the recorded high word must equal StableNameKeyDerivation.Derive('"
                    + schema.StableName + "').High");
                Assert.That(
                    ParseUInt64Hex(literal.Groups[2].Value),
                    Is.EqualTo(expected.Low),
                    schema.SerializerKeyName + ": the recorded low word must equal StableNameKeyDerivation.Derive('"
                    + schema.StableName + "').Low");
                Assert.That(
                    uint.Parse(literal.Groups[3].Value, CultureInfo.InvariantCulture),
                    Is.EqualTo(schema.SerializerKeyVersion),
                    schema.SerializerKeyName + ": the recorded key version must equal the declared serializer key version");
            }
        }

        [Test]
        public void EveryCheckpointSchemaIsDeclaredRequiredWithAscendingFieldIds()
        {
            string root = RepoLayout.FindRoot();
            string descriptionPath = RepoLayout.Resolve(root, DescriptionPath);

            CatalogDescription description = ReadDescription(descriptionPath);

            Assert.That(
                description.Schemas.Count,
                Is.EqualTo(DeclaredSchemaCount),
                "the GC-018 checkpoint catalog declares one schema per persisted checkpoint record kind");
            Assert.That(
                description.Groups.Count,
                Is.EqualTo(0),
                "the checkpoint catalog registers schemas only; a registration group is a separate declaration");

            foreach (CatalogSchemaDeclaration schema in description.Schemas)
            {
                Assert.That(schema.IsRequired, Is.True, schema.StableName + " must be declared required");
                Assert.That(schema.Fields.Count, Is.GreaterThan(0), schema.StableName + " declares no field");
                Assert.That(
                    schema.Fields[0].FieldId,
                    Is.EqualTo(1),
                    schema.StableName + " must start its field ids at 1 (0 is the envelope checksum field id)");

                for (int i = 0; i < schema.Fields.Count; i++)
                {
                    CatalogFieldDeclaration field = schema.Fields[i];
                    Assert.That(field.Required, Is.True, schema.StableName + "." + field.Name + " must be required");
                    if (i > 0)
                    {
                        Assert.That(
                            field.FieldId,
                            Is.GreaterThan(schema.Fields[i - 1].FieldId),
                            schema.StableName + " declares field ids out of ascending order at " + field.Name);
                    }
                }
            }
        }

        /// <summary>Reads and validates the committed description, returning the validated declarations.</summary>
        private static CatalogDescription ReadDescription(string descriptionPath)
        {
            CatalogCompilationResult compilation = CatalogDescriptionReader.Read(
                File.ReadAllText(descriptionPath),
                out CatalogDescription? description);
            Assert.That(compilation.Succeeded, Is.True, compilation.Describe());
            Assert.That(description, Is.Not.Null);
            return description!;
        }

        /// <summary>Parses one emitted uppercase 16-digit hexadecimal <c>ulong</c> literal.</summary>
        private static ulong ParseUInt64Hex(string text)
        {
            Assert.That(text, Has.Length.EqualTo(16));
            return ulong.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }
}
