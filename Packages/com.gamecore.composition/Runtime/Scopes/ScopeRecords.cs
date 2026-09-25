// GameCore.Composition — scope tree records (P-010), memberships and Conservative grant data (P-013).
//
// Scopes form one rooted acyclic tree per world; a live target has exactly one owner scope and a plugin is
// installed at exactly one scope (P-010). This file holds the authoritative structure: parent, depth, the two
// separate isolation sets of P-016, exclusions, and the Conservative-mode import grants of P-013. Membership
// of installations is *not* duplicated here — the installation store owns that, so there is one source of
// truth per fact (05 s1: composition indexes store metadata and support, never a second copy of authority).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>
    /// The Conservative-mode grant data of one scope (P-013). In Conservative a descendant rule applies only
    /// when the provider exports the capability and the target's recipe/schema/scope explicitly imports it, or
    /// the target has a full explicit opt-in. Target-level opt-ins belong to target descriptors (GC-006); the
    /// scope-level half is stored and validated here and consumed by derivation later.
    /// </summary>
    public sealed class ScopeGrants
    {
        public ScopeGrants(IReadOnlyList<CapabilityImport>? imports)
        {
            Imports = CanonicalScopeOrder.SortImports(imports);
        }

        public static ScopeGrants None { get; } = new ScopeGrants(null);

        /// <summary>Explicit capability imports declared at this scope, in canonical order, without duplicates.</summary>
        public IReadOnlyList<CapabilityImport> Imports { get; }

        public bool IsEmpty => Imports.Count == 0;

        /// <summary>True when the scope explicitly imports this capability from this provider installation.</summary>
        public bool ImportsFrom(CapabilityId capability, ProviderInstallationId provider)
        {
            for (int i = 0; i < Imports.Count; i++)
            {
                CapabilityImport import = Imports[i];
                if (import.CapabilityId.Equals(capability) && import.ProviderInstallationId.Equals(provider))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// One composition scope as stored assembly data (P-004: explicit saved data, never derived from creation
    /// order). <see cref="Depth"/> is derived from the parent chain and cached so subtree enumeration does not
    /// walk to the root per query.
    /// </summary>
    public sealed class ScopeRecord
    {
        public ScopeRecord(
            ScopeId scope,
            ScopeId parent,
            int depth,
            IsolationSet serviceIsolation,
            IsolationSet capabilityIsolation,
            IReadOnlyList<ExclusionRule>? exclusions,
            ScopeGrants? grants)
        {
            if (scope.IsDefault)
            {
                throw new ArgumentException("A scope record requires a real stable scope identity (P-004).", nameof(scope));
            }

            if (depth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(depth), "A scope depth is never negative.");
            }

            Scope = scope;
            Parent = parent;
            Depth = depth;
            ServiceIsolation = serviceIsolation ?? new IsolationSet(false, null);
            CapabilityIsolation = capabilityIsolation ?? new IsolationSet(false, null);
            Exclusions = CanonicalScopeOrder.SortExclusions(exclusions);
            Grants = grants ?? ScopeGrants.None;

            if (IsRoot != Parent.IsDefault)
            {
                throw new ArgumentException("Only the world root scope has no parent (P-010).", nameof(parent));
            }
        }

        public ScopeId Scope { get; }

        /// <summary>Default means the world root: the only scope without a parent (P-010).</summary>
        public ScopeId Parent { get; }

        /// <summary>Root is 0; children are exactly one deeper than their parent.</summary>
        public int Depth { get; }

        public bool IsRoot => Scope.IsDefault == false && Parent.IsDefault;

        /// <summary>Named service-isolation set of this scope; `*` means all contracts (P-016).</summary>
        public IsolationSet ServiceIsolation { get; }

        /// <summary>Named capability-isolation set of this scope; `*` means all contracts (P-016).</summary>
        public IsolationSet CapabilityIsolation { get; }

        public IReadOnlyList<ExclusionRule> Exclusions { get; }

        /// <summary>Conservative-mode grant data; interpreted by derivation, stored and validated here (P-013).</summary>
        public ScopeGrants Grants { get; }

        /// <summary>Snapshot of this scope; the world-level mode is supplied because it is one world setting (P-013).</summary>
        public ScopeSnapshot ToSnapshot(PropagationMode mode, IReadOnlyList<PluginInstanceId>? installs) =>
            new ScopeSnapshot(Scope, Parent, mode, ServiceIsolation, CapabilityIsolation, Exclusions, installs);
    }

    /// <summary>
    /// Immutable scope registry: the desired scope tree plus its queries. Every enumeration is in canonical
    /// order and no query depends on insertion timing or dictionary order (P-008). Sibling branches are never
    /// reachable from one another: <see cref="Ancestors"/> walks parents and <see cref="Descendants"/> walks
    /// children, and neither is a sibling search (P-011).
    /// </summary>
    public sealed class ScopeRegistry
    {
        private readonly Dictionary<Id128, ScopeRecord> byScope;
        private readonly Dictionary<Id128, List<ScopeId>> children;
        private readonly List<ScopeRecord> canonicalScopes;

        public ScopeRegistry(ScopeRecord root, IReadOnlyList<ScopeRecord>? additional)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (!root.IsRoot || root.Depth != 0)
            {
                throw new ArgumentException("A registry must start from the depth-0 world root scope (P-010).", nameof(root));
            }

            byScope = new Dictionary<Id128, ScopeRecord>();
            children = new Dictionary<Id128, List<ScopeId>>();
            canonicalScopes = new List<ScopeRecord>();

            Add(root);
            if (additional != null)
            {
                var parentFirst = new List<ScopeRecord>(additional.Count);
                for (int i = 0; i < additional.Count; i++)
                {
                    ScopeRecord record = additional[i];
                    if (record == null)
                    {
                        throw new ArgumentException("A scope record must not be null.", nameof(additional));
                    }

                    parentFirst.Add(record);
                }

                parentFirst.Sort((left, right) =>
                {
                    int depth = left.Depth.CompareTo(right.Depth);
                    return depth != 0 ? depth : CompareRecords(left, right);
                });
                for (int i = 0; i < parentFirst.Count; i++)
                {
                    Add(parentFirst[i]);
                }
            }

            canonicalScopes.Sort(CompareRecords);
            foreach (KeyValuePair<Id128, List<ScopeId>> pair in children)
            {
                pair.Value.Sort(CompareScopeIds);
            }
        }

        /// <summary>All scopes in canonical identity order; enumeration order is a property of the data, not of insertion.</summary>
        public IReadOnlyList<ScopeRecord> Scopes => canonicalScopes;

        public int Count => canonicalScopes.Count;

        public ScopeId Root => canonicalScopes[0].Scope;

        public bool TryGet(ScopeId scope, out ScopeRecord? record)
        {
            if (byScope.TryGetValue(scope.Value, out ScopeRecord? found))
            {
                record = found;
                return true;
            }

            record = null;
            return false;
        }

        /// <summary>Depth of one scope, or -1 when this registry does not contain it (never a default policy).</summary>
        public int Depth(ScopeId scope) => byScope.TryGetValue(scope.Value, out ScopeRecord? record) ? record.Depth : -1;

        /// <summary>Direct children of a scope in canonical order; empty for an unknown scope (never inventing a node).</summary>
        public IReadOnlyList<ScopeId> ChildrenOf(ScopeId scope)
        {
            if (children.TryGetValue(scope.Value, out List<ScopeId>? list))
            {
                return list;
            }

            return Array.Empty<ScopeId>();
        }

        /// <summary>
        /// Ancestor chain from the parent up to the root, nearest first. This is the only upward traversal:
        /// sibling scopes are never visited, so a sibling's provider can never be observed (P-011).
        /// </summary>
        public IReadOnlyList<ScopeId> Ancestors(ScopeId scope)
        {
            List<ScopeId> chain = new List<ScopeId>();
            if (!byScope.TryGetValue(scope.Value, out ScopeRecord? current))
            {
                return chain;
            }

            while (!current.Parent.IsDefault)
            {
                chain.Add(current.Parent);
                if (!byScope.TryGetValue(current.Parent.Value, out ScopeRecord? next))
                {
                    break;
                }

                current = next;
            }

            return chain;
        }

        /// <summary>Self plus ancestors, nearest first; the effective domain of an `AncestorsAndSelf` dependency (P-011).</summary>
        public IReadOnlyList<ScopeId> SelfAndAncestors(ScopeId scope)
        {
            List<ScopeId> chain = new List<ScopeId>();
            if (!byScope.ContainsKey(scope.Value))
            {
                return chain;
            }

            chain.Add(scope);
            chain.AddRange(Ancestors(scope));
            return chain;
        }

        /// <summary>Every descendant of a scope in canonical order; the scope itself is excluded.</summary>
        public IReadOnlyList<ScopeId> Descendants(ScopeId scope)
        {
            List<ScopeId> result = new List<ScopeId>();
            if (!byScope.ContainsKey(scope.Value))
            {
                return result;
            }

            AddDescendants(scope, result);
            result.Sort(CompareScopeIds);
            return result;
        }

        /// <summary>Self plus every descendant in canonical order: the domain of a `SelfAndDescendants` selector (P-013).</summary>
        public IReadOnlyList<ScopeId> SelfAndDescendants(ScopeId scope)
        {
            List<ScopeId> result = new List<ScopeId>();
            if (!byScope.ContainsKey(scope.Value))
            {
                return result;
            }

            result.Add(scope);
            AddDescendants(scope, result);
            result.Sort(CompareScopeIds);
            return result;
        }

        /// <summary>True when <paramref name="candidate"/> is the scope or one of its ancestors (`covers` up the tree).</summary>
        public bool IsSelfOrAncestorOf(ScopeId ancestor, ScopeId candidate)
        {
            ScopeId current = candidate;
            if (ancestor.Equals(current))
            {
                return true;
            }

            while (byScope.TryGetValue(current.Value, out ScopeRecord? record) && !record.Parent.IsDefault)
            {
                if (ancestor.Equals(record.Parent))
                {
                    return true;
                }

                current = record.Parent;
            }

            return false;
        }

        /// <summary>
        /// Whether making <paramref name="newParent"/> the parent of <paramref name="scope"/> would create a
        /// cycle, including making a scope its own parent (O-02 cycle rejection, P-010).
        /// </summary>
        public bool WouldCreateCycle(ScopeId scope, ScopeId newParent)
        {
            if (scope.Equals(newParent))
            {
                return true;
            }

            ScopeId current = newParent;
            while (byScope.TryGetValue(current.Value, out ScopeRecord? record))
            {
                if (!record.Parent.IsDefault && record.Parent.Equals(scope))
                {
                    return true;
                }

                if (record.Parent.IsDefault)
                {
                    return false;
                }

                current = record.Parent;
            }

            return false;
        }

        /// <summary>
        /// A registry with one more scope, or a diagnostic. The parent must already exist and the depth must be
        /// exactly one deeper, so the tree can never contain a detached or inconsistent node (P-010).
        /// </summary>
        public bool TryAdd(ScopeRecord record, out ScopeRegistry? next, out DiagnosticCode code)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            next = null;
            code = DiagnosticCode.None;

            if (!record.IsRoot && !byScope.ContainsKey(record.Parent.Value))
            {
                code = DiagnosticCode.MissingDependency;
                return false;
            }

            if (byScope.ContainsKey(record.Scope.Value))
            {
                code = DiagnosticCode.OwnershipConflict;
                return false;
            }

            if (record.IsRoot)
            {
                // There is one rooted tree per world, so a second root is an ownership conflict (P-010).
                code = DiagnosticCode.OwnershipConflict;
                return false;
            }

            ScopeRecord parent = byScope[record.Parent.Value];
            if (record.Depth != parent.Depth + 1)
            {
                code = DiagnosticCode.OwnershipConflict;
                return false;
            }

            List<ScopeRecord> all = new List<ScopeRecord>(canonicalScopes.Count + 1);
            all.AddRange(canonicalScopes);
            all.Add(record);
            next = new ScopeRegistry(FindRoot(all), RemoveRoot(all));
            return true;
        }

        /// <summary>
        /// A registry with one scope moved under a new parent, or a diagnostic (O-02 `Reparent`). A cycle, an
        /// unknown parent, the root scope or an unchanged parent is rejected here; depths of the whole moved
        /// subtree are recomputed so the tree stays consistent (P-010). Cross-world movement cannot be expressed
        /// because a registry holds exactly one world's scopes.
        /// </summary>
        public bool TryReparent(ScopeId scope, ScopeId newParent, out ScopeRegistry? next, out DiagnosticCode code)
        {
            next = null;
            code = DiagnosticCode.None;

            if (!byScope.TryGetValue(scope.Value, out ScopeRecord? moved) || !byScope.TryGetValue(newParent.Value, out ScopeRecord? parent))
            {
                code = DiagnosticCode.MissingDependency;
                return false;
            }

            if (moved.IsRoot || moved.Parent.Equals(newParent))
            {
                code = DiagnosticCode.OwnershipConflict;
                return false;
            }

            if (WouldCreateCycle(scope, newParent))
            {
                code = DiagnosticCode.Cycle;
                return false;
            }

            int delta = (parent.Depth + 1) - moved.Depth;
            List<ScopeRecord> rebuilt = new List<ScopeRecord>(canonicalScopes.Count);
            for (int i = 0; i < canonicalScopes.Count; i++)
            {
                ScopeRecord record = canonicalScopes[i];
                if (record.Scope.Equals(scope))
                {
                    rebuilt.Add(new ScopeRecord(record.Scope, newParent, record.Depth + delta, record.ServiceIsolation, record.CapabilityIsolation, record.Exclusions, record.Grants));
                    continue;
                }

                if (IsSelfOrAncestorOf(scope, record.Scope))
                {
                    rebuilt.Add(new ScopeRecord(record.Scope, record.Parent, record.Depth + delta, record.ServiceIsolation, record.CapabilityIsolation, record.Exclusions, record.Grants));
                    continue;
                }

                rebuilt.Add(record);
            }

            next = new ScopeRegistry(FindRoot(rebuilt), RemoveRoot(rebuilt));
            return true;
        }

        private void Add(ScopeRecord record)
        {
            if (byScope.ContainsKey(record.Scope.Value))
            {
                throw new ArgumentException("A scope identity appears once in one world (P-004).", nameof(record));
            }

            if (!record.IsRoot && !byScope.ContainsKey(record.Parent.Value))
            {
                throw new ArgumentException("A scope's parent must already exist in the same world (P-010).", nameof(record));
            }

            byScope.Add(record.Scope.Value, record);
            canonicalScopes.Add(record);
            if (!record.IsRoot)
            {
                if (!children.TryGetValue(record.Parent.Value, out List<ScopeId>? list))
                {
                    list = new List<ScopeId>();
                    children.Add(record.Parent.Value, list);
                }

                list.Add(record.Scope);
            }
        }

        private void AddDescendants(ScopeId scope, List<ScopeId> result)
        {
            IReadOnlyList<ScopeId> direct = ChildrenOf(scope);
            for (int i = 0; i < direct.Count; i++)
            {
                result.Add(direct[i]);
                AddDescendants(direct[i], result);
            }
        }

        private static ScopeRecord FindRoot(List<ScopeRecord> records)
        {
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].IsRoot)
                {
                    return records[i];
                }
            }

            throw new ArgumentException("A scope registry always contains its world root.", nameof(records));
        }

        private static List<ScopeRecord> RemoveRoot(List<ScopeRecord> records)
        {
            List<ScopeRecord> rest = new List<ScopeRecord>(records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                if (!records[i].IsRoot)
                {
                    rest.Add(records[i]);
                }
            }

            return rest;
        }

        private static int CompareRecords(ScopeRecord left, ScopeRecord right) => CompareScopeIds(left.Scope, right.Scope);

        private static int CompareScopeIds(ScopeId left, ScopeId right) => left.Value.CompareTo(right.Value);
    }

    /// <summary>
    /// Canonical ordering for scope-owned data. Every comparator is a total order whose final key is a stable
    /// identity, so no result depends on registration timing or enumeration order (P-008).
    /// </summary>
    public static class CanonicalScopeOrder
    {
        public static IReadOnlyList<CapabilityImport> SortImports(IReadOnlyList<CapabilityImport>? imports) =>
            CanonicalOrder.Sort(imports, CompareImports);

        public static IReadOnlyList<ExclusionRule> SortExclusions(IReadOnlyList<ExclusionRule>? exclusions) =>
            CanonicalOrder.Sort(exclusions, CompareExclusions);

        public static IReadOnlyList<PluginInstanceId> SortInstalls(IReadOnlyList<PluginInstanceId>? installs) =>
            CanonicalOrder.Sort(installs, CompareInstalls);

        public static IReadOnlyList<ServiceSelection> SortSelections(IReadOnlyList<ServiceSelection>? selections) =>
            CanonicalOrder.Sort(selections, CompareSelections);

        public static int CompareImports(CapabilityImport left, CapabilityImport right)
        {
            int capability = left.CapabilityId.Value.CompareTo(right.CapabilityId.Value);
            return capability != 0 ? capability : left.ProviderInstallationId.Value.CompareTo(right.ProviderInstallationId.Value);
        }

        public static int CompareInstalls(PluginInstanceId left, PluginInstanceId right) => left.Value.CompareTo(right.Value);

        public static int CompareSelections(ServiceSelection left, ServiceSelection right)
        {
            int contract = left.Contract.ContractId.CompareTo(right.Contract.ContractId);
            return contract != 0 ? contract : left.Provider.Value.CompareTo(right.Provider.Value);
        }

        /// <summary>
        /// Exclusion order: kind, then selector scope, then selector target, then the excluded identity. The
        /// kind prefix keeps a capability exclusion from ever sorting as if it were a provider exclusion.
        /// </summary>
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

        /// <summary>Removes duplicate entries from an already canonically sorted list.</summary>
        public static List<T> Deduplicate<T>(IReadOnlyList<T> sorted, Func<T, T, bool> equal)
        {
            List<T> unique = new List<T>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i == 0 || !equal(sorted[i - 1], sorted[i]))
                {
                    unique.Add(sorted[i]);
                }
            }

            return unique;
        }
    }
}
