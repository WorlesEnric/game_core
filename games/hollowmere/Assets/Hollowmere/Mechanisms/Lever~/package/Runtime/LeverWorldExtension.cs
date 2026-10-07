#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;

namespace Hollowmere.Mechanism.Lever
{
    /// <summary>Explicit game-owned composition only; this type never registers itself with the trusted game.</summary>
    public sealed class LeverWorldExtension : IGameplayWorldExtension, IGameplayWorldTargets
    {
        private readonly GameplayPluginMount[] plugins;
        private readonly GameplaySystem[] systems;
        private GameApplicationRoot? root;
        private WorldSlotReader? slots;
        private LeverBinder? binder;

        public LeverWorldExtension()
        {
            plugins = new[] { new GameplayPluginMount(new CatalogPluginDeclaration(LeverDeclarations.Manifest(), ConfigDocument.Empty), LeverDeclarations.Instance) };
            systems = new[] { new GameplaySystem(LeverDeclarations.CommandSystem, LeverDeclarations.CommandSystemRegistration()) };
        }

        public string Name => "hollowmere-lever";
        public IReadOnlyList<GameplayPluginMount> Plugins => plugins;
        public IReadOnlyList<GameplaySystem> Systems => systems;
        public IReadOnlyList<CommandRoute> Routes => LeverDeclarations.Routes();
        public IReadOnlyList<MessageBufferDescriptor> Lanes => LeverDeclarations.Lanes();
        public LeverModule? Module { get; private set; }

        /// <summary>Committed int32 position, 0=off or 1=on; -1 when not yet present in this world.</summary>
        public int State => slots == null ? -1 : slots.ReadOrDefault(LeverDeclarations.Target, LeverDeclarations.Owner, LeverDeclarations.StateSlot, -1);

        /// <summary>Enqueues one bounded toggle. True is ingress admission, not a committed transition.</summary>
        public bool Toggle()
        {
            GameApplicationRoot? current = root;
            return current != null && current.State == GameApplicationState.Running
                && current.Host.Submit(new CommandEnvelope(current.NextOperation(), LeverDeclarations.ToggleRoute,
                    LeverDeclarations.Target, LeverDeclarations.ToggleCommand, null, LeverToggleCommand.Encode())).Admitted;
        }

        public void BindReaders(CommandPayloadReaders readers) => LeverReaders.BindInto(readers);
        public IReadOnlyList<SpawnRecipe> Recipes() => new[] { LeverRecipes.Create() };
        public IReadOnlyList<GameplayExtensionTarget> Targets(RegionManifest manifest) => new[]
        {
            new GameplayExtensionTarget("lever", LeverDeclarations.Target, LeverDeclarations.Recipe),
        };

        public void Validate(RegionManifest manifest)
        {
            if (manifest == null) { throw new ArgumentNullException(nameof(manifest)); }
        }

        public void Attach(GameplayWorld world, bool seedSlots)
        {
            AttachRoot(world.Root);
            // Applier seeds only newly created targets. Never overwrite a restored slot, including Attach(false).
            binder = new LeverBinder(Toggle);
            world.AddBinder(binder);
        }

        public void Detach(GameplayWorld world)
        {
            binder?.Dispose();
            binder = null;
            if (root != null)
            {
                LeverCommandSystem? system = root.Host.EntityWorld.GetExistingSystemManaged<LeverCommandSystem>();
                if (system != null) { system.Module = null; }
            }

            Module = null;
            root = null;
            slots = null;
        }

        // Standalone sandbox smoke shares the same module and command surface, never the live composition hook.
        internal void AttachRoot(GameApplicationRoot current)
        {
            LeverCommandSystem? system = current.Host.EntityWorld.GetExistingSystemManaged<LeverCommandSystem>();
            if (system == null) { throw new InvalidOperationException("lever command system is not registered"); }
            root = current;
            slots = new WorldSlotReader(current.Host.EntityWorld, current.Registry);
            Module = new LeverModule(current.Host, current.Registry);
            system.Module = Module;
        }
    }
}
