// GameCore.Gameplay.Ui - authorable UI definitions (P1.5, catalog row 10; Studio 03 s4).
//
//   UiDocumentDefinition  one screen: its UXML, extra style sheets, draw layer and binding map
//                         (element name -> property <- source: vm:, slot:, event: or command:)
//   ScreenFlowDefinition  the game's screen set: start screen, documents, theme, message table, game title
//   ThemeDefinition       the PanelSettings asset, the theme style sheet and the shared style sheets
//
// Binding sources (see BindingHost):
//   vm:<model>.<Property>              a view model property (UI Toolkit runtime data binding)
//   slot:<owner>/<domain>.<member>@<t> a committed int32 slot; t = session | focus | <authoring id>; owner without the
//                                      "gameplay." prefix (e.g. slot:player.owner/player.stamina@focus)
//   event:<name>                       the latest value of a committed event stream (region-entered, message)
//   command:<name>                     the UI command a click or value change dispatches (CommandDispatcher)
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
    /// <summary>One binding-map entry: element name, bound property, source path and an optional format.</summary>
    [Serializable]
    public sealed class UiBindingEntry
    {
        [SerializeField] private string element = string.Empty;
        [SerializeField] private string property = "text";
        [SerializeField] private string source = string.Empty;
        [SerializeField] private string format = string.Empty;

        public UiBindingEntry()
        {
        }

        public UiBindingEntry(string element, string property, string source, string format = "")
        {
            this.element = element ?? string.Empty;
            this.property = property ?? string.Empty;
            this.source = source ?? string.Empty;
            this.format = format ?? string.Empty;
        }

        /// <summary>The element's name in the UXML.</summary>
        public string Element => element;

        /// <summary>text | value | visible | enabled | selected | items | clicked | changed.</summary>
        public string Property => property;

        public string Source => source;

        /// <summary>Optional format: a string.Format pattern ("{0} coins") or "percent:&lt;max&gt;" for numeric slots.</summary>
        public string Format => format;

        public override string ToString() => element + "." + property + " <- " + source;
    }

    /// <summary>One UI screen document.</summary>
    [Authorable("ui.document", DisplayName = "UI Document", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Live,
        Doc = "A runtime UI Toolkit document: UXML, style sheets, draw layer, the screen it belongs to and its binding map.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/UI Document", fileName = "UiDocument")]
    public sealed class UiDocumentDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "The screen (ui.screen value) the document is shown on; Hud documents also show under modal screens.")]
        [SerializeField] private UiScreen screen = UiScreen.Hud;

        [AuthorField(Doc = "Draw order; larger is drawn above.", Min = 0, Max = 100)]
        [SerializeField] private int layer;

        [AuthorRef(Category = "asset.uxml", Doc = "The document's UXML.")]
        [SerializeField] private VisualTreeAsset? uxml;

        [AuthorRef(Category = "asset.uss", Required = false, Doc = "Extra style sheets of this document.")]
        [SerializeField] private List<StyleSheet> styles = new List<StyleSheet>();

        [AuthorField(Doc = "Binding map: element -> property <- source (vm:/slot:/event:/command:).")]
        [SerializeField] private List<UiBindingEntry> bindings = new List<UiBindingEntry>();

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        /// <summary>UI documents are not baked into the world; the stamp stays empty.</summary>
        public string ContentStamp => string.Empty;

        public UiScreen Screen => screen;

        public int Layer => layer;

        public VisualTreeAsset? Uxml => uxml;

        public IReadOnlyList<StyleSheet> Styles => styles;

        public IReadOnlyList<UiBindingEntry> Bindings => bindings;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(UiScreen documentScreen, int drawLayer, VisualTreeAsset? tree)
        {
            screen = documentScreen;
            layer = drawLayer;
            uxml = tree;
        }

        public void SetStyles(IReadOnlyList<StyleSheet> sheets) => styles = new List<StyleSheet>(sheets);

        public void SetBindings(IReadOnlyList<UiBindingEntry> entries) => bindings = new List<UiBindingEntry>(entries);

        /// <summary>Adds or replaces the binding of one element property; returns true when something changed.</summary>
        public bool Bind(string element, string property, string source, string format)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (string.Equals(bindings[i].Element, element, StringComparison.Ordinal)
                    && string.Equals(bindings[i].Property, property, StringComparison.Ordinal))
                {
                    if (string.Equals(bindings[i].Source, source, StringComparison.Ordinal) && string.Equals(bindings[i].Format, format, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    bindings[i] = new UiBindingEntry(element, property, source, format);
                    return true;
                }
            }

            bindings.Add(new UiBindingEntry(element, property, source, format));
            return true;
        }

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }

    /// <summary>The panel settings, theme and shared style sheets of a game's UI.</summary>
    [Authorable("ui.theme", DisplayName = "UI Theme", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Live,
        Doc = "The PanelSettings asset, theme style sheet and shared style sheets every UI document uses.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/UI Theme", fileName = "UiTheme")]
    public sealed class ThemeDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "asset.panelSettings", Doc = "The runtime panel settings (scale mode, reference resolution, theme).")]
        [SerializeField] private PanelSettings? panelSettings;

        [AuthorRef(Category = "asset.tss", Required = false, Doc = "The theme style sheet (overrides the panel settings' theme when set).")]
        [SerializeField] private ThemeStyleSheet? themeStyleSheet;

        [AuthorRef(Category = "asset.uss", Required = false, Doc = "Style sheets added to every document.")]
        [SerializeField] private List<StyleSheet> styleSheets = new List<StyleSheet>();

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => string.Empty;

        public PanelSettings? PanelSettings => panelSettings;

        public ThemeStyleSheet? ThemeStyleSheet => themeStyleSheet;

        public IReadOnlyList<StyleSheet> StyleSheets => styleSheets;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(PanelSettings? settings, ThemeStyleSheet? theme, IReadOnlyList<StyleSheet> sheets)
        {
            panelSettings = settings;
            themeStyleSheet = theme;
            styleSheets = new List<StyleSheet>(sheets);
        }

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }

    /// <summary>One message of the UI message table (the text a ui.message key shows).</summary>
    [Serializable]
    public sealed class UiMessageEntry
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string text = string.Empty;

        public UiMessageEntry()
        {
        }

        public UiMessageEntry(string id, string text)
        {
            this.id = id ?? string.Empty;
            this.text = text ?? string.Empty;
        }

        /// <summary>Stable message id (e.g. ui.saved, save.unsafe-state); its key is PresentationSlots.KeyOf(id).</summary>
        public string Id => id;

        public string Text => text;
    }

    /// <summary>The screen set of a game: start screen, documents, theme and message table.</summary>
    [Authorable("ui.flow", DisplayName = "Screen Flow", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "The game's UI: the screen a new world starts on, every screen document, the theme and the message table.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Screen Flow", fileName = "ScreenFlow")]
    public sealed class ScreenFlowDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "Game title shown on the main menu.")]
        [SerializeField] private string gameTitle = string.Empty;

        [AuthorField(Doc = "The screen a freshly booted world starts on.")]
        [SerializeField] private UiScreen startScreen = UiScreen.Menu;

        [AuthorRef(Category = "ui.theme", Doc = "Panel settings and styles.")]
        [SerializeField] private ThemeDefinition? theme;

        [AuthorRef(Category = "ui.document", Doc = "Every screen document.")]
        [SerializeField] private List<UiDocumentDefinition> documents = new List<UiDocumentDefinition>();

        [AuthorField(Doc = "Message table: id -> text (save outcomes, refusal codes, logic showMessage ids).")]
        [SerializeField] private List<UiMessageEntry> messages = new List<UiMessageEntry>();

        [AuthorField(Unit = "ms", Min = 0, Max = 20000, Doc = "How long the region name banner stays on the HUD after a region change.")]
        [SerializeField] private int regionBannerMs = 3000;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => string.Empty;

        public string GameTitle => gameTitle;

        public UiScreen StartScreen => startScreen;

        public ThemeDefinition? Theme => theme;

        public IReadOnlyList<UiDocumentDefinition> Documents => documents;

        public IReadOnlyList<UiMessageEntry> Messages => messages;

        public int RegionBannerMs => regionBannerMs;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(string title, UiScreen start, ThemeDefinition? uiTheme, int bannerMs)
        {
            gameTitle = title ?? string.Empty;
            startScreen = start;
            theme = uiTheme;
            regionBannerMs = bannerMs;
        }

        public void SetTheme(ThemeDefinition? uiTheme) => theme = uiTheme;

        public void SetDocuments(IReadOnlyList<UiDocumentDefinition> list) => documents = new List<UiDocumentDefinition>(list);

        public void SetMessages(IReadOnlyList<UiMessageEntry> list) => messages = new List<UiMessageEntry>(list);

        /// <summary>Adds a document when it is not listed; returns true when added.</summary>
        public bool AddDocument(UiDocumentDefinition document)
        {
            if (document == null || documents.Contains(document))
            {
                return false;
            }

            documents.Add(document);
            return true;
        }

        /// <summary>The documents of one screen ordered by layer.</summary>
        public List<UiDocumentDefinition> DocumentsOf(UiScreen screen)
        {
            var list = new List<UiDocumentDefinition>();
            for (int i = 0; i < documents.Count; i++)
            {
                if (documents[i] != null && documents[i].Screen == screen)
                {
                    list.Add(documents[i]);
                }
            }

            list.Sort((l, r) => l.Layer.CompareTo(r.Layer));
            return list;
        }

        /// <summary>The message table as key -> text (UiMessageTable).</summary>
        public UiMessageTable BuildMessageTable() => new UiMessageTable(messages);

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }

    /// <summary>Message key -> text, built from a flow's message table; unknown keys show their id or the key.</summary>
    public sealed class UiMessageTable
    {
        private readonly Dictionary<int, string> texts = new Dictionary<int, string>();
        private readonly Dictionary<int, string> ids = new Dictionary<int, string>();
        private readonly List<string> collisions = new List<string>();

        public UiMessageTable(IReadOnlyList<UiMessageEntry>? entries)
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Add(entries[i].Id, entries[i].Text);
            }
        }

        /// <summary>Message ids whose keys collide with an earlier id (GP-UI-006).</summary>
        public IReadOnlyList<string> Collisions => collisions;

        public int Count => texts.Count;

        public void Add(string id, string text)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            int key = PresentationSlots.KeyOf(id);
            if (ids.TryGetValue(key, out string? existing) && !string.Equals(existing, id, StringComparison.Ordinal))
            {
                collisions.Add(id);
                return;
            }

            ids[key] = id;
            texts[key] = text ?? string.Empty;
        }

        public bool TryGet(int key, out string text) => texts.TryGetValue(key, out text!);

        public bool TryGetId(int key, out string id) => ids.TryGetValue(key, out id!);

        /// <summary>The text of a key; empty for 0, the id or "#key" for an unknown key.</summary>
        public string TextOf(int key)
        {
            if (key == 0)
            {
                return string.Empty;
            }

            if (texts.TryGetValue(key, out string? text) && text.Length > 0)
            {
                return text;
            }

            return ids.TryGetValue(key, out string? id) ? id : "#" + key.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
