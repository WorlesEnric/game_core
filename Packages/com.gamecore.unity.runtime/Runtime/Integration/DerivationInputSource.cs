// GameCore.Unity.Runtime — W2 integration seam: the real committed composition (GC-004) as a derivation input.
//
// GC-006 never reads live composition state: it consumes one immutable, indexed snapshot (P-023 indexes are
// derived data, rebuildable, not a second authoritative store). GC-004 owns the committed composition. This file
// is the one place that translates between them, and it invents nothing:
//
//   * scopes come from the committed scope registry (parent, the capability isolation set of P-016, the exclusions
//     of P-016 and the Conservative grant data of P-013); service isolation is deliberately not copied, because
//     06's derivation input has no service domain (services never pass the propagation gate, P-013);
//   * installations come from the committed install records together with the manifest that declares their rules,
//     so an installation that is not `Active` contributes nothing (P-012);
//   * capability contracts are the union of the mounted manifests' declarations, and two different declarations of
//     one capability identity are refused here rather than resolved by an implicit winner, because 02 section 4 /
//     P-019 call a mixed policy for one slot a catalog error.
//
// A composition that cannot be expressed as a derivation input is reported with the protocol's own diagnostic
// code; this seam never substitutes a default, a synthetic scope or an empty contract table for a real failure.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>How one derivation input was produced from a committed composition.</summary>
    public enum DerivationInputOutcome
    {
        /// <summary>The snapshot was built; <see cref="DerivationInputReport.Snapshot"/> is usable.</summary>
        Built = 0,

        /// <summary>The committed composition has no scope at all, so it has no root to derive from (P-010).</summary>
        EmptyWorld = 1,

        /// <summary>Two installed manifests declare one capability identity differently (P-019 catalog error).</summary>
        ConflictingCapabilityContract = 2,

        /// <summary>The committed state could not be expressed as a derivation input; the detail names it.</summary>
        InvalidComposition = 3,
    }

    /// <summary>
    /// Result of building one derivation input. A refusal carries the code and a detail that names the declaration
    /// at fault, so a caller never has to guess whether a missing snapshot means "nothing to derive" or "invalid".
    /// </summary>
    public sealed class DerivationInputReport
    {
        private DerivationInputReport(
            DerivationInputOutcome outcome,
            DerivationSnapshot? snapshot,
            IReadOnlyList<CapabilityContract> contracts,
            DiagnosticCode code,
            string detail)
        {
            Outcome = outcome;
            Snapshot = snapshot;
            Contracts = contracts;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public DerivationInputOutcome Outcome { get; }

        /// <summary>True only when the snapshot exists; every other outcome refused without building one.</summary>
        public bool Succeeded => Outcome == DerivationInputOutcome.Built;

        /// <summary>The immutable input, or null when this report refused.</summary>
        public DerivationSnapshot? Snapshot { get; }

        /// <summary>The capability contracts the snapshot was built from, in canonical capability order.</summary>
        public IReadOnlyList<CapabilityContract> Contracts { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        internal static DerivationInputReport Built(DerivationSnapshot snapshot, IReadOnlyList<CapabilityContract> contracts)
            => new DerivationInputReport(DerivationInputOutcome.Built, snapshot, contracts, DiagnosticCode.None, string.Empty);

        internal static DerivationInputReport Refused(
            DerivationInputOutcome outcome,
            IReadOnlyList<CapabilityContract>? contracts,
            DiagnosticCode code,
            string detail)
            => new DerivationInputReport(
                outcome,
                null,
                contracts ?? Array.Empty<CapabilityContract>(),
                code,
                detail);

        public string Describe()
            => Outcome == DerivationInputOutcome.Built
                ? "derivation input built: scopes=" + (Snapshot != null ? Snapshot.Scopes.Count : 0).ToString(CultureInfo.InvariantCulture)
                    + ", installs=" + (Snapshot != null ? Snapshot.Installs.Count : 0).ToString(CultureInfo.InvariantCulture)
                    + ", targets=" + (Snapshot != null ? Snapshot.Targets.Count : 0).ToString(CultureInfo.InvariantCulture)
                    + ", contracts=" + Contracts.Count.ToString(CultureInfo.InvariantCulture)
                    + ", hash=" + (Snapshot != null ? Snapshot.SnapshotHash.ToHex() : "<none>")
                : "derivation input refused(" + DiagnosticCodeText.Of(Code) + "): " + Detail;
    }

    /// <summary>
    /// Translates one committed composition into the derivation input GC-006 consumes, with real target
    /// descriptors supplied by the caller (they belong to the live world, not to the control lane).
    /// </summary>
    public static class CompositionDerivationInput
    {
        /// <summary>
        /// Builds the snapshot for one committed composition.
        /// </summary>
        /// <param name="committed">The lane's committed composition; staged, unpublished edits are never visible.</param>
        /// <param name="targets">The live targets with their immutable descriptors (see <see cref="LiveTargetIndex"/>).</param>
        /// <param name="ruleKeys">
        /// Declared ordering keys of the catalog revision. The frozen <see cref="DerivationRule"/> carries no
        /// before/after fields, so the keys travel with the derivation input (GC-006 handoff, decision 6.2).
        /// </param>
        /// <param name="overrides">Versioned provider-selection overrides of this revision, or null for none.</param>
        public static DerivationInputReport Build(
            CompositionState committed,
            IReadOnlyList<DerivationTarget>? targets,
            IReadOnlyList<DerivationRuleKeys>? ruleKeys,
            IReadOnlyList<ProviderSelectionOverride>? overrides)
        {
            if (committed == null)
            {
                throw new ArgumentNullException(nameof(committed));
            }

            IReadOnlyList<ScopeRecord> scopeRecords = committed.Scopes.Scopes;
            if (scopeRecords.Count == 0)
            {
                return DerivationInputReport.Refused(
                    DerivationInputOutcome.EmptyWorld,
                    null,
                    DiagnosticCode.MissingDependency,
                    "the committed composition has no scope record, so there is no world root to derive from (P-010).");
            }

            List<CapabilityContract> contracts = new List<CapabilityContract>();
            Dictionary<Id128, string> contractTexts = new Dictionary<Id128, string>();
            List<DerivationScope> scopes = new List<DerivationScope>(scopeRecords.Count);
            for (int i = 0; i < scopeRecords.Count; i++)
            {
                ScopeRecord record = scopeRecords[i];
                scopes.Add(new DerivationScope(
                    record.Scope,
                    record.Parent,
                    record.CapabilityIsolation,
                    record.Exclusions,
                    record.Grants.Imports));
            }

            List<DerivationInstall> installs = new List<DerivationInstall>(committed.InstallCount);
            for (int i = 0; i < committed.InstallCount; i++)
            {
                InstallEntry entry = committed.Installs[i];
                installs.Add(new DerivationInstall(entry.Record, entry.State, entry.Manifest));

                IReadOnlyList<CapabilityContract> declared = entry.Manifest.CapabilityContracts;
                for (int c = 0; c < declared.Count; c++)
                {
                    CapabilityContract contract = declared[c];
                    string text = CanonicalContractText(contract);
                    Id128 capability = contract.Capability.Capability.Value;
                    if (contractTexts.TryGetValue(capability, out string? existing))
                    {
                        if (!string.Equals(existing, text, StringComparison.Ordinal))
                        {
                            return DerivationInputReport.Refused(
                                DerivationInputOutcome.ConflictingCapabilityContract,
                                contracts,
                                DiagnosticCode.CapabilityConflict,
                                "capability " + contract.Capability.ToString() + " is declared differently by two installed"
                                + " manifests; one capability identity has one declaration per catalog revision (P-019, 02 s4).");
                        }

                        continue;
                    }

                    contractTexts.Add(capability, text);
                    contracts.Add(contract);
                }
            }

            try
            {
                var snapshot = new DerivationSnapshot(
                    committed.World,
                    committed.Revision,
                    committed.Epoch,
                    committed.Mode,
                    scopes,
                    installs,
                    targets,
                    contracts,
                    ruleKeys,
                    overrides);

                return DerivationInputReport.Built(snapshot, snapshot.Contracts.Contracts);
            }
            catch (ArgumentException exception)
            {
                // The snapshot's own structural validation refused this composition: one rooted acyclic scope tree,
                // one declaration per rule identity, unique target/install identities. That is a refusal, never a
                // partially indexed input.
                return DerivationInputReport.Refused(
                    DerivationInputOutcome.InvalidComposition,
                    contracts,
                    DiagnosticCode.MissingDependency,
                    "the committed composition is not a valid derivation input: " + exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return DerivationInputReport.Refused(
                    DerivationInputOutcome.InvalidComposition,
                    contracts,
                    DiagnosticCode.MissingDependency,
                    "the committed composition is not a valid derivation input: " + exception.Message);
            }
        }

        /// <summary>
        /// Canonical text of one capability contract: identity, stratum, every output slot with its schema, its
        /// single composition policy and its reducer key, plus the incompatible-capability set. Two contracts with
        /// one identity compare equal exactly when they declare the same thing (P-008, P-019).
        /// </summary>
        public static string CanonicalContractText(CapabilityContract contract)
        {
            if (contract == null)
            {
                throw new ArgumentNullException(nameof(contract));
            }

            var text = new StringBuilder();
            text.Append("capability=").Append(contract.Capability.Capability.Value.ToString())
                .Append('/').Append(contract.Capability.Version.ToString(CultureInfo.InvariantCulture))
                .Append(";stratum=").Append(contract.Stratum.ToString(CultureInfo.InvariantCulture));

            for (int i = 0; i < contract.OutputSlots.Count; i++)
            {
                OutputSlotSchema slot = contract.OutputSlots[i];
                text.Append(";slot=").Append(slot.Slot.Value.ToString())
                    .Append('/').Append(slot.Schema.Id.Value.ToString())
                    .Append('@').Append(slot.Schema.Version.ToString(CultureInfo.InvariantCulture));
            }

            for (int i = 0; i < contract.SlotPolicies.Count; i++)
            {
                SlotCompositionPolicy policy = contract.SlotPolicies[i];
                text.Append(";policy=").Append(policy.Slot.Value.ToString())
                    .Append('/').Append(policy.Policy.ToString())
                    .Append('/').Append(policy.Reducer.RegistrationKey.ToString())
                    .Append('@').Append(policy.Reducer.KeyVersion.ToString(CultureInfo.InvariantCulture));
            }

            for (int i = 0; i < contract.IncompatibleCapabilities.Count; i++)
            {
                text.Append(";incompatible=").Append(contract.IncompatibleCapabilities[i].Value.ToString());
            }

            return text.ToString();
        }
    }
}
