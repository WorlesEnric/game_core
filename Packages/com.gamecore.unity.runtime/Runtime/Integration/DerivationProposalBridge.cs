// GameCore.Unity.Runtime — W2 integration seam: the derivation result (GC-006) as an assembly proposal (GC-008).
//
// P-013/P-015 say a provider's contributions reach a target without any per-instance import; P-017 says a
// contribution's identity survives a payload reconfiguration; P-018 ranks candidates and P-019 composes them. GC-006
// computes exactly that as an immutable effective assembly per target. GC-008 consumes a `CompositionProposal`
// (mounts with per-capability declarations) and publishes binding rows. This file is the one translation between
// the two, and it refuses rather than guesses at every point where the frozen shapes cannot express GC-006's result:
//
//   * the provider installation of a contribution must be a live installation of the same committed composition,
//     so a mount is always built from a real install record (never a synthesised provider);
//   * the value a binding row carries is a single 32-bit integer, encoded with the canonical big-endian integer
//     scalar convention of 05 section 6 — the same four bytes `FixturePayload.Int32` writes — so the reference
//     descriptors and this transfer cannot disagree on a value. A payload that is not exactly one such int32 is
//     refused: truncating or zero-filling it would publish a value nobody derived;
//   * an effective slot that more than one contribution supports has no representation in a row set keyed by
//     (target, capability, output slot), so it is refused with a detail naming the slot. Derivation's own suite
//     proves the multi-contribution policies; the exact transfer of a composed value to a binding row is the seam
//     that does not exist yet, and inventing a winner here would silently drop it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Planning;

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

        /// <summary>An effective slot has no single int32 value, or more than one supporter (see the file header).</summary>
        UnsupportedSlotValue = 3,

        /// <summary>A derived capability has no stable identity, so no contribution key could be built (P-004).</summary>
        InvalidContribution = 4,
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

                    if (slot.Support.Count != 1)
                    {
                        return DerivationProposalReport.Refused(
                            DerivationProposalOutcome.UnsupportedSlotValue,
                            DiagnosticCode.CapabilityConflict,
                            "target " + assembly.Target.ToString() + " slot " + slot.Capability.ToString()
                            + "#" + slot.Slot.ToString(CultureInfo.InvariantCulture) + " is supported by "
                            + slot.Support.Count.ToString(CultureInfo.InvariantCulture) + " contributions under the "
                            + slot.Policy.ToString() + " policy; a binding row holds one value per (capability, output"
                            + " slot), so a composed multi-contribution value has no representation here (P-019).");
                    }

                    CapabilityContribution contribution = slot.Support[0];
                    if (contribution.Capability.IsDefault || contribution.OutputSlot > int.MaxValue)
                    {
                        return DerivationProposalReport.Refused(
                            DerivationProposalOutcome.InvalidContribution,
                            DiagnosticCode.MissingDependency,
                            "contribution " + contribution.Key.ToString() + " has no usable output slot (P-004).");
                    }

                    if (!IntegrationSlotValues.TryReadInt32(contribution.Payload, out int value))
                    {
                        return DerivationProposalReport.Refused(
                            DerivationProposalOutcome.UnsupportedSlotValue,
                            DiagnosticCode.UnsupportedVersion,
                            "contribution " + contribution.Key.ToString() + " carries a "
                            + (contribution.Payload != null ? contribution.Payload.Length : 0).ToString(CultureInfo.InvariantCulture)
                            + "-byte payload; a binding row's value is exactly one canonical int32 scalar, and a"
                            + " different payload is refused rather than truncated (P-017, P-019).");
                    }

                    Id128 providerKey = contribution.Key.Provider.Value;
                    if (!committed.TryGetInstall(new PluginInstanceId(providerKey), out InstallEntry? install) || install == null)
                    {
                        return DerivationProposalReport.Refused(
                            DerivationProposalOutcome.UnknownProvider,
                            DiagnosticCode.StaleHandle,
                            "contribution " + contribution.Key.ToString() + " is supported by installation "
                            + contribution.Key.Provider.ToString() + ", which the committed composition does not hold;"
                            + " a mount is never built from a synthesised provider (P-009, P-044).");
                    }

                    var declared = new ProposedCapability(
                        contribution.Rule,
                        new CapabilityRef(contribution.Capability, slot.Version),
                        contribution.Schema,
                        contribution.OutputSlot,
                        slot.Policy,
                        value,
                        install.Record.Priority,
                        new List<DefinitionRef> { assembly.BaseRecipe });

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
