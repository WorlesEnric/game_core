// GameCore.Gameplay.Player.Editor - the player plugin's registrations in the generated gameplay catalog (P1.3).
#nullable enable
using GameCore.Gameplay.Compile;

namespace GameCore.Gameplay.Player.Editor
{
    /// <summary>Contributes the player plugin, its command system, layout and schemas to the bake's catalog description.</summary>
    public sealed class PlayerCatalogContributor : IGameplayCatalogContributor
    {
        public GameplayCatalogContribution Contribution =>
            new GameplayCatalogContribution("com.gamecore.gameplay.player", PlayerDeclarations.CatalogSchemas, PlayerDeclarations.CatalogEntries);
    }
}
