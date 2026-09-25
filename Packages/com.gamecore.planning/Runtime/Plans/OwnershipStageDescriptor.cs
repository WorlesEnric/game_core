// GameCore.Planning — the frozen ownership/stage descriptor this task's planner consumes (GC-008).
//
// Wave 2 context: GC-007 owns the real ownership validator and its generated mutually exclusive partitions
// (`Runtime/Ownership/`), GC-009 owns the real stage/schedule compiler (`Runtime/Scheduling/`), and 09's
// "Parallel work" note requires every Wave 2 task to keep working through a frozen descriptor fixture until the
// W2 gate substitutes the real modules. This file is that fixture contract: a validated, immutable description of
//   * state slots and their single owner (P-032, P-034),
//   * stages with their systems, hosts, inner edges and backward stage edges (P-039, P-040),
//   * declared buffer ports (P-043),
// and nothing else. The real validator and compiler produce the same *shape* at the W2 gate; what is frozen here
// is the shape, not a second implementation of their rules.
//
// Validation is deliberately small but real, because the planner's plan hash must not depend on an unvalidated
// descriptor: duplicate stage indices, forward stage edges (a cycle in a backward-only order), one slot claimed by
// two owners, a slot with no owner, a duplicate system key inside a stage and a buffer consumer that is not a
// declared stage all reject with the protocol's own diagnostic code.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>One system entry of a descriptor stage: a generated registration key plus its host kind (P-039).</summary>
    public sealed class DescriptorSystem
    {
        public DescriptorSystem(
            FactoryKey key,
            SystemDispatchKind kind,
            IReadOnlyList<FactoryKey>? requiredBefore,
            IReadOnlyList<FactoryKey>? requiredAfter)
        {
            Key = key;
            Kind = kind;
            RequiredBefore = ContractCollections.Freeze(requiredBefore);
            RequiredAfter = ContractCollections.Freeze(requiredAfter);
        }

        public FactoryKey Key { get; }

        public SystemDispatchKind Kind { get; }

        /// <summary>Systems of the same stage that must run after this one (P-040 inner DAG).</summary>
        public IReadOnlyList<FactoryKey> RequiredBefore { get; }

        public IReadOnlyList<FactoryKey> RequiredAfter { get; }

        public override string ToString() => Key.ToString() + ":" + Kind;
    }

    /// <summary>One stage of the descriptor: its fence slot, systems and backward stage edges (P-039, P-040).</summary>
    public sealed class DescriptorStage
    {
        public DescriptorStage(
            StageId stage,
            uint version,
            int stageIndex,
            IReadOnlyList<DescriptorSystem>? systems,
            IReadOnlyList<int>? predecessorStages)
        {
            Stage = stage;
            Version = version;
            StageIndex = stageIndex;
            Systems = ContractCollections.Freeze(systems);
            PredecessorStages = ContractCollections.Freeze(predecessorStages);
        }

        public StageId Stage { get; }

        public uint Version { get; }

        /// <summary>Index into the host's native fence table; the compiled order is the descriptor order (P-040).</summary>
        public int StageIndex { get; }

        public IReadOnlyList<DescriptorSystem> Systems { get; }

        /// <summary>Stages whose completed fences are incoming edges; strictly lower indices (P-041).</summary>
        public IReadOnlyList<int> PredecessorStages { get; }

        public override string ToString() => Stage.ToString() + "@" + StageIndex.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One state slot of the descriptor: exactly one owner, one schema/version and an explicit last-support
    /// policy. There is no implicit default owner and no implicit zero initialisation (P-032, P-034).
    /// </summary>
    public sealed class OwnedSlotSpec
    {
        public OwnedSlotSpec(
            SlotId slot,
            OwnerId owner,
            SchemaRef schema,
            uint ownerVersion,
            LastSupportPolicy lastSupport,
            Id128 partition,
            FactoryKey versionChangePolicy = default(FactoryKey))
        {
            Slot = slot;
            Owner = owner;
            Schema = schema;
            OwnerVersion = ownerVersion;
            LastSupport = lastSupport;
            Partition = partition;
            VersionChangePolicy = versionChangePolicy;
        }

        public SlotId Slot { get; }

        public OwnerId Owner { get; }

        public SchemaRef Schema { get; }

        public uint OwnerVersion { get; }

        public LastSupportPolicy LastSupport { get; }

        /// <summary>
        /// Registered migration key this slot uses when its state crosses a schema version (P-032, 05 s5
        /// `Migrate_*`). A default key means the descriptor declares no such policy, which the planner then reports
        /// as `MigrationRequired` instead of zero-initialising the value.
        /// </summary>
        public FactoryKey VersionChangePolicy { get; }

        /// <summary>True when this slot declares a registered version-change migration key (P-032).</summary>
        public bool HasVersionChangePolicy => !VersionChangePolicy.RegistrationKey.IsDefault;

        /// <summary>Validated disjoint partition of the slot, when the generator assigns one (P-034).</summary>
        public Id128 Partition { get; }

        public bool IsPartitioned => !Partition.IsDefault;

        public override string ToString() => Slot.ToString() + ":" + Owner.ToString();
    }

    /// <summary>
    /// Immutable, validated ownership/stage descriptor of one catalog revision. `Revision` is a content hash of
    /// its own canonical text, so a plan that was validated against one descriptor is stale against another (P-028).
    /// </summary>
    public sealed class OwnershipStageDescriptor
    {
        public OwnershipStageDescriptor(
            ContentHash revision,
            IReadOnlyList<OwnedSlotSpec>? slots,
            IReadOnlyList<DescriptorStage>? stages,
            IReadOnlyList<BufferBinding>? buffers)
        {
            Revision = revision;
            Slots = ContractCollections.Freeze(slots);
            Stages = ContractCollections.Freeze(stages);
            Buffers = ContractCollections.Freeze(buffers);
        }

        public ContentHash Revision { get; }

        public IReadOnlyList<OwnedSlotSpec> Slots { get; }

        public IReadOnlyList<DescriptorStage> Stages { get; }

        public IReadOnlyList<BufferBinding> Buffers { get; }

        /// <summary>Stage fence slots this descriptor needs; one per declared stage index (P-041).</summary>
        public int StageCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Stages.Count; i++)
                {
                    if (Stages[i].StageIndex + 1 > count)
                    {
                        count = Stages[i].StageIndex + 1;
                    }
                }

                return count;
            }
        }

        /// <summary>Resolves one slot's owner spec; a miss is a value, never a substituted default (P-032).</summary>
        public bool TryGetSlot(SlotId slot, out OwnedSlotSpec? spec)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].Slot.Equals(slot))
                {
                    spec = Slots[i];
                    return true;
                }
            }

            spec = null;
            return false;
        }

        /// <summary>Resolves one stage by identity; a miss means the stage is not declared in this revision (P-039).</summary>
        public bool TryGetStage(StageId stage, out DescriptorStage? found)
        {
            for (int i = 0; i < Stages.Count; i++)
            {
                if (Stages[i].Stage.Equals(stage))
                {
                    found = Stages[i];
                    return true;
                }
            }

            found = null;
            return false;
        }

        /// <summary>
        /// Descriptor validation (P-028, P-034, P-039, P-040, P-043). The planner refuses to build a plan against a
        /// descriptor that fails this, so a plan hash never encodes an ambiguous ownership or execution graph.
        /// </summary>
        public bool TryValidate(out DiagnosticCode code, out string detail)
        {
            if (Revision.IsEmpty)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the descriptor declares no revision, so a plan validated against it could not be revalidated (P-028).";
                return false;
            }

            // One slot, one logical owner; a partition is optional but must be a real identity when present (P-034).
            for (int i = 0; i < Slots.Count; i++)
            {
                OwnedSlotSpec spec = Slots[i];
                if (spec.Slot.IsDefault)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "a state slot with a default zero identity is not a stable identity (P-004).";
                    return false;
                }

                if (spec.Owner.IsDefault)
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "slot " + spec.Slot.ToString() + " declares no owning authority (P-034).";
                    return false;
                }

                if (spec.Schema.Id.IsDefault)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "slot " + spec.Slot.ToString() + " declares no schema identity (P-032).";
                    return false;
                }

                for (int j = i + 1; j < Slots.Count; j++)
                {
                    if (!Slots[j].Slot.Equals(spec.Slot))
                    {
                        continue;
                    }

                    if (!Slots[j].Owner.Equals(spec.Owner))
                    {
                        code = DiagnosticCode.OwnershipConflict;
                        detail = "slot " + spec.Slot.ToString() + " is claimed by two owners ("
                            + spec.Owner.ToString() + " and " + Slots[j].Owner.ToString() + "); P-034 allows one owner "
                            + "unless ownership is explicitly transferred.";
                        return false;
                    }

                    if (!Slots[j].Schema.Equals(spec.Schema))
                    {
                        code = DiagnosticCode.UnsupportedVersion;
                        detail = "slot " + spec.Slot.ToString() + " is declared with two schema versions ("
                            + spec.Schema.ToString() + " and " + Slots[j].Schema.ToString() + ").";
                        return false;
                    }
                }
            }

            // Stage indices are the fence slots and the compiled order: unique, dense from zero, backward-only edges.
            var stageIndices = new HashSet<int>();
            for (int i = 0; i < Stages.Count; i++)
            {
                DescriptorStage stage = Stages[i];
                if (stage.Stage.IsDefault)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "a stage with a default zero identity is not a stable identity (P-004).";
                    return false;
                }

                if (stage.StageIndex < 0)
                {
                    code = DiagnosticCode.AmbiguousOrder;
                    detail = "stage " + stage.Stage.ToString() + " declares a negative fence index.";
                    return false;
                }

                if (!stageIndices.Add(stage.StageIndex))
                {
                    code = DiagnosticCode.AmbiguousOrder;
                    detail = "two stages declare fence index " + stage.StageIndex.ToString(CultureInfo.InvariantCulture)
                        + "; one index is one fence slot (P-040).";
                    return false;
                }

                for (int p = 0; p < stage.PredecessorStages.Count; p++)
                {
                    int predecessor = stage.PredecessorStages[p];
                    if (predecessor < 0 || predecessor >= stage.StageIndex)
                    {
                        code = DiagnosticCode.Cycle;
                        detail = "stage " + stage.Stage.ToString() + " declares predecessor index "
                            + predecessor.ToString(CultureInfo.InvariantCulture)
                            + ", which is not strictly below its own index; a backward-only order cannot contain a cycle (P-040).";
                        return false;
                    }
                }

                if (!ValidateSystems(stage, out code, out detail))
                {
                    return false;
                }
            }

            for (int i = 0; i < Buffers.Count; i++)
            {
                BufferBinding binding = Buffers[i];
                if (binding.Buffer.IsDefault)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "a buffer binding with a default zero identity is not a stable identity (P-004).";
                    return false;
                }

                if (!TryGetStage(binding.ConsumerStage, out _))
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "buffer " + binding.Buffer.ToString()
                        + " names consumer stage " + binding.ConsumerStage.ToString()
                        + ", which the descriptor does not declare; no consumer reads a buffer before its producer completes (P-043).";
                    return false;
                }
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        /// <summary>Canonical fingerprint of the descriptor contents, used as the plan's catalog/descriptor input (P-028).</summary>
        public ContentHash Fingerprint()
        {
            var builder = new StringBuilder();
            builder.Append("descriptor\n");
            builder.Append("revision=").Append(PlanHashing.HashText(Revision)).Append('\n');

            var slots = new List<string>(Slots.Count);
            for (int i = 0; i < Slots.Count; i++)
            {
                OwnedSlotSpec spec = Slots[i];
                slots.Add(
                    PlanHashing.IdText(spec.Slot.Value) + ";"
                    + PlanHashing.IdText(spec.Owner.Value) + ";"
                    + PlanHashing.IdText(spec.Schema.Id.Value) + ";"
                    + spec.Schema.Version.ToString(CultureInfo.InvariantCulture) + ";"
                    + spec.OwnerVersion.ToString(CultureInfo.InvariantCulture) + ";"
                    + spec.LastSupport.ToString() + ";"
                    + PlanHashing.IdText(spec.Partition));
            }

            slots.Sort(StringComparer.Ordinal);
            for (int i = 0; i < slots.Count; i++)
            {
                builder.Append("slot=").Append(slots[i]).Append('\n');
            }

            var stages = new List<string>(Stages.Count);
            for (int i = 0; i < Stages.Count; i++)
            {
                DescriptorStage stage = Stages[i];
                var systems = new List<string>(stage.Systems.Count);
                for (int s = 0; s < stage.Systems.Count; s++)
                {
                    DescriptorSystem system = stage.Systems[s];
                    systems.Add(
                        PlanHashing.IdText(system.Key.RegistrationKey) + ";"
                        + system.Key.KeyVersion.ToString(CultureInfo.InvariantCulture) + ";"
                        + system.Kind.ToString());
                }

                systems.Sort(StringComparer.Ordinal);
                var predecessors = new List<string>(stage.PredecessorStages.Count);
                for (int p = 0; p < stage.PredecessorStages.Count; p++)
                {
                    predecessors.Add(stage.PredecessorStages[p].ToString(CultureInfo.InvariantCulture));
                }

                predecessors.Sort(StringComparer.Ordinal);
                stages.Add(
                    PlanHashing.IdText(stage.Stage.Value) + ";"
                    + stage.Version.ToString(CultureInfo.InvariantCulture) + ";"
                    + stage.StageIndex.ToString(CultureInfo.InvariantCulture) + ";"
                    + string.Join("|", systems.ToArray()) + ";"
                    + string.Join("|", predecessors.ToArray()));
            }

            stages.Sort(StringComparer.Ordinal);
            for (int i = 0; i < stages.Count; i++)
            {
                builder.Append("stage=").Append(stages[i]).Append('\n');
            }

            var buffers = new List<string>(Buffers.Count);
            for (int i = 0; i < Buffers.Count; i++)
            {
                BufferBinding binding = Buffers[i];
                var producers = new List<string>(binding.Producers.Count);
                for (int p = 0; p < binding.Producers.Count; p++)
                {
                    producers.Add(
                        PlanHashing.IdText(binding.Producers[p].RegistrationKey) + ";"
                        + binding.Producers[p].KeyVersion.ToString(CultureInfo.InvariantCulture));
                }

                producers.Sort(StringComparer.Ordinal);
                buffers.Add(
                    PlanHashing.IdText(binding.Buffer.Value) + ";"
                    + PlanHashing.IdText(binding.ConsumerStage.Value) + ";"
                    + string.Join("|", producers.ToArray()));
            }

            buffers.Sort(StringComparer.Ordinal);
            for (int i = 0; i < buffers.Count; i++)
            {
                builder.Append("buffer=").Append(buffers[i]).Append('\n');
            }

            return PlanHashing.Of(builder.ToString());
        }

        private bool ValidateSystems(DescriptorStage stage, out DiagnosticCode code, out string detail)
        {
            var keys = new HashSet<Id128>();
            for (int i = 0; i < stage.Systems.Count; i++)
            {
                DescriptorSystem system = stage.Systems[i];
                if (system.Key.RegistrationKey.IsDefault)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "stage " + stage.Stage.ToString() + " declares a system with a default zero registration key.";
                    return false;
                }

                if (!keys.Add(system.Key.RegistrationKey))
                {
                    // One precompiled system type has one live scheduling instance per world in V1 (04 s4).
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "stage " + stage.Stage.ToString()
                        + " declares system key " + system.Key.ToString()
                        + " twice; one system type occupies one position in the compiled order (04 s4).";
                    return false;
                }

                if (!Declares(stage, system.RequiredAfter))
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "stage " + stage.Stage.ToString() + " names an inner edge to a system it does not declare (P-040).";
                    return false;
                }

                if (!Declares(stage, system.RequiredBefore))
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "stage " + stage.Stage.ToString() + " names an inner edge to a system it does not declare (P-040).";
                    return false;
                }
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        private static bool Declares(DescriptorStage stage, IReadOnlyList<FactoryKey> keys)
        {
            for (int i = 0; i < keys.Count; i++)
            {
                bool found = false;
                for (int s = 0; s < stage.Systems.Count; s++)
                {
                    if (stage.Systems[s].Key.Equals(keys[i]))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
