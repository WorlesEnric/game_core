#nullable enable
// Parsed bytes are untrusted; only StageAdmission can attach companion verification.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    /// <summary>One step of a verdict.</summary>
    public sealed class StageVerdictStep
    {
        public StageVerdictStep(string id, string status, long durationMs, string? logRef, string? detail)
        {
            Id = id;
            Status = status;
            DurationMs = durationMs;
            LogRef = logRef;
            Detail = detail;
        }

        public string Id { get; }

        /// <summary><c>pass</c>, <c>fail</c> or <c>skipped</c>.</summary>
        public string Status { get; }

        public long DurationMs { get; }

        public string? LogRef { get; }

        public string? Detail { get; }
    }

    /// <summary>A parsed stage verdict.</summary>
    public sealed class StageVerdict
    {
        public const string SchemaId = "gamecore.studio.stage-verdict/1";

        private StageVerdict(JObject document, string digest)
        {
            _document = (JObject)document.DeepClone();
            Digest = digest;
            ChangeSetId = (string?)document["changeSetId"] ?? string.Empty;
            Slot = (string?)document["slot"] ?? string.Empty;
            Package = (string?)document["package"] ?? string.Empty;
            Pass = document["pass"]?.Type == JTokenType.Boolean && document["pass"]!.Value<bool>();
            Partial = document["partial"]?.Type == JTokenType.Boolean && document["partial"]!.Value<bool>();
            Failure = (string?)document["failure"];
            DurationMs = document["durationMs"]?.Type == JTokenType.Integer ? document["durationMs"]!.Value<long>() : 0;
            BudgetMs = document["budgetMs"]?.Type == JTokenType.Integer ? document["budgetMs"]!.Value<long>() : 0;

            List<StageVerdictStep> steps = new List<StageVerdictStep>();
            foreach (JToken step in document["steps"] as JArray ?? new JArray())
            {
                steps.Add(new StageVerdictStep(
                    (string?)step["id"] ?? string.Empty,
                    (string?)step["status"] ?? string.Empty,
                    step["durationMs"]?.Type == JTokenType.Integer ? step["durationMs"]!.Value<long>() : 0,
                    (string?)step["logRef"],
                    (string?)step["detail"]));
            }

            Steps = steps;
            Dictionary<string, string> artifacts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JToken artifact in document["artifacts"] as JArray ?? new JArray())
            {
                string? role = (string?)artifact["role"];
                string? sha = (string?)artifact["sha256"];
                if (role != null && sha != null)
                {
                    artifacts[role] = sha;
                }
            }

            Artifacts = artifacts;
            SortedDictionary<string, string> files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (JToken file in document["files"] as JArray ?? new JArray())
            {
                string? path = (string?)file["path"];
                string? sha = (string?)file["sha256"];
                if (path != null && sha != null)
                {
                    files[path] = sha;
                }
            }

            Files = files;
            JObject delta = document["catalogDelta"] as JObject ?? new JObject();
            World = (string?)delta["world"];
            Predicted = (string?)delta["predicted"];
            List<KeyValuePair<string, string>> mechanisms = new List<KeyValuePair<string, string>>();
            foreach (JToken mechanism in delta["mechanisms"] as JArray ?? new JArray())
            {
                string? type = (string?)mechanism["catalogType"];
                string? fingerprint = (string?)mechanism["fingerprint"];
                if (type != null && fingerprint != null)
                {
                    mechanisms.Add(new KeyValuePair<string, string>(type, fingerprint));
                }
            }

            Mechanisms = mechanisms;
            ForbiddenHits = (document["forbiddenHits"] as JArray)?.Count ?? 0;
        }

        /// <summary>The verdict document.</summary>
        private readonly JObject _document;
        public JObject Document => (JObject)_document.DeepClone();
        internal bool CompanionVerified { get; set; }
        internal bool HostAllowed { get; set; }
        public string Confinement => (string?)_document["confinement"] ?? string.Empty;
        public bool ColdCache => _document["coldCache"]?.Type == JTokenType.Boolean && _document["coldCache"]!.Value<bool>();

        /// <summary>The verdict id: SHA-256 (lowercase hex) of its bytes.</summary>
        public string Digest { get; }

        public string Reference => ContentStamp.Prefix + Digest;

        public string ChangeSetId { get; }

        public string Slot { get; }

        public string Package { get; }

        public bool Pass { get; }

        public bool Partial { get; }

        /// <summary>The stage_failed reason (<c>timeout</c>, ...) when the stage failed as a whole.</summary>
        public string? Failure { get; }

        public long DurationMs { get; }

        public long BudgetMs { get; }

        public IReadOnlyList<StageVerdictStep> Steps { get; }

        /// <summary>Code artifacts by role (<c>package</c>, <c>proposal</c>) to their sha256.</summary>
        public IReadOnlyDictionary<string, string> Artifacts { get; }

        /// <summary>The staged package's files (path relative to the package root) to their sha256.</summary>
        public IReadOnlyDictionary<string, string> Files { get; }

        /// <summary>The world catalog fingerprint the stage inputs baked to.</summary>
        public string? World { get; }

        /// <summary>The catalog-set hash the live world must report after admission.</summary>
        public string? Predicted { get; }

        /// <summary>The mechanism catalogs (catalog type full name, fingerprint).</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Mechanisms { get; }

        public int ForbiddenHits { get; }

        /// <summary>One line for journal details and logs.</summary>
        public string Summary
        {
            get
            {
                StringBuilder text = new StringBuilder();
                text.Append(Pass ? "pass" : "fail").Append(" verdict ").Append(Digest.Substring(0, Math.Min(12, Digest.Length)));
                text.Append(" slot ").Append(Slot).Append(" (").Append(DurationMs.ToString(CultureInfo.InvariantCulture)).Append(" ms");
                foreach (StageVerdictStep step in Steps)
                {
                    if (step.Status == "fail")
                    {
                        text.Append("; ").Append(step.Id).Append(" failed");
                        if (!string.IsNullOrEmpty(step.Detail))
                        {
                            text.Append(": ").Append(step.Detail);
                        }

                        break;
                    }
                }

                text.Append(')');
                return text.ToString();
            }
        }

        /// <summary>Parses verdict bytes; null with a problem when they are not a verdict.</summary>
        public static StageVerdict? Parse(byte[] bytes, out string? problem)
        {
            problem = null;
            if (bytes == null || bytes.Length == 0)
            {
                problem = "the verdict is empty";
                return null;
            }

            JObject document;
            try
            {
                using (JsonTextReader reader = new JsonTextReader(new System.IO.StringReader(Encoding.UTF8.GetString(bytes))))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Decimal;
                    document = JObject.Load(reader);
                }
            }
            catch (JsonException error)
            {
                problem = "the verdict is not JSON: " + error.Message;
                return null;
            }

            if (!string.Equals((string?)document["schema"], SchemaId, StringComparison.Ordinal))
            {
                problem = "the verdict schema is '" + (string?)document["schema"] + "', expected " + SchemaId;
                return null;
            }

            return new StageVerdict(document, ContentStamp.Sha256Hex(bytes));
        }
    }

    /// <summary>Why a verdict does not admit a change set (journal <c>data.reason</c>).</summary>
    public static class VerdictReasons
    {
        public const string Untrusted = "verdict_untrusted";
        public const string Missing = "verdict_missing";
        public const string Failed = "verdict_failed";
        public const string Mismatch = "artifact_mismatch";
    }

    /// <summary>The rule "a passing verdict for exactly these artifacts" (03 s8 RequiresStageVerdict).</summary>
    public static class VerdictCheck
    {
        /// <summary>
        /// Checks <paramref name="verdict"/> against the change set and its code artifacts. Returns null when the verdict
        /// admits them, else the reason (<see cref="VerdictReasons"/>) with a message.
        /// </summary>
        public static string? Check(StageVerdict? verdict, string changeSetId, string packageSha, string? proposalSha, IReadOnlyDictionary<string, string>? packageFiles, out string message)
        {
            if (verdict == null)
            {
                message = "No stage verdict is retained for change set " + changeSetId + "; stage it first (POST /v1/stage).";
                return VerdictReasons.Missing;
            }

            if (!verdict.CompanionVerified)
            {
                message = "Verdict must be fetched and verified by the authenticated companion stage service.";
                return VerdictReasons.Untrusted;
            }
            if (verdict.Confinement != "docker" && !(verdict.Confinement == "host" && verdict.HostAllowed))
            {
                message = "Host confinement requires explicit operator opt-in; unknown confinement is refused.";
                return VerdictReasons.Untrusted;
            }
            var required = new HashSet<string>(new[] { "scan", "checkers", "dotnet", "unity-editmode", "playmode-smoke", "determinism", "budget" }, StringComparer.Ordinal);
            foreach (StageVerdictStep step in verdict.Steps)
            {
                if (step.Status != "pass" || !required.Remove(step.Id))
                {
                    message = "Every mandatory stage step must pass exactly once.";
                    return VerdictReasons.Failed;
                }
            }
            if (required.Count != 0 || verdict.Document["forbiddenHits"] is not JArray hits || hits.Count != 0
                || verdict.Document["coldCache"]?.Type != JTokenType.Boolean || verdict.BudgetMs != 360000
                || verdict.DurationMs < 0 || (!verdict.ColdCache && verdict.DurationMs > 360000))
            {
                message = "Incomplete, forbidden, or over-budget verdict.";
                return VerdictReasons.Failed;
            }

            if (!string.Equals(verdict.ChangeSetId, changeSetId, StringComparison.Ordinal))
            {
                message = "The verdict " + verdict.Digest + " is for change set " + verdict.ChangeSetId + ", not " + changeSetId + ".";
                return VerdictReasons.Mismatch;
            }

            if (!verdict.Pass || verdict.Partial || verdict.Failure != null)
            {
                message = "The stage verdict did not pass: " + verdict.Summary + ".";
                return VerdictReasons.Failed;
            }

            if (!verdict.Artifacts.TryGetValue("package", out string? stagedPackage) || !string.Equals(stagedPackage, packageSha, StringComparison.Ordinal))
            {
                message = "The package artifact sha256:" + packageSha + " is not the one the verdict covers (" + (stagedPackage ?? "none") + ").";
                return VerdictReasons.Mismatch;
            }

            if (proposalSha == null || !verdict.Artifacts.TryGetValue("proposal", out string? stagedProposal) || !string.Equals(stagedProposal, proposalSha, StringComparison.Ordinal))
            {
                message = "The proposal artifact sha256:" + proposalSha + " is not the one the verdict covers.";
                return VerdictReasons.Mismatch;
            }

            if (packageFiles != null)
            {
                foreach (KeyValuePair<string, string> file in packageFiles)
                {
                    if (!verdict.Files.TryGetValue(file.Key, out string? staged) || !string.Equals(staged, file.Value, StringComparison.Ordinal))
                    {
                        message = "The package file " + file.Key + " differs from the staged file (sha256 mismatch or not staged).";
                        return VerdictReasons.Mismatch;
                    }
                }

                foreach (string staged in verdict.Files.Keys)
                {
                    if (!packageFiles.ContainsKey(staged))
                    {
                        message = "The staged file " + staged + " is missing from the package.";
                        return VerdictReasons.Mismatch;
                    }
                }
            }

            message = "verdict " + verdict.Digest + " admits change set " + changeSetId;
            return null;
        }
    }

    /// <summary>
    /// The catalog-set hash (P2.4): the fingerprint of the catalog a world runs when admitted mechanisms add their
    /// catalogs to its baked catalog. Lowercase hex SHA-256 of UTF-8 "gamecore.catalog-set/1\n" + world + "\n" + the
    /// mechanism fingerprints sorted ordinally and joined by "\n"; with no mechanism it is the world fingerprint itself.
    /// The same formula is implemented by the stage harness and by a mechanism's CompositeCatalog.
    /// </summary>
    public static class CatalogSet
    {
        public const string Prefix = "gamecore.catalog-set/1";

        public static string Combine(string world, IEnumerable<string> mechanisms)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            List<string> sorted = new List<string>(mechanisms ?? Array.Empty<string>());
            if (sorted.Count == 0)
            {
                return world;
            }

            sorted.Sort(StringComparer.Ordinal);
            string text = Prefix + "\n" + world + "\n" + string.Join("\n", sorted);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                StringBuilder hex = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
                {
                    hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return hex.ToString();
            }
        }
    }
}
