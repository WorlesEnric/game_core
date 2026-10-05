// GameCore.Studio.Etos - context packing for an AI edit (02 s4 step 1, 03 s2/s3, 04 s2): the selection snapshot, the
// bounded semantic-index slice (selection closure at depth 2, cut at 64 KB with an explicit truncation flag by the
// index service; the companion caps at 2 MiB) and the tool catalog revision. The full catalog travels only when the
// companion does not hold that revision (hello.toolCatalogRevisions, or a stale_context answer).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos
{
    /// <summary>Builds requests from Studio state.</summary>
    public static class AgentRequestBuilder
    {
        /// <summary>The slice depth of 03 s3.</summary>
        public const int SliceDepth = 2;

        /// <summary>The slice budget of a request.</summary>
        public const int SliceBytes = 64 * 1024;

        /// <summary>A selection snapshot of <paramref name="targets"/> (Edit or Play mode by the editor state).</summary>
        public static SelectionSnapshot SnapshotOf(StudioRuntime runtime, IEnumerable<UnityEngine.Object> targets, AuthorScope? scope = null)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            List<AuthoringRef> refs = new List<AuthoringRef>();
            foreach (UnityEngine.Object target in targets ?? Array.Empty<UnityEngine.Object>())
            {
                AuthoringRef? reference = target == null ? null : runtime.Resolver.BuildRef(target, scope, true);
                if (reference != null)
                {
                    refs.Add(reference);
                }
            }

            string id = IdDerivation.NewSelectionId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CryptoIdEntropy.Instance);
            return new SelectionSnapshot(id, UnityEditor.EditorApplication.isPlaying ? SelectionMode.Play : SelectionMode.Edit, refs, runtime.Index.Revision);
        }

        /// <summary>A request for <paramref name="intent"/> on <paramref name="selection"/> with the runtime's slice and catalog revision.</summary>
        public static AgentRequest Build(StudioRuntime runtime, SelectionSnapshot selection, string intent, IEnumerable<Attachment>? attachments = null, string mode = "design")
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            IndexSlice slice = runtime.Index.Slice(selection.Targets, SliceDepth, SliceBytes);
            string revision = runtime.Registry.Catalog.Revision ?? runtime.Registry.Catalog.ComputeRevision();
            AgentRequest request = new AgentRequest(intent, selection, slice.Index, revision) { Mode = mode };
            if (attachments != null)
            {
                request.Attachments.AddRange(attachments);
            }

            return request;
        }

        /// <summary>The companion body of <paramref name="request"/>; the catalog is attached only when given.</summary>
        public static EditRequestBody ToBody(AgentRequest request, string changeSetId, string designWorker, string mechanismWorker, ToolCatalog? catalog)
        {
            JObject selection = (JObject)StudioJson.ToToken(request.Selection);
            JObject slice = (JObject)StudioJson.ToToken(request.ContextSlice);
            string origin = request.VoiceTranscriptId != null ? "voice" : "agent";
            EditRequestBody body = new EditRequestBody(changeSetId, request.Intent, origin, selection, slice, request.ToolCatalogRevision)
            {
                VoiceTranscriptId = request.VoiceTranscriptId,
                Worker = string.Equals(request.Mode, "mechanism", StringComparison.Ordinal) ? mechanismWorker : designWorker,
            };
            if (catalog != null)
            {
                body.ToolCatalog = (JObject)StudioJson.ToToken(catalog);
            }

            foreach (Attachment attachment in request.Attachments)
            {
                body.Attachments.Add(new AttachmentBody(attachment.Name, attachment.MediaType, attachment.Data, attachment.Role));
            }

            return body;
        }
    }
}
