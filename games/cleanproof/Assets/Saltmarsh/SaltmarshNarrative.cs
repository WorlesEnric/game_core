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

namespace Saltmarsh.Narrative
{
    public sealed class SaltmarshNarrativeModules
    {
        public SaltmarshNarrativeModules()
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

    public static class SaltmarshNarrative
    {
        public static NarrativeWorld Boot(
            RegionManifest manifest,
            GameplayContentManifest content,
            SaltmarshNarrativeModules modules,
            GameApplicationBootOptions? options,
            WorldBuildOptions? build,
            bool start)
        {
            return NarrativeComposer.Boot(manifest, content, modules.All, options, build, start);
        }

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
