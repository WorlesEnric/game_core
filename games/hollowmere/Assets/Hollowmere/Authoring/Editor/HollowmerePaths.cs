// Hollowmere - the asset paths and world positions the P3.1 authoring steps use (one place, so the steps, the tests
// and PACKET.md agree). Positions are world space (metres); the three region centres are the village (0,0,0), the
// marsh (200,0,0) and the belfry (100,0,200), each 80 m square.
#nullable enable
using UnityEngine;

namespace Hollowmere.Authoring
{
    /// <summary>Asset paths of the Hollowmere content.</summary>
    public static class HollowmerePaths
    {
        public const string WorldPath = "Assets/Hollowmere/World/Hollowmere.asset";
        public const string ManifestPath = "Assets/Hollowmere/World/Hollowmere.manifest.asset";
        public const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        public const string VillageScene = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        public const string MarshScene = "Assets/Hollowmere/Regions/BlackmereMarsh.unity";
        public const string BelfryScene = "Assets/Hollowmere/Regions/DrownedBelfry.unity";
        public const string VillageRegion = "Assets/Hollowmere/World/Regions/ThornwickVillage.asset";
        public const string MarshRegion = "Assets/Hollowmere/World/Regions/BlackmereMarsh.asset";
        public const string BelfryRegion = "Assets/Hollowmere/World/Regions/DrownedBelfry.asset";

        public const string ContentSet = "Assets/Hollowmere/Rules/HollowmereContent.asset";
        public const string ContentManifest = "Assets/Hollowmere/Rules/HollowmereContent.content.asset";
        public const string Facts = "Assets/Hollowmere/Dialogue/Facts";
        public const string Graphs = "Assets/Hollowmere/Dialogue/Graphs";
        public const string Items = "Assets/Hollowmere/Items";
        public const string Conditions = "Assets/Hollowmere/Rules/Conditions";
        public const string Actions = "Assets/Hollowmere/Rules/Actions";
        public const string Rules = "Assets/Hollowmere/Rules";
        public const string P31Rules = "Assets/Hollowmere/Rules/Story";
        public const string Quest = "Assets/Hollowmere/Quests/DrownedBell.asset";

        public const string NpcDefinitions = "Assets/Hollowmere/Npcs/Definitions";
        public const string NpcRoster = "Assets/Hollowmere/Npcs/NpcRoster.asset";
        public const string NpcPrefab = "Assets/Hollowmere/Npcs/Prefabs/NpcCapsule.prefab";
        public const string Interactables = "Assets/Hollowmere/Interactables/Definitions";
        public const string InteractionRoster = "Assets/Hollowmere/Interactables/InteractionRoster.asset";
        public const string PlayerDefinition = "Assets/Hollowmere/Player/PlayerDefinition.asset";
        public const string InputActions = "Assets/Hollowmere/Input/Player.inputactions";

        public const string Director = "Assets/Hollowmere/Game/HollowmereDirector.asset";
        public const string AudioSet = "Assets/Hollowmere/Audio/Definitions/HollowmereAudio.asset";
        public const string AudioBank = "Assets/Hollowmere/Audio/Definitions/HollowmereAudioBank.asset";
        public const string HudDocument = "Assets/Hollowmere/UI/Definitions/Doc_Hud.asset";

        public const string Scenery = "Assets/Hollowmere/Scenery";
        public const string Textures = "Assets/Hollowmere/Scenery/Textures";
        public const string Materials = "Assets/Hollowmere/Scenery/Materials";
        public const string Animators = "Assets/Hollowmere/Npcs/Animators";
        public const string VolumeProfile = "Assets/Hollowmere/Scenery/HollowmerePostProcessing.asset";
        public const string Media = "Assets/Hollowmere/Media";
        public const string Portraits = "Assets/Hollowmere/Media/Portraits";
        public const string Voices = "Assets/Hollowmere/Media/Voices";

        /// <summary>The P1.x authoring scripts stop re-applying their content once this asset exists (P3.1 owns it).</summary>
        public const string SupersededMarker = Director;

        public static string Fact(string name) => Facts + "/" + name + ".asset";

        public static string Item(string name) => Items + "/" + name + ".asset";

        public static string Graph(string name) => Graphs + "/" + name + ".asset";

        public static string Condition(string name) => Conditions + "/" + name + ".asset";

        public static string Action(string name) => Actions + "/" + name + ".asset";

        public static string Rule(string name) => P31Rules + "/" + name + ".asset";

        public static string OldRule(string name) => Rules + "/" + name + ".asset";

        public static string Npc(string name) => NpcDefinitions + "/" + name + ".asset";

        public static string Interactable(string name) => Interactables + "/" + name + ".asset";

        public static string Material(string name) => Materials + "/" + name + ".mat";

        public static string Texture(string name) => Textures + "/" + name + ".png";
    }

    /// <summary>Where things stand in the world (metres).</summary>
    public static class HollowmereLayout
    {
        // Thornwick Village (centre 0,0,0)
        public static readonly Vector3 Inn = new Vector3(17f, 0f, -14f);
        public static readonly Vector3 Bram = new Vector3(11f, 0f, -13f);
        public static readonly Vector3 TinkersBench = new Vector3(11f, 0f, -17.5f);
        public static readonly Vector3 BarnLantern = new Vector3(8f, 0f, 7f);
        public static readonly Vector3 GardenHerbs = new Vector3(-9f, 0f, 15f);
        public static readonly Vector3 VillageCoins = new Vector3(4f, 0f, -3f);
        public static readonly Vector3 VillageGate = new Vector3(31f, 0f, 0f);
        public static readonly Vector3 HealerHouse = new Vector3(-14f, 0f, 10f);

        // Blackmere Marsh (centre 200,0,0)
        public static readonly Vector3 Hale = new Vector3(167.5f, 0f, 4.5f);
        public static readonly Vector3 CausewayGate = new Vector3(169.5f, 0f, 2f);
        public static readonly Vector3 ShrineWest = new Vector3(199f, 0f, 4f);
        public static readonly Vector3 ShrineMiddle = new Vector3(206f, 0f, 2f);
        public static readonly Vector3 ShrineEast = new Vector3(213f, 0f, 4f);
        public static readonly Vector3 Shrine = new Vector3(206f, 0f, 9.5f);
        public static readonly Vector3 Clapper = new Vector3(194f, 0f, 9f);
        public static readonly Vector3 Sinkhole = new Vector3(216f, 0f, -12f);
        public static readonly Vector3 Satchel = new Vector3(186f, 0f, -16f);
        public static readonly Vector3 MarshOil = new Vector3(190f, 0f, -6f);
        public static readonly Vector3 MarshHerbs = new Vector3(204f, 0f, -14f);
        public static readonly Vector3 Jetty = new Vector3(224f, 0f, 24f);
        public static readonly Vector3 Odd = new Vector3(221f, 0f, 20f);
        public static readonly Vector3 Punt = new Vector3(228.5f, 0f, 27f);

        // The Drowned Belfry (centre 100,0,200)
        public static readonly Vector3 Bell = new Vector3(100f, 0f, 198f);
        public static readonly Vector3 Echo = new Vector3(100f, 0f, 191f);
    }
}
