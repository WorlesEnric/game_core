// GameCore.Studio.UI - the candidate panel (GameCore/Studio/Candidates) and the compact strip inside the viewport
// (SR-3.2/3.5/1.8 from the review side): per candidate the summary (ops by tool, targets, requirement badges), Preview
// (staged with ghosts, before/after toggle, per-op list with stale/conflict findings and skip/rebase controls), Compare
// (property diff, image before/after/diff, audio playback), Apply (policy picker, what happens in Play vs Edit, timed,
// per-op outcomes) and Reject (reason kept by the gateway). A change set that proposes a mechanism shows its staging
// state instead of Apply (journal `validation`: verdict pending/pass/fail, admission, undo) with Stage (the companion's
// staging lane through IStageService), Refresh verdict (authenticated after reload) and Admit
// (StageAdmission, enabled only on a passing verdict). All actions go through CandidateCoordinator, which only calls
// the edit engine and StageAdmission.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>The full candidate panel.</summary>
    public sealed class CandidatePanelView : VisualElement
    {
        private readonly StudioUiContext _context;
        private readonly ScrollView _list;
        private readonly ScrollView _details;
        private readonly EditorAudioPlayer _audio = new EditorAudioPlayer();
        private readonly List<UnityEngine.Object> _ownedTextures = new List<UnityEngine.Object>();
        private string? _selected;
        private bool _attached;

        public CandidatePanelView(StudioUiContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            name = "candidate-panel";
            AddToClassList("gcs-candidates");
            VisualElement body = new VisualElement();
            body.AddToClassList("gcs-split");
            Add(body);
            _list = new ScrollView { name = "candidate-list" };
            _list.AddToClassList("gcs-split__list");
            body.Add(_list);
            _details = new ScrollView { name = "candidate-details" };
            _details.AddToClassList("gcs-split__details");
            body.Add(_details);
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
            Rebuild();
        }

        public string? SelectedId => _selected;

        /// <summary>Message of the last action (apply time, refusal, ...).</summary>
        public string LastMessage { get; private set; } = string.Empty;

        public void Select(string changeSetId)
        {
            _selected = changeSetId;
            Rebuild();
        }

        public void Rebuild()
        {
            ReleaseTextures();
            _list.Clear();
            IReadOnlyList<CandidateEntry> entries = _context.Candidates.Entries;
            if (_selected == null && entries.Count > 0)
            {
                _selected = entries[entries.Count - 1].Id;
            }

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                CandidateEntry entry = entries[i];
                VisualElement row = new VisualElement { name = "candidate-" + entry.Id, focusable = true };
                row.AddToClassList("gcs-tray__row");
                row.EnableInClassList("gcs-tray__row--selected", entry.Id == _selected);
                row.Add(StudioStyles.Badge(entry.Stage.ToString(), entry.Stage.ToString().ToLowerInvariant()));
                if (CandidateRequirements.NeedsStageVerdict(entry.ChangeSet))
                {
                    StageState stage = _context.Candidates.StageStateOf(entry);
                    row.Add(StudioStyles.Badge(stage.Label, VerdictModifier(stage)));
                }

                Label summary = new Label(StudioStyles.Safe(entry.ChangeSet.Intent.Text + " - " + entry.Summary)) { tooltip = StudioStyles.Safe(entry.Id) };
                summary.AddToClassList("gcs-tray__intent");
                row.Add(summary);
                string id = entry.Id;
                row.RegisterCallback<PointerDownEvent>(_ => Select(id));
                row.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                    {
                        Select(id);
                    }
                });
                _list.Add(row);
            }

            if (entries.Count == 0)
            {
                _list.Add(StudioStyles.Text("No candidates. Agent candidates appear here when a request finishes.", "gcs-muted"));
            }

            BuildDetails();
        }

        private void BuildDetails()
        {
            _details.Clear();
            CandidateEntry? entry = _selected == null ? null : _context.Candidates.Find(_selected);
            if (entry == null)
            {
                return;
            }

            ChangeSet changeSet = entry.ChangeSet;
            _details.Add(StudioStyles.Header("Candidate " + entry.Id));
            _details.Add(StudioStyles.Text(changeSet.Intent.Text + " (" + changeSet.Intent.Origin + ")"));
            _details.Add(StudioStyles.Text(entry.Summary + " - stage " + entry.Stage + (entry.StageMilliseconds.HasValue ? " - staged in " + Ms(entry.StageMilliseconds.Value) : string.Empty)));
            VisualElement badges = new VisualElement { name = "requirement-badges" };
            badges.AddToClassList("gcs-row");
            foreach (string badge in entry.Badges)
            {
                badges.Add(StudioStyles.Badge(badge, badge == CandidateRequirements.RequiresStageVerdict ? "warn" : null));
            }

            if (CandidateRequirements.NeedsStageVerdict(changeSet))
            {
                badges.Add(StudioStyles.Badge("verdict pending", "warn"));
            }

            _details.Add(badges);
            _details.Add(StudioStyles.Text("Targets: " + Targets(changeSet)));
            foreach (Diagnostic problem in entry.Problems)
            {
                _details.Add(StudioStyles.Text(problem.Code + ": " + problem.Message, "gcs-diagnostic"));
            }

            // ---------------------------------------------------------------- preview
            _details.Add(StudioStyles.Header("Preview"));
            VisualElement previewRow = new VisualElement();
            previewRow.AddToClassList("gcs-row");
            Button preview = new Button(() => Run(() => _context.Candidates.Preview(entry), "Staged with previews.")) { name = "candidate-preview", text = StudioStyles.Safe(entry.Stage == CandidateStage.Previewing ? "Re-stage" : "Preview") };
            preview.SetEnabled(entry.IsOpen);
            previewRow.Add(preview);
            Toggle after = new Toggle("Show after") { name = "candidate-after", value = entry.ShowingAfter };
            after.SetEnabled(entry.Stage == CandidateStage.Previewing);
            after.RegisterValueChangedCallback(change => _context.Candidates.ShowAfter(entry, change.newValue));
            previewRow.Add(after);
            _details.Add(previewRow);
            StagedChangeSet? staged = entry.Staged;
            if (staged != null)
            {
                foreach (Diagnostic diagnostic in staged.Diagnostics)
                {
                    _details.Add(StudioStyles.Text(diagnostic.Code + ": " + diagnostic.Message + (diagnostic.Hint != null ? " (" + diagnostic.Hint + ")" : string.Empty), "gcs-diagnostic"));
                }

                if (staged.Conflicts.Count > 0)
                {
                    _details.Add(new Button(() => Run(() => _context.Candidates.Rebase(entry), "Rebased conflicting operations.")) { name = "candidate-rebase", text = StudioStyles.Safe("Rebase " + staged.Conflicts.Count + " conflicting op(s)") });
                }
            }

            foreach (Operation operation in changeSet.Operations)
            {
                _details.Add(BuildOperationRow(entry, operation, staged?.Find(operation.OpId)));
            }

            // ---------------------------------------------------------------- compare
            _details.Add(StudioStyles.Header("Compare"));
            IReadOnlyList<PropertyDiffRow> diff = CandidateCompare.PropertyDiff(_context.Runtime, changeSet);
            if (diff.Count > 0)
            {
                VisualElement table = new VisualElement { name = "property-diff" };
                table.AddToClassList("gcs-table");
                table.Add(DiffRow("op", "target.field", "current", "proposed", true, false));
                foreach (PropertyDiffRow row in diff)
                {
                    table.Add(DiffRow(row.OpId, row.Target + "." + row.Field, row.Current, row.Proposed, false, row.Changed));
                }

                _details.Add(table);
            }
            else
            {
                _details.Add(StudioStyles.Text("No set/assign operations to diff.", "gcs-muted"));
            }

            foreach (ArtifactRef image in CandidateCompare.Images(changeSet))
            {
                _details.Add(BuildImageCompare(changeSet, image));
            }

            foreach (ArtifactRef clip in CandidateCompare.Audio(changeSet))
            {
                _details.Add(BuildAudioRow(clip));
            }

            // ---------------------------------------------------------------- staging lane
            if (CandidateRequirements.NeedsStageVerdict(changeSet))
            {
                BuildStaging(entry);
            }

            // ---------------------------------------------------------------- apply
            _details.Add(StudioStyles.Header("Apply"));
            List<string> policies = new List<string> { ApplyPolicy.AllOrNothing.ToString(), ApplyPolicy.BestEffort.ToString() };
            PopupField<string> policy = new PopupField<string>("Policy", policies, changeSet.EffectivePolicy == ApplyPolicy.BestEffort ? 1 : 0) { name = "candidate-policy" };
            policy.SetEnabled(entry.IsOpen);
            policy.RegisterValueChangedCallback(change => Run(() => _context.Candidates.SetPolicy(entry, change.newValue == "BestEffort" ? ApplyPolicy.BestEffort : ApplyPolicy.AllOrNothing), "Policy set to " + change.newValue + "."));
            _details.Add(policy);
            _details.Add(StudioStyles.Text(CandidateRequirements.Explain(changeSet, EditorApplication.isPlaying), "gcs-muted"));
            VisualElement applyRow = new VisualElement();
            applyRow.AddToClassList("gcs-row");
            Button apply = new Button(() => Run(() =>
            {
                ApplyReport report = _context.Candidates.Apply(entry);
                LastMessage = "Apply " + report.State + " in " + Ms(entry.ApplyMilliseconds ?? report.Milliseconds) + (report.Diagnostics.Count > 0 ? " - " + report.Diagnostics[0].Code + ": " + report.Diagnostics[0].Message : string.Empty);
            }, null))
            { name = "candidate-apply", text = "Apply" };
            apply.AddToClassList("gcs-primary");
            apply.SetEnabled(entry.IsOpen && !CandidateRequirements.NeedsStageVerdict(changeSet));
            applyRow.Add(apply);
            TextField reason = new TextField { name = "candidate-reject-reason" };
            reason.AddToClassList("gcs-grow");
            reason.tooltip = "Why you reject it (recorded locally)";
            applyRow.Add(reason);
            Button reject = new Button(() => Run(() => _context.Candidates.Reject(entry, reason.value), "Rejected.")) { name = "candidate-reject", text = "Reject" };
            reject.SetEnabled(entry.Stage != CandidateStage.Applied && entry.Stage != CandidateStage.Rejected);
            applyRow.Add(reject);
            _details.Add(applyRow);
            if (entry.ApplyMilliseconds.HasValue)
            {
                _details.Add(StudioStyles.Text("Last apply took " + Ms(entry.ApplyMilliseconds.Value) + " on the main thread.", "gcs-muted"));
            }

            if (entry.Report != null)
            {
                foreach (OperationOutcome outcome in entry.Report.Outcomes)
                {
                    _details.Add(StudioStyles.Text(outcome.OpId + ": " + outcome.Status + (outcome.Code != null ? " " + outcome.Code : string.Empty) + (outcome.Detail != null ? " - " + outcome.Detail : string.Empty), outcome.Status == OutcomeStatus.Applied ? null : "gcs-diagnostic"));
                }

                foreach (Diagnostic diagnostic in entry.Report.Diagnostics)
                {
                    _details.Add(StudioStyles.Text(diagnostic.Code + ": " + diagnostic.Message, "gcs-diagnostic"));
                }
            }

            if (entry.RejectReason != null)
            {
                _details.Add(StudioStyles.Text("Rejected: " + entry.RejectReason, "gcs-muted"));
            }

            if (LastMessage.Length > 0)
            {
                _details.Add(StudioStyles.Text(LastMessage, "gcs-status"));
            }
        }

        /// <summary>The staging-lane block: journal progress, authenticated verdict details and explicit Admit.</summary>
        private void BuildStaging(CandidateEntry entry)
        {
            _details.Add(StudioStyles.Header("Staging lane"));
            StageState stage = _context.Candidates.StageStateOf(entry);
            VisualElement states = new VisualElement { name = "candidate-stage-state" };
            states.AddToClassList("gcs-row");
            states.Add(StudioStyles.Badge(stage.Label, VerdictModifier(stage)));
            if (entry.Staging)
            {
                states.Add(StudioStyles.Badge("staging...", "pending"));
            }

            _details.Add(states);
            foreach (ValidationScenario? scenario in new[] { stage.Verdict, stage.Admission, stage.Undo })
            {
                if (scenario?.Detail != null)
                {
                    _details.Add(StudioStyles.Text(scenario.Scenario + ": " + scenario.Detail, scenario.Status == ScenarioStatus.Fail ? "gcs-diagnostic" : "gcs-muted"));
                }
            }

            if (entry.StageJobId != null) _details.Add(StudioStyles.Text("Job: " + entry.StageJobId));
            StageVerdict? verdict = entry.VerifiedVerdict;
            if (verdict != null)
            {
                _details.Add(StudioStyles.Text("Verified verdict: " + verdict.Reference));
                _details.Add(StudioStyles.Text("Confinement: " + verdict.Confinement + "; coldCache: " + verdict.ColdCache));
                if (verdict.Confinement == "host")
                    _details.Add(StudioStyles.Text("Warning: host confinement runs without OS sandbox isolation. Companion operator opt-in is required.", "gcs-diagnostic"));
                foreach (StageVerdictStep step in verdict.Steps)
                    _details.Add(StudioStyles.Text(step.Id + ": " + step.Status + " (" + step.DurationMs + " ms) " + step.Detail));
                _details.Add(StudioStyles.Text("Forbidden hits: " + verdict.ForbiddenHits));
                foreach (Newtonsoft.Json.Linq.JToken hit in verdict.Document["forbiddenHits"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray())
                    _details.Add(StudioStyles.Text(hit.ToString(), "gcs-diagnostic"));
            }

            _details.Add(StudioStyles.Text("Generated code reaches the editor only with a passing verdict for exactly these artifacts and an explicit Admit (compile, checkers, catalog check; rolled back on any failure).", "gcs-muted"));
            VisualElement row = new VisualElement();
            row.AddToClassList("gcs-row");
            bool canStage = _context.Candidates.CanRequestStage;
            Button stageButton = new Button(() => _ = StageAsync(entry)) { name = "candidate-stage", text = "Stage" };
            stageButton.tooltip = StudioStyles.Safe(canStage ? "Stage in an isolated slot project (POST /v1/stage); the verdict is recorded in the journal." : "The registered gateway has no staging lane; configure the authenticated companion stage service.");
            stageButton.SetEnabled(canStage && entry.IsOpen && !entry.Staging);
            row.Add(stageButton);
            Button refresh = new Button(() => _ = RefreshStageAsync(entry)) { name = "candidate-refresh-verdict", text = "Refresh verdict" };
            refresh.SetEnabled(canStage && entry.IsOpen && !entry.Staging && entry.StageJobId != null);
            row.Add(refresh);
            Toggle capture = new Toggle("Capture and stop Play") { name = "candidate-admit-capture", value = EditorApplication.isPlaying };
            capture.SetEnabled(EditorApplication.isPlaying);
            row.Add(capture);
            Button admit = new Button(() => Run(() =>
            {
                AdmissionResult result = _context.Candidates.Admit(entry, capture.value);
                LastMessage = "Admit " + result.Outcome + (result.Reason != null ? " (" + result.Reason + ")" : string.Empty) + ": " + result.Detail;
            }, null))
            { name = "candidate-admit", text = "Admit" };
            admit.AddToClassList("gcs-primary");
            admit.tooltip = StudioStyles.Safe(_context.Candidates.CanAdmit(entry) ? "Install the staged package through StageAdmission (compile, checkers, catalog check)." : "Admit needs a passing stage verdict.");
            admit.SetEnabled(_context.Candidates.CanAdmit(entry));
            row.Add(admit);
            _details.Add(row);
            if (entry.Admission != null)
            {
                _details.Add(StudioStyles.Text("Admission: " + entry.Admission.Outcome + (entry.Admission.Reason != null ? " (" + entry.Admission.Reason + ")" : string.Empty) + " - " + entry.Admission.Detail, entry.Admission.Outcome == AdmissionOutcome.Admitted || entry.Admission.Outcome == AdmissionOutcome.Pending ? "gcs-muted" : "gcs-diagnostic"));
            }
        }

        private async System.Threading.Tasks.Task RefreshStageAsync(CandidateEntry entry)
        {
            Diagnostic? failure = await _context.Candidates.RefreshStage(entry);
            LastMessage = failure == null ? "Verified verdict refreshed." : StudioStyles.Safe(failure.Message);
            Rebuild();
            return;
        }

        private async System.Threading.Tasks.Task StageAsync(CandidateEntry entry)
        {
            LastMessage = "Staging " + entry.Id + "...";
            Rebuild();
            Diagnostic? failure = await _context.Candidates.RequestStage(entry);
            LastMessage = failure == null ? "Stage verdict recorded: " + _context.Candidates.StageStateOf(entry).Label + "." : "Stage failed: " + failure.Code + ": " + failure.Message;
            Rebuild();
            return;
        }

        internal static string VerdictModifier(StageState stage)
        {
            if (stage.Verdict == null)
            {
                return "stale";
            }

            return stage.Verdict.Status == ScenarioStatus.Pass ? "applied" : stage.Verdict.Status == ScenarioStatus.Fail ? "failed" : "pending";
        }

        private VisualElement BuildOperationRow(CandidateEntry entry, Operation operation, StagedOperation? staged)
        {
            VisualElement row = new VisualElement { name = "op-" + operation.OpId };
            row.AddToClassList("gcs-op");
            VisualElement line = new VisualElement();
            line.AddToClassList("gcs-row");
            line.Add(new Label(StudioStyles.Safe(operation.OpId + "  " + operation.Tool)) { tooltip = StudioStyles.Safe(operation.Args?.ToString(Newtonsoft.Json.Formatting.None) ?? string.Empty) });
            string target = operation.Target == null ? "-" : (operation.Target.Path ?? operation.Target.AuthoringId ?? operation.Target.Kind.ToString());
            Label targetLabel = new Label(StudioStyles.Safe(target));
            targetLabel.AddToClassList("gcs-muted");
            targetLabel.AddToClassList("gcs-grow");
            line.Add(targetLabel);
            if (staged != null)
            {
                if (staged.Conflict != null)
                {
                    line.Add(StudioStyles.Badge("conflict", "error"));
                    line.Add(new Button(() => Run(() => _context.Candidates.Rebase(entry, new[] { operation.OpId }), "Rebased " + operation.OpId + ".")) { text = "Rebase" });
                }
                else if (staged.Blocked)
                {
                    line.Add(StudioStyles.Badge(staged.Diagnostics[0].Code == DiagnosticCodes.StaleTarget ? "stale" : "blocked", "error"));
                }
                else
                {
                    line.Add(StudioStyles.Badge(staged.Live ? "live" : "ok"));
                }
            }

            Button skip = new Button(() => Run(() => _context.Candidates.Skip(entry, new[] { operation.OpId }), "Skipped " + operation.OpId + ".")) { text = "Skip" };
            skip.SetEnabled(entry.IsOpen && entry.ChangeSet.Operations.Count > 1);
            line.Add(skip);
            row.Add(line);
            if (staged != null)
            {
                foreach (Diagnostic diagnostic in staged.Diagnostics)
                {
                    row.Add(StudioStyles.Text("  " + diagnostic.Code + ": " + diagnostic.Message, "gcs-diagnostic"));
                }
            }

            return row;
        }

        private VisualElement BuildImageCompare(ChangeSet changeSet, ArtifactRef artifact)
        {
            VisualElement block = new VisualElement { name = "image-" + artifact.Sha256.Substring(0, 8) };
            block.Add(StudioStyles.Text("Image " + (artifact.Name ?? artifact.Sha256.Substring(0, 12)) + " (" + artifact.Bytes.ToString(CultureInfo.InvariantCulture) + " bytes)"));
            VisualElement row = new VisualElement();
            row.AddToClassList("gcs-row");
            Texture? before = CandidateCompare.CurrentTextureFor(_context.Runtime, changeSet, artifact);
            Texture2D? afterTexture = CandidateCompare.LoadImage(_context.Runtime, artifact.Sha256);
            if (afterTexture != null)
            {
                _ownedTextures.Add(afterTexture);
            }

            row.Add(ImageCell("before", before));
            row.Add(ImageCell("after", afterTexture));
            if (before != null && afterTexture != null)
            {
                Texture2D? diff = CandidateCompare.Difference(before, afterTexture, out string? reason);
                if (diff != null)
                {
                    _ownedTextures.Add(diff);
                    row.Add(ImageCell("diff", diff));
                }
                else if (reason != null)
                {
                    row.Add(StudioStyles.Text(reason, "gcs-muted"));
                }
            }

            block.Add(row);
            return block;
        }

        private VisualElement BuildAudioRow(ArtifactRef artifact)
        {
            VisualElement row = new VisualElement { name = "audio-" + artifact.Sha256.Substring(0, 8) };
            row.AddToClassList("gcs-row");
            row.Add(new Label(StudioStyles.Safe("Audio " + (artifact.Name ?? artifact.Sha256.Substring(0, 12)))));
            Label status = new Label();
            status.AddToClassList("gcs-muted");
            row.Add(new Button(() =>
            {
                AudioClip? clip = CandidateCompare.LoadAudio(_context.Runtime, artifact, out string? reason);
                if (clip == null)
                {
                    status.text = StudioStyles.Safe(reason ?? "cannot decode");
                    return;
                }

                _ownedTextures.Add(clip);
                _audio.Play(clip);
                status.text = StudioStyles.Safe("playing " + clip.length.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            }) { text = "Play" });
            row.Add(new Button(_audio.Stop) { text = "Stop" });
            row.Add(status);
            return row;
        }

        private static VisualElement ImageCell(string caption, Texture? texture)
        {
            VisualElement cell = new VisualElement();
            cell.AddToClassList("gcs-image-cell");
            cell.Add(new Label(StudioStyles.Safe(caption)));
            if (texture != null)
            {
                Image image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("gcs-image-cell__image");
                cell.Add(image);
            }
            else
            {
                cell.Add(new Label("(none)"));
            }

            return cell;
        }

        private static VisualElement DiffRow(string op, string field, string current, string proposed, bool header, bool changed)
        {
            VisualElement row = new VisualElement();
            row.AddToClassList("gcs-table__row");
            row.EnableInClassList("gcs-table__row--header", header);
            row.EnableInClassList("gcs-table__row--changed", changed);
            foreach (string text in new[] { op, field, current, proposed })
            {
                Label cell = new Label(StudioStyles.Safe(text)) { tooltip = StudioStyles.Safe(text) };
                cell.AddToClassList("gcs-table__cell");
                row.Add(cell);
            }

            return row;
        }

        private static string Targets(ChangeSet changeSet)
        {
            List<string> names = new List<string>();
            foreach (Operation operation in changeSet.Operations)
            {
                if (operation.Target == null)
                {
                    continue;
                }

                string name = operation.Target.Path ?? operation.Target.AuthoringId ?? operation.Target.Kind.ToString();
                if (!names.Contains(name))
                {
                    names.Add(name);
                }
            }

            return names.Count == 0 ? "-" : string.Join(", ", names);
        }

        private void Run(Action action, string? message)
        {
            try
            {
                action();
                if (message != null)
                {
                    LastMessage = message;
                }
            }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
            {
                LastMessage = error.Message;
            }

            Rebuild();
        }

        private static string Ms(double value) => value.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

        private void ReleaseTextures()
        {
            foreach (UnityEngine.Object owned in _ownedTextures)
            {
                if (owned != null && !(owned is AudioClip && _audio.IsPlaying))
                {
                    UnityEngine.Object.DestroyImmediate(owned);
                }
            }

            _ownedTextures.Clear();
        }

        private void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _context.Candidates.Changed += Rebuild;
            Rebuild();
        }

        private void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _context.Candidates.Changed -= Rebuild;
            _audio.Dispose();
            ReleaseTextures();
        }
    }

    /// <summary>The compact candidate strip in the viewport's bottom-right corner.</summary>
    public sealed class CandidateStripView : VisualElement
    {
        public const int MaxShown = 3;

        private readonly StudioUiContext _context;
        private bool _attached;

        public CandidateStripView(StudioUiContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            name = "candidate-strip";
            AddToClassList("gcs-strip");
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
            Rebuild();
        }

        public string LastMessage { get; private set; } = string.Empty;

        public void Rebuild()
        {
            Clear();
            IReadOnlyList<CandidateEntry> open = _context.Candidates.Open();
            style.display = open.Count == 0 && LastMessage.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            if (open.Count > 0)
            {
                Label title = new Label(StudioStyles.Safe("Candidates (" + open.Count + ")"));
                title.AddToClassList("gcs-section__title");
                Add(title);
            }

            for (int i = 0; i < Math.Min(MaxShown, open.Count); i++)
            {
                CandidateEntry entry = open[i];
                VisualElement card = new VisualElement { name = "strip-" + entry.Id };
                card.AddToClassList("gcs-strip__card");
                Label summary = new Label(StudioStyles.Safe(entry.ChangeSet.Intent.Text)) { tooltip = StudioStyles.Safe(entry.Id) };
                summary.AddToClassList("gcs-tray__intent");
                card.Add(summary);
                if (entry.VerifiedVerdict?.Confinement == "host")
                    card.Add(StudioStyles.Text("Warning: host confinement; operator opt-in required.", "gcs-diagnostic"));
                card.Add(new Label(StudioStyles.Safe(entry.Summary + " · " + entry.Stage)));
                bool staging = CandidateRequirements.NeedsStageVerdict(entry.ChangeSet);
                StageState? stage = staging ? _context.Candidates.StageStateOf(entry) : null;
                VisualElement badges = new VisualElement();
                badges.AddToClassList("gcs-row");
                foreach (string badge in entry.Badges)
                {
                    badges.Add(StudioStyles.Badge(badge));
                }

                if (stage != null)
                {
                    badges.Add(StudioStyles.Badge(stage.Label, CandidatePanelView.VerdictModifier(stage)));
                }

                card.Add(badges);
                VisualElement buttons = new VisualElement();
                buttons.AddToClassList("gcs-row");
                buttons.Add(new Button(() => Run(() => _context.Candidates.Preview(entry), "Previewing " + entry.Id)) { text = "Preview" });
                Button apply = new Button(() => Run(() =>
                {
                    ApplyReport report = _context.Candidates.Apply(entry);
                    LastMessage = "Apply " + report.State + " in " + (entry.ApplyMilliseconds ?? 0).ToString("0.0", CultureInfo.InvariantCulture) + " ms";
                }, null)) { text = "Apply" };
                apply.SetEnabled(!staging);
                buttons.Add(apply);
                if (stage != null)
                {
                    Button admit = new Button(() => Run(() =>
                    {
                        AdmissionResult result = _context.Candidates.Admit(entry, EditorApplication.isPlaying);
                        LastMessage = "Admit " + result.Outcome + (result.Reason != null ? " (" + result.Reason + ")" : string.Empty);
                    }, null)) { text = "Admit", tooltip = StudioStyles.Safe(_context.Candidates.CanAdmit(entry) ? "Admit through StageAdmission" : "Admit needs a passing stage verdict (open the panel to stage).") };
                    admit.SetEnabled(_context.Candidates.CanAdmit(entry));
                    buttons.Add(admit);
                }

                buttons.Add(new Button(() => Run(() => _context.Candidates.Reject(entry, "Rejected from the viewport strip."), "Rejected " + entry.Id)) { text = "Reject" });
                buttons.Add(new Button(() => StudioCandidatesWindow.Open(entry.Id)) { text = "...", tooltip = "Open the candidate panel" });
                card.Add(buttons);
                Add(card);
            }

            if (LastMessage.Length > 0)
            {
                Label message = new Label(StudioStyles.Safe(LastMessage));
                message.AddToClassList("gcs-status");
                Add(message);
            }
        }

        private void Run(Action action, string? message)
        {
            try
            {
                action();
                if (message != null)
                {
                    LastMessage = message;
                }
            }
            catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
            {
                LastMessage = error.Message;
            }

            Rebuild();
        }

        private void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _context.Candidates.Changed += Rebuild;
            Rebuild();
        }

        private void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _context.Candidates.Changed -= Rebuild;
        }
    }
}
