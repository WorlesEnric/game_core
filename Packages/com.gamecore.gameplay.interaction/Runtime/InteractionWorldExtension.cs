// GameCore.Gameplay.Interaction - the interaction plugin as a world extension (P1.3).
//
// Validate turns the roster and the baked manifest into records: every placed entity whose definition an
// InteractableDefinition or TriggerDefinition of the roster names. Attach seeds interact.state (the definition's
// initial state), uses, cooldownMs and occupants (unless the root comes from a restore) and hands the system its module.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Gameplay.Interaction
{
    /// <summary>The interaction plugin's world extension.</summary>
    public sealed class InteractionWorldExtension : IGameplayWorldExtension
    {
        private readonly List<GameplayPluginMount> plugins;
        private readonly List<GameplaySystem> systems;
        private readonly List<InteractableRecord> records = new List<InteractableRecord>();

        public InteractionWorldExtension(InteractionRoster? roster)
        {
            Roster = roster;
            plugins = new List<GameplayPluginMount>
            {
                new GameplayPluginMount(new CatalogPluginDeclaration(InteractionDeclarations.Manifest(), ConfigDocument.Empty), InteractionDeclarations.Instance),
            };
            systems = new List<GameplaySystem>
            {
                new GameplaySystem(InteractionDeclarations.CommandSystem, InteractionDeclarations.CommandSystemRegistration()),
            };
        }

        public string Name => "interaction";

        public InteractionRoster? Roster { get; }

        public InteractionModule? Module { get; private set; }

        /// <summary>Condition evaluator handed to the module at Attach (P1.4 logic installs one; null object otherwise).</summary>
        public IConditionEvaluator Conditions { get; set; } = new NullConditionEvaluator();

        public IReadOnlyList<InteractableRecord> Records => records;

        public IReadOnlyList<GameplayPluginMount> Plugins => plugins;

        public IReadOnlyList<GameplaySystem> Systems => systems;

        public IReadOnlyList<CommandRoute> Routes => InteractionDeclarations.Routes();

        public IReadOnlyList<MessageBufferDescriptor> Lanes => InteractionDeclarations.Lanes();

        public void BindReaders(CommandPayloadReaders readers) => InteractionReaders.BindInto(readers);

        public void Validate(RegionManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            records.Clear();
            if (Roster == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Roster.Interactables.Count; i++)
            {
                InteractableDefinition definition = Roster.Interactables[i];
                if (definition == null || definition.Entity == null)
                {
                    throw new InvalidOperationException(PlayerNpcInteractionCodes.InteractableMissingEntityDefinition + ": roster " + Roster.name
                        + " lists an interactable without an entity definition");
                }

                if (!seen.Add(definition.Entity.AuthoringId))
                {
                    throw new InvalidOperationException(PlayerNpcInteractionCodes.InteractableDuplicateEntityDefinition + ": two definitions of roster "
                        + Roster.name + " name entity definition " + definition.Entity.name);
                }
            }

            for (int i = 0; i < Roster.Triggers.Count; i++)
            {
                TriggerDefinition trigger = Roster.Triggers[i];
                if (trigger == null || trigger.Entity == null || !seen.Add(trigger.Entity.AuthoringId))
                {
                    throw new InvalidOperationException(PlayerNpcInteractionCodes.InteractableDuplicateEntityDefinition + ": roster " + Roster.name
                        + " lists a trigger without a distinct entity definition");
                }
            }

            for (int i = 0; i < manifest.Entities.Count; i++)
            {
                ManifestEntity entity = manifest.Entities[i];
                if (!AuthoringIds.IsValid(entity.authoringId))
                {
                    continue;
                }

                InteractableDefinition? interactable = Roster.FindInteractable(entity.definitionId);
                TriggerDefinition? trigger = interactable == null ? Roster.FindTrigger(entity.definitionId) : null;
                if (interactable == null && trigger == null)
                {
                    continue;
                }

                records.Add(new InteractableRecord(AuthoringIds.TargetIdFor(entity.authoringId), entity.authoringId, entity.name, interactable, trigger));
            }
        }

        public void Attach(GameplayWorld world, bool seedSlots)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            GameApplicationRoot root = world.Root;
            if (seedSlots)
            {
                for (int i = 0; i < records.Count; i++)
                {
                    InteractableRecord record = records[i];
                    Seed(root, record.Target, InteractionSlots.State, record.Interactable != null ? record.Interactable.InitialState : 0);
                    Seed(root, record.Target, InteractionSlots.Uses, 0);
                    Seed(root, record.Target, InteractionSlots.CooldownMs, 0);
                    Seed(root, record.Target, InteractionSlots.Occupants, 0);
                }
            }

            InteractionCommandSystem? system = root.Host.EntityWorld.GetExistingSystemManaged<InteractionCommandSystem>();
            if (system == null)
            {
                throw new InvalidOperationException("the interaction command system is not registered in world " + root.Host.DiagnosticName);
            }

            Module = new InteractionModule(root.Host, root.Registry, world.Slots, Roster != null ? Roster.StepMilliseconds : 20, records)
            {
                Conditions = Conditions,
            };
            system.Module = Module;
        }

        public void Detach(GameplayWorld world)
        {
        }

        private static void Seed(GameApplicationRoot root, TargetId target, SlotId slot, int value)
        {
            if (!root.Seeder.TrySeedSlot(target, InteractionSlots.Owner, slot, GameplaySlots.SchemaVersion, value, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException("seeding interactable " + target + " failed: " + code + ": " + detail);
            }
        }
    }
}
