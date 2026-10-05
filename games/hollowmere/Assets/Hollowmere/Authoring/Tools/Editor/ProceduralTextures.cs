// Hollowmere - ProceduralTextures (P3.1): deterministic, seamless placeholder textures.
//
// These PNGs stand in for the textures generated through the etos gateway (generate.image) and are labelled as
// procedural placeholders wherever they are recorded (media manifest, journal intent). Everything is computed on the
// CPU from periodic lattice noise and periodic Voronoi cells, so the result tiles, needs no graphics device (works in
// -batchmode -nographics) and is byte-identical for the same kind, size and seed.
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hollowmere.Authoring.Tools
{
    /// <summary>Seamless placeholder texture generator.</summary>
    public static class ProceduralTextures
    {
        private static readonly string[] KindNames =
        {
            "grass", "cobble", "thatch", "plaster", "timber", "stone", "mossStone", "mud", "water", "planks", "bronze",
            "cloth", "reed", "bark", "leaves", "glow",
        };

        public static IReadOnlyList<string> Kinds => KindNames;

        public static bool IsKind(string? kind) => kind != null && Array.IndexOf(KindNames, kind) >= 0;

        /// <summary>A seamless RGBA PNG of <paramref name="kind"/>, <paramref name="size"/> pixels square (16..1024).</summary>
        public static byte[] Png(string kind, int size, int seed)
        {
            if (!IsKind(kind))
            {
                throw new ArgumentException("HM-TEX-001: unknown texture kind '" + kind + "' (known: " + string.Join(", ", KindNames) + ")");
            }

            if (size < 16 || size > 1024)
            {
                throw new ArgumentException("HM-TEX-002: texture size must be 16..1024, not " + size);
            }

            Color32[] pixels = Pixels(kind, size, seed);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>The raw pixels (row-major, bottom row first, as Texture2D expects).</summary>
        public static Color32[] Pixels(string kind, int size, int seed)
        {
            var result = new Color32[size * size];
            int kindSeed = seed * 92821 + Array.IndexOf(KindNames, kind) * 7919;
            var noise = new PeriodicNoise(kindSeed);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;
                    Color c = Shade(kind, u, v, noise);
                    result[y * size + x] = (Color32)new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
                }
            }

            return result;
        }

        private static Color Shade(string kind, float u, float v, PeriodicNoise n)
        {
            switch (kind)
            {
                case "grass":
                {
                    float base0 = n.Fbm(u, v, 6, 5);
                    float blades = n.Fbm(u * 1f, v * 1f, 48, 2) * 0.5f + n.Value(u, v, 96) * 0.5f;
                    Color dark = new Color(0.16f, 0.28f, 0.1f), light = new Color(0.36f, 0.5f, 0.2f);
                    return Color.Lerp(dark, light, base0 * 0.7f + blades * 0.3f);
                }

                case "cobble":
                {
                    n.Voronoi(u, v, 7, out float f1, out float f2, out float cell);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((f2 - f1) * 9f));
                    float tone = 0.35f + 0.25f * cell + 0.15f * n.Fbm(u, v, 24, 3);
                    Color stone = new Color(tone, tone * 0.97f, tone * 0.92f);
                    return Color.Lerp(new Color(0.12f, 0.11f, 0.1f), stone, edge);
                }

                case "thatch":
                {
                    float straw = n.Fbm(u * 1f, v * 1f, 4, 3) * 0.4f + StretchedStreaks(u, v, n, 64, 4) * 0.6f;
                    float rows = 0.85f + 0.15f * Mathf.Abs(Mathf.Sin(v * Mathf.PI * 8f));
                    return Color.Lerp(new Color(0.35f, 0.27f, 0.13f), new Color(0.72f, 0.6f, 0.33f), straw) * rows;
                }

                case "plaster":
                {
                    float t = n.Fbm(u, v, 8, 5);
                    float crack = Mathf.Clamp01(1f - Mathf.Abs(n.Fbm(u, v, 5, 3) - 0.5f) * 60f) * 0.25f;
                    Color c = Color.Lerp(new Color(0.74f, 0.69f, 0.6f), new Color(0.88f, 0.84f, 0.76f), t);
                    return c * (1f - crack);
                }

                case "timber":
                {
                    Color plaster = Color.Lerp(new Color(0.76f, 0.71f, 0.62f), new Color(0.88f, 0.84f, 0.75f), n.Fbm(u, v, 8, 4));
                    float bandU = Band(u, 2f, 0.1f);
                    float bandV = Band(v, 2f, 0.1f);
                    float diagonal = Band(Frac(u + v), 1f, 0.05f);
                    float beam = Mathf.Max(bandU, Mathf.Max(bandV, diagonal));
                    Color wood = Color.Lerp(new Color(0.18f, 0.11f, 0.06f), new Color(0.3f, 0.2f, 0.11f), StretchedStreaks(u, v, n, 32, 4));
                    return Color.Lerp(plaster, wood, beam);
                }

                case "stone":
                {
                    float rows = 6f;
                    float row = Mathf.Floor(v * rows);
                    float offset = (row % 2f) * 0.5f;
                    float bu = Frac(u * 4f + offset);
                    float bv = Frac(v * rows);
                    float mortar = Mathf.Min(Mathf.Min(bu, 1f - bu) * 4f * 6f, Mathf.Min(bv, 1f - bv) * 6f * 6f);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(mortar));
                    float block = Hash(Mathf.FloorToInt(u * 4f + offset) * 31 + (int)row * 17 + n.Seed);
                    float tone = 0.38f + 0.18f * block + 0.14f * n.Fbm(u, v, 16, 4);
                    return Color.Lerp(new Color(0.16f, 0.15f, 0.14f), new Color(tone, tone, tone * 1.02f), edge);
                }

                case "mossStone":
                {
                    Color stone = Shade("stone", u, v, n);
                    float moss = Mathf.SmoothStep(0.45f, 0.7f, n.Fbm(u + 0.37f, v + 0.11f, 4, 5));
                    Color green = Color.Lerp(new Color(0.15f, 0.25f, 0.08f), new Color(0.3f, 0.42f, 0.14f), n.Value(u, v, 64));
                    return Color.Lerp(stone, green, moss);
                }

                case "mud":
                {
                    float t = n.Fbm(u, v, 6, 5);
                    float puddle = Mathf.SmoothStep(0.55f, 0.65f, n.Fbm(u + 0.5f, v, 3, 3));
                    Color mud = Color.Lerp(new Color(0.18f, 0.13f, 0.08f), new Color(0.36f, 0.27f, 0.17f), t);
                    return Color.Lerp(mud, new Color(0.1f, 0.08f, 0.06f), puddle * 0.7f);
                }

                case "water":
                {
                    float ripple = 0.5f + 0.5f * Mathf.Sin((u * 6f + n.Fbm(u, v, 4, 3) * 2f) * Mathf.PI * 2f) * Mathf.Sin((v * 5f + n.Fbm(v, u, 4, 3) * 2f) * Mathf.PI * 2f);
                    float t = n.Fbm(u, v, 8, 4);
                    return Color.Lerp(new Color(0.04f, 0.09f, 0.08f), new Color(0.12f, 0.2f, 0.18f), t * 0.6f + ripple * 0.4f);
                }

                case "planks":
                {
                    float planks = 5f;
                    float row = Mathf.Floor(v * planks);
                    float pv = Frac(v * planks);
                    float seam = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(pv, 1f - pv) * 40f));
                    float grain = StretchedStreaks(Frac(u + Hash((int)row + n.Seed) * 0.37f), v, n, 48, 6);
                    float tone = 0.8f + 0.2f * Hash((int)row * 13 + n.Seed);
                    Color wood = Color.Lerp(new Color(0.25f, 0.16f, 0.09f), new Color(0.48f, 0.34f, 0.2f), grain) * tone;
                    return Color.Lerp(new Color(0.08f, 0.05f, 0.03f), wood, seam);
                }

                case "bronze":
                {
                    float t = n.Fbm(u, v, 8, 5);
                    float verdigris = Mathf.SmoothStep(0.5f, 0.75f, n.Fbm(u + 0.2f, v + 0.7f, 5, 4));
                    Color metal = Color.Lerp(new Color(0.42f, 0.28f, 0.12f), new Color(0.7f, 0.5f, 0.24f), t);
                    return Color.Lerp(metal, new Color(0.25f, 0.5f, 0.42f), verdigris);
                }

                case "cloth":
                {
                    float warp = Mathf.Sin(u * Mathf.PI * 2f * 64f) * 0.5f + 0.5f;
                    float weft = Mathf.Sin(v * Mathf.PI * 2f * 64f) * 0.5f + 0.5f;
                    float stripe = Band(u, 8f, 0.25f);
                    Color a = new Color(0.55f, 0.14f, 0.1f), b = new Color(0.78f, 0.62f, 0.3f);
                    Color c = Color.Lerp(a, b, stripe);
                    return c * (0.8f + 0.1f * warp + 0.1f * weft) * (0.9f + 0.1f * n.Fbm(u, v, 8, 3));
                }

                case "reed":
                {
                    float streak = StretchedStreaks(u, v, n, 64, 3);
                    return Color.Lerp(new Color(0.3f, 0.36f, 0.14f), new Color(0.64f, 0.6f, 0.32f), streak);
                }

                case "bark":
                {
                    float streak = StretchedStreaks(u, v, n, 24, 5);
                    float knot = Mathf.SmoothStep(0.7f, 0.8f, n.Fbm(u, v, 4, 2));
                    Color c = Color.Lerp(new Color(0.12f, 0.08f, 0.05f), new Color(0.34f, 0.24f, 0.15f), streak);
                    return Color.Lerp(c, new Color(0.08f, 0.05f, 0.03f), knot);
                }

                case "leaves":
                {
                    n.Voronoi(u, v, 16, out float f1, out float f2, out float cell);
                    float leaf = Mathf.Clamp01(1f - f1 * 3f);
                    Color c = Color.Lerp(new Color(0.06f, 0.16f, 0.06f), new Color(0.22f, 0.4f, 0.14f), cell * 0.6f + leaf * 0.4f);
                    return c * (0.85f + 0.15f * n.Fbm(u, v, 8, 3));
                }

                default:
                {
                    float t = n.Fbm(u, v, 4, 4);
                    float flicker = 0.85f + 0.15f * n.Value(u, v, 32);
                    return Color.Lerp(new Color(1f, 0.55f, 0.18f), new Color(1f, 0.88f, 0.55f), t) * flicker;
                }
            }
        }

        private static float StretchedStreaks(float u, float v, PeriodicNoise n, int frequency, int vStretch)
        {
            // Long streaks along v: high frequency across u, low along v; both periodic so the tile wraps.
            return n.Fbm2(u, v, frequency, Mathf.Max(1, frequency / vStretch), 3);
        }

        private static float Band(float t, float count, float width)
        {
            float f = Frac(t * count);
            float d = Mathf.Min(f, 1f - f);
            return 1f - Mathf.SmoothStep(width * 0.5f, width * 0.5f + 0.02f, d);
        }

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static float Hash(int n) => ProceduralMeshes.Hash01(n);

        /// <summary>Periodic value noise and Voronoi over the unit square (period 1 in u and v).</summary>
        private sealed class PeriodicNoise
        {
            public PeriodicNoise(int seed)
            {
                Seed = seed;
            }

            public int Seed { get; }

            /// <summary>Smooth value noise with <paramref name="cells"/> lattice cells per unit (wraps at 1).</summary>
            public float Value(float u, float v, int cells) => Value2(u, v, cells, cells, 0);

            /// <summary>Fractal value noise: <paramref name="octaves"/> octaves starting at <paramref name="cells"/> cells.</summary>
            public float Fbm(float u, float v, int cells, int octaves) => Fbm2(u, v, cells, cells, octaves);

            public float Fbm2(float u, float v, int cellsU, int cellsV, int octaves)
            {
                float sum = 0f, amplitude = 0.5f, norm = 0f;
                int cu = Mathf.Max(1, cellsU), cv = Mathf.Max(1, cellsV);
                for (int o = 0; o < Mathf.Max(1, octaves); o++)
                {
                    sum += Value2(u, v, cu, cv, o) * amplitude;
                    norm += amplitude;
                    amplitude *= 0.5f;
                    cu *= 2;
                    cv *= 2;
                }

                return sum / norm;
            }

            /// <summary>Periodic Voronoi: nearest and second-nearest feature distances and the nearest cell's value.</summary>
            public void Voronoi(float u, float v, int cells, out float f1, out float f2, out float cellValue)
            {
                float x = Frac(u) * cells, y = Frac(v) * cells;
                int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
                f1 = 10f;
                f2 = 10f;
                cellValue = 0f;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cx = ix + dx, cy = iy + dy;
                        int wx = ((cx % cells) + cells) % cells, wy = ((cy % cells) + cells) % cells;
                        float px = cx + Hash(Lattice(wx, wy, 101)), py = cy + Hash(Lattice(wx, wy, 211));
                        float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                        if (d < f1)
                        {
                            f2 = f1;
                            f1 = d;
                            cellValue = Hash(Lattice(wx, wy, 307));
                        }
                        else if (d < f2)
                        {
                            f2 = d;
                        }
                    }
                }
            }

            private float Value2(float u, float v, int cellsU, int cellsV, int octave)
            {
                float x = Frac(u) * cellsU, y = Frac(v) * cellsV;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                int x1 = (x0 + 1) % cellsU, y1 = (y0 + 1) % cellsV;
                x0 %= cellsU;
                y0 %= cellsV;
                int salt = octave * 1013;
                float a = Hash(Lattice(x0, y0, salt)), b = Hash(Lattice(x1, y0, salt));
                float c = Hash(Lattice(x0, y1, salt)), d = Hash(Lattice(x1, y1, salt));
                float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
                return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
            }

            private int Lattice(int x, int y, int salt)
            {
                unchecked
                {
                    return x * 73856093 ^ y * 19349663 ^ (Seed + salt) * 83492791;
                }
            }
        }
    }
}
