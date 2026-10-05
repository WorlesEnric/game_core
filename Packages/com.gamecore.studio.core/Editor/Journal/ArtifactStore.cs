// GameCore.Studio.Edit - content-addressed artifact retention (docs/studio/03-authoring-contracts.md s6, 02 s5).
//   Studio/Artifacts/sha256/<aa>/<hash>   the bytes (aa = first two hex digits)
//   Studio/Artifacts/manifest.json         one entry per artifact: sha256, bytes, mediaType, name, role, producer,
//                                          large (true when > 8 MiB and therefore listed in Studio/Artifacts/.gitignore)
// Every write is verified: the stored file is re-hashed and must equal its name. Reads verify too, so a corrupted or
// truncated artifact is refused instead of silently re-imported. Undo and redo read artifacts, never regenerate them.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    /// <summary>An artifact is missing, corrupted or does not match its declared digest.</summary>
    public sealed class ArtifactStoreException : Exception
    {
        public ArtifactStoreException(string message)
            : base(message)
        {
        }
    }

    /// <summary>One manifest row.</summary>
    public sealed class ArtifactManifestEntry
    {
        [JsonProperty("sha256", Required = Required.Always)]
        public string Sha256 { get; set; } = string.Empty;

        [JsonProperty("bytes", Required = Required.Always)]
        public long Bytes { get; set; }

        [JsonProperty("mediaType", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? MediaType { get; set; }

        [JsonProperty("name", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Name { get; set; }

        [JsonProperty("role", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Role { get; set; }

        [JsonProperty("producer", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public JObject? Producer { get; set; }

        /// <summary>True when the bytes are larger than <see cref="ArtifactStore.LargeThreshold"/> and git-ignored.</summary>
        [JsonProperty("large", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool? Large { get; set; }
    }

    /// <summary>The retained artifacts of a project.</summary>
    public sealed class ArtifactStore
    {
        /// <summary>Artifacts above this size are kept out of git (listed in .gitignore); the manifest stays tracked.</summary>
        public const long LargeThreshold = 8L * 1024 * 1024;

        private const string ManifestSchema = "gamecore.studio.artifacts/1";

        private readonly string _root;
        private SortedDictionary<string, ArtifactManifestEntry>? _manifest;

        public ArtifactStore(StudioPaths paths)
        {
            _root = (paths ?? throw new ArgumentNullException(nameof(paths))).ArtifactsRoot;
        }

        public string Root => _root;

        public string ManifestPath => Path.Combine(_root, "manifest.json");

        /// <summary>Number of <see cref="Read"/> calls (tests assert redo reads instead of regenerating).</summary>
        public int ReadCount { get; private set; }

        /// <summary>Number of <see cref="Put"/> calls that stored new bytes.</summary>
        public int WriteCount { get; private set; }

        /// <summary>The file holding <paramref name="digest"/> (lowercase hex).</summary>
        public string PathOf(string digest)
        {
            string hex = Normalize(digest);
            return Path.Combine(_root, "sha256", hex.Substring(0, 2), hex);
        }

        /// <summary>True when the artifact is retained (its file exists).</summary>
        public bool Has(string digest)
        {
            string? hex = TryNormalize(digest);
            return hex != null && File.Exists(PathOf(hex));
        }

        /// <summary>The manifest row of <paramref name="digest"/>, or null.</summary>
        public ArtifactManifestEntry? Describe(string digest)
        {
            string? hex = TryNormalize(digest);
            return hex != null && Manifest.TryGetValue(hex, out ArtifactManifestEntry? entry) ? entry : null;
        }

        /// <summary>Every manifest row, ordered by digest.</summary>
        public IReadOnlyList<ArtifactManifestEntry> Entries => new List<ArtifactManifestEntry>(Manifest.Values);

        /// <summary>
        /// Retains <paramref name="bytes"/> and returns their digest (lowercase hex). When <paramref name="declared"/> is
        /// given its sha256 and byte count must match the bytes, and its metadata fills the manifest row.
        /// </summary>
        public string Put(byte[] bytes, ArtifactRef? declared)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            string digest = ContentStamp.Sha256Hex(bytes);
            if (declared != null)
            {
                if (!string.Equals(Normalize(declared.Sha256), digest, StringComparison.Ordinal))
                {
                    throw new ArtifactStoreException("Artifact bytes hash to sha256:" + digest + ", but the change set declares sha256:" + declared.Sha256 + ".");
                }

                if (declared.Bytes != bytes.LongLength)
                {
                    throw new ArtifactStoreException("Artifact sha256:" + digest + " has " + bytes.LongLength + " bytes, but the change set declares " + declared.Bytes + ".");
                }
            }

            string path = PathOf(digest);
            if (!File.Exists(path) || !Verify(path, digest))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temp, path);
                if (!Verify(path, digest))
                {
                    throw new ArtifactStoreException("Artifact sha256:" + digest + " did not verify after writing.");
                }

                WriteCount++;
            }

            bool changed = false;
            if (!Manifest.TryGetValue(digest, out ArtifactManifestEntry? entry))
            {
                entry = new ArtifactManifestEntry { Sha256 = digest, Bytes = bytes.LongLength };
                Manifest[digest] = entry;
                changed = true;
            }

            if (declared != null)
            {
                changed |= Fill(entry, declared);
            }

            if (bytes.LongLength > LargeThreshold && entry.Large != true)
            {
                entry.Large = true;
                changed = true;
                AddToGitIgnore(digest);
            }

            if (changed)
            {
                SaveManifest();
            }

            return digest;
        }

        /// <summary>Retains the artifact a change set declares, reading the bytes from <paramref name="sourceFile"/>.</summary>
        public string PutFile(string sourceFile, ArtifactRef? declared)
        {
            return Put(File.ReadAllBytes(sourceFile), declared);
        }

        /// <summary>The verified bytes of <paramref name="digest"/>; throws <see cref="ArtifactStoreException"/> when missing or corrupt.</summary>
        public byte[] Read(string digest)
        {
            string? hex = TryNormalize(digest);
            if (hex == null)
            {
                throw new ArtifactStoreException("'" + digest + "' is not a sha256 digest.");
            }

            string path = PathOf(hex);
            if (!File.Exists(path))
            {
                throw new ArtifactStoreException("Artifact sha256:" + hex + " is not retained in Studio/Artifacts.");
            }

            byte[] bytes = File.ReadAllBytes(path);
            if (!string.Equals(ContentStamp.Sha256Hex(bytes), hex, StringComparison.Ordinal))
            {
                throw new ArtifactStoreException("Artifact sha256:" + hex + " is corrupted (its bytes no longer match its digest).");
            }

            ReadCount++;
            return bytes;
        }

        /// <summary>Digests of the change set's declared artifacts that are not retained.</summary>
        public IReadOnlyList<string> Missing(ChangeSet changeSet)
        {
            List<string> missing = new List<string>();
            if (changeSet.Artifacts == null)
            {
                return missing;
            }

            foreach (ArtifactRef artifact in changeSet.Artifacts)
            {
                if (!Has(artifact.Sha256))
                {
                    missing.Add(artifact.Sha256);
                }
            }

            return missing;
        }

        /// <summary>Re-reads the manifest from disk (another process or a test changed it).</summary>
        public void Reload()
        {
            _manifest = null;
        }

        private SortedDictionary<string, ArtifactManifestEntry> Manifest => _manifest ??= LoadManifest();

        private SortedDictionary<string, ArtifactManifestEntry> LoadManifest()
        {
            SortedDictionary<string, ArtifactManifestEntry> entries = new SortedDictionary<string, ArtifactManifestEntry>(StringComparer.Ordinal);
            if (!File.Exists(ManifestPath))
            {
                return entries;
            }

            try
            {
                JObject document = JObject.Parse(File.ReadAllText(ManifestPath, Encoding.UTF8));
                if (document["artifacts"] is JArray rows)
                {
                    foreach (JToken row in rows)
                    {
                        ArtifactManifestEntry? entry = row.ToObject<ArtifactManifestEntry>();
                        if (entry != null && ContentStamp.IsValidHex(entry.Sha256))
                        {
                            entries[entry.Sha256] = entry;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // A damaged manifest is rebuilt from the files on the next write; the bytes are the source of truth.
            }

            return entries;
        }

        private void SaveManifest()
        {
            JArray rows = new JArray();
            foreach (ArtifactManifestEntry entry in Manifest.Values)
            {
                rows.Add(JObject.FromObject(entry, JsonSerializer.Create(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })));
            }

            JObject document = new JObject { ["schema"] = ManifestSchema, ["artifacts"] = rows };
            StudioPaths.WriteAllTextAtomic(ManifestPath, document.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
        }

        private void AddToGitIgnore(string digest)
        {
            string ignore = Path.Combine(_root, ".gitignore");
            string line = "sha256/" + digest.Substring(0, 2) + "/" + digest;
            string existing = File.Exists(ignore) ? File.ReadAllText(ignore, Encoding.UTF8) : string.Empty;
            if (existing.Contains(line))
            {
                return;
            }

            StudioPaths.WriteAllTextAtomic(ignore, existing + (existing.Length == 0 || existing.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n") + line + "\n");
        }

        private static bool Fill(ArtifactManifestEntry entry, ArtifactRef declared)
        {
            bool changed = false;
            if (entry.MediaType == null)
            {
                entry.MediaType = declared.MediaType;
                changed = true;
            }

            if (entry.Name == null && declared.Name != null)
            {
                entry.Name = declared.Name;
                changed = true;
            }

            if (entry.Role == null && declared.Role != null)
            {
                entry.Role = declared.Role;
                changed = true;
            }

            if (entry.Producer == null && declared.Producer != null)
            {
                entry.Producer = (JObject)StudioJson.ToToken(declared.Producer);
                changed = true;
            }

            return changed;
        }

        private static bool Verify(string path, string digest)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder hex = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    hex.Append(value.ToString("x2"));
                }

                return string.Equals(hex.ToString(), digest, StringComparison.Ordinal);
            }
        }

        private static string Normalize(string digest)
        {
            return TryNormalize(digest) ?? throw new ArtifactStoreException("'" + digest + "' is not a sha256 digest.");
        }

        private static string? TryNormalize(string? digest)
        {
            if (digest == null)
            {
                return null;
            }

            if (ContentStamp.IsValid(digest))
            {
                return ContentStamp.DigestOf(digest);
            }

            string lower = digest.ToLowerInvariant();
            return ContentStamp.IsValidHex(lower) ? lower : null;
        }
    }
}
