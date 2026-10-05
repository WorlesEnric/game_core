#nullable enable
using System;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

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

    /// <summary>Runs tools through the registry's authority and argument checks.</summary>
    public sealed class ReadOnlyToolInvoker
    {
        private readonly StudioRuntime _runtime;

        public ReadOnlyToolInvoker(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool IsAvailable(string toolId) => _runtime.Registry.Find(toolId)?.ReadOnly == true;

        public ToolInvocation Invoke(string toolId, AuthoringRef? target, JObject? args)
        {
            OperationResult result = _runtime.Registry.Invoke(toolId, target, args);
            return result.Status == OutcomeStatus.Applied
                ? new ToolInvocation(true, result.Output, null, null)
                : new ToolInvocation(false, null, result.Code, result.Detail);
        }
    }
}
