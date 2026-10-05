// GameCore.Studio.Etos - user-level ETOS settings (04 s8: "UserSettings/GameCoreStudio.json holds only the file path
// and the base URL"; SADR-018). The file is per user and git-ignored (games/.gitignore ignores UserSettings/), lives
// under the "etos" member so other Studio sections can share the file, and never holds the key itself: only the path
// of the key file written by `etos app pair`. GAMECORE_ETOS_KEY_FILE wins over the stored path; without either the
// pairing default ~/.config/gamecore-studio/app-key.json is used when it exists.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos
{
    /// <summary>The ETOS section of the user's Studio settings.</summary>
    public sealed class EtosSettings
    {
        public const string RelativePath = "UserSettings/GameCoreStudio.json";

        public const string SectionName = "etos";

        public const double DefaultMaxCostUsd = 0.50;

        /// <summary>The node API; empty means "the URL in the key file, else http://127.0.0.1:7410".</summary>
        public string NodeUrl { get; set; } = string.Empty;

        /// <summary>The key file path; empty means "GAMECORE_ETOS_KEY_FILE, else the pairing default".</summary>
        public string KeyFile { get; set; } = string.Empty;

        public string AppName { get; set; } = "gamecore-unity";

        public string AgentName { get; set; } = "gamecore-studio";

        /// <summary>Cost ceiling sent with every media op (USD).</summary>
        public double MaxCostUsd { get; set; } = DefaultMaxCostUsd;

        /// <summary>The node's web UI ("Open node UI").</summary>
        public string NodeUiUrl { get; set; } = "http://127.0.0.1:7400";

        /// <summary>Where generated media is imported when no path is given.</summary>
        public string GeneratedFolder { get; set; } = "Assets/Generated/Studio";

        /// <summary>Import and stage candidates as soon as they arrive.</summary>
        public bool AutoImport { get; set; } = true;

        /// <summary>The settings file of a project.</summary>
        public static string PathFor(string projectRoot) => Path.Combine(projectRoot, "UserSettings", "GameCoreStudio.json");

        public static EtosSettings Load(string projectRoot)
        {
            EtosSettings settings = new EtosSettings();
            string path = PathFor(projectRoot);
            if (!File.Exists(path))
            {
                return settings;
            }

            try
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                JObject? section = root[SectionName] as JObject;
                if (section == null)
                {
                    return settings;
                }

                settings.NodeUrl = Json.Str(section, "nodeUrl") ?? settings.NodeUrl;
                settings.KeyFile = Json.Str(section, "keyFile") ?? settings.KeyFile;
                settings.AppName = Json.Str(section, "appName") ?? settings.AppName;
                settings.AgentName = Json.Str(section, "agentName") ?? settings.AgentName;
                settings.MaxCostUsd = Json.Double(section, "maxCostUsd") ?? settings.MaxCostUsd;
                settings.NodeUiUrl = Json.Str(section, "nodeUiUrl") ?? settings.NodeUiUrl;
                settings.GeneratedFolder = Json.Str(section, "generatedFolder") ?? settings.GeneratedFolder;
                settings.AutoImport = Json.Bool(section, "autoImport") ?? settings.AutoImport;
            }
            catch (Exception error) when (error is IOException || error is JsonException || error is UnauthorizedAccessException)
            {
                return settings;
            }

            return settings;
        }

        /// <summary>Writes the section (other sections of the file are kept). The key is never written.</summary>
        public void Save(string projectRoot)
        {
            string path = PathFor(projectRoot);
            JObject root = new JObject();
            if (File.Exists(path))
            {
                try
                {
                    root = JObject.Parse(File.ReadAllText(path));
                }
                catch (JsonException)
                {
                    root = new JObject();
                }
            }

            root[SectionName] = new JObject
            {
                ["nodeUrl"] = NodeUrl,
                ["keyFile"] = KeyFile,
                ["appName"] = AppName,
                ["agentName"] = AgentName,
                ["maxCostUsd"] = MaxCostUsd,
                ["nodeUiUrl"] = NodeUiUrl,
                ["generatedFolder"] = GeneratedFolder,
                ["autoImport"] = AutoImport,
            };
            string text = root.ToString(Formatting.Indented);
            if (EtosRedaction.ContainsSecret(text))
            {
                throw new InvalidOperationException("Refusing to write a credential into " + RelativePath + "; store only the key file path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text + "\n");
        }

        /// <summary>The key file in effect (environment, then settings, then the pairing default when it exists), or null.</summary>
        public string? EffectiveKeyFile()
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return EtosCredentials.ExpandHome(fromEnvironment!.Trim());
            }

            if (!string.IsNullOrWhiteSpace(KeyFile))
            {
                return EtosCredentials.ExpandHome(KeyFile.Trim());
            }

            string? fallback = EtosCredentials.DefaultKeyFile();
            return fallback != null && File.Exists(fallback) ? fallback : null;
        }

        /// <summary>Where the key file setting comes from (for the settings page).</summary>
        public string KeyFileSource()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable)))
            {
                return EtosCredentials.KeyFileVariable;
            }

            return string.IsNullOrWhiteSpace(KeyFile) ? "pairing default" : "settings";
        }

        /// <summary>True when a key file is in effect and exists (the gateway is registered only then).</summary>
        public bool IsConfigured
        {
            get
            {
                string? file = EffectiveKeyFile();
                return file != null && File.Exists(file);
            }
        }

        /// <summary>Reads the credentials; throws <see cref="EtosException"/> (<c>not_configured</c>) without them.</summary>
        public EtosCredentials ReadCredentials()
        {
            string? file = EffectiveKeyFile();
            if (file == null)
            {
                throw new EtosException(new EtosError(0, EtosCodes.NotConfigured, "No app key file: set one in Project Settings > GameCore Studio > ETOS, or " + EtosCredentials.KeyFileVariable + ".", "etos app pair gamecore-unity --approve --out ~/" + EtosCredentials.DefaultKeyFileRelative));
            }

            return EtosCredentials.FromKeyFile(file);
        }

        /// <summary>Client options for these settings and credentials.</summary>
        public EtosClientOptions ToClientOptions(EtosCredentials credentials, Action<string>? log = null)
        {
            string node = !string.IsNullOrWhiteSpace(NodeUrl) ? NodeUrl.Trim() : credentials.NodeUrl ?? "http://127.0.0.1:7410";
            return new EtosClientOptions
            {
                NodeUrl = node,
                AgentName = string.IsNullOrWhiteSpace(AgentName) ? "gamecore-studio" : AgentName,
                AppName = string.IsNullOrWhiteSpace(AppName) ? "gamecore-unity" : AppName,
                DefaultMaxCostUsd = MaxCostUsd > 0 ? MaxCostUsd : DefaultMaxCostUsd,
                Log = log,
            };
        }

        /// <summary>The settings page's key line: never the key, only its prefix, the mask and its length.</summary>
        public string KeyStatus()
        {
            try
            {
                EtosCredentials credentials = ReadCredentials();
                return credentials.Display + " from " + KeyFileSource();
            }
            catch (EtosException error)
            {
                return error.Error.Message;
            }
        }

        public override string ToString()
        {
            return "node " + (NodeUrl.Length == 0 ? "(from key file)" : NodeUrl) + ", key file " + (EffectiveKeyFile() ?? "(none)") + ", app " + AppName + ", max_cost_usd " + MaxCostUsd.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
