// GameCore.Gameplay.Logic.Editor - the narrative part of the gameplay bake (P1.4, through IGameplayBakeExtension).
//
// For the world being baked, the logic extension finds its one GameplayContentSet, collects the listed narrative
// definitions plus every narrative definition they reference (transitively), mints missing authoring ids, converts
// them with every package converter (TypeCache) to validate them - each conversion problem is a GP-* diagnostic that
// refuses the bake - and computes the content manifest: entries (kind, authoring id, name, key, content stamp, asset)
// in authoring-id order, the fact table in name order, and a SHA-256 content hash. Write stamps every definition and
// writes <set>.content.asset next to the set when anything changed; Verify recomputes and compares, writing nothing.
//
// The catalog registrations of the four narrative plugins come in through P1.3's IGameplayCatalogContributor (one
// contributor per package: LogicCatalogContributor here, DialogueCatalogContributor, QuestCatalogContributor and
// InventoryCatalogContributor in the other packages), whether or not the world has a content set.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Logic.Editor
{
    /// <summary>The computed narrative bake of one content set.</summary>
    public sealed class NarrativeBakePlan
    {
        public NarrativeBakePlan(GameplayContentSet set, string setPath, string worldId)
        {
            Set = set;
            SetPath = setPath;
            WorldId = worldId;
            ManifestPath = Path.GetDirectoryName(setPath)!.Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(setPath) + ".content.asset";
        }

        public GameplayContentSet Set { get; }

        public string SetPath { get; }

        public string ManifestPath { get; }

        public string WorldId { get; }

        public List<ScriptableObject> Definitions { get; } = new List<ScriptableObject>();

        public List<ContentEntry> Entries { get; } = new List<ContentEntry>();

        public List<FactEntry> Facts { get; } = new List<FactEntry>();

        public string ContentHash { get; set; } = string.Empty;

        public NarrativeModelSet? Models { get; set; }
    }

    /// <summary>Content-set discovery, planning, writing and verifying.</summary>
    public static class NarrativeBake
    {
        private static readonly Regex CodePattern = new Regex("GP-[A-Z]{3}-[0-9]{3}", RegexOptions.CultureInvariant);

        /// <summary>The content sets that belong to <paramref name="world"/>, in path order.</summary>
        public static List<GameplayContentSet> FindContentSets(WorldDefinition world, out List<string> paths)
        {
            var sets = new List<GameplayContentSet>();
            paths = new List<string>();
            var found = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(GameplayContentSet)))
            {
                found.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            found.Sort(string.CompareOrdinal);
            for (int i = 0; i < found.Count; i++)
            {
                GameplayContentSet? set = AssetDatabase.LoadAssetAtPath<GameplayContentSet>(found[i]);
                if (set != null && set.World == world)
                {
                    sets.Add(set);
                    paths.Add(found[i]);
                }
            }

            return sets;
        }

        /// <summary>True when the world has narrative content (the narrative packages then contribute their catalog entries).</summary>
        public static bool HasContent(WorldDefinition world) => FindContentSets(world, out List<string> _).Count > 0;

        /// <summary>Every package converter, found with TypeCache.</summary>
        public static List<INarrativeContentConverter> Converters()
        {
            var converters = new List<INarrativeContentConverter>();
            var types = new List<Type>(TypeCache.GetTypesDerivedFrom<INarrativeContentConverter>());
            types.Sort((l, r) => string.CompareOrdinal(l.FullName, r.FullName));
            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is INarrativeContentConverter converter)
                {
                    converters.Add(converter);
                }
            }

            return converters;
        }

        /// <summary>Plans the bake of a content set; problems are appended to <paramref name="diagnostics"/>.</summary>
        public static NarrativeBakePlan Plan(GameplayContentSet set, string setPath, string worldId, List<GameplayDiagnostic> diagnostics)
        {
            var plan = new NarrativeBakePlan(set, setPath, worldId);
            var seen = new HashSet<int>();
            var queue = new Queue<ScriptableObject>();
            for (int i = 0; i < set.Definitions.Count; i++)
            {
                ScriptableObject definition = set.Definitions[i];
                if (definition == null)
                {
                    continue;
                }

                if (!(definition is INarrativeDefinition))
                {
                    diagnostics.Add(new GameplayDiagnostic(NarrativeDiagnosticCodes.ContentUnknownKind, setPath, definition.name + " is not a narrative definition"));
                    continue;
                }

                if (!seen.Add(definition.GetInstanceID()))
                {
                    diagnostics.Add(new GameplayDiagnostic(NarrativeDiagnosticCodes.ContentSetDuplicate, setPath, definition.name + " is listed twice"));
                    continue;
                }

                queue.Enqueue(definition);
            }

            while (queue.Count > 0)
            {
                ScriptableObject definition = queue.Dequeue();
                plan.Definitions.Add(definition);
                foreach (ScriptableObject referenced in References(definition))
                {
                    if (referenced is INarrativeDefinition && seen.Add(referenced.GetInstanceID()))
                    {
                        queue.Enqueue(referenced);
                    }
                }
            }

            for (int i = 0; i < plan.Definitions.Count; i++)
            {
                var definition = (INarrativeDefinition)plan.Definitions[i];
                if (definition.EnsureAuthoringId())
                {
                    EditorUtility.SetDirty(plan.Definitions[i]);
                }
            }

            NarrativeConversion conversion = NarrativeContent.Convert(worldId, plan.Definitions, Converters());
            plan.Models = conversion.Models;
            for (int i = 0; i < conversion.Models.Problems.Count; i++)
            {
                string problem = conversion.Models.Problems[i];
                Match code = CodePattern.Match(problem);
                diagnostics.Add(new GameplayDiagnostic(
                    code.Success ? code.Value : NarrativeDiagnosticCodes.ContentKeyCollision, setPath, problem));
            }

            var keys = new Dictionary<int, string>();
            for (int i = 0; i < plan.Definitions.Count; i++)
            {
                ScriptableObject asset = plan.Definitions[i];
                var definition = (INarrativeDefinition)asset;
                int key = NarrativeRefs.KeyOf(asset);
                if (keys.TryGetValue(key, out string? other))
                {
                    diagnostics.Add(new GameplayDiagnostic(NarrativeDiagnosticCodes.ContentKeyCollision, definition.AuthoringId,
                        definition.DefinitionName + " has the key of " + other));
                }
                else
                {
                    keys[key] = definition.DefinitionName;
                }

                plan.Entries.Add(new ContentEntry
                {
                    kind = definition.NarrativeKind,
                    authoringId = definition.AuthoringId,
                    name = definition.DefinitionName,
                    key = key,
                    contentStamp = DefinitionCanonicalizer.ContentStamp(asset),
                    asset = asset,
                });
                if (asset is IFactDefinition fact)
                {
                    plan.Facts.Add(new FactEntry
                    {
                        name = fact.FactName,
                        key = key,
                        initial = fact.InitialValue,
                        persistent = fact.Persistent,
                        authoringId = fact.AuthoringId,
                    });
                }
            }

            plan.Entries.Sort((l, r) => string.CompareOrdinal(l.authoringId, r.authoringId));
            plan.Facts.Sort((l, r) => string.CompareOrdinal(l.name, r.name));
            plan.ContentHash = HashOf(worldId, plan.Entries, plan.Facts);
            return plan;
        }

        /// <summary>SHA-256 (lowercase hex) of the canonical manifest text.</summary>
        public static string HashOf(string worldId, IReadOnlyList<ContentEntry> entries, IReadOnlyList<FactEntry> facts)
        {
            var text = new StringBuilder(GameplayContentManifest.Format).Append('\n').Append("world=").Append(worldId).Append('\n');
            for (int i = 0; i < entries.Count; i++)
            {
                ContentEntry entry = entries[i];
                text.Append(entry.kind).Append('|').Append(entry.authoringId).Append('|').Append(entry.name).Append('|')
                    .Append(entry.key.ToString(CultureInfo.InvariantCulture)).Append('|').Append(entry.contentStamp).Append('\n');
            }

            for (int i = 0; i < facts.Count; i++)
            {
                FactEntry fact = facts[i];
                text.Append("fact|").Append(fact.name).Append('|').Append(fact.key.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(fact.initial.ToString(CultureInfo.InvariantCulture)).Append('|').Append(fact.persistent ? "1" : "0").Append('\n');
            }

            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(new UTF8Encoding(false).GetBytes(text.ToString()));
            }

            var hex = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }

        /// <summary>Stamps the definitions and writes the manifest when anything changed.</summary>
        public static void Write(NarrativeBakePlan plan, ICollection<string> changedFiles)
        {
            for (int i = 0; i < plan.Entries.Count; i++)
            {
                ContentEntry entry = plan.Entries[i];
                if (entry.asset is INarrativeDefinition definition && !string.Equals(definition.ContentStamp, entry.contentStamp, StringComparison.Ordinal))
                {
                    definition.SetContentStamp(entry.contentStamp);
                    EditorUtility.SetDirty(entry.asset);
                    changedFiles.Add(AssetDatabase.GetAssetPath(entry.asset));
                }
            }

            GameplayContentManifest? manifest = AssetDatabase.LoadAssetAtPath<GameplayContentManifest>(plan.ManifestPath);
            if (manifest != null && Same(manifest, plan))
            {
                return;
            }

            bool created = manifest == null;
            if (manifest == null)
            {
                manifest = ScriptableObject.CreateInstance<GameplayContentManifest>();
            }

            manifest.Assign(plan.WorldId, plan.ContentHash, plan.Entries, plan.Facts);
            if (created)
            {
                AssetDatabase.CreateAsset(manifest, plan.ManifestPath);
            }
            else
            {
                EditorUtility.SetDirty(manifest);
            }

            changedFiles.Add(plan.ManifestPath);
        }

        /// <summary>Compares the manifest and the definition stamps with the plan.</summary>
        public static void Verify(NarrativeBakePlan plan, ICollection<GameplayDiagnostic> mismatches)
        {
            GameplayContentManifest? manifest = AssetDatabase.LoadAssetAtPath<GameplayContentManifest>(plan.ManifestPath);
            if (manifest == null || !Same(manifest, plan))
            {
                mismatches.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.BakeVerifyMismatch, plan.ManifestPath,
                    "the content manifest is not the bake of the current narrative content"));
            }

            for (int i = 0; i < plan.Entries.Count; i++)
            {
                ContentEntry entry = plan.Entries[i];
                if (entry.asset is INarrativeDefinition definition && !string.Equals(definition.ContentStamp, entry.contentStamp, StringComparison.Ordinal))
                {
                    mismatches.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.BakeVerifyMismatch, AssetDatabase.GetAssetPath(entry.asset),
                        definition.DefinitionName + " carries a stale content stamp"));
                }
            }
        }

        private static bool Same(GameplayContentManifest manifest, NarrativeBakePlan plan)
        {
            if (!string.Equals(manifest.FormatId, GameplayContentManifest.Format, StringComparison.Ordinal)
                || !string.Equals(manifest.ContentHash, plan.ContentHash, StringComparison.Ordinal)
                || manifest.Entries.Count != plan.Entries.Count)
            {
                return false;
            }

            for (int i = 0; i < plan.Entries.Count; i++)
            {
                if (manifest.Entries[i].asset != plan.Entries[i].asset)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The ScriptableObjects a definition references through its serialized fields.</summary>
        public static List<ScriptableObject> References(ScriptableObject asset)
        {
            var references = new List<ScriptableObject>();
            var serialized = new SerializedObject(asset);
            SerializedProperty property = serialized.GetIterator();
            bool enter = true;
            while (property.Next(enter))
            {
                enter = property.propertyType != SerializedPropertyType.String;
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is ScriptableObject referenced
                    && referenced != asset)
                {
                    references.Add(referenced);
                }
            }

            return references;
        }
    }

    /// <summary>The logic plugin's catalog registrations (P1.3's catalog contribution seam).</summary>
    public sealed class LogicCatalogContributor : IGameplayCatalogContributor
    {
        public GameplayCatalogContribution Contribution =>
            new GameplayCatalogContribution("com.gamecore.gameplay.logic", NarrativeCatalogNames.Logic.Schemas, NarrativeCatalogNames.Logic.Entries);
    }

    /// <summary>The logic package's bake extension: the content manifest and the logic plugin's catalog registrations.</summary>
    public sealed class LogicBakeExtension : IGameplayBakeExtension
    {
        private const string PlanKey = "gameplay.narrative.plan";

        public string ExtensionId => "gameplay.narrative.logic";

        public void Plan(GameplayBakeContext context)
        {
            List<GameplayContentSet> sets = NarrativeBake.FindContentSets(context.World, out List<string> paths);
            if (sets.Count == 0)
            {
                return;
            }

            if (sets.Count > 1)
            {
                context.AddDiagnostic(new GameplayDiagnostic(NarrativeDiagnosticCodes.ContentSetDuplicate, paths[1],
                    "world " + context.World.name + " has " + sets.Count.ToString(CultureInfo.InvariantCulture) + " content sets; one is allowed"));
                return;
            }

            var diagnostics = new List<GameplayDiagnostic>();
            NarrativeBakePlan plan = NarrativeBake.Plan(sets[0], paths[0], context.World.AuthoringId, diagnostics);
            for (int i = 0; i < diagnostics.Count; i++)
            {
                context.AddDiagnostic(diagnostics[i]);
            }

            context.Put(PlanKey, plan);
        }

        public void Write(GameplayBakeContext context, ICollection<string> changedFiles)
        {
            if (context.TryGet(PlanKey, out NarrativeBakePlan? plan) && plan != null)
            {
                NarrativeBake.Write(plan, changedFiles);
            }
        }

        public void Verify(GameplayBakeContext context, ICollection<GameplayDiagnostic> mismatches)
        {
            if (context.TryGet(PlanKey, out NarrativeBakePlan? plan) && plan != null)
            {
                NarrativeBake.Verify(plan, mismatches);
            }
        }

        /// <summary>The plan of the last Plan (EditMode tests read the models from it).</summary>
        public static NarrativeBakePlan? PlanOf(GameplayBakeContext context) =>
            context.TryGet(PlanKey, out NarrativeBakePlan? plan) ? plan : null;
    }
}
