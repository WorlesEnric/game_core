// GameCore.Gameplay.Ui.Editor - the UI validator with stable codes (P1.5, catalog row 10).
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Rules.Gameplay.Ui;
using UnityEngine.UIElements;

namespace GameCore.Gameplay.Ui.Editor
{
    /// <summary>Validates screen flows and UI documents.</summary>
    [AuthorValidator("ui.validator", Codes = new[]
    {
        PresentationDiagnosticCodes.UiMissingDocument,
        PresentationDiagnosticCodes.UiUnknownElement,
        PresentationDiagnosticCodes.UiBadSource,
        PresentationDiagnosticCodes.UiUnknownScreen,
        PresentationDiagnosticCodes.UiDuplicateScreen,
        PresentationDiagnosticCodes.UiMessageKeyCollision,
        PresentationDiagnosticCodes.UiMissingTheme,
    })]
    public static class UiValidator
    {
        /// <summary>Problems of one document: missing UXML, unresolved elements, bad sources.</summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(UiDocumentDefinition document)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (document == null)
            {
                return diagnostics;
            }

            string subject = string.IsNullOrEmpty(document.AuthoringId) ? document.name : document.AuthoringId;
            if (!ScreenFlowRules.IsKnown((int)document.Screen))
            {
                diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.UiUnknownScreen, subject, document.name + " names screen " + (int)document.Screen));
            }

            if (document.Uxml == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.UiMissingDocument, subject, document.name + " has no UXML"));
                return diagnostics;
            }

            VisualElement tree = document.Uxml.Instantiate();
            UiBindingReport report = BindingHost.Check(tree, document.Bindings, new UiViewModels());
            for (int i = 0; i < report.Problems.Count; i++)
            {
                string problem = report.Problems[i];
                string code = problem.StartsWith(PresentationDiagnosticCodes.UiUnknownElement, System.StringComparison.Ordinal)
                    ? PresentationDiagnosticCodes.UiUnknownElement
                    : PresentationDiagnosticCodes.UiBadSource;
                diagnostics.Add(new GameplayDiagnostic(code, subject, document.name + ": " + problem));
            }

            return diagnostics;
        }

        /// <summary>Problems of a flow: theme, documents, duplicates, message keys.</summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(ScreenFlowDefinition flow)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (flow == null)
            {
                return diagnostics;
            }

            string subject = string.IsNullOrEmpty(flow.AuthoringId) ? flow.name : flow.AuthoringId;
            if (flow.Theme == null || flow.Theme.PanelSettings == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.UiMissingTheme, subject, flow.name + " has no theme with PanelSettings"));
            }

            if (!ScreenFlowRules.IsKnown((int)flow.StartScreen))
            {
                diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.UiUnknownScreen, subject, flow.name + " starts on an unknown screen"));
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < flow.Documents.Count; i++)
            {
                UiDocumentDefinition document = flow.Documents[i];
                if (document == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.UiMissingDocument, subject, flow.name + " lists an empty document slot " + i));
                    continue;
                }

                if (!seen.Add((int)document.Screen + ":" + document.Layer))
                {
                    diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.UiDuplicateScreen, subject,
                        document.name + " repeats screen " + document.Screen + " layer " + document.Layer));
                }

                diagnostics.AddRange(Validate(document));
            }

            UiMessageTable table = flow.BuildMessageTable();
            for (int i = 0; i < table.Collisions.Count; i++)
            {
                diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.UiMessageKeyCollision, subject,
                    "message id '" + table.Collisions[i] + "' collides with an earlier id"));
            }

            return diagnostics;
        }
    }
}
