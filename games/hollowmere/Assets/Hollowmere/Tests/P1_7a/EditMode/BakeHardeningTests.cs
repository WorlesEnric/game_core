// Hollowmere P1.7a EditMode - bake hardening (A8 structural recipe revision, A10 planning never mints or dirties).
//
//   A10  Entry.Verify on the committed world mints no authoring id, dirties no asset and changes no file; the narrative
//        plan refuses an un-minted definition with GP-ID-001 instead of minting it; the canonicalizer refuses a
//        reference that is neither a saved asset nor an authored object; BakeResult.ChangedFiles is sorted ordinal.
//   A8   cosmetic and tuning edits keep a definition's structural stamp (the recipe revision source) while its content
//        stamp still changes; structural edits (prefab, variant set, variant prefab, interaction kind, overridable
//        fields) change it; the committed manifest's recipe revision is the revision of the baked structural stamp.
//
// Every edit is made on in-memory objects (CreateInstance or Instantiate copies), so no committed asset changes.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.World;
using Hollowmere.WorldAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_7a.EditMode.Tests
{
    [TestFixture]
    public sealed class BakeHardeningTests
    {
        private const string HollowmereAssets = "Assets/Hollowmere";
        private const string MissingDirectory = "Assets/__P1_7a_VerifyNowhere";

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(created[i]);
                }
            }

            created.Clear();
        }

        // ---------------------------------------------------------------- A10

        [Test]
        public void Verify_OnTheCommittedWorld_MintsNoIdDirtiesNoAssetAndWritesNoFile()
        {
            WorldDefinition world = RequireWorld();
            BakePaths paths = BakePaths.ConventionFor(HollowmereWorldAuthoring.WorldPath);
            List<ScriptableObject> watched = WatchedAssets(world);
            Assert.That(watched.Count, Is.GreaterThan(1), "the committed world and its definitions are watched");

            var dirtyBefore = new int[watched.Count];
            var idsBefore = new string[watched.Count];
            for (int i = 0; i < watched.Count; i++)
            {
                dirtyBefore[i] = EditorUtility.GetDirtyCount(watched[i]);
                idsBefore[i] = watched[i] is IAuthoredObject authored ? authored.AuthoringId : string.Empty;
            }

            var files = new List<string> { paths.DescriptionPath, paths.GeneratedPath, paths.ReportPath, paths.ManifestPath };
            for (int i = 0; i < watched.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(watched[i]);
                if (!string.IsNullOrEmpty(path) && !files.Contains(path))
                {
                    files.Add(path);
                }
            }

            for (int i = 0; i < world.Regions.Count; i++)
            {
                RegionDefinition region = world.Regions[i];
                if (region != null && !string.IsNullOrEmpty(region.ScenePath) && !files.Contains(region.ScenePath))
                {
                    files.Add(region.ScenePath);
                }
            }

            Dictionary<string, byte[]?> bytesBefore = Snapshot(files);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            BakeResult verify = Entry.Verify(world, paths);
            Debug.Log("[P1.7a] verify " + clock.ElapsedMilliseconds + " ms over " + watched.Count + " watched assets, " + files.Count
                + " watched files: " + (verify.Succeeded ? "up to date" : "stale (" + verify.ChangedFiles.Count + " file(s))"));

            for (int i = 0; i < watched.Count; i++)
            {
                Assert.That(EditorUtility.GetDirtyCount(watched[i]), Is.EqualTo(dirtyBefore[i]), watched[i].name + " was dirtied by Verify");
                string id = watched[i] is IAuthoredObject authored ? authored.AuthoringId : string.Empty;
                Assert.That(id, Is.EqualTo(idsBefore[i]), watched[i].name + " had its authoring id changed by Verify");
            }

            Dictionary<string, byte[]?> bytesAfter = Snapshot(files);
            foreach (KeyValuePair<string, byte[]?> file in bytesBefore)
            {
                Assert.That(bytesAfter[file.Key], Is.EqualTo(file.Value), file.Key + " changed on disk during Verify");
            }
        }

        [Test]
        public void NarrativePlan_RefusesAnUnmintedDefinition_WithoutMintingOrDirtyingIt()
        {
            ActionSetDefinition actions = Make<ActionSetDefinition>();
            SetAuthoringId(actions, string.Empty);
            GameplayContentSet set = Make<GameplayContentSet>();
            set.Configure(null, new ScriptableObject[] { actions });
            int dirty = EditorUtility.GetDirtyCount(actions);

            var diagnostics = new List<GameplayDiagnostic>();
            NarrativeBake.Plan(set, MissingDirectory + "/Set.asset", AuthoringIds.Mint(), diagnostics);

            Assert.That(HasCode(diagnostics, GameplayDiagnosticCodes.MissingAuthoringId), Is.True, Describe(diagnostics));
            Assert.That(actions.AuthoringId, Is.Empty, "planning never mints an authoring id");
            Assert.That(EditorUtility.GetDirtyCount(actions), Is.EqualTo(dirty), "planning never dirties a definition");
        }

        [Test]
        public void Canonicalizer_RefusesUnmintedAndMalformedIds_IncludingThroughAReferencedVariant()
        {
            EntityDefinition definition = Make<EntityDefinition>();
            SetAuthoringId(definition, string.Empty);
            Assert.That(HasCode(DefinitionCanonicalizer.Validate(definition), GameplayDiagnosticCodes.MissingAuthoringId), Is.True);

            SetAuthoringId(definition, "not-an-authoring-id");
            Assert.That(HasCode(DefinitionCanonicalizer.Validate(definition), GameplayDiagnosticCodes.InvalidAuthoringId), Is.True);

            definition.EnsureAuthoringId();
            Assert.That(DefinitionCanonicalizer.Validate(definition), Is.Empty, "a minted definition without references is valid");

            VariantDefinition variant = Make<VariantDefinition>();
            SetAuthoringId(variant, string.Empty);
            definition.SetVariants(new[] { variant });
            IReadOnlyList<GameplayDiagnostic> problems = DefinitionCanonicalizer.Validate(definition);
            Assert.That(HasCode(problems, GameplayDiagnosticCodes.MissingAuthoringId), Is.True, "an un-minted variant refuses its definition: " + Describe(problems));
            Assert.That(variant.AuthoringId, Is.Empty, "validation never mints");
        }

        [Test]
        public void Canonicalizer_RefusesAReferenceThatIsNeitherAnAssetNorAnAuthoredObject()
        {
            EntityDefinition definition = Make<EntityDefinition>();
            definition.EnsureAuthoringId();
            var unsaved = new GameObject("P1_7a unsaved prefab");
            created.Add(unsaved);
            definition.Configure(unsaved, GameplayUnits.ScaleOne, true, true);

            IReadOnlyList<GameplayDiagnostic> problems = DefinitionCanonicalizer.Validate(definition);
            Assert.That(problems.Count, Is.EqualTo(1), Describe(problems));
            Assert.That(problems[0].Code, Does.StartWith("GP-"));
            Assert.That(problems[0].Code, Is.EqualTo(GameplayDiagnosticCodes.MissingAuthoringId));
            Assert.That(problems[0].Message, Does.Contain("neither a saved asset nor an authored object"));

            // A committed definition (saved prefab, minted variants) validates clean, also as an in-memory copy.
            EntityDefinition committed = CommittedDefinitionWithPrefab();
            Assert.That(DefinitionCanonicalizer.Validate(committed), Is.Empty, Describe(DefinitionCanonicalizer.Validate(committed)));
            EntityDefinition copy = Copy(committed);
            Assert.That(DefinitionCanonicalizer.Validate(copy), Is.Empty);
        }

        [Test]
        public void ChangedFiles_AreSortedOrdinalAndDistinct_AndVerifyCreatesNothing()
        {
            WorldDefinition world = RequireWorld();
            var paths = new BakePaths(MissingDirectory + "/Hollowmere.asset", MissingDirectory + "/Generated",
                new CatalogNaming("Hollowmere.Generated", "HollowmereCatalog"));
            BakeResult verify = Entry.Verify(world, paths);

            Assert.That(verify.ChangedFiles.Count, Is.GreaterThanOrEqualTo(4), "every output is missing at the probe paths: " + verify);
            var sorted = new List<string>(verify.ChangedFiles);
            sorted.Sort(string.CompareOrdinal);
            Assert.That(verify.ChangedFiles, Is.EqualTo(sorted), "ChangedFiles is sorted ordinal");
            Assert.That(new HashSet<string>(verify.ChangedFiles, StringComparer.Ordinal).Count, Is.EqualTo(verify.ChangedFiles.Count), "each path once");
            Assert.That(Directory.Exists(Path.GetFullPath(MissingDirectory)), Is.False, "Verify writes nothing");
        }

        // ---------------------------------------------------------------- A8

        [Test]
        public void StructuralFieldLists_NameRealAuthorableFields()
        {
            AssertListNamesFields(typeof(EntityDefinition), DefinitionCanonicalizer.EntityDefinitionType);
            AssertListNamesFields(typeof(VariantDefinition), DefinitionCanonicalizer.EntityVariantType);
            Assert.That(DefinitionCanonicalizer.IsStructural("unknown.type", "anything"), Is.True, "a type without a list is structural in every field");
            Assert.That(DefinitionCanonicalizer.IsStructural(DefinitionCanonicalizer.EntityVariantType, "tint"), Is.False);
            Assert.That(DefinitionCanonicalizer.IsStructural(DefinitionCanonicalizer.EntityDefinitionType, "defaultScaleMilli"), Is.False);
        }

        [Test]
        public void CosmeticAndTuningEdits_KeepTheStructuralStamp_ButChangeTheContentStamp()
        {
            EntityDefinition definition = NewDefinition(out VariantDefinition variant);
            string structural = DefinitionCanonicalizer.StructuralStamp(definition);
            string content = DefinitionCanonicalizer.ContentStamp(definition);

            variant.Configure(variant.Prefab, Color.red);
            Assert.That(DefinitionCanonicalizer.StructuralStamp(definition), Is.EqualTo(structural), "a variant tint is cosmetic");
            Assert.That(DefinitionCanonicalizer.ContentStamp(definition), Is.Not.EqualTo(content), "the content stamp still sees the tint");

            definition.Configure(definition.Prefab, 1750, false, false);
            Assert.That(DefinitionCanonicalizer.StructuralStamp(definition), Is.EqualTo(structural), "seed defaults are tuning");
            Assert.That(DefinitionHashing.RevisionOf(DefinitionCanonicalizer.StructuralStamp(definition)), Is.EqualTo(DefinitionHashing.RevisionOf(structural)));
            Assert.That(DefinitionCanonicalizer.StructuralCanonical(definition).Text(), Does.Not.Contain("defaultScaleMilli"));
        }

        [Test]
        public void StructuralEdits_ChangeTheStructuralStamp_AndRestoringTheValueRestoresIt()
        {
            EntityDefinition definition = NewDefinition(out VariantDefinition variant);
            string baseline = DefinitionCanonicalizer.StructuralStamp(definition);
            var replacement = new GameObject("P1_7a replacement prefab");
            created.Add(replacement);

            definition.SetInteractionKind("lever");
            AssertChanged(definition, baseline, "interaction kind");
            definition.SetInteractionKind("door");
            Assert.That(DefinitionCanonicalizer.StructuralStamp(definition), Is.EqualTo(baseline));

            definition.SetVariants(Array.Empty<VariantDefinition>());
            AssertChanged(definition, baseline, "variant set");
            definition.SetVariants(new[] { variant });

            variant.Configure(replacement, variant.Tint);
            AssertChanged(definition, baseline, "variant prefab replacement");
            variant.Configure(null, variant.Tint);

            definition.Configure(replacement, definition.DefaultScaleMilli, definition.StartsVisible, definition.StartsAlive);
            AssertChanged(definition, baseline, "prefab");
            definition.Configure(null, definition.DefaultScaleMilli, definition.StartsVisible, definition.StartsAlive);

            definition.SetOverridableFields(new[] { OverrideSet.ScaleMilli });
            AssertChanged(definition, baseline, "overridable fields");
        }

        [Test]
        public void CommittedManifest_RecipeRevisionIsTheRevisionOfTheBakedStructuralStamp()
        {
            RequireWorld();
            RegionManifest? manifest = AssetDatabase.LoadAssetAtPath<RegionManifest>(HollowmereWorldAuthoring.ManifestPath);
            Assert.That(manifest, Is.Not.Null);
            ManifestDefinition? sample = null;
            for (int i = 0; i < manifest!.Definitions.Count; i++)
            {
                ManifestDefinition entry = manifest.Definitions[i];
                Assert.That(entry.definition, Is.Not.Null, entry.name);
                Assert.That(entry.structuralStamp, Is.EqualTo(DefinitionCanonicalizer.StructuralStamp(entry.definition!)),
                    entry.name + ": the manifest records the structural stamp of the current definition (rebake after A8)");
                Assert.That(entry.contentStamp, Is.EqualTo(DefinitionCanonicalizer.ContentStamp(entry.definition!)), entry.name);
                Assert.That(entry.Revision, Is.EqualTo(DefinitionHashing.RevisionOf(entry.structuralStamp)), entry.name);
                if (sample == null || (entry.definition!.Variants.Count > 0 && sample.definition!.Variants.Count == 0))
                {
                    sample = entry;
                }
            }

            Assert.That(sample, Is.Not.Null, "the committed world has definitions");

            // A cosmetic edit of an in-memory copy keeps the committed recipe revision; a structural edit changes it.
            EntityDefinition copy = Copy(sample!.definition!);
            var variants = new List<VariantDefinition>();
            for (int v = 0; v < copy.Variants.Count; v++)
            {
                VariantDefinition variantCopy = UnityEngine.Object.Instantiate(copy.Variants[v]);
                created.Add(variantCopy);
                variantCopy.Configure(variantCopy.Prefab, new Color(0.25f, 0.5f, 0.75f, 1f));
                variants.Add(variantCopy);
            }

            copy.SetVariants(variants);
            copy.Configure(copy.Prefab, copy.DefaultScaleMilli + 7, !copy.StartsVisible, copy.StartsAlive);
            Assert.That(DefinitionHashing.RevisionOf(DefinitionCanonicalizer.StructuralStamp(copy)), Is.EqualTo(sample.Revision));
            Assert.That(DefinitionCanonicalizer.ContentStamp(copy), Is.Not.EqualTo(sample.contentStamp));

            copy.SetInteractionKind(copy.InteractionKind + ".p17a");
            Assert.That(DefinitionHashing.RevisionOf(DefinitionCanonicalizer.StructuralStamp(copy)), Is.Not.EqualTo(sample.Revision));
            Assert.That(sample.contentStamp, Is.EqualTo(DefinitionCanonicalizer.ContentStamp(sample.definition!)), "the committed asset is untouched");
        }

        // ---------------------------------------------------------------- helpers

        private T Make<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            instance.name = typeof(T).Name + " (P1.7a)";
            created.Add(instance);
            return instance;
        }

        private EntityDefinition Copy(EntityDefinition source)
        {
            EntityDefinition copy = UnityEngine.Object.Instantiate(source);
            created.Add(copy);
            return copy;
        }

        private EntityDefinition NewDefinition(out VariantDefinition variant)
        {
            variant = Make<VariantDefinition>();
            variant.EnsureAuthoringId();
            EntityDefinition definition = Make<EntityDefinition>();
            definition.EnsureAuthoringId();
            definition.SetVariants(new[] { variant });
            definition.SetInteractionKind("door");
            definition.SetOverridableFields(new[] { OverrideSet.ScaleMilli, OverrideSet.Visible, OverrideSet.Tint });
            return definition;
        }

        private static void AssertChanged(EntityDefinition definition, string baseline, string what)
        {
            Assert.That(DefinitionCanonicalizer.StructuralStamp(definition), Is.Not.EqualTo(baseline), what + " is structural");
            Assert.That(DefinitionHashing.RevisionOf(DefinitionCanonicalizer.StructuralStamp(definition)), Is.Not.EqualTo(DefinitionHashing.RevisionOf(baseline)), what);
        }

        private static void AssertListNamesFields(Type type, string typeId)
        {
            Assert.That(DefinitionCanonicalizer.HasStructuralList(typeId), Is.True, typeId);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo field in DefinitionCanonicalizer.AuthorableFields(type))
            {
                names.Add(field.Name);
            }

            string[] structural = DefinitionCanonicalizer.StructuralFieldNames(typeId);
            Assert.That(structural.Length, Is.GreaterThan(0), typeId);
            for (int i = 0; i < structural.Length; i++)
            {
                Assert.That(names.Contains(structural[i]), Is.True, typeId + " lists '" + structural[i] + "', which is not an authorable field of " + type.Name);
            }
        }

        private static void SetAuthoringId(ScriptableObject asset, string value)
        {
            for (Type? type = asset.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo? field = type.GetField("authoringId", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(asset, value);
                    return;
                }
            }

            Assert.Fail(asset.GetType().Name + " has no authoringId field");
        }

        private static bool HasCode(IReadOnlyList<GameplayDiagnostic> diagnostics, string code)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (string.Equals(diagnostics[i].Code, code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string Describe(IReadOnlyList<GameplayDiagnostic> diagnostics)
        {
            var parts = new List<string>();
            for (int i = 0; i < diagnostics.Count; i++)
            {
                parts.Add(diagnostics[i].ToString());
            }

            return parts.Count == 0 ? "(no diagnostics)" : string.Join("; ", parts);
        }

        private static WorldDefinition RequireWorld()
        {
            WorldDefinition? world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(HollowmereWorldAuthoring.WorldPath);
            Assert.That(world, Is.Not.Null, "the committed Hollowmere world exists at " + HollowmereWorldAuthoring.WorldPath);
            return world!;
        }

        private static EntityDefinition CommittedDefinitionWithPrefab()
        {
            foreach (EntityDefinition definition in CommittedEntityDefinitions())
            {
                if (definition.Prefab != null)
                {
                    return definition;
                }
            }

            Assert.Fail("no committed entity definition with a prefab under " + HollowmereAssets);
            throw new InvalidOperationException();
        }

        private static List<EntityDefinition> CommittedEntityDefinitions()
        {
            var definitions = new List<EntityDefinition>();
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(EntityDefinition), new[] { HollowmereAssets });
            Array.Sort(guids, string.CompareOrdinal);
            for (int i = 0; i < guids.Length; i++)
            {
                EntityDefinition? definition = AssetDatabase.LoadAssetAtPath<EntityDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (definition != null)
                {
                    definitions.Add(definition);
                }
            }

            return definitions;
        }

        /// <summary>The world, its regions and portals, every committed entity definition and variant, and the narrative closure.</summary>
        private static List<ScriptableObject> WatchedAssets(WorldDefinition world)
        {
            var watched = new List<ScriptableObject> { world };
            var seen = new HashSet<int> { world.GetInstanceID() };
            void Add(ScriptableObject? asset)
            {
                if (asset != null && seen.Add(asset.GetInstanceID()))
                {
                    watched.Add(asset);
                }
            }

            for (int i = 0; i < world.Regions.Count; i++)
            {
                Add(world.Regions[i]);
            }

            for (int i = 0; i < world.Portals.Count; i++)
            {
                Add(world.Portals[i]);
            }

            foreach (EntityDefinition definition in CommittedEntityDefinitions())
            {
                Add(definition);
                for (int v = 0; v < definition.Variants.Count; v++)
                {
                    Add(definition.Variants[v]);
                }
            }

            List<GameplayContentSet> sets = NarrativeBake.FindContentSets(world, out List<string> _);
            for (int s = 0; s < sets.Count; s++)
            {
                Add(sets[s]);
                foreach (ScriptableObject definition in NarrativeAuthoring.Closure(sets[s].Definitions))
                {
                    Add(definition);
                }
            }

            return watched;
        }

        private static Dictionary<string, byte[]?> Snapshot(List<string> paths)
        {
            var bytes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Count; i++)
            {
                string full = Path.GetFullPath(paths[i]);
                bytes[paths[i]] = File.Exists(full) ? File.ReadAllBytes(full) : null;
            }

            return bytes;
        }
    }
}
