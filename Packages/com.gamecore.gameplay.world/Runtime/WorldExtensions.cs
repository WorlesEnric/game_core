// GameCore.Gameplay.World - world extension additions: session targets and extension sources (P1.5).
//
// The extension seam itself is P1.3's IGameplayWorldExtension (GameplayWorldExtension.cs). A plugin whose authoritative
// slots live on a session target of its own rather than on a baked entity (UI flow, audio settings) also implements
// IGameplayWorldTargets: WorldBuilder adds its spawn recipes to the recipe catalog and seeds its targets under the world
// (root) scope before the plugins are mounted. The catalog must register the extension's plugin factory, command
// system, appliers, layouts and schemas (GameplayCatalogNames); a missing registration refuses the boot (P-009).
//
// IGameplayWorldExtensionSource lets a game's boot code obtain the extensions (and their presentation) of a content
// asset it loads by path, without a compile-time reference to the packages that implement them.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Unity.Runtime;

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

    /// <summary>
    /// Implemented by an <see cref="IGameplayWorldExtension"/> that owns session targets: its recipes join the recipe
    /// catalog and its targets are seeded under the world scope at boot.
    /// </summary>
    public interface IGameplayWorldTargets
    {
        IReadOnlyList<SpawnRecipe> Recipes();

        /// <summary>Targets seeded under the world scope, in a deterministic order.</summary>
        IReadOnlyList<GameplayExtensionTarget> Targets(RegionManifest manifest);
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
