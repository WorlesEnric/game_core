// GameCore.Studio.Views - whether a plugin [AuthorOperation] tool can receive all its parameters through the engine.
//
// The engine's reflected tools bind the target (the first parameter of an [Authorable] type without [AuthorArg]) and
// [AuthorArg] parameters; any other parameter without a default value arrives as null. P1.1's world.connectRegions
// (regionA, regionB), world.addPortal and world.setSpawnPoint (an AuthoredRegion, not [Authorable]) have such
// parameters, so a change set naming them fails when applied. Views check bindability and use a generic equivalent
// where one exists; the gap is listed in PACKET.md. Once the tools declare the parameters, the views use them.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;

namespace GameCore.Studio.Views
{
    public static class ToolBinding
    {
        /// <summary>The [AuthorOperation] method declaring <paramref name="toolId"/>, or null.</summary>
        public static MethodInfo? MethodOf(string toolId)
        {
            foreach (MethodInfo method in AuthoringTypeCache.ToolMethods())
            {
                AuthorOperationAttribute? operation = AuthoringMetadata.Operation(method);
                if (operation != null && string.Equals(operation.ToolId, toolId, StringComparison.Ordinal))
                {
                    return method;
                }
            }

            return null;
        }

        /// <summary>The parameters the engine cannot bind (empty when the tool is fully bindable or unknown).</summary>
        public static IReadOnlyList<string> UnboundParameters(string toolId)
        {
            List<string> unbound = new List<string>();
            MethodInfo? method = MethodOf(toolId);
            if (method == null)
            {
                return unbound;
            }

            bool targetSeen = false;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (AuthoringMetadata.Arg(parameter) != null || parameter.HasDefaultValue
                    || parameter.ParameterType == typeof(EditContext) || parameter.ParameterType == typeof(Operation))
                {
                    continue;
                }

                if (!targetSeen && AuthoringMetadata.Authorable(parameter.ParameterType) != null)
                {
                    targetSeen = true;
                    continue;
                }

                unbound.Add(parameter.Name ?? parameter.ParameterType.Name);
            }

            return unbound;
        }

        /// <summary>True when the tool exists and the engine can bind every parameter.</summary>
        public static bool IsBindable(string toolId) => MethodOf(toolId) != null && UnboundParameters(toolId).Count == 0;
    }
}
