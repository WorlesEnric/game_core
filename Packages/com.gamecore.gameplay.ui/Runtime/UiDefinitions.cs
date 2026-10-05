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
