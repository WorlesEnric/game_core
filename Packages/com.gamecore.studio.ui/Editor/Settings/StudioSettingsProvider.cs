// GameCore.Studio.UI - the "Project/GameCore Studio" settings page (GameCore/Studio/Settings): journal location, preview
// ghost material, picking options, the agent gateway status and every IStudioSettingsSection other Studio packages
// declare (P2.2's pairing section, P2.4's staging section). Sections are discovered with TypeCache, so there is no
// static registry: any non-abstract class implementing IStudioSettingsSection with a public parameterless constructor
// appears, ordered by Order then Title.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>A section of the Studio settings page contributed by another Studio package.</summary>
    public interface IStudioSettingsSection
    {
        /// <summary>Stable id (e.g. <c>etos.pairing</c>).</summary>
        string Id { get; }

        string Title { get; }

        /// <summary>Sort key; built-in sections use 0..99, the gateway section 100.</summary>
        int Order { get; }

        /// <summary>Fills <paramref name="container"/> (called each time the page opens).</summary>
        void Build(VisualElement container);
    }

    /// <summary>Discovers settings sections.</summary>
    public static class StudioSettingsSections
    {
        /// <summary>Fresh instances of every declared section, sorted.</summary>
        public static IReadOnlyList<IStudioSettingsSection> Discover()
        {
            List<IStudioSettingsSection> sections = new List<IStudioSettingsSection>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IStudioSettingsSection>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(type) is IStudioSettingsSection section)
                    {
                        sections.Add(section);
                    }
                }
                catch (Exception error) when (error is MissingMethodException || error is System.Reflection.TargetInvocationException || error is MemberAccessException)
                {
                    Debug.LogWarning(StudioStyles.Safe("GameCore Studio: settings section " + type.FullName + " could not be created: " + error.Message));
                }
            }

            sections.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0 ? order : string.CompareOrdinal(left.Title, right.Title);
            });
            return sections;
        }
    }

    /// <summary>The settings page.</summary>
    public sealed class StudioSettingsProvider : SettingsProvider
    {
        public const string PagePath = "Project/GameCore Studio";

        private StudioSettingsProvider()
            : base(PagePath, SettingsScope.Project, new[] { "GameCore", "Studio", "journal", "ghost", "picking", "agent", "etos" })
        {
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new StudioSettingsProvider();

        [MenuItem(StudioWindowIds.SettingsMenu, false, 120)]
        public static void Open() => SettingsService.OpenProjectSettings(PagePath);

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            StudioStyles.Apply(rootElement);
            ScrollView scroll = new ScrollView();
            scroll.AddToClassList("gcs-settings");
            rootElement.Add(scroll);
            Label title = new Label("GameCore Studio");
            title.AddToClassList("gcs-title");
            scroll.Add(title);
            Build(scroll.contentContainer);
        }

        /// <summary>Builds every section into <paramref name="root"/> (also used by tests).</summary>
        public static void Build(VisualElement root)
        {
            StudioUiSettings settings = StudioUiSettings.instance;
            StudioRuntime runtime = StudioServices.Runtime;

            VisualElement journal = Section(root, "Journal");
            journal.Add(new Label(StudioStyles.Safe("History: " + runtime.Paths.HistoryRoot)));
            journal.Add(new Label(StudioStyles.Safe("Artifacts: " + runtime.Paths.ArtifactsRoot)));
            journal.Add(new Label("The journal location is fixed by the edit engine (Studio/History under the project root, 03 s6)."));
            Button reveal = new Button(() => EditorUtility.RevealInFinder(runtime.Paths.HistoryRoot)) { text = "Reveal journal folder" };
            journal.Add(reveal);

            VisualElement preview = Section(root, "Preview");
            ObjectField ghost = new ObjectField("Ghost material") { objectType = typeof(Material), allowSceneObjects = false, value = settings.GhostMaterial };
            ghost.tooltip = "Applied to candidate preview ghosts; empty keeps the engine's translucent material.";
            ghost.RegisterValueChangedCallback(change => settings.GhostMaterial = change.newValue as Material);
            preview.Add(ghost);

            VisualElement picking = Section(root, "Picking");
            Toggle ui = new Toggle("Pick runtime UI") { value = settings.IncludeUi };
            ui.RegisterValueChangedCallback(change => settings.IncludeUi = change.newValue);
            picking.Add(ui);
            FloatField band = new FloatField("Overlap depth band (fraction)") { value = settings.OverlapDepthFraction, isDelayed = true };
            band.RegisterValueChangedCallback(change => settings.OverlapDepthFraction = change.newValue);
            picking.Add(band);
            IntegerField radius = new IntegerField("Overlap radius (px)") { value = settings.OverlapRadiusPixels, isDelayed = true };
            radius.RegisterValueChangedCallback(change => settings.OverlapRadiusPixels = change.newValue);
            picking.Add(radius);
            FloatField ground = new FloatField("Ground plane height") { value = settings.GroundHeight, isDelayed = true };
            ground.RegisterValueChangedCallback(change => settings.GroundHeight = change.newValue);
            picking.Add(ground);
            Toggle full = new Toggle("Marquee: full containment by default") { value = settings.FullContainmentDefault };
            full.RegisterValueChangedCallback(change => settings.FullContainmentDefault = change.newValue);
            picking.Add(full);
            IntegerField hz = new IntegerField("Edit-mode render rate (Hz)") { value = settings.EditModeRenderHz, isDelayed = true };
            hz.RegisterValueChangedCallback(change => settings.EditModeRenderHz = change.newValue);
            picking.Add(hz);

            VisualElement agent = Section(root, "Agent gateway");
            IAgentGateway gateway = StudioAgentGateways.Resolve(runtime);
            agent.Add(new Label(StudioStyles.Safe("Gateway: " + (gateway is NullAgentGateway ? "none registered (NullAgentGateway)" : gateway.GetType().FullName))));
            ProviderStatus status = gateway.Status;
            agent.Add(new Label(StudioStyles.Safe("Connection: " + ProviderNames.ConnectionOf(status) + (status.Problem != null ? " (" + status.Problem.Code + ")" : string.Empty)
                + (status.CompanionVersion != null ? ", companion " + status.CompanionVersion : string.Empty))));
            if (status.Problem != null)
            {
                Label detail = new Label(StudioStyles.Safe(status.Problem.Message + (status.Problem.Hint != null ? " " + status.Problem.Hint : string.Empty)));
                detail.AddToClassList("gcs-wrap");
                agent.Add(detail);
            }

            Label etos = new Label("The key file, node URL and connection test are on Project Settings > GameCore Studio > ETOS (P2.2).");
            etos.AddToClassList("gcs-wrap");
            agent.Add(etos);

            VisualElement chips = new VisualElement();
            chips.AddToClassList("gcs-row");
            foreach (string name in ProviderNames.All)
            {
                chips.Add(StudioStyles.ProviderChip(name, status.For(name)));
            }

            agent.Add(chips);

            foreach (IStudioSettingsSection section in StudioSettingsSections.Discover())
            {
                VisualElement container = Section(root, section.Title);
                try
                {
                    section.Build(container);
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    container.Add(new Label(StudioStyles.Safe("This section failed to build: " + error.Message)));
                }
            }

            VisualElement help = Section(root, "First run");
            help.Add(new Button(() =>
            {
                StudioUiSettings.FirstRunDone = false;
                FirstRunWizardWindow.Open();
            }) { text = "Show the first-run guide" });
        }

        private static VisualElement Section(VisualElement root, string title)
        {
            VisualElement section = new VisualElement { name = "settings-" + title.ToLowerInvariant().Replace(' ', '-') };
            section.AddToClassList("gcs-section");
            Label header = new Label(StudioStyles.Safe(title));
            header.AddToClassList("gcs-section__title");
            section.Add(header);
            root.Add(section);
            return section;
        }
    }
}
