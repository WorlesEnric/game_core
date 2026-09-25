// GameCore.Planning — per-slot generated layouts (GC-015).
//
// Normative sources: docs/game-core/00-core-protocols.md P-033 ("where one ECS component implements several slots,
// the generated layout declares a single physical owner and a field mapping, or splits storage; competing arbitrary
// component initializers are rejected") and 05 s3/s4 (`StateSlotSpec.PhysicalLayoutKey`/`FieldOwnership`,
// `LayoutOperation`).
//
// The layout is *generated*, never inferred at runtime: the input is the catalog revision the GC-003 content
// compiler emitted (each slot's declared physical layout key and field ownership), and the output is one record per
// physical storage. Deriving the layout here, once, is what lets the planner, the ownership validator and the Unity
// apply path agree about which component stores which field:
//
//   * one layout key is one physical component with one owner: two slots that name the same key share that component
//     and their field mappings are merged, and a second owner for the same key is refused (P-033);
//   * two slots of one schema under *different* keys are two physical storages (`SplitStorage`), which is the
//     documented alternative to one component implementing several slots (P-033);
//   * two slots that claim the same field of one physical component are refused rather than merged silently
//     ("competing arbitrary component initializers are rejected", P-033).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Planning.StatePolicies
{
    /// <summary>How one generated slot layout stores its state (P-033).</summary>
    public enum SlotStorageKind
    {
        /// <summary>One physical component implements one slot.</summary>
        SingleSlot = 0,

        /// <summary>One physical component implements several slots through the declared field mapping (P-033).</summary>
        SharedComponent = 1,

        /// <summary>Slots of one schema are stored in distinct physical components (P-033's split storage).</summary>
        SplitStorage = 2,
    }

    /// <summary>
    /// One generated physical layout: the layout key the catalog declared, its single physical owner, the schemas it
    /// stores, the slots it implements and its field mapping (P-033).
    /// </summary>
    public sealed class GeneratedSlotLayout
    {
        internal GeneratedSlotLayout(
            FactoryKey layoutKey,
            OwnerId owner,
            IReadOnlyList<SchemaRef> schemas,
            IReadOnlyList<SlotId> slots,
            IReadOnlyList<FieldOwnership> fields,
            SlotStorageKind storageKind)
        {
            LayoutKey = layoutKey;
            Owner = owner;
            Schemas = schemas;
            Slots = slots;
            Fields = fields;
            StorageKind = storageKind;
        }

        /// <summary>Generated physical layout key from the catalog (never invented here).</summary>
        public FactoryKey LayoutKey { get; }

        /// <summary>The layout's single physical owner: two owners for one component are refused (P-033).</summary>
        public OwnerId Owner { get; }

        /// <summary>Schemas this physical component stores, in declaration order.</summary>
        public IReadOnlyList<SchemaRef> Schemas { get; }

        /// <summary>Slots this physical component implements, in declaration order.</summary>
        public IReadOnlyList<SlotId> Slots { get; }

        /// <summary>Declared field mapping of this component, in declaration order (P-033).</summary>
        public IReadOnlyList<FieldOwnership> Fields { get; }

        public SlotStorageKind StorageKind { get; }

        /// <summary>True when this physical component implements several slots.</summary>
        public bool ImplementsSeveralSlots => Slots.Count > 1;

        /// <summary>True when this layout declares the given field of the given component schema.</summary>
        public bool DeclaresField(SchemaRef componentSchema, Id128 fieldKey)
        {
            for (int i = 0; i < Fields.Count; i++)
            {
                if (Fields[i].FieldKey.Equals(fieldKey)
                    && Fields[i].ComponentSchema.Id.Value.Equals(componentSchema.Id.Value))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
            => LayoutKey.ToString() + "@" + Owner.ToString()
                + "(" + StorageKind.ToString() + ", slots="
                + Slots.Count.ToString(CultureInfo.InvariantCulture)
                + ", fields=" + Fields.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>The generated layout table of one catalog revision, queried by layout key or by slot identity.</summary>
    public sealed class SlotLayoutTable
    {
        internal SlotLayoutTable(IReadOnlyList<GeneratedSlotLayout>? layouts, IReadOnlyList<SlotId>? slotsInOrder)
        {
            Layouts = ContractCollections.Freeze(layouts);
            SlotsInOrder = ContractCollections.Freeze(slotsInOrder);
        }

        public IReadOnlyList<GeneratedSlotLayout> Layouts { get; }

        /// <summary>Slots of the revision in canonical declaration order; a slot appears exactly once.</summary>
        public IReadOnlyList<SlotId> SlotsInOrder { get; }

        /// <summary>Physical components this revision stores: one per generated layout key (P-033).</summary>
        public int PhysicalComponentCount => Layouts.Count;

        /// <summary>Generated layouts that implement more than one slot through a field mapping (P-033).</summary>
        public int SharedComponentCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Layouts.Count; i++)
                {
                    if (Layouts[i].ImplementsSeveralSlots)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Generated layouts that are one of several storages of one schema (P-033's split storage).</summary>
        public int SplitStorageCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Layouts.Count; i++)
                {
                    if (Layouts[i].StorageKind == SlotStorageKind.SplitStorage)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Finds the physical layout that stores one slot; a miss means the slot has no generated layout.</summary>
        public bool TryGet(SlotId slot, out GeneratedSlotLayout? layout)
        {
            for (int i = 0; i < Layouts.Count; i++)
            {
                for (int s = 0; s < Layouts[i].Slots.Count; s++)
                {
                    if (Layouts[i].Slots[s].Equals(slot))
                    {
                        layout = Layouts[i];
                        return true;
                    }
                }
            }

            layout = null;
            return false;
        }

        public bool TryGetByLayoutKey(FactoryKey layoutKey, out GeneratedSlotLayout? layout)
        {
            for (int i = 0; i < Layouts.Count; i++)
            {
                if (Layouts[i].LayoutKey.Equals(layoutKey))
                {
                    layout = Layouts[i];
                    return true;
                }
            }

            layout = null;
            return false;
        }

        public override string ToString()
            => "layouts=" + Layouts.Count.ToString(CultureInfo.InvariantCulture)
                + ", shared=" + SharedComponentCount.ToString(CultureInfo.InvariantCulture)
                + ", split=" + SplitStorageCount.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Generates one revision's per-slot physical layouts from its compiled declarations (P-033).</summary>
    public static class SlotLayoutGenerator
    {
        /// <summary>
        /// Generates the layout table of one catalog revision. A slot that declares no physical layout key is
        /// `MissingDependency`: the compiler must have emitted the key, and this generator never invents one.
        /// </summary>
        public static bool TryGenerate(
            IReadOnlyList<StateSlotSpec>? specs,
            out SlotLayoutTable? table,
            out DiagnosticCode code,
            out string detail)
        {
            table = null;
            code = DiagnosticCode.None;
            detail = string.Empty;

            var keys = new List<FactoryKey>();
            var owners = new List<OwnerId>();
            var schemas = new List<List<SchemaRef>>();
            var slots = new List<List<SlotId>>();
            var fields = new List<List<FieldOwnership>>();
            var slotsInOrder = new List<SlotId>();

            if (specs != null)
            {
                for (int i = 0; i < specs.Count; i++)
                {
                    StateSlotSpec spec = specs[i];
                    if (spec == null)
                    {
                        continue;
                    }

                    if (spec.SlotId.Value.IsDefault)
                    {
                        code = DiagnosticCode.MissingDependency;
                        detail = "a declared state slot has a default zero identity, so it has no generated layout (P-004).";
                        return false;
                    }

                    if (spec.PhysicalLayoutKey.RegistrationKey.IsDefault)
                    {
                        code = DiagnosticCode.MissingDependency;
                        detail = "state slot " + spec.SlotId.ToString()
                            + " declares no physical layout key; the compiled catalog must emit the component that"
                            + " stores the slot's fields (P-033).";
                        return false;
                    }

                    if (ContainsSlot(slotsInOrder, spec.SlotId))
                    {
                        // One slot, one declaration per revision (P-032); a repeated identical declaration is the
                        // same slot, not a second storage.
                        continue;
                    }

                    int index = IndexOfKey(keys, spec.PhysicalLayoutKey);
                    if (index < 0)
                    {
                        keys.Add(spec.PhysicalLayoutKey);
                        owners.Add(spec.Owner);
                        schemas.Add(new List<SchemaRef> { spec.Schema });
                        slots.Add(new List<SlotId> { spec.SlotId });
                        fields.Add(CopyFields(spec.FieldOwnership));
                        slotsInOrder.Add(spec.SlotId);
                        continue;
                    }

                    if (!owners[index].Equals(spec.Owner))
                    {
                        code = DiagnosticCode.OwnershipConflict;
                        detail = "physical layout " + spec.PhysicalLayoutKey.ToString() + " is claimed by owners "
                            + owners[index].ToString() + " and " + spec.Owner.ToString()
                            + "; one component layout has one physical owner (P-033).";
                        return false;
                    }

                    for (int f = 0; f < spec.FieldOwnership.Count; f++)
                    {
                        FieldOwnership field = spec.FieldOwnership[f];
                        if (DeclaresField(fields[index], field))
                        {
                            code = DiagnosticCode.OwnershipConflict;
                            detail = "physical layout " + spec.PhysicalLayoutKey.ToString() + " would hold field "
                                + field.FieldKey.ToString() + " of " + field.ComponentSchema.ToString()
                                + " for two state slots; competing component initializers are rejected (P-033).";
                            return false;
                        }

                        fields[index].Add(field);
                    }

                    AddSchema(schemas[index], spec.Schema);
                    slots[index].Add(spec.SlotId);
                    slotsInOrder.Add(spec.SlotId);
                }
            }

            var layouts = new List<GeneratedSlotLayout>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                SlotStorageKind kind = slots[i].Count > 1
                    ? SlotStorageKind.SharedComponent
                    : SlotStorageKind.SingleSlot;
                layouts.Add(new GeneratedSlotLayout(
                    keys[i],
                    owners[i],
                    schemas[i],
                    slots[i],
                    fields[i],
                    kind));
            }

            // Two layouts of one schema are two physical storages: the declared split, recorded as such (P-033).
            for (int i = 0; i < layouts.Count; i++)
            {
                if (!IsOneOfSeveralStoragesOfItsSchema(layouts, i))
                {
                    continue;
                }

                GeneratedSlotLayout layout = layouts[i];
                layouts[i] = new GeneratedSlotLayout(
                    layout.LayoutKey,
                    layout.Owner,
                    layout.Schemas,
                    layout.Slots,
                    layout.Fields,
                    SlotStorageKind.SplitStorage);
            }

            table = new SlotLayoutTable(layouts, slotsInOrder);
            return true;
        }

        private static bool IsOneOfSeveralStoragesOfItsSchema(List<GeneratedSlotLayout> layouts, int index)
        {
            // Layout keys are unique by construction, so a different index is a different physical component; two
            // such components storing one schema are the declared split (P-033).
            for (int i = 0; i < layouts.Count; i++)
            {
                if (i != index && SharesSchema(layouts[index], layouts[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SharesSchema(GeneratedSlotLayout left, GeneratedSlotLayout right)
        {
            for (int i = 0; i < left.Schemas.Count; i++)
            {
                for (int j = 0; j < right.Schemas.Count; j++)
                {
                    if (left.Schemas[i].Id.Value.Equals(right.Schemas[j].Id.Value))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static List<FieldOwnership> CopyFields(IReadOnlyList<FieldOwnership> source)
        {
            var copy = new List<FieldOwnership>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                copy.Add(source[i]);
            }

            return copy;
        }

        private static bool DeclaresField(List<FieldOwnership> fields, FieldOwnership candidate)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                if (fields[i].FieldKey.Equals(candidate.FieldKey)
                    && fields[i].ComponentSchema.Id.Value.Equals(candidate.ComponentSchema.Id.Value))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddSchema(List<SchemaRef> schemas, SchemaRef schema)
        {
            for (int i = 0; i < schemas.Count; i++)
            {
                if (schemas[i].Id.Value.Equals(schema.Id.Value) && schemas[i].Version == schema.Version)
                {
                    return;
                }
            }

            schemas.Add(schema);
        }

        private static bool ContainsSlot(List<SlotId> slots, SlotId slot)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Equals(slot))
                {
                    return true;
                }
            }

            return false;
        }

        private static int IndexOfKey(List<FactoryKey> keys, FactoryKey key)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i].Equals(key))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
