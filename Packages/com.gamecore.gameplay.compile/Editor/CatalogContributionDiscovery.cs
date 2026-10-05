// GameCore.Gameplay.Compile.Editor - discovery of gameplay catalog contributors (P1.3 seam).
//
// Every non-abstract IGameplayCatalogContributor with a public parameterless constructor in the project's Editor
// domain contributes; the result is ordered by package name (GameplayCatalogContributions.Canonical), so the order of
// type discovery never reaches the bake output.
#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Finds the project's catalog contributors.</summary>
    public static class CatalogContributionDiscovery
    {
        public static IReadOnlyList<GameplayCatalogContribution> Discover()
        {
            var found = new List<GameplayCatalogContribution>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IGameplayCatalogContributor>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                var contributor = (IGameplayCatalogContributor?)Activator.CreateInstance(type);
                if (contributor != null)
                {
                    found.Add(contributor.Contribution);
                }
            }

            return GameplayCatalogContributions.Canonical(found);
        }
    }
}
