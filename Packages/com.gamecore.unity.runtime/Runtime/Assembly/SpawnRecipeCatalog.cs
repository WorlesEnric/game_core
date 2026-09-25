// GameCore.Unity.Runtime — generated-style spawn recipes and their precompiled variant cache (GC-008).
//
// Normative sources: 00 P-024 (a precompiled `SpawnRecipe` contains base schema/layout, immutable definitions,
// descriptor, required asset leases, auxiliary entity recipe and derivation template; derived variants are cached
// by recipe revision, scope inheritance fingerprint, mode and catalog hash; on spawn publication the variant is
// validated against the current composition revision and a stale variant is recomputed or rejected `StalePlan`,
// never published half-assembled), 04 s6 (a generated `SpawnRecipeId` entry carries its compatible schema/trait
// set, prefab/archetype references, initializer and supported derived-binding installers; unknown schema/recipe
// ids fail before activation with a diagnostic naming the missing catalog entry) and P-058 (precompiled plugins
// only: no runtime code loading, no reflection).
//
// Recipes are supplied as data plus a direct typed applier: the fixture (and, later, generated code) writes them
// by hand, and a miss is reported as a diagnostic instead of being substituted by a default recipe.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using Unity.Entities;

namespace GameCore.Unity.Runtime
{
    /// <summary>
    /// The base layout + initializer of one recipe. A generated entry point supplies this as a direct typed call;
    /// nothing here is discovered reflectively (04 s8).
    /// </summary>
    public interface ISpawnApplier
    {
        /// <summary>Generated registration key of this applier (P-009).</summary>
        FactoryKey Key { get; }

        /// <summary>
        /// Installs the recipe's base layout on a freshly created, pending entity: components only, no derived rows.
        /// The assembly publisher adds the derived rows before the target becomes visible (P-024).
        /// </summary>
        void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe);
    }

    /// <summary>
    /// One precompiled spawn recipe (P-024). The descriptor is the shared immutable compatibility record; the
    /// layout is the closed set of base component schema the applier installs.
    /// </summary>
    public sealed class SpawnRecipe
    {
        public SpawnRecipe(
            DefinitionRef recipe,
            TargetDescriptor descriptor,
            IReadOnlyList<SchemaRef>? baseLayout,
            ISpawnApplier applier)
        {
            Recipe = recipe;
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            BaseLayout = ContractCollections.Freeze(baseLayout);
            Applier = applier ?? throw new ArgumentNullException(nameof(applier));
        }

        public DefinitionRef Recipe { get; }

        /// <summary>Immutable reusable compatibility descriptor: schemas, capabilities, tags and sparse overrides.</summary>
        public TargetDescriptor Descriptor { get; }

        public IReadOnlyList<SchemaRef> BaseLayout { get; }

        public ISpawnApplier Applier { get; }

        public DefinitionRevision Revision => Recipe.Revision;

        public override string ToString() => Recipe.ToString() + ":" + Applier.Key.ToString();
    }

    /// <summary>
    /// Cache key of one derived variant (P-024): recipe revision, scope inheritance fingerprint, mode and catalog
    /// hash. Two spawns with the same key derive the same assembly, which is what makes a future target appear
    /// fully assembled without per-instance imports (P-013).
    /// </summary>
    public readonly struct DerivedVariantKey : IEquatable<DerivedVariantKey>
    {
        public readonly DefinitionRef Recipe;
        public readonly ScopeId Scope;
        public readonly ContentHash InheritanceFingerprint;
        public readonly PropagationMode Mode;
        public readonly ContentHash CatalogHash;

        public DerivedVariantKey(
            DefinitionRef recipe,
            ScopeId scope,
            ContentHash inheritanceFingerprint,
            PropagationMode mode,
            ContentHash catalogHash)
        {
            Recipe = recipe;
            Scope = scope;
            InheritanceFingerprint = inheritanceFingerprint;
            Mode = mode;
            CatalogHash = catalogHash;
        }

        public bool Equals(DerivedVariantKey other) =>
            Recipe.Equals(other.Recipe)
            && Scope.Equals(other.Scope)
            && InheritanceFingerprint.Equals(other.InheritanceFingerprint)
            && Mode == other.Mode
            && CatalogHash.Equals(other.CatalogHash);

        public override bool Equals(object? obj) => obj is DerivedVariantKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Recipe.GetHashCode();
                hash = (hash * 31) + InheritanceFingerprint.GetHashCode();
                hash = (hash * 31) + (int)Mode;
                hash = (hash * 31) + CatalogHash.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => Recipe.ToString() + "@" + Scope.ToString() + ":" + Mode;
    }

    /// <summary>
    /// The world's closed recipe catalog: stable recipe id to one precompiled recipe. An unknown recipe id or an
    /// unsupported schema is `MissingDependency`/`UnsupportedVersion` before activation, and never a guessed
    /// adapter (P-015, 04 s6).
    /// </summary>
    public sealed class SpawnRecipeCatalog
    {
        private readonly Dictionary<Id128, SpawnRecipe> byRecipeId = new Dictionary<Id128, SpawnRecipe>();
        private readonly List<SpawnRecipe> ordered = new List<SpawnRecipe>();

        public SpawnRecipeCatalog(IReadOnlyList<SpawnRecipe>? recipes)
        {
            if (recipes == null)
            {
                return;
            }

            for (int i = 0; i < recipes.Count; i++)
            {
                SpawnRecipe recipe = recipes[i];
                if (recipe == null || recipe.Recipe.Id.IsDefault)
                {
                    continue;
                }

                if (!byRecipeId.ContainsKey(recipe.Recipe.Id.Value))
                {
                    byRecipeId.Add(recipe.Recipe.Id.Value, recipe);
                    ordered.Add(recipe);
                }
            }
        }

        public int Count => ordered.Count;

        public IReadOnlyList<SpawnRecipe> Recipes => ordered;

        /// <summary>Resolutions refused because the recipe revision is not the registered one (P-024 `StalePlan`).</summary>
        public int StaleRevisionCount { get; private set; }

        /// <summary>Resolutions refused because no recipe with that stable id is registered (P-009).</summary>
        public int UnknownRecipeCount { get; private set; }

        /// <summary>
        /// Resolves the recipe for a request. An unknown id is a miss; a known id at another revision is `StalePlan`,
        /// because a stale variant is recomputed or rejected, never published half-assembled (P-024).
        /// </summary>
        public bool TryResolve(DefinitionRef requested, out SpawnRecipe? recipe, out DiagnosticCode code)
        {
            if (byRecipeId.TryGetValue(requested.Id.Value, out SpawnRecipe found))
            {
                if (!found.Recipe.Revision.Equals(requested.Revision))
                {
                    StaleRevisionCount++;
                    recipe = null;
                    code = DiagnosticCode.StalePlan;
                    return false;
                }

                recipe = found;
                code = DiagnosticCode.None;
                return true;
            }

            UnknownRecipeCount++;
            recipe = null;
            code = DiagnosticCode.MissingDependency;
            return false;
        }

        /// <summary>
        /// Validates a recipe against the declared composition revision and the schema it must support (P-024):
        /// a spawn prepared before a composition edit is revalidated here rather than activated stale.
        /// </summary>
        public bool TryValidateForPublication(
            DefinitionRef requested,
            CompositionRevision currentRevision,
            CompositionRevision expectedRevision,
            out SpawnRecipe? recipe,
            out DiagnosticCode code)
        {
            if (!TryResolve(requested, out recipe, out code))
            {
                return false;
            }

            if (!currentRevision.Equals(expectedRevision))
            {
                // The variant belongs to another composition revision: recompute or reject, never publish stale.
                StaleRevisionCount++;
                recipe = null;
                code = DiagnosticCode.StalePlan;
                return false;
            }

            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>Canonical fingerprint of the recipe table; part of a variant's cache key (P-024).</summary>
        public ContentHash Fingerprint()
        {
            var text = new System.Text.StringBuilder();
            text.Append("recipes\n");
            for (int i = 0; i < ordered.Count; i++)
            {
                SpawnRecipe recipe = ordered[i];
                text.Append("recipe=")
                    .Append(PlanHashing.IdText(recipe.Recipe.Id.Value)).Append(';')
                    .Append(PlanHashing.IdText(recipe.Recipe.Schema.Id.Value)).Append(';')
                    .Append(recipe.Recipe.Schema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                    .Append(recipe.Recipe.Revision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                    .Append(PlanHashing.IdText(recipe.Applier.Key.RegistrationKey)).Append(';')
                    .Append(recipe.Applier.Key.KeyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            return PlanHashing.Of(text.ToString());
        }
    }
}
