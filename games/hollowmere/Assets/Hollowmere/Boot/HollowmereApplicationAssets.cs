// Hollowmere - the boot assets loadable before any scene (P3.1, P1.7a A11): the same baked region manifest, narrative
// content and P1.3 definitions Boot.unity's GameBoot holds, placed under a Resources folder
// (Assets/Hollowmere/Boot/Resources/Hollowmere/Application.asset) so HollowmereApplication can compose and register the
// game's definition at SubsystemRegistration, before Boot.unity loads. Written by the Studio tool
// hollowmere.registerApplication, which copies GameBoot's references (one source of truth: the Boot scene).
#nullable enable
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>What GameBoot boots, loadable from Resources before any scene (P1.7a A11).</summary>
    public sealed class HollowmereApplicationAssets : ScriptableObject
    {
        /// <summary>The Resources path HollowmereApplication loads.</summary>
        public const string ResourcePath = "Hollowmere/Application";

        [SerializeField] private RegionManifest? manifest;
        [SerializeField] private GameplayContentManifest? content;
        [SerializeField] private PlayerDefinition? player;
        [SerializeField] private NpcRoster? npcs;
        [SerializeField] private InteractionRoster? interactions;

        public RegionManifest? Manifest => manifest;

        public GameplayContentManifest? Content => content;

        public PlayerDefinition? Player => player;

        public NpcRoster? Npcs => npcs;

        public InteractionRoster? Interactions => interactions;

        /// <summary>Sets the references (the hollowmere.registerApplication tool).</summary>
        public void Configure(RegionManifest? regionManifest, GameplayContentManifest? contentManifest, PlayerDefinition? playerDefinition, NpcRoster? npcRoster, InteractionRoster? interactionRoster)
        {
            manifest = regionManifest;
            content = contentManifest;
            player = playerDefinition;
            npcs = npcRoster;
            interactions = interactionRoster;
        }

        /// <summary>True when these are exactly the assets a GameBoot holds (the adopt check).</summary>
        public bool Matches(RegionManifest? regionManifest, GameplayContentManifest? contentManifest, PlayerDefinition? playerDefinition, NpcRoster? npcRoster, InteractionRoster? interactionRoster) =>
            manifest == regionManifest && content == contentManifest && player == playerDefinition && npcs == npcRoster && interactions == interactionRoster;
    }
}
