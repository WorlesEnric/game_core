#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEditor;

namespace GameCore.Gameplay.World.Editor
{
    /// <summary>The trusted world adapter must validate route, target, schema and all arguments without side effects.
    /// It must encode a revision-checked command, not submit one while translating.</summary>
    public interface IWorldLiveActionBridge
    {
        CompositionEditPayload? Translate(EditContext context, ulong expectedRevision, out Diagnostic? problem);
    }

    /// <summary>Translates catalog-declared runtime actions, including admitted mechanism routes.</summary>
    public sealed class WorldLiveOpTranslator : ILiveOpTranslator
    {
        public IWorldLiveActionBridge? Bridge { get; set; }

        public bool CanTranslate(EditContext context)
        {
            ToolEntry? entry = context.Runtime.Registry.Find(context.Operation.Tool)?.Entry;
            return entry != null && entry.RuntimeOnly && entry.RuntimeApply == RuntimeApply.Live;
        }

        public CompositionEditPayload? Translate(EditContext context, out Diagnostic? problem)
        {
            if (!CanTranslate(context) || !context.IsPlayMode || !context.Runtime.Live.IsAvailable)
            {
                problem = context.Problem(DiagnosticCodes.Refused, "Runtime actions require a catalog declaration and an available Play world.");
                return null;
            }

            if (!context.TryArg<ulong>("expectedRevision", out ulong expected, out string? detail))
            {
                problem = context.Problem(DiagnosticCodes.Refused, detail ?? "An explicit expectedRevision is required.");
                return null;
            }

            if (expected != context.Runtime.Live.CommittedRevision)
            {
                problem = context.Problem(DiagnosticCodes.Conflict, "The runtime action's expected revision no longer matches the live world.");
                return null;
            }

            if (Bridge == null)
            {
                problem = context.Problem(DiagnosticCodes.NotConfigured,
                    "The world command-to-composition bridge is unavailable; no runtime command was submitted.");
                return null;
            }

            return Bridge.Translate(context, expected, out problem);
        }

        public static WorldLiveOpTranslator Register(StudioRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            foreach (ILiveOpTranslator existing in runtime.Services.LiveTranslators)
            {
                if (existing is WorldLiveOpTranslator translator) return translator;
            }

            var created = new WorldLiveOpTranslator();
            runtime.Services.RegisterLiveTranslator(created);
            RegisterTool(runtime, "world.travel", new ArgSpec("regionId", "string", true), new ArgSpec("portalId", "string", false));
            RegisterTool(runtime, "dialogue.start", new ArgSpec("graphId", "string", true));
            RegisterTool(runtime, "entity.spawn", new ArgSpec("visible", "bool", false));
            RegisterTool(runtime, "entity.despawn");
            RegisterTool(runtime, "npc.goTo", new ArgSpec("x", "int", true), new ArgSpec("y", "int", true), new ArgSpec("z", "int", true));
            return created;
        }
        private static void RegisterTool(StudioRuntime runtime, string id, params ArgSpec[] args)
        {
            if (runtime.Registry.Find(id) != null) return;
            var specs = new List<ArgSpec>(args) { new ArgSpec("expectedRevision", "int", true, min: 0) };
            runtime.Registry.Register(new RuntimeActionTool(new ToolEntry(id, ToolTier.Configure, RuntimeApply.Live,
                true, specs, doc: "Revision-checked runtime action. No authored changes or inverse.", runtimeOnly: true)));
        }

        private sealed class RuntimeActionTool : IStudioTool
        {
            public RuntimeActionTool(ToolEntry entry) => Entry = entry;
            public ToolEntry Entry { get; }
            public bool Internal => false;
            public bool ReadOnly => false;
            public ToolStageResult Stage(EditContext context)
            {
                var result = new ToolStageResult();
                ILiveOpTranslator? translator = context.Services.FindLiveTranslator(context);
                if (translator == null) return result.Add(context.Problem(DiagnosticCodes.NotConfigured, "No world translator is registered."));
                if (translator.Translate(context, out Diagnostic? problem) == null)
                    result.Add(problem ?? context.Problem(DiagnosticCodes.Refused, "The runtime action could not be validated."));
                return result;
            }

            public OperationResult Apply(EditContext context) => OperationResult.Refused(DiagnosticCodes.Refused,
                "Runtime actions execute only through the ChangeSetEngine live gateway.");
        }

    }

    // First-party Editor integration: D1's ban on candidate InitializeOnLoad code does not apply here.
    [InitializeOnLoad]
    internal static class WorldStudioRegistration
    {
        static WorldStudioRegistration() => EditorApplication.update += Bind;

        private static void Bind()
        {
            if (StudioServices.HasRuntime) WorldLiveOpTranslator.Register(StudioServices.Runtime);
        }
    }
}
