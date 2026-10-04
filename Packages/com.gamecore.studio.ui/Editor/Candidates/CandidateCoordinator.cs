// GameCore.Studio.UI - candidate review (docs/studio/02-architecture.md s4 step 5, 03 s6/s7/s9, SR-3.x): a candidate
// change set arrives from the gateway, its artifacts are fetched and retained (digest re-verified by the artifact store;
// a mismatch is refused, never imported), then the user previews (ChangeSetEngine.Stage in Candidate mode with the
// candidate's tool catalog revision: StaleContext when the catalog moved), compares, skips or rebases operations, picks
// a policy and applies (ChangeSetEngine.Apply, timed), or rejects (ChangeSetEngine.Discard with reject, plus the reason
// sent back through the gateway). Nothing here writes project state except through the engine.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
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

        internal CandidateEntry(string requestId, ChangeSet changeSet, AgentCandidate? source, string? toolCatalogRevision)
        {
            RequestId = requestId;
            ChangeSet = changeSet;
            Source = source;
            ToolCatalogRevision = toolCatalogRevision;
            ReceivedUtc = DateTime.UtcNow;
        }

        public string RequestId { get; }

        public string Id => ChangeSet.Id;

        /// <summary>The change set under review (a policy change replaces it with a copy).</summary>
        public ChangeSet ChangeSet { get; internal set; }

        public AgentCandidate? Source { get; }

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
        private readonly StudioRuntime _runtime;
        private readonly Func<IStudioAgentGateway> _gateway;
        private readonly TaskLedger? _tasks;
        private readonly List<CandidateEntry> _entries = new List<CandidateEntry>();

        public CandidateCoordinator(StudioRuntime runtime, Func<IStudioAgentGateway> gateway, TaskLedger? tasks = null)
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
        /// fetched once). Failures become an Invalid entry with the diagnostic; nothing is fabricated.
        /// </summary>
        public async Task<CandidateEntry> Receive(string requestId, string? changeSetIdHint = null)
        {
            CandidateEntry? existing = Find(requestId) ?? (changeSetIdHint != null ? Find(changeSetIdHint) : null);
            if (existing != null)
            {
                return existing;
            }

            IStudioAgentGateway gateway = _gateway();
            AgentCandidate candidate;
            try
            {
                candidate = await gateway.FetchCandidate(requestId);
            }
            catch (AgentGatewayException error)
            {
                return AddInvalid(requestId, changeSetIdHint, error.Diagnostic);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                return AddInvalid(requestId, changeSetIdHint, new Diagnostic(DiagnosticCodes.CandidateInvalid, "The candidate could not be fetched: " + error.Message));
            }

            existing = Find(candidate.ChangeSet.Id);
            if (existing != null)
            {
                return existing;
            }

            List<Diagnostic> problems = new List<Diagnostic>();
            foreach (ArtifactRef artifact in candidate.Artifacts)
            {
                if (_runtime.Artifacts.Has(artifact.Sha256))
                {
                    continue;
                }

                try
                {
                    byte[] bytes = await gateway.FetchArtifact(artifact.Sha256);
                    _runtime.Artifacts.Put(bytes, artifact);
                }
                catch (ArtifactStoreException error)
                {
                    problems.Add(new Diagnostic(DiagnosticCodes.CandidateInvalid, "Artifact sha256:" + artifact.Sha256 + " was refused: " + error.Message, "The bytes do not match the candidate's digest; the artifact is not imported."));
                }
                catch (AgentGatewayException error)
                {
                    problems.Add(error.Diagnostic);
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    problems.Add(new Diagnostic(DiagnosticCodes.CandidateInvalid, "Artifact sha256:" + artifact.Sha256 + " could not be fetched: " + error.Message));
                }
            }

            CandidateEntry entry = Add(candidate);
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
        public CandidateEntry Add(AgentCandidate candidate)
        {
            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            CandidateEntry? existing = Find(candidate.ChangeSet.Id);
            if (existing != null)
            {
                return existing;
            }

            string? revision = candidate.ToolCatalogRevision;
            if (revision == null && _tasks != null)
            {
                TaskRow? row = _tasks.Find(candidate.ChangeSet.Id) ?? _tasks.Find(candidate.RequestId);
                if (row != null && row.toolCatalogRevision.Length > 0)
                {
                    revision = row.toolCatalogRevision;
                }
            }

            CandidateEntry entry = new CandidateEntry(candidate.RequestId, candidate.ChangeSet, candidate, revision);
            foreach (Diagnostic warning in candidate.Warnings)
            {
                entry.AddProblem(warning);
            }

            _entries.Add(entry);
            Changed?.Invoke();
            return entry;
        }

        /// <summary>Stages with previews (ghosts) in Candidate mode; returns the staged change set.</summary>
        public StagedChangeSet Preview(CandidateEntry entry)
        {
            RequireOpen(entry);
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

        /// <summary>Rejects: previews dropped, a journaled candidate becomes Rejected, the reason goes back through the gateway.</summary>
        public Task Reject(CandidateEntry entry, string reason)
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
            Changed?.Invoke();
            return _gateway().RejectCandidate(entry.Id, text);
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

        private CandidateEntry AddInvalid(string requestId, string? changeSetId, Diagnostic diagnostic)
        {
            string id = changeSetId != null && IdDerivation.IsChangeSetId(changeSetId) ? changeSetId : IdDerivation.NewChangeSetId();
            Operation placeholder = new Operation("op1", BuiltInToolIds.InspectDescribe);
            ChangeSet empty = new ChangeSet(id, ChangeSet.SchemaId, new Intent("candidate of " + requestId + " (not fetched)", IntentOrigin.Agent), new[] { placeholder });
            CandidateEntry entry = new CandidateEntry(requestId, empty, null, null) { Stage = CandidateStage.Invalid };
            entry.AddProblem(diagnostic);
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
