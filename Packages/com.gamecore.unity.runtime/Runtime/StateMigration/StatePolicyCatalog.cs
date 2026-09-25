// GameCore.Unity.Runtime — the catalog-side state-policy surface of GC-015.
//
// This is the module that turns one catalog revision into the three things the state executors need, all from the
// declarations the GC-003 content compiler already emitted (nothing is inferred from live storage, and no Unity type
// enters `GameCore.Planning.StatePolicies`):
//
//   * `Policies`  — every declared state slot with its init/config-change/version-change/owner-transfer/last-support
//                   policies (P-032);
//   * `Layouts`   — the generated per-slot physical layouts of this revision (P-033): which component stores which
//                   field, which component implements several slots, and where one schema is split across storages;
//   * `Migrations`/`InitialValues` — the registered pure migrations (05 s5) and the declared initialization policies,
//                   so a version change or an explicit reset resolves through a registered key and never through an
//                   implicit zero (P-032).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Planning.StatePolicies;

namespace GameCore.Unity.Runtime.StateMigration
{
    /// <summary>How one revision's state-policy surface was built.</summary>
    public enum StatePolicyCatalogOutcome
    {
        /// <summary>Every declaration validated and the layouts were generated.</summary>
        Built = 0,

        /// <summary>A slot declaration is duplicated with different owners or schemas (P-032, P-034).</summary>
        DuplicateDeclaration = 1,

        /// <summary>A declared policy set is incomplete or internally inconsistent (P-032).</summary>
        PolicyRejected = 2,

        /// <summary>A physical layout is claimed twice, or its field mapping conflicts (P-033).</summary>
        LayoutRejected = 3,
    }

    /// <summary>One revision's state-policy surface plus the verdict of every module that produced part of it.</summary>
    public sealed class StatePolicyCatalog
    {
        private StatePolicyCatalog(
            StatePolicyCatalogOutcome outcome,
            DiagnosticCode code,
            string detail,
            SlotStatePolicySet? policies,
            SlotLayoutTable? layouts,
            MigrationRegistry? migrations,
            IInitializationPolicyRegistry? initialValues,
            IReadOnlyList<Diagnostic>? slotPolicyResults)
        {
            Outcome = outcome;
            Code = code;
            Detail = detail ?? string.Empty;
            Policies = policies;
            Layouts = layouts;
            Migrations = migrations ?? new MigrationRegistry(null);
            InitialValues = initialValues;
            SlotPolicyResults = slotPolicyResults ?? (IReadOnlyList<Diagnostic>)Array.Empty<Diagnostic>();
        }

        public StatePolicyCatalogOutcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>Declared policies of this revision, or null when it was refused (P-032).</summary>
        public SlotStatePolicySet? Policies { get; }

        /// <summary>Generated physical layouts of this revision, or null when it was refused (P-033).</summary>
        public SlotLayoutTable? Layouts { get; }

        /// <summary>Registered migration handlers of this revision (05 s5).</summary>
        public MigrationRegistry Migrations { get; }

        /// <summary>Registered initialization policies a declared reset reads its value from (P-032).</summary>
        public IInitializationPolicyRegistry? InitialValues { get; }

        /// <summary>One result per declared slot policy, so a caller can report the validator's own verdict (P-028).</summary>
        public IReadOnlyList<Diagnostic> SlotPolicyResults { get; }

        public bool Succeeded => Outcome == StatePolicyCatalogOutcome.Built
            && Policies != null
            && Layouts != null;

        /// <summary>
        /// Builds the surface of one catalog revision: the union of the mounted manifests' declared state slots is the
        /// input, exactly as it is for `OwnershipSchedulePipeline` (P-009).
        /// </summary>
        public static StatePolicyCatalog Build(
            IReadOnlyList<PluginManifest>? manifests,
            IReadOnlyList<ISlotMigration>? migrations,
            IInitializationPolicyRegistry? initialValues)
        {
            var policyResults = new List<Diagnostic>();
            if (!SlotStatePolicySet.TryBuildFromManifests(
                    manifests, out SlotStatePolicySet? policies, out DiagnosticCode buildCode, out string buildDetail)
                || policies == null)
            {
                return new StatePolicyCatalog(
                    StatePolicyCatalogOutcome.DuplicateDeclaration, buildCode, buildDetail, null, null,
                    new MigrationRegistry(migrations), initialValues, policyResults);
            }

            var specs = new List<StateSlotSpec>();
            if (manifests != null)
            {
                for (int i = 0; i < manifests.Count; i++)
                {
                    PluginManifest manifest = manifests[i];
                    if (manifest == null)
                    {
                        continue;
                    }

                    for (int s = 0; s < manifest.StateSlots.Count; s++)
                    {
                        specs.Add(manifest.StateSlots[s]);
                    }
                }
            }

            for (int i = 0; i < policies.Count; i++)
            {
                SlotAuthorityDeclaration declaration = policies.Policies[i].Declaration;
                SlotPolicyResult declared = SlotPolicyValidator.ValidateDeclaration(declaration);
                if (!declared.Succeeded)
                {
                    policyResults.Add(Diagnostic.Create(
                        declared.Code,
                        OperationPhase.Planning,
                        default(OperationId),
                        "state slot " + declaration.SlotId.ToString() + ": " + declared.Detail));
                    return new StatePolicyCatalog(
                        StatePolicyCatalogOutcome.PolicyRejected, declared.Code, declared.Detail, null, null,
                        new MigrationRegistry(migrations), initialValues, policyResults);
                }

                policyResults.Add(Diagnostic.Create(
                    DiagnosticCode.None,
                    OperationPhase.Planning,
                    default(OperationId),
                    "state slot " + declaration.SlotId.ToString() + " declares a complete policy set (P-032)."));
            }

            if (!SlotLayoutGenerator.TryGenerate(specs, out SlotLayoutTable? layouts, out DiagnosticCode layoutCode, out string layoutDetail)
                || layouts == null)
            {
                return new StatePolicyCatalog(
                    StatePolicyCatalogOutcome.LayoutRejected, layoutCode, layoutDetail, policies, null,
                    new MigrationRegistry(migrations), initialValues, policyResults);
            }

            return new StatePolicyCatalog(
                StatePolicyCatalogOutcome.Built,
                DiagnosticCode.None,
                string.Empty,
                policies,
                layouts,
                new MigrationRegistry(migrations),
                initialValues,
                policyResults);
        }

        public string Describe()
            => Outcome.ToString()
                + (Code == DiagnosticCode.None ? string.Empty : "(" + DiagnosticCodeText.Of(Code) + ")")
                + "; policies=" + (Policies != null ? Policies.Count : 0)
                + "; layouts=" + (Layouts != null ? Layouts.PhysicalComponentCount : 0)
                + "; migrations=" + Migrations.Count
                + (Detail.Length != 0 ? "; " + Detail : string.Empty);
    }

    /// <summary>
    /// The world's dormant state: which slots retain a value with no active writer (P-032 `PreserveDormant`). The
    /// registry is derived data rebuilt from the published dispositions, and it exists so "dormant state has no writer
    /// but is retained" is an observable fact rather than a claim.
    /// </summary>
    public sealed class DormantStateRegistry
    {
        private readonly List<DormantSlotRecord> dormant = new List<DormantSlotRecord>();

        public int Count => dormant.Count;

        public IReadOnlyList<DormantSlotRecord> Slots => dormant;

        /// <summary>Records that one slot went dormant with its retained value and version (P-032).</summary>
        public void MarkDormant(StateSlotKey slot, int retainedValue, uint schemaVersion)
        {
            for (int i = 0; i < dormant.Count; i++)
            {
                if (dormant[i].Slot.Equals(slot))
                {
                    dormant[i] = new DormantSlotRecord(slot, retainedValue, schemaVersion);
                    return;
                }
            }

            dormant.Add(new DormantSlotRecord(slot, retainedValue, schemaVersion));
        }

        /// <summary>Records that one slot has an active writer again; its dormant record is dropped (P-032).</summary>
        public bool Reactivate(StateSlotKey slot)
        {
            for (int i = 0; i < dormant.Count; i++)
            {
                if (dormant[i].Slot.Equals(slot))
                {
                    dormant.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public bool IsDormant(StateSlotKey slot)
        {
            for (int i = 0; i < dormant.Count; i++)
            {
                if (dormant[i].Slot.Equals(slot))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGet(StateSlotKey slot, out DormantSlotRecord record)
        {
            for (int i = 0; i < dormant.Count; i++)
            {
                if (dormant[i].Slot.Equals(slot))
                {
                    record = dormant[i];
                    return true;
                }
            }

            record = default(DormantSlotRecord);
            return false;
        }

        public override string ToString() => "dormantSlots=" + Count;
    }

    /// <summary>One dormant slot: the retained value and version, and the fact that no writer owns it now (P-032).</summary>
    public readonly struct DormantSlotRecord
    {
        public readonly StateSlotKey Slot;
        public readonly int RetainedValue;
        public readonly uint SchemaVersion;

        public DormantSlotRecord(StateSlotKey slot, int retainedValue, uint schemaVersion)
        {
            Slot = slot;
            RetainedValue = retainedValue;
            SchemaVersion = schemaVersion;
        }

        public override string ToString()
            => Slot.ToString() + "=" + RetainedValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "@" + SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
