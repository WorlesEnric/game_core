// GameCore.Gameplay.Interaction - the interaction plugin's kernel half: payloads, readers, records, the per-world module
// and the system (P1.3; P-032, P-042, P-044).
//
//   interact.use       actor target, actor key     -> InteractionSucceeded (A actor, B new state, C uses, D old state)
//                                                     + InteractableStateChanged (A new, B old) when the state changed
//                                                  -> InteractionRefused (A actor, B refusal code, C state) otherwise
//   interact.setState  state                       -> InteractableStateChanged (refusals are rejected)
//   interact.trigger   actor key, entered (1/0)    -> TriggerEntered / TriggerExited (A actor, B occupant count,
//                                                     C first entry); interact.occupants is a bit set of actor keys
//
// A use is refused as a committed outcome (InteractionRefused carries the code, P-045): the command commits, the slots
// do not change. Only a malformed command or an unknown target is rejected. The use range is measured from the actor's
// committed world.pos (authoritative for every entity, P1.7a) to the interactable's world.pos. The condition ref is
// evaluated through IConditionEvaluator with a committed-slot context: while the interactable is Locked it is the
// unlock condition, otherwise the use precondition (NullConditionEvaluator: Unknown, so uses pass and locks hold).
// After the commands, every interactable with a running cooldown counts it down by the step length.
//
// P1.7a (A1): a committed success or trigger transit is handed to the world's step tap (GameplayStepTaps.Of), and the
// interactable's action reference is recorded as an outbox obligation in the same step; the host-side dispatcher runs
// the action itself only when the world has no tap.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Interaction;
using GameCore.Rules.Gameplay.Player;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Interaction
{
    /// <summary>interact.use payload: the actor's target and stable key.</summary>
    public readonly struct UsePayload
    {
        public const int Length = 20;

        public UsePayload(TargetId actor, int actorKey)
        {
            Actor = actor;
            ActorKey = actorKey;
        }

        public TargetId Actor { get; }

        public int ActorKey { get; }

        public static FrozenPayload Encode(TargetId actor, int actorKey) => new GameplayPayloadWriter().Id(actor.Value).Int32(actorKey).Freeze();
    }

    /// <summary>interact.setState / interact.trigger payload: two int32 values.</summary>
    public readonly struct InteractionValuePayload
    {
        public const int Length = 8;

        public InteractionValuePayload(int a, int b)
        {
            A = a;
            B = b;
        }

        public int A { get; }

        public int B { get; }

        public static FrozenPayload Encode(int a, int b) => new GameplayPayloadWriter().Int32(a).Int32(b).Freeze();
    }

    public sealed class UseReader : ICommandPayloadReader<UsePayload>
    {
        public SchemaRef Schema => InteractionSlots.UseCommand;

        public UsePayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(UsePayload.Length))
            {
                throw new FormatException("an interact.use command is exactly " + UsePayload.Length + " bytes");
            }

            return new UsePayload(new TargetId(reader.Id()), reader.Int32());
        }
    }

    public sealed class InteractionValueReader : ICommandPayloadReader<InteractionValuePayload>
    {
        public InteractionValueReader(SchemaRef schema)
        {
            Schema = schema;
        }

        public SchemaRef Schema { get; }

        public InteractionValuePayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(InteractionValuePayload.Length))
            {
                throw new FormatException("an interaction value command is exactly " + InteractionValuePayload.Length + " bytes");
            }

            return new InteractionValuePayload(reader.Int32(), reader.Int32());
        }
    }

    public static class InteractionReaders
    {
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            Require(readers.TryBind(new UseReader(), out string failure), failure);
            Require(readers.TryBind(new InteractionValueReader(InteractionSlots.SetStateCommand), out failure), failure);
            Require(readers.TryBind(new InteractionValueReader(InteractionSlots.TriggerCommand), out failure), failure);
        }

        private static void Require(bool bound, string failure)
        {
            if (!bound)
            {
                throw new InvalidOperationException("interaction reader registration failed: " + failure);
            }
        }
    }

    /// <summary>One interactable or trigger of a world.</summary>
    public sealed class InteractableRecord
    {
        public InteractableRecord(TargetId target, string authoringId, string name, InteractableDefinition? interactable, TriggerDefinition? trigger)
        {
            Target = target;
            AuthoringId = authoringId ?? string.Empty;
            Key = AuthoringIds.IsValid(authoringId) ? AuthoringIds.StableKey(authoringId!) : 0;
            Name = name ?? string.Empty;
            Interactable = interactable;
            Trigger = trigger;
            Profile = interactable != null ? interactable.ToProfile() : new InteractableProfile(InteractableKind.Point, 0, 0, 0);
        }

        public TargetId Target { get; }

        public string AuthoringId { get; }

        public int Key { get; }

        public string Name { get; }

        public InteractableDefinition? Interactable { get; }

        public TriggerDefinition? Trigger { get; }

        public bool IsTrigger => Trigger != null;

        /// <summary>The definition's numeric tuning; <see cref="Retune"/> re-reads it (SADR-013 Live edit, P1.7a).</summary>
        public InteractableProfile Profile { get; private set; }

        /// <summary>Re-reads the numeric tuning from the (edited) interactable definition; slots are untouched. True when it changed.</summary>
        public bool Retune()
        {
            if (Interactable == null)
            {
                return false;
            }

            InteractableProfile next = Interactable.ToProfile();
            bool changed = !next.Equals(Profile);
            Profile = next;
            return changed;
        }

        public string ConditionRef => Interactable != null ? Interactable.ConditionRef : (Trigger != null ? Trigger.ConditionRef : string.Empty);

        public string ActionRef => Interactable != null ? Interactable.ActionRef : (Trigger != null ? Trigger.ActionRef : string.Empty);
    }

    /// <summary>The interaction plugin's state of one world. Instance state only.</summary>
    public sealed class InteractionModule
    {
        private readonly Dictionary<TargetId, InteractableRecord> byTarget = new Dictionary<TargetId, InteractableRecord>();
        private readonly Dictionary<int, InteractableRecord> byKey = new Dictionary<int, InteractableRecord>();
        private readonly List<InteractableRecord> ordered = new List<InteractableRecord>();

        public InteractionModule(UnityWorldHost host, TargetRegistry registry, ICommittedSlotReader slots, int stepMilliseconds, IReadOnlyList<InteractableRecord> records)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
            StepMilliseconds = stepMilliseconds < 1 ? 20 : stepMilliseconds;
            for (int i = 0; i < records.Count; i++)
            {
                InteractableRecord record = records[i];
                if (record.Key == 0 || byTarget.ContainsKey(record.Target))
                {
                    continue;
                }

                byTarget[record.Target] = record;
                byKey[record.Key] = record;
                ordered.Add(record);
            }

            ordered.Sort((l, r) => l.Key.CompareTo(r.Key));
        }

        public UnityWorldHost Host { get; }

        public TargetRegistry Registry { get; }

        /// <summary>Slot reads for range and condition contexts.</summary>
        public ICommittedSlotReader Slots { get; }

        public int StepMilliseconds { get; }

        public IConditionEvaluator Conditions { get; set; } = new NullConditionEvaluator();

        public IReadOnlyList<InteractableRecord> Records => ordered;

        public int Successes { get; private set; }

        public int Refusals { get; private set; }

        public int Rejected { get; private set; }

        public int Transits { get; private set; }

        public int DroppedEvents { get; private set; }

        public bool TryGet(TargetId target, out InteractableRecord? record) => byTarget.TryGetValue(target, out record);

        /// <summary>Re-reads every interactable's numeric tuning (a SADR-013 Live edit, between frames). Returns how many changed.</summary>
        public int RetuneAll()
        {
            int changed = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Retune())
                {
                    changed++;
                }
            }

            return changed;
        }

        public bool TryGetByKey(int key, out InteractableRecord? record) => byKey.TryGetValue(key, out record);

        internal void CountSuccess() => Successes++;

        internal void CountRefusal() => Refusals++;

        internal void CountRejected() => Rejected++;

        internal void CountTransit() => Transits++;

        internal void CountDropped() => DroppedEvents++;

        public static InteractableSnapshot Read(EntityManager entityManager, Entity entity) =>
            new InteractableSnapshot(
                SlotState.ReadOrDefault(entityManager, entity, InteractionSlots.Owner, InteractionSlots.State, 0),
                SlotState.ReadOrDefault(entityManager, entity, InteractionSlots.Owner, InteractionSlots.Uses, 0),
                SlotState.ReadOrDefault(entityManager, entity, InteractionSlots.Owner, InteractionSlots.CooldownMs, 0));

        public static void Write(EntityManager entityManager, Entity entity, InteractableSnapshot state)
        {
            SlotState.Write(entityManager, entity, InteractionSlots.Owner, InteractionSlots.State, state.State);
            SlotState.Write(entityManager, entity, InteractionSlots.Owner, InteractionSlots.Uses, state.Uses);
            SlotState.Write(entityManager, entity, InteractionSlots.Owner, InteractionSlots.CooldownMs, state.CooldownMilliseconds);
        }

        /// <summary>The committed planar position of an actor: world.pos only (the one pose every plugin writes).</summary>
        public static bool TryActorPosition(ICommittedSlotReader slots, TargetId actor, out int x, out int z)
        {
            if (slots.TryRead(actor, GameplaySlots.WorldOwner, GameplaySlots.PosX, out x) && slots.TryRead(actor, GameplaySlots.WorldOwner, GameplaySlots.PosZ, out z))
            {
                return true;
            }

            z = 0;
            return false;
        }

        public static ConditionAnswer Answer(ConditionVerdict verdict) =>
            verdict == ConditionVerdict.True ? ConditionAnswer.True : (verdict == ConditionVerdict.False ? ConditionAnswer.False : ConditionAnswer.Unknown);
    }

    /// <summary>The interaction stage: uses, state changes and trigger transits, then cooldowns.</summary>
    [DisableAutoCreation]
    public partial class InteractionCommandSystem : SystemBase
    {
        public InteractionModule? Module { get; set; }

        protected override void OnUpdate()
        {
            InteractionModule? module = Module;
            if (module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            IGameplayStepTap? tap = GameplayStepTaps.Of(World);
            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(InteractionDeclarations.Owner);
            var used = new HashSet<TargetId>();
            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                if (!module.TryGet(message.Target, out InteractableRecord? record) || record == null
                    || !module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity entity) || !entityManager.Exists(entity))
                {
                    if (message.Route.Equals(InteractionSlots.UseRoute) && TryRead(plane, message, out UsePayload unknown))
                    {
                        // A use of something that is not interactable is a committed refusal, not a malformed command.
                        Refuse(module, plane, message, unknown.ActorKey, InteractionRefusal.NotInteractable, 0);
                        continue;
                    }

                    module.CountRejected();
                    plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                    continue;
                }

                if (message.Route.Equals(InteractionSlots.UseRoute))
                {
                    Use(module, plane, entityManager, entity, record, message, tap);
                    used.Add(record.Target);
                }
                else if (message.Route.Equals(InteractionSlots.SetStateRoute))
                {
                    SetState(module, plane, entityManager, entity, record, message);
                }
                else if (message.Route.Equals(InteractionSlots.TriggerRoute))
                {
                    Transit(module, plane, entityManager, entity, record, message, tap);
                }
                else
                {
                    module.CountRejected();
                    plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                }
            }

            IReadOnlyList<InteractableRecord> records = module.Records;
            for (int i = 0; i < records.Count; i++)
            {
                InteractableRecord record = records[i];
                if (used.Contains(record.Target) || !module.Registry.TryResolveTarget(record.Target, out TargetHandle _, out Entity entity))
                {
                    continue;
                }

                InteractableSnapshot state = InteractionModule.Read(entityManager, entity);
                if (state.CooldownMilliseconds > 0)
                {
                    InteractionModule.Write(entityManager, entity, InteractionRules.Tick(state, module.StepMilliseconds));
                }
            }

            plane.ReleaseConsumed(InteractionDeclarations.Owner);
        }

        private static bool TryRead(WorldMessagePlane plane, StepMessage message, out UsePayload use)
        {
            byte[] payload = plane.PayloadOf(message);
            return plane.Readers.TryRead<UsePayload>(message.PayloadSchema, payload, out use, out string _) == PayloadDecodeOutcome.Decoded;
        }

        private static bool TryRead(WorldMessagePlane plane, StepMessage message, out InteractionValuePayload value)
        {
            byte[] payload = plane.PayloadOf(message);
            return plane.Readers.TryRead<InteractionValuePayload>(message.PayloadSchema, payload, out value, out string _) == PayloadDecodeOutcome.Decoded;
        }

        private static void Use(InteractionModule module, WorldMessagePlane plane, EntityManager entityManager, Entity entity, InteractableRecord record, StepMessage message, IGameplayStepTap? tap)
        {
            if (!TryRead(plane, message, out UsePayload use))
            {
                module.CountRejected();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            InteractableSnapshot state = InteractionModule.Read(entityManager, entity);
            if (record.IsTrigger)
            {
                Refuse(module, plane, message, use.ActorKey, InteractionRefusal.NotInteractable, state.State);
                return;
            }

            int distance = int.MaxValue;
            if (InteractionModule.TryActorPosition(module.Slots, use.Actor, out int ax, out int az))
            {
                int tx = SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0);
                int tz = SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0);
                distance = PlanarMath.Distance(ax, az, tx, tz);
            }

            ConditionAnswer answer = ConditionAnswer.Unknown;
            if (!string.IsNullOrEmpty(record.ConditionRef))
            {
                var context = new InteractionContext(record.AuthoringId, record.Key, use.ActorKey, state.State, module.Slots);
                answer = InteractionModule.Answer(module.Conditions.Evaluate(record.ConditionRef, context));
            }

            bool locked = state.State == InteractableStates.Locked;
            InteractionOutcome outcome = InteractionRules.Use(
                state,
                record.Profile,
                distance,
                locked ? ConditionAnswer.Unknown : answer,
                locked ? answer : ConditionAnswer.Unknown);
            if (!outcome.Succeeded)
            {
                Refuse(module, plane, message, use.ActorKey, outcome.Refusal, state.State);
                return;
            }

            InteractableSnapshot after = outcome.After;
            FrozenPayload succeeded = GameplayActorEvent.Encode(message.Target, use.ActorKey, after.State, after.Uses, state.State);
            if (!plane.Commit(message, InteractionSlots.SucceededEvent, succeeded, plane.ExecutingStep, out string _))
            {
                module.CountRejected();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            InteractionModule.Write(entityManager, entity, after);
            module.CountSuccess();

            // P1.7a (A1): the success's rule triggers and quest signal, and the interactable's action, become outbox
            // obligations in this step (the host-side dispatcher no longer runs the action when a tap is set).
            if (tap != null)
            {
                tap.OnCommitted(InteractionSlots.SucceededEvent, succeeded, message.Request);
                if (!string.IsNullOrEmpty(record.ActionRef))
                {
                    tap.OnActionDemand(record.ActionRef, record.AuthoringId, record.Key, use.ActorKey, after.State, message.Request);
                }
            }
            if (outcome.StateChanged)
            {
                Emit(module, plane, message, InteractionSlots.StateChangedEvent, after.State, state.State, use.ActorKey, 0);
            }
        }

        private static void SetState(InteractionModule module, WorldMessagePlane plane, EntityManager entityManager, Entity entity, InteractableRecord record, StepMessage message)
        {
            if (!TryRead(plane, message, out InteractionValuePayload value) || record.IsTrigger)
            {
                module.CountRejected();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            InteractableSnapshot state = InteractionModule.Read(entityManager, entity);
            InteractionOutcome outcome = InteractionRules.SetState(state, record.Profile.Kind, value.A);
            if (!outcome.Succeeded)
            {
                module.CountRejected();
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            if (!plane.Commit(message, InteractionSlots.StateChangedEvent,
                    GameplayActorEvent.Encode(message.Target, outcome.After.State, state.State, 0, 0), plane.ExecutingStep, out string _))
            {
                module.CountRejected();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            InteractionModule.Write(entityManager, entity, outcome.After);
        }

        private static void Transit(InteractionModule module, WorldMessagePlane plane, EntityManager entityManager, Entity entity, InteractableRecord record, StepMessage message, IGameplayStepTap? tap)
        {
            if (!TryRead(plane, message, out InteractionValuePayload value) || !record.IsTrigger)
            {
                module.CountRejected();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            bool entered = value.B != 0;
            int occupants = SlotState.ReadOrDefault(entityManager, entity, InteractionSlots.Owner, InteractionSlots.Occupants, 0);
            if (entered && !string.IsNullOrEmpty(record.ConditionRef))
            {
                var context = new InteractionContext(record.AuthoringId, record.Key, value.A, OccupancyRules.Count(occupants), module.Slots);
                if (module.Conditions.Evaluate(record.ConditionRef, context) == ConditionVerdict.False)
                {
                    module.CountRejected();
                    plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                    return;
                }
            }

            OccupancyOutcome outcome = OccupancyRules.Transit(occupants, value.A, entered, record.Trigger!.MaxOccupants);
            if (!outcome.Accepted)
            {
                module.CountRejected();
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            SchemaRef schema = entered ? InteractionSlots.TriggerEnteredEvent : InteractionSlots.TriggerExitedEvent;
            FrozenPayload transit = GameplayActorEvent.Encode(message.Target, value.A, outcome.Occupants, outcome.FirstEntered ? 1 : 0, 0);
            if (!plane.Commit(message, schema, transit, plane.ExecutingStep, out string _))
            {
                module.CountRejected();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            SlotState.Write(entityManager, entity, InteractionSlots.Owner, InteractionSlots.Occupants, outcome.Mask);
            module.CountTransit();
            if (tap != null)
            {
                tap.OnCommitted(schema, transit, message.Request);
                if (entered && outcome.FirstEntered && !string.IsNullOrEmpty(record.ActionRef))
                {
                    tap.OnActionDemand(record.ActionRef, record.AuthoringId, record.Key, value.A, outcome.Occupants, message.Request);
                }
            }
        }

        private static void Refuse(InteractionModule module, WorldMessagePlane plane, StepMessage message, int actorKey, InteractionRefusal refusal, int state)
        {
            if (plane.Commit(message, InteractionSlots.RefusedEvent,
                    GameplayActorEvent.Encode(message.Target, actorKey, (int)refusal, state, 0), plane.ExecutingStep, out string _))
            {
                module.CountRefusal();
                return;
            }

            module.CountRejected();
            plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
        }

        private static void Emit(InteractionModule module, WorldMessagePlane plane, StepMessage cause, SchemaRef schema, int a, int b, int c, int d)
        {
            var copy = new StepMessage(
                cause.Step,
                cause.Epoch,
                cause.Request,
                cause.Route,
                cause.Owner,
                cause.Target,
                cause.PayloadSchema,
                MessageKind.Request,
                cause.Order,
                InteractionDeclarations.InternalProducer,
                0,
                0);
            if (!plane.Commit(copy, schema, GameplayActorEvent.Encode(cause.Target, a, b, c, d), plane.ExecutingStep, out string _))
            {
                module.CountDropped();
            }
        }
    }
}
