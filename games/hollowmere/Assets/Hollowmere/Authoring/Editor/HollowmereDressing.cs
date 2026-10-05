// Hollowmere - the dressing half of AuthorAll (P3.1): procedural textures (imported as retained artifacts), URP
// materials, procedural scenery meshes per region (houses, the inn, the healer's cottage, trees, fences, the marsh's
// pools, reeds, dead willow, shrine, jetty, punt and the causeway fence; the belfry's tower, bell frame and ruins),
// region atmospheres, lights, the boot presentation volume, NPC animators, NPC tints and textured prop prefabs.
// The P1 primitive blocks that stood in for buildings are hidden (kept in the scene) once their meshes stand.
#nullable enable
using System.Collections.Generic;
using Hollowmere.Authoring.Tools;
using GameCore.Gameplay.Entities;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static Hollowmere.Authoring.HollowmerePaths;

namespace Hollowmere.Authoring
{
    /// <summary>Textures, materials, scenery, atmosphere, lights and looks.</summary>
    public static class HollowmereDressing
    {
        public const int TextureSize = 256;

        /// <summary>Material name, texture kind (empty = none), colour, smoothness, metallic, tiling, emission, transparent.</summary>
        private static readonly (string Name, string Texture, Color Base, float Smooth, float Metal, Vector2 Tiling, Color Emission, bool Transparent)[] MaterialSpecs =
        {
            ("GroundVillage", "grass", Color.white, 0.1f, 0f, new Vector2(20f, 20f), Color.black, false),
            ("GroundMarsh", "mud", Color.white, 0.35f, 0f, new Vector2(20f, 20f), Color.black, false),
            ("GroundBelfry", "mossStone", Color.white, 0.2f, 0f, new Vector2(16f, 16f), Color.black, false),
            ("Plaster", "plaster", Color.white, 0.1f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("Thatch", "thatch", Color.white, 0.05f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("Timber", "timber", Color.white, 0.15f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("Stone", "stone", Color.white, 0.15f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("MossStone", "mossStone", Color.white, 0.2f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("Planks", "planks", Color.white, 0.2f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("Cobble", "cobble", Color.white, 0.25f, 0f, new Vector2(0.4f, 0.4f), Color.black, false),
            ("Bark", "bark", Color.white, 0.1f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("Leaves", "leaves", Color.white, 0.1f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("Reed", "reed", Color.white, 0.1f, 0f, new Vector2(0.5f, 0.5f), Color.black, false),
            ("Cloth", "cloth", Color.white, 0.1f, 0f, new Vector2(1f, 1f), Color.black, false),
            ("Bronze", "bronze", Color.white, 0.6f, 0.8f, new Vector2(1f, 1f), Color.black, false),
            ("Water", "water", new Color(0.35f, 0.45f, 0.4f, 0.78f), 0.92f, 0f, new Vector2(0.15f, 0.15f), Color.black, true),
            ("BlackWater", "water", new Color(0.12f, 0.16f, 0.15f, 0.88f), 0.95f, 0f, new Vector2(0.1f, 0.1f), Color.black, true),
            ("LanternGlow", "glow", Color.white, 0.5f, 0f, new Vector2(1f, 1f), new Color(1.6f, 1.1f, 0.5f), false),
            ("ShrineGlow", "glow", new Color(0.7f, 1f, 0.8f), 0.5f, 0f, new Vector2(1f, 1f), new Color(0.3f, 1.2f, 0.6f), false),
        };

        public static void Author(StudioAuthor a)
        {
            Textures(a);
            Materials(a);
            Village(a);
            Marsh(a);
            Belfry(a);
            Atmospheres(a);
            Looks(a);
            Boot(a);
        }

        // ------------------------------------------------------------------ textures and materials

        private static void Textures(StudioAuthor a)
        {
            var kinds = new List<string>();
            foreach (var spec in MaterialSpecs)
            {
                if (spec.Texture.Length > 0 && !kinds.Contains(spec.Texture))
                {
                    kinds.Add(spec.Texture);
                }
            }

            var artifacts = new List<ArtifactRef>();
            a.Step("dress.textures", "Import " + kinds.Count + " procedural tiling textures (labelled procedural in the media manifest)", () =>
            {
                var ops = new List<Operation>();
                for (int i = 0; i < kinds.Count; i++)
                {
                    byte[] png = ProceduralTextures.Png(kinds[i], TextureSize, 1000 + i);
                    ArtifactRef artifact = a.Retain(png, "image/png", kinds[i] + ".png", "procedural.texture", "texture");
                    artifacts.Add(artifact);
                    HollowmereMedia.RecordProcedural(Texture(kinds[i]), "procedural.texture", "ProceduralTextures." + kinds[i] + " " + TextureSize + "px seed " + (1000 + i), png);
                    ops.Add(StudioAuthor.Import("t" + i, Texture(kinds[i]), artifact));
                }

                return ops;
            }, artifacts);
        }

        private static void Materials(StudioAuthor a)
        {
            a.Step("dress.materials", "Create " + MaterialSpecs.Length + " URP Lit materials over the procedural textures", () =>
            {
                var ops = new List<Operation>();
                foreach (var spec in MaterialSpecs)
                {
                    var args = new JObject
                    {
                        ["path"] = Material(spec.Name),
                        ["baseColor"] = StudioAuthor.Color(spec.Base),
                        ["smoothness"] = spec.Smooth,
                        ["metallic"] = spec.Metal,
                        ["tiling"] = StudioAuthor.V2(spec.Tiling.x, spec.Tiling.y),
                        ["emission"] = StudioAuthor.Color(spec.Emission),
                        ["transparent"] = spec.Transparent,
                    };
                    if (spec.Texture.Length > 0)
                    {
                        args["baseMap"] = Texture(spec.Texture);
                    }

                    ops.Add(StudioAuthor.Call("m_" + spec.Name, "hollowmere.createMaterial", null, args));
                }

                return ops;
            });
        }

        // ------------------------------------------------------------------ scenery

        private static JObject Piece(string kind, Vector3 at, float yaw, Vector3 size, string name, bool collider, params string[] materials)
        {
            var mats = new JArray();
            foreach (string m in materials)
            {
                mats.Add(Material(m));
            }

            return new JObject
            {
                ["kind"] = kind,
                ["position"] = StudioAuthor.V3(at),
                ["yaw"] = yaw,
                ["size"] = StudioAuthor.V3(size),
                ["materials"] = mats,
                ["collider"] = collider,
                ["name"] = name,
            };
        }

        private static void Scenery(StudioAuthor a, string step, string intent, string region, List<JObject> pieces)
        {
            a.Step(step, intent, () =>
            {
                AuthoringRef target = a.Ref(region);
                var ops = new List<Operation>();
                for (int i = 0; i < pieces.Count; i++)
                {
                    ops.Add(StudioAuthor.Call("s" + i, "hollowmere.addScenery", target, pieces[i]));
                }

                return ops;
            });
        }

        private static void Skin(StudioAuthor a, string step, string intent, string region, (string Object, string[] Materials, bool Hide)[] skins)
        {
            a.Step(step, intent, () =>
            {
                AuthoringRef target = a.Ref(region);
                var ops = new List<Operation>();
                for (int i = 0; i < skins.Length; i++)
                {
                    var mats = new JArray();
                    foreach (string m in skins[i].Materials)
                    {
                        mats.Add(Material(m));
                    }

                    ops.Add(StudioAuthor.Call("k" + i, "hollowmere.skinSceneObject", target, new JObject
                    {
                        ["objectName"] = skins[i].Object,
                        ["materials"] = mats,
                        ["hide"] = skins[i].Hide,
                    }));
                }

                return ops;
            });
        }

        private static void Lights(StudioAuthor a, string step, string intent, string region, (string Name, Vector3 At, Color Color, float Intensity, float Range, bool Shadows)[] lights)
        {
            a.Step(step, intent, () =>
            {
                AuthoringRef target = a.Ref(region);
                var ops = new List<Operation>();
                for (int i = 0; i < lights.Length; i++)
                {
                    ops.Add(StudioAuthor.Call("l" + i, "hollowmere.addLight", target, new JObject
                    {
                        ["name"] = lights[i].Name,
                        ["position"] = StudioAuthor.V3(lights[i].At),
                        ["color"] = StudioAuthor.Color(lights[i].Color),
                        ["intensity"] = lights[i].Intensity,
                        ["range"] = lights[i].Range,
                        ["shadows"] = lights[i].Shadows,
                    }));
                }

                return ops;
            });
        }

        private static readonly Vector3 Default = Vector3.zero;
        private static readonly Color Warm = new Color(1f, 0.72f, 0.4f);

        private static void Village(StudioAuthor a)
        {
            HollowmereWorldContent.Activate(VillageScene);
            var p = new List<JObject>
            {
                Piece("house", new Vector3(14f, 0f, 12f), 180f, new Vector3(10f, 6f, 8f), "Barn", true, "Planks", "Thatch"),
                Piece("house", HollowmereLayout.HealerHouse, 0f, new Vector3(8f, 5f, 6f), "Healer's Cottage", true, "Plaster", "Thatch"),
                Piece("inn", HollowmereLayout.Inn, -90f, Default, "The Drowned Lantern Inn", true, "Timber", "Thatch"),
                Piece("well", new Vector3(0f, 0f, 0f), 0f, Default, "Village Well Stones", true, "Stone", "Planks"),
                Piece("hut", new Vector3(-20f, 0f, -14f), 30f, Default, "Weaver's Hut", true, "Plaster", "Thatch"),
                Piece("house", new Vector3(-24f, 0f, 2f), 90f, Default, "Miller's House", true, "Timber", "Thatch"),
                Piece("hut", new Vector3(22f, 0f, 20f), 200f, Default, "Shepherd's Hut", true, "Plaster", "Thatch"),
                Piece("causeway", new Vector3(18f, 0f, -1f), 90f, new Vector3(3f, 0.05f, 28f), "Gate Road", false, "Cobble"),
                Piece("causeway", new Vector3(2f, 0f, -7f), 0f, new Vector3(3f, 0.05f, 10f), "Square Road", false, "Cobble"),
                Piece("stall", new Vector3(-12.5f, 0f, 2f), 90f, Default, "Market Stall", true, "Planks", "Cloth"),
                Piece("barrel", new Vector3(12.2f, 0f, -9.6f), 0f, Default, "Inn Barrel 1", true, "Planks"),
                Piece("barrel", new Vector3(12.9f, 0f, -10.5f), 40f, Default, "Inn Barrel 2", true, "Planks"),
                Piece("fence", new Vector3(-14f, 0f, 17f), 0f, new Vector3(8f, 1.1f, 0.15f), "Garden Fence North", true, "Timber"),
                Piece("fence", new Vector3(-18f, 0f, 15f), 90f, new Vector3(4f, 1.1f, 0.15f), "Garden Fence West", true, "Timber"),
                Piece("lanternPost", new Vector3(9f, 0f, -3f), 0f, Default, "Road Lantern 1", true, "Timber", "LanternGlow"),
                Piece("lanternPost", new Vector3(21f, 0f, -3f), 0f, Default, "Road Lantern 2", true, "Timber", "LanternGlow"),
                Piece("lanternPost", new Vector3(31f, 0f, -2.6f), 0f, Default, "Gate Lantern South", true, "Timber", "LanternGlow"),
                Piece("lanternPost", new Vector3(31f, 0f, 2.6f), 0f, Default, "Gate Lantern North", true, "Timber", "LanternGlow"),
            };
            Vector3[] trees = { new Vector3(-27f, 0f, 20f), new Vector3(-31f, 0f, -8f), new Vector3(27f, 0f, 27f), new Vector3(26f, 0f, -28f), new Vector3(-12f, 0f, -27f), new Vector3(4f, 0f, 27f), new Vector3(-30f, 0f, 30f), new Vector3(33f, 0f, -16f) };
            for (int i = 0; i < trees.Length; i++)
            {
                p.Add(Piece("tree", trees[i], i * 47f, Default, "Oak " + (i + 1), true, "Bark", "Leaves"));
            }

            Vector3[] rocks = { new Vector3(-6f, 0f, 22f), new Vector3(18f, 0f, 30f), new Vector3(-26f, 0f, -24f) };
            for (int i = 0; i < rocks.Length; i++)
            {
                p.Add(Piece("rock", rocks[i], i * 70f, Default, "Field Rock " + (i + 1), true, "Stone"));
            }

            Scenery(a, "dress.village-scenery", "Dress Thornwick Village: barn, healer's cottage, the Drowned Lantern inn, huts, roads, trees, lanterns", VillageRegion, p);
            Skin(a, "dress.village-skin", "Texture the village ground and retire the primitive stand-in blocks", VillageRegion, new[]
            {
                ("Ground", new[] { "GroundVillage" }, false),
                ("Barn", new string[0], true),
                ("Cottage", new string[0], true),
                ("Well", new string[0], true),
            });
            Lights(a, "dress.village-lights", "Lantern light in the village (road, gate and inn windows)", VillageRegion, new[]
            {
                ("Road Lantern 1 Light", new Vector3(9f, 2.5f, -3f), Warm, 2.5f, 9f, false),
                ("Road Lantern 2 Light", new Vector3(21f, 2.5f, -3f), Warm, 2.5f, 9f, false),
                ("Gate Light", new Vector3(31f, 2.6f, 0f), Warm, 3f, 10f, false),
                ("Inn Window Light", new Vector3(11.5f, 2.2f, -14f), Warm, 3.5f, 9f, true),
                ("Cottage Window Light", new Vector3(-14f, 2f, 6.5f), Warm, 2f, 7f, false),
            });
        }

        private static void Marsh(StudioAuthor a)
        {
            HollowmereWorldContent.Activate(MarshScene);
            var p = new List<JObject>
            {
                Piece("water", new Vector3(206f, 0.03f, 6f), 0f, new Vector3(14f, 0f, 10f), "Sunken Pool", false, "Water"),
                Piece("water", new Vector3(228f, 0.02f, 30f), 0f, new Vector3(24f, 0f, 20f), "Black Water", false, "BlackWater"),
                Piece("water", new Vector3(216f, 0.03f, -12f), 0f, new Vector3(6f, 0f, 5f), "Sinkhole Water", false, "BlackWater"),
                Piece("water", new Vector3(180f, 0.03f, -22f), 0f, new Vector3(16f, 0f, 12f), "Marsh Pool South", false, "Water"),
                Piece("shrine", HollowmereLayout.Shrine, 180f, Default, "Sunken Shrine", true, "MossStone", "Stone"),
                Piece("deadTree", new Vector3(192f, 0f, 12f), 0f, new Vector3(2.4f, 7f, 2.4f), "Dead Willow", true, "Bark"),
                Piece("deadTree", new Vector3(178f, 0f, 18f), 60f, Default, "Dead Tree 2", true, "Bark"),
                Piece("deadTree", new Vector3(232f, 0f, -4f), 120f, Default, "Dead Tree 3", true, "Bark"),
                Piece("deadTree", new Vector3(196f, 0f, -28f), 200f, Default, "Dead Tree 4", true, "Bark"),
                Piece("jetty", HollowmereLayout.Jetty, 45f, Default, "Odd's Jetty", false, "Planks"),
                Piece("punt", new Vector3(229.6f, 0.05f, 26.8f), 45f, Default, "The Old Punt", false, "Planks"),
                Piece("hut", new Vector3(214f, 0f, 27f), 200f, Default, "Odd's Hut", true, "Timber", "Thatch"),
                Piece("causeway", new Vector3(178f, 0f, 2f), 90f, new Vector3(3f, 0.08f, 16f), "Causeway", false, "Planks"),
                Piece("lanternPost", new Vector3(171f, 0f, -0.8f), 0f, Default, "Gate Lantern", true, "Timber", "LanternGlow"),
                Piece("lanternPost", new Vector3(219.5f, 0f, 18f), 0f, Default, "Jetty Lantern", true, "Timber", "LanternGlow"),
            };
            Vector3[] reeds = { new Vector3(200f, 0f, 14f), new Vector3(212.5f, 0f, 12f), new Vector3(185f, 0f, -12f), new Vector3(213f, 0f, -9f), new Vector3(223f, 0f, 16f), new Vector3(178f, 0f, 7f), new Vector3(205f, 0f, -20f), new Vector3(230f, 0f, 14f), new Vector3(189f, 0f, -24f) };
            for (int i = 0; i < reeds.Length; i++)
            {
                p.Add(Piece("reeds", reeds[i], i * 33f, Default, "Reeds " + (i + 1), false, "Reed"));
            }

            Vector3[] rocks = { new Vector3(188f, 0f, 4f), new Vector3(219f, 0f, 6f), new Vector3(231f, 0f, 9f), new Vector3(214.5f, 0f, -14.5f) };
            for (int i = 0; i < rocks.Length; i++)
            {
                p.Add(Piece("rock", rocks[i], i * 51f, Default, "Mossy Rock " + (i + 1), true, "MossStone"));
            }

            // The causeway fence: the gate (closed until Warden Hale opens it) is the only way east, 13 six-metre panels.
            for (int i = 0; i < 7; i++)
            {
                p.Add(Piece("fence", new Vector3(169.5f, 0f, -2.5f - 6f * i), 90f, new Vector3(6f, 1.4f, 0.2f), "Causeway Fence South " + (i + 1), true, "Timber"));
            }

            for (int i = 0; i < 6; i++)
            {
                p.Add(Piece("fence", new Vector3(169.5f, 0f, 6.5f + 6f * i), 90f, new Vector3(6f, 1.4f, 0.2f), "Causeway Fence North " + (i + 1), true, "Timber"));
            }

            Scenery(a, "dress.marsh-scenery", "Dress Blackmere Marsh: pools, sunken shrine, dead willow, reeds, jetty, punt, Odd's hut, causeway fence", MarshRegion, p);
            Skin(a, "dress.marsh-skin", "Texture the marsh ground and retire the primitive pool and tree", MarshRegion, new[]
            {
                ("Ground", new[] { "GroundMarsh" }, false),
                ("Pool", new string[0], true),
                ("Dead Tree", new string[0], true),
            });
            Lights(a, "dress.marsh-lights", "Marsh light: the gate and jetty lanterns, the shrine's faint glow", MarshRegion, new[]
            {
                ("Gate Lantern Light", new Vector3(171f, 2.5f, -0.8f), Warm, 2.5f, 9f, false),
                ("Jetty Lantern Light", new Vector3(219.5f, 2.5f, 18f), Warm, 2.5f, 9f, false),
                ("Shrine Glow", HollowmereLayout.Shrine + new Vector3(0f, 2f, 0f), new Color(0.45f, 1f, 0.6f), 1.2f, 8f, false),
            });
        }

        private static void Belfry(StudioAuthor a)
        {
            HollowmereWorldContent.Activate(BelfryScene);
            var p = new List<JObject>
            {
                Piece("tower", new Vector3(100f, 0f, 208f), 0f, new Vector3(8f, 16f, 8f), "Drowned Belfry Tower", true, "MossStone", "Timber"),
                Piece("water", new Vector3(100f, 0.03f, 198f), 0f, new Vector3(20f, 0f, 14f), "Flooded Nave", false, "Water"),
                Piece("water", new Vector3(100f, 0.02f, 170f), 0f, new Vector3(40f, 0f, 14f), "Belfry Shallows", false, "BlackWater"),
                Piece("bellFrame", HollowmereLayout.Bell + new Vector3(0f, 0f, 0.6f), 0f, Default, "Bell Frame", true, "Timber"),
                Piece("jetty", new Vector3(100f, 0f, 179f), 0f, new Vector3(3f, 0.5f, 8f), "Belfry Landing", false, "Planks"),
                Piece("ruinWall", new Vector3(88f, 0f, 203f), 90f, Default, "Nave Wall West", true, "MossStone"),
                Piece("ruinWall", new Vector3(112f, 0f, 203f), 90f, Default, "Nave Wall East", true, "MossStone"),
                Piece("ruinWall", new Vector3(91f, 0f, 213f), 0f, new Vector3(5f, 4f, 0.8f), "Apse Wall West", true, "MossStone"),
                Piece("ruinWall", new Vector3(109f, 0f, 213f), 0f, new Vector3(5f, 4f, 0.8f), "Apse Wall East", true, "MossStone"),
                Piece("ruinWall", new Vector3(86f, 0f, 190f), 20f, new Vector3(5f, 2.4f, 0.8f), "Broken Wall South West", true, "Stone"),
                Piece("ruinWall", new Vector3(114f, 0f, 191f), -15f, new Vector3(5f, 2f, 0.8f), "Broken Wall South East", true, "Stone"),
                Piece("deadTree", new Vector3(80f, 0f, 182f), 30f, Default, "Drowned Tree 1", true, "Bark"),
                Piece("deadTree", new Vector3(121f, 0f, 221f), 160f, Default, "Drowned Tree 2", true, "Bark"),
                Piece("reeds", new Vector3(91f, 0f, 185f), 0f, Default, "Belfry Reeds 1", false, "Reed"),
                Piece("reeds", new Vector3(110f, 0f, 184f), 45f, Default, "Belfry Reeds 2", false, "Reed"),
                Piece("rock", new Vector3(118f, 0f, 200f), 10f, Default, "Fallen Stone 1", true, "MossStone"),
                Piece("rock", new Vector3(83f, 0f, 208f), 80f, Default, "Fallen Stone 2", true, "MossStone"),
            };
            Scenery(a, "dress.belfry-scenery", "Dress the Drowned Belfry: tower, bell frame, flooded nave, ruined walls, landing", BelfryRegion, p);
            Skin(a, "dress.belfry-skin", "Texture the belfry ground and retire the primitive tower and floor", BelfryRegion, new[]
            {
                ("Ground", new[] { "GroundBelfry" }, false),
                ("Tower", new string[0], true),
                ("Flooded Floor", new string[0], true),
            });
            Lights(a, "dress.belfry-lights", "Belfry light: moonlight on the bell, the lantern by the crates", BelfryRegion, new[]
            {
                ("Moonlight on the Bell", HollowmereLayout.Bell + new Vector3(0f, 7f, -2f), new Color(0.6f, 0.75f, 1f), 3f, 18f, true),
                ("Belfry Lantern Light", new Vector3(92f, 1.6f, 190f), Warm, 2.5f, 8f, false),
            });
        }

        private static void Atmospheres(StudioAuthor a)
        {
            a.Step("dress.atmosphere", "Region atmospheres: warm dusk in the village, green murk in the marsh, cold night at the belfry", () => new List<Operation>
            {
                Atmosphere(a, VillageRegion, new Color(0.64f, 0.6f, 0.55f), 0.010f, new Color(0.55f, 0.6f, 0.72f), new Color(0.46f, 0.42f, 0.38f), new Color(0.2f, 0.18f, 0.15f),
                    new Color(1f, 0.84f, 0.64f), 1.25f, new Vector3(32f, -35f, 0f), 0.15f, 6f, 8f, 0.45f, 0.22f, new Color(1f, 0.97f, 0.92f)),
                Atmosphere(a, MarshRegion, new Color(0.3f, 0.36f, 0.31f), 0.032f, new Color(0.25f, 0.32f, 0.3f), new Color(0.2f, 0.25f, 0.22f), new Color(0.08f, 0.1f, 0.08f),
                    new Color(0.72f, 0.82f, 0.74f), 0.55f, new Vector3(18f, 60f, 0f), 0f, -18f, 12f, 0.8f, 0.38f, new Color(0.86f, 1f, 0.9f)),
                Atmosphere(a, BelfryRegion, new Color(0.18f, 0.22f, 0.3f), 0.024f, new Color(0.2f, 0.25f, 0.38f), new Color(0.14f, 0.16f, 0.22f), new Color(0.05f, 0.05f, 0.08f),
                    new Color(0.6f, 0.7f, 1f), 0.4f, new Vector3(48f, 150f, 0f), -0.15f, -10f, 15f, 1f, 0.45f, new Color(0.86f, 0.9f, 1f)),
            });
        }

        private static Operation Atmosphere(StudioAuthor a, string region, Color fog, float density, Color sky, Color equator, Color ground, Color sun, float sunIntensity, Vector3 sunEuler,
            float exposure, float saturation, float contrast, float bloom, float vignette, Color filter) =>
            StudioAuthor.Call("atm_" + System.IO.Path.GetFileNameWithoutExtension(region), "hollowmere.setAtmosphere", a.Ref(region), new JObject
            {
                ["fogColor"] = StudioAuthor.Color(fog),
                ["fogDensity"] = density,
                ["ambientSky"] = StudioAuthor.Color(sky),
                ["ambientEquator"] = StudioAuthor.Color(equator),
                ["ambientGround"] = StudioAuthor.Color(ground),
                ["sunColor"] = StudioAuthor.Color(sun),
                ["sunIntensity"] = sunIntensity,
                ["sunEuler"] = StudioAuthor.V3(sunEuler),
                ["postExposure"] = exposure,
                ["saturation"] = saturation,
                ["contrast"] = contrast,
                ["bloomIntensity"] = bloom,
                ["vignetteIntensity"] = vignette,
                ["colorFilter"] = StudioAuthor.Color(filter),
            });

        // ------------------------------------------------------------------ looks

        /// <summary>Placed NPC name, scene and tint.</summary>
        private static readonly (string Name, string Scene, string Tint)[] NpcTints =
        {
            ("Maren", VillageScene, "#6f9a76"),
            ("Pip", VillageScene, "#d8a23f"),
            ("Bram", VillageScene, "#8e3d2f"),
            ("Hale", MarshScene, "#52668c"),
            ("Odd", MarshScene, "#6e5b44"),
            ("Belfry Echo", BelfryScene, "#a9c9ea"),
        };

        private static void Looks(StudioAuthor a)
        {
            a.Step("dress.npc-animator", "Generate the NPC Animator (idle, walk, talk) on the shared NPC prefab", () => new List<Operation>
            {
                StudioAuthor.Call("anim", "hollowmere.generateNpcAnimator", a.Ref(Npc("MarenEntity")), new JObject { ["folder"] = Animators, ["bodyChild"] = "Body" }),
            });

            // The controller created by the step above is written inside the engine's asset edit block, before its state
            // machine could be added; this second change set runs on the imported controller and adds the states.
            a.Step("dress.npc-animator-states", "Complete the NPC Animator's state machine on the imported controller", () => new List<Operation>
            {
                StudioAuthor.Call("anim", "hollowmere.generateNpcAnimator", a.Ref(Npc("MarenEntity")), new JObject { ["folder"] = Animators, ["bodyChild"] = "Body" }),
            });

            a.Step("dress.prop-skins", "Texture the NPC body and the prop prefabs (lanterns, reeds, stones, crates, bell, signpost, bucket)", () => new List<Operation>
            {
                SkinPrefab(a, "npc", Npc("MarenEntity"), "Body", "Cloth"),
                SkinPrefab(a, "lantern", Interactables + "/PickupLanternEntity.asset", string.Empty, "Bronze"),
                SkinPrefab(a, "reed", Interactables + "/PickupHerbsEntity.asset", string.Empty, "Reed"),
                SkinPrefab(a, "stone", Interactables + "/PickupCoinsEntity.asset", string.Empty, "Stone"),
                SkinPrefab(a, "bell", Interactables + "/PickupClapperEntity.asset", string.Empty, "Bronze"),
                SkinPrefab(a, "crate", Interactables + "/DrownedSatchelEntity.asset", string.Empty, "Planks"),
                SkinPrefab(a, "signpost", Interactables + "/OldPuntEntity.asset", string.Empty, "Timber"),
            });

            a.Step("dress.npc-tints", "Give each NPC its own colours (tint overrides on the placed entities)", () =>
            {
                var ops = new List<Operation>();
                foreach (var npc in NpcTints)
                {
                    AuthoredEntity? entity = HollowmereWorldContent.FindByName(npc.Scene, npc.Name);
                    if (entity != null)
                    {
                        ops.Add(StudioAuthor.Call("tint_" + npc.Name.Replace(' ', '_'), "entity.applyOverride", a.Ref(entity.gameObject),
                            new JObject { ["field"] = "tint", ["value"] = npc.Tint }));
                    }
                }

                AuthoredEntity? coins = HollowmereWorldContent.FindByName(VillageScene, "Coins (square)");
                if (coins != null)
                {
                    ops.Add(StudioAuthor.Call("tint_coins", "entity.applyOverride", a.Ref(coins.gameObject), new JObject { ["field"] = "tint", ["value"] = "#d6b03a" }));
                }

                return ops;
            });
        }

        private static Operation SkinPrefab(StudioAuthor a, string opId, string entityPath, string child, string material) =>
            StudioAuthor.Call("skin_" + opId, "hollowmere.skinPrefab", a.Ref(entityPath), new JObject
            {
                ["materials"] = new JArray(Material(material)),
                ["childName"] = child,
            });

        private static void Boot(StudioAuthor a)
        {
            HollowmereWorldContent.Activate(BootScene);
            a.Step("dress.boot-presentation", "Boot presentation: global post-processing volume, camera post and FXAA, soft sun shadows", () => new List<Operation>
            {
                StudioAuthor.Call("boot", "hollowmere.configureBootPresentation", null, new JObject { ["volumeProfilePath"] = VolumeProfile }),
            });
        }
    }
}
