// GameCore.Gameplay.Ui - ThemeDefinition (P1.5, catalog row 10). Own file: Unity binds a ScriptableObject to the script named after it.
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
}
