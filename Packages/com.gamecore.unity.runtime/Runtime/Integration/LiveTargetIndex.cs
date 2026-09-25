// GameCore.Unity.Runtime — W2 integration seam: the live targets of one owned world (P-010, P-015) as they appear
// to derivation (GC-006) and to the planner (GC-008).
//
// Both consumers need the same two facts per target — its stable identity with its single owner scope, and its
// immutable compatibility descriptor — but they want them in different shapes (`DerivationTarget` for GC-006,
// `TargetDefinition` for GC-008). The descriptor itself is never invented here: it is the one the world's
// precompiled recipe catalog already holds for that recipe, resolved by exact identity and exact revision, so a
// target whose recipe revision this catalog does not know is refused instead of being described by a guess.
//
// The index is engine-free on purpose (it holds no `Entity`): the ECS half of seeding a target, stamping it and
// reading its live state slots lives in `LiveTargetSeeder`, which owns the same index.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Planning;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>
    /// One live target as the composition sees it: stable identity, exactly one owner scope and the recipe whose
    /// descriptor describes what it is (P-010, P-015).
    /// </summary>
    public readonly struct LiveTarget
    {
        public readonly TargetId Target;
        public readonly ScopeId Scope;
        public readonly DefinitionRef Recipe;

        public LiveTarget(TargetId target, ScopeId scope, DefinitionRef recipe)
        {
            Target = target;
            Scope = scope;
            Recipe = recipe;
        }

        public override string ToString() => Target.ToString() + "@" + Scope.ToString() + ":" + Recipe.ToString();
    }

    /// <summary>
    /// The world's live targets, in canonical identity order, each with the descriptor its recipe resolves to.
    /// Registration is explicit: an unknown recipe, a stale recipe revision or a scope-less target is refused with
    /// the protocol's own code, because a target the catalog cannot describe cannot be derived for (P-009, P-015).
    /// </summary>
    public sealed class LiveTargetIndex
    {
        private readonly SpawnRecipeCatalog recipes;
        private readonly Dictionary<Id128, LiveTarget> byTarget = new Dictionary<Id128, LiveTarget>();
        private readonly List<LiveTarget> ordered = new List<LiveTarget>();

        public LiveTargetIndex(SpawnRecipeCatalog recipes)
        {
            this.recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        }

        public int Count => ordered.Count;

        /// <summary>Registered targets in canonical identity order, independent of registration timing (P-008).</summary>
        public IReadOnlyList<LiveTarget> Targets => ordered;

        /// <summary>
        /// Registers one live target after resolving its recipe against the recipe catalog. A recipe this catalog
        /// does not register, or registers at another revision, is refused: a stale variant is recomputed or
        /// rejected, never described by a substituted default (P-024).
        /// </summary>
        public bool TryRegister(TargetId target, ScopeId scope, DefinitionRef recipe, out DiagnosticCode code, out string detail)
        {
            if (target.IsDefault)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "a live target needs a real stable identity (P-004).";
                return false;
            }

            if (scope.IsDefault)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "target " + target.ToString() + " declares no owner scope; a live target has exactly one (P-010).";
                return false;
            }

            if (!recipes.TryResolve(recipe, out SpawnRecipe? resolved, out DiagnosticCode recipeCode) || resolved == null)
            {
                code = recipeCode;
                detail = "target " + target.ToString() + " declares recipe " + recipe.ToString()
                    + " which the recipe catalog does not resolve (P-009, P-024).";
                return false;
            }

            if (byTarget.ContainsKey(target.Value))
            {
                code = DiagnosticCode.OwnershipConflict;
                detail = "target " + target.ToString() + " is registered twice; one stable identity is one target (P-004).";
                return false;
            }

            var live = new LiveTarget(target, scope, resolved.Recipe);
            byTarget.Add(target.Value, live);
            ordered.Add(live);
            ordered.Sort(CompareTargets);
            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        /// <summary>
        /// Retires one target: it disappears from both consumer views in the same call, so a despawned target can
        /// never be derived for or planned against afterwards (P-024).
        /// </summary>
        public bool TryRetire(TargetId target)
        {
            if (!byTarget.Remove(target.Value))
            {
                return false;
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Target.Equals(target))
                {
                    ordered.RemoveAt(i);
                    break;
                }
            }

            return true;
        }

        public bool Contains(TargetId target) => byTarget.ContainsKey(target.Value);

        public bool TryGet(TargetId target, out LiveTarget live)
            => byTarget.TryGetValue(target.Value, out live);

        /// <summary>The immutable descriptor of one registered target, resolved from its recipe (P-015).</summary>
        public bool TryDescriptorOf(TargetId target, out TargetDescriptor? descriptor, out DiagnosticCode code, out string detail)
        {
            descriptor = null;
            if (!byTarget.TryGetValue(target.Value, out LiveTarget live))
            {
                code = DiagnosticCode.StaleHandle;
                detail = "target " + target.ToString() + " is not a live target of this world (P-005).";
                return false;
            }

            if (!recipes.TryResolve(live.Recipe, out SpawnRecipe? resolved, out code) || resolved == null)
            {
                detail = "target " + target.ToString() + " declares recipe " + live.Recipe.ToString()
                    + " which the recipe catalog no longer resolves (P-009).";
                return false;
            }

            descriptor = resolved.Descriptor;
            detail = string.Empty;
            return true;
        }

        /// <summary>
        /// The derivation view of every live target. A single unresolvable descriptor refuses the whole view: a
        /// partial target set would silently narrow a derivation and hide an ineligible target (P-015).
        /// </summary>
        public DerivationInputTargets BuildDerivationTargets()
        {
            var targets = new List<DerivationTarget>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                LiveTarget live = ordered[i];
                if (!TryDescriptorOf(live.Target, out TargetDescriptor? descriptor, out DiagnosticCode code, out string detail)
                    || descriptor == null)
                {
                    return DerivationInputTargets.Refused(code, detail);
                }

                targets.Add(new DerivationTarget(live.Target, live.Scope, descriptor));
            }

            return DerivationInputTargets.Built(targets);
        }

        /// <summary>The planner view of every live target: identity, recipe and owner scope (P-015).</summary>
        public IReadOnlyList<TargetDefinition> PlannerTargets()
        {
            var definitions = new List<TargetDefinition>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                LiveTarget live = ordered[i];
                definitions.Add(new TargetDefinition(live.Target, live.Recipe, live.Scope));
            }

            return definitions;
        }

        private static int CompareTargets(LiveTarget left, LiveTarget right)
            => left.Target.Value.CompareTo(right.Target.Value);

        public override string ToString() => "liveTargets=" + ordered.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The derived view of a target set, or the reason it could not be built. It exists so a caller cannot mistake
    /// "no targets" for "a target whose descriptor is missing" (P-015).
    /// </summary>
    public sealed class DerivationInputTargets
    {
        private DerivationInputTargets(
            IReadOnlyList<DerivationTarget> targets,
            DiagnosticCode code,
            string detail)
        {
            Targets = targets;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public IReadOnlyList<DerivationTarget> Targets { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        public bool Succeeded => Code == DiagnosticCode.None;

        internal static DerivationInputTargets Built(IReadOnlyList<DerivationTarget> targets)
            => new DerivationInputTargets(targets, DiagnosticCode.None, string.Empty);

        internal static DerivationInputTargets Refused(DiagnosticCode code, string detail)
            => new DerivationInputTargets(Array.Empty<DerivationTarget>(), code, detail);

        public string Describe()
            => Succeeded
                ? "targets=" + Targets.Count.ToString(CultureInfo.InvariantCulture)
                : "targets refused(" + DiagnosticCodeText.Of(Code) + "): " + Detail;
    }
}
