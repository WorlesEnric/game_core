// GameCore.Studio.Etos - "Project Settings > GameCore Studio > ETOS": node URL, key file path (never the key: only its
// prefix, a mask and its length are shown), cost ceiling, provider status, "Test connection" and "Open node UI".
// Every line shown goes through EtosRedaction.
#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Etos
{
    /// <summary>The ETOS settings page.</summary>
    public sealed class EtosSettingsProvider : SettingsProvider
    {
        public const string PagePath = "Project/GameCore Studio/ETOS";

        private EtosSettings? _settings;
        private string _testResult = string.Empty;
        private Task<string>? _test;

        public EtosSettingsProvider()
            : base(PagePath, SettingsScope.Project, new[] { "etos", "agent", "companion", "app key", "voice", "studio" })
        {
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new EtosSettingsProvider();

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            _settings = EtosSettings.Load(StudioPaths.ForCurrentProject().ProjectRoot);
        }

        public override void OnGUI(string searchContext)
        {
            EtosSettings settings = _settings ??= EtosSettings.Load(StudioPaths.ForCurrentProject().ProjectRoot);
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            settings.NodeUrl = EditorGUILayout.TextField(new GUIContent("Node URL", "Empty: the URL in the key file, else http://127.0.0.1:7410."), settings.NodeUrl);
            using (new EditorGUILayout.HorizontalScope())
            {
                settings.KeyFile = EditorGUILayout.TextField(new GUIContent("App key file", "The file written by `etos app pair gamecore-unity`. " + EtosCredentials.KeyFileVariable + " overrides it. The key itself is never stored in the project."), settings.KeyFile);
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    string picked = EditorUtility.OpenFilePanel("App key file", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), string.Empty);
                    if (!string.IsNullOrEmpty(picked))
                    {
                        settings.KeyFile = picked;
                    }
                }
            }

            EditorGUILayout.LabelField("Key", EtosRedaction.Redact(settings.KeyStatus()));
            settings.MaxCostUsd = EditorGUILayout.DoubleField(new GUIContent("Max cost per op (USD)", "Sent as max_cost_usd with every media op."), settings.MaxCostUsd);
            settings.AutoImport = EditorGUILayout.Toggle(new GUIContent("Stage candidates on arrival"), settings.AutoImport);
            settings.GeneratedFolder = EditorGUILayout.TextField("Generated assets folder", settings.GeneratedFolder);
            settings.NodeUiUrl = EditorGUILayout.TextField("Node UI", settings.NodeUiUrl);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save and restart session"))
                {
                    settings.Save(StudioPaths.ForCurrentProject().ProjectRoot);
                    EtosStudioSession.Start();
                }

                using (new EditorGUI.DisabledScope(_test != null && !_test.IsCompleted))
                {
                    if (GUILayout.Button("Test connection"))
                    {
                        _testResult = "Testing...";
                        _test = EtosStudioSession.TestConnectionAsync(settings);
                    }
                }

                if (GUILayout.Button("Open node UI"))
                {
                    Application.OpenURL(settings.NodeUiUrl);
                }
            }

            if (_test != null && _test.IsCompleted)
            {
                _testResult = _test.Status == TaskStatus.RanToCompletion ? _test.Result : "Failed: " + EtosRedaction.Redact(_test.Exception?.GetBaseException().Message ?? "unknown error");
                _test = null;
            }

            if (_testResult.Length > 0)
            {
                EditorGUILayout.HelpBox(EtosRedaction.Redact(_testResult), _testResult.StartsWith("OK") ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Session", EditorStyles.boldLabel);
            EtosAgentGateway? gateway = EtosStudioSession.Gateway;
            if (gateway == null)
            {
                EditorGUILayout.HelpBox(EtosRedaction.Redact(EtosStudioSession.Problem?.Message ?? "Not started."), MessageType.None);
                return;
            }

            ProviderStatus status = gateway.Status;
            EditorGUILayout.LabelField("Node", status.NodeReachable ? "reachable" : "unreachable");
            EditorGUILayout.LabelField("Agent", status.AgentReady ? "ready (" + status.CompanionVersion + ")" : "not ready");
            foreach (KeyValuePair<string, ProviderState> row in new[]
            {
                new KeyValuePair<string, ProviderState>("Image", status.Image),
                new KeyValuePair<string, ProviderState>("Speech (tts)", status.Tts),
                new KeyValuePair<string, ProviderState>("Voice", status.Voice),
                new KeyValuePair<string, ProviderState>("3D", status.ThreeD),
                new KeyValuePair<string, ProviderState>("Describe", status.Describe),
            })
            {
                EditorGUILayout.LabelField(row.Key, row.Value.ToString());
            }

            EditorGUILayout.LabelField("Events", gateway.Events.State + " at cursor " + gateway.Events.Cursor);
            if (status.Problem != null)
            {
                EditorGUILayout.HelpBox(EtosRedaction.Redact(status.Problem.Code + ": " + status.Problem.Message), MessageType.Warning);
            }
        }
    }
}
