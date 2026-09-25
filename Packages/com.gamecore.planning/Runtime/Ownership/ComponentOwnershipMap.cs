// GameCore.Planning — field-to-component ownership (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-033, P-034 and docs/game-core/05-contracts-and-data-model.md s3.
// Where one ECS component implements several state slots, exactly one generated layout declares the physical owner
// and the field mapping, or the storage is split into distinct layouts. Competing arbitrary component initializers
// (two layouts owning one component, or two slots claiming one field) are rejected rather than merged silently.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Planning.Ownership
{
    /// <summary>One physical component layout: its single owner, its fields and the slots it implements.</summary>
    public sealed class ComponentOwnershipEntry
    {
        internal ComponentOwnershipEntry(
            SchemaRef component,
            FactoryKey layoutKey,
            OwnerId owner,
            IReadOnlyList<FieldOwnership> fields,
            IReadOnlyList<SlotId> slots)
        {
            Component = component;
            LayoutKey = layoutKey;
            Owner = owner;
            Fields = fields;
            Slots = slots;
        }

        public SchemaRef Component { get; }

        public FactoryKey LayoutKey { get; }

        public OwnerId Owner { get; }

        /// <summary>Fields of this physical component, in declaration order.</summary>
        public IReadOnlyList<FieldOwnership> Fields { get; }

        /// <summary>State slots whose schema this component stores; empty for a base recipe component.</summary>
        public IReadOnlyList<SlotId> Slots { get; }

        public bool DeclaresField(Id128 fieldKey)
        {
            for (int i = 0; i < Fields.Count; i++)
            {
                if (Fields[i].FieldKey.Equals(fieldKey))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
            => Component.ToString() + "/" + LayoutKey.ToString() + "@" + Owner.ToString();
    }

    /// <summary>
    /// Resolved field-to-component ownership of one authority declaration. The map answers the two questions a
    /// generated apply path asks: which single owner holds this component, and which slot a physical field belongs
    /// to.
    /// </summary>
    public sealed class ComponentOwnershipMap
    {
        public static readonly ComponentOwnershipMap Empty =
            new ComponentOwnershipMap(null, null);

        private readonly List<ComponentOwnershipEntry> entries;
        private readonly Dictionary<Id128, FieldSlotRecord> fieldSlots;

        private ComponentOwnershipMap(
            List<ComponentOwnershipEntry>? entries,
            Dictionary<Id128, FieldSlotRecord>? fieldSlots)
        {
            this.entries = entries ?? new List<ComponentOwnershipEntry>();
            this.fieldSlots = fieldSlots ?? new Dictionary<Id128, FieldSlotRecord>();
        }

        public int Count => entries.Count;

        public IReadOnlyList<ComponentOwnershipEntry> Entries => entries;

        public bool TryGetEntry(SchemaRef component, out ComponentOwnershipEntry? entry)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Component.Id.Value.Equals(component.Id.Value))
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>The one physical owner of a component; false when no layout declares it.</summary>
        public bool TryGetOwner(SchemaRef component, out OwnerId owner)
        {
            if (TryGetEntry(component, out ComponentOwnershipEntry? entry) && entry != null)
            {
                owner = entry.Owner;
                return true;
            }

            owner = default(OwnerId);
            return false;
        }

        /// <summary>Resolves one physical field to the state slot that owns it (P-033).</summary>
        public bool TryResolveField(SchemaRef component, Id128 fieldKey, out SlotId slot)
        {
            if (fieldSlots.TryGetValue(FieldSlotKey(component, fieldKey), out FieldSlotRecord record))
            {
                slot = record.Slot;
                return true;
            }

            slot = default(SlotId);
            return false;
        }

        /// <summary>
        /// Builds the map from layout declarations plus slot declarations. Every rejection is a value: a component
        /// with two physical owners, a duplicate field inside one layout, a field a layout does not declare, or two
        /// slots claiming one field.
        /// </summary>
        public static bool TryBuild(
            IReadOnlyList<ComponentLayoutDeclaration>? components,
            IReadOnlyList<SlotAuthorityDeclaration>? slots,
            out ComponentOwnershipMap? map,
            out IReadOnlyList<Diagnostic> diagnostics)
        {
            List<Diagnostic> collected = new List<Diagnostic>();
            var built = new List<ComponentOwnershipEntry>();
            var fieldOwners = new Dictionary<Id128, FieldSlotRecord>();
            var byComponent = new Dictionary<Id128, ComponentLayoutDeclaration>();

            if (components != null)
            {
                for (int i = 0; i < components.Count; i++)
                {
                    ComponentLayoutDeclaration declaration = components[i];
                    if (declaration.Component.Id.Value.IsDefault)
                    {
                        collected.Add(Reject(
                            DiagnosticCode.MissingDependency,
                            "a default zero component schema is not a layout identity (P-033)"));
                        continue;
                    }

                    if (declaration.Owner.Value.IsDefault)
                    {
                        collected.Add(Reject(
                            DiagnosticCode.MissingDependency,
                            "component layout " + declaration.Component
                            + " declares a default zero owner; one physical owner is required (P-033)"));
                    }

                    if (byComponent.TryGetValue(declaration.Component.Id.Value, out ComponentLayoutDeclaration existing))
                    {
                        if (!existing.Owner.Equals(declaration.Owner) || !existing.LayoutKey.Equals(declaration.LayoutKey))
                        {
                            collected.Add(Reject(
                                DiagnosticCode.OwnershipConflict,
                                "component " + declaration.Component
                                + " is declared by two physical layouts (" + existing.LayoutKey + "@" + existing.Owner
                                + " and " + declaration.LayoutKey + "@" + declaration.Owner
                                + "); one ECS component has one physical owner unless storage is split (P-033)"));
                        }

                        continue;
                    }

                    byComponent.Add(declaration.Component.Id.Value, declaration);

                    var fields = new List<FieldOwnership>(declaration.Fields.Count);
                    var seenFields = new HashSet<Id128>();
                    for (int f = 0; f < declaration.Fields.Count; f++)
                    {
                        FieldOwnership field = declaration.Fields[f];
                        if (!field.ComponentSchema.Id.Value.Equals(declaration.Component.Id.Value))
                        {
                            collected.Add(Reject(
                                DiagnosticCode.MissingDependency,
                                "component layout " + declaration.Component + " declares field "
                                + field.FieldKey + " of another component " + field.ComponentSchema));
                            continue;
                        }

                        if (!seenFields.Add(field.FieldKey))
                        {
                            collected.Add(Reject(
                                DiagnosticCode.OwnershipConflict,
                                "component layout " + declaration.Component + " declares field "
                                + field.FieldKey + " twice"));
                            continue;
                        }

                        fields.Add(field);
                    }

                    var slotIds = new List<SlotId>();
                    if (slots != null)
                    {
                        for (int s = 0; s < slots.Count; s++)
                        {
                            SlotAuthorityDeclaration slot = slots[s];
                            if (slot.Schema.Id.Value.Equals(declaration.Component.Id.Value)
                                && !ContainsSlot(slotIds, slot.SlotId))
                            {
                                slotIds.Add(slot.SlotId);
                            }
                        }
                    }

                    built.Add(new ComponentOwnershipEntry(
                        declaration.Component,
                        declaration.LayoutKey,
                        declaration.Owner,
                        fields,
                        slotIds));
                }
            }

            if (slots != null)
            {
                for (int s = 0; s < slots.Count; s++)
                {
                    SlotAuthorityDeclaration slot = slots[s];
                    for (int f = 0; f < slot.FieldOwnership.Count; f++)
                    {
                        FieldOwnership field = slot.FieldOwnership[f];
                        if (!byComponent.TryGetValue(field.ComponentSchema.Id.Value, out ComponentLayoutDeclaration layout))
                        {
                            collected.Add(Reject(
                                DiagnosticCode.MissingDependency,
                                "state slot " + slot.SlotId + " declares field " + field.FieldKey
                                + " of component " + field.ComponentSchema
                                + " which no layout declaration owns (P-033)"));
                            continue;
                        }

                        if (slot.HasPhysicalLayoutKey && !slot.PhysicalLayoutKey.Equals(layout.LayoutKey))
                        {
                            collected.Add(Reject(
                                DiagnosticCode.OwnershipConflict,
                                "state slot " + slot.SlotId + " names layout key " + slot.PhysicalLayoutKey
                                + " but component " + layout.Component + " is owned by layout " + layout.LayoutKey
                                + " (P-033)"));
                            continue;
                        }

                        Id128 key = FieldSlotKey(field.ComponentSchema, field.FieldKey);
                        if (fieldOwners.TryGetValue(key, out FieldSlotRecord existingRecord))
                        {
                            if (!existingRecord.Slot.Equals(slot.SlotId))
                            {
                                collected.Add(Reject(
                                    DiagnosticCode.OwnershipConflict,
                                    "component field " + layout.Component + "#" + field.FieldKey
                                    + " is claimed by slot " + existingRecord.Slot + " and slot " + slot.SlotId
                                    + "; a field has exactly one slot owner (P-033)"));
                            }

                            continue;
                        }

                        if (!DeclaresField(layout, field.FieldKey))
                        {
                            collected.Add(Reject(
                                DiagnosticCode.MissingDependency,
                                "state slot " + slot.SlotId + " claims field " + field.FieldKey
                                + " which layout " + layout.LayoutKey + " of component " + layout.Component
                                + " does not declare"));
                            continue;
                        }

                        fieldOwners.Add(key, new FieldSlotRecord(slot.SlotId));
                    }
                }
            }

            map = new ComponentOwnershipMap(built, fieldOwners);
            diagnostics = collected;
            return collected.Count == 0;
        }

        private static bool DeclaresField(ComponentLayoutDeclaration layout, Id128 fieldKey)
        {
            for (int i = 0; i < layout.Fields.Count; i++)
            {
                if (layout.Fields[i].FieldKey.Equals(fieldKey))
                {
                    return true;
                }
            }

            return false;
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

        private static Id128 FieldSlotKey(SchemaRef component, Id128 fieldKey)
            => new Id128(
                component.Id.Value.High ^ fieldKey.High ^ 0x4649454C444F574EUL,
                component.Id.Value.Low ^ fieldKey.Low);

        private static Diagnostic Reject(DiagnosticCode code, string summary)
            => Diagnostic.Create(code, OperationPhase.Validation, default(OperationId), summary);

        private readonly struct FieldSlotRecord
        {
            internal readonly SlotId Slot;

            internal FieldSlotRecord(SlotId slot)
            {
                Slot = slot;
            }
        }
    }
}
