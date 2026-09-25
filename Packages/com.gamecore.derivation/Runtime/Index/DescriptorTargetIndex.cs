// GameCore.Derivation — the descriptor index (GC-013, P-015/P-023).
//
// P-023 indexes "descriptor-to-target matches" so a rule's candidate population comes from a bucket lookup rather
// than a target scan. This index keeps the four descriptor dimensions a rule can select on — accepted schema,
// advertised capability, declared tag and base recipe — in both directions, and it owns the structural comparison
// of two descriptors so the invalidation closure can tell "this target's descriptor changed" from "this target is
// untouched" without re-deriving anything.
//
// Forward buckets answer "which targets advertise X"; the reverse map answers "which facts does this target
// advertise", which is what a descriptor or membership change needs in order to mark exactly the buckets it left
// and entered (02 s7 "old and new descriptor index buckets").
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Targets by descriptor fact, and the structural comparison of two target descriptors (P-015).</summary>
    public sealed class DescriptorTargetIndex
    {
        private readonly DerivationSnapshot snapshot;
        private readonly Dictionary<Id128, List<DerivationTarget>> bySchema;
        private readonly Dictionary<Id128, List<DerivationTarget>> byCapability;
        private readonly Dictionary<Id128, List<DerivationTarget>> byTag;
        private readonly Dictionary<Id128, List<DerivationTarget>> byRecipe;
        private readonly Dictionary<Id128, TargetFacts> factsByTarget;

        private DescriptorTargetIndex(DerivationSnapshot snapshot)
        {
            this.snapshot = snapshot;
            bySchema = new Dictionary<Id128, List<DerivationTarget>>();
            byCapability = new Dictionary<Id128, List<DerivationTarget>>();
            byTag = new Dictionary<Id128, List<DerivationTarget>>();
            byRecipe = new Dictionary<Id128, List<DerivationTarget>>();
            factsByTarget = new Dictionary<Id128, TargetFacts>();

            IReadOnlyList<DerivationTarget> targets = snapshot.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                DerivationTarget target = targets[i];
                TargetDescriptor descriptor = target.Descriptor;

                IReadOnlyList<SchemaRef> schemas = descriptor.SupportedSchemas;
                for (int s = 0; s < schemas.Count; s++)
                {
                    Add(bySchema, schemas[s].Id.Value, target);
                }

                IReadOnlyList<CapabilityRef> capabilities = descriptor.SupportedCapabilities;
                for (int c = 0; c < capabilities.Count; c++)
                {
                    Add(byCapability, capabilities[c].Capability.Value, target);
                }

                IReadOnlyList<Id128> tags = descriptor.Tags;
                for (int t = 0; t < tags.Count; t++)
                {
                    Add(byTag, tags[t], target);
                }

                Add(byRecipe, descriptor.Recipe.Id.Value, target);
                factsByTarget[target.Target.Value] = new TargetFacts(target.Scope, descriptor);
            }
        }

        /// <summary>Builds the descriptor index of one snapshot.</summary>
        public static DescriptorTargetIndex Build(DerivationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new DescriptorTargetIndex(snapshot);
        }

        /// <summary>Targets advertising a schema identity at any version, canonical order (P-015).</summary>
        public IReadOnlyList<DerivationTarget> TargetsWithSchema(SchemaId schema) => Lookup(bySchema, schema.Value);

        /// <summary>Targets advertising a capability identity at any version, canonical order (P-015).</summary>
        public IReadOnlyList<DerivationTarget> TargetsWithCapability(CapabilityId capability) =>
            Lookup(byCapability, capability.Value);

        /// <summary>Targets declaring one immutable tag, canonical order (P-015).</summary>
        public IReadOnlyList<DerivationTarget> TargetsWithTag(Id128 tag) => Lookup(byTag, tag);

        /// <summary>Targets built from one base recipe definition, canonical order (P-024).</summary>
        public IReadOnlyList<DerivationTarget> TargetsWithRecipe(DefinitionId recipe) => Lookup(byRecipe, recipe.Value);

        /// <summary>
        /// The candidate population of one rule, exactly as the snapshot defines it: its reach domain intersected
        /// with the targets that advertise an accepted selector schema or the rule's output capability (P-023).
        /// </summary>
        public IReadOnlyList<DerivationTarget> CandidatesOf(
            ScopeId providerScope,
            PropagationReach reach,
            IReadOnlyList<SchemaRef> selectors,
            CapabilityId capability,
            InvalidationCounters? counters = null)
        {
            IReadOnlyList<DerivationTarget> candidates = snapshot.TargetsInReach(
                providerScope, reach, selectors, capability);
            if (counters != null)
            {
                counters.RulesVisited++;
                counters.TargetsVisited += candidates.Count;
            }

            return candidates;
        }

        /// <summary>
        /// The indexed reach domain of one provider scope: the scopes a rule with this reach can select. LocalOnly
        /// is the scope itself, DescendantsOnly is its strict subtree, and SelfAndDescendants adds the scope.
        /// </summary>
        public IReadOnlyList<ScopeId> ReachScopes(ScopeMembershipIndex membership, ScopeId providerScope, PropagationReach reach)
        {
            if (membership == null)
            {
                throw new ArgumentNullException(nameof(membership));
            }

            if (!membership.Contains(providerScope))
            {
                return Array.Empty<ScopeId>();
            }

            switch (reach)
            {
                case PropagationReach.LocalOnly:
                    return new List<ScopeId>(1) { providerScope }.AsReadOnly();
                case PropagationReach.DescendantsOnly:
                    {
                        IReadOnlyList<ScopeId> subtree = membership.Subtree(providerScope);
                        List<ScopeId> descendants = new List<ScopeId>(subtree.Count);
                        for (int i = 0; i < subtree.Count; i++)
                        {
                            if (!subtree[i].Equals(providerScope))
                            {
                                descendants.Add(subtree[i]);
                            }
                        }

                        return descendants.AsReadOnly();
                    }
                default:
                    return membership.Subtree(providerScope);
            }
        }

        /// <summary>
        /// True when two descriptors are indistinguishable to every eligibility and composition check: the base
        /// recipe, the advertised schemas, capabilities and tags, the asset adapter, the local patches, the
        /// imports and opt-ins, and the exclusion set. Element order is preserved, so a reordered but otherwise
        /// equal descriptor compares unequal — the conservative direction (P-015, P-008).
        /// </summary>
        public static bool DescriptorsEqual(TargetDescriptor left, TargetDescriptor right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.Recipe.Equals(right.Recipe)
                && SchemasEqual(left.SupportedSchemas, right.SupportedSchemas)
                && CapabilitiesEqual(left.SupportedCapabilities, right.SupportedCapabilities)
                && IdsEqual(left.Tags, right.Tags)
                && left.AssetAdapter.Equals(right.AssetAdapter)
                && DefinitionsEqual(left.LocalPatches, right.LocalPatches)
                && ImportsEqual(left.Imports, right.Imports)
                && left.AssetAdapter.AdapterId.Equals(right.AssetAdapter.AdapterId)
                && left.AssetAdapter.Version == right.AssetAdapter.Version
                && ExclusionsEqual(left.Exclusions, right.Exclusions);
        }

        /// <summary>Element-wise schema reference equality, versions included (P-015).</summary>
        public static bool SchemasEqual(IReadOnlyList<SchemaRef> left, IReadOnlyList<SchemaRef> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Element-wise capability reference equality, versions included (P-015).</summary>
        public static bool CapabilitiesEqual(IReadOnlyList<CapabilityRef> left, IReadOnlyList<CapabilityRef> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IdsEqual(IReadOnlyList<Id128> left, IReadOnlyList<Id128> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool DefinitionsEqual(IReadOnlyList<DefinitionRef> left, IReadOnlyList<DefinitionRef> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ImportsEqual(IReadOnlyList<CapabilityImport> left, IReadOnlyList<CapabilityImport> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].CapabilityId.Equals(right[i].CapabilityId)
                    || !left[i].ProviderInstallationId.Equals(right[i].ProviderInstallationId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool OptInsEqual(IReadOnlyList<TargetOptIn> left, IReadOnlyList<TargetOptIn> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].CapabilityId.Equals(right[i].CapabilityId)
                    || !left[i].ProviderInstallationId.Equals(right[i].ProviderInstallationId))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ExclusionsEqual(IReadOnlyList<ExclusionRule> left, IReadOnlyList<ExclusionRule> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!CanonicalDerivationOrder.ExclusionsEqual(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Owner scope and descriptor of one target as the previous snapshot recorded them.</summary>
        public bool TryGetFacts(TargetId target, out ScopeId scope, out TargetDescriptor? descriptor)
        {
            if (factsByTarget.TryGetValue(target.Value, out TargetFacts facts))
            {
                scope = facts.Scope;
                descriptor = facts.Descriptor;
                return true;
            }

            scope = default(ScopeId);
            descriptor = null;
            return false;
        }

        private static IReadOnlyList<DerivationTarget> Lookup(
            Dictionary<Id128, List<DerivationTarget>> index,
            Id128 key) =>
            index.TryGetValue(key, out List<DerivationTarget>? list)
                ? list
                : (IReadOnlyList<DerivationTarget>)Array.Empty<DerivationTarget>();

        private static void Add(Dictionary<Id128, List<DerivationTarget>> index, Id128 key, DerivationTarget target)
        {
            if (!index.TryGetValue(key, out List<DerivationTarget>? list))
            {
                list = new List<DerivationTarget>();
                index.Add(key, list);
            }

            list.Add(target);
        }

        private readonly struct TargetFacts
        {
            public readonly ScopeId Scope;
            public readonly TargetDescriptor Descriptor;

            public TargetFacts(ScopeId scope, TargetDescriptor descriptor)
            {
                Scope = scope;
                Descriptor = descriptor;
            }
        }
    }
}
