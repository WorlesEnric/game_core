// Hollowmere.P2_1.Evidence - captures the Studio windows' own pixels (never the desktop): each open window of the
// GameCore.Studio.UI namespace is repainted and read back from its editor view (GUIView.GrabPixels, reached by
// reflection because it is internal), and the grabs are composed at their screen positions on a neutral background.
// Other applications on the host display can therefore never appear in the evidence.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P2_1.Evidence
{
    /// <summary>Composes PNGs of the Studio windows.</summary>
    public static class UnityWindowCapture
    {
        public const string StudioNamespace = "GameCore.Studio.UI";

        /// <summary>Writes the composite PNG; returns a problem description, or null on success.</summary>
        public static string? CaptureStudio(string path, bool flipVertically)
        {
            List<EditorWindow> windows = new List<EditorWindow>();
            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window != null && window.GetType().Namespace == StudioNamespace)
                {
                    windows.Add(window);
                }
            }

            if (windows.Count == 0)
            {
                return "no Studio window is open";
            }

            // Utility windows (the first-run guide) float above the tiled panels.
            windows.Sort((left, right) => (left.GetType().Name == "FirstRunWizardWindow" ? 1 : 0).CompareTo(right.GetType().Name == "FirstRunWizardWindow" ? 1 : 0));

            float scale = EditorGUIUtility.pixelsPerPoint;
            Rect union = windows[0].position;
            foreach (EditorWindow window in windows)
            {
                union = Rect.MinMaxRect(Mathf.Min(union.xMin, window.position.xMin), Mathf.Min(union.yMin, window.position.yMin), Mathf.Max(union.xMax, window.position.xMax), Mathf.Max(union.yMax, window.position.yMax));
            }

            int width = Mathf.Clamp(Mathf.RoundToInt(union.width * scale), 16, 4096);
            int height = Mathf.Clamp(Mathf.RoundToInt(union.height * scale), 16, 4096);
            Texture2D composite = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            Color32[] background = new Color32[width * height];
            for (int i = 0; i < background.Length; i++)
            {
                background[i] = new Color32(40, 40, 46, 255);
            }

            composite.SetPixels32(background);
            List<string> problems = new List<string>();
            foreach (EditorWindow window in windows)
            {
                Texture2D? grab = Grab(window, scale, flipVertically, out string? problem);
                if (grab == null)
                {
                    problems.Add(window.titleContent.text + ": " + problem);
                    continue;
                }

                int x = Mathf.RoundToInt((window.position.x - union.x) * scale);
                int yTop = Mathf.RoundToInt((window.position.y - union.y) * scale);
                int w = Mathf.Min(grab.width, width - x);
                int h = Mathf.Min(grab.height, height - yTop);
                if (w > 0 && h > 0)
                {
                    Color[] pixels = grab.GetPixels(0, grab.height - h, w, h);
                    composite.SetPixels(x, height - yTop - h, w, h, pixels);
                }

                UnityEngine.Object.DestroyImmediate(grab);
            }

            composite.Apply(false, false);
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(composite));
            UnityEngine.Object.DestroyImmediate(composite);
            return problems.Count == 0 ? null : string.Join("; ", problems);
        }

        private static Texture2D? Grab(EditorWindow window, float scale, bool flip, out string? problem)
        {
            problem = null;
            FieldInfo? parentField = typeof(EditorWindow).GetField("m_Parent", BindingFlags.NonPublic | BindingFlags.Instance);
            object? parent = parentField?.GetValue(window);
            if (parent == null)
            {
                problem = "no host view";
                return null;
            }

            MethodInfo? repaint = typeof(EditorWindow).GetMethod("RepaintImmediately", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (repaint != null)
            {
                repaint.Invoke(window, null);
            }
            else
            {
                window.Repaint();
            }

            MethodInfo? grabPixels = FindMethod(parent.GetType(), "GrabPixels", new[] { typeof(RenderTexture), typeof(Rect) });
            PropertyInfo? positionProperty = FindProperty(parent.GetType(), "position");
            if (grabPixels == null || positionProperty == null)
            {
                problem = "GUIView.GrabPixels is not available";
                return null;
            }

            Rect viewRect = (Rect)positionProperty.GetValue(parent)!;
            int width = Mathf.Clamp(Mathf.RoundToInt(viewRect.width * scale), 4, 4096);
            int height = Mathf.Clamp(Mathf.RoundToInt(viewRect.height * scale), 4, 4096);
            RenderTexture target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture? previous = RenderTexture.active;
            try
            {
                target.Create();
                grabPixels.Invoke(parent, new object[] { target, new Rect(0f, 0f, width, height) });
                RenderTexture.active = target;
                Texture2D grab = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                grab.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                if (flip)
                {
                    Color[] pixels = grab.GetPixels();
                    Color[] flipped = new Color[pixels.Length];
                    for (int row = 0; row < height; row++)
                    {
                        Array.Copy(pixels, row * width, flipped, (height - 1 - row) * width, width);
                    }

                    grab.SetPixels(flipped);
                }

                grab.Apply(false, false);
                return grab;
            }
            catch (TargetInvocationException error)
            {
                problem = "GrabPixels failed: " + (error.InnerException?.Message ?? error.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static MethodInfo? FindMethod(Type? type, string name, Type[] parameters)
        {
            for (; type != null; type = type.BaseType)
            {
                MethodInfo? method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, parameters, null);
                if (method != null)
                {
                    return method;
                }
            }

            return null;
        }

        private static PropertyInfo? FindProperty(Type? type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                PropertyInfo? property = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null && property.PropertyType == typeof(Rect))
                {
                    return property;
                }
            }

            return null;
        }
    }
}
