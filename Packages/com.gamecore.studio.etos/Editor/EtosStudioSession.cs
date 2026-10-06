// GameCore.Studio.Etos - the project's etos session (a ScriptableSingleton, the only place this package keeps editor
// state). It builds the gateway over the project's StudioRuntime, registers it into StudioServiceRegistry.AgentGateway,
// pumps the main-thread queue from EditorApplication.update, refreshes provider status every 30 s and on focus, and
// rebuilds itself when StudioServices rebuilds the runtime (every domain reload). Sockets are closed before a reload.
// The event cursor persists in Library/GameCoreStudio/etos-events.cursor so a reload resumes the stream without gaps.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Etos
{
    /// <summary>Owner of the project's <see cref="EtosAgentGateway"/>.</summary>
    [FilePath("Library/GameCoreStudio/etos-session.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class EtosStudioSession : ScriptableSingleton<EtosStudioSession>, IMediaGenerationGatewayProvider
    {
        public const string CursorFileName = "etos-events.cursor";

        /// <summary>The most request ids kept as "submitted by this project".</summary>
        public const int OwnRequestLimit = 256;

        [SerializeField]
        private int starts;

        [SerializeField]
        private List<string> ownRequests = new List<string>();

        [NonSerialized]
        private EtosAgentGateway? _gateway;

        [NonSerialized]
        private EtosMediaGenerator? _mediaGateway;

        [NonSerialized]
        private EtosAgentGateway? _mediaGatewaySource;

        [NonSerialized]
        private string? _mediaProjectId;

        [NonSerialized]
        private string? _mediaAppName;

        [NonSerialized]
        private StudioRuntime? _runtime;

        [NonSerialized]
        private Diagnostic? _problem;

        [NonSerialized]
        private bool _hooked;

        [NonSerialized]
        private double _nextPairingCheck;

        [NonSerialized]
        private bool _automatic;

        /// <summary>Observe Studio runtime creation on Open Studio and after domain reload.</summary>
        public static void EnableAutomaticStartup()
        {
            instance._automatic = true;
            instance._nextPairingCheck = 0;
            instance.Hook();
        }

        /// <summary>Idempotent entry seam for OpenStudio and domain reload.</summary>
        public static bool EnsureStarted() => instance._gateway != null || Start();

        /// <summary>The running gateway, or null (not configured, or not started).</summary>
        public static EtosAgentGateway? Gateway => instance._gateway;

        /// <summary>Why the session is not running, or null.</summary>
        public static Diagnostic? Problem => instance._problem;

        /// <summary>Times a gateway was started in this project (survives reloads).</summary>
        public int Starts => starts;

        /// <summary>The cached adapter for this session binding, or null while unpaired/stopped.
        /// Gameplay lookup only inspects existing sessions; it never starts one or reads credentials.</summary>
        public IMediaGenerationGateway? MediaGateway
        {
            get
            {
                EtosAgentGateway? gateway = _gateway;
                if (gateway == null)
                {
                    ClearMediaGateway();
                    return null;
                }

                EtosClientOptions options = gateway.Client.Options;
                if (_mediaGateway == null || !ReferenceEquals(_mediaGatewaySource, gateway)
                    || _mediaProjectId != options.ProjectId || _mediaAppName != options.AppName)
                {
                    _mediaGateway = new EtosMediaGenerator(gateway, gateway.Runtime, gateway.Queue);
                    _mediaGatewaySource = gateway;
                    _mediaProjectId = options.ProjectId;
                    _mediaAppName = options.AppName;
                }

                return _mediaGateway;
            }
        }

        /// <summary>Starts (or restarts) the session over the project's runtime; false with <see cref="Problem"/> set when it cannot.</summary>
        public static bool Start()
        {
            instance._automatic = true;
            return instance.StartCore(StudioServices.Runtime);
        }

        /// <summary>Stops the session and unregisters the gateway.</summary>
        public static void Stop()
        {
            instance._automatic = false;
            instance.StopCore();
        }

        /// <summary>Hello with the current settings (the settings page's "Test connection"); the summary is redacted.</summary>
        public static async Task<string> TestConnectionAsync(EtosSettings settings)
        {
            try
            {
                EtosCredentials credentials = settings.ReadCredentials();
                using (CompanionClient client = new CompanionClient(settings.ToClientOptions(credentials), credentials))
                {
                    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                    HelloInfo hello = await client.HelloAsync().ConfigureAwait(false);
                    string providers = string.Join(", ", new[] { "image", "tts", "voice", "3d", "describe" }.Select(p => p + " " + (hello.Provider(p) ?? ProviderStates.Unknown)));
                    return EtosRedaction.Redact("OK in " + watch.ElapsedMilliseconds + " ms: " + hello.Service + " " + hello.Version + " via app " + (hello.App ?? settings.AppName) + ", agent " + (hello.Connected ? "connected" : "not connected") + "; " + providers + ".");
                }
            }
            catch (EtosException error)
            {
                return EtosRedaction.Redact("Failed: " + error.Error);
            }
        }

        private bool StartCore(StudioRuntime runtime)
        {
            StopCore();
            _runtime = runtime;
            try
            {
                EtosSettings settings = EtosSettings.Load(runtime.Paths.ProjectRoot);
                // A stale project key-file preference must not hide an installed host pairing.
                string? keyFile = EtosCredentials.ResolveAutomaticKeyFile();
                EtosCredentials credentials = EtosCredentials.FromKeyFile(keyFile ?? string.Empty);
                settings.KeyFile = keyFile ?? string.Empty;
                RedactingStudioLog log = new RedactingStudioLog(runtime.Log);
                CompanionClient client = new CompanionClient(settings.ToClientOptions(credentials, line => log.Write(StudioLogLevel.Debug, "etos", line)), credentials);
                MainThreadQueue queue = new MainThreadQueue(log);
                string cursorScope = Json.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(client.Options.AppName + "\n" + client.Options.ProjectId));
                FileCursorStore cursors = new FileCursorStore(Path.Combine(runtime.Paths.LibraryRoot, cursorScope + "-" + CursorFileName));
                HashSet<string> own = new HashSet<string>(ownRequests, StringComparer.Ordinal);
                EtosGatewayOptions options = new EtosGatewayOptions { MaxCostUsd = settings.MaxCostUsd, AutoImport = settings.AutoImport, GeneratedFolder = settings.GeneratedFolder, OwnRequests = own };
                _gateway = new EtosAgentGateway(client, runtime, queue, cursors, options, log);
                _gateway.RequestChanged += view => RememberOwn(view.RequestId);
                runtime.Services.AgentGateway = _gateway;
                EtosProjectContext.Bind(runtime, client);
                _problem = null;
                _ = RefreshVerdicts(runtime);
                _gateway.Start();
                starts++;
                Save(true);
                log.Write(StudioLogLevel.Info, "etos", "etos session started: " + settings);
            }
            catch (EtosException error)
            {
                _problem = EtosAgentGateway.DiagnosticOf(error.Error);
                _gateway = null;
            }

            _nextPairingCheck = EditorApplication.timeSinceStartup + 1;
            Hook();
            return _gateway != null;
        }

        private async Task RefreshVerdicts(StudioRuntime runtime)
        {
            try { await StageAdmission.Of(runtime).RefreshPendingVerdicts(); }
            catch (Exception error) { _problem = new Diagnostic(EtosCodes.StageFailed, EtosRedaction.Redact(error.Message)); }
            return;
        }

        private void RememberOwn(string requestId)
        {
            EtosAgentGateway? gateway = _gateway;
            if (gateway == null || !gateway.IsOwn(requestId) || ownRequests.Contains(requestId))
            {
                return;
            }

            ownRequests.Add(requestId);
            if (ownRequests.Count > OwnRequestLimit)
            {
                ownRequests.RemoveRange(0, ownRequests.Count - OwnRequestLimit);
            }

            Save(true);
        }

        private void StopCore()
        {
            ClearMediaGateway();
            if (_gateway != null)
            {
                if (_runtime != null && ReferenceEquals(_runtime.Services.AgentGateway, _gateway))
                {
                    _runtime.Services.AgentGateway = null;
                }

                if (_runtime != null) StageAdmission.Of(_runtime).Options.StageService = null;
                _gateway.Dispose();
                _gateway.Client.Dispose();
                _gateway = null;
            }
        }

        private void ClearMediaGateway()
        {
            _mediaGateway = null;
            _mediaGatewaySource = null;
            _mediaProjectId = null;
            _mediaAppName = null;
        }

        private void Hook()
        {
            if (_hooked)
            {
                return;
            }

            EditorApplication.update += Pump;
            EditorApplication.focusChanged += OnFocus;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += OnBeforeReload;
            _hooked = true;
        }

        private void Pump()
        {
            EtosAgentGateway? gateway = _gateway;
            if (gateway != null)
            {
                gateway.Queue.Pump();
                gateway.Tick();
            }

            // Opening Studio creates its runtime. Wait for that boundary instead of creating it during
            // package/domain initialization, when optional services and project identity may not be ready.
            if (_automatic && _gateway == null && StudioServices.HasRuntime
                && EditorApplication.timeSinceStartup >= _nextPairingCheck)
                StartCore(StudioServices.Runtime);

            if (_automatic && _runtime != null && StudioServices.HasRuntime && !ReferenceEquals(StudioServices.Runtime, _runtime))
                StartCore(StudioServices.Runtime);
        }

        private void OnFocus(bool focused)
        {
            if (focused && _gateway != null)
            {
                _ = _gateway.RefreshStatusAsync();
            }
        }

        private void OnBeforeReload()
        {
            _automatic = false;
            StopCore();
            if (_hooked)
            {
                EditorApplication.update -= Pump;
                EditorApplication.focusChanged -= OnFocus;
                AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
                EditorApplication.quitting -= OnBeforeReload;
                _hooked = false;
            }
        }
    }

    /// <summary>Starts the session after each domain reload (not in batch mode unless GAMECORE_ETOS_AUTOSTART=1).</summary>
    [InitializeOnLoad]
    internal static class EtosBootstrap
    {
        static EtosBootstrap()
        {
            EditorApplication.delayCall += StartIfWanted;
        }

        private static void StartIfWanted()
        {
            if (Application.isBatchMode && Environment.GetEnvironmentVariable("GAMECORE_ETOS_AUTOSTART") != "1")
            {
                return;
            }

            EtosStudioSession.EnableAutomaticStartup();
        }
    }
}
