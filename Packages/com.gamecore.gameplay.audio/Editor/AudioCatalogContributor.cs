// GameCore.Gameplay.Audio.Editor - the audio plugin's registrations in the generated gameplay catalog (P1.5).
#nullable enable
using GameCore.Gameplay.Compile;

namespace GameCore.Gameplay.Audio.Editor
{
    /// <summary>Contributes the audio plugin, its command system, session applier and layout, and schemas to the bake's catalog description.</summary>
    public sealed class AudioCatalogContributor : IGameplayCatalogContributor
    {
        public GameplayCatalogContribution Contribution =>
            new GameplayCatalogContribution("com.gamecore.gameplay.audio", AudioDeclarations.CatalogSchemas, AudioDeclarations.CatalogEntries);
    }
}
