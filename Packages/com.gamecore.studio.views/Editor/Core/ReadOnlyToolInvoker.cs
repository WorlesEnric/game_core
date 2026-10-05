// GameCore.Studio.Views - running side-effect-free plugin tools from a view (preview, simulate, explain, test).
//
// ToolRegistry.Invoke(toolId, target, args) runs only tools that declare themselves read-only or direct; every
// [AuthorOperation] method is a ReflectedTool with ReadOnly = false, so P1.4's dialogue.preview, quest.simulate,
// logic.explain and logic.test (pure computations over the authored content that write nothing) are refused there and
// would otherwise need a journaled change set per preview. Until the catalog can mark such operations read-only, this
// invoker runs exactly these allowlisted tool ids by finding their [AuthorOperation] method (TypeCache, model or mirror
// attribute, by tool id: no gameplay type dependency), binding the target and [AuthorArg] arguments with the project's
// value codec the way ReflectedTool does, and returning the output. Any other id goes through ToolRegistry.Invoke and is
// refused unless the registry accepts it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameCore.Studio.Views
{
    /// <summary>The outcome of a read-only tool run.</summary>
    public sealed class ToolInvocation
    {
        public ToolInvocation(bool ok, JToken? output, string? code, string? problem)
        {
            Ok = ok;
            Output = output;
            Code = code;
            Problem = problem;
        }

        public bool Ok { get; }

        public JToken? Output { get; }

        public string? Code { get; }

        public string? Problem { get; }

        /// <summary>The output as text (string outputs as is, others as JSON), or the problem.</summary>
        public string Text
        {
            get
            {
                if (!Ok)
                {
                    return (Code ?? "Refused") + ": " + (Problem ?? string.Empty);
                }

                if (Output == null)
                {
                    return string.Empty;
                }

                return Output.Type == JTokenType.String ? Output.Value<string>() ?? string.Empty : Output.ToString();
            }
        }
    }

    /// <summary>Runs allowlisted pure tools by id (see the file header).</summary>
    public sealed class ReadOnlyToolInvoker
    {
        /// <summary>Tool ids known to compute without writing anything (P1.4).</summary>
        public static readonly IReadOnlyList<string> PureTools = new[] { "dialogue.preview", "quest.simulate", "logic.explain", "logic.test" };

        private readonly StudioRuntime _runtime;
        private readonly Dictionary<string, MethodInfo?> _methods = new Dictionary<string, MethodInfo?>(StringComparer.Ordinal);

        public ReadOnlyToolInvoker(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public static bool IsPure(string toolId)
        {
            foreach (string id in PureTools)
            {
                if (string.Equals(id, toolId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the tool exists in the project's registry.</summary>
        public bool IsAvailable(string toolId) => _runtime.Registry.Find(toolId) != null;

        public ToolInvocation Invoke(string toolId, AuthoringRef? target, JObject? args)
        {
            IStudioTool? tool = _runtime.Registry.Find(toolId);
            if (tool == null)
            {
                return new ToolInvocation(false, null, DiagnosticCodes.UnknownTool, "Tool '" + toolId + "' is not registered in this project.");
            }

            if (tool.ReadOnly || tool is IDirectTool || !IsPure(toolId))
            {
                OperationResult result = _runtime.Registry.Invoke(toolId, target, args);
                return result.Status == OutcomeStatus.Applied
                    ? new ToolInvocation(true, result.Output, null, null)
                    : new ToolInvocation(false, null, result.Code, result.Detail);
            }

            MethodInfo? method = MethodOf(toolId);
            if (method == null)
            {
                return new ToolInvocation(false, null, DiagnosticCodes.UnknownTool, "No [AuthorOperation] method declares '" + toolId + "'.");
            }

            UnityEngine.Object? resolved = target == null || target.Kind == AuthoringKind.Location ? null : _runtime.Resolver.Find(target);
            if (target != null && resolved == null)
            {
                return new ToolInvocation(false, null, DiagnosticCodes.StaleTarget, "The target does not resolve.");
            }

            ParameterInfo[] parameters = method.GetParameters();
            object?[] arguments = new object?[parameters.Length];
            bool targetBound = false;
            JObject values = args ?? new JObject();
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                AuthorArgAttribute? arg = AuthoringMetadata.Arg(parameter);
                if (arg == null)
                {
                    if (!targetBound && resolved != null && AuthoringMetadata.Authorable(parameter.ParameterType) != null)
                    {
                        UnityEngine.Object? adapted = Adapt(resolved, parameter.ParameterType);
                        if (adapted == null)
                        {
                            return new ToolInvocation(false, null, DiagnosticCodes.InvalidArgs, "Tool '" + toolId + "' needs a " + parameter.ParameterType.Name + ", the target is a " + resolved.GetType().Name + ".");
                        }

                        arguments[i] = adapted;
                        targetBound = true;
                    }
                    else
                    {
                        arguments[i] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
                    }

                    continue;
                }

                string name = arg.Name ?? parameter.Name ?? string.Empty;
                JToken? raw = values[name];
                if (raw == null || raw.Type == JTokenType.Null)
                {
                    if (arg.Required && !parameter.HasDefaultValue)
                    {
                        return new ToolInvocation(false, null, DiagnosticCodes.InvalidArgs, "Tool '" + toolId + "' requires argument '" + name + "'.");
                    }

                    arguments[i] = parameter.HasDefaultValue ? parameter.DefaultValue : (parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null);
                    continue;
                }

                if (!_runtime.Resolver.Codec.TryToClr(raw, parameter.ParameterType, out object? converted, out string? problem))
                {
                    return new ToolInvocation(false, null, DiagnosticCodes.InvalidArgs, "Argument '" + name + "': " + problem);
                }

                arguments[i] = converted;
            }

            try
            {
                object? returned = method.Invoke(null, arguments);
                JToken? output = returned is string text ? new JValue(text) : (returned == null ? null : _runtime.Resolver.Codec.FromClr(returned));
                return new ToolInvocation(true, output, null, null);
            }
            catch (TargetInvocationException error)
            {
                Exception inner = error.InnerException ?? error;
                return new ToolInvocation(false, null, DiagnosticCodes.Refused, inner.Message);
            }
        }

        private MethodInfo? MethodOf(string toolId)
        {
            if (_methods.TryGetValue(toolId, out MethodInfo? known))
            {
                return known;
            }

            MethodInfo? found = null;
            foreach (MethodInfo method in AuthoringTypeCache.ToolMethods())
            {
                AuthorOperationAttribute? operation = AuthoringMetadata.Operation(method);
                if (operation != null && method.IsStatic && string.Equals(operation.ToolId, toolId, StringComparison.Ordinal))
                {
                    found = method;
                    break;
                }
            }

            _methods[toolId] = found;
            return found;
        }

        private static UnityEngine.Object? Adapt(UnityEngine.Object target, Type type)
        {
            if (type.IsInstanceOfType(target))
            {
                return target;
            }

            GameObject? gameObject = target as GameObject ?? (target as Component)?.gameObject;
            if (gameObject != null && typeof(Component).IsAssignableFrom(type))
            {
                Component? component = gameObject.GetComponent(type);
                return component == null ? null : component;
            }

            return null;
        }
    }
}
