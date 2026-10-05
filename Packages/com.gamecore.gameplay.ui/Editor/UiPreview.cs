// GameCore.Gameplay.Ui.Editor - ui.previewScreen: one screen rendered into a RenderTexture (P1.5, catalog row 10).
//
// A preview builds the flow's documents in a hidden UIDocument (HideAndDontSave) whose runtime PanelSettings render into
// a RenderTexture, applies a view-model state to a world-less UiRuntime, shows the requested screen and reads the
// texture back into a PNG. It needs a graphics device: under -nographics Begin reports the preview unavailable
// (GP-UI-008) and nothing is created. Callers that can wait (an EditMode [UnityTest]) let a few editor frames pass
// between Begin and Capture so the panel is laid out and repainted.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Gameplay.Ui.Editor
{
    /// <summary>The outcome of a preview.</summary>
    public sealed class UiPreviewResult
    {
        public bool Skipped { get; internal set; }

        /// <summary>Why the preview was skipped or failed (a GP-UI code and text); empty on success.</summary>
        public string Reason { get; internal set; } = string.Empty;

        public string Path { get; internal set; } = string.Empty;

        public int Width { get; internal set; }

        public int Height { get; internal set; }

        /// <summary>True when the captured image is not one flat colour (something was drawn).</summary>
        public bool Rendered { get; internal set; }

        public int VisibleLayers { get; internal set; }

        public override string ToString() => Skipped ? "skipped: " + Reason : Path + " (" + Width + "x" + Height + (Rendered ? ", rendered" : ", blank") + ")";
    }

    /// <summary>A hidden preview panel for one screen.</summary>
    public sealed class UiPreviewSession : IDisposable
    {
        private GameObject? host;
        private RenderTexture? texture;

        private UiPreviewSession()
        {
        }

        public bool Available { get; private set; }

        public UiPreviewResult Result { get; } = new UiPreviewResult();

        public UiRuntime? Runtime { get; private set; }

        public UiRoot? Root { get; private set; }

        /// <summary>Builds the hidden panel; check <see cref="Available"/>.</summary>
        public static UiPreviewSession Begin(ScreenFlowDefinition flow, UiScreen screen, string state, int width, int height)
        {
            var session = new UiPreviewSession();
            session.Result.Width = Math.Max(64, Math.Min(4096, width));
            session.Result.Height = Math.Max(64, Math.Min(4096, height));
            if (flow == null)
            {
                return session.Skip(PresentationDiagnosticCodes.UiMissingDocument + ": a screen flow is required");
            }

            if (BinderEnvironment.IsHeadless || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return session.Skip(PresentationDiagnosticCodes.UiPreviewUnavailable + ": no graphics device (-nographics); run the preview in a graphical Editor");
            }

            if (flow.Theme == null || flow.Theme.PanelSettings == null)
            {
                return session.Skip(PresentationDiagnosticCodes.UiMissingTheme + ": the flow has no theme with PanelSettings");
            }

            session.texture = new RenderTexture(session.Result.Width, session.Result.Height, 24, RenderTextureFormat.ARGB32) { name = "UiPreview" };
            session.texture.Create();
            session.host = new GameObject("GameCore UI Preview") { hideFlags = HideFlags.HideAndDontSave };
            var runtime = new UiRuntime(new UiRuntimeOptions { GameTitle = flow.GameTitle, Messages = flow.BuildMessageTable() });
            ApplyState(runtime.Models, state);
            runtime.Models.Screen.Screen = (int)screen;
            UiRoot root = session.host.AddComponent<UiRoot>();
            root.Build(runtime, flow);
            if (root.Document != null && root.Document.panelSettings != null)
            {
                root.Document.panelSettings.targetTexture = session.texture;
                root.Document.panelSettings.clearColor = true;
            }

            session.Result.VisibleLayers = root.Apply((int)screen);
            session.Runtime = runtime;
            session.Root = root;
            session.Available = true;
            return session;
        }

        /// <summary>Reads the texture into a PNG at <paramref name="outputPath"/> (default under Library/GameCoreStudio/UiPreview).</summary>
        public UiPreviewResult Capture(string outputPath)
        {
            if (!Available || texture == null)
            {
                return Result;
            }

            string path = string.IsNullOrEmpty(outputPath)
                ? System.IO.Path.Combine("Library", "GameCoreStudio", "UiPreview",
                    "preview-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + ".png")
                : outputPath;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".");
            RenderTexture? previous = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                image.Apply();
                Color32[] pixels = image.GetPixels32();
                bool varied = false;
                for (int i = 1; i < pixels.Length && !varied; i++)
                {
                    varied = !pixels[i].Equals(pixels[0]);
                }

                File.WriteAllBytes(path, image.EncodeToPNG());
                Result.Path = path;
                Result.Rendered = varied;
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
            }

            return Result;
        }

        public void Dispose()
        {
            if (host != null)
            {
                UnityEngine.Object.DestroyImmediate(host);
                host = null;
            }

            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
                texture = null;
            }
        }

        /// <summary>
        /// Applies "model.Property=value;..." to the view models: strings verbatim, ints, floats (invariant), booleans
        /// (true/false) and string arrays ('|'-separated). Unknown models or properties are ignored.
        /// </summary>
        public static int ApplyState(UiViewModels models, string state)
        {
            if (string.IsNullOrEmpty(state))
            {
                return 0;
            }

            int applied = 0;
            string[] pairs = state.Split(';');
            for (int i = 0; i < pairs.Length; i++)
            {
                int equals = pairs[i].IndexOf('=');
                int dot = pairs[i].IndexOf('.');
                if (equals <= 0 || dot <= 0 || dot > equals)
                {
                    continue;
                }

                UiViewModel? model = models.Find(pairs[i].Substring(0, dot).Trim());
                PropertyInfo? property = model?.GetType().GetProperty(pairs[i].Substring(dot + 1, equals - dot - 1).Trim(), BindingFlags.Public | BindingFlags.Instance);
                if (model == null || property == null || property.GetSetMethod() == null)
                {
                    continue;
                }

                string text = pairs[i].Substring(equals + 1);
                object? value = Parse(property.PropertyType, text);
                if (value != null)
                {
                    property.SetValue(model, value);
                    applied++;
                }
            }

            return applied;
        }

        private static object? Parse(Type type, string text)
        {
            if (type == typeof(string))
            {
                return text;
            }

            if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
            {
                return i;
            }

            if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                return f;
            }

            if (type == typeof(bool) && bool.TryParse(text, out bool b))
            {
                return b;
            }

            if (type == typeof(string[]))
            {
                return text.Length == 0 ? Array.Empty<string>() : text.Split('|');
            }

            return null;
        }

        private UiPreviewSession Skip(string reason)
        {
            Result.Skipped = true;
            Result.Reason = reason;
            return this;
        }
    }
}
