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

            string? world = null;
            bool worldVerified = false;
            string worldSummary = "no GameCore.Gameplay.Compile in the slot";
            Type? entry = HarnessFiles.FindType("GameCore.Gameplay.Compile.Entry");
            MethodInfo? verify = entry?.GetMethod("Verify", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (verify != null)
            {
                object? bake = verify.Invoke(null, null);
                if (bake != null)
                {
                    string fingerprint = Read<string>(bake, "CatalogFingerprint") ?? string.Empty;
                    worldVerified = Read<bool>(bake, "Succeeded");
                    worldSummary = Read<string>(bake, "Summary") ?? string.Empty;
                    if (fingerprint.Length == 64)
                    {
                        world = fingerprint;
                    }
                }
            }

            result.Str("world", world).Bool("worldVerified", worldVerified).Str("worldSummary", worldSummary);

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

        private static T? Read<T>(object owner, string property)
        {
            PropertyInfo? info = owner.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            object? value = info?.GetValue(owner);
            return value is T typed ? typed : default;
        }
    }
}
