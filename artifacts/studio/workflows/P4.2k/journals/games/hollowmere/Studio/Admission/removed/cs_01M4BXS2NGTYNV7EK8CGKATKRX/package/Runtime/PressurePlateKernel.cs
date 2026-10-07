#nullable enable
// Hollowmere.Mechanism.PressurePlate - the pressure plate plugin's kernel half: payload codec, reader, recipe, the
// per-world module and the command system (W-MECH-01 sample).
//
//   plate.press {actor: TargetId, load: int32}   one actor steps on (load 1) or off (load 0) the target plate
//
// The pure rules (Hollowmere.Mechanism.PressurePlate.Rules) validate every press; a refused press is rejected with no
// write. An accepted press writes plate.weight / plate.pressed and commits exactly one event in the same step:
// PlatePressed when the plate flips to pressed, PlateReleased when it flips back, PlateWeightChanged otherwise (the
// message plane commits a request only together with its event, P-044). Event payloads are {plate, actor, weight}
// (16 + 16 + 4 bytes), because a committed event does not otherwise name its target.
//
// The system finds its world's module through a property the mechanism sets after boot; there is no static registry,
// so two worlds never share plate state.
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Hollowmere.Mechanism.PressurePlate.Rules;
using Unity.Entities;

namespace Hollowmere.Mechanism.PressurePlate
{
    /// <summary>The payload of plate.press: the actor and whether it steps on (load) or off.</summary>
    public readonly struct PlatePressCommand
    {
        public const int Length = 20;

        public PlatePressCommand(TargetId actor, bool load)
        {
            Actor = actor;
            Load = load;
        }

        public TargetId Actor { get; }

        public bool Load { get; }

        public static FrozenPayload Encode(TargetId actor, bool load) =>
            new GameplayPayloadWriter().Id(actor.Value).Int32(load ? 1 : 0).Freeze();
    }

    /// <summary>Generated-style reader of the plate.press schema.</summary>
    public sealed class PlatePressCommandReader : ICommandPayloadReader<PlatePressCommand>
    {
        public SchemaRef Schema => PressurePlateDeclarations.PressCommand;

        public PlatePressCommand Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(PlatePressCommand.Length))
            {
                throw new FormatException("a plate.press payload is exactly " + PlatePressCommand.Length + " bytes");
            }

            var actor = new TargetId(reader.Id());
            int load = reader.Int32();
            if (load != 0 && load != 1)
            {
                throw new FormatException("plate.press load is 0 or 1, not " + load);
            }

            return new PlatePressCommand(actor, load == 1);
        }
    }

    /// <summary>Registration of the plate payload reader.</summary>
    public static class PlateReaders
    {
        /// <summary>Binds the plate.press reader; a no-op when <paramref name="readers"/> already reads the schema.</summary>
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            if (readers.CanRead(PressurePlateDeclarations.PressCommand))
            {
                return;
            }

            if (!readers.TryBind(new PlatePressCommandReader(), out string failure))
            {
                throw new InvalidOperationException("pressure plate reader registration failed: " + failure);
            }
        }
    }

    /// <summary>Kind of a committed plate event.</summary>
    public enum PlateEventKind
    {
        /// <summary>PlatePressed: released -> pressed.</summary>
        Pressed = 1,

        /// <summary>PlateReleased: pressed -> released.</summary>
        Released = 2,

        /// <summary>PlateWeightChanged: an accepted press that did not flip the plate.</summary>
        WeightChanged = 3,
    }

    /// <summary>A decoded plate event.</summary>
    public readonly struct PlateEvent
    {
        public const int Length = 36;

        public PlateEvent(PlateEventKind kind, TargetId plate, TargetId actor, int weight)
        {
            Kind = kind;
            Plate = plate;
            Actor = actor;
            Weight = weight;
        }

        public PlateEventKind Kind { get; }

        public TargetId Plate { get; }

        public TargetId Actor { get; }

        /// <summary>The plate's weight after the press.</summary>
        public int Weight { get; }

        public override string ToString() => Kind + "(" + Plate + ", actor " + Actor + ", weight " + Weight + ")";

        public static FrozenPayload Encode(TargetId plate, TargetId actor, int weight) =>
            new GameplayPayloadWriter().Id(plate.Value).Id(actor.Value).Int32(weight).Freeze();

        /// <summary>The event schema of a transition.</summary>
        public static SchemaRef SchemaOf(PlateTransition transition)
        {
            switch (transition)
            {
                case PlateTransition.Pressed:
                    return PressurePlateDeclarations.PressedEvent;
                case PlateTransition.Released:
                    return PressurePlateDeclarations.ReleasedEvent;
                default:
                    return PressurePlateDeclarations.WeightChangedEvent;
            }
        }

        /// <summary>Decodes a committed event of a plate schema; false for any other event.</summary>
        public static bool TryDecode(CommittedEvent committed, out PlateEvent decoded)
        {
            decoded = default(PlateEvent);
            if (committed == null || committed.Payload == null || committed.Payload.Length != Length)
            {
                return false;
            }

            PlateEventKind kind;
            if (committed.Schema.Equals(PressurePlateDeclarations.PressedEvent))
            {
                kind = PlateEventKind.Pressed;
            }
            else if (committed.Schema.Equals(PressurePlateDeclarations.ReleasedEvent))
            {
                kind = PlateEventKind.Released;
            }
            else if (committed.Schema.Equals(PressurePlateDeclarations.WeightChangedEvent))
            {
                kind = PlateEventKind.WeightChanged;
            }
            else
            {
                return false;
            }

            var reader = new GameplayPayloadReader(committed.Payload.Bytes);
            var plate = new TargetId(reader.Id());
            var actor = new TargetId(reader.Id());
            decoded = new PlateEvent(kind, plate, actor, reader.Int32());
            return true;
        }
    }

    /// <summary>Base layout of a plate target: an owned-slot buffer (slots are seeded when the plate is placed).</summary>
    public sealed class PlateRecipeApplier : ISpawnApplier
    {
        public FactoryKey Key => PressurePlateDeclarations.Applier;

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                entityManager.AddBuffer<TargetSlotState>(entity);
            }
        }
    }

    /// <summary>The plate spawn recipe.</summary>
    public static class PlateRecipes
    {
        public static SpawnRecipe Create()
        {
            var schemas = new List<SchemaRef> { PressurePlateDeclarations.RecipeSchema };
            var descriptor = new TargetDescriptor(
                PressurePlateDeclarations.PlateRecipe,
                schemas,
                null,
                null,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);
            return new SpawnRecipe(PressurePlateDeclarations.PlateRecipe, descriptor, schemas, new PlateRecipeApplier());
        }
    }

    /// <summary>What the plate module knows about one plate target: its scope and definition values.</summary>
    public sealed class PlateRecord
    {
        public PlateRecord(TargetId target, ScopeId scope, int threshold, int maxWeight)
        {
            Target = target;
            Scope = scope;
            Spec = new PlateSpec(threshold, maxWeight);
        }

        public TargetId Target { get; }

        public ScopeId Scope { get; }

        public PlateSpec Spec { get; }
    }

    /// <summary>The plate plugin's state of one world: host, registry and plate records. Instance state only.</summary>
    public sealed class PlateModule
    {
        /// <summary>Refusal of an accepted press whose event did not fit the step's committed-event bound (P-045).</summary>
        public const string EventBudgetRefusal = "plate.event-budget";

        private readonly Dictionary<TargetId, PlateRecord> records = new Dictionary<TargetId, PlateRecord>();
        private readonly Dictionary<string, int> refusals = new Dictionary<string, int>(StringComparer.Ordinal);

        public PlateModule(UnityWorldHost host, TargetRegistry registry)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public UnityWorldHost Host { get; }

        public TargetRegistry Registry { get; }

        public int RecordCount => records.Count;

        /// <summary>Accepted presses.</summary>
        public int Committed { get; private set; }

        /// <summary>Presses the rules or the module refused.</summary>
        public int Refused { get; private set; }

        /// <summary>Presses whose payload did not decode.</summary>
        public int Malformed { get; private set; }

        /// <summary>The refusal code of the most recent refused press, or empty.</summary>
        public string LastRefusal { get; private set; } = string.Empty;

        /// <summary>Refusals so far with <paramref name="code"/> (a <see cref="PlateRefusals"/> code).</summary>
        public int RefusalCount(string code) => refusals.TryGetValue(code ?? string.Empty, out int count) ? count : 0;

        public void Add(PlateRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            records[record.Target] = record;
        }

        public bool TryGet(TargetId target, out PlateRecord? record) => records.TryGetValue(target, out record);

        /// <summary>Plate targets in canonical (id) order.</summary>
        public IReadOnlyList<TargetId> Plates()
        {
            var plates = new List<TargetId>(records.Keys);
            plates.Sort((l, r) => l.Value.CompareTo(r.Value));
            return plates;
        }

        internal void CountCommitted() => Committed++;

        internal void CountMalformed() => Malformed++;

        internal void CountRefused(string code)
        {
            Refused++;
            LastRefusal = code;
            refusals[code] = RefusalCount(code) + 1;
        }

        public static PlateState Read(EntityManager entityManager, Entity entity) =>
            new PlateState(
                SlotState.ReadOrDefault(entityManager, entity, PressurePlateDeclarations.Owner, PressurePlateDeclarations.WeightSlot, 0),
                SlotState.ReadOrDefault(entityManager, entity, PressurePlateDeclarations.Owner, PressurePlateDeclarations.PressedSlot, 0));

        public static void Write(EntityManager entityManager, Entity entity, PlateState state)
        {
            SlotState.Write(entityManager, entity, PressurePlateDeclarations.Owner, PressurePlateDeclarations.WeightSlot, state.Weight);
            SlotState.Write(entityManager, entity, PressurePlateDeclarations.Owner, PressurePlateDeclarations.PressedSlot, state.Pressed);
        }
    }

    /// <summary>The plate command stage: drains plate.press and applies the pure transition.</summary>
    [DisableAutoCreation]
    public partial class PressurePlateCommandSystem : SystemBase
    {
        /// <summary>This world's module; set by the mechanism after boot. Until then the stage is idle.</summary>
        public PlateModule? Module { get; set; }

        protected override void OnUpdate()
        {
            PlateModule? module = Module;
            if (module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(PressurePlateDeclarations.Owner);
            EntityManager entityManager = EntityManager;
            for (int i = 0; i < batch.Count; i++)
            {
                Execute(module, plane, entityManager, batch[i]);
            }

            plane.ReleaseConsumed(PressurePlateDeclarations.Owner);
        }

        private static void Execute(PlateModule module, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<PlatePressCommand>(message.PayloadSchema, payload, out PlatePressCommand command, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                module.CountMalformed();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            if (!message.Route.Equals(PressurePlateDeclarations.PressRoute)
                || !module.TryGet(message.Target, out PlateRecord? record)
                || record == null
                || !module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity entity)
                || !entityManager.Exists(entity))
            {
                module.CountRefused(PlateRefusals.Unknown);
                plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                return;
            }

            PlatePressResult result = PressurePlateRules.Press(PlateModule.Read(entityManager, entity), command.Load, record.Spec);
            if (!result.Accepted)
            {
                module.CountRefused(result.Refusal);
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            FrozenPayload eventPayload = PlateEvent.Encode(message.Target, command.Actor, result.State.Weight);
            if (!plane.Commit(message, PlateEvent.SchemaOf(result.Transition), eventPayload, plane.ExecutingStep, out string _))
            {
                module.CountRefused(PlateModule.EventBudgetRefusal);
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            PlateModule.Write(entityManager, entity, result.State);
            module.CountCommitted();
        }
    }
}
