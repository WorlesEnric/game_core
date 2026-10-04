// GameCore.Studio.Etos - the project's etos session (a ScriptableSingleton, the only place this package keeps editor
// state). It builds the gateway over the project's StudioRuntime, registers it into StudioServiceRegistry.AgentGateway,
// pumps the main-thread queue from EditorApplication.update, refreshes provider status every 30 s and on focus, and
// rebuilds itself when StudioServices rebuilds the runtime (every domain reload). Sockets are closed before a reload.
// The event cursor persists in Library/GameCoreStudio/etos-events.cursor so a reload resumes the stream without gaps.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    public sealed class EtosStudioSession : ScriptableSingleton<EtosStudioSession>
    {
        public const string CursorFileName = "etos-events.cursor";

        [SerializeField]
        private int starts;

        [NonSerialized]
        private EtosAgentGateway? _gateway;

        [NonSerialized]
        private StudioRuntime? _runtime;

        [NonSerialized]
        private Diagnostic? _problem;

        [NonSerialized]
        private bool _hooked;

        /// <summary>The running gateway, or null (not configured, or not started).</summary>
        public static EtosAgentGateway? Gateway => instance._gateway;

        /// <summary>Why the session is not running, or null.</summary>
        public static Diagnostic? Problem => instance._problem;

        /// <summary>Times a gateway was started in this project (survives reloads).</summary>
        public int Starts => starts;

        /// <summary>Starts (or restarts) the session over the project's runtime; false with <see cref="Problem"/> set when it cannot.</summary>
        public static bool Start()
        {
            return instance.StartCore(StudioServices.Runtime);
        }

        /// <summary>Stops the session and unregisters the gateway.</summary>
        public static void Stop()
        {
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
            EtosSettings settings = EtosSettings.Load(runtime.Paths.ProjectRoot);
            if (!settings.IsConfigured)
            {
                _problem = new Diagnostic(EtosCodes.NotConfigured, "No app key file: Project Settings > GameCore Studio > ETOS, or " + EtosCredentials.KeyFileVariable + ".");
                Hook();
                return false;
            }

            try
            {
                EtosCredentials credentials = settings.ReadCredentials();
                RedactingStudioLog log = new RedactingStudioLog(runtime.Log);
                CompanionClient client = new CompanionClient(settings.ToClientOptions(credentials, line => log.Write(StudioLogLevel.Debug, "etos", line)), credentials);
                MainThreadQueue queue = new MainThreadQueue(log);
                FileCursorStore cursors = new FileCursorStore(Path.Combine(runtime.Paths.LibraryRoot, CursorFileName));
                EtosGatewayOptions options = new EtosGatewayOptions { MaxCostUsd = settings.MaxCostUsd, AutoImport = settings.AutoImport, GeneratedFolder = settings.GeneratedFolder };
                _gateway = new EtosAgentGateway(client, runtime, queue, cursors, options, log);
                runtime.Services.AgentGateway = _gateway;
                _gateway.Start();
                _problem = null;
                starts++;
                Save(true);
                log.Write(StudioLogLevel.Info, "etos", "etos session started: " + settings);
            }
            catch (EtosException error)
            {
                _problem = EtosAgentGateway.DiagnosticOf(error.Error);
                _gateway = null;
            }

            Hook();
            return _gateway != null;
        }

        private void StopCore()
        {
            if (_gateway != null)
            {
                if (_runtime != null && ReferenceEquals(_runtime.Services.AgentGateway, _gateway))
                {
                    _runtime.Services.AgentGateway = null;
                }

                _gateway.Dispose();
                _gateway.Client.Dispose();
                _gateway = null;
            }
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

            if (_runtime != null && StudioServices.HasRuntime && !ReferenceEquals(StudioServices.Runtime, _runtime))
            {
                StartCore(StudioServices.Runtime);
            }
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

            EtosStudioSession.Start();
        }
    }
}
