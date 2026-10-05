// GameCore.Gameplay.Ui - UiRoot: the engine side of the UI (UIDocument, PanelSettings, theme, documents, input) (P1.5).
//
// UiRoot owns one UIDocument whose root holds one layer per UiDocumentDefinition of the flow (cloned from its UXML,
// with the theme's and the document's style sheets), bound through the BindingHost to the runtime's view models. Its
// view binder (added to every attached world) shows the layers of the committed ui.screen after each pump - HUD
// documents stay visible under the pause, journal, inventory, settings, save and load screens - refreshes slot and
// event sources, and moves focus to the first button of a newly shown screen. Keyboard/gamepad navigation and submit
// come from UiInput (Input System); Pause/Journal/Inventory intents come from the player through IUiIntentSink.
//
// Headless (batchmode without a graphics device) nothing here is created: UiRoot.Create returns null and the runtime's
// view models carry the whole UI state.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Ui;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Gameplay.Ui
{

    /// <summary>The runtime UI Toolkit root of a game.</summary>
    public sealed class UiRoot : MonoBehaviour
    {
        private readonly List<Layer> layers = new List<Layer>();
        private UIDocument? document;
        private BindingHost? bindings;
        private UiInput? input;
        private ViewBinder? viewBinder;
        private int shownScreen = -1;
        private bool clickFeedback;

        /// <summary>The feedback id a button click plays through the world's IFeedbackSink (the bank resolves sfx.ui.click).</summary>
        public const string ClickFeedback = "ui.click";

        public UiRuntime? Runtime { get; private set; }

        public ScreenFlowDefinition? Flow { get; private set; }

        public UIDocument? Document => document;

        public BindingHost? Bindings => bindings;

        /// <summary>Problems of the last build (unresolved elements, bad sources); empty when every binding resolved.</summary>
        public List<string> Problems { get; } = new List<string>();

        /// <summary>Creates the UI root under <paramref name="parent"/>; null when headless or the flow has no theme.</summary>
        public static UiRoot? Create(Transform? parent, UiRuntime runtime, ScreenFlowDefinition flow)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (flow == null)
            {
                throw new ArgumentNullException(nameof(flow));
            }

            if (BinderEnvironment.IsHeadless || flow.Theme == null || flow.Theme.PanelSettings == null)
            {
                return null;
            }

            var host = new GameObject("GameCore UI");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            UiRoot root = host.AddComponent<UiRoot>();
            root.Build(runtime, flow);
            return root;
        }

        /// <summary>Builds the document (also used by the preview tool on a hidden root).</summary>
        public void Build(UiRuntime runtime, ScreenFlowDefinition flow)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Flow = flow ?? throw new ArgumentNullException(nameof(flow));
            ThemeDefinition theme = flow.Theme ?? throw new InvalidOperationException(PresentationDiagnosticCodes.UiMissingTheme + ": the flow has no theme");
            PanelSettings source = theme.PanelSettings ?? throw new InvalidOperationException(PresentationDiagnosticCodes.UiMissingTheme + ": the theme has no PanelSettings");
            PanelSettings settings = Instantiate(source);
            settings.name = source.name + " (runtime)";
            if (theme.ThemeStyleSheet != null)
            {
                settings.themeStyleSheet = theme.ThemeStyleSheet;
            }

            document = gameObject.GetComponent<UIDocument>();
            if (document == null)
            {
                document = gameObject.AddComponent<UIDocument>();
            }

            document.panelSettings = settings;
            bindings = new BindingHost(runtime.Models, runtime.Dispatcher.Invoke);
            if (!clickFeedback)
            {
                clickFeedback = true;
                document.rootVisualElement.RegisterCallback<ClickEvent>(OnClick);
            }

            Rebuild();
            if (viewBinder == null)
            {
                viewBinder = new ViewBinder(this);
                runtime.AddViewBinder(viewBinder);
                runtime.ActivateRequested += ActivateFocused;
            }

            if (Application.isPlaying)
            {
                input ??= new UiInput(runtime);
            }
        }

        /// <summary>Re-clones every document and rebinds (after an edit of a document or the theme).</summary>
        public void Rebuild()
        {
            if (document == null || bindings == null || Flow == null || Runtime == null)
            {
                return;
            }

            VisualElement root = document.rootVisualElement;
            root.Clear();
            bindings.Clear();
            layers.Clear();
            Problems.Clear();
            root.AddToClassList("gc-root");
            ThemeDefinition? theme = Flow.Theme;
            var documents = new List<UiDocumentDefinition>();
            for (int i = 0; i < Flow.Documents.Count; i++)
            {
                if (Flow.Documents[i] != null)
                {
                    documents.Add(Flow.Documents[i]);
                }
            }

            documents.Sort((l, r) => l.Layer != r.Layer ? l.Layer.CompareTo(r.Layer) : string.CompareOrdinal(l.name, r.name));
            for (int i = 0; i < documents.Count; i++)
            {
                UiDocumentDefinition definition = documents[i];
                if (definition.Uxml == null)
                {
                    Problems.Add(PresentationDiagnosticCodes.UiMissingDocument + ": " + definition.name + " has no UXML");
                    continue;
                }

                VisualElement layer = definition.Uxml.Instantiate();
                layer.name = "layer-" + definition.name;
                layer.AddToClassList("gc-layer");
                layer.pickingMode = PickingMode.Ignore;
                layer.style.position = Position.Absolute;
                layer.style.left = 0;
                layer.style.top = 0;
                layer.style.right = 0;
                layer.style.bottom = 0;
                if (theme != null)
                {
                    for (int s = 0; s < theme.StyleSheets.Count; s++)
                    {
                        if (theme.StyleSheets[s] != null)
                        {
                            layer.styleSheets.Add(theme.StyleSheets[s]);
                        }
                    }
                }

                for (int s = 0; s < definition.Styles.Count; s++)
                {
                    if (definition.Styles[s] != null)
                    {
                        layer.styleSheets.Add(definition.Styles[s]);
                    }
                }

                UiBindingReport report = bindings.Bind(layer, definition.Bindings);
                for (int p = 0; p < report.Problems.Count; p++)
                {
                    Problems.Add(definition.name + ": " + report.Problems[p]);
                }

                root.Add(layer);
                layers.Add(new Layer(definition, layer));
            }

            shownScreen = -1;
            Apply((int)Runtime.Screen);
        }

        /// <summary>Shows the layers of <paramref name="screen"/>; returns how many layers are visible.</summary>
        public int Apply(int screen)
        {
            int visible = 0;
            var current = (UiScreen)screen;
            bool hudUnder = current == UiScreen.Hud || current == UiScreen.Pause || current == UiScreen.Journal || current == UiScreen.Inventory
                || current == UiScreen.Settings || current == UiScreen.Save || current == UiScreen.Load;
            for (int i = 0; i < layers.Count; i++)
            {
                Layer layer = layers[i];
                bool show = layer.Definition.Screen == current || (layer.Definition.Screen == UiScreen.Hud && hudUnder);
                layer.Element.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show)
                {
                    visible++;
                }
            }

            if (screen != shownScreen)
            {
                shownScreen = screen;
                FocusFirst(current);
            }

            return visible;
        }

        private void FocusFirst(UiScreen screen)
        {
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                if (layers[i].Definition.Screen != screen)
                {
                    continue;
                }

                Button? first = layers[i].Element.Q<Button>();
                if (first != null)
                {
                    first.Focus();
                    return;
                }
            }
        }

        private void OnClick(ClickEvent click)
        {
            if (click.target is Button && Runtime != null && Runtime.World != null)
            {
                Runtime.World.Presentation.Get<IFeedbackSink>()?.Play(ClickFeedback, default(System.Numerics.Vector3));
            }
        }

        private void ActivateFocused()
        {
            if (document == null)
            {
                return;
            }

            Focusable? focused = document.rootVisualElement.panel?.focusController?.focusedElement;
            if (focused is VisualElement element)
            {
                using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = element;
                    element.SendEvent(submit);
                }
            }
        }

        private void Update()
        {
            input?.Poll();
        }

        private void OnDestroy()
        {
            input?.Dispose();
            input = null;
            if (Runtime != null)
            {
                Runtime.ActivateRequested -= ActivateFocused;
            }
        }

        private int Present()
        {
            if (Runtime == null || bindings == null)
            {
                return 0;
            }

            int touched = Apply((int)Runtime.Screen);
            touched += bindings.Refresh(Runtime.ReadSlotSource, Runtime.EventValue);
            return touched;
        }

        private sealed class Layer
        {
            public Layer(UiDocumentDefinition definition, VisualElement element)
            {
                Definition = definition;
                Element = element;
            }

            public UiDocumentDefinition Definition { get; }

            public VisualElement Element { get; }
        }

        /// <summary>The engine binder: inactive headless (UiRoot is never created headless).</summary>
        private sealed class ViewBinder : IPresentationBinder
        {
            private readonly UiRoot root;

            public ViewBinder(UiRoot root)
            {
                this.root = root;
            }

            public string BinderName => "gameplay.ui.document";

            public bool IsActive => root != null && !BinderEnvironment.IsHeadless;

            public int Present(ICommittedSlotReader slots) => root != null ? root.Present() : 0;
        }
    }
}
