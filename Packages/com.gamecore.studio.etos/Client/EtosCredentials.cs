// GameCore.Studio.Etos.Client - the paired app key (04 s1, s8; SADR-018). The key lives in a file outside the
// repository, written by `etos app pair gamecore-unity --approve --out ~/.config/gamecore-studio/app-key.json`
// (JSON {"url": "http://127.0.0.1:7410", "key": "etk_..."}); a plain-text file holding only the key is accepted too.
// The key is held in memory only, never written anywhere by Studio, and every textual form of this object is redacted.
#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>An app key and the node URL it was paired for.</summary>
    public sealed class EtosCredentials
    {
        /// <summary>Environment variable naming the key file (wins over Studio's user settings).</summary>
        public const string KeyFileVariable = "GAMECORE_ETOS_KEY_FILE";

        /// <summary>Where <c>etos app pair ... --out</c> writes the key by default (SADR-018), relative to the home directory.</summary>
        public const string DefaultKeyFileRelative = ".config/gamecore-studio/app-key.json";

        private readonly string _key;

        public EtosCredentials(string key, string? nodeUrl, string? source)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("The app key is empty.", nameof(key));
            }

            _key = key.Trim();
            NodeUrl = string.IsNullOrWhiteSpace(nodeUrl) ? null : nodeUrl!.Trim().TrimEnd('/');
            Source = source;
        }

        /// <summary>The node URL recorded in the key file (null for a plain-text key file).</summary>
        public string? NodeUrl { get; }

        /// <summary>The file the key was read from (a path, never the key).</summary>
        public string? Source { get; }

        /// <summary>The key's length (shown in settings instead of the key).</summary>
        public int KeyLength => _key.Length;

        /// <summary>The display form: prefix, mask and length.</summary>
        public string Display => EtosRedaction.Describe(_key);

        /// <summary>The header value; used only to build the Authorization header.</summary>
        internal string AuthorizationValue => "Bearer " + _key;

        internal string StageKey => _key;

        internal string SignAppCandidate(byte[] payload)
        {
            byte[] domain = Encoding.UTF8.GetBytes("gamecore.stage.app-candidate/1\n");
            byte[] message = new byte[domain.Length + payload.Length];
            Buffer.BlockCopy(domain, 0, message, 0, domain.Length);
            Buffer.BlockCopy(payload, 0, message, domain.Length, payload.Length);
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_key)))
                return BitConverter.ToString(hmac.ComputeHash(message)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Automatic startup uses the explicit host override or documented pairing location, never project settings.</summary>
        public static string? ResolveAutomaticKeyFile()
        {
            string? configured = Environment.GetEnvironmentVariable(KeyFileVariable);
            return string.IsNullOrWhiteSpace(configured) ? DefaultKeyFile() : ExpandHome(configured!.Trim());
        }

        /// <summary>True when <paramref name="text"/> contains this key (used by redaction tests and evidence checks).</summary>
        public bool AppearsIn(string? text) => text != null && text.IndexOf(_key, StringComparison.Ordinal) >= 0;

        /// <summary>The default key file under the user's home directory, or null when HOME is unknown.</summary>
        public static string? DefaultKeyFile()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
            }

            return home.Length == 0 ? null : Path.Combine(home, DefaultKeyFileRelative);
        }

        /// <summary>Expands a leading <c>~/</c> to the home directory.</summary>
        public static string ExpandHome(string path)
        {
            if (path.StartsWith("~/", StringComparison.Ordinal) || path == "~")
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(home))
                {
                    home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
                }

                return path.Length <= 2 ? home : Path.Combine(home, path.Substring(2));
            }

            return path;
        }

        /// <summary>Reads a key file; a missing or unreadable file is <c>not_configured</c> (the message names the path only).</summary>
        public static EtosCredentials FromKeyFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw NotConfigured("No app key file is set.");
            }

            string full = ExpandHome(path.Trim());
            string text;
            try
            {
                text = File.ReadAllText(full);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is NotSupportedException || error is ArgumentException)
            {
                throw NotConfigured("The app key file " + full + " cannot be read (" + error.GetType().Name + ").");
            }

            string trimmed = text.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                JObject obj;
                try
                {
                    obj = JObject.Parse(trimmed);
                }
                catch (JsonException)
                {
                    throw NotConfigured("The app key file " + full + " is not valid JSON.");
                }

                string? key = Json.Str(obj, "key");
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw NotConfigured("The app key file " + full + " has no \"key\" member.");
                }

                return new EtosCredentials(key!, Json.Str(obj, "url"), full);
            }

            if (trimmed.Length == 0 || trimmed.IndexOfAny(new[] { '\n', '\r', ' ', '\t' }) >= 0)
            {
                throw NotConfigured("The app key file " + full + " holds neither a JSON key document nor a single key.");
            }

            return new EtosCredentials(trimmed, null, full);
        }

        public override string ToString() => "EtosCredentials(" + Display + (Source == null ? string.Empty : ", from " + Source) + ")";

        private static EtosException NotConfigured(string message)
        {
            return new EtosException(new EtosError(0, EtosCodes.NotConfigured, message, "Pair the Unity app: etos app pair gamecore-unity --approve --out ~/" + DefaultKeyFileRelative + ", then set the key file in Project Settings > GameCore Studio > ETOS or " + KeyFileVariable + "."));
        }
    }
}
