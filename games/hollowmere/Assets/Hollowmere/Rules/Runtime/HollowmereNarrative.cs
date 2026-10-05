// Hollowmere - the narrative side of the Hollowmere world (P1.4): which modules it runs and where its baked content is.
//
// The four P1.4 modules are listed here, in the game, because the logic package cannot know the packages built on it.
// Boot composes them onto the baked P1.1 world (NarrativeComposer.Boot); GameBoot (P1.1) keeps booting the world without
// narrative until the boot scene is switched over (left to P1.5/P3.1, which own the boot scene and the UI views).
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
using GameCore.Unity.App;

namespace Hollowmere.Narrative
{
    /// <summary>The Hollowmere narrative modules of one world.</summary>
    public sealed class HollowmereNarrativeModules
    {
        public HollowmereNarrativeModules()
        {
            Logic = new LogicModule();
            Inventory = new InventoryModule();
            Quest = new QuestModule();
            Dialogue = new DialogueModule();
            All = new INarrativeModule[] { Logic, Inventory, Quest, Dialogue };
        }

        public LogicModule Logic { get; }

        public InventoryModule Inventory { get; }

        public QuestModule Quest { get; }

        public DialogueModule Dialogue { get; }

        public IReadOnlyList<INarrativeModule> All { get; }
    }

    /// <summary>Boots Hollowmere with its narrative content.</summary>
    public static class HollowmereNarrative
    {
        public const string ContentSetPath = "Assets/Hollowmere/Rules/HollowmereContent.asset";
        public const string ContentManifestPath = "Assets/Hollowmere/Rules/HollowmereContent.content.asset";

        /// <summary>The graph, quest and subject names the game and its tests use.</summary>
        public const string MarenGraph = "Maren";
        public const string OddGraph = "Odd";
        public const string PipGraph = "Pip";
        public const string HaleGraph = "Hale";
        public const string EchoGraph = "BelfryEcho";
        public const string Quest = "DrownedBell";
        public const string Vendor = "OddsStall";
        public const string OldCoin = "OldCoin";
        public const string GateKey = "GateKey";
        public const string BellClapper = "BellClapper";
        public const string Lantern = "Lantern";
        public const string ClapperWorldItem = "BellClapper_Marsh";
        public const string GateCondition = "HasGateKey";
        public const string GateActions = "OpenGate";
        public const string BellCondition = "HasBellClapper";
        public const string BellActions = "RingBell";

        /// <summary>The subject id of the marsh gate (no scene entity yet; P1.3's gate interactable passes this id).</summary>
        public const string GateId = "4d52acb7-adef-473d-9b21-45bba7e3121a";

        /// <summary>The Drowned Bell entity in the belfry (P1.1).</summary>
        public const string BellId = "e45e9ce2-198c-48ee-8a4e-b01bc4bf0bbf";

        /// <summary>Composes the narrative modules onto the baked world and boots it (Ready unless <paramref name="start"/>).</summary>
        public static NarrativeWorld Boot(
            RegionManifest manifest,
            GameplayContentManifest content,
            HollowmereNarrativeModules modules,
            GameApplicationBootOptions? options,
            bool start)
        {
            return NarrativeComposer.Boot(manifest, content, modules.All, options, null, start);
        }
    }
}
