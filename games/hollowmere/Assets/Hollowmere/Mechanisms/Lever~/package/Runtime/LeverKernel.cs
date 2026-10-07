#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Hollowmere.Mechanism.Lever.Rules;
using Unity.Entities;

namespace Hollowmere.Mechanism.Lever
{
    public readonly struct LeverToggleCommand
    {
        public LeverToggleCommand(int turns) { Turns = turns; }
        public int Turns { get; }
        public static FrozenPayload Encode(int turns = 1) => new GameplayPayloadWriter().Int32(turns).Freeze();
    }

    public sealed class LeverToggleReader : ICommandPayloadReader<LeverToggleCommand>
    {
        public SchemaRef Schema => LeverDeclarations.ToggleCommand;
        public LeverToggleCommand Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(sizeof(int)))
            {
                throw new FormatException("lever.toggle requires exactly one int32 turn count");
            }

            return new LeverToggleCommand(reader.Int32());
        }
    }

    public static class LeverReaders
    {
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (!readers.CanRead(LeverDeclarations.ToggleCommand) && !readers.TryBind(new LeverToggleReader(), out string failure))
            {
                throw new InvalidOperationException("lever payload reader: " + failure);
            }
        }
    }

    public readonly struct LeverEvent
    {
        public LeverEvent(TargetId target, int state) { Target = target; State = state; }
        public TargetId Target { get; }
        public int State { get; }
        public static FrozenPayload Encode(TargetId target, int state) =>
            new GameplayPayloadWriter().Id(target.Value).Int32(state).Freeze();

        public static bool TryDecode(CommittedEvent committed, out LeverEvent decoded)
        {
            decoded = default(LeverEvent);
            if (!committed.Schema.Equals(LeverDeclarations.ToggledEvent) || committed.Payload == null || committed.Payload.Length != 20)
            {
                return false;
            }

            var reader = new GameplayPayloadReader(committed.Payload.Bytes);
            var target = new TargetId(reader.Id());
            int state = reader.Int32();
            if (state != 0 && state != 1)
            {
                return false;
            }

            decoded = new LeverEvent(target, state);
            return true;
        }
    }

    public sealed class LeverRecipeApplier : ISpawnApplier
    {
        public FactoryKey Key => LeverDeclarations.Applier;
        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                entityManager.AddBuffer<TargetSlotState>(entity);
            }

            // New target default only. Restore subsequently writes the captured slot rows.
            SlotState.Write(entityManager, entity, LeverDeclarations.Owner, LeverDeclarations.StateSlot, 0);
        }
    }

    public static class LeverRecipes
    {
        public static SpawnRecipe Create()
        {
            var schemas = new[] { LeverDeclarations.RecipeSchema };
            var descriptor = new TargetDescriptor(LeverDeclarations.Recipe, schemas, null, null,
                default(AssetAdapterDescriptor), null, null, null, null);
            return new SpawnRecipe(LeverDeclarations.Recipe, descriptor, schemas, new LeverRecipeApplier());
        }
    }

    public sealed class LeverModule
    {
        public LeverModule(UnityWorldHost host, TargetRegistry registry)
        {
            Host = host;
            Registry = registry;
        }

        public UnityWorldHost Host { get; }
        public TargetRegistry Registry { get; }
        public int Committed { get; internal set; }
        public string LastRefusal { get; internal set; } = string.Empty;
    }

    [DisableAutoCreation]
    public partial class LeverCommandSystem : SystemBase
    {
        public LeverModule? Module { get; set; }

        protected override void OnUpdate()
        {
            LeverModule? module = Module;
            WorldMessagePlane? plane = module?.Host.Messages;
            if (module == null || plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(LeverDeclarations.Owner);
            for (int i = 0; i < batch.Count; i++)
            {
                Execute(module, plane, batch[i]);
            }

            plane.ReleaseConsumed(LeverDeclarations.Owner);
        }

        private void Execute(LeverModule module, WorldMessagePlane plane, StepMessage message)
        {
            if (plane.Readers.TryRead<LeverToggleCommand>(message.PayloadSchema, plane.PayloadOf(message),
                out LeverToggleCommand command, out string _) != PayloadDecodeOutcome.Decoded)
            {
                module.LastRefusal = "lever.malformed";
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            if (!message.Route.Equals(LeverDeclarations.ToggleRoute)
                || !message.Target.Equals(LeverDeclarations.Target)
                || !module.Registry.TryResolveTarget(message.Target, out TargetHandle _, out Entity entity)
                || !EntityManager.Exists(entity))
            {
                module.LastRefusal = "lever.unknown";
                plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                return;
            }

            int state = SlotState.ReadOrDefault(EntityManager, entity, LeverDeclarations.Owner, LeverDeclarations.StateSlot, -1);
            LeverTransition result = LeverRules.Toggle(state, command.Turns);
            if (!result.Accepted)
            {
                module.LastRefusal = result.Refusal;
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            if (!plane.Commit(message, LeverDeclarations.ToggledEvent, LeverEvent.Encode(message.Target, result.State),
                plane.ExecutingStep, out string _))
            {
                module.LastRefusal = "lever.event-budget";
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            SlotState.Write(EntityManager, entity, LeverDeclarations.Owner, LeverDeclarations.StateSlot, result.State);
            module.Committed++;
            module.LastRefusal = string.Empty;
        }
    }
}
