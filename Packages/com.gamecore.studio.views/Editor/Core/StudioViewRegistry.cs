// GameCore.Studio.Views - the extension point for plugin views (SADR-007 plugin model): a gameplay or game package
// adds a structural view by implementing IStudioViewProvider (a public parameterless class); the registry finds it
// through TypeCache, and StudioViewRegistry.Open(viewId) opens it in a PluginViewWindow with the same context,
// selection and gameplay bridges as the built-in views. Built-in views are providers too.
#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views
{
    /// <summary>A view a package contributes.</summary>
    public interface IStudioViewProvider
    {
        /// <summary>A stable id (reverse-dns, e.g. com.mygame.views.recipes).</summary>
        string ViewId { get; }

        string Title { get; }

        StudioViewBase Create(StudioViewContext context);
    }

    public static class StudioViewRegistry
    {
        /// <summary>The built-in providers plus every IStudioViewProvider class in the project.</summary>
        public static IReadOnlyList<IStudioViewProvider> Providers()
        {
            List<IStudioViewProvider> providers = new List<IStudioViewProvider>
            {
                new BuiltIn(StudioViewIds.Relationships, "Relationships", context => new RelationshipsView(context)),
                new BuiltIn(StudioViewIds.Dialogue, "Dialogue", context => new DialogueView(context)),
                new BuiltIn(StudioViewIds.Quests, "Quests", context => new QuestsView(context)),
                new BuiltIn(StudioViewIds.World, "World", context => new WorldView(context)),
                new BuiltIn(StudioViewIds.Tables, "Tables", context => new TablesView(context)),
                new BuiltIn(StudioViewIds.Changes, "Changes", context => new ChangesView(context)),
            };
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IStudioViewProvider>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null || type == typeof(BuiltIn))
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is IStudioViewProvider provider)
                {
                    providers.Add(provider);
                }
            }

            return providers;
        }

        public static IStudioViewProvider? Find(string viewId)
        {
            foreach (IStudioViewProvider provider in Providers())
            {
                if (string.Equals(provider.ViewId, viewId, StringComparison.Ordinal))
                {
                    return provider;
                }
            }

            return null;
        }

        /// <summary>Opens a view by id (built-in windows for the built-in ids, a PluginViewWindow otherwise).</summary>
        public static EditorWindow? Open(string viewId)
        {
            switch (viewId)
            {
                case StudioViewIds.Relationships: return StudioViewWindow.Open<RelationshipsWindow>();
                case StudioViewIds.Dialogue: return StudioViewWindow.Open<DialogueWindow>();
                case StudioViewIds.Quests: return StudioViewWindow.Open<QuestsWindow>();
                case StudioViewIds.World: return StudioViewWindow.Open<WorldWindow>();
                case StudioViewIds.Tables: return StudioViewWindow.Open<TablesWindow>();
                case StudioViewIds.Changes: return StudioViewWindow.Open<ChangesWindow>();
            }

            IStudioViewProvider? provider = Find(viewId);
            if (provider == null)
            {
                return null;
            }

            PluginViewWindow window = EditorWindow.CreateWindow<PluginViewWindow>(provider.Title);
            window.Bind(provider.ViewId);
            window.position = new Rect(80f, 80f, StudioViewIds.DefaultWidth, StudioViewIds.DefaultHeight);
            window.Show();
            return window;
        }

        private sealed class BuiltIn : IStudioViewProvider
        {
            private readonly Func<StudioViewContext, StudioViewBase> _create;

            public BuiltIn(string id, string title, Func<StudioViewContext, StudioViewBase> create)
            {
                ViewId = id;
                Title = title;
                _create = create;
            }

            public string ViewId { get; }

            public string Title { get; }

            public StudioViewBase Create(StudioViewContext context) => _create(context);
        }
    }

    /// <summary>Hosts a plugin view by id (the id is serialized with the window layout).</summary>
    public sealed class PluginViewWindow : StudioViewWindow
    {
        [SerializeField]
        private string viewId = string.Empty;

        protected override string ViewTitle => StudioViewRegistry.Find(viewId)?.Title ?? viewId;

        public void Bind(string id)
        {
            viewId = id;
            Rebuild();
        }

        protected override StudioViewBase CreateView(StudioViewContext context)
        {
            IStudioViewProvider? provider = StudioViewRegistry.Find(viewId);
            if (provider == null)
            {
                throw new InvalidOperationException("No view provider has id '" + viewId + "'.");
            }

            return provider.Create(context);
        }
    }
}
