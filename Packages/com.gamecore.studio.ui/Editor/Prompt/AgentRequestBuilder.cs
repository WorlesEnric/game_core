// GameCore.Studio.UI - builds an AgentRequest from the prompt bar (docs/studio/04-etos-integration.md s2 EditRequest,
// 03 s2/s3/s9): intent, SelectionSnapshot, the bounded index slice of the selection closure (depth 2, 64 KiB, truncation
// reported), the tool catalog revision, mode and attachments. The change-set id is minted here (02 s4 step 1).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.UI
{
    /// <summary>Request construction and the prompt bar's enablement rules.</summary>
    public sealed class AgentRequestBuilder
    {
        /// <summary>Selection closure depth of the context slice (03 s3).</summary>
        public const int SliceDepth = 2;

        /// <summary>Byte cap of the context slice sent with a prompt (P2.1: 64 KiB; the companion caps at 2 MiB).</summary>
        public const int SliceByteCap = 64 * 1024;

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
        public AgentRequest Build(
            string text,
            SelectionSnapshot selection,
            AgentRequestMode mode,
            IntentOrigin origin = IntentOrigin.Agent,
            string? voiceTranscriptId = null,
            IReadOnlyList<AgentAttachment>? attachments = null,
            string? parent = null,
            string? worker = null)
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
            JObject sliceJson = (JObject)StudioJson.ToToken(slice.Index);
            ToolCatalog catalog = _runtime.Registry.Catalog;
            string revision = catalog.Revision ?? catalog.ComputeRevision();
            Intent intent = new Intent(text.Trim(), origin, voiceTranscriptId);
            return new AgentRequest(
                IdDerivation.NewChangeSetId(),
                intent,
                selection,
                sliceJson,
                slice.Truncated,
                slice.Bytes,
                slice.OmittedNodes,
                revision,
                mode,
                attachments,
                parent,
                worker);
        }

        /// <summary>True when the intent text refers to a selection ("this", "these", "here", ...).</summary>
        public static bool IsSelectionScoped(string text) => !string.IsNullOrEmpty(text) && SelectionWords.IsMatch(text);

        /// <summary>
        /// Why the prompt cannot be sent, or null when it can: no gateway/node, no key (not configured), a refused or
        /// unreachable companion, no text, or a selection-scoped intent without a selection.
        /// </summary>
        public static string? DisabledReason(ProviderStatus status, string text, bool hasSelection)
        {
            switch (status.Connection)
            {
                case GatewayConnection.NotConfigured:
                    return "Not configured: " + (status.Detail ?? "no Studio companion is paired (GameCore/Studio/Settings).");
                case GatewayConnection.Disconnected:
                    return "No node: the Studio companion does not answer" + (status.Detail != null ? " (" + status.Detail + ")" : string.Empty) + ".";
                case GatewayConnection.Refused:
                    return "Refused by the node" + (status.Code != null ? " (" + status.Code + ")" : string.Empty) + ": " + (status.Detail ?? "check the app key and pairing.");
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
        public static AgentAttachment AttachmentFor(string path)
        {
            FileInfo info = new FileInfo(path);
            return new AgentAttachment(info.FullName, info.Name, MediaTypeOf(info.Extension), info.Exists ? info.Length : 0);
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
