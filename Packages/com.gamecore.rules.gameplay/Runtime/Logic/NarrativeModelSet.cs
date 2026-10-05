// GameCore.Rules.Gameplay.Logic - the baked narrative content of one world as pure models (P1.4).
//
// Built once per boot from the content manifest (facts, condition and action sets, rules, items, inventories, vendors,
// loot tables, world items, quests and dialogue graphs), keyed by the int32 keys the slots and payloads carry. The four
// narrative modules, the presenters and the Studio tools all read the same instance; it is immutable after Freeze.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Quest;

namespace GameCore.Rules.Gameplay.Logic
{
    /// <summary>A declared fact: one int32 slot narrative.fact.&lt;name&gt; on the narrative state target.</summary>
    public sealed class FactModel
    {
        public FactModel(int key, string name, int initial, bool persistent)
        {
            Key = key;
            Name = name ?? string.Empty;
            Initial = initial;
            Persistent = persistent;
        }

        public int Key { get; }

        public string Name { get; }

        public int Initial { get; }

        /// <summary>False = the fact resets to its initial value when a conversation ends (conversation-local facts).</summary>
        public bool Persistent { get; }
    }

    /// <summary>A placed world item (a pickup lying in a region).</summary>
    public sealed class WorldItemModel
    {
        public WorldItemModel(int key, string authoringId, string name, int itemKey, int count, int regionKey, string regionId, int x, int y, int z, string entityId)
        {
            Key = key;
            AuthoringId = authoringId ?? string.Empty;
            Name = name ?? string.Empty;
            ItemKey = itemKey;
            Count = count < 1 ? 1 : count;
            RegionKey = regionKey;
            RegionId = regionId ?? string.Empty;
            X = x;
            Y = y;
            Z = z;
            EntityId = entityId ?? string.Empty;
        }

        public int Key { get; }

        public string AuthoringId { get; }

        public string Name { get; }

        public int ItemKey { get; }

        public int Count { get; }

        public int RegionKey { get; }

        public string RegionId { get; }

        /// <summary>Position in millimetres.</summary>
        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        /// <summary>Authoring id of a placed entity that presents the item (empty = none).</summary>
        public string EntityId { get; }
    }

    /// <summary>Every narrative model of one world.</summary>
    public sealed class NarrativeModelSet : IConditionSetLookup, IActionSetLookup, IItemLookup
    {
        private readonly Dictionary<int, FactModel> facts = new Dictionary<int, FactModel>();
        private readonly Dictionary<int, ConditionSetModel> conditionSets = new Dictionary<int, ConditionSetModel>();
        private readonly Dictionary<int, ActionSetModel> actionSets = new Dictionary<int, ActionSetModel>();
        private readonly Dictionary<int, RuleModel> rules = new Dictionary<int, RuleModel>();
        private readonly Dictionary<int, ItemModel> items = new Dictionary<int, ItemModel>();
        private readonly Dictionary<int, InventoryModel> inventories = new Dictionary<int, InventoryModel>();
        private readonly Dictionary<int, VendorModel> vendors = new Dictionary<int, VendorModel>();
        private readonly Dictionary<int, LootTableModel> lootTables = new Dictionary<int, LootTableModel>();
        private readonly Dictionary<int, WorldItemModel> worldItems = new Dictionary<int, WorldItemModel>();
        private readonly Dictionary<int, QuestModel> quests = new Dictionary<int, QuestModel>();
        private readonly Dictionary<int, DialogueGraphModel> graphs = new Dictionary<int, DialogueGraphModel>();
        private readonly Dictionary<string, int> refs = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, string> names = new Dictionary<int, string>();
        private readonly List<string> problems = new List<string>();

        public bool IsFrozen { get; private set; }

        public IReadOnlyList<string> Problems => problems;

        public IEnumerable<FactModel> Facts => Sorted(facts);

        public IEnumerable<RuleModel> Rules => Sorted(rules);

        public IEnumerable<ItemModel> Items => Sorted(items);

        public IEnumerable<InventoryModel> Inventories => Sorted(inventories);

        public IEnumerable<VendorModel> Vendors => Sorted(vendors);

        public IEnumerable<LootTableModel> LootTables => Sorted(lootTables);

        public IEnumerable<WorldItemModel> WorldItems => Sorted(worldItems);

        public IEnumerable<QuestModel> Quests => Sorted(quests);

        public IEnumerable<DialogueGraphModel> Graphs => Sorted(graphs);

        public IEnumerable<ConditionSetModel> ConditionSets => Sorted(conditionSets);

        public IEnumerable<ActionSetModel> ActionSets => Sorted(actionSets);

        public int FactCount => facts.Count;

        public int RuleCount => rules.Count;

        public int QuestCount => quests.Count;

        public int GraphCount => graphs.Count;

        public int InventoryCount => inventories.Count;

        public int VendorCount => vendors.Count;

        public int WorldItemCount => worldItems.Count;

        /// <summary>The largest K of any inventory or vendor stock (the inv.item/inv.count slots the plugin declares).</summary>
        public int MaxInventorySlots
        {
            get
            {
                int max = 1;
                foreach (InventoryModel inventory in inventories.Values)
                {
                    max = Math.Max(max, inventory.SlotCount);
                }

                foreach (VendorModel vendor in vendors.Values)
                {
                    max = Math.Max(max, vendor.StockSlots);
                }

                return max;
            }
        }

        /// <summary>The most objectives of any quest (the quest.obj.n slots the plugin declares).</summary>
        public int MaxObjectives
        {
            get
            {
                int max = 0;
                foreach (QuestModel quest in quests.Values)
                {
                    max = Math.Max(max, quest.Objectives.Count);
                }

                return max;
            }
        }

        /// <summary>The most visited words of any graph (the dialogue.visited.w slots the plugin declares).</summary>
        public int MaxVisitedWords
        {
            get
            {
                int max = 1;
                foreach (DialogueGraphModel graph in graphs.Values)
                {
                    max = Math.Max(max, graph.VisitedWords);
                }

                return max;
            }
        }

        /// <summary>The player's inventory (IsPlayer), else the lowest-keyed inventory, else null.</summary>
        public InventoryModel? PlayerInventory
        {
            get
            {
                InventoryModel? fallback = null;
                foreach (InventoryModel inventory in Sorted(inventories))
                {
                    if (inventory.IsPlayer)
                    {
                        return inventory;
                    }

                    if (fallback == null)
                    {
                        fallback = inventory;
                    }
                }

                return fallback;
            }
        }

        public void AddFact(FactModel fact) => Put(facts, fact.Key, fact, fact.Name, "fact");

        public void AddConditionSet(ConditionSetModel set, string reference) => Put(conditionSets, set.Key, set, reference, "condition set");

        public void AddActionSet(ActionSetModel set, string reference) => Put(actionSets, set.Key, set, reference, "action set");

        public void AddRule(RuleModel rule, string reference) => Put(rules, rule.Key, rule, reference, "rule");

        public void AddItem(ItemModel item, string reference) => Put(items, item.Key, item, reference, "item");

        public void AddInventory(InventoryModel inventory, string reference) => Put(inventories, inventory.Key, inventory, reference, "inventory");

        public void AddVendor(VendorModel vendor, string reference) => Put(vendors, vendor.Key, vendor, reference, "vendor");

        public void AddLootTable(LootTableModel table, string reference) => Put(lootTables, table.Key, table, reference, "loot table");

        public void AddWorldItem(WorldItemModel item) => Put(worldItems, item.Key, item, item.AuthoringId, "world item");

        public void AddQuest(QuestModel quest, string reference) => Put(quests, quest.Key, quest, reference, "quest");

        public void AddGraph(DialogueGraphModel graph, string reference) => Put(graphs, graph.Key, graph, reference, "dialogue graph");

        /// <summary>Records an alias (a name or an authoring id) for a key.</summary>
        public void Alias(string reference, int key)
        {
            ThrowIfFrozen();
            if (!string.IsNullOrEmpty(reference) && !refs.ContainsKey(reference))
            {
                refs[reference] = key;
            }
        }

        /// <summary>Records a problem found while building (the bake reports them; the runtime logs them).</summary>
        public void Problem(string problem)
        {
            if (!string.IsNullOrEmpty(problem) && problems.Count < 256)
            {
                problems.Add(problem);
            }
        }

        public void Freeze() => IsFrozen = true;

        /// <summary>The key of an authoring id or name recorded for any model.</summary>
        public bool TryResolve(string reference, out int key) => refs.TryGetValue(reference ?? string.Empty, out key);

        /// <summary>A readable name of a key (the model's name), or the key in decimal.</summary>
        public string NameOf(int key) => names.TryGetValue(key, out string? name) ? name : key.ToString(CultureInfo.InvariantCulture);

        public bool TryGetFact(int key, out FactModel? fact) => Get(facts, key, out fact);

        public bool TryGetFactByName(string name, out FactModel? fact)
        {
            foreach (FactModel candidate in facts.Values)
            {
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    fact = candidate;
                    return true;
                }
            }

            fact = null;
            return false;
        }

        public bool TryGet(int key, out ConditionSetModel? set) => Get(conditionSets, key, out set);

        public bool TryGet(int key, out ActionSetModel? set) => Get(actionSets, key, out set);

        public bool TryGet(int key, out ItemModel? item) => Get(items, key, out item);

        public bool TryGetRule(int key, out RuleModel? rule) => Get(rules, key, out rule);

        public bool TryGetInventory(int key, out InventoryModel? inventory) => Get(inventories, key, out inventory);

        public bool TryGetVendor(int key, out VendorModel? vendor) => Get(vendors, key, out vendor);

        public bool TryGetLootTable(int key, out LootTableModel? table) => Get(lootTables, key, out table);

        public bool TryGetWorldItem(int key, out WorldItemModel? item) => Get(worldItems, key, out item);

        public bool TryGetQuest(int key, out QuestModel? quest) => Get(quests, key, out quest);

        public bool TryGetGraph(int key, out DialogueGraphModel? graph) => Get(graphs, key, out graph);

        private void Put<T>(Dictionary<int, T> table, int key, T model, string reference, string kind)
            where T : class
        {
            ThrowIfFrozen();
            if (key == 0)
            {
                Problem(kind + " " + reference + " has key 0");
                return;
            }

            if (table.ContainsKey(key) || names.ContainsKey(key))
            {
                Problem(kind + " " + reference + " collides with key " + key.ToString(CultureInfo.InvariantCulture) + " (" + NameOf(key) + ")");
                return;
            }

            table[key] = model;
            names[key] = string.IsNullOrEmpty(reference) ? kind + " " + key.ToString(CultureInfo.InvariantCulture) : reference;
            Alias(reference, key);
        }

        private static bool Get<T>(Dictionary<int, T> table, int key, out T? model)
            where T : class
        {
            if (table.TryGetValue(key, out T? found))
            {
                model = found;
                return true;
            }

            model = null;
            return false;
        }

        private static List<T> Sorted<T>(Dictionary<int, T> table)
        {
            var keys = new List<int>(table.Keys);
            keys.Sort();
            var list = new List<T>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                list.Add(table[keys[i]]);
            }

            return list;
        }

        private void ThrowIfFrozen()
        {
            if (IsFrozen)
            {
                throw new InvalidOperationException("the narrative model set is frozen");
            }
        }
    }
}
