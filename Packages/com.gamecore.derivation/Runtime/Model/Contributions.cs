// GameCore.Derivation — contributions, effective slots, target assemblies and the delta (GC-006).
//
// P-017: each candidate has `ContributionId = (PluginInstanceId, RuleId, TargetId, CapabilityId, OutputSlot)`,
// an immutable payload revision and provenance; identity survives configuration changes, and support from
// multiple contributions is a set of ids rather than a boolean owned by the last plugin. Everything here is
// immutable and canonically ordered, so two equal assemblies compare equal without touching a dictionary.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Why one candidate ended up in a slot. Provenance keeps every one of these states (P-017, P-026).</summary>
    public enum ContributionDisposition
    {
        /// <summary>The contribution supplies the slot's effective value (or one member of it).</summary>
        Active = 0,

        /// <summary>The candidates were composed but this one did not supply the effective value (P-019).</summary>
        Shadowed = 1,
    }

    /// <summary>
    /// One emitted contribution: its stable key, the value it supplies and the provenance of the evaluation that
    /// produced it. A payload reconfiguration changes <see cref="PayloadHash"/> and the value while
    /// <see cref="Key"/> stays the same, which is exactly the P-017 distinction between identity and revision.
    /// </summary>
    public sealed class CapabilityContribution
    {
        public CapabilityContribution(
            ContributionKey key,
            SchemaRef schema,
            CompositionPolicy policy,
            FrozenPayload payload,
            ContentHash payloadHash,
            ContributionDisposition disposition)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            Key = key;
            Schema = schema;
            Policy = policy;
            Payload = payload;
            PayloadHash = payloadHash;
            Disposition = disposition;
        }

        public ContributionKey Key { get; }

        public SchemaRef Schema { get; }

        public CompositionPolicy Policy { get; }

        public FrozenPayload Payload { get; }

        public ContentHash PayloadHash { get; }

        public ContributionDisposition Disposition { get; }

        public ProviderInstallationId Provider => Key.Provider;

        public RuleId Rule => Key.Rule;

        public TargetId Target => Key.Target;

        public CapabilityId Capability => Key.Capability;

        public uint OutputSlot => Key.OutputSlot;

        public override string ToString() =>
            "Contribution(" + Key.ToString() + ", " + Disposition.ToString() + ")";
    }

    /// <summary>One precedence-ranked candidate as the composition policy sees it (P-018).</summary>
    public sealed class RankedCandidate
    {
        public RankedCandidate(
            CapabilityContribution contribution,
            int priority,
            int rulePriority,
            int providerDepth,
            ScopeId providerScope)
        {
            Contribution = contribution;
            Priority = priority;
            RulePriority = rulePriority;
            ProviderDepth = providerDepth;
            ProviderScope = providerScope;
        }

        public CapabilityContribution Contribution { get; }

        /// <summary>The provider installation's signed 32-bit manifest priority (P-018).</summary>
        public int Priority { get; }

        /// <summary>The rule's signed 32-bit manifest priority, used after the provider priority (P-018).</summary>
        public int RulePriority { get; }

        /// <summary>Depth of the provider scope; a nearer provider wins at equal priority (P-018).</summary>
        public int ProviderDepth { get; }

        public ScopeId ProviderScope { get; }

        public ContributionKey Key => Contribution.Key;

        /// <summary>
        /// Total order of P-018: higher priority, then higher rule priority, then nearer provider scope, then
        /// ascending provider installation id, rule id and output slot. No step depends on insertion order.
        /// </summary>
        public static int Compare(RankedCandidate left, RankedCandidate right)
        {
            int priority = right.Priority.CompareTo(left.Priority);
            if (priority != 0)
            {
                return priority;
            }

            int rulePriority = right.RulePriority.CompareTo(left.RulePriority);
            if (rulePriority != 0)
            {
                return rulePriority;
            }

            int depth = right.ProviderDepth.CompareTo(left.ProviderDepth);
            if (depth != 0)
            {
                return depth;
            }

            int provider = left.Key.Provider.Value.CompareTo(right.Key.Provider.Value);
            if (provider != 0)
            {
                return provider;
            }

            int rule = left.Key.Rule.Value.CompareTo(right.Key.Rule.Value);
            if (rule != 0)
            {
                return rule;
            }

            return left.Key.OutputSlot.CompareTo(right.Key.OutputSlot);
        }

        public override string ToString() =>
            "Ranked(" + Key.ToString() + ";priority=" + Priority + ";rulePriority=" + RulePriority
            + ";depth=" + ProviderDepth + ")";
    }

    /// <summary>
    /// One effective output slot of one target after composition: the winning value(s), the support set and the
    /// shadowed candidates (P-017, P-019). <see cref="Values"/> is canonical for its policy: one entry for
    /// <c>Replace</c>, <c>Exclusive</c>, <c>Incompatible</c> and reducer-folded <c>Additive</c>; the ordered or
    /// set members for <c>Ordered</c> and set-union <c>Additive</c>.
    /// </summary>
    public sealed class EffectiveSlot
    {
        public EffectiveSlot(
            TargetId target,
            CapabilityId capability,
            uint version,
            int stratum,
            uint slot,
            SchemaRef schema,
            CompositionPolicy policy,
            IReadOnlyList<FrozenPayload>? values,
            IReadOnlyList<CapabilityContribution>? support,
            IReadOnlyList<CapabilityContribution>? shadowed,
            ContentHash hash)
        {
            Target = target;
            Capability = capability;
            Version = version;
            Stratum = stratum;
            Slot = slot;
            Schema = schema;
            Policy = policy;
            Values = ContractCollections.Freeze(values);
            Support = ContractCollections.Freeze(support);
            Shadowed = ContractCollections.Freeze(shadowed);
            Hash = hash;
        }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public uint Version { get; }

        public int Stratum { get; }

        public uint Slot { get; }

        public SchemaRef Schema { get; }

        public CompositionPolicy Policy { get; }

        public IReadOnlyList<FrozenPayload> Values { get; }

        /// <summary>Contributions that supply this slot; more than one means shared support (P-017).</summary>
        public IReadOnlyList<CapabilityContribution> Support { get; }

        /// <summary>Eligible candidates that did not supply the effective value (P-019).</summary>
        public IReadOnlyList<CapabilityContribution> Shadowed { get; }

        /// <summary>Canonical hash of identity plus composed values, excluding provider identities (P-024).</summary>
        public ContentHash Hash { get; }

        /// <summary>True when no contribution supports this slot any more; the slot is absent from the assembly.</summary>
        public bool IsEmpty => Support.Count == 0;

        public override string ToString() =>
            "Slot(" + Capability.ToString() + "#" + Slot + ", " + Policy.ToString() + ", support=" + Support.Count + ")";
    }

    /// <summary>
    /// One target's complete effective assembly: the reusable base recipe plus its derived capability slots and
    /// the recipe hash P-024 caches derived variants by and P-026 reports (05 s4).
    /// </summary>
    public sealed class TargetAssembly
    {
        public TargetAssembly(
            TargetId target,
            ScopeId scope,
            DefinitionRef baseRecipe,
            IReadOnlyList<EffectiveSlot>? slots,
            IReadOnlyList<CapabilityContribution>? contributions,
            IReadOnlyList<CapabilityId>? effectiveCapabilities,
            ContentHash recipeHash)
        {
            Target = target;
            Scope = scope;
            BaseRecipe = baseRecipe;
            Slots = ContractCollections.Freeze(slots);
            Contributions = ContractCollections.Freeze(contributions);
            EffectiveCapabilities = ContractCollections.Freeze(effectiveCapabilities);
            RecipeHash = recipeHash;
        }

        public TargetId Target { get; }

        public ScopeId Scope { get; }

        public DefinitionRef BaseRecipe { get; }

        /// <summary>Effective capability slots in canonical (capability, version, slot) order.</summary>
        public IReadOnlyList<EffectiveSlot> Slots { get; }

        /// <summary>Every active contribution of this target, including shadowed losers, in canonical order.</summary>
        public IReadOnlyList<CapabilityContribution> Contributions { get; }

        /// <summary>Capabilities the target actually has after composition, ascending and duplicate-free.</summary>
        public IReadOnlyList<CapabilityId> EffectiveCapabilities { get; }

        /// <summary>Content hash of the base recipe plus the effective slot values (P-024 derived-variant key).</summary>
        public ContentHash RecipeHash { get; }

        /// <summary>True when nothing derived: the target keeps exactly its base recipe (P-015 ineligible case).</summary>
        public bool IsBaseOnly => Slots.Count == 0;

        public bool HasCapability(CapabilityId capability)
        {
            for (int i = 0; i < EffectiveCapabilities.Count; i++)
            {
                if (EffectiveCapabilities[i].Equals(capability))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() =>
            "Assembly(" + Target.ToString() + ", slots=" + Slots.Count + ")";
    }

    /// <summary>One effective slot that appeared, disappeared or changed value between two derivations.</summary>
    public sealed class EffectiveSlotChange
    {
        public EffectiveSlotChange(
            TargetId target,
            CapabilityId capability,
            uint slot,
            bool added,
            bool removed,
            bool changed,
            ContentHash oldHash,
            ContentHash newHash,
            IReadOnlyList<ContributionKey>? support,
            IReadOnlyList<ContributionKey>? lostSupport)
        {
            Target = target;
            Capability = capability;
            Slot = slot;
            Added = added;
            Removed = removed;
            Changed = changed;
            OldHash = oldHash;
            NewHash = newHash;
            Support = ContractCollections.Freeze(support);
            LostSupport = ContractCollections.Freeze(lostSupport);
        }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public uint Slot { get; }

        public bool Added { get; }

        public bool Removed { get; }

        public bool Changed { get; }

        public ContentHash OldHash { get; }

        public ContentHash NewHash { get; }

        /// <summary>Support identities of the new state (empty when the slot vanished).</summary>
        public IReadOnlyList<ContributionKey> Support { get; }

        /// <summary>Support identities that disappeared while the slot itself survived (P-017 shared support).</summary>
        public IReadOnlyList<ContributionKey> LostSupport { get; }

        public override string ToString() =>
            "SlotChange(" + Target.ToString() + "/" + Capability.ToString() + "#" + Slot
            + (Added ? " added" : Removed ? " removed" : Changed ? " changed" : " unchanged") + ")";
    }

    /// <summary>
    /// Derivation delta between two results: added/removed/changed contribution keys and effective slot changes,
    /// each carrying the surviving and the lost support identities (05 s4 derivation delta, P-017).
    /// </summary>
    public sealed class DerivationDelta
    {
        public DerivationDelta(
            IReadOnlyList<ContributionKey>? added,
            IReadOnlyList<ContributionKey>? removed,
            IReadOnlyList<ContributionKey>? changed,
            IReadOnlyList<EffectiveSlotChange>? slots,
            IReadOnlyList<TargetId>? affectedTargets,
            IReadOnlyList<TargetId>? affectedRecipes)
        {
            Added = ContractCollections.Freeze(added);
            Removed = ContractCollections.Freeze(removed);
            Changed = ContractCollections.Freeze(changed);
            Slots = ContractCollections.Freeze(slots);
            AffectedTargets = ContractCollections.Freeze(affectedTargets);
            AffectedRecipes = ContractCollections.Freeze(affectedRecipes);
        }

        public IReadOnlyList<ContributionKey> Added { get; }

        public IReadOnlyList<ContributionKey> Removed { get; }

        public IReadOnlyList<ContributionKey> Changed { get; }

        public IReadOnlyList<EffectiveSlotChange> Slots { get; }

        /// <summary>Targets whose published contribution set changed.</summary>
        public IReadOnlyList<TargetId> AffectedTargets { get; }

        /// <summary>Targets whose derived recipe hash changed, so a cached variant must be rebuilt (P-024).</summary>
        public IReadOnlyList<TargetId> AffectedRecipes { get; }

        /// <summary>True when nothing at all changed: no revision increment and no publication (05 s4 `NoChange`).</summary>
        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

        public override string ToString() =>
            "Delta(+=" + Added.Count + ", -=" + Removed.Count + ", ~=" + Changed.Count + ", slots=" + Slots.Count + ")";
    }
}
