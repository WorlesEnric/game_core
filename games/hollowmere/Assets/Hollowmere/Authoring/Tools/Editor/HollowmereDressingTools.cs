// Hollowmere - game-side Studio tools that dress the reference game (P3.1). Discovered by Studio's tool registry
// through the mirror authoring attributes of GameCore.Gameplay.Contracts (P1.6 reads them by name), so every call goes
// through ChangeSetEngine.Apply and lands in the journal.
//
// Tool ids (for PACKET.md):
//   hollowmere.addScenery                 place/update a procedural scenery mesh in a loaded region scene
//   hollowmere.createMaterial             create/update a URP Lit material asset
//   hollowmere.setAtmosphere              add/update the RegionAtmosphere of a loaded region scene
//   hollowmere.addLight                   add/update a point light in a loaded region scene
//   hollowmere.skinSceneObject            assign materials to (or hide) a named object of a loaded region scene
//   hollowmere.skinPrefab                 assign materials to an entity definition's view prefab
//   hollowmere.generateNpcAnimator        procedural idle/walk/talk clips + controller on an NPC view prefab
//   hollowmere.configureBootPresentation  global post-processing volume, camera post-processing/FXAA, soft shadows
//
// Every tool validates before it changes anything, records Undo for scene objects, marks what it edits dirty, refuses
// with an ArgumentException whose message starts with an HM-* code, never saves a scene, and can be re-run with the
// same arguments without creating duplicates (objects are found by name and updated in place; assets keep their GUID).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using Hollowmere.Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hollowmere.Authoring.Tools
{
    /// <summary>The hollowmere.* dressing operations.</summary>
    public static class HollowmereDressingTools
    {
        public const string MeshFolder = "Assets/Hollowmere/Scenery/Meshes";
        public const string BootScenePath = "Assets/Hollowmere/Boot/Boot.unity";
        public const string LitShader = "Universal Render Pipeline/Lit";
        public const string SceneryRoot = "Scenery";
        public const string LightsRoot = "Lights";
        public const string AtmosphereObject = "Atmosphere";
        public const string PostProcessingObject = "Post Processing";

        // ------------------------------------------------------------------ scenery

        [AuthorOperation("hollowmere.addScenery", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild, Requires = "world.region",
            Doc = "Places (or updates, by name) a procedural scenery mesh in a region scene: kinds ground, house, inn, hut, well, tree, deadTree, reeds, rock, fence, jetty, punt, shrine, ruinWall, tower, bellFrame, water, causeway, lanternPost, stall, barrel. The region scene must be loaded.")]
        public static GameObject AddScenery(
            RegionDefinition region,
            [AuthorArg(Doc = "Mesh kind.")] string kind,
            [AuthorArg(Unit = "m", Doc = "World position of the base centre.")] Vector3 position,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Heading around +Y.")] float yaw = 0f,
            [AuthorArg(Unit = "m", Required = false, Doc = "Size (x width, y height, z depth); zero = the kind's default.")] Vector3 size = default,
            [AuthorArg(Category = "asset.material", Required = false, Doc = "Materials per submesh (missing entries reuse the last).")] Material[]? materials = null,
            [AuthorArg(Required = false, Doc = "Add a static mesh collider.")] bool collider = true,
            [AuthorArg(Required = false, Doc = "Object name (default: the kind).")] string name = "")
        {
            if (!ProceduralMeshes.IsKind(kind))
            {
                throw new ArgumentException("HM-DRS-001: unknown scenery kind '" + kind + "' (known: " + string.Join(", ", ProceduralMeshes.Kinds) + ")");
            }

            if (size.x < 0f || size.y < 0f || size.z < 0f)
            {
                throw new ArgumentException("HM-DRS-002: a scenery size cannot be negative");
            }

            AuthoredRegion marker = RegionMarker(region, "hollowmere.addScenery");
            Vector3 effective = ProceduralMeshes.EffectiveSize(kind, size);
            string objectName = string.IsNullOrEmpty(name) ? kind : name;
            string meshPath = MeshPathFor(kind, effective);
            Transform root = EnsureChild(marker.transform, SceneryRoot, "hollowmere.addScenery");
            Mesh mesh = EnsureMesh(kind, effective, meshPath, marker.gameObject.scene);

            Transform? existing = root.Find(objectName);
            GameObject piece;
            if (existing == null)
            {
                piece = new GameObject(objectName);
                Undo.RegisterCreatedObjectUndo(piece, "hollowmere.addScenery");
                piece.transform.SetParent(root, false);
            }
            else
            {
                piece = existing.gameObject;
                Undo.RecordObject(piece.transform, "hollowmere.addScenery");
            }

            piece.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            piece.transform.localScale = Vector3.one;
            piece.layer = 0;
            MeshFilter filter = Ensure<MeshFilter>(piece);
            Undo.RecordObject(filter, "hollowmere.addScenery");
            filter.sharedMesh = mesh;
            MeshRenderer renderer = Ensure<MeshRenderer>(piece);
            Undo.RecordObject(renderer, "hollowmere.addScenery");
            renderer.sharedMaterials = Expand(materials, ProceduralMeshes.SubmeshCount(kind), renderer.sharedMaterials);
            renderer.shadowCastingMode = kind == "ground" || kind == "water" ? ShadowCastingMode.Off : ShadowCastingMode.On;
            MeshCollider? meshCollider = piece.GetComponent<MeshCollider>();
            if (collider)
            {
                if (meshCollider == null)
                {
                    meshCollider = Undo.AddComponent<MeshCollider>(piece);
                }

                Undo.RecordObject(meshCollider, "hollowmere.addScenery");
                meshCollider.sharedMesh = mesh;
                meshCollider.convex = false;
            }
            else if (meshCollider != null)
            {
                Undo.DestroyObjectImmediate(meshCollider);
            }

            GameObjectUtility.SetStaticEditorFlags(piece,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI);
            EditorUtility.SetDirty(piece);
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
            return piece;
        }

        /// <summary>The asset path of the shared mesh of a kind at a size (millimetre integers in the name).</summary>
        public static string MeshPathFor(string kind, Vector3 size)
        {
            return MeshFolder + "/" + kind + "_" + Millimetres(size.x) + "x" + Millimetres(size.y) + "x" + Millimetres(size.z) + ".asset";
        }

        // ------------------------------------------------------------------ materials

        [AuthorOperation("hollowmere.createMaterial", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Live,
            Doc = "Creates (or updates in place, keeping its GUID) a URP Lit material asset: base map and colour, smoothness, metallic, tiling, emission and an optional transparent surface.")]
        public static Material CreateMaterial(
            [AuthorArg(Doc = "Material asset path (Assets/.../X.mat).")] string path,
            [AuthorArg(Category = "asset.texture", Required = false, Doc = "Base map.")] Texture2D? baseMap = null,
            [AuthorArg(Required = false, Doc = "Base colour (default white).")] Color baseColor = default,
            [AuthorArg(Required = false, Min = 0, Max = 1, Doc = "Smoothness.")] float smoothness = 0.2f,
            [AuthorArg(Required = false, Min = 0, Max = 1, Doc = "Metallic.")] float metallic = 0f,
            [AuthorArg(Required = false, Doc = "Base map tiling (default 1,1).")] Vector2 tiling = default,
            [AuthorArg(Required = false, Doc = "Emission colour (black or empty = none).")] Color emission = default,
            [AuthorArg(Required = false, Doc = "Transparent (alpha-blended) surface.")] bool transparent = false)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".mat", StringComparison.Ordinal) || path.Contains(".."))
            {
                throw new ArgumentException("HM-DRS-010: a material path is Assets/.../<name>.mat, not '" + path + "'");
            }

            Shader? shader = Shader.Find(LitShader);
            if (shader == null)
            {
                throw new ArgumentException("HM-DRS-011: the URP Lit shader '" + LitShader + "' is not available in this project");
            }

            Color color = baseColor == default ? Color.white : baseColor;
            Vector2 scale = tiling == Vector2.zero ? Vector2.one : tiling;
            Material? material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;
            if (material == null)
            {
                EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            }
            else
            {
                Undo.RecordObject(material, "hollowmere.createMaterial");
                if (material.shader != shader)
                {
                    material.shader = shader;
                }
            }

            material.SetTexture("_BaseMap", baseMap);
            material.SetTextureScale("_BaseMap", scale);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
            material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            bool emissive = emission.maxColorComponent > 0f;
            material.SetColor("_EmissionColor", emissive ? emission : Color.black);
            if (emissive)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetOverrideTag("RenderType", "Transparent");
            }
            else
            {
                material.SetFloat("_Surface", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.One);
                material.SetFloat("_DstBlend", (float)BlendMode.Zero);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                material.SetFloat("_ZWrite", 1f);
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Geometry;
                material.SetOverrideTag("RenderType", "Opaque");
            }

            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
                Undo.RegisterCreatedObjectUndo(material, "hollowmere.createMaterial");
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------ atmosphere and lights

        [AuthorOperation("hollowmere.setAtmosphere", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live, Requires = "world.region",
            Doc = "Sets a region's atmosphere (fog, trilight ambient, sun, colour grading) on its 'Atmosphere' object; applied when the player enters the region. The region scene must be loaded.")]
        public static RegionAtmosphere SetAtmosphere(
            RegionDefinition region,
            [AuthorArg(Doc = "Fog colour.")] Color fogColor,
            [AuthorArg(Min = 0, Max = 1, Doc = "Exponential-squared fog density.")] float fogDensity,
            [AuthorArg(Doc = "Ambient sky colour.")] Color ambientSky,
            [AuthorArg(Doc = "Ambient equator colour.")] Color ambientEquator,
            [AuthorArg(Doc = "Ambient ground colour.")] Color ambientGround,
            [AuthorArg(Doc = "Sun colour.")] Color sunColor,
            [AuthorArg(Min = 0, Max = 8, Doc = "Sun intensity.")] float sunIntensity,
            [AuthorArg(Unit = "deg", Doc = "Sun rotation (Euler angles).")] Vector3 sunEuler,
            [AuthorArg(Required = false, Min = -5, Max = 5, Doc = "Post exposure (EV).")] float postExposure = 0f,
            [AuthorArg(Required = false, Min = -100, Max = 100, Doc = "Saturation offset.")] float saturation = 0f,
            [AuthorArg(Required = false, Min = -100, Max = 100, Doc = "Contrast offset.")] float contrast = 0f,
            [AuthorArg(Required = false, Min = 0, Max = 10, Doc = "Bloom intensity.")] float bloomIntensity = 0.3f,
            [AuthorArg(Required = false, Min = 0, Max = 1, Doc = "Vignette intensity.")] float vignetteIntensity = 0.25f,
            [AuthorArg(Required = false, Doc = "Colour filter (default white).")] Color colorFilter = default)
        {
            if (fogDensity < 0f || fogDensity > 1f)
            {
                throw new ArgumentException("HM-DRS-020: fog density must be 0..1");
            }

            if (sunIntensity < 0f)
            {
                throw new ArgumentException("HM-DRS-021: sun intensity cannot be negative");
            }

            AuthoredRegion marker = RegionMarker(region, "hollowmere.setAtmosphere");
            Transform holder = EnsureChild(marker.transform, AtmosphereObject, "hollowmere.setAtmosphere");
            RegionAtmosphere atmosphere = Ensure<RegionAtmosphere>(holder.gameObject);
            Undo.RecordObject(atmosphere, "hollowmere.setAtmosphere");
            atmosphere.Configure(fogColor, fogDensity, ambientSky, ambientEquator, ambientGround, sunColor, sunIntensity, sunEuler,
                postExposure, saturation, contrast, bloomIntensity, vignetteIntensity, colorFilter == default ? Color.white : colorFilter);
            EditorUtility.SetDirty(atmosphere);
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
            return atmosphere;
        }

        [AuthorOperation("hollowmere.addLight", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Live, Requires = "world.region",
            Doc = "Adds (or updates, by name) a realtime point light under the region's 'Lights' object (lantern glow, shrine light). The region scene must be loaded.")]
        public static Light AddLight(
            RegionDefinition region,
            [AuthorArg(Doc = "Light object name.")] string name,
            [AuthorArg(Unit = "m", Doc = "World position.")] Vector3 position,
            [AuthorArg(Doc = "Light colour.")] Color color,
            [AuthorArg(Min = 0, Max = 100, Doc = "Intensity.")] float intensity,
            [AuthorArg(Unit = "m", Min = 0.1, Max = 200, Doc = "Range.")] float range,
            [AuthorArg(Required = false, Doc = "Cast soft shadows.")] bool shadows = false)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("HM-DRS-030: a light needs a name");
            }

            if (intensity < 0f || range <= 0f)
            {
                throw new ArgumentException("HM-DRS-031: a light needs intensity >= 0 and range > 0");
            }

            AuthoredRegion marker = RegionMarker(region, "hollowmere.addLight");
            Transform root = EnsureChild(marker.transform, LightsRoot, "hollowmere.addLight");
            Transform holder = EnsureChild(root, name, "hollowmere.addLight");
            Undo.RecordObject(holder, "hollowmere.addLight");
            holder.position = position;
            Light light = Ensure<Light>(holder.gameObject);
            Undo.RecordObject(light, "hollowmere.addLight");
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            EditorUtility.SetDirty(light);
            EditorSceneManager.MarkSceneDirty(marker.gameObject.scene);
            return light;
        }

        // ------------------------------------------------------------------ skins

        [AuthorOperation("hollowmere.skinSceneObject", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live, Requires = "world.region",
            Doc = "Assigns materials to a named object of a region scene (first match, depth first), or hides it (deactivates it, keeping it in the scene). The region scene must be loaded.")]
        public static void SkinSceneObject(
            RegionDefinition region,
            [AuthorArg(Doc = "Object name.")] string objectName,
            [AuthorArg(Category = "asset.material", Doc = "Materials per submesh (missing entries reuse the last).")] Material[] materials,
            [AuthorArg(Required = false, Doc = "Hide the object instead.")] bool hide = false)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                throw new ArgumentException("HM-DRS-040: an object name is required");
            }

            AuthoredRegion marker = RegionMarker(region, "hollowmere.skinSceneObject");
            Scene scene = marker.gameObject.scene;
            GameObject? found = FindByName(scene, objectName);
            if (found == null)
            {
                throw new ArgumentException("HM-DRS-041: region scene " + scene.path + " has no object named '" + objectName + "'");
            }

            if (hide)
            {
                if (found.activeSelf)
                {
                    Undo.RecordObject(found, "hollowmere.skinSceneObject");
                    found.SetActive(false);
                }
            }
            else
            {
                if (materials == null || materials.Length == 0)
                {
                    throw new ArgumentException("HM-DRS-042: give at least one material (or hide)");
                }

                Renderer? renderer = found.GetComponent<Renderer>();
                if (renderer == null)
                {
                    throw new ArgumentException("HM-DRS-043: '" + objectName + "' has no renderer");
                }

                Undo.RecordObject(renderer, "hollowmere.skinSceneObject");
                int slots = Math.Max(1, renderer.sharedMaterials.Length);
                renderer.sharedMaterials = Expand(materials, slots, renderer.sharedMaterials);
                EditorUtility.SetDirty(renderer);
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        [AuthorOperation("hollowmere.skinPrefab", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild, Requires = "entity.definition",
            Doc = "Assigns materials to the view prefab of an entity definition: the renderer of the named child (default 'Body'), or every renderer when the child name is empty.")]
        public static void SkinPrefab(
            EntityDefinition entity,
            [AuthorArg(Category = "asset.material", Doc = "Materials per submesh (missing entries reuse the last).")] Material[] materials,
            [AuthorArg(Required = false, Doc = "Child holding the renderer (empty = every renderer).")] string childName = "Body")
        {
            if (entity == null || entity.Prefab == null)
            {
                throw new ArgumentException("HM-DRS-050: the entity definition has no view prefab");
            }

            if (materials == null || materials.Length == 0)
            {
                throw new ArgumentException("HM-DRS-051: give at least one material");
            }

            string path = AssetDatabase.GetAssetPath(entity.Prefab);
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("HM-DRS-052: the view prefab of " + entity.name + " is not an asset");
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var renderers = new List<Renderer>();
                if (string.IsNullOrEmpty(childName))
                {
                    renderers.AddRange(contents.GetComponentsInChildren<Renderer>(true));
                }
                else
                {
                    Transform? child = FindDeep(contents.transform, childName);
                    Renderer? renderer = child != null ? child.GetComponent<Renderer>() : null;
                    if (renderer == null)
                    {
                        throw new ArgumentException("HM-DRS-053: prefab " + path + " has no renderer on a child named '" + childName + "'");
                    }

                    renderers.Add(renderer);
                }

                for (int i = 0; i < renderers.Count; i++)
                {
                    if (renderers[i] is ParticleSystemRenderer || renderers[i].GetComponent<TextMesh>() != null)
                    {
                        continue;
                    }

                    renderers[i].sharedMaterials = Expand(materials, Math.Max(1, renderers[i].sharedMaterials.Length), renderers[i].sharedMaterials);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            EditorUtility.SetDirty(entity);
        }

        // ------------------------------------------------------------------ NPC animator

        [AuthorOperation("hollowmere.generateNpcAnimator", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild, Requires = "entity.definition",
            Doc = "Generates procedural Idle/Walk/Talk clips and an Animator Controller driven by the NPC animator binder's parameters (float Speed, int State: 0 idle, 1 patrol, 2 approach, 3 converse, 4 custom) and puts an Animator with it on the entity definition's view prefab.")]
        public static RuntimeAnimatorController GenerateNpcAnimator(
            EntityDefinition npcEntity,
            [AuthorArg(Doc = "Folder for the clips and the controller (Assets/...).")] string folder,
            [AuthorArg(Required = false, Doc = "Child transform the clips move (default 'Body').")] string bodyChild = "Body")
        {
            if (npcEntity == null || npcEntity.Prefab == null)
            {
                throw new ArgumentException("HM-DRS-060: the NPC entity definition has no view prefab");
            }

            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets/", StringComparison.Ordinal) || folder.Contains(".."))
            {
                throw new ArgumentException("HM-DRS-061: the animator folder must be under Assets/, not '" + folder + "'");
            }

            string prefabPath = AssetDatabase.GetAssetPath(npcEntity.Prefab);
            if (string.IsNullOrEmpty(prefabPath))
            {
                throw new ArgumentException("HM-DRS-062: the view prefab of " + npcEntity.name + " is not an asset");
            }

            Transform? body = FindDeep(npcEntity.Prefab.transform, bodyChild);
            if (body == null)
            {
                throw new ArgumentException("HM-DRS-063: prefab " + prefabPath + " has no child named '" + bodyChild + "'");
            }

            string bodyPath = AnimationUtility.CalculateTransformPath(body, npcEntity.Prefab.transform);
            Vector3 restPosition = body.localPosition;
            Vector3 restEuler = body.localEulerAngles;
            EnsureFolder(folder.TrimEnd('/'));
            string root = folder.TrimEnd('/');
            AnimationClip idle = EnsureClip(root + "/Npc_Idle.anim", "Npc_Idle", bodyPath, restPosition, restEuler, NpcMotion.Idle);
            AnimationClip walk = EnsureClip(root + "/Npc_Walk.anim", "Npc_Walk", bodyPath, restPosition, restEuler, NpcMotion.Walk);
            AnimationClip talk = EnsureClip(root + "/Npc_Talk.anim", "Npc_Talk", bodyPath, restPosition, restEuler, NpcMotion.Talk);
            AnimatorController controller = EnsureController(root + "/NpcAnimator.controller", idle, walk, talk);

            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Animator? animator = contents.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = contents.AddComponent<Animator>();
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                animator.updateMode = AnimatorUpdateMode.Normal;
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            EditorUtility.SetDirty(npcEntity);
            return controller;
        }

        /// <summary>The Animator parameters NpcAnimatorBinder writes.</summary>
        public const string SpeedParameter = "Speed";

        public const string StateParameter = "State";

        private enum NpcMotion
        {
            Idle,
            Walk,
            Talk,
        }

        private static AnimationClip EnsureClip(string path, string name, string bodyPath, Vector3 restPosition, Vector3 restEuler, NpcMotion motion)
        {
            AnimationClip? clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool created = clip == null;
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
            }

            clip.ClearCurves();
            clip.frameRate = 30f;
            float length;
            int samples;
            switch (motion)
            {
                case NpcMotion.Walk:
                    length = 0.8f;
                    samples = 16;
                    break;
                case NpcMotion.Talk:
                    length = 1.6f;
                    samples = 24;
                    break;
                default:
                    length = 3f;
                    samples = 24;
                    break;
            }

            var px = new Keyframe[samples + 1];
            var py = new Keyframe[samples + 1];
            var pz = new Keyframe[samples + 1];
            var rx = new Keyframe[samples + 1];
            var ry = new Keyframe[samples + 1];
            var rz = new Keyframe[samples + 1];
            for (int i = 0; i <= samples; i++)
            {
                float t = length * i / samples;
                float phase = (float)i / samples * Mathf.PI * 2f;
                Vector3 offset;
                Vector3 euler;
                switch (motion)
                {
                    case NpcMotion.Walk:
                        offset = new Vector3(Mathf.Sin(phase) * 0.03f, Mathf.Abs(Mathf.Sin(phase)) * 0.07f, 0f);
                        euler = new Vector3(6f, 0f, Mathf.Sin(phase) * 4f);
                        break;
                    case NpcMotion.Talk:
                        offset = new Vector3(0f, Mathf.Sin(phase * 2f) * 0.01f, 0f);
                        euler = new Vector3(Mathf.Max(0f, Mathf.Sin(phase * 2f)) * 7f, Mathf.Sin(phase) * 6f, 0f);
                        break;
                    default:
                        offset = new Vector3(0f, Mathf.Sin(phase) * 0.015f, 0f);
                        euler = new Vector3(0f, 0f, Mathf.Sin(phase) * 1.5f);
                        break;
                }

                px[i] = new Keyframe(t, restPosition.x + offset.x);
                py[i] = new Keyframe(t, restPosition.y + offset.y);
                pz[i] = new Keyframe(t, restPosition.z + offset.z);
                rx[i] = new Keyframe(t, restEuler.x + euler.x);
                ry[i] = new Keyframe(t, restEuler.y + euler.y);
                rz[i] = new Keyframe(t, restEuler.z + euler.z);
            }

            SetCurve(clip, bodyPath, "m_LocalPosition.x", px);
            SetCurve(clip, bodyPath, "m_LocalPosition.y", py);
            SetCurve(clip, bodyPath, "m_LocalPosition.z", pz);
            SetCurve(clip, bodyPath, "localEulerAnglesRaw.x", rx);
            SetCurve(clip, bodyPath, "localEulerAnglesRaw.y", ry);
            SetCurve(clip, bodyPath, "localEulerAnglesRaw.z", rz);
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created)
            {
                AssetDatabase.CreateAsset(clip, path);
            }

            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static void SetCurve(AnimationClip clip, string path, string property, Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                curve.SmoothTangents(i, 0f);
            }

            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
        }

        private static AnimatorController EnsureController(string path, AnimationClip idle, AnimationClip walk, AnimationClip talk)
        {
            AnimatorController? controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            }

            // Parameters: exactly Speed (float) and State (int).
            AnimatorControllerParameter[] parameters = controller.parameters;
            for (int i = parameters.Length - 1; i >= 0; i--)
            {
                controller.RemoveParameter(parameters[i]);
            }

            controller.AddParameter(SpeedParameter, AnimatorControllerParameterType.Float);
            controller.AddParameter(StateParameter, AnimatorControllerParameterType.Int);

            // A controller created inside the engine's AssetDatabase edit block is written before its sub-assets can be
            // added (the layer's state machine, states and transitions are lost on import). Once imported, a re-run
            // repairs it here: a missing layer or state machine is created as a sub-asset of the imported controller.
            AnimatorControllerLayer[] layers = controller.layers;
            if (layers.Length == 0)
            {
                controller.AddLayer("Base Layer");
                layers = controller.layers;
            }

            if (layers[0].stateMachine == null)
            {
                var created = new AnimatorStateMachine { name = layers[0].name, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(created, controller);
                layers[0].stateMachine = created;
                controller.layers = layers;
            }

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            ChildAnimatorState[] states = machine.states;
            for (int i = states.Length - 1; i >= 0; i--)
            {
                machine.RemoveState(states[i].state);
            }

            AnimatorStateTransition[] anyTransitions = machine.anyStateTransitions;
            for (int i = anyTransitions.Length - 1; i >= 0; i--)
            {
                machine.RemoveAnyStateTransition(anyTransitions[i]);
            }

            AnimatorState idleState = machine.AddState("Idle", new Vector3(300f, 0f, 0f));
            idleState.motion = idle;
            AnimatorState walkState = machine.AddState("Walk", new Vector3(300f, 80f, 0f));
            walkState.motion = walk;
            AnimatorState talkState = machine.AddState("Talk", new Vector3(300f, 160f, 0f));
            talkState.motion = talk;
            machine.defaultState = idleState;

            AnimatorStateTransition toWalk = AnyTransition(machine, walkState);
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, SpeedParameter);
            AnimatorStateTransition toTalk = AnyTransition(machine, talkState);
            toTalk.AddCondition(AnimatorConditionMode.Equals, 3f, StateParameter);
            toTalk.AddCondition(AnimatorConditionMode.Less, 0.1f, SpeedParameter);
            AnimatorStateTransition toIdle = AnyTransition(machine, idleState);
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, SpeedParameter);
            toIdle.AddCondition(AnimatorConditionMode.NotEqual, 3f, StateParameter);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorStateTransition AnyTransition(AnimatorStateMachine machine, AnimatorState to)
        {
            AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.2f;
            transition.canTransitionToSelf = false;
            return transition;
        }

        // ------------------------------------------------------------------ boot presentation

        [AuthorOperation("hollowmere.configureBootPresentation", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Doc = "Configures Boot.unity's presentation (Boot.unity must be loaded): a global 'Post Processing' Volume with a profile asset (ACES tonemapping, bloom, colour adjustments, vignette), post-processing and FXAA on the main camera, soft shadows on the directional light.")]
        public static void ConfigureBootPresentation(
            [AuthorArg(Doc = "Volume profile asset path (Assets/.../X.asset).")] string volumeProfilePath)
        {
            if (string.IsNullOrEmpty(volumeProfilePath) || !volumeProfilePath.StartsWith("Assets/", StringComparison.Ordinal)
                || !volumeProfilePath.EndsWith(".asset", StringComparison.Ordinal) || volumeProfilePath.Contains(".."))
            {
                throw new ArgumentException("HM-DRS-070: the volume profile path is Assets/.../<name>.asset, not '" + volumeProfilePath + "'");
            }

            Scene boot = SceneManager.GetSceneByPath(BootScenePath);
            if (!boot.IsValid() || !boot.isLoaded)
            {
                throw new ArgumentException("HM-DRS-071: " + BootScenePath + " must be loaded");
            }

            VolumeProfile? profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(volumeProfilePath);
            if (profile == null)
            {
                EnsureFolder(Path.GetDirectoryName(volumeProfilePath)!.Replace('\\', '/'));
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = Path.GetFileNameWithoutExtension(volumeProfilePath);
                AssetDatabase.CreateAsset(profile, volumeProfilePath);
            }

            Tonemapping tonemapping = ProfileOverride<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);
            Bloom bloom = ProfileOverride<Bloom>(profile);
            bloom.intensity.Override(0.3f);
            bloom.threshold.Override(1f);
            ColorAdjustments adjustments = ProfileOverride<ColorAdjustments>(profile);
            adjustments.postExposure.Override(0f);
            Vignette vignette = ProfileOverride<Vignette>(profile);
            vignette.intensity.Override(0.25f);
            EditorUtility.SetDirty(profile);

            GameObject? volumeObject = FindRoot(boot, PostProcessingObject);
            if (volumeObject == null)
            {
                volumeObject = new GameObject(PostProcessingObject);
                Undo.RegisterCreatedObjectUndo(volumeObject, "hollowmere.configureBootPresentation");
                SceneManager.MoveGameObjectToScene(volumeObject, boot);
            }

            Volume volume = Ensure<Volume>(volumeObject);
            Undo.RecordObject(volume, "hollowmere.configureBootPresentation");
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            EditorUtility.SetDirty(volume);

            Camera? camera = null;
            Light? sun = null;
            GameObject[] roots = boot.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Camera[] cameras = roots[i].GetComponentsInChildren<Camera>(true);
                for (int c = 0; c < cameras.Length && camera == null; c++)
                {
                    if (cameras[c].CompareTag("MainCamera"))
                    {
                        camera = cameras[c];
                    }
                }

                if (sun == null)
                {
                    Light[] lights = roots[i].GetComponentsInChildren<Light>(true);
                    for (int l = 0; l < lights.Length && sun == null; l++)
                    {
                        if (lights[l].type == LightType.Directional)
                        {
                            sun = lights[l];
                        }
                    }
                }
            }

            if (camera != null)
            {
                UniversalAdditionalCameraData? data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null)
                {
                    data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
                }

                Undo.RecordObject(data, "hollowmere.configureBootPresentation");
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                EditorUtility.SetDirty(data);
            }

            if (sun != null)
            {
                Undo.RecordObject(sun, "hollowmere.configureBootPresentation");
                sun.shadows = LightShadows.Soft;
                EditorUtility.SetDirty(sun);
            }

            EditorSceneManager.MarkSceneDirty(boot);
        }

        private static T ProfileOverride<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (profile.TryGet(out T existing))
            {
                existing.active = true;
                return existing;
            }

            T component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            if (EditorUtility.IsPersistent(profile))
            {
                AssetDatabase.AddObjectToAsset(component, profile);
            }

            return component;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>The AuthoredRegion of the region's loaded scene; refuses when the scene is not loaded.</summary>
        private static AuthoredRegion RegionMarker(RegionDefinition region, string tool)
        {
            if (region == null)
            {
                throw new ArgumentException("HM-DRS-090: " + tool + " needs a region");
            }

            string path = region.ScenePath;
            Scene scene = string.IsNullOrEmpty(path) ? default : SceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException("HM-DRS-091: the scene of region " + region.DisplayName + " (" + path + ") is not loaded; open it before " + tool);
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                AuthoredRegion[] markers = roots[i].GetComponentsInChildren<AuthoredRegion>(true);
                for (int m = 0; m < markers.Length; m++)
                {
                    if (markers[m].Definition == region)
                    {
                        return markers[m];
                    }
                }
            }

            throw new ArgumentException("HM-DRS-092: scene " + path + " has no AuthoredRegion for region " + region.DisplayName);
        }

        private static Transform EnsureChild(Transform parent, string name, string undo)
        {
            Transform? child = parent.Find(name);
            if (child != null)
            {
                return child;
            }

            var created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, undo);
            created.transform.SetParent(parent, false);
            created.transform.localPosition = Vector3.zero;
            created.transform.localRotation = Quaternion.identity;
            created.transform.localScale = Vector3.one;
            return created.transform;
        }

        private static T Ensure<T>(GameObject target)
            where T : Component
        {
            T? existing = target.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(target);
        }

        /// <summary>
        /// The shared mesh asset of a kind and size: the asset at <paramref name="path"/> when Unity knows it, else the mesh
        /// already used by a scenery piece of a loaded scene (a piece placed earlier in the same change set, whose asset is
        /// written but not imported yet), else a new asset.
        /// </summary>
        private static Mesh EnsureMesh(string kind, Vector3 size, string path, Scene preferred)
        {
            Mesh? mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
            {
                return mesh;
            }

            mesh = FindPlacedMesh(preferred, path);
            for (int i = 0; mesh == null && i < SceneManager.sceneCount; i++)
            {
                mesh = FindPlacedMesh(SceneManager.GetSceneAt(i), path);
            }

            if (mesh != null)
            {
                return mesh;
            }

            if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), path)))
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Mesh? imported = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (imported != null)
                {
                    return imported;
                }

                throw new ArgumentException("HM-DRS-003: mesh asset " + path + " exists on disk but is not imported yet; refresh the AssetDatabase and re-run");
            }

            EnsureFolder(MeshFolder);
            Mesh built = ProceduralMeshes.Build(kind, size);
            built.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(built, path);
            return built;
        }

        private static Mesh? FindPlacedMesh(Scene scene, string path)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                MeshFilter[] filters = roots[i].GetComponentsInChildren<MeshFilter>(true);
                for (int f = 0; f < filters.Length; f++)
                {
                    Mesh? shared = filters[f].sharedMesh;
                    // A mesh created earlier in the same change set has no asset path until the batch is imported; it
                    // carries the file name of its asset.
                    string sharedPath = shared != null ? AssetDatabase.GetAssetPath(shared) : string.Empty;
                    if (shared != null && (string.Equals(sharedPath, path, StringComparison.Ordinal)
                        || (sharedPath.Length == 0 && string.Equals(shared.name, Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal))))
                    {
                        return shared;
                    }
                }
            }

            return null;
        }

        private static Material[] Expand(Material[]? given, int slots, Material[] current)
        {
            int count = Math.Max(1, slots);
            var result = new Material[count];
            for (int i = 0; i < count; i++)
            {
                if (given != null && given.Length > 0)
                {
                    result[i] = given[Math.Min(i, given.Length - 1)];
                }
                else
                {
                    result[i] = i < current.Length ? current[i] : (current.Length > 0 ? current[current.Length - 1] : null!);
                }
            }

            return result;
        }

        private static GameObject? FindByName(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                {
                    return roots[i];
                }
            }

            for (int i = 0; i < roots.Length; i++)
            {
                Transform? found = FindDeep(roots[i].transform, name);
                if (found != null)
                {
                    return found.gameObject;
                }
            }

            return null;
        }

        private static GameObject? FindRoot(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static Transform? FindDeep(Transform parent, string name)
        {
            if (parent.name == name)
            {
                return parent;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform? found = FindDeep(parent.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static string Millimetres(float metres) => Mathf.RoundToInt(metres * 1000f).ToString(CultureInfo.InvariantCulture);

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
