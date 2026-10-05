// GameCore.Gameplay.Ui.Editor - UI authoring operations (P1.5, catalog row 10; Studio 03 s4/s5).
//
// Inspector-free static tools, each one [AuthorOperation] with a stable tool id. A tool validates first and changes
// nothing when it refuses (an ArgumentException whose message starts with the GP-UI code); an accepted edit records
// Undo and marks the asset dirty.
//
//   ui.bind           bind an element property of a document to a source (vm:/slot:/event:/command:)
//   ui.setCommand     bind a button's click (or a control's change) to a UI command
//   ui.addScreen      add a screen document (UXML created when missing) to a flow
//   ui.setTheme       set a flow's theme, or a theme's panel settings / theme style sheet / shared style sheets
//   ui.previewScreen  render one screen with a given view-model state into a PNG (graphical Editor only)
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Contracts;
using GameCore.Rules.Gameplay.Ui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Gameplay.Ui.Editor
{
    /// <summary>The ui.* authoring operations.</summary>
    public static class UiTools
    {
        [AuthorOperation("ui.bind", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live, Validator = typeof(UiValidator),
            TargetKinds = new[] { AuthoringKind.Definition, AuthoringKind.UiElement },
            Doc = "Binds an element property (text, value, visible, enabled, selected, items) of a UI document to a vm:, slot: or event: source.")]
        public static UiBindingEntry Bind(
            UiDocumentDefinition document,
            [AuthorArg(Doc = "Element name in the document's UXML.")] string element,
            [AuthorArg(Doc = "vm:<model>.<Property> | slot:<owner>/<domain>.<member>@<target> | event:<name>.")] string source,
            [AuthorArg(Required = false, Doc = "text | value | visible | enabled | selected | items.")] string property = "text",
            [AuthorArg(Required = false, Doc = "Format: a {0} pattern or percent:<max>; for items, command:<prefix>.")] string format = "")
        {
            if (property == "clicked" || property == "changed")
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiBadSource + ": use ui.setCommand for '" + property + "'");
            }

            return Apply(document, element, property, source, format, "ui.bind");
        }

        [AuthorOperation("ui.setCommand", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live, Validator = typeof(UiValidator),
            TargetKinds = new[] { AuthoringKind.Definition, AuthoringKind.UiElement },
            Doc = "Binds a button click (or a slider/toggle/dropdown change) to a UI command (open.<screen>, close, newgame, save.<n>, choose.<i>, volume.<channel>, ...).")]
        public static UiBindingEntry SetCommand(
            UiDocumentDefinition document,
            [AuthorArg(Doc = "Element name in the document's UXML.")] string element,
            [AuthorArg(Doc = "The UI command name (CommandDispatcher).")] string command,
            [AuthorArg(Required = false, Doc = "clicked (buttons) or changed (value controls).")] string property = "clicked")
        {
            if (property != "clicked" && property != "changed")
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiBadSource + ": a command binds 'clicked' or 'changed', not '" + property + "'");
            }

            return Apply(document, element, property, "command:" + command, string.Empty, "ui.setCommand");
        }

        [AuthorOperation("ui.addScreen", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Live, Validator = typeof(UiValidator),
            Doc = "Adds a screen document to a flow: creates the UiDocumentDefinition (and a minimal UXML when the path does not exist).")]
        public static UiDocumentDefinition AddScreen(
            ScreenFlowDefinition flow,
            [AuthorArg(Doc = "The screen the document is shown on.")] UiScreen screen,
            [AuthorArg(Doc = "Project path of the UXML (created when missing).")] string uxmlPath,
            [AuthorArg(Required = false, Min = 0, Max = 100, Doc = "Draw layer.")] int layer = 10,
            [AuthorArg(Required = false, Doc = "Asset path of the new definition; defaults next to the UXML.")] string assetPath = "")
        {
            if (flow == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiMissingDocument + ": a screen flow is required");
            }

            if (!ScreenFlowRules.IsKnown((int)screen))
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiUnknownScreen + ": screen " + (int)screen + " is not defined");
            }

            if (string.IsNullOrEmpty(uxmlPath) || !uxmlPath.StartsWith("Assets/", StringComparison.Ordinal) || !uxmlPath.EndsWith(".uxml", StringComparison.Ordinal))
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiMissingDocument + ": the UXML path must be Assets/....uxml");
            }

            for (int i = 0; i < flow.Documents.Count; i++)
            {
                if (flow.Documents[i] != null && flow.Documents[i].Screen == screen && flow.Documents[i].Layer == layer)
                {
                    throw new ArgumentException(PresentationDiagnosticCodes.UiDuplicateScreen + ": " + flow.Documents[i].name + " already uses screen " + screen + " layer " + layer);
                }
            }

            VisualTreeAsset? tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (tree == null)
            {
                string root = screen.ToString().ToLowerInvariant() + "-root";
                Directory.CreateDirectory(Path.GetDirectoryName(uxmlPath) ?? "Assets");
                File.WriteAllText(uxmlPath,
                    "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\">\n    <ui:VisualElement name=\"" + root + "\" class=\"gc-screen\" />\n</ui:UXML>\n");
                AssetDatabase.ImportAsset(uxmlPath);
                tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            }

            string path = string.IsNullOrEmpty(assetPath) ? Path.ChangeExtension(uxmlPath, null) + ".asset" : assetPath;
            var document = ScriptableObject.CreateInstance<UiDocumentDefinition>();
            document.EnsureAuthoringId();
            document.Configure(screen, layer, tree);
            AssetDatabase.CreateAsset(document, path);
            Undo.RegisterCreatedObjectUndo(document, "ui.addScreen");
            Undo.RecordObject(flow, "ui.addScreen");
            flow.AddDocument(document);
            EditorUtility.SetDirty(flow);
            AssetDatabase.SaveAssets();
            return document;
        }

        [AuthorOperation("ui.setTheme", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live, Validator = typeof(UiValidator),
            Doc = "Sets a flow's theme (panel settings, theme style sheet and shared style sheets).")]
        public static ThemeDefinition SetTheme(
            ScreenFlowDefinition flow,
            [AuthorArg(Category = "ui.theme", Doc = "The theme to use.")] ThemeDefinition theme,
            [AuthorArg(Required = false, Category = "asset.panelSettings", Doc = "Replace the theme's panel settings.")] PanelSettings? panelSettings = null,
            [AuthorArg(Required = false, Category = "asset.tss", Doc = "Replace the theme style sheet.")] ThemeStyleSheet? themeStyleSheet = null,
            [AuthorArg(Required = false, Category = "asset.uss", Doc = "Replace the shared style sheets.")] StyleSheet[]? styleSheets = null)
        {
            if (flow == null || theme == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiMissingTheme + ": a flow and a theme are required");
            }

            PanelSettings? settings = panelSettings != null ? panelSettings : theme.PanelSettings;
            if (settings == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiMissingTheme + ": the theme has no PanelSettings");
            }

            Undo.RecordObject(theme, "ui.setTheme");
            theme.Configure(settings, themeStyleSheet != null ? themeStyleSheet : theme.ThemeStyleSheet,
                styleSheets != null ? (IReadOnlyList<StyleSheet>)styleSheets : theme.StyleSheets);
            theme.EnsureAuthoringId();
            EditorUtility.SetDirty(theme);
            Undo.RecordObject(flow, "ui.setTheme");
            flow.SetTheme(theme);
            EditorUtility.SetDirty(flow);
            return theme;
        }

        [AuthorOperation("ui.previewScreen", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Doc = "Renders one screen of a flow with a view-model state (model.Property=value;...) into a PNG; graphical Editor only.")]
        public static UiPreviewResult PreviewScreen(
            ScreenFlowDefinition flow,
            [AuthorArg(Doc = "The screen to render.")] UiScreen screen,
            [AuthorArg(Required = false, Doc = "View-model state: model.Property=value pairs separated by ';' (arrays use '|').")] string state = "",
            [AuthorArg(Required = false, Doc = "Output PNG path; defaults under Library/GameCoreStudio/UiPreview.")] string outputPath = "",
            [AuthorArg(Required = false, Min = 64, Max = 4096, Doc = "Width in pixels.")] int width = 1280,
            [AuthorArg(Required = false, Min = 64, Max = 4096, Doc = "Height in pixels.")] int height = 720)
        {
            using (UiPreviewSession session = UiPreviewSession.Begin(flow, screen, state, width, height))
            {
                if (!session.Available)
                {
                    return session.Result;
                }

                EditorApplication.QueuePlayerLoopUpdate();
                return session.Capture(outputPath);
            }
        }

        private static UiBindingEntry Apply(UiDocumentDefinition document, string element, string property, string source, string format, string tool)
        {
            if (document == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiMissingDocument + ": a UI document is required");
            }

            if (document.Uxml == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.UiMissingDocument + ": " + document.name + " has no UXML");
            }

            var entry = new UiBindingEntry(element, property, source, format);
            UiBindingReport report = BindingHost.Check(document.Uxml.Instantiate(), new[] { entry }, new UiViewModels());
            if (!report.Ok)
            {
                throw new ArgumentException(report.Problems[0]);
            }

            Undo.RecordObject(document, tool);
            document.EnsureAuthoringId();
            document.Bind(element, property, source, format ?? string.Empty);
            EditorUtility.SetDirty(document);
            return entry;
        }
    }
}
