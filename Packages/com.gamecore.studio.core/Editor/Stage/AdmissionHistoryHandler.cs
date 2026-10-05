#nullable enable
using GameCore.Studio.Model;

namespace GameCore.Studio.Edit
{
    public sealed class AdmissionHistoryHandler : IHistoryEntryHandler
    {
        private readonly StageAdmission _admission;
        public AdmissionHistoryHandler(StageAdmission admission) { _admission = admission; }
        public HistoryEntryKind Kind => HistoryEntryKind.Admission;
        public HistoryResult Handle(ChangeSet entry, HistoryAction action, bool force)
        {
            // Force never bypasses admission ownership, provenance, or compile checks.
            switch (action)
            {
                case HistoryAction.Undo: return Convert(_admission.Undo(entry.Id));
                case HistoryAction.Redo: return Convert(_admission.Redo(entry.Id));
                case HistoryAction.Resume: return Convert(_admission.Resume(entry.Id));
                case HistoryAction.Rollback: return Convert(_admission.RollbackPending(entry.Id));
                default: throw new System.ArgumentOutOfRangeException(nameof(action));
            }
        }
        private HistoryResult Convert(AdmissionResult result)
        {
            bool ok = result.Outcome == AdmissionOutcome.Admitted || result.Outcome == AdmissionOutcome.Undone || result.Outcome == AdmissionOutcome.RolledBack;
            return new HistoryResult(result.ChangeSetId, ok, _admission.Runtime.Journal.Read(result.ChangeSetId)?.EffectiveState,
                result.Diagnostics, null);
        }
    }
}
