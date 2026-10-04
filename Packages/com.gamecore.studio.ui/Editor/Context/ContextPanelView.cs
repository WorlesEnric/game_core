// GameCore.Studio.UI - the contextual control panel (GameCore/Studio/Context; SR-1.6, SR-2.2/2.3, SR-3.4): for the
// selection's primary target, a header (name, type, authoring id, definition@revision, residency/stale badges), the
// generated inspector (P1.6 AuthoringInspectorBuilder: commits are journaled set/assign change sets), tool buttons from
// the ToolRegistry filtered by target kind/type/scope and grouped by tier (Direct tools run at once as single-op change
// sets, with an argument form when they need arguments; Agent-tier tools - mechanism.propose, asset.generate - open the
// prompt bar prefilled), live validator diagnostics, references and impact from the semantic index, and Explain from the
// registered explain sources.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>How a tool button acts.</summary>
    public enum ToolButtonKind
    {
        /// <summary>Read-only tool: invoked directly, output shown.</summary>
        Query,
        /// <summary>Runs at once as a single-op change set (argument form first when it has required arguments).</summary>
        Direct,
        /// <summary>Opens the prompt bar prefilled (generation and mechanism tools).</summary>
        Agent,
    }

    /// <summary>Tool filtering and classification for a target (UI-free, tested directly).</summary>
    public static class ContextTools
    {
        /// <summary>Tools applicable to <paramref name="target"/>: not internal, not direct-only, kind/type/scope allowed.</summary>
        public static IReadOnlyList<IStudioTool> For(StudioRuntime runtime, AuthoringRef target)
        {
            string? nodeType = runtime.Index.FindNode(target)?.Type;
            AuthorScope scope = target.Scope ?? AuthorScope.Instance;
            List<IStudioTool> tools = new List<IStudioTool>();
            foreach (IStudioTool tool in runtime.Registry.All)
            {
                ToolEntry entry = tool.Entry;
                if (tool.Internal || tool is IDirectTool)
                {
                    continue;
                }

                if (entry.TargetKinds != null && !Contains(entry.TargetKinds, target.Kind))
                {
                    continue;
                }

                if (entry.TargetType != null && !string.Equals(entry.TargetType, nodeType, StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Scopes != null && entry.Scopes.Count > 0 && !ContainsScope(entry.Scopes, scope))
                {
                    continue;
                }

                if (entry.TargetKinds == null && entry.TargetType == null && !entry.TargetRequired && target.Kind != AuthoringKind.Location && !tool.ReadOnly)
                {
                    continue;
                }

                tools.Add(tool);
            }

            tools.Sort((left, right) =>
            {
                int tier = left.Entry.Tier.CompareTo(right.Entry.Tier);
                return tier != 0 ? tier : string.CompareOrdinal(left.Entry.Id, right.Entry.Id);
            });
            return tools;
        }

        public static ToolButtonKind KindOf(IStudioTool tool)
        {
            if (tool.ReadOnly)
            {
                return ToolButtonKind.Query;
            }

            if (tool.Entry.Tier == ToolTier.Mechanism
                || string.Equals(tool.Entry.Id, BuiltInToolIds.AssetGenerate, StringComparison.Ordinal)
                || string.Equals(tool.Entry.Id, BuiltInToolIds.MechanismPropose, StringComparison.Ordinal))
            {
                return ToolButtonKind.Agent;
            }

            return ToolButtonKind.Direct;
        }

        /// <summary>Required arguments a Direct tool needs from the user.</summary>
        public static IReadOnlyList<ArgSpec> RequiredArgs(IStudioTool tool)
        {
            List<ArgSpec> required = new List<ArgSpec>();
            foreach (ArgSpec arg in tool.Entry.Args)
            {
                if (arg.Required)
                {
                    required.Add(arg);
                }
            }

            return required;
        }

        /// <summary>The single-op change set a Direct tool runs (origin manual).</summary>
        public static ChangeSet SingleOp(IStudioTool tool, AuthoringRef target, JObject args, string targetName)
        {
            AuthoringRef? opTarget = tool.Entry.TargetRequired || tool.Entry.TargetKinds != null || target.Kind == AuthoringKind.Location ? target : null;
            Operation operation = new Operation("op1", tool.Entry.Id, opTarget, args);
            return StudioRuntime.Single(tool.Entry.Id + " on " + targetName, IntentOrigin.Manual, operation);
        }

        /// <summary>Parses an argument field: JSON when it parses (numbers, arrays, objects, booleans), else the raw string.</summary>
        public static JToken ParseArg(ArgSpec spec, string text)
        {
            string trimmed = text.Trim();
            if (spec.Type == "string" || spec.Type == "text")
            {
                return new JValue(text);
            }

            if (trimmed.Length == 0)
            {
                return JValue.CreateNull();
            }

            try
            {
                return JToken.Parse(trimmed);
            }
            catch (JsonException)
            {
                return new JValue(text);
            }
        }

        private static bool Contains(IReadOnlyList<AuthoringKind> kinds, AuthoringKind kind)
        {
            foreach (AuthoringKind candidate in kinds)
            {
                if (candidate == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsScope(IReadOnlyList<AuthorScope> scopes, AuthorScope scope)
        {
            foreach (AuthorScope candidate in scopes)
            {
                if ((candidate & scope) != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Live validator diagnostics by convention: public static <c>Validate(T)</c> methods of classes carrying an
    /// <c>[AuthorValidator]</c> attribute (the Studio model's or a same-named mirror, e.g. gameplay.contracts'); result
    /// items are read as Diagnostic or by their <c>Code</c>/<c>Message</c> properties. One instance caches the methods.
    /// </summary>
    public sealed class ValidatorDiagnostics
    {
        private List<MethodInfo>? _methods;

        /// <summary>Validate methods found (after the first call).</summary>
        public int MethodCount => _methods?.Count ?? 0;

        /// <summary>Runs every matching validator on <paramref name="target"/>.</summary>
        public IReadOnlyList<Diagnostic> For(UnityEngine.Object target)
        {
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (target == null)
            {
                return diagnostics;
            }

            Type targetType = target.GetType();
            foreach (MethodInfo method in Methods())
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(targetType))
                {
                    continue;
                }

                object? result;
                try
                {
                    result = method.Invoke(null, new object[] { target });
                }
                catch (TargetInvocationException error)
                {
                    diagnostics.Add(new Diagnostic(DiagnosticCodes.ValidationFailed, method.DeclaringType?.Name + " threw: " + (error.InnerException?.Message ?? error.Message)));
                    continue;
                }

                if (!(result is System.Collections.IEnumerable items))
                {
                    continue;
                }

                foreach (object? item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item is Diagnostic model)
                    {
                        diagnostics.Add(model);
                        continue;
                    }

                    string code = item.GetType().GetProperty("Code")?.GetValue(item) as string ?? DiagnosticCodes.ValidationFailed;
                    string message = item.GetType().GetProperty("Message")?.GetValue(item) as string ?? item.ToString() ?? string.Empty;
                    diagnostics.Add(new Diagnostic(code, message));
                }
            }

            return diagnostics;
        }

        private List<MethodInfo> Methods()
        {
            if (_methods != null)
            {
                return _methods;
            }

            _methods = new List<MethodInfo>();
            List<Type> validators = new List<Type>();
            foreach (Type attribute in TypeCache.GetTypesDerivedFrom<Attribute>())
            {
                if (attribute.Name != "AuthorValidatorAttribute")
                {
                    continue;
                }

                foreach (Type type in TypeCache.GetTypesWithAttribute(attribute))
                {
                    if (!validators.Contains(type))
                    {
                        validators.Add(type);
                    }
                }
            }

            foreach (Type type in validators)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == "Validate" && !method.ContainsGenericParameters)
                    {
                        _methods.Add(method);
                    }
                }
            }

            return _methods;
        }
    }

    /// <summary>The context panel.</summary>
    public sealed class ContextPanelView : VisualElement
    {
        private readonly StudioUiContext _context;
        private readonly ScrollView _scroll;
        private readonly ValidatorDiagnostics _validators = new ValidatorDiagnostics();
        private bool _attached;
        private long _builtVersion = -1;

        public ContextPanelView(StudioUiContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            name = "context-panel";
            AddToClassList("gcs-context");
            _scroll = new ScrollView();
            Add(_scroll);
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
            Rebuild();
        }

        /// <summary>Output of the last tool or explain call.</summary>
        public string LastOutput { get; private set; } = string.Empty;

        public IReadOnlyList<IStudioTool> ShownTools { get; private set; } = Array.Empty<IStudioTool>();

        public void Rebuild()
        {
            _builtVersion = _context.Selection.Version;
            _scroll.Clear();
            StudioRuntime runtime = _context.Runtime;
            AuthoringRef? target = _context.Selection.Primary;
            if (target == null)
            {
                ShownTools = Array.Empty<IStudioTool>();
                _scroll.Add(StudioStyles.Text("Select something in the Studio viewport (Select mode) or in the Hierarchy. The generated inspector, tools, diagnostics, references and impact appear here.", "gcs-muted"));
                return;
            }

            UnityEngine.Object? resolved = target.Kind == AuthoringKind.Location ? null : runtime.Resolver.Find(target);
            SelectionBadge badge = target.Kind == AuthoringKind.Location
                ? new SelectionBadge(target, "Location", "location", true, null, false, null)
                : SelectionModel.DescribeRef(runtime, target);

            // ------------------------------------------------------------- header
            Label title = new Label(badge.Label) { name = "context-title" };
            title.AddToClassList("gcs-title");
            _scroll.Add(title);
            _scroll.Add(StudioStyles.Text("type " + badge.TypeId + " · kind " + target.Kind + (target.Scope.HasValue ? " · scope " + target.Scope : string.Empty)));
            if (target.AuthoringId != null)
            {
                _scroll.Add(StudioStyles.Text("authoring id " + target.AuthoringId));
            }

            if (target.Definition != null)
            {
                _scroll.Add(StudioStyles.Text("definition " + target.Definition));
            }

            if (target.Location != null)
            {
                _scroll.Add(StudioStyles.Text("region " + target.Location.Region + " · position " + string.Join(", ", Numbers(target.Location.Position))));
            }

            VisualElement badges = new VisualElement();
            badges.AddToClassList("gcs-row");
            if (!badge.Resident)
            {
                badges.Add(StudioStyles.Badge("unloaded", "warn"));
            }

            if (badge.Stale)
            {
                badges.Add(StudioStyles.Badge("stale", "error"));
            }

            if (_context.Selection.Targets.Count > 1)
            {
                badges.Add(StudioStyles.Badge("+" + (_context.Selection.Targets.Count - 1) + " more selected"));
            }

            _scroll.Add(badges);
            if (badge.ResidencyReason != null)
            {
                _scroll.Add(StudioStyles.Text(badge.ResidencyReason, "gcs-diagnostic"));
            }

            if (badge.StaleReason != null)
            {
                _scroll.Add(StudioStyles.Text(badge.StaleReason, "gcs-diagnostic"));
            }

            // ------------------------------------------------------------- inspector
            if (resolved != null)
            {
                AuthoringInspectorBuilder builder = new AuthoringInspectorBuilder(runtime.Identity, runtime.Resolver.Codec, new ManualEditCommitter(runtime));
                VisualElement? inspector = builder.Build(resolved);
                _scroll.Add(StudioStyles.Header("Inspector"));
                if (inspector != null)
                {
                    inspector.SetEnabled(badge.Resident);
                    _scroll.Add(inspector);
                }
                else
                {
                    _scroll.Add(StudioStyles.Text(resolved.GetType().Name + " has no [Authorable] metadata; use the tools below or Unity's Inspector.", "gcs-muted"));
                }
            }

            // ------------------------------------------------------------- tools
            ShownTools = ContextTools.For(runtime, target);
            _scroll.Add(StudioStyles.Header("Tools"));
            ToolTier? tier = null;
            VisualElement? group = null;
            foreach (IStudioTool tool in ShownTools)
            {
                if (tier != tool.Entry.Tier || group == null)
                {
                    tier = tool.Entry.Tier;
                    _scroll.Add(new Label(tier.ToString()) { name = "tier-" + tier });
                    group = new VisualElement();
                    group.AddToClassList("gcs-wrap-row");
                    _scroll.Add(group);
                }

                group.Add(BuildToolButton(tool, target, badge.Label, badge.Resident));
            }

            if (ShownTools.Count == 0)
            {
                _scroll.Add(StudioStyles.Text("No tool applies to this target.", "gcs-muted"));
            }

            VisualElement form = new VisualElement { name = "tool-form" };
            _scroll.Add(form);

            // ------------------------------------------------------------- diagnostics
            _scroll.Add(StudioStyles.Header("Diagnostics"));
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (resolved != null)
            {
                diagnostics.AddRange(_validators.For(resolved));
                if (resolved is Component component && !(resolved is Transform))
                {
                    foreach (MonoBehaviour behaviour in component.GetComponents<MonoBehaviour>())
                    {
                        if (behaviour != null && behaviour != resolved)
                        {
                            diagnostics.AddRange(_validators.For(behaviour));
                        }
                    }
                }
            }

            if (diagnostics.Count == 0)
            {
                _scroll.Add(StudioStyles.Text("No validator findings.", "gcs-muted"));
            }

            foreach (Diagnostic diagnostic in diagnostics)
            {
                _scroll.Add(StudioStyles.Text(diagnostic.Code + ": " + diagnostic.Message, "gcs-diagnostic"));
            }

            // ------------------------------------------------------------- references / impact
            if (target.Kind != AuthoringKind.Location)
            {
                _scroll.Add(StudioStyles.Header("References"));
                IReadOnlyList<IndexReference> references = runtime.Index.ReferencesTo(target);
                if (references.Count == 0)
                {
                    _scroll.Add(StudioStyles.Text("Nothing in the index references this.", "gcs-muted"));
                }

                for (int i = 0; i < Math.Min(references.Count, 20); i++)
                {
                    _scroll.Add(StudioStyles.Text(Name(runtime, references[i].From) + "." + references[i].Field + " (" + references[i].FromType + ")"));
                }

                ImpactReport impact = runtime.Index.ImpactOf(target, 3);
                _scroll.Add(StudioStyles.Header("Impact (" + impact.Items.Count + ")"));
                for (int i = 0; i < Math.Min(impact.Items.Count, 20); i++)
                {
                    ImpactItem item = impact.Items[i];
                    _scroll.Add(StudioStyles.Text(new string(' ', item.Depth * 2) + Name(runtime, item.Ref) + " (" + item.Relation + (item.Field != null ? " via " + item.Field : string.Empty) + ")"));
                }

                // --------------------------------------------------------- explain
                IExplainSource? source = runtime.Services.FindExplainSource(target, null);
                _scroll.Add(StudioStyles.Header("Explain"));
                if (source == null)
                {
                    _scroll.Add(StudioStyles.Text("No explain source covers this target (gameplay packages register them).", "gcs-muted"));
                }
                else
                {
                    _scroll.Add(new Button(() =>
                    {
                        ServiceResult result = source.Explain(target, null);
                        LastOutput = result.Output?.ToString(Formatting.Indented) ?? (result.Diagnostic != null ? result.Diagnostic.Code + ": " + result.Diagnostic.Message : result.Status.ToString());
                        Rebuild();
                    }) { name = "explain", text = "Explain (" + source.Id + ")" });
                }
            }

            if (LastOutput.Length > 0)
            {
                _scroll.Add(StudioStyles.Header("Output"));
                TextField output = new TextField { name = "context-output", multiline = true, isReadOnly = true, value = LastOutput };
                _scroll.Add(output);
            }
        }

        /// <summary>Runs a Direct tool with arguments as a single-op change set (also used by tests).</summary>
        public ApplyReport RunDirect(IStudioTool tool, AuthoringRef target, JObject args, string targetName)
        {
            ChangeSet changeSet = ContextTools.SingleOp(tool, target, args, targetName);
            ApplyReport report = _context.Runtime.Engine.Apply(changeSet);
            LastOutput = tool.Entry.Id + ": " + report.State + " in " + report.Milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms"
                + (report.Diagnostics.Count > 0 ? "\n" + report.Diagnostics[0].Code + ": " + report.Diagnostics[0].Message : string.Empty);
            return report;
        }

        private VisualElement BuildToolButton(IStudioTool tool, AuthoringRef target, string targetName, bool resident)
        {
            ToolButtonKind kind = ContextTools.KindOf(tool);
            Button button = new Button { name = "tool-" + tool.Entry.Id, text = tool.Entry.Id, tooltip = (tool.Entry.Doc ?? tool.Entry.Id) + " [" + kind + ", " + tool.Entry.RuntimeApply + "]" };
            button.AddToClassList("gcs-tool");
            button.AddToClassList("gcs-tool--" + kind.ToString().ToLowerInvariant());
            button.SetEnabled(resident || kind == ToolButtonKind.Query);
            button.clicked += () =>
            {
                switch (kind)
                {
                    case ToolButtonKind.Query:
                    {
                        OperationResult result = _context.Runtime.Registry.Invoke(tool.Entry.Id, target, new JObject());
                        LastOutput = tool.Entry.Id + ": " + result.Status + (result.Code != null ? " " + result.Code : string.Empty) + (result.Detail != null ? " - " + result.Detail : string.Empty)
                            + (result.Output != null ? "\n" + result.Output.ToString(Formatting.Indented) : string.Empty);
                        Rebuild();
                        break;
                    }

                    case ToolButtonKind.Agent:
                        _context.RequestPrompt("Use " + tool.Entry.Id + " on " + targetName + ": ");
                        break;

                    default:
                    {
                        IReadOnlyList<ArgSpec> required = ContextTools.RequiredArgs(tool);
                        if (required.Count == 0)
                        {
                            RunDirect(tool, target, new JObject(), targetName);
                            Rebuild();
                        }
                        else
                        {
                            ShowForm(tool, target, targetName);
                        }

                        break;
                    }
                }
            };
            return button;
        }

        private void ShowForm(IStudioTool tool, AuthoringRef target, string targetName)
        {
            VisualElement? form = _scroll.Q("tool-form");
            if (form == null)
            {
                return;
            }

            form.Clear();
            form.AddToClassList("gcs-section");
            form.Add(StudioStyles.Header(tool.Entry.Id));
            if (tool.Entry.Doc != null)
            {
                form.Add(StudioStyles.Text(tool.Entry.Doc, "gcs-muted"));
            }

            Dictionary<string, TextField> fields = new Dictionary<string, TextField>(StringComparer.Ordinal);
            foreach (ArgSpec arg in tool.Entry.Args)
            {
                TextField field = new TextField(arg.Name + (arg.Required ? " *" : string.Empty) + " (" + arg.Type + (arg.Unit != null ? ", " + arg.Unit : string.Empty) + ")") { name = "arg-" + arg.Name };
                field.tooltip = arg.Doc ?? string.Empty;
                if (arg.EnumValues != null && arg.EnumValues.Count > 0)
                {
                    field.tooltip += " Values: " + string.Join(", ", arg.EnumValues);
                }

                fields[arg.Name] = field;
                form.Add(field);
            }

            Label result = new Label();
            result.AddToClassList("gcs-status");
            form.Add(new Button(() =>
            {
                JObject args = new JObject();
                foreach (ArgSpec arg in tool.Entry.Args)
                {
                    string text = fields[arg.Name].value ?? string.Empty;
                    if (text.Trim().Length == 0 && !arg.Required)
                    {
                        continue;
                    }

                    args[arg.Name] = ContextTools.ParseArg(arg, text);
                }

                ApplyReport report = RunDirect(tool, target, args, targetName);
                result.text = LastOutput;
                if (report.Ok)
                {
                    Rebuild();
                }
            }) { name = "tool-run", text = "Run " + tool.Entry.Id });
            form.Add(result);
        }

        private static string Name(StudioRuntime runtime, AuthoringRef reference)
        {
            return runtime.Index.FindNode(reference)?.Name ?? reference.Path ?? reference.AuthoringId ?? reference.Kind.ToString();
        }

        private static IEnumerable<string> Numbers(IReadOnlyList<double> values)
        {
            foreach (double value in values)
            {
                yield return value.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        private void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _context.Selection.Changed += OnSelectionChanged;
            _context.Runtime.Engine.Applied += OnApplied;
            Rebuild();
        }

        private void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _context.Selection.Changed -= OnSelectionChanged;
            _context.Runtime.Engine.Applied -= OnApplied;
        }

        private void OnSelectionChanged()
        {
            if (_builtVersion != _context.Selection.Version)
            {
                LastOutput = string.Empty;
                Rebuild();
            }
        }

        private void OnApplied(ApplyReport report) => schedule.Execute(Rebuild);
    }
}
