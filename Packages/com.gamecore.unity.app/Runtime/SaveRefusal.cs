// GameCore.Unity.App - the typed refusals of the save service (SADR-012 (studio), catalog row 12).
//
// Every save, restore or delete that does not happen returns one of these, never a bare false: the code says which
// rule refused, the hint says what a player or an author can do about it, and the detail carries the kernel's own
// diagnostic (P-052). The gameplay save package maps the same codes onto its `SaveRefused{code,hint}` event.
#nullable enable
using System;
using GameCore.Contracts;

namespace GameCore.Unity.App
{
    /// <summary>Why a save operation refused (SADR-012).</summary>
    public enum SaveRefusalCode
    {
        None = 0,

        /// <summary>The slot has no save (no header or no checkpoint file).</summary>
        MissingSlot = 1,

        /// <summary>The slot's files do not verify: envelope checksum, document decoding or header/document hash.</summary>
        CorruptFile = 2,

        /// <summary>The save was captured under another catalog and this build declares no compatible migration.</summary>
        CatalogMismatch = 3,

        /// <summary>A slot row needs a migration this build does not register (P-032, P-054).</summary>
        MigrationPathMissing = 4,

        /// <summary>The world is not at a state where a capture or restore is safe (mid-step, faulted, stopped).</summary>
        UnsafeState = 5,

        /// <summary>The slot name is not a valid slot name.</summary>
        InvalidSlot = 6,

        /// <summary>Capturing the world failed at the committed boundary (P-053).</summary>
        CaptureFailed = 7,

        /// <summary>Writing or removing the slot's files failed.</summary>
        StorageFailed = 8,

        /// <summary>The checkpoint read and migrated, but the kernel refused to rebuild or expose the world (O-21).</summary>
        RestoreRefused = 9,

        /// <summary>The save was written by a newer build (a newer header format or a newer slot schema version).</summary>
        NewerBuild = 10,
    }

    /// <summary>One typed refusal: code, hint and the kernel's diagnostic (P-052).</summary>
    public sealed class SaveRefusal
    {
        public SaveRefusal(SaveRefusalCode code, string hint, DiagnosticCode kernelCode, string detail)
        {
            if (code == SaveRefusalCode.None)
            {
                throw new ArgumentException("A refusal names why it refused.", nameof(code));
            }

            Code = code;
            Hint = string.IsNullOrEmpty(hint) ? DefaultHint(code) : hint;
            KernelCode = kernelCode;
            Detail = detail ?? string.Empty;
        }

        public SaveRefusalCode Code { get; }

        /// <summary>One actionable sentence for the player or the author.</summary>
        public string Hint { get; }

        public DiagnosticCode KernelCode { get; }

        public string Detail { get; }

        /// <summary>The stable string id of a code (`save.missing-slot`), shared with the gameplay save events.</summary>
        public string CodeId => IdOf(Code);

        public static string IdOf(SaveRefusalCode code)
        {
            switch (code)
            {
                case SaveRefusalCode.MissingSlot: return "save.missing-slot";
                case SaveRefusalCode.CorruptFile: return "save.corrupt-file";
                case SaveRefusalCode.CatalogMismatch: return "save.catalog-mismatch";
                case SaveRefusalCode.MigrationPathMissing: return "save.migration-path-missing";
                case SaveRefusalCode.UnsafeState: return "save.unsafe-state";
                case SaveRefusalCode.InvalidSlot: return "save.invalid-slot";
                case SaveRefusalCode.CaptureFailed: return "save.capture-failed";
                case SaveRefusalCode.StorageFailed: return "save.storage-failed";
                case SaveRefusalCode.RestoreRefused: return "save.restore-refused";
                case SaveRefusalCode.NewerBuild: return "save.newer-build";
                default: return "save.none";
            }
        }

        public static string DefaultHint(SaveRefusalCode code)
        {
            switch (code)
            {
                case SaveRefusalCode.MissingSlot: return "there is no save in this slot; choose another slot";
                case SaveRefusalCode.CorruptFile: return "the save file is damaged and cannot be loaded; delete it or load another slot";
                case SaveRefusalCode.CatalogMismatch: return "the save belongs to another content version of the game and this build declares no migration from it";
                case SaveRefusalCode.MigrationPathMissing: return "the save needs a data migration this build does not include";
                case SaveRefusalCode.UnsafeState: return "the game is busy; try again at the next pause";
                case SaveRefusalCode.InvalidSlot: return SaveSlotNames.Describe(null);
                case SaveRefusalCode.CaptureFailed: return "the game could not be captured at a safe point; try again";
                case SaveRefusalCode.StorageFailed: return "the save could not be written to disk; check free space and permissions";
                case SaveRefusalCode.RestoreRefused: return "the save could not be rebuilt into a running world";
                case SaveRefusalCode.NewerBuild: return "the save was written by a newer build of the game; load it with that build";
                default: return string.Empty;
            }
        }

        public override string ToString() => CodeId + ": " + Hint + (Detail.Length == 0 ? string.Empty : " (" + Detail + ")");
    }
}
