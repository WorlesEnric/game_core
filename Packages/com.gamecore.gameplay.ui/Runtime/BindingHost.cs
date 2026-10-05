// GameCore.Gameplay.Ui - BindingHost: binds a document's binding map to a cloned visual tree (P1.5, catalog row 10).
//
// Sources:
//   vm:<model>.<Property>  text/value of a string/float property: a UI Toolkit DataBinding (runtime data binding; the
//                          model raises propertyChanged); every other property kind is pushed by the host when the
//                          model raises propertyChanged for that property.
//   slot:<owner>/<domain>.<member>@<target>  a committed int32 slot read after each pump (Refresh).
//   event:<name>           the latest value of a committed event stream the UI runtime keeps (Refresh).
//   command:<name>         a click (property "clicked") or a value change ("changed") dispatches the UI command.
// Items: property "items" rebuilds the element's children from a string[] (one Button per item, class gc-item);
// the entry's format names the item command prefix (command:choose -> choose.<index>). Property "selected" toggles the
// class gc-selected on the child at an int index (format offset:<n> shifts it, e.g. offset:-1 for 1-based slots).
//
// Check() validates a binding map against a tree without binding anything (the EditMode test and ui.bind use it).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using Unity.Properties;
using UnityEngine.UIElements;

namespace GameCore.Gameplay.Ui
{
    /// <summary>The kind of a binding source.</summary>
    public enum UiSourceKind
    {
        ViewModel = 0,
        Slot = 1,
        Event = 2,
        Command = 3,
    }

    /// <summary>A parsed binding source.</summary>
    public sealed class UiBindingSource
    {
        public UiSourceKind Kind { get; set; }

        /// <summary>vm: the model name; slot: the owner name; event: the event name; command: the command name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>vm: the property; slot: the slot domain.</summary>
        public string Property { get; set; } = string.Empty;

        /// <summary>slot: the slot member.</summary>
        public string Member { get; set; } = string.Empty;

        /// <summary>slot: session | audio | focus | an authoring id.</summary>
        public string Target { get; set; } = string.Empty;

        public override string ToString() => Kind + ":" + Name + "." + Property + Member + (Target.Length > 0 ? "@" + Target : string.Empty);
    }

    /// <summary>The outcome of binding or checking one document.</summary>
    public sealed class UiBindingReport
    {
        private readonly List<string> problems = new List<string>();

        public int Resolved { get; internal set; }

        public IReadOnlyList<string> Problems => problems;

        public bool Ok => problems.Count == 0;

        internal void Problem(string code, string text) => problems.Add(code + ": " + text);

        public override string ToString() => Ok ? Resolved + " bindings resolved" : string.Join("; ", problems);
    }

    /// <summary>Binds binding maps to visual trees and refreshes slot and event sources.</summary>
    public sealed class BindingHost
    {
        public const string ItemClass = "gc-item";
        public const string SelectedClass = "gc-selected";

        private static readonly string[] Properties = { "text", "value", "visible", "enabled", "selected", "items", "clicked", "changed" };

        private readonly UiViewModels models;
        private readonly List<PushBinding> pushes = new List<PushBinding>();
        private readonly List<SourceBinding> sources = new List<SourceBinding>();
        private readonly Func<string, float, bool> dispatch;

        public BindingHost(UiViewModels models, Func<string, float, bool> dispatch)
        {
            this.models = models ?? throw new ArgumentNullException(nameof(models));
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        }

        public int DataBindings { get; private set; }

        public int PushBindings => pushes.Count;

        public int SourceBindings => sources.Count;

        /// <summary>Parses a source path; false with a problem text when it is malformed.</summary>
        public static bool TryParseSource(string? source, out UiBindingSource parsed, out string problem)
        {
            parsed = new UiBindingSource();
            problem = string.Empty;
            if (string.IsNullOrEmpty(source))
            {
                problem = "empty source";
                return false;
            }

            int colon = source!.IndexOf(':');
            if (colon <= 0 || colon == source.Length - 1)
            {
                problem = "source '" + source + "' has no kind prefix (vm:, slot:, event:, command:)";
                return false;
            }

            string kind = source.Substring(0, colon);
            string rest = source.Substring(colon + 1);
            switch (kind)
            {
                case "vm":
                {
                    int dot = rest.IndexOf('.');
                    if (dot <= 0 || dot == rest.Length - 1)
                    {
                        problem = "vm source '" + source + "' is not vm:<model>.<Property>";
                        return false;
                    }

                    parsed.Kind = UiSourceKind.ViewModel;
                    parsed.Name = rest.Substring(0, dot);
                    parsed.Property = rest.Substring(dot + 1);
                    return true;
                }

                case "slot":
                {
                    int slash = rest.IndexOf('/');
                    int at = rest.LastIndexOf('@');
                    if (slash <= 0 || at <= slash + 1 || at == rest.Length - 1)
                    {
                        problem = "slot source '" + source + "' is not slot:<owner>/<domain>.<member>@<target>";
                        return false;
                    }

                    string slot = rest.Substring(slash + 1, at - slash - 1);
                    int dot = slot.IndexOf('.');
                    if (dot <= 0 || dot == slot.Length - 1)
                    {
                        problem = "slot source '" + source + "' names no <domain>.<member>";
                        return false;
                    }

                    parsed.Kind = UiSourceKind.Slot;
                    parsed.Name = rest.Substring(0, slash);
                    parsed.Property = slot.Substring(0, dot);
                    parsed.Member = slot.Substring(dot + 1);
                    parsed.Target = rest.Substring(at + 1);
                    return true;
                }

                case "event":
                    parsed.Kind = UiSourceKind.Event;
                    parsed.Name = rest;
                    return true;
                case "command":
                    parsed.Kind = UiSourceKind.Command;
                    parsed.Name = rest;
                    return true;
                default:
                    problem = "unknown source kind '" + kind + "' in '" + source + "'";
                    return false;
            }
        }

        /// <summary>Checks a binding map against a tree: every element exists, every property and source is valid.</summary>
        public static UiBindingReport Check(VisualElement root, IReadOnlyList<UiBindingEntry> entries, UiViewModels models)
        {
            var report = new UiBindingReport();
            for (int i = 0; i < entries.Count; i++)
            {
                CheckEntry(root, entries[i], models, report, out VisualElement? _, out UiBindingSource? _);
            }

            return report;
        }

        /// <summary>Binds a document's map onto <paramref name="root"/> (a clone of its UXML).</summary>
        public UiBindingReport Bind(VisualElement root, IReadOnlyList<UiBindingEntry> entries)
        {
            var report = new UiBindingReport();
            for (int i = 0; i < entries.Count; i++)
            {
                UiBindingEntry entry = entries[i];
                if (!CheckEntry(root, entry, models, report, out VisualElement? element, out UiBindingSource? source) || element == null || source == null)
                {
                    continue;
                }

                switch (source.Kind)
                {
                    case UiSourceKind.ViewModel:
                        BindViewModel(element, entry, source);
                        break;
                    case UiSourceKind.Slot:
                    case UiSourceKind.Event:
                        sources.Add(new SourceBinding(element, entry, source));
                        break;
                    case UiSourceKind.Command:
                        BindCommand(element, entry, source);
                        break;
                }
            }

            return report;
        }

        /// <summary>
        /// Pushes slot and event sources. <paramref name="readSlot"/> reads a parsed slot source (false when the target
        /// does not hold it); <paramref name="eventValue"/> returns the latest value of an event stream.
        /// </summary>
        public int Refresh(Func<UiBindingSource, (bool found, int value)> readSlot, Func<string, string> eventValue)
        {
            int touched = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                SourceBinding binding = sources[i];
                object? value;
                if (binding.Source.Kind == UiSourceKind.Slot)
                {
                    (bool found, int raw) = readSlot(binding.Source);
                    value = found ? (object)raw : null;
                }
                else
                {
                    value = eventValue(binding.Source.Name);
                }

                if (Equals(value, binding.Last) && binding.Applied)
                {
                    continue;
                }

                binding.Last = value;
                binding.Applied = true;
                Apply(binding.Element, binding.Entry, value, dispatch);
                touched++;
            }

            return touched;
        }

        /// <summary>Drops every binding (the document was rebuilt).</summary>
        public void Clear()
        {
            for (int i = 0; i < pushes.Count; i++)
            {
                pushes[i].Detach();
            }

            pushes.Clear();
            sources.Clear();
            DataBindings = 0;
        }

        private static bool CheckEntry(
            VisualElement root,
            UiBindingEntry entry,
            UiViewModels models,
            UiBindingReport report,
            out VisualElement? element,
            out UiBindingSource? source)
        {
            element = null;
            source = null;
            if (string.IsNullOrEmpty(entry.Element))
            {
                report.Problem(PresentationDiagnosticCodes.UiUnknownElement, "a binding names no element");
                return false;
            }

            element = root.Q(entry.Element);
            if (element == null)
            {
                report.Problem(PresentationDiagnosticCodes.UiUnknownElement, "no element named '" + entry.Element + "'");
                return false;
            }

            if (Array.IndexOf(Properties, entry.Property) < 0)
            {
                report.Problem(PresentationDiagnosticCodes.UiBadSource, entry.Element + ": unknown property '" + entry.Property + "'");
                return false;
            }

            if (!TryParseSource(entry.Source, out UiBindingSource parsed, out string problem))
            {
                report.Problem(PresentationDiagnosticCodes.UiBadSource, entry.Element + ": " + problem);
                return false;
            }

            bool command = entry.Property == "clicked" || entry.Property == "changed";
            if (command != (parsed.Kind == UiSourceKind.Command))
            {
                report.Problem(PresentationDiagnosticCodes.UiBadSource,
                    entry.Element + ": property '" + entry.Property + "' needs " + (command ? "a command: source" : "a vm:, slot: or event: source"));
                return false;
            }

            if (parsed.Kind == UiSourceKind.ViewModel)
            {
                UiViewModel? model = models.Find(parsed.Name);
                if (model == null)
                {
                    report.Problem(PresentationDiagnosticCodes.UiBadSource, entry.Element + ": unknown view model '" + parsed.Name + "'");
                    return false;
                }

                bool known = false;
                IReadOnlyList<string> names = model.PropertyNames;
                for (int i = 0; i < names.Count; i++)
                {
                    known |= string.Equals(names[i], parsed.Property, StringComparison.Ordinal);
                }

                if (!known)
                {
                    report.Problem(PresentationDiagnosticCodes.UiBadSource, entry.Element + ": view model '" + parsed.Name + "' has no property '" + parsed.Property + "'");
                    return false;
                }
            }

            if (entry.Property == "clicked" && !(element is Button))
            {
                report.Problem(PresentationDiagnosticCodes.UiBadSource, entry.Element + ": 'clicked' needs a Button");
                return false;
            }

            if ((entry.Property == "text") && !(element is TextElement) && !(element is BaseField<string>))
            {
                report.Problem(PresentationDiagnosticCodes.UiBadSource, entry.Element + ": 'text' needs a text element");
                return false;
            }

            source = parsed;
            report.Resolved++;
            return true;
        }

        private void BindViewModel(VisualElement element, UiBindingEntry entry, UiBindingSource source)
        {
            UiViewModel model = models.Find(source.Name)!;
            PropertyInfo? info = model.GetType().GetProperty(source.Property, BindingFlags.Public | BindingFlags.Instance);
            bool plainText = entry.Property == "text" && entry.Format.Length == 0 && info != null && info.PropertyType == typeof(string) && element is TextElement;
            bool plainValue = entry.Property == "value" && entry.Format.Length == 0 && info != null && info.PropertyType == typeof(float) && element is ProgressBar;
            if (plainText || plainValue)
            {
                element.dataSource = model;
                element.SetBinding(new BindingId(entry.Property), new DataBinding
                {
                    dataSourcePath = new PropertyPath(source.Property),
                    bindingMode = BindingMode.ToTarget,
                });
                DataBindings++;
                return;
            }

            var push = new PushBinding(element, entry, model, info, dispatch);
            pushes.Add(push);
            push.Push();
        }

        private void BindCommand(VisualElement element, UiBindingEntry entry, UiBindingSource source)
        {
            string name = source.Name;
            if (entry.Property == "clicked" && element is Button button)
            {
                button.clicked += () => dispatch(name, 0f);
                return;
            }

            switch (element)
            {
                case Slider slider:
                    slider.RegisterValueChangedCallback(e => dispatch(name, e.newValue));
                    break;
                case SliderInt sliderInt:
                    sliderInt.RegisterValueChangedCallback(e => dispatch(name, e.newValue));
                    break;
                case Toggle toggle:
                    toggle.RegisterValueChangedCallback(e => dispatch(name, e.newValue ? 1f : 0f));
                    break;
                case DropdownField dropdown:
                    dropdown.RegisterValueChangedCallback(_ => dispatch(name, dropdown.index));
                    break;
            }
        }

        /// <summary>Applies one value to an element property (shared by push, slot and event bindings).</summary>
        internal static void Apply(VisualElement element, UiBindingEntry entry, object? value, Func<string, float, bool> dispatch)
        {
            switch (entry.Property)
            {
                case "text":
                    string text = Format(value, entry.Format);
                    if (element is TextElement textElement)
                    {
                        textElement.text = text;
                    }
                    else if (element is BaseField<string> field)
                    {
                        field.SetValueWithoutNotify(text);
                    }

                    break;
                case "value":
                    ApplyValue(element, value, entry.Format);
                    break;
                case "visible":
                    element.style.display = Truthy(value) ? DisplayStyle.Flex : DisplayStyle.None;
                    break;
                case "enabled":
                    element.SetEnabled(Truthy(value));
                    break;
                case "selected":
                    int selected = ToInt(value);
                    if (entry.Format.StartsWith("offset:", StringComparison.Ordinal)
                        && int.TryParse(entry.Format.Substring(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset))
                    {
                        selected += offset;
                    }
                    for (int i = 0; i < element.childCount; i++)
                    {
                        element[i].EnableInClassList(SelectedClass, i == selected);
                    }

                    break;
                case "items":
                    ApplyItems(element, entry, value as string[] ?? Array.Empty<string>(), dispatch);
                    break;
            }
        }

        internal static bool Truthy(object? value)
        {
            switch (value)
            {
                case null: return false;
                case bool b: return b;
                case int i: return i != 0;
                case float f: return Math.Abs(f) > 1e-6f;
                case string s: return s.Length > 0;
                case Array a: return a.Length > 0;
                default: return true;
            }
        }

        internal static int ToInt(object? value)
        {
            switch (value)
            {
                case int i: return i;
                case float f: return (int)Math.Round(f);
                case bool b: return b ? 1 : 0;
                default: return -1;
            }
        }

        internal static float ToFloat(object? value)
        {
            switch (value)
            {
                case int i: return i;
                case float f: return f;
                case bool b: return b ? 1f : 0f;
                default: return 0f;
            }
        }

        /// <summary>Formats a value: "percent:&lt;max&gt;" scales a number to 0..100; any other format is a string.Format pattern.</summary>
        internal static string Format(object? value, string format)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (format.StartsWith("percent:", StringComparison.Ordinal)
                && float.TryParse(format.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out float max) && max > 0f)
            {
                return Math.Round(ToFloat(value) * 100f / max).ToString(CultureInfo.InvariantCulture) + "%";
            }

            string plain = value is float f ? f.ToString("0.##", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return format.Length == 0 ? plain : string.Format(CultureInfo.InvariantCulture, format, plain);
        }

        private static void ApplyValue(VisualElement element, object? value, string format)
        {
            float number = ToFloat(value);
            if (format.StartsWith("percent:", StringComparison.Ordinal)
                && float.TryParse(format.Substring(8), NumberStyles.Float, CultureInfo.InvariantCulture, out float max) && max > 0f)
            {
                number = number * 100f / max;
            }

            switch (element)
            {
                case ProgressBar bar:
                    bar.value = number;
                    break;
                case Slider slider:
                    slider.SetValueWithoutNotify(number);
                    break;
                case SliderInt sliderInt:
                    sliderInt.SetValueWithoutNotify((int)Math.Round(number));
                    break;
                case Toggle toggle:
                    toggle.SetValueWithoutNotify(Truthy(value));
                    break;
                case DropdownField dropdown:
                    dropdown.SetValueWithoutNotify(ToInt(value) >= 0 && ToInt(value) < dropdown.choices.Count ? dropdown.choices[ToInt(value)] : dropdown.value);
                    break;
            }
        }

        private static void ApplyItems(VisualElement container, UiBindingEntry entry, string[] items, Func<string, float, bool> dispatch)
        {
            if (container is DropdownField dropdown)
            {
                int index = dropdown.index;
                dropdown.choices = new List<string>(items);
                dropdown.SetValueWithoutNotify(index >= 0 && index < items.Length ? items[index] : (items.Length > 0 ? items[0] : string.Empty));
                return;
            }

            container.Clear();
            string prefix = entry.Format.StartsWith("command:", StringComparison.Ordinal) ? entry.Format.Substring(8) : string.Empty;
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                var item = new Button { text = items[i], name = container.name + "-" + i.ToString(CultureInfo.InvariantCulture) };
                item.AddToClassList(ItemClass);
                if (prefix.Length > 0)
                {
                    item.clicked += () => dispatch(prefix + "." + index.ToString(CultureInfo.InvariantCulture), 0f);
                }

                container.Add(item);
            }
        }

        /// <summary>A view-model property pushed into an element when the model raises propertyChanged.</summary>
        private sealed class PushBinding
        {
            private readonly VisualElement element;
            private readonly UiBindingEntry entry;
            private readonly UiViewModel model;
            private readonly PropertyInfo? info;
            private readonly Func<string, float, bool> dispatch;
            private readonly BindingId property;

            public PushBinding(VisualElement element, UiBindingEntry entry, UiViewModel model, PropertyInfo? info, Func<string, float, bool> dispatch)
            {
                this.element = element;
                this.entry = entry;
                this.model = model;
                this.info = info;
                this.dispatch = dispatch;
                UiBindingSource parsed;
                TryParseSource(entry.Source, out parsed, out string _);
                property = new BindingId(parsed.Property);
                model.propertyChanged += OnChanged;
            }

            public void Push() => Apply(element, entry, info != null ? info.GetValue(model) : null, dispatch);

            public void Detach() => model.propertyChanged -= OnChanged;

            private void OnChanged(object? sender, BindablePropertyChangedEventArgs e)
            {
                if (e.propertyName.Equals(property))
                {
                    Push();
                }
            }
        }

        private sealed class SourceBinding
        {
            public SourceBinding(VisualElement element, UiBindingEntry entry, UiBindingSource source)
            {
                Element = element;
                Entry = entry;
                Source = source;
            }

            public VisualElement Element { get; }

            public UiBindingEntry Entry { get; }

            public UiBindingSource Source { get; }

            public object? Last { get; set; }

            public bool Applied { get; set; }
        }
    }

    /// <summary>Resolves slot sources against a world (owner and slot ids derive from the stable names).</summary>
    public static class UiSlotSources
    {
        public static OwnerId OwnerOf(UiBindingSource source) => GameplayIds.Owner(source.Name);

        public static SlotId SlotOf(UiBindingSource source) => SlotNames.Of(source.Property, source.Member);
    }
}
