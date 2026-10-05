// GameCore.Gameplay.Ui - UiDocumentDefinition (P1.5, catalog row 10). Own file: Unity binds a ScriptableObject to the script named after it.
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

        [AuthorRef(Category = "asset.uxml", Structural = true, Doc = "The document's UXML (its element layout).")]
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
}
