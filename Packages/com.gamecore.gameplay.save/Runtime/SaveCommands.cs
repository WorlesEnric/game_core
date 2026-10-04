// GameCore.Gameplay.Save - typed save commands and events (plugin catalog row 12, SADR-012 (studio)).
//
// `save.capture{slot}`, `save.restore{slot}` and `save.delete{slot}` are host-level commands: a save is not a step of the
// simulation, it is an operation on the world (O-20, O-21), so the commands never enter the world's message plane.
// `SaveCommandHost` hands each one to the application root's `SaveService` and answers with exactly one event:
// `SaveWritten`, `SaveRestored` or `SaveRefused{code,hint}`. The refusal codes are the SaveService's typed codes
// (missing slot, corrupt file, catalog mismatch without a compatible migration, migration path missing, restore during
// an unsafe state, ...), with their stable string ids.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.App;

namespace GameCore.Gameplay.Save
{
    /// <summary>The three save command kinds.</summary>
    public enum SaveCommandKind
    {
        Capture = 0,
        Restore = 1,
        Delete = 2,
    }

    /// <summary>One typed save command: <c>save.capture{slot}</c>, <c>save.restore{slot}</c> or <c>save.delete{slot}</c>.</summary>
    public readonly struct SaveCommand
    {
        public const string CaptureId = "save.capture";
        public const string RestoreId = "save.restore";
        public const string DeleteId = "save.delete";

        private SaveCommand(SaveCommandKind kind, string slot, string? thumbnailPath)
        {
            Kind = kind;
            Slot = slot ?? throw new ArgumentNullException(nameof(slot));
            ThumbnailPath = thumbnailPath;
        }

        public SaveCommandKind Kind { get; }

        public string Slot { get; }

        /// <summary>Optional thumbnail path written to the header of a capture.</summary>
        public string? ThumbnailPath { get; }

        /// <summary>The command's stable id.</summary>
        public string Id => Kind == SaveCommandKind.Capture ? CaptureId : (Kind == SaveCommandKind.Restore ? RestoreId : DeleteId);

        public static SaveCommand Capture(string slot, string? thumbnailPath = null) => new SaveCommand(SaveCommandKind.Capture, slot, thumbnailPath);

        public static SaveCommand Restore(string slot) => new SaveCommand(SaveCommandKind.Restore, slot, null);

        public static SaveCommand Delete(string slot) => new SaveCommand(SaveCommandKind.Delete, slot, null);

        /// <summary>Parses a command id and slot (the shape a Studio or a script sends); false for an unknown id.</summary>
        public static bool TryParse(string? id, string? slot, out SaveCommand command)
        {
            command = default(SaveCommand);
            if (slot == null)
            {
                return false;
            }

            switch (id)
            {
                case CaptureId: command = Capture(slot); return true;
                case RestoreId: command = Restore(slot); return true;
                case DeleteId: command = Delete(slot); return true;
                default: return false;
            }
        }

        public override string ToString() => Id + "{" + Slot + "}";
    }

    /// <summary>A save was written.</summary>
    public sealed class SaveWritten
    {
        public SaveWritten(string slot, SaveSlotHeader header, WorldId world, string slotHash)
        {
            Slot = slot;
            Header = header;
            World = world;
            SlotHash = slotHash;
        }

        public string Slot { get; }

        public SaveSlotHeader Header { get; }

        public WorldId World { get; }

        public string SlotHash { get; }

        public override string ToString() => "SaveWritten{" + Slot + ",step=" + Header.LogicalStep.ToString(CultureInfo.InvariantCulture) + "}";
    }

    /// <summary>A save was restored into a new world (P-049).</summary>
    public sealed class SaveRestored
    {
        public SaveRestored(string slot, WorldId previousWorld, WorldId restoredWorld, RestoredTemporalOrigin origin, int migratedRows)
        {
            Slot = slot;
            PreviousWorld = previousWorld;
            RestoredWorld = restoredWorld;
            Origin = origin;
            MigratedRows = migratedRows;
        }

        public string Slot { get; }

        public WorldId PreviousWorld { get; }

        public WorldId RestoredWorld { get; }

        public RestoredTemporalOrigin Origin { get; }

        public int MigratedRows { get; }

        public override string ToString() => "SaveRestored{" + Slot + "," + Origin + "}";
    }

    /// <summary>A save command refused, with a stable code and an actionable hint.</summary>
    public sealed class SaveRefused
    {
        public SaveRefused(SaveCommand command, SaveRefusalCode code, string hint, string detail)
        {
            Command = command;
            Code = code;
            Hint = hint ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public SaveCommand Command { get; }

        public SaveRefusalCode Code { get; }

        /// <summary>The stable string id of <see cref="Code"/> (`save.corrupt-file`).</summary>
        public string CodeId => SaveRefusal.IdOf(Code);

        public string Hint { get; }

        public string Detail { get; }

        public override string ToString() => "SaveRefused{" + Command + "," + CodeId + "," + Hint + "}";
    }

    /// <summary>
    /// The host-level handler of the save commands: it runs each command on the application root's save service and
    /// raises exactly one event per command. It runs on the main thread between frames, like every host operation.
    /// </summary>
    public sealed class SaveCommandHost
    {
        public SaveCommandHost(SaveService service)
        {
            Service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public SaveService Service { get; }

        public event Action<SaveWritten>? Written;

        public event Action<SaveRestored>? Restored;

        public event Action<SaveRefused>? Refused;

        /// <summary>Commands handled, refused or not.</summary>
        public int HandledCount { get; private set; }

        public int RefusedCount { get; private set; }

        /// <summary>Handles one command; returns the service's result (the event has already been raised).</summary>
        public SaveResult Handle(SaveCommand command)
        {
            HandledCount++;
            SaveResult result;
            switch (command.Kind)
            {
                case SaveCommandKind.Capture:
                    result = Service.Capture(command.Slot, command.ThumbnailPath);
                    break;
                case SaveCommandKind.Restore:
                    result = Service.Restore(command.Slot);
                    break;
                default:
                    result = Service.Delete(command.Slot);
                    break;
            }

            if (result.Refusal != null)
            {
                RefusedCount++;
                Refused?.Invoke(new SaveRefused(command, result.Refusal.Code, result.Refusal.Hint, result.Refusal.Detail));
                return result;
            }

            if (command.Kind == SaveCommandKind.Capture && result.Header != null)
            {
                Written?.Invoke(new SaveWritten(command.Slot, result.Header, result.SourceWorld, result.SlotHash));
            }
            else if (command.Kind == SaveCommandKind.Restore)
            {
                Restored?.Invoke(new SaveRestored(
                    command.Slot,
                    result.SourceWorld,
                    result.RestoredWorld,
                    result.Origin ?? RestoredTemporalOrigin.Fresh,
                    result.Migration != null ? result.Migration.MigratedRows : 0));
            }

            return result;
        }

        /// <summary>Handles a command given as id and slot; an unknown id is a refusal event, never an exception.</summary>
        public SaveResult? Handle(string id, string slot)
        {
            if (!SaveCommand.TryParse(id, slot, out SaveCommand command))
            {
                RefusedCount++;
                Refused?.Invoke(new SaveRefused(
                    SaveCommand.Capture(slot ?? string.Empty),
                    SaveRefusalCode.InvalidSlot,
                    "unknown save command '" + (id ?? "null") + "'; use " + SaveCommand.CaptureId + ", " + SaveCommand.RestoreId
                    + " or " + SaveCommand.DeleteId,
                    string.Empty));
                return null;
            }

            return Handle(command);
        }
    }

    /// <summary>The slots a save menu shows (catalog row 12): readable headers newest first, plus broken slot names.</summary>
    public interface ISaveSlotCatalog
    {
        IReadOnlyList<SaveSlotHeader> Slots { get; }

        /// <summary>Slot names whose header did not read; a menu can offer to delete them.</summary>
        IReadOnlyList<string> Unreadable { get; }

        bool Exists(string slot);

        bool TryGet(string slot, out SaveSlotHeader? header);

        /// <summary>Re-reads the save directory.</summary>
        void Refresh();
    }

    /// <summary>The slot catalog over a save service's directory.</summary>
    public sealed class SaveServiceSlotCatalog : ISaveSlotCatalog
    {
        private readonly SaveService service;
        private IReadOnlyList<SaveSlotHeader> slots = Array.Empty<SaveSlotHeader>();
        private IReadOnlyList<string> unreadable = Array.Empty<string>();

        public SaveServiceSlotCatalog(SaveService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            Refresh();
        }

        public IReadOnlyList<SaveSlotHeader> Slots => slots;

        public IReadOnlyList<string> Unreadable => unreadable;

        public bool Exists(string slot) => service.Exists(slot);

        public bool TryGet(string slot, out SaveSlotHeader? header)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (string.Equals(slots[i].Slot, slot, StringComparison.Ordinal))
                {
                    header = slots[i];
                    return true;
                }
            }

            header = null;
            return false;
        }

        public void Refresh()
        {
            slots = service.ListSlots(out IReadOnlyList<string> broken);
            unreadable = broken;
        }
    }
}
