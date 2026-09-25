// GameCore.Planning — state-authority declarations (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-032, P-033, P-034, P-039 and
// docs/game-core/03-runtime-and-execution.md s4. These are runtime authority declarations, not a second catalog:
// the catalog's `StateSlotSpec`/`StageSpec` remain the declaration source, and these records add the two facts a
// generated builder knows (which systems write which domain under which owner, and the physical layout/field
// mapping of one component that implements several slots).
//
// Diagnostic-code mapping used by the validators in this folder (no new protocol literal; every code is an
// existing 00 s9 literal):
//   MissingDependency   a default zero owner/slot/schema/partition, a missing policy or layout key, a field the
//                       physical layout does not declare, a support/slot that is not declared
//   OwnershipConflict   two different owners claiming one authoritative domain, a duplicate stable id, a second
//                       physical owner or a duplicate field claim inside one component layout, a proposal that
//                       asks for a disposition or a last-support policy its declaration does not permit
//   AmbiguousOrder      overlapping writers of one domain with neither a directed required edge nor proven
//                       mutually exclusive partitions (P-040)
//   MigrationRequired   a schema/version change without a declared and registered migration (P-032)
//   Ineligible          a declared policy that cannot be applied to this slot at all (for example a
//                       RemoveDerived last-support policy on state that is not disposable derived data)
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Planning.Ownership
{
    /// <summary>
    /// One declared writer of authoritative state: the generated owner grant of one system entry plus its declared
    /// access set (P-034, P-039). A writer whose <see cref="Access"/> contains a write declaration claims part of
    /// that schema's authority; a read-only writer claims nothing.
    /// </summary>
    public readonly struct WriterDeclaration
    {
        public readonly StageId Stage;
        public readonly FactoryKey SystemKey;

        /// <summary>The single logical owner this writer acts for; default is rejected (P-034).</summary>
        public readonly OwnerId Owner;

        public readonly AccessSet Access;

        /// <summary>
        /// Multiplicity of this system entry. A per-partition writer that declares no explicit partition receives a
        /// generated, deterministic partition id, which is what makes two writers provably disjoint (P-034, P-040).
        /// </summary>
        public readonly SystemMultiplicity Multiplicity;

        /// <summary>Declared systems that must run before this writer; participates in the order proof (P-040).</summary>
        public readonly IReadOnlyList<FactoryKey> RequiredBeforeSystems;

        /// <summary>Declared systems that must run after this writer; participates in the order proof (P-040).</summary>
        public readonly IReadOnlyList<FactoryKey> RequiredAfterSystems;

        public WriterDeclaration(
            StageId stage,
            FactoryKey systemKey,
            OwnerId owner,
            AccessSet access,
            SystemMultiplicity multiplicity,
            IReadOnlyList<FactoryKey>? requiredBeforeSystems,
            IReadOnlyList<FactoryKey>? requiredAfterSystems)
        {
            Stage = stage;
            SystemKey = systemKey;
            Owner = owner;
            Access = access ?? throw new ArgumentNullException(nameof(access));
            Multiplicity = multiplicity;
            RequiredBeforeSystems = ContractCollections.Freeze(requiredBeforeSystems);
            RequiredAfterSystems = ContractCollections.Freeze(requiredAfterSystems);
        }

        /// <summary>True when this declaration names a real generated system key.</summary>
        public bool IsDeclared => !SystemKey.RegistrationKey.IsDefault;

        /// <summary>True when the generated multiplicity asks for one instance per partition.</summary>
        public bool GeneratesPartitions => Multiplicity == SystemMultiplicity.PerPartition;

        /// <summary>Every schema this writer declares for writing; a read-only declaration claims no authority.</summary>
        public IReadOnlyList<SchemaRef> DeclaredWriteSchemas()
        {
            var writes = new List<SchemaRef>();
            for (int i = 0; i < Access.Declarations.Count; i++)
            {
                AccessDeclaration declaration = Access.Declarations[i];
                if (declaration.Mode != AccessMode.Read && !ContainsSchema(writes, declaration.Schema))
                {
                    writes.Add(declaration.Schema);
                }
            }

            return writes;
        }

        /// <summary>The partition this writer declares for one schema, or a default id when it declares none.</summary>
        public Id128 DeclaredPartition(SchemaRef schema)
        {
            for (int i = 0; i < Access.Declarations.Count; i++)
            {
                AccessDeclaration declaration = Access.Declarations[i];
                if (declaration.Mode != AccessMode.Read && declaration.Schema.Id.Value.Equals(schema.Id.Value))
                {
                    return declaration.PartitionId;
                }
            }

            return Id128.Zero;
        }

        public override string ToString()
            => SystemKey.ToString() + "@" + Stage.ToString();

        private static bool ContainsSchema(List<SchemaRef> schemas, SchemaRef schema)
        {
            for (int i = 0; i < schemas.Count; i++)
            {
                if (schemas[i].Id.Value.Equals(schema.Id.Value))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Options a generated slot declaration adds to the catalog's <see cref="StateSlotSpec"/>: P-032 requires an
    /// explicit proposal field and reason before a slot may be reset, `PreserveDormant` is recorded policy rather
    /// than implicit behaviour, and `RemoveDerived` is legal only for disposable derived data (P-032, P-033).
    /// </summary>
    public readonly struct SlotAuthorityOptions
    {
        /// <summary>True only when the manifest explicitly supports a `Reset` for this slot (P-032).</summary>
        public readonly bool ResetPermitted;

        /// <summary>The explicit reason recorded with that support; empty means the reset is not permitted.</summary>
        public readonly string ResetReason;

        /// <summary>True when the declaration explicitly permits retaining dormant state (P-032).</summary>
        public readonly bool PreserveDormantPermitted;

        /// <summary>True when this slot's state is disposable derived data (P-032, P-033).</summary>
        public readonly bool DisposableDerived;

        public SlotAuthorityOptions(bool resetPermitted, string? resetReason, bool preserveDormantPermitted, bool disposableDerived)
        {
            ResetPermitted = resetPermitted;
            ResetReason = resetReason ?? string.Empty;
            PreserveDormantPermitted = preserveDormantPermitted;
            DisposableDerived = disposableDerived;
        }

        /// <summary>Disposable derived state: a final-support loss may remove it (P-032, P-033).</summary>
        public static SlotAuthorityOptions DerivedData()
            => new SlotAuthorityOptions(false, null, false, true);

        /// <summary>Durable state that is not disposable derived data; a final-support loss may not remove it.</summary>
        public static SlotAuthorityOptions Durable()
            => new SlotAuthorityOptions(false, null, false, false);

        /// <summary>State whose declaration explicitly permits dormant retention (P-032).</summary>
        public static SlotAuthorityOptions Dormant()
            => new SlotAuthorityOptions(false, null, true, false);

        /// <summary>Explicit manifest-supported reset with its recorded reason (P-032).</summary>
        public static SlotAuthorityOptions Resettable(string reason, bool preserveDormantPermitted, bool disposableDerived)
            => new SlotAuthorityOptions(true, reason, preserveDormantPermitted, disposableDerived);

        public override string ToString()
            => "reset=" + (ResetPermitted ? "yes" : "no")
                + ", dormant=" + (PreserveDormantPermitted ? "yes" : "no")
                + ", derived=" + (DisposableDerived ? "yes" : "no");
    }

    /// <summary>
    /// One authoritative state slot with its lifecycle policies (P-032). The catalog's <see cref="StateSlotSpec"/>
    /// supplies the identity, owner, schema, layout key, field mapping and policy keys; this record adds the
    /// generated options above so the policies can be validated before any migration executor exists.
    /// </summary>
    public sealed class SlotAuthorityDeclaration
    {
        public SlotAuthorityDeclaration(
            SlotId slotId,
            OwnerId owner,
            SchemaRef schema,
            FactoryKey physicalLayoutKey,
            IReadOnlyList<FieldOwnership>? fieldOwnership,
            LastSupportPolicy lastSupport,
            FactoryKey transferPolicy,
            IReadOnlyList<FactoryKey>? migrationKeys,
            SlotAuthorityOptions options)
        {
            SlotId = slotId;
            Owner = owner;
            Schema = schema;
            PhysicalLayoutKey = physicalLayoutKey;
            FieldOwnership = ContractCollections.Freeze(fieldOwnership);
            LastSupport = lastSupport;
            TransferPolicy = transferPolicy;
            MigrationKeys = ContractCollections.Freeze(migrationKeys);
            Options = options;
        }

        /// <summary>Projects one catalog declaration, attaching the generated options (never infers them).</summary>
        public static SlotAuthorityDeclaration FromSpec(StateSlotSpec spec, SlotAuthorityOptions options)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            return new SlotAuthorityDeclaration(
                spec.SlotId,
                spec.Owner,
                spec.Schema,
                spec.PhysicalLayoutKey,
                spec.FieldOwnership,
                spec.LastSupport,
                spec.TransferPolicy,
                spec.MigrationKeys,
                options);
        }

        public SlotId SlotId { get; }

        public OwnerId Owner { get; }

        public SchemaRef Schema { get; }

        public FactoryKey PhysicalLayoutKey { get; }

        public IReadOnlyList<FieldOwnership> FieldOwnership { get; }

        public LastSupportPolicy LastSupport { get; }

        public FactoryKey TransferPolicy { get; }

        public IReadOnlyList<FactoryKey> MigrationKeys { get; }

        public SlotAuthorityOptions Options { get; }

        public bool HasTransferPolicy => !TransferPolicy.RegistrationKey.IsDefault;

        public bool HasPhysicalLayoutKey => !PhysicalLayoutKey.RegistrationKey.IsDefault;

        /// <summary>True when this declaration records a non-default migration key.</summary>
        public bool DeclaresAnyMigration()
        {
            for (int i = 0; i < MigrationKeys.Count; i++)
            {
                if (!MigrationKeys[i].RegistrationKey.IsDefault)
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() => SlotId.ToString() + "@" + Owner.ToString();
    }

    /// <summary>
    /// One physical component layout: the single physical owner of that component plus the fields it holds
    /// (P-033). Where one ECS component implements several slots, exactly one declaration like this is legal for a
    /// layout key; competing arbitrary component initializers are rejected by the validator.
    /// </summary>
    public readonly struct ComponentLayoutDeclaration
    {
        public readonly SchemaRef Component;

        /// <summary>Generated physical layout key; distinct keys mean split storage for the same component schema.</summary>
        public readonly FactoryKey LayoutKey;

        public readonly OwnerId Owner;

        /// <summary>Fields this physical component holds, in declaration order.</summary>
        public readonly IReadOnlyList<FieldOwnership> Fields;

        public ComponentLayoutDeclaration(
            SchemaRef component,
            FactoryKey layoutKey,
            OwnerId owner,
            IReadOnlyList<FieldOwnership>? fields)
        {
            Component = component;
            LayoutKey = layoutKey;
            Owner = owner;
            Fields = ContractCollections.Freeze(fields);
        }

        public override string ToString()
            => Component.ToString() + "/" + LayoutKey.ToString() + "@" + Owner.ToString();
    }

    /// <summary>
    /// The complete authority input of one plan: its declared writers, its state slots and its physical component
    /// layouts. <see cref="StageOrder"/> gives the compiled stage order when one is available (GC-009), so two
    /// writers in different stages are provably ordered; without it every writer is treated as unordered and must
    /// prove disjointness by partitions (P-040).
    /// </summary>
    public sealed class OwnerAuthorityDeclaration
    {
        public static readonly OwnerAuthorityDeclaration Empty =
            new OwnerAuthorityDeclaration(null, null, null, null);

        public OwnerAuthorityDeclaration(
            IReadOnlyList<WriterDeclaration>? writers,
            IReadOnlyList<SlotAuthorityDeclaration>? slots,
            IReadOnlyList<ComponentLayoutDeclaration>? components,
            IReadOnlyList<StageId>? stageOrder)
        {
            Writers = ContractCollections.Freeze(writers);
            Slots = ContractCollections.Freeze(slots);
            Components = ContractCollections.Freeze(components);
            StageOrder = ContractCollections.Freeze(stageOrder);
        }

        public IReadOnlyList<WriterDeclaration> Writers { get; }

        public IReadOnlyList<SlotAuthorityDeclaration> Slots { get; }

        public IReadOnlyList<ComponentLayoutDeclaration> Components { get; }

        /// <summary>Compiled stage order, or empty when no order is known; index is the execution position.</summary>
        public IReadOnlyList<StageId> StageOrder { get; }

        /// <summary>Position of one stage in <see cref="StageOrder"/>, or -1 when it is unknown.</summary>
        public int StagePosition(StageId stage)
        {
            for (int i = 0; i < StageOrder.Count; i++)
            {
                if (StageOrder[i].Equals(stage))
                {
                    return i;
                }
            }

            return -1;
        }

        public override string ToString()
            => "writers=" + Writers.Count.ToString(CultureInfo.InvariantCulture)
                + ", slots=" + Slots.Count.ToString(CultureInfo.InvariantCulture)
                + ", components=" + Components.Count.ToString(CultureInfo.InvariantCulture)
                + ", stages=" + StageOrder.Count.ToString(CultureInfo.InvariantCulture);
    }
}
