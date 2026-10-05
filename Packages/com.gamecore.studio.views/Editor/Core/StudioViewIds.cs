// GameCore.Studio.Views - window ids and menu paths of the nonvisual views (docs/studio/02-architecture.md s1, SR-1.7).
// The ids are the stable names an integrator (P2.1's viewport, the creator guide, automation) uses to open a view;
// the menu paths are where a creator finds them. Plugin-provided views register through IStudioViewProvider.
#nullable enable

namespace GameCore.Studio.Views
{
    /// <summary>Stable ids and menu paths of the Studio views.</summary>
    public static class StudioViewIds
    {
        public const string MenuRoot = "GameCore/Studio/";

        public const string Relationships = "gamecore.studio.views.relationships";
        public const string Dialogue = "gamecore.studio.views.dialogue";
        public const string Quests = "gamecore.studio.views.quests";
        public const string World = "gamecore.studio.views.world";
        public const string Tables = "gamecore.studio.views.tables";
        public const string Changes = "gamecore.studio.views.changes";

        public const string RelationshipsMenu = MenuRoot + "Relationships";
        public const string DialogueMenu = MenuRoot + "Dialogue";
        public const string QuestsMenu = MenuRoot + "Quests";
        public const string WorldMenu = MenuRoot + "World";
        public const string TablesMenu = MenuRoot + "Tables";
        public const string ChangesMenu = MenuRoot + "Changes";

        /// <summary>Every built-in view id, in menu order.</summary>
        public static readonly System.Collections.Generic.IReadOnlyList<string> All = System.Array.AsReadOnly(new[] { Relationships, Dialogue, Quests, World, Tables, Changes });

        /// <summary>The minimum individual view panel; the overall Studio layout is at least 1280x720 (D6).</summary>
        public const float MinWidth = 640f;
        public const float MinHeight = 360f;

        /// <summary>The size the evidence capture and a fresh floating window use.</summary>
        public const float LayoutMinWidth = 1280f;
        public const float LayoutMinHeight = 720f;
        public const float DefaultWidth = LayoutMinWidth;
        public const float DefaultHeight = LayoutMinHeight;
    }
}
