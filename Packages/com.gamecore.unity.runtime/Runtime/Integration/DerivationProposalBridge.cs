// GameCore.Unity.Runtime — W2 integration seam: the derivation result (GC-006) as an assembly proposal (GC-008).
//
// P-013/P-015 say a provider's contributions reach a target without any per-instance import; P-017 says a
// contribution's identity survives a payload reconfiguration and that "support from multiple contributions is a
// set of IDs, not a boolean owned by the last plugin"; P-018 ranks candidates and P-019 composes them. GC-006
// computes exactly that as an immutable effective assembly per target. GC-008 consumes a `CompositionProposal`
// (mounts with per-capability declarations) and publishes binding rows. This file is the one translation between
// the two, and it refuses rather than guesses at every point where the frozen shapes cannot express GC-006's
// result:
//
//   * the provider installation of a contribution must be a live installation of the same committed composition,
//     so a mount is always built from a real install record (never a synthesised provider);
//   * the value a binding row carries is a single 32-bit integer, encoded with the canonical big-endian integer
//     scalar convention of 05 section 6 — the same four bytes `FixturePayload.Int32` writes — so the reference
//     descriptors and this transfer cannot disagree on a value. A payload that is not exactly one such int32 is
//     refused: truncating or zero-filling it would publish a value nobody derived;
//   * a slot that composes several *values* (reducer-less `Additive`, `Ordered`) is refused explicitly, because
//     one binding row publishes one int32 and dropping members would publish an assembly nobody derived;
//   * a slot that several contributions *support* is no longer refused. GC-012 closed that gap: the composed
//     value the kernel folded (`EffectiveSlot.Values[0]`) becomes the row's value, and every supporter
//     (`EffectiveSlot.Support`) becomes a `CapabilitySupport` on that row, so an `Additive` slot with two or more
//     providers reaches a live world with both its composed value and its full provenance (P-017, P-019).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Planning;
using CompositionProposal = GameCore.Planning.CompositionProposal;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>
    /// The one value a derived output slot transfers into a binding row: a single 32-bit integer, in the canonical
    /// big-endian scalar encoding of 05 section 6. Reading is bounded and total, so a payload of another length or
    /// another shape is a refusal rather than a partially decoded number.
    /// </summary>
    public static class IntegrationSlotValues
    {
        /// <summary>Bytes one integer slot value occupies; the canonical fixed-width field of 05 section 6.</summary>
        public const int Int32PayloadBytes = 4;

        /// <summary>
        /// True when the payload is exactly one big-endian int32 scalar. A different length is not a slot value, and
        /// this never reads beyond the payload's own bytes.
        /// </summary>
        public static bool TryReadInt32(FrozenPayload? payload, out int value)
        {
            value = 0;
            if (payload == null)
            {
                return false;
            }

            IReadOnlyList<byte> bytes = payload.Bytes;
            if (bytes.Count != Int32PayloadBytes)
            {
                return false;
            }

            uint raw = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            value = unchecked((int)raw);
            return true;
        }

        /// <summary>Writes one integer slot value in the same canonical encoding, for fixtures and declarations.</summary>
        public static FrozenPayload WriteInt32(int value)
        {
            uint raw = unchecked((uint)value);
            return new FrozenPayload(new[]
            {
                (byte)(raw >> 24),
                (byte)(raw >> 16),
                (byte)(raw >> 8),
                (byte)raw,
            });
        }

        /// <summary>Canonical hex of a payload's bytes, or a marker when the payload is absent.</summary>
        public static string Describe(FrozenPayload? payload)
            => payload == null ? "<none>" : PayloadCodec.ToHex(payload);
    }

    /// <summary>How one composition proposal was derived from a derivation result.</summary>
    public enum DerivationProposalOutcome
    {
        /// <summary>The proposal was built; it carries at least one mount.</summary>
        Built = 0,

        /// <summary>Derivation produced no target assembly at all, so there is nothing to publish.</summary>
        NoAssemblies = 1,

        /// <summary>A contribution names a provider installation the committed composition does not hold.</summary>
        UnknownProvider = 2,

        /// <summary>A slot's composed value is not one canonical int32 scalar, so no binding row could carry it.</summary>
        UnsupportedSlotValue = 3,

        /// <summary>A derived capability has no stable identity, so no contribution key could be built (P-004).</summary>
        InvalidContribution = 4,

        /// <summary>
        /// A slot composes more than one value (reducer-less `Additive`, `Ordered`). The composed value of P-019 is
        /// then a set, and a binding row publishes one int32; the set is refused rather than truncated.
        /// </summary>
        MultiValueSlot = 5,
    }

    /// <summary>Result of translating one derivation result into an assembly proposal.</summary>
    public sealed class DerivationProposalReport
    {
        private DerivationProposalReport(
            DerivationProposalOutcome outcome,
            CompositionProposal? proposal,
            int mountCount,
            int capabilityCount,
            int targetCount,
            DiagnosticCode code,
            string detail)
        {
            Outcome = outcome;
            Proposal = proposal;
            MountCount = mountCount;
            CapabilityCount = capabilityCount;
            TargetCount = targetCount;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public DerivationProposalOutcome Outcome { get; }

        public bool Succeeded => Outcome == DerivationProposalOutcome.Built;

        public CompositionProposal? Proposal { get; }

        public int MountCount { get; }

        public int CapabilityCount { get; }

        public int TargetCount { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        internal static DerivationProposalReport Built(
            CompositionProposal proposal,
            int mountCount,
            int capabilityCount,
            int targetCount)
            => new DerivationProposalReport(
                DerivationProposalOutcome.Built,
                proposal,
                mountCount,
                capabilityCount,
                targetCount,
                DiagnosticCode.None,
                string.Empty);

        internal static DerivationProposalReport Refused(
            DerivationProposalOutcome outcome,
            DiagnosticCode code,
            string detail)
            => new DerivationProposalReport(outcome, null, 0, 0, 0, code, detail);

        public string Describe()
            => Succeeded
                ? "proposal(mounts=" + MountCount.ToString(CultureInfo.InvariantCulture)
                    + ", capabilities=" + CapabilityCount.ToString(CultureInfo.InvariantCulture)
                    + ", targets=" + TargetCount.ToString(CultureInfo.InvariantCulture) + ")"
                : "proposal refused(" + Outcome.ToString() + ", " + DiagnosticCodeText.Of(Code) + "): " + Detail;
    }

    /// <summary>
    /// Builds the immutable assembly proposal GC-008 publishes from the immutable derivation result GC-006 produced,
    /// over the same committed composition.
    /// </summary>
    public static class DerivedCompositionProposal
    {
        /// <summary>
        /// Translates one derivation result into a proposal declared against the world's published pair. The
        /// expected revision and base epoch belong to the *world's* current assembly, not to the composition
        /// publication being adopted: the planner rechecks them before any write (P-027, P-028).
        /// </summary>
        public static DerivationProposalReport Build(
            DerivationResult derivation,
            CompositionState committed,
            CompositionRevision expectedRevision,
            AssemblyEpoch baseEpoch,
            ContentHash inputHash,
            ContentHash catalogHash,
            OperationId operation)
        {
            if (derivation == null)
            {
                throw new ArgumentNullException(nameof(derivation));
            }

            if (committed == null)
            {
                throw new ArgumentNullException(nameof(committed));
            }

            if (!derivation.Accepted)
            {
                return DerivationProposalReport.Refused(
                    DerivationProposalOutcome.NoAssemblies,
                    derivation.DiagnosticCode,
                    "derivation was rejected (" + derivation.Rejection.ToString() + "); a rejected proposal publishes"
                    + " nothing and leaves the old assembly usable (P-028).");
            }

            if (derivation.Assemblies.Count == 0)
            {
                return DerivationProposalReport.Refused(
                    DerivationProposalOutcome.NoAssemblies,
                    DiagnosticCode.None,
                    "derivation produced no target assembly; there is no effective change to publish (P-028).");
            }

            var capabilitiesByProvider = new Dictionary<Id128, List<ProposedCapability>>();
            var providerOrder = new List<Id128>();
            var mounts = new List<ProposedMount>();
            int capabilityCount = 0;

            for (int a = 0; a < derivation.Assemblies.Count; a++)
            {
                TargetAssembly assembly = derivation.Assemblies[a];
                for (int s = 0; s < assembly.Slots.Count; s++)
                {
                    EffectiveSlot slot = assembly.Slots[s];
                    if (slot.IsEmpty)
                    {
                        // No contribution supports the slot any more; it is absent from the assembly and there is
                        // nothing to declare for it (P-017).
                        continue;
                    }

                    if (slot.Values.Count != 1)
                    {
                        // `Ordered` and reducer-less `Additive` compose a *set* of values, and one binding row holds
                        // one int32. A set is therefore refused explicitly rather than truncated to a member: the
                        // representation for a multi-value slot is a later task's, and dropping members here would
                        // publish an assembly nobody derived (P-017, P-019).
                        return DerivationProposalReport.Refused(
                            DerivationProposalOutcome.MultiValueSlot,
                            DiagnosticCode.UnsupportedVersion,
                            "target " + assembly.Target.ToString() + " slot " + slot.Capability.ToString()
                            + "#" + slot.Slot.ToString(CultureInfo.InvariantCulture) + " composes "
                            + slot.Values.Count.ToString(CultureInfo.InvariantCulture) + " values under the "
                            + slot.Policy.ToString() + " policy, and a binding row publishes exactly one canonical"
                            + " int32 scalar (P-019).");
                    }

                    if (!IntegrationSlotValues.TryReadInt32(slot.Values[0], out int composedValue))
                    {
                        return DerivationProposalReport.Refused(
                            DerivationProposalOutcome.UnsupportedSlotValue,
                            DiagnosticCode.UnsupportedVersion,
                            "target " + assembly.Target.ToString() + " slot " + slot.Capability.ToString()
                            + "#" + slot.Slot.ToString(CultureInfo.InvariantCulture)
                            + " has a composed value that is not exactly one canonical int32 scalar; a different"
                            + " payload is refused rather than truncated (P-017, P-019).");
                    }

                    // P-017: the support set is the *set of contributions* that produce this slot's effective value,
                    // so a slot with several supporters publishes several of them. Each supporter reports its own
                    // contribution value, which is what makes the composed value explainable (P-026).
                    //
                    // The (support, contribution) pairs are de-duplicated and canonically ordered here as *pairs*, not
                    // as two independently sorted lists: the declaration for a supporter must carry that supporter's
                    // own rule, schema and slot identity, so the two lists must never be indexed against each other.
                    var pairs = new List<KeyValuePair<CapabilitySupport, CapabilityContribution>>(slot.Support.Count);
                    for (int k = 0; k < slot.Support.Count; k++)
                    {
                        CapabilityContribution supporter = slot.Support[k];
                        if (supporter.OutputSlot > int.MaxValue)
                        {
                            return DerivationProposalReport.Refused(
                                DerivationProposalOutcome.InvalidContribution,
                                DiagnosticCode.MissingDependency,
                                "contribution " + supporter.Key.ToString() + " has no usable output slot (P-004).");
                        }

                        if (!IntegrationSlotValues.TryReadInt32(supporter.Payload, out int supporterValue))
                        {
                            return DerivationProposalReport.Refused(
                                DerivationProposalOutcome.UnsupportedSlotValue,
                                DiagnosticCode.UnsupportedVersion,
                                "contribution " + supporter.Key.ToString() + " carries a "
                                + (supporter.Payload != null ? supporter.Payload.Length : 0).ToString(CultureInfo.InvariantCulture)
                                + "-byte payload; a contribution value is exactly one canonical int32 scalar, and a"
                                + " different payload is refused rather than truncated (P-017, P-019).");
                        }

                        if (!committed.TryGetInstall(
                                new PluginInstanceId(supporter.Key.Provider.Value),
                                out InstallEntry? supporterInstall)
                            || supporterInstall == null)
                        {
                            return DerivationProposalReport.Refused(
                                DerivationProposalOutcome.UnknownProvider,
                                DiagnosticCode.StaleHandle,
                                "contribution " + supporter.Key.ToString() + " is supported by installation "
                                + supporter.Key.Provider.ToString() + ", which the committed composition does not"
                                + " hold; a mount is never built from a synthesised provider (P-009, P-044).");
                        }

                        var record = new CapabilitySupport(
                            supporter.Key.Provider,
                            supporterInstall.Record.Generation.Value,
                            supporter.Key.Rule,
                            supporterValue,
                            supporterInstall.Record.Priority);

                        bool duplicate = false;
                        for (int p = 0; p < pairs.Count; p++)
                        {
                            if (pairs[p].Key.HasSameIdentity(record))
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (!duplicate)
                        {
                            pairs.Add(new KeyValuePair<CapabilitySupport, CapabilityContribution>(record, supporter));
                        }
                    }

                    if (pairs.Count == 0)
                    {
                        return DerivationProposalReport.Refused(
                            DerivationProposalOutcome.InvalidContribution,
                            DiagnosticCode.MissingDependency,
                            "target " + assembly.Target.ToString() + " slot " + slot.Capability.ToString()
                            + "#" + slot.Slot.ToString(CultureInfo.InvariantCulture)
                            + " reports support without a single usable contribution identity (P-004, P-017).");
                    }

                    pairs.Sort(CompareSupportPairs);
                    var orderedSupports = new List<CapabilitySupport>(pairs.Count);
                    for (int p = 0; p < pairs.Count; p++)
                    {
                        orderedSupports.Add(pairs[p].Key);
                    }

                    IReadOnlyList<CapabilitySupport> frozenSupports = CapabilitySupport.Freeze(orderedSupports);

                    // One declaration per supporter, all carrying the slot's one composed value and its full support
                    // set. The planner ranks the declarations by P-018 like any other candidates and installs one
                    // row, so an `Additive` slot with N supporters no longer collapses to its top-ranked candidate.
                    for (int p = 0; p < pairs.Count; p++)
                    {
                        CapabilitySupport support = pairs[p].Key;
                        CapabilityContribution contributor = pairs[p].Value;
                        Id128 providerKey = support.Provider.Value;
                        if (!committed.TryGetInstall(new PluginInstanceId(providerKey), out InstallEntry? install)
                            || install == null)
                        {
                            return DerivationProposalReport.Refused(
                                DerivationProposalOutcome.UnknownProvider,
                                DiagnosticCode.StaleHandle,
                                "contribution " + contributor.Key.ToString() + " is supported by installation "
                                + support.Provider.ToString() + ", which the committed composition does not hold;"
                                + " a mount is never built from a synthesised provider (P-009, P-044).");
                        }

                        var declared = new ProposedCapability(
                            contributor.Rule,
                            new CapabilityRef(contributor.Capability, slot.Version),
                            contributor.Schema,
                            contributor.OutputSlot,
                            slot.Policy,
                            composedValue,
                            install.Record.Priority,
                            new List<DefinitionRef> { assembly.BaseRecipe },
                            new List<TargetId> { assembly.Target },
                            frozenSupports);

                        if (!capabilitiesByProvider.TryGetValue(providerKey, out List<ProposedCapability>? list) || list == null)
                        {
                            list = new List<ProposedCapability>();
                            capabilitiesByProvider.Add(providerKey, list);
                            providerOrder.Add(providerKey);
                        }

                        list.Add(declared);
                        capabilityCount++;
                    }
                }
            }

            if (providerOrder.Count == 0)
            {
                return DerivationProposalReport.Refused(
                    DerivationProposalOutcome.NoAssemblies,
                    DiagnosticCode.None,
                    "the derivation result carries no supported slot, so no contribution would be published (P-017).");
            }

            for (int i = 0; i < providerOrder.Count; i++)
            {
                Id128 providerKey = providerOrder[i];
                if (!committed.TryGetInstall(new PluginInstanceId(providerKey), out InstallEntry? install) || install == null)
                {
                    return DerivationProposalReport.Refused(
                        DerivationProposalOutcome.UnknownProvider,
                        DiagnosticCode.StaleHandle,
                        "installation " + providerKey.ToString() + " is not part of the committed composition (P-009).");
                }

                mounts.Add(new ProposedMount(
                    install.Record.Instance,
                    install.Record.PluginType,
                    new ProviderInstallationId(providerKey),
                    install.Record.Scope,
                    install.Record.Generation.Value,
                    capabilitiesByProvider[providerKey]));
            }

            var proposal = new CompositionProposal(
                operation,
                inputHash,
                expectedRevision,
                baseEpoch,
                catalogHash,
                committed.Mode,
                mounts,
                null);

            return DerivationProposalReport.Built(proposal, mounts.Count, capabilityCount, derivation.Assemblies.Count);
        }

        /// <summary>
        /// Canonical order of one slot's (support, contribution) pairs (P-008): the same order
        /// <see cref="CapabilitySupport.CompareCanonical"/> defines, applied to the pair so the two lists stay
        /// aligned. Reporting order only — the effective value was already composed by the slot's policy (P-019).
        /// </summary>
        private static int CompareSupportPairs(
            KeyValuePair<CapabilitySupport, CapabilityContribution> left,
            KeyValuePair<CapabilitySupport, CapabilityContribution> right)
            => CapabilitySupport.CompareCanonical(left.Key, right.Key);

        /// <summary>
        /// Canonical input hash of one derivation result for the plan record: the result hash derivation itself
        /// computed over the effective assembly (P-027 semantic inputs only, P-008 stable ordering).
        /// </summary>
        public static ContentHash InputHashOf(DerivationResult derivation)
        {
            if (derivation == null)
            {
                throw new ArgumentNullException(nameof(derivation));
            }

            var text = new StringBuilder();
            text.Append("derivation\n").Append(derivation.ResultHash.ToHex()).Append('\n');
            for (int i = 0; i < derivation.Assemblies.Count; i++)
            {
                TargetAssembly assembly = derivation.Assemblies[i];
                text.Append("target=").Append(assembly.Target.Value.ToString()).Append(';')
                    .Append(assembly.RecipeHash.ToHex()).Append('\n');
            }

            return PlanHashing.Of(text.ToString());
        }
    }
}
