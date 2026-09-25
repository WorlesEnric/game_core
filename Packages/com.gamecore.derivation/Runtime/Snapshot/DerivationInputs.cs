// GameCore.Derivation — immutable derivation input records (GC-006).
//
// The engine never reads live composition state: it consumes one immutable, indexed snapshot built from the
// committed composition (P-023: indexes are derived data, rebuildable, not a second authoritative store).
// This file holds the records that snapshot contains plus the canonical orders every comparison uses, so no
// result can depend on registration or dictionary order (P-008).
//
// Normative sources: docs/game-core/00-core-protocols.md (P-010, P-013, P-015, P-016, P-017, P-018),
// 05-contracts-and-data-model.md s3 and 02-composition-and-propagation.md s3-s5.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>
    /// Canonical ordering for derivation records. Every comparator ends in a stable identity comparison, so a
    /// sorted list is a property of the data and never of insertion or enumeration order (P-008).
    /// </summary>
    public static class CanonicalDerivationOrder
    {
        /// <summary>Sorted, duplicate-free copy; null or empty yields an empty list.</summary>
        public static IReadOnlyList<T> SortedUnique<T>(
            IReadOnlyList<T>? source,
            Comparison<T> comparison,
            Func<T, T, bool> equal)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            List<T> copy = new List<T>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                copy.Add(source[i]);
            }

            copy.Sort(comparison);

            int write = 0;
            for (int read = 0; read < copy.Count; read++)
            {
                if (write == 0 || !equal(copy[write - 1], copy[read]))
                {
                    copy[write] = copy[read];
                    write++;
                }
            }

            if (write < copy.Count)
            {
                copy.RemoveRange(write, copy.Count - write);
            }

            return copy.AsReadOnly();
        }

        public static IReadOnlyList<T> Sorted<T>(IReadOnlyList<T>? source, Comparison<T> comparison)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            List<T> copy = new List<T>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                copy.Add(source[i]);
            }

            copy.Sort(comparison);
            return copy.AsReadOnly();
        }

        public static int CompareImports(CapabilityImport left, CapabilityImport right)
        {
            int capability = left.CapabilityId.Value.CompareTo(right.CapabilityId.Value);
            return capability != 0
                ? capability
                : left.ProviderInstallationId.Value.CompareTo(right.ProviderInstallationId.Value);
        }

        public static bool ImportsEqual(CapabilityImport left, CapabilityImport right) =>
            left.CapabilityId.Equals(right.CapabilityId) && left.ProviderInstallationId.Equals(right.ProviderInstallationId);

        /// <summary>Exclusion order: kind, selector scope, selector target, subtree flag, excluded identity.</summary>
        public static int CompareExclusions(ExclusionRule left, ExclusionRule right)
        {
            int kind = ((int)left.Kind).CompareTo((int)right.Kind);
            if (kind != 0)
            {
                return kind;
            }

            int scope = left.AtScope.Value.CompareTo(right.AtScope.Value);
            if (scope != 0)
            {
                return scope;
            }

            int target = left.AtTarget.Value.CompareTo(right.AtTarget.Value);
            if (target != 0)
            {
                return target;
            }

            int subtree = left.AppliesToSubtree.CompareTo(right.AppliesToSubtree);
            return subtree != 0 ? subtree : left.TargetId.CompareTo(right.TargetId);
        }

        public static bool ExclusionsEqual(ExclusionRule left, ExclusionRule right) =>
            CompareExclusions(left, right) == 0;

        public static int CompareOrderKeyEdges(OrderKeyEdge left, OrderKeyEdge right)
        {
            int key = left.Key.CompareTo(right.Key);
            return key != 0 ? key : left.Required.CompareTo(right.Required);
        }

        public static bool OrderKeyEdgesEqual(OrderKeyEdge left, OrderKeyEdge right) =>
            left.Key.Equals(right.Key) && left.Required == right.Required;

        public static int CompareOverrides(ProviderSelectionOverride left, ProviderSelectionOverride right)
        {
            int scope = left.AtScope.Value.CompareTo(right.AtScope.Value);
            if (scope != 0)
            {
                return scope;
            }

            int target = left.AtTarget.Value.CompareTo(right.AtTarget.Value);
            if (target != 0)
            {
                return target;
            }

            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            if (capability != 0)
            {
                return capability;
            }

            int provider = left.Provider.Value.CompareTo(right.Provider.Value);
            return provider != 0 ? provider : left.AppliesToSubtree.CompareTo(right.AppliesToSubtree);
        }

        public static bool OverridesEqual(ProviderSelectionOverride left, ProviderSelectionOverride right) =>
            CompareOverrides(left, right) == 0;

        public static int CompareRuleKeys(DerivationRuleKeys left, DerivationRuleKeys right) =>
            left.Rule.Value.CompareTo(right.Rule.Value);

        public static int CompareInstalls(DerivationInstall left, DerivationInstall right) =>
            left.Instance.Value.CompareTo(right.Instance.Value);

        public static int CompareTargets(DerivationTarget left, DerivationTarget right) =>
            left.Target.Value.CompareTo(right.Target.Value);

        public static int CompareScopes(DerivationScope left, DerivationScope right) =>
            left.Scope.Value.CompareTo(right.Scope.Value);

        public static int CompareScopeIds(ScopeId left, ScopeId right) => left.Value.CompareTo(right.Value);

        public static int CompareCapabilityRefs(CapabilityRef left, CapabilityRef right)
        {
            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            return capability != 0 ? capability : left.Version.CompareTo(right.Version);
        }

        public static int CompareSchemaRefs(SchemaRef left, SchemaRef right)
        {
            int schema = left.Id.Value.CompareTo(right.Id.Value);
            return schema != 0 ? schema : left.Version.CompareTo(right.Version);
        }
    }

    /// <summary>
    /// One composition scope as derivation sees it (P-010). Service isolation is deliberately absent: services
    /// never pass the propagation gate (P-013), so only the capability boundary of P-016 and the Conservative
    /// import data of P-013 live here. Depth is derived by the snapshot from the parent chain.
    /// </summary>
    public sealed class DerivationScope
    {
        private static readonly IsolationSet NoIsolation = new IsolationSet(false, null);

        public DerivationScope(
            ScopeId scope,
            ScopeId parent,
            IsolationSet? capabilityIsolation,
            IReadOnlyList<ExclusionRule>? exclusions,
            IReadOnlyList<CapabilityImport>? imports)
        {
            if (scope.IsDefault)
            {
                throw new ArgumentException("A derivation scope requires a real stable scope identity (P-004).", nameof(scope));
            }

            Scope = scope;
            Parent = parent;
            CapabilityIsolation = capabilityIsolation ?? NoIsolation;
            Exclusions = CanonicalDerivationOrder.SortedUnique(
                exclusions,
                CanonicalDerivationOrder.CompareExclusions,
                CanonicalDerivationOrder.ExclusionsEqual);
            Imports = CanonicalDerivationOrder.SortedUnique(
                imports,
                CanonicalDerivationOrder.CompareImports,
                CanonicalDerivationOrder.ImportsEqual);
        }

        public ScopeId Scope { get; }

        /// <summary>Default means the world root: the only scope without a parent (P-010).</summary>
        public ScopeId Parent { get; }

        public bool IsRoot => Parent.IsDefault;

        /// <summary>Named capability-isolation set of this scope; `*` means all contracts (P-016).</summary>
        public IsolationSet CapabilityIsolation { get; }

        public IReadOnlyList<ExclusionRule> Exclusions { get; }

        /// <summary>Explicit capability imports declared at this scope (P-013); sorted and duplicate-free.</summary>
        public IReadOnlyList<CapabilityImport> Imports { get; }

        /// <summary>True when this scope's capability boundary blocks the named capability (P-016).</summary>
        public bool BlocksCapability(CapabilityId capability)
        {
            if (CapabilityIsolation.AllContracts)
            {
                return true;
            }

            IReadOnlyList<Id128> contracts = CapabilityIsolation.Contracts;
            for (int i = 0; i < contracts.Count; i++)
            {
                if (contracts[i].Equals(capability.Value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when this scope explicitly imports the capability from that provider installation (P-013).</summary>
        public bool ImportsFrom(CapabilityId capability, ProviderInstallationId provider)
        {
            IReadOnlyList<CapabilityImport> imports = Imports;
            for (int i = 0; i < imports.Count; i++)
            {
                if (imports[i].CapabilityId.Equals(capability) && imports[i].ProviderInstallationId.Equals(provider))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() => "Scope(" + Scope.ToString() + ")";
    }

    /// <summary>
    /// One installation plus the manifest that declares its rules (P-009, P-046). Only an <c>Active</c>
    /// installation contributes: a waiting, suspended or retiring installation has retracted its
    /// contributions (P-012).
    /// </summary>
    public sealed class DerivationInstall
    {
        public DerivationInstall(InstallRecord record, InstallationState state, PluginManifest manifest)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (record.Scope.IsDefault)
            {
                throw new ArgumentException("An install record must name its owning scope (P-010).", nameof(record));
            }

            Record = record;
            State = state;
            Manifest = manifest;
        }

        public InstallRecord Record { get; }

        public InstallationState State { get; }

        public PluginManifest Manifest { get; }

        public PluginInstanceId Instance => Record.Instance;

        public ScopeId Scope => Record.Scope;

        public bool IsActive => State == InstallationState.Active;

        public override string ToString() => "Install(" + Instance.ToString() + ")";
    }

    /// <summary>One live target: stable identity, its single owner scope and its immutable descriptor (P-010, P-015).</summary>
    public sealed class DerivationTarget
    {
        public DerivationTarget(TargetId target, ScopeId scope, TargetDescriptor descriptor)
        {
            if (target.IsDefault)
            {
                throw new ArgumentException("A derivation target requires a real stable target identity (P-004).", nameof(target));
            }

            if (scope.IsDefault)
            {
                throw new ArgumentException("A live target has exactly one owner scope (P-010).", nameof(scope));
            }

            Target = target;
            Scope = scope;
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public TargetId Target { get; }

        public ScopeId Scope { get; }

        public TargetDescriptor Descriptor { get; }

        /// <summary>Exact declared schema identity and version (P-015: versions are compared, never inferred).</summary>
        public bool DeclaresSchema(SchemaRef schema)
        {
            IReadOnlyList<SchemaRef> declared = Descriptor.SupportedSchemas;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].Id.Equals(schema.Id) && declared[i].Version == schema.Version)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when any version of that schema is declared; used to report a version mismatch.</summary>
        public bool DeclaresSchemaId(SchemaId schema)
        {
            IReadOnlyList<SchemaRef> declared = Descriptor.SupportedSchemas;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].Id.Equals(schema))
                {
                    return true;
                }
            }

            return false;
        }

        public bool DeclaresCapability(CapabilityRef capability)
        {
            IReadOnlyList<CapabilityRef> declared = Descriptor.SupportedCapabilities;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].Capability.Equals(capability.Capability) && declared[i].Version == capability.Version)
                {
                    return true;
                }
            }

            return false;
        }

        public bool DeclaresCapabilityId(CapabilityId capability)
        {
            IReadOnlyList<CapabilityRef> declared = Descriptor.SupportedCapabilities;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].Capability.Equals(capability))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Declared version of that capability, or 0 when the descriptor does not advertise it at all.</summary>
        public uint DeclaredCapabilityVersion(CapabilityId capability)
        {
            IReadOnlyList<CapabilityRef> declared = Descriptor.SupportedCapabilities;
            uint found = 0U;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].Capability.Equals(capability))
                {
                    found = declared[i].Version;
                }
            }

            return found;
        }

        public bool DeclaresTag(Id128 tag)
        {
            IReadOnlyList<Id128> tags = Descriptor.Tags;
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i].Equals(tag))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() => "Target(" + Target.ToString() + ")";
    }

    /// <summary>One declared ordering edge of an <c>Ordered</c> candidate (P-019): a key plus its necessity.</summary>
    public readonly struct OrderKeyEdge : IEquatable<OrderKeyEdge>
    {
        public readonly Id128 Key;

        /// <summary>True when the endpoint must exist; a missing optional endpoint drops the edge (P-019).</summary>
        public readonly bool Required;

        public OrderKeyEdge(Id128 key, bool required)
        {
            Key = key;
            Required = required;
        }

        public bool Equals(OrderKeyEdge other) => Key.Equals(other.Key) && Required == other.Required;

        public override bool Equals(object? obj) => obj is OrderKeyEdge other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return (Key.GetHashCode() * 397) ^ (Required ? 1 : 0);
            }
        }

        public override string ToString() => (Required ? "required:" : "optional:") + Key.ToString();
    }

    /// <summary>
    /// The declared ordering keys of one rule's output (P-019 <c>Ordered</c>). The frozen manifest shape carries
    /// one opaque payload definition per rule and no ordering fields, so the keys travel with the derivation
    /// input instead; the contract's package declares them and the plan records them as versioned composition
    /// data. <see cref="SelfKey"/> default means the candidate can never be an endpoint of another edge.
    /// </summary>
    public sealed class DerivationRuleKeys
    {
        public DerivationRuleKeys(
            RuleId rule,
            CapabilityId capability,
            Id128 selfKey,
            IReadOnlyList<OrderKeyEdge>? before,
            IReadOnlyList<OrderKeyEdge>? after)
        {
            if (rule.IsDefault)
            {
                throw new ArgumentException("Ordering keys require a real rule identity (P-004).", nameof(rule));
            }

            if (capability.IsDefault)
            {
                throw new ArgumentException("Ordering keys require the capability they order (P-017).", nameof(capability));
            }

            Rule = rule;
            Capability = capability;
            SelfKey = selfKey;
            Before = CanonicalDerivationOrder.SortedUnique(
                before,
                CanonicalDerivationOrder.CompareOrderKeyEdges,
                CanonicalDerivationOrder.OrderKeyEdgesEqual);
            After = CanonicalDerivationOrder.SortedUnique(
                after,
                CanonicalDerivationOrder.CompareOrderKeyEdges,
                CanonicalDerivationOrder.OrderKeyEdgesEqual);
        }

        public RuleId Rule { get; }

        public CapabilityId Capability { get; }

        /// <summary>Key other candidates reference to order against this candidate; default means unreferencable.</summary>
        public Id128 SelfKey { get; }

        public bool HasSelfKey => !SelfKey.IsDefault;

        /// <summary>Keys this candidate must precede.</summary>
        public IReadOnlyList<OrderKeyEdge> Before { get; }

        /// <summary>Keys this candidate must follow.</summary>
        public IReadOnlyList<OrderKeyEdge> After { get; }

        public bool HasEdges => Before.Count > 0 || After.Count > 0;

        public override string ToString() => "RuleKeys(" + Rule.ToString() + ")";
    }

    /// <summary>
    /// One versioned composition override naming the provider that wins a <c>Replace</c> or <c>Exclusive</c>
    /// slot (P-018). A default selector means "unset"; a default scope and target together are rejected by the
    /// snapshot because an override must be addressable.
    /// </summary>
    public readonly struct ProviderSelectionOverride : IEquatable<ProviderSelectionOverride>
    {
        public readonly ScopeId AtScope;

        public readonly TargetId AtTarget;

        public readonly CapabilityId Capability;

        public readonly ProviderInstallationId Provider;

        public readonly bool AppliesToSubtree;

        public ProviderSelectionOverride(
            ScopeId atScope,
            TargetId atTarget,
            CapabilityId capability,
            ProviderInstallationId provider,
            bool appliesToSubtree)
        {
            AtScope = atScope;
            AtTarget = atTarget;
            Capability = capability;
            Provider = provider;
            AppliesToSubtree = appliesToSubtree;
        }

        public bool HasScope => !AtScope.IsDefault;

        public bool HasTarget => !AtTarget.IsDefault;

        public bool Equals(ProviderSelectionOverride other) =>
            CanonicalDerivationOrder.CompareOverrides(this, other) == 0;

        public override bool Equals(object? obj) => obj is ProviderSelectionOverride other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + AtScope.GetHashCode();
                hash = (hash * 31) + AtTarget.GetHashCode();
                hash = (hash * 31) + Capability.GetHashCode();
                hash = (hash * 31) + Provider.GetHashCode();
                hash = (hash * 31) + (AppliesToSubtree ? 1 : 0);
                return hash;
            }
        }

        public override string ToString() =>
            Capability.ToString() + " -> " + Provider.ToString() + (HasTarget ? "@target" : "@scope");
    }

    /// <summary>Scope-level Conservative imports (P-013): the composition package's grant data, as derivation input.</summary>
    public sealed class DerivationScopeGrants
    {
        public DerivationScopeGrants(ScopeId scope, IReadOnlyList<CapabilityImport>? imports)
        {
            if (scope.IsDefault)
            {
                throw new ArgumentException("Scope grants require a real scope identity (P-004).", nameof(scope));
            }

            Scope = scope;
            Imports = CanonicalDerivationOrder.SortedUnique(
                imports,
                CanonicalDerivationOrder.CompareImports,
                CanonicalDerivationOrder.ImportsEqual);
        }

        public ScopeId Scope { get; }

        public IReadOnlyList<CapabilityImport> Imports { get; }

        public override string ToString() => "Grants(" + Scope.ToString() + ")";
    }
}
