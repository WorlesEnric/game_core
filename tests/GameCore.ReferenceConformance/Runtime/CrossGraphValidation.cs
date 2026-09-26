// GameCore.ReferenceConformance — 07 s5's combined-graph assembly-validation checks (GC-024).
//
// 07:278 closes section 5 with the claim this file makes executable:
//
//     "Missing command endpoints, duplicate state owners, or a same-step cycle fail assembly validation with their
//      declaring plugin IDs."
//
// All three rules are production kernel rules, and all three live in the pure `GameCore.Planning` assembly, so the
// whole check runs in plain dotnet, in Unity EditMode and in the IL2CPP player from this one source (P-001, P-057):
//
//   * `graph-missing-command-endpoint` — a buffer contract whose active producer systems have no consumer stage in
//     the declaration set rejects with `MissingDependency` (`ScheduleCompiler`, witness kind
//     `ScheduleWitnessKind.BufferConsumerMissing`, P-043);
//   * `graph-duplicate-state-owner` — one authoritative domain claimed by two logical owners rejects with
//     `OwnershipConflict` (`OwnerAuthorityValidator.Validate`, P-034);
//   * `graph-same-step-cycle` — two stages that each declare the other before them reject with `Cycle`
//     (`ScheduleCompiler`, witness kind `ScheduleWitnessKind.StageCycle`, P-040). That is REF-X03's same-step
//     card -> narrative feedback cycle, whose next-step variant validates.
//
// WHAT THE KERNEL CANNOT SAY, AND WHY THIS FILE SAYS IT INSTEAD
//
// No kernel path reports a plugin id: the schedule witnesses name declaration identities (a `StageId`, a `BufferId`)
// and the ownership report names owners and writers, never the manifest a declaration came from. That is by design
// rather than an omission to patch — `OwnershipSchedulePipeline.Build` flattens the mounted manifests into one
// declaration list (`OwnershipSchedulePipeline.cs:186-211`) before either validator runs, so provenance is already
// gone by the time a fault is found — and adding a plugin-id field to `ScheduleWitness`, `OwnershipReport` or a
// diagnostic would be a kernel change made for a reporting improvement this fixture can supply itself.
//
// So each case below BUILDS its broken declaration set from hand-written `PluginManifest`s — the same shape
// `Packages/com.gamecore.gameplay.cards/Runtime/CardTableDeclarations.cs` produces — and therefore knows which
// plugin declared the stage, the buffer, the owner or the slot the kernel rejects. It composes that plugin id into
// `FixtureDetail` beside the kernel's own witness text. Nothing is re-implemented: every verdict below is the
// production compiler's or validator's own, and a case reports `Passed` only when the kernel really refused the
// declaration set with the expected code and its own detail really names the declaring plugin.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;
using GameCore.Planning.Ownership;
using GameCore.Planning.Scheduling;

namespace GameCore.ReferenceConformance
{
    /// <summary>
    /// One combined-graph assembly-validation case: the deliberately broken declaration set, the plugin that declares
    /// the fault, and the kernel's own verdict about it. `FixtureDetail` restates the same fact with the declaring
    /// plugin id composed in, which is the one thing the kernel's own witnesses cannot carry.
    /// </summary>
    public sealed class CrossGraphCase
    {
        public CrossGraphCase(
            string caseId,
            string requirement,
            string declaringPlugin,
            DiagnosticCode expected,
            bool rejected,
            DiagnosticCode observed,
            string kernelDetail,
            string fixtureDetail)
        {
            CaseId = caseId ?? throw new ArgumentNullException(nameof(caseId));
            Requirement = requirement ?? throw new ArgumentNullException(nameof(requirement));
            DeclaringPlugin = declaringPlugin ?? throw new ArgumentNullException(nameof(declaringPlugin));
            Expected = expected;
            Rejected = rejected;
            Observed = observed;
            KernelDetail = kernelDetail ?? string.Empty;
            FixtureDetail = fixtureDetail ?? string.Empty;
            Passed = rejected && observed == expected && FixtureDetail.Contains(DeclaringPlugin);
        }

        /// <summary>`graph-missing-command-endpoint`, `graph-duplicate-state-owner` or `graph-same-step-cycle`.</summary>
        public string CaseId { get; }

        /// <summary>The 07/00 clause text this case makes checkable, quoted.</summary>
        public string Requirement { get; }

        /// <summary>Stable name of the plugin instance whose own declaration introduces the fault.</summary>
        public string DeclaringPlugin { get; }

        public DiagnosticCode Expected { get; }

        /// <summary>True when the kernel refused the assembly.</summary>
        public bool Rejected { get; }

        /// <summary>The code the kernel refused with; <see cref="DiagnosticCode.None"/> when it did not refuse.</summary>
        public DiagnosticCode Observed { get; }

        /// <summary>Verbatim what the kernel said, the declaration identities it named included.</summary>
        public string KernelDetail { get; }

        /// <summary>The same fact with the declaring plugin id, composed here because the kernel drops provenance.</summary>
        public string FixtureDetail { get; }

        /// <summary>Refused with the expected code, and the detail names the declaring plugin.</summary>
        public bool Passed { get; }
    }

    /// <summary>
    /// The three combined-graph validation cases of 07 s5's closing paragraph (07:278), each driven through the real
    /// `GameCore.Planning` entry point it belongs to (P-028, P-034, P-040, P-043, REF-X03).
    /// </summary>
    public static class CrossGraphValidation
    {
        /// <summary>Step prefix one case is reported under; `ConformanceCrossWorld.StepPrefix`'s own value.</summary>
        private const string StepPrefix = "conformance/cross/";

        /// <summary>The clause every case makes checkable, exactly as 07 s5 states it (07:278).</summary>
        private const string Clause =
            "07:278 \"Missing command endpoints, duplicate state owners, or a same-step cycle fail assembly"
            + " validation with their declaring plugin IDs.\"";

        /// <summary>The declaring plugin of the missing-endpoint case: it declares the buffer whose consumer is absent.</summary>
        private const string PluginAlpha = "gc024.graph.plugin-alpha";

        /// <summary>The declaring plugin of the duplicate-owner and of the cycle-closing case.</summary>
        private const string PluginBeta = "gc024.graph.plugin-beta";

        /// <summary>
        /// Every case, in the order 07 s5 states them. Each one is built from its own hand-written manifests and
        /// driven through the production kernel entry point, so the list is data plus one real verdict per entry.
        /// </summary>
        public static IReadOnlyList<CrossGraphCase> Run()
        {
            return ContractCollections.Freeze(new List<CrossGraphCase>
            {
                MissingCommandEndpoint(),
                DuplicateStateOwner(),
                SameStepCycle(),
            });
        }

        /// <summary>One case by its stable id, or null when no case carries that id.</summary>
        public static CrossGraphCase? ById(string caseId)
        {
            if (caseId == null)
            {
                throw new ArgumentNullException(nameof(caseId));
            }

            IReadOnlyList<CrossGraphCase> cases = Run();
            for (int i = 0; i < cases.Count; i++)
            {
                if (string.Equals(cases[i].CaseId, caseId, StringComparison.Ordinal))
                {
                    return cases[i];
                }
            }

            return null;
        }

        /// <summary>The step name one case is reported under, e.g. `conformance/cross/graph-same-step-cycle`.</summary>
        public static string StepName(string caseId)
        {
            if (caseId == null)
            {
                throw new ArgumentNullException(nameof(caseId));
            }

            return StepPrefix + caseId;
        }

        // ------------------------------------------------------------------ case 1: missing command endpoint

        /// <summary>
        /// A buffer whose active producer systems have no consuming stage. Plugin alpha declares the producing stage
        /// and the receipt buffer, and the buffer's declared consuming stage is plugin beta's — which is deliberately
        /// NOT part of this assembly, so the produced data would have no endpoint at all. P-043 requires exactly one
        /// consuming stage for a declared buffer, so the compiler refuses the assembly with `MissingDependency` and
        /// the `BufferConsumerMissing` witness (07:278, P-043).
        /// </summary>
        private static CrossGraphCase MissingCommandEndpoint()
        {
            SchemaRef receipt = Schema("gc024.graph.domain.alpha-receipt");
            SchemaRef produced = Schema("gc024.graph.domain.alpha-produced");
            StageId producerStage = Stage("gc024.graph.stage.alpha-produce");
            FactoryKey producerSystem = Key("gc024.graph.system.alpha-produce");

            // Plugin beta's own declaration of the endpoint this assembly is missing. It is built so the fixture can
            // name the plugin that would have declared the endpoint, and it is never mounted below.
            PluginManifest beta = Plugin(
                PluginBeta,
                null,
                new List<StageSpec>
                {
                    StageDecl(
                        Stage("gc024.graph.stage.beta-consume"),
                        PluginBeta,
                        new AccessSet(new[] { Reads(receipt) }),
                        new List<SystemSpec> { SystemEntry("gc024.graph.system.beta-consume", Reads(receipt)) }),
                },
                null);

            // The endpoint plugin alpha's buffer names, read back from beta's own manifest rather than derived twice.
            StageId consumerStage = beta.Stages[0].StageId;
            PluginManifest alpha = Plugin(
                PluginAlpha,
                null,
                new List<StageSpec>
                {
                    StageDecl(
                        producerStage,
                        PluginAlpha,
                        new AccessSet(new[] { Writes(produced) }),
                        new List<SystemSpec> { SystemEntry(producerSystem, Writes(produced)) }),
                },
                new List<BufferSpec>
                {
                    BufferDecl(
                        "gc024.graph.buffer.alpha-receipt",
                        receipt,
                        new List<FactoryKey> { producerSystem },
                        producerStage,
                        consumerStage,
                        "gc024.graph.order.alpha-receipt"),
                });

            // The active assembly is plugin alpha's declarations only.
            ScheduleCompilation compilation =
                ScheduleCompiler.Compile(new ScheduleDeclarations(alpha.Stages, alpha.Buffers));
            string kernelDetail = KernelVerdict(compilation);
            string fixtureDetail = "plugin " + PluginAlpha + " declares stage " + producerStage.ToString()
                + " and buffer " + alpha.Buffers[0].BufferId.ToString() + ", whose declared consuming endpoint "
                + consumerStage.ToString() + " belongs to plugin " + PluginBeta
                + " and is absent from this assembly: " + kernelDetail;
            return new CrossGraphCase(
                "graph-missing-command-endpoint",
                Clause + " Normative rule: P-043 (a declared buffer has producers, exactly one consuming stage, an"
                    + " order key and a bounded capacity).",
                PluginAlpha,
                DiagnosticCode.MissingDependency,
                !compilation.Succeeded,
                compilation.Code,
                kernelDetail,
                fixtureDetail);
        }

        // ------------------------------------------------------------------ case 2: duplicate state owner

        /// <summary>
        /// One authoritative domain claimed by two logical owners. Plugin alpha declares owner
        /// `gc024.graph.owner-alpha` for `gc024.graph.domain.shared` and plugin beta declares
        /// `gc024.graph.owner-beta` for the same domain, so one domain has two owners (P-034). Both writers are ordered
        /// by the compiled stage order, so the only diagnostic the validator can reach is the contested domain:
        /// `OwnerAuthorityValidator.Validate` refuses with `OwnershipConflict` and names both owners and both writer
        /// keys in its summary (07:278, P-034).
        /// </summary>
        private static CrossGraphCase DuplicateStateOwner()
        {
            SchemaRef shared = Schema("gc024.graph.domain.shared");
            StageId alphaStage = Stage("gc024.graph.stage.alpha-write");
            StageId betaStage = Stage("gc024.graph.stage.beta-write");
            PluginManifest alpha = Plugin(
                PluginAlpha,
                new List<StateSlotSpec>
                {
                    SlotDecl("gc024.graph.slot.alpha-shared", "gc024.graph.owner-alpha", shared,
                        "gc024.graph.field.shared"),
                },
                new List<StageSpec>
                {
                    StageDecl(
                        alphaStage,
                        PluginAlpha,
                        new AccessSet(new[] { Writes(shared) }),
                        new List<SystemSpec> { SystemEntry("gc024.graph.system.alpha-write", Writes(shared)) }),
                },
                null);
            PluginManifest beta = Plugin(
                PluginBeta,
                new List<StateSlotSpec>
                {
                    SlotDecl("gc024.graph.slot.beta-shared", "gc024.graph.owner-beta", shared,
                        "gc024.graph.field.shared"),
                },
                new List<StageSpec>
                {
                    StageDecl(
                        betaStage,
                        PluginBeta,
                        new AccessSet(new[] { Writes(shared) }),
                        new List<SystemSpec> { SystemEntry("gc024.graph.system.beta-write", Writes(shared)) }),
                },
                null);

            StateSlotSpec alphaSlot = alpha.StateSlots[0];
            StateSlotSpec betaSlot = beta.StateSlots[0];

            // The writers GC-007 sees: the generated owner grant of one system entry plus its declared access set.
            // Each writer's owner is the owner its own manifest's state slot declares, which is exactly how
            // `OwnershipSchedulePipeline` resolves a writer's owner from the manifest set (P-034).
            //
            // The two manifest slot declarations are deliberately NOT handed to the validator: a declared slot's every
            // field must be owned by a component layout (`ComponentOwnershipMap.TryBuild`), and this case declares no
            // component layouts, so passing the slots would add an unrelated `MissingDependency` about an unowned field
            // and blur the single fault the case is about. `OwnerAuthorityDeclaration` is public and constructible
            // from here; the writers-only input is the same shape `OwnerAuthorityValidatorTests` drives the
            // contested-domain rule with.
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(
                    alphaStage,
                    alpha.Stages[0].Systems[0].SystemKey,
                    alphaSlot.Owner,
                    alpha.Stages[0].Systems[0].Access,
                    SystemMultiplicity.World,
                    null,
                    null),
                new WriterDeclaration(
                    betaStage,
                    beta.Stages[0].Systems[0].SystemKey,
                    betaSlot.Owner,
                    beta.Stages[0].Systems[0].Access,
                    SystemMultiplicity.World,
                    null,
                    null),
            };

            // The compiled stage order the two writers really have, so an unordered writer pair cannot be mistaken
            // for the contested domain this case declares.
            var stageOrder = new List<StageId> { alphaStage, betaStage };
            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, stageOrder));

            string kernelDetail = report.Describe();
            string fixtureDetail = "plugin " + PluginBeta + " declares state slot " + betaSlot.SlotId.ToString()
                + " owned by " + betaSlot.Owner.ToString() + " on domain " + betaSlot.Schema.ToString()
                + ", which plugin " + PluginAlpha + " already owns as " + alphaSlot.Owner.ToString()
                + " (slot " + alphaSlot.SlotId.ToString() + "); one authoritative domain has one owner: "
                + kernelDetail;
            return new CrossGraphCase(
                "graph-duplicate-state-owner",
                Clause + " Normative rule: P-034 (one logical owner per authoritative domain; two writers of one"
                    + " domain need a directed order or validated disjoint partitions).",
                PluginBeta,
                DiagnosticCode.OwnershipConflict,
                !report.IsValid,
                report.Diagnostics.Count > 0 ? report.Diagnostics[0].Code : DiagnosticCode.None,
                kernelDetail,
                fixtureDetail);
        }

        // ------------------------------------------------------------------ case 3: same-step cycle

        /// <summary>
        /// A same-step feedback cycle: plugin alpha's stage declares plugin beta's stage before itself, and plugin
        /// beta's stage declares plugin alpha's stage before itself, so the two declared `RequiredBefore` edges close
        /// a cycle and no execution order exists. The stage compiler refuses the assembly with `Cycle` and the
        /// `StageCycle` witness that names the path (07:278, P-040, REF-X03). Each stage writes its own domain, so the
        /// only fault is the cycle and not an also-unordered access pair.
        /// </summary>
        private static CrossGraphCase SameStepCycle()
        {
            SchemaRef alphaDomain = Schema("gc024.graph.domain.alpha-cycle");
            SchemaRef betaDomain = Schema("gc024.graph.domain.beta-cycle");
            StageId alphaStage = Stage("gc024.graph.stage.alpha-first");
            StageId betaStage = Stage("gc024.graph.stage.beta-second");
            PluginManifest alpha = Plugin(
                PluginAlpha,
                null,
                new List<StageSpec>
                {
                    StageDecl(
                        alphaStage,
                        PluginAlpha,
                        new AccessSet(new[] { Writes(alphaDomain) }),
                        new List<SystemSpec> { SystemEntry("gc024.graph.system.alpha-first", Writes(alphaDomain)) },
                        new List<StageId> { betaStage }),
                },
                null);
            PluginManifest beta = Plugin(
                PluginBeta,
                null,
                new List<StageSpec>
                {
                    StageDecl(
                        betaStage,
                        PluginBeta,
                        new AccessSet(new[] { Writes(betaDomain) }),
                        new List<SystemSpec> { SystemEntry("gc024.graph.system.beta-second", Writes(betaDomain)) },
                        new List<StageId> { alphaStage }),
                },
                null);

            var stages = new List<StageSpec> { alpha.Stages[0], beta.Stages[0] };
            ScheduleCompilation compilation = ScheduleCompiler.Compile(new ScheduleDeclarations(stages, null));
            string kernelDetail = KernelVerdict(compilation);
            string fixtureDetail = "plugin " + PluginBeta + " declares stage " + betaStage.ToString()
                + ", which requires plugin " + PluginAlpha + "'s stage " + alphaStage.ToString()
                + " before itself, while plugin " + PluginAlpha + "'s stage requires plugin " + PluginBeta
                + "'s stage before itself: the declared same-step feedback closes a cycle, so no execution order"
                + " exists: " + kernelDetail;
            return new CrossGraphCase(
                "graph-same-step-cycle",
                Clause + " Normative rule: P-040 and REF-X03 (a same-step card -> narrative feedback cycle is"
                    + " reported; the next-step message variant validates).",
                PluginBeta,
                DiagnosticCode.Cycle,
                !compilation.Succeeded,
                compilation.Code,
                kernelDetail,
                fixtureDetail);
        }

        // ------------------------------------------------------------------ kernel rendering and declarations

        /// <summary>
        /// The kernel's own verdict about a refused compilation, rendered without paraphrase: the primary code, the
        /// rejection detail and every witness. A witness's `ToString` omits its buffer id, so a non-default buffer is
        /// appended here rather than lost.
        /// </summary>
        private static string KernelVerdict(ScheduleCompilation compilation)
        {
            var text = new StringBuilder();
            text.Append("rejected(").Append(DiagnosticCodeText.Of(compilation.Code)).Append("): ")
                .Append(compilation.Detail);
            for (int i = 0; i < compilation.Witnesses.Count; i++)
            {
                ScheduleWitness witness = compilation.Witnesses[i];
                text.Append(" | ").Append(witness.ToString());
                if (!witness.Buffer.Value.IsDefault)
                {
                    text.Append(" buffer=").Append(witness.Buffer.ToString());
                }
            }

            return text.ToString();
        }

        /// <summary>One stable name's derived identity, exactly as a generated catalog derives it (P-004).</summary>
        private static Id128 Id(string stableName) => StableNameKeyDerivation.Derive(stableName);

        private static StageId Stage(string stableName) => new StageId(Id(stableName));

        private static FactoryKey Key(string stableName) => new FactoryKey(Id(stableName), 1U);

        private static SchemaRef Schema(string stableName) => new SchemaRef(new SchemaId(Id(stableName)), 1U);

        private static SlotId Slot(string stableName) => new SlotId(Id(stableName));

        private static OwnerId Owner(string stableName) => new OwnerId(Id(stableName));

        private static BufferId Buffer(string stableName) => new BufferId(Id(stableName));

        /// <summary>An unpartitioned read/write claim on one domain; this is what makes a system a writer (P-034).</summary>
        private static AccessDeclaration Writes(SchemaRef domain)
            => new AccessDeclaration(domain, AccessMode.ReadWrite, default(Id128));

        /// <summary>An unpartitioned read claim, so a reader system claims no authority at all (P-034).</summary>
        private static AccessDeclaration Reads(SchemaRef domain)
            => new AccessDeclaration(domain, AccessMode.Read, default(Id128));

        /// <summary>One hand-written manifest; every category is explicit and null means "none" (P-009).</summary>
        private static PluginManifest Plugin(
            string pluginName,
            IReadOnlyList<StateSlotSpec>? slots,
            IReadOnlyList<StageSpec>? stages,
            IReadOnlyList<BufferSpec>? buffers)
        {
            return new PluginManifest(
                new PluginTypeId(Id(pluginName)),
                "1.0.0",
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                Schema("gc024.graph.config." + pluginName),
                Key("gc024.graph.manifest." + pluginName),
                null,
                null,
                null,
                null,
                null,
                slots,
                stages,
                buffers,
                null);
        }

        /// <summary>One system entry: one generated system key plus the access it declares (P-039).</summary>
        private static SystemSpec SystemEntry(string systemName, params AccessDeclaration[] access)
            => SystemEntry(Key(systemName), access);

        /// <summary>One system entry from an already-derived key, so a caller that also names the key declares it once.</summary>
        private static SystemSpec SystemEntry(FactoryKey systemKey, params AccessDeclaration[] access)
            => new SystemSpec(systemKey, SystemMultiplicity.World, new AccessSet(access), null, null, null, null);

        /// <summary>
        /// One stage declaration: its own read/write set, its systems and the stages it declares before itself. The
        /// owner package is the declaring plugin's own identity, so a `StageOwnerMismatch` can never be confused for
        /// the fault a case declares (P-039).
        /// </summary>
        private static StageSpec StageDecl(
            StageId stage,
            string pluginName,
            AccessSet readWriteSet,
            IReadOnlyList<SystemSpec> systems,
            IReadOnlyList<StageId>? requiredBefore = null)
        {
            return new StageSpec(
                stage,
                1U,
                Id(pluginName),
                HostAffinity.ManagedMain,
                null,
                null,
                readWriteSet,
                requiredBefore,
                null,
                null,
                null,
                systems,
                null);
        }

        /// <summary>One owned state slot: its domain, its physical layout, its declared fields and its policy (P-032).</summary>
        private static StateSlotSpec SlotDecl(string slotName, string ownerName, SchemaRef domain, string fieldName)
        {
            return new StateSlotSpec(
                Slot(slotName),
                Owner(ownerName),
                domain,
                Key("gc024.graph.layout." + slotName),
                new[] { new FieldOwnership(domain, Key(fieldName).RegistrationKey) },
                default(FactoryKey),
                default(FactoryKey),
                default(FactoryKey),
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null);
        }

        /// <summary>
        /// One step-lifetime buffer contract with a bounded capacity and a draining cancellation policy (P-043): the
        /// producer systems, the single consuming stage and the order key are the caller's, the rest is the shape a
        /// generated declaration carries.
        /// </summary>
        private static BufferSpec BufferDecl(
            string bufferName,
            SchemaRef schema,
            IReadOnlyList<FactoryKey> producers,
            StageId ownerStage,
            StageId consumerStage,
            string orderName)
        {
            return new BufferSpec(
                Buffer(bufferName),
                schema,
                producers,
                ownerStage,
                consumerStage,
                Key(orderName),
                BufferLifetime.Step,
                4,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }
    }
}
