// GameCore.Gameplay.Ui - ScreenFlowDefinition (P1.5, catalog row 10). Own file: Unity binds a ScriptableObject to the script named after it.
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
}
