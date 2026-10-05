// Hollowmere - "The Drowned Bell" as P3.1 authors it (docs/studio/05 "Reference game content"): the facts, items,
// condition and action sets, the six conversations, the quest with its three endings and its failure path, the rules,
// the inn vendor and the world items. Every step is a change set through the Studio edit engine (StudioAuthor), in
// dependency order: assets are created first (create, fixed authoring ids), registered on the content set (assign
// append) and wired afterwards (set), so no operation refers to an object its own change set creates.
//
// The story (05): Rumour (Healer Maren) -> Lantern (find it in the barn, buy it from Bram at the inn, or craft it at
// the tinker's bench from an oil flask and marsh herbs) -> Crossing (Warden Hale opens the causeway gate for anyone
// carrying a lantern; then Odd's ferry - his favour is earned by lighting the sunken shrine's three lanterns west to
// east - or the old punt, sealed with an oil flask) -> Belfry (ring the bell with its clapper: ending B, or ending C
// when the shrine burns; or let it sleep: ending A). Losing the only lantern in the marsh's sinkhole fails the quest.
//
// Effects the action kinds cannot express yet (buying from a vendor, rolling a loot table, restoring stamina) are raised
// as counter facts that the director's interim fact-request bridge turns into typed commands (HollowmereDirector).
#nullable enable
using System.Collections.Generic;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using static Hollowmere.Authoring.HollowmerePaths;

namespace Hollowmere.Authoring
{
    /// <summary>The narrative half of AuthorAll.</summary>
    public static class HollowmereStory
    {
        public static readonly string[] NewFacts =
        {
            "shrine_lit", "shrine_post_1", "shrine_post_2", "shrine_post_3", "odd_favour", "punt_repaired", "bell_silenced",
            "lantern_lost", "ending_a", "ending_b", "ending_c", "buy_lantern", "buy_oil", "buy_herbs", "herbs_used", "search_satchel",
        };

        /// <summary>Facts whose value counts requests (the director executes each increment once).</summary>
        public static readonly string[] RequestFacts = { "buy_lantern", "buy_oil", "buy_herbs", "herbs_used", "search_satchel" };

        // ------------------------------------------------------------------ values

        public static JObject Cond(string kind, string? subject, string op, int value) => new JObject
        {
            ["kind"] = kind,
            ["subject"] = subject,
            ["inventory"] = null,
            ["entityId"] = string.Empty,
            ["index"] = 0,
            ["op"] = op,
            ["value"] = value,
        };

        public static JObject Act(string kind, string? target, int value = 1, string text = "", string entityId = "") => new JObject
        {
            ["kind"] = kind,
            ["target"] = target,
            ["inventory"] = null,
            ["entityId"] = entityId,
            ["value"] = value,
            ["text"] = text,
        };

        public static JObject FactAtLeast(string fact, int value = 1) => Cond("Fact", Fact(fact), "GreaterOrEqual", value);

        public static JObject FactIs(string fact, int value) => Cond("Fact", Fact(fact), "Equal", value);

        public static JObject Holds(string item, int count = 1) => Cond("ItemCount", Item(item), "GreaterOrEqual", count);

        public static JObject HoldsNone(string item) => Cond("ItemCount", Item(item), "Equal", 0);

        public static JObject SetFact(string fact, int value = 1) => Act("SetFact", Fact(fact), value);

        public static JObject AddFact(string fact, int value = 1) => Act("AddFact", Fact(fact), value);

        public static JObject Message(string text) => Act("ShowMessage", null, 1, text);

        public static JObject Audio(string clip) => Act("PlayAudio", null, 1, clip);

        // ------------------------------------------------------------------ steps

        public static void Author(StudioAuthor a)
        {
            CreateFactsAndItems(a);
            CreateSets(a);
            RegisterContent(a, "story.register-1", AllFirstWave());
            ConfigureItems(a);
            CreateVendorAndLoot(a);
            Quest(a);
        }

        private static void CreateFactsAndItems(StudioAuthor a)
        {
            a.Step("story.facts", "Create the P3.1 facts of The Drowned Bell (shrine, crossing, endings, failure, interim requests)", () =>
            {
                var ops = new List<Operation>();
                foreach (string fact in NewFacts)
                {
                    ops.Add(StudioAuthor.Create("f_" + fact, "narrative.fact", Fact(fact), fact, StudioAuthor.Id("fact." + fact),
                        new JObject { ["factName"] = fact, ["initialValue"] = 0, ["persistent"] = true }));
                }

                return ops;
            });

            a.Step("story.items", "Create the oil flask and marsh herbs items", () => new List<Operation>
            {
                StudioAuthor.Create("i_oil", "inventory.item", Item("OilFlask"), "OilFlask", StudioAuthor.Id("item.OilFlask"),
                    new JObject { ["displayName"] = "Oil Flask", ["maxStack"] = 5, ["weight"] = 300, ["price"] = 2, ["tags"] = StudioAuthor.Strings("consumable", "pitch") }),
                StudioAuthor.Create("i_herbs", "inventory.item", Item("MarshHerbs"), "MarshHerbs", StudioAuthor.Id("item.MarshHerbs"),
                    new JObject { ["displayName"] = "Marsh Herbs", ["maxStack"] = 10, ["weight"] = 50, ["price"] = 1, ["tags"] = StudioAuthor.Strings("herb", "heal") }),
            });
        }

        /// <summary>Condition sets and action sets (inline entries reference facts and items by path).</summary>
        private static readonly (string Name, string Mode, JObject[] Entries)[] ConditionSets =
        {
            ("HasLantern", "All", new[] { Holds("Lantern") }),
            ("HeardRumour", "All", new[] { FactAtLeast("heard_rumour") }),
            ("ShrineLit", "All", new[] { FactAtLeast("shrine_lit") }),
            ("OddFavour", "All", new[] { FactAtLeast("odd_favour") }),
            ("ShrinePost1Lit", "All", new[] { Holds("Lantern"), FactAtLeast("shrine_post_1") }),
            ("ShrinePost2Lit", "All", new[] { Holds("Lantern"), FactAtLeast("shrine_post_2") }),
            ("HasCoins1", "All", new[] { Holds("OldCoin", 1) }),
            ("HasCoins2", "All", new[] { Holds("OldCoin", 2) }),
            ("HasCoins4", "All", new[] { Holds("OldCoin", 4) }),
            ("LanternLostNoSpare", "All", new[] { FactAtLeast("lantern_lost"), HoldsNone("Lantern") }),
            ("EndingReached", "Any", new[] { FactAtLeast("ending_a"), FactAtLeast("ending_b"), FactAtLeast("ending_c") }),
        };

        private static readonly (string Name, JObject[] Entries)[] ActionSets =
        {
            ("OddGrantsFavour", new[] { SetFact("odd_favour"), Audio("sfx.ui.confirm") }),
            ("FerryCrossing", new[] { Message("Odd poles you across the black water to the drowned belfry."), Act("Travel", BelfryRegion) }),
            ("SilenceBell", new[] { SetFact("bell_silenced"), SetFact("ending_a") }),
            ("BuyLantern", new[] { AddFact("buy_lantern") }),
            ("BuyOil", new[] { AddFact("buy_oil") }),
            ("BuyHerbs", new[] { AddFact("buy_herbs") }),
        };

        private static void CreateSets(StudioAuthor a)
        {
            a.Step("story.sets", "Create the condition and action sets of the P3.1 story", () =>
            {
                var ops = new List<Operation>();
                foreach (var set in ConditionSets)
                {
                    ops.Add(StudioAuthor.Create("c_" + set.Name, "logic.conditionSet", Condition(set.Name), set.Name, StudioAuthor.Id("conditions." + set.Name),
                        new JObject { ["mode"] = set.Mode, ["conditions"] = new JArray(set.Entries) }));
                }

                foreach (var set in ActionSets)
                {
                    ops.Add(StudioAuthor.Create("a_" + set.Name, "logic.actionSet", Action(set.Name), set.Name, StudioAuthor.Id("actions." + set.Name),
                        new JObject { ["actions"] = new JArray(set.Entries) }));
                }

                return ops;
            });
        }

        private static List<string> AllFirstWave()
        {
            var paths = new List<string>();
            foreach (string fact in NewFacts)
            {
                paths.Add(Fact(fact));
            }

            paths.Add(Item("OilFlask"));
            paths.Add(Item("MarshHerbs"));
            foreach (var set in ConditionSets)
            {
                paths.Add(Condition(set.Name));
            }

            foreach (var set in ActionSets)
            {
                paths.Add(Action(set.Name));
            }

            return paths;
        }

        /// <summary>Lists definitions on the content set (assign append, one op each).</summary>
        public static void RegisterContent(StudioAuthor a, string step, List<string> paths)
        {
            a.Step(step, "Register " + paths.Count + " definitions on the Hollowmere content set", () =>
            {
                AuthoringRef set = a.Ref(ContentSet);
                var ops = new List<Operation>();
                for (int i = 0; i < paths.Count; i++)
                {
                    ops.Add(StudioAuthor.Assign("r" + i, set, "definitions", paths[i], true));
                }

                return ops;
            });
        }

        private static void ConfigureItems(StudioAuthor a)
        {
            a.Step("story.item-tuning", "Lantern stacks to two (a spare is possible) and costs four coins; the coin is shown as Coin", () => new List<Operation>
            {
                StudioAuthor.Set("lantern", a.Ref(Item("Lantern")), new JObject { ["maxStack"] = 2, ["price"] = 4 }),
                StudioAuthor.Set("coin", a.Ref(Item("OldCoin")), new JObject { ["displayName"] = "Coin" }),
            });

            a.Step("story.starting-coins", "The traveller starts with two coins", () => new List<Operation>
            {
                StudioAuthor.Call("grant", "inventory.grantStarting", a.Ref(Item("PlayerInventory")), new JObject { ["item"] = Item("OldCoin"), ["count"] = 2 }),
            });
        }

        private static void CreateVendorAndLoot(StudioAuthor a)
        {
            a.Step("story.inn-vendor", "Create Bram's stock at the Drowned Lantern inn (lantern 4, oil flask 2, marsh herbs 1 coin)", () => new List<Operation>
            {
                StudioAuthor.Create("vendor", "inventory.vendor", Item("InnStock"), "InnStock", StudioAuthor.Id("vendor.InnStock"), new JObject
                {
                    ["displayName"] = "The Drowned Lantern",
                    ["paymentItem"] = Item("OldCoin"),
                    ["stockSlots"] = 8,
                    ["stock"] = new JArray
                    {
                        new JObject { ["item"] = Item("Lantern"), ["stock"] = 1, ["buyPrice"] = 4, ["sellPrice"] = 2, ["unlimited"] = false },
                        new JObject { ["item"] = Item("OilFlask"), ["stock"] = 3, ["buyPrice"] = 2, ["sellPrice"] = 1, ["unlimited"] = false },
                        new JObject { ["item"] = Item("MarshHerbs"), ["stock"] = 1, ["buyPrice"] = 1, ["sellPrice"] = 0, ["unlimited"] = true },
                    },
                }),
            });
            RegisterContent(a, "story.register-vendor", new List<string> { Item("InnStock") });

            a.Step("story.marsh-loot", "The drowned satchel's loot: coins, herbs or an oil flask", () => new List<Operation>
            {
                StudioAuthor.Set("loot", a.Ref(Item("MarshLoot")), new JObject
                {
                    ["rolls"] = 2,
                    ["entries"] = new JArray
                    {
                        new JObject { ["item"] = Item("OldCoin"), ["weight"] = 3, ["min"] = 1, ["max"] = 3 },
                        new JObject { ["item"] = Item("MarshHerbs"), ["weight"] = 2, ["min"] = 1, ["max"] = 2 },
                        new JObject { ["item"] = Item("OilFlask"), ["weight"] = 1, ["min"] = 1, ["max"] = 1 },
                    },
                }),
            });
        }

        private static void Quest(StudioAuthor a)
        {
            a.Step("story.quest", "Restructure The Drowned Bell: Rumour, Lantern, Crossing, Belfry with three endings and a failure condition", () => new List<Operation>
            {
                StudioAuthor.Set("quest", a.Ref(HollowmerePaths.Quest), new JObject
                {
                    ["title"] = "The Drowned Bell",
                    ["branchNames"] = StudioAuthor.Strings("let it sleep", "ring the bell", "free the echo"),
                    ["stages"] = new JArray
                    {
                        Stage("Rumour", "Healer Maren speaks of the Drowned Bell, silent since the flood.", 1),
                        Stage("A light for the marsh", "Blackmere is dark. Find a lantern, buy one at the inn, or craft one at the tinker's bench.", 2),
                        Stage("The crossing", "Get past Warden Hale's gate, then cross the black water: earn Odd's favour or mend the old punt.", 3),
                        Stage("The drowned belfry", "Ring the bell with its clapper, or let it sleep.", -1),
                    },
                    ["objectives"] = new JArray
                    {
                        Objective(0, "Fact", Fact("heard_rumour"), 1, 0, "Hear Maren out"),
                        Objective(1, "Collect", Item("Lantern"), 1, 0, "Get a lantern (find, buy or craft one)"),
                        Objective(2, "Fact", Fact("gate_open"), 1, 0, "Pass Warden Hale's gate"),
                        Objective(2, "Reach", BelfryRegion, 1, 0, "Cross to the Drowned Belfry"),
                        Objective(3, "Fact", Fact("ending_a"), 1, 1, "Let the bell sleep"),
                        Objective(3, "Fact", Fact("ending_b"), 1, 2, "Ring the Drowned Bell"),
                        Objective(3, "Fact", Fact("ending_c"), 1, 3, "Ring the bell with the shrine alight"),
                    },
                    ["rewards"] = new JArray
                    {
                        new JObject { ["kind"] = "Fact", ["target"] = Fact("maren_grateful"), ["value"] = 1, ["branch"] = 2 },
                        new JObject { ["kind"] = "Fact", ["target"] = Fact("maren_grateful"), ["value"] = 1, ["branch"] = 3 },
                    },
                    ["failConditions"] = Condition("LanternLostNoSpare"),
                }),
            });
        }

        private static JObject Stage(string title, string description, int next) =>
            new JObject { ["title"] = title, ["description"] = description, ["next"] = next };

        private static JObject Objective(int stage, string kind, string target, int required, int branch, string text) => new JObject
        {
            ["stage"] = stage,
            ["kind"] = kind,
            ["target"] = target,
            ["targetEntityId"] = string.Empty,
            ["required"] = required,
            ["branch"] = branch,
            ["text"] = text,
        };
    }
}
