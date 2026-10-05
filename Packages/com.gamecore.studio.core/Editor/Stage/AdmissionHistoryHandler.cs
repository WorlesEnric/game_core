#nullable enable
using GameCore.Studio.Model;

namespace GameCore.Studio.Edit
{
    // R2-A owns HistoryService dispatch; keep this declaration until integration moves it to Journal/.
    public interface IHistoryEntryHandler
    {
        bool CanHandle(ChangeSet entry);
        HistoryResult Undo(ChangeSet entry, bool force);
        HistoryResult Redo(ChangeSet entry);
        HistoryResult Resume(ChangeSet entry);
        HistoryResult Rollback(ChangeSet entry);
    }

    public sealed class AdmissionHistoryHandler : IHistoryEntryHandler
    {
        private readonly StageAdmission _admission;
        public AdmissionHistoryHandler(StageAdmission admission) { _admission = admission; }
        public bool CanHandle(ChangeSet entry)
        {
            foreach (Operation op in entry.Operations)
                if (op.Tool == MechanismAdmission.AdmitTool || op.Tool == MechanismAdmission.RemoveTool) return true;
            return false;
        }
        public HistoryResult Undo(ChangeSet entry, bool force) => Convert(_admission.Undo(entry.Id));
        public HistoryResult Redo(ChangeSet entry) => Convert(_admission.Redo(entry.Id));
        public HistoryResult Resume(ChangeSet entry) => Convert(_admission.Resume(entry.Id));
        public HistoryResult Rollback(ChangeSet entry) => Convert(_admission.RollbackPending(entry.Id));
        private HistoryResult Convert(AdmissionResult result)
        {
            bool ok = result.Outcome == AdmissionOutcome.Admitted || result.Outcome == AdmissionOutcome.Undone;
            return new HistoryResult(result.ChangeSetId, ok, _admission.Runtime.Journal.Read(result.ChangeSetId)?.EffectiveState,
                result.Diagnostics, null);
        }
    }
}
