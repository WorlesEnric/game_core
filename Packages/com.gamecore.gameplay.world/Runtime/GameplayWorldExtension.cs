// GameCore.Gameplay.World - world extensions: further gameplay plugins composed into a gameplay world (P1.3 seam).
//
// WorldBuilder composes the entities and world plugins. A gameplay package that adds its own kernel plugin (player,
// npc, interaction; the P1.4/P1.5 groups the same way) hands an IGameplayWorldExtension to WorldBuildOptions.Extensions.
// Build adds the extension's plugin declarations, managed systems, command routes, ingress lanes and payload readers to
// the application definition and mounts its plugins at the world scope after the two P1.1 plugins; Attach calls the
// extension once the GameplayWorld exists (still paused), where it seeds its slots (unless the root comes from a
// restore) and hands its systems their per-world module. Extensions are explicit composition from game code: nothing
// is discovered at runtime, and an extension whose plugin the generated catalog does not register is refused at boot
// (the catalog's PluginDeclarationRejected), which is how a stale bake is detected.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Gameplay.World
{
    /// <summary>One plugin of an extension and the instance it is mounted as at the world scope.</summary>
    public sealed class GameplayPluginMount
    {
        public GameplayPluginMount(CatalogPluginDeclaration declaration, PluginInstanceId instance)
        {
            Declaration = declaration;
            Instance = instance;
        }

        public CatalogPluginDeclaration Declaration { get; }

        public PluginInstanceId Instance { get; }
    }

    /// <summary>One managed system of an extension: its catalog key and its registration.</summary>
    public sealed class GameplaySystem
    {
        public GameplaySystem(FactoryKey key, SystemRegistration registration)
        {
            Key = key;
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
        }

        public FactoryKey Key { get; }

        public SystemRegistration Registration { get; }
    }

    /// <summary>A gameplay plugin composed into a gameplay world by <see cref="WorldBuilder"/>.</summary>
    public interface IGameplayWorldExtension
    {
        /// <summary>Stable extension name (boot step names, diagnostics).</summary>
        string Name { get; }

        IReadOnlyList<GameplayPluginMount> Plugins { get; }

        IReadOnlyList<GameplaySystem> Systems { get; }

        IReadOnlyList<CommandRoute> Routes { get; }

        IReadOnlyList<MessageBufferDescriptor> Lanes { get; }

        /// <summary>Registers the extension's command payload readers.</summary>
        void BindReaders(CommandPayloadReaders readers);

        /// <summary>Checks the extension against the baked manifest; throws with a GP code when it cannot run.</summary>
        void Validate(RegionManifest manifest);

        /// <summary>
        /// Called once the world exists and before it runs: seed slots when <paramref name="seedSlots"/> (false for a root
        /// composed by a restore, whose slots hold the checkpoint) and hand the systems their per-world module.
        /// </summary>
        void Attach(GameplayWorld world, bool seedSlots);

        /// <summary>Called by <see cref="GameplayWorld.Shutdown"/>: release views and presentation objects.</summary>
        void Detach(GameplayWorld world);
    }
}
