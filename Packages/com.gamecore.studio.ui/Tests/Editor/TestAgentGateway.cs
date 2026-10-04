// GameCore.Studio.UI.Tests - the EditMode-only gateway: records submitted requests, replays canned candidates from the
// JSON fixtures in Tests/Editor/Fixtures (placeholders "$ref:<name>", "$csid", "$catalog" substituted per test) and lets
// tests push gateway events. It is never referenced by production code (06 s4).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI.Tests
{
    internal sealed class TestAgentGateway : IStudioAgentGateway
    {
        public const string FixtureFolder = "Packages/com.gamecore.studio.ui/Tests/Editor/Fixtures";

        private readonly List<IObserver<AgentEvent>> _observers = new List<IObserver<AgentEvent>>();
        private readonly Dictionary<string, AgentCandidate> _candidates = new Dictionary<string, AgentCandidate>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private int _requests;

        public TestAgentGateway()
        {
            Status = new ProviderStatus(
                GatewayConnection.Connected,
                new Dictionary<string, ProviderAvailability>
                {
                    [ProviderNames.Image] = ProviderAvailability.Live,
                    [ProviderNames.Tts] = ProviderAvailability.Live,
                    [ProviderNames.Voice] = ProviderAvailability.Live,
                    [ProviderNames.ThreeD] = ProviderAvailability.NotConfigured,
                    [ProviderNames.Describe] = ProviderAvailability.Live,
                },
                "test gateway",
                "test");
        }

        public bool IsConfigured => true;

        public ProviderStatus Status { get; set; }

        public IObservable<AgentEvent> Events => new Stream(this);

        public IVoiceSession? Voice { get; set; }

        public List<AgentRequest> Submitted { get; } = new List<AgentRequest>();

        public List<string> Cancelled { get; } = new List<string>();

        public List<KeyValuePair<string, string>> Rejected { get; } = new List<KeyValuePair<string, string>>();

        public List<AgentRequestInfo> Listed { get; } = new List<AgentRequestInfo>();

        public int ArtifactFetches { get; private set; }

        public int Subscribers => _observers.Count;

        public ServiceResult GenerateAsset(AgentAssetRequest request) => new ServiceResult(ServiceRequestStatus.Accepted, taskId: "t_test_asset");

        public ServiceResult ProposeMechanism(AgentMechanismRequest request) => new ServiceResult(ServiceRequestStatus.Accepted, taskId: "t_test_mechanism");

        public Task<RequestHandle> Submit(AgentRequest request)
        {
            Submitted.Add(request);
            _requests++;
            return Task.FromResult(new RequestHandle(request.ChangeSetId, AgentRequestState.Queued, "req_test_" + _requests, "t_test_" + _requests));
        }

        public Task<AgentRequestInfo?> Cancel(string requestId)
        {
            Cancelled.Add(requestId);
            return Task.FromResult<AgentRequestInfo?>(null);
        }

        public Task<IReadOnlyList<AgentRequestInfo>> ListRequests(long after = 0) => Task.FromResult<IReadOnlyList<AgentRequestInfo>>(new List<AgentRequestInfo>(Listed));

        public Task<AgentCandidate> FetchCandidate(string requestId)
        {
            return _candidates.TryGetValue(requestId, out AgentCandidate? candidate)
                ? Task.FromResult(candidate)
                : Task.FromException<AgentCandidate>(new AgentGatewayException(new Diagnostic(DiagnosticCodes.Refused, "No candidate for " + requestId + ".")));
        }

        public Task<byte[]> FetchArtifact(string sha256)
        {
            ArtifactFetches++;
            return _artifacts.TryGetValue(sha256, out byte[]? bytes)
                ? Task.FromResult(bytes)
                : Task.FromException<byte[]>(new AgentGatewayException(new Diagnostic(DiagnosticCodes.Refused, "No artifact sha256:" + sha256 + ".")));
        }

        public Task RejectCandidate(string changeSetId, string reason)
        {
            Rejected.Add(new KeyValuePair<string, string>(changeSetId, reason));
            return Task.CompletedTask;
        }

        /// <summary>Emits an event to every subscriber (synchronously, like a transport thread would).</summary>
        public void Emit(AgentEvent agentEvent)
        {
            foreach (IObserver<AgentEvent> observer in _observers.ToArray())
            {
                observer.OnNext(agentEvent);
            }
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
        /// Loads a candidate fixture, substitutes placeholders, registers it for <see cref="FetchCandidate"/> and its
        /// artifact bodies for <see cref="FetchArtifact"/>; returns the candidate.
        /// </summary>
        public AgentCandidate LoadCandidate(string fileName, IReadOnlyDictionary<string, AuthoringRef> refs, string catalogRevision)
        {
            JObject root = JObject.Parse(FixtureText(fileName));
            string changeSetId = IdDerivation.NewChangeSetId();
            Substitute(root, refs, new Dictionary<string, string> { ["$csid"] = changeSetId, ["$catalog"] = catalogRevision });
            ChangeSet changeSet = StudioJson.Deserialize<ChangeSet>(root["changeSet"]!.ToString());
            List<ArtifactRef> artifacts = new List<ArtifactRef>(changeSet.Artifacts ?? Array.Empty<ArtifactRef>());
            if (root["artifactBodies"] is JObject bodies)
            {
                foreach (JProperty body in bodies.Properties())
                {
                    _artifacts[body.Name] = Convert.FromBase64String((string)body.Value!);
                }
            }

            string requestId = (string)root["requestId"]!;
            AgentCandidate candidate = new AgentCandidate(requestId, changeSet, (string?)root["toolCatalogRevision"], artifacts, null, (string?)root["taskId"]);
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
                AgentRequestState state = AgentRequestStates.Parse((string?)item["state"]) ?? AgentRequestState.Unresolved;
                List<string> taskIds = new List<string>();
                if (item["taskIds"] is JArray ids)
                {
                    foreach (JToken id in ids)
                    {
                        taskIds.Add((string)id!);
                    }
                }

                Listed.Add(new AgentRequestInfo(
                    (string)item["requestId"]!,
                    (string)item["changeSetId"]!,
                    state,
                    (string?)item["intentText"] ?? string.Empty,
                    DateTime.UtcNow.AddSeconds(-30),
                    DateTime.UtcNow,
                    (string?)item["worker"],
                    null,
                    (double?)item["costUsd"],
                    taskIds,
                    null,
                    null,
                    null,
                    null,
                    (long?)item["sequence"] ?? 0,
                    (string?)item["etosStatus"]));
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

        private sealed class Stream : IObservable<AgentEvent>
        {
            private readonly TestAgentGateway _owner;

            public Stream(TestAgentGateway owner)
            {
                _owner = owner;
            }

            public IDisposable Subscribe(IObserver<AgentEvent> observer)
            {
                _owner._observers.Add(observer);
                return new Subscription(_owner, observer);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly TestAgentGateway _owner;
            private readonly IObserver<AgentEvent> _observer;

            public Subscription(TestAgentGateway owner, IObserver<AgentEvent> observer)
            {
                _owner = owner;
                _observer = observer;
            }

            public void Dispose() => _owner._observers.Remove(_observer);
        }
    }

    /// <summary>A voice session the tests drive by hand (no microphone).</summary>
    internal sealed class ScriptedVoiceSession : IVoiceSession
    {
        public bool IsActive { get; private set; }

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public event Action<VoiceTranscript>? Transcript;

        public event Action<float>? Level;

        public event Action<Diagnostic>? Failed;

        public void Start()
        {
            IsActive = true;
            Starts++;
        }

        public void Stop()
        {
            IsActive = false;
            Stops++;
        }

        public void Say(VoiceTranscript transcript) => Transcript?.Invoke(transcript);

        public void Meter(float level) => Level?.Invoke(level);

        public void Fail(Diagnostic diagnostic) => Failed?.Invoke(diagnostic);
    }
}
