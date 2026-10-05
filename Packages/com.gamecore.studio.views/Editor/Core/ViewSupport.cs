// GameCore.Studio.Views - small helpers several views share: region residency, staleness, file export.
#nullable enable
using System;
using System.IO;
using System.Text;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Views
{
    public static class ViewSupport
    {
        /// <summary>
        /// The residency of a region node: in Play Mode the running streamer's answer (through the gameplay bridge), in
        /// Edit Mode whether the region's scene (its <c>scenePath</c> field) is open.
        /// </summary>
        public static string Residency(StudioViewContext context, IndexGraph graph, string regionKey)
        {
            IndexNode? region = graph.Node(regionKey);
            if (region == null)
            {
                return string.Empty;
            }

            if (context.IsPlaying && region.Ref.AuthoringId != null && context.Gameplay.TryResidency(region.Ref.AuthoringId, out string live))
            {
                return live;
            }

            string? scenePath = IndexGraph.StringField(region, "scenePath");
            if (string.IsNullOrEmpty(scenePath))
            {
                return string.Empty;
            }

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            return scene.IsValid() && scene.isLoaded ? "Resident" : "Unloaded";
        }

        public static Color ResidencyColor(string residency)
        {
            switch (residency)
            {
                case "Resident": return ViewPalette.Good;
                case "Loading": return ViewPalette.Info;
                case "Unloading": return ViewPalette.Warn;
                case "Unloaded": return ViewPalette.Muted;
                default: return ViewPalette.Neutral;
            }
        }

        /// <summary>Null when current; else the first stale reason (Destroyed, RegionUnloaded, StampChanged...).</summary>
        public static string? StaleReason(StudioRuntime runtime, AuthoringRef reference)
        {
            if (reference.Kind == AuthoringKind.Location || (reference.Path != null && reference.Path.StartsWith(IndexGraph.ByNamePrefix, StringComparison.Ordinal)))
            {
                return reference.Kind == AuthoringKind.Location ? null : "Unresolved";
            }

            ResolveResult result = runtime.Resolver.Resolve(reference);
            return result.IsCurrent ? null : (result.Stale.Count > 0 ? result.Stale[0].Reason.ToString() : "Stale");
        }

        /// <summary>Writes text to a file the user picks (and copies it to the clipboard); returns the path or null.</summary>
        public static string? Export(string title, string defaultName, string extension, string content)
        {
            EditorGUIUtility.systemCopyBuffer = content;
            string path = EditorUtility.SaveFilePanel(title, string.Empty, defaultName, extension);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
    }
}
