// GameCore.Studio.UI - shared style sheet (Editor/Resources/GameCoreStudio/StudioStyles.uss) and small element
// factories (state chips, provider chips, badges) used by every panel.
#nullable enable
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Authoring;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>Style sheet and chip helpers.</summary>
    public static class StudioStyles
    {
        public static string Safe(string? text) => new SecretRedactor().Redact(text ?? string.Empty);

        /// <summary>Sanitize controls supplied by the core inspector before showing them; masking never commits an edit.</summary>
        public static void SanitizeInspector(VisualElement root)
        {
            root.Query<TextField>().ForEach(field =>
            {
                string name = field.parent?.name ?? string.Empty;
                JToken masked = new SecretRedactor().RedactJson(new JObject { [name] = field.value });
                string value = ((JObject)masked).First?.First?.Value<string>() ?? Safe(field.value);
                if (value != field.value) { field.SetValueWithoutNotify(value); field.SetEnabled(false); }
                field.tooltip = Safe(field.tooltip);
            });
            root.Query<TextElement>().ForEach(label => { label.text = Safe(label.text); label.tooltip = Safe(label.tooltip); });
        }

        public const string StyleSheetPath = "GameCoreStudio/StudioStyles";

        /// <summary>Adds the Studio style sheet to <paramref name="root"/> (no-op when already added or missing).</summary>
        public static void Apply(VisualElement root)
        {
            StyleSheet? sheet = Resources.Load<StyleSheet>(StyleSheetPath);
            if (sheet != null && !root.styleSheets.Contains(sheet))
            {
                root.styleSheets.Add(sheet);
            }

            root.AddToClassList("gcs-root");
        }

        /// <summary>A provider status chip (<c>image: live</c>).</summary>
        public static Label ProviderChip(string name, ProviderState availability)
        {
            Label chip = new Label(StudioStyles.Safe(name + ": " + ProviderNames.Wire(availability))) { name = "provider-" + name };
            chip.AddToClassList("gcs-chip");
            chip.AddToClassList("gcs-chip--" + ProviderNames.Wire(availability).Replace('_', '-'));
            chip.tooltip = StudioStyles.Safe("Provider family '" + name + "' as reported by the Studio companion (/v1/hello).");
            return chip;
        }

        /// <summary>A request state chip.</summary>
        public static Label StateChip(string label, AgentRequestState state)
        {
            Label chip = new Label(StudioStyles.Safe(label));
            chip.AddToClassList("gcs-chip");
            chip.AddToClassList("gcs-state--" + AgentRequestStates.Wire(state).Replace('_', '-'));
            return chip;
        }

        /// <summary>A requirement badge.</summary>
        public static Label Badge(string text, string? modifier = null)
        {
            Label badge = new Label(StudioStyles.Safe(text));
            badge.AddToClassList("gcs-badge");
            if (modifier != null)
            {
                badge.AddToClassList("gcs-badge--" + modifier);
            }

            return badge;
        }

        /// <summary>A section header label.</summary>
        public static Label Header(string text)
        {
            Label header = new Label(StudioStyles.Safe(text));
            header.AddToClassList("gcs-section__title");
            return header;
        }

        /// <summary>A wrapping, selectable text label.</summary>
        public static Label Text(string text, string? cssClass = null)
        {
            Label label = new Label(StudioStyles.Safe(text)) { selection = { isSelectable = true } };
            label.AddToClassList("gcs-wrap");
            if (cssClass != null)
            {
                label.AddToClassList(cssClass);
            }

            return label;
        }
    }
}
