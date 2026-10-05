#nullable enable
using GameCore.Studio.Model;

namespace GameCore.Studio.Edit
{
    public enum HistoryEntryKind { Edit, Admission }
    public enum HistoryAction { Undo, Redo, Resume, Rollback }

    /// <summary>Register per StudioRuntime with History.RegisterHandler after every reload.
    /// The handler owns durable lifecycle/compile checks and final journal state for its kind.
    /// All history tools and views must call HistoryService, never replay admission tools directly.</summary>
    public interface IHistoryEntryHandler
    {
        HistoryEntryKind Kind { get; }
        HistoryResult Handle(ChangeSet entry, HistoryAction action, bool force);
    }
}
