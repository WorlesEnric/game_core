// GameCore.Gameplay.Contracts.Narrative - catalog registrations and diagnostic codes of the dialogue, quest, inventory
// and logic plugins (P1.4).
//
// Each plugin needs its plugin factory and configuration schema in the generated catalog (CatalogManifestSource refuses
// a mount whose factory or schema the catalog does not register); its command system, recipe applier, slot layouts and
// domain schemas are registered as well, exactly like the entities and world plugins (GameplayCatalogNames). The four
// packages' bake extensions add these registrations through the gameplay compile hook
// (GameCore.Gameplay.Compile.IGameplayBakeExtension), so every bake of a project that contains a package includes it.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Gameplay.Contracts.Narrative
{
    /// <summary>The catalog registrations of one narrative plugin.</summary>
    public sealed class NarrativeCatalogSet
    {
        public NarrativeCatalogSet(
            string package,
            string plugin,
            string commandSystem,
            string applier,
            IReadOnlyList<string> layouts,
            IReadOnlyList<GameplayCatalogNames.SchemaName> schemas,
            IReadOnlyList<GameplayCatalogNames.EntryName> entries)
        {
            Package = package;
            Plugin = plugin;
            CommandSystem = commandSystem;
            Applier = applier;
            Layouts = layouts;
            Schemas = schemas;
            Entries = entries;
        }

        /// <summary>Owner package stable name (without the gameplay prefix), e.g. <c>package.logic</c>.</summary>
        public string Package { get; }

        public string Plugin { get; }

        public string CommandSystem { get; }

        public string Applier { get; }

        public IReadOnlyList<string> Layouts { get; }

        public IReadOnlyList<GameplayCatalogNames.SchemaName> Schemas { get; }

        public IReadOnlyList<GameplayCatalogNames.EntryName> Entries { get; }
    }

    /// <summary>Stable names (without the <c>gameplay.</c> prefix) of the narrative plugins' catalog registrations.</summary>
    public static class NarrativeCatalogNames
    {
        public const string LogicPackage = "package.logic";
        public const string InventoryPackage = "package.inventory";
        public const string QuestPackage = "package.quest";
        public const string DialoguePackage = "package.dialogue";

        public const string LogicConfigSchema = "logic.schema.config";
        public const string LogicRuleDomain = "logic.domain.rule";
        public const string InventoryConfigSchema = "inventory.schema.config";
        public const string InventoryDomain = "inventory.domain.inventory";
        public const string WorldItemDomain = "inventory.domain.world-item";
        public const string QuestConfigSchema = "quest.schema.config";
        public const string QuestDomain = "quest.domain.quest";
        public const string DialogueConfigSchema = "dialogue.schema.config";
        public const string DialogueStateDomain = "dialogue.domain.state";
        public const string DialogueGraphDomain = "dialogue.domain.graph";

        public static NarrativeCatalogSet Logic { get; } = Set(
            LogicPackage, "logic", "Logic",
            new[] { "logic.layout.rule" },
            new[]
            {
                new GameplayCatalogNames.SchemaName(LogicConfigSchema, "logic.serializer.config", "LogicConfig", LogicPackage),
                new GameplayCatalogNames.SchemaName(LogicRuleDomain, "logic.serializer.domain-rule", "LogicRuleDomain", LogicPackage),
            });

        public static NarrativeCatalogSet Inventory { get; } = Set(
            InventoryPackage, "inventory", "Inventory",
            new[] { "inventory.layout.inventory", "inventory.layout.world-item" },
            new[]
            {
                new GameplayCatalogNames.SchemaName(InventoryConfigSchema, "inventory.serializer.config", "InventoryConfig", InventoryPackage),
                new GameplayCatalogNames.SchemaName(InventoryDomain, "inventory.serializer.domain-inventory", "InventoryDomain", InventoryPackage),
                new GameplayCatalogNames.SchemaName(WorldItemDomain, "inventory.serializer.domain-world-item", "WorldItemDomain", InventoryPackage),
            });

        public static NarrativeCatalogSet Quest { get; } = Set(
            QuestPackage, "quest", "Quest",
            new[] { "quest.layout.quest" },
            new[]
            {
                new GameplayCatalogNames.SchemaName(QuestConfigSchema, "quest.serializer.config", "QuestConfig", QuestPackage),
                new GameplayCatalogNames.SchemaName(QuestDomain, "quest.serializer.domain-quest", "QuestDomain", QuestPackage),
            });

        public static NarrativeCatalogSet Dialogue { get; } = Set(
            DialoguePackage, "dialogue", "Dialogue",
            new[] { "dialogue.layout.state", "dialogue.layout.graph" },
            new[]
            {
                new GameplayCatalogNames.SchemaName(DialogueConfigSchema, "dialogue.serializer.config", "DialogueConfig", DialoguePackage),
                new GameplayCatalogNames.SchemaName(DialogueStateDomain, "dialogue.serializer.domain-state", "DialogueStateDomain", DialoguePackage),
                new GameplayCatalogNames.SchemaName(DialogueGraphDomain, "dialogue.serializer.domain-graph", "DialogueGraphDomain", DialoguePackage),
            });

        private static NarrativeCatalogSet Set(
            string package,
            string stem,
            string keyStem,
            string[] layouts,
            GameplayCatalogNames.SchemaName[] schemas)
        {
            string plugin = stem + ".plugin";
            string system = stem + ".system.command";
            string applier = stem + ".applier";
            var entries = new List<GameplayCatalogNames.EntryName>
            {
                new GameplayCatalogNames.EntryName("PluginFactory", plugin, keyStem + "PluginKey", package),
                new GameplayCatalogNames.EntryName("SystemFactory", system, keyStem + "CommandSystemKey", package),
                new GameplayCatalogNames.EntryName("LayoutApply", applier, keyStem + "ApplierKey", package),
            };

            for (int i = 0; i < layouts.Length; i++)
            {
                entries.Add(new GameplayCatalogNames.EntryName("LayoutApply", layouts[i], keyStem + "Layout" + (i + 1) + "Key", package));
            }

            return new NarrativeCatalogSet(
                package,
                plugin,
                system,
                applier,
                System.Array.AsReadOnly(layouts),
                System.Array.AsReadOnly(schemas),
                entries.AsReadOnly());
        }
    }

    /// <summary>Stable diagnostic codes of the narrative plugins (GP-&lt;area&gt;-&lt;nnn&gt;).</summary>
    public static class NarrativeDiagnosticCodes
    {
        // Content and bake
        public const string ContentSetMissingWorld = "GP-LOG-001";
        public const string ContentSetDuplicate = "GP-LOG-002";
        public const string ContentUnknownKind = "GP-LOG-003";
        public const string ContentKeyCollision = "GP-LOG-004";
        public const string ContentMissingReference = "GP-LOG-005";
        public const string ContentStale = "GP-LOG-006";

        // Logic
        public const string FactUnknown = "GP-LOG-010";
        public const string FactInvalidName = "GP-LOG-011";
        public const string FactDuplicate = "GP-LOG-012";
        public const string ConditionInvalid = "GP-LOG-013";
        public const string ConditionCycle = "GP-LOG-014";
        public const string ActionInvalid = "GP-LOG-015";
        public const string RuleMissingTrigger = "GP-LOG-016";
        public const string RuleOnCooldown = "GP-LOG-020";
        public const string RuleAlreadyFired = "GP-LOG-021";
        public const string RuleMaxFires = "GP-LOG-022";
        public const string RuleConditionFailed = "GP-LOG-023";
        public const string RuleUnknown = "GP-LOG-024";
        public const string ActionSetUnknown = "GP-LOG-025";
        public const string ConditionSetUnknown = "GP-LOG-026";

        // Dialogue
        public const string GraphEmpty = "GP-DLG-001";
        public const string GraphBadEntry = "GP-DLG-002";
        public const string GraphDanglingEdge = "GP-DLG-003";
        public const string GraphTooManyOptions = "GP-DLG-004";
        public const string GraphUnreachableNode = "GP-DLG-005";
        public const string GraphTooManyNodes = "GP-DLG-006";
        public const string ConversationActive = "GP-DLG-010";
        public const string ConversationInactive = "GP-DLG-011";
        public const string ChoiceUnavailable = "GP-DLG-012";
        public const string AdvanceAtChoice = "GP-DLG-013";
        public const string GraphUnknown = "GP-DLG-014";

        // Quest
        public const string QuestNoStages = "GP-QST-001";
        public const string QuestBadBranch = "GP-QST-002";
        public const string QuestTooManyObjectives = "GP-QST-003";
        public const string QuestObjectiveTarget = "GP-QST-004";
        public const string QuestNotActive = "GP-QST-010";
        public const string QuestAlreadyStarted = "GP-QST-011";
        public const string QuestStageOutOfRange = "GP-QST-012";
        public const string QuestObjectiveOutOfRange = "GP-QST-013";
        public const string QuestUnknown = "GP-QST-014";

        // Inventory
        public const string ItemBadStack = "GP-INV-001";
        public const string InventoryBadSlots = "GP-INV-002";
        public const string VendorMissingStock = "GP-INV-003";
        public const string WorldItemMissingRegion = "GP-INV-004";
        public const string LootTableEmpty = "GP-INV-005";
        public const string InventoryFull = "GP-INV-010";
        public const string InventoryOverweight = "GP-INV-011";
        public const string ItemNotHeld = "GP-INV-012";
        public const string CurrencyShort = "GP-INV-013";
        public const string VendorOutOfStock = "GP-INV-014";
        public const string ItemNotSold = "GP-INV-015";
        public const string WorldItemTaken = "GP-INV-016";
        public const string ItemUnknown = "GP-INV-017";
        public const string InventoryUnknown = "GP-INV-018";
        public const string RequestAlreadyApplied = "GP-INV-019";

        /// <summary>Every code, in declaration order.</summary>
        public static IReadOnlyList<string> All { get; } = System.Array.AsReadOnly(new[]
        {
            ContentSetMissingWorld, ContentSetDuplicate, ContentUnknownKind, ContentKeyCollision, ContentMissingReference, ContentStale,
            FactUnknown, FactInvalidName, FactDuplicate, ConditionInvalid, ConditionCycle, ActionInvalid, RuleMissingTrigger,
            RuleOnCooldown, RuleAlreadyFired, RuleMaxFires, RuleConditionFailed, RuleUnknown, ActionSetUnknown, ConditionSetUnknown,
            GraphEmpty, GraphBadEntry, GraphDanglingEdge, GraphTooManyOptions, GraphUnreachableNode, GraphTooManyNodes,
            ConversationActive, ConversationInactive, ChoiceUnavailable, AdvanceAtChoice, GraphUnknown,
            QuestNoStages, QuestBadBranch, QuestTooManyObjectives, QuestObjectiveTarget, QuestNotActive, QuestAlreadyStarted,
            QuestStageOutOfRange, QuestObjectiveOutOfRange, QuestUnknown,
            ItemBadStack, InventoryBadSlots, VendorMissingStock, WorldItemMissingRegion, LootTableEmpty, InventoryFull,
            InventoryOverweight, ItemNotHeld, CurrencyShort, VendorOutOfStock, ItemNotSold, WorldItemTaken, ItemUnknown,
            InventoryUnknown, RequestAlreadyApplied,
        });
    }
}
