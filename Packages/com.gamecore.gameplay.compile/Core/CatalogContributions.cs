// GameCore.Gameplay.Compile - catalog contributions of gameplay plugin packages beyond entities and world (P1.3 seam).
//
// The generated gameplay catalog always declares GameplayCatalogNames' static registrations (the entities and world
// plugins). A gameplay package that adds its own kernel plugin (player, npc, interaction, and the P1.4/P1.5 groups)
// contributes its plugin factory, system factories, slot layouts and one-field schemas here, from an Editor-side
// IGameplayCatalogContributor with a public parameterless constructor. The bake discovers contributors (Editor
// TypeCache), orders them by package name (ordinal) and appends their registrations after the static ones, so the
// description stays deterministic and a project without the package bakes exactly as before.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;

namespace GameCore.Gameplay.Compile
{
    /// <summary>The catalog registrations one gameplay package adds.</summary>
    public sealed class GameplayCatalogContribution
    {
        public GameplayCatalogContribution(
            string packageName,
            IReadOnlyList<GameplayCatalogNames.SchemaName> schemas,
            IReadOnlyList<GameplayCatalogNames.EntryName> entries)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                throw new ArgumentException("A catalog contribution names its package.", nameof(packageName));
            }

            PackageName = packageName;
            Schemas = schemas ?? Array.Empty<GameplayCatalogNames.SchemaName>();
            Entries = entries ?? Array.Empty<GameplayCatalogNames.EntryName>();
        }

        /// <summary>The package's catalog name (e.g. <c>package.player</c>); contributions are ordered by it.</summary>
        public string PackageName { get; }

        public IReadOnlyList<GameplayCatalogNames.SchemaName> Schemas { get; }

        public IReadOnlyList<GameplayCatalogNames.EntryName> Entries { get; }
    }

    /// <summary>An Editor-side provider of one package's catalog contribution (public parameterless constructor).</summary>
    public interface IGameplayCatalogContributor
    {
        GameplayCatalogContribution Contribution { get; }
    }

    /// <summary>Ordering and validation of contributions.</summary>
    public static class GameplayCatalogContributions
    {
        /// <summary>
        /// The contributions sorted by package name; throws when two contributions name the same package or register a
        /// stable name twice (also against the static registrations).
        /// </summary>
        public static IReadOnlyList<GameplayCatalogContribution> Canonical(IEnumerable<GameplayCatalogContribution>? contributions)
        {
            var list = new List<GameplayCatalogContribution>();
            if (contributions != null)
            {
                foreach (GameplayCatalogContribution contribution in contributions)
                {
                    if (contribution != null)
                    {
                        list.Add(contribution);
                    }
                }
            }

            list.Sort((l, r) => string.CompareOrdinal(l.PackageName, r.PackageName));
            var packages = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < GameplayCatalogNames.StaticEntries.Count; i++)
            {
                names.Add(GameplayCatalogNames.StaticEntries[i].Name);
            }

            for (int i = 0; i < GameplayCatalogNames.Schemas.Count; i++)
            {
                names.Add(GameplayCatalogNames.Schemas[i].Schema);
            }

            for (int i = 0; i < list.Count; i++)
            {
                GameplayCatalogContribution contribution = list[i];
                if (!packages.Add(contribution.PackageName))
                {
                    throw new InvalidOperationException("two catalog contributions name package " + contribution.PackageName);
                }

                for (int s = 0; s < contribution.Schemas.Count; s++)
                {
                    if (!names.Add(contribution.Schemas[s].Schema))
                    {
                        throw new InvalidOperationException("catalog schema " + contribution.Schemas[s].Schema + " is registered twice");
                    }
                }

                for (int e = 0; e < contribution.Entries.Count; e++)
                {
                    if (!names.Add(contribution.Entries[e].Name))
                    {
                        throw new InvalidOperationException("catalog registration " + contribution.Entries[e].Name + " is registered twice");
                    }
                }
            }

            return list;
        }
    }
}
