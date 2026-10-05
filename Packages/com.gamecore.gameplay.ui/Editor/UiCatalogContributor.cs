// GameCore.Gameplay.Ui.Editor - the ui plugin's registrations in the generated gameplay catalog (P1.5).
#nullable enable
using GameCore.Gameplay.Compile;

namespace GameCore.Gameplay.Ui.Editor
{
    /// <summary>Contributes the ui plugin, its command system, session applier and layout, and schemas to the bake's catalog description.</summary>
    public sealed class UiCatalogContributor : IGameplayCatalogContributor
    {
        public GameplayCatalogContribution Contribution =>
            new GameplayCatalogContribution("com.gamecore.gameplay.ui", UiDeclarations.CatalogSchemas, UiDeclarations.CatalogEntries);
    }
}
