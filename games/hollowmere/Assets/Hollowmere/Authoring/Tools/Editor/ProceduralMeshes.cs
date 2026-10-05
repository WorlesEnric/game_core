// Hollowmere - ProceduralMeshes (P3.1): deterministic low-poly scenery meshes for the dressing tools.
//
// Every mesh is flat shaded (each triangle owns its vertices, so normals are exact), its UVs are box-projected in world
// metres (a tiling texture keeps the same scale on every piece), its origin is the centre of its base (y = 0 on the
// ground) and its name is its kind. The same kind and size always produce the same vertices in the same order.
// Submeshes split materials: e.g. house = 0 walls, 1 roof (SubmeshCount tells how many a kind has).
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollowmere.Authoring.Tools
{
    /// <summary>Builds the scenery meshes of the Hollowmere dressing tools.</summary>
    public static class ProceduralMeshes
    {
        private static readonly string[] KindNames =
        {
            "ground", "house", "inn", "hut", "well", "tree", "deadTree", "reeds", "rock", "fence", "jetty", "punt",
            "shrine", "ruinWall", "tower", "bellFrame", "water", "causeway", "lanternPost", "stall", "barrel",
        };

        /// <summary>Every kind <see cref="Build"/> accepts.</summary>
        public static IReadOnlyList<string> Kinds => KindNames;

        /// <summary>True when <paramref name="kind"/> is a known kind.</summary>
        public static bool IsKind(string? kind) => kind != null && Array.IndexOf(KindNames, kind) >= 0;

        /// <summary>How many submeshes (material slots) a kind has.</summary>
        public static int SubmeshCount(string kind)
        {
            switch (kind)
            {
                case "house":
                case "inn":
                case "hut":
                case "well":
                case "tree":
                case "shrine":
                case "tower":
                case "lanternPost":
                case "stall":
                    return 2;
                default:
                    return 1;
            }
        }

        /// <summary>The size used when the caller gives a zero size (x width, y height, z depth; metres).</summary>
        public static Vector3 DefaultSize(string kind)
        {
            switch (kind)
            {
                case "ground": return new Vector3(80f, 0f, 80f);
                case "house": return new Vector3(6f, 5f, 5f);
                case "inn": return new Vector3(10f, 7f, 8f);
                case "hut": return new Vector3(5f, 4.5f, 5f);
                case "well": return new Vector3(2.4f, 2.8f, 2.4f);
                case "tree": return new Vector3(4f, 7f, 4f);
                case "deadTree": return new Vector3(2f, 6f, 2f);
                case "reeds": return new Vector3(1.5f, 1.6f, 1.5f);
                case "rock": return new Vector3(1.5f, 1f, 1.2f);
                case "fence": return new Vector3(6f, 1.2f, 0.15f);
                case "jetty": return new Vector3(3f, 0.6f, 12f);
                case "punt": return new Vector3(1.4f, 0.6f, 4f);
                case "shrine": return new Vector3(3f, 3.5f, 3f);
                case "ruinWall": return new Vector3(6f, 3.5f, 0.8f);
                case "tower": return new Vector3(8f, 16f, 8f);
                case "bellFrame": return new Vector3(4f, 5f, 2.5f);
                case "water": return new Vector3(20f, 0f, 20f);
                case "causeway": return new Vector3(3f, 0.4f, 16f);
                case "lanternPost": return new Vector3(0.4f, 2.6f, 0.4f);
                case "stall": return new Vector3(3f, 2.6f, 1.6f);
                case "barrel": return new Vector3(0.7f, 1f, 0.7f);
                default: return Vector3.one;
            }
        }

        /// <summary>The size actually built: the default for a zero vector, otherwise the given one.</summary>
        public static Vector3 EffectiveSize(string kind, Vector3 size) => size == Vector3.zero ? DefaultSize(kind) : size;

        /// <summary>Builds the mesh of <paramref name="kind"/> at <paramref name="size"/> (zero = the kind's default).</summary>
        public static Mesh Build(string kind, Vector3 size)
        {
            if (!IsKind(kind))
            {
                throw new ArgumentException("HM-MESH-001: unknown scenery kind '" + kind + "' (known: " + string.Join(", ", KindNames) + ")");
            }

            Vector3 s = EffectiveSize(kind, size);
            var b = new Builder(SubmeshCount(kind));
            switch (kind)
            {
                case "ground": Ground(b, s); break;
                case "house": House(b, s, false); break;
                case "inn": House(b, s, true); break;
                case "hut": Hut(b, s); break;
                case "well": Well(b, s); break;
                case "tree": Tree(b, s); break;
                case "deadTree": DeadTree(b, s); break;
                case "reeds": Reeds(b, s); break;
                case "rock": Rock(b, s); break;
                case "fence": Fence(b, s); break;
                case "jetty": Jetty(b, s); break;
                case "punt": Punt(b, s); break;
                case "shrine": Shrine(b, s); break;
                case "ruinWall": RuinWall(b, s); break;
                case "tower": Tower(b, s); break;
                case "bellFrame": BellFrame(b, s); break;
                case "water": Water(b, s); break;
                case "causeway": Causeway(b, s); break;
                case "lanternPost": LanternPost(b, s); break;
                case "stall": Stall(b, s); break;
                case "barrel": Barrel(b, s); break;
            }

            Mesh mesh = b.ToMesh();
            mesh.name = kind;
            return mesh;
        }

        // ------------------------------------------------------------------ kinds

        private static void Ground(Builder b, Vector3 s)
        {
            int nx = Mathf.Clamp(Mathf.CeilToInt(s.x / 8f), 1, 32);
            int nz = Mathf.Clamp(Mathf.CeilToInt(s.z / 8f), 1, 32);
            b.Grid(0, Matrix4x4.identity, s.x, s.z, nx, nz, 0f);
        }

        private static void Water(Builder b, Vector3 s)
        {
            b.Grid(0, Matrix4x4.identity, s.x, s.z, 1, 1, 0f);
        }

        private static void House(Builder b, Vector3 s, bool inn)
        {
            float wallHeight = s.y * 0.6f;
            float roofHeight = s.y - wallHeight;
            b.Box(0, Matrix4x4.identity, new Vector3(0f, wallHeight * 0.5f, 0f), new Vector3(s.x, wallHeight, s.z));
            // Door frame and two window sills on the front (-z) face, slightly proud of the wall.
            float front = -s.z * 0.5f - 0.04f;
            b.Box(0, Matrix4x4.identity, new Vector3(0f, 1.05f, front), new Vector3(1.1f, 2.1f, 0.08f));
            b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.3f, wallHeight * 0.55f, front), new Vector3(0.9f, 0.8f, 0.08f));
            b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.3f, wallHeight * 0.55f, front), new Vector3(0.9f, 0.8f, 0.08f));
            b.Gable(1, Matrix4x4.Translate(new Vector3(0f, wallHeight, 0f)), s.x + 0.6f, s.z + 0.8f, roofHeight);
            if (inn)
            {
                // Porch roof on two posts and a chimney through the roof.
                float porchDepth = 1.6f;
                b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.25f, 1.2f, -s.z * 0.5f - porchDepth + 0.1f), new Vector3(0.2f, 2.4f, 0.2f));
                b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.25f, 1.2f, -s.z * 0.5f - porchDepth + 0.1f), new Vector3(0.2f, 2.4f, 0.2f));
                b.Box(1, Matrix4x4.identity, new Vector3(0f, 2.5f, -s.z * 0.5f - porchDepth * 0.5f), new Vector3(s.x * 0.6f, 0.15f, porchDepth + 0.2f));
                b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.3f, wallHeight + roofHeight * 0.6f, s.z * 0.15f), new Vector3(0.8f, roofHeight * 1.2f, 0.8f));
            }
        }

        private static void Hut(Builder b, Vector3 s)
        {
            float radius = Mathf.Min(s.x, s.z) * 0.5f;
            float wallHeight = s.y * 0.5f;
            b.Frustum(0, Matrix4x4.identity, radius, radius, wallHeight, 12, false, true);
            b.Box(0, Matrix4x4.identity, new Vector3(0f, 1f, -radius - 0.03f), new Vector3(1f, 2f, 0.1f));
            b.Frustum(1, Matrix4x4.Translate(new Vector3(0f, wallHeight, 0f)), radius + 0.4f, 0f, s.y - wallHeight, 12, false, true);
        }

        private static void Well(Builder b, Vector3 s)
        {
            float radius = Mathf.Min(s.x, s.z) * 0.5f;
            float ringHeight = s.y * 0.3f;
            int sides = 10;
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                Vector3 c = new Vector3(Mathf.Cos(a) * radius * 0.85f, ringHeight * 0.5f, Mathf.Sin(a) * radius * 0.85f);
                Matrix4x4 m = Matrix4x4.TRS(c, Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f), Vector3.one);
                b.Box(0, m, Vector3.zero, new Vector3(radius * 2f * Mathf.PI / sides + 0.05f, ringHeight, radius * 0.3f));
            }

            float postHeight = s.y * 0.75f;
            b.Box(1, Matrix4x4.identity, new Vector3(-radius * 0.85f, postHeight * 0.5f, 0f), new Vector3(0.15f, postHeight, 0.15f));
            b.Box(1, Matrix4x4.identity, new Vector3(radius * 0.85f, postHeight * 0.5f, 0f), new Vector3(0.15f, postHeight, 0.15f));
            b.Box(1, Matrix4x4.identity, new Vector3(0f, postHeight * 0.8f, 0f), new Vector3(radius * 1.9f, 0.1f, 0.1f));
            b.Gable(1, Matrix4x4.Translate(new Vector3(0f, postHeight, 0f)), radius * 2.2f, radius * 1.4f, s.y - postHeight);
        }

        private static void Tree(Builder b, Vector3 s)
        {
            float trunkHeight = s.y * 0.45f;
            b.Frustum(0, Matrix4x4.identity, s.x * 0.08f, s.x * 0.05f, trunkHeight, 7, false, false);
            float canopy = s.y - trunkHeight;
            float r = Mathf.Min(s.x, s.z) * 0.5f;
            b.Frustum(1, Matrix4x4.Translate(new Vector3(0f, trunkHeight * 0.85f, 0f)), r, r * 0.55f, canopy * 0.45f, 8, true, true);
            b.Frustum(1, Matrix4x4.Translate(new Vector3(0f, trunkHeight * 0.85f + canopy * 0.4f, 0f)), r * 0.8f, 0f, canopy * 0.6f, 8, true, false);
        }

        private static void DeadTree(Builder b, Vector3 s)
        {
            float r = Mathf.Min(s.x, s.z) * 0.12f;
            b.Frustum(0, Matrix4x4.identity, r, r * 0.4f, s.y, 6, false, false);
            float[] heights = { 0.45f, 0.62f, 0.78f };
            float[] yaws = { 20f, 150f, 260f };
            for (int i = 0; i < heights.Length; i++)
            {
                Matrix4x4 m = Matrix4x4.TRS(new Vector3(0f, s.y * heights[i], 0f), Quaternion.Euler(0f, yaws[i], -55f), Vector3.one);
                b.Frustum(0, m, r * 0.45f, r * 0.12f, s.x * 0.6f, 5, false, false);
            }
        }

        private static void Reeds(Builder b, Vector3 s)
        {
            int blades = 14;
            for (int i = 0; i < blades; i++)
            {
                float u = Hash01(i * 7 + 1);
                float v = Hash01(i * 13 + 5);
                float a = u * Mathf.PI * 2f;
                float d = Mathf.Sqrt(v) * 0.5f;
                Vector3 root = new Vector3(Mathf.Cos(a) * d * s.x, 0f, Mathf.Sin(a) * d * s.z);
                float h = s.y * (0.6f + 0.4f * Hash01(i * 31 + 3));
                float lean = (Hash01(i * 17 + 9) - 0.5f) * 0.5f;
                Vector3 tip = root + new Vector3(lean * Mathf.Cos(a), h, lean * Mathf.Sin(a));
                Vector3 side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.04f;
                b.DoubleTri(0, root - side, root + side, tip);
            }
        }

        private static void Rock(Builder b, Vector3 s)
        {
            int seed = Mathf.RoundToInt(s.x * 1000f) * 73856093 ^ Mathf.RoundToInt(s.y * 1000f) * 19349663 ^ Mathf.RoundToInt(s.z * 1000f) * 83492791;
            b.Icosphere(0, s, seed);
        }

        private static void Fence(Builder b, Vector3 s)
        {
            int posts = Mathf.Max(2, Mathf.RoundToInt(s.x / 2f) + 1);
            for (int i = 0; i < posts; i++)
            {
                float x = -s.x * 0.5f + s.x * i / (posts - 1);
                b.Box(0, Matrix4x4.identity, new Vector3(x, s.y * 0.5f, 0f), new Vector3(0.14f, s.y, 0.14f));
            }

            b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y * 0.4f, 0f), new Vector3(s.x, 0.1f, Mathf.Max(0.05f, s.z * 0.6f)));
            b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y * 0.8f, 0f), new Vector3(s.x, 0.1f, Mathf.Max(0.05f, s.z * 0.6f)));
        }

        private static void Jetty(Builder b, Vector3 s)
        {
            int planks = Mathf.Max(1, Mathf.RoundToInt(s.z / 0.35f));
            float step = s.z / planks;
            for (int i = 0; i < planks; i++)
            {
                float z = -s.z * 0.5f + step * (i + 0.5f);
                b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y - 0.04f, z), new Vector3(s.x, 0.08f, step * 0.9f));
            }

            int pairs = Mathf.Max(2, Mathf.RoundToInt(s.z / 3f) + 1);
            for (int i = 0; i < pairs; i++)
            {
                float z = -s.z * 0.5f + s.z * i / (pairs - 1);
                b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.45f, (s.y - 0.4f) * 0.5f - 0.2f, z), new Vector3(0.18f, s.y + 0.4f, 0.18f));
                b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.45f, (s.y - 0.4f) * 0.5f - 0.2f, z), new Vector3(0.18f, s.y + 0.4f, 0.18f));
            }

            b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.45f, s.y - 0.14f, 0f), new Vector3(0.12f, 0.12f, s.z));
            b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.45f, s.y - 0.14f, 0f), new Vector3(0.12f, 0.12f, s.z));
        }

        private static void Punt(Builder b, Vector3 s)
        {
            float t = 0.07f;
            b.Box(0, Matrix4x4.identity, new Vector3(0f, t * 0.5f, 0f), new Vector3(s.x, t, s.z));
            b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.5f + t * 0.5f, s.y * 0.5f, 0f), new Vector3(t, s.y, s.z));
            b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.5f - t * 0.5f, s.y * 0.5f, 0f), new Vector3(t, s.y, s.z));
            Matrix4x4 bow = Matrix4x4.TRS(new Vector3(0f, s.y * 0.5f, s.z * 0.5f), Quaternion.Euler(-35f, 0f, 0f), Vector3.one);
            b.Box(0, bow, Vector3.zero, new Vector3(s.x, s.y * 1.1f, t));
            Matrix4x4 stern = Matrix4x4.TRS(new Vector3(0f, s.y * 0.5f, -s.z * 0.5f), Quaternion.Euler(35f, 0f, 0f), Vector3.one);
            b.Box(0, stern, Vector3.zero, new Vector3(s.x, s.y * 1.1f, t));
            b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y * 0.6f, 0f), new Vector3(s.x - t * 2f, 0.06f, 0.3f));
        }

        private static void Shrine(Builder b, Vector3 s)
        {
            float plinth = s.y * 0.15f;
            b.Box(0, Matrix4x4.identity, new Vector3(0f, plinth * 0.5f, 0f), new Vector3(s.x, plinth, s.z));
            float pillarHeight = s.y * 0.7f;
            b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.38f, plinth + pillarHeight * 0.5f, 0f), new Vector3(s.x * 0.16f, pillarHeight, s.z * 0.3f));
            b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.38f, plinth + pillarHeight * 0.5f, 0f), new Vector3(s.x * 0.16f, pillarHeight, s.z * 0.3f));
            b.Box(0, Matrix4x4.identity, new Vector3(0f, plinth + pillarHeight + (s.y - plinth - pillarHeight) * 0.5f, 0f), new Vector3(s.x * 1.02f, s.y - plinth - pillarHeight, s.z * 0.36f));
            b.Frustum(1, Matrix4x4.Translate(new Vector3(0f, plinth, -s.z * 0.15f)), s.x * 0.18f, s.x * 0.24f, s.y * 0.18f, 10, true, false);
        }

        private static void RuinWall(Builder b, Vector3 s)
        {
            int columns = Mathf.Max(3, Mathf.RoundToInt(s.x / 0.75f));
            float w = s.x / columns;
            for (int i = 0; i < columns; i++)
            {
                float h = s.y * (0.35f + 0.65f * Hash01(i * 11 + Mathf.RoundToInt(s.x * 10f)));
                float x = -s.x * 0.5f + w * (i + 0.5f);
                b.Box(0, Matrix4x4.identity, new Vector3(x, h * 0.5f, 0f), new Vector3(w * 1.01f, h, s.z));
            }
        }

        private static void Tower(Builder b, Vector3 s)
        {
            float wall = Mathf.Max(0.4f, Mathf.Min(s.x, s.z) * 0.1f);
            float doorWidth = s.x * 0.3f;
            float doorHeight = Mathf.Min(3.2f, s.y * 0.25f);
            // Back and side walls full height, broken tops.
            b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y * 0.5f, s.z * 0.5f - wall * 0.5f), new Vector3(s.x, s.y, wall));
            b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.5f + wall * 0.5f, s.y * 0.45f, 0f), new Vector3(wall, s.y * 0.9f, s.z));
            b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.5f - wall * 0.5f, s.y * 0.4f, 0f), new Vector3(wall, s.y * 0.8f, s.z));
            // Front wall with an arch opening (two jambs and a lintel band above it).
            float jamb = (s.x - doorWidth) * 0.5f;
            float frontZ = -s.z * 0.5f + wall * 0.5f;
            b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.5f + jamb * 0.5f, s.y * 0.35f, frontZ), new Vector3(jamb, s.y * 0.7f, wall));
            b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.5f - jamb * 0.5f, s.y * 0.3f, frontZ), new Vector3(jamb, s.y * 0.6f, wall));
            b.Box(0, Matrix4x4.identity, new Vector3(0f, doorHeight + (s.y * 0.55f - doorHeight) * 0.5f, frontZ), new Vector3(doorWidth, s.y * 0.55f - doorHeight, wall));
            // Upper belfry openings: beams across the open top.
            float top = s.y * 0.78f;
            b.Box(1, Matrix4x4.identity, new Vector3(0f, top, 0f), new Vector3(s.x - wall, 0.3f, 0.3f));
            b.Box(1, Matrix4x4.identity, new Vector3(0f, top, 0f), new Vector3(0.3f, 0.3f, s.z - wall));
            b.Box(1, Matrix4x4.identity, new Vector3(0f, s.y * 0.3f, s.z * 0.2f), new Vector3(s.x - wall * 2f, 0.2f, s.z * 0.5f));
        }

        private static void BellFrame(Builder b, Vector3 s)
        {
            float beam = 0.22f;
            float half = s.x * 0.5f;
            float legAngle = Mathf.Atan2(s.z * 0.5f, s.y) * Mathf.Rad2Deg;
            float legLength = Mathf.Sqrt(s.y * s.y + s.z * s.z * 0.25f);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (half - beam * 0.5f);
                Matrix4x4 front = Matrix4x4.TRS(new Vector3(x, s.y * 0.5f, -s.z * 0.25f), Quaternion.Euler(-legAngle, 0f, 0f), Vector3.one);
                Matrix4x4 back = Matrix4x4.TRS(new Vector3(x, s.y * 0.5f, s.z * 0.25f), Quaternion.Euler(legAngle, 0f, 0f), Vector3.one);
                b.Box(0, front, Vector3.zero, new Vector3(beam, legLength, beam));
                b.Box(0, back, Vector3.zero, new Vector3(beam, legLength, beam));
                b.Box(0, Matrix4x4.identity, new Vector3(x, s.y * 0.35f, 0f), new Vector3(beam * 0.8f, beam * 0.8f, s.z * 0.65f));
            }

            b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y - beam * 0.5f, 0f), new Vector3(s.x, beam * 1.2f, beam * 1.2f));
        }

        private static void Causeway(Builder b, Vector3 s)
        {
            b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y * 0.5f - 0.2f, 0f), new Vector3(s.x, s.y + 0.4f, s.z));
            int stones = Mathf.Max(2, Mathf.RoundToInt(s.z / 2f));
            for (int i = 0; i < stones; i++)
            {
                float z = -s.z * 0.5f + s.z * (i + 0.5f) / stones;
                float x = (Hash01(i * 5 + 2) - 0.5f) * s.x * 0.4f;
                b.Box(0, Matrix4x4.identity, new Vector3(x, s.y + 0.03f, z), new Vector3(s.x * 0.35f, 0.06f, s.z / stones * 0.7f));
            }
        }

        private static void LanternPost(Builder b, Vector3 s)
        {
            float pole = Mathf.Max(0.08f, s.x * 0.3f);
            b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y * 0.45f, 0f), new Vector3(pole, s.y * 0.9f, pole));
            b.Box(0, Matrix4x4.identity, new Vector3(0f, s.y * 0.9f, 0f), new Vector3(s.x * 0.9f, 0.08f, s.z * 0.9f));
            b.Box(1, Matrix4x4.identity, new Vector3(0f, s.y * 0.9f - 0.2f, 0f), new Vector3(s.x * 0.6f, 0.32f, s.z * 0.6f));
            b.Frustum(0, Matrix4x4.Translate(new Vector3(0f, s.y * 0.9f + 0.04f, 0f)), s.x * 0.5f, 0f, s.y * 0.1f, 4, false, true);
        }

        private static void Stall(Builder b, Vector3 s)
        {
            float counter = 1.05f;
            b.Box(0, Matrix4x4.identity, new Vector3(0f, counter * 0.5f, 0f), new Vector3(s.x, counter, s.z * 0.5f));
            b.Box(0, Matrix4x4.identity, new Vector3(-s.x * 0.47f, s.y * 0.5f, s.z * 0.2f), new Vector3(0.12f, s.y, 0.12f));
            b.Box(0, Matrix4x4.identity, new Vector3(s.x * 0.47f, s.y * 0.5f, s.z * 0.2f), new Vector3(0.12f, s.y, 0.12f));
            Matrix4x4 awning = Matrix4x4.TRS(new Vector3(0f, s.y - 0.1f, 0f), Quaternion.Euler(-15f, 0f, 0f), Vector3.one);
            b.Box(1, awning, Vector3.zero, new Vector3(s.x + 0.3f, 0.06f, s.z * 1.1f));
        }

        private static void Barrel(Builder b, Vector3 s)
        {
            float r = Mathf.Min(s.x, s.z) * 0.5f;
            b.Frustum(0, Matrix4x4.identity, r * 0.85f, r, s.y * 0.5f, 10, false, true);
            b.Frustum(0, Matrix4x4.Translate(new Vector3(0f, s.y * 0.5f, 0f)), r, r * 0.85f, s.y * 0.5f, 10, true, false);
        }

        /// <summary>A deterministic value in [0, 1) from an integer (no shared random state).</summary>
        internal static float Hash01(int n)
        {
            unchecked
            {
                uint x = (uint)n * 747796405u + 2891336453u;
                x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
                x = (x >> 22) ^ x;
                return (x & 0xFFFFFF) / 16777216f;
            }
        }

        // ------------------------------------------------------------------ builder

        /// <summary>Accumulates flat-shaded triangles per submesh.</summary>
        private sealed class Builder
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int>[] triangles;

            public Builder(int submeshes)
            {
                triangles = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++)
                {
                    triangles[i] = new List<int>();
                }
            }

            public void Tri(int submesh, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f)
                {
                    return;
                }

                n.Normalize();
                int baseIndex = vertices.Count;
                Add(a, n);
                Add(b, n);
                Add(c, n);
                List<int> list = triangles[Mathf.Clamp(submesh, 0, triangles.Length - 1)];
                list.Add(baseIndex);
                list.Add(baseIndex + 1);
                list.Add(baseIndex + 2);
            }

            public void DoubleTri(int submesh, Vector3 a, Vector3 b, Vector3 c)
            {
                Tri(submesh, a, b, c);
                Tri(submesh, a, c, b);
            }

            public void Quad(int submesh, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                Tri(submesh, a, b, c);
                Tri(submesh, a, c, d);
            }

            /// <summary>An axis-aligned box (in the matrix's space) with outward faces.</summary>
            public void Box(int submesh, Matrix4x4 m, Vector3 center, Vector3 size)
            {
                Vector3 h = size * 0.5f;
                Vector3 P(float x, float y, float z) => m.MultiplyPoint3x4(center + new Vector3(x * h.x, y * h.y, z * h.z));
                Vector3 p000 = P(-1, -1, -1), p100 = P(1, -1, -1), p010 = P(-1, 1, -1), p110 = P(1, 1, -1);
                Vector3 p001 = P(-1, -1, 1), p101 = P(1, -1, 1), p011 = P(-1, 1, 1), p111 = P(1, 1, 1);
                Quad(submesh, p000, p010, p110, p100); // -z
                Quad(submesh, p101, p111, p011, p001); // +z
                Quad(submesh, p001, p011, p010, p000); // -x
                Quad(submesh, p100, p110, p111, p101); // +x
                Quad(submesh, p010, p011, p111, p110); // +y
                Quad(submesh, p001, p000, p100, p101); // -y
            }

            /// <summary>A gable roof: ridge along x, eaves at y = 0 spanning depth along z.</summary>
            public void Gable(int submesh, Matrix4x4 m, float length, float depth, float height)
            {
                float hx = length * 0.5f, hz = depth * 0.5f;
                Vector3 P(float x, float y, float z) => m.MultiplyPoint3x4(new Vector3(x, y, z));
                Vector3 a = P(-hx, 0f, -hz), bb = P(hx, 0f, -hz), c = P(hx, height, 0f), d = P(-hx, height, 0f);
                Vector3 e = P(-hx, 0f, hz), f = P(hx, 0f, hz);
                Quad(submesh, a, d, c, bb);
                Quad(submesh, f, c, d, e);
                Tri(submesh, a, e, d);
                Tri(submesh, bb, c, f);
                Quad(submesh, e, a, bb, f);
            }

            /// <summary>A frustum from radius <paramref name="r0"/> at y = 0 to <paramref name="r1"/> at <paramref name="height"/>.</summary>
            public void Frustum(int submesh, Matrix4x4 m, float r0, float r1, float height, int sides, bool capBottom, bool capTop)
            {
                Vector3 P(float r, float y, int i)
                {
                    float a = i * Mathf.PI * 2f / sides;
                    return m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                }

                Vector3 bottom = m.MultiplyPoint3x4(Vector3.zero);
                Vector3 top = m.MultiplyPoint3x4(new Vector3(0f, height, 0f));
                for (int i = 0; i < sides; i++)
                {
                    Vector3 a0 = P(r0, 0f, i), a1 = P(r0, 0f, i + 1);
                    Vector3 b0 = P(r1, height, i), b1 = P(r1, height, i + 1);
                    if (r1 <= 0f)
                    {
                        Tri(submesh, a0, top, a1);
                    }
                    else
                    {
                        Quad(submesh, a0, b0, b1, a1);
                        if (capTop)
                        {
                            Tri(submesh, top, b1, b0);
                        }
                    }

                    if (capBottom && r0 > 0f)
                    {
                        Tri(submesh, bottom, a0, a1);
                    }
                }
            }

            /// <summary>A flat grid at height <paramref name="y"/> facing up, centred on the origin.</summary>
            public void Grid(int submesh, Matrix4x4 m, float sx, float sz, int nx, int nz, float y)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    for (int iz = 0; iz < nz; iz++)
                    {
                        float x0 = -sx * 0.5f + sx * ix / nx, x1 = -sx * 0.5f + sx * (ix + 1) / nx;
                        float z0 = -sz * 0.5f + sz * iz / nz, z1 = -sz * 0.5f + sz * (iz + 1) / nz;
                        Quad(submesh,
                            m.MultiplyPoint3x4(new Vector3(x0, y, z0)),
                            m.MultiplyPoint3x4(new Vector3(x0, y, z1)),
                            m.MultiplyPoint3x4(new Vector3(x1, y, z1)),
                            m.MultiplyPoint3x4(new Vector3(x1, y, z0)));
                    }
                }
            }

            /// <summary>A once-subdivided icosahedron, radially perturbed by a seeded hash, scaled to fit the size, base at y = 0.</summary>
            public void Icosphere(int submesh, Vector3 size, int seed)
            {
                float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
                var points = new List<Vector3>
                {
                    new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                    new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                    new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
                };
                int[] faces =
                {
                    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
                };
                var midpoints = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (midpoints.TryGetValue(key, out int index))
                    {
                        return index;
                    }

                    points.Add((points[a] + points[b]) * 0.5f);
                    midpoints.Add(key, points.Count - 1);
                    return points.Count - 1;
                }

                var subdivided = new List<int>();
                for (int i = 0; i < faces.Length; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    subdivided.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }

                var shaped = new Vector3[points.Count];
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 dir = points[i].normalized;
                    float k = 0.8f + 0.35f * Hash01(seed + i * 101);
                    shaped[i] = new Vector3(dir.x * size.x * 0.5f * k, (dir.y * 0.5f + 0.5f) * size.y * k, dir.z * size.z * 0.5f * k);
                }

                for (int i = 0; i < subdivided.Count; i += 3)
                {
                    // Tri keeps a triangle whose (b - a) x (c - a) points outward, which Unity renders as front-facing.
                    Tri(submesh, shaped[subdivided[i]], shaped[subdivided[i + 1]], shaped[subdivided[i + 2]]);
                }
            }

            public Mesh ToMesh()
            {
                var mesh = new Mesh();
                if (vertices.Count > 65535)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }

                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++)
                {
                    mesh.SetTriangles(triangles[i], i, false);
                }

                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                return mesh;
            }

            private void Add(Vector3 position, Vector3 normal)
            {
                vertices.Add(position);
                normals.Add(normal);
                float ax = Mathf.Abs(normal.x), ay = Mathf.Abs(normal.y), az = Mathf.Abs(normal.z);
                if (ay >= ax && ay >= az)
                {
                    uvs.Add(new Vector2(position.x, position.z));
                }
                else if (ax >= az)
                {
                    uvs.Add(new Vector2(position.z, position.y));
                }
                else
                {
                    uvs.Add(new Vector2(position.x, position.y));
                }
            }
        }
    }
}
