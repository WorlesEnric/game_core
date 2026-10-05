// Hollowmere - the placed half of "The Drowned Bell" (P3.1): interactables and their entity definitions, Bram the
// innkeeper, Odd moved to the marsh jetty and Hale to the causeway gate, the pickups and their world items, the
// enclosures that close the two walk-in portals to the belfry (the belfry is reached by Odd's ferry or the punt), the
// dialogue graphs, the story rules, the director asset and the HollowmereGame component on Boot.unity.
// Region scenes must be open (AuthorAll opens Boot and the three regions); a step that places objects first makes its
// region scene active (npc.addAt, interaction.addExaminable and entity.place place into the active scene).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Hollowmere.Authoring.HollowmerePaths;
using static Hollowmere.Authoring.HollowmereStory;

namespace Hollowmere.Authoring
{
    /// <summary>The scene-dependent half of AuthorAll.</summary>
    public static class HollowmereWorldContent
    {
        public const string OddId = "feae7fb3-57f9-4711-8549-04fa8f161ac2";
        public const string HaleId = "3f2e423d-ec9f-4051-8713-662180be4db9";
        public const string BellId = "e45e9ce2-198c-48ee-8a4e-b01bc4bf0bbf";

        /// <summary>A pickup: interactable definition, its entity look, the placed name, where, and its world item.</summary>
        public sealed class Pickup
        {
            public Pickup(string definition, string prefab, int scaleMilli, string prompt, string placed, string scene, Vector3 at, string worldItem, string item, int count, string region, Vector3 regionOrigin, bool existingWorldItem)
            {
                Definition = definition;
                Prefab = prefab;
                ScaleMilli = scaleMilli;
                Prompt = prompt;
                Placed = placed;
                Scene = scene;
                At = at;
                WorldItem = worldItem;
                ItemName = item;
                Count = count;
                Region = region;
                RegionOrigin = regionOrigin;
                ExistingWorldItem = existingWorldItem;
            }

            public string Definition { get; }

            public string Prefab { get; }

            public int ScaleMilli { get; }

            public string Prompt { get; }

            public string Placed { get; }

            public string Scene { get; }

            public Vector3 At { get; }

            public string WorldItem { get; }

            public string ItemName { get; }

            public int Count { get; }

            public string Region { get; }

            public Vector3 RegionOrigin { get; }

            public bool ExistingWorldItem { get; }
        }

        private const string LanternPrefab = "Assets/Hollowmere/World/Prefabs/Lantern.prefab";
        private const string ReedPrefab = "Assets/Hollowmere/World/Prefabs/Reed.prefab";
        private const string StonePrefab = "Assets/Hollowmere/World/Prefabs/Stone.prefab";
        private const string CratePrefab = "Assets/Hollowmere/World/Prefabs/Crate.prefab";
        private const string BellPrefab = "Assets/Hollowmere/World/Prefabs/Bell.prefab";
        private const string SignpostPrefab = "Assets/Hollowmere/World/Prefabs/Signpost.prefab";
        private const string BucketPrefab = "Assets/Hollowmere/Interactables/Prefabs/WellBucket.prefab";

        private static readonly Vector3 VillageOrigin = Vector3.zero;
        private static readonly Vector3 MarshOrigin = new Vector3(200f, 0f, 0f);

        public static readonly Pickup[] Pickups =
        {
            new Pickup("PickupLantern", LanternPrefab, 1000, "Pick up the lantern", "Lantern (barn)", VillageScene, HollowmereLayout.BarnLantern, "Lantern_Barn", "Lantern", 1, VillageRegion, VillageOrigin, false),
            new Pickup("PickupHerbs", ReedPrefab, 600, "Gather the marsh herbs", "Marsh Herbs (garden)", VillageScene, HollowmereLayout.GardenHerbs, "MarshHerbs_Garden", "MarshHerbs", 1, VillageRegion, VillageOrigin, false),
            new Pickup("PickupCoins", StonePrefab, 350, "Pick up the coins", "Coins (square)", VillageScene, HollowmereLayout.VillageCoins, "OldCoins_Village", "OldCoin", 3, VillageRegion, VillageOrigin, true),
            new Pickup("PickupClapper", BellPrefab, 300, "Lift the bell clapper", "Bell Clapper", MarshScene, HollowmereLayout.Clapper, "BellClapper_Marsh", "BellClapper", 1, MarshRegion, MarshOrigin, true),
            new Pickup("PickupOil", BucketPrefab, 450, "Pick up the oil flask", "Oil Flask (marsh)", MarshScene, HollowmereLayout.MarshOil, "OilFlask_Marsh", "OilFlask", 1, MarshRegion, MarshOrigin, false),
            new Pickup("PickupMarshHerbs", ReedPrefab, 600, "Gather the marsh herbs", "Marsh Herbs (marsh)", MarshScene, HollowmereLayout.MarshHerbs, "MarshHerbs_Marsh", "MarshHerbs", 2, MarshRegion, MarshOrigin, false),
        };

        /// <summary>The other interactables: definition, look, kind, prompts, condition ref, placed name, scene, where, yaw.</summary>
        private static readonly (string Name, string Prefab, int Scale, string Kind, string Initial, string[] Prompts, string Condition, string Placed, string Scene, Vector3 At, float Yaw)[] Fixtures =
        {
            ("ShrinePostWest", LanternPrefab, 1400, "Switch", "off", new[] { "off=Light the west shrine lantern", "on=The west lantern burns" }, "HasLantern", "Shrine Lantern West", MarshScene, HollowmereLayout.ShrineWest, 0f),
            ("ShrinePostMiddle", LanternPrefab, 1400, "Switch", "off", new[] { "off=Light the middle shrine lantern", "on=The middle lantern burns" }, "ShrinePost1Lit", "Shrine Lantern Middle", MarshScene, HollowmereLayout.ShrineMiddle, 0f),
            ("ShrinePostEast", LanternPrefab, 1400, "Switch", "off", new[] { "off=Light the east shrine lantern", "on=The east lantern burns" }, "ShrinePost2Lit", "Shrine Lantern East", MarshScene, HollowmereLayout.ShrineEast, 0f),
            ("OldPunt", SignpostPrefab, 700, "Examinable", "idle", new[] { "idle=Use the old punt" }, string.Empty, "Old Punt", MarshScene, HollowmereLayout.Punt, 45f),
            ("SinkholeGlint", StonePrefab, 500, "Examinable", "idle", new[] { "idle=Reach into the glinting water" }, string.Empty, "Glinting Sinkhole", MarshScene, HollowmereLayout.Sinkhole, 0f),
            ("DrownedSatchel", CratePrefab, 450, "Examinable", "idle", new[] { "idle=Search the drowned satchel" }, string.Empty, "Drowned Satchel", MarshScene, HollowmereLayout.Satchel, 20f),
            ("TinkersBench", CratePrefab, 900, "Examinable", "idle", new[] { "idle=Use the tinker's bench" }, string.Empty, "Tinker's Bench", VillageScene, HollowmereLayout.TinkersBench, 90f),
        };

        public static void Author(StudioAuthor a)
        {
            Definitions(a);
            Npcs(a);
            Place(a);
            WorldItems(a);
            Graphs(a, false);
            Rules(a);
            Director(a);
            Boot(a);
        }

        // ------------------------------------------------------------------ definitions

        private static void Definitions(StudioAuthor a)
        {
            a.Step("world.entity-looks", "Create the entity looks of the pickups and story interactables", () =>
            {
                var ops = new List<Operation>();
                foreach (Pickup p in Pickups)
                {
                    ops.Add(EntityLook(p.Definition, p.Prefab, p.ScaleMilli));
                }

                foreach (var f in Fixtures)
                {
                    ops.Add(EntityLook(f.Name, f.Prefab, f.Scale));
                }

                return ops;
            });

            a.Step("world.interactables", "Create the pickup and story interactables (shrine lanterns, punt, sinkhole, satchel, tinker's bench)", () =>
            {
                var ops = new List<Operation>();
                foreach (Pickup p in Pickups)
                {
                    ops.Add(InteractableOp(p.Definition, "Examinable", "idle", new[] { "idle=" + p.Prompt }, string.Empty, 2.2f));
                }

                foreach (var f in Fixtures)
                {
                    ops.Add(InteractableOp(f.Name, f.Kind, f.Initial, f.Prompts, f.Condition, 2.6f));
                }

                return ops;
            });

            a.Step("world.interaction-roster", "List the new interactables on the interaction roster", () =>
            {
                AuthoringRef roster = a.Ref(InteractionRoster);
                var ops = new List<Operation>();
                int n = 0;
                foreach (Pickup p in Pickups)
                {
                    ops.Add(StudioAuthor.Assign("r" + n++, roster, "interactables", Interactable(p.Definition), true));
                }

                foreach (var f in Fixtures)
                {
                    ops.Add(StudioAuthor.Assign("r" + n++, roster, "interactables", Interactable(f.Name), true));
                }

                return ops;
            });
        }

        private static Operation EntityLook(string name, string prefab, int scale) =>
            StudioAuthor.Create("e_" + name, "entity.definition", Interactables + "/" + name + "Entity.asset", name + "Entity", StudioAuthor.Id("entity." + name), new JObject
            {
                ["prefab"] = prefab,
                ["defaultScaleMilli"] = scale,
                ["startsVisible"] = true,
                ["startsAlive"] = true,
                ["overridableFields"] = StudioAuthor.Strings("scaleMilli", "visible", "alive", "tint"),
                ["interactionKind"] = string.Empty,
            });

        private static Operation InteractableOp(string name, string kind, string initial, string[] prompts, string condition, float range)
        {
            var list = new JArray();
            foreach (string prompt in prompts)
            {
                int eq = prompt.IndexOf('=');
                list.Add(new JObject { ["state"] = prompt.Substring(0, eq), ["text"] = prompt.Substring(eq + 1) });
            }

            return StudioAuthor.Create("x_" + name, "interaction.interactable", Interactable(name), name, StudioAuthor.Id("interactable." + name), new JObject
            {
                ["entity"] = Interactables + "/" + name + "Entity.asset",
                ["kind"] = kind,
                ["initialState"] = initial,
                ["prompts"] = list,
                ["conditionRef"] = condition,
                ["actionRef"] = string.Empty,
                ["maxUses"] = 0,
                ["cooldownSeconds"] = 0.5f,
                ["range"] = range,
                ["focusPriority"] = 2,
                ["focusHeight"] = 0.8f,
                ["cue"] = "sfx.ui.click",
            });
        }

        // ------------------------------------------------------------------ NPCs

        private static void Npcs(StudioAuthor a)
        {
            a.Step("world.bram-definition", "Create Bram the innkeeper: entity, idle behaviour, NPC definition", () => new List<Operation>
            {
                StudioAuthor.Create("entity", "entity.definition", Npc("BramEntity"), "BramEntity", StudioAuthor.Id("entity.Bram"), new JObject
                {
                    ["prefab"] = NpcPrefab,
                    ["defaultScaleMilli"] = 1000,
                    ["startsVisible"] = true,
                    ["startsAlive"] = true,
                    ["overridableFields"] = StudioAuthor.Strings("scaleMilli", "visible", "tint"),
                }),
                StudioAuthor.Create("behaviour", "npc.behaviour", Npc("BramBehaviour"), "BramBehaviour", StudioAuthor.Id("behaviour.Bram"), new JObject
                {
                    ["kind"] = "Idle",
                    ["waitSeconds"] = 1.5f,
                }),
            });

            a.Step("world.bram-npc", "Bram's NPC definition (answers to dialogue.bram)", () => new List<Operation>
            {
                StudioAuthor.Create("npc", "npc.definition", Npc("Bram"), "Bram", StudioAuthor.Id("npc.Bram"), new JObject
                {
                    ["entity"] = Npc("BramEntity"),
                    ["displayName"] = "Bram",
                    ["speed"] = 1.6f,
                    ["converseSeconds"] = 4f,
                    ["arriveRadius"] = 0.05f,
                    ["voiceId"] = "voice.bram",
                    ["dialogueGraph"] = "dialogue.bram",
                    ["behaviour"] = Npc("BramBehaviour"),
                    ["focusPriority"] = 1,
                    ["talkRange"] = 2.5f,
                }),
            });

            a.Step("world.npc-roster", "List Bram on the NPC roster", () => new List<Operation>
            {
                StudioAuthor.Assign("roster", a.Ref(NpcRoster), "npcs", Npc("Bram"), true),
            });

            a.Step("world.odd-behaviour", "Odd stands at the jetty (idle behaviour without patrol points)", () => new List<Operation>
            {
                StudioAuthor.Set("odd", a.Ref(Npc("OddBehaviour")), new JObject { ["kind"] = "Idle", ["patrolPoints"] = new JArray() }),
                StudioAuthor.Set("hale", a.Ref(Npc("HaleBehaviour")), new JObject { ["kind"] = "Idle", ["patrolPoints"] = new JArray() }),
            });
        }

        // ------------------------------------------------------------------ placement

        private static void Place(StudioAuthor a)
        {
            // Odd leaves the village for the jetty: his village placement is removed, a new one stands in the marsh.
            if (!a.IsApplied("world.odd-leaves-village"))
            {
                Activate(VillageScene);
            }

            a.Step("world.odd-leaves-village", "Odd the ferryman leaves the village square", () =>
            {
                AuthoredEntity? odd = FindById(VillageScene, OddId);
                return odd == null ? new List<Operation>() : new List<Operation> { new Operation("delete", "delete", a.Ref(odd.gameObject), new JObject()) };
            });

            if (!a.IsApplied("world.marsh-npcs"))
            {
                Activate(MarshScene);
            }

            a.Step("world.marsh-npcs", "Odd waits at the marsh jetty; Warden Hale guards the causeway gate", () =>
            {
                var ops = new List<Operation>
                {
                    StudioAuthor.Call("odd", "npc.addAt", a.Ref(Npc("Odd")), new JObject { ["location"] = StudioAuthor.V3(HollowmereLayout.Odd), ["yaw"] = 45f, ["name"] = "Odd" }),
                };
                AuthoredEntity? hale = FindById(MarshScene, HaleId);
                if (hale != null)
                {
                    ops.Add(new Operation("hale", "move", a.Ref(hale.gameObject), new JObject { ["position"] = StudioAuthor.V3(HollowmereLayout.Hale) }));
                }

                return ops;
            });

            if (!a.IsApplied("world.village-npcs"))
            {
                Activate(VillageScene);
            }

            a.Step("world.village-npcs", "Bram stands behind the inn's counter", () => new List<Operation>
            {
                StudioAuthor.Call("bram", "npc.addAt", a.Ref(Npc("Bram")), new JObject { ["location"] = StudioAuthor.V3(HollowmereLayout.Bram), ["yaw"] = 270f, ["name"] = "Bram" }),
            });

            foreach (string scene in new[] { VillageScene, MarshScene })
            {
                string step = "world.place-" + (scene == VillageScene ? "village" : "marsh");
                if (!a.IsApplied(step))
                {
                    Activate(scene);
                }

                a.Step(step, "Place the pickups and story interactables of " + System.IO.Path.GetFileNameWithoutExtension(scene), () =>
                {
                    var ops = new List<Operation>();
                    int n = 0;
                    foreach (Pickup p in Pickups)
                    {
                        if (p.Scene == scene)
                        {
                            ops.Add(StudioAuthor.Call("p" + n++, "interaction.addExaminable", a.Ref(Interactable(p.Definition)),
                                new JObject { ["location"] = StudioAuthor.V3(p.At), ["yaw"] = 0f, ["name"] = p.Placed }));
                        }
                    }

                    foreach (var f in Fixtures)
                    {
                        if (f.Scene == scene)
                        {
                            ops.Add(StudioAuthor.Call("f" + n++, "interaction.addExaminable", a.Ref(Interactable(f.Name)),
                                new JObject { ["location"] = StudioAuthor.V3(f.At), ["yaw"] = f.Yaw, ["name"] = f.Placed }));
                        }
                    }

                    return ops;
                });
            }

            Enclosures(a);
        }

        /// <summary>
        /// Walls around the box of each walk-in portal into the belfry (village and marsh side): the belfry is an island
        /// reached by boat, while the return portals (and their arrival points in front of these boxes) stay open.
        /// </summary>
        private static void Enclosures(StudioAuthor a)
        {
            foreach ((string scene, string region, string label) in new[] { (VillageScene, VillageRegion, "village"), (MarshScene, MarshRegion, "marsh") })
            {
                a.Step("world.belfry-enclosure-" + label, "Close the collapsed causeway to the belfry on the " + label + " side (ruin walls around the walk-in portal)", () =>
                {
                    RegionPortal? portal = FindPortal(scene, "Portal to Drowned Belfry");
                    if (portal == null)
                    {
                        throw new InvalidOperationException("P3.1: no 'Portal to Drowned Belfry' in " + scene);
                    }

                    Transform t = portal.transform;
                    float yaw = t.eulerAngles.y;
                    var ops = new List<Operation>();
                    (Vector3 Local, float Yaw, Vector3 Size, string Name)[] walls =
                    {
                        (new Vector3(0f, 0f, 1.4f), 0f, new Vector3(5.2f, 3.2f, 0.8f), "Collapsed Arch Front"),
                        (new Vector3(0f, 0f, -1.4f), 0f, new Vector3(5.2f, 3.2f, 0.8f), "Collapsed Arch Back"),
                        (new Vector3(2.4f, 0f, 0f), 90f, new Vector3(3.6f, 3.2f, 0.8f), "Collapsed Arch Right"),
                        (new Vector3(-2.4f, 0f, 0f), 90f, new Vector3(3.6f, 3.2f, 0.8f), "Collapsed Arch Left"),
                    };
                    for (int i = 0; i < walls.Length; i++)
                    {
                        Vector3 world = t.TransformPoint(Vector3.zero) + Quaternion.Euler(0f, yaw, 0f) * walls[i].Local;
                        world.y = 0f;
                        ops.Add(StudioAuthor.Call("w" + i, "hollowmere.addScenery", a.Ref(region), new JObject
                        {
                            ["kind"] = "ruinWall",
                            ["position"] = StudioAuthor.V3(world),
                            ["yaw"] = yaw + walls[i].Yaw,
                            ["size"] = StudioAuthor.V3(walls[i].Size),
                            ["collider"] = true,
                            ["name"] = walls[i].Name,
                        }));
                    }

                    return ops;
                });
            }
        }

        private static void WorldItems(StudioAuthor a)
        {
            a.Step("world.world-items", "Create the world items behind the new pickups (lantern in the barn, herbs, oil flask)", () =>
            {
                var ops = new List<Operation>();
                foreach (Pickup p in Pickups)
                {
                    if (p.ExistingWorldItem)
                    {
                        continue;
                    }

                    ops.Add(StudioAuthor.Create("w_" + p.WorldItem, "inventory.worldItem", Item(p.WorldItem), p.WorldItem, StudioAuthor.Id("worldItem." + p.WorldItem), new JObject
                    {
                        ["item"] = Item(p.ItemName),
                        ["count"] = p.Count,
                        ["region"] = p.Region,
                        ["position"] = StudioAuthor.V3(p.At - p.RegionOrigin),
                        ["entityId"] = EntityIdOf(p.Scene, p.Placed),
                    }));
                }

                return ops;
            });

            var fresh = new List<string>();
            foreach (Pickup p in Pickups)
            {
                if (!p.ExistingWorldItem)
                {
                    fresh.Add(Item(p.WorldItem));
                }
            }

            RegisterContent(a, "world.register-world-items", fresh);

            a.Step("world.existing-world-items", "The village coins and the marsh clapper now show through their pickups", () =>
            {
                var ops = new List<Operation>();
                foreach (Pickup p in Pickups)
                {
                    if (p.ExistingWorldItem)
                    {
                        ops.Add(StudioAuthor.Set(p.WorldItem, a.Ref(Item(p.WorldItem)), new JObject { ["entityId"] = EntityIdOf(p.Scene, p.Placed) }));
                    }
                }

                return ops;
            });
        }

        // ------------------------------------------------------------------ graphs

        /// <summary>Writes the six graphs whole (step id differs when voices are included, so the media pass re-writes them).</summary>
        public static void Graphs(StudioAuthor a, bool voices, int voicedLines = 0)
        {
            if (!voices)
            {
                a.Step("world.bram-graph", "Create Bram's conversation asset (answers to dialogue.bram)", () => new List<Operation>
                {
                    StudioAuthor.Create("graph", "dialogue.graph", Graph("Bram"), "Bram", StudioAuthor.Id("graph.Bram"), new JObject
                    {
                        ["speaker"] = "Bram",
                        ["npcGraphRef"] = "dialogue.bram",
                    }),
                });
                RegisterContent(a, "world.register-bram-graph", new List<string> { Graph("Bram") });
            }

            foreach (string name in HollowmereDialogues.All)
            {
                string step = (voices ? "media.voices-" + voicedLines + "." : "story.graph.") + name;
                a.Step(step, (voices ? "Attach the generated voice lines of " : "Write the conversation of ") + name, () =>
                {
                    GraphBuilder graph = HollowmereDialogues.Build(name, voices);
                    return new List<Operation> { StudioAuthor.Set("graph", a.Ref(Graph(name)), graph.Fields(SpeakerEntity(name))) };
                });
            }
        }

        private static string SpeakerEntity(string graph)
        {
            switch (graph)
            {
                case "Maren": return "da525107-c2c1-4626-ac08-5058a1e01c06";
                case "Hale": return HaleId;
                case "Pip": return "508f7d78-7b44-42e8-8028-be4b2c9ee4b0";
                case "BelfryEcho": return "a91b8bb5-cbd4-4872-871b-118e53cc465d";
                case "Odd": return EntityIdOf(MarshScene, "Odd");
                default: return EntityIdOf(VillageScene, "Bram");
            }
        }

        // ------------------------------------------------------------------ rules

        private static JObject RuleFields(string trigger, string? subject, string entityId, JObject[] conditions, JObject[] actions, bool once, int priority = 0, int value = 0, bool any = true) => new JObject
        {
            ["trigger"] = trigger,
            ["triggerSubject"] = subject,
            ["triggerEntityId"] = entityId,
            ["triggerValue"] = value,
            ["matchAnyValue"] = any,
            ["conditions"] = new JArray(conditions),
            ["actions"] = new JArray(actions),
            ["once"] = once,
            ["cooldownMs"] = 0,
            ["maxFires"] = 0,
            ["priority"] = priority,
        };

        private static List<(string Name, JObject Fields)> RuleSet()
        {
            string Entity(string scene, string name) => EntityIdOf(scene, name);
            var rules = new List<(string, JObject)>();
            foreach (Pickup p in Pickups)
            {
                string id = Entity(p.Scene, p.Placed);
                rules.Add(("Take_" + p.WorldItem, RuleFields("Interacted", null, id, Array.Empty<JObject>(), new[]
                {
                    Act("Pickup", Item(p.WorldItem)),
                    Act("Despawn", null, 1, string.Empty, id),
                    Audio("sfx.pickup"),
                }, true)));
            }

            string west = Entity(MarshScene, "Shrine Lantern West");
            string middle = Entity(MarshScene, "Shrine Lantern Middle");
            string east = Entity(MarshScene, "Shrine Lantern East");
            rules.Add(("ShrinePost1", RuleFields("Interacted", null, west, Array.Empty<JObject>(), new[] { SetFact("shrine_post_1"), Audio("sfx.shrine.light") }, true)));
            rules.Add(("ShrinePost2", RuleFields("Interacted", null, middle, Array.Empty<JObject>(), new[] { SetFact("shrine_post_2"), Audio("sfx.shrine.light") }, true)));
            rules.Add(("ShrinePost3", RuleFields("Interacted", null, east, Array.Empty<JObject>(), new[] { SetFact("shrine_post_3"), Audio("sfx.shrine.light") }, true)));
            rules.Add(("ShrineLit", RuleFields("FactSet", Fact("shrine_post_3"), string.Empty, new[] { FactAtLeast("shrine_post_1"), FactAtLeast("shrine_post_2") },
                new[] { SetFact("shrine_lit"), Message("The sunken shrine flares green-gold. Somewhere across the water, something stirs."), Audio("stinger.bell") }, true)));

            string punt = Entity(MarshScene, "Old Punt");
            rules.Add(("PuntCross", RuleFields("Interacted", null, punt, new[] { FactAtLeast("punt_repaired") },
                new[] { Message("You pole the old punt across the black water."), Act("Travel", BelfryRegion) }, false, 0)));
            rules.Add(("PuntRepair", RuleFields("Interacted", null, punt, new[] { FactIs("punt_repaired", 0), Holds("OilFlask"), Holds("Lantern") },
                new[] { Act("Consume", Item("OilFlask"), 1), SetFact("punt_repaired"), Message("You seal the punt's seams with pitch from the oil flask. Use it again to push off.") }, true, 1)));
            rules.Add(("PuntLeaks", RuleFields("Interacted", null, punt, new[] { FactIs("punt_repaired", 0), HoldsNone("OilFlask") },
                new[] { Message("The old punt leaks. Pitch from an oil flask would seal it.") }, false, 2)));
            rules.Add(("PuntNeedsLight", RuleFields("Interacted", null, punt, new[] { FactIs("punt_repaired", 0), Holds("OilFlask"), HoldsNone("Lantern") },
                new[] { Message("Too dark to work on the punt without a lantern.") }, false, 3)));

            string sinkhole = Entity(MarshScene, "Glinting Sinkhole");
            rules.Add(("SinkholeTakesLantern", RuleFields("Interacted", null, sinkhole, new[] { Holds("Lantern") },
                new[] { Act("Consume", Item("Lantern"), 1), SetFact("lantern_lost"), Act("Grant", Item("OldCoin"), 2), Message("Coins! But the marsh snatches your lantern into the black water."), Audio("sfx.splash") }, false)));
            rules.Add(("SinkholeDark", RuleFields("Interacted", null, sinkhole, new[] { HoldsNone("Lantern") },
                new[] { Message("Too dark to see what glints down there.") }, false)));

            string satchel = Entity(MarshScene, "Drowned Satchel");
            rules.Add(("SearchSatchel", RuleFields("Interacted", null, satchel, Array.Empty<JObject>(),
                new[] { AddFact("search_satchel"), Act("Despawn", null, 1, string.Empty, satchel), Message("You empty the drowned satchel.") }, true)));

            string bench = Entity(VillageScene, "Tinker's Bench");
            rules.Add(("CraftLantern", RuleFields("Interacted", null, bench, new[] { Holds("OilFlask"), Holds("MarshHerbs") },
                new[] { Act("Consume", Item("OilFlask"), 1), Act("Consume", Item("MarshHerbs"), 1), Act("Grant", Item("Lantern"), 1), Message("You twist a wick from dried herbs and fill the lamp with oil: a working lantern.") }, false)));
            rules.Add(("BenchNeedsOil", RuleFields("Interacted", null, bench, new[] { HoldsNone("OilFlask") },
                new[] { Message("A lantern needs an oil flask and dried marsh herbs for a wick.") }, false)));
            rules.Add(("BenchNeedsHerbs", RuleFields("Interacted", null, bench, new[] { Holds("OilFlask"), HoldsNone("MarshHerbs") },
                new[] { Message("You have oil, but no dried herbs for a wick.") }, false)));

            rules.Add(("HerbsRestore", RuleFields("ItemConsumed", Item("MarshHerbs"), string.Empty, Array.Empty<JObject>(),
                new[] { AddFact("herbs_used"), Message("The bitter herbs ease your aching legs.") }, false)));
            rules.Add(("BellNoClapper", RuleFields("Interacted", null, BellId, new[] { HoldsNone("BellClapper") },
                new[] { Message("The bell has no clapper. It cannot ring.") }, false)));
            rules.Add(("EndingRing", RuleFields("FactSet", Fact("bell_rung"), string.Empty, new[] { FactIs("shrine_lit", 0) },
                new[] { SetFact("ending_b"), Message("The Drowned Bell tolls across Blackmere.") }, true)));
            rules.Add(("EndingFree", RuleFields("FactSet", Fact("bell_rung"), string.Empty, new[] { FactAtLeast("shrine_lit") },
                new[] { SetFact("ending_c"), SetFact("belfry_echo_freed") }, true)));
            rules.Add(("LanternLost", RuleFields("FactSet", Fact("lantern_lost"), string.Empty, new[] { HoldsNone("Lantern") },
                new[] { Message("Without a light, the marsh closes in around you.") }, false)));
            return rules;
        }

        private static void Rules(StudioAuthor a)
        {
            a.Step("story.rules", "Create the story rules (pickups, shrine puzzle, punt, sinkhole, satchel, crafting, herbs, endings)", () =>
            {
                var ops = new List<Operation>();
                foreach (var rule in RuleSet())
                {
                    ops.Add(StudioAuthor.Create("rule_" + rule.Name, "logic.rule", Rule(rule.Name), rule.Name, StudioAuthor.Id("rule." + rule.Name), rule.Fields));
                }

                return ops;
            });

            var paths = new List<string>();
            foreach (var rule in RuleSet())
            {
                paths.Add(Rule(rule.Name));
            }

            RegisterContent(a, "story.register-rules", paths);

            a.Step("story.retire-p1_4-rules", "Retire the P1.4 rules the new story replaces (gate key, Odd's stall, the return to Maren)", () => new List<Operation>
            {
                StudioAuthor.Set("gatekey", a.Ref(OldRule("GateKeyOpensGate")), new JObject { ["maxFires"] = 0, ["once"] = true, ["trigger"] = "Manual" }),
                StudioAuthor.Set("oddpaid", a.Ref(OldRule("OddPaidOnTrade")), new JObject { ["once"] = true, ["trigger"] = "Manual" }),
                StudioAuthor.Set("maren", a.Ref(OldRule("ReturnToMaren")), new JObject
                {
                    ["actions"] = new JArray { Audio("sting.quest.complete") },
                }),
            });
        }

        // ------------------------------------------------------------------ director and boot

        private static void Director(StudioAuthor a)
        {
            a.Step("game.director", "Create the Hollowmere director: endings, music, the shrine's ambience layer, interim fact requests", () => new List<Operation>
            {
                StudioAuthor.Create("director", "hollowmere.director", HollowmerePaths.Director, "HollowmereDirector", StudioAuthor.Id("director"), new JObject
                {
                    ["quest"] = "DrownedBell",
                    ["endings"] = new JArray
                    {
                        Ending(1, "Ending A: The Silence", "You let the Drowned Bell sleep. The Echo fades into the stones and Blackmere keeps its silence. Thornwick never learns how close the marsh came to waking.", "music.ending"),
                        Ending(2, "Ending B: The Toll", "The bell tolls once across the black water. Thornwick sleeps soundly for the first time since the flood, though in the belfry the Echo stays, bound to its bell.", "music.ending"),
                        Ending(3, "Ending C: The Freed Echo", "With the shrine alight, the toll carries the Echo out over the water like a lantern on the tide. The marsh is only a marsh again, and Maren lights a candle for the drowned.", "music.ending"),
                        Ending(-1, "The Marsh Keeps What It Takes", "Your last light sinks into Blackmere. In the dark the reeds close behind you, and the bell stays silent.", "music.tense"),
                    },
                    ["music"] = new JArray
                    {
                        new JObject { ["fact"] = "gate_open", ["value"] = 1, ["musicState"] = "music.explore" },
                        new JObject { ["fact"] = "lantern_lost", ["value"] = 1, ["musicState"] = "music.tense" },
                        new JObject { ["fact"] = "bell_rung", ["value"] = 1, ["musicState"] = "music.ending" },
                    },
                    ["layers"] = new JArray
                    {
                        new JObject { ["fact"] = "shrine_lit", ["value"] = 1, ["regionId"] = "7f21b99a-8e74-412f-a1da-5f7d60843080", ["clipId"] = "ambience.shrine", ["volume"] = 0.55f },
                    },
                    ["requests"] = new JArray
                    {
                        Request("buy_lantern", "Buy", "InnStock", "Lantern", 1, null),
                        Request("buy_oil", "Buy", "InnStock", "OilFlask", 1, null),
                        Request("buy_herbs", "Buy", "InnStock", "MarshHerbs", 1, null),
                        Request("herbs_used", "RestoreStamina", string.Empty, string.Empty, 400, null),
                        Request("search_satchel", "Loot", string.Empty, string.Empty, 1, Item("MarshLoot")),
                    },
                }),
            });
        }

        private static JObject Ending(int branch, string title, string body, string music) =>
            new JObject { ["branch"] = branch, ["title"] = title, ["body"] = body, ["musicState"] = music };

        private static JObject Request(string fact, string kind, string vendor, string item, int count, string? loot) => new JObject
        {
            ["fact"] = fact,
            ["kind"] = kind,
            ["vendor"] = vendor,
            ["item"] = item,
            ["count"] = count,
            ["lootTable"] = loot,
        };

        private static void Boot(StudioAuthor a)
        {
            if (!a.IsApplied("game.boot-component"))
            {
                Activate(BootScene);
            }

            a.Step("game.boot-component", "Add HollowmereGame (saves, director, command line) next to GameBoot in Boot.unity", () =>
            {
                Hollowmere.Boot.GameBoot? boot = UnityEngine.Object.FindAnyObjectByType<Hollowmere.Boot.GameBoot>();
                if (boot == null)
                {
                    throw new InvalidOperationException("P3.1: Boot.unity has no GameBoot");
                }

                if (boot.GetComponent<Hollowmere.Game.HollowmereGame>() != null)
                {
                    return new List<Operation>();
                }

                return new List<Operation>
                {
                    new Operation("game", "addComponent", a.Ref(boot.gameObject), new JObject
                    {
                        ["type"] = "hollowmere.game",
                        ["fields"] = new JObject { ["director"] = HollowmerePaths.Director, ["gameId"] = "hollowmere" },
                        ["authoringId"] = StudioAuthor.Id("boot.game"),
                    }),
                };
            });
        }

        // ------------------------------------------------------------------ scenes

        public static void Activate(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            SceneManager.SetActiveScene(scene);
        }

        public static AuthoredEntity? FindById(string scenePath, string authoringId)
        {
            foreach (AuthoredEntity entity in EntitiesIn(scenePath))
            {
                if (entity.AuthoringId == authoringId)
                {
                    return entity;
                }
            }

            return null;
        }

        public static AuthoredEntity? FindByName(string scenePath, string name)
        {
            foreach (AuthoredEntity entity in EntitiesIn(scenePath))
            {
                if (entity.gameObject.name == name)
                {
                    return entity;
                }
            }

            return null;
        }

        /// <summary>The authoring id of a placed entity by name (throws when it is not placed).</summary>
        public static string EntityIdOf(string scenePath, string name)
        {
            AuthoredEntity? entity = FindByName(scenePath, name);
            if (entity == null)
            {
                throw new InvalidOperationException("P3.1: no placed entity '" + name + "' in " + scenePath);
            }

            return entity.AuthoringId;
        }

        private static IEnumerable<AuthoredEntity> EntitiesIn(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (AuthoredEntity entity in root.GetComponentsInChildren<AuthoredEntity>(true))
                {
                    yield return entity;
                }
            }
        }

        public static RegionPortal? FindPortal(string scenePath, string name)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (RegionPortal portal in root.GetComponentsInChildren<RegionPortal>(true))
                {
                    if (portal.gameObject.name == name)
                    {
                        return portal;
                    }
                }
            }

            return null;
        }
    }
}
