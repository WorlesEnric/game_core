// GameCore.Derivation — the pure rule predicates of P-013, P-015, P-016 and P-018 (GC-006).
//
// Both the indexed runtime engine and the full-recompute oracle call exactly these predicates, so the two
// traversals can only disagree about *which* candidates they discovered, never about what a candidate means
// (02 s4: they share schema definitions and rule contracts, but not invalidation or traversal code).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Finalized capabilities per target: the strictly lower strata a rule may read (P-021).</summary>
    public sealed class CapabilityLedger
    {
        private readonly Dictionary<Id128, HashSet<Id128>> finalized = new Dictionary<Id128, HashSet<Id128>>();

        /// <summary>Records that a capability was finalized for a target by a strictly lower stratum.</summary>
        public void Finalize(TargetId target, CapabilityId capability)
        {
            if (!finalized.TryGetValue(target.Value, out HashSet<Id128>? set))
            {
                set = new HashSet<Id128>();
                finalized.Add(target.Value, set);
            }

            set.Add(capability.Value);
        }

        /// <summary>True when the capability is effective on the target from an earlier stratum.</summary>
        public bool IsEffective(TargetId target, CapabilityId capability) =>
            finalized.TryGetValue(target.Value, out HashSet<Id128>? set) && set.Contains(capability.Value);

        /// <summary>Effective capabilities of one target in ascending canonical order; empty when it has none.</summary>
        public IReadOnlyList<CapabilityId> EffectiveCapabilitiesOf(TargetId target)
        {
            if (!finalized.TryGetValue(target.Value, out HashSet<Id128>? set) || set.Count == 0)
            {
                return Array.Empty<CapabilityId>();
            }

            List<Id128> ids = new List<Id128>(set);
            ids.Sort(Id128Codec.CompareBigEndian);
            CapabilityId[] result = new CapabilityId[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                result[i] = new CapabilityId(ids[i]);
            }

            return Array.AsReadOnly(result);
        }
    }

    /// <summary>The full evaluation of one candidate: its status plus every witness the explanation needs (P-026).</summary>
    public sealed class CandidateEvaluation
    {
        public CandidateEvaluation(
            CandidateStatus status,
            CandidateRejectionReason reason,
            DiagnosticCode diagnostic,
            ModeGateDecision modeGate,
            IReadOnlyList<ExclusionEvidence>? exclusions,
            IReadOnlyList<BoundaryEvidence>? boundaries,
            IReadOnlyList<CapabilityId>? missingInputs,
            IReadOnlyList<Id128>? evidenceKeys)
        {
            Status = status;
            Reason = reason;
            Diagnostic = diagnostic;
            ModeGate = modeGate;
            Exclusions = ContractCollections.Freeze(exclusions);
            Boundaries = ContractCollections.Freeze(boundaries);
            MissingInputs = ContractCollections.Freeze(missingInputs);
            EvidenceKeys = ContractCollections.Freeze(evidenceKeys);
        }

        public CandidateStatus Status { get; }

        public CandidateRejectionReason Reason { get; }

        public DiagnosticCode Diagnostic { get; }

        public ModeGateDecision ModeGate { get; }

        public IReadOnlyList<ExclusionEvidence> Exclusions { get; }

        public IReadOnlyList<BoundaryEvidence> Boundaries { get; }

        public IReadOnlyList<CapabilityId> MissingInputs { get; }

        public IReadOnlyList<Id128> EvidenceKeys { get; }

        public bool Eligible => Status == CandidateStatus.Emitted;
    }

    /// <summary>
    /// The pure rule predicates. Every method is a function of the immutable snapshot, the target descriptor and
    /// the finalized lower strata: no clock, no live ECS value and no side effect takes part (P-015).
    /// </summary>
    public static class DerivationPolicy
    {
        /// <summary>
        /// Evaluates one candidate in the canonical check order: selector contracts (P-015), exclusions and
        /// capability boundaries (P-016), the mode gate (P-013), lower-stratum inputs (P-021) and the static
        /// predicate (P-015). Exclusions and boundaries run before the mode gate because denial wins over
        /// imports, opt-ins and selection in both modes.
        /// </summary>
        public static CandidateEvaluation Evaluate(
            DerivationSnapshot snapshot,
            IDerivationValueSource values,
            DerivationInstall install,
            DerivationRule rule,
            DerivationTarget target,
            CapabilityLedger ledger)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (install == null)
            {
                throw new ArgumentNullException(nameof(install));
            }

            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (ledger == null)
            {
                throw new ArgumentNullException(nameof(ledger));
            }

            ModeGateDecision gate = EvaluateModeGate(snapshot, rule, install, target);

            // 1. Selector contracts: "each rule specifies accepted contract/schema versions" (P-015).
            if (!SelectorMatches(rule, target))
            {
                CandidateStatus status = DeclaresAnySelectorSchema(rule, target)
                    ? CandidateStatus.SelectorVersionMismatch
                    : CandidateStatus.SelectorNotAdvertised;
                CandidateRejectionReason reason = status == CandidateStatus.SelectorVersionMismatch
                    ? CandidateRejectionReason.SelectorVersionMismatch
                    : CandidateRejectionReason.SelectorNotAdvertised;
                return new CandidateEvaluation(
                    status,
                    reason,
                    DiagnosticCode.Ineligible,
                    gate,
                    null,
                    null,
                    null,
                    SelectorEvidence(rule, target, status));
            }

            // 2. Exclusions and boundaries deny in both modes (P-016).
            List<ExclusionEvidence> exclusions = CollectExclusions(snapshot, rule, install, target);
            if (exclusions.Count > 0)
            {
                return new CandidateEvaluation(
                    CandidateStatus.Excluded,
                    CandidateRejectionReason.TargetExcluded,
                    DiagnosticCode.Ineligible,
                    gate,
                    exclusions,
                    null,
                    null,
                    ExclusionEvidenceKeys(exclusions));
            }

            List<BoundaryEvidence> boundaries = CollectBoundaries(
                snapshot, install.Scope, target.Scope, rule.OutputCapability.Capability);
            if (boundaries.Count > 0)
            {
                return new CandidateEvaluation(
                    CandidateStatus.BlockedByBoundary,
                    CandidateRejectionReason.CapabilityBoundary,
                    DiagnosticCode.Ineligible,
                    gate,
                    null,
                    boundaries,
                    null,
                    BoundaryEvidenceKeys(boundaries));
            }

            // 3. Mode gate (P-013).
            if (gate == ModeGateDecision.ConservativeNotGranted)
            {
                return new CandidateEvaluation(
                    CandidateStatus.ModeDenied,
                    CandidateRejectionReason.ModeNotGranted,
                    DiagnosticCode.Ineligible,
                    gate,
                    null,
                    null,
                    null,
                    ModeEvidence(rule, install, target));
            }

            // 4. Lower-stratum inputs: declared descriptor capabilities or finalized lower strata (P-015, P-021).
            List<CapabilityId> missing = CollectMissingInputs(rule, target, ledger);
            if (missing.Count > 0)
            {
                return new CandidateEvaluation(
                    CandidateStatus.InputMissing,
                    CandidateRejectionReason.InputCapabilityMissing,
                    DiagnosticCode.MissingDependency,
                    gate,
                    null,
                    null,
                    missing,
                    MissingInputKeys(missing));
            }

            // 5. Static predicate (P-015). A declared but unregistered predicate is a catalog error (P-028).
            if (!rule.StaticPredicate.RegistrationKey.IsDefault)
            {
                DerivationPredicateContext context = new DerivationPredicateContext(
                    target,
                    target.Descriptor.SupportedCapabilities,
                    ledger.EffectiveCapabilitiesOf(target.Target),
                    target.Descriptor.SupportedSchemas,
                    target.Descriptor.Tags,
                    rule.OutputStratum);

                bool accepted;
                if (!values.TryEvaluate(rule.StaticPredicate, context, out accepted))
                {
                    return new CandidateEvaluation(
                        CandidateStatus.PredicateUnregistered,
                        CandidateRejectionReason.PredicateUnregistered,
                        DiagnosticCode.MissingDependency,
                        gate,
                        null,
                        null,
                        null,
                        new[] { rule.StaticPredicate.RegistrationKey });
                }

                if (!accepted)
                {
                    return new CandidateEvaluation(
                        CandidateStatus.PredicateRejected,
                        CandidateRejectionReason.PredicateRejected,
                        DiagnosticCode.Ineligible,
                        gate,
                        null,
                        null,
                        null,
                        PredicateEvidence(rule));
                }
            }

            return new CandidateEvaluation(
                CandidateStatus.Emitted,
                CandidateRejectionReason.None,
                DiagnosticCode.None,
                gate,
                null,
                null,
                null,
                MatchedEvidence(target, rule, install));
        }

        /// <summary>
        /// The mode grant predicate of P-013, as the exact table of 02 s5:
        /// a `LocalOnly` rule is permitted at its installation scope in both modes; in Automatic an eligible
        /// descendant is permitted without any import; in Conservative a descendant-reach rule needs
        /// `ExportToDescendants` plus an explicit import or a full explicit target opt-in.
        /// </summary>
        public static ModeGateDecision EvaluateModeGate(
            DerivationSnapshot snapshot,
            DerivationRule rule,
            DerivationInstall install,
            DerivationTarget target)
        {
            if (rule.Reach == PropagationReach.LocalOnly)
            {
                return ModeGateDecision.LocalOnlyRule;
            }

            if (snapshot.Mode == PropagationMode.Automatic)
            {
                return ModeGateDecision.AutomaticDescendantGrant;
            }

            CapabilityId capability = rule.OutputCapability.Capability;
            ProviderInstallationId provider = new ProviderInstallationId(install.Instance.Value);
            if (rule.ExportToDescendants && ImportsCapability(snapshot, target, capability, provider))
            {
                return ModeGateDecision.ConservativeExportedAndImported;
            }

            if (HasTargetOptIn(target, capability, provider))
            {
                return ModeGateDecision.ConservativeTargetOptIn;
            }

            return ModeGateDecision.ConservativeNotGranted;
        }

        /// <summary>
        /// True when the target's descriptor, its scope or an ancestor scope explicitly imports that capability
        /// from that provider installation. Imports are data and stay stored while inactive; Automatic never
        /// manufactures one (P-013).
        /// </summary>
        public static bool ImportsCapability(
            DerivationSnapshot snapshot,
            DerivationTarget target,
            CapabilityId capability,
            ProviderInstallationId provider)
        {
            IReadOnlyList<CapabilityImport> declared = target.Descriptor.Imports;
            for (int i = 0; i < declared.Count; i++)
            {
                if (declared[i].CapabilityId.Equals(capability) && declared[i].ProviderInstallationId.Equals(provider))
                {
                    return true;
                }
            }

            IReadOnlyList<ScopeId> chain = snapshot.SelfAndAncestors(target.Scope);
            for (int s = 0; s < chain.Count; s++)
            {
                if (!snapshot.TryGetScope(chain[s], out DerivationScope? scope) || scope == null)
                {
                    continue;
                }

                if (scope.ImportsFrom(capability, provider))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the descriptor carries a complete explicit opt-in for that provider and capability.</summary>
        public static bool HasTargetOptIn(
            DerivationTarget target,
            CapabilityId capability,
            ProviderInstallationId provider)
        {
            IReadOnlyList<TargetOptIn> optIns = target.Descriptor.OptIns;
            for (int i = 0; i < optIns.Count; i++)
            {
                if (optIns[i].CapabilityId.Equals(capability) && optIns[i].ProviderInstallationId.Equals(provider))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Capability boundaries of P-016 on the propagation path: a scope strictly below the provider scope and
        /// at or above the target blocks the capability for targets beneath it, because a boundary blocks
        /// outside rules while providers installed at or below it still work. A descendant cannot reopen an
        /// ancestor boundary, and sibling branches are never on the path.
        /// </summary>
        public static List<BoundaryEvidence> CollectBoundaries(
            DerivationSnapshot snapshot,
            ScopeId providerScope,
            ScopeId targetScope,
            CapabilityId capability)
        {
            List<BoundaryEvidence> evidence = new List<BoundaryEvidence>();
            ScopeId current = targetScope;
            while (!current.IsDefault && !current.Equals(providerScope))
            {
                if (snapshot.TryGetScope(current, out DerivationScope? scope) && scope != null && scope.BlocksCapability(capability))
                {
                    evidence.Add(new BoundaryEvidence(current, capability, scope.CapabilityIsolation.AllContracts));
                }

                current = snapshot.ParentOf(current);
            }

            return evidence;
        }

        /// <summary>
        /// Exclusions of P-016 that apply to this candidate: exclusions declared on the target descriptor and on
        /// the target's own scope or an ancestor scope. A scope-stored exclusion addresses the subtree it is
        /// declared in unless it names a single target; a descriptor-stored exclusion may name a scope instead.
        /// </summary>
        public static List<ExclusionEvidence> CollectExclusions(
            DerivationSnapshot snapshot,
            DerivationRule rule,
            DerivationInstall install,
            DerivationTarget target)
        {
            List<ExclusionEvidence> evidence = new List<ExclusionEvidence>();
            IReadOnlyList<ExclusionRule> declared = target.Descriptor.Exclusions;
            for (int i = 0; i < declared.Count; i++)
            {
                if (ExclusionAppliesFromDescriptor(snapshot, declared[i], target) &&
                    NamesCandidate(declared[i], rule, install))
                {
                    evidence.Add(new ExclusionEvidence(declared[i], false, target.Scope));
                }
            }

            IReadOnlyList<ScopeId> chain = snapshot.SelfAndAncestors(target.Scope);
            for (int s = 0; s < chain.Count; s++)
            {
                if (!snapshot.TryGetScope(chain[s], out DerivationScope? scope) || scope == null)
                {
                    continue;
                }

                IReadOnlyList<ExclusionRule> scoped = scope.Exclusions;
                for (int e = 0; e < scoped.Count; e++)
                {
                    if (ExclusionAppliesFromScope(scoped[e], chain[s], target.Scope) &&
                        NamesCandidate(scoped[e], rule, install))
                    {
                        evidence.Add(new ExclusionEvidence(scoped[e], true, chain[s]));
                    }
                }
            }

            return evidence;
        }

        /// <summary>True when an exclusion names this candidate's capability, rule or provider installation.</summary>
        public static bool NamesCandidate(ExclusionRule exclusion, DerivationRule rule, DerivationInstall install)
        {
            switch (exclusion.Kind)
            {
                case ExclusionTargetKind.Capability:
                    return exclusion.TargetId.Equals(rule.OutputCapability.Capability.Value);
                case ExclusionTargetKind.Rule:
                    return exclusion.TargetId.Equals(rule.RuleId.Value);
                case ExclusionTargetKind.Provider:
                    return exclusion.TargetId.Equals(install.Instance.Value);
                default:
                    return false;
            }
        }

        /// <summary>Selector match: at least one accepted contract/schema of the rule, or no selector at all.</summary>
        public static bool SelectorMatches(DerivationRule rule, DerivationTarget target)
        {
            IReadOnlyList<SchemaRef> selectors = rule.SelectorContracts;
            if (selectors.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < selectors.Count; i++)
            {
                if (target.DeclaresSchema(selectors[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DeclaresAnySelectorSchema(DerivationRule rule, DerivationTarget target)
        {
            IReadOnlyList<SchemaRef> selectors = rule.SelectorContracts;
            for (int i = 0; i < selectors.Count; i++)
            {
                if (target.DeclaresSchemaId(selectors[i].Id))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<CapabilityId> CollectMissingInputs(
            DerivationRule rule,
            DerivationTarget target,
            CapabilityLedger ledger)
        {
            List<CapabilityId> missing = new List<CapabilityId>();
            IReadOnlyList<CapabilityRef> inputs = rule.InputCapabilities;
            for (int i = 0; i < inputs.Count; i++)
            {
                CapabilityRef input = inputs[i];
                if (ledger.IsEffective(target.Target, input.Capability))
                {
                    continue;
                }

                if (target.DeclaresCapability(input))
                {
                    continue;
                }

                missing.Add(input.Capability);
            }

            return missing;
        }

        private static bool ExclusionAppliesFromScope(ExclusionRule exclusion, ScopeId declaredAt, ScopeId targetScope)
        {
            if (exclusion.HasTarget)
            {
                return false;
            }

            if (exclusion.AppliesToSubtree)
            {
                // The chain walk already guarantees that the declaring scope is the target scope or an ancestor.
                return true;
            }

            return declaredAt.Equals(targetScope);
        }

        private static bool ExclusionAppliesFromDescriptor(DerivationSnapshot snapshot, ExclusionRule exclusion, DerivationTarget target)
        {
            if (exclusion.HasTarget)
            {
                return exclusion.AtTarget.Equals(target.Target);
            }

            if (!exclusion.HasScope)
            {
                // A descriptor exclusion without a scope and without a target addresses its own target.
                return true;
            }

            if (exclusion.AppliesToSubtree)
            {
                return snapshot.IsSelfOrAncestor(exclusion.AtScope, target.Scope);
            }

            return exclusion.AtScope.Equals(target.Scope);
        }

        private static IReadOnlyList<Id128> SelectorEvidence(
            DerivationRule rule,
            DerivationTarget target,
            CandidateStatus status)
        {
            List<Id128> keys = new List<Id128>();
            IReadOnlyList<SchemaRef> selectors = rule.SelectorContracts;
            for (int i = 0; i < selectors.Count; i++)
            {
                keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey(
                    status == CandidateStatus.SelectorVersionMismatch ? "selectorVersion" : "selector",
                    selectors[i].ToString())));
            }

            IReadOnlyList<SchemaRef> declared = target.Descriptor.SupportedSchemas;
            for (int i = 0; i < declared.Count; i++)
            {
                keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("declaredSchema", declared[i].ToString())));
            }

            keys.Sort(Id128Codec.CompareBigEndian);
            return keys.AsReadOnly();
        }

        private static IReadOnlyList<Id128> MatchedEvidence(DerivationTarget target, DerivationRule rule, DerivationInstall install)
        {
            List<Id128> keys = new List<Id128>();
            IReadOnlyList<SchemaRef> declared = target.Descriptor.SupportedSchemas;
            for (int i = 0; i < declared.Count; i++)
            {
                keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("declaredSchema", declared[i].ToString())));
            }

            keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("provider", install.Instance.ToString())));
            keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("rule", rule.RuleId.ToString())));
            keys.Sort(Id128Codec.CompareBigEndian);
            return keys.AsReadOnly();
        }

        private static IReadOnlyList<Id128> ModeEvidence(
            DerivationRule rule,
            DerivationInstall install,
            DerivationTarget target)
        {
            List<Id128> keys = new List<Id128>
            {
                EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("modeGate", "conservativeNotGranted")),
                EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("provider", install.Instance.ToString())),
                EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("capability", rule.OutputCapability.Capability.ToString())),
                EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("target", target.Target.ToString())),
            };
            keys.Sort(Id128Codec.CompareBigEndian);
            return keys.AsReadOnly();
        }

        private static IReadOnlyList<Id128> ExclusionEvidenceKeys(IReadOnlyList<ExclusionEvidence> exclusions)
        {
            List<Id128> keys = new List<Id128>(exclusions.Count);
            for (int i = 0; i < exclusions.Count; i++)
            {
                keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey(
                    exclusions[i].FromScope ? "scopeExclusion" : "targetExclusion",
                    exclusions[i].Rule.ToString())));
            }

            keys.Sort(Id128Codec.CompareBigEndian);
            return keys.AsReadOnly();
        }

        private static IReadOnlyList<Id128> BoundaryEvidenceKeys(IReadOnlyList<BoundaryEvidence> boundaries)
        {
            List<Id128> keys = new List<Id128>(boundaries.Count);
            for (int i = 0; i < boundaries.Count; i++)
            {
                keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("boundary", boundaries[i].ToString())));
            }

            keys.Sort(Id128Codec.CompareBigEndian);
            return keys.AsReadOnly();
        }

        private static IReadOnlyList<Id128> MissingInputKeys(IReadOnlyList<CapabilityId> missing)
        {
            List<Id128> keys = new List<Id128>(missing.Count);
            for (int i = 0; i < missing.Count; i++)
            {
                keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("missingInput", missing[i].ToString())));
            }

            keys.Sort(Id128Codec.CompareBigEndian);
            return keys.AsReadOnly();
        }

        private static IReadOnlyList<Id128> PredicateEvidence(DerivationRule rule)
        {
            List<Id128> keys = new List<Id128>
            {
                EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("predicate", rule.StaticPredicate.RegistrationKey.ToString())),
            };
            keys.Sort(Id128Codec.CompareBigEndian);
            return keys.AsReadOnly();
        }
    }
}
