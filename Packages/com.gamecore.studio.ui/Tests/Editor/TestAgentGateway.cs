// GameCore.Studio.UI.Tests - the EditMode-only gateway (P2.2's IAgentGateway plus the P1.6 tool-facing one, like the
// etos gateway): records submitted requests, replays canned candidates from the JSON fixtures in Tests/Editor/Fixtures
// (placeholders "$ref:<name>", "$csid", "$catalog" substituted per test) and lets tests raise gateway events. With
// ImportOwn it behaves like P2.2's AutoImport (stages its own candidates and reports LocalState "staged"). It is never
// referenced by production code (06 s4).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using AgentGateway = GameCore.Studio.Authoring.Agent.IAgentGateway;
using ToolGateway = GameCore.Studio.Authoring.IAgentGateway;

namespace GameCore.Studio.UI.Tests
{
    /// <summary>A replayed candidate.</summary>
    internal sealed class TestCandidate
    {
        public TestCandidate(string requestId, ChangeSet changeSet, string? toolCatalogRevision)
        {
            RequestId = requestId;
            ChangeSet = changeSet;
            ToolCatalogRevision = toolCatalogRevision;
        }

        public string RequestId { get; }

        public ChangeSet ChangeSet { get; }

        public string? ToolCatalogRevision { get; }
    }

    internal sealed class TestAgentGateway : AgentGateway, ToolGateway
    {
        public const string FixtureFolder = "Packages/com.gamecore.studio.ui/Tests/Editor/Fixtures";

        private readonly Dictionary<string, TestCandidate> _candidates = new Dictionary<string, TestCandidate>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, StagedChangeSet> _staged = new Dictionary<string, StagedChangeSet>(StringComparer.Ordinal);

        public TestAgentGateway()
        {
            Status = new ProviderStatus(ProviderState.Live, ProviderState.Live, ProviderState.Live, ProviderState.NotConfigured, ProviderState.Live, true, true, "test", null, DateTime.UtcNow);
        }

        public bool IsConfigured => true;

        public ProviderStatus Status { get; set; }

        public List<RequestView> Listed { get; } = new List<RequestView>();

        public IReadOnlyList<RequestView> Requests => Listed;

        public event Action<ProviderStatus>? StatusChanged;

        public event Action<RequestView>? RequestChanged;

        public event Action<CandidateNotice>? CandidateReady;

        public List<AgentRequest> Submitted { get; } = new List<AgentRequest>();

        public List<string> Cancelled { get; } = new List<string>();

        public List<KeyValuePair<string, string>> Rejected { get; } = new List<KeyValuePair<string, string>>();

        public int ArtifactFetches { get; private set; }

        /// <summary>The next voice session handed out.</summary>
        public ScriptedVoiceSession Voice { get; set; } = new ScriptedVoiceSession();

        /// <summary>When set, SubmitAsync refuses with this diagnostic (code preserved).</summary>
        public Diagnostic? RefuseWith { get; set; }

        // ------------------------------------------------- P2.2 AutoImport look-alike (read by GatewayExtras by name)

        /// <summary>The candidates this gateway staged itself.</summary>
        public IReadOnlyDictionary<string, StagedChangeSet> Staged => _staged;

        public TestGatewayOptions Options { get; } = new TestGatewayOptions();

        public bool IsOwn(string requestId) => Submitted.Exists(request => request.ChangeSetId == requestId) || _candidates.ContainsKey(requestId);

        public void Reject(string requestId, string reason)
        {
            if (_staged.TryGetValue(requestId, out StagedChangeSet? staged) && !staged.Consumed)
            {
                throw new InvalidOperationException("The UI should discard the staged change set before telling the gateway.");
            }

            Rejected.Add(new KeyValuePair<string, string>(requestId, reason));
        }

        /// <summary>Stages a loaded candidate like EtosAgentGateway.ImportCandidateAsync (artifacts retained, Candidate mode).</summary>
        public StagedChangeSet Import(StudioRuntime runtime, string requestId)
        {
            TestCandidate candidate = _candidates[requestId];
            foreach (ArtifactRef artifact in candidate.ChangeSet.Artifacts ?? Array.Empty<ArtifactRef>())
            {
                runtime.Artifacts.Put(_artifacts[artifact.Sha256], artifact);
            }

            StagedChangeSet staged = runtime.Engine.Stage(candidate.ChangeSet, new StageOptions { Mode = ValidationMode.Candidate, ToolCatalogRevision = candidate.ToolCatalogRevision });
            _staged[requestId] = staged;
            return staged;
        }

        // ------------------------------------------------- IAgentGateway (UI-facing)

        public Task<string> SubmitAsync(AgentRequest req, CancellationToken ct)
        {
            if (RefuseWith != null)
            {
                return Task.FromException<string>(new StudioGatewayException(RefuseWith));
            }

            Submitted.Add(req);
            return Task.FromResult(req.ChangeSetId ?? IdDerivation.NewChangeSetId());
        }

        public Task CancelAsync(string requestId, CancellationToken ct)
        {
            Cancelled.Add(requestId);
            return Task.CompletedTask;
        }

        public Task<ChangeSet> FetchCandidateAsync(string requestId, CancellationToken ct)
        {
            return _candidates.TryGetValue(requestId, out TestCandidate? candidate)
                ? Task.FromResult(candidate.ChangeSet)
                : Task.FromException<ChangeSet>(new StudioGatewayException(new Diagnostic("not_found", "No candidate for " + requestId + ".")));
        }

        public Task<byte[]> FetchArtifactAsync(string sha256, CancellationToken ct)
        {
            ArtifactFetches++;
            return _artifacts.TryGetValue(sha256, out byte[]? bytes)
                ? Task.FromResult(bytes)
                : Task.FromException<byte[]>(new StudioGatewayException(new Diagnostic("not_found", "No artifact sha256:" + sha256 + ".")));
        }

        public Task<OpResult> GenerateAsync(OpRequest req, CancellationToken ct) => Task.FromResult(OpResult.Refused(new Diagnostic("not_configured", "The test gateway generates nothing.")));

        public IVoiceSession CreateVoiceSession() => Voice;

        // ------------------------------------------------- IAgentGateway (tool-facing, P1.6)

        public ServiceResult GenerateAsset(AgentAssetRequest request) => new ServiceResult(ServiceRequestStatus.Accepted, taskId: "t_test_asset");

        public ServiceResult ProposeMechanism(AgentMechanismRequest request) => new ServiceResult(ServiceRequestStatus.Accepted, taskId: "t_test_mechanism");

        // ------------------------------------------------- test controls

        /// <summary>Raises RequestChanged (synchronously, like a transport callback would).</summary>
        public void Emit(RequestView view) => RequestChanged?.Invoke(view);

        public void EmitCandidate(string requestId, string changeSetId) => CandidateReady?.Invoke(new CandidateNotice(requestId, changeSetId));

        public void EmitStatus(ProviderStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }

        /// <summary>A RequestView as the companion would report it.</summary>
        public static RequestView View(string id, string state, string? intent = null, string? worker = null, string? taskStatus = null, JObject? outcome = null, long seq = 1, string? localState = null, bool hasCandidate = false, string? progress = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new RequestView(id, id, state, worker, "t_" + id, taskStatus, 1, new[] { "t_" + id }, outcome, hasCandidate, seq, now - 5000, now, intent, progress, localState);
        }

        /// <summary>Reads a fixture file's text.</summary>
        public static string FixtureText(string fileName)
        {
            string assetPath = FixtureFolder + "/" + fileName;
            TextAsset? asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset != null)
            {
                return asset.text;
            }

            return File.ReadAllText(Path.GetFullPath(assetPath));
        }

        /// <summary>
        /// Loads a candidate fixture, substitutes placeholders, registers it for <see cref="FetchCandidateAsync"/> (keyed by
        /// <paramref name="requestId"/>, else the fixture's) and its artifact bodies for <see cref="FetchArtifactAsync"/>.
        /// </summary>
        public TestCandidate LoadCandidate(string fileName, IReadOnlyDictionary<string, AuthoringRef> refs, string catalogRevision, string? changeSetId = null)
        {
            JObject root = JObject.Parse(FixtureText(fileName));
            string id = changeSetId ?? IdDerivation.NewChangeSetId();
            Substitute(root, refs, new Dictionary<string, string> { ["$csid"] = id, ["$catalog"] = catalogRevision });
            ChangeSet changeSet = StudioJson.Deserialize<ChangeSet>(root["changeSet"]!.ToString());
            if (root["artifactBodies"] is JObject bodies)
            {
                foreach (JProperty body in bodies.Properties())
                {
                    _artifacts[body.Name] = Convert.FromBase64String((string)body.Value!);
                }
            }

            string requestId = changeSetId ?? (string)root["requestId"]!;
            TestCandidate candidate = new TestCandidate(requestId, changeSet, (string?)root["toolCatalogRevision"]);
            _candidates[requestId] = candidate;
            return candidate;
        }

        /// <summary>Loads a request list fixture with the given substitutions into <see cref="Listed"/>.</summary>
        public void LoadRequests(string fileName, IReadOnlyDictionary<string, string> values)
        {
            JObject root = JObject.Parse(FixtureText(fileName));
            Substitute(root, new Dictionary<string, AuthoringRef>(), values);
            Listed.Clear();
            foreach (JToken item in (JArray)root["requests"]!)
            {
                string id = (string)item["requestId"]!;
                Listed.Add(new RequestView(
                    id,
                    (string)item["changeSetId"]!,
                    (string)item["state"]!,
                    (string?)item["worker"],
                    (string?)item["taskId"],
                    (string?)item["taskStatus"],
                    1,
                    item["tasks"] is JArray tasks ? tasks.ToObject<List<string>>()! : new List<string>(),
                    item["outcome"] as JObject,
                    (bool?)item["hasCandidate"] ?? false,
                    (long?)item["seq"] ?? 0,
                    (long?)item["createdAt"] ?? 0,
                    (long?)item["updatedAt"] ?? 0,
                    (string?)item["intent"]));
            }
        }

        private static void Substitute(JToken token, IReadOnlyDictionary<string, AuthoringRef> refs, IReadOnlyDictionary<string, string> values)
        {
            if (token is JObject obj)
            {
                foreach (JProperty property in new List<JProperty>(obj.Properties()))
                {
                    JToken? replacement = Replacement(property.Value, refs, values);
                    if (replacement != null)
                    {
                        property.Value = replacement;
                    }
                    else
                    {
                        Substitute(property.Value, refs, values);
                    }
                }
            }
            else if (token is JArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    JToken? replacement = Replacement(array[i], refs, values);
                    if (replacement != null)
                    {
                        array[i] = replacement;
                    }
                    else
                    {
                        Substitute(array[i], refs, values);
                    }
                }
            }
        }

        private static JToken? Replacement(JToken token, IReadOnlyDictionary<string, AuthoringRef> refs, IReadOnlyDictionary<string, string> values)
        {
            if (token.Type != JTokenType.String)
            {
                return null;
            }

            string text = (string)token!;
            if (text.StartsWith("$ref:", StringComparison.Ordinal))
            {
                string name = text.Substring(5);
                if (!refs.TryGetValue(name, out AuthoringRef? reference))
                {
                    throw new InvalidOperationException("The fixture needs a ref named " + name + ".");
                }

                return StudioJson.ToToken(reference);
            }

            return values.TryGetValue(text, out string? value) ? new JValue(value) : null;
        }
    }

    /// <summary>The AutoImport switch the UI reads by name (P2.2's EtosGatewayOptions.AutoImport).</summary>
    internal sealed class TestGatewayOptions
    {
        public bool AutoImport { get; set; }
    }

    /// <summary>A voice session the tests drive by hand (no microphone).</summary>
    internal sealed class ScriptedVoiceSession : IVoiceSession
    {
        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public event Action<TranscriptUpdate>? Transcript;

        public event Action<float>? Level;

        public event Action<Diagnostic>? Error;

        public Task StartAsync()
        {
            Starts++;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stops++;
            return Task.CompletedTask;
        }

        public void Say(TranscriptUpdate transcript) => Transcript?.Invoke(transcript);

        public void Meter(float level) => Level?.Invoke(level);

        public void Fail(Diagnostic diagnostic) => Error?.Invoke(diagnostic);
    }
}
