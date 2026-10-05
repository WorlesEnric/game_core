// Hollowmere - the narrative side of the Hollowmere world (P1.4): which modules it runs, where its baked content is,
// and how it plugs into P1.3's player, NPC and interaction plugins.
//
// The four P1.4 modules are listed here, in the game, because the logic package cannot know the packages built on it.
// Boot composes them onto the baked world plan (NarrativeComposer.Boot); P1.3's world extensions travel in the
// WorldBuildOptions and are composed by WorldBuilder as usual. Wire then hands P1.3's seams their P1.4 implementations:
// the interaction module's IConditionEvaluator (the Causeway Gate's lock condition is narrative.fact.gate_open), the
// interaction dispatcher's IActionRunner and the NPC talk dispatcher's IConversationStarter (NpcDefinition.dialogueGraph
// refs such as dialogue.maren resolve to the graph that answers to them). GameBoot (P1.3's boot scene) is switched to
// this boot by its owner (P1.5/P3.1).
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
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
        public const string BellCondition = "HasBellClapper";
        public const string BellActions = "RingBell";

        /// <summary>The NPC graph refs of P1.3's NpcDefinitions (NpcDefinition.dialogueGraph).</summary>
        public const string MarenGraphRef = "dialogue.maren";
        public const string OddGraphRef = "dialogue.odd";
        public const string PipGraphRef = "dialogue.pip";
        public const string HaleGraphRef = "dialogue.hale";
        public const string EchoGraphRef = "dialogue.belfry_echo";

        /// <summary>The placed NPC entities (P1.3's NPCs on P1.1's world).</summary>
        public const string MarenId = "da525107-c2c1-4626-ac08-5058a1e01c06";
        public const string OddId = "feae7fb3-57f9-4711-8549-04fa8f161ac2";
        public const string PipId = "508f7d78-7b44-42e8-8028-be4b2c9ee4b0";
        public const string HaleId = "3f2e423d-ec9f-4051-8713-662180be4db9";
        public const string EchoId = "a91b8bb5-cbd4-4872-871b-118e53cc465d";

        /// <summary>The Causeway Gate entity in the marsh (P1.3's interactable, locked by narrative.fact.gate_open).</summary>
        public const string GateId = "0b44e6c8-4c3c-483f-9202-57dc00024a20";

        /// <summary>The Drowned Bell entity in the belfry (P1.1's bell, P1.3's switch).</summary>
        public const string BellId = "e45e9ce2-198c-48ee-8a4e-b01bc4bf0bbf";

        /// <summary>
        /// Composes the narrative modules onto the baked world (with P1.3's extensions when <paramref name="build"/> carries
        /// them) and boots it (Ready unless <paramref name="start"/>).
        /// </summary>
        public static NarrativeWorld Boot(
            RegionManifest manifest,
            GameplayContentManifest content,
            HollowmereNarrativeModules modules,
            GameApplicationBootOptions? options,
            WorldBuildOptions? build,
            bool start)
        {
            return NarrativeComposer.Boot(manifest, content, modules.All, options, build, start);
        }

        /// <summary>Hands P1.3's seams their P1.4 implementations; every argument but the world may be null.</summary>
        public static void Wire(NarrativeWorld game, InteractionWorldExtension? interactions, InteractionSession? interactionSession, NpcSession? npcs)
        {
            if (interactions != null)
            {
                interactions.Conditions = game.Conditions;
                if (interactions.Module != null)
                {
                    interactions.Module.Conditions = game.Conditions;
                }
            }

            if (interactionSession != null)
            {
                interactionSession.Dispatcher.Actions = game.Actions;
            }

            if (npcs != null)
            {
                npcs.Conversations.Conversations = game.Conversations;
            }
        }
    }
}
