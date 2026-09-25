// GC-011 card catalog tests. Unity-free, engine-free, deterministic.
//
// The committed card catalog must be exactly what GameCore.Content.Compiler generates from the committed
// description document, its recorded file hash and fingerprint must verify, and every derived key literal in it
// must equal StableNameKeyDerivation.Derive of a canonical stable name (P-004, P-009, P-028).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GameCore.Contracts;
using GameCore.ProtocolFixtures;
using NUnit.Framework;

namespace GameCore.Content.Compiler.Tests
{
    /// <summary>
    /// The committed GC-011 card catalog is generated content: one description document is the only source of
    /// truth, and a fresh generation must reproduce the committed file byte for byte.
    /// </summary>
    [TestFixture]
    public sealed class CardCatalogTests
    {
        private const string DescriptionPath = "unity/GameCore.Validation/Catalogs/CardCatalog.catalog.json";
        private const string GeneratedPath =
            "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs";

        [Test]
        public void CommittedCardCatalogMatchesAFreshGeneration()
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
                "The committed card catalog differs from a fresh generation of " + DescriptionPath +
                ". Run the generator on the build host and commit its output:\n" +
                "  <UNITY> -batchmode -nographics -quit -projectPath unity/GameCore.Validation " +
                "-executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog " +
                "-logFile artifacts/gc-011/codegen.log");
        }

        [Test]
        public void CommittedCardCatalogPassesItsOwnVerification()
        {
            string root = RepoLayout.FindRoot();
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            CatalogGenerationReport report = CatalogGenerator.Verify(generatedPath);
            Assert.That(report.Succeeded, Is.True, report.Summary);
            Assert.That(report.CatalogFileHash, Is.Not.Null);
            Assert.That(report.CatalogFingerprint, Has.Length.EqualTo(64));
        }

        [Test]
        public void CommittedCardCatalogFingerprintMatchesItsDeclarations()
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
                Is.EqualTo(DeclarationsFingerprint(description)),
                "the fingerprint embedded in the committed card catalog must match its declarations (P-028)");
        }

        [Test]
        public void CommittedCardCatalogKeyLiteralsMatchTheirDerivation()
        {
            string root = RepoLayout.FindRoot();
            string descriptionPath = RepoLayout.Resolve(root, DescriptionPath);
            string generatedPath = RepoLayout.Resolve(root, GeneratedPath);

            CatalogDescription description = ReadDescription(descriptionPath);
            string generated = File.ReadAllText(generatedPath);

            List<string> expected = new List<string>();
            foreach (CatalogRegistrationGroup group in description.Groups)
            {
                foreach (CatalogRegistrationEntry entry in group.Entries)
                {
                    expected.Add(KeyLiteral(entry.KeyName, StableNameKeyDerivation.Derive(entry.StableName)));
                }
            }

            foreach (CatalogSchemaDeclaration schema in description.Schemas)
            {
                expected.Add(KeyLiteral(
                    schema.SerializerKeyName,
                    StableNameKeyDerivation.Derive(schema.StableName)));
            }

            foreach (string literal in expected)
            {
                Assert.That(
                    generated.IndexOf(literal, StringComparison.Ordinal) >= 0,
                    Is.True,
                    "the committed card catalog is missing the derived key literal " + literal);
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

        /// <summary>
        /// Recomputes the catalog fingerprint from the validated declarations without going through the
        /// emitter, so the literal embedded in the generated file is checked against an independent
        /// construction of the same registrations (P-028, P-053).
        /// </summary>
        private static string DeclarationsFingerprint(CatalogDescription description)
        {
            List<FactoryRegistration> factories = new List<FactoryRegistration>();
            List<SchemaRegistration> schemas = new List<SchemaRegistration>();
            List<Id128> features = new List<Id128>();

            foreach (CatalogRegistrationGroup group in description.Groups)
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

            foreach (string feature in description.SupportedFeatureHexIds)
            {
                features.Add(Identity(feature));
            }

            return CatalogFingerprint.Compute(factories, schemas, features).ToHex();
        }

        /// <summary>The exact generated declaration text for one derived key, uppercase hex like the emitter.</summary>
        private static string KeyLiteral(string keyName, Id128 key)
        {
            string id = string.Format(
                CultureInfo.InvariantCulture,
                "new Id128(0x{0:X16}UL, 0x{1:X16}UL)",
                key.High,
                key.Low);
            return "public static readonly FactoryKey " + keyName + " = new FactoryKey(" + id + ", 1U);";
        }

        private static Id128 Identity(string hex)
        {
            Assert.That(Id128Codec.TryParseHex(hex, out Id128 value), Is.True, "'" + hex + "' is not canonical hex");
            return value;
        }
    }
}
