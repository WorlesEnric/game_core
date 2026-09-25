// GameCore.Planning — the effective binding table of one world revision (GC-008).
//
// Normative sources: 00 P-017 (one candidate identity per target/slot; all candidates for a target/slot are
// composed before assembly), P-018/P-019 (canonical order and per-slot policies; a losing candidate stays
// provenance, not active support), P-024 (a target first becomes query-visible with its complete effective
// assembly), P-033 (removing one provider removes exactly its support) and P-008 (canonical big-endian stable-id
// order, never registration timing or dictionary enumeration).
//
// The table is the *derived* side of an assembly: persistent gameplay state lives in ECS components, while this
// table is what the publication exposes as the target's effective bindings. It is immutable and canonically
// ordered, so two plans that derive the same assembly produce the same rows in the same order — which is what the
// publication fingerprint and the tests compare.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// One effective binding of one target slot. Its identity is `(Target, Capability, OutputSlot)` (P-017); value,
    /// provider and priority may change without the identity changing.
    ///
    /// `Value` is the *composed* value of the slot's policy (P-019): for `Additive` it is what the declared reducer
    /// folded over every supporter, not one candidate's raw value. `Supports` names those supporters (P-017), while
    /// `Provider`/`ProviderGeneration`/`Priority` remain the highest-ranked supporter — so a reader that only wants
    /// "who owns this row" still has one answer and the composed value never masquerades as a single contribution.
    /// </summary>
    public readonly struct TargetBindingRow
    {
        public readonly TargetId Target;
        public readonly CapabilityId Capability;
        public readonly uint CapabilityVersion;
        public readonly uint OutputSlot;
        public readonly int Value;
        public readonly ProviderInstallationId Provider;
        public readonly ulong ProviderGeneration;
        public readonly int Priority;
        public readonly SchemaRef Schema;

        /// <summary>
        /// Every contribution that supports this row, canonically ordered (P-017). One entry for `Replace`,
        /// `Exclusive` and `Incompatible`; one or more for `Additive`, `Ordered` and set-union slots. A row whose
        /// caller supplies no set still carries its own single supporter, so one-element support is the same code
        /// path as any other set rather than a special case.
        /// </summary>
        public readonly IReadOnlyList<CapabilitySupport> Supports;

        public TargetBindingRow(
            TargetId target,
            CapabilityId capability,
            uint capabilityVersion,
            uint outputSlot,
            int value,
            ProviderInstallationId provider,
            ulong providerGeneration,
            int priority,
            SchemaRef schema,
            IReadOnlyList<CapabilitySupport>? supports = null,
            RuleId rule = default(RuleId))
        {
            Target = target;
            Capability = capability;
            CapabilityVersion = capabilityVersion;
            OutputSlot = outputSlot;
            Value = value;
            Provider = provider;
            ProviderGeneration = providerGeneration;
            Priority = priority;
            Schema = schema;
            Supports = supports != null
                ? CapabilitySupport.Freeze(supports)
                : SingleSupport(provider, providerGeneration, rule, value, priority);
        }

        /// <summary>Canonical support set of a row that has exactly one supporter (P-017).</summary>
        public static IReadOnlyList<CapabilitySupport> SingleSupport(
            ProviderInstallationId provider,
            ulong providerGeneration,
            RuleId rule,
            int value,
            int priority)
            => CapabilitySupport.Freeze(new[]
            {
                new CapabilitySupport(provider, providerGeneration, rule, value, priority),
            });

        /// <summary>Number of contributions that support this row; one is the ordinary single-provider case.</summary>
        public int SupporterCount => Supports.Count;

        /// <summary>True when more than one contribution supports this row (P-017).</summary>
        public bool IsMultiSupport => Supports.Count > 1;

        /// <summary>Contribution identity of this row (P-017); two rows with the same identity are one slot.</summary>
        public bool HasSameIdentity(TargetBindingRow other) =>
            Target.Equals(other.Target)
            && Capability.Equals(other.Capability)
            && OutputSlot == other.OutputSlot;

        /// <summary>
        /// True when the two rows publish the same effective binding. The support set participates: a slot whose
        /// composed value is unchanged but whose supporters changed is a content change, because removing one of
        /// two supporters must be observable (P-017, P-033).
        /// </summary>
        public bool HasSameContent(TargetBindingRow other) =>
            HasSameIdentity(other)
            && CapabilityVersion == other.CapabilityVersion
            && Value == other.Value
            && Provider.Equals(other.Provider)
            && ProviderGeneration == other.ProviderGeneration
            && Priority == other.Priority
            && Schema.Equals(other.Schema)
            && CapabilitySupport.SetEquals(Supports, other.Supports);

        public override string ToString() =>
            Target.ToString() + "/" + Capability.ToString()
            + "#" + OutputSlot.ToString(CultureInfo.InvariantCulture)
            + "=" + Value.ToString(CultureInfo.InvariantCulture)
            + "@" + Provider.ToString()
            + "[support=" + SupporterCount.ToString(CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>
    /// One active derivation rule of the published revision: which recipe in which scope receives which capability
    /// from which provider(s). A future spawn derives its assembly from these rules, so it appears fully assembled
    /// at its first visibility without per-instance imports (P-013, P-024).
    ///
    /// The rule carries the same composed value and support set as the `TargetBindingRow` it produced, so a target
    /// spawned after the publication inherits the *composed* binding — including the number of supporters an
    /// `Additive` slot was folded from (P-017, P-019) — rather than only the winning candidate's raw value.
    /// </summary>
    public readonly struct DerivedBindingRule
    {
        public readonly DefinitionRef Recipe;
        public readonly ScopeId Scope;
        public readonly CapabilityId Capability;
        public readonly uint CapabilityVersion;
        public readonly uint OutputSlot;
        public readonly int Value;
        public readonly ProviderInstallationId Provider;
        public readonly ulong ProviderGeneration;
        public readonly int Priority;
        public readonly CompositionPolicy Policy;
        public readonly SchemaRef Schema;

        /// <summary>Contributions this rule's effective value was composed from, canonically ordered (P-017).</summary>
        public readonly IReadOnlyList<CapabilitySupport> Supports;

        public DerivedBindingRule(
            DefinitionRef recipe,
            ScopeId scope,
            CapabilityId capability,
            uint capabilityVersion,
            uint outputSlot,
            int value,
            ProviderInstallationId provider,
            ulong providerGeneration,
            int priority,
            CompositionPolicy policy,
            SchemaRef schema,
            IReadOnlyList<CapabilitySupport>? supports = null,
            RuleId rule = default(RuleId))
        {
            Recipe = recipe;
            Scope = scope;
            Capability = capability;
            CapabilityVersion = capabilityVersion;
            OutputSlot = outputSlot;
            Value = value;
            Provider = provider;
            ProviderGeneration = providerGeneration;
            Priority = priority;
            Policy = policy;
            Schema = schema;
            Supports = supports != null
                ? CapabilitySupport.Freeze(supports)
                : CapabilitySupport.Freeze(new[]
                {
                    new CapabilitySupport(provider, providerGeneration, rule, value, priority),
                });
        }

        /// <summary>Number of contributions this rule's value was composed from (P-017).</summary>
        public int SupporterCount => Supports.Count;

        /// <summary>True when this rule composes more than one contribution (P-017, P-019).</summary>
        public bool IsMultiSupport => Supports.Count > 1;

        /// <summary>True when this rule derives a binding for a target of the given recipe in the given scope.</summary>
        public bool AppliesTo(DefinitionRef recipe, ScopeId scope) =>
            Recipe.Equals(recipe) && Scope.Equals(scope);

        /// <summary>
        /// Canonical identity of one rule slot inside one recipe/scope: `(capability, output slot)`. A new winner
        /// replaces the rule of the same identity rather than adding a second, so the rule set after a publication
        /// is the effective one a future spawn derives from (P-017, P-024).
        /// </summary>
        public string RuleIdentity() =>
            Id128Codec.ToHex(Recipe.Id.Value) + "|" + Id128Codec.ToHex(Scope.Value) + "|"
            + Id128Codec.ToHex(Capability.Value) + "|"
            + OutputSlot.ToString(CultureInfo.InvariantCulture);

        public override string ToString() =>
            Recipe.ToString() + "->" + Capability.ToString() + "=" + Value.ToString(CultureInfo.InvariantCulture)
            + "[support=" + SupporterCount.ToString(CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>
    /// Immutable, canonically ordered effective binding table of one assembly. The constructor rejects two rows
    /// with the same contribution identity, because composing them is a policy decision (P-019) that the planner
    /// must already have made; silently keeping the last one would be last-writer-wins.
    /// </summary>
    public sealed class TargetBindingTable
    {
        private readonly TargetBindingRow[] rows;
        private readonly TargetId[] targets;
        private readonly Dictionary<Id128, List<TargetBindingRow>> byTarget;
        private readonly Dictionary<Id128, TargetId> knownTargets;

        public static readonly TargetBindingTable Empty = new TargetBindingTable(null, null);

        public TargetBindingTable(IReadOnlyList<TargetId>? targets, IReadOnlyList<TargetBindingRow>? rows)
        {
            var sorted = new List<TargetBindingRow>(rows != null ? rows.Count : 0);
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    sorted.Add(rows[i]);
                }
            }

            sorted.Sort(CompareRows);
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i - 1].HasSameIdentity(sorted[i]))
                {
                    throw new ArgumentException(
                        "Two rows share the contribution identity " + sorted[i].ToString()
                        + "; composing them is a policy decision the planner must make first (P-017).",
                        nameof(rows));
                }
            }

            this.rows = sorted.ToArray();

            var targetSet = new Dictionary<Id128, TargetId>();
            var targetList = new List<TargetId>();
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targetSet.ContainsKey(targets[i].Value) || targets[i].IsDefault)
                    {
                        continue;
                    }

                    targetSet.Add(targets[i].Value, targets[i]);
                    targetList.Add(targets[i]);
                }
            }

            byTarget = new Dictionary<Id128, List<TargetBindingRow>>();
            for (int i = 0; i < this.rows.Length; i++)
            {
                TargetBindingRow row = this.rows[i];
                if (!targetSet.ContainsKey(row.Target.Value))
                {
                    // A row for a target the caller did not list still means that target exists in the assembly.
                    targetSet.Add(row.Target.Value, row.Target);
                    targetList.Add(row.Target);
                }

                if (!byTarget.TryGetValue(row.Target.Value, out List<TargetBindingRow>? list) || list == null)
                {
                    list = new List<TargetBindingRow>();
                    byTarget.Add(row.Target.Value, list);
                }

                list.Add(row);
            }

            targetList.Sort(CompareTargets);
            this.targets = targetList.ToArray();
            knownTargets = targetSet;
        }

        public IReadOnlyList<TargetBindingRow> Rows => rows;

        /// <summary>Distinct targets of this assembly in canonical order, including targets with no binding yet.</summary>
        public IReadOnlyList<TargetId> Targets => targets;

        public int Count => rows.Length;

        public int TargetCount => targets.Length;

        public bool HasTarget(TargetId target) => knownTargets.ContainsKey(target.Value);

        /// <summary>Effective bindings of one target in canonical order; an unknown target yields an empty list.</summary>
        public IReadOnlyList<TargetBindingRow> BindingsOf(TargetId target)
        {
            if (byTarget.TryGetValue(target.Value, out List<TargetBindingRow>? list) && list != null)
            {
                return list;
            }

            return Array.Empty<TargetBindingRow>();
        }

        /// <summary>Resolves one contribution identity; a miss means the slot has no active binding (P-017).</summary>
        public bool TryGet(TargetId target, CapabilityId capability, uint outputSlot, out TargetBindingRow row)
        {
            if (byTarget.TryGetValue(target.Value, out List<TargetBindingRow>? list) && list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    TargetBindingRow candidate = list[i];
                    if (candidate.Capability.Equals(capability) && candidate.OutputSlot == outputSlot)
                    {
                        row = candidate;
                        return true;
                    }
                }
            }

            row = default(TargetBindingRow);
            return false;
        }

        /// <summary>Number of active bindings still supported by one provider installation (P-033).</summary>
        public int SupportCountOf(ProviderInstallationId provider)
        {
            int count = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Provider.Equals(provider))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Applies one publication's binding delta: replacements are keyed by contribution identity, so a changed
        /// value replaces its own row and a retraction removes exactly one support (P-017, P-033).
        /// </summary>
        public TargetBindingTable Merge(
            IReadOnlyList<TargetBindingRow>? installs,
            IReadOnlyList<TargetBindingRow>? removals,
            IReadOnlyList<TargetId>? addedTargets = null)
        {
            var next = new List<TargetBindingRow>();
            for (int i = 0; i < rows.Length; i++)
            {
                if (!IsRemoved(rows[i], removals))
                {
                    next.Add(rows[i]);
                }
            }

            if (installs != null)
            {
                for (int i = 0; i < installs.Count; i++)
                {
                    TargetBindingRow install = installs[i];
                    for (int r = next.Count - 1; r >= 0; r--)
                    {
                        if (next[r].HasSameIdentity(install))
                        {
                            next.RemoveAt(r);
                        }
                    }

                    next.Add(install);
                }
            }

            var allTargets = new List<TargetId>(targets.Length + (addedTargets != null ? addedTargets.Count : 0));
            for (int i = 0; i < targets.Length; i++)
            {
                allTargets.Add(targets[i]);
            }

            if (addedTargets != null)
            {
                for (int i = 0; i < addedTargets.Count; i++)
                {
                    allTargets.Add(addedTargets[i]);
                }
            }

            return new TargetBindingTable(allTargets, next);
        }

        /// <summary>Removes one target's rows and identity; a despawn retracts exactly its own support (P-024).</summary>
        public TargetBindingTable WithoutTarget(TargetId target)
        {
            var next = new List<TargetBindingRow>(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                if (!rows[i].Target.Equals(target))
                {
                    next.Add(rows[i]);
                }
            }

            var remaining = new List<TargetId>(targets.Length);
            for (int i = 0; i < targets.Length; i++)
            {
                if (!targets[i].Equals(target))
                {
                    remaining.Add(targets[i]);
                }
            }

            return new TargetBindingTable(remaining, next);
        }

        /// <summary>Canonical fingerprint of the effective assembly; equal content gives equal bytes (P-008, TEST-022).</summary>
        public ContentHash Fingerprint()
        {
            var builder = new StringBuilder();
            builder.Append("bindings\n");
            for (int i = 0; i < targets.Length; i++)
            {
                builder.Append("target=").Append(PlanHashing.IdText(targets[i].Value)).Append('\n');
                IReadOnlyList<TargetBindingRow> list = BindingsOf(targets[i]);
                for (int r = 0; r < list.Count; r++)
                {
                    TargetBindingRow row = list[r];
                    builder.Append("binding=")
                        .Append(PlanHashing.IdText(row.Capability.Value)).Append(';')
                        .Append(row.CapabilityVersion.ToString(CultureInfo.InvariantCulture)).Append(';')
                        .Append(row.OutputSlot.ToString(CultureInfo.InvariantCulture)).Append(';')
                        .Append(row.Value.ToString(CultureInfo.InvariantCulture)).Append(';')
                        .Append(PlanHashing.IdText(row.Provider.Value)).Append(';')
                        .Append(row.ProviderGeneration.ToString(CultureInfo.InvariantCulture)).Append(';')
                        .Append(row.Priority.ToString(CultureInfo.InvariantCulture)).Append(';')
                        .Append(PlanHashing.IdText(row.Schema.Id.Value)).Append(';')
                        .Append(row.Schema.Version.ToString(CultureInfo.InvariantCulture))
                        .Append('\n');
                }
            }

            return PlanHashing.Of(builder.ToString());
        }

        private static bool IsRemoved(TargetBindingRow row, IReadOnlyList<TargetBindingRow>? removals)
        {
            if (removals == null)
            {
                return false;
            }

            for (int i = 0; i < removals.Count; i++)
            {
                if (row.HasSameIdentity(removals[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareRows(TargetBindingRow left, TargetBindingRow right)
        {
            int target = left.Target.Value.CompareTo(right.Target.Value);
            if (target != 0)
            {
                return target;
            }

            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            if (capability != 0)
            {
                return capability;
            }

            return left.OutputSlot.CompareTo(right.OutputSlot);
        }

        private static int CompareTargets(TargetId left, TargetId right) => left.Value.CompareTo(right.Value);
    }
}
