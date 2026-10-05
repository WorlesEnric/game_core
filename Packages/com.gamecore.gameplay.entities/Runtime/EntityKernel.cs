// GameCore.Gameplay.Entities - the entities plugin's kernel half: payloads, recipes, the per-world module and the
// command system (P1.1; P-024, P-032, P-042, P-044).
//
//   entity.spawn       (no argument)   a dead entity becomes alive; visibility is retained       -> EntitySpawned
//   entity.despawn     (no argument)   an alive entity becomes dead (logical)        -> EntityDespawned
//   entity.setVariant  (int variant)   selects one of the definition's variants      -> EntityVariantChanged
//
// Every command is validated by the pure rules (GameCore.Rules.Gameplay.Entities); a refused command is rejected with
// no write, an accepted one writes the owner's slots and commits its event in the same step. Event payloads start with
// the target id (16 bytes), because a committed event does not otherwise name its target.
//
// The system finds its world's module through a property the application root sets after boot ("per-world modules
// attached in the application root"); there is no static registry, so two worlds never share entity state.
//
// P1.7a (A1): a command may carry a trailing request id (8-byte payload). A negative id is an outbox obligation's (the
// narrative entity ports): the system claims it through the world's step tap before applying, and settles it in the
// same step, so a redelivered obligation (after a lost acknowledgement or a restore) changes nothing.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Rules.Gameplay.Entities;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Entities
{
    /// <summary>
    /// The payload of every entity command: one int32 (the variant for setVariant, zero otherwise), optionally followed
    /// by a request id (<see cref="LengthWithRequest"/> bytes; P1.7a).
    /// </summary>
    public readonly struct EntityCommand
    {
        public EntityCommand(int value)
            : this(value, 0)
        {
        }

        public EntityCommand(int value, int requestId) : this(value, requestId, null)
        {
        }

        public EntityCommand(int value, int requestId, bool? visible)
        {
            SpawnVisible = visible;
            Value = value;
            RequestId = requestId;
        }

        public int Value { get; }

        public bool? SpawnVisible { get; }

        /// <summary>0 when the command carries none; negative for an outbox obligation's id.</summary>
        public int RequestId { get; }

        public static FrozenPayload EncodeSpawn(bool? visible, int requestId = 0) =>
            new GameplayPayloadWriter().Int32(0).Int32(requestId).Int32(visible.HasValue ? (visible.Value ? 1 : 0) : -1).Freeze();

        public const int Length = 4;

        public const int LengthWithRequest = 8;

        public static FrozenPayload Encode(int value) => new GameplayPayloadWriter().Int32(value).Freeze();

        public static FrozenPayload Encode(int value, int requestId) => new GameplayPayloadWriter().Int32(value).Int32(requestId).Freeze();
    }

    /// <summary>Generated-style reader of one entity command schema.</summary>
    public sealed class EntityCommandReader : ICommandPayloadReader<EntityCommand>
    {
        public EntityCommandReader(SchemaRef schema)
        {
            Schema = schema;
        }

        public SchemaRef Schema { get; }

        public EntityCommand Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (reader.HasLength(12))
            {
                int value = reader.Int32();
                int request = reader.Int32();
                int visible = reader.Int32();
                if (visible < -1 || visible > 1) throw new FormatException("spawn visibility must be -1, 0 or 1");
                return new EntityCommand(value, request, visible == -1 ? (bool?)null : visible == 1);
            }

            if (reader.HasLength(EntityCommand.LengthWithRequest))
            {
                return new EntityCommand(reader.Int32(), reader.Int32());
            }

            if (!reader.HasLength(EntityCommand.Length))
            {
                throw new FormatException("an entity command is " + EntityCommand.Length + " or " + EntityCommand.LengthWithRequest + " bytes (12 with spawn visibility)");
            }

            return new EntityCommand(reader.Int32());
        }
    }

    /// <summary>A decoded entity event: target, kind and value.</summary>
    public readonly struct EntityEvent
    {
        public EntityEvent(TargetId target, int value)
        {
            Target = target;
            Value = value;
        }

        public TargetId Target { get; }

        /// <summary>The variant for EntityVariantChanged and EntitySpawned; zero for EntityDespawned.</summary>
        public int Value { get; }

        public static FrozenPayload Encode(TargetId target, int value) =>
            new GameplayPayloadWriter().Id(target.Value).Int32(value).Freeze();

        public static bool TryDecode(FrozenPayload payload, out EntityEvent decoded)
        {
            decoded = default(EntityEvent);
            if (payload == null || payload.Length != 20)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(payload.Bytes);
            decoded = new EntityEvent(new TargetId(reader.Id()), reader.Int32());
            return true;
        }
    }

    /// <summary>Registration helpers of the entity payload readers.</summary>
    public static class EntityReaders
    {
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            Bind(readers, EntityDeclarations.SpawnCommand);
            Bind(readers, EntityDeclarations.DespawnCommand);
            Bind(readers, EntityDeclarations.SetVariantCommand);
        }

        private static void Bind(CommandPayloadReaders readers, SchemaRef schema)
        {
            if (!readers.TryBind(new EntityCommandReader(schema), out string failure))
            {
                throw new InvalidOperationException("entity reader registration failed: " + failure);
            }
        }
    }

    /// <summary>Base layout of every entity target: an empty owned-slot buffer (slots are seeded after boot).</summary>
    public sealed class EntityRecipeApplier : ISpawnApplier
    {
        public FactoryKey Key => EntityDeclarations.Applier;

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                entityManager.AddBuffer<TargetSlotState>(entity);
            }
        }
    }

    /// <summary>Recipes of entity definitions: one per definition, keyed by its DefinitionRef.</summary>
    public static class EntityRecipes
    {
        /// <summary>
        /// The recipe reference of a definition: definition id derived from its authoring id, the entity recipe schema,
        /// and the exact revision of its baked content hash.
        /// </summary>
        public static DefinitionRef RecipeOf(string definitionId, ulong revision) =>
            new DefinitionRef(
                AuthoringIds.DefinitionIdFor(definitionId),
                EntityDeclarations.RecipeSchema,
                new DefinitionRevision(revision == 0UL ? 1UL : revision));

        public static SpawnRecipe Create(DefinitionRef recipe, ISpawnApplier applier)
        {
            var schemas = new List<SchemaRef> { EntityDeclarations.RecipeSchema };
            var descriptor = new TargetDescriptor(
                recipe,
                schemas,
                null,
                null,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);
            return new SpawnRecipe(recipe, descriptor, schemas, applier);
        }
    }

    /// <summary>What the entities module knows about one entity target.</summary>
    public sealed class EntityRecord
    {
        public EntityRecord(TargetId target, string authoringId, string definitionId, int variantCount)
        {
            Target = target;
            AuthoringId = authoringId;
            DefinitionId = definitionId;
            VariantCount = variantCount < 1 ? 1 : variantCount;
        }

        public TargetId Target { get; }

        public string AuthoringId { get; }

        public string DefinitionId { get; }

        public int VariantCount { get; }
    }

    /// <summary>The entities plugin's state of one world: its host, registry and entity records. Instance state only.</summary>
    public sealed class EntityModule : IGameplayStepTapHost
    {
        private readonly Dictionary<TargetId, EntityRecord> records = new Dictionary<TargetId, EntityRecord>();

        public EntityModule(UnityWorldHost host, TargetRegistry registry)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public UnityWorldHost Host { get; }

        public TargetRegistry Registry { get; }

        /// <summary>The world's in-step outbox seam (set by the gameplay world); null without one.</summary>
        public IGameplayStepTap? StepTap { get; set; }

        public int RecordCount => records.Count;

        /// <summary>Every entity record, authored and runtime-spawned.</summary>
        public IEnumerable<EntityRecord> Records => records.Values;

        public int Committed { get; private set; }

        public int Refused { get; private set; }

        public int Malformed { get; private set; }

        public void Add(EntityRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            records[record.Target] = record;
        }

        public bool TryGet(TargetId target, out EntityRecord? record) => records.TryGetValue(target, out record);

        internal void CountCommitted() => Committed++;

        internal void CountRefused() => Refused++;

        internal void CountMalformed() => Malformed++;

        /// <summary>The state of an entity target as its four slots hold it.</summary>
        public EntityState Read(EntityManager entityManager, Entity entity) =>
            new EntityState(
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.EntityOwner, GameplaySlots.Alive, 0),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.EntityOwner, GameplaySlots.Variant, 0),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.EntityOwner, GameplaySlots.ScaleMilli, GameplayUnits.ScaleOne),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.EntityOwner, GameplaySlots.Visible, 1));

        public static void Write(EntityManager entityManager, Entity entity, EntityState state)
        {
            SlotState.Write(entityManager, entity, GameplaySlots.EntityOwner, GameplaySlots.Alive, state.Alive);
            SlotState.Write(entityManager, entity, GameplaySlots.EntityOwner, GameplaySlots.Variant, state.Variant);
            SlotState.Write(entityManager, entity, GameplaySlots.EntityOwner, GameplaySlots.ScaleMilli, state.ScaleMilli);
            SlotState.Write(entityManager, entity, GameplaySlots.EntityOwner, GameplaySlots.Visible, state.Visible);
        }
    }

    /// <summary>The entities command stage: drains the three routes and applies the pure transitions.</summary>
    [DisableAutoCreation]
    public partial class EntityCommandSystem : SystemBase
    {
        /// <summary>This world's module; set by the application root after boot. Until then the stage is idle.</summary>
        public EntityModule? Module { get; set; }

        protected override void OnUpdate()
        {
            EntityModule? module = Module;
            if (module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(EntityDeclarations.Owner);
            EntityManager entityManager = EntityManager;
            for (int i = 0; i < batch.Count; i++)
            {
                Execute(module, plane, entityManager, batch[i]);
            }

            plane.ReleaseConsumed(EntityDeclarations.Owner);
        }

        private static void Execute(EntityModule module, WorldMessagePlane plane, EntityManager entityManager, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<EntityCommand>(message.PayloadSchema, payload, out EntityCommand command, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                module.CountMalformed();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            if (GameplayObligations.Claim(module.StepTap, command.RequestId) == ObligationClaim.AlreadyApplied)
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.IdempotencyConflict, plane.ExecutingStep);
                return;
            }

            if (!module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity entity)
                || !entityManager.Exists(entity)
                || !module.TryGet(message.Target, out EntityRecord? record)
                || record == null)
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                return;
            }

            EntityState state = module.Read(entityManager, entity);
            EntityTransition transition;
            SchemaRef eventSchema;
            if (message.Route.Equals(EntityDeclarations.SpawnRoute))
            {
                transition = EntityRules.Spawn(state, command.SpawnVisible);
                eventSchema = EntityDeclarations.SpawnedEvent;
            }
            else if (message.Route.Equals(EntityDeclarations.DespawnRoute))
            {
                transition = EntityRules.Despawn(state);
                eventSchema = EntityDeclarations.DespawnedEvent;
            }
            else if (message.Route.Equals(EntityDeclarations.SetVariantRoute))
            {
                transition = EntityRules.SetVariant(state, command.Value, record.VariantCount);
                eventSchema = EntityDeclarations.VariantChangedEvent;
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

            int eventValue = message.Route.Equals(EntityDeclarations.DespawnRoute) ? 0 : transition.State.Variant;
            FrozenPayload committed = EntityEvent.Encode(message.Target, eventValue);
            if (!plane.Commit(message, eventSchema, committed, plane.ExecutingStep, out string _))
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            EntityModule.Write(entityManager, entity, transition.State);
            module.StepTap?.OnCommitted(eventSchema, committed, message.Request);
            GameplayObligations.Settle(module.StepTap, command.RequestId);
            module.CountCommitted();
        }
    }
}
