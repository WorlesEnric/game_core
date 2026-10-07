#nullable enable
using System;
using GameCore.Studio.Authoring.Agent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using System.Text;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    /// <summary>The outcome of one compile requested by an admission.</summary>
    public sealed class AdmissionCompileResult
    {
        public AdmissionCompileResult(bool succeeded, bool reloadPending, string detail, IReadOnlyList<string>? errors = null)
        {
            Succeeded = succeeded;
            ReloadPending = reloadPending;
            Detail = new SecretRedactor().Redact(detail);
            var redactor = new SecretRedactor();
            var redactedErrors = new List<string>();
            foreach (string error in errors ?? Array.Empty<string>()) redactedErrors.Add(redactor.Redact(error));
            Errors = redactedErrors.AsReadOnly();
        }

        public bool Succeeded { get; }

        /// <summary>A domain reload follows: the admission continues in the next domain (AdmissionResumer).</summary>
        public bool ReloadPending { get; }

        public string Detail { get; }

        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>Requests a script compile (and package re-resolve) and reports how it ended.</summary>
    public interface IAdmissionCompiler
    {
        void Compile(string reason, Action<AdmissionCompileResult> done);
    }

    /// <summary>Reads the live catalog fingerprints.</summary>
    public interface IAdmissionCatalog
    {
        /// <summary>The world's catalog fingerprint after the re-bake check (Entry.Verify, Bake when stale); null with a problem.</summary>
        string? WorldFingerprint(out string? problem);

        /// <summary>The fingerprint of a loaded mechanism catalog type; null when the type is not loaded.</summary>
        string? MechanismFingerprint(string catalogType, out string? problem);
    }

    /// <summary>Re-runs the project checkers on an admitted package.</summary>
    public interface IAdmissionChecker
    {
        bool Check(string packageDirectory, string package, out string detail);
    }

    /// <summary>Captures and restores the running game around an admission (SaveService through the game's hook).</summary>
    public interface IAdmissionCapture
    {
        /// <summary>Capture once per slot. Retries must validate/reuse an existing checkpoint, never overwrite it.</summary>
        bool TryCapture(string slot, out string? problem);

        bool TryRestore(string slot, out string? problem);
    }

    /// <summary>Where the fault hook is called (rollback tests).</summary>
    public enum AdmissionFaultPoint
    {
        Pending,
        Capture,
        AfterCapture,
        StopPlay,
        Compile,
        Reload,
        Rebake,
        Verify,
        Applied,
        UndoPending,
        UndoCompile,
        UndoVerify,
        RestorePlay,
        RestoreCapture,
        Smoke,
        AfterApply,
        BeforeCompile,
        BeforeChecks,
        BeforeCatalogCheck,
        SmokePending,
    }

    /// <summary>The result of one frame of a trusted game's admission smoke test.</summary>
    public enum AdmissionSmokeStatus
    {
        Pending,
        Passed,
        Failed,
    }

    /// <summary>Admission switches; every member has a Unity default.</summary>
    public sealed class AdmissionOptions
    {
        /// <summary>Install under the repository's Packages/ with a manifest entry instead of the project's Packages/.</summary>
        public bool SharedPolicy { get; set; }

        /// <summary>Overrides the directory the package directory is created in (tests).</summary>
        public string? PackagesRoot { get; set; }

        /// <summary>The repository checkout (shared policy, checkers); default: the first ancestor of the project holding studio/tools.</summary>
        public string? RepositoryRoot { get; set; }

        public IAdmissionCompiler? Compiler { get; set; }

        public IAdmissionCatalog? Catalog { get; set; }

        public IAdmissionChecker? Checker { get; set; }

        public IAdmissionCapture? Capture { get; set; }
        public IStageService? StageService { get; set; }
        public string? ProjectId { get; set; }
        public Func<string>? SourceRevision { get; set; }
        public Func<string>? CatalogRevision { get; set; }
        public bool AllowHostConfinement { get; set; }
        public Action? StartPlayMode { get; set; }
        public Func<bool>? SessionReady { get; set; }
        public Func<StageVerdict, bool>? SmokeTest { get; set; }
        /// <summary>Optional asynchronous smoke test, polled once per idle Editor update. Rebind after reload.</summary>
        public Func<StageVerdict, AdmissionSmokeStatus>? PollSmokeTest { get; set; }
        /// <summary>Maximum smoke polling frames, including waits for service rebinding; persisted at smoke entry.</summary>
        public int SmokeTestFrameBudget { get; set; } = 120;
        /// <summary>Wall-clock smoke budget in seconds, including time across reload; persisted at smoke entry.</summary>
        public double SmokeTestTimeoutSeconds { get; set; } = 60;
        // Trusted game bootstrap re-registers these services after every domain reload.


        public Func<bool>? PlayModeProbe { get; set; }

        public Action? StopPlayMode { get; set; }

        /// <summary>Called at each <see cref="AdmissionFaultPoint"/>; throwing fails the admission (and rolls it back).</summary>
        public Action<AdmissionFaultPoint, string>? FaultHook { get; set; }

        /// <summary>Re-bake attempts after a refused world bake before the admission rolls back (default 3).</summary>
        public int RebakeRetries { get; set; } = 3;

        /// <summary>Runs an action after a delay in seconds once the Editor is idle; default: <see cref="StageAdmission.UnityDefer"/>.</summary>
        public Action<double, Action>? Defer { get; set; }
    }

    /// <summary>How an admission step ended.</summary>
    public enum AdmissionOutcome
    {
        /// <summary>Waiting for a compile / domain reload; the admission continues by itself.</summary>
        Pending,
        Admitted,
        Refused,
        RolledBack,
        Undone,
        UndoFailed,
    }

    /// <summary>The result of an admission step.</summary>
    public sealed class AdmissionResult
    {
        public AdmissionResult(string changeSetId, AdmissionOutcome outcome, string detail)
        {
            ChangeSetId = changeSetId;
            Outcome = outcome;
            Detail = new SecretRedactor().Redact(detail);
        }

        public string ChangeSetId { get; }

        public AdmissionOutcome Outcome { get; }

        public string Detail { get; }

        public string? Reason { get; set; }

        public string? Package { get; set; }

        public string? Directory { get; set; }

        public string? Verdict { get; set; }

        public string? Slot { get; set; }

        /// <summary>The live catalog-set hash before the admission.</summary>
        public string? Before { get; set; }

        /// <summary>The live catalog-set hash after the step.</summary>
        public string? Live { get; set; }

        public string? Predicted { get; set; }

        public string? Confinement { get; set; }

        public bool? ColdCache { get; set; }

        /// <summary>The save slot captured before the admission (offer restore when rolled back).</summary>
        public string? CaptureSlot { get; set; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; set; } = Array.Empty<Diagnostic>();

        public double Milliseconds { get; set; }

        public JObject ToJson()
        {
            JObject json = new JObject
            {
                ["changeSetId"] = ChangeSetId,
                ["outcome"] = Outcome.ToString(),
                ["detail"] = Detail,
                ["milliseconds"] = Math.Round(Milliseconds, 1),
            };
            Add(json, "reason", Reason);
            Add(json, "package", Package);
            Add(json, "directory", Directory);
            Add(json, "verdict", Verdict);
            Add(json, "slot", Slot);
            Add(json, "before", Before);
            Add(json, "live", Live);
            Add(json, "predicted", Predicted);
            Add(json, "confinement", Confinement);
            if (ColdCache.HasValue) json["coldCache"] = ColdCache.Value;
            Add(json, "captureSlot", CaptureSlot);
            if (Diagnostics.Count > 0)
            {
                JArray diagnostics = new JArray();
                foreach (Diagnostic diagnostic in Diagnostics)
                {
                    diagnostics.Add(diagnostic.Code + ": " + diagnostic.Message);
                }

                json["diagnostics"] = diagnostics;
            }

            return json;
        }

        private static void Add(JObject json, string name, string? value)
        {
            if (value != null)
            {
                json[name] = value;
            }
        }
    }

    /// <summary>The staging lane's admission service of one Studio runtime.</summary>
    public sealed partial class StageAdmission
    {
        public const string VerdictScenario = "stage.verdict";
        public const string AdmissionScenario = "stage.admission";
        public const string UndoScenario = "stage.undo";
        public const string VerdictMediaType = "application/vnd.gamecore.stage-verdict+json";



        private readonly StudioRuntime _runtime;
        private readonly Dictionary<string, DateTime> _started = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private StageAdmission(StudioRuntime runtime, AdmissionOptions options)
        {
            _runtime = runtime;
            Options = options;
            runtime.History.RegisterHandler(new AdmissionHistoryHandler(this));
            Finished += result =>
            {
                if (result.Outcome == AdmissionOutcome.Admitted || result.Outcome == AdmissionOutcome.Undone)
                    runtime.History.CompleteHandledEntry(result.ChangeSetId);
            };
        }

        public AdmissionOptions Options { get; }

        public StudioRuntime Runtime => _runtime;

        /// <summary>Raised when an admission or undo finishes (not for Pending).</summary>
        public event Action<AdmissionResult>? Finished;

        public string StateRoot => Path.Combine(_runtime.Paths.StateRoot, "Studio", "Admission");

        /// <summary>The admission service of <paramref name="runtime"/> (Unity defaults unless configured first).</summary>
        public static StageAdmission Of(StudioRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            return AdmissionSession.instance.Instances.GetValue(runtime, r => new StageAdmission(r, new AdmissionOptions()));
        }

        /// <summary>Sets the options of <paramref name="runtime"/>'s admission service (tests, tools); call before first use.</summary>
        public static StageAdmission Configure(StudioRuntime runtime, AdmissionOptions options)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (AdmissionSession.instance.Instances.TryGetValue(runtime, out StageAdmission previous))
                previous.StopSmokePolling();
            AdmissionSession.instance.Instances.Remove(runtime);
            StageAdmission admission = new StageAdmission(runtime, options ?? new AdmissionOptions());
            AdmissionSession.instance.Instances.Add(runtime, admission);
            return admission;
        }

        private IAdmissionCompiler Compiler => Options.Compiler ??= new UnityAdmissionCompiler();

        private IAdmissionCatalog Catalog => Options.Catalog ??= new ReflectionAdmissionCatalog();

        private IAdmissionChecker Checker => Options.Checker ??= new PythonAdmissionChecker(RepositoryRoot);

        private bool IsPlaying => Options.PlayModeProbe?.Invoke() ?? UnityEditor.EditorApplication.isPlaying;

        // ------------------------------------------------------------------------------------------- paths

        /// <summary>The repository checkout holding the project (null when the project is not inside one).</summary>
        public string? RepositoryRoot
        {
            get
            {
                if (Options.RepositoryRoot != null)
                {
                    return Options.RepositoryRoot;
                }

                for (DirectoryInfo? directory = new DirectoryInfo(_runtime.Paths.ProjectRoot); directory != null; directory = directory.Parent)
                {
                    if (System.IO.Directory.Exists(Path.Combine(directory.FullName, "studio", "tools")))
                    {
                        return directory.FullName;
                    }
                }

                return null;
            }
        }

        /// <summary>Where a package is installed.</summary>
        public string PackageDirectory(string package, bool shared)
        {
            if (Options.PackagesRoot != null)
            {
                return Path.GetFullPath(Path.Combine(Options.PackagesRoot, package));
            }

            if (shared || Options.SharedPolicy)
            {
                string repository = RepositoryRoot ?? throw new InvalidOperationException("The shared policy needs the project inside a repository checkout.");
                return Path.GetFullPath(Path.Combine(repository, "Packages", package));
            }

            return Path.GetFullPath(Path.Combine(_runtime.Paths.ProjectRoot, "Packages", package));
        }

        public string ProjectRelative(string path)
        {
            string relative = Path.GetRelativePath(_runtime.Paths.ProjectRoot, path);
            return relative.Replace('\\', '/');
        }

        public string FromProjectRelative(string relative)
        {
            return Path.GetFullPath(Path.Combine(_runtime.Paths.ProjectRoot, relative));
        }

        /// <summary>Null when <paramref name="path"/> is an installed package directory of <paramref name="package"/> that may be deleted.</summary>
        public string? CheckRemovable(string path, string package)
        {
            string full = Path.GetFullPath(path).TrimEnd('/', '\\');
            if (package.StartsWith("com.gamecore.", StringComparison.OrdinalIgnoreCase)
                || package.StartsWith("com.unity.", StringComparison.OrdinalIgnoreCase)) return "reserved_package";
            JObject? owner = ReadState("admitted.json")[package] as JObject;
            string? id = (string?)owner?["changeSetId"];
            if (id == null || _runtime.Journal.Read(id) == null
                || !string.Equals((string?)owner?["directory"], ProjectRelative(full), StringComparison.Ordinal)) return "package_not_admitted";
            StageDataPaths.ContainedFile(Path.GetDirectoryName(full)!, Path.GetFileName(full));

            if (!string.Equals(Path.GetFileName(full), package, StringComparison.Ordinal))
            {
                return "The directory " + full + " is not named after the package " + package + ".";
            }

            string parent = Path.GetDirectoryName(full) ?? string.Empty;
            List<string> allowed = new List<string> { Path.GetFullPath(Path.Combine(_runtime.Paths.ProjectRoot, "Packages")) };
            if (Options.PackagesRoot != null)
            {
                allowed.Add(Path.GetFullPath(Options.PackagesRoot));
            }

            if (RepositoryRoot != null)
            {
                allowed.Add(Path.GetFullPath(Path.Combine(RepositoryRoot, "Packages")));
            }

            bool inside = false;
            foreach (string root in allowed)
            {
                inside |= string.Equals(root.TrimEnd('/', '\\'), parent, StringComparison.Ordinal);
            }

            if (!inside)
            {
                return "The directory " + full + " is not an installed package location.";
            }

            if (!System.IO.Directory.Exists(full))
            {
                return "The package directory " + full + " does not exist.";
            }

            string manifest = Path.Combine(full, "package.json");
            try
            {
                JObject json = JObject.Parse(File.ReadAllText(manifest, Encoding.UTF8));
                if (!string.Equals((string?)json["name"], package, StringComparison.Ordinal))
                {
                    return "The package.json in " + full + " names " + (string?)json["name"] + ", not " + package + ".";
                }
            }
            catch (Exception error) when (error is IOException || error is JsonException || error is UnauthorizedAccessException)
            {
                return "The package directory " + full + " has no readable package.json: " + error.Message;
            }

            return null;
        }

        /// <summary>Adds <c>"name": "file:..."</c> to the project manifest (shared policy); null on success.</summary>
        internal string? AddManifestEntry(string package, string directory)
        {
            string path = Path.Combine(_runtime.Paths.ProjectRoot, "Packages", "manifest.json");
            try
            {
                JObject manifest = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                JObject dependencies = manifest["dependencies"] as JObject ?? new JObject();
                string relative = Path.GetRelativePath(Path.GetDirectoryName(path)!, directory).Replace('\\', '/');
                dependencies[package] = "file:" + relative;
                manifest["dependencies"] = Sorted(dependencies);
                StudioPaths.WriteAllTextAtomic(path, manifest.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
                return null;
            }
            catch (Exception error) when (error is IOException || error is JsonException || error is UnauthorizedAccessException)
            {
                return "cannot edit " + path + ": " + error.Message;
            }
        }

        /// <summary>Removes a package from the project manifest; null on success.</summary>
        internal string? RemoveManifestEntry(string package)
        {
            string path = Path.Combine(_runtime.Paths.ProjectRoot, "Packages", "manifest.json");
            try
            {
                JObject manifest = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                if (manifest["dependencies"] is JObject dependencies && dependencies.Remove(package))
                {
                    StudioPaths.WriteAllTextAtomic(path, manifest.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
                }

                return null;
            }
            catch (Exception error) when (error is IOException || error is JsonException || error is UnauthorizedAccessException)
            {
                return "cannot edit " + path + ": " + error.Message;
            }
        }

        /// <summary>Deletes a directory tree, clearing read-only attributes first.</summary>
        public static void DeleteDirectory(string path)
        {
            if (!System.IO.Directory.Exists(path))
            {
                return;
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                System.IO.Directory.Delete(path);
                return;
            }
            foreach (string file in System.IO.Directory.GetFiles(path))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            foreach (string directory in System.IO.Directory.GetDirectories(path)) DeleteDirectory(directory);
            System.IO.Directory.Delete(path);

        }

        // ------------------------------------------------------------------------------------------- verdicts

        /// <summary>Retains the artifacts of a candidate directory (change-set.json + artifacts/) and returns the change set.</summary>
        public ChangeSet RetainCandidate(string candidateDirectory)
        {
            string text = File.ReadAllText(StageDataPaths.ContainedFile(candidateDirectory, "change-set.json"), Encoding.UTF8);
            ChangeSet changeSet = StudioJson.Deserialize<ChangeSet>(text);
            foreach (ArtifactRef artifact in changeSet.Artifacts ?? Array.Empty<ArtifactRef>())
            {
                if (artifact.Name != null && (Path.GetFileName(artifact.Name) != artifact.Name || artifact.Name.Contains("\\") || artifact.Name == "." || artifact.Name == ".."))
                    throw new ArgumentException("artifact_path_forbidden");
                string? file = null;
                foreach (string? candidate in new[] { artifact.Name, artifact.Sha256, "sha256-" + artifact.Sha256 })
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    foreach (string folder in new[] { Path.Combine(candidateDirectory, "artifacts"), candidateDirectory })
                    {
                        string path = StageDataPaths.ContainedFile(folder, candidate);
                        if (file == null && File.Exists(path))
                        {
                            file = path;
                        }
                    }
                }

                if (file == null)
                {
                    throw new ArtifactStoreException("Artifact " + (artifact.Name ?? artifact.Sha256) + " is not in " + candidateDirectory + ".");
                }

                _runtime.Artifacts.PutFile(file, artifact);
            }

            return changeSet;
        }

        /// <summary>Marks the change set's verdict pending (a stage job was submitted).</summary>
        public void MarkStagePending(string changeSetId, string? slot)
        {
            SetScenario(changeSetId, VerdictScenario, ScenarioStatus.Pending, "staging" + (slot == null ? string.Empty : " in slot " + slot));
        }

        private readonly Dictionary<string, StageVerdict> _verified = new Dictionary<string, StageVerdict>(StringComparer.Ordinal);

        // Compatibility reader for old clients: display/evidence only, never authorization.
        public StageVerdict RecordVerdict(byte[] bytes)
        {
            StageVerdict verdict = StageVerdict.Parse(bytes, out string? problem) ?? throw new ArgumentException(problem);
            _runtime.Artifacts.Put(bytes, new ArtifactRef(verdict.Digest, VerdictMediaType, bytes.LongLength, "verdict.json", null, "verdict"));
            SetScenario(verdict.ChangeSetId, VerdictScenario, ScenarioStatus.Fail, "verdict_untrusted: authenticated stage fetch required");
            return verdict;
        }

        public async Task<StageVerdict> FetchVerdict(string jobId, StageCandidateRequest expected)
        {
            _verified.Remove(expected.ChangeSetId);
            IStageService service = Options.StageService ?? throw new InvalidOperationException("stage_service_unavailable");
            SignedVerdict signed = await service.GetVerdict(jobId);
            if (string.IsNullOrWhiteSpace(jobId) || signed.JobId != jobId || string.IsNullOrWhiteSpace(signed.Signature))
                throw new InvalidOperationException(VerdictReasons.Untrusted);
            StageVerification verified = await service.VerifyVerdict(jobId, new StageVerificationRequest(signed, expected));
            if (!verified.Verified || verified.JobId != jobId) throw new InvalidOperationException(VerdictReasons.Untrusted);
            JObject document = signed.Verdict;
            foreach (var pair in new[] { new KeyValuePair<string, string>("jobId", jobId),
                new KeyValuePair<string, string>("projectId", expected.ProjectId),
                new KeyValuePair<string, string>("sourceRevision", expected.SourceRevision),
                new KeyValuePair<string, string>("catalogRevision", expected.CatalogRevision),
                new KeyValuePair<string, string>("changeSetId", expected.ChangeSetId) })
                if ((string?)document[pair.Key] != pair.Value) throw new InvalidOperationException(VerdictReasons.Mismatch);
            byte[] bytes = Encoding.UTF8.GetBytes(document.ToString(Formatting.None));
            StageVerdict verdict = StageVerdict.Parse(bytes, out string? problem) ?? throw new ArgumentException(problem);
            verdict.CompanionVerified = true;
            verdict.HostAllowed = Options.AllowHostConfinement;
            string? reason = VerdictCheck.Check(verdict, expected.ChangeSetId, expected.PackageDigest, expected.ProposalDigest, null, out string message);
            if (reason != null) throw new InvalidOperationException(reason + ": " + message);
            _runtime.Artifacts.Put(bytes, new ArtifactRef(verdict.Digest, VerdictMediaType, bytes.LongLength, "verdict.json", null, "verdict"));
            _verified[expected.ChangeSetId] = verdict;
            JObject index = ReadState("verdicts.json");
            index[expected.ChangeSetId] = new JObject { ["jobId"] = jobId, ["expected"] = JObject.FromObject(expected), ["digest"] = verdict.Digest };
            WriteState("verdicts.json", index);
            SetScenario(expected.ChangeSetId, VerdictScenario, ScenarioStatus.Pass, verdict.Summary + "; " + verdict.Reference);
            return verdict;
        }

        public StageVerdict? VerdictOf(string changeSetId) => _verified.TryGetValue(changeSetId, out StageVerdict value) ? value : null;

        /// <summary>Revokes local authorization only after the companion acknowledges completed cancellation.</summary>
        public async Task CancelStage(string jobId, string changeSetId)
        {
            if (Options.StageService is not IStageJobControl control)
                throw new InvalidOperationException("stage_cancel_unavailable");
            string state = await control.CancelStage(jobId);
            if (state != "cancelled") throw new InvalidOperationException("stage_job_" + state);
            _verified.Remove(changeSetId);
            JObject index = ReadState("verdicts.json");
            index.Remove(changeSetId);
            WriteState("verdicts.json", index);
            SetScenario(changeSetId, VerdictScenario, ScenarioStatus.Fail, "stage_job_cancelled");
        }

        public async Task<int> RefreshPendingVerdicts()
        {
            int count = 0;
            foreach (JProperty item in ReadState("verdicts.json").Properties())
            {
                if (ReadPending(item.Name) == null || item.Value is not JObject record) continue;
                StageCandidateRequest expected = record["expected"]!.ToObject<StageCandidateRequest>()!;
                await FetchVerdict((string)record["jobId"]!, expected);
                count++;
            }
            return count;
        }

        public StageCandidateRequest BuildStageRequest(ChangeSet candidate, string sourceProject)
        {
            Operation operation = ProposalOperation(candidate);
            string package = MechanismAdmission.Digest(operation.Args?["package"]) ?? throw new ArgumentException("package_missing");
            string proposal = MechanismAdmission.Digest(operation.Args?["proposal"]) ?? throw new ArgumentException("proposal_missing");
            StageDataPaths.ValidateProposal(JObject.Parse(Encoding.UTF8.GetString(_runtime.Artifacts.Read(proposal))));
            var inputs = new List<string>();
            foreach (JToken input in operation.Args?["stageInputs"] as JArray ?? new JArray())
            {
                string path = input.Value<string>() ?? "";
                StageDataPaths.ValidateInput(path);
                StageDataPaths.ContainedFile(_runtime.Paths.ProjectRoot, path);
                inputs.Add(path);
            }
            return new StageCandidateRequest(candidate.Id, Options.ProjectId ?? "", sourceProject, Options.SourceRevision?.Invoke() ?? "",
                Options.CatalogRevision?.Invoke() ?? "", package, proposal, inputs);
        }

        private static Operation ProposalOperation(ChangeSet candidate)
        {
            if (candidate.Operations.Count != 1 || (candidate.Operations[0].Tool != BuiltInToolIds.MechanismPropose
                && candidate.Operations[0].Tool != MechanismAdmission.AdmitTool)) throw new ArgumentException("invalid_candidate");
            return candidate.Operations[0];
        }

        /// <summary>
        /// The admission change set of a staged candidate: its <c>mechanism.propose</c> becomes <c>mechanism.admit</c>
        /// (same change-set id, same package and proposal artifacts, plus the verdict artifact).
        /// </summary>
        public ChangeSet ToAdmission(ChangeSet candidate, StageVerdict? verdict)
        {
            Operation? propose = null;
            foreach (Operation operation in candidate.Operations)
            {
                if (operation.Tool == BuiltInToolIds.MechanismPropose || operation.Tool == MechanismAdmission.AdmitTool)
                {
                    if (propose != null)
                    {
                        throw new ArgumentException("A staged change set carries exactly one mechanism.propose operation.", nameof(candidate));
                    }

                    propose = operation;
                }
            }

            if (propose == null)
            {
                throw new ArgumentException("The change set carries no mechanism.propose operation.", nameof(candidate));
            }

            JObject args = new JObject();
            JObject source = propose.Args ?? new JObject();
            foreach (string name in new[] { "package", "proposal" })
            {
                if (source[name] != null)
                {
                    args[name] = source[name]!.DeepClone();
                }
            }

            if (Options.SharedPolicy)
            {
                args["policy"] = MechanismAdmission.SharedPolicy;
            }

            List<ArtifactRef> artifacts = new List<ArtifactRef>();
            foreach (ArtifactRef artifact in candidate.Artifacts ?? Array.Empty<ArtifactRef>())
            {
                string reference = ContentStamp.Prefix + artifact.Sha256;
                if (Uses(args, reference))
                {
                    artifacts.Add(artifact);
                }
            }

            List<ValidationScenario> validation = new List<ValidationScenario>();
            if (verdict != null)
            {
                args["verdict"] = new JObject { ["artifact"] = verdict.Reference };
                long size = _runtime.Artifacts.Describe(verdict.Digest)?.Bytes ?? _runtime.Artifacts.Read(verdict.Digest).LongLength;
                artifacts.Add(new ArtifactRef(verdict.Digest, VerdictMediaType, size, "verdict.json", null, "verdict"));
                validation.Add(new ValidationScenario(VerdictScenario, verdict.Pass ? ScenarioStatus.Pass : ScenarioStatus.Fail, verdict.Summary + "; " + verdict.Reference));
            }

            Operation admit = new Operation(propose.OpId, MechanismAdmission.AdmitTool, null, args, null, null, RuntimeApply.Compile);
            return new ChangeSet(
                candidate.Id,
                ChangeSet.SchemaId,
                candidate.Intent,
                new[] { admit },
                artifacts: artifacts,
                validation: validation,
                requirements: new Requirements(RuntimeApply.Compile, false, true, false),
                links: candidate.Links,
                timestamps: candidate.Timestamps);
        }

        // ------------------------------------------------------------------------------------------- helpers

        /// <summary>The live catalog-set hash: Combine(world, admitted mechanisms) (<paramref name="admitted"/> or the registry).</summary>
        public string? LiveHash(JObject? admitted, out string? problem)
        {
            string? world = Catalog.WorldFingerprint(out problem);
            if (world == null)
            {
                return null;
            }

            List<string> mechanisms = new List<string>();
            foreach (JProperty property in (admitted ?? ReadState("admitted.json")).Properties())
            {
                string? type = (string?)property.Value["catalogType"];
                string? fingerprint = type == null ? null : Catalog.MechanismFingerprint(type, out _);
                if (fingerprint != null)
                {
                    mechanisms.Add(fingerprint);
                }
            }

            return CatalogSet.Combine(world, mechanisms);
        }

        private const double RebakeRetrySeconds = 10;

        /// <summary>Runs <paramref name="action"/> once <paramref name="seconds"/> have passed and the Editor neither compiles nor imports.</summary>
        public static void UnityDefer(double seconds, Action action)
        {
            double due = UnityEditor.EditorApplication.timeSinceStartup + seconds;
            UnityEditor.EditorApplication.CallbackFunction? tick = null;
            tick = () =>
            {
                if (UnityEditor.EditorApplication.timeSinceStartup < due || UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
                {
                    return;
                }

                UnityEditor.EditorApplication.update -= tick;
                action();
            };
            UnityEditor.EditorApplication.update += tick;
        }

        private AdmissionResult Refuse(string changeSetId, string reason, string detail)
        {
            SetScenario(changeSetId, AdmissionScenario, ScenarioStatus.Fail, "refused: " + detail);
            AdmissionResult result = new AdmissionResult(changeSetId, AdmissionOutcome.Refused, detail) { Reason = reason };
            Finished?.Invoke(result);
            return result;
        }

        private void Fault(AdmissionFaultPoint point, string changeSetId)
        {
            Options.FaultHook?.Invoke(point, changeSetId);
        }

        private double Elapsed(string changeSetId, JObject? pending)
        {
            // Unix milliseconds, not an ISO string: Newtonsoft re-reads ISO strings as local DateTime values.
            if (_started.TryGetValue(changeSetId, out DateTime started))
            {
                _started.Remove(changeSetId);
                return (DateTime.UtcNow - started).TotalMilliseconds;
            }

            long? startedMs = (long?)pending?["startedMs"];
            return startedMs == null ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startedMs.Value;
        }

        private void SetScenario(string changeSetId, string scenario, ScenarioStatus status, string detail)
        {
            if (!IdDerivation.IsChangeSetId(changeSetId))
            {
                return;
            }

            ChangeSet? entry = _runtime.Journal.Read(changeSetId);
            if (entry != null)
            {
                _runtime.Journal.Write(WithScenario(entry, scenario, status, detail));
            }
        }

        /// <summary>The change set with <paramref name="scenario"/> replaced (or added).</summary>
        public static ChangeSet WithScenario(ChangeSet entry, string scenario, ScenarioStatus status, string detail)
        {
            detail = new SecretRedactor().Redact(detail);
            List<ValidationScenario> validation = new List<ValidationScenario>();
            foreach (ValidationScenario existing in entry.Validation ?? Array.Empty<ValidationScenario>())
            {
                if (!string.Equals(existing.Scenario, scenario, StringComparison.Ordinal))
                {
                    validation.Add(existing);
                }
            }

            validation.Add(new ValidationScenario(scenario, status, detail.Length > 900 ? detail.Substring(0, 900) + "..." : detail));
            return entry.WithValidation(validation);
        }

        private static bool Uses(JToken args, string reference)
        {
            foreach (JToken token in args.SelectTokens("$..artifact"))
            {
                if (token.Type == JTokenType.String && string.Equals(token.Value<string>(), reference, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static JObject Sorted(JObject value)
        {
            List<string> names = new List<string>();
            foreach (JProperty property in value.Properties())
            {
                names.Add(property.Name);
            }

            names.Sort(StringComparer.Ordinal);
            JObject sorted = new JObject();
            foreach (string name in names)
            {
                sorted[name] = value[name]!.DeepClone();
            }

            return sorted;
        }

        private string PendingPath(string changeSetId) => Path.Combine(StateRoot, "pending-" + changeSetId + ".json");

        /// <summary>The pending record of an admission or undo, or null.</summary>
        public JObject? ReadPending(string changeSetId)
        {
            string path = PendingPath(changeSetId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private void WritePending(string changeSetId, JObject pending)
        {
            bool first = !File.Exists(PendingPath(changeSetId));
            WriteDurable(PendingPath(changeSetId), Pruned(pending).ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
            if (first) AdmissionResumer.Wake();
        }

        private void DeletePending(string changeSetId)
        {
            StopSmokePolling(changeSetId);
            string path = PendingPath(changeSetId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private JObject ReadState(string name)
        {
            string path = Path.Combine(StateRoot, name);
            if (!File.Exists(path))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (JsonException)
            {
                return new JObject();
            }
        }

        private void WriteState(string name, JObject value)
        {
            WriteDurable(Path.Combine(StateRoot, name), Pruned(value).ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
        }

        private static void WriteDurable(string path, string text)
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static JObject Pruned(JObject value)
        {
            JObject copy = (JObject)value.DeepClone();
            List<JToken> nulls = new List<JToken>();
            foreach (JToken token in copy.Descendants())
            {
                if (token is JProperty property && property.Value.Type == JTokenType.Null)
                {
                    nulls.Add(property);
                }
            }

            foreach (JToken token in nulls)
            {
                token.Remove();
            }

            return copy;
        }
    }
}
