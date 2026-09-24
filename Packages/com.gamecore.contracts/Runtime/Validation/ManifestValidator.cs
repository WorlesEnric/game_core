// GameCore.Contracts - manifest and declaration validation (GC-003). Normative sources:
// docs/game-core/00-core-protocols.md P-009 (a catalog rejects duplicate ids, unknown schema/contract
// versions, missing precompiled factories, unsupported required protocol features, undeclared access and
// missing policies before activation), P-012/P-021/P-032/P-039/P-040/P-043/P-055 and
// docs/game-core/05-contracts-and-data-model.md s3/s4.
//
// Diagnostic-code mapping used by this validator (no new protocol literal is introduced; every code is an
// existing 00 s9 literal):
//   MissingDependency   unknown schema, unknown generated key, missing policy key, missing stage/buffer/
//                       resource dependency, a default zero identity, an undeclared access set
//   UnsupportedVersion  protocol major/minor mismatch, unknown required feature, unaccepted schema version
//   OwnershipConflict   duplicate stable id, one identity declared twice, an ambiguous write conflict with no
//                       ordering edge, a default zero access set
//   CapabilityConflict  capability-contract slot/policy inconsistency, output stratum disagreeing with the
//                       declared capability contract
//   ServiceConflict     one manifest exporting the same contract twice
//   Ineligible          stratum outside 0..31, a rule reading a same-or-higher stratum, LocalOnly plus export
//   Cycle               stage- or system-level required-edge cycle, resource dependency cycle
//   AmbiguousOrder      overlapping write access with neither a directed edge nor disjoint partitions
//   BudgetExceeded      a declared bound that cannot be positive (buffer capacity)
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Outcome of validating one manifest batch against a catalog description.</summary>
    public sealed class ManifestValidationReport
    {
        internal ManifestValidationReport(bool isValid, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyList<PluginTypeId> validatedPluginTypes)
        {
            IsValid = isValid;
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
            ValidatedPluginTypes = validatedPluginTypes ?? Array.Empty<PluginTypeId>();
        }

        /// <summary>True only when every manifest in the batch was accepted with no diagnostic.</summary>
        public bool IsValid { get; }

        /// <summary>Structured rejections in deterministic order; empty when accepted.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>Plugin type ids that were validated, in canonical identity order.</summary>
        public IReadOnlyList<PluginTypeId> ValidatedPluginTypes { get; }

        /// <summary>One-line description for diagnostics and build logs; never an empty string.</summary>
        public string Describe()
        {
            if (IsValid)
            {
                return "accepted " + ValidatedPluginTypes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                       " manifest(s)";
            }

            if (Diagnostics.Count == 0)
            {
                return "rejected without a diagnostic";
            }

            string[] parts = new string[Diagnostics.Count];
            for (int i = 0; i < Diagnostics.Count; i++)
            {
                parts[i] = Diagnostics[i].CodeText + "(" + Diagnostics[i].Summary + ")";
            }

            return string.Join("; ", parts);
        }
    }

    /// <summary>
    /// Validates versioned declarative manifests against a catalog before activation (P-009). Every rejection
    /// is a value with a stable 00 s9 code and a named identity; nothing is written, mounted or allocated.
    /// </summary>
    public static class ManifestValidator
    {
        private const int MaxStratum = 31;

        /// <summary>
        /// Validates a batch of manifests. The batch is validated as one catalog contribution set, so duplicate
        /// stable ids across manifests, cross-manifest capability/rule dependencies, stage coalescing, buffer
        /// endpoints and resource dependencies are all checked (P-009–P-012, P-021, P-039–P-043).
        /// </summary>
        /// <param name="manifests">Manifests to validate; an empty batch is legal and yields an accepted report.</param>
        /// <param name="catalog">Catalog the generated keys and schemas are resolved against.</param>
        /// <param name="supportedFeatureIds">Protocol features this build supports; a manifest requiring an id outside this set rejects (P-055).</param>
        /// <param name="protocol">Protocol version the build implements (V1 is 1.0).</param>
        public static ManifestValidationReport Validate(
            IReadOnlyList<PluginManifest>? manifests,
            ICatalog catalog,
            IReadOnlyList<Id128>? supportedFeatureIds,
            ProtocolVersion protocol)
        {
            if (manifests == null)
            {
                throw new ArgumentNullException(nameof(manifests));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            PluginManifest[] sorted = SortManifests(manifests, diagnostics);
            HashSet<Id128> features = ToFeatureSet(supportedFeatureIds);
            Index index = Index.Build(sorted);
            HashSet<Id128> seenTypeIds = new HashSet<Id128>();

            for (int i = 0; i < sorted.Length; i++)
            {
                PluginManifest manifest = sorted[i];
                ValidateManifest(manifest, catalog, features, protocol, index, seenTypeIds, diagnostics);
            }

            ValidateStageGraphs(sorted, diagnostics);
            ValidateResourceGraphs(sorted, diagnostics);

            if (diagnostics.Count != 0)
            {
                return new ManifestValidationReport(false, diagnostics, Array.Empty<PluginTypeId>());
            }

            PluginTypeId[] accepted = new PluginTypeId[sorted.Length];
            for (int i = 0; i < sorted.Length; i++)
            {
                accepted[i] = sorted[i].PluginTypeId;
            }

            return new ManifestValidationReport(true, Array.Empty<Diagnostic>(), accepted);
        }

        private static PluginManifest[] SortManifests(IReadOnlyList<PluginManifest> manifests, List<Diagnostic> diagnostics)
        {
            List<PluginManifest> copy = new List<PluginManifest>(manifests.Count);
            for (int i = 0; i < manifests.Count; i++)
            {
                PluginManifest manifest = manifests[i];
                if (manifest == null)
                {
                    throw new ArgumentException("A manifest batch cannot contain null entries.", nameof(manifests));
                }

                copy.Add(manifest);
            }

            // Canonical identity order makes the diagnostic list independent of declaration order (P-008).
            copy.Sort(CompareManifests);
            return copy.ToArray();
        }

        private static int CompareManifests(PluginManifest left, PluginManifest right) =>
            left.PluginTypeId.Value.CompareTo(right.PluginTypeId.Value);

        private static void ValidateManifest(
            PluginManifest manifest,
            ICatalog catalog,
            HashSet<Id128> features,
            ProtocolVersion protocol,
            Index index,
            HashSet<Id128> seenTypeIds,
            List<Diagnostic> diagnostics)
        {
            string owner = "plugin " + manifest.PluginTypeId;

            if (manifest.PluginTypeId.Value.IsDefault)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.MissingDependency, "a default zero plugin type id is not a catalog identity", default(Id128)));
                return;
            }

            if (!seenTypeIds.Add(manifest.PluginTypeId.Value))
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.OwnershipConflict,
                    "duplicate plugin type id across the validated manifest batch",
                    manifest.PluginTypeId.Value));
            }

            if (string.IsNullOrEmpty(manifest.PackageVersion))
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.MissingDependency,
                    owner + " declares no exact package version (P-009)",
                    manifest.PluginTypeId.Value));
            }

            if (manifest.PackageContentHash.IsEmpty)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.MissingDependency,
                    owner + " declares no package content hash (P-009)",
                    manifest.PluginTypeId.Value));
            }

            ValidateProtocolRange(manifest, protocol, owner, diagnostics);
            ValidateFeatures(manifest, features, owner, diagnostics);
            ResolveKey(manifest.FactoryKey, catalog, owner + " precompiled factory", manifest.PluginTypeId.Value, diagnostics);
            ResolveSchema(manifest.ConfigSchema, catalog, owner + " config schema", manifest.PluginTypeId.Value, diagnostics);
            ValidateServiceExports(manifest, catalog, owner, diagnostics);
            ValidateServiceDependencies(manifest, catalog, owner, diagnostics);
            ValidateCapabilityContracts(manifest, catalog, owner, diagnostics);
            ValidateRules(manifest, catalog, index, owner, diagnostics);
            ValidateTargetDescriptors(manifest, catalog, index, owner, diagnostics);
            ValidateStateSlots(manifest, catalog, owner, diagnostics);
            ValidateStages(manifest, catalog, index, owner, diagnostics);
            ValidateBuffers(manifest, catalog, index, owner, diagnostics);
            ValidateResources(manifest, catalog, index, owner, diagnostics);
        }

        private static void ValidateProtocolRange(PluginManifest manifest, ProtocolVersion protocol, string owner, List<Diagnostic> diagnostics)
        {
            SupportedProtocolRange range = manifest.ProtocolRange;
            if (range.Major != protocol.Major || protocol.Minor < range.MinMinor || protocol.Minor > range.MaxMinor)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.UnsupportedVersion,
                    owner + " supports protocol " + range + " but this build implements " + protocol +
                    "; a mismatch never falls back to another mode (P-055)",
                    manifest.PluginTypeId.Value));
            }
        }

        private static void ValidateFeatures(PluginManifest manifest, HashSet<Id128> features, string owner, List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < manifest.RequiredFeatureIds.Count; i++)
            {
                Id128 featureId = manifest.RequiredFeatureIds[i];
                if (featureId.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        owner + " requires a default zero feature id, which is not a declared protocol feature",
                        manifest.PluginTypeId.Value));
                    continue;
                }

                if (!features.Contains(featureId))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.UnsupportedVersion,
                        owner + " requires unsupported protocol feature " + featureId + " (P-055)",
                        featureId));
                }
            }
        }

        private static void ValidateServiceExports(PluginManifest manifest, ICatalog catalog, string owner, List<Diagnostic> diagnostics)
        {
            ServiceExport[] exports = new ServiceExport[manifest.ServiceExports.Count];
            for (int i = 0; i < exports.Length; i++)
            {
                exports[i] = manifest.ServiceExports[i];
            }

            Array.Sort(exports, CompareServiceExports);

            HashSet<ContractRef> seen = new HashSet<ContractRef>();
            for (int i = 0; i < exports.Length; i++)
            {
                ServiceExport export = exports[i];
                ResolveKey(export.Factory, catalog, owner + " service export " + export.Contract, manifest.PluginTypeId.Value, diagnostics);
                if (!seen.Add(export.Contract))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.ServiceConflict,
                        owner + " exports service contract " + export.Contract +
                        " more than once; service resolution is independent of registration order (P-011)",
                        manifest.PluginTypeId.Value));
                }
            }
        }

        private static int CompareServiceExports(ServiceExport left, ServiceExport right)
        {
            int byContract = left.Contract.ContractId.Value.CompareTo(right.Contract.ContractId.Value);
            return byContract != 0 ? byContract : left.Contract.Version.CompareTo(right.Contract.Version);
        }

        private static void ValidateServiceDependencies(PluginManifest manifest, ICatalog catalog, string owner, List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < manifest.ServiceDependencies.Count; i++)
            {
                ServiceDependency dependency = manifest.ServiceDependencies[i];
                if (dependency.HasFallback)
                {
                    ResolveKey(
                        dependency.FallbackFactory,
                        catalog,
                        owner + " optional fallback for " + dependency.Contract,
                        manifest.PluginTypeId.Value,
                        diagnostics);
                }

                if (dependency.SupportedVersions.MinVersion > dependency.SupportedVersions.MaxVersion)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.UnsupportedVersion,
                        owner + " declares an inverted version range " + dependency.SupportedVersions +
                        " for contract " + dependency.Contract,
                        manifest.PluginTypeId.Value));
                }
            }
        }

        private static void ValidateCapabilityContracts(PluginManifest manifest, ICatalog catalog, string owner, List<Diagnostic> diagnostics)
        {
            CapabilityContract[] contracts = new CapabilityContract[manifest.CapabilityContracts.Count];
            for (int i = 0; i < contracts.Length; i++)
            {
                contracts[i] = manifest.CapabilityContracts[i];
            }

            Array.Sort(contracts, CompareCapabilityContracts);
            HashSet<Id128> seen = new HashSet<Id128>();

            for (int i = 0; i < contracts.Length; i++)
            {
                CapabilityContract contract = contracts[i];
                string contractName = owner + " capability " + contract.Capability;
                Id128 capabilityId = contract.Capability.Capability.Value;

                if (capabilityId.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero capability id is not a catalog identity",
                        manifest.PluginTypeId.Value));
                    continue;
                }

                if (!seen.Add(capabilityId))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.CapabilityConflict,
                        "duplicate capability contract id " + capabilityId + " in one or more manifests (P-019)",
                        capabilityId));
                    continue;
                }

                if (contract.Stratum < 0 || contract.Stratum > MaxStratum)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.Ineligible,
                        contractName + " declares stratum " + contract.Stratum.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "; capability strata are 0..31 (P-021)",
                        capabilityId));
                }

                HashSet<Id128> declaredSlots = new HashSet<Id128>();
                for (int s = 0; s < contract.OutputSlots.Count; s++)
                {
                    OutputSlotSchema slot = contract.OutputSlots[s];
                    if (!declaredSlots.Add(slot.Slot.Value))
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.OwnershipConflict,
                            contractName + " declares output slot " + slot.Slot + " twice",
                            slot.Slot.Value));
                    }

                    ResolveSchema(slot.Schema, catalog, contractName + " output slot " + slot.Slot, capabilityId, diagnostics);
                }

                HashSet<Id128> policedSlots = new HashSet<Id128>();
                for (int p = 0; p < contract.SlotPolicies.Count; p++)
                {
                    SlotCompositionPolicy policy = contract.SlotPolicies[p];
                    if (!policedSlots.Add(policy.Slot.Value))
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.CapabilityConflict,
                            contractName + " declares two composition policies for slot " + policy.Slot +
                            "; every output slot chooses exactly one policy (P-019)",
                            policy.Slot.Value));
                        continue;
                    }

                    if (!declaredSlots.Contains(policy.Slot.Value))
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.CapabilityConflict,
                            contractName + " declares a composition policy for undeclared output slot " + policy.Slot,
                            policy.Slot.Value));
                    }

                    ResolveKey(policy.Reducer, catalog, contractName + " reducer for slot " + policy.Slot, capabilityId, diagnostics);
                }

                foreach (Id128 slot in declaredSlots)
                {
                    if (!policedSlots.Contains(slot))
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.CapabilityConflict,
                            contractName + " declares output slot " + slot +
                            " without a composition policy; mixed or missing policies are catalog errors (P-019)",
                            slot));
                    }
                }
            }
        }

        private static int CompareCapabilityContracts(CapabilityContract left, CapabilityContract right)
        {
            int byId = left.Capability.Capability.Value.CompareTo(right.Capability.Capability.Value);
            return byId != 0 ? byId : left.Capability.Version.CompareTo(right.Capability.Version);
        }

        private static void ValidateRules(
            PluginManifest manifest,
            ICatalog catalog,
            Index index,
            string owner,
            List<Diagnostic> diagnostics)
        {
            DerivationRule[] rules = new DerivationRule[manifest.DerivationRules.Count];
            for (int i = 0; i < rules.Length; i++)
            {
                rules[i] = manifest.DerivationRules[i];
            }

            Array.Sort(rules, CompareRules);
            HashSet<Id128> seen = new HashSet<Id128>();

            for (int i = 0; i < rules.Length; i++)
            {
                DerivationRule rule = rules[i];
                string ruleName = owner + " derivation rule " + rule.RuleId;

                if (rule.RuleId.Value.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero rule id is not a catalog identity",
                        manifest.PluginTypeId.Value));
                    continue;
                }

                if (!seen.Add(rule.RuleId.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate derivation rule id " + rule.RuleId.Value,
                        rule.RuleId.Value));
                    continue;
                }

                CapabilityEntry? declared;
                if (!index.Capabilities.TryGetValue(rule.OutputCapability.Capability.Value, out declared))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        ruleName + " outputs " + rule.OutputCapability +
                        " but no manifest in this catalog declares that capability contract (P-021)",
                        rule.OutputCapability.Capability.Value));
                }
                else
                {
                    if (declared.Version != rule.OutputCapability.Version)
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.CapabilityConflict,
                            ruleName + " outputs capability version " + rule.OutputCapability.Version +
                            " but the declared contract is version " + declared.Version,
                            rule.OutputCapability.Capability.Value));
                    }

                    if (declared.Stratum != rule.OutputStratum)
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.CapabilityConflict,
                            ruleName + " occupies stratum " + rule.OutputStratum +
                            " but its capability contract declares stratum " + declared.Stratum,
                            rule.OutputCapability.Capability.Value));
                    }
                }

                if (rule.OutputStratum < 0 || rule.OutputStratum > MaxStratum)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.Ineligible,
                        ruleName + " occupies stratum " + rule.OutputStratum + "; capability strata are 0..31 (P-021)",
                        rule.RuleId.Value));
                }

                if (rule.MaxOutputSlots == 0U)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        ruleName + " declares a fixed output-slot bound of zero, which emits nothing (P-021)",
                        rule.RuleId.Value));
                }

                if (rule.Reach == PropagationReach.LocalOnly && rule.ExportToDescendants)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.Ineligible,
                        ruleName + " is LocalOnly and cannot be exported to descendants (P-013)",
                        rule.RuleId.Value));
                }

                for (int s = 0; s < rule.SelectorContracts.Count; s++)
                {
                    ResolveSchema(rule.SelectorContracts[s], catalog, ruleName + " selector", rule.RuleId.Value, diagnostics);
                }

                if (rule.StaticPredicate.RegistrationKey.IsDefault)
                {
                    // No predicate is legal: the rule then matches every eligible target of its selector contracts.
                }
                else
                {
                    ResolveKey(rule.StaticPredicate, catalog, ruleName + " static predicate", rule.RuleId.Value, diagnostics);
                }

                for (int inputIndex = 0; inputIndex < rule.InputCapabilities.Count; inputIndex++)
                {
                    CapabilityRef input = rule.InputCapabilities[inputIndex];
                    if (!index.Capabilities.TryGetValue(input.Capability.Value, out CapabilityEntry? inputEntry))
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.MissingDependency,
                            ruleName + " reads " + input + " but no manifest in this catalog declares that capability",
                            input.Capability.Value));
                        continue;
                    }

                    if (inputEntry.Stratum >= rule.OutputStratum)
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.Ineligible,
                            ruleName + " reads stratum " + inputEntry.Stratum +
                            " from its output stratum " + rule.OutputStratum +
                            "; a rule reads strictly lower strata only (P-021)",
                            input.Capability.Value));
                    }
                }
            }
        }

        private static int CompareRules(DerivationRule left, DerivationRule right) =>
            left.RuleId.Value.CompareTo(right.RuleId.Value);

        private static void ValidateTargetDescriptors(
            PluginManifest manifest,
            ICatalog catalog,
            Index index,
            string owner,
            List<Diagnostic> diagnostics)
        {
            TargetDescriptor[] descriptors = new TargetDescriptor[manifest.TargetDescriptors.Count];
            for (int i = 0; i < descriptors.Length; i++)
            {
                descriptors[i] = manifest.TargetDescriptors[i];
            }

            Array.Sort(descriptors, CompareDescriptors);
            HashSet<Id128> seen = new HashSet<Id128>();

            for (int i = 0; i < descriptors.Length; i++)
            {
                TargetDescriptor descriptor = descriptors[i];
                string descriptorName = owner + " target descriptor " + descriptor.Recipe;

                if (descriptor.Recipe.Id.Value.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero recipe definition id is not a catalog identity",
                        manifest.PluginTypeId.Value));
                    continue;
                }

                if (!seen.Add(descriptor.Recipe.Id.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate recipe definition id " + descriptor.Recipe.Id.Value,
                        descriptor.Recipe.Id.Value));
                    continue;
                }

                for (int s = 0; s < descriptor.SupportedSchemas.Count; s++)
                {
                    ResolveSchema(descriptor.SupportedSchemas[s], catalog, descriptorName + " supported schema", descriptor.Recipe.Id.Value, diagnostics);
                }

                for (int c = 0; c < descriptor.SupportedCapabilities.Count; c++)
                {
                    CapabilityRef capability = descriptor.SupportedCapabilities[c];
                    if (!index.Capabilities.ContainsKey(capability.Capability.Value))
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.MissingDependency,
                            descriptorName + " advertises " + capability +
                            " but no manifest in this catalog declares that capability contract",
                            capability.Capability.Value));
                    }
                }

                for (int p = 0; p < descriptor.LocalPatches.Count; p++)
                {
                    if (descriptor.LocalPatches[p].Id.Value.IsDefault)
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.MissingDependency,
                            descriptorName + " declares a default zero local patch definition id",
                            descriptor.Recipe.Id.Value));
                    }
                }
            }
        }

        private static int CompareDescriptors(TargetDescriptor left, TargetDescriptor right)
        {
            int byId = left.Recipe.Id.Value.CompareTo(right.Recipe.Id.Value);
            return byId != 0 ? byId : left.Recipe.SchemaVersion.CompareTo(right.Recipe.SchemaVersion);
        }

        private static void ValidateStateSlots(
            PluginManifest manifest,
            ICatalog catalog,
            string owner,
            List<Diagnostic> diagnostics)
        {
            StateSlotSpec[] slots = new StateSlotSpec[manifest.StateSlots.Count];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = manifest.StateSlots[i];
            }

            Array.Sort(slots, CompareStateSlots);
            HashSet<Id128> seen = new HashSet<Id128>();

            for (int i = 0; i < slots.Length; i++)
            {
                StateSlotSpec slot = slots[i];
                string slotName = owner + " state slot " + slot.SlotId;

                if (slot.SlotId.Value.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero state-slot id is not a catalog identity",
                        manifest.PluginTypeId.Value));
                    continue;
                }

                if (!seen.Add(slot.SlotId.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate state-slot id " + slot.SlotId.Value,
                        slot.SlotId.Value));
                    continue;
                }

                if (slot.Owner.Value.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        slotName + " declares a default zero owner; every authoritative slot names one owner (P-034)",
                        slot.SlotId.Value));
                }

                ResolveSchema(slot.Schema, catalog, slotName + " schema", slot.SlotId.Value, diagnostics);
                ResolveKey(slot.PhysicalLayoutKey, catalog, slotName + " physical layout key", slot.SlotId.Value, diagnostics);
                ResolveKey(slot.InitPolicy, catalog, slotName + " initialization policy", slot.SlotId.Value, diagnostics);
                ResolveKey(slot.ConfigChangePolicy, catalog, slotName + " configuration-update policy", slot.SlotId.Value, diagnostics);
                ResolveKey(slot.VersionChangePolicy, catalog, slotName + " version-change policy", slot.SlotId.Value, diagnostics);

                if (slot.LastSupport == LastSupportPolicy.TransferTo)
                {
                    if (slot.TransferPolicy.RegistrationKey.IsDefault)
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.MissingDependency,
                            slotName + " declares TransferTo without a registered owner-transfer policy (P-032)",
                            slot.SlotId.Value));
                    }
                    else
                    {
                        ResolveKey(slot.TransferPolicy, catalog, slotName + " owner-transfer policy", slot.SlotId.Value, diagnostics);
                    }
                }

                for (int m = 0; m < slot.MigrationKeys.Count; m++)
                {
                    ResolveKey(slot.MigrationKeys[m], catalog, slotName + " migration", slot.SlotId.Value, diagnostics);
                }

                for (int f = 0; f < slot.FieldOwnership.Count; f++)
                {
                    ResolveSchema(
                        slot.FieldOwnership[f].ComponentSchema,
                        catalog,
                        slotName + " field ownership",
                        slot.SlotId.Value,
                        diagnostics);
                }
            }
        }

        private static int CompareStateSlots(StateSlotSpec left, StateSlotSpec right) =>
            left.SlotId.Value.CompareTo(right.SlotId.Value);

        private static void ValidateStages(
            PluginManifest manifest,
            ICatalog catalog,
            Index index,
            string owner,
            List<Diagnostic> diagnostics)
        {
            StageSpec[] stages = new StageSpec[manifest.Stages.Count];
            for (int i = 0; i < stages.Length; i++)
            {
                stages[i] = manifest.Stages[i];
            }

            Array.Sort(stages, CompareStages);
            HashSet<Id128> seenInManifest = new HashSet<Id128>();

            for (int i = 0; i < stages.Length; i++)
            {
                StageSpec stage = stages[i];
                string stageName = owner + " stage " + stage.StageId;

                if (stage.StageId.Value.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero stage id is not a catalog identity",
                        manifest.PluginTypeId.Value));
                    continue;
                }

                if (!seenInManifest.Add(stage.StageId.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate stage id " + stage.StageId.Value + " inside one manifest",
                        stage.StageId.Value));
                }

                if (index.StageVersions.TryGetValue(stage.StageId.Value, out uint declaredVersion) && declaredVersion != stage.StageVersion)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        stageName + " declares version " + stage.StageVersion +
                        " while another declaration uses version " + declaredVersion +
                        "; shared stage declarations coalesce only when contract and version match (P-039)",
                        stage.StageId.Value));
                }

                for (int f = 0; f < stage.FactoryKeys.Count; f++)
                {
                    ResolveKey(stage.FactoryKeys[f], catalog, stageName + " stage factory", stage.StageId.Value, diagnostics);
                }

                ResolveAccessSet(stage.ReadWriteSet, catalog, stageName + " stage access", stage.StageId.Value, diagnostics);
                ValidateStageSystems(stage, catalog, index, owner, diagnostics);

                for (int p = 0; p < stage.BufferPorts.Count; p++)
                {
                    BufferPort port = stage.BufferPorts[p];
                    if (!index.Buffers.Contains(port.Buffer.Value))
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.MissingDependency,
                            stageName + " declares a port for buffer " + port.Buffer +
                            " that no manifest in this catalog declares (P-043)",
                            port.Buffer.Value));
                    }
                }
            }
        }

        private static int CompareStages(StageSpec left, StageSpec right) =>
            left.StageId.Value.CompareTo(right.StageId.Value);

        private static void ValidateStageSystems(
            StageSpec stage,
            ICatalog catalog,
            Index index,
            string owner,
            List<Diagnostic> diagnostics)
        {
            SystemSpec[] systems = new SystemSpec[stage.Systems.Count];
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i] = stage.Systems[i];
            }

            Array.Sort(systems, CompareSystems);
            string stageName = owner + " stage " + stage.StageId;
            HashSet<Id128> localKeys = new HashSet<Id128>();

            for (int i = 0; i < systems.Length; i++)
            {
                SystemSpec system = systems[i];
                string systemName = stageName + " system " + system.SystemKey;
                Id128 systemId = system.SystemKey.RegistrationKey;

                if (systemId.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        stageName + " declares a default zero system key",
                        stage.StageId.Value));
                    continue;
                }

                if (!localKeys.Add(systemId))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        stageName + " declares system key " + systemId + " twice",
                        systemId));
                    continue;
                }

                if (index.SystemOwnerStages.TryGetValue(systemId, out Id128 ownerStage) && !ownerStage.Equals(stage.StageId.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        systemName + " is also registered by stage " + ownerStage +
                        "; a world owns at most one system instance per key (P-039)",
                        systemId));
                }

                ResolveKey(system.SystemKey, catalog, systemName, systemId, diagnostics);

                if (system.Access.Declarations.Count == 0)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        systemName + " declares no access set; undeclared access rejects before activation (P-009, P-039)",
                        systemId));
                }

                ResolveAccessSet(system.Access, catalog, systemName + " access", systemId, diagnostics);

                for (int b = 0; b < system.RequiredBeforeSystems.Count; b++)
                {
                    RequireSystemKeyInStage(system.RequiredBeforeSystems[b], systems, systemName + " required-before", systemId, diagnostics);
                }

                for (int a = 0; a < system.RequiredAfterSystems.Count; a++)
                {
                    RequireSystemKeyInStage(system.RequiredAfterSystems[a], systems, systemName + " required-after", systemId, diagnostics);
                }

                for (int o = 0; o < system.OptionalBeforeSystems.Count; o++)
                {
                    if (system.OptionalBeforeSystems[o].RegistrationKey.IsDefault)
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.MissingDependency,
                            systemName + " declares a default zero optional dependency key",
                            systemId));
                    }
                }
            }

            ValidateSystemCycles(stage, systems, diagnostics);
            ValidateSystemAccessOrder(stage, systems, diagnostics);
        }

        private static int CompareSystems(SystemSpec left, SystemSpec right) =>
            left.SystemKey.RegistrationKey.CompareTo(right.SystemKey.RegistrationKey);

        private static void RequireSystemKeyInStage(
            FactoryKey key,
            SystemSpec[] systems,
            string context,
            Id128 involved,
            List<Diagnostic> diagnostics)
        {
            if (key.RegistrationKey.IsDefault)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.MissingDependency, context + " names a default zero system key", involved));
                return;
            }

            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i].SystemKey.RegistrationKey.Equals(key.RegistrationKey))
                {
                    return;
                }
            }

            diagnostics.Add(CatalogDiagnostics.Reject(
                DiagnosticCode.MissingDependency,
                context + " names required system " + key + " which this stage does not declare (P-039)",
                key.RegistrationKey));
        }

        private static void ValidateSystemCycles(StageSpec stage, SystemSpec[] systems, List<Diagnostic> diagnostics)
        {
            Dictionary<Id128, List<Id128>> edges = new Dictionary<Id128, List<Id128>>();
            for (int i = 0; i < systems.Length; i++)
            {
                Id128 from = systems[i].SystemKey.RegistrationKey;
                if (from.IsDefault)
                {
                    continue;
                }

                List<Id128> targets = new List<Id128>();
                CollectRequiredEdges(systems[i].RequiredBeforeSystems, targets);
                CollectRequiredEdges(systems[i].RequiredAfterSystems, targets);
                edges[from] = targets;
            }

            List<Id128>? cycle = RequiredEdgeCycle.Find(edges);
            if (cycle != null)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.Cycle,
                    "stage " + stage.StageId + " declares a system dependency cycle: " + DescribeCycle(cycle),
                    stage.StageId.Value));
            }
        }

        private static void CollectRequiredEdges(IReadOnlyList<FactoryKey> keys, List<Id128> targets)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (!keys[i].RegistrationKey.IsDefault)
                {
                    targets.Add(keys[i].RegistrationKey);
                }
            }
        }

        private static void ValidateSystemAccessOrder(StageSpec stage, SystemSpec[] systems, List<Diagnostic> diagnostics)
        {
            Dictionary<Id128, List<Id128>> edges = new Dictionary<Id128, List<Id128>>();
            for (int i = 0; i < systems.Length; i++)
            {
                Id128 from = systems[i].SystemKey.RegistrationKey;
                if (from.IsDefault || edges.ContainsKey(from))
                {
                    continue;
                }

                List<Id128> targets = new List<Id128>();
                CollectRequiredEdges(systems[i].RequiredBeforeSystems, targets);
                CollectRequiredEdges(systems[i].RequiredAfterSystems, targets);
                edges[from] = targets;
            }

            for (int i = 0; i < systems.Length; i++)
            {
                for (int j = i + 1; j < systems.Length; j++)
                {
                    SystemSpec left = systems[i];
                    SystemSpec right = systems[j];
                    Id128 leftId = left.SystemKey.RegistrationKey;
                    Id128 rightId = right.SystemKey.RegistrationKey;
                    if (leftId.IsDefault || rightId.IsDefault)
                    {
                        continue;
                    }

                    if (!AccessesConflict(left.Access, right.Access))
                    {
                        continue;
                    }

                    if (HasPath(edges, leftId, rightId) || HasPath(edges, rightId, leftId))
                    {
                        continue;
                    }

                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.AmbiguousOrder,
                        "stage " + stage.StageId + " systems " + leftId + " and " + rightId +
                        " declare overlapping write access with neither a dependency edge nor validated disjoint " +
                        "partitions; the scheduler never invents gameplay order (P-040)",
                        leftId));
                }
            }
        }

        /// <summary>
        /// True when two access sets overlap on a schema and at least one side writes it, and the partitions are
        /// not provably disjoint (P-040, P-034).
        /// </summary>
        private static bool AccessesConflict(AccessSet left, AccessSet right)
        {
            for (int i = 0; i < left.Declarations.Count; i++)
            {
                AccessDeclaration a = left.Declarations[i];
                for (int j = 0; j < right.Declarations.Count; j++)
                {
                    AccessDeclaration b = right.Declarations[j];
                    if (!a.Schema.Id.Value.Equals(b.Schema.Id.Value))
                    {
                        continue;
                    }

                    bool writes = a.Mode != AccessMode.Read || b.Mode != AccessMode.Read;
                    if (!writes)
                    {
                        continue;
                    }

                    // Validated disjointness requires both sides to declare a partition and to name different
                    // partitions; a partitioned writer against a whole-schema writer still overlaps (P-040).
                    bool disjointPartitions = a.IsPartitioned && b.IsPartitioned &&
                                              !a.PartitionId.Equals(b.PartitionId);

                    if (!disjointPartitions)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasPath(Dictionary<Id128, List<Id128>> edges, Id128 from, Id128 to)
        {
            HashSet<Id128> visited = new HashSet<Id128>();
            List<Id128> frontier = new List<Id128>();
            frontier.Add(from);
            while (frontier.Count != 0)
            {
                Id128 current = frontier[frontier.Count - 1];
                frontier.RemoveAt(frontier.Count - 1);
                if (!visited.Add(current))
                {
                    continue;
                }

                if (current.Equals(to) && !current.Equals(from))
                {
                    return true;
                }

                if (!edges.TryGetValue(current, out List<Id128>? targets))
                {
                    continue;
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    frontier.Add(targets[i]);
                }
            }

            return false;
        }

        private static void ValidateBuffers(
            PluginManifest manifest,
            ICatalog catalog,
            Index index,
            string owner,
            List<Diagnostic> diagnostics)
        {
            BufferSpec[] buffers = new BufferSpec[manifest.Buffers.Count];
            for (int i = 0; i < buffers.Length; i++)
            {
                buffers[i] = manifest.Buffers[i];
            }

            Array.Sort(buffers, CompareBuffers);
            HashSet<Id128> seen = new HashSet<Id128>();

            for (int i = 0; i < buffers.Length; i++)
            {
                BufferSpec buffer = buffers[i];
                string bufferName = owner + " buffer " + buffer.BufferId;

                if (buffer.BufferId.Value.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero buffer id is not a catalog identity",
                        manifest.PluginTypeId.Value));
                    continue;
                }

                if (!seen.Add(buffer.BufferId.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate buffer id " + buffer.BufferId.Value,
                        buffer.BufferId.Value));
                    continue;
                }

                ResolveSchema(buffer.Schema, catalog, bufferName + " schema", buffer.BufferId.Value, diagnostics);
                ResolveKey(buffer.OrderKey, catalog, bufferName + " order key", buffer.BufferId.Value, diagnostics);

                if (buffer.Capacity <= 0)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.BudgetExceeded,
                        bufferName + " declares capacity " + buffer.Capacity +
                        "; a bounded buffer requires a positive bound (P-022, P-043)",
                        buffer.BufferId.Value));
                }

                for (int p = 0; p < buffer.Producers.Count; p++)
                {
                    ResolveKey(buffer.Producers[p], catalog, bufferName + " producer", buffer.BufferId.Value, diagnostics);
                }

                if (!index.StageVersions.ContainsKey(buffer.OwnerStage.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        bufferName + " names owner stage " + buffer.OwnerStage +
                        " that no manifest in this catalog declares (P-043)",
                        buffer.OwnerStage.Value));
                }

                if (!index.StageVersions.ContainsKey(buffer.ConsumerStage.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        bufferName + " names consumer stage " + buffer.ConsumerStage +
                        " that no manifest in this catalog declares; a buffer has exactly one consuming owner (P-043)",
                        buffer.ConsumerStage.Value));
                }
            }
        }

        private static int CompareBuffers(BufferSpec left, BufferSpec right) =>
            left.BufferId.Value.CompareTo(right.BufferId.Value);

        private static void ValidateResources(
            PluginManifest manifest,
            ICatalog catalog,
            Index index,
            string owner,
            List<Diagnostic> diagnostics)
        {
            ResourceSpec[] resources = new ResourceSpec[manifest.Resources.Count];
            for (int i = 0; i < resources.Length; i++)
            {
                resources[i] = manifest.Resources[i];
            }

            Array.Sort(resources, CompareResources);
            HashSet<Id128> seen = new HashSet<Id128>();

            for (int i = 0; i < resources.Length; i++)
            {
                ResourceSpec resource = resources[i];
                string resourceName = owner + " resource " + resource.ResourceKey;

                if (resource.ResourceKey.Value.IsDefault)
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.MissingDependency,
                        "a default zero resource key is not a catalog identity",
                        manifest.PluginTypeId.Value));
                    continue;
                }

                if (!seen.Add(resource.ResourceKey.Value))
                {
                    diagnostics.Add(CatalogDiagnostics.Reject(
                        DiagnosticCode.OwnershipConflict,
                        "duplicate resource key " + resource.ResourceKey.Value,
                        resource.ResourceKey.Value));
                    continue;
                }

                ResolveKey(resource.Factory, catalog, resourceName + " factory", resource.ResourceKey.Value, diagnostics);
                ResolveKey(resource.Disposer, catalog, resourceName + " disposer", resource.ResourceKey.Value, diagnostics);

                if (!resource.PreparationGate.RegistrationKey.IsDefault)
                {
                    ResolveKey(resource.PreparationGate, catalog, resourceName + " preparation gate", resource.ResourceKey.Value, diagnostics);
                }

                for (int d = 0; d < resource.Dependencies.Count; d++)
                {
                    if (!index.Resources.Contains(resource.Dependencies[d].Value))
                    {
                        diagnostics.Add(CatalogDiagnostics.Reject(
                            DiagnosticCode.MissingDependency,
                            resourceName + " depends on resource " + resource.Dependencies[d] +
                            " that no manifest in this catalog declares (P-048)",
                            resource.Dependencies[d].Value));
                    }
                }
            }
        }

        private static int CompareResources(ResourceSpec left, ResourceSpec right) =>
            left.ResourceKey.Value.CompareTo(right.ResourceKey.Value);

        private static void ValidateStageGraphs(PluginManifest[] manifests, List<Diagnostic> diagnostics)
        {
            Dictionary<Id128, List<Id128>> edges = new Dictionary<Id128, List<Id128>>();
            for (int i = 0; i < manifests.Length; i++)
            {
                StageSpec[] stages = new StageSpec[manifests[i].Stages.Count];
                for (int s = 0; s < stages.Length; s++)
                {
                    stages[s] = manifests[i].Stages[s];
                }

                Array.Sort(stages, CompareStages);
                for (int s = 0; s < stages.Length; s++)
                {
                    Id128 from = stages[s].StageId.Value;
                    if (from.IsDefault)
                    {
                        continue;
                    }

                    if (!edges.TryGetValue(from, out List<Id128>? targets))
                    {
                        targets = new List<Id128>();
                        edges[from] = targets;
                    }

                    for (int b = 0; b < stages[s].RequiredBefore.Count; b++)
                    {
                        targets.Add(stages[s].RequiredBefore[b].Value);
                    }

                    for (int a = 0; a < stages[s].RequiredAfter.Count; a++)
                    {
                        targets.Add(stages[s].RequiredAfter[a].Value);
                    }
                }
            }

            List<Id128>? cycle = RequiredEdgeCycle.Find(edges);
            if (cycle != null)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.Cycle,
                    "scope/stage required edges form a cycle: " + DescribeCycle(cycle) + " (P-028, P-039)",
                    cycle[0]));
            }
        }

        private static void ValidateResourceGraphs(PluginManifest[] manifests, List<Diagnostic> diagnostics)
        {
            Dictionary<Id128, List<Id128>> edges = new Dictionary<Id128, List<Id128>>();
            for (int i = 0; i < manifests.Length; i++)
            {
                ResourceSpec[] resources = new ResourceSpec[manifests[i].Resources.Count];
                for (int r = 0; r < resources.Length; r++)
                {
                    resources[r] = manifests[i].Resources[r];
                }

                Array.Sort(resources, CompareResources);
                for (int r = 0; r < resources.Length; r++)
                {
                    Id128 from = resources[r].ResourceKey.Value;
                    if (from.IsDefault)
                    {
                        continue;
                    }

                    if (!edges.TryGetValue(from, out List<Id128>? targets))
                    {
                        targets = new List<Id128>();
                        edges[from] = targets;
                    }

                    for (int d = 0; d < resources[r].Dependencies.Count; d++)
                    {
                        targets.Add(resources[r].Dependencies[d].Value);
                    }
                }
            }

            List<Id128>? cycle = RequiredEdgeCycle.Find(edges);
            if (cycle != null)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    DiagnosticCode.Cycle,
                    "resource dependencies form a cycle: " + DescribeCycle(cycle) +
                    "; teardown order must be a DAG (P-048)",
                    cycle[0]));
            }
        }

        private static void ResolveAccessSet(AccessSet access, ICatalog catalog, string context, Id128 involved, List<Diagnostic> diagnostics)
        {
            for (int i = 0; i < access.Declarations.Count; i++)
            {
                ResolveSchema(access.Declarations[i].Schema, catalog, context, involved, diagnostics);
            }
        }

        private static void ResolveKey(FactoryKey key, ICatalog catalog, string context, Id128 involved, List<Diagnostic> diagnostics)
        {
            CatalogLookup lookup = catalog.Lookup(key);
            if (!lookup.Found)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    lookup.Code,
                    context + " requires generated key " + key + " which the catalog does not register (" +
                    lookup.Describe() + "); no reflection fallback is permitted (P-009)",
                    involved,
                    key));
            }
        }

        private static void ResolveSchema(SchemaRef schema, ICatalog catalog, string context, Id128 involved, List<Diagnostic> diagnostics)
        {
            CatalogLookup lookup = catalog.LookupSchema(schema);
            if (!lookup.Found)
            {
                diagnostics.Add(CatalogDiagnostics.Reject(
                    lookup.Code,
                    context + " requires schema " + schema + " which the catalog does not accept (" +
                    lookup.Describe() + "); an unknown required schema rejects before activation (P-009, P-054)",
                    involved));
            }
        }

        private static HashSet<Id128> ToFeatureSet(IReadOnlyList<Id128>? supportedFeatureIds)
        {
            HashSet<Id128> features = new HashSet<Id128>();
            if (supportedFeatureIds == null)
            {
                return features;
            }

            for (int i = 0; i < supportedFeatureIds.Count; i++)
            {
                features.Add(supportedFeatureIds[i]);
            }

            return features;
        }

        private static string DescribeCycle(List<Id128> cycle)
        {
            string[] parts = new string[cycle.Count + 1];
            for (int i = 0; i < cycle.Count; i++)
            {
                parts[i] = cycle[i].ToString();
            }

            parts[cycle.Count] = cycle[0].ToString();
            return string.Join(" -> ", parts);
        }

        /// <summary>Deterministic batch indexes used for cross-manifest reference and cycle checks.</summary>
        private sealed class Index
        {
            private Index(
                Dictionary<Id128, CapabilityEntry> capabilities,
                Dictionary<Id128, uint> stageVersions,
                Dictionary<Id128, Id128> systemOwnerStages,
                HashSet<Id128> buffers,
                HashSet<Id128> resources)
            {
                Capabilities = capabilities;
                StageVersions = stageVersions;
                SystemOwnerStages = systemOwnerStages;
                Buffers = buffers;
                Resources = resources;
            }

            internal Dictionary<Id128, CapabilityEntry> Capabilities { get; }

            internal Dictionary<Id128, uint> StageVersions { get; }

            internal Dictionary<Id128, Id128> SystemOwnerStages { get; }

            internal HashSet<Id128> Buffers { get; }

            internal HashSet<Id128> Resources { get; }

            internal static Index Build(PluginManifest[] manifests)
            {
                Dictionary<Id128, CapabilityEntry> capabilities = new Dictionary<Id128, CapabilityEntry>();
                Dictionary<Id128, uint> stageVersions = new Dictionary<Id128, uint>();
                Dictionary<Id128, Id128> systemOwnerStages = new Dictionary<Id128, Id128>();
                HashSet<Id128> buffers = new HashSet<Id128>();
                HashSet<Id128> resources = new HashSet<Id128>();

                for (int i = 0; i < manifests.Length; i++)
                {
                    PluginManifest manifest = manifests[i];

                    for (int c = 0; c < manifest.CapabilityContracts.Count; c++)
                    {
                        CapabilityContract contract = manifest.CapabilityContracts[c];
                        Id128 id = contract.Capability.Capability.Value;
                        if (!id.IsDefault && !capabilities.ContainsKey(id))
                        {
                            capabilities.Add(id, new CapabilityEntry(contract.Capability.Version, contract.Stratum));
                        }
                    }

                    for (int s = 0; s < manifest.Stages.Count; s++)
                    {
                        StageSpec stage = manifest.Stages[s];
                        if (!stage.StageId.Value.IsDefault && !stageVersions.ContainsKey(stage.StageId.Value))
                        {
                            stageVersions.Add(stage.StageId.Value, stage.StageVersion);
                        }

                        for (int y = 0; y < stage.Systems.Count; y++)
                        {
                            Id128 systemId = stage.Systems[y].SystemKey.RegistrationKey;
                            if (!systemId.IsDefault && !systemOwnerStages.ContainsKey(systemId))
                            {
                                systemOwnerStages.Add(systemId, stage.StageId.Value);
                            }
                        }
                    }

                    for (int b = 0; b < manifest.Buffers.Count; b++)
                    {
                        if (!manifest.Buffers[b].BufferId.Value.IsDefault)
                        {
                            buffers.Add(manifest.Buffers[b].BufferId.Value);
                        }
                    }

                    for (int r = 0; r < manifest.Resources.Count; r++)
                    {
                        if (!manifest.Resources[r].ResourceKey.Value.IsDefault)
                        {
                            resources.Add(manifest.Resources[r].ResourceKey.Value);
                        }
                    }
                }

                return new Index(capabilities, stageVersions, systemOwnerStages, buffers, resources);
            }
        }

        /// <summary>One declared capability contract in the validated batch.</summary>
        private sealed class CapabilityEntry
        {
            internal CapabilityEntry(uint version, int stratum)
            {
                Version = version;
                Stratum = stratum;
            }

            internal uint Version { get; }

            internal int Stratum { get; }
        }

        /// <summary>First cycle in a required-edge graph, as the ids forming it; null when the graph is acyclic.</summary>
        private static class RequiredEdgeCycle
        {
            internal static List<Id128>? Find(Dictionary<Id128, List<Id128>> edges)
            {
                List<Id128> nodes = new List<Id128>(edges.Keys);
                nodes.Sort(CatalogOrdering.CompareIds);

                HashSet<Id128> settled = new HashSet<Id128>();
                HashSet<Id128> onPath = new HashSet<Id128>();
                List<Id128> path = new List<Id128>();

                for (int i = 0; i < nodes.Count; i++)
                {
                    List<Id128>? cycle = Visit(nodes[i], edges, settled, onPath, path);
                    if (cycle != null)
                    {
                        return cycle;
                    }
                }

                return null;
            }

            private static List<Id128>? Visit(
                Id128 node,
                Dictionary<Id128, List<Id128>> edges,
                HashSet<Id128> settled,
                HashSet<Id128> onPath,
                List<Id128> path)
            {
                if (settled.Contains(node))
                {
                    return null;
                }

                if (onPath.Contains(node))
                {
                    int start = path.IndexOf(node);
                    List<Id128> cycle = new List<Id128>();
                    for (int i = start < 0 ? 0 : start; i < path.Count; i++)
                    {
                        cycle.Add(path[i]);
                    }

                    return cycle;
                }

                onPath.Add(node);
                path.Add(node);

                if (edges.TryGetValue(node, out List<Id128>? targets))
                {
                    targets.Sort(CatalogOrdering.CompareIds);
                    for (int i = 0; i < targets.Count; i++)
                    {
                        List<Id128>? cycle = Visit(targets[i], edges, settled, onPath, path);
                        if (cycle != null)
                        {
                            return cycle;
                        }
                    }
                }

                path.RemoveAt(path.Count - 1);
                onPath.Remove(node);
                settled.Add(node);
                return null;
            }
        }
    }
}
