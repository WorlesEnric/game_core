// Hollowmere - the Studio tool that places the application's boot assets where the player can load them before any
// scene (P3.1, P1.7a A11). Discovered by Studio's tool registry like the dressing tools, so the call is journaled.
//
// Tool id (for PACKET.md):
//   hollowmere.registerApplication  write Resources/Hollowmere/Application.asset from the Boot scene's GameBoot
//
// It copies the references Boot.unity's GameBoot holds (region manifest, narrative content, player definition, NPC and
// interaction rosters) into a HollowmereApplicationAssets asset under a Resources folder, so the Boot scene stays the one
// source of truth and HollowmereApplication registers exactly what GameBoot would boot. Re-running with an unchanged
// scene changes nothing; the asset keeps its GUID.
#nullable enable
using System;
using System.IO;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using Hollowmere.Boot;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.Authoring.Tools
{
    /// <summary>The hollowmere.registerApplication operation.</summary>
    public static class HollowmereApplicationTools
    {
        public const string DefaultPath = "Assets/Hollowmere/Boot/Resources/Hollowmere/Application.asset";
        private const string ResourcesSuffix = "/Resources/" + HollowmereApplicationAssets.ResourcePath + ".asset";

        [AuthorOperation("hollowmere.registerApplication", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Doc = "Writes the application's boot assets (Resources/Hollowmere/Application.asset) from the loaded Boot scene's GameBoot, so the player registers Hollowmere's definition at SubsystemRegistration (GameApplication.Register) before any scene loads.")]
        public static HollowmereApplicationAssets RegisterApplication(
            [AuthorArg(Required = false, Doc = "Asset path; must end with /Resources/Hollowmere/Application.asset.")] string path = DefaultPath)
        {
            string target = string.IsNullOrEmpty(path) ? DefaultPath : path;
            if (!target.StartsWith("Assets/", StringComparison.Ordinal) || !target.EndsWith(ResourcesSuffix, StringComparison.Ordinal) || target.Contains(".."))
            {
                throw new ArgumentException("HM-APP-001: the application assets live at Assets/.../Resources/Hollowmere/Application.asset, not '" + target + "'");
            }

            Scene boot = SceneManager.GetSceneByPath(HollowmereDressingTools.BootScenePath);
            if (!boot.IsValid() || !boot.isLoaded)
            {
                throw new ArgumentException("HM-APP-002: open " + HollowmereDressingTools.BootScenePath + " first (the GameBoot references are read from it)");
            }

            GameBoot? gameBoot = null;
            foreach (GameObject root in boot.GetRootGameObjects())
            {
                gameBoot = root.GetComponentInChildren<GameBoot>(true);
                if (gameBoot != null)
                {
                    break;
                }
            }

            if (gameBoot == null)
            {
                throw new ArgumentException("HM-APP-003: " + HollowmereDressingTools.BootScenePath + " has no GameBoot");
            }

            var source = new SerializedObject(gameBoot);
            var manifest = source.FindProperty("manifest").objectReferenceValue as RegionManifest;
            var content = source.FindProperty("content").objectReferenceValue as GameplayContentManifest;
            var player = source.FindProperty("player").objectReferenceValue as PlayerDefinition;
            var npcs = source.FindProperty("npcs").objectReferenceValue as NpcRoster;
            var interactions = source.FindProperty("interactions").objectReferenceValue as InteractionRoster;
            if (manifest == null || content == null)
            {
                throw new ArgumentException("HM-APP-004: the Boot scene's GameBoot has no region manifest or no content manifest");
            }

            HollowmereApplicationAssets? assets = AssetDatabase.LoadAssetAtPath<HollowmereApplicationAssets>(target);
            if (assets != null && assets.Matches(manifest, content, player, npcs, interactions))
            {
                return assets;
            }

            if (assets == null)
            {
                EnsureFolder(Path.GetDirectoryName(target)!.Replace('\\', '/'));
                assets = ScriptableObject.CreateInstance<HollowmereApplicationAssets>();
                assets.name = Path.GetFileNameWithoutExtension(target);
                assets.Configure(manifest, content, player, npcs, interactions);
                AssetDatabase.CreateAsset(assets, target);
                Undo.RegisterCreatedObjectUndo(assets, "hollowmere.registerApplication");
            }
            else
            {
                Undo.RecordObject(assets, "hollowmere.registerApplication");
                assets.Configure(manifest, content, player, npcs, interactions);
            }

            EditorUtility.SetDirty(assets);
            return assets;
        }

        private static void EnsureFolder(string folder)
        {
            // Inside the engine's AssetDatabase edit block a folder created earlier in the change set exists on disk but
            // is not imported yet, so IsValidFolder is false; CreateFolder would then make a numbered duplicate
            // ("Materials 1"). The disk is the truth: a folder that exists there is left alone.
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)
                || Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), folder)))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
