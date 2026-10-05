#nullable enable
// GameCore.Studio.Edit - StageAdmission: the only path by which staged code reaches the live editor (P2.4,
// docs/studio/03 s8, 02 s7, W-MECH-01).
//
// RecordVerdict  retains a stage verdict (bytes, by digest) and writes the journal's "stage.verdict" validation
//                scenario (pending/pass/fail; P2.1 shows it).
// Admit          the creator's explicit Admit of a staged mechanism.propose candidate:
//                  1. Play Mode must be stopped (or, with captureAndStop, the running game is captured to the save
//                     slot admit-<id> through the registered capture hook and Play Mode is stopped first);
//                  2. the candidate becomes a mechanism.admit change set (same id) whose validator RequiresStageVerdict
//                     refuses it without a passing verdict for exactly its artifacts; the engine applies it (journal
//                     Interrupted -> per-op checkpoint -> Applied), writing the package from the retained archive;
//                  3. the entry is held Interrupted ("stage.admission" pending) and a pending record is written under
//                     Library/GameCoreStudio/stage, then the project recompiles;
//                  4. compile errors (no domain reload) or, after the domain reload (AdmissionResumer), a failing
//                     checker run, re-bake or catalog check roll the admission back: journal RollbackInterrupted (the
//                     mechanism.remove inverse deletes the package) and a recompile; a capture is offered for restore;
//                  5. otherwise the live catalog-set hash Combine(world, {mechanism}) must equal the verdict's predicted
//                     catalogDelta, and the entry becomes Applied ("stage.admission" pass).
// Undo           journal undo of an admitted change set (mechanism.remove), recompile, then the live catalog hash
//                must return to its value before the admission ("stage.undo").
// Compilation, the catalog and the checkers are behind interfaces so EditMode tests drive every branch without a domain
// reload; the Unity implementations are in UnityAdmissionServices.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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
            Detail = detail;
            Errors = errors ?? Array.Empty<string>();
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
        bool TryCapture(string slot, out string? problem);

        bool TryRestore(string slot, out string? problem);
    }

    /// <summary>Where the fault hook is called (rollback tests).</summary>
    public enum AdmissionFaultPoint
    {
        AfterApply,
        BeforeCompile,
        BeforeChecks,
        BeforeCatalogCheck,
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

        public Func<bool>? PlayModeProbe { get; set; }

        public Action? StopPlayMode { get; set; }

        /// <summary>Called at each <see cref="AdmissionFaultPoint"/>; throwing fails the admission (and rolls it back).</summary>
        public Action<AdmissionFaultPoint, string>? FaultHook { get; set; }
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
            Detail = detail;
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
    public sealed class StageAdmission
    {
        public const string VerdictScenario = "stage.verdict";
        public const string AdmissionScenario = "stage.admission";
        public const string UndoScenario = "stage.undo";
        public const string VerdictMediaType = "application/vnd.gamecore.stage-verdict+json";

        private static readonly ConditionalWeakTable<StudioRuntime, StageAdmission> Instances = new ConditionalWeakTable<StudioRuntime, StageAdmission>();

        private readonly StudioRuntime _runtime;
        private readonly Dictionary<string, DateTime> _started = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private StageAdmission(StudioRuntime runtime, AdmissionOptions options)
        {
            _runtime = runtime;
            Options = options;
        }

        public AdmissionOptions Options { get; }

        public StudioRuntime Runtime => _runtime;

        /// <summary>Raised when an admission or undo finishes (not for Pending).</summary>
        public event Action<AdmissionResult>? Finished;

        public string StateRoot => Path.Combine(_runtime.Paths.LibraryRoot, "stage");

        /// <summary>The admission service of <paramref name="runtime"/> (Unity defaults unless configured first).</summary>
        public static StageAdmission Of(StudioRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            return Instances.GetValue(runtime, r => new StageAdmission(r, new AdmissionOptions()));
        }

        /// <summary>Sets the options of <paramref name="runtime"/>'s admission service (tests, tools); call before first use.</summary>
        public static StageAdmission Configure(StudioRuntime runtime, AdmissionOptions options)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            Instances.Remove(runtime);
            StageAdmission admission = new StageAdmission(runtime, options ?? new AdmissionOptions());
            Instances.Add(runtime, admission);
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
        public string? AddManifestEntry(string package, string directory)
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
        public string? RemoveManifestEntry(string package)
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

            foreach (string file in System.IO.Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            System.IO.Directory.Delete(path, true);
        }

        // ------------------------------------------------------------------------------------------- verdicts

        /// <summary>Retains the artifacts of a candidate directory (change-set.json + artifacts/) and returns the change set.</summary>
        public ChangeSet RetainCandidate(string candidateDirectory)
        {
            string text = File.ReadAllText(Path.Combine(candidateDirectory, "change-set.json"), Encoding.UTF8);
            ChangeSet changeSet = StudioJson.Deserialize<ChangeSet>(text);
            foreach (ArtifactRef artifact in changeSet.Artifacts ?? Array.Empty<ArtifactRef>())
            {
                string? file = null;
                foreach (string? candidate in new[] { artifact.Name, artifact.Sha256, "sha256-" + artifact.Sha256 })
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    foreach (string folder in new[] { Path.Combine(candidateDirectory, "artifacts"), candidateDirectory })
                    {
                        string path = Path.Combine(folder, candidate);
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

        /// <summary>Retains verdict bytes and records the verdict in the journal ("stage.verdict").</summary>
        public StageVerdict RecordVerdict(byte[] bytes)
        {
            StageVerdict verdict = StageVerdict.Parse(bytes, out string? problem) ?? throw new ArgumentException("Not a stage verdict: " + problem, nameof(bytes));
            _runtime.Artifacts.Put(bytes, new ArtifactRef(verdict.Digest, VerdictMediaType, bytes.LongLength, "verdict.json", null, "verdict"));
            JObject index = ReadState("verdicts.json");
            index[verdict.ChangeSetId] = verdict.Digest;
            WriteState("verdicts.json", index);
            SetScenario(verdict.ChangeSetId, VerdictScenario, verdict.Pass ? ScenarioStatus.Pass : ScenarioStatus.Fail, verdict.Summary + "; " + verdict.Reference);
            _runtime.Log.Write(StudioLogLevel.Info, "stage", "verdict for " + verdict.ChangeSetId + ": " + verdict.Summary);
            return verdict;
        }

        /// <summary>The latest recorded verdict of a change set, or null.</summary>
        public StageVerdict? VerdictOf(string changeSetId)
        {
            string? digest = (string?)ReadState("verdicts.json")[changeSetId];
            if (digest == null || !_runtime.Artifacts.Has(digest))
            {
                return null;
            }

            return StageVerdict.Parse(_runtime.Artifacts.Read(digest), out _);
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

        // ------------------------------------------------------------------------------------------- admit

        /// <summary>
        /// Admits a staged candidate (the creator's explicit Admit). <paramref name="verdictBytes"/> records a verdict
        /// first; otherwise the latest recorded verdict of the change set is used.
        /// </summary>
        public AdmissionResult Admit(ChangeSet candidate, byte[]? verdictBytes = null, bool captureAndStop = false)
        {
            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            DateTime started = DateTime.UtcNow;
            StageVerdict? verdict = verdictBytes != null ? RecordVerdict(verdictBytes) : VerdictOf(candidate.Id);
            string? captureSlot = null;
            if (IsPlaying)
            {
                if (!captureAndStop)
                {
                    return Refuse(candidate.Id, "play_mode", "Stop Play Mode before admitting a mechanism, or choose capture & stop.");
                }

                captureSlot = "admit-" + candidate.Id.ToLowerInvariant();
                string? captureProblem = "no capture hook is registered (AdmissionOptions.Capture)";
                if (Options.Capture == null || !Options.Capture.TryCapture(captureSlot, out captureProblem))
                {
                    return Refuse(candidate.Id, "capture_failed", "The running game could not be captured to " + captureSlot + ": " + captureProblem);
                }

                (Options.StopPlayMode ?? (() => UnityEditor.EditorApplication.isPlaying = false))();
                if (IsPlaying)
                {
                    WriteState("admit-after-play-" + candidate.Id + ".json", new JObject { ["captureSlot"] = captureSlot });
                    return new AdmissionResult(candidate.Id, AdmissionOutcome.Pending, "Captured to " + captureSlot + "; admitting once Play Mode has stopped (call Admit again).") { CaptureSlot = captureSlot };
                }
            }

            ChangeSet admission;
            try
            {
                admission = ToAdmission(candidate, verdict);
            }
            catch (ArgumentException error)
            {
                return Refuse(candidate.Id, "invalid_candidate", error.Message);
            }

            string? before = LiveHash(null, out string? beforeProblem);
            ApplyReport report = _runtime.Engine.Apply(admission);
            if (!report.Ok)
            {
                Diagnostic? first = report.Diagnostics.Count > 0 ? report.Diagnostics[0] : null;
                foreach (Diagnostic diagnostic in report.Diagnostics)
                {
                    if (diagnostic.Data?["reason"] != null)
                    {
                        first = diagnostic;
                        break;
                    }
                }

                string reason = (string?)first?.Data?["reason"] ?? (first?.Code ?? "refused");
                string detail = first?.Message ?? ("The admission was " + report.State + ".");
                SetScenario(candidate.Id, AdmissionScenario, ScenarioStatus.Fail, "refused: " + detail);
                AdmissionResult refused = new AdmissionResult(candidate.Id, AdmissionOutcome.Refused, detail) { Reason = reason, Diagnostics = report.Diagnostics, Verdict = verdict?.Reference };
                Finished?.Invoke(refused);
                return refused;
            }

            ChangeSet entry = _runtime.Journal.Read(candidate.Id) ?? report.Entry;
            string package = verdict?.Package ?? string.Empty;
            string catalogType = verdict != null && verdict.Mechanisms.Count > 0 ? verdict.Mechanisms[0].Key : string.Empty;
            string directory = ProjectRelative(PackageDirectory(package, Options.SharedPolicy));
            JObject pending = new JObject
            {
                ["schema"] = "gamecore.studio.admission/1",
                ["changeSetId"] = candidate.Id,
                ["phase"] = "admit",
                ["package"] = package,
                ["directory"] = directory,
                ["catalogType"] = catalogType,
                ["verdict"] = verdict?.Digest,
                ["slot"] = verdict?.Slot,
                ["startedMs"] = new DateTimeOffset(started).ToUnixTimeMilliseconds(),
            };
            if (before != null)
            {
                pending["before"] = before;
            }

            if (captureSlot != null)
            {
                pending["captureSlot"] = captureSlot;
            }

            WritePending(candidate.Id, pending);
            _runtime.Journal.Write(WithScenario(entry.WithState(ChangeSetState.Interrupted), AdmissionScenario, ScenarioStatus.Pending, "compiling " + package + " (verdict " + verdict?.Digest + ", slot " + verdict?.Slot + ")"));
            _started[candidate.Id] = started;
            try
            {
                Fault(AdmissionFaultPoint.AfterApply, candidate.Id);
                Fault(AdmissionFaultPoint.BeforeCompile, candidate.Id);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                return Rollback(candidate.Id, "fault", "Admission failed before the compile: " + error.Message);
            }

            AdmissionResult? immediate = null;
            Compiler.Compile("admit " + candidate.Id, result => immediate = OnCompiled(candidate.Id, result));
            return immediate ?? new AdmissionResult(candidate.Id, AdmissionOutcome.Pending, "Compiling " + package + "; the admission continues after the domain reload.")
            {
                Package = package,
                Directory = directory,
                Verdict = verdict?.Reference,
                Slot = verdict?.Slot,
                Before = before,
                Predicted = verdict?.Predicted,
                CaptureSlot = captureSlot,
            };
        }

        private AdmissionResult OnCompiled(string changeSetId, AdmissionCompileResult result)
        {
            JObject? pending = ReadPending(changeSetId);
            string phase = (string?)pending?["phase"] ?? "admit";
            if (!result.Succeeded)
            {
                if (phase == "undo")
                {
                    return FinishUndo(changeSetId, false, "the recompile after the undo failed: " + result.Detail);
                }

                return Rollback(changeSetId, "compile_failed", "The project does not compile with the package: " + result.Detail);
            }

            if (result.ReloadPending)
            {
                if (pending != null)
                {
                    pending["awaitingReload"] = true;
                    WritePending(changeSetId, pending);
                }

                return new AdmissionResult(changeSetId, AdmissionOutcome.Pending, "Compiled; continuing after the domain reload.");
            }

            return phase == "undo" ? VerifyUndo(changeSetId) : Finalize(changeSetId);
        }

        /// <summary>Continues every admission or undo a domain reload interrupted (AdmissionResumer calls this).</summary>
        public IReadOnlyList<AdmissionResult> ResumePending()
        {
            List<AdmissionResult> results = new List<AdmissionResult>();
            if (!System.IO.Directory.Exists(StateRoot))
            {
                return results;
            }

            foreach (string file in System.IO.Directory.GetFiles(StateRoot, "pending-cs_*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(file).Substring("pending-".Length);
                JObject? pending = ReadPending(id);
                if (pending == null)
                {
                    continue;
                }

                results.Add((string?)pending["phase"] == "undo" ? VerifyUndo(id) : Finalize(id));
            }

            return results;
        }

        /// <summary>Checks, re-bake and catalog verification after the package compiled; Applied or rolled back.</summary>
        public AdmissionResult Finalize(string changeSetId)
        {
            JObject? pending = ReadPending(changeSetId);
            if (pending == null)
            {
                return new AdmissionResult(changeSetId, AdmissionOutcome.Refused, "No admission of " + changeSetId + " is pending.");
            }

            string package = (string?)pending["package"] ?? string.Empty;
            string directory = FromProjectRelative((string?)pending["directory"] ?? string.Empty);
            StageVerdict? verdict = VerdictOf(changeSetId);
            try
            {
                Fault(AdmissionFaultPoint.BeforeChecks, changeSetId);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                return Rollback(changeSetId, "fault", "Admission failed before the checkers: " + error.Message);
            }

            if (!Checker.Check(directory, package, out string checkDetail))
            {
                return Rollback(changeSetId, "checkers_failed", "The project checkers reject the admitted package: " + checkDetail);
            }

            try
            {
                Fault(AdmissionFaultPoint.BeforeCatalogCheck, changeSetId);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                return Rollback(changeSetId, "fault", "Admission failed before the catalog check: " + error.Message);
            }

            if (verdict == null || verdict.World == null || verdict.Predicted == null || verdict.Mechanisms.Count == 0)
            {
                return Rollback(changeSetId, "catalog_unpredicted", "The verdict carries no catalog delta (world, mechanism, predicted) to verify against.");
            }

            string? world = Catalog.WorldFingerprint(out string? worldProblem);
            if (world == null)
            {
                return Rollback(changeSetId, "rebake_failed", "The world re-bake failed: " + worldProblem);
            }

            string catalogType = verdict.Mechanisms[0].Key;
            string? mechanism = Catalog.MechanismFingerprint(catalogType, out string? mechanismProblem);
            if (mechanism == null)
            {
                return Rollback(changeSetId, "catalog_missing", "The mechanism catalog " + catalogType + " is not loaded after the compile: " + mechanismProblem);
            }

            string live = CatalogSet.Combine(world, new[] { mechanism });
            if (!string.Equals(world, verdict.World, StringComparison.Ordinal)
                || !string.Equals(mechanism, verdict.Mechanisms[0].Value, StringComparison.Ordinal)
                || !string.Equals(live, verdict.Predicted, StringComparison.Ordinal))
            {
                return Rollback(changeSetId, "catalog_mismatch", "The live catalog-set hash " + live + " (world " + world + ", mechanism " + mechanism + ") differs from the predicted " + verdict.Predicted + ".");
            }

            JObject admitted = ReadState("admitted.json");
            admitted[package] = new JObject { ["changeSetId"] = changeSetId, ["catalogType"] = catalogType, ["fingerprint"] = mechanism, ["directory"] = (string?)pending["directory"] };
            WriteState("admitted.json", admitted);
            string full = LiveHash(null, out _) ?? live;
            ChangeSet? entry = _runtime.Journal.Read(changeSetId);
            if (entry != null)
            {
                Timestamps stamps = new Timestamps(entry.Timestamps?.Requested, entry.Timestamps?.Candidate, Journal.Now());
                _runtime.Journal.Write(WithScenario(entry.WithState(ChangeSetState.Applied).WithTimestamps(stamps), AdmissionScenario, ScenarioStatus.Pass, "live catalog " + live + " == predicted; " + checkDetail));
            }

            DeletePending(changeSetId);
            AdmissionResult result = new AdmissionResult(changeSetId, AdmissionOutcome.Admitted, "Admitted " + package + "; live catalog-set hash equals the predicted catalogDelta.")
            {
                Package = package,
                Directory = (string?)pending["directory"],
                Verdict = verdict.Reference,
                Slot = verdict.Slot,
                Before = (string?)pending["before"],
                Live = full,
                Predicted = verdict.Predicted,
                CaptureSlot = (string?)pending["captureSlot"],
                Milliseconds = Elapsed(changeSetId, pending),
            };
            _runtime.Log.Write(StudioLogLevel.Info, "stage", result.Detail + " (" + live + ")");
            Finished?.Invoke(result);
            return result;
        }

        private AdmissionResult Rollback(string changeSetId, string reason, string detail)
        {
            JObject? pending = ReadPending(changeSetId);
            HistoryResult history = _runtime.History.RollbackInterrupted(changeSetId);
            ChangeSet? entry = _runtime.Journal.Read(changeSetId);
            if (entry != null)
            {
                _runtime.Journal.Write(WithScenario(entry, AdmissionScenario, ScenarioStatus.Fail, "rolled back (" + reason + "): " + detail));
            }

            DeletePending(changeSetId);
            Compiler.Compile("rollback " + changeSetId, compiled =>
            {
                if (!compiled.Succeeded)
                {
                    _runtime.Log.Write(StudioLogLevel.Error, "stage", "the recompile after rolling back " + changeSetId + " failed: " + compiled.Detail);
                }
            });
            AdmissionResult result = new AdmissionResult(changeSetId, AdmissionOutcome.RolledBack, detail)
            {
                Reason = reason,
                Package = (string?)pending?["package"],
                Directory = (string?)pending?["directory"],
                Before = (string?)pending?["before"],
                CaptureSlot = (string?)pending?["captureSlot"],
                Diagnostics = history.Diagnostics,
                Milliseconds = Elapsed(changeSetId, pending),
            };
            _runtime.Log.Write(StudioLogLevel.Warning, "stage", "rolled back " + changeSetId + " (" + reason + "): " + detail + (result.CaptureSlot == null ? string.Empty : "; restore " + result.CaptureSlot + " with RestoreCapture"));
            Finished?.Invoke(result);
            return result;
        }

        /// <summary>Restores the save slot captured before a rolled-back admission.</summary>
        public bool RestoreCapture(string captureSlot, out string? problem)
        {
            if (Options.Capture == null)
            {
                problem = "no capture hook is registered (AdmissionOptions.Capture)";
                return false;
            }

            return Options.Capture.TryRestore(captureSlot, out problem);
        }

        // ------------------------------------------------------------------------------------------- undo

        /// <summary>Undoes an admitted change set (the package is removed), recompiles and verifies the catalog hash returns.</summary>
        public AdmissionResult Undo(string changeSetId)
        {
            ChangeSet? entry = _runtime.Journal.Read(changeSetId);
            if (entry == null || entry.EffectiveState != ChangeSetState.Applied)
            {
                return new AdmissionResult(changeSetId, AdmissionOutcome.UndoFailed, "Change set " + changeSetId + " is not an applied admission.");
            }

            JObject admitted = ReadState("admitted.json");
            string? package = null;
            foreach (JProperty property in admitted.Properties())
            {
                if ((string?)property.Value["changeSetId"] == changeSetId)
                {
                    package = property.Name;
                }
            }

            JObject? record = package == null ? null : admitted[package] as JObject;
            if (package != null)
            {
                admitted.Remove(package);
            }

            string? expected = LiveHash(admitted, out _);
            HistoryResult history = _runtime.History.Undo(changeSetId);
            if (!history.Ok)
            {
                string detail = history.Diagnostics.Count > 0 ? history.Diagnostics[0].Message : "the journal undo failed";
                return new AdmissionResult(changeSetId, AdmissionOutcome.UndoFailed, detail) { Diagnostics = history.Diagnostics };
            }

            WriteState("admitted.json", admitted);
            JObject pending = new JObject
            {
                ["schema"] = "gamecore.studio.admission/1",
                ["changeSetId"] = changeSetId,
                ["phase"] = "undo",
                ["package"] = package,
                ["catalogType"] = (string?)record?["catalogType"],
                ["directory"] = (string?)record?["directory"],
                ["startedMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            if (expected != null)
            {
                pending["before"] = expected;
            }

            WritePending(changeSetId, pending);
            _started[changeSetId] = DateTime.UtcNow;
            AdmissionResult? immediate = null;
            Compiler.Compile("undo " + changeSetId, result => immediate = OnCompiled(changeSetId, result));
            return immediate ?? new AdmissionResult(changeSetId, AdmissionOutcome.Pending, "Removed " + package + "; verifying the catalog after the domain reload.") { Package = package, Before = expected };
        }

        private AdmissionResult VerifyUndo(string changeSetId)
        {
            JObject? pending = ReadPending(changeSetId);
            if (pending == null)
            {
                return new AdmissionResult(changeSetId, AdmissionOutcome.UndoFailed, "No undo of " + changeSetId + " is pending.");
            }

            string? catalogType = (string?)pending["catalogType"];
            string? stillLoaded = string.IsNullOrEmpty(catalogType) ? null : Catalog.MechanismFingerprint(catalogType!, out _);
            string? live = LiveHash(null, out string? problem);
            string? expected = (string?)pending["before"];
            string? directory = (string?)pending["directory"];
            bool removed = directory == null || !System.IO.Directory.Exists(FromProjectRelative(directory));
            if (stillLoaded != null || !removed)
            {
                return FinishUndo(changeSetId, false, "the package is still " + (removed ? "loaded (" + catalogType + ")" : "on disk at " + directory) + " after the undo");
            }

            if (live == null || expected == null || !string.Equals(live, expected, StringComparison.Ordinal))
            {
                return FinishUndo(changeSetId, false, "the live catalog hash " + (live ?? "(" + problem + ")") + " did not return to " + (expected ?? "(unknown)"));
            }

            AdmissionResult result = FinishUndo(changeSetId, true, "catalog hash returned to " + live);
            result.Live = live;
            result.Before = expected;
            result.Package = (string?)pending["package"];
            return result;
        }

        private AdmissionResult FinishUndo(string changeSetId, bool ok, string detail)
        {
            JObject? pending = ReadPending(changeSetId);
            ChangeSet? entry = _runtime.Journal.Read(changeSetId);
            if (entry != null)
            {
                _runtime.Journal.Write(WithScenario(entry, UndoScenario, ok ? ScenarioStatus.Pass : ScenarioStatus.Fail, detail));
            }

            DeletePending(changeSetId);
            AdmissionResult result = new AdmissionResult(changeSetId, ok ? AdmissionOutcome.Undone : AdmissionOutcome.UndoFailed, detail)
            {
                Package = (string?)pending?["package"],
                Before = (string?)pending?["before"],
                Milliseconds = Elapsed(changeSetId, pending),
            };
            Finished?.Invoke(result);
            return result;
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
            StudioPaths.WriteAllTextAtomic(PendingPath(changeSetId), Pruned(pending).ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
        }

        private void DeletePending(string changeSetId)
        {
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
            StudioPaths.WriteAllTextAtomic(Path.Combine(StateRoot, name), Pruned(value).ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
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
