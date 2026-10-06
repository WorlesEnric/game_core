#nullable enable
// GameCore Studio staging harness, EditMode (P2.4): the catalog delta of the staged mechanism.
//
// Writes <out>/catalog-delta.json:
//   world        the catalog fingerprint the slot's world re-bake produces (GameCore.Gameplay.Compile.Entry.Verify,
//                in memory, nothing written), present only when the stage inputs carry a WorldDefinition;
//   mechanisms   the candidate's own catalog: the generated `CatalogFingerprint` constant of the type its proposal
//                names, checked against the fingerprint of the catalog its `BuildCatalog()` really builds;
//   predicted    the catalog-set hash of world + mechanism (CatalogSetHash.Combine), the hash the live world must
//                report after admission (the Studio admission recomputes and compares it).
// The test fails when the declared catalog is missing, does not build, or disagrees with its constant.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Stage.Harness
{
    public sealed class CatalogProbe
    {
        [Test]
        public void WritesTheCatalogDelta()
        {
            HarnessConfig config = HarnessFiles.Load();
            var result = new HarnessJson().Str("changeSetId", config.changeSetId).Str("package", config.package);

            string world = ReadSourceWorld(config);
            result.Str("world", world).Bool("worldVerified", true)
                .Str("worldSnapshotSha256", config.worldSnapshotSha256);

            var mechanisms = new List<HarnessJson>();
            var fingerprints = new List<string>();
            if (!string.IsNullOrEmpty(config.catalogType))
            {
                Type? catalogType = HarnessFiles.FindType(config.catalogType);
                Assert.That(catalogType, Is.Not.Null, "the proposal's catalog type " + config.catalogType + " is not compiled");
                FieldInfo? constant = catalogType!.GetField("CatalogFingerprint", BindingFlags.Public | BindingFlags.Static);
                Assert.That(constant, Is.Not.Null, config.catalogType + " declares no CatalogFingerprint");
                string declared = (constant!.IsLiteral ? constant.GetRawConstantValue() : constant.GetValue(null)) as string ?? string.Empty;
                MethodInfo? build = catalogType.GetMethod("BuildCatalog", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                Assert.That(build, Is.Not.Null, config.catalogType + " has no generated BuildCatalog()");
                object? built = build!.Invoke(null, null);
                ICatalog? catalog = built == null ? null : Read<ICatalog>(built, "Catalog");
                Assert.That(catalog, Is.Not.Null, config.catalogType + ".BuildCatalog() refused to build");
                string actual = catalog!.Fingerprint.ToHex();
                Assert.That(actual, Is.EqualTo(declared), "the built catalog's fingerprint differs from the generated constant");
                fingerprints.Add(actual);
                mechanisms.Add(new HarnessJson().Str("package", config.package).Str("catalogType", config.catalogType).Str("fingerprint", actual));
            }

            result.Arr("mechanisms", mechanisms);
            if (world != null)
            {
                result.Str("predicted", CatalogSetHash.Combine(world, fingerprints));
            }

            HarnessFiles.Write(config, "catalog-delta.json", result.ToString());
            UnityEngine.Debug.Log("[stage-harness] catalog delta " + result);
        }

        // Recompile trusted source data with the trusted content compiler. No generated source or
        // candidate fingerprint is accepted as the world's identity.
        public static string ReadSourceWorld(HarnessConfig config)
        {
            Assert.That(config.worldSnapshotSha256, Has.Length.EqualTo(64), "source_world_missing");
            byte[] bytes = File.ReadAllBytes(config.worldSnapshotPath);
            using (SHA256 sha = SHA256.Create())
            {
                string digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Assert.That(digest, Is.EqualTo(config.worldSnapshotSha256), "source_world_changed");
            }
            Type? reader = HarnessFiles.FindType("GameCore.Content.Compiler.CatalogDescriptionReader");
            Assert.That(reader, Is.Not.Null, "trusted catalog compiler missing");
            MethodInfo? read = reader!.GetMethod("Read", new[] { typeof(string) });
            Assert.That(read, Is.Not.Null);
            object? compiled = read!.Invoke(null, new object[] { Encoding.UTF8.GetString(bytes) });
            Assert.That(Read<bool>(compiled!, "Succeeded"), Is.True, "source_world_invalid");
            string code = Read<string>(compiled!, "GeneratedCode") ?? string.Empty;
            var match = System.Text.RegularExpressions.Regex.Match(code,
                "public const string CatalogFingerprint = \"([0-9a-f]{64})\"");
            Assert.That(match.Success, Is.True, "source_world_invalid");
            return match.Groups[1].Value;
        }

        private static T? Read<T>(object owner, string property)
        {
            PropertyInfo? info = owner.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            object? value = info?.GetValue(owner);
            return value is T typed ? typed : default;
        }
    }
}
