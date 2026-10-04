// GameCore.Gameplay.World - world extensions: further gameplay plugins mounted with the baked world (P1.5).
//
// WorldBuilder composes the entities and world plugins of P1.1. A plugin package that adds authoritative slots of its
// own (UI flow, audio settings, ...) contributes one IGameplayWorldExtension through WorldBuildOptions.Extensions:
// its plugin declaration, routes, lanes, payload readers, command system and recipes join the application definition,
// its session targets are seeded under the world (root) scope, the plugin is mounted at the world scope after the two
// core plugins, and Attach hands the extension the booted root so it can seed its slots and give its command system its
// per-world module. Attach also runs for a root composed by a restore (seedSlots false), so an extension rebinds to a
// restored world exactly like the core plugins do. The catalog must register the extension's plugin factory, command
// system, appliers, layouts and schemas (GameplayCatalogNames); a missing registration refuses the boot (P-009).
//
// IGameplayWorldExtensionSource lets a game's boot code obtain the extensions (and their presentation) of a content
// asset it loads by path, without a compile-time reference to the packages that implement them.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Gameplay.World
{
    /// <summary>One target an extension seeds under the world scope at boot (e.g. a UI session target).</summary>
    public readonly struct GameplayExtensionTarget
    {
        public GameplayExtensionTarget(string name, TargetId target, DefinitionRef recipe)
        {
            Name = name ?? string.Empty;
            Target = target;
            Recipe = recipe;
        }

        /// <summary>Boot step name suffix (diagnostics only).</summary>
        public string Name { get; }

        public TargetId Target { get; }

        /// <summary>The recipe the target is seeded from; it must be one of the extension's recipes.</summary>
        public DefinitionRef Recipe { get; }
    }

    /// <summary>A further gameplay plugin mounted with the baked world.</summary>
    public interface IGameplayWorldExtension
    {
        /// <summary>Stable extension name (boot step names, diagnostics).</summary>
        string Name { get; }

        CatalogPluginDeclaration Declaration { get; }

        PluginInstanceId Instance { get; }

        /// <summary>The command system's factory key (its dispatch kind is ManagedSystem).</summary>
        FactoryKey CommandSystem { get; }

        SystemRegistration CommandSystemRegistration();

        IReadOnlyList<CommandRoute> Routes();

        IReadOnlyList<MessageBufferDescriptor> Lanes();

        void BindReaders(CommandPayloadReaders readers);

        IReadOnlyList<SpawnRecipe> Recipes();

        /// <summary>Targets seeded under the world scope, in a deterministic order.</summary>
        IReadOnlyList<GameplayExtensionTarget> Targets(RegionManifest manifest);

        /// <summary>
        /// Called once per root, right after the core plugins are attached, while the world is still Ready (paused):
        /// seed slots when <paramref name="seedSlots"/> is true (a restored root already holds its committed values) and
        /// hand the command system its per-world module.
        /// </summary>
        void Attach(GameApplicationRoot root, GameplayWorld world, bool seedSlots);
    }

    /// <summary>
    /// A content object (typically a ScriptableObject a game loads from Resources) that contributes world extensions and
    /// presentation to a world build. <see cref="Contribute"/> runs before <see cref="WorldBuilder.Build"/>.
    /// </summary>
    public interface IGameplayWorldExtensionSource
    {
        /// <summary>
        /// Adds the source's extensions to <paramref name="options"/> and prepares its presentation under
        /// <paramref name="host"/> (an engine object owned by the boot code, or null when there is none).
        /// </summary>
        void Contribute(WorldBuildOptions options, object? host);
    }
}
