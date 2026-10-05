// GameCore.Gameplay.Npc - the npc plugin's kernel half: payloads, readers, records, the logical mover, the per-world
// module and the system (P1.3; P-032, P-042, P-044).
//
//   npc.setBehaviour  behaviour code                    -> NpcStateChanged (A new state, B old, C behaviour, D phase)
//   npc.goTo          x, z (mm)                         -> NpcStateChanged (to approach); NpcArrived on arrival
//   npc.face          x, z (mm)                         -> NpcUpdated (A = 1 yaw, B = yaw)
//   npc.setMood       mood (-100..100)                  -> NpcUpdated (A = 2 mood, B = mood)
//   npc.converse      hold ms (0 ends the conversation) -> NpcStateChanged
//
// After the commands, every step advances every NPC with the NpcLogicalMover, in key order: NPCs whose region is
// resident every step, NPCs elsewhere every UnloadedStride-th step with that many steps of time (all NPCs move whether
// their region is loaded or not; their views exist only while it is). A schedule phase change, an arrival or a state
// change commits an event through an internal message (default causal request = internal work, P-045).
//
// P1.7a:
//   * Pose authority (A4): world.posX/posZ/yaw is the NPC's authoritative pose (world.posY is left to the world). The
//     system reads it for every decision and writes it in the step that moves the NPC; npc.posX/posZ/yaw are mirrors. A
//     mirror that differs from world.pos at the start of an update means a host/Studio world.place moved the NPC: the
//     pose is adopted, and an NPC that was standing (not walking a route or approaching) keeps standing at the new spot
//     instead of walking back - so a moved NPC stays where it was put across region unloads, saves and restores.
//   * Schedules read GameplayClock (A5): world time = step x stepMs + the schedule's start offset.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Npc;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Npc
{
    /// <summary>Payload of every npc command: up to two int32 values (zero-padded).</summary>
    public readonly struct NpcCommandPayload
    {
        public const int Length = 8;

        public NpcCommandPayload(int a, int b)
        {
            A = a;
            B = b;
        }

        public int A { get; }

        public int B { get; }

        public static FrozenPayload Encode(int a, int b) => new GameplayPayloadWriter().Int32(a).Int32(b).Freeze();
    }

    public sealed class NpcCommandReader : ICommandPayloadReader<NpcCommandPayload>
    {
        public NpcCommandReader(SchemaRef schema)
        {
            Schema = schema;
        }

        public SchemaRef Schema { get; }

        public NpcCommandPayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(NpcCommandPayload.Length))
            {
                throw new FormatException("an npc command is exactly " + NpcCommandPayload.Length + " bytes");
            }

            return new NpcCommandPayload(reader.Int32(), reader.Int32());
        }
    }

    public static class NpcReaders
    {
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            SchemaRef[] schemas =
            {
                NpcSlots.SetBehaviourCommand, NpcSlots.GoToCommand, NpcSlots.FaceCommand, NpcSlots.SetMoodCommand, NpcSlots.ConverseCommand,
            };
            for (int i = 0; i < schemas.Length; i++)
            {
                if (!readers.TryBind(new NpcCommandReader(schemas[i]), out string failure))
                {
                    throw new InvalidOperationException("npc reader registration failed: " + failure);
                }
            }
        }
    }

    /// <summary>What NpcUpdated's A field names.</summary>
    public static class NpcUpdate
    {
        public const int Yaw = 1;
        public const int Mood = 2;
    }

    /// <summary>One NPC of a world: its target, definition data in rule units, and its region.</summary>
    public sealed class NpcRecord
    {
        public NpcRecord(
            TargetId target,
            string authoringId,
            string name,
            NpcDefinition? definition,
            NpcProfile profile,
            IReadOnlyList<PatrolPoint> route,
            IReadOnlyList<SchedulePhase> phases,
            int dayLengthMilliseconds,
            int startOffsetMilliseconds)
        {
            Target = target;
            AuthoringId = authoringId ?? string.Empty;
            Key = AuthoringIds.IsValid(authoringId) ? AuthoringIds.StableKey(authoringId!) : 0;
            Name = name ?? string.Empty;
            Definition = definition;
            Profile = profile;
            Route = route ?? Array.Empty<PatrolPoint>();
            Phases = phases ?? Array.Empty<SchedulePhase>();
            DayLengthMilliseconds = dayLengthMilliseconds;
            StartOffsetMilliseconds = startOffsetMilliseconds;
        }

        public TargetId Target { get; }

        public string AuthoringId { get; }

        public int Key { get; }

        public string Name { get; }

        public NpcDefinition? Definition { get; }

        /// <summary>The definition's numeric tuning in rule units; <see cref="Retune"/> re-reads it (SADR-013 Live edit, P1.7a).</summary>
        public NpcProfile Profile { get; private set; }

        /// <summary>Re-reads the numeric tuning from the (edited) definition; the slot state is untouched. True when it changed.</summary>
        public bool Retune()
        {
            if (Definition == null)
            {
                return false;
            }

            NpcProfile next = Definition.ToProfile();
            bool changed = !next.Equals(Profile);
            Profile = next;
            return changed;
        }

        public IReadOnlyList<PatrolPoint> Route { get; }

        public IReadOnlyList<SchedulePhase> Phases { get; }

        public int DayLengthMilliseconds { get; }

        public int StartOffsetMilliseconds { get; }

        public bool HasSchedule => Phases.Count > 0 && DayLengthMilliseconds > 0;

        public string DisplayName => Definition != null ? Definition.DisplayName : Name;

        public string DialogueGraph => Definition != null ? Definition.DialogueGraph : string.Empty;
    }

    /// <summary>
    /// The logical mover: advances one NPC by one update (schedule phase first, then the behaviour step). Pure apart
    /// from the record it reads; every NPC is advanced by it whether or not its region is loaded.
    /// </summary>
    public static class NpcLogicalMover
    {
        /// <summary>The result of one update: the new state, the phase change (or -1), and the step transition.</summary>
        public readonly struct Update
        {
            public Update(NpcSnapshot before, NpcSnapshot after, bool phaseChanged, NpcTransition step)
            {
                Before = before;
                After = after;
                PhaseChanged = phaseChanged;
                Step = step;
            }

            public NpcSnapshot Before { get; }

            public NpcSnapshot After { get; }

            public bool PhaseChanged { get; }

            public NpcTransition Step { get; }

            public bool StateChanged => Before.State != After.State || Before.Behaviour != After.Behaviour || PhaseChanged;
        }

        public static Update Advance(NpcSnapshot state, NpcRecord record, ulong step, int stepMilliseconds, bool resident, int unloadedStride)
        {
            NpcSnapshot current = state;
            bool phaseChanged = false;
            if (record.HasSchedule)
            {
                long time = GameplayClock.StepTimeMs(step, stepMilliseconds) + record.StartOffsetMilliseconds;
                int phase = ScheduleRules.PhaseAt(time, record.DayLengthMilliseconds, record.Phases);
                if (phase >= 0 && phase != current.SchedulePhase)
                {
                    NpcTransition entered = NpcRules.EnterPhase(current, phase, record.Phases[phase], record.Route);
                    if (entered.Accepted)
                    {
                        current = entered.State;
                        phaseChanged = true;
                    }
                }
            }

            int elapsed = NpcRules.ElapsedOf(stepMilliseconds, resident, unloadedStride);
            NpcTransition moved = NpcRules.Step(current, record.Profile, record.Route, elapsed);
            return new Update(state, moved.State, phaseChanged, moved);
        }
    }

    /// <summary>The npc plugin's state of one world. Instance state only.</summary>
    public sealed class NpcModule
    {
        private readonly Dictionary<TargetId, NpcRecord> byTarget = new Dictionary<TargetId, NpcRecord>();
        private readonly Dictionary<int, NpcRecord> byKey = new Dictionary<int, NpcRecord>();
        private readonly List<NpcRecord> ordered = new List<NpcRecord>();

        public NpcModule(UnityWorldHost host, TargetRegistry registry, int stepMilliseconds, int unloadedStride, IReadOnlyList<NpcRecord> npcs)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            StepMilliseconds = stepMilliseconds < 1 ? 20 : stepMilliseconds;
            UnloadedStride = unloadedStride < 1 ? 1 : unloadedStride;
            for (int i = 0; i < npcs.Count; i++)
            {
                NpcRecord record = npcs[i];
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

        public int StepMilliseconds { get; }

        public int UnloadedStride { get; }

        /// <summary>NPCs in canonical (key) order.</summary>
        public IReadOnlyList<NpcRecord> Npcs => ordered;

        /// <summary>Region keys currently resident (the system reads them from world.residency slots each step).</summary>
        public Func<int, bool>? IsRegionResident { get; set; }

        public int Commands { get; private set; }

        public int Refused { get; private set; }

        public int Updates { get; private set; }

        public int UnloadedUpdates { get; private set; }

        public int Arrivals { get; private set; }

        public int PhaseChanges { get; private set; }

        public int DroppedEvents { get; private set; }

        /// <summary>Updates that adopted a pose the world wrote (world.place), P1.7a.</summary>
        public int Adoptions { get; private set; }

        public bool TryGet(TargetId target, out NpcRecord? record) => byTarget.TryGetValue(target, out record);

        /// <summary>Re-reads every NPC's numeric tuning from its definition (a SADR-013 Live edit, between frames). Returns how many changed.</summary>
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

        public bool TryGetByKey(int key, out NpcRecord? record) => byKey.TryGetValue(key, out record);

        internal void CountCommand() => Commands++;

        internal void CountRefused() => Refused++;

        internal void CountUpdate(bool resident)
        {
            Updates++;
            if (!resident)
            {
                UnloadedUpdates++;
            }
        }

        internal void CountArrival() => Arrivals++;

        internal void CountPhase() => PhaseChanges++;

        internal void CountDropped() => DroppedEvents++;

        internal void CountAdoption() => Adoptions++;

        /// <summary>The NPC's state with its authoritative pose (world.posX/posZ/yaw; the npc.pos mirror only as a fallback).</summary>
        public static NpcSnapshot Read(EntityManager entityManager, Entity entity)
        {
            int mirrorX = SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.PosX, 0);
            int mirrorZ = SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.PosZ, 0);
            int mirrorYaw = SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.Yaw, 0);
            return new NpcSnapshot(
                SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.State, 0),
                SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.Behaviour, 0),
                SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.PatrolIndex, 0),
                SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.Mood, 0),
                SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.SchedulePhase, 0),
                SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.TargetX, 0),
                SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.TargetZ, 0),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosX, mirrorX),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosZ, mirrorZ),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.Yaw, mirrorYaw),
                SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, NpcSlots.TimerMs, 0));
        }

        /// <summary>True when the npc.pos mirror differs from world.pos (a host/Studio world.place moved the NPC).</summary>
        public static bool MirrorDiffers(EntityManager entityManager, Entity entity)
        {
            return Differs(entityManager, entity, NpcSlots.PosX, GameplaySlots.PosX)
                || Differs(entityManager, entity, NpcSlots.PosZ, GameplaySlots.PosZ)
                || Differs(entityManager, entity, NpcSlots.Yaw, GameplaySlots.Yaw);
        }

        /// <summary>
        /// The state after adopting a pose the world wrote: a standing NPC (idle, or waiting at a point) takes the new spot
        /// as its target too, so it stays there; a walking NPC resumes its route from the new spot.
        /// </summary>
        public static NpcSnapshot Adopt(NpcSnapshot state)
        {
            bool walking = state.StateCode == NpcStateCode.Patrol || state.StateCode == NpcStateCode.Approach;
            return walking ? state : state.With(targetX: state.PosX, targetZ: state.PosZ);
        }

        private static bool Differs(EntityManager entityManager, Entity entity, SlotId mirror, SlotId authoritative)
        {
            return SlotState.TryRead(entityManager, entity, GameplaySlots.WorldOwner, authoritative, out int world)
                && SlotState.ReadOrDefault(entityManager, entity, NpcSlots.Owner, mirror, world) != world;
        }

        /// <summary>Writes the state: the pose to world.posX/posZ/yaw (authoritative) and to the npc.pos mirror.</summary>
        public static void Write(EntityManager entityManager, Entity entity, NpcSnapshot state)
        {
            SlotState.Write(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosX, state.PosX);
            SlotState.Write(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosZ, state.PosZ);
            SlotState.Write(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.Yaw, state.Yaw);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.State, state.State);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.Behaviour, state.Behaviour);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.PatrolIndex, state.PatrolIndex);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.Mood, state.Mood);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.SchedulePhase, state.SchedulePhase);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.TargetX, state.TargetX);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.TargetZ, state.TargetZ);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.PosX, state.PosX);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.PosZ, state.PosZ);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.Yaw, state.Yaw);
            SlotState.Write(entityManager, entity, NpcSlots.Owner, NpcSlots.TimerMs, state.TimerMilliseconds);
        }
    }

    /// <summary>The npc stage: commands, then every NPC's logical update.</summary>
    [DisableAutoCreation]
    public partial class NpcCommandSystem : SystemBase
    {
        public NpcModule? Module { get; set; }

        protected override void OnUpdate()
        {
            NpcModule? module = Module;
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
            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(NpcDeclarations.Owner);
            for (int i = 0; i < batch.Count; i++)
            {
                Execute(module, plane, entityManager, batch[i]);
            }

            ulong step = plane.ExecutingStep.Value;
            IReadOnlyList<NpcRecord> npcs = module.Npcs;
            for (int i = 0; i < npcs.Count; i++)
            {
                NpcRecord record = npcs[i];
                if (!module.Registry.TryResolveTarget(record.Target, out TargetHandle _, out Entity entity) || !entityManager.Exists(entity))
                {
                    continue;
                }

                int region = SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.Region, 0);
                bool resident = module.IsRegionResident == null || module.IsRegionResident(region);
                if (!NpcRules.ShouldUpdate(step, resident, module.UnloadedStride))
                {
                    continue;
                }

                NpcSnapshot before = NpcModule.Read(entityManager, entity);
                if (NpcModule.MirrorDiffers(entityManager, entity))
                {
                    before = NpcModule.Adopt(before);
                    module.CountAdoption();
                }

                NpcLogicalMover.Update update = NpcLogicalMover.Advance(before, record, step, module.StepMilliseconds, resident, module.UnloadedStride);
                NpcModule.Write(entityManager, entity, update.After);
                module.CountUpdate(resident);
                if (update.PhaseChanged)
                {
                    module.CountPhase();
                }

                if (update.StateChanged)
                {
                    Emit(module, plane, record.Target, NpcSlots.StateChangedEvent, update.After.State, before.State, update.After.Behaviour, update.After.SchedulePhase);
                }

                if (update.Step.Arrived)
                {
                    module.CountArrival();
                    Emit(module, plane, record.Target, NpcSlots.ArrivedEvent, update.Step.ArrivedIndex, update.After.PosX, update.After.PosZ, before.State);
                }
            }

            plane.ReleaseConsumed(NpcDeclarations.Owner);
        }

        private static void Emit(NpcModule module, WorldMessagePlane plane, TargetId target, SchemaRef schema, int a, int b, int c, int d)
        {
            var message = new StepMessage(
                plane.ExecutingStep,
                default(AssemblyEpoch),
                default(OperationId),
                NpcDeclarations.StepRoute,
                NpcDeclarations.Owner,
                target,
                schema,
                MessageKind.Request,
                default(MessageOrderKey),
                NpcDeclarations.InternalProducer,
                0,
                0);
            if (!plane.Commit(message, schema, GameplayActorEvent.Encode(target, a, b, c, d), plane.ExecutingStep, out string _))
            {
                module.CountDropped();
            }
        }

        private static void Execute(NpcModule module, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<NpcCommandPayload>(message.PayloadSchema, payload, out NpcCommandPayload command, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            if (!module.TryGet(message.Target, out NpcRecord? record) || record == null
                || !module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity entity) || !entityManager.Exists(entity))
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                return;
            }

            NpcSnapshot state = NpcModule.Read(entityManager, entity);
            NpcTransition transition;
            SchemaRef eventSchema = NpcSlots.StateChangedEvent;
            int updateKind = 0;
            if (message.Route.Equals(NpcSlots.SetBehaviourRoute))
            {
                transition = NpcRules.SetBehaviour(state, command.A, record.Route);
            }
            else if (message.Route.Equals(NpcSlots.GoToRoute))
            {
                transition = NpcRules.GoTo(state, command.A, command.B);
            }
            else if (message.Route.Equals(NpcSlots.FaceRoute))
            {
                transition = NpcRules.Face(state, command.A, command.B);
                eventSchema = NpcSlots.UpdatedEvent;
                updateKind = NpcUpdate.Yaw;
            }
            else if (message.Route.Equals(NpcSlots.SetMoodRoute))
            {
                transition = NpcRules.SetMood(state, command.A);
                eventSchema = NpcSlots.UpdatedEvent;
                updateKind = NpcUpdate.Mood;
            }
            else if (message.Route.Equals(NpcSlots.ConverseRoute))
            {
                transition = command.A > 0 ? NpcRules.Converse(state, command.A) : NpcRules.EndConverse(state, record.Route);
            }
            else
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            if (!transition.Accepted)
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            NpcSnapshot next = transition.State;
            FrozenPayload eventPayload = updateKind == 0
                ? GameplayActorEvent.Encode(message.Target, next.State, state.State, next.Behaviour, next.SchedulePhase)
                : GameplayActorEvent.Encode(message.Target, updateKind, updateKind == NpcUpdate.Yaw ? next.Yaw : next.Mood, 0, 0);
            if (!plane.Commit(message, eventSchema, eventPayload, plane.ExecutingStep, out string _))
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            NpcModule.Write(entityManager, entity, next);
            module.CountCommand();
        }
    }
}
