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
            int inspected = 0;
            foreach (UnityEngine.Object target in targets ?? Array.Empty<UnityEngine.Object>())
            {
                if (inspected++ >= SceneContextObjects) break;
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

        /// <summary>The attachment name of <see cref="SceneContext"/>.</summary>
        public const string SceneContextName = "scene-context.json";
        public const int SceneContextBytes = 64 * 1024;
        public const int SceneContextObjects = 128;
        public const int SceneTextCharacters = 512;

        /// <summary>
        /// A request for <paramref name="targets"/>: the selection snapshot, the slice, the catalog revision and, when
        /// any target lives in a scene, the <see cref="SceneContext"/> attachment (the semantic index carries authored
        /// fields, not transforms, and a worker cannot move an object it cannot locate).
        /// </summary>
        public static AgentRequest ForObjects(StudioRuntime runtime, IReadOnlyList<UnityEngine.Object> targets, string intent, string mode = "design")
        {
            SelectionSnapshot selection = SnapshotOf(runtime, targets);
            List<Attachment> attachments = new List<Attachment>();
            Attachment? scene = SceneContext(runtime, targets);
            if (scene != null)
            {
                attachments.Add(scene);
            }

            return Build(runtime, selection, intent, attachments, mode);
        }

        /// <summary>
        /// <c>scene-context.json</c> (<c>gamecore.studio.scenecontext/1</c>): for each selected scene object its
        /// AuthoringRef, GameObject name and hierarchy path, scene, world position (metres), world rotation
        /// (quaternion), local scale and the world axes (+Z is north, +X east, +Y up). Null when no target is in a scene.
        /// </summary>
        public static Attachment? SceneContext(StudioRuntime runtime, IReadOnlyList<UnityEngine.Object> targets)
        {
            JArray objects = new JArray();
            bool truncated = false;
            int packedBytes = 512; // Envelope and truncation metadata reserve.
            int examined = 0;
            foreach (UnityEngine.Object target in targets)
            {
                if (examined++ >= SceneContextObjects) { truncated = true; break; }
                UnityEngine.GameObject? gameObject = target as UnityEngine.GameObject ?? (target as UnityEngine.Component)?.gameObject;
                if (gameObject == null || !gameObject.scene.IsValid())
                {
                    continue;
                }

                if (gameObject.name.Length > SceneTextCharacters || gameObject.scene.path.Length > SceneTextCharacters) truncated = true;
                UnityEngine.Transform transform = gameObject.transform;
                AuthoringRef? reference = runtime.Resolver.BuildRef(target, null, true);
                JObject entry = new JObject
                {
                    ["name"] = BoundedText(gameObject.name),
                    ["path"] = HierarchyPath(transform),
                    ["scene"] = BoundedText(gameObject.scene.path),
                    ["position"] = new JArray(Round(transform.position.x), Round(transform.position.y), Round(transform.position.z)),
                    ["rotation"] = new JArray(Round(transform.rotation.x), Round(transform.rotation.y), Round(transform.rotation.z), Round(transform.rotation.w)),
                    ["scale"] = new JArray(Round(transform.localScale.x), Round(transform.localScale.y), Round(transform.localScale.z)),
                };
                if (reference != null)
                {
                    JObject referenceJson = (JObject)new SecretRedactor().RedactJson(StudioJson.ToToken(reference));
                    foreach (JProperty property in referenceJson.Properties())
                        if (property.Value.Type == JTokenType.String) property.Value = BoundedText(property.Value.Value<string>() ?? "");
                    entry["ref"] = referenceJson;
                }

                entry = (JObject)new SecretRedactor().RedactJson(entry);
                int entryBytes = System.Text.Encoding.UTF8.GetByteCount(entry.ToString(Newtonsoft.Json.Formatting.None)) + 1;
                if (packedBytes + entryBytes > SceneContextBytes) { truncated = true; break; }
                packedBytes += entryBytes;
                objects.Add(entry);
            }

            if (objects.Count == 0 && !truncated)
            {
                return null;
            }

            JObject document = new JObject
            {
                ["schema"] = "gamecore.studio.scenecontext/1",
                ["units"] = "metres",
                ["axes"] = new JObject { ["north"] = "+z", ["east"] = "+x", ["up"] = "+y" },
                ["objects"] = objects,
                ["truncated"] = truncated,
            };
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(document.ToString(Newtonsoft.Json.Formatting.None));
            return new Attachment(SceneContextName, "application/json", bytes, "context");
        }

        private static double Round(float value) => Math.Round(value, 4);

        private static string BoundedText(string text)
        {
            string redacted = new SecretRedactor().Redact(text);
            return redacted.Length <= SceneTextCharacters ? redacted : redacted.Substring(0, SceneTextCharacters);
        }

        private static string HierarchyPath(UnityEngine.Transform transform)
        {
            string path = BoundedText(transform.name);
            int depth = 0;
            for (UnityEngine.Transform? parent = transform.parent; parent != null && depth++ < 32; parent = parent.parent)
            {
                path = BoundedText(parent.name) + "/" + path;
                if (path.Length >= SceneTextCharacters) break;
            }
            return BoundedText(path);
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
