// GameCore.Gameplay.Narrative.Fixtures — the narrative world: shared state, the six command stages and their module.
//
// The step table this world runs is the one GC-009's compiler produced from the chapter provider's declarations, and
// GC-005's guarded dispatcher executes it in the compiled order. Every stage below is ordinary gameplay code:
//
//   narrative.input       drains the host's bounded ingress lane and forwards each admitted choice, unchanged and
//                         with the same causal request, to the dialogue owner; it also advances the trail (P-037).
//   narrative.dialogue    validates the choice with the pure rules, writes the authoritative conversation state,
//                         commits the command with its own result event, and requests exactly one fact mutation.
//   narrative.quest       the single writer of the durable facts: it applies the transition, then hands the sealed
//                         fact observation to the gate and encounter owners through their declared buffers.
//   narrative.gates       evaluates its chapter's condition from the committed fact and commits the gate decision.
//   narrative.encounters  reacts to the same fact under its own lifecycle policy.
//   narrative.output      reads the earlier stages' results after them and projects the trail.
//
// No stage writes another owner's domain, no stage reads a mutable value another owner holds, and the one ordering
// that matters (facts before gates) is a declared buffer edge, not a hope about system registration order (P-034,
// P-041, P-043).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Planning;
using CompiledSchedule = GameCore.Planning.Scheduling.CompiledSchedule;
using GameCore.Rules.Narrative;
using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Gameplay.Narrative.Fixtures
{
    /// <summary>Owner-state access of one target entity, through the published slot storage (P-032, P-034).</summary>
    public static class NarrativeState
    {
        /// <summary>Reads one owned slot's value and schema version; a target without the slot reports a miss.</summary>
        public static bool TryRead(
            EntityManager entityManager,
            Entity entity,
            OwnerId owner,
            SlotId slot,
            out int value,
            out uint schemaVersion)
        {
            value = 0;
            schemaVersion = 0U;
            if (!entityManager.Exists(entity) || !entityManager.HasBuffer<TargetSlotState>(entity))
            {
                return false;
            }

            DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
            if (!AssemblyStorage.TryFindSlot(slots, owner, slot, out int row))
            {
                return false;
            }

            TargetSlotState state = slots[row];
            value = state.Value;
            schemaVersion = state.SchemaVersion;
            return true;
        }

        /// <summary>Reads one owned slot's value, or a caller-supplied default when the target does not hold it.</summary>
        public static int ReadOrDefault(
            EntityManager entityManager,
            Entity entity,
            OwnerId owner,
            SlotId slot,
            int fallback)
            => TryRead(entityManager, entity, owner, slot, out int value, out uint _) ? value : fallback;

        /// <summary>
        /// Writes one owned slot at the declared schema version, appending the row when the owner has no state yet.
        /// An owner initializing its own slot is the declared initialization policy, not a second authority (P-032).
        /// </summary>
        public static void Write(
            EntityManager entityManager,
            Entity entity,
            OwnerId owner,
            SlotId slot,
            uint schemaVersion,
            int value)
        {
            if (!entityManager.Exists(entity))
            {
                return;
            }

            DynamicBuffer<TargetSlotState> slots = entityManager.HasBuffer<TargetSlotState>(entity)
                ? entityManager.GetBuffer<TargetSlotState>(entity)
                : entityManager.AddBuffer<TargetSlotState>(entity);

            if (AssemblyStorage.TryFindSlot(slots, owner, slot, out int row))
            {
                TargetSlotState existing = slots[row];
                existing.SchemaVersion = schemaVersion;
                existing.Value = value;
                existing.Active = 1;
                slots[row] = existing;
                return;
            }

            slots.Add(new TargetSlotState
            {
                Slot = slot,
                Owner = owner,
                SchemaVersion = schemaVersion,
                Value = value,
                Active = 1,
            });
        }

        /// <summary>Advances one owned counter slot by a delta and returns its new value.</summary>
        public static int Advance(
            EntityManager entityManager,
            Entity entity,
            OwnerId owner,
            SlotId slot,
            uint schemaVersion,
            int delta)
        {
            int current = ReadOrDefault(entityManager, entity, owner, slot, 0);
            int next = current + delta;
            Write(entityManager, entity, owner, slot, schemaVersion, next);
            return next;
        }

        /// <summary>
        /// The chapter a target's effective assembly selected, read from its derived binding row (P-017). The row's
        /// value is the chapter's binding ordinal, so the assembly itself says which chapter owns this target.
        /// </summary>
        public static bool TryChapterOf(
            EntityManager entityManager,
            Entity entity,
            CapabilityId capability,
            out ChapterDefinition? chapter)
        {
            chapter = null;
            if (!entityManager.Exists(entity) || !entityManager.HasBuffer<CapabilityBinding>(entity))
            {
                return false;
            }

            DynamicBuffer<CapabilityBinding> bindings = entityManager.GetBuffer<CapabilityBinding>(entity);
            for (int i = 0; i < bindings.Length; i++)
            {
                CapabilityBinding row = bindings[i];
                if (!row.Capability.Equals(capability))
                {
                    continue;
                }

                return NarrativeChapters.TryGetByOrdinal(row.Value, out chapter) && chapter != null;
            }

            return false;
        }
    }

    /// <summary>
    /// Fixture-side state of one narrative world: the compiled schedule it runs, the adapter result that installed
    /// it, the time driver that frames it and the entities its systems share.
    /// </summary>
    public sealed class NarrativeModule : IDisposable
    {
        private static readonly List<NarrativeModule> modules = new List<NarrativeModule>();

        private readonly UnityWorldHost host;
        private readonly Dictionary<Id128, Entity> targetEntities = new Dictionary<Id128, Entity>();
        private readonly List<TargetId> mappedTargets = new List<TargetId>();
        private bool disposed;

        private NarrativeModule(UnityWorldHost host, CompiledSchedule schedule)
        {
            this.host = host;
            Schedule = schedule;
            EntityManager entityManager = host.EntityWorld.EntityManager;
            RootEntity = entityManager.CreateEntity();
            entityManager.SetName(RootEntity, "NarrativeRoot");
        }

        public UnityWorldHost Host => host;

        /// <summary>The compiled schedule this world's dispatch table was installed from (GC-009's own output).</summary>
        public CompiledSchedule Schedule { get; }

        /// <summary>The entity carrying the slice's trail slots: the ledger target, and this world's observability.</summary>
        public Entity RootEntity { get; private set; }

        /// <summary>Time driver adopted by this world (input cutoff, plugin clocks and the wrapped pump).</summary>
        public WorldTimeDriver? Time { get; internal set; }

        /// <summary>Stages of the compiled schedule this world dispatched, by identity.</summary>
        public int InputStageIndex { get; private set; } = -1;

        public int DialogueStageIndex { get; private set; } = -1;

        public int QuestStageIndex { get; private set; } = -1;

        public int GateStageIndex { get; private set; } = -1;

        public int EncounterStageIndex { get; private set; } = -1;

        public int OutputStageIndex { get; private set; } = -1;

        /// <summary>Choices the input stage forwarded to the dialogue owner.</summary>
        public int ForwardedChoices { get; internal set; }

        /// <summary>Choices the dialogue owner refused without a write (P-042: admission is not success).</summary>
        public int RefusedChoices { get; internal set; }

        /// <summary>Durable fact transitions the quest owner committed (P-044).</summary>
        public int CommittedFacts { get; internal set; }

        /// <summary>Mutation requests the fact rule refused because they changed nothing (REF-N02).</summary>
        public int DuplicateMutations { get; internal set; }

        /// <summary>Gate decisions the gate owner evaluated from a committed fact.</summary>
        public int GateDecisions { get; internal set; }

        /// <summary>Encounter lifecycle transitions the encounter owner applied.</summary>
        public int EncounterHooks { get; internal set; }

        /// <summary>Steps the output stage projected after the earlier stages (P-040).</summary>
        public int OutputRuns { get; internal set; }

        public int MappedTargetCount => targetEntities.Count;

        /// <summary>Registered targets in canonical identity order, independent of registration timing (P-008).</summary>
        public IReadOnlyList<TargetId> MappedTargets => mappedTargets;

        public static NarrativeModule Attach(UnityWorldHost host, CompiledSchedule schedule)
        {
            var module = new NarrativeModule(host, schedule);
            module.ResolveStageIndexes();
            modules.Add(module);
            return module;
        }

        public static bool TryGet(World world, out NarrativeModule? module)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].host.EntityWorld == world)
                {
                    module = modules[i];
                    return true;
                }
            }

            module = null;
            return false;
        }

        public static void DetachAll()
        {
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                modules[i].Dispose();
            }

            modules.Clear();
        }

        /// <summary>
        /// Records the entity that carries one live target's state, so a stage resolves a message's stable target id
        /// without reading a second authority (P-004).
        /// </summary>
        public void MapTarget(TargetId target, Entity entity)
        {
            if (!targetEntities.ContainsKey(target.Value))
            {
                mappedTargets.Add(target);
            }

            targetEntities[target.Value] = entity;
        }

        /// <summary>The trail's own entity: the world-level ledger target once it exists (P-034).</summary>
        public void SetRootEntity(Entity entity) => RootEntity = entity;

        public bool TryEntity(TargetId target, out Entity entity) => targetEntities.TryGetValue(target.Value, out entity);

        /// <summary>Reads one trail counter, so a caller sees the compiled order's own evidence (P-040).</summary>
        public int ReadTrail(SlotId slot)
            => NarrativeState.ReadOrDefault(host.EntityWorld.EntityManager, RootEntity, NarrativeKeys.TrailOwner, slot, 0);

        /// <summary>Slot the compiled schedule assigned to one stage, or -1 when it is not in the plan.</summary>
        public int StageIndexOf(StageId stage) => Schedule.TryGetStageIndex(stage, out int index) ? index : -1;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            modules.Remove(this);
        }

        private void ResolveStageIndexes()
        {
            InputStageIndex = StageIndexOf(NarrativeKeys.InputStage);
            DialogueStageIndex = StageIndexOf(NarrativeKeys.DialogueStage);
            QuestStageIndex = StageIndexOf(NarrativeKeys.QuestStage);
            GateStageIndex = StageIndexOf(NarrativeKeys.GateStage);
            EncounterStageIndex = StageIndexOf(NarrativeKeys.EncounterStage);
            OutputStageIndex = StageIndexOf(NarrativeKeys.OutputStage);
        }
    }

    /// <summary>
    /// Input stage: it drains the host's bounded ingress lane and forwards every admitted choice to the dialogue
    /// owner, retaining the message identity and its causal request, so the state owner that validates the choice is
    /// the one that answers it (P-037, P-042). A payload the generated reader cannot decode is rejected observably.
    /// </summary>
    [DisableAutoCreation]
    public partial class NarrativeInputSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!NarrativeModule.TryGet(World, out NarrativeModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(NarrativeKeys.IngressOwner);
            EntityManager entityManager = EntityManager;
            NarrativeState.Advance(
                entityManager,
                module.RootEntity,
                NarrativeKeys.TrailOwner,
                NarrativeKeys.TrailStepsSlot,
                NarrativeKeys.TrailDomain.Version,
                1);

            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                byte[] payload = plane.PayloadOf(message);

                if (plane.Readers.TryRead<NarrativeChoice>(
                        message.PayloadSchema,
                        payload,
                        out NarrativeChoice _,
                        out string _) != PayloadDecodeOutcome.Decoded)
                {
                    plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                    continue;
                }

                // The same identity travels with the forwarded row: the dialogue owner is the state owner that
                // answers this request, and the host's ledger row is settled by that one commit (P-042).
                var forwarded = new StepMessage(
                    message.Step,
                    message.Epoch,
                    message.Request,
                    message.Route,
                    NarrativeKeys.DialogueOwner,
                    message.Target,
                    message.PayloadSchema,
                    MessageKind.Command,
                    message.Order,
                    NarrativeKeys.InputSystem,
                    0,
                    payload.Length);

                BufferAppendOutcome appended = plane.PublishInternal(
                    NarrativeKeys.ChoiceRequestBuffer,
                    forwarded,
                    payload,
                    out string _);

                if (appended != BufferAppendOutcome.Accepted)
                {
                    // A bounded lane that cannot take the row rejects the affected command before any mutation.
                    plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                    continue;
                }

                module.ForwardedChoices++;
            }

            plane.ReleaseConsumed(NarrativeKeys.IngressOwner);
        }
    }

    /// <summary>
    /// Dialogue stage: the one owner that validates a choice. It writes the authoritative conversation state, commits
    /// the command with its own result event, and requests exactly one durable fact mutation when the accepted choice
    /// asks for one (P-034, P-042, P-044).
    /// </summary>
    [DisableAutoCreation]
    public partial class NarrativeDialogueSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!NarrativeModule.TryGet(World, out NarrativeModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(NarrativeKeys.DialogueOwner);
            EntityManager entityManager = EntityManager;

            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                byte[] payload = plane.PayloadOf(message);

                if (plane.Readers.TryRead<NarrativeChoice>(
                        message.PayloadSchema,
                        payload,
                        out NarrativeChoice choice,
                        out string _) != PayloadDecodeOutcome.Decoded)
                {
                    plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                    continue;
                }

                if (!module.TryEntity(message.Target, out Entity entity) || !entityManager.Exists(entity))
                {
                    plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                    continue;
                }

                if (!NarrativeState.TryChapterOf(entityManager, entity, NarrativeKeys.DialogueBinding, out ChapterDefinition? chapter)
                    || chapter == null)
                {
                    // No chapter selected a conversation for this target, so it has no dialogue to answer (P-015).
                    plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                    continue;
                }

                int node = NarrativeState.ReadOrDefault(
                    entityManager, entity, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, 0);
                int status = NarrativeState.ReadOrDefault(
                    entityManager, entity, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationStatusSlot,
                    NarrativeConversationStatus.Idle);

                ChoiceValidation validation = NarrativeDialogueRules.Validate(chapter, choice, node, status);
                if (!validation.Accepted)
                {
                    module.RefusedChoices++;
                    plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                    continue;
                }

                NarrativeState.Write(
                    entityManager, entity, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot,
                    NarrativeKeys.ConversationDomain.Version, validation.ResultingNodeOrdinal);
                NarrativeState.Write(
                    entityManager, entity, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationStatusSlot,
                    NarrativeKeys.ConversationDomain.Version, validation.ResultingStatus);

                // The dialogue owner answers the admitted request itself, so the committed result and the accepted
                // decision are the same step's output (P-042, P-044).
                if (!plane.Commit(
                        message,
                        NarrativeKeys.ChoiceCommittedSchema,
                        new FrozenPayload(NarrativePayloadCodec.EncodeChoiceOutcome(
                            validation.ResultingNodeOrdinal,
                            validation.ResultingStatus)),
                        plane.ExecutingStep,
                        out string _))
                {
                    plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                    continue;
                }

                if (!validation.RequestsFactMutation)
                {
                    continue;
                }

                // The quest owner is the single writer of the facts, so the dialogue owner never writes one: it
                // requests the transition through the declared buffer edge (P-034, P-042).
                if (!RulesNarrativeFacts.TryGetFactOrdinal(validation.FactKey, out int factOrdinal))
                {
                    continue;
                }

                byte[] mutationPayload = NarrativePayloadCodec.EncodeMutation(
                    new NarrativeMutation(factOrdinal, validation.FactValue));

                var request = new StepMessage(
                    message.Step,
                    message.Epoch,
                    message.Request,
                    message.Route,
                    NarrativeKeys.QuestOwner,
                    message.Target,
                    NarrativeKeys.QuestMutationSchema,
                    MessageKind.Request,
                    message.Order,
                    NarrativeKeys.DialogueSystem,
                    0,
                    mutationPayload.Length);

                plane.PublishInternal(NarrativeKeys.FactChangeBuffer, request, mutationPayload, out string _);
            }

            plane.ReleaseConsumed(NarrativeKeys.DialogueOwner);
        }
    }

    /// <summary>
    /// Quest stage: the single writer of the durable facts. It applies the requested transition under the fact rule's
    /// own policy (a no-op request is refused and counted, never re-committed) and then hands the sealed fact
    /// observation to the gate and encounter owners through their declared buffers (P-032, P-043, P-044).
    /// </summary>
    [DisableAutoCreation]
    public partial class NarrativeQuestSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!NarrativeModule.TryGet(World, out NarrativeModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(NarrativeKeys.QuestOwner);
            EntityManager entityManager = EntityManager;

            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                byte[] payload = plane.PayloadOf(message);

                if (plane.Readers.TryRead<NarrativeMutation>(
                        message.PayloadSchema,
                        payload,
                        out NarrativeMutation mutation,
                        out string _) != PayloadDecodeOutcome.Decoded)
                {
                    continue;
                }

                if (!RulesNarrativeFacts.TryGetFactKeyByOrdinal(mutation.FactOrdinal, out string factKey)
                    || !RulesNarrativeFacts.TryGetFactSlotTag(factKey, out string slotTag))
                {
                    continue;
                }

                Entity ledger = module.RootEntity;
                SlotId valueSlot = NarrativeKeys.FactValueSlot(slotTag);
                SlotId versionSlot = NarrativeKeys.FactVersionSlot(slotTag);

                int current = NarrativeState.ReadOrDefault(
                    entityManager, ledger, NarrativeKeys.QuestOwner, valueSlot, RulesNarrativeFacts.InitialValue);
                int version = NarrativeState.ReadOrDefault(
                    entityManager, ledger, NarrativeKeys.QuestOwner, versionSlot, RulesNarrativeFacts.InitialVersion);

                if (!RulesNarrativeFacts.TryTransition(current, mutation.RequestedValue, out int next, out string _))
                {
                    // A request that changes nothing is not a transition: the fact keeps its value and version, and
                    // no second committed result can appear (REF-N02).
                    module.DuplicateMutations++;
                    continue;
                }

                int nextVersion = RulesNarrativeFacts.NextVersion(version);
                NarrativeState.Write(
                    entityManager, ledger, NarrativeKeys.QuestOwner, valueSlot,
                    NarrativeKeys.QuestDomain.Version, next);
                NarrativeState.Write(
                    entityManager, ledger, NarrativeKeys.QuestOwner, versionSlot,
                    NarrativeKeys.QuestDomain.Version, nextVersion);
                NarrativeState.Advance(
                    entityManager, ledger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailFactsSlot,
                    NarrativeKeys.TrailDomain.Version, 1);
                module.CommittedFacts++;

                // The sealed observation travels to the owners whose conditions read this fact, exactly as the
                // reference graph declares: `quest -> gates` and `quest -> encounters` (07 section 3.2).
                PublishObservations(module, plane, message, factKey, mutation.FactOrdinal, next, nextVersion);
            }

            plane.ReleaseConsumed(NarrativeKeys.QuestOwner);
        }

        private void PublishObservations(
            NarrativeModule module,
            WorldMessagePlane plane,
            StepMessage mutation,
            string factKey,
            int factOrdinal,
            int value,
            int version)
        {
            byte[] payload = NarrativePayloadCodec.EncodeObservation(
                new NarrativeObservation(factOrdinal, value, version));

            EntityManager entityManager = EntityManager;
            IReadOnlyList<TargetId> targets = module.MappedTargets;
            uint ordinal = 0U;

            for (int t = 0; t < targets.Count; t++)
            {
                if (!module.TryEntity(targets[t], out Entity entity) || !entityManager.Exists(entity))
                {
                    continue;
                }

                // A gate reads the fact its own chapter's condition names; an encounter reads the fact its chapter's
                // hook condition names. Both are resolved from the target's effective assembly, not from a table.
                Publish(
                    module, plane, mutation, entityManager, targets[t], entity,
                    NarrativeKeys.GateBinding, NarrativeKeys.FactObservedBuffer, NarrativeKeys.GateOwner,
                    NarrativeKeys.FactObservedSchema, factKey, payload, ref ordinal);

                Publish(
                    module, plane, mutation, entityManager, targets[t], entity,
                    NarrativeKeys.EncounterBinding, NarrativeKeys.EncounterObservedBuffer,
                    NarrativeKeys.EncounterOwner, NarrativeKeys.FactObservedSchema,
                    factKey, payload, ref ordinal);
            }
        }

        private void Publish(
            NarrativeModule module,
            WorldMessagePlane plane,
            StepMessage mutation,
            EntityManager entityManager,
            TargetId target,
            Entity entity,
            CapabilityId binding,
            BufferId buffer,
            OwnerId owner,
            SchemaRef schema,
            string factKey,
            byte[] payload,
            ref uint ordinal)
        {
            if (!NarrativeState.TryChapterOf(entityManager, entity, binding, out ChapterDefinition? chapter)
                || chapter == null)
            {
                return;
            }

            if (!string.Equals(chapter.GateConditionFactKey, factKey, StringComparison.Ordinal))
            {
                return;
            }

            var observation = new StepMessage(
                mutation.Step,
                mutation.Epoch,
                mutation.Request,
                mutation.Route,
                owner,
                target,
                schema,
                MessageKind.Request,
                new MessageOrderKey(mutation.Order.Admitted, ordinal, target.Value),
                NarrativeKeys.QuestSystem,
                0,
                payload.Length);

            ordinal++;
            plane.PublishInternal(buffer, observation, payload, out string _);
        }
    }

    /// <summary>
    /// Gate stage: it evaluates its chapter's condition from the committed fact observation and writes the gate's
    /// decision together with the fact version it was taken from, then commits that decision as its own output
    /// (P-032, P-044).
    /// </summary>
    [DisableAutoCreation]
    public partial class NarrativeGateSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!NarrativeModule.TryGet(World, out NarrativeModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(NarrativeKeys.GateOwner);
            EntityManager entityManager = EntityManager;

            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                byte[] payload = plane.PayloadOf(message);

                if (plane.Readers.TryRead<NarrativeObservation>(
                        message.PayloadSchema,
                        payload,
                        out NarrativeObservation observation,
                        out string _) != PayloadDecodeOutcome.Decoded)
                {
                    continue;
                }

                if (!module.TryEntity(message.Target, out Entity entity) || !entityManager.Exists(entity))
                {
                    continue;
                }

                if (!NarrativeGateRules.TryEvaluate(
                        observation.Value,
                        observation.Version,
                        out int decision,
                        out int evaluatedVersion,
                        out string _))
                {
                    continue;
                }

                NarrativeState.Write(
                    entityManager, entity, NarrativeKeys.GateOwner, NarrativeKeys.GateDecisionSlot,
                    NarrativeKeys.GateDomain.Version, decision);
                NarrativeState.Write(
                    entityManager, entity, NarrativeKeys.GateOwner, NarrativeKeys.GateEvaluatedVersionSlot,
                    NarrativeKeys.GateDomain.Version, evaluatedVersion);
                NarrativeState.Advance(
                    entityManager, module.RootEntity, NarrativeKeys.TrailOwner, NarrativeKeys.TrailGateDecisionsSlot,
                    NarrativeKeys.TrailDomain.Version, 1);
                module.GateDecisions++;

                plane.Commit(
                    message,
                    NarrativeKeys.GateChangedSchema,
                    new FrozenPayload(NarrativePayloadCodec.EncodeGateChange(
                        new NarrativeGateChange(decision, evaluatedVersion))),
                    plane.ExecutingStep,
                    out string _);
            }

            plane.ReleaseConsumed(NarrativeKeys.GateOwner);
        }
    }

    /// <summary>
    /// Encounter stage: it reacts to the same committed fact under the encounter domain's own lifecycle policy. An
    /// encounter is a narrative beat, so it needs no actor, vitality or physics concept (P-001).
    /// </summary>
    [DisableAutoCreation]
    public partial class NarrativeEncounterSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!NarrativeModule.TryGet(World, out NarrativeModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(NarrativeKeys.EncounterOwner);
            EntityManager entityManager = EntityManager;

            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                byte[] payload = plane.PayloadOf(message);

                if (plane.Readers.TryRead<NarrativeObservation>(
                        message.PayloadSchema,
                        payload,
                        out NarrativeObservation observation,
                        out string _) != PayloadDecodeOutcome.Decoded)
                {
                    continue;
                }

                if (!module.TryEntity(message.Target, out Entity entity) || !entityManager.Exists(entity))
                {
                    continue;
                }

                int status = NarrativeState.ReadOrDefault(
                    entityManager, entity, NarrativeKeys.EncounterOwner, NarrativeKeys.EncounterStatusSlot,
                    NarrativeEncounterStatus.Idle);

                if (!NarrativeEncounterRules.TryReactToCondition(
                        status,
                        observation.Value == RulesNarrativeFacts.True,
                        out int next))
                {
                    continue;
                }

                NarrativeState.Write(
                    entityManager, entity, NarrativeKeys.EncounterOwner, NarrativeKeys.EncounterStatusSlot,
                    NarrativeKeys.EncounterDomain.Version, next);
                NarrativeState.Advance(
                    entityManager, module.RootEntity, NarrativeKeys.TrailOwner, NarrativeKeys.TrailHooksSlot,
                    NarrativeKeys.TrailDomain.Version, 1);
                module.EncounterHooks++;
            }

            plane.ReleaseConsumed(NarrativeKeys.EncounterOwner);
        }
    }

    /// <summary>
    /// Output stage: the last stage of the compiled graph. It reads the earlier stages' results after they ran and
    /// projects the trail, which is the observable proof of the compiled order (P-034, P-040).
    /// </summary>
    [DisableAutoCreation]
    public partial class NarrativeOutputSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!NarrativeModule.TryGet(World, out NarrativeModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            int steps = NarrativeState.ReadOrDefault(
                entityManager, module.RootEntity, NarrativeKeys.TrailOwner, NarrativeKeys.TrailStepsSlot, 0);

            NarrativeState.Write(
                entityManager, module.RootEntity, NarrativeKeys.TrailOwner, NarrativeKeys.TrailProjectedSlot,
                NarrativeKeys.TrailDomain.Version, steps);
            module.OutputRuns++;
        }
    }
}
