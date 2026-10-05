// GameCore.Studio.Etos.Client - how a client reaches the companion (04 s1, s2).
#nullable enable
using System;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Client configuration. Every member has the contract's default.</summary>
    public sealed class EtosClientOptions
    {
        /// <summary>The node API, <c>http://127.0.0.1:7410</c> on the Studio host (04 s8).</summary>
        public string NodeUrl { get; set; } = "http://127.0.0.1:7410";

        /// <summary>The installed companion agent.</summary>
        public string AgentName { get; set; } = "gamecore-studio";

        /// <summary>The paired Unity app. etos sets <c>X-Etos-App</c> itself from the key; the client sends it too.</summary>
        public string AppName { get; set; } = "gamecore-unity";

        /// <summary>The cost ceiling sent with every media op that does not name one (USD).</summary>
        public double DefaultMaxCostUsd { get; set; } = 0.50;

        /// <summary>Deadline of ordinary calls.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Deadline of <c>/v1/ops/generate</c> (the node blocks until the provider answers).</summary>
        public TimeSpan GenerateTimeout { get; set; } = TimeSpan.FromSeconds(240);

        /// <summary>Deadline of an artifact download.</summary>
        public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromSeconds(180);

        /// <summary>Use the system HTTP proxy. Off by default: the node listens on loopback and desktop proxies break it.</summary>
        public bool UseSystemProxy { get; set; }

        /// <summary>Where downloads are written before verification (default: the system temp directory).</summary>
        public string? TempDirectory { get; set; }

        /// <summary>
        /// Test hook: called with the temp file of every artifact download after the bytes are written and before they are
        /// verified, so a test can tamper with them and prove the verification refuses them. Null in production.
        /// </summary>
        public Action<string>? DownloadTamperHook { get; set; }

        /// <summary>Optional log sink; every line is redacted before it is written.</summary>
        public Action<string>? Log { get; set; }
    }
}
