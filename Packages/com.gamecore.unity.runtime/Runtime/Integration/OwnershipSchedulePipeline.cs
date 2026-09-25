// GameCore.Unity.Runtime — W2 integration seam: the real ownership validator (GC-007) and the real stage compiler
// (GC-009) producing the frozen ownership/stage descriptor GC-008's planner consumes.
//
// GC-008 deliberately works through a frozen descriptor fixture until the W2 exit gate substitutes the real
// modules (09 GC-008, "Parallel work"). This file is that substitution, and the descriptor it returns is built from
// the real modules' output rather than from a literal:
//
//   1. the mounted manifests' declared stages, systems, buffers and state slots are the input (they are catalog
//      data, P-039/P-043);
//   2. every per-partition writer's partition id is generated with GC-007's `PartitionIdGenerator` and written into
//      the access declarations, so GC-009's disjointness proof and GC-007's own proof are the *same* claim about
//      the *same* ids (P-034, P-040);
//   3. `ScheduleCompiler.Compile` compiles the declaration set into the one stable schedule (P-040) and
//      `CompiledScheduleAdapter.Adapt` turns it into the executable dispatch table (04 section 4);
//   4. `OwnerAuthorityValidator.Validate` validates one owner per domain and proves each writer pair ordered (by the
//      compiled stage order) or provably disjoint (by partition), and `SlotPolicyValidator` validates each state
//      slot's declared policies and its version-change request against a *registered* migration (P-032);
//   5. the descriptor's stages/systems/edges come from the compiled schedule, and its slots come from the validated
//      slot declarations; its revision is a content hash of exactly those inputs (P-028).
//
// Nothing here invents a stage, an owner, a partition or a migration: a declaration the model cannot express is a
// refusal with the protocol's own diagnostic code, never a substituted default.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Planning.Scheduling;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>How one ownership/schedule descriptor was produced from the real validators and compiler.</summary>
    public enum PipelineDescriptorOutcome
    {
        /// <summary>The descriptor was built; every real validator accepted the declarations.</summary>
        Built = 0,

        /// <summary>No manifest declares a stage, a buffer or a state slot, so there is nothing to compile.</summary>
        NoDeclarations = 1,

        /// <summary>Two manifests declare one identity differently (P-009, P-039).</summary>
        DuplicateDeclaration = 2,

        /// <summary>A writer claims a domain no state slot declares, or one writer claims two owners (P-034).</summary>
        UnmappedDomain = 3,

        /// <summary>A declared slot policy is incomplete, or a version change has no registered migration (P-032).</summary>
        SlotPolicyRejected = 4,

        /// <summary>The ownership validator refused the declaration set (P-034, P-040).</summary>
        OwnershipRejected = 5,

        /// <summary>The stage compiler refused the declaration set, with edge witnesses (P-028, P-040).</summary>
        ScheduleRejected = 6,

        /// <summary>The compiled schedule could not be adapted into a dispatch table (P-028, P-040).</summary>
        AdaptationRejected = 7,
    }

    /// <summary>
    /// Result of one descriptor build, carrying every real module's own verdict beside the descriptor so a caller
    /// can assert on the validators' output instead of only on the product.
    /// </summary>
    public sealed class PipelineDescriptorReport
    {
        private PipelineDescriptorReport(
            PipelineDescriptorOutcome outcome,
            OwnershipStageDescriptor? descriptor,
            OwnershipReport? ownership,
            IReadOnlyList<SlotPolicyResult> slotPolicies,
            ScheduleCompilation? compilation,
            ScheduleAdaptation? adaptation,
            DiagnosticCode code,
            string detail)
        {
            Outcome = outcome;
            Descriptor = descriptor;
            Ownership = ownership;
            SlotPolicies = slotPolicies;
            Compilation = compilation;
            Adaptation = adaptation;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public PipelineDescriptorOutcome Outcome { get; }

        public bool Succeeded => Outcome == PipelineDescriptorOutcome.Built;

        /// <summary>The frozen shape GC-008's planner consumes, built from the real modules' output.</summary>
        public OwnershipStageDescriptor? Descriptor { get; }

        /// <summary>GC-007's own report: one owner per domain, canonical partitions, component ownership.</summary>
        public OwnershipReport? Ownership { get; }

        /// <summary>GC-007's per-slot verdicts, in canonical slot order.</summary>
        public IReadOnlyList<SlotPolicyResult> SlotPolicies { get; }

        /// <summary>GC-009's compilation, carrying the schedule and any edge witnesses.</summary>
        public ScheduleCompilation? Compilation { get; }

        /// <summary>GC-009's adapted dispatch table and native buffer slots.</summary>
        public ScheduleAdaptation? Adaptation { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        internal static PipelineDescriptorReport Built(
            OwnershipStageDescriptor descriptor,
            OwnershipReport ownership,
            IReadOnlyList<SlotPolicyResult> slotPolicies,
            ScheduleCompilation compilation,
            ScheduleAdaptation adaptation)
            => new PipelineDescriptorReport(
                PipelineDescriptorOutcome.Built,
                descriptor,
                ownership,
                slotPolicies,
                compilation,
                adaptation,
                DiagnosticCode.None,
                string.Empty);

        internal static PipelineDescriptorReport Refused(
            PipelineDescriptorOutcome outcome,
            OwnershipReport? ownership,
            IReadOnlyList<SlotPolicyResult>? slotPolicies,
            ScheduleCompilation? compilation,
            ScheduleAdaptation? adaptation,
            DiagnosticCode code,
            string detail)
            => new PipelineDescriptorReport(
                outcome,
                null,
                ownership,
                slotPolicies ?? Array.Empty<SlotPolicyResult>(),
                compilation,
                adaptation,
                code,
                detail);

        public string Describe()
        {
            if (!Succeeded)
            {
                return "descriptor refused(" + Outcome.ToString() + ", " + DiagnosticCodeText.Of(Code) + "): " + Detail;
            }

            return "descriptor(" + Descriptor!.Stages.Count.ToString(CultureInfo.InvariantCulture) + " stages, "
                + Descriptor.Slots.Count.ToString(CultureInfo.InvariantCulture) + " slots, "
                + Descriptor.Buffers.Count.ToString(CultureInfo.InvariantCulture) + " buffers, revision "
                + Descriptor.Revision.ToHex() + ")";
        }
    }

    /// <summary>
    /// Builds the frozen ownership/stage descriptor from the real GC-007 validator and GC-009 compiler.
    /// </summary>
    public static class OwnershipSchedulePipeline
    {
        /// <summary>
        /// Compiles, validates and describes one catalog revision's execution and ownership surface.
        /// </summary>
        /// <param name="manifests">Every active installation's manifest; the union of their declarations is the input.</param>
        /// <param name="kinds">Generated dispatch kinds of the world's system registrations (04 section 8).</param>
        /// <param name="migrations">Registered migration executors of this catalog revision (GC-007's registry view).</param>
        public static PipelineDescriptorReport Build(
            IReadOnlyList<PluginManifest>? manifests,
            IScheduleDispatchKindResolver kinds,
            ISlotMigrationRegistry? migrations)
        {
            if (kinds == null)
            {
                throw new ArgumentNullException(nameof(kinds));
            }

            var stageSpecs = new List<StageSpec>();
            var bufferSpecs = new List<BufferSpec>();
            var slotSpecs = new List<StateSlotSpec>();
            if (manifests != null)
            {
                for (int i = 0; i < manifests.Count; i++)
                {
                    PluginManifest manifest = manifests[i];
                    for (int s = 0; s < manifest.Stages.Count; s++)
                    {
                        stageSpecs.Add(manifest.Stages[s]);
                    }

                    for (int b = 0; b < manifest.Buffers.Count; b++)
                    {
                        bufferSpecs.Add(manifest.Buffers[b]);
                    }

                    for (int l = 0; l < manifest.StateSlots.Count; l++)
                    {
                        StateSlotSpec spec = manifest.StateSlots[l];
                        if (!TryAddSlot(slotSpecs, spec, out DiagnosticCode slotCode, out string slotDetail))
                        {
                            return PipelineDescriptorReport.Refused(
                                PipelineDescriptorOutcome.DuplicateDeclaration, null, null, null, null, slotCode, slotDetail);
                        }
                    }
                }
            }

            if (stageSpecs.Count == 0 && bufferSpecs.Count == 0 && slotSpecs.Count == 0)
            {
                return PipelineDescriptorReport.Refused(
                    PipelineDescriptorOutcome.NoDeclarations, null, null, null, null,
                    DiagnosticCode.MissingDependency,
                    "no mounted manifest declares a stage, a buffer or a state slot, so this catalog revision has no"
                    + " execution or ownership surface to compile (P-039).");
            }

            // 1. One owner per authoritative domain, from the state-slot declarations (P-034).
            var ownerOfDomain = new Dictionary<Id128, OwnerId>();
            for (int i = 0; i < slotSpecs.Count; i++)
            {
                StateSlotSpec spec = slotSpecs[i];
                if (spec.Schema.Id.IsDefault || spec.Owner.IsDefault)
                {
                    return PipelineDescriptorReport.Refused(
                        PipelineDescriptorOutcome.UnmappedDomain, null, null, null, null,
                        DiagnosticCode.MissingDependency,
                        "state slot " + spec.SlotId.ToString() + " declares a default schema or owner identity (P-004).");
                }

                if (ownerOfDomain.TryGetValue(spec.Schema.Id.Value, out OwnerId existing))
                {
                    if (!existing.Equals(spec.Owner))
                    {
                        return PipelineDescriptorReport.Refused(
                            PipelineDescriptorOutcome.UnmappedDomain, null, null, null, null,
                            DiagnosticCode.OwnershipConflict,
                            "domain " + spec.Schema.ToString() + " is declared by two owners ("
                            + existing.ToString() + " and " + spec.Owner.ToString() + "); P-034 allows one (P-034).");
                    }

                    continue;
                }

                ownerOfDomain.Add(spec.Schema.Id.Value, spec.Owner);
            }

            // 2. Every per-partition writer's partition id, generated once by GC-007 and written into the access
            //    declaration GC-009 validates, so both modules prove the same disjointness claim (P-034, P-040).
            var generator = new PartitionIdGenerator();
            var partitionRewrites = 0;
            var rewrittenStages = new List<StageSpec>(stageSpecs.Count);
            for (int i = 0; i < stageSpecs.Count; i++)
            {
                StageSpec stage = stageSpecs[i];
                var systems = new List<SystemSpec>(stage.Systems.Count);
                for (int s = 0; s < stage.Systems.Count; s++)
                {
                    SystemSpec system = stage.Systems[s];
                    var declarations = new List<AccessDeclaration>(system.Access.Declarations.Count);
                    bool rewritten = false;
                    for (int d = 0; d < system.Access.Declarations.Count; d++)
                    {
                        AccessDeclaration declaration = system.Access.Declarations[d];
                        if (declaration.Mode == AccessMode.Read
                            || !declaration.PartitionId.IsDefault
                            || system.Multiplicity != SystemMultiplicity.PerPartition)
                        {
                            declarations.Add(declaration);
                            continue;
                        }

                        if (!TryOwnerOf(ownerOfDomain, declaration.Schema, out OwnerId owner))
                        {
                            return PipelineDescriptorReport.Refused(
                                PipelineDescriptorOutcome.UnmappedDomain, null, null, null, null,
                                DiagnosticCode.MissingDependency,
                                "system " + system.SystemKey.ToString() + " in stage " + stage.StageId.ToString()
                                + " writes domain " + declaration.Schema.ToString()
                                + " which no state-slot declaration owns; an undeclared domain has no authority (P-034).");
                        }

                        declarations.Add(new AccessDeclaration(
                            declaration.Schema,
                            declaration.Mode,
                            generator.Next(owner, declaration.Schema, system.SystemKey)));
                        rewritten = true;
                        partitionRewrites++;
                    }

                    systems.Add(rewritten
                        ? new SystemSpec(
                            system.SystemKey,
                            system.Multiplicity,
                            new AccessSet(declarations),
                            system.RequiredBeforeSystems,
                            system.RequiredAfterSystems,
                            system.OptionalBeforeSystems,
                            system.OptionalAfterSystems)
                        : system);
                }

                rewrittenStages.Add(new StageSpec(
                    stage.StageId,
                    stage.StageVersion,
                    stage.OwnerPackageId,
                    stage.Affinity,
                    stage.FactoryKeys,
                    stage.ActivationMemberships,
                    stage.ReadWriteSet,
                    stage.RequiredBefore,
                    stage.RequiredAfter,
                    stage.OptionalBefore,
                    stage.OptionalAfter,
                    systems,
                    stage.BufferPorts));
            }

            // 3. Compile the declarations into the one stable schedule (P-040) and adapt it for dispatch.
            ScheduleCompilation compilation = ScheduleCompiler.Compile(
                new ScheduleDeclarations(rewrittenStages, bufferSpecs));
            if (!compilation.Succeeded || compilation.Schedule == null)
            {
                return PipelineDescriptorReport.Refused(
                    PipelineDescriptorOutcome.ScheduleRejected, null, null, compilation, null,
                    compilation.Code,
                    "the stage compiler refused the declaration set: " + compilation.Detail
                    + " (" + compilation.Witnesses.Count.ToString(CultureInfo.InvariantCulture) + " witness(es))");
            }

            CompiledSchedule schedule = compilation.Schedule;
            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(schedule, kinds);
            if (!adaptation.Succeeded || adaptation.StepPlan == null)
            {
                return PipelineDescriptorReport.Refused(
                    PipelineDescriptorOutcome.AdaptationRejected, null, null, compilation, adaptation,
                    adaptation.Code,
                    "the compiled schedule could not be adapted into a dispatch table: " + adaptation.Explain());
            }

            // 4. The declared writers, their generated owners and the compiled stage order, validated by GC-007.
            var writers = new List<WriterDeclaration>();
            for (int i = 0; i < rewrittenStages.Count; i++)
            {
                StageSpec stage = rewrittenStages[i];
                for (int s = 0; s < stage.Systems.Count; s++)
                {
                    SystemSpec system = stage.Systems[s];
                    if (!TryWriterOwner(ownerOfDomain, system, out OwnerId owner, out DiagnosticCode writerCode, out string writerDetail))
                    {
                        return PipelineDescriptorReport.Refused(
                            PipelineDescriptorOutcome.UnmappedDomain, null, null, compilation, adaptation,
                            writerCode, writerDetail);
                    }

                    if (!WritesAnyDomain(system))
                    {
                        // A read-only or access-free system claims no authority, so it is not a `WriterDeclaration`
                        // at all (P-034); declaring it with a default owner would be a false claim.
                        continue;
                    }

                    writers.Add(new WriterDeclaration(
                        stage.StageId,
                        system.SystemKey,
                        owner,
                        system.Access,
                        system.Multiplicity,
                        system.RequiredBeforeSystems,
                        system.RequiredAfterSystems));
                }
            }

            var slotDeclarations = new List<SlotAuthorityDeclaration>(slotSpecs.Count);
            var slotPolicies = new List<SlotPolicyResult>(slotSpecs.Count);
            for (int i = 0; i < slotSpecs.Count; i++)
            {
                StateSlotSpec spec = slotSpecs[i];
                SlotAuthorityDeclaration declaration = SlotAuthorityDeclaration.FromSpec(spec, OptionsOf(spec));
                SlotPolicyResult declared = SlotPolicyValidator.ValidateDeclaration(declaration);
                slotPolicies.Add(declared);
                if (!declared.Succeeded)
                {
                    return PipelineDescriptorReport.Refused(
                        PipelineDescriptorOutcome.SlotPolicyRejected, null, slotPolicies, compilation, adaptation,
                        declared.Code,
                        "state slot " + spec.SlotId.ToString() + " declares an incomplete policy set: " + declared.Detail);
                }

                if (spec.Schema.Version > 1U && !declaration.DeclaresAnyMigration())
                {
                    return PipelineDescriptorReport.Refused(
                        PipelineDescriptorOutcome.SlotPolicyRejected, null, slotPolicies, compilation, adaptation,
                        DiagnosticCode.MigrationRequired,
                        "state slot " + spec.SlotId.ToString() + " declares schema " + spec.Schema.ToString()
                        + " but no migration key, so a version change could only zero-initialise live state (P-032).");
                }

                if (declaration.DeclaresAnyMigration())
                {
                    FactoryKey migrationKey = FirstMigrationKey(spec);
                    SlotPolicyResult request = SlotPolicyValidator.Validate(
                        declaration,
                        SlotPolicyRequest.VersionChange(spec.Schema, migrationKey),
                        migrations);
                    slotPolicies.Add(request);
                    if (!request.Succeeded || request.Outcome != SlotPolicyOutcome.Migrate)
                    {
                        return PipelineDescriptorReport.Refused(
                            PipelineDescriptorOutcome.SlotPolicyRejected, null, slotPolicies, compilation, adaptation,
                            request.Code == DiagnosticCode.None ? DiagnosticCode.MigrationRequired : request.Code,
                            "state slot " + spec.SlotId.ToString() + " declared migration " + migrationKey.ToString()
                            + " resolved to " + request.Outcome.ToString() + ": " + request.Detail
                            + " (P-032 requires a registered executor for a declared migration).");
                    }
                }
            }

            var components = new List<ComponentLayoutDeclaration>();
            if (!TryBuildComponents(slotSpecs, out components, out DiagnosticCode componentCode, out string componentDetail))
            {
                return PipelineDescriptorReport.Refused(
                    PipelineDescriptorOutcome.OwnershipRejected, null, slotPolicies, compilation, adaptation,
                    componentCode, componentDetail);
            }

            var stageOrder = new List<StageId>(schedule.Stages.Count);
            for (int i = 0; i < schedule.Stages.Count; i++)
            {
                stageOrder.Add(schedule.Stages[i].Stage);
            }

            OwnershipReport ownership = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, slotDeclarations, components, stageOrder));
            if (!ownership.IsValid)
            {
                Diagnostic first = ownership.Diagnostics.Count > 0
                    ? ownership.Diagnostics[0]
                    : null!;
                return PipelineDescriptorReport.Refused(
                    PipelineDescriptorOutcome.OwnershipRejected, ownership, slotPolicies, compilation, adaptation,
                    first != null ? first.Code : DiagnosticCode.OwnershipConflict,
                    "the ownership validator refused the declaration set: "
                    + (first != null ? first.Summary : ownership.Describe()));
            }

            // 5. The descriptor: stages and edges are the compiled schedule's, slots are the validated declarations'.
            var descriptorSlots = new List<OwnedSlotSpec>(slotSpecs.Count);
            for (int i = 0; i < slotSpecs.Count; i++)
            {
                StateSlotSpec spec = slotSpecs[i];
                descriptorSlots.Add(new OwnedSlotSpec(
                    spec.SlotId,
                    spec.Owner,
                    spec.Schema,
                    spec.PhysicalLayoutKey.KeyVersion == 0U ? 1U : spec.PhysicalLayoutKey.KeyVersion,
                    spec.LastSupport,
                    PartitionOf(ownership, spec),
                    FirstMigrationKeyOrDefault(spec)));
            }

            var descriptorStages = new List<DescriptorStage>(schedule.Stages.Count);
            for (int i = 0; i < schedule.Stages.Count; i++)
            {
                ScheduleStage stage = schedule.Stages[i];
                var systems = new List<DescriptorSystem>(stage.Systems.Count);
                for (int s = 0; s < stage.Systems.Count; s++)
                {
                    ScheduleEntry entry = stage.Systems[s];
                    if (!kinds.TryResolveKind(entry.SystemKey, out SystemDispatchKind kind))
                    {
                        return PipelineDescriptorReport.Refused(
                            PipelineDescriptorOutcome.AdaptationRejected, ownership, slotPolicies, compilation, adaptation,
                            DiagnosticCode.MissingDependency,
                            "compiled entry " + entry.ToString() + " has no generated dispatch kind (04 section 8).");
                    }

                    systems.Add(new DescriptorSystem(entry.SystemKey, kind, null, entry.PredecessorSystems));
                }

                descriptorStages.Add(new DescriptorStage(
                    stage.Stage,
                    stage.Version,
                    stage.StageIndex,
                    systems,
                    stage.PredecessorStages));
            }

            ContentHash revision = DescriptorRevision(schedule, descriptorSlots);
            var descriptor = new OwnershipStageDescriptor(
                revision,
                descriptorSlots,
                descriptorStages,
                schedule.BufferBindings);

            if (!descriptor.TryValidate(out DiagnosticCode descriptorCode, out string descriptorDetail))
            {
                return PipelineDescriptorReport.Refused(
                    PipelineDescriptorOutcome.ScheduleRejected, ownership, slotPolicies, compilation, adaptation,
                    descriptorCode,
                    "the descriptor built from the compiled schedule is not valid: " + descriptorDetail);
            }

            _ = partitionRewrites;
            return PipelineDescriptorReport.Built(descriptor, ownership, slotPolicies, compilation, adaptation);
        }

        /// <summary>
        /// Content hash of one descriptor: the compiled schedule's own semantic hash plus every validated slot
        /// declaration, so a plan validated against one descriptor is stale against another (P-008, P-028).
        /// </summary>
        public static ContentHash DescriptorRevision(
            CompiledSchedule schedule,
            IReadOnlyList<OwnedSlotSpec> slots)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            if (slots == null)
            {
                throw new ArgumentNullException(nameof(slots));
            }

            var text = new StringBuilder();
            text.Append("ownership-stage-descriptor\nschedule=").Append(schedule.Hash.ToHex()).Append('\n');
            for (int i = 0; i < slots.Count; i++)
            {
                OwnedSlotSpec slot = slots[i];
                text.Append("slot=").Append(slot.Slot.Value.ToString())
                    .Append(';').Append(slot.Owner.Value.ToString())
                    .Append(';').Append(slot.Schema.Id.Value.ToString())
                    .Append('@').Append(slot.Schema.Version.ToString(CultureInfo.InvariantCulture))
                    .Append(';').Append(slot.LastSupport.ToString())
                    .Append(';').Append(slot.Partition.ToString())
                    .Append(';').Append(slot.VersionChangePolicy.RegistrationKey.ToString())
                    .Append('@').Append(slot.VersionChangePolicy.KeyVersion.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            return PlanHashing.Of(text.ToString());
        }

        private static bool TryAddSlot(
            List<StateSlotSpec> slots,
            StateSlotSpec spec,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].SlotId.Equals(spec.SlotId))
                {
                    continue;
                }

                bool identical = slots[i].Owner.Equals(spec.Owner) && slots[i].Schema.Equals(spec.Schema);
                if (identical)
                {
                    return true;
                }

                code = DiagnosticCode.OwnershipConflict;
                detail = "state slot " + spec.SlotId.ToString() + " is declared twice with different owners or schemas;"
                    + " one slot has one declaration per catalog revision (P-032, P-034).";
                return false;
            }

            slots.Add(spec);
            return true;
        }

        private static bool TryOwnerOf(Dictionary<Id128, OwnerId> owners, SchemaRef schema, out OwnerId owner)
            => owners.TryGetValue(schema.Id.Value, out owner);

        /// <summary>True when one declared system writes at least one domain, i.e. it claims authority (P-034).</summary>
        private static bool WritesAnyDomain(SystemSpec system)
        {
            for (int i = 0; i < system.Access.Declarations.Count; i++)
            {
                if (system.Access.Declarations[i].Mode != AccessMode.Read)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The single logical owner one declared system writes for: every written domain of that system must name
        /// the same owner, because a writer is the grant of one owner (P-034).
        /// </summary>
        private static bool TryWriterOwner(
            Dictionary<Id128, OwnerId> owners,
            SystemSpec system,
            out OwnerId owner,
            out DiagnosticCode code,
            out string detail)
        {
            owner = default(OwnerId);
            code = DiagnosticCode.None;
            detail = string.Empty;
            bool decided = false;
            for (int i = 0; i < system.Access.Declarations.Count; i++)
            {
                AccessDeclaration declaration = system.Access.Declarations[i];
                if (declaration.Mode == AccessMode.Read)
                {
                    continue;
                }

                if (!TryOwnerOf(owners, declaration.Schema, out OwnerId candidate))
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "system " + system.SystemKey.ToString() + " writes domain " + declaration.Schema.ToString()
                        + " which no state-slot declaration owns; an undeclared domain has no authority (P-034).";
                    return false;
                }

                if (!decided)
                {
                    owner = candidate;
                    decided = true;
                    continue;
                }

                if (!owner.Equals(candidate))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "system " + system.SystemKey.ToString() + " writes domains owned by "
                        + owner.ToString() + " and " + candidate.ToString()
                        + "; a writer declaration carries the single logical owner it acts for (P-034).";
                    return false;
                }
            }

            return true;
        }

        private static SlotAuthorityOptions OptionsOf(StateSlotSpec spec)
        {
            switch (spec.LastSupport)
            {
                case LastSupportPolicy.PreserveDormant:
                    return SlotAuthorityOptions.Dormant();
                case LastSupportPolicy.RemoveDerived:
                    return SlotAuthorityOptions.DerivedData();
                default:
                    return SlotAuthorityOptions.Durable();
            }
        }

        private static FactoryKey FirstMigrationKey(StateSlotSpec spec)
        {
            for (int i = 0; i < spec.MigrationKeys.Count; i++)
            {
                if (!spec.MigrationKeys[i].RegistrationKey.IsDefault)
                {
                    return spec.MigrationKeys[i];
                }
            }

            return default(FactoryKey);
        }

        private static FactoryKey FirstMigrationKeyOrDefault(StateSlotSpec spec) => FirstMigrationKey(spec);

        /// <summary>
        /// The validated partition of one slot's domain: the generated assignment of its writer when the domain has
        /// exactly one, otherwise none. A slot with several writers has no single partition, and this never picks an
        /// arbitrary one (P-034).
        /// </summary>
        private static Id128 PartitionOf(OwnershipReport report, StateSlotSpec spec)
        {
            IReadOnlyList<FactoryKey> writers = report.Map.WritersOf(spec.Schema);
            if (writers.Count != 1)
            {
                return Id128.Zero;
            }

            return report.Map.TryGetPartition(spec.Schema, writers[0], out PartitionAssignment assignment)
                ? assignment.PartitionId
                : Id128.Zero;
        }

        /// <summary>
        /// One physical component layout per declared layout key, with every field the slots place in it. Two
        /// layouts of one component schema with one owner are two physical storages, which is what the declaration
        /// says; one layout claimed by two owners is refused (P-033).
        /// </summary>
        private static bool TryBuildComponents(
            IReadOnlyList<StateSlotSpec> slots,
            out List<ComponentLayoutDeclaration> components,
            out DiagnosticCode code,
            out string detail)
        {
            components = new List<ComponentLayoutDeclaration>();
            code = DiagnosticCode.None;
            detail = string.Empty;
            for (int i = 0; i < slots.Count; i++)
            {
                StateSlotSpec spec = slots[i];
                if (spec.PhysicalLayoutKey.RegistrationKey.IsDefault)
                {
                    continue;
                }

                int existing = -1;
                for (int c = 0; c < components.Count; c++)
                {
                    if (components[c].LayoutKey.Equals(spec.PhysicalLayoutKey))
                    {
                        existing = c;
                        break;
                    }
                }

                if (existing < 0)
                {
                    components.Add(new ComponentLayoutDeclaration(
                        spec.Schema,
                        spec.PhysicalLayoutKey,
                        spec.Owner,
                        spec.FieldOwnership));
                    continue;
                }

                ComponentLayoutDeclaration current = components[existing];
                if (!current.Owner.Equals(spec.Owner))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "physical layout " + spec.PhysicalLayoutKey.ToString() + " is claimed by owners "
                        + current.Owner.ToString() + " and " + spec.Owner.ToString()
                        + "; one component layout has one physical owner (P-033).";
                    return false;
                }

                var fields = new List<FieldOwnership>();
                for (int f = 0; f < current.Fields.Count; f++)
                {
                    fields.Add(current.Fields[f]);
                }

                for (int f = 0; f < spec.FieldOwnership.Count; f++)
                {
                    fields.Add(spec.FieldOwnership[f]);
                }

                components[existing] = new ComponentLayoutDeclaration(
                    current.Component,
                    current.LayoutKey,
                    current.Owner,
                    fields);
            }

            return true;
        }
    }
}
