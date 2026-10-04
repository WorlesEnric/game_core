// GameCore.Gameplay.Save - the authorable save schema (plugin catalog row 12, SADR-012 (studio)).
//
// A save schema is data an author edits in the Studio: which slot schemas a save carries and at which version this
// build writes each one, which forward migration ids a save from an older build may need, and which earlier catalog
// fingerprints this build declares compatible. Migration *bodies* are code (pure `SlotMigrationStep`s registered by
// the game, 05 s5); the schema names them by id, so an author can see "this build needs migration X" and a missing body
// is a load-time error rather than a silent zero (P-032, P-054).
//
// Identities are stable names, derived with the kernel's `StableNameKeyDerivation` exactly as every plugin derives its
// owner, slot and schema ids (P-004): the owner `game.player`, the slot `game.player.health`, the schema
// `game.player.health.schema`.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using GameCore.Studio.Model;
using GameCore.Unity.App;
using UnityEngine;

namespace GameCore.Gameplay.Save
{
    /// <summary>One slot schema a save carries and the version this build writes (P-032).</summary>
    [Serializable]
    public sealed class SaveSlotSchemaEntry
    {
        [Tooltip("Stable name of the slot's owner (derived to an OwnerId).")]
        public string owner = string.Empty;

        [Tooltip("Stable name of the slot (derived to a SlotId).")]
        public string slot = string.Empty;

        [Tooltip("Stable name of the slot's schema (derived to a SchemaId).")]
        public string schema = string.Empty;

        [Tooltip("The schema version this build writes.")]
        public uint currentVersion = 1U;
    }

    /// <summary>One forward migration a save may need, named by id; its body is registered in code (P-054).</summary>
    [Serializable]
    public sealed class SaveMigrationEntry
    {
        [Tooltip("Stable migration id; the game registers a SlotMigrationStep with the same id.")]
        public string migrationId = string.Empty;

        [Tooltip("Stable name of the slot schema the migration converts.")]
        public string schema = string.Empty;

        public uint fromVersion = 1U;

        public uint toVersion = 2U;
    }

    /// <summary>What a save schema validation found; empty when the schema is well formed.</summary>
    public sealed class SaveSchemaValidation
    {
        private readonly List<string> problems = new List<string>();

        public IReadOnlyList<string> Problems => problems;

        public bool IsValid => problems.Count == 0;

        internal void Add(string problem) => problems.Add(problem);

        public override string ToString() => IsValid ? "valid" : string.Join("; ", problems.ToArray());
    }

    /// <summary>The authorable save schema of a game (catalog row 12).</summary>
    [Authorable("save.schema", DisplayName = "Save schema", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "Slot schemas a save carries, the version this build writes, the forward migration ids a save may need and the compatible catalogs.")]
    [CreateAssetMenu(menuName = "GameCore/Save Schema", fileName = "SaveSchema")]
    public sealed class SaveSchemaDefinition : ScriptableObject
    {
        [Tooltip("The game id written to every save header; a save from another game id is refused.")]
        public string gameId = string.Empty;

        public List<SaveSlotSchemaEntry> slotSchemas = new List<SaveSlotSchemaEntry>();

        public List<SaveMigrationEntry> migrations = new List<SaveMigrationEntry>();

        [Tooltip("Lowercase hex fingerprints of earlier catalogs this build declares compatible through its migrations.")]
        public List<string> compatibleCatalogs = new List<string>();

        /// <summary>Checks names, versions and migration chains without touching any registered body.</summary>
        public SaveSchemaValidation Validate()
        {
            var validation = new SaveSchemaValidation();
            if (string.IsNullOrEmpty(gameId))
            {
                validation.Add("the save schema names no game id");
            }

            var pairs = new HashSet<string>(StringComparer.Ordinal);
            var currentBySchema = new Dictionary<string, uint>(StringComparer.Ordinal);
            for (int i = 0; i < slotSchemas.Count; i++)
            {
                SaveSlotSchemaEntry entry = slotSchemas[i];
                string at = "slotSchemas[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                CheckName(validation, at + ".owner", entry.owner);
                CheckName(validation, at + ".slot", entry.slot);
                CheckName(validation, at + ".schema", entry.schema);
                if (entry.currentVersion == 0U)
                {
                    validation.Add(at + " declares version 0; schema versions start at 1");
                }

                if (!pairs.Add(entry.owner + "|" + entry.slot))
                {
                    validation.Add(at + " repeats slot " + entry.slot + " of owner " + entry.owner);
                }

                if (currentBySchema.TryGetValue(entry.schema, out uint existing) && existing != entry.currentVersion)
                {
                    validation.Add(at + " writes schema " + entry.schema + " at v" + entry.currentVersion.ToString(CultureInfo.InvariantCulture)
                        + " and another slot writes it at v" + existing.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    currentBySchema[entry.schema] = entry.currentVersion;
                }
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < migrations.Count; i++)
            {
                SaveMigrationEntry entry = migrations[i];
                string at = "migrations[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                CheckName(validation, at + ".migrationId", entry.migrationId);
                CheckName(validation, at + ".schema", entry.schema);
                if (!ids.Add(entry.migrationId))
                {
                    validation.Add(at + " repeats migration id " + entry.migrationId);
                }

                if (entry.toVersion <= entry.fromVersion)
                {
                    validation.Add(at + " (" + entry.migrationId + ") is not forward: v" + entry.fromVersion.ToString(CultureInfo.InvariantCulture)
                        + " -> v" + entry.toVersion.ToString(CultureInfo.InvariantCulture));
                }

                if (currentBySchema.TryGetValue(entry.schema, out uint current) && entry.toVersion > current)
                {
                    validation.Add(at + " (" + entry.migrationId + ") migrates past the current v" + current.ToString(CultureInfo.InvariantCulture));
                }
            }

            for (int i = 0; i < compatibleCatalogs.Count; i++)
            {
                if (!ContentHash.TryParseHex(compatibleCatalogs[i], out ContentHash _))
                {
                    validation.Add("compatibleCatalogs[" + i.ToString(CultureInfo.InvariantCulture) + "] is not a 64-character lowercase hex fingerprint");
                }
            }

            return validation;
        }

        /// <summary>The slot schema catalog this schema declares (P-032).</summary>
        public SlotSchemaCatalog ToSlotSchemaCatalog()
        {
            var catalog = new SlotSchemaCatalog();
            for (int i = 0; i < slotSchemas.Count; i++)
            {
                SaveSlotSchemaEntry entry = slotSchemas[i];
                catalog.Add(new SlotSchemaBinding(
                    new OwnerId(StableNameKeyDerivation.Derive(entry.owner)),
                    new SlotId(StableNameKeyDerivation.Derive(entry.slot)),
                    new SchemaRef(new SchemaId(StableNameKeyDerivation.Derive(entry.schema)), entry.currentVersion),
                    entry.schema));
            }

            return catalog;
        }

        /// <summary>The header's schema version list (one row per distinct schema).</summary>
        public IReadOnlyList<SaveSchemaVersion> ToSchemaVersions()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var versions = new List<SaveSchemaVersion>();
            for (int i = 0; i < slotSchemas.Count; i++)
            {
                if (seen.Add(slotSchemas[i].schema))
                {
                    versions.Add(new SaveSchemaVersion(slotSchemas[i].schema, slotSchemas[i].currentVersion));
                }
            }

            versions.Sort((left, right) => string.CompareOrdinal(left.Schema, right.Schema));
            return versions;
        }

        /// <summary>The compatible catalogs as fingerprints; malformed entries are skipped (Validate reports them).</summary>
        public IReadOnlyList<ContentHash> ToCompatibleCatalogs()
        {
            var hashes = new List<ContentHash>();
            for (int i = 0; i < compatibleCatalogs.Count; i++)
            {
                if (ContentHash.TryParseHex(compatibleCatalogs[i], out ContentHash hash))
                {
                    hashes.Add(hash);
                }
            }

            return hashes;
        }

        /// <summary>
        /// The executable registry: every listed migration id must have a registered body in
        /// <paramref name="bodies"/> whose schema and versions match the listing. A missing or mismatched body is a
        /// problem in <paramref name="problems"/> and the step is left out, so a restore that needs it refuses with
        /// "migration path missing" rather than running something else (P-054).
        /// </summary>
        public SlotMigrationRegistry BuildMigrations(IEnumerable<SlotMigrationStep>? bodies, out IReadOnlyList<string> problems)
        {
            var found = new Dictionary<string, SlotMigrationStep>(StringComparer.Ordinal);
            if (bodies != null)
            {
                foreach (SlotMigrationStep body in bodies)
                {
                    if (body != null)
                    {
                        found[body.MigrationId] = body;
                    }
                }
            }

            var list = new List<string>();
            var registry = new SlotMigrationRegistry();
            for (int i = 0; i < migrations.Count; i++)
            {
                SaveMigrationEntry entry = migrations[i];
                if (!found.TryGetValue(entry.migrationId, out SlotMigrationStep? body) || body == null)
                {
                    list.Add("save needs migration " + entry.migrationId + " (" + entry.schema + " v"
                        + entry.fromVersion.ToString(CultureInfo.InvariantCulture) + " -> v"
                        + entry.toVersion.ToString(CultureInfo.InvariantCulture) + ") and the build registers no body for it");
                    continue;
                }

                var schemaId = new SchemaId(StableNameKeyDerivation.Derive(entry.schema));
                if (!body.From.Equals(new SchemaRef(schemaId, entry.fromVersion)) || !body.To.Equals(new SchemaRef(schemaId, entry.toVersion)))
                {
                    list.Add("migration " + entry.migrationId + " is listed as " + entry.schema + " v"
                        + entry.fromVersion.ToString(CultureInfo.InvariantCulture) + " -> v"
                        + entry.toVersion.ToString(CultureInfo.InvariantCulture) + " but its body converts " + body.From + " -> " + body.To);
                    continue;
                }

                if (!registry.Register(body))
                {
                    list.Add(registry.Rejections[registry.Rejections.Count - 1]);
                }
            }

            problems = list;
            return registry;
        }

        /// <summary>Fills a save service's options from this schema and the registered migration bodies.</summary>
        public IReadOnlyList<string> ApplyTo(SaveServiceOptions options, IEnumerable<SlotMigrationStep>? bodies)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.SlotSchemas = ToSlotSchemaCatalog();
            options.SlotMigrations = BuildMigrations(bodies, out IReadOnlyList<string> problems);
            options.CompatibleCatalogs = ToCompatibleCatalogs();
            options.SchemaVersions = ToSchemaVersions();
            return problems;
        }

        private static void CheckName(SaveSchemaValidation validation, string at, string? name)
        {
            if (!StableNameKeyDerivation.IsCanonicalStableName(name))
            {
                validation.Add(at + " is not a canonical stable name (" + StableNameKeyDerivation.AllowedCharacters + "): '" + (name ?? "null") + "'");
            }
        }
    }
}
