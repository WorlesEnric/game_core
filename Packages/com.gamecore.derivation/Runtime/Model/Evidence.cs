// GameCore.Derivation — candidate decisions, exclusion evidence and complete provenance (GC-006).
//
// P-026: `Explain(TargetId, CapabilityId, SnapshotToken)` returns matching and rejected rules, source scope path,
// descriptor evidence, exclusions/boundaries, mode gate, stratum, candidates, composition decisions, support ids,
// resulting recipe hash and state disposition. Records may be interned/compacted but stay reconstructable for the
// retained epoch, and diagnostics never retain an unbounded string tree per entity.
//
// This file is the interned form: one compact decision per candidate, one explanation per (target, capability),
// both immutable and canonically ordered, projected into the shared `ExplanationPage` shape on demand.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Outcome of one candidate evaluation (P-015, P-016, P-018, P-021).</summary>
    public enum CandidateStatus
    {
        /// <summary>The candidate emitted bounded output slots.</summary>
        Emitted = 0,

        /// <summary>Eligible, but another candidate supplied the effective value (P-019).</summary>
        Shadowed = 1,

        /// <summary>The target does not declare any contract the rule selects (P-015).</summary>
        SelectorNotAdvertised = 2,

        /// <summary>The target declares the schema, but not an accepted version (P-015).</summary>
        SelectorVersionMismatch = 3,

        /// <summary>An exclusion on the target or in its scope chain names this capability, rule or provider (P-016).</summary>
        Excluded = 4,

        /// <summary>A capability boundary between the provider and the target blocks this capability (P-016).</summary>
        BlockedByBoundary = 5,

        /// <summary>The mode gate denies the candidate (P-013).</summary>
        ModeDenied = 6,

        /// <summary>A declared lower-stratum input capability is absent on this target (P-015, P-021).</summary>
        InputMissing = 7,

        /// <summary>The rule's static predicate rejected this target (P-015).</summary>
        PredicateRejected = 8,

        /// <summary>The rule declares a predicate the catalog did not register (P-028).</summary>
        PredicateUnregistered = 9,
    }

    /// <summary>Why one candidate was denied, in the order the checks run (P-015, P-016, P-013, P-019).</summary>
    public enum CandidateRejectionReason
    {
        None = 0,

        /// <summary>No selector contract matches the descriptor.</summary>
        SelectorNotAdvertised = 1,

        /// <summary>The descriptor advertises the schema at another version.</summary>
        SelectorVersionMismatch = 2,

        /// <summary>A capability, rule or provider exclusion applies (P-016).</summary>
        TargetExcluded = 3,

        /// <summary>A capability boundary of a scope on the propagation path applies (P-016).</summary>
        CapabilityBoundary = 4,

        /// <summary>Conservative mode without an export-plus-import grant or a full target opt-in (P-013).</summary>
        ModeNotGranted = 5,

        /// <summary>A declared lower-stratum input capability is absent.</summary>
        InputCapabilityMissing = 6,

        /// <summary>The static predicate returned false.</summary>
        PredicateRejected = 7,

        /// <summary>The static predicate key is not registered in this catalog (P-028).</summary>
        PredicateUnregistered = 8,
    }

    /// <summary>How the mode gate treated one candidate (P-013); reported by every explanation.</summary>
    public enum ModeGateDecision
    {
        /// <summary>Automatic mode permits eligible descendants without any import.</summary>
        AutomaticDescendantGrant = 0,

        /// <summary>A `LocalOnly` rule applies at its installation scope in both modes.</summary>
        LocalOnlyRule = 1,

        /// <summary>Conservative mode granted the candidate because it exports and the target imports it.</summary>
        ConservativeExportedAndImported = 2,

        /// <summary>Conservative mode granted the candidate through a full explicit target opt-in.</summary>
        ConservativeTargetOptIn = 3,

        /// <summary>Conservative mode denied the candidate: no export-plus-import and no opt-in.</summary>
        ConservativeNotGranted = 4,
    }

    /// <summary>One capability boundary along the propagation path that denied a candidate (P-016).</summary>
    public readonly struct BoundaryEvidence
    {
        public readonly ScopeId BoundaryScope;
        public readonly CapabilityId Capability;
        public readonly bool AllContracts;

        public BoundaryEvidence(ScopeId boundaryScope, CapabilityId capability, bool allContracts)
        {
            BoundaryScope = boundaryScope;
            Capability = capability;
            AllContracts = allContracts;
        }

        public override string ToString() =>
            "Boundary(" + BoundaryScope.ToString() + ", " + (AllContracts ? "*" : Capability.ToString()) + ")";
    }

    /// <summary>One exclusion that denied a candidate, with the rule that produced it (P-016).</summary>
    public readonly struct ExclusionEvidence
    {
        public readonly ExclusionRule Rule;
        public readonly bool FromScope;
        public readonly ScopeId SourceScope;

        public ExclusionEvidence(ExclusionRule rule, bool fromScope, ScopeId sourceScope)
        {
            Rule = rule;
            FromScope = fromScope;
            SourceScope = sourceScope;
        }

        public override string ToString() =>
            (FromScope ? "scopeExclusion(" : "targetExclusion(") + Rule.ToString() + ")";
    }

    /// <summary>
    /// One candidate evaluation record. <see cref="RecordKey"/> is the stable, internable identity of the
    /// (provider, rule, target, capability, slot) evaluation, so two runs of the same input produce equal keys.
    /// </summary>
    public sealed class CandidateDecision
    {
        public CandidateDecision(
            Id128 recordKey,
            ProviderInstallationId provider,
            RuleId rule,
            TargetId target,
            CapabilityId capability,
            uint outputSlot,
            ScopeId providerScope,
            int providerDepth,
            int stratum,
            CandidateStatus status,
            CandidateRejectionReason reason,
            DiagnosticCode diagnostic,
            ModeGateDecision modeGate,
            int priority,
            int rulePriority,
            IReadOnlyList<ExclusionEvidence>? exclusions,
            IReadOnlyList<BoundaryEvidence>? boundaries,
            IReadOnlyList<CapabilityId>? missingInputs,
            IReadOnlyList<Id128>? evidenceKeys)
        {
            RecordKey = recordKey;
            Provider = provider;
            Rule = rule;
            Target = target;
            Capability = capability;
            OutputSlot = outputSlot;
            ProviderScope = providerScope;
            ProviderDepth = providerDepth;
            Stratum = stratum;
            Status = status;
            Reason = reason;
            Diagnostic = diagnostic;
            ModeGate = modeGate;
            Priority = priority;
            RulePriority = rulePriority;
            Exclusions = ContractCollections.Freeze(exclusions);
            Boundaries = ContractCollections.Freeze(boundaries);
            MissingInputs = ContractCollections.Freeze(missingInputs);
            EvidenceKeys = ContractCollections.Freeze(evidenceKeys);
        }

        public Id128 RecordKey { get; }

        public ProviderInstallationId Provider { get; }

        public RuleId Rule { get; }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public uint OutputSlot { get; }

        public ScopeId ProviderScope { get; }

        /// <summary>Depth of the provider scope; deeper means nearer (P-018).</summary>
        public int ProviderDepth { get; }

        public int Stratum { get; }

        public CandidateStatus Status { get; }

        /// <summary><see cref="CandidateRejectionReason.None"/> when the candidate emitted.</summary>
        public CandidateRejectionReason Reason { get; }

        /// <summary>Stable diagnostic code for this decision; <see cref="DiagnosticCode.None"/> when it emitted.</summary>
        public DiagnosticCode Diagnostic { get; }

        public ModeGateDecision ModeGate { get; }

        public int Priority { get; }

        public int RulePriority { get; }

        public IReadOnlyList<ExclusionEvidence> Exclusions { get; }

        public IReadOnlyList<BoundaryEvidence> Boundaries { get; }

        public IReadOnlyList<CapabilityId> MissingInputs { get; }

        /// <summary>Interned descriptor/exclusion evidence keys for the shared explanation page (P-026).</summary>
        public IReadOnlyList<Id128> EvidenceKeys { get; }

        public bool Matched => Status == CandidateStatus.Emitted || Status == CandidateStatus.Shadowed;

        /// <summary>
        /// A copy with a corrected status. The engine records every eligible candidate as emitted and only after
        /// slot composition decides which of them actually supplied the effective value, so a losing candidate
        /// becomes <see cref="CandidateStatus.Shadowed"/> instead of claiming support (P-017, P-019).
        /// </summary>
        public CandidateDecision WithStatus(CandidateStatus status) =>
            new CandidateDecision(
                RecordKey,
                Provider,
                Rule,
                Target,
                Capability,
                OutputSlot,
                ProviderScope,
                ProviderDepth,
                Stratum,
                status,
                status == CandidateStatus.Shadowed ? CandidateRejectionReason.None : Reason,
                status == CandidateStatus.Shadowed ? DiagnosticCode.None : Diagnostic,
                ModeGate,
                Priority,
                RulePriority,
                Exclusions,
                Boundaries,
                MissingInputs,
                EvidenceKeys);

        public override string ToString() =>
            "Decision(" + Rule.ToString() + "@" + Provider.ToString() + " -> " + Target.ToString()
            + ", " + Status.ToString() + ")";
    }

    /// <summary>
    /// Complete explanation of one (target, capability) at one snapshot: matching and rejected rules, source
    /// scope path, descriptor evidence, boundaries and exclusions, mode gate, stratum, candidates, composition
    /// decisions, support ids and the resulting recipe hash (P-026).
    /// </summary>
    public sealed class DerivationExplanation
    {
        public DerivationExplanation(
            TargetId target,
            CapabilityId capability,
            SnapshotToken token,
            PropagationMode mode,
            int stratum,
            IReadOnlyList<ScopeId>? scopePath,
            IReadOnlyList<CandidateDecision>? decisions,
            IReadOnlyList<CapabilityContribution>? winners,
            IReadOnlyList<CapabilityContribution>? shadowed,
            IReadOnlyList<EffectiveSlot>? slots,
            IReadOnlyList<Id128>? descriptorEvidence,
            IReadOnlyList<ExclusionRule>? targetExclusions,
            IReadOnlyList<CapabilityId>? effectiveCapabilities,
            ContentHash recipeHash,
            ContentHash slotHash)
        {
            Target = target;
            Capability = capability;
            Token = token;
            Mode = mode;
            Stratum = stratum;
            ScopePath = ContractCollections.Freeze(scopePath);
            Decisions = ContractCollections.Freeze(decisions);
            Winners = ContractCollections.Freeze(winners);
            Shadowed = ContractCollections.Freeze(shadowed);
            Slots = ContractCollections.Freeze(slots);
            DescriptorEvidence = ContractCollections.Freeze(descriptorEvidence);
            TargetExclusions = ContractCollections.Freeze(targetExclusions);
            EffectiveCapabilities = ContractCollections.Freeze(effectiveCapabilities);
            RecipeHash = recipeHash;
            SlotHash = slotHash;
        }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public SnapshotToken Token { get; }

        public PropagationMode Mode { get; }

        public int Stratum { get; }

        /// <summary>Source scope path of the target's scope, nearest first (P-026).</summary>
        public IReadOnlyList<ScopeId> ScopePath { get; }

        /// <summary>Every candidate the engine evaluated for this pair, in canonical order.</summary>
        public IReadOnlyList<CandidateDecision> Decisions { get; }

        /// <summary>Active support ids that supply the effective value (P-017).</summary>
        public IReadOnlyList<CapabilityContribution> Winners { get; }

        public IReadOnlyList<CapabilityContribution> Shadowed { get; }

        public IReadOnlyList<EffectiveSlot> Slots { get; }

        /// <summary>Interning keys of the descriptor facts this decision was based on (P-015).</summary>
        public IReadOnlyList<Id128> DescriptorEvidence { get; }

        public IReadOnlyList<ExclusionRule> TargetExclusions { get; }

        public IReadOnlyList<CapabilityId> EffectiveCapabilities { get; }

        public ContentHash RecipeHash { get; }

        public ContentHash SlotHash { get; }

        /// <summary>
        /// The same explanation at another snapshot token. Carrying an unchanged target's provenance forward across
        /// a publication keeps every clause of the record (decisions, support, hash, evidence) and re-stamps only
        /// the token, because a token names the observation image, not the composition (P-006, P-026).
        /// </summary>
        public DerivationExplanation WithToken(SnapshotToken token) =>
            new DerivationExplanation(
                Target,
                Capability,
                token,
                Mode,
                Stratum,
                ScopePath,
                Decisions,
                Winners,
                Shadowed,
                Slots,
                DescriptorEvidence,
                TargetExclusions,
                EffectiveCapabilities,
                RecipeHash,
                SlotHash);

        public bool HasSupport => Winners.Count > 0;

        public override string ToString() =>
            "Explanation(" + Target.ToString() + "/" + Capability.ToString() + ", matches=" + Winners.Count
            + ", rejected=" + (Decisions.Count - Winners.Count - Shadowed.Count) + ")";
    }
}
