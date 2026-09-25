// GameCore.Composition.Diagnostics — reconstructable compact provenance (GC-016).
//
// P-026: "Explain(TargetId, CapabilityId, SnapshotToken) returns matching and rejected rules, source scope path,
// descriptor evidence, exclusions/boundaries, mode gate, stratum, candidates, composition decisions, support ids,
// resulting recipe hash and state disposition. Records may be interned/compacted but remain reconstructable for the
// retained epoch."
//
// "Interned/compacted but reconstructable" is the whole design constraint, so the retained form is deliberately
// poorer than the producer's:
//
//   * `CapabilityProvenance` is one compact record per candidate decision: identity, kind, code, rule/provider,
//     slot, stratum, mode gate, provider depth, the two content hashes, and *keys* into the interned evidence table;
//   * `ProvenanceEntry` is the interned evidence a key resolves to: descriptor facts, exclusions, boundaries,
//     missing inputs, scope path members, support ids and slot identities;
//   * `ProvenanceReconstruction` is what a reader gets back: the full per-pair record set with winners, losers,
//     exclusions, boundaries, mode-gate denials and state dispositions, paged and bounded.
//
// Repeating evidence across 500 targets costs one entry, not 500 strings, and the reconstruction is a pure function
// of the retained compact form, which is exactly what the "reconstructable" tests pin.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition.Diagnostics
{
    /// <summary>How one retained candidate relates to the effective capability it was evaluated for.</summary>
    public enum ProvenanceKind
    {
        /// <summary>Provides the effective value, or one member of it: the support P-017 keeps as a set of ids.</summary>
        Winner = 0,

        /// <summary>Composed, but another candidate supplied the effective value (P-019).</summary>
        Loser = 1,

        /// <summary>Denied by a target or scope exclusion (P-016).</summary>
        Excluded = 2,

        /// <summary>Denied by a capability boundary on the propagation path (P-016).</summary>
        BoundaryBlocked = 3,

        /// <summary>Denied by the mode gate: conservative mode without an export-plus-import or a full opt-in (P-013).</summary>
        ModeDenied = 4,

        /// <summary>A declared lower-stratum input capability is absent on the target.</summary>
        InputMissing = 5,

        /// <summary>The rule's static predicate rejected the target.</summary>
        PredicateRejected = 6,

        /// <summary>The target advertises no selector the rule accepts (P-015).</summary>
        SelectorMismatch = 7,

        /// <summary>No rule selected the target at all, so it keeps its base recipe.</summary>
        Ineligible = 8,
    }

    /// <summary>The kind of interned evidence a provenance key resolves to.</summary>
    public enum ProvenanceEvidenceKind
    {
        /// <summary>A descriptor fact the target advertised (P-015).</summary>
        Descriptor = 0,

        /// <summary>A target or scope exclusion rule (P-016).</summary>
        Exclusion = 1,

        /// <summary>A capability boundary that denied the candidate (P-016).</summary>
        CapabilityBoundary = 2,

        /// <summary>A missing lower-stratum input capability.</summary>
        MissingInput = 3,

        /// <summary>A scope on the source path of the propagation (P-026 "source scope path").</summary>
        Scope = 4,

        /// <summary>A supporting contribution identity (P-017).</summary>
        Support = 5,

        /// <summary>A state slot the effective assembly carries (P-032/P-033 dispositions).</summary>
        StateSlot = 6,

        /// <summary>The mode-gate decision that produced this candidate's outcome (P-013).</summary>
        ModeGate = 7,
    }

    /// <summary>
    /// One compact retained provenance record: exactly one candidate evaluation, identified by the producer's own
    /// stable record key, with the evidence reduced to interned keys. It carries no string that a reader needs in
    /// order to reconstruct the decision, which is what makes the retained form reconstructable rather than lossy.
    /// </summary>
    public sealed class CapabilityProvenance
    {
        public CapabilityProvenance(
            Id128 recordKey,
            TargetId target,
            CapabilityId capability,
            ProvenanceKind kind,
            DiagnosticCode code,
            RuleId rule,
            ProviderInstallationId provider,
            uint outputSlot,
            int stratum,
            PropagationMode mode,
            int providerDepth,
            ContentHash recipeHash,
            ContentHash slotHash,
            IReadOnlyList<Id128>? evidenceKeys)
        {
            RecordKey = recordKey;
            Target = target;
            Capability = capability;
            Kind = kind;
            Code = code;
            Rule = rule;
            Provider = provider;
            OutputSlot = outputSlot;
            Stratum = stratum;
            Mode = mode;
            ProviderDepth = providerDepth;
            RecipeHash = recipeHash ?? ContentHash.Empty;
            SlotHash = slotHash ?? ContentHash.Empty;
            EvidenceKeys = ContractCollections.Freeze(evidenceKeys);
        }

        /// <summary>Stable identity of the evaluation, so two runs of the same input produce equal records.</summary>
        public Id128 RecordKey { get; }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public ProvenanceKind Kind { get; }

        /// <summary>Stable code of the denial; <see cref="DiagnosticCode.None"/> when the candidate was effective.</summary>
        public DiagnosticCode Code { get; }

        public RuleId Rule { get; }

        public ProviderInstallationId Provider { get; }

        public uint OutputSlot { get; }

        /// <summary>Rule stratum the candidate was evaluated in (P-021).</summary>
        public int Stratum { get; }

        /// <summary>Propagation mode in force when the candidate was evaluated (P-013).</summary>
        public PropagationMode Mode { get; }

        /// <summary>Depth of the provider scope; deeper means nearer (P-018).</summary>
        public int ProviderDepth { get; }

        public ContentHash RecipeHash { get; }

        public ContentHash SlotHash { get; }

        /// <summary>Keys into the interned evidence table; resolve with `ProvenanceStore.TryResolveEntry`.</summary>
        public IReadOnlyList<Id128> EvidenceKeys { get; }

        public override string ToString() =>
            "Provenance(" + Kind.ToString() + ", " + Rule.ToString() + "@" + Provider.ToString()
            + " -> " + Target.ToString() + ")";
    }

    /// <summary>
    /// One interned evidence entry. <see cref="Label"/> exists for a human reader only: it is never an input to
    /// ordering, identity or reconstruction, which is why a wording change cannot move a record (P-052 discipline).
    /// </summary>
    public sealed class ProvenanceEntry
    {
        public ProvenanceEntry(
            Id128 key,
            ProvenanceEvidenceKind kind,
            Id128 primary,
            Id128 secondary,
            ScopeId scope,
            CapabilityId capability,
            int number,
            string label)
        {
            Key = key;
            Kind = kind;
            Primary = primary;
            Secondary = secondary;
            Scope = scope;
            Capability = capability;
            Number = number;
            Label = label ?? string.Empty;
        }

        public Id128 Key { get; }

        public ProvenanceEvidenceKind Kind { get; }

        /// <summary>Main identity of the evidence: the descriptor, exclusion, boundary scope or support id.</summary>
        public Id128 Primary { get; }

        /// <summary>Secondary identity (rule, owner, target of an exclusion), or zero.</summary>
        public Id128 Secondary { get; }

        public ScopeId Scope { get; }

        public CapabilityId Capability { get; }

        /// <summary>Small numeric payload of the evidence (slot number, provider depth, stratum).</summary>
        public int Number { get; }

        /// <summary>Human-readable label; never used to order, key or reconstruct.</summary>
        public string Label { get; }

        public override string ToString() =>
            "Evidence(" + Kind.ToString() + ", " + Key.ToString() + ", n="
            + Number.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The reconstructed provenance of one (target, capability) pair: every retained record of the pair, grouped by
    /// kind, with its evidence resolved and its state dispositions attached. This is the answer O-25's probe asks
    /// for: every effective capability explains its winners, its losers and its exclusions.
    /// </summary>
    public sealed class ProvenanceReconstruction
    {
        internal ProvenanceReconstruction(
            ExplanationSource source,
            SnapshotToken token,
            OperationId operation,
            TargetId target,
            CapabilityId capability,
            IReadOnlyList<CapabilityProvenance>? page,
            IReadOnlyList<CapabilityProvenance>? retained,
            IReadOnlyList<ProvenanceEntry>? evidence,
            IReadOnlyList<StateDisposition>? dispositions,
            uint offset,
            uint maxRecords,
            ulong totalRecords,
            bool hasMore,
            ContentHash digest)
        {
            Source = source;
            Token = token;
            Operation = operation;
            Target = target;
            Capability = capability;
            Page = ContractCollections.Freeze(page);
            Records = ContractCollections.Freeze(retained);
            Evidence = ContractCollections.Freeze(evidence);
            StateDispositions = ContractCollections.Freeze(dispositions);
            Offset = offset;
            MaxRecords = maxRecords;
            TotalRecords = totalRecords;
            HasMore = hasMore;
            Digest = digest;
            Winners = OfKind(Page, ProvenanceKind.Winner);
            Losers = OfKind(Page, ProvenanceKind.Loser);
            Excluded = OfKind(Page, ProvenanceKind.Excluded);
            BoundaryBlocked = OfKind(Page, ProvenanceKind.BoundaryBlocked);
            ModeDenied = OfKind(Page, ProvenanceKind.ModeDenied);
            InputMissing = OfKind(Page, ProvenanceKind.InputMissing);
            PredicateRejected = OfKind(Page, ProvenanceKind.PredicateRejected);
            SelectorMismatched = OfKind(Page, ProvenanceKind.SelectorMismatch);
            Ineligible = OfKind(Page, ProvenanceKind.Ineligible);
        }

        /// <summary>Published composition or staged plan: the two are never presented as the same thing (05 s5).</summary>
        public ExplanationSource Source { get; }

        /// <summary>The published token this reconstruction belongs to; default for a staged reconstruction.</summary>
        public SnapshotToken Token { get; }

        /// <summary>The still-unpublished operation this reconstruction belongs to; default for a published one.</summary>
        public OperationId Operation { get; }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public bool IsStaged => Source == ExplanationSource.StagedPlan;

        /// <summary>The page slice of the pair's records, canonically ordered.</summary>
        public IReadOnlyList<CapabilityProvenance> Page { get; }

        /// <summary>Every retained record of the pair, regardless of the page window.</summary>
        public IReadOnlyList<CapabilityProvenance> Records { get; }

        /// <summary>Evidence resolved for the page slice, in first-use order.</summary>
        public IReadOnlyList<ProvenanceEntry> Evidence { get; }

        public IReadOnlyList<StateDisposition> StateDispositions { get; }

        public IReadOnlyList<CapabilityProvenance> Winners { get; }

        public IReadOnlyList<CapabilityProvenance> Losers { get; }

        public IReadOnlyList<CapabilityProvenance> Excluded { get; }

        public IReadOnlyList<CapabilityProvenance> BoundaryBlocked { get; }

        public IReadOnlyList<CapabilityProvenance> ModeDenied { get; }

        public IReadOnlyList<CapabilityProvenance> InputMissing { get; }

        public IReadOnlyList<CapabilityProvenance> PredicateRejected { get; }

        public IReadOnlyList<CapabilityProvenance> SelectorMismatched { get; }

        public IReadOnlyList<CapabilityProvenance> Ineligible { get; }

        public uint Offset { get; }

        public uint MaxRecords { get; }

        public ulong TotalRecords { get; }

        public bool HasMore { get; }

        /// <summary>Offset of the next page; equal to <see cref="Offset"/> when there is no next page.</summary>
        public uint NextOffset => Offset + (uint)Page.Count;

        /// <summary>Canonical digest of the *whole* record set, so it does not depend on the page window.</summary>
        public ContentHash Digest { get; }

        /// <summary>True when the pair has any effective support at all (P-017); false means it is not effective.</summary>
        public bool HasSupport
        {
            get
            {
                for (int i = 0; i < Records.Count; i++)
                {
                    if (Records[i].Kind == ProvenanceKind.Winner)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public ProvenanceEntry? EvidenceOf(Id128 key)
        {
            for (int i = 0; i < Evidence.Count; i++)
            {
                if (Evidence[i].Key.Equals(key))
                {
                    return Evidence[i];
                }
            }

            return null;
        }

        public override string ToString() =>
            "ProvenanceReconstruction(" + Source.ToString() + ", " + Target.ToString() + ", records="
            + Records.Count.ToString(CultureInfo.InvariantCulture) + ", page="
            + Page.Count.ToString(CultureInfo.InvariantCulture) + ")";

        private static IReadOnlyList<CapabilityProvenance> OfKind(
            IReadOnlyList<CapabilityProvenance> records, ProvenanceKind kind)
        {
            var selected = new List<CapabilityProvenance>();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Kind == kind)
                {
                    selected.Add(records[i]);
                }
            }

            return selected;
        }
    }

    /// <summary>
    /// Canonical digest of a compact record set. It is computed from the typed fields of the records and their
    /// evidence keys, in canonical order, so a caller can compare two reconstructions without string comparison and
    /// without depending on how the producer enumerated its candidates (P-008, TEST-022).
    /// </summary>
    public static class ProvenanceDigest
    {
        public static ContentHash Of(IReadOnlyList<CapabilityProvenance>? records)
        {
            var bytes = new List<byte>(256);
            int count = records == null ? 0 : records.Count;
            DiagnosticDocument.AppendUInt32(bytes, (uint)count);
            for (int i = 0; i < count; i++)
            {
                CapabilityProvenance record = records![i];
                DiagnosticDocument.AppendId128(bytes, record.RecordKey);
                DiagnosticDocument.AppendId128(bytes, record.Target.Value);
                DiagnosticDocument.AppendId128(bytes, record.Capability.Value);
                DiagnosticDocument.AppendUInt32(bytes, (uint)record.Kind);
                DiagnosticDocument.AppendUInt32(bytes, (uint)record.Code);
                DiagnosticDocument.AppendId128(bytes, record.Rule.Value);
                DiagnosticDocument.AppendId128(bytes, record.Provider.Value);
                DiagnosticDocument.AppendUInt32(bytes, record.OutputSlot);
                DiagnosticDocument.AppendUInt32(bytes, unchecked((uint)record.Stratum));
                DiagnosticDocument.AppendUInt32(bytes, (uint)record.Mode);
                DiagnosticDocument.AppendUInt32(bytes, unchecked((uint)record.ProviderDepth));
                DiagnosticDocument.AppendHash(bytes, record.RecipeHash);
                DiagnosticDocument.AppendHash(bytes, record.SlotHash);
                DiagnosticDocument.AppendUInt32(bytes, (uint)record.EvidenceKeys.Count);
                for (int k = 0; k < record.EvidenceKeys.Count; k++)
                {
                    DiagnosticDocument.AppendId128(bytes, record.EvidenceKeys[k]);
                }
            }

            return ContentHash.Compute(bytes.ToArray());
        }
    }
}
