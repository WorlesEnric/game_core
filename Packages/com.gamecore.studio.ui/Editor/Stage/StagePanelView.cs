#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameCore.Studio.Edit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>Creator controls for companion stages. Cancelling is not candidate rejection or slot discard.</summary>
    public sealed class StagePanelView : VisualElement
    {
        private readonly StudioUiContext _context;
        private readonly HashSet<string> _cancelling = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _cancelledJobs = new HashSet<string>(StringComparer.Ordinal);
        private bool _attached;
        public string LastMessage { get; private set; } = string.Empty;

        public StagePanelView(StudioUiContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            name = "stage-panel";
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
            Rebuild();
        }

        public void Rebuild()
        {
            Clear();
            Add(StudioStyles.Text("Stage", "gcs-section__title"));
            foreach (CandidateEntry entry in _context.Candidates.Entries)
            {
                if (!entry.IsOpen || !CandidateRequirements.NeedsStageVerdict(entry.ChangeSet)) continue;
                VisualElement row = new VisualElement { name = "stage-" + entry.Id };
                row.Add(new Label(StudioStyles.Safe(entry.ChangeSet.Intent.Text)));
                row.Add(new Label(StudioStyles.Safe(entry.StageJobId ?? "Not submitted")));
                row.Add(new Label(StudioStyles.Safe(_context.Candidates.StageStateOf(entry).Label)));
                bool cancelling = _cancelling.Contains(entry.Id);
                Button stage = new Button(() => _ = Request(entry, false)) { text = "Stage" };
                stage.SetEnabled(!entry.Staging && !cancelling && _context.Candidates.CanRequestStage);
                row.Add(stage);
                Button refresh = new Button(() => _ = Request(entry, true)) { text = "Refresh" };
                refresh.SetEnabled(!entry.Staging && !cancelling && entry.StageJobId != null);
                row.Add(refresh);
                Button cancel = new Button(() => _ = CancelAsync(entry))
                {
                    name = "stage-cancel-" + entry.Id,
                    text = cancelling ? "Cancelling…" : "Cancel",
                    tooltip = "Cancel this companion stage and release its execution resources."
                };
                cancel.SetEnabled(!cancelling && entry.StageJobId != null && !_cancelledJobs.Contains(entry.StageJobId)
                    && entry.VerifiedVerdict == null && StageAdmission.Of(_context.Runtime).Options.StageService is IStageJobControl);
                row.Add(cancel);
                Button admit = new Button(() => Admit(entry)) { name = "stage-admit-" + entry.Id, text = "Admit" };
                admit.SetEnabled(!cancelling && _context.Candidates.CanAdmit(entry));
                row.Add(admit);
                Add(row);
            }
            Add(new Label(StudioStyles.Safe(LastMessage)));
        }

        public async Task CancelAsync(CandidateEntry entry)
        {
            string? job = entry.StageJobId;
            if (job == null || !_cancelling.Add(entry.Id)) return;
            Rebuild();
            try
            {
                await StageAdmission.Of(_context.Runtime).CancelStage(job, entry.Id);
                _cancelledJobs.Add(job);
                entry.VerifiedVerdict = null;
                LastMessage = "Stage cancelled; execution resources released.";
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                LastMessage = error.Message;
            }
            finally
            {
                _cancelling.Remove(entry.Id);
                Rebuild();
            }
        }

        private async Task Request(CandidateEntry entry, bool refresh)
        {
            var problem = refresh ? await _context.Candidates.RefreshStage(entry) : await _context.Candidates.RequestStage(entry);
            LastMessage = problem?.Message ?? "Stage finished. Review the verdict before Admit.";
            Rebuild();
            return;
        }

        private void Admit(CandidateEntry entry)
        {
            try { LastMessage = _context.Candidates.Admit(entry, EditorApplication.isPlaying).Outcome.ToString(); }
            catch (Exception error) when (!(error is OutOfMemoryException)) { LastMessage = error.Message; }
            Rebuild();
        }

        private void Attach()
        {
            if (_attached) return;
            _attached = true;
            _context.Candidates.Changed += Rebuild;
            Rebuild();
        }

        private void Detach()
        {
            if (!_attached) return;
            _attached = false;
            _context.Candidates.Changed -= Rebuild;
        }
    }

    public sealed class StudioStageWindow : EditorWindow
    {
        public StagePanelView? View { get; private set; }

        [MenuItem("GameCore/Studio/Stage", false, 106)]
        public static void Open()
        {
            StudioStageWindow window = GetWindow<StudioStageWindow>("Stage");
            window.minSize = new Vector2(320f, 240f);
            window.Show();
        }

        private void CreateGUI()
        {
            titleContent = new GUIContent("Stage");
            rootVisualElement.Clear();
            StudioStyles.Apply(rootVisualElement);
            View = new StagePanelView(StudioUiSession.Context);
            ScrollView scroll = new ScrollView();
            scroll.Add(View);
            rootVisualElement.Add(scroll);
        }
    }
}
