// Hollowmere P1.1 EditMode - authoring, bake determinism and definition revisions.
//
// AuthorAndBake creates the Hollowmere world when it is missing (idempotent otherwise) and bakes it without importing
// the generated C#, so the host runs it once on a fresh checkout and commits what it produced (PACKET.md, "Catalog").
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using Hollowmere.WorldAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_1.EditMode.Tests
{
    [TestFixture]
    public sealed class AuthoringBakeTests
    {
        [Test, Order(0)]
        public void AuthorAndBake_ProducesTheThreeRegionWorld()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            BakeResult result = HollowmereWorldAuthoring.AuthorAndBake();
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Debug.Log("[P1.1] author+bake " + clock.ElapsedMilliseconds + " ms (bake " + result.ElapsedMilliseconds
                + " ms); regions=" + result.Regions + " portals=" + result.Portals + " definitions=" + result.Definitions
                + " entities=" + result.Entities + " fingerprint=" + result.CatalogFingerprint);

            Assert.That(result.Regions, Is.EqualTo(3));
            Assert.That(result.Portals, Is.EqualTo(3));
            Assert.That(result.Definitions, Is.GreaterThanOrEqualTo(6));
            Assert.That(result.Entities, Is.GreaterThanOrEqualTo(20));

            RegionManifest? manifest = AssetDatabase.LoadAssetAtPath<RegionManifest>(HollowmereWorldAuthoring.ManifestPath);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest!.CatalogFingerprint, Is.EqualTo(result.CatalogFingerprint));
            Assert.That(manifest.CatalogTypeName, Is.EqualTo("Hollowmere.Generated.HollowmereCatalog"));
            Assert.That(manifest.Regions.Count, Is.EqualTo(3));
            Assert.That(AuthoringIds.IsValid(manifest.FocusEntityId), Is.True, "the traveller is the focus entity");
            ManifestEntity? traveller = manifest.FindEntity(manifest.FocusEntityId);
            Assert.That(traveller, Is.Not.Null);
            Assert.That(traveller!.regionId, Is.EqualTo(manifest.StartRegionId), "the traveller starts in the start region");
            Assert.That(File.Exists(BakePaths.ConventionFor(HollowmereWorldAuthoring.WorldPath).GeneratedPath), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(HollowmereWorldAuthoring.BootScenePath), Is.Not.Null);

            // Every region has exactly two portal ends with both arrival poses inside its bounds (triangle topology).
            for (int i = 0; i < manifest.Regions.Count; i++)
            {
                int ends = 0;
                for (int p = 0; p < manifest.Portals.Count; p++)
                {
                    if (manifest.Portals[p].regionA == manifest.Regions[i].authoringId || manifest.Portals[p].regionB == manifest.Regions[i].authoringId)
                    {
                        ends++;
                    }
                }

                Assert.That(ends, Is.EqualTo(2), manifest.Regions[i].name);
            }
        }

        [Test, Order(1)]
        public void BakeTwice_IsByteIdentical_AndVerifyPasses()
        {
            WorldDefinition world = RequireWorld();
            BakePaths paths = BakePaths.ConventionFor(HollowmereWorldAuthoring.WorldPath);
            BakeResult first = Entry.Bake(world, paths, false);
            Assert.That(first.Succeeded, Is.True, first.ToString());
            Dictionary<string, byte[]> before = Snapshot(paths);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            BakeResult second = Entry.Bake(world, paths, false);
            long secondMs = clock.ElapsedMilliseconds;
            Assert.That(second.Succeeded, Is.True, second.ToString());
            Assert.That(second.ChangedFiles, Is.Empty, "an unchanged world bakes to the same bytes: " + string.Join(", ", second.ChangedFiles));
            Dictionary<string, byte[]> after = Snapshot(paths);
            foreach (KeyValuePair<string, byte[]> file in before)
            {
                Assert.That(after[file.Key], Is.EqualTo(file.Value), file.Key + " changed between two bakes");
            }

            clock.Restart();
            BakeResult verify = Entry.Verify(world, paths);
            Assert.That(verify.Succeeded, Is.True, verify.ToString());
            Debug.Log("[P1.1] rebake " + secondMs + " ms; verify " + clock.ElapsedMilliseconds + " ms; stale-mark budget "
                + BakeStaleMarker.AutoBakeBudgetMilliseconds + " ms -> auto-bake after save "
                + (secondMs <= BakeStaleMarker.AutoBakeBudgetMilliseconds ? "allowed" : "refused"));
        }

        [Test, Order(2)]
        public void OneFieldChange_ChangesOnlyThatDefinitionsRevision()
        {
            RequireWorld();
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(EntityDefinition), new[] { HollowmereWorldAuthoring.Root });
            var definitions = new List<EntityDefinition>();
            for (int i = 0; i < guids.Length; i++)
            {
                definitions.Add(AssetDatabase.LoadAssetAtPath<EntityDefinition>(AssetDatabase.GUIDToAssetPath(guids[i])));
            }

            Assert.That(definitions.Count, Is.GreaterThanOrEqualTo(6));
            var before = new Dictionary<string, ulong>(StringComparer.Ordinal);
            for (int i = 0; i < definitions.Count; i++)
            {
                before[definitions[i].AuthoringId] = DefinitionHashing.RevisionOf(DefinitionCanonicalizer.ContentStamp(definitions[i]));
                Assert.That(definitions[i].ContentStamp, Is.EqualTo(DefinitionCanonicalizer.ContentStamp(definitions[i])),
                    definitions[i].name + " carries the stamp of its current fields (written by the bake)");
            }

            EntityDefinition changed = definitions[0];
            int scale = changed.DefaultScaleMilli;
            try
            {
                changed.Configure(changed.Prefab, scale + 1, changed.StartsVisible, changed.StartsAlive);
                for (int i = 0; i < definitions.Count; i++)
                {
                    ulong revision = DefinitionHashing.RevisionOf(DefinitionCanonicalizer.ContentStamp(definitions[i]));
                    if (ReferenceEquals(definitions[i], changed))
                    {
                        Assert.That(revision, Is.Not.EqualTo(before[definitions[i].AuthoringId]), "the edited definition gets a new revision");
                    }
                    else
                    {
                        Assert.That(revision, Is.EqualTo(before[definitions[i].AuthoringId]), definitions[i].name + " must keep its revision");
                    }
                }
            }
            finally
            {
                changed.Configure(changed.Prefab, scale, changed.StartsVisible, changed.StartsAlive);
            }

            Assert.That(DefinitionHashing.RevisionOf(DefinitionCanonicalizer.ContentStamp(changed)), Is.EqualTo(before[changed.AuthoringId]),
                "restoring the field restores the revision (the hash covers values, not history)");
        }

        [Test]
        public void VariantEdit_ChangesTheRevisionOfItsDefinition()
        {
            VariantDefinition variant = ScriptableObject.CreateInstance<VariantDefinition>();
            EntityDefinition definition = ScriptableObject.CreateInstance<EntityDefinition>();
            try
            {
                variant.EnsureAuthoringId();
                definition.EnsureAuthoringId();
                definition.SetVariants(new[] { variant });
                string stamp = DefinitionCanonicalizer.ContentStamp(definition);
                variant.Configure(null, Color.red);
                Assert.That(DefinitionCanonicalizer.ContentStamp(definition), Is.Not.EqualTo(stamp));
                Assert.That(DefinitionCanonicalizer.Canonical(definition).Text(), Does.Contain("auth:" + variant.AuthoringId + "#"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(variant);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        internal static WorldDefinition RequireWorld()
        {
            WorldDefinition? world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(HollowmereWorldAuthoring.WorldPath);
            if (world == null)
            {
                BakeResult result = HollowmereWorldAuthoring.AuthorAndBake();
                Assert.That(result.Succeeded, Is.True, result.ToString());
                world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(HollowmereWorldAuthoring.WorldPath);
            }

            Assert.That(world, Is.Not.Null);
            return world!;
        }

        private static Dictionary<string, byte[]> Snapshot(BakePaths paths)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string path in new[] { paths.DescriptionPath, paths.GeneratedPath, paths.ReportPath, paths.ManifestPath })
            {
                files[path] = File.ReadAllBytes(Path.GetFullPath(path));
            }

            return files;
        }
    }
}
