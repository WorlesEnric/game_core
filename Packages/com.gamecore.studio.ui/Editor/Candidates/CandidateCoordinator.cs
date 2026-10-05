// GameCore.Studio.UI - candidate review (docs/studio/02-architecture.md s4 step 5, 03 s6/s7/s8/s9, SR-3.x). A candidate
// reaches the panel in one of two ways:
//   - adopted: P2.2's etos gateway imports its own requests' candidates (artifacts verified, ArtifactStore.Put,
//     ChangeSetEngine.Stage in Candidate mode, journaled as Candidate); the panel takes that staged change set as is;
//   - fetched: for any other gateway the panel fetches the change set and its artifacts (digest re-verified by the
//     artifact store; a mismatch is refused, never imported) and stages it itself on Preview.
// Then the user previews (ghosts; StaleContext when the catalog moved), compares, skips or rebases operations, picks a
// policy and applies (ChangeSetEngine.Apply, timed), or rejects (ChangeSetEngine.Discard with reject; the gateway's
// Reject when it has one). A change set that proposes a mechanism goes through the staging lane instead (P2.4): Stage
// asks the companion for a verdict (POST /v1/stage through the etos gateway) or records a verdict file, and Admit calls
// StageAdmission. Nothing here writes project state except through the engine and StageAdmission.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>Where a candidate is in its review.</summary>
    public enum CandidateStage
    {
        /// <summary>Fetched (artifacts retained), not staged.</summary>
        Received,
        /// <summary>Staged with previews (ghosts visible).</summary>
        Previewing,
        Applied,
        /// <summary>The apply ran and was refused or rolled back (per-op outcomes say why).</summary>
        Failed,
        Rejected,
        /// <summary>Fetching or verifying the candidate failed (diagnostics say why); nothing can be applied.</summary>
        Invalid,
    }

    /// <summary>One candidate under review.</summary>
    public sealed class CandidateEntry
    {
        private readonly List<Diagnostic> _problems = new List<Diagnostic>();

        internal CandidateEntry(string requestId, ChangeSet changeSet, string? toolCatalogRevision)
        {
            RequestId = requestId;
            ChangeSet = changeSet;
            ToolCatalogRevision = toolCatalogRevision;
            ReceivedUtc = DateTime.UtcNow;
        }

        public string RequestId { get; }

        public string Id => ChangeSet.Id;

        /// <summary>The change set under review (a policy change replaces it with a copy).</summary>
        public ChangeSet ChangeSet { get; internal set; }

        /// <summary>True when the staged change set came from the gateway's own import (adopted, not staged here).</summary>
        public bool GatewayStaged { get; internal set; }

        /// <summary>The last StageAdmission result (Admit) of a mechanism candidate.</summary>
        public AdmissionResult? Admission { get; internal set; }

        /// <summary>A stage request to the companion is in flight.</summary>
        public bool Staging { get; internal set; }

        /// <summary>The revision the candidate was planned against (candidate envelope, else the request's).</summary>
        public string? ToolCatalogRevision { get; }

        public DateTime ReceivedUtc { get; }

        public CandidateStage Stage { get; internal set; }

        public StagedChangeSet? Staged { get; internal set; }

        public ApplyReport? Report { get; internal set; }

        /// <summary>True while the "after" state (ghosts) is shown; false shows the scene as it is.</summary>
        public bool ShowingAfter { get; internal set; } = true;

        /// <summary>Fetch/verification problems and stage findings that are not per-operation.</summary>
        public IReadOnlyList<Diagnostic> Problems => _problems;

        public double? StageMilliseconds { get; internal set; }

        public double? ApplyMilliseconds { get; internal set; }

        public string? RejectReason { get; internal set; }

        public bool IsOpen => Stage == CandidateStage.Received || Stage == CandidateStage.Previewing || Stage == CandidateStage.Failed;

        public string Summary => CandidateRequirements.Summary(ChangeSet);

        public IReadOnlyList<string> Badges => CandidateRequirements.Badges(ChangeSet);

        internal void AddProblem(Diagnostic diagnostic) => _problems.Add(diagnostic);

        internal void ClearProblems() => _problems.Clear();
    }

    /// <summary>Fetches, stages, applies and rejects candidates.</summary>
    public sealed class CandidateCoordinator
    {
        /// <summary>How long a stage job may take before the panel stops waiting (B-STAGE is 360 s).</summary>
        public static readonly TimeSpan StageBudget = TimeSpan.FromSeconds(420);

        private readonly StudioRuntime _runtime;
        private readonly Func<IAgentGateway> _gateway;
        private readonly TaskLedger? _tasks;
        private readonly List<CandidateEntry> _entries = new List<CandidateEntry>();

        public CandidateCoordinator(StudioRuntime runtime, Func<IAgentGateway> gateway, TaskLedger? tasks = null)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _tasks = tasks;
        }

        public IReadOnlyList<CandidateEntry> Entries => _entries;

        /// <summary>Optional material applied to preview ghosts (Studio settings); null keeps the engine's ghost material.</summary>
        public Func<Material?>? GhostMaterial { get; set; }

        public event Action? Changed;

        public CandidateEntry? Find(string changeSetOrRequestId)
        {
            foreach (CandidateEntry entry in _entries)
            {
                if (entry.Id == changeSetOrRequestId || entry.RequestId == changeSetOrRequestId)
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>Open candidates (received, previewing or failed), newest first.</summary>
        public IReadOnlyList<CandidateEntry> Open()
        {
            List<CandidateEntry> open = new List<CandidateEntry>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].IsOpen)
                {
                    open.Add(_entries[i]);
                }
            }

            return open;
        }

        /// <summary>
        /// Fetches a request's candidate and retains its artifacts. An existing entry is returned as is (a candidate is
        /// fetched once). Failures become an Invalid entry with the diagnostic; nothing is fabricated. The tool catalog
        /// revision the candidate was planned against is <paramref name="toolCatalogRevision"/> when known, else the request's.
        /// </summary>
        public async Task<CandidateEntry> Receive(string requestId, string? changeSetIdHint = null, string? toolCatalogRevision = null)
        {
            CandidateEntry? existing = Find(requestId) ?? (changeSetIdHint != null ? Find(changeSetIdHint) : null);
            if (existing != null)
            {
                return existing;
            }

            IAgentGateway gateway = _gateway();
            ChangeSet changeSet;
            try
            {
                changeSet = await gateway.FetchCandidateAsync(requestId, CancellationToken.None);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Diagnostic diagnostic = GatewayErrors.ToDiagnostic(error);
                return AddInvalid(requestId, changeSetIdHint, new[] { diagnostic.Code == "transport" ? new Diagnostic(DiagnosticCodes.CandidateInvalid, "The candidate could not be fetched: " + diagnostic.Message) : diagnostic });
            }

            existing = Find(changeSet.Id);
            if (existing != null)
            {
                return existing;
            }

            List<Diagnostic> problems = new List<Diagnostic>();
            foreach (ArtifactRef artifact in changeSet.Artifacts ?? Array.Empty<ArtifactRef>())
            {
                if (_runtime.Artifacts.Has(artifact.Sha256))
                {
                    continue;
                }

                try
                {
                    byte[] bytes = await gateway.FetchArtifactAsync(artifact.Sha256, CancellationToken.None);
                    _runtime.Artifacts.Put(bytes, artifact);
                }
                catch (ArtifactStoreException error)
                {
                    problems.Add(new Diagnostic(DiagnosticCodes.CandidateInvalid, "Artifact sha256:" + artifact.Sha256 + " was refused: " + error.Message, "The bytes do not match the candidate's digest; the artifact is not imported."));
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    Diagnostic diagnostic = GatewayErrors.ToDiagnostic(error);
                    problems.Add(diagnostic.Code == "transport" ? new Diagnostic(DiagnosticCodes.CandidateInvalid, "Artifact sha256:" + artifact.Sha256 + " could not be fetched: " + diagnostic.Message) : diagnostic);
                }
            }

            CandidateEntry entry = Add(requestId, changeSet, toolCatalogRevision);
            foreach (Diagnostic problem in problems)
            {
                entry.AddProblem(problem);
            }

            if (problems.Count > 0)
            {
                entry.Stage = CandidateStage.Invalid;
            }

            Changed?.Invoke();
            return entry;
        }

        /// <summary>Adds a candidate whose artifacts are already retained (tests, recovery).</summary>
        public CandidateEntry Add(string requestId, ChangeSet changeSet, string? toolCatalogRevision = null)
        {
            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            CandidateEntry? existing = Find(changeSet.Id);
            if (existing != null)
            {
                return existing;
            }

            CandidateEntry entry = new CandidateEntry(requestId, changeSet, toolCatalogRevision ?? RevisionOf(requestId, changeSet.Id));
            _entries.Add(entry);
            Changed?.Invoke();
            return entry;
        }

        /// <summary>
        /// Takes a candidate the gateway imported and staged itself (P2.2 AutoImport): the staged change set (with its
        /// previews) becomes the entry's; a refused stage makes the entry Invalid with the stage findings.
        /// </summary>
        public CandidateEntry Adopt(string requestId, StagedChangeSet staged)
        {
            if (staged == null)
            {
                throw new ArgumentNullException(nameof(staged));
            }

            CandidateEntry? entry = Find(staged.Id) ?? Find(requestId);
            if (entry != null && (!entry.IsOpen || ReferenceEquals(entry.Staged, staged)))
            {
                return entry;
            }

            if (entry == null)
            {
                entry = new CandidateEntry(requestId, staged.ChangeSet, RevisionOf(requestId, staged.Id));
                _entries.Add(entry);
            }
            else
            {
                DropStage(entry);
                entry.ChangeSet = staged.ChangeSet;
            }

            entry.Staged = staged;
            entry.GatewayStaged = true;
            entry.ShowingAfter = true;
            entry.ClearProblems();
            if (staged.Ok)
            {
                entry.Stage = CandidateStage.Previewing;
                ApplyGhostMaterial(entry);
            }
            else
            {
                entry.Stage = CandidateStage.Invalid;
                foreach (Diagnostic diagnostic in staged.AllDiagnostics)
                {
                    entry.AddProblem(diagnostic);
                }
            }

            Changed?.Invoke();
            return entry;
        }

        /// <summary>Stages with previews (ghosts) in Candidate mode; returns the staged change set.</summary>
        public StagedChangeSet Preview(CandidateEntry entry)
        {
            RequireOpen(entry);
            if (entry.GatewayStaged && entry.Staged != null && !entry.Staged.Consumed)
            {
                entry.Stage = CandidateStage.Previewing;
                ShowAfter(entry, true);
                return entry.Staged;
            }

            DropStage(entry);
            entry.Staged = StageCore(entry, true);
            entry.Stage = CandidateStage.Previewing;
            entry.ShowingAfter = true;
            ApplyGhostMaterial(entry);
            Changed?.Invoke();
            return entry.Staged;
        }

        /// <summary>Shows the ghosts (after) or hides them (before).</summary>
        public void ShowAfter(CandidateEntry entry, bool after)
        {
            entry.ShowingAfter = after;
            foreach (GameObject ghost in _runtime.Staging.GhostsOf(entry.Id))
            {
                if (ghost != null)
                {
                    ghost.SetActive(after);
                }
            }

            Changed?.Invoke();
        }

        /// <summary>Drops operations (and their dependants) and re-stages.</summary>
        public StagedChangeSet Skip(CandidateEntry entry, IEnumerable<string> opIds)
        {
            RequireOpen(entry);
            StagedChangeSet staged = entry.Staged != null && !entry.Staged.Consumed ? entry.Staged : StageCore(entry, true);
            entry.Staged = _runtime.Engine.Skip(staged, opIds);
            entry.ChangeSet = entry.Staged.ChangeSet;
            entry.Stage = CandidateStage.Previewing;
            ApplyGhostMaterial(entry);
            Changed?.Invoke();
            return entry.Staged;
        }

        /// <summary>Re-plans conflicting operations against the current stamps and re-stages (03 s7 rebase).</summary>
        public StagedChangeSet Rebase(CandidateEntry entry, IEnumerable<string>? opIds = null)
        {
            RequireOpen(entry);
            StagedChangeSet staged = entry.Staged != null && !entry.Staged.Consumed ? entry.Staged : StageCore(entry, true);
            entry.Staged = _runtime.Engine.Rebase(staged, opIds);
            entry.ChangeSet = entry.Staged.ChangeSet;
            entry.Stage = CandidateStage.Previewing;
            ApplyGhostMaterial(entry);
            Changed?.Invoke();
            return entry.Staged;
        }

        /// <summary>Changes the partial-failure policy (re-stages when staged).</summary>
        public void SetPolicy(CandidateEntry entry, ApplyPolicy policy)
        {
            RequireOpen(entry);
            if (entry.ChangeSet.EffectivePolicy == policy && entry.ChangeSet.Policy.HasValue)
            {
                return;
            }

            bool wasPreviewing = entry.Staged != null && !entry.Staged.Consumed;
            DropStage(entry);
            entry.ChangeSet = CandidateRequirements.WithPolicy(entry.ChangeSet, policy);
            if (wasPreviewing)
            {
                entry.Staged = StageCore(entry, true);
                ApplyGhostMaterial(entry);
            }

            Changed?.Invoke();
        }

        /// <summary>Applies (staging first when needed); the duration is measured and kept on the entry.</summary>
        public ApplyReport Apply(CandidateEntry entry)
        {
            RequireOpen(entry);
            if (CandidateStaging.Requires(_runtime, entry.ChangeSet))
            {
                throw new InvalidOperationException("Candidate " + entry.Id + " proposes a mechanism; it reaches the editor only through Stage and Admit.");
            }

            StagedChangeSet staged = entry.Staged != null && !entry.Staged.Consumed ? entry.Staged : StageCore(entry, false);
            Stopwatch watch = Stopwatch.StartNew();
            ApplyReport report = _runtime.Engine.Apply(staged);
            watch.Stop();
            entry.ApplyMilliseconds = watch.Elapsed.TotalMilliseconds;
            entry.Report = report;
            entry.Staged = null;
            entry.Stage = report.Ok ? CandidateStage.Applied : CandidateStage.Failed;
            Changed?.Invoke();
            return report;
        }

        /// <summary>
        /// Rejects: previews dropped, a journaled candidate becomes Rejected, and the gateway is told when it has a
        /// Reject (P2.2 keeps the reason with the request and in its log; the companion has no route for it).
        /// </summary>
        public void Reject(CandidateEntry entry, string reason)
        {
            if (entry.Stage == CandidateStage.Applied || entry.Stage == CandidateStage.Rejected)
            {
                throw new InvalidOperationException("Candidate " + entry.Id + " is already " + entry.Stage + ".");
            }

            string text = string.IsNullOrWhiteSpace(reason) ? "Rejected in the Studio candidate panel." : reason.Trim();
            if (entry.Staged != null && !entry.Staged.Consumed)
            {
                _runtime.Engine.Discard(entry.Staged, true);
            }
            else if (entry.Stage != CandidateStage.Invalid && _runtime.Journal.Exists(entry.Id))
            {
                _runtime.Engine.Discard(StageCore(entry, false), true);
            }

            entry.Staged = null;
            entry.Stage = CandidateStage.Rejected;
            entry.RejectReason = text;
            GatewayExtras.TryReject(_gateway(), entry.RequestId, text);
            Changed?.Invoke();
        }

        /// <summary>The staging state (journal validation) of a candidate.</summary>
        public StageState StageStateOf(CandidateEntry entry) => CandidateStaging.StateOf(_runtime, entry.Id, entry.ChangeSet);

        /// <summary>True when the gateway exposes the companion's staging lane.</summary>
        public bool CanRequestStage => GatewayExtras.HasStageLane(_gateway());

        /// <summary>
        /// Asks the companion to stage the candidate (POST /v1/stage with its package artifact), waits for the job and
        /// records the verdict (StageAdmission.RecordVerdict: retained, journal stage.verdict pass|fail). A failed job
        /// marks stage.verdict fail with its reason. Returns the diagnostic of a failure, or null.
        /// </summary>
        public async Task<Diagnostic?> RequestStage(CandidateEntry entry, TimeSpan? poll = null)
        {
            string? packageRef = CandidateStaging.PackageRef(entry.ChangeSet);
            if (packageRef == null)
            {
                return Problem(entry, new Diagnostic(DiagnosticCodes.StageFailed, "The candidate carries no package artifact to stage."));
            }

            IAgentGateway gateway = _gateway();
            StageAdmission admission = StageAdmission.Of(_runtime);
            admission.MarkStagePending(entry.Id, null);
            entry.Staging = true;
            Changed?.Invoke();
            try
            {
                JObject job = await GatewayExtras.StageAsync(gateway, entry.Id, packageRef, poll ?? TimeSpan.FromSeconds(3), StageBudget, CancellationToken.None);
                string? verdictRef = CandidateStaging.VerdictRef(job);
                if (verdictRef != null)
                {
                    byte[] bytes = await gateway.FetchArtifactAsync(CandidateStaging.Digest(verdictRef), CancellationToken.None);
                    admission.RecordVerdict(bytes);
                    return null;
                }

                Diagnostic failure = CandidateStaging.FailureOf(job) ?? new Diagnostic(DiagnosticCodes.StageFailed, "The stage job ended without a verdict (state " + job["state"] + ").");
                CandidateStaging.MarkVerdict(_runtime, entry.Id, ScenarioStatus.Fail, failure.Message);
                return Problem(entry, failure);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Diagnostic failure = error is ArgumentException ? new Diagnostic(DiagnosticCodes.StageFailed, error.Message) : GatewayErrors.ToDiagnostic(error);
                CandidateStaging.MarkVerdict(_runtime, entry.Id, ScenarioStatus.Fail, failure.Code + ": " + failure.Message);
                return Problem(entry, failure);
            }
            finally
            {
                entry.Staging = false;
                Changed?.Invoke();
            }
        }

        /// <summary>Records a verdict file (an operator's <c>gamecore-studio stage run --verdict-out</c>) for the candidate.</summary>
        public StageVerdict RecordVerdictFile(CandidateEntry entry, string path)
        {
            StageVerdict verdict = StageAdmission.Of(_runtime).RecordVerdict(File.ReadAllBytes(path));
            if (verdict.ChangeSetId != entry.Id)
            {
                entry.AddProblem(new Diagnostic(DiagnosticCodes.StageFailed, "The verdict is for " + verdict.ChangeSetId + ", not " + entry.Id + "."));
            }

            Changed?.Invoke();
            return verdict;
        }

        /// <summary>
        /// Admits a staged mechanism candidate through StageAdmission (the creator's explicit Admit). Previews are
        /// dropped first; the result is kept on the entry (Pending continues after the domain reload by itself).
        /// </summary>
        public AdmissionResult Admit(CandidateEntry entry, bool captureAndStop)
        {
            RequireOpen(entry);
            if (!StageStateOf(entry).VerdictPassed)
            {
                throw new InvalidOperationException("Candidate " + entry.Id + " has no passing stage verdict; stage it first.");
            }

            DropStage(entry);
            Stopwatch watch = Stopwatch.StartNew();
            AdmissionResult result = StageAdmission.Of(_runtime).Admit(entry.ChangeSet, null, captureAndStop);
            watch.Stop();
            entry.ApplyMilliseconds = watch.Elapsed.TotalMilliseconds;
            entry.Admission = result;
            switch (result.Outcome)
            {
                case AdmissionOutcome.Admitted:
                    entry.Stage = CandidateStage.Applied;
                    break;
                case AdmissionOutcome.Pending:
                    break;
                default:
                    entry.Stage = CandidateStage.Failed;
                    foreach (Diagnostic diagnostic in result.Diagnostics)
                    {
                        entry.AddProblem(diagnostic);
                    }

                    break;
            }

            Changed?.Invoke();
            return result;
        }

        private Diagnostic Problem(CandidateEntry entry, Diagnostic diagnostic)
        {
            entry.AddProblem(diagnostic);
            Changed?.Invoke();
            return diagnostic;
        }

        private string? RevisionOf(string requestId, string changeSetId)
        {
            TaskRow? row = _tasks?.Find(changeSetId) ?? _tasks?.Find(requestId);
            return row != null && row.toolCatalogRevision.Length > 0 ? row.toolCatalogRevision : null;
        }

        private StagedChangeSet StageCore(CandidateEntry entry, bool previews)
        {
            Stopwatch watch = Stopwatch.StartNew();
            StagedChangeSet staged = _runtime.Engine.Stage(entry.ChangeSet, new StageOptions
            {
                Mode = ValidationMode.Candidate,
                ToolCatalogRevision = entry.ToolCatalogRevision,
                IndexIsSlice = true,
                Previews = previews,
            });
            watch.Stop();
            entry.StageMilliseconds = watch.Elapsed.TotalMilliseconds;
            return staged;
        }

        private void DropStage(CandidateEntry entry)
        {
            if (entry.Staged != null && !entry.Staged.Consumed)
            {
                _runtime.Engine.Discard(entry.Staged, false);
            }

            entry.Staged = null;
        }

        private void ApplyGhostMaterial(CandidateEntry entry)
        {
            Material? material = GhostMaterial?.Invoke();
            if (material == null)
            {
                return;
            }

            foreach (GameObject ghost in _runtime.Staging.GhostsOf(entry.Id))
            {
                if (ghost == null)
                {
                    continue;
                }

                foreach (Renderer renderer in ghost.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        materials[i] = material;
                    }

                    renderer.sharedMaterials = materials;
                }
            }
        }

        /// <summary>A candidate that could not be fetched or imported: an Invalid entry carrying the diagnostics (codes preserved).</summary>
        public CandidateEntry AddInvalid(string requestId, string? changeSetId, IEnumerable<Diagnostic> diagnostics)
        {
            CandidateEntry? existing = (changeSetId != null ? Find(changeSetId) : null) ?? Find(requestId);
            if (existing != null && existing.Stage == CandidateStage.Invalid)
            {
                return existing;
            }

            string id = changeSetId != null && IdDerivation.IsChangeSetId(changeSetId) ? changeSetId : IdDerivation.NewChangeSetId();
            Operation placeholder = new Operation("op1", BuiltInToolIds.InspectDescribe);
            ChangeSet empty = new ChangeSet(id, ChangeSet.SchemaId, new Intent("candidate of " + requestId + " (not imported)", IntentOrigin.Agent), new[] { placeholder });
            CandidateEntry entry = new CandidateEntry(requestId, empty, null) { Stage = CandidateStage.Invalid };
            foreach (Diagnostic diagnostic in diagnostics)
            {
                entry.AddProblem(diagnostic);
            }

            _entries.Add(entry);
            Changed?.Invoke();
            return entry;
        }

        private static void RequireOpen(CandidateEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (!entry.IsOpen)
            {
                throw new InvalidOperationException("Candidate " + entry.Id + " is " + entry.Stage + "; it cannot be staged or applied.");
            }
        }
    }
}
