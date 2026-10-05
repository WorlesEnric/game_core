// GameCore.Studio.UI - builds P2.2's AgentRequest from the prompt bar (docs/studio/04-etos-integration.md s2
// EditRequest, 03 s2/s3/s9): intent, SelectionSnapshot, the bounded index slice of the selection closure (depth 2,
// 64 KiB, truncation reported), the tool catalog revision, worker mode and attachments (read from disk, at most 16 MiB
// each), plus P2.2's scene-context.json (gamecore.studio.scenecontext/1: name, hierarchy path, scene, world position,
// rotation, scale and the axes +Z north / +X east / +Y up) for selected scene objects, because the semantic index
// carries authored fields, not transforms, and a worker cannot move what it cannot locate (seen live: gc-designer asked
// for the well's position). The change-set id is minted here (02 s4 step 1); P2.2 answers with it as the request id.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using AgentAttachment = GameCore.Studio.Authoring.Agent.Attachment;

namespace GameCore.Studio.UI
{
    /// <summary>A file dragged onto the prompt bar (read when the request is built).</summary>
    public sealed class PromptAttachment
    {
        public PromptAttachment(string path, string name, string mediaType, long bytes)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
            Bytes = bytes;
        }

        public string Path { get; }

        public string Name { get; }

        public string MediaType { get; }

        public long Bytes { get; }
    }

    /// <summary>A built request plus what the UI shows about it (slice size and truncation, viewport mode, intent origin).</summary>
    public sealed class PreparedRequest
    {
        public PreparedRequest(AgentRequest request, Intent intent, int contextBytes, bool contextTruncated, int contextOmittedNodes)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            ContextBytes = contextBytes;
            ContextTruncated = contextTruncated;
            ContextOmittedNodes = contextOmittedNodes;
        }

        /// <summary>What is sent (P2.2's request; its ChangeSetId is set).</summary>
        public AgentRequest Request { get; }

        public string ChangeSetId => Request.ChangeSetId ?? string.Empty;

        /// <summary>The intent with its origin (agent or voice) and transcript id.</summary>
        public Intent Intent { get; }

        public SelectionSnapshot Selection => Request.Selection;

        public string ToolCatalogRevision => Request.ToolCatalogRevision;

        public string? Parent => Request.Parent;

        public int ContextBytes { get; }

        public bool ContextTruncated { get; }

        public int ContextOmittedNodes { get; }
    }

    /// <summary>Request construction and the prompt bar's enablement rules.</summary>
    public sealed class AgentRequestBuilder
    {
        /// <summary>Selection closure depth of the context slice (03 s3).</summary>
        public const int SliceDepth = 2;

        /// <summary>Byte cap of the context slice sent with a prompt (P2.1: 64 KiB; the companion caps at 2 MiB).</summary>
        public const int SliceByteCap = 64 * 1024;

        /// <summary>Attachments larger than this are refused (04 s2: 16 MiB).</summary>
        public const long MaxAttachmentBytes = 16L * 1024 * 1024;

        /// <summary>The default worker mode (gc-designer); <c>mechanism</c> selects gc-mechanic.</summary>
        public const string DesignWorker = "design";

        private static readonly Regex SelectionWords = new Regex(
            @"\b(this|these|that|those|it|them|selected|selection|here|there)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly StudioRuntime _runtime;

        public AgentRequestBuilder(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>Slice depth (default <see cref="SliceDepth"/>).</summary>
        public int Depth { get; set; } = SliceDepth;

        /// <summary>Slice byte cap (default <see cref="SliceByteCap"/>; tests lower it to force truncation).</summary>
        public int ByteCap { get; set; } = SliceByteCap;

        /// <summary>Builds a request; nothing is sent.</summary>
        public PreparedRequest Build(
            string text,
            SelectionSnapshot selection,
            IntentOrigin origin = IntentOrigin.Agent,
            string? voiceTranscriptId = null,
            IReadOnlyList<PromptAttachment>? attachments = null,
            string? parent = null,
            string worker = DesignWorker)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("An intent needs text.", nameof(text));
            }

            if (selection == null)
            {
                throw new ArgumentNullException(nameof(selection));
            }

            if (!_runtime.Index.IsBuilt)
            {
                _runtime.Index.Rebuild();
            }

            List<AuthoringRef> closure = new List<AuthoringRef>();
            foreach (AuthoringRef target in selection.Targets)
            {
                if (target.Kind != AuthoringKind.Location)
                {
                    closure.Add(target);
                }
            }

            if (selection.Parts != null)
            {
                foreach (PartRef part in selection.Parts)
                {
                    closure.Add(part.Owner);
                }
            }

            IndexSlice slice = _runtime.Index.Slice(closure, Depth, ByteCap);
            ToolCatalog catalog = _runtime.Registry.Catalog;
            string revision = catalog.Revision ?? catalog.ComputeRevision();
            Intent intent = new Intent(text.Trim(), origin, voiceTranscriptId);
            AgentRequest request = new AgentRequest(intent.Text, selection, slice.Index, revision)
            {
                Mode = worker,
                Parent = parent,
                VoiceTranscriptId = voiceTranscriptId,
                ChangeSetId = IdDerivation.NewChangeSetId(),
            };
            foreach (PromptAttachment attachment in attachments ?? Array.Empty<PromptAttachment>())
            {
                FileInfo file = new FileInfo(attachment.Path);
                if (!file.Exists)
                {
                    throw new FileNotFoundException("The attachment is gone: " + attachment.Path, attachment.Path);
                }

                if (file.Length > MaxAttachmentBytes)
                {
                    throw new ArgumentException("The attachment " + attachment.Name + " is larger than 16 MiB.", nameof(attachments));
                }

                request.Attachments.Add(new AgentAttachment(attachment.Name, attachment.MediaType, File.ReadAllBytes(file.FullName), "reference"));
            }

            AgentAttachment? scene = SceneContext(closure);
            if (scene != null)
            {
                request.Attachments.Add(scene);
            }

            return new PreparedRequest(request, intent, slice.Bytes, slice.Truncated, slice.OmittedNodes);
        }

        /// <summary>The attachment name of the scene context (the same as P2.2's AgentRequestBuilder.SceneContextName).</summary>
        public const string SceneContextName = "scene-context.json";

        /// <summary>
        /// <c>scene-context.json</c> for the targets that resolve to scene objects (P2.2's format), or null when none does.
        /// </summary>
        public AgentAttachment? SceneContext(IReadOnlyList<AuthoringRef> targets)
        {
            JArray objects = new JArray();
            foreach (AuthoringRef target in targets)
            {
                UnityEngine.Object? resolved = _runtime.Resolver.Find(target);
                UnityEngine.GameObject? gameObject = resolved as UnityEngine.GameObject ?? (resolved as UnityEngine.Component)?.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid())
                {
                    continue;
                }

                UnityEngine.Transform transform = gameObject.transform;
                objects.Add(new JObject
                {
                    ["ref"] = StudioJson.ToToken(target),
                    ["name"] = gameObject.name,
                    ["path"] = HierarchyPath(transform),
                    ["scene"] = gameObject.scene.path,
                    ["position"] = new JArray(Round(transform.position.x), Round(transform.position.y), Round(transform.position.z)),
                    ["rotation"] = new JArray(Round(transform.rotation.x), Round(transform.rotation.y), Round(transform.rotation.z), Round(transform.rotation.w)),
                    ["scale"] = new JArray(Round(transform.localScale.x), Round(transform.localScale.y), Round(transform.localScale.z)),
                });
            }

            if (objects.Count == 0)
            {
                return null;
            }

            JObject document = new JObject
            {
                ["schema"] = "gamecore.studio.scenecontext/1",
                ["units"] = "metres",
                ["axes"] = new JObject { ["north"] = "+z", ["east"] = "+x", ["up"] = "+y" },
                ["objects"] = objects,
            };
            return new AgentAttachment(SceneContextName, "application/json", System.Text.Encoding.UTF8.GetBytes(document.ToString(Newtonsoft.Json.Formatting.Indented)), "context");
        }

        private static double Round(float value) => Math.Round(value, 4);

        private static string HierarchyPath(UnityEngine.Transform transform)
        {
            string path = transform.name;
            for (UnityEngine.Transform? parent = transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        /// <summary>True when the intent text refers to a selection ("this", "these", "here", ...).</summary>
        public static bool IsSelectionScoped(string text) => !string.IsNullOrEmpty(text) && SelectionWords.IsMatch(text);

        /// <summary>
        /// Why the prompt cannot be sent, or null when it can: no gateway/key (not configured), an unreachable or
        /// refusing companion, an agent not connected yet, no text, or a selection-scoped intent without a selection.
        /// </summary>
        public static string? DisabledReason(ProviderStatus status, string text, bool hasSelection)
        {
            Diagnostic? problem = status.Problem;
            switch (ProviderNames.ConnectionOf(status))
            {
                case GatewayConnection.NotConfigured:
                    return "Not configured: " + (problem?.Message ?? "no Studio companion is paired (Project Settings > GameCore Studio > ETOS).");
                case GatewayConnection.Disconnected:
                    return "No node: the Studio companion does not answer" + (problem != null ? " (" + problem.Code + ": " + problem.Message + ")" : string.Empty) + ".";
                case GatewayConnection.Refused:
                    return "Refused by the node (" + problem?.Code + "): " + (problem?.Hint ?? problem?.Message ?? "check the app key and pairing.");
                case GatewayConnection.AgentStarting:
                    return "The Studio agent is not connected yet" + (problem != null ? " (" + problem.Code + ")" : string.Empty) + "; wait a moment.";
                case GatewayConnection.Connecting:
                    return "Connecting to the Studio companion...";
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return "Type an intent (Ctrl+Enter sends).";
            }

            if (!hasSelection && IsSelectionScoped(text))
            {
                return "This intent refers to a selection; select something in the viewport (or point at a location) first.";
            }

            return null;
        }

        /// <summary>A short summary of a snapshot for the tray ("2 targets, 1 location").</summary>
        public static string Summarize(SelectionSnapshot selection)
        {
            int locations = 0;
            int targets = 0;
            foreach (AuthoringRef target in selection.Targets)
            {
                if (target.Kind == AuthoringKind.Location)
                {
                    locations++;
                }
                else
                {
                    targets++;
                }
            }

            string text = targets == 1 ? "1 target" : targets + " targets";
            if (locations > 0)
            {
                text += ", " + (locations == 1 ? "1 location" : locations + " locations");
            }

            return text + " (" + selection.Mode + ")";
        }

        /// <summary>An attachment for a dragged file (media type from the extension).</summary>
        public static PromptAttachment AttachmentFor(string path)
        {
            FileInfo info = new FileInfo(path);
            return new PromptAttachment(info.FullName, info.Name, MediaTypeOf(info.Extension), info.Exists ? info.Length : 0);
        }

        public static string MediaTypeOf(string extension)
        {
            switch (extension.ToLowerInvariant())
            {
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".wav":
                    return "audio/wav";
                case ".mp3":
                    return "audio/mpeg";
                case ".ogg":
                    return "audio/ogg";
                case ".txt":
                case ".md":
                    return "text/plain";
                case ".json":
                    return "application/json";
                case ".fbx":
                    return "model/fbx";
                case ".glb":
                    return "model/gltf-binary";
                default:
                    return "application/octet-stream";
            }
        }
    }
}
