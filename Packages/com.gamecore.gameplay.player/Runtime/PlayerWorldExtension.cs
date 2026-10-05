// GameCore.Gameplay.Player - the player plugin as a world extension (P1.3).
//
// Composed by game code into WorldBuildOptions.Extensions. Validate finds the player: the world's focus entity, whose
// definition must be the PlayerDefinition's entity definition. Attach seeds the eight player slots from the player's
// baked placement (world.pos*, world.yaw, world.region; full stamina, no focus) unless the root comes from a restore,
// and hands the command system its module.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Player;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Gameplay.Player
{
    /// <summary>The player plugin's world extension.</summary>
    public sealed class PlayerWorldExtension : IGameplayWorldExtension
    {
        private readonly List<GameplayPluginMount> plugins;
        private readonly List<GameplaySystem> systems;

        public PlayerWorldExtension(PlayerDefinition? definition)
            : this(definition, definition != null ? definition.ToTuning() : PlayerTuning.Default)
        {
        }

        public PlayerWorldExtension(PlayerDefinition? definition, PlayerTuning tuning)
        {
            Definition = definition;
            Tuning = tuning;
            plugins = new List<GameplayPluginMount>
            {
                new GameplayPluginMount(new CatalogPluginDeclaration(PlayerDeclarations.Manifest(), ConfigDocument.Empty), PlayerDeclarations.Instance),
            };
            systems = new List<GameplaySystem>
            {
                new GameplaySystem(PlayerDeclarations.CommandSystem, PlayerDeclarations.CommandSystemRegistration()),
            };
        }

        public string Name => "player";

        public PlayerDefinition? Definition { get; }

        public PlayerTuning Tuning { get; }

        /// <summary>The player's entity target (set by Validate).</summary>
        public TargetId Player { get; private set; }

        public string PlayerAuthoringId { get; private set; } = string.Empty;

        public int PlayerKey { get; private set; }

        /// <summary>The module of the attached world (null before Attach).</summary>
        public PlayerModule? Module { get; private set; }

        public IReadOnlyList<GameplayPluginMount> Plugins => plugins;

        public IReadOnlyList<GameplaySystem> Systems => systems;

        public IReadOnlyList<CommandRoute> Routes => PlayerDeclarations.Routes();

        public IReadOnlyList<MessageBufferDescriptor> Lanes => PlayerDeclarations.Lanes();

        public void BindReaders(CommandPayloadReaders readers) => PlayerReaders.BindInto(readers);

        public void Validate(RegionManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            string focus = manifest.FocusEntityId;
            ManifestEntity? entity = null;
            for (int i = 0; i < manifest.Entities.Count; i++)
            {
                if (string.Equals(manifest.Entities[i].authoringId, focus, StringComparison.Ordinal))
                {
                    entity = manifest.Entities[i];
                    break;
                }
            }

            if (entity == null || !AuthoringIds.IsValid(focus))
            {
                throw new InvalidOperationException(
                    PlayerNpcInteractionCodes.PlayerNotInWorld + ": the world names no placed focus entity to be the player");
            }

            if (Definition != null && Definition.Entity != null
                && !string.Equals(Definition.Entity.AuthoringId, entity.definitionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    PlayerNpcInteractionCodes.PlayerNotFocusEntity + ": the focus entity " + entity.name + " is not an instance of "
                    + Definition.name + "'s entity definition");
            }

            PlayerAuthoringId = entity.authoringId;
            Player = AuthoringIds.TargetIdFor(entity.authoringId);
            PlayerKey = AuthoringIds.StableKey(entity.authoringId);
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
                int region = world.Slots.ReadOrDefault(Player, GameplaySlots.WorldOwner, GameplaySlots.Region, 0);
                PlayerState spawned = PlayerRules.Spawned(
                    world.Slots.ReadOrDefault(Player, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0),
                    world.Slots.ReadOrDefault(Player, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0),
                    world.Slots.ReadOrDefault(Player, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0),
                    world.Slots.ReadOrDefault(Player, GameplaySlots.WorldOwner, GameplaySlots.Yaw, 0),
                    region,
                    Tuning);
                Seed(root, PlayerSlots.PosX, spawned.PosX);
                Seed(root, PlayerSlots.PosY, spawned.PosY);
                Seed(root, PlayerSlots.PosZ, spawned.PosZ);
                Seed(root, PlayerSlots.Yaw, spawned.Yaw);
                Seed(root, PlayerSlots.Stamina, spawned.Stamina);
                Seed(root, PlayerSlots.Focus, spawned.Focus);
                Seed(root, PlayerSlots.RegionKey, spawned.RegionKey);
                Seed(root, PlayerSlots.RegenDelayMs, spawned.RegenDelayMilliseconds);
            }

            PlayerCommandSystem? system = root.Host.EntityWorld.GetExistingSystemManaged<PlayerCommandSystem>();
            if (system == null)
            {
                throw new InvalidOperationException("the player command system is not registered in world " + root.Host.DiagnosticName);
            }

            Module = new PlayerModule(root.Host, root.Registry, Player, PlayerAuthoringId, Tuning);
            system.Module = Module;
        }

        public void Detach(GameplayWorld world)
        {
        }

        private void Seed(GameApplicationRoot root, SlotId slot, int value)
        {
            if (!root.Seeder.TrySeedSlot(Player, PlayerSlots.Owner, slot, GameplaySlots.SchemaVersion, value, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException("seeding the player failed: " + code + ": " + detail);
            }
        }
    }
}
